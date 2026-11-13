import argparse
import hashlib
import json
from pathlib import Path


def rows(path):
    return [json.loads(line) for line in path.read_bytes().splitlines()]


def check_case(folder, name, distance, executable):
    trace = folder / f"{name}.jsonl"
    evidence = rows(trace)
    manifest = json.loads((folder / f"{name}-metadata.json").read_bytes())
    digest = hashlib.sha256(trace.read_bytes()).hexdigest()
    assert (manifest["initial_distance"], manifest["executable_sha256"], manifest["trace_sha256"]) == (distance, executable, digest), name
    assert all(row["case"] == name and row["roundState"] == 2 for row in evidence), name
    end = {r["frame"]: r["chars"] for r in evidence if r["phase"] == "end"}
    collision = {r["frame"]: r["chars"] for r in evidence if r["phase"] == "after-collision"}
    pulses = [r for r in evidence if r["phase"] == "hitdef" and r["hitDefOwner"] == 1]
    assert list(end) == list(collision) == list(range(76)), name
    preset, normal, scenario = name.split("-", 2)
    light = normal == "light"
    alt = preset == "mode2"
    mirror = scenario == "mirror"
    whiff = scenario == "whiff"
    simultaneous = scenario.startswith("both-")
    far = scenario.startswith("far-") or scenario == "both-70"
    early_crouch = scenario in ("crouch-guard", "crouch-release", "crouch-to-stand")
    guarded = scenario in ("guard", "crouch-guard", "crouch-late", "second-guard",
                           "guard-release", "crouch-release", "crouch-to-stand")
    close_state = 205 if light else 215
    expected_state = (200 if light else 210) if far else close_state
    action = 211 if expected_state == 210 and alt else expected_state
    entered = 13 if whiff else 5
    start_x = distance / 2 if mirror else -distance / 2
    assert end[0][0]["pos"][0] == start_x and end[0][1]["pos"][0] == -start_x, name
    assert [end[0][0]["facing"], end[0][1]["facing"]] == ([-1, 1] if mirror else [1, -1]), name
    assert (end[4][0]["bodyDistX"], end[4][1]["bodyDistX"]) == (distance - 30, distance - 30), name
    assert all(r[0]["vars"]["var10"] == (2 if alt else 0) and r[1]["vars"]["var10"] == 0 for r in end.values()), name
    assert (end[entered][0]["state"], end[entered][0]["anim"]) == (expected_state, action), name
    if alt:
        intro = rows(folder / f"{name}-mode.jsonl")
        assert [(r["var10"], r["grantBuffer"]) for r in intro if r["introFrame"] == 11] == [(1, 1), (2, 1)], name
    if simultaneous:
        assert end[5][0]["raw"] == 384, name
        command = next(r["chars"][0]["commands"] for r in evidence if r["frame"] == 5 and r["phase"] == "after-command")
        assert all(max(command[button]) > 0 for button in ("x", "y")), name

    contacts = [(r, i, c) for r, pair in collision.items()
                for i, c in enumerate(pair) if c["gethit"]]
    received = [(r, c["guarded"], c["damage"]) for r, i, c in contacts if i == 1]
    if whiff:
        assert not contacts, name
        expected_ticks = range(16, 20 if light else 26)
        assert [r["frame"] for r in pulses] == list(expected_ticks), name
        assert all(collision[t][0]["frame"]["clsn1"] for t in expected_ticks), name
        assert all(end[t][1]["pos"][1] < 0 for t in expected_ticks), name
        exit_frame = 25 if light else 41
        assert end[exit_frame - 1][0]["state"] == expected_state and end[exit_frame][0]["ctrl"], name
    elif scenario == "trade":
        assert [(r, i) for r, i, _ in contacts] == [(8, 0), (8, 1)], name
        assert [c["damage"] for _, _, c in contacts] == ([61, 30] if light else [54, 54]), name
        assert [end[8][i]["hitShake"] for i in (0, 1)] == [13, 13], name
        assert all(end[9][i]["life"] < 1000 for i in (0, 1)), name
    elif far:
        contact_frame, damage = (8, 46) if light else (10, 115)
        assert received == [(contact_frame, False, damage)], name
    elif scenario in ("second-only", "second-guard"):
        assert received == [(11, guarded, 0 if guarded else 69)], name
        assert [r["frame"] for r in pulses] == [8, 9, 10, 11], name
        assert all(r["chars"][0]["vars"]["var23"] == 1 for r in pulses), name
        assert pulses[-1]["chars"][0]["hitDef"]["groundVel"] == [-10.5, 0, 0], name
        assert end[44][0]["ctrl"] and end[40 if guarded else 43][1]["ctrl"], name
        assert collision[11][0]["frame"]["clsn1"] == [[30, -80, 75, -44]], name
    elif light:
        assert received == [(8, guarded, 0 if guarded else 30)], name
        assert len(pulses) == 1 and pulses[0]["frame"] == 8, name
        assert pulses[0]["chars"][0]["hitDef"]["damage"] == 38, name
        assert end[8][0]["hitPause"] == 11 and end[8][1]["hitShake"] == (11 if guarded else 13), name
        assert collision[8][0]["frame"]["clsn1"] == [[3, -91, 49, -67]], name
        assert end[28][0]["ctrl"] and end[29 if guarded else 32][1]["ctrl"], name
        if scenario in ("crouch-guard", "crouch-late"):
            assert end[8][1]["state"] == 152 and end[9][1]["anim"] == 151, name
    else:
        assert received == [(8, guarded, 0 if guarded else 61), (22, guarded, 0 if guarded else 29)], name
        assert [r["frame"] for r in pulses] == [8, 22], name
        assert [r["chars"][0]["vars"]["var23"] for r in pulses] == [1, 2], name
        assert [r["chars"][0]["hitDef"]["damage"] for r in pulses] == [76, 38], name
        assert [r["chars"][0]["hitDef"]["groundVel"] for r in pulses] == [[0, 0, 0], [-10.5, 0, 0]], name
        assert collision[8][0]["frame"]["clsn1"] == [[14, -80, 47, -26]], name
        assert collision[22][0]["frame"]["clsn1"] == [[30, -80, 75, -44]], name
        assert end[8][0]["hitPause"] == end[22][0]["hitPause"] == 11, name
        assert end[8][1]["hitShake"] == end[22][1]["hitShake"] == (11 if guarded else 13), name
        assert end[55][0]["ctrl"] and end[51 if guarded else 54][1]["ctrl"], name
        if early_crouch or scenario == "crouch-late":
            assert end[8][1]["state"] == 152 and end[9][1]["anim"] == 151, name
        if scenario in ("crouch-guard", "crouch-late"):
            assert end[22][1]["state"] == 152 and end[23][1]["anim"] == 151, name
        if scenario in ("guard-release", "crouch-release", "crouch-to-stand"):
            assert collision[22][1]["raw"] == (8 if scenario == "crouch-to-stand" else 0), name
            assert collision[22][1]["state"] == 151 and collision[22][1]["guardFlag"], name
            assert end[22][1]["state"] == 150 and end[23][1]["anim"] == 150, name
    print(f"{name:29} {len(evidence):3} {digest}")


def main():
    parser = argparse.ArgumentParser(description="Check Stage 16 original-engine traces")
    parser.add_argument("directory", type=Path)
    supplemental = parser.add_mutually_exclusive_group()
    supplemental.add_argument("--guard-release", action="store_true", help="verify supplemental guard release cases")
    supplemental.add_argument("--simultaneous", action="store_true", help="verify same-seat light and heavy cases")
    args = parser.parse_args()
    if not __debug__:
        raise SystemExit("Run the trace checker without Python optimization")
    metadata = json.loads((args.directory / "run-metadata.json").read_bytes())
    assert metadata["engine"] == "IKEMEN GO"
    assert metadata["commit"] == "5dac5caf9a02a9b81bf2264e88a1bbf68e21bba0"
    assert metadata["capture_profile"] == "Stage16"
    distances = metadata["scenario_distance"]
    release_cases = ("-guard-release", "-crouch-release", "-crouch-to-stand")
    both_cases = ("-both-44", "-both-70")
    if args.guard_release:
        selected = [name for name in distances if name.endswith(release_cases)]
        assert len(selected) == 6
    elif args.simultaneous:
        selected = [name for name in distances if name.endswith(both_cases)]
        assert len(selected) == 4
    else:
        selected = [name for name in distances if not name.endswith(release_cases + both_cases)]
        assert len(selected) == 46
    for name in selected:
        check_case(args.directory, name, distances[name], metadata["executable_sha256"])


if __name__ == "__main__":
    main()
