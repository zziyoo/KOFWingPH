"""Replay all frozen Stage 21 fixed Vulkan scenarios with the current build."""

import argparse
from pathlib import Path
from subprocess import TimeoutExpired
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'Stage21Validation'))
from stage20_authority import ROOT, authority, recorded_arguments, require_same, run_godot


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--godot', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--whiff-fallback', required=True, type=Path)
    options = parser.parse_args()
    output = options.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    archive = ROOT / 'local-mugen/stage21-validation'
    total_frames = total_rows = 0
    try:
        for group, count in {'fixed': 32, 'mode2': 2, 'mixed': 2, 'guard-release': 2,
                             'legacy': 3, 'reentry': 6}.items():
            fixtures = sorted(path.parent.parent for path in (archive / group).glob('*/sprite/duel.log'))
            if len(fixtures) != count:
                raise ValueError(f'{group}: expected {count} fixtures, found {len(fixtures)}')
            for fixture in fixtures:
                if group == 'fixed' and fixture.name == 'whiff-17':
                    fixture = options.whiff_fallback
                frozen = fixture / 'sprite/duel.log'
                frames, rows = authority(frozen, frozen.read_bytes())
                for variant in ('sprite', 'plain'):
                    source = fixture / variant / 'duel.log'
                    require_same(rows, authority(source, source.read_bytes())[1], f'{group}/{fixture.name} archive {variant}')
                    path = output / group / fixture.name / variant
                    path.mkdir(parents=True)
                    shot = path / 'end.png'
                    args = recorded_arguments(source.with_suffix('.args.txt'), shot)
                    log = path / 'duel.log'
                    require_same(rows, authority(log, run_godot(options.godot, args, log, shot))[1],
                                 f'{group}/{fixture.name} replay {variant}')
                total_frames += frames
                total_rows += len(rows)
            print(f'{group}: {count} exact fixture pairs', flush=True)
        if (total_frames, total_rows) != (6062, 56315):
            raise ValueError(f'incomplete Stage 21 evidence: {total_frames} frames, {total_rows} rows')
        print(f'Matched 47 Stage 21 fixtures, {total_frames} logic frames and {total_rows} authority rows.')
    except (OSError, ValueError, TimeoutExpired) as error:
        parser.exit(1, f'Stage 21 authority replay failed: {error}\n')


if __name__ == '__main__':
    main()
