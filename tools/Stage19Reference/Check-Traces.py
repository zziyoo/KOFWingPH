"""Check the native Stage 19 Power Wave research captures."""

import argparse
import hashlib
import json
from pathlib import Path


ENGINE_COMMIT = "5dac5caf9a02a9b81bf2264e88a1bbf68e21bba0"
SPECIAL_SOURCE = "60171bf640c7e8316d34e2b0ab001f663472bfd14dc66d174dcf5a8eebd8dc21"
WAVE_STATES = (1000, 1050)
ROOT_PHASES = ("before-command", "after-command", "after-collision", "end")


def read_rows(path):
    return [json.loads(line) for line in path.read_bytes().splitlines()]


def read_cases(directory):
    run = json.loads((directory / "run-metadata.json").read_bytes())
    assert (run["engine"], run["commit"], run["capture_profile"]) == ("IKEMEN GO", ENGINE_COMMIT, "Stage19")
    assert run["source_sha256"]["Terry-S.cns"] == SPECIAL_SOURCE
    for name in run["executed_cases"]:
        case = json.loads((directory / f"{name}-metadata.json").read_bytes())
        trace = directory / f"{name}.jsonl"
        assert case["case"] == name and case["initial_distance"] == run["scenario_distance"][name], name
        assert case["trace_sha256"] == hashlib.sha256(trace.read_bytes()).hexdigest(), name
        assert all(case[key] == run[key] for key in ("config_sha256", "executable_sha256", "rng_seed")), name
        assert f"COMBAT_RESEARCH_DONE {name}" in (directory / f"{name}-stderr.txt").read_text(
            encoding="utf-8", errors="replace"
        ), name
        data = read_rows(trace)
        assert all(row["case"] == name for row in data), name
        phases = {phase: [row for row in data if row["phase"] == phase] for phase in ROOT_PHASES}
        assert all([row["frame"] for row in phase] == list(range(76)) for phase in phases.values()), name
        assert all(row["roundState"] == 2 for row in phases["end"]), name
        initial = phases["before-command"][0]["chars"]
        mirrored = "-mirror-" in name
        assert [fighter["facing"] for fighter in initial] == ([-1, 1] if mirrored else [1, -1]), name
        assert abs(initial[1]["pos"][0] - initial[0]["pos"][0]) == case["initial_distance"], name
        mode2 = name.startswith("mode2-")
        assert initial[0]["vars"]["var10"] == (2 if mode2 else 0), name
        if mode2:
            intro = read_rows(directory / f"{name}-mode.jsonl")
            assert [(row["var10"], row["grantBuffer"]) for row in intro if row["introFrame"] == 11] == [
                (1, 1), (2, 1)
            ], name
        changes = [row for row in data if row["phase"] == "state-change"]
        waves = [row for row in changes if row["to"] in WAVE_STATES]
        contacts = [(row["frame"], char["pn"], char["damage"], char["guarded"])
                    for row in phases["after-collision"] for char in row["chars"] if char["gethit"]]
        for frame, seat, damage, _ in contacts:
            collision_life = phases["after-collision"][frame]["chars"][seat - 1]["life"]
            next_lives = [row["chars"][seat - 1]["life"] for row in phases["end"][frame + 1:frame + 4]]
            assert collision_life - damage in next_lives, (name, frame, damage, next_lives)
        print(f"{name:38} wave={[(row['frame'], row['seat'], row['from'], row['to'], row['stateTime']) for row in waves]} contacts={contacts}")
        yield name, (phases, changes, waves, contacts)


def wave(cases, name, start, source, elapsed, *, mode2=False):
    full = f"{'mode2' if mode2 else 'default'}-special-{name}"
    phases, _, waves, _ = cases[full]
    assert len(waves) == 1, (full, waves)
    row = waves[0]
    state = 1050 if mode2 else 1000
    assert (row["frame"], row["seat"], row["from"], row["to"]) == (start, 1, source, state), (full, row)
    assert row["stateTime"] == elapsed or elapsed is None, (full, row)
    assert row["minus"] == -1 and row["hitPause"] == 0, (full, row)
    assert row["powerWaveCommand"] or row["helperWave"] > 0, (full, row)
    committed = phases["after-collision"][start]["chars"][0]
    assert (committed["state"], committed["anim"], committed["time"], committed["elem"]) == (state, state, 1, 0), full
    return row


def verify_primary(cases):
    for edge, start in ((8, 9), (9, 9), (10, 10), (11, 11)):
        row = wave(cases, f"whiff-{edge}", start, 200, start - 5)
        assert row["var1"] == 1 and row["moveContact"] == 0, row
        assert row["powerWaveCommand"] == (edge == start), row
        assert row["helperWave"] == (11 if edge != start else 0), row
    terminal = wave(cases, "whiff-17", 17, 200, 12)
    assert terminal["var1"] == 0 and terminal["var2"] == 1, terminal

    for edge in (17, 18, 19, 20, 21, 22):
        start = max(20, edge)
        row = wave(cases, f"hit-{edge}", start, 200, start - 16)
        assert row["var1"] == 1 and row["moveHit"] > 0 and row["powerWaveCommand"], row
        assert row["helperWave"] == ({17: 3, 18: 7, 19: 11}.get(edge, 0)), row
    released = wave(cases, "hit-11", 20, 200, 4)
    held = wave(cases, "hit-held-11", 20, 200, 4)
    assert released["powerWaveCommand"] and released["helperWave"] < 0, released
    assert held["powerWaveCommand"] and held["helperWave"] == 3, held
    for name in ("hit-11", "hit-held-11"):
        phases = cases[f"default-special-{name}"][0]
        assert all(phases["after-command"][frame]["chars"][0]["powerWaveCommand"] for frame in range(11, 21)), name
    for edge in (20, 22):
        row = wave(cases, f"guard-{edge}", edge, 200, edge - 16)
        assert row["var1"] == 1 and row["moveGuarded"] > 0, row

    for name, frame in (("whiff-12", 12), ("hit-23", 23), ("guard-23", 23)):
        _, changes, waves, _ = cases[f"default-special-{name}"]
        assert not waves, name
        fallback = next(row for row in changes if row["frame"] == frame)
        assert (fallback["from"], fallback["to"], fallback["stateTime"], fallback["var1"]) == (200, 200, 7, 0), fallback
        assert fallback["powerWaveCommand"] and fallback["xBuffer"], fallback
    assert not cases["default-special-hit-no-motion-20"][2], "ordinary x cannot substitute for a recognized motion"
    wave(cases, "mirror-whiff-9", 9, 200, 4)
    wave(cases, "mirror-hit-20", 20, 200, 4)
    wave(cases, "hit-20", 20, 200, 4, mode2=True)
    wave(cases, "whiff-9", 9, 200, 4, mode2=True)
    direct = wave(cases, "neutral-9", 9, 20, 1)
    assert direct["ctrl"] and direct["var1"] == direct["var2"] == 0, direct
    near = wave(cases, "near-whiff-17", 17, 205, 4)
    assert near["var1"] == 1 and near["moveContact"] == 0, near

    for name, frame, life_damage, guard in (("hit-20", 36, 76, False), ("guard-20", 36, 9, True),
                                            ("mirror-hit-20", 36, 76, False), ("whiff-9", 25, 76, False)):
        phases, _, waves, contacts = cases[f"default-special-{name}"]
        start = waves[0]["frame"]
        owner = phases["after-collision"][start + 15]["chars"][0]
        assert len(owner["helpers"]) == 1 and owner["projectiles"] == 0, name
        helper = owner["helpers"][0]
        assert (helper["helperId"], helper["state"], helper["anim"], helper["time"], helper["elem"]) == (1000, 1020, 1020, 1, 0), helper
        assert helper["frame"]["sprite"] == [1010, 0] and helper["vel"][0] == 2.5, helper
        assert helper["facing"] == (-1 if name.startswith("mirror-") else 1), helper
        attacker, target = phases["after-collision"][frame]["chars"]
        hit = attacker["helpers"][0]["hitDef"]
        assert (frame, 2, life_damage, guard) in contacts and attacker["hitPause"] == 0, (name, contacts)
        assert hit["damage"] == 76 and hit["guardDamage"] == 9 and hit["fall"], (name, hit)
        assert hit["hitPause"] == [0, 13] and hit["guardPause"] == [0, 11], (name, hit)
        assert target["hitShake"] == (11 if guard else 13), name


def verify_supplement(cases):
    def case(name):
        return cases[f"default-special-{name}"]

    outside = "outside-whiff-9"
    wave(cases, outside, 9, 200, 4)
    phases, changes, _, contacts = case(outside)
    assert not contacts and any(row["frame"] == 61 and row["from"] == 1000 and row["to"] == 0
                                and row["stateTime"] == 52 for row in changes), (outside, changes)
    end = [row["chars"][0] for row in phases["end"]]
    assert not end[23]["helpers"], outside
    assert [end[frame]["helpers"][0]["vel"][0] for frame in (24, 34, 35)] == [2.5, 2.5, 0], outside
    assert [end[frame]["helpers"][0]["destroyed"] for frame in (48, 49)] == [False, True], outside
    assert all(row["globalPause"] == 0 and row["turbo"] == 1 for row in phases["end"]), outside

    late = "far-whiff-9"
    wave(cases, late, 9, 200, 4)
    phases, changes, _, contacts = case(late)
    assert contacts == [(33, 2, 92, False)], contacts
    owner, target = phases["after-collision"][33]["chars"]
    helper = owner["helpers"][0]
    assert helper["elem"] == 5 and helper["hitDef"]["groundType"] == 2 and not target["hitFall"], helper
    assert helper["hitDef"]["groundVel"] == [-10.5, 0, 0], helper
    assert any(row["frame"] == 68 and row["from"] == 1000 and row["to"] == 0 and row["stateTime"] == 52
               for row in changes), changes
    for frame in (48, 54, 60):
        assert phases["end"][frame]["globalPause"] == 1 and phases["end"][frame]["turbo"] == 1, (late, frame)
        assert phases["end"][frame]["chars"][0]["time"] == phases["end"][frame - 1]["chars"][0]["time"], (late, frame)

    terminal = wave(cases, "terminal-heavy16-17", 17, 200, 12)
    assert terminal["var1"] == 0 and terminal["var2"] == 1 and terminal["helperY"] == 2, terminal
    assert terminal["powerWaveCommand"] and terminal["raw"] == 136, terminal

    for scenario in ("dual-9", "mirror-dual-9"):
        phases, changes, waves, contacts = case(scenario)
        assert [(row["frame"], row["seat"], row["from"], row["to"], row["stateTime"]) for row in waves] == [
            (9, 1, 200, 1000, 4), (9, 2, 200, 1000, 4)
        ], (scenario, waves)
        assert contacts == [(25, 1, 107, False), (25, 2, 99, False)], (scenario, contacts)
        assert [(row["frame"], row["seat"], row["to"]) for row in changes if row["from"] == 1000] == [
            (25, 1, 5000), (25, 2, 5000)
        ], (scenario, changes)
        for frame in (24, 25, 53, 54):
            chars = phases["end"][frame]["chars"]
            assert all(char["projectiles"] == 0 and len(char["helpers"]) == 1 for char in chars), (scenario, frame)
            helpers = [char["helpers"][0] for char in chars]
            assert len({helper["id"] for helper in helpers}) == 2, (scenario, frame)
            assert [helper["facing"] for helper in helpers] == ([-1, 1] if "mirror" in scenario else [1, -1]), (scenario, frame)
            assert all(helper["destroyed"] == (frame == 54) for helper in helpers), (scenario, frame)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directories", type=Path, nargs="+")
    options = parser.parse_args()
    cases = {}
    for directory in options.directories:
        for name, result in read_cases(directory):
            assert name not in cases, f"duplicate native case: {name}"
            cases[name] = result
    verify_primary(cases)
    if "default-special-dual-9" in cases:
        verify_supplement(cases)
    print(f"Verified {len(cases)} native Stage 19 traces")


if __name__ == "__main__":
    if not __debug__:
        raise SystemExit("Run without Python optimization")
    main()
