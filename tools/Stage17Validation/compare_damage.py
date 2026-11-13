"""Check native and Godot State 200/205/210/215 contact clocks and realized damage operands."""

import argparse
from collections import defaultdict
import json
import math
from pathlib import Path
import struct

from damage_duel import ACTIONS, GUARD_SCENES, HEAVY, MODES, NEAR_HEAVY, SCENES, fixture


RNG_MODULUS = 2147483647
RNG_MULTIPLIER = 16807
STATE_BY_MOVE = {**ACTIONS, HEAVY: 210}
END_COUNTER = {"carry-live": 2, "carry-edge": 3, "carry-last": 0, "carry-expired": 0}
HIT_REACTIONS = {"Hitstun", "Blockstun", "AirHitstun", "AirBlockstun", "Knockdown", "Wakeup"}


def f32(value):
    return struct.unpack("<f", struct.pack("<f", value))[0]


def ratio(data, key):
    value = data[key]
    return value["numerator"] / value["denominator"]


def damage_roll(rng, maximum):
    return rng // (RNG_MODULUS // (maximum + 1) + 1)


def raw_damage(formula, variance, counter, divisor, scale):
    base = formula["counterBase" if counter else "normalBase"] - variance
    quotient = base // divisor if formula["useContactDivisor"] else base
    return math.floor(f32(f32(quotient) * f32(scale)))


def committed_damage(raw, multiplier, defense, remaining_life):
    if raw == 0:
        return 0
    scaled = raw * f32(multiplier) / defense
    return math.floor(min(max(1, scaled), remaining_life) + 0.5)


def source_contacts(path):
    phases = defaultdict(dict)
    definitions = {}
    latest_draw = {}
    contacts = []
    for row in map(json.loads, path.read_text(encoding="utf-8").splitlines()):
        frame, phase = row["frame"], row["phase"]
        if phase == "var21-draw":
            if (row["min"], row["max"], row["value"]) != (0, 2, damage_roll(row["rng"], 2)):
                raise ValueError(f"native variance outside the Park–Miller range map: {row}")
            latest_draw[row["seat"] - 1] = row
        if phase == "hitdef":
            seat = row["hitDefOwner"] - 1
            definitions[seat] = (row, latest_draw[seat])
        if phase not in ("before-command", "after-collision", "end"):
            continue
        phases[phase][frame] = row["chars"]
        if phase != "after-collision":
            continue
        for target, defender in enumerate(row["chars"]):
            if not defender["gethit"]:
                continue
            seat = 1 - target
            hitdef, draw = definitions[seat]
            attacker = hitdef["chars"][seat]
            if draw["value"] != attacker["vars"]["var21"]:
                raise ValueError(f"native contact carries an unrelated variance draw: {hitdef}")
            contacts.append({"frame": frame, "source": seat, "target": target,
                             "kind": "Blocked" if defender["guarded"] else "Hit",
                             "attacker": attacker, "defender": defender, "draw": draw,
                             "definition_frame": hitdef["frame"]})
    if max(phases["end"], default=-1) != 75:
        raise ValueError(f"native trace incomplete: {path}")
    return contacts, phases


def move_start(phases, seat, move):
    state = STATE_BY_MOVE[move]
    return min(frame for frame, pair in phases["after-collision"].items() if pair[seat]["state"] == state)


def native_action(move, mode):
    return 211 if move == HEAVY and mode == "mode2" else STATE_BY_MOVE[move]


def check_rng(rows, profiles, seed, check):
    count = 0
    initial = 1 + (max(seed, 1) - 1) % (RNG_MODULUS - 1)
    for seat, profile in enumerate(profiles):
        damage = [row for row in rows if int(row["fighter"]) == seat]
        maximum = profile["damageRules"]["varianceMaximum"]
        first_rng = 1 + (initial - 1 + seat * 104729) % (RNG_MODULUS - 1)
        check(first_rng * RNG_MULTIPLIER % RNG_MODULUS, int(damage[0]["rng"]), f"seat {seat} first seeded draw")
        check(damage_roll(int(damage[0]["rng"]), maximum), int(damage[0]["variance"]), f"seat {seat} first variance")
        for previous, current in zip(damage, damage[1:]):
            elapsed = int(current["logic"]) - int(previous["logic"])
            check(1, elapsed, f"seat {seat} damage audit clock")
            before, after = int(previous["rng"]), int(current["rng"])
            expected = before if int(previous["pause"]) > 0 else before * RNG_MULTIPLIER % RNG_MODULUS
            check(expected, after, f"seat {seat} RNG at logic {current['logic']}")
            check(damage_roll(after, maximum), int(current["variance"]), f"seat {seat} variance at logic {current['logic']}")
            count += 1
    return count


def compare(mode, name, source, trace, profiles):
    scene = fixture(mode, name)
    contacts, phases = source_contacts(source)
    events = trace["events"]
    audit = {(int(row["fighter"]), int(row["logic"])): row for row in trace["damage"]}
    moves = {(int(row["fighter"]), int(row["logic"])): row for row in trace["moves"]}
    mirrored = name.startswith("mirror-")
    starts = {event["source"]: event for event in events if event["kind"] == "MoveStarted"}
    host_origin = min(event["frame"] for event in starts.values())
    native_origin = min(move_start(phases, spec.seat ^ mirrored, spec.move_id) for spec in scene.moves)
    host_contacts = [event for event in events if event["kind"] in ("Hit", "Blocked")]
    errors = []

    def check(expected, actual, description):
        if expected != actual:
            errors.append(f"{description}: expected {expected!r}, got {actual!r}")

    def native_key(event):
        return event["frame"] - native_origin, event["source"] ^ mirrored, event["kind"]

    def host_key(event):
        return event["frame"] - host_origin, event["source"], event["kind"]

    check(sorted(map(native_key, contacts)), sorted(map(host_key, host_contacts)), "normalized contact order and seats")
    for spec, window in zip(scene.moves, trace["windows"], strict=True):
        native_seat = spec.seat ^ mirrored
        start = move_start(phases, native_seat, spec.move_id)
        state = STATE_BY_MOVE[spec.move_id]
        finish = min(frame for frame, pair in phases["end"].items() if frame > start and pair[native_seat]["state"] != state)
        action = native_action(spec.move_id, trace["modes"][spec.seat])
        check(action, window["action"], f"seat {spec.seat} actual Action")
        check(action, phases["after-collision"][start][native_seat]["anim"], f"seat {spec.seat} native Action")
        check(finish - start, window["end"] - window["begin"], f"seat {spec.seat} normalized move lifetime")
    rng_transitions = check_rng(trace["damage"], profiles, trace["seed"], check)
    host_by_contact = {host_key(event): event for event in host_contacts}
    samples = []
    for contact in contacts:
        if native_key(contact) not in host_by_contact:
            continue
        event = host_by_contact[native_key(contact)]
        source_seat, target_seat, frame = event["source"], event["target"], event["frame"]
        native_attacker, native_defender = contact["attacker"], contact["defender"]
        native_frame, native_target = contact["frame"], contact["target"]
        target_before = moves[target_seat, frame - 1]
        runtime_attacker, runtime_defender = moves[source_seat, frame], moves[target_seat, frame]
        runtime_damage = audit[source_seat, frame]
        start = starts[source_seat]["frame"]
        native_start = move_start(phases, contact["source"], event["move"])
        profile = profiles[source_seat]
        rules = profile["damageRules"]
        target_rules = profiles[target_seat]["damageRules"]
        attack_config = next(move for move in profile["moves"] if move["moveId"] == event["move"])
        host_tick = int(runtime_attacker["elapsed"]) if runtime_attacker["move"] == event["move"] else frame - start
        pulse = int(event["move"] == NEAR_HEAVY and host_tick >= 6)
        attack = attack_config["attacks"][pulse]
        formula = attack["dynamicDamage"]
        native_variance = native_attacker["vars"]["var21"]
        native_counter = native_attacker["vars"]["var6"]
        native_divisor = max(1, native_attacker["vars"]["var23"])
        host_variance = int(runtime_damage["variance"])
        host_counter = int(runtime_damage["counter"])
        host_divisor = int(runtime_damage["divisor"]) if formula["useContactDivisor"] else 1
        native_remaining = phases["before-command"][native_frame][native_target]["life"]
        host_remaining = int(target_before["health"])
        native_multiplier = native_attacker["attackMul"]
        native_defense = native_defender["finalDefense"]
        expected_native_raw = raw_damage(formula, native_variance, native_counter, native_divisor, native_attacker["vars"]["fvar7"])
        expected_host_raw = raw_damage(formula, host_variance, host_counter, host_divisor, ratio(rules, "baseScale"))
        native_raw = native_attacker["hitDef"]["damage"]
        host_defense = ratio(target_rules, "defenseMultiplier")
        if target_before["state"] in HIT_REACTIONS:
            host_defense = f32(host_defense / f32(ratio(target_rules, "hitstunDefenseMultiplier")))
        if event["kind"] == "Blocked":
            check(0, native_attacker["hitDef"]["guardDamage"], f"contact {native_frame} native chip damage")
            native_expected = host_expected = 0
        else:
            native_expected = committed_damage(expected_native_raw, native_multiplier, native_defense, native_remaining)
            host_expected = committed_damage(expected_host_raw, ratio(rules, "attackMultiplier"), host_defense, host_remaining)
        native_actual = native_remaining - phases["after-collision"][native_frame + 1][native_target]["life"]
        check(native_frame - native_start, frame - start, f"contact {native_frame} source elapsed clock")
        check(STATE_BY_MOVE[event["move"]], native_attacker["state"], f"contact {native_frame} source StateDef")
        check(native_attacker["time"], host_tick, f"contact {native_frame} attacker motion tick")
        check("Right" if native_attacker["facing"] > 0 else "Left", runtime_attacker["facing"],
              f"contact {native_frame} committed attacker facing")
        check(native_raw, expected_native_raw, f"contact {native_frame} native HitDef raw")
        check(f32(native_defense), host_defense, f"contact {native_frame} defender multiplier")
        check(native_actual, native_expected, f"contact {native_frame} native committed life delta")
        check(host_expected, event["amount"], f"contact {native_frame} Godot committed amount")
        check(host_remaining - host_expected, int(runtime_defender["health"]), f"contact {native_frame} Godot atomic life")
        if formula["useContactDivisor"]:
            check(native_divisor, host_divisor, f"contact {native_frame} divisor")
        check(native_counter, host_counter, f"contact {native_frame} counter age")
        if name in END_COUNTER and event["move"] == HEAVY:
            check(END_COUNTER[name], host_counter, f"{name} carry boundary")
        check(native_defender["hitShake"], int(runtime_defender["stop"]), f"contact {native_frame} defender freeze")
        check(phases["end"][native_frame][contact["source"]]["hitPause"], int(runtime_damage["pause"]),
              f"contact {native_frame} attacker script pause")
        samples.append({"native_frame": native_frame, "logic": frame, "move": event["move"], "source": source_seat,
                        "native": {"rng": contact["draw"]["rng"], "variance": native_variance, "counter": native_counter,
                                   "divisor": native_divisor, "raw": native_raw, "damage": native_actual,
                                   "defense": native_defense, "definition_frame": contact["definition_frame"]},
                        "godot": {"rng": int(runtime_damage["rng"]), "variance": host_variance, "counter": host_counter,
                                  "divisor": host_divisor, "raw": expected_host_raw, "damage": event["amount"],
                                  "defense": host_defense}})
    return {"errors": errors, "rng_transitions": rng_transitions, "samples": samples}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reference", type=Path, required=True)
    parser.add_argument("--edge-reference", type=Path, help="Supplemental native directory for the exact age-3 case")
    parser.add_argument("--candidate", type=Path, required=True)
    parser.add_argument("--updated-candidate", type=Path, help="Prefer an available targeted rerun over the main capture")
    parser.add_argument("--profile-dir", type=Path, default=Path("data/imported-characters/terry98-stage17-data"))
    parser.add_argument("--mode", choices=MODES, action="append")
    parser.add_argument("--scene", choices=SCENES | GUARD_SCENES, action="append")
    options = parser.parse_args()
    failed = []
    try:
        presets = {mode: json.loads((options.profile_dir / f"{mode}.fighter.json").read_text(encoding="utf-8-sig"))
                   for mode in MODES}
        for mode in options.mode or MODES:
            for scene in options.scene or SCENES:
                name = f"{mode}-damage-{scene}"
                candidate = options.candidate / name / "sprite/trace.json"
                if options.updated_candidate is not None:
                    rerun = options.updated_candidate / name / "sprite/trace.json"
                    if rerun.is_file():
                        candidate = rerun
                reference_dir = options.edge_reference if scene == "carry-edge" and options.edge_reference else options.reference
                reference = reference_dir / f"{name}.jsonl"
                trace = json.loads(candidate.read_text(encoding="utf-8"))
                result = compare(mode, scene, reference, trace, [presets[preset] for preset in trace["modes"]])
                candidate.with_name("damage-comparison.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
                errors = result["errors"]
                print(f"{name}: {len(result['samples'])} contacts, {result['rng_transitions']} RNG transitions, {len(errors)} discrepancies")
                if errors:
                    failed.append(name)
                    for error in errors[:8]:
                        print(f"  {error}")
    except (OSError, KeyError, ValueError) as error:
        parser.exit(1, f"Stage 17 damage comparison failed: {error!r}\n")
    if failed:
        parser.exit(1, "Stage 17 damage comparison failed: " + ", ".join(failed) + "\n")


if __name__ == "__main__":
    main()
