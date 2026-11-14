"""Replay accepted Stage 24 Vulkan pairs and verify Wave sources on Stage 25."""

import argparse
import json
from pathlib import Path
from subprocess import TimeoutExpired
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'Stage24Validation'))
from crouch_kick_duel import AUTHORITY, COMMAND_AUTHORITY, ROOT, WAVE_RESTRICTION_AUTHORITY
from crouch_light_kick_duel import NATIVE, PROFILE
from power_wave_duel import MOVE, audit_rows, battle_events
from stage19_22_authority import authority, recorded_arguments, require_same, run_godot


GROUPS = {
    'body': {
        'body-v3': ('crouch-guard-0', 'down-forward-heavy-and-kick-0'),
        'body-controls': ('air-0', 'crouch-hit-0', 'down-back-kick-0', 'down-forward-kick-0',
                          'down-heavy-and-kick-0', 'down-light-and-kick-0', 'down-light-kicks-0', 'guard-0',
                          'held-down-0', 'interrupt-0', 'mirror-hit-0', 'stand-0', 'tech-55', 'tech-57',
                          'tech-60', 'tech-61', 'tech-held-55', 'whiff-0'),
        'body-boundaries': ('jump-and-kick-0', 'stand-to-down-0'),
        'tech-landing': ('tech-56',),
        'final-canonical': ('hit-0',),
    },
    'routes': {
        'routes-final': ('compete-heavy-16', 'compete-kick-16', 'crouch-guard-27', 'crouch-hit-27',
                         'edge-0', 'edge-16', 'hit-19', 'hit-33', 'hit-34', 'hit-held-19', 'incoming-wave-22',
                         'incoming-wave-34', 'mirror-hit-27', 'repeat-kick-23', 'repeat-kick-45', 'whiff-12',
                         'whiff-13', 'whiff-14', 'whiff-15', 'whiff-17', 'whiff-22', 'whiff-23', 'whiff-45'),
        'camera': ('incoming-wave-16', 'incoming-wave-26'),
        'extended': ('hit-repeat-kick-56', 'hit-repeat-kick-walk-58', 'mixed-27'),
        'routes-first': ('whiff-16',),
        'final-canonical': ('hit-27',),
        'final-priority': ('repeat-kick-light-45', 'tech-split-56'),
    },
    'mode2': {'mode2-landing': ('hit-0', 'hit-27')},
    'coexistence': {
        'coexistence-sources200-205': ('mixed-sequential',),
        'coexistence-source215': ('hit-22',),
        'coexistence-source400-final': ('hit-held-11',),
        'coexistence-source410': ('hit-22', 'whiff-11'),
    },
}
TOTALS = {'body': (24, 2784, 26369), 'routes': (32, 4318, 42030), 'mode2': (2, 290, 2816),
          'coexistence': (5, 800, 7843)}
PREFIXES = (*AUTHORITY, COMMAND_AUTHORITY, WAVE_RESTRICTION_AUTHORITY)
WAVE_SOURCES = {
    ('coexistence-sources200-205', 'mixed-sequential'): (205, 200),
    ('coexistence-source215', 'hit-22'): (215,),
    ('coexistence-source400-final', 'hit-held-11'): (400,),
    ('coexistence-source410', 'hit-22'): (410,),
    ('routes-final', 'whiff-13'): (440,),
}


def rows(path, data):
    frames, _ = authority(path, data)
    return frames, [line for line in data.splitlines() if line.startswith(PREFIXES)]


def candidate(options, saved, target, *, profile=None):
    target.mkdir(parents=True, exist_ok=False)
    shot, log = target / 'end.png', target / 'duel.log'
    args = recorded_arguments(saved.with_suffix('.args.txt'), shot)
    if options.resolution:
        args[args.index('--resolution') + 1] = options.resolution
    if profile:
        for seat in (1, 2):
            prefix = f'--p{seat}-fighter-file='
            matches = [i for i, value in enumerate(args) if value.startswith(prefix)]
            index, = matches
            args[index] = f'{prefix}res://{profile}'
            visual = f'--p{seat}-visual='
            args = [f'{visual}{options.visual_alias}' if value.startswith(visual) else value for value in args]
        if '--command-audit' not in args:
            args.append('--command-audit')
    return log, run_godot(options.godot, args, log, shot)


def shared_wave_rows(records, fighter_id, *, wave_audited):
    fighter = f' id={fighter_id} '.encode()
    shared = []
    for row in records:
        if row.startswith(COMMAND_AUTHORITY) or (not wave_audited and row.startswith(WAVE_RESTRICTION_AUTHORITY)):
            continue
        if row.startswith(b'[MoveAudit] '):
            if fighter not in row:
                raise ValueError(f'expected fighter ID {fighter_id} in {row!r}')
            row = row.replace(fighter, b' id=Fighter ', 1)
        shared.append(row)
    return shared


def verify_wave_sources(data, sources, *, sprites):
    lines = data.decode('utf-8').splitlines()
    started = [event['move'] for event in battle_events(lines) if event['kind'] == 'MoveStarted']
    expected = [move for source in sources for move in (NATIVE[source], MOVE)]
    require_same(expected, started, 'Wave source and follow-up sequence')
    entities = audit_rows(lines, 'WaveEntityAudit')
    restrictions = audit_rows(lines, 'WaveRestrictionAudit')
    if len({row['id'] for row in entities}) != len(sources) or not restrictions:
        raise ValueError(f'missing Wave entities or their eligibility audits for {sources}')
    if sprites != any(row['sprite_selected'] == 'True' for row in audit_rows(lines, 'SpriteRenderAudit')):
        raise ValueError('Wave source visual overlay does not match the requested variant')


def replay_wave_sources(options):
    profile = Path(options.wave_profile) / 'default.fighter.json'
    fighter_id = json.loads((ROOT / profile).read_bytes())['fighterId']
    totals = [0, 0, 0, 0]
    for (folder, name), sources in WAVE_SOURCES.items():
        fixture = options.archive / f'NewKOF-stage24-vulkan-{folder}' / name
        reference = fixture / 'sprite/duel.log'
        length, expected = rows(reference, reference.read_bytes())
        wave_audited = any(row.startswith(WAVE_RESTRICTION_AUTHORITY) for row in expected)
        shared = shared_wave_rows(expected, 'TerrySweepWave', wave_audited=wave_audited)
        variants = []
        for variant in ('sprite', 'plain'):
            saved = fixture / variant / 'duel.log'
            require_same(expected, rows(saved, saved.read_bytes())[1], f'{fixture} frozen {variant}')
            target = options.output / folder / name / variant
            log, data = candidate(options, saved, target, profile=profile.as_posix())
            verify_wave_sources(data, sources, sprites=variant == 'sprite')
            _, actual = rows(log, data)
            require_same(shared, shared_wave_rows(actual, fighter_id, wave_audited=wave_audited),
                         f'{fixture} shared authority on Stage 25 {variant}')
            variants.append(actual)
        require_same(*variants, f'{fixture} Stage 25 visual/plain authority')
        totals = [a + b for a, b in zip(totals, (len(sources), length, len(shared), len(variants[0])), strict=True)]
        print(f'{folder}/{name}: {sources}, {length} logic frames, {len(shared)} frozen rows, '
              f'{len(variants[0])} Stage 25 authority rows', flush=True)
    sources, frames, shared, current = totals
    print(f'Matched {sources} Wave sources across {len(WAVE_SOURCES)} pairs, {frames} logic frames, '
          f'{shared} frozen and {current} Stage 25 authority rows.', flush=True)


def replay(options):
    total = [0, 0, 0]
    for group in options.group or GROUPS:
        cases = frames = count = 0
        for folder, scenes in GROUPS[group].items():
            for name in scenes:
                fixture = options.archive / f'NewKOF-stage24-vulkan-{folder}' / name
                reference = fixture / 'sprite/duel.log'
                length, expected = rows(reference, reference.read_bytes())
                for variant in ('sprite', 'plain'):
                    saved = fixture / variant / 'duel.log'
                    require_same(expected, rows(saved, saved.read_bytes())[1], f'{fixture} frozen {variant}')
                    if options.godot:
                        target = options.output / group / folder / name / variant
                        log, actual = candidate(options, saved, target)
                        require_same(expected, rows(log, actual)[1], f'{fixture} replayed {variant}')
                cases += 1
                frames += length
                count += len(expected)
                print(f'{group}/{folder}/{name}: {length} logic frames, {len(expected)} rows', flush=True)
        if (cases, frames, count) != TOTALS[group]:
            raise ValueError(f'incomplete Stage 24 {group} evidence: {cases} / {frames} / {count}')
        total = [a + b for a, b in zip(total, (cases, frames, count), strict=True)]
    cases, frames, count = total
    print(f'Matched {cases} Stage 24 pairs, {frames} logic frames and {count} authority rows.', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument('--archive-only', action='store_true')
    mode.add_argument('--godot', type=Path)
    parser.add_argument('--archive', type=Path, required=True)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--group', choices=GROUPS, action='append')
    parser.add_argument('--resolution', choices=('320x180',))
    parser.add_argument('--wave-profile', nargs='?', const=PROFILE, metavar='PROFILE_DIR',
                        help='verify the six old Wave sources on the Stage 25 profile')
    parser.add_argument('--visual-alias', default='terry98-stage25-default',
                        help='visual alias for the selected Wave profile')
    options = parser.parse_args()
    if options.godot and options.output is None:
        parser.error('--godot requires --output')
    if options.wave_profile and (not options.godot or options.group):
        parser.error('--wave-profile requires --godot without --group')
    try:
        if options.godot:
            options.output = options.output.resolve()
            options.output.mkdir(parents=True, exist_ok=False)
        if options.wave_profile:
            replay_wave_sources(options)
        else:
            replay(options)
    except (OSError, ValueError, TimeoutExpired) as error:
        parser.exit(1, f'Stage 24 authority replay failed: {error}\n')


if __name__ == '__main__':
    main()
