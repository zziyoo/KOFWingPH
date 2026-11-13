"""Check or replay the Stage 18 fixed authority overlay and direct interruption."""

import argparse
from pathlib import Path
from subprocess import TimeoutExpired
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Stage18Validation"))
from reentry_duel import REENTRY_AUTHORITY, ROOT, require_same, run_godot
from stage17_authority import recorded_arguments


FROZEN = ROOT / "local-mugen/stage18-validation"
INTERRUPT = ROOT / "local-mugen/stage18-closeout/interruption-final"


def cases(root, interrupt):
    selected = {}
    for folder in ("fixed-down", "fixed-refined", "fixed-boundary", "fixed-v1"):
        for trace in sorted((root / folder).glob("*/sprite/trace.json")):
            fixture = trace.parent.parent
            selected.setdefault(fixture.name, fixture)
    return {**selected, "direct-interruption": interrupt}


def authority(log):
    rows = [line for line in log.splitlines() if line.startswith(REENTRY_AUTHORITY)]
    counts = [sum(line.startswith(tag) for line in rows) for tag in REENTRY_AUTHORITY]
    if not counts[0] or counts != [counts[0] * multiplier for multiplier in (1, 1, 2, 2, 2)]:
        raise ValueError(f"unexpected Stage 18 authority line counts: {counts}")
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--archive-only", action="store_true")
    mode.add_argument("--godot", type=Path)
    parser.add_argument("--output", type=Path)
    options = parser.parse_args()
    if options.godot:
        if options.output is None:
            parser.error("--godot requires --output")
        options.output = options.output.resolve()
        if (ROOT / "local-mugen").resolve() not in options.output.parents:
            parser.error("--output must be a fresh local-mugen directory")
    try:
        if options.godot:
            options.output.mkdir(parents=True, exist_ok=False)
        frozen = cases(FROZEN, INTERRUPT)
        total_rows = 0
        for name, directory in frozen.items():
            expected = authority((directory / "sprite/duel.log").read_bytes())
            require_same(expected, authority((directory / "plain/duel.log").read_bytes()), name)
            if name == "direct-interruption":
                require_same(expected, authority((directory / "seed-repeat/duel.log").read_bytes()), name)
            if options.godot:
                for variant in ("sprite", "plain"):
                    out = options.output / name / variant
                    out.mkdir(parents=True)
                    shot = out / "end.png"
                    args = recorded_arguments(directory / variant / "duel.args.txt", shot)
                    actual = authority(run_godot(options.godot, args, out / "duel.log", shot))
                    require_same(expected, actual, f"{name}/{variant}")
            total_rows += len(expected)
            print(f"{name}: {len(expected)} exact authority rows", flush=True)
        if (len(frozen), total_rows) != (53, 39776):
            raise ValueError(f"incomplete Stage 18 evidence: {len(frozen)} / {total_rows}")
        print(f"Matched {len(frozen)} Stage 18 fixtures, {total_rows // 8} logic frames and {total_rows} authority rows.")
    except (OSError, ValueError, TimeoutExpired) as error:
        parser.exit(1, f"Stage 18 authority replay failed: {error}\n")


if __name__ == "__main__":
    main()
