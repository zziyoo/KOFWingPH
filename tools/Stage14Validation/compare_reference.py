"""Compare Godot duel evidence with the sampled IKEMEN State 200 timeline."""

import argparse
import json
from pathlib import Path

from state200_duel import MOVE, SCENARIOS


def reference_boxes(fighter, collision_key):
    origin_x, origin_y, _ = fighter["pos"]
    facing = fighter["facing"]
    boxes = []
    for x0, y0, x1, y1 in fighter["frame"][collision_key] or []:
        left, right = sorted((origin_x + x0 * facing, origin_x + x1 * facing))
        boxes.append([left, -origin_y - y1, right, -origin_y - y0])
    return boxes


def fixed_boxes(value):
    return [[int(edge) / 65536 for edge in box.split(",")] for box in value.split(";") if box]


def compare(scenario, reference, candidate):
    _, seat, outcome = SCENARIOS[scenario]
    attacker, defender = seat - 1, 2 - seat
    raw = [json.loads(line) for line in reference.read_text(encoding="utf-8").splitlines()]
    collision = {row["frame"]: row["chars"] for row in raw if row["phase"] == "after-collision"}
    end = {row["frame"]: row["chars"] for row in raw if row["phase"] == "end"}
    reference_start = min(frame for frame, pair in collision.items() if pair[0]["state"] == 200)
    reference_exit = min(frame for frame, pair in collision.items() if frame > reference_start and pair[0]["state"] == 0)
    evidence = json.loads(candidate.read_text(encoding="utf-8"))
    godot_start = next(event[0] for event in evidence["events"] if event[1] == "MoveStarted")
    godot_active = next(event[0] for event in evidence["events"] if event[1] == "AttackActive")
    moves = {(int(row["logic"]), int(row["fighter"])): row for row in evidence["moves"]}
    godot_exit = min(frame for (frame, fighter), row in moves.items()
                     if frame > godot_start and fighter == attacker and row["move"] == "-")
    reference_active = min(frame for frame, pair in collision.items()
                           if frame >= reference_start and pair[0]["state"] == 200 and reference_boxes(pair[0], "clsn1"))
    errors = []

    def check(expected, actual, label):
        if expected != actual:
            errors.append(f"{label}: IKEMEN={expected!r}, Godot={actual!r}")

    check(reference_active - reference_start, godot_active - godot_start, "first active tick")
    check(reference_exit - reference_start, godot_exit - godot_start, "attacker exit tick")
    comparisons = []
    for ref_frame in range(reference_start, reference_exit + 1):
        tick = ref_frame - reference_start
        ref_attacker, ref_defender = collision[ref_frame]
        host_attacker = moves[godot_start + tick, attacker]
        host_defender = moves[godot_start + tick, defender]
        host_active = host_attacker["move"] == MOVE
        ref_active = ref_attacker["state"] == 200
        check(ref_active, host_active, f"tick {tick} State 200 active")
        if ref_active and host_active:
            check(ref_attacker["time"] - 1, int(host_attacker["elapsed"]), f"tick {tick} attacker move clock")
        check("Right" if ref_attacker["facing"] > 0 else "Left", host_attacker["facing"], f"tick {tick} facing")
        check(ref_attacker["pos"][0], int(host_attacker["x"]) / 65536, f"tick {tick} attacker x")
        check(reference_boxes(ref_attacker, "clsn1") if ref_active else [],
              fixed_boxes(host_attacker["attacks"]), f"tick {tick} world attack box")
        same_neutral = (ref_defender["anim"], host_defender["state"]) in ((0, "Standing"), (11, "Crouching"))
        if same_neutral:
            check(reference_boxes(ref_defender, "clsn2"), fixed_boxes(host_defender["hurt"]),
                  f"tick {tick} neutral defender hurtboxes")
        check(end[ref_frame][0]["hitPause"], int(host_attacker["stop"]), f"tick {tick} attacker next-frame stop")
        check(end[ref_frame][1]["hitShake"], int(host_defender["stop"]), f"tick {tick} defender next-frame stop")
        comparisons.append({"tick": tick, "ikemen_collision": [ref_attacker, ref_defender],
                            "godot_committed": [host_attacker, host_defender]})

    contact = None
    if outcome is None:
        check(0, max(pair[0]["moveContact"] for pair in collision.values()), "reference whiff did not settle contact")
        check(collision[reference_start - 1][1]["life"], int(moves[godot_exit, defender]["health"]), "whiff life unchanged")
    else:
        host_contact, _, _, _, amount = next(event for event in evidence["events"] if event[1] == outcome)
        ref_contact = min(frame for frame, pair in collision.items() if frame >= reference_start and pair[0]["moveContact"] == 1)
        check(ref_contact - reference_start, host_contact - godot_start, "contact tick")
        check(outcome == "Blocked", collision[ref_contact][1]["guarded"], "contact guarded")
        hp_before = collision[reference_start - 1][1]["life"]
        ref_damage = hp_before - collision[ref_contact + 1][1]["life"]
        check(ref_damage, amount, "damage after contact")
        check(hp_before - ref_damage, int(moves[host_contact, defender]["health"]), "Godot commits damage with contact")
        ref_resume = min(frame for frame in end if frame > ref_contact and end[frame][1]["ctrl"])
        host_resume = min(frame for (frame, fighter), row in moves.items()
                          if frame > host_contact and fighter == defender and row["stun"] == "0" and row["stop"] == "0"
                          and row["state"] not in ("Hitstun", "Blockstun"))
        check(ref_resume - ref_contact, host_resume - host_contact, "defender control restored after contact")
        ref_x0 = collision[ref_contact][1]["pos"][0]
        host_x0 = int(moves[host_contact, defender]["x"]) / 65536
        deltas = []
        for tick in range(ref_resume - ref_contact + 1):
            ref_x = collision[ref_contact + tick][1]["pos"][0] - ref_x0
            host_x = int(moves[host_contact + tick, defender]["x"]) / 65536 - host_x0
            deltas.append([tick, ref_x, host_x])
            # IKEMEN integrates float32; NewKOF integrates Q16. 0.002 world units covers rounding only.
            if abs(ref_x - host_x) > 0.002:
                errors.append(f"contact+{tick}: defender push IKEMEN={ref_x:.6f}, Godot={host_x:.6f}")
        contact = {"ikemen_frame": ref_contact, "godot_frame": host_contact, "outcome": outcome,
                   "damage": amount, "ikemen_defender_initial_x": ref_x0, "godot_defender_initial_x": host_x0,
                   "defender_displacement": deltas}
    return {"scenario": scenario, "ikemen_move_origin": reference_start, "godot_move_origin": godot_start,
            "ikemen_exit": reference_exit, "godot_exit": godot_exit, "contact": contact,
            "timeline": comparisons, "mismatches": errors}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reference", required=True, type=Path, help="Directory containing IKEMEN JSONL traces")
    parser.add_argument("--candidate", required=True, type=Path, help="Directory created by state200_duel.py")
    parser.add_argument("--scene", choices=SCENARIOS, action="append")
    options = parser.parse_args()
    try:
        failed = False
        for scene in options.scene or SCENARIOS:
            candidate = options.candidate / scene / "sprite/trace.json"
            result = compare(scene, options.reference / f"{scene}.jsonl", candidate)
            with candidate.with_name("reference-diff.json").open("w", encoding="utf-8") as output:
                json.dump(result, output, indent=2)
                output.write("\n")
            if result["mismatches"]:
                print(f"{scene}: {len(result['mismatches'])} mismatches\n" + "\n".join(result["mismatches"][:12]))
            else:
                print(f"{scene}: sampled reference agrees across {len(result['timeline'])} state ticks")
            failed |= bool(result["mismatches"])
    except (KeyError, OSError, StopIteration, ValueError) as error:
        parser.exit(1, f"Reference comparison failed: {error!r}\n")
    raise SystemExit(1 if failed else 0)


if __name__ == "__main__":
    main()
