import argparse
import hashlib
import json
import math
import struct
from pathlib import Path

MODULUS = 2_147_483_647
BASE_BY_STATE = {200: (7, 8), 205: (5, 6), 210: (16, 20), 215: (9, 9)}
ATTACK_MOVE_TYPE = 131072
MODE2_STARTUP_INTERRUPT_TICKS = {"live": 6, "edge": 6, "last": 7}
STATE_MINUS_TWO_SHA256 = "90b83cdacd33a3800a825ee42bdbd6502b7989f4dec358126587edeb4495fdc4"


def json_rows(path):
    return [json.loads(line) for line in path.read_bytes().splitlines()]


def f32(value):
    return struct.unpack("<f", struct.pack("<f", value))[0]


def verify_trace(directory, name, metadata):
    file = directory / f"{name}.jsonl"
    rows = json_rows(file)
    case = json.loads((directory / f"{name}-metadata.json").read_bytes())
    assert metadata["source_sha256"].get("Terry-2-3.cns", STATE_MINUS_TWO_SHA256) == STATE_MINUS_TWO_SHA256
    assert case["rng_seed"] == metadata["rng_seed"], name
    assert case["executable_sha256"] == metadata["executable_sha256"], name
    assert hashlib.sha256(file.read_bytes()).hexdigest() == case["trace_sha256"], name
    assert all(row["case"] == name for row in rows), name
    state_rows = [row for row in rows if "chars" in row]
    assert all(row["roundState"] == 2 for row in state_rows), name
    assert state_rows[0]["frame"] == 0 and state_rows[-1]["frame"] == 75, name
    assert state_rows[0]["phase"] == "before-command" and state_rows[0]["rng"] == metadata["rng_seed"], name
    assert state_rows[0]["chars"][0]["pos"][0] == (
        case["initial_distance"] if "-mirror-" in name else -case["initial_distance"]
    ) / 2, name
    alternate = name.startswith("mode2-")
    assert state_rows[0]["chars"][0]["vars"]["var10"] == (2 if alternate else 0), name
    if alternate:
        intro = json_rows(directory / f"{name}-mode.jsonl")
        assert [(row["var10"], row["grantBuffer"]) for row in intro if row["introFrame"] == 11] == [(1, 1), (2, 1)], name

    seed = metadata["rng_seed"]
    hitdefs = {}
    contacts = []
    end = {}
    draws = []
    age_changes = []
    last_draw = {}
    for row in rows:
        kind = row["phase"]
        tick = row["frame"]
        if kind == "rng-draw":
            assert row["before"] == seed and row["after"] == seed * 16_807 % MODULUS, (name, tick, row)
            seed = row["after"]
            draws.append(seed)
        elif kind == "var21-draw":
            assert row["min"] == 0 and row["max"] == 2, (name, tick, row)
            assert row["rng"] == seed and row["value"] == seed // (MODULUS // 3 + 1), (name, tick, row)
            last_draw[row["seat"]] = row["value"]
        elif kind == "damage-var":
            assert row["variable"] in (6, 23), (name, tick, row)
            if row["variable"] == 6:
                if row["operation"] == "set" and row["after"] == 1:
                    assert row["opponentMoveType"] == ATTACK_MOVE_TYPE and row["hitPause"] == 0, (name, tick, row)
                elif row["operation"] == "add":
                    assert row["opponentMoveType"] != ATTACK_MOVE_TYPE and row["after"] == row["before"] + 1, (name, tick, row)
                    assert row["hitPause"] == 0, (name, tick, row)
                else:
                    assert row["operation"] == "set" and row["before"] == 4 and row["after"] == 0, (name, tick, row)
                age_changes.append((tick, row["seat"], row["before"], row["after"]))
        else:
            assert row["rng"] == seed, (name, tick, row)
            if kind == "hitdef":
                owner = row["hitDefOwner"]
                actor = row["chars"][owner-1]
                counter, random, divisor = (actor["vars"][key] for key in ("var6", "var21", "var23"))
                assert random == last_draw[owner], (name, tick, owner)
                ordinary, counter_base = BASE_BY_STATE[actor["state"]]
                base = counter_base if counter else ordinary
                multiplier = f32(actor["vars"]["fvar7"])
                raw = math.floor(f32(((base - random) // max(1, divisor)) * multiplier))
                assert actor["hitDef"]["damage"] == raw, (name, tick, owner, actor["vars"], raw)
                hitdefs[(tick, owner)] = (raw, counter, random, divisor, actor["state"])
            elif kind == "after-collision":
                for target, char in enumerate(row["chars"], 1):
                    if not char["gethit"]:
                        continue
                    owner = 3 - target
                    raw, counter, random, divisor, state = hitdefs[(tick, owner)]
                    actor = row["chars"][owner-1]
                    scaled = raw * f32(actor["attackMul"]) * actor["attackBase"] / 100 / char["finalDefense"]
                    damage = 0 if char["guarded"] else math.floor(max(1, scaled) + 0.5)
                    assert char["damage"] == damage, (name, tick, owner, char["damage"], damage)
                    contacts.append((tick, owner, state, counter, random, divisor, raw, damage, char["life"], char["moveType"]))
            elif kind == "end":
                end[tick] = row["chars"]
    assert list(end) == list(range(76)), name
    for tick, owner, _, _, _, _, _, damage, life, _ in contacts:
        assert end[tick][2-owner]["life"] == life, (name, tick, owner)
        assert end[tick+1][2-owner]["life"] == life - damage, (name, tick, owner)
    if "-carry-" in name:
        assert len(contacts) == 1, (name, contacts)
        suffix = name.rsplit("-", 1)[-1]
        contact = contacts[0]
        if alternate and suffix in MODE2_STARTUP_INTERRUPT_TICKS:
            assert contact[:4] == (MODE2_STARTUP_INTERRUPT_TICKS[suffix], 2, 200, 1), (name, contact)
            assert contact[-1] == ATTACK_MOVE_TYPE, (name, contact)
        else:
            age = {"live": 2, "edge": 3, "last": 0, "expired": 0}[suffix]
            assert contact[1:4] == (1, 210, age) and contact[-1] != ATTACK_MOVE_TYPE, (name, contact)
    if name.endswith("-heavy-guard"):
        assert [(item[0], item[1], item[2], item[5], item[7]) for item in contacts] == [
            (8, 1, 215, 1, 0), (22, 1, 215, 2, 0)
        ], (name, contacts)
    if name.endswith("-heavy-far-guard"):
        assert [(item[0], item[1], item[2], item[5], item[7]) for item in contacts] == [
            (10, 1, 210, 0, 0)
        ], (name, contacts)
    print(f"{name:35} seed={metadata['rng_seed']:4} draws={len(draws):3} "
          f"contact={[(tick, owner, state, age, bucket, divisor, raw, damage) for tick, owner, state, age, bucket, divisor, raw, damage, _, _ in contacts]}")
    return state_rows, draws, age_changes, contacts


def main():
    parser = argparse.ArgumentParser(description="Verify the original IKEMEN GO Stage 17 damage and RNG audit")
    parser.add_argument("directories", type=Path, nargs="+")
    parser.add_argument("--replay", type=Path)
    options = parser.parse_args()
    results = {}
    for directory in options.directories:
        metadata = json.loads((directory / "run-metadata.json").read_bytes())
        assert metadata["engine"] == "IKEMEN GO" and metadata["commit"] == "5dac5caf9a02a9b81bf2264e88a1bbf68e21bba0"
        assert metadata["capture_profile"] == "Stage17"
        seed = metadata["rng_seed"]
        for name in metadata["executed_cases"]:
            key = (seed, name)
            assert key not in results, (directory, "duplicate seed/case", key)
            results[key] = verify_trace(directory, name, metadata)
    if options.replay:
        metadata = json.loads((options.replay / "run-metadata.json").read_bytes())
        for name in metadata["executed_cases"]:
            actual = verify_trace(options.replay, name, metadata)
            expected = results[(metadata["rng_seed"], name)]
            # Native startup intro counters may vary. The fixture reseeds at RoundState 2.
            assert actual[1:] == expected[1:], (name, "replay RNG/controller/settlement mismatch")
        print(f"Repeated seed {metadata['rng_seed']}: {len(metadata['executed_cases'])} identical event streams")
    default = "default-damage-interrupt"
    if (1, default) in results and (1337, default) in results:
        first, second = (results[(seed, default)][-1][0] for seed in (1, 1337))
        assert first[:4] == second[:4] == (8, 2, 200, 1)
        assert (first[4], first[6], first[7]) == (0, 76, 61)
        assert (second[4], second[6], second[7]) == (1, 67, 54)
        print("Same contact, different seed: 61 and 54 verified")
        if (2026, default) in results:
            third = results[(2026, default)][-1][0]
            assert third[:4] == (8, 2, 200, 1)
            assert (third[4], third[6], third[7]) == (2, 57, 46)
            print("The third RNG bucket produces 46 in the same contact")


if __name__ == "__main__":
    if not __debug__:
        raise SystemExit("Run without Python optimization")
    main()
