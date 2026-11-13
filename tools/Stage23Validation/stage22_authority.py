"""Replay the 29 frozen Stage 22 Vulkan pairs with a candidate Godot build."""

import argparse
from pathlib import Path
from subprocess import TimeoutExpired
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'Stage21Validation'))
from stage20_authority import authority, recorded_arguments, require_same, run_godot


GROUPS = {'fixed': 24, 'mode2': 2, 'incoming': 1, 'after-cancel-v3': 1, 'near-fallback-v2': 1}


def replay(archive, godot, output, groups):
    cases = frames = count = 0
    for group in groups:
        fixtures = sorted(path.parent.parent for path in (archive / group).glob('*/sprite/trace.json'))
        if len(fixtures) != GROUPS[group]:
            raise ValueError(f'{group}: expected {GROUPS[group]} frozen fixtures, found {len(fixtures)}')
        for fixture in fixtures:
            original = fixture / 'sprite/duel.log'
            length, expected = authority(original, original.read_bytes())
            for variant in ('sprite', 'plain'):
                frozen = fixture / variant / 'duel.log'
                require_same(expected, authority(frozen, frozen.read_bytes())[1], f'{fixture.name} archived {variant}')
                if godot is None:
                    continue
                destination = output / group / fixture.name / variant
                destination.mkdir(parents=True)
                shot = destination / 'end.png'
                args = recorded_arguments(frozen.with_suffix('.args.txt'), shot)
                log = destination / 'duel.log'
                actual = run_godot(godot, args, log, shot)
                require_same(expected, authority(log, actual)[1], f'{fixture.name} replayed {variant}')
            cases += 1
            frames += length
            count += len(expected)
        print(f'{group}: {len(fixtures)} exact fixture pairs', flush=True)
    if set(groups) == GROUPS.keys() and (cases, frames, count) != (29, 5249, 48260):
        raise ValueError(f'incomplete Stage 22 evidence: {cases} / {frames} / {count}')
    print(f'Matched {cases} Stage 22 fixtures, {frames} logic frames and {count} authority rows.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument('--archive-only', action='store_true')
    mode.add_argument('--godot', type=Path)
    parser.add_argument('--archive', type=Path, required=True)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--group', choices=GROUPS, action='append')
    options = parser.parse_args()
    try:
        if options.godot is not None:
            if options.output is None:
                parser.error('--godot requires --output')
            options.output = options.output.resolve()
            options.output.mkdir(parents=True, exist_ok=False)
        replay(options.archive, options.godot, options.output, options.group or list(GROUPS))
    except (OSError, ValueError, TimeoutExpired) as error:
        parser.exit(1, f'Stage 22 authority replay failed: {error}\n')


if __name__ == '__main__':
    main()
