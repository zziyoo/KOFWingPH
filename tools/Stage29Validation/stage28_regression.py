"""Replay the six Stage 28 runtime controls and compare their archived authority rows."""

import argparse
from hashlib import file_digest
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'Stage28Validation'))
from runtime_check import ROOT, SCENES, run_scene

AUTHORITY = (b'[MoveAudit] ', b'[VisualAudit] ', b'[EntityAudit] ', b'[SpriteFrameAudit] ')


def build_identity():
    build = ROOT / '.godot/mono/temp/bin/Debug'
    result = {}
    for name in ('NewKOF.dll', 'NewKOF.BattleCore.dll'):
        with (build / name).open('rb') as source:
            result[name] = file_digest(source, 'sha256').hexdigest()
    return result


def authority(path):
    rows = [line for line in path.read_bytes().splitlines() if line.startswith(AUTHORITY)]
    if not rows:
        raise ValueError(f'archive contains no authority rows: {path}')
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--godot', type=Path)
    parser.add_argument('--output', required=True, type=Path, help='Fresh run directory, or existing current logs with --compare-only')
    parser.add_argument('--archive', type=Path, default=Path('local-mugen/stage28-runtime-check-final'))
    parser.add_argument('--compare-only', action='store_true', help='Compare existing current replay logs without starting Godot')
    options = parser.parse_args()
    if not options.compare_only and not options.godot:
        parser.error('--godot is required for a fresh replay')
    options.frames, options.timeout = 76, 90
    build = None
    if not options.compare_only:
        options.output.mkdir(parents=True, exist_ok=False)
        build = build_identity()
        print(f'Stage 28 regression using Debug build {build}', flush=True)
    total = 0
    for name, *scene in SCENES:
        current = options.output / name
        if not options.compare_only:
            failures, current = run_scene(options, name, *scene)
            if failures:
                parser.exit(1, f'{name}: {failures}\n')
        try:
            archived = authority(options.archive / name / 'duel.log')
            observed = authority(current / 'duel.log')
        except (OSError, ValueError) as error:
            parser.exit(1, f'{name}: {error}\n')
        if archived != observed:
            first = next((i for i, pair in enumerate(zip(archived, observed)) if pair[0] != pair[1]),
                         min(len(archived), len(observed)))
            parser.exit(1, f'{name}: authority mismatch at row {first + 1}; see {current / "duel.log"}\n')
        total += len(observed)
        print(f'{name}: matched {len(observed)} Stage 28 authority rows', flush=True)
    if build is not None and build != build_identity():
        parser.exit(1, 'Debug build changed during Stage 28 replay\n')
    print(f'Matched {len(SCENES)} Stage 28 runtime controls, {total} authority rows', flush=True)


if __name__ == '__main__':
    main()
