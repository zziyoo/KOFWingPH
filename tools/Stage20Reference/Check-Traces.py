"""Verify the native IKEMEN GO Stage 20 near-light Power Wave traces."""

import argparse
import hashlib
import json
from pathlib import Path


ENGINE_COMMIT = "5dac5caf9a02a9b81bf2264e88a1bbf68e21bba0"
ARCHIVE_SHA = "299203338248952735fd33b4951aba84b39580123290abd8ac70387580ff4698"
SOURCE_SHA = {
    "Terry.cmd": "ee57ffe1d6d4a2770b47590ab69177de79b24ccfc0153702dd3602f633507b59",
    "Terry-N.cns": "dcbee6b2378faf00c7b7912a393e3c640ff3b9179b113b14b7c2ce9a5ecbab75",
    "Terry-2-3.cns": "90b83cdacd33a3800a825ee42bdbd6502b7989f4dec358126587edeb4495fdc4",
    "Terry-S.cns": "60171bf640c7e8316d34e2b0ab001f663472bfd14dc66d174dcf5a8eebd8dc21",
    "Helper.cns": "b2d683f70a3c6dd76587edee1a54f0638f9c6e006d5c70af598ff9b0163dd62a",
    "Terry.air": "252646cc2940f776e94df0b7b7fd3772e098d75fda6e9b781135eaa36e0fd851",
}
PHASES = ("before-command", "after-command", "after-collision", "end")
DEFAULT_CASES = {
    *(f"whiff-{frame}" for frame in (16, 17, 18, 19, 20, 25)),
    *(f"hit-{frame}" for frame in (11, 17, 18, 19, 20, 21, 22, 23)),
    "hit-held-11", "hit-no-motion-20", "guard-20", "guard-22", "guard-23", "crouch-guard-20",
    "mirror-hit-20", "mirror-guard-20", "mirror-whiff-17", "hit-far-23", "guard-far-23",
    "boundary-46-20", "boundary-46p5-20", "dual-20", "mirror-dual-20",
}
REQUIRED = {f"default-near-wave-{name}" for name in DEFAULT_CASES} | {
    "mode2-near-wave-hit-20", "mode2-near-wave-reentry-23",
}
MIXED = {"default-near-wave-mixed-sequential", "default-near-wave-mirror-mixed-sequential"}
TERMINAL = {"default-near-wave-whiff-terminal-heavy-light-25", "default-near-wave-whiff-terminal-heavy-wave-25"}


def read_cases(directory):
    run = json.loads((directory / "run-metadata.json").read_bytes())
    assert (run["engine"], run["commit"], run["capture_profile"]) == ("IKEMEN GO", ENGINE_COMMIT, "Stage20")
    assert run["terry_archive_sha256"] == ARCHIVE_SHA
    scripts = run["source_sha256"]
    assert all(scripts.get(name) == digest for name, digest in SOURCE_SHA.items()), directory
    if "Constants.cns" in scripts:
        assert scripts["Constants.cns"] == "f2dec82cca8857f78a702ee03149642c632a2efa2a70f01ac2129ca82fa98c55", directory
    last_frame = run.get("last_frame", 75)
    assert last_frame >= 75 and run["rng_seed"] == 1337
    for name in run["executed_cases"]:
        trace = directory / f"{name}.jsonl"
        case = json.loads((directory / f"{name}-metadata.json").read_bytes())
        assert case["case"] == name and case["initial_distance"] == run["scenario_distance"][name], name
        assert all(case[key] == run[key] for key in ("executable_sha256", "config_sha256", "rng_seed")), name
        raw = trace.read_bytes()
        assert hashlib.sha256(raw).hexdigest() == case["trace_sha256"], name
        stderr = (directory / f"{name}-stderr.txt").read_bytes()
        assert f"COMBAT_RESEARCH_DONE {name}".encode() in stderr, name
        rows = [json.loads(line) for line in raw.splitlines()]
        assert all(row["case"] == name for row in rows), name
        phases = {phase: [row for row in rows if row["phase"] == phase] for phase in PHASES}
        assert all([row["frame"] for row in values] == list(range(last_frame + 1)) for values in phases.values()), name
        assert all(row["roundState"] == 2 and isinstance(row["rng"], int) for row in phases["end"]), name
        initial = phases["before-command"][0]["chars"]
        mirrored = "-mirror-" in name
        mode2 = name.startswith("mode2-")
        assert [fighter["facing"] for fighter in initial] == ([-1, 1] if mirrored else [1, -1]), name
        assert abs(initial[1]["pos"][0] - initial[0]["pos"][0]) == case["initial_distance"], name
        assert [fighter["vars"]["var10"] for fighter in initial] == ([2, 0] if mode2 else [0, 0]), name
        if mode2:
            intro = [json.loads(line) for line in (directory / f"{name}-mode.jsonl").read_bytes().splitlines()]
            assert [(row["var10"], row["grantBuffer"]) for row in intro if row["introFrame"] == 11] == [
                (1, 1), (2, 1)
            ], name
        transitions = [row for row in rows if row["phase"] == "state-change"]
        contacts = [(row["frame"], char["pn"], char["damage"], char["guarded"])
                    for row in phases["after-collision"] for char in row["chars"] if char["gethit"]]
        for frame, seat, damage, _ in contacts:
            life = phases["after-collision"][frame]["chars"][seat - 1]["life"]
            assert life - damage in [row["chars"][seat - 1]["life"] for row in phases["end"][frame + 1:frame + 4]], (
                name, frame, seat, damage
            )
        print(f"{name:38} states={[(r['frame'], r['seat'], r['from'], r['to'], r['stateTime']) for r in transitions]} "
              f"contacts={contacts}")
        yield name, (phases, transitions, contacts)


def verify_cases(cases):
    allowed = REQUIRED | MIXED | TERMINAL
    assert REQUIRED <= cases.keys() <= allowed, (REQUIRED - cases.keys(), cases.keys() - allowed)

    def default(name):
        return cases[f"default-near-wave-{name}"]

    def wave(name, frame, old_tick, *, source=205, dest=1000, seat=1, mode2=False):
        full = f"{'mode2' if mode2 else 'default'}-near-wave-{name}"
        phases, transitions, _ = cases[full]
        matches = [row for row in transitions if row["seat"] == seat and row["to"] in (1000, 1050)]
        assert len(matches) == 1, (full, matches)
        row = matches[0]
        assert (row["frame"], row["from"], row["to"], row["stateTime"], row["minus"], row["hitPause"]) == (
            frame, source, dest, old_tick, -1, 0
        ), (full, row)
        assert (row["var1"], row["var2"]) == (int(old_tick in (4, 5, 6)), int(old_tick == 12)), (full, row)
        assert row["powerWaveCommand"] or row["helperWave"] > 0, (full, row)
        committed = phases["after-collision"][frame]["chars"][seat - 1]
        assert (committed["state"], committed["anim"], committed["time"], committed["elem"]) == (dest, dest, 1, 0), (full, committed)
        assert not (committed["moveHit"] or committed["moveGuarded"] or committed["moveContact"]), (full, committed)
        return row

    for edge, start in ((16, 17), (17, 17), (18, 18), (19, 19)):
        name = f"whiff-{edge}"
        row = wave(name, start, start - 13)
        assert row["moveContact"] == 0 and (row["powerWaveCommand"], row["helperWave"]) == (
            (False, 11) if edge == 16 else (True, 0)
        ), (name, row)
        phases, transitions, contacts = default(name)
        assert [(item["frame"], item["to"], item["bodyDistX"]) for item in transitions if item["to"] == 205] == [
            (13, 205, 10)
        ], (name, transitions)
        assert all(contact[0] > start for contact in contacts), (name, contacts)
        assert phases["before-command"][0]["chars"][1]["raw"] == 1, name
        assert phases["after-collision"][start + 16]["chars"][1]["stateType"] == (4 if edge <= 17 else 1), name
    assert wave("mirror-whiff-17", 17, 4)["moveContact"] == 0
    assert wave("whiff-25", 25, 12)["moveContact"] == 0

    def fallback(name, frame, dest):
        _, transitions, _ = default(name)
        assert not any(row["to"] in (1000, 1050) for row in transitions), (name, transitions)
        choices = [row for row in transitions if row["frame"] == frame and row["from"] == 205]
        assert len(choices) == 1, (name, choices)
        row = choices[0]
        assert (row["to"], row["stateTime"], row["var1"], row["var2"], row["powerWaveCommand"], row["xBuffer"]) == (
            dest, 7, 0, 0, True, True
        ), (name, row)
        assert (row["bodyDistX"] <= 16) == (dest == 205), (name, row)

    fallback("whiff-20", 20, 205)
    fallback("hit-23", 23, 205)
    assert default("whiff-20")[2] == [(23, 2, 22, False)]
    assert default("hit-23")[2] == [(8, 2, 30, False), (26, 2, 22, False)]

    for edge in (11, 17, 18, 19, 20, 21, 22):
        start = max(20, edge)
        name = f"hit-{edge}"
        row = wave(name, start, start - 16)
        assert row["moveHit"] > 0 and row["powerWaveCommand"], (name, row)
        assert row["helperWave"] == {11: -1, 17: 3, 18: 7, 19: 11}.get(edge, 0), (name, row)
        phases, _, contacts = default(name)
        assert contacts == [(8, 2, 30, False), (start + 16, 2, 84 if edge == 21 else 76, False)], (name, contacts)
        assert [phases["after-command"][frame]["chars"][0]["hitPause"] for frame in range(9, 20)] == list(range(11, 0, -1)), name
    held = wave("hit-held-11", 20, 4)
    assert held["powerWaveCommand"] and held["helperWave"] == 3, held
    no_motion = default("hit-no-motion-20")
    assert all(row["to"] not in (1000, 1050) for row in no_motion[1]) and no_motion[2] == [(8, 2, 30, False)]

    for name, start, x in (("guard-20", 20, 48.735325), ("guard-22", 22, 48.735325),
                           ("mirror-guard-20", 20, -48.735325), ("crouch-guard-20", 20, 45.746014)):
        row = wave(name, start, start - 16)
        assert row["moveGuarded"] > 0, (name, row)
        phases, changes, contacts = default(name)
        assert contacts == [(8, 2, 0, True), (start + 16, 2, 9, True)], (name, contacts)
        assert all(world["globalPause"] == 0 for world in phases["end"]), name
        target = phases["after-collision"][start + 16]["chars"][1]
        assert target["hitShake"] == 11 and abs(target["pos"][0] - x) < 0.0001, (name, target)
        assert any((r["frame"], r["from"], r["to"], r["stateTime"]) == (start + 52, 1000, 0, 52) for r in changes), name
    fallback("guard-23", 23, 200)
    assert default("guard-23")[2] == [(8, 2, 0, True), (26, 2, 0, True)]
    assert wave("mirror-hit-20", 20, 4)["moveHit"] > 0
    assert default("mirror-hit-20")[2] == default("hit-20")[2]
    fallback("hit-far-23", 23, 200)
    assert default("hit-far-23")[2] == [(8, 2, 30, False), (27, 2, 37, False)]
    fallback("guard-far-23", 23, 200)
    assert default("guard-far-23")[2] == default("guard-23")[2]

    for name, source, body_dist, normal_damage in (("boundary-46-20", 205, 16, 30),
                                                    ("boundary-46p5-20", 200, 16.5, 46)):
        row = wave(name, 20, 4, source=source)
        assert row["bodyDistX"] == body_dist, (name, row)
        _, changes, contacts = default(name)
        assert [(r["frame"], r["to"], r["bodyDistX"]) for r in changes if r["to"] in (200, 205)] == [
            (5, source, body_dist)
        ], (name, changes)
        assert contacts == [(8, 2, normal_damage, False), (36, 2, 76, False)], (name, contacts)

    for name in ("dual-20", "mirror-dual-20"):
        phases, changes, contacts = default(name)
        assert [(r["frame"], r["seat"], r["to"]) for r in changes] == [
            (5, 1, 205), (5, 2, 205), (8, 1, 5000), (8, 2, 5000)
        ], (name, changes)
        assert contacts == [(8, 1, 38, False), (8, 2, 30, False)], (name, contacts)
        assert all(not c["helpers"] for row in phases["end"] for c in row["chars"]), name
        assert all(row["globalPause"] == 0 for row in phases["end"]), name

    assert wave("hit-20", 20, 4, dest=1050, mode2=True)["moveHit"] > 0
    assert cases["mode2-near-wave-hit-20"][2] == [(8, 2, 30, False), (34, 2, 69, False)]
    _, mode2_reentry, mode2_contacts = cases["mode2-near-wave-reentry-23"]
    assert [(r["frame"], r["from"], r["to"], r["stateTime"]) for r in mode2_reentry if r["frame"] == 23] == [
        (23, 205, 205, 7)
    ], mode2_reentry
    assert not any(r["to"] in (1000, 1050) for r in mode2_reentry), mode2_reentry
    assert mode2_contacts == [(8, 2, 30, False), (26, 2, 22, False)], mode2_contacts

    for name in sorted(cases.keys() & MIXED):
        simple_name = name.removeprefix("default-near-wave-")
        first = wave(simple_name, 20, 4)
        second = wave(simple_name, 94, 4, source=200, seat=2)
        phases, changes, contacts = cases[name]
        assert first["moveGuarded"] > 0 and second["moveContact"] == 0, name
        assert [(r["frame"], r["seat"], r["to"]) for r in changes if r["to"] in (200, 205)] == [
            (5, 1, 205), (90, 2, 200)
        ], (name, changes)
        assert contacts == [(8, 2, 0, True), (36, 2, 9, True), (115, 1, 76, False)], (name, contacts)
        assert phases["before-command"][90]["chars"][1]["ctrl"], name
        assert [r["frame"] for r in changes if r["from"] == 1000 and r["to"] == 0] == [72, 154], name
        def living(frame, seat):
            return [entity for entity in phases["end"][frame]["chars"][seat - 1]["helpers"] or [] if not entity["destroyed"]]

        assert not living(90, 1) and not living(90, 2), name
        first_entity, = living(35, 1)
        second_entity, = living(109, 2)
        assert first_entity["id"] != second_entity["id"], name
        direction = -1 if "-mirror-" in name else 1
        assert [first_entity["facing"], second_entity["facing"]] == [direction, -direction], name
        assert not living(60, 1) and not living(136, 2), name

    for name in sorted(cases.keys() & TERMINAL):
        phases, changes, contacts = cases[name]
        wave_input = "-wave-25" in name
        assert (phases["before-command"][24]["chars"][0]["raw"], phases["before-command"][25]["chars"][0]["raw"]) == (
            (264, 136) if wave_input else (256, 128)
        ), name
        assert [(row["frame"], row["seat"], row["to"]) for row in changes if row["frame"] < 25] == [(13, 1, 205)], name
        selected, = [row for row in changes if row["frame"] == 25 and row["seat"] == 1]
        assert (selected["from"], selected["to"], selected["stateTime"], selected["minus"]) == (
            205, 1000 if wave_input else 205, 12, -1
        ), (name, selected)
        assert (selected["var1"], selected["var2"], selected["helperY"], selected["yBuffer"], selected["xBuffer"]) == (
            0, 1, 2, False, True
        ), (name, selected)
        assert selected["powerWaveCommand"] == wave_input and selected["moveContact"] == 0, (name, selected)
        assert all(frame > 25 for frame, *_ in contacts), (name, contacts)
        if wave_input:
            wave(name.removeprefix("default-near-wave-"), 25, 12)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directories", type=Path, nargs="+")
    options = parser.parse_args()
    cases = {}
    for directory in options.directories:
        for name, result in read_cases(directory):
            assert name not in cases, f"duplicate native case {name}"
            cases[name] = result
    verify_cases(cases)
    print(f"Verified {len(cases)} native Stage 20 traces")


if __name__ == "__main__":
    if not __debug__:
        raise SystemExit("Run without Python optimization")
    main()
