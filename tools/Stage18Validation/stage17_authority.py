"""Replay the frozen Stage 17 Vulkan authority cases with their original input and seed."""

import argparse
from pathlib import Path
from subprocess import TimeoutExpired
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Stage17Validation"))
from damage_duel import DAMAGE_AUTHORITY, GUARD_SCENES, MODES, SCENES, ROOT, require_same, run_godot
from stage16_authority import SPRITE, recorded_arguments


BASELINE = ROOT / "local-mugen/stage17-validation"
GROUPS = ("primary", "guard", "seeds", "ko")


def cases():
    for mode in MODES:
        for scene in SCENES:
            folder = "fixed-carry" if scene.startswith("carry-") else "fixed-v1"
            yield "primary", Path(folder, f"{mode}-damage-{scene}")
        for scene in GUARD_SCENES:
            yield "guard", Path("fixed-guard", f"{mode}-damage-{scene}")
    for run in ("seed2026", "seed2026-repeat"):
        yield "seeds", Path(run, "default-damage-interrupt")
    yield "ko", Path("fixed-ko")


def authority(log, sprites, fixture):
    lines = log.splitlines()
    counts = tuple(sum(line.startswith(tag) for line in lines) for tag in DAMAGE_AUTHORITY)
    frames, entities, moves, damage = counts
    if frames == 0 or entities != frames or moves != 2 * frames or damage != 2 * frames:
        raise ValueError(f"{fixture}: incomplete authority audit {counts}")
    if any(line.startswith(SPRITE) for line in lines) != sprites:
        raise ValueError(f"{fixture}: unexpected sprite render audit presence")
    return frames, [line for line in lines if line.startswith(DAMAGE_AUTHORITY)]


def replay(options):
    fixtures = total_frames = total_rows = 0
    for group, case in cases():
        if options.group and group not in options.group:
            continue
        expected = []
        actual = []
        for variant in ("sprite", "plain"):
            sprites = variant == "sprite"
            saved = options.baseline / case / variant
            label = f"{case}/{variant}"
            frames, rows = authority((saved / "duel.log").read_bytes(), sprites, f"frozen {label}")
            expected.append(rows)
            if options.godot is not None:
                output = options.output / case / variant
                output.mkdir(parents=True)
                shot = output / "end.png"
                args = recorded_arguments(saved / "duel.args.txt", shot)
                log = run_godot(options.godot, args, output / "duel.log", shot)
                _, rows = authority(log, sprites, f"candidate {label}")
                require_same(expected[-1], rows, f"{label} candidate vs frozen")
                actual.append(rows)
        require_same(*expected, f"{case} frozen sprite vs plain")
        if actual:
            require_same(*actual, f"{case} candidate sprite vs plain")
        fixtures += 1
        total_frames += frames
        total_rows += len(expected[0])
        print(f"{case}: {frames} frames, {len(expected[0])} matching authority rows", flush=True)
    print(f"Matched {fixtures} Stage 17 fixtures, {total_frames} frames and {total_rows} authority rows.")
    if not options.group and (fixtures, total_frames, total_rows) != (45, 4247, 25482):
        raise ValueError(f"unexpected Stage 17 frozen totals: {fixtures}, {total_frames}, {total_rows}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--archive-only", action="store_true")
    mode.add_argument("--godot", type=Path)
    parser.add_argument("--baseline", default=BASELINE, type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--group", choices=GROUPS, action="append")
    options = parser.parse_args()
    try:
        if options.godot is not None:
            options.godot = options.godot.resolve(strict=True)
            if options.output is None:
                parser.error("--godot requires --output")
            options.output = options.output.resolve()
            if (ROOT / "local-mugen").resolve() not in options.output.parents:
                parser.error("--output must be a fresh directory under local-mugen")
            options.output.mkdir(parents=True, exist_ok=False)
        replay(options)
    except (OSError, ValueError, TimeoutExpired) as error:
        parser.exit(1, f"Stage 17 authority gate failed: {error}\n")


if __name__ == "__main__":
    main()
