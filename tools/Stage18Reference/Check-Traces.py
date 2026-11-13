"""Assert native Stage 18 re-entry timing, input provenance, and combat outcomes."""

import argparse
import hashlib
import json
from pathlib import Path


MOVES = (200, 205)
MODE_PREFIXES = ("default", "mode2")
PHASES = ("before-command", "after-command", "after-collision", "end")
LIGHT, HEAVY, DOWN = 128, 256, 2
SOURCE_COMMIT = "5dac5caf9a02a9b81bf2264e88a1bbf68e21bba0"
ARCHIVE = "299203338248952735fd33b4951aba84b39580123290abd8ac70387580ff4698"
HELPER = "b2d683f70a3c6dd76587edee1a54f0638f9c6e006d5c70af598ff9b0163dd62a"


def rows(path):
    return [json.loads(line) for line in path.read_bytes().splitlines()]


def read_capture(directory):
    run = json.loads((directory / "run-metadata.json").read_bytes())
    assert (run["engine"], run["commit"], run["capture_profile"]) == ("IKEMEN GO", SOURCE_COMMIT, "Stage18")
    assert run["terry_archive_sha256"] == ARCHIVE and run["source_sha256"]["Helper.cns"] == HELPER
    assert len(set(run["executed_cases"])) == len(run["executed_cases"])
    for name in run["executed_cases"]:
        info = json.loads((directory / f"{name}-metadata.json").read_bytes())
        trace = directory / f"{name}.jsonl"
        assert info["case"] == name and hashlib.sha256(trace.read_bytes()).hexdigest() == info["trace_sha256"], name
        assert info["initial_distance"] == run["scenario_distance"][name], name
        assert all(info[key] == run[key] for key in ("config_sha256", "executable_sha256", "rng_seed")), name
        assert f"COMBAT_RESEARCH_DONE {name}" in (directory / f"{name}-stderr.txt").read_text(
            encoding="utf-8", errors="replace"
        ), name
        data = rows(trace)
        assert all(row["case"] == name for row in data), name
        snapshots = {phase: [row for row in data if row["phase"] == phase] for phase in PHASES}
        for phase in PHASES:
            assert [row["frame"] for row in snapshots[phase]] == list(range(76)), (name, phase)
            assert all(row["roundState"] == 2 and [char["pn"] for char in row["chars"]] == [1, 2]
                       for row in snapshots[phase]), (name, phase)
        initial = snapshots["before-command"][0]["chars"]
        mirrored = "-mirror-" in name
        assert [char["facing"] for char in initial] == ([-1, 1] if mirrored else [1, -1]), name
        assert initial[0]["pos"][0] == info["initial_distance"] * (.5 if mirrored else -.5), name
        assert initial[0]["vars"]["var10"] == (2 if name.startswith("mode2-") else 0), name
        if name.startswith("mode2-"):
            intro = rows(directory / f"{name}-mode.jsonl")
            assert [(row["var10"], row["grantBuffer"]) for row in intro if row["introFrame"] == 11] == [
                (1, 1), (2, 1)
            ], name
        yield name, inspect_trace(name, data, snapshots)


def inspect_trace(name, data, snapshots):
    changes = [row for row in data if row["phase"] == "state-change" and row["seat"] == 1]
    raw = [row["chars"][0]["raw"] for row in snapshots["before-command"]]
    light = [frame for frame, buttons in enumerate(raw) if buttons & LIGHT and (frame == 0 or not raw[frame-1] & LIGHT)]
    reentry = []
    for change in changes:
        if change["from"] not in MOVES or change["to"] not in MOVES or change["ctrl"] or change["var2"]:
            continue
        frame = change["frame"]
        assert change["minus"] == -1 and change["hitPause"] == 0 and change["elem"] == 2, (name, change)
        assert 7 <= change["stateTime"] <= 11 and not change["raw"] & DOWN, (name, change)
        assert change["to"] == (205 if change["bodyDistX"] <= 16 else 200), (name, change)
        press = max(value for value in light if value <= frame)
        if press != frame:
            assert change["helperX"] == 3 - (frame - press) and not change["xBuffer"], (name, change)
        else:
            assert change["xBuffer"], (name, change)
        fresh = snapshots["after-collision"][frame]["chars"][0]
        assert fresh["state"] == fresh["anim"] == change["to"] and fresh["time"] == 1, (name, fresh)
        assert fresh["elem"] == fresh["elemTime"] == 0 and fresh["frame"]["sprite"] == [change["to"], 0], (name, fresh)
        assert not any(fresh[key] for key in ("moveContact", "moveHit", "moveGuarded")), (name, fresh)
        hitdef = next(row for row in data if row["phase"] == "hitdef" and row["hitDefOwner"] == 1 and row["frame"] > frame)
        assert hitdef["frame"] == frame + 3 and hitdef["chars"][0]["state"] == change["to"], (name, hitdef)
        reentry.append((frame, change["from"], change["to"], change["stateTime"], frame - press, change["bodyDistX"]))
    contacts = [(row["frame"], row["chars"][1]["damage"], row["chars"][1]["guarded"])
                for row in snapshots["after-collision"] if row["chars"][1]["gethit"]]
    return reentry, contacts, changes, raw, snapshots


def verify_contract(cases, mode):
    def case(name):
        return cases[f"{mode}-reentry-{name}"]

    def restart(name, expected):
        actual = case(name)[0]
        assert len(actual) == 1 and actual[0][:len(expected)] == expected, (mode, name, actual, expected)

    for press in (10, 11, 12):
        restart(f"whiff-{press}", (12, 200, 200, 7, 12 - press))
    for scenario in ("whiff-held-10", "whiff-held-11", "whiff-both-12", "mirror-whiff-12", "whiff-down-11"):
        restart(scenario, (12, 200, 200, 7))
    restart("whiff-16", (16, 200, 200, 11, 0))
    for scenario in ("whiff-9", "whiff-17", "held", "whiff-heavy-12", "whiff-down-12",
                     "far-hit-9", "far-hit-17", "far-hit-19", "far-hit-20", "far-hit-down-19"):
        assert not case(scenario)[0], (mode, scenario, case(scenario)[0])
    terminal = next(row for row in case("whiff-17")[2] if row["frame"] == 17)
    assert terminal["stateTime"] == 12 and terminal["var2"] == 1 and terminal["to"] == 200, (mode, terminal)
    crouch = next(row for row in case("whiff-down-12")[2] if row["frame"] == 12)
    assert crouch["raw"] == LIGHT | DOWN and crouch["to"] == 400, (mode, crouch)
    assert case("whiff-both-12")[3][12] == LIGHT | HEAVY and case("whiff-heavy-12")[3][12] == HEAVY, mode
    assert case("held")[3][5:41] == [LIGHT] * 36, mode
    for press in (21, 22, 23):
        restart(f"far-hit-{press}", (23, 200, 200, 7, 23 - press))
    for press in (9, 17, 19):
        commands = [row["chars"][0] for row in case(f"far-hit-{press}")[4]["after-command"]]
        assert all(commands[frame]["commands"]["x"][0] == 2 and commands[frame]["hitPause"] > 0
                   for frame in range(press, 20)), (mode, press)
        assert [commands[frame]["commands"]["x"][0] for frame in (20, 21, 23)] == [1, 0, 0], (mode, press)
    restart("far-guard-23", (23, 200, 200, 7, 0))
    restart("near-guard-23", (23, 205, 200, 7, 0))
    restart("near-hit-23", (23, 205, 200, 7, 0, 20))
    restart("mirror-near-hit-23", (23, 205, 200, 7, 0, 20))
    restart("near-tight-hit-23", (23, 205, 205, 7, 0, 12))
    for scenario in ("near-hit-23", "near-tight-hit-23"):
        assert len(case(scenario)[1]) == 2 and all(damage > 0 and not guard for _, damage, guard in case(scenario)[1]), mode
    for scenario in ("far-guard-23", "near-guard-23"):
        assert case(scenario)[1] and all(damage == 0 and guard for _, damage, guard in case(scenario)[1]), mode
    for scenario in ("approach-90-20", "mirror-approach-90-20"):
        restart(scenario, (20, 200, 205, 7, 0, 11.5))
    restart("approach-94p5-20", (20, 200, 205, 7, 0, 16))
    restart("approach-95-20", (20, 200, 200, 7, 0, 16.5))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directories", nargs="+", type=Path)
    args = parser.parse_args()
    cases = {}
    for directory in args.directories:
        for name, result in read_capture(directory):
            assert name not in cases, name
            cases[name] = result
            print(f"{name:40} reentry={result[0]} contacts={result[1]}")
    for mode in MODE_PREFIXES:
        verify_contract(cases, mode)
    print(f"Verified {len(cases)} original Stage 18 traces across both modes")


if __name__ == "__main__":
    if not __debug__:
        raise SystemExit("Run without Python optimization")
    main()
