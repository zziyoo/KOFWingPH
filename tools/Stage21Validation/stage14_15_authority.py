"""Replay the archived Stage 14 and 15 authority runs without dense GPU capture."""

import argparse
from pathlib import Path
import shlex
from subprocess import TimeoutExpired

from stage20_authority import AUTHORITY, ROOT, recorded_arguments, require_same, run_godot


ARCHIVED = ROOT / 'local-mugen/stage20-history'
BASELINES = {
    'compact-14': (8, 640, 2560),
    'compact-15': (26, 2520, 10080),
    'compact-15-fractional': (2, 190, 760),
}
TAGS = AUTHORITY[:3]


def authority(log):
    rows = [row for row in log.splitlines() if row.startswith(TAGS)]
    counts = [sum(row.startswith(tag) for row in rows) for tag in TAGS]
    if not counts[0] or counts != [counts[0] * multiplier for multiplier in (1, 1, 2)]:
        raise ValueError(f'incomplete Stage 14/15 authority rows {counts}')
    return counts[0], rows


def replay_args(path, shot, group):
    if group != 'compact-14':
        return recorded_arguments(path, shot)
    args = [token.strip('"') for token in shlex.split(path.read_text(encoding='utf-8-sig'), posix=False)][1:]
    if args.count('--') != 1 or '--fixed-fps' not in args or sum(arg.startswith('--shot=') for arg in args) != 1:
        raise ValueError(f'{path}: malformed Stage 14 frozen arguments')
    return [f'--shot={shot.as_posix()}' if arg.startswith('--shot=') else arg for arg in args]


def replay(godot, output):
    for group, expected_total in BASELINES.items():
        cases = sorted(path.parent.parent for path in (ARCHIVED / group).glob('*/sprite/duel.log'))
        frames = total = 0
        for case in cases:
            frame_count, reference = authority((case / 'sprite/duel.log').read_bytes())
            for variant in ('sprite', 'plain'):
                source = case / variant / 'duel.log'
                require_same(reference, authority(source.read_bytes())[1], f'{case.name} archived {variant}')
                target = output / group / case.name / variant
                target.mkdir(parents=True)
                shot = target / 'end.png'
                args = replay_args(source.with_suffix('.args.txt'), shot, group)
                data = run_godot(godot, args, target / 'duel.log', shot)
                require_same(reference, authority(data)[1], f'{case.name} replay {variant}')
            frames += frame_count
            total += len(reference)
        if (len(cases), frames, total) != expected_total:
            raise ValueError(f'{group}: incomplete archived fixtures {len(cases)} / {frames} / {total}')
        print(f'{group}: matched {len(cases)} fixtures, {frames} logic frames, {total} authority rows', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--godot', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    try:
        output = args.output.resolve()
        output.mkdir(parents=True, exist_ok=False)
        replay(args.godot, output)
    except (OSError, ValueError, TimeoutExpired) as error:
        parser.exit(1, f'Stage 14/15 authority replay failed: {error}\n')


if __name__ == '__main__':
    main()
