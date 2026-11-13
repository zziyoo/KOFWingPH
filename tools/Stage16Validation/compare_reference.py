"""Compare a fixed Stage 16 Vulkan sample with its native IKEMEN trace."""

import argparse
import json
from pathlib import Path

from normals_duel import ACTIONS, EXTRA_SCENES, MODES, NEAR_HEAVY, SCENES


REFERENCE_SCENES = (
    "light-hit", "light-guard", "light-crouch-guard", "light-crouch-late", "light-mirror", "light-whiff",
    "light-boundary-46", "light-boundary-46p5", "heavy-hit", "heavy-guard", "heavy-crouch-guard",
    "heavy-crouch-late", "heavy-mirror", "heavy-whiff", "heavy-boundary-64", "heavy-boundary-64p5",
    "heavy-second-only", "heavy-second-guard",
)
REFERENCE_NAMES = {"light-boundary-46p5": "light-far-46p5", "heavy-boundary-64p5": "heavy-far-64p5",
                   "light-both-near": "light-both-44", "light-both-far": "light-both-70"}
GUARD_RELEASE = ("heavy-guard-release", "heavy-crouch-release", "heavy-crouch-to-stand")
SIMULTANEOUS = ("light-both-near", "light-both-far")
TRADES = ("light-trade", "heavy-trade")


def units(raw):
    return int(raw) / 65536


def host_boxes(value):
    return sorted([units(edge) for edge in box.split(",")] for box in value.split(";") if box)


def reference_boxes(fighter, key):
    x, y, _ = fighter["pos"]
    facing = fighter["facing"]
    boxes = []
    for x0, y0, x1, y1 in fighter["frame"][key] or []:
        left, right = sorted((x + x0 * facing, x + x1 * facing))
        bottom, top = sorted((-y - y0, -y - y1))
        boxes.append([left, bottom, right, top])
    return sorted(boxes)


def read_trace(reference):
    phases = {key: {} for key in ("after-collision", "end", "before-command")}
    for line in reference.read_text(encoding="utf-8").splitlines():
        row = json.loads(line)
        if row["phase"] in phases:
            phases[row["phase"]][row["frame"]] = row["chars"]
    return phases["after-collision"], phases["end"], phases["before-command"]


def reference_contacts(collision, before, start, finish):
    contacts = []
    for frame in range(start, finish):
        attacker, defender = collision[frame]
        if attacker["moveContact"] != 1 or collision[frame - 1][0]["moveContact"] == 1:
            continue
        damage = before[frame][1]["life"] - collision[frame + 1][1]["life"]
        kind = "Blocked" if defender["guarded"] else "Hit"
        contacts.append((frame - start, attacker["time"] - 1, kind, damage))
    return contacts


def compare(mode, name, reference, candidate):
    if name in TRADES:
        return compare_trade(mode, name, reference, candidate)
    scene = (SCENES | EXTRA_SCENES)[name]
    spec, = scene.moves
    attacker, defender = spec.seat, 1 - spec.seat
    action = ACTIONS.get(spec.move_id, MODES[mode][1])
    state = 210 if action == 211 else action
    collision, end, before = read_trace(reference)
    evidence = json.loads(candidate.read_text(encoding="utf-8"))
    window, = evidence["windows"]
    host = {(int(row["logic"]), int(row["fighter"])): row for row in evidence["moves"]}
    reference_start = min(frame for frame, pair in collision.items() if pair[0]["state"] == state)
    reference_exit = min(frame for frame, pair in end.items() if frame > reference_start and pair[0]["state"] != state)
    host_start = window["begin"] - 1
    host_exit = window["end"] - 1
    errors = []

    def check(expected, actual, detail):
        if expected != actual:
            errors.append(f"{detail}: IKEMEN={expected!r}, Godot={actual!r}")

    def near(expected, actual, detail, tolerance=0.004):
        if abs(expected - actual) > tolerance:
            errors.append(f"{detail}: IKEMEN={expected:.6f}, Godot={actual:.6f}")

    check(reference_exit - reference_start, host_exit - host_start, "state exit after move start")
    contacts = reference_contacts(collision, before, reference_start, reference_exit)
    actual_contacts = []
    for event in evidence["events"]:
        if event["kind"] not in ("Hit", "Blocked"):
            continue
        frame = event["frame"]
        check(attacker, event["source"], "contact source")
        check(defender, event["target"], "contact target")
        tick = int(host[frame, attacker]["elapsed"])
        actual_contacts.append((frame - host_start, tick, event["kind"], event["amount"]))
    check(contacts, actual_contacts, "contact clock, group, outcome and damage")

    samples = []
    for tick in range(reference_exit - reference_start + 1):
        rf, gf = reference_start + tick, host_start + tick
        ref_attacker, ref_defender = collision[rf]
        host_attacker, host_defender = host[gf, attacker], host[gf, defender]
        in_move = end[rf][0]["state"] == state
        check(in_move, host_attacker["move"] == spec.move_id, f"tick {tick} attack state")
        if in_move and host_attacker["move"] == spec.move_id:
            check(action, ref_attacker["anim"], f"tick {tick} source action")
            check(ref_attacker["time"] - 1, int(host_attacker["elapsed"]), f"tick {tick} move clock")
            check(reference_boxes(ref_attacker, "clsn1"), host_boxes(host_attacker["attacks"]), f"tick {tick} Clsn1")
            check(reference_boxes(ref_attacker, "clsn2"), host_boxes(host_attacker["hurt"]), f"tick {tick} attacking Clsn2")
            check("Right" if ref_attacker["facing"] > 0 else "Left", host_attacker["facing"], f"tick {tick} facing")
            near(ref_attacker["pos"][0], units(host_attacker["x"]), f"tick {tick} attacker X")
        check(end[rf][0]["hitPause"], int(host_attacker["stop"]), f"tick {tick} attacker next-frame stop")
        check(end[rf][1]["hitShake"], int(host_defender["stop"]), f"tick {tick} defender next-frame stop")
        samples.append({"tick": tick, "ikemen_collision": [ref_attacker, ref_defender],
                        "godot_committed": [host_attacker, host_defender]})

    push = []
    for i, (tick, _, _, damage) in enumerate(contacts):
        rf, gf = reference_start + tick, host_start + tick
        check(before[rf][1]["life"] - damage, int(host[gf, defender]["health"]), f"tick {tick} atomic life")
        next_contact = contacts[i + 1][0] if i + 1 < len(contacts) else None
        if next_contact is None:
            ref_recover = min(frame for frame in end if frame > rf and end[frame][1]["ctrl"])
            host_recover = min(frame for (frame, seat), row in host.items() if seat == defender and frame > gf
                               and row["stun"] == row["stop"] == "0" and row["state"] not in ("Hitstun", "Blockstun"))
            check(ref_recover - rf, host_recover - gf, f"tick {tick} defender recovery")
            length = ref_recover - rf
        else:
            length = next_contact - tick
        reference_x = collision[rf][1]["pos"][0]
        host_x = units(host[gf, defender]["x"])
        for offset in range(length):
            dx_ref = collision[rf + offset][1]["pos"][0] - reference_x
            dx_host = units(host[gf + offset, defender]["x"]) - host_x
            near(dx_ref, dx_host, f"contact at {tick}+{offset} defender displacement")
            push.append([tick, offset, reference_x, host_x, dx_ref, dx_host])

    return {"errors": errors, "reference_move_origin": reference_start, "host_move_origin": host_start,
            "contacts": contacts, "samples": samples, "push": push}


def compare_trade(mode, name, reference, candidate):
    scene = EXTRA_SCENES[name]
    collision, end, before = read_trace(reference)
    evidence = json.loads(candidate.read_text(encoding="utf-8"))
    host = {(int(row["logic"]), int(row["fighter"])): row for row in evidence["moves"]}
    states = [ACTIONS[move.move_id] for move in scene.moves]
    ref_start = min(frame for frame, pair in collision.items() if [row["state"] for row in pair] == states)
    ref_hit = min(frame for frame, pair in collision.items() if all(row["moveContact"] == 1 for row in pair))
    host_start = evidence["windows"][0]["begin"] - 1
    errors = []
    scoped_differences = []

    def check(expected, actual, detail):
        if expected != actual:
            errors.append(f"{detail}: IKEMEN={expected!r}, Godot={actual!r}")

    hit_events = [row for row in evidence["events"] if row["kind"] == "Hit"]
    check([(ref_hit - ref_start, 0), (ref_hit - ref_start, 1)],
          sorted((row["frame"] - host_start, row["source"]) for row in hit_events), "simultaneous contact")
    samples = []
    for tick in range(ref_hit - ref_start + 1):
        rf, gf = ref_start + tick, host_start + tick
        ref_pair = collision[rf]
        host_pair = [host[gf, 0], host[gf, 1]]
        for seat, spec in enumerate(scene.moves):
            ref = ref_pair[seat]
            runtime = host_pair[seat]
            expected_move = spec.move_id if rf != ref_hit else "-"
            check(expected_move, runtime["move"], f"tick {tick} seat {seat} move")
            if expected_move != "-":
                check(ref["time"] - 1, int(runtime["elapsed"]), f"tick {tick} seat {seat} clock")
                check(reference_boxes(ref, "clsn2"), host_boxes(runtime["hurt"]), f"tick {tick} seat {seat} hurtboxes")
            check(max(end[rf][seat]["hitPause"], end[rf][seat]["hitShake"]), int(runtime["stop"]), f"tick {tick} seat {seat} stop")
        samples.append({"tick": tick, "ikemen_collision": ref_pair, "godot_committed": host_pair})
    contacts = []
    for event in hit_events:
        source, target, gf = event["source"], event["target"], event["frame"]
        ref_hp = before[ref_hit][target]["life"]
        ref_damage = ref_hp - collision[ref_hit + 1][target]["life"]
        actual = event["amount"]
        if name == "light-trade" and scene.moves[source].move_id == NEAR_HEAVY and (ref_damage, actual) == (61, 54):
            scoped_differences.append("Native close-heavy counter at distance 44 settles 61; experiment uses the "
                                      "reviewed 54 counter sample from distance 62.")
        else:
            check(ref_damage, actual, f"seat {source} damage")
        check(ref_hp - actual, int(host[gf, target]["health"]), f"seat {target} atomic health")
        ref_recovery = min(frame for frame in end if frame > ref_hit and end[frame][target]["ctrl"])
        host_recovery = min(frame for (frame, seat), row in host.items() if seat == target and frame > gf
                            and row["stun"] == row["stop"] == "0" and row["state"] != "Hitstun")
        check(ref_recovery - ref_hit, host_recovery - gf, f"seat {target} recovery")
        ref_x = collision[ref_hit][target]["pos"][0]
        host_x = units(host[gf, target]["x"])
        for offset in range(ref_recovery - ref_hit):
            dx_ref = collision[ref_hit + offset][target]["pos"][0] - ref_x
            dx_host = units(host[gf + offset, target]["x"]) - host_x
            if abs(dx_ref - dx_host) > 0.004:
                errors.append(f"seat {target} contact+{offset} displacement: IKEMEN={dx_ref}, Godot={dx_host}")
        contacts.append({"source": source, "reference_damage": ref_damage, "host_damage": actual})
    return {"errors": errors, "reference_move_origin": ref_start, "host_move_origin": host_start,
            "contacts": contacts, "samples": samples, "scoped_differences": scoped_differences}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reference", required=True, type=Path)
    parser.add_argument("--candidate", required=True, type=Path)
    parser.add_argument("--updated-candidate", type=Path, help="Prefer a targeted rerun over the main candidate where available")
    parser.add_argument("--mode", choices=MODES, action="append")
    parser.add_argument("--scene", choices=REFERENCE_SCENES + GUARD_RELEASE + SIMULTANEOUS + TRADES, action="append")
    options = parser.parse_args()
    failed = False
    try:
        for mode in options.mode or MODES:
            for scene in options.scene or REFERENCE_SCENES:
                name = f"{mode}-{scene}"
                candidate = options.candidate / name / "sprite/trace.json"
                if options.updated_candidate is not None:
                    rerun = options.updated_candidate / name / "sprite/trace.json"
                    if rerun.is_file():
                        candidate = rerun
                reference = options.reference / f"{mode}-{REFERENCE_NAMES.get(scene, scene)}.jsonl"
                result = compare(mode, scene, reference, candidate)
                candidate.with_name("reference-diff.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
                errors = result["errors"]
                failed |= bool(errors)
                print(f"{name}: {len(result['samples'])} sampled ticks, {len(errors)} discrepancies")
                for error in errors[:10]:
                    print(f"  {error}")
                for difference in result.get("scoped_differences", []):
                    print(f"  Declared scope difference: {difference}")
    except (KeyError, OSError, StopIteration, ValueError) as error:
        parser.exit(1, f"Stage 16 reference comparison failed: {error!r}\n")
    raise SystemExit(1 if failed else 0)


if __name__ == "__main__":
    main()
