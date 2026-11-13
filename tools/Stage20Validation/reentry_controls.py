"""Replay far-light re-entry and rejected crouch-input controls with Stage 20 profiles."""

import argparse
from pathlib import Path
import sys
from subprocess import TimeoutExpired

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Stage19Validation"))
from power_wave_duel import ROOT, NATIVE_MOVES, assert_equal, battle_events, require_same, run_godot
from stage18_authority import REENTRY_AUTHORITY, recorded_arguments


CASES = {
    "approach-90-20": ("fixed-v1", ((27, 200), (34, 205))),
    "whiff-12": ("fixed-v1", ((19, 200), (26, 200))),
    "whiff-down-12": ("fixed-down", ((19, 200),)),
}


def run(exe, output, case, mode):
    name = f"{mode}-reentry-{case}"
    folder, transitions = CASES[case]
    source = ROOT / "local-mugen/stage18-validation" / folder / name
    expected = [(frame, 0, NATIVE_MOVES[state]) for frame, state in transitions]
    rows = []
    for variant in ("sprite", "plain"):
        path = output / name / variant
        path.mkdir(parents=True)
        shot = path / "end.png"
        args = recorded_arguments(source / variant / "duel.args.txt", shot)
        for seat in ("p1", "p2"):
            file_flag = f"--{seat}-fighter-file="
            index, = (index for index, arg in enumerate(args) if arg.startswith(file_flag))
            preset = Path(args[index]).name.split(".")[0]
            args[index] = f"{file_flag}res://data/imported-characters/terry98-stage20-data/{preset}.fighter.json"
            alias = f"--{seat}-visual=terry98-stage18-"
            args = [arg.replace(alias, f"--{seat}-visual=terry98-stage20-") for arg in args]
        if variant == "sprite":
            args += [f"--capture-dir={path.as_posix()}/frames", "--capture-start=28", "--capture-end=48"]
        log = run_godot(exe, args, path / "duel.log", shot)
        events = battle_events(log.decode("utf-8").splitlines())
        entries = [(event["frame"], event["source"], event["move"]) for event in events if event["kind"] == "MoveStarted"]
        assert_equal(expected, entries, name)
        variant_rows = [line for line in log.splitlines() if line.startswith(REENTRY_AUTHORITY)]
        if not variant_rows:
            raise ValueError(f"{name}/{variant}: missing authority rows")
        rows.append(variant_rows)
    require_same(*rows, name)
    print(f"{name}: {[state for _, state in transitions]}, {len(rows[0])} exact authority rows", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--godot", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--case", choices=CASES, action="append")
    args = parser.parse_args()
    try:
        output = args.output.resolve()
        output.mkdir(parents=True, exist_ok=False)
        for mode in ("default", "mode2"):
            for case in args.case or CASES:
                run(args.godot, output, case, mode)
    except (OSError, ValueError, TimeoutExpired) as error:
        parser.exit(1, f"Stage 20 re-entry control failed: {error}\n")


if __name__ == "__main__":
    main()
