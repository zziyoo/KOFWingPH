"""Verify original IKEMEN GO Stage 21 heavy-to-Power-Wave traces."""

import argparse
import importlib.util
import json
import math
from pathlib import Path


def load_checker(stage):
    path = Path(__file__).resolve().parents[1] / f"Stage{stage}Reference" / "Check-Traces.py"
    spec = importlib.util.spec_from_file_location(f"stage{stage}_reference", path)
    checker = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(checker)
    return checker


stage17 = load_checker(17)
stage20 = load_checker(20)
PREFIX = "default-heavy-wave-"
MODE2 = "mode2-heavy-wave-"
EXTRA_PHASES = ("hitdef", "rng-draw", "var21-draw", "damage-var")
DEFAULT_CASES = {
    "hit-held-11", "hit-20", "hit-22", "hit-25", "hit-34", "hit-39", "hit-40", "hit-none-0",
    "guard-20", "guard-25", "guard-34", "guard-none-0", "crouch-guard-20", "mirror-hit-34", "mirror-guard-20",
    "second-only-11", "second-only-14", "second-only-23", "second-only-28", "second-only-29", "second-only-none-0",
    "second-guard-14", "second-guard-23", "second-guard-none-0", "mirror-second-only-23",
    "whiff-16", "whiff-17", "whiff-25", "whiff-26", "whiff-light-26", "whiff-41", "mirror-whiff-17",
}
REQUIRED = {PREFIX + name for name in DEFAULT_CASES} | {MODE2 + "hit-20", MODE2 + "hit-none-0"}
MIXED = {PREFIX + "mixed-sequential", PREFIX + "mirror-mixed-sequential"}
GUARD_RELEASE = {PREFIX + "guard-release-20", PREFIX + "guard-release-34"}


def read_cases(directory):
    run = json.loads((directory / "run-metadata.json").read_bytes())
    common_hash = run["source_sha256"].get("common_kof.cns")
    if common_hash is not None:
        assert common_hash == "3b934b5390e0b805df549db35601b4882d6c28b9165b56e3dcff72d3bd86c65b", directory
    yield from stage20.read_cases(directory, capture_profile="Stage21", extra_phases=EXTRA_PHASES)


def verify_damage(phases, contacts, name):
    draw_stream = phases["rng-draw"]
    assert draw_stream and draw_stream[0]["before"] == 1337, name
    assert all(row["after"] == row["before"] * 16_807 % stage17.MODULUS for row in draw_stream), name
    assert all(a["after"] == b["before"] for a, b in zip(draw_stream, draw_stream[1:])), name
    assert all((row["min"], row["max"], row["value"]) == (0, 2, row["rng"] // (stage17.MODULUS // 3 + 1))
               for row in phases["var21-draw"]), name
    defs = {}
    for row in phases["hitdef"]:
        owner = row["hitDefOwner"]
        fighter = row["chars"][owner - 1]
        if fighter["state"] != 215:
            continue
        assert fighter["elem"] in (1, 2), (name, row["frame"], fighter)
        variance, divisor, multiplier = (fighter["vars"][key] for key in ("var21", "var23", "fvar7"))
        raw = math.floor(stage17.f32(((9 - variance) // divisor) * stage17.f32(multiplier)))
        assert fighter["hitDef"]["damage"] == raw, (name, row["frame"], fighter)
        defs[row["frame"], owner] = fighter
    for frame, target, damage, guarded in contacts:
        owner = 3 - target
        hitdef = defs.get((frame, owner))
        if hitdef is None:
            continue
        victim = phases["after-collision"][frame]["chars"][target - 1]
        scaled = hitdef["hitDef"]["damage"] * stage17.f32(hitdef["attackMul"]) * hitdef["attackBase"] / 100 / victim["finalDefense"]
        assert damage == (0 if guarded else math.floor(max(1, scaled) + 0.5)), (name, frame, victim)
        assert phases["end"][frame]["chars"][target - 1]["life"] == victim["life"], (name, frame)
        assert phases["end"][frame + 1]["chars"][target - 1]["life"] == victim["life"] - damage, (name, frame)


def wave_transitions(transitions):
    return [row for row in transitions if row["to"] in (1000, 1050)]


def verify_wave(phases, row, name):
    frame, seat, destination = row["frame"], row["seat"], row["to"]
    assert (row["minus"], row["hitPause"]) == (-1, 0), (name, row)
    assert row["powerWaveCommand"] or row["helperWave"] > 0, (name, row)
    if row["from"] == 215:
        assert (row["var1"], row["var2"]) == (int(4 <= row["stateTime"] <= 12), int(row["stateTime"] == 28)), (name, row)
    committed = phases["after-collision"][frame]["chars"][seat - 1]
    assert (committed["state"], committed["anim"], committed["time"], committed["elem"]) == (destination, destination, 1, 0), (name, row)
    assert all(committed[key] == 0 for key in ("moveContact", "moveHit", "moveGuarded")), (name, row)
    if destination == 1000 and frame + 15 < len(phases["end"]):
        def living(at):
            return [entity for entity in phases["end"][at]["chars"][seat - 1]["helpers"] or [] if not entity["destroyed"]]

        assert not living(frame + 14), (name, row)
        entity, = living(frame + 15)
        assert (entity["state"], entity["anim"], entity["time"]) == (1020, 1020, 1), (name, row, entity)


def actor_at(phases, phase, frame, seat=1):
    return phases[phase][frame]["chars"][seat - 1]


def verify_case_inputs(name, phases):
    scenario = name.split("heavy-wave-", 1)[1].removeprefix("mirror-")
    attack = 13 if scenario.startswith("whiff-") else 5
    assert actor_at(phases, "before-command", attack)["raw"] == 256, name
    if scenario == "mixed-sequential":
        completion = 20
    else:
        completion = int(scenario.rsplit("-", 1)[1])
    if completion == 0:
        assert all(actor_at(phases, "before-command", frame)["raw"] == 0 for frame in range(attack + 1, len(phases["end"]))), name
    elif "-light-" in scenario:
        assert actor_at(phases, "before-command", completion)["raw"] == 128, name
    else:
        forward = 4 if "-mirror-" in name else 8
        frames = (completion - 3, completion - 2, completion - 1, completion)
        if frames[0] == attack:
            frames = (attack - 1, *frames[1:])
        assert [actor_at(phases, "before-command", frame)["raw"] for frame in frames] == [2, 2 | forward, forward, 128 | forward], name


def verify_cases(cases):
    allowed = REQUIRED | MIXED | GUARD_RELEASE
    assert REQUIRED <= cases.keys() <= allowed, (REQUIRED - cases.keys(), cases.keys() - allowed)
    for name, (phases, transitions, contacts) in cases.items():
        verify_case_inputs(name, phases)
        verify_damage(phases, contacts, name)
        for row in wave_transitions(transitions):
            verify_wave(phases, row, name)

    def verify(name, expected_waves, expected_contacts):
        full_name = name if name.startswith(MODE2) else PREFIX + name
        phases, transitions, contacts = cases[full_name]
        actual = [(row["frame"], row["from"], row["to"], row["stateTime"]) for row in wave_transitions(transitions)]
        assert actual == expected_waves and contacts == expected_contacts, (full_name, actual, contacts)
        starts = [row["frame"] for row in transitions if row["to"] == 215]
        assert starts == [13 if name.removeprefix("mirror-").startswith("whiff-") else 5], (full_name, starts)
        return phases, transitions

    first_contact = (8, 2, 61, False)
    two_contacts = [first_contact, (22, 2, 29, False)]
    for name in ("hit-20", "hit-held-11"):
        phases, transitions = verify(name, [(20, 215, 1000, 4)], [first_contact, (36, 2, 89, False)])
        assert [actor_at(phases, "after-command", tick)["hitPause"] for tick in range(9, 20)] == list(range(11, 0, -1)), name
        row, = wave_transitions(transitions)
        assert row["moveHit"] == 1 and row["helperWave"] == (3 if "held" in name else 0), name

    phases, transitions = verify("hit-22", [(22, 215, 1000, 6)], [first_contact, (38, 2, 74, False)])
    row, = wave_transitions(transitions)
    assert row["moveContact"] == 3 and not any(r["frame"] >= 22 for r in phases["hitdef"]), row
    for name in ("hit-25", "hit-34", "mirror-hit-34"):
        phases, transitions = verify(name, [(34, 215, 1000, 7)], [*two_contacts, (55, 2, 76, False)])
        assert [actor_at(phases, "after-command", tick)["hitPause"] for tick in range(23, 34)] == list(range(11, 0, -1)), name
        row, = wave_transitions(transitions)
        assert row["moveContact"] == 1 and row["helperWave"] == (-1 if name == "hit-25" else 0), name
    hit_phases, _, _ = cases[PREFIX + "hit-34"]
    tail = [(frame, fighter["hitTime"]) for frame in range(23, 55)
            if (fighter := actor_at(hit_phases, "after-collision", frame, 2))["anim"] == 5007 and fighter["elem"] == 2]
    assert tail == list(zip(range(48, 54), (4, 3, 2, 1, 0, -1))), tail
    verify("hit-39", [(39, 215, 1000, 12)], [*two_contacts, (59, 2, 92, False)])
    for name in ("hit-none-0", "hit-40"):
        phases, transitions = verify(name, [], two_contacts)
        assert [(r["frame"], r["stateTime"]) for r in transitions if r["from"] == 215 and r["to"] == 0] == [(55, 28)], name
        if name == "hit-40":
            actor = actor_at(phases, "after-command", 40)
            assert (actor["time"], actor["powerWaveCommand"], actor["vars"]["var1"], actor["vars"]["var2"]) == (13, True, 1, 0), name
            assert actor_at(phases, "end", 40)["vars"]["var1"] == 0, name

    first_guard = (8, 2, 0, True)
    two_guards = [first_guard, (22, 2, 0, True)]
    for name in ("guard-20", "crouch-guard-20", "mirror-guard-20"):
        phases, transitions = verify(name, [(20, 215, 1000, 4)], [first_guard, (36, 2, 9, True)])
        assert wave_transitions(transitions)[0]["moveGuarded"] == 1, name
        assert actor_at(phases, "after-collision", 8, 2)["hitShake"] == 11, name
        assert all(row["globalPause"] == 0 for row in phases["end"]), name
    for name in ("guard-25", "guard-34"):
        phases, transitions = verify(name, [(34, 215, 1000, 7)], [*two_guards, (54, 2, 9, True)])
        assert wave_transitions(transitions)[0]["moveGuarded"] == 1, name
        assert actor_at(phases, "after-collision", 22, 2)["hitShake"] == 11, name
    verify("guard-none-0", [], two_guards)

    phases, transitions = verify("second-only-11", [(11, 215, 1000, 6)], [(27, 2, 76, False)])
    assert wave_transitions(transitions)[0]["moveContact"] == 0, transitions
    assert not any(row["frame"] >= 11 for row in phases["hitdef"]), "second-only-11"
    second_hit = (11, 2, 69, False)
    for name in ("second-only-14", "second-only-23", "mirror-second-only-23"):
        phases, transitions = verify(name, [(23, 215, 1000, 7)], [second_hit, (44, 2, 76, False)])
        assert [actor_at(phases, "after-command", tick)["hitPause"] for tick in range(12, 23)] == list(range(11, 0, -1)), name
        assert wave_transitions(transitions)[0]["moveHit"] == 1, name
    verify("second-only-28", [(28, 215, 1000, 12)], [second_hit, (49, 2, 76, False)])
    for name in ("second-only-29", "second-only-none-0"):
        verify(name, [], [second_hit])

    second_guard = (11, 2, 0, True)
    for name in ("second-guard-14", "second-guard-23"):
        phases, transitions = verify(name, [(23, 215, 1000, 7)], [second_guard, (47, 2, 9, True)])
        assert wave_transitions(transitions)[0]["moveGuarded"] == 1, name
        assert actor_at(phases, "after-collision", 11, 2)["hitShake"] == 11, name
    verify("second-guard-none-0", [], [second_guard])

    for name in ("whiff-16", "whiff-17", "mirror-whiff-17"):
        phases, transitions = verify(name, [(17, 215, 1000, 4)], [(33, 2, 84, False)])
        row, = wave_transitions(transitions)
        assert row["moveContact"] == 0 and (row["powerWaveCommand"], row["helperWave"]) == (
            (False, 11) if name == "whiff-16" else (True, 0)
        ), (name, row)
        assert actor_at(phases, "before-command", 0, 2)["raw"] == 1, name
    phases, _ = verify("whiff-25", [(25, 215, 1000, 12)], [(41, 2, 84, False)])
    assert not actor_at(phases, "after-collision", 25, 2)["gethit"], "whiff-25"
    for name in ("whiff-26", "whiff-light-26"):
        phases, changes = verify(name, [], [])
        assert [(row["frame"], row["to"]) for row in changes] == [(13, 215), (41, 0)], name
        actor = actor_at(phases, "end", 26)
        assert actor["vars"]["var1"] == 0 and actor["powerWaveCommand"] == (name == "whiff-26"), name
        if name == "whiff-26":
            assert actor["helperWave"] == 11 and actor_at(phases, "end", 41)["helperWave"] <= 0, name
    _, transitions = verify("whiff-41", [(41, 215, 1000, 28)], [(57, 2, 92, False)])
    assert wave_transitions(transitions)[0]["var2"] == 1, "whiff-41"
    verify(MODE2 + "hit-20", [(20, 215, 1050, 4)], [first_contact, (34, 2, 81, False)])
    verify(MODE2 + "hit-none-0", [], two_contacts)

    for name in sorted(cases.keys() & GUARD_RELEASE):
        first_only = name.endswith("-20")
        start, old_tick, contact = (20, 4, 36) if first_only else (34, 7, 54)
        prior = [first_guard] if first_only else two_guards
        phases, _ = verify(name.removeprefix(PREFIX), [(start, 215, 1000, old_tick)], [*prior, (contact, 2, 9, True)])
        target = actor_at(phases, "after-collision", contact, 2)
        assert (target["raw"], target["state"], target["guardFlag"]) == (0, 151 if first_only else 140, True), name

    for name in sorted(cases.keys() & MIXED):
        phases, changes = verify(name.removeprefix(PREFIX), [(20, 215, 1000, 4), (98, 200, 1000, 4)],
                                 [first_guard, (36, 2, 9, True), (120, 1, 84, False)])
        assert [(row["frame"], row["seat"], row["to"]) for row in changes if row["to"] in (200, 215)] == [
            (5, 1, 215), (94, 2, 200),
        ], name
        first, = actor_at(phases, "end", 35)["helpers"]
        later, = actor_at(phases, "end", 113, 2)["helpers"]
        assert first["id"] != later["id"] and first["facing"] == -later["facing"], name

    return {"lateHitstunFrames": len(tail), "guardReleaseVerified": GUARD_RELEASE <= cases.keys()}


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
    print(f"Verified {len(cases)} native Stage 21 traces")


if __name__ == "__main__":
    if not __debug__:
        raise SystemExit("Run without Python optimization")
    main()
