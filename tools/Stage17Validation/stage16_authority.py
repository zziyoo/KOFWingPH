"""Replay and compare all frozen Stage 16 Vulkan authority scenarios."""

import argparse
from pathlib import Path
import shlex
from subprocess import TimeoutExpired
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Stage16Validation"))
from normals_duel import AUTHORITY, EXTRA_SCENES, MODES, ROOT, SCENES, require_same, run_godot
from frozen_authority import SPRITE


BASELINE = ROOT / "local-mugen/stage16-validation/fixed-final"
REFINED = ROOT / "local-mugen/stage16-validation/fixed-refined"


def audit_rows(log, sprites, fixture):
    lines = log.splitlines()
    counts = tuple(sum(line.startswith(tag) for line in lines) for tag in AUTHORITY)
    frames, entities, moves = counts
    if not frames or entities != frames or moves != 2 * frames:
        raise ValueError(f"{fixture}: incomplete authority audit {counts}")
    if any(line.startswith(SPRITE) for line in lines) != sprites:
        raise ValueError(f"{fixture}: unexpected sprite render audit presence")
    return frames, [line for line in lines if line.startswith(AUTHORITY)]


def recorded_arguments(path, shot):
    # Frozen Windows subprocess command. Keeping quotes preserves the boundary of arguments with spaces.
    tokens = [token.removeprefix('"').removesuffix('"') for token in shlex.split(
        path.read_text(encoding="utf-8-sig"), posix=False)]
    args = tokens[1:]
    if args.count("--") != 1 or sum(arg.startswith("--shot=") for arg in args) != 1:
        raise ValueError(f"{path}: unexpected recorded Godot arguments")
    if "--fixed-fps" not in args or "--capture-input-only" not in args:
        raise ValueError(f"{path}: missing fixed mode or input isolation")
    # A single final image is sufficient for authority comparison. Leave the frozen captures untouched.
    return [f"--shot={shot.as_posix()}" if arg.startswith("--shot=") else arg for arg in args
            if not arg.startswith(("--capture-dir=", "--capture-start=", "--capture-end="))]


def frozen_case(main, refined, name):
    rerun = refined / name
    return rerun if (rerun / "sprite/trace.json").is_file() else main / name


def compare(options):
    total_frames = total_rows = 0
    fixtures = []
    for mode in options.mode or MODES:
        for scene in options.scene or (SCENES | EXTRA_SCENES):
            name = f"{mode}-{scene}"
            directory = frozen_case(options.baseline, options.refined, name)
            expected = []
            actual = []
            for variant in ("sprite", "plain"):
                sprites = variant == "sprite"
                saved = directory / variant
                frames, reference = audit_rows((saved / "duel.log").read_bytes(), sprites, f"frozen {name}/{variant}")
                expected.append(reference)
                if options.godot is not None:
                    output = options.output / name / variant
                    output.mkdir(parents=True)
                    shot = output / "end.png"
                    args = recorded_arguments(saved / "duel.args.txt", shot)
                    log = run_godot(options.godot, args, output / "duel.log", shot)
                    _, rows = audit_rows(log, sprites, f"candidate {name}/{variant}")
                    require_same(reference, rows, f"{name}/{variant} candidate vs frozen")
                    actual.append(rows)
            require_same(*expected, f"{name} frozen sprite vs plain")
            if actual:
                require_same(*actual, f"{name} candidate sprite vs plain")
            fixtures.append(name)
            total_frames += frames
            total_rows += len(expected[0])
            print(f"{name}: {frames} frames, {len(expected[0])} matching authority rows", flush=True)
    if not options.mode and not options.scene and (len(fixtures), total_frames, total_rows) != (58, 5676, 22704):
        raise ValueError(f"unexpected Stage 16 frozen totals: {len(fixtures)}, {total_frames}, {total_rows}")
    print(f"Matched {len(fixtures)} Stage 16 fixtures, {total_frames} frames and {total_rows} authority rows.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--archive-only", action="store_true")
    mode.add_argument("--godot", type=Path)
    parser.add_argument("--baseline", default=BASELINE, type=Path)
    parser.add_argument("--refined", default=REFINED, type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--mode", choices=MODES, action="append")
    parser.add_argument("--scene", choices=SCENES | EXTRA_SCENES, action="append")
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
        compare(options)
    except (OSError, ValueError, TimeoutExpired) as error:
        parser.exit(1, f"Stage 16 authority gate failed: {error}\n")


if __name__ == "__main__":
    main()
