"""Compare the Stage 15 Godot evidence against matching IKEMEN GO samples."""

import argparse
import json
from pathlib import Path

from state210_duel import EXTRA_SCENES, HEAVY, LIGHT, MODES, SCENES


def world_boxes(fighter, key):
    x, y, _ = fighter["pos"]
    facing = fighter["facing"]
    result = []
    for x0, y0, x1, y1 in fighter["frame"][key] or []:
        left, right = sorted((x + x0 * facing, x + x1 * facing))
        bottom, top = sorted((-y - y0, -y - y1))
        result.append([left, bottom, right, top])
    return result


def q16(value):
    return int(value) / 65536


def host_boxes(value):
    return [[q16(edge) for edge in box.split(",")] for box in value.split(";") if box]


def push_tolerance():
    # .85 is truncated to 55705/65536 in NewKOF. The measured 10.5 push compounds that
    # coefficient difference over up to 16 updates; truncation of each multiply adds at most 1/65536.
    return 0.004


def compare(mode, name, reference, candidate):
    scene = (SCENES | EXTRA_SCENES)[name]
    expected_action = MODES[mode][1]
    raw = [json.loads(line) for line in reference.read_text(encoding="utf-8").splitlines()]
    collision = {row["frame"]: row["chars"] for row in raw if row["phase"] == "after-collision"}
    end = {row["frame"]: row["chars"] for row in raw if row["phase"] == "end"}
    inputs = {row["frame"]: row["chars"] for row in raw if row["phase"] == "before-command"}
    evidence = json.loads(candidate.read_text(encoding="utf-8"))
    moves = {(int(row["logic"]), int(row["fighter"])): row for row in evidence["moves"]}
    events = evidence["events"]
    errors = []

    def check(expected, actual, label):
        if expected != actual:
            errors.append(f"{label}: IKEMEN={expected!r}, Godot={actual!r}")

    def check_position(expected, actual, label, tolerance=0.002):
        if abs(expected - actual) > tolerance:
            errors.append(f"{label}: IKEMEN={expected:.6f}, Godot={actual:.6f}")

    if name == "close-64":
        reference_choice = next(pair[0]["state"] for pair in collision.values() if pair[0]["state"] in (210, 215))
        check(215, reference_choice, "close source chooses 215")
        check([], [event for event in events if event["kind"] == "MoveStarted"], "Godot withholds unimplemented 215 and gated 210")
        return {"mismatches": errors, "reference_choice": reference_choice, "samples": []}

    host_primary = scene.moves[0].seat
    if scene.moves[0].move_id != HEAVY:
        raise ValueError(f"{name}: comparator expects the first move to be State 210")
    seat_map = (host_primary, 1 - host_primary)
    ref_start = min(frame for frame, pair in collision.items() if pair[0]["state"] == 210)
    godot_start = next(event["frame"] for event in events if event["kind"] == "MoveStarted" and event["source"] == host_primary)
    ref_exit = min(frame for frame, pair in end.items() if frame > ref_start and pair[0]["state"] != 210)
    host_exit = min(frame for (frame, fighter), row in moves.items()
                    if frame > godot_start and fighter == host_primary and row["move"] != HEAVY)
    check(ref_exit - ref_start, host_exit - godot_start, "first heavy exit")

    ref_entries = []
    ref_contacts = []
    for frame, pair in sorted(collision.items()):
        previous = collision.get(frame - 1, pair)
        for ref_seat, fighter in enumerate(pair):
            state = fighter["state"]
            if state not in (200, 210):
                continue
            if previous[ref_seat]["state"] != state:
                ref_entries.append((frame - ref_start, seat_map[ref_seat], HEAVY if state == 210 else LIGHT))
            if fighter["moveContact"] == 1 and previous[ref_seat]["moveContact"] != 1:
                target = pair[1 - ref_seat]
                outcome = "Blocked" if target["guarded"] else "Hit"
                damage = inputs[frame][1 - ref_seat]["life"] - collision[frame + 1][1 - ref_seat]["life"]
                ref_contacts.append((frame - ref_start, seat_map[ref_seat], outcome, damage))
    # Result events are emitted in defender-seat order, which reverses under mirrored seats.
    host_entries = [(e["frame"] - godot_start, e["source"], e["move"]) for e in events if e["kind"] == "MoveStarted"]
    host_contacts = [(e["frame"] - godot_start, e["source"], e["kind"], e["amount"]) for e in events
                     if e["kind"] in ("Hit", "Blocked")]
    check(sorted(ref_entries), sorted(host_entries), "normalized move entries")
    check(sorted(ref_contacts), sorted(host_contacts), "normalized contact events and damage")

    render_windows = {(window["seat"], window["begin"]): window for window in evidence["windows"]}
    last_exit = max(window["end"] - 1 - godot_start for window in render_windows.values())
    samples = []
    for tick in range(last_exit + 1):
        rf, gf = ref_start + tick, godot_start + tick
        ref_pair = collision[rf]
        host_pair = [moves[gf, seat] for seat in seat_map]
        for reference_seat, (ref, host) in enumerate(zip(ref_pair, host_pair)):
            host_seat = seat_map[reference_seat]
            label = f"tick {tick} P{host_seat + 1}"
            ref_attack = end[rf][reference_seat]["state"] in (200, 210)
            host_attack = host["move"] in (LIGHT, HEAVY)
            check(ref_attack, host_attack, f"{label} active move presence")
            if ref_attack and host_attack:
                check(HEAVY if ref["state"] == 210 else LIGHT, host["move"], f"{label} move identity")
                if ref["state"] == 210:
                    action = expected_action if reference_seat == 0 else 210
                    check(action, ref["anim"], f"{label} reference animation")
                else:
                    check(200, ref["anim"], f"{label} reference animation")
                check(ref["time"] - 1, int(host["elapsed"]), f"{label} move clock")
                check(world_boxes(ref, "clsn1"), host_boxes(host["attacks"]), f"{label} attack boxes")
                check(sorted(world_boxes(ref, "clsn2")), sorted(host_boxes(host["hurt"])), f"{label} AIR move hurtboxes")
                check("Right" if ref["facing"] > 0 else "Left", host["facing"], f"{label} facing")
                check_position(ref["pos"][0], q16(host["x"]), f"{label} attacker X")
            next_stop = max(end[rf][reference_seat]["hitPause"], end[rf][reference_seat]["hitShake"])
            check(next_stop, int(host["stop"]), f"{label} next-frame stop")
        samples.append({"tick": tick, "ikemen_collision": ref_pair, "godot_committed": host_pair})

    contacts = []
    for tick, source, outcome, damage in ref_contacts:
        ref_defender = seat_map.index(1 - source)
        rf, gf = ref_start + tick, godot_start + tick
        host_target = 1 - source
        before_life = inputs[rf][ref_defender]["life"]
        host_damage = next((hit[3] for hit in host_contacts if hit[:3] == (tick, source, outcome)), None)
        if host_damage is not None:
            check(before_life - host_damage, int(moves[gf, host_target]["health"]), f"tick {tick} atomic life settlement")
        ref_recovery = min(frame for frame in end if frame > rf and end[frame][ref_defender]["ctrl"])
        host_recovery = min(frame for (frame, fighter), row in moves.items()
                            if frame > gf and fighter == host_target and row["stun"] == "0" and row["stop"] == "0"
                            and row["state"] not in ("Hitstun", "Blockstun"))
        check(ref_recovery - rf, host_recovery - gf, f"tick {tick} defender control restoration")
        ref_x = collision[rf][ref_defender]["pos"][0]
        host_x = q16(moves[gf, host_target]["x"])
        push = []
        # A held guard direction can become walk input at the control boundary. Compare displacement while stunned.
        for offset in range(ref_recovery - rf):
            ref_dx = collision[rf + offset][ref_defender]["pos"][0] - ref_x
            host_dx = q16(moves[gf + offset, host_target]["x"]) - host_x
            check_position(ref_dx, host_dx, f"tick {tick}+{offset} defender displacement", push_tolerance())
            push.append((offset, ref_dx, host_dx))
        contacts.append({"tick": tick, "source": source, "outcome": outcome, "damage": damage,
                         "reference_x": ref_x, "host_x": host_x, "relative_push": push})
    return {"mismatches": errors, "reference_move_origin": ref_start, "host_move_origin": godot_start,
            "reference_exit": ref_exit, "host_exit": host_exit, "samples": samples, "contacts": contacts}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reference", required=True, type=Path)
    parser.add_argument("--candidate", required=True, type=Path)
    parser.add_argument("--mode", choices=MODES, action="append")
    references = [name for name, scene in SCENES.items() if scene.reference_case is not None]
    parser.add_argument("--scene", choices=references + list(EXTRA_SCENES), action="append")
    options = parser.parse_args()
    failed = False
    try:
        for mode in options.mode or MODES:
            for name in options.scene or references:
                candidate = options.candidate / f"{mode}-{name}" / "sprite/trace.json"
                scene = (SCENES | EXTRA_SCENES)[name]
                reference = options.reference / f"{mode}-{scene.reference_case}.jsonl"
                comparison = compare(mode, name, reference, candidate)
                candidate.with_name("reference-diff.json").write_text(json.dumps(comparison, indent=2) + "\n", encoding="utf-8")
                errors = comparison["mismatches"]
                failed |= bool(errors)
                print(f"{mode}-{name}: {len(comparison['samples'])} sampled ticks, {len(errors)} discrepancies")
                for error in errors[:12]:
                    print(f"  {error}")
    except (KeyError, OSError, StopIteration, ValueError) as error:
        parser.exit(1, f"Reference comparison failed: {error!r}\n")
    raise SystemExit(1 if failed else 0)


if __name__ == "__main__":
    main()
