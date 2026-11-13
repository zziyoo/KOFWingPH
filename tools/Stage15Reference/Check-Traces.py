import argparse
import hashlib
import json
from pathlib import Path


def read_rows(file):
    return [json.loads(line) for line in file.read_bytes().splitlines()]


def check_case(directory, metadata, name, distance):
    trace = directory / f"{name}.jsonl"
    rows = read_rows(trace)
    expected_hash = json.loads((directory / f"{name}-metadata.json").read_bytes())
    assert expected_hash["executable_sha256"] == metadata["executable_sha256"]
    digest = hashlib.sha256(trace.read_bytes()).hexdigest()
    assert digest == expected_hash["trace_sha256"]
    assert expected_hash["initial_distance"] == distance
    collision = {row["frame"]: row["chars"] for row in rows if row["phase"] == "after-collision"}
    end = {row["frame"]: row["chars"] for row in rows if row["phase"] == "end"}
    hitdefs = [row for row in rows if row["phase"] == "hitdef"]
    assert len(rows) == 380 + len(hitdefs)
    assert list(collision) == list(end) == list(range(76))
    assert all(row["roundState"] == 2 and row["case"] == name for row in rows)
    mode, scenario = name.split("-", 1)
    alternate = mode == "mode2"
    action = 211 if alternate else 210
    target_mode = 0
    assert end[4][0]["bodyDistX"] == distance - 30
    assert end[0][0]["pos"][0] == (distance if scenario == "mirror" else -distance) / 2
    assert end[0][1]["pos"][0] == -end[0][0]["pos"][0]
    assert all(pair[0]["vars"]["var10"] == (2 if alternate else 0)
               and pair[1]["vars"]["var10"] == target_mode for pair in end.values())
    if alternate:
        intro = read_rows(directory / f"{name}-mode.jsonl")
        assert all(row["roundState"] == 1 and row["palNo"] == 7 for row in intro)
        assert [(row["introFrame"], row["raw"]) for row in intro if row["phase"] == "after-command" and row["raw"]] == [
            (1, 1), (3, 1), (5, 2), (7, 2), (9, 1), (11, 2),
        ]
        assert [row["var10"] for row in intro if row["introFrame"] == 11] == [1, 2]
        assert [row["grantBuffer"] for row in intro if row["introFrame"] == 11] == [1, 1]

    near = scenario.startswith("close-")
    assert (end[5][0]["state"], end[5][0]["anim"]) == ((215, 215) if near else (210, action))
    incoming = [(f, seat, fighter["guarded"], fighter["damage"])
                for f, pair in collision.items() for seat, fighter in enumerate(pair, 1) if fighter["gethit"]]
    if near:
        assert incoming == [(8, 2, False, 61), (22, 2, False, 29)]
    elif scenario == "trade":
        assert incoming == [(10, 1, False, 54), (10, 2, False, 154)]
        assert [row["hitDefOwner"] for row in hitdefs] == [1, 2]
        assert [fighter["vars"]["var6"] for fighter in collision[10]] == [1, 1]
        assert [fighter["hitPause"] for fighter in collision[10]] == [12, 0]
        assert [fighter["hitPause"] for fighter in end[10]] == [0, 0]
        assert [fighter["hitShake"] for fighter in end[10]] == [13, 13]
        assert [fighter["state"] for fighter in end[10]] == [5000, 5000]
        assert [end[34][0]["ctrl"], end[42][1]["ctrl"]] == [True, True]
        for event in incoming:
            seat, _, damage = event[1:]
            assert end[11][seat - 1]["life"] == 1000 - damage
    elif scenario == "interrupt":
        assert incoming == [(8, 1, False, 54)]
        assert end[8][0]["state"] == 5000 and end[8][0]["hitShake"] == 13
        assert end[8][1]["hitPause"] == 11 and end[9][0]["life"] == 946
        assert end[22][0]["pos"][0] == -42 and end[32][0]["ctrl"] and end[28][1]["ctrl"]
        assert [row["hitDefOwner"] for row in hitdefs] == [2]
    elif scenario in ("whiff", "boundary-87"):
        assert not incoming
        assert [row["frame"] for row in hitdefs] == list(range(10, 17 if alternate else 18))
        assert all(pair[0]["life"] == pair[1]["life"] == 1000 for pair in end.values())
        assert end[34][0]["state"] == 210 and end[35][0]["state"] == 0 and end[35][0]["ctrl"]
        durations = (1, 2, 2, 3, 4, 3, 4, 4, 7) if alternate else (2, 3, 8, 8, 9)
        sprites = ((211, 6), (211, 0), (211, 1), (211, 2), (211, 3), (211, 4), (211, 5), (211, 6), (0, 0)) if alternate else (
            (210, 0), (210, 1), (210, 2), (215, 3), (0, 0),
        )
        expected = [sprite for sprite, duration in zip(sprites, durations, strict=True) for _ in range(duration)]
        assert [tuple(collision[frame][0]["frame"]["sprite"]) for frame in range(5, 35)] == expected
        assert [frame for frame in range(5, 35) if collision[frame][0]["frame"]["clsn1"]] == list(range(10, 17 if alternate else 18))
        assert all(collision[frame][0]["frame"]["clsn1"] == [[12, -84, 67, -61]]
                   for frame in range(10, 17 if alternate else 18))
    else:
        guarded = scenario in ("guard", "crouch-guard", "crouch-guard-late")
        crouched = scenario in ("crouch-guard", "crouch-guard-late")
        assert incoming == [(10, 2, guarded, 0 if guarded else 115)]
        assert collision[10][0]["hitPause"] == 12 and end[10][0]["hitPause"] == 11
        assert [end[10][1]["hitShake"], end[10][1]["hitTime"]] == ([11, 17] if guarded else [13, 18])
        assert end[10][1]["life"] == 1000 and end[11][1]["life"] == (1000 if guarded else 885)
        assert len({(end[f][0]["time"], end[f][0]["elem"], end[f][0]["elemTime"]) for f in range(10, 22)}) == 1
        assert end[22][0]["time"] == end[21][0]["time"] + 1
        assert end[45][0]["state"] == 210 and end[46][0]["state"] == 0 and end[46][0]["ctrl"]
        start = 22 if guarded else 24
        recover = 39 if guarded else 42
        x0 = end[10][1]["pos"][0]
        assert all(end[f][1]["pos"][0] == x0 and not end[f][1]["ctrl"] for f in range(10, start))
        direction = -1 if scenario == "mirror" else 1
        assert end[start][1]["pos"][0] - x0 == 10.5 * direction
        final_displacement = 56.33462 if crouched else 63.8852
        assert abs(end[recover][1]["pos"][0] - x0 - final_displacement * direction) < 1e-4
        assert abs(end[start][1]["vel"][0] + 10.5 * (0.82 if crouched else 0.85)) < 1e-5
        assert not end[recover - 1][1]["ctrl"] and end[recover][1]["ctrl"]
        if guarded:
            assert [end[f][1]["state"] for f in (10, 22, 39)] == ([152, 153, 140] if crouched else [150, 151, 140])
        else:
            assert [end[f][1]["state"] for f in (10, 24, 42)] == [5000, 5001, 0]

    print(f"{name:25} {len(rows):3} {digest}")


def main():
    parser = argparse.ArgumentParser(description="Check Stage 15 original-engine traces")
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    if not __debug__:
        raise SystemExit("Run the trace checker without Python optimization")
    metadata = json.loads((args.directory / "run-metadata.json").read_bytes())
    assert metadata["engine"] == "IKEMEN GO"
    assert metadata["commit"] == "5dac5caf9a02a9b81bf2264e88a1bbf68e21bba0"
    assert metadata["capture_profile"] == "Stage15"
    assert len(metadata["scenario_distance"]) == 26
    for name, distance in metadata["scenario_distance"].items():
        check_case(args.directory, metadata, name, distance)


if __name__ == "__main__":
    main()
