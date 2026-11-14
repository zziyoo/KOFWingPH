"""Replay Stage 19–22 authority against the verified Stage 23 replay archives."""

import argparse
from pathlib import Path
import sys
from time import sleep

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'Stage23Validation'))
from stage22_authority import GROUPS as STAGE22_GROUPS
from stage20_authority import GROUPS as STAGE20_GROUPS, authority, recorded_arguments, require_same, run_godot
from stage19_authority import FIXTURES as STAGE19_FIXTURES, rows as stage19_rows


ARCHIVES = {
    19: 'NewKOF-stage23-legacy/stage19',
    20: 'NewKOF-stage23-legacy/stage20',
    21: 'NewKOF-stage23-stage21-frozen',
    22: 'NewKOF-stage23-stage22-frozen',
}
GROUPS = {
    20: STAGE20_GROUPS,
    21: {'fixed': 32, 'mode2': 2, 'mixed': 2, 'guard-release': 2, 'legacy': 3, 'reentry': 6},
    22: STAGE22_GROUPS,
}
TOTALS = {19: (10, 1207, 11812), 20: (41, 4623, 43202),
          21: (47, 6062, 56315), 22: (29, 5249, 48260)}


def fixtures(stage, archive):
    if stage == 19:
        for name in STAGE19_FIXTURES:
            yield archive / name
        return
    for group, expected in GROUPS[stage].items():
        found = sorted(path.parent.parent for path in (archive / group).glob('*/sprite/duel.log'))
        if len(found) != expected:
            raise ValueError(f'{group}: expected {expected} archived pairs, found {len(found)}')
        yield from found


def replay(options, stage):
    archive = options.archive / ARCHIVES[stage]
    output = options.output / f'stage{stage}' if options.godot else None
    if output:
        output.mkdir(parents=True, exist_ok=False)

    def authority_rows(path, data):
        return stage19_rows(data) if stage == 19 else authority(path, data)

    count = frames = rows = 0
    for fixture in fixtures(stage, archive):
        relative = fixture.relative_to(archive)
        sprite = fixture / 'sprite/duel.log'
        length, expected = authority_rows(sprite, sprite.read_bytes())
        for variant in ('sprite', 'plain'):
            saved = fixture / variant / 'duel.log'
            require_same(expected, authority_rows(saved, saved.read_bytes())[1], f'{relative} archived {variant}')
            if output:
                destination = output / relative / variant
                destination.mkdir(parents=True)
                shot = destination / 'end.png'
                log = destination / 'duel.log'
                args = recorded_arguments(saved.with_suffix('.args.txt'), shot)
                if options.resolution:
                    args[args.index('--resolution') + 1] = options.resolution
                while options.hold_file and options.hold_file.is_file():
                    sleep(1)
                actual = run_godot(options.godot, args, log, shot)
                require_same(expected, authority_rows(log, actual)[1], f'{relative} replayed {variant}')
        count += 1
        frames += length
        rows += len(expected)
        print(f'{relative}: {length} frames, {len(expected)} exact authority rows', flush=True)
    if (count, frames, rows) != TOTALS[stage]:
        raise ValueError(f'incomplete Stage {stage} evidence: {count} / {frames} / {rows}')
    print(f'Matched {count} Stage {stage} pairs, {frames} logic frames and {rows} authority rows.', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument('--archive-only', action='store_true')
    mode.add_argument('--godot', type=Path)
    parser.add_argument('--archive', type=Path, required=True, help='parent directory of Stage 23 replay archives')
    parser.add_argument('--output', type=Path)
    parser.add_argument('--resolution', choices=('320x180',), help='reduce fixed-frame render cost without altering inputs')
    parser.add_argument('--hold-file', type=Path, help='wait before launching the next Godot process while this file exists')
    parser.add_argument('--stage', type=int, choices=ARCHIVES, action='append')
    options = parser.parse_args()
    if options.godot and options.output is None:
        parser.error('--godot requires --output')
    for stage in options.stage or ARCHIVES:
        replay(options, stage)


if __name__ == '__main__':
    main()
