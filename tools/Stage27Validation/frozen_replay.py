"""Replay the Stage 13C–26 authority gates against archived fixed inputs."""

import argparse
from hashlib import file_digest
from pathlib import Path
import shlex
import subprocess
import sys
from time import sleep
from types import SimpleNamespace

TOOLS = Path(__file__).resolve().parents[1]
for stage in (17, 18, 19, 24, 25, 26):
    sys.path.insert(0, str(TOOLS / f'Stage{stage}Validation'))

import stage16_authority as ground_history
import stage17_authority as damage_history
import stage18_authority as reentry_history
import stage19_22_authority as wave_history
import stage23_authority as crouch_heavy_history
import stage24_authority as sweep_history
import stage25_authority as crouch_light_history


ROOT = sweep_history.ROOT
STAGES = ('13C', *map(str, range(14, 27)))
KEY_AUDIT = '[CaptureHelper] 渲染帧 '.encode()
STAGE18_ARCHIVE_PROFILE = 'res://local-mugen/stage18-import-smoke-v2/'
STAGE18_FINAL = 'data/imported-characters/terry98-stage18-data'
STAGE18_PROFILE_HASHES = {
    'default.fighter.json': 'cff51e70c676a6f19a1f252a4099038fbaa9bdb30be1ca499169ea1694275235',
    'mode2.fighter.json': '5bd6cb577001b941838606f8181ef8ed0ab903f50335e5319d33ba1008cce5a4',
}
TOTALS = {14: (8, 640, 2560), 15: (28, 2710, 10840), 16: (58, 5676, 22704),
          17: (45, 4247, 25482), 18: (53, 4972, 39776), **wave_history.TOTALS,
          23: (47, 7898, 73151), 24: (63, 8192, 79058), 25: crouch_light_history.TOTAL,
          26: (55, 7477, 69155)}
STAGE26_GROUPS = {
    'release': ('far-hit', 'far-wave-whiff-13', 'far-wave-whiff-24', 'near-hit', 'near-reenter-guard-release-back-23',
                'near-reenter-guard-release-down-23', 'near-to-crouch-jab-wave-free-release',
                'near-to-far-guard-release-back-40.25', 'near-to-far-guard-release-down-40.25',
                'near-to-jab-wave-clean-held-58', 'near-to-jab-wave-clean-release-58', 'near-wave-whiff-14',
                'recovery-backstep-distance', 'recovery-backstep-own-kick', 'sweep-buffer-ground-cross',
                'sweep-buffer-ground-remain-far', 'threshold-exact'),
    'wave-extended': ('near-to-crouch-jab-wave-free-held', 'near-wave-whiff-13', 'near-wave-whiff-9'),
    'mode2-wave': ('far-wave-mode2-natural', 'near-wave-mode2-10', 'near-wave-mode2-natural'),
    'split': ('near-frozen-direction-split-button-first',),
    'split-resume': ('near-frozen-direction-split-button-neutral-first', 'near-frozen-direction-split-diagonal-first',
                     'near-frozen-direction-split-direction-first', 'near-frozen-direction-x-11',
                     'near-frozen-direction-y-11'),
    'final-ordinary': ('far-no-chain-13', 'near-self-whiff-15', 'near-self-whiff-16', 'near-to-crouch-kick-whiff-16',
                       'near-to-crouch-kick-whiff-8', 'near-to-crouch-kick-whiff-9'),
    'final-ordinary-mode2': ('near-self-whiff-15', 'near-self-whiff-9'),
    'final-wave-outcomes': ('far-wave-mixed-natural', 'near-to-far-ground-hit-34-31', 'near-wave-guard-21',
                           'near-wave-hit-21', 'near-wave-mixed-natural'),
    'final-430-last': ('near-to-crouch-kick-grounded-last',),
    'grounded-205-aligned': ('near-to-close-jab-wave-grounded-aligned-held', 'near-to-close-jab-wave-grounded-aligned-release'),
    'natural-720': ('far-dodge-recovery-edge', 'near-dodge-recovery-edge'),
    'final-body': ('far-interrupt', 'mirror-near-hit', 'near-guard', 'p2-far-hit', 'threshold-above', 'threshold-below'),
    'final-body-mode2': ('far-crouch-guard', 'near-hit'),
}


def build_hashes():
    base = ROOT / '.godot/mono/temp/bin/Debug'
    result = {}
    for filename in ('NewKOF.dll', 'NewKOF.BattleCore.dll'):
        with (base / filename).open('rb') as source:
            result[filename] = file_digest(source, 'sha256').hexdigest()
    return result


def wait_for_replay_slot(hold_file):
    if hold_file and hold_file.is_file():
        print(f'Queue paused by {hold_file}', flush=True)
        while hold_file.is_file():
            sleep(1)


def stage13c_gate(options, output):
    subprocess.run([sys.executable, '-u', str(TOOLS / 'Stage14Validation/frozen_authority.py'),
                    '--godot', str(options.godot), '--output', str(output)], cwd=ROOT, check=True)


def selected_groups(groups):
    for group, entries in groups.items():
        if isinstance(entries, dict):
            for folder, names in entries.items():
                for name in names:
                    yield Path(group, folder, name)
        else:
            for name in entries:
                yield Path(group, name)


def frozen_cases(archive, stage):
    def archived(*suffixes):
        return tuple(archive / f'NewKOF-stage26-{suffix}' for suffix in suffixes)

    if stage <= 18:
        previous = ROOT / 'local-mugen/stage26-freeze'
        roots = (previous / f'stage{stage}',)
        if stage == 15:
            roots += (previous / 'stage15-fractional-far-64.5',)
        if stage == 18:
            roots += (archived('resume-18')[0] / 'stage18',)
        cases = sorted({path.parent.parent.relative_to(root) for root in roots
                        for path in root.glob('**/sprite/duel.args.txt') if (path.parent / 'end.png').is_file()})
        if stage <= 16:
            def row_filter(path, data):
                return ground_history.audit_rows(data, path.parent.name == 'sprite', path)
        elif stage == 17:
            def row_filter(path, data):
                return damage_history.authority(data, path.parent.name == 'sprite', path)
        else:
            def row_filter(path, data):
                records = reentry_history.authority(data)
                return len(records) // 8, records
    elif stage in wave_history.TOTALS:
        suffixes = {19: ('frozen19-22',), 20: ('frozen19-22',), 21: ('resume-21', 'frozen19-22'),
                    22: ('resume-22',)}[stage]
        roots = tuple(root / f'stage{stage}' for root in archived(*suffixes))
        if stage == 19:
            cases = map(Path, wave_history.STAGE19_FIXTURES)
        else:
            found = {path.parent.parent.relative_to(root) for root in roots
                     for path in root.glob('**/sprite/duel.args.txt') if (path.parent / 'end.png').is_file()}
            for group, expected in wave_history.GROUPS[stage].items():
                if sum(path.parts[0] == group for path in found) != expected:
                    raise ValueError(f'invalid Stage {stage} archive group: {group}')
            cases = sorted(found)
        row_filter = (lambda path, data: wave_history.stage19_rows(data)) if stage == 19 else wave_history.authority
    elif stage == 23:
        roots = archived('frozen23-compact', 'frozen23-resume', 'frozen23')
        cases = selected_groups(crouch_heavy_history.GROUPS)
        row_filter = crouch_heavy_history.authority
    elif stage == 24:
        roots = archived('frozen24-resume', 'frozen24')
        cases = selected_groups(sweep_history.GROUPS)
        row_filter = sweep_history.rows
    elif stage == 25:
        roots = archived('frozen25')
        cases = selected_groups(crouch_light_history.GROUPS)
        row_filter = sweep_history.rows
    else:
        roots = archived('verified')
        cases = selected_groups(STAGE26_GROUPS)
        row_filter = sweep_history.rows
    return roots, tuple(cases), row_filter


def archived_variant(roots, scene, variant):
    for root in roots:
        path = root / scene / variant
        shot, log, command = (path / name for name in ('end.png', 'duel.log', 'duel.args.txt'))
        if not all(file.is_file() for file in (shot, log, command)):
            continue
        data = log.read_bytes()
        if data.startswith(b'Godot Engine v4.7.2.stable.mono.') and not any(
                line.startswith((b'ERROR:', b'SCRIPT ERROR:')) for line in data.splitlines()):
            return log, data
    raise ValueError(f'missing verified archive variant: {scene}/{variant}')


def replay_headless(options, saved, archived, folder):
    """Keep the same fixed-cycle input schedule without requesting a dummy image."""
    folder.mkdir(parents=True, exist_ok=False)
    log = folder / 'duel.log'
    shot = folder / 'no-image.png'
    tokens = shlex.split(saved.with_suffix('.args.txt').read_text(encoding='utf-8-sig'), posix=False)
    args = [token.strip('"') for token in tokens[1:]
            if not token.startswith(('--capture-dir=', '--capture-start=', '--capture-end='))]
    for index, arg in enumerate(args):
        key, _, value = arg.partition('=')
        if key not in ('--p1-fighter-file', '--p2-fighter-file') or not value.startswith(STAGE18_ARCHIVE_PROFILE):
            continue
        filename = value.removeprefix(STAGE18_ARCHIVE_PROFILE)
        final = ROOT / STAGE18_FINAL / filename
        if filename not in STAGE18_PROFILE_HASHES or not final.is_file():
            raise ValueError(f'missing Stage 18 frozen profile: {final}')
        with final.open('rb') as source:
            if file_digest(source, 'sha256').hexdigest() != STAGE18_PROFILE_HASHES[filename]:
                raise ValueError(f'Stage 18 frozen profile hash mismatch: {final}')
        args[index] = f'{key}=res://{STAGE18_FINAL}/{filename}'
    if args.count('--') != 1 or args[args.index('--fixed-fps') + 1] != '60':
        raise ValueError(f'invalid fixed replay arguments: {saved}')
    for key in ('--rendering-method', '--rendering-driver', '--resolution'):
        index = args.index(key)
        del args[index:index + 2]
    shot_arg, = (index for index, arg in enumerate(args) if arg.startswith('--shot='))
    count_arg, = (index for index, arg in enumerate(args) if arg.startswith('--shotframes='))
    count = int(args[count_arg].partition('=')[2])
    args[shot_arg] = f'--shot={shot.as_posix()}'
    args[count_arg] = f'--shotframes={count + 1}'
    marker = args.index('--')
    args[marker:marker] = ['--headless', '--quit-after', str(count), '--log-file',
                           (options.output / 'godot-engine.log').as_posix()]
    command = [str(options.godot), *args]
    log.with_suffix('.args.txt').write_text(subprocess.list2cmdline(command) + '\n', encoding='utf-8')
    data = run_logged(command, log)
    if shot.exists():
        raise ValueError(f'headless Godot execution failed: {log}')
    verify_headless_keys(saved, archived, data.splitlines(), args)
    return log, data


def run_logged(command, log):
    for attempt in range(2):
        with log.open('wb') as stream:
            result = subprocess.run(command, cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT, timeout=25)
        data = log.read_bytes()
        if (result.returncode == 0 and data.startswith(b'Godot Engine v4.7.2.stable.mono.') and
                not any(line.startswith((b'ERROR:', b'SCRIPT ERROR:', b'Vulkan ', b'Fatal error.'))
                        for line in data.splitlines())):
            return data
        if attempt == 0 and b'Leaked unsafe reference to object: InputEventKey:' in data:
            log.rename(log.with_name('duel-first-shutdown.log'))
            print(f'Retrying one Godot Mono shutdown error: {log}', flush=True)
            continue
        raise ValueError(f'headless Godot execution failed: {log}')


def verify_headless_keys(saved, archived, lines, args):
    expected_keys = [line for line in archived.splitlines() if line.startswith(KEY_AUDIT)]
    actual_keys = [line for line in lines if line.startswith(KEY_AUDIT)]
    if len(expected_keys) != sum(arg.startswith('--renderkey=') for arg in args):
        raise ValueError(f'incomplete frozen physical input audit: {saved}')
    sweep_history.require_same(expected_keys, actual_keys, f'{saved} fixed physical inputs')


def replay_stage(options, stage, output):
    roots, scenes, row_filter = frozen_cases(options.archive, stage)
    frames = records = 0
    for scene in scenes:
        expected = None
        for variant in ('sprite', 'plain'):
            saved, archived = archived_variant(roots, scene, variant)
            length, golden = row_filter(saved, archived)
            if expected is None:
                expected = golden
                frames += length
                records += len(golden)
            else:
                sweep_history.require_same(expected, golden, f'{scene} archived {variant}')
            if options.godot:
                wait_for_replay_slot(options.hold_file)
                if options.reuse and stage == 14:
                    log, data = archived_variant((options.reuse / 'stage14',), scene, variant)
                elif options.headless_authority:
                    log, data = replay_headless(options, saved, archived, output / scene / variant)
                else:
                    folder = output / scene / variant
                    comparison = SimpleNamespace(godot=options.godot, resolution='320x180')
                    log, data = sweep_history.candidate(comparison, saved, folder)
                sweep_history.require_same(expected, row_filter(log, data)[1], f'{scene} current {variant}')
            print(f'{scene.as_posix()}/{variant} <- {saved}', flush=True)
    total = (len(scenes), frames, records)
    if total != TOTALS[stage]:
        raise ValueError(f'Stage {stage} evidence totals {total}, expected {TOTALS[stage]}')
    print(f'Matched Stage {stage}: {len(scenes)} pairs, {frames} logic frames, {records} authority rows', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument('--godot', type=Path)
    mode.add_argument('--archive-only', action='store_true')
    parser.add_argument('--archive', type=Path, default=Path('D:/'))
    parser.add_argument('--output', type=Path)
    parser.add_argument('--stage', choices=STAGES, action='append')
    parser.add_argument('--hold-file', type=Path, help='pause before the next Godot launch while this file exists')
    parser.add_argument('--reuse', type=Path, help='reuse completed Stage 14 output with the identical build fingerprint')
    parser.add_argument('--headless-authority', action='store_true', help='replay Stage 14–26 fixed inputs without frame readback')
    options = parser.parse_args()
    if options.godot and options.output is None:
        parser.error('--godot requires a new --output directory under local-mugen')
    requested = options.stage or (STAGES if options.godot else STAGES[1:])
    if options.archive_only and '13C' in requested:
        parser.error('use the original Stage 13C verifier for its ZIP archive-only check')
    if options.headless_authority and options.archive_only:
        parser.error('headless authority needs Godot')
    try:
        hashes = None
        if options.godot:
            options.godot = options.godot.resolve(strict=True)
            options.output = options.output.resolve()
            if (ROOT / 'local-mugen').resolve() not in options.output.parents:
                raise ValueError('output must be a new directory under local-mugen for Stage 13C')
            options.output.mkdir(parents=True, exist_ok=False)
            hashes = build_hashes()
            identity = f'Candidate Godot Debug build {hashes}'
            if options.reuse:
                previous_log = options.reuse.with_suffix('.log')
                if previous_log.read_text(encoding='utf-8-sig').splitlines()[0] != identity:
                    raise ValueError('cannot reuse Stage 14 logs from a different Godot Debug build')
            print(identity, flush=True)
        for stage in requested:
            output = options.output / f'stage{stage.lower()}' if options.godot else None
            wait_for_replay_slot(options.hold_file)
            if stage == '13C':
                stage13c_gate(options, output)
            else:
                replay_stage(options, int(stage), output)
            if hashes and build_hashes() != hashes:
                raise ValueError('candidate Godot Debug build changed during the replay')
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        parser.exit(1, f'Stage 27 history replay failed: {error}\n')


if __name__ == '__main__':
    main()
