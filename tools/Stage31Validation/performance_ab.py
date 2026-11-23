"""Sequential DLL/config A/B/A/B diagnostic; never changes acceptance thresholds."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import time

import psutil

ROOT = Path(__file__).resolve().parents[2]
DLLS = ('NewKOF.dll', 'NewKOF.BattleCore.dll')


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def stop_run(process):
    """The Windows console launcher owns a graphical child holding the DLLs."""
    if process.poll() is not None:
        return
    try:
        parent = psutil.Process(process.pid)
        owned = parent.children(recursive=True) + [parent]
    except psutil.NoSuchProcess:
        process.wait()
        return
    for child in owned:
        try:
            child.terminate()
        except psutil.NoSuchProcess:
            pass
    _, alive = psutil.wait_procs(owned, timeout=5)
    for child in alive:
        try:
            child.kill()
        except psutil.NoSuchProcess:
            pass
    _, alive = psutil.wait_procs(alive, timeout=5)
    if alive:
        raise RuntimeError(f'Owned Godot processes did not exit: {[p.pid for p in alive]}')
    process.wait()


def system_snapshot():
    gpu = subprocess.run(['nvidia-smi', '--query-gpu=name,driver_version,temperature.gpu,utilization.gpu,power.draw,clocks.current.graphics',
                          '--format=csv,noheader'], capture_output=True, text=True)
    power = subprocess.run(['powercfg', '/getactivescheme'], capture_output=True)
    return {'gpu': gpu.stdout.strip(), 'gpuExit': gpu.returncode,
            'powerSchemeRaw': power.stdout.decode(errors='replace'),
            'cpuTemperature': None, 'cpuTemperatureReason': 'No CPU temperature sensor exposed by psutil',
            'processes': [{'pid': p.pid, 'name': p.info['name']} for p in psutil.process_iter(['name'])]}


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument('--baseline-bin', required=True, type=Path)
    parser.add_argument('--command-file', required=True, type=Path)
    parser.add_argument('--candidate-profile', required=True)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    active = [p.pid for p in psutil.process_iter(['name']) if (p.info['name'] or '').lower().startswith('godot')]
    if active:
        raise RuntimeError(f'Godot processes already running: {active}')
    # Commands produced by the existing runner have no quoted/space-containing paths here.
    command = args.command_file.read_text(encoding='utf-8-sig').strip().split()
    if any('"' in part or "'" in part for part in command):
        raise ValueError('Use a runner command without quoted paths')
    installed = ROOT / '.godot/mono/temp/bin/Debug'
    saved = args.output / 'candidate-dll'
    saved.mkdir()
    for name in DLLS:
        shutil.copy2(installed / name, saved / name)
    results = []
    try:
        for audit in (True, False):
            for iteration, variant in enumerate(('A', 'B', 'A', 'B')):
                lane = args.output / f'audit-{audit}-{iteration}-{variant}'
                lane.mkdir()
                source = args.baseline_bin if variant == 'A' else saved
                profile = 'data/imported-characters/terry98-stage30-final' if variant == 'A' else args.candidate_profile
                for name in DLLS:
                    shutil.copy2(source / name, installed / name)
                run = []
                for arg in command:
                    if arg == '--sprite-frame-audit' and not audit:
                        continue
                    if arg.startswith(('--p1-fighter-file=', '--p2-fighter-file=')):
                        arg = arg.split('=')[0] + '=res://' + profile + '/default.fighter.json'
                    elif arg.startswith(('--p1-visual=', '--p2-visual=')):
                        arg = arg.split('=')[0] + ('=terry98-stage30-default' if variant == 'A' else '=terry98-stage31-default')
                    elif arg.startswith('--shot='):
                        arg = '--shot=' + (lane.resolve() / 'end.png').as_posix()
                    elif arg.startswith('--capture-dir='):
                        arg = '--capture-dir=' + (lane.resolve() / 'frames').as_posix()
                    run.append(arg)
                identity = {str(path): digest(path) for path in
                            [*(installed / name for name in DLLS), ROOT / profile / 'default.fighter.json', ROOT / 'project.godot']}
                record = {'variant': variant, 'audit': audit, 'command': run, 'identity': identity,
                          'inputSha256': hashlib.sha256('\n'.join(a for a in run if a.startswith('--renderkey=')).encode()).hexdigest(),
                          'before': system_snapshot(), 'samples': []}
                (lane / 'identity.json').write_text(json.dumps(record, indent=2), encoding='utf-8')
                begin = time.monotonic()
                with (lane / 'duel.log').open('wb') as output:
                    process = subprocess.Popen(run, cwd=ROOT, stdout=output, stderr=subprocess.STDOUT)
                    try:
                        while process.poll() is None:
                            record['samples'].append({'seconds': time.monotonic() - begin, 'cpuPercent': psutil.cpu_percent(interval=0.5)})
                            if time.monotonic() - begin > 90:
                                raise TimeoutError(str(lane))
                    finally:
                        stop_run(process)
                lines = (lane / 'duel.log').read_text(encoding='utf-8-sig', errors='replace').splitlines()
                record.update(exitCode=process.returncode, seconds=time.monotonic() - begin, after=system_snapshot(),
                              fps=[int(m[1]) for line in lines if (m := re.match(r'Project FPS: (\d+)', line))],
                              backlog=[line for line in lines if '帧积压' in line],
                              auditRows=sum(line.startswith(('[SpriteFrameAudit]', '[SpriteRenderAudit]', '[ProjectileSetAudit]')) for line in lines),
                              logBytes=(lane / 'duel.log').stat().st_size,
                              auditDuration=None, nativeWriteFlushCount=None,
                              instrumentationLimit='Native GD.Print duration/write/flush counts are not instrumented; no per-frame timing attribution claimed.')
                (lane / 'result.json').write_text(json.dumps(record, indent=2), encoding='utf-8')
                results.append(record)
                print(f'{lane.name}: exit={process.returncode}, fps={record["fps"]}, backlog={len(record["backlog"])}', flush=True)
    finally:
        (args.output / 'summary.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
        for name in DLLS:
            shutil.copy2(saved / name, installed / name)
        assert all(digest(installed / name) == digest(saved / name) for name in DLLS)


if __name__ == '__main__':
    main()
