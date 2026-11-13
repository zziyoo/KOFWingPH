"""Replay representative frozen Stage 19 Vulkan scenarios against a candidate binary."""

import argparse
from pathlib import Path
import sys
from subprocess import TimeoutExpired

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Stage19Validation"))
from power_wave_duel import AUTHORITY, ROOT, require_same, run_godot
from stage18_authority import recorded_arguments


FROZEN = ROOT / "local-mugen/stage19-validation"
FIXTURES = {
    "whiff-8": "fixed-final/whiff-8",
    "whiff-12": "fixed-final/whiff-12",
    "hit-11": "fixed-final/hit-11",
    "hit-20": "fixed-final-rest-d/hit-20",
    "hit-23": "fixed-final-rest-d/hit-23",
    "guard-20": "ground-recovery-final/guard-20",
    "far-whiff-9": "ground-recovery-final/far-whiff-9",
    "dual-9": "fixed-final-rest-d/dual-9",
    "mirror-dual-9": "fixed-final-rest-d/mirror-dual-9",
    "launch-recovery": "recovery-v3/hit-20",
}


def rows(log):
    result = [row for row in log.splitlines() if row.startswith(AUTHORITY)]
    tags = [sum(row.startswith(tag) for row in result) for tag in AUTHORITY]
    if not tags[0] or tags[:6] != [tags[0] * count for count in (1, 1, 2, 2, 2, 1)]:
        raise ValueError(f"incomplete Stage 19 authority trace: {tags}")
    return tags[0], result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--archive-only", action="store_true")
    mode.add_argument("--godot", type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args()
    try:
        if options.godot:
            if options.output is None:
                parser.error("--godot requires --output")
            options.output = options.output.resolve()
            options.output.mkdir(parents=True, exist_ok=False)
        frames = count = 0
        for fixture, relative in FIXTURES.items():
            base = FROZEN / relative
            expected_frames, expected = rows((base / "sprite/duel.log").read_bytes())
            for variant in ("sprite", "plain"):
                frozen = base / variant
                _, archived = rows((frozen / "duel.log").read_bytes())
                require_same(expected, archived, f"frozen {fixture}/{variant}")
                if options.godot:
                    out = options.output / fixture / variant
                    out.mkdir(parents=True)
                    shot = out / "end.png"
                    args = recorded_arguments(frozen / "duel.args.txt", shot)
                    _, actual = rows(run_godot(options.godot, args, out / "duel.log", shot))
                    require_same(expected, actual, f"candidate {fixture}/{variant}")
            frames += expected_frames
            count += len(expected)
            print(f"{fixture}: {expected_frames} frames, {len(expected)} authority rows", flush=True)
        print(f"Matched {len(FIXTURES)} representative Stage 19 fixtures, {frames} frames and {count} authority rows.")
    except (OSError, ValueError, TimeoutExpired) as error:
        parser.exit(1, f"Stage 19 authority replay failed: {error}\n")


if __name__ == "__main__":
    main()
