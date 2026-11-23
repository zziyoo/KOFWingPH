"""Strict new runs against certified Stage 30 mirrors of deleted regressions.

These retained logs are mirrored references, not restored deleted originals.
--verify-only audits the chain without launching Godot or claiming a current run.
"""
import argparse
from hashlib import sha256
import json
from pathlib import Path
import shlex
import subprocess
import sys
import time

import psutil

from performance_ab import stop_run

TOOLS = Path(__file__).resolve().parents[1]
sys.path[:0] = [str(TOOLS / name) for name in
               ('Stage30Validation', 'Stage29Validation', 'Stage27Validation', 'MugenImporter')]
import stage29_regression as s29
import current_profile as wave
from frozen_replay import ROOT, build_hashes, valid_headless_log, verify_headless_keys
from rising_upper_duel import candidate_identity
from mugen_importer.state_rising_upper import FROZEN_BASE

SWEEP = s29.sweep_history
STAGE30 = 'data/imported-characters/terry98-stage30-final'
CANDIDATE_SHA = '88c2aaad82ac415a452dd90a886b29cccec81fcf5a08f20d1490e71a6e137159'
PROOF_SHA = {
    'stage29': 'ec6e4648a085c4dc484ec50f8997747e9ba7361875d90e5c85853015ba8d7472',
    'old-wave': '1bf58741ca03255a262209adba10ab47a13269559938fd566ea19ba9b59357c2',
}
REFERENCE_BUILD = {
    'NewKOF.dll': '731bdaf3f1d9447407990fbe0019d4e2b2cc1f924c871c9dd595bf132733ddae',
    'NewKOF.BattleCore.dll': '1b94c655d5cddeda64e6fe42e7188673cd9ea52256d0ccfbb6ae79a327a947ad',
}
CURRENT_BUILD = {
    'NewKOF.dll': '5e6cebb79211ed7ca4e757476695ec733d67a2f901cc0af70a2fd76cfc36f02b',
    'NewKOF.BattleCore.dll': '76198dd2173b6688856103ef49b196eb4ec04462365b9a9d7d23093710e342de',
}
MARKERS = {
    'stage29': 'Matched 7 Stage 29 pairs, 9473 authority rows, Stage 29 frozen and Stage 30 current profiles',
    'old-wave': 'Matched 13 Stage 30 compatibility pairs, 1965 frames, 19076 shared rows and 19218 current authority rows; 7 old Wave sources',
}


def digest(path):
    return sha256(path.read_bytes()).hexdigest()


def pin(path, expected, records):
    actual = digest(path)
    s29.assert_equal(expected, actual, f'pinned reference {path}')
    records[str(path.resolve())] = actual


def record(path, records):
    records[str(path.resolve())] = digest(path)


def tokens(path):
    return [token.strip('"') for token in
            shlex.split(path.read_text(encoding='utf-8-sig'), posix=False)]


def arguments(log):
    args = tokens(log.with_suffix('.args.txt'))[1:]
    if args.count('--') != 1 or args[args.index('--fixed-fps') + 1] != '60':
        raise ValueError(f'invalid fixed arguments: {log}')
    if '--capture-input-only' not in args or sum(a.startswith('--shot=') for a in args) != 1:
        raise ValueError(f'missing isolated input/final shot: {log}')
    return args


def fighter_id(profile, mode='default'):
    return json.loads((ROOT / profile / f'{mode}.fighter.json').read_bytes())['fighterId']


def inherited_configs(options, manifest):
    for mode in ('default', 'mode2'):
        for kind, expected in FROZEN_BASE[mode].items():
            pin(ROOT / s29.STAGE29_PROFILE / f'{mode}.{kind}.json', expected, manifest)
        source = json.loads((ROOT / options.profile / f'{mode}.source.json').read_bytes())
        expected = {kind: digest(ROOT / STAGE30 / f'{mode}.{kind}.json')
                    for kind in ('fighter', 'binding', 'source')}
        s29.assert_equal(expected, source['frozenBaseSha256'], f'{mode} inherited Stage 30 source')
    source = json.loads((ROOT / s29.STAGE29_PROFILE / 'default.source.json').read_bytes())
    fighter = json.loads((ROOT / options.profile / 'default.fighter.json').read_bytes())
    s29.assert_equal(source['launchAirHurtboxes'], fighter['groundHurtboxes']['launchAir'],
                     'inherited authored launch boxes')


def proof(options, suite, manifest):
    base = options.archive / f'NewKOF-stage30-delivery-r3-{suite}'
    identity = base / 'candidate.json'
    pin(identity, CANDIDATE_SHA, manifest)
    saved = json.loads(identity.read_bytes())
    for filename, expected in REFERENCE_BUILD.items():
        s29.assert_equal(expected, saved[f'.godot/mono/temp/bin/Debug/{filename}'], 'official Stage 30 DLL')
    for relative, expected in saved.items():
        if relative.startswith('.godot/') or relative == 'project.godot':
            continue
        pin(ROOT / relative, expected, manifest)
    proof_log = ROOT / f'local-mugen/stage30-delivery-r3-{suite}.log'
    pin(proof_log, PROOF_SHA[suite], manifest)
    if MARKERS[suite] not in proof_log.read_text(encoding='utf-8-sig').splitlines():
        raise ValueError(f'missing complete certified comparison: {proof_log}')
    return base


def retained(log, manifest, *, headless=False):
    args = arguments(log)
    for path in (log, log.with_suffix('.args.txt')):
        record(path, manifest)
    data = log.read_bytes()
    if headless:
        if '--headless' not in args or not valid_headless_log(data):
            raise ValueError(f'invalid retained headless reference: {log}')
        count = int(next(a.partition('=')[2] for a in args if a.startswith('--shotframes=')))
        s29.assert_equal(str(count - 1), args[args.index('--quit-after') + 1], 'original headless quit ticks')
    else:
        shot = log.parent / 'end.png'
        record(shot, manifest)
        if not valid_headless_log(data.replace(b'Vulkan ', b'Renderer ', 1)):
            raise ValueError(f'invalid retained graphics reference: {log}')
        if not any(line.startswith(b'Vulkan ') and b' - Forward+ - ' in line for line in data.splitlines()):
            raise ValueError(f'missing Vulkan Forward+ reference marker: {log}')
        for key, value in (('--rendering-method', 'forward_plus'), ('--rendering-driver', 'vulkan')):
            s29.assert_equal(value, args[args.index(key) + 1], f'{log} renderer')
    verify_headless_keys(log, data, data.splitlines(), args)
    return data


def physical(args):
    return [arg for arg in args if arg.startswith('--renderkey=')]


def execute_owned(command, stream, item, *, timeout=60):
    """Reap the launcher and every observed descendant before evidence is hashed."""
    process = None
    descendants = {}
    deadline = time.monotonic() + timeout
    try:
        process = subprocess.Popen(command, cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT)
        while True:
            try:
                descendants.update((child.pid, child) for child in
                                   psutil.Process(process.pid).children(recursive=True))
            except psutil.NoSuchProcess:
                pass
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise subprocess.TimeoutExpired(command, timeout)
            try:
                item['exit'] = process.wait(timeout=min(0.5, remaining))
                return item['exit']
            except subprocess.TimeoutExpired:
                pass
    except BaseException as error:
        item['exit'] = ('timeout' if isinstance(error, subprocess.TimeoutExpired) else
                        'interrupted' if isinstance(error, (KeyboardInterrupt, SystemExit)) else
                        'exception' if process is not None else 'launch-error')
        item['error'] = f'{type(error).__name__}: {error}'
        raise
    finally:
        if process is not None:
            try:
                stop_run(process, descendants.values())
                item['cleanup'] = 'completed'
            except BaseException as error:
                item['cleanup'] = f'failed: {type(error).__name__}: {error}'
                raise
            finally:
                item['process_exit'] = process.returncode


def run(options, saved, target, manifest, runs, *, profile=None, mode='default', headless=False):
    target.mkdir(parents=True, exist_ok=False)
    args = arguments(saved)
    original = list(args)
    shot = target / ('no-image.png' if headless else 'end.png')
    for i, arg in enumerate(args):
        if arg == '--log-file':
            args[i + 1] = (target / 'godot-engine.log').as_posix()
        elif arg.startswith('--shot='):
            args[i] = f'--shot={shot.as_posix()}'
        elif arg.startswith('--capture-dir='):
            args[i] = f'--capture-dir={(target / "capture").as_posix()}'
        elif profile and arg.startswith(('--p1-fighter-file=', '--p2-fighter-file=')):
            args[i] = f'{arg.partition("=")[0]}=res://{profile}/{mode}.fighter.json'
        elif profile and arg.startswith(('--p1-visual=', '--p2-visual=')):
            args[i] = f'{arg.partition("=")[0]}=terry98-stage30-{mode}'
    s29.require_same(physical(original), physical(args), 'unchanged original physical arguments')
    log = target / 'duel.log'
    command = [str(options.godot), *args]
    log.with_suffix('.args.txt').write_text(subprocess.list2cmdline(command) + '\n', encoding='utf-8')
    item = {'reference': str(saved), 'output': str(log), 'headless': headless, 'exit': None}
    runs.append(item)
    try:
        with log.open('wb') as stream:
            returncode = execute_owned(command, stream, item)
    finally:
        for path in (log, log.with_suffix('.args.txt')):
            if path.is_file():
                record(path, manifest)
    if returncode:
        raise ValueError(f'new Godot run exit {returncode}: {log}')
    data = retained(log, manifest, headless=headless)
    if headless and shot.exists():
        raise ValueError(f'unexpected headless image: {shot}')
    verify_headless_keys(saved, saved.read_bytes(), data.splitlines(), args)
    return log, data


def stage29(options, manifest, runs):
    base = proof(options, 'stage29', manifest)
    total = 0
    for _, name, mode, moves, contacts in s29.CASES:
        frozen_rows, graphics_rows, new_frozen, new_graphics = [], [], [], []
        old_id = fighter_id(s29.STAGE29_PROFILE, mode)
        ref_id = fighter_id(STAGE30, mode)
        new_id = fighter_id(options.profile, mode)
        for variant in ('sprite', 'plain'):
            frozen = base / 'stage29-headless' / name / variant / 'duel.log'
            graphic = base / 'stage30-vulkan' / name / variant / 'duel.log'
            before = retained(frozen, manifest, headless=True)
            graphics = retained(graphic, manifest)
            s29.require_same(physical(arguments(frozen)), physical(arguments(graphic)), 'mirror physical schedule')
            verify_headless_keys(frozen, before, graphics.splitlines(), arguments(graphic))
            for data in (before, graphics):
                s29.branch(name, data, moves, contacts)
            frozen_rows.append(s29.authority(before))
            graphics_rows.append(s29.profile_rows(s29.authority(graphics), ref_id, old_id))
            s29.require_same(frozen_rows[-1], graphics_rows[-1], f'{name}/{variant} certified mirror chain')
            if not options.verify_only:
                _, data = run(options, frozen, options.output / 'stage29-headless' / name / variant,
                              manifest, runs, headless=True)
                s29.branch(name, data, moves, contacts)
                new_frozen.append(s29.authority(data))
                s29.require_same(frozen_rows[-1], new_frozen[-1], f'{name}/{variant} new core frozen profile')
                _, data = run(options, graphic, options.output / 'stage31-vulkan' / name / variant,
                              manifest, runs, profile=options.profile, mode=mode)
                s29.branch(name, data, moves, contacts)
                new_graphics.append(s29.profile_rows(s29.authority(data), new_id, old_id))
                s29.require_same(frozen_rows[-1], new_graphics[-1], f'{name}/{variant} new Vulkan authority')
        for rows in (frozen_rows, graphics_rows, new_frozen, new_graphics):
            if rows:
                s29.require_same(*rows, f'{name} sprite/plain authority')
        total += len(frozen_rows[0])
        print(f'{name}: {len(frozen_rows[0])} complete authority rows per variant', flush=True)
    s29.assert_equal(9473, total, 'complete Stage 29 mirrored row total')
    return {'pairs': len(s29.CASES), 'authority_rows': total}


def old_wave(options, manifest, runs):
    base = proof(options, 'old-wave', manifest)
    totals = [0, 0, 0]
    scenes = (*wave.WAVE_CASES, *sorted(wave.OTHER_CASES))
    for batch, name in scenes:
        references, current = [], []
        for variant in ('sprite', 'plain'):
            saved = base / batch / name / variant / 'duel.log'
            original = retained(saved, manifest)
            length, expected = SWEEP.rows(saved, original)
            references.append(expected)
            audited = any(row.startswith(SWEEP.WAVE_RESTRICTION_AUTHORITY) for row in expected)
            shared = SWEEP.shared_wave_rows(expected, fighter_id(STAGE30), wave_audited=audited)
            sources = wave.WAVE_CASES.get((batch, name))
            if sources:
                wave.verify_waves(original, sources, Path(name), sprites=variant == 'sprite')
            if not options.verify_only:
                log, data = run(options, saved, options.output / 'old-wave' / batch / name / variant,
                                manifest, runs, profile=options.profile)
                if sources:
                    wave.verify_waves(data, sources, Path(name), sprites=variant == 'sprite')
                frames, actual = SWEEP.rows(log, data)
                s29.assert_equal(length, frames, 'old Wave full frame count')
                actual = s29.profile_rows(actual, fighter_id(options.profile), fighter_id(STAGE30))
                s29.require_same(expected, actual, f'{name}/{variant} complete Stage 30 authority')
                current.append(actual)
        for rows in (references, current):
            if rows:
                s29.require_same(*rows, f'{name} sprite/plain complete authority')
        totals = [a + b for a, b in zip(totals, (length, len(shared), len(expected)), strict=True)]
        print(f'{batch}/{name}: {length} frames, {len(shared)} shared, {len(expected)} complete rows', flush=True)
    s29.assert_equal([1965, 19076, 19218], totals, 'complete old Wave mirrored totals')
    return dict(zip(('pairs', 'frames', 'shared_rows', 'authority_rows', 'sources'), (len(scenes), *totals, 7)))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    execution = parser.add_mutually_exclusive_group(required=True)
    execution.add_argument('--verify-only', action='store_true')
    execution.add_argument('--godot', type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--archive', default=Path('D:/'), type=Path)
    parser.add_argument('--profile', default='data/imported-characters/terry98-stage31-recovery-r2')
    parser.add_argument('--suite', choices=('stage29', 'old-wave'), action='append')
    options = parser.parse_args()
    options.profile_dir = options.profile
    options.output = options.output.resolve()
    options.output.mkdir(parents=True, exist_ok=False)
    report = {'reference_kind': 'certified Stage 30 mirrored reference; deleted originals not restored',
              'mode': 'reference-audit-only' if options.verify_only else 'new-current-build-runs',
              'status': 'incomplete', 'reference_source_commit': 'a96233e', 'hashes': {}, 'runs': [], 'totals': {}}
    try:
        if options.godot:
            options.godot = options.godot.resolve(strict=True)
            record(options.godot, report['hashes'])
        identity = candidate_identity(options)
        (options.output / 'candidate.json').write_text(json.dumps(identity, indent=2) + '\n', encoding='utf-8')
        report['candidate'] = identity
        for relative in identity:
            record(ROOT / relative, report['hashes'])
        record(options.output / 'candidate.json', report['hashes'])
        s29.assert_equal(CURRENT_BUILD, build_hashes(), 'pinned recovered current DLLs')
        inherited_configs(options, report['hashes'])
        for suite in options.suite or ('stage29', 'old-wave'):
            report['totals'][suite] = (stage29 if suite == 'stage29' else old_wave)(options, report['hashes'], report['runs'])
        s29.assert_equal(identity, candidate_identity(options), 'candidate remained stable')
        for path, expected in list(report['hashes'].items()):
            s29.assert_equal(expected, digest(Path(path)), f'evidence remained stable {path}')
        report['status'] = 'reference-audit-passed' if options.verify_only else 'new-runs-passed'
        print(f'{report["status"]}: {report["totals"]}', flush=True)
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        report['error'] = str(error)
        raise SystemExit(str(error)) from error
    finally:
        (options.output / 'mirror-report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
