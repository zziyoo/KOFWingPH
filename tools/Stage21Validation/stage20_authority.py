"""Replay the frozen Stage 20 visual-overlay pairs with the current Godot build."""

import argparse
from pathlib import Path
from subprocess import TimeoutExpired
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'Stage20Validation'))
from stage19_authority import AUTHORITY, ROOT, recorded_arguments, require_same, run_godot


FROZEN = ROOT / 'local-mugen/stage20-validation'
GROUPS = {'fixed-final': 31, 'fixed-mode2': 2, 'terminal-priority': 2, 'reentry-controls': 4,
          'reentry-down-controls': 2}


def authority(path, data):
    rows = [line for line in data.splitlines() if line.startswith(AUTHORITY)]
    counts = [sum(line.startswith(tag) for line in rows) for tag in AUTHORITY[:5]]
    if not counts[0] or counts != [counts[0] * multiplier for multiplier in (1, 1, 2, 2, 2)]:
        raise ValueError(f'{path}: incomplete authority trace {counts}')
    return counts[0], rows


def compare(godot, output):
    scenes = frames = count = 0
    for group, expected_count in GROUPS.items():
        fixtures = sorted(path.parent.parent for path in (FROZEN / group).glob('*/sprite/duel.log'))
        if len(fixtures) != expected_count:
            raise ValueError(f'{group}: expected {expected_count} frozen fixtures, found {len(fixtures)}')
        for fixture in fixtures:
            original = fixture / 'sprite/duel.log'
            length, expected = authority(original, original.read_bytes())
            for variant in ('sprite', 'plain'):
                frozen = fixture / variant / 'duel.log'
                require_same(expected, authority(frozen, frozen.read_bytes())[1], f'{fixture.name} archived {variant}')
                if godot is not None:
                    destination = output / group / fixture.name / variant
                    destination.mkdir(parents=True)
                    shot = destination / 'end.png'
                    args = recorded_arguments(frozen.with_suffix('.args.txt'), shot)
                    log = destination / 'duel.log'
                    actual = run_godot(godot, args, log, shot)
                    require_same(expected, authority(log, actual)[1], f'{fixture.name} replayed {variant}')
            scenes += 1
            frames += length
            count += len(expected)
        print(f'{group}: {len(fixtures)} exact fixture pairs', flush=True)
    if (scenes, frames, count) != (41, 4623, 43202):
        raise ValueError(f'incomplete Stage 20 evidence: {scenes} / {frames} / {count}')
    print(f'Matched {scenes} Stage 20 fixtures, {frames} logic frames and {count} authority rows.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument('--archive-only', action='store_true')
    mode.add_argument('--godot', type=Path)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    if args.godot and args.output is None:
        parser.error('--godot requires --output')
    try:
        if args.godot:
            args.output = args.output.resolve()
            args.output.mkdir(parents=True, exist_ok=False)
        compare(args.godot, args.output)
    except (OSError, ValueError, TimeoutExpired) as error:
        parser.exit(1, f'Stage 20 authority replay failed: {error}\n')


if __name__ == '__main__':
    main()
