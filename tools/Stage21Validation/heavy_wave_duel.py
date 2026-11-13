"""Replay the native Terry State 215 cancel and its two contact groups in Godot Vulkan."""

import argparse
from dataclasses import replace
from functools import partial
from hashlib import sha256
import json
from pathlib import Path
from subprocess import TimeoutExpired
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Stage20Validation"))
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Stage19Validation"))
from power_wave_duel import (MOVE, NATIVE_MOVES, ROOT, assert_equal, audit_rows, indexed, native_case,
                             run_case, verify_contacts, verify_grounded_wave_recovery, visual_timeline)
from near_wave_duel import (capture_sparse_realtime, compare_repeat, fixture as stage20_fixture, verify_routes,
                            verify_stage20_contacts)
from compare_damage import (HIT_REACTIONS, RNG_MODULUS, RNG_MULTIPLIER, committed_damage,
                            damage_roll, f32, ratio, raw_damage)


HEAVY = NATIVE_MOVES[215]
PROFILE_DIR = "data/imported-characters/terry98-stage21-data"
CONTACTS = ("Hit", "Blocked", "ProjectileHit", "ProjectileBlocked")


def fixture(path, name, mode):
    metadata = json.loads((path.parent / "run-metadata.json").read_text(encoding="utf-8-sig"))
    if path.stem not in metadata["executed_cases"]:
        raise ValueError(f"case absent from native run metadata: {path.stem}")
    case_meta = json.loads(path.with_name(f"{path.stem}-metadata.json").read_text(encoding="utf-8-sig"))
    assert_equal(case_meta["trace_sha256"], sha256(path.read_bytes()).hexdigest(), "native trace SHA-256")
    last = metadata.get("last_frame", 75)
    scene, phases, changes = native_case(path, name, last_frame=last)
    if mode == "mode2" and any(row["to"] == 1050 for row in changes):
        boundary = next(row["frame"] for row in changes if row["to"] == 1050)
        control = path.with_name("mode2-heavy-wave-hit-none-0.jsonl")
        _, (expected, control_phases, _) = fixture(control, "hit-none-0", mode)
        assert_equal(tuple(row for row in scene.contacts if row["frame"] < boundary),
                     tuple(row for row in expected.contacts if row["frame"] < boundary), "mode 2 common contact prefix")
        # Replay the motion input as a negative control. The supported baseline stays in 215
        # and matches the native no-motion control after the out-of-scope 1050 entry.
        phases["end"] = control_phases["end"]
        phases["after-collision"] = control_phases["after-collision"]
        scene = replace(scene, contacts=expected.contacts, differences=("native-mode2-1050-outside-scope",))
    if "whiff-" in name:
        up = "W" if name.startswith("mirror-") else "Up"
        jump = {f"23:down:{up}": f"17:down:{up}", f"24:up:{up}": f"23:up:{up}"}
        keys = tuple(jump.get(step, step) for step in scene.keys)
        scene = replace(scene, keys=keys, differences=(*scene.differences, "neutral-jump-timing"))
    first = min(row["frame"] for row in scene.moves)
    waves = [row for row in scene.moves if row["move"] == MOVE]
    if len(waves) > 1:
        first = waves[-1]["frame"] - 5
    wave = waves[-1] if waves else None
    # Limit readbacks to the active/cancel and entity boundaries. The full pose clock remains audited.
    final_capture = (wave["frame"] + 23 + 26) if wave else min(last, first + 30) + 23
    scene = replace(scene, captures=(first + 23, min(first + 112, final_capture)))
    return last, (scene, phases, changes)


def normal_damage(profile, damage, moves, event, attack):
    seat, target, logic = (event[key] for key in ("source", "target", "frame"))
    prior = moves[target, logic - 1]
    operands = damage[seat, logic]
    rules = profile["damageRules"]
    base = raw_damage(attack["dynamicDamage"], int(operands["variance"]), int(operands["counter"]),
                      int(operands["divisor"]), ratio(rules, "baseScale"))
    defense = ratio(rules, "defenseMultiplier")
    if prior["state"] in HIT_REACTIONS:
        defense = f32(defense / f32(ratio(rules, "hitstunDefenseMultiplier")))
    return committed_damage(base, ratio(rules, "attackMultiplier"), defense, int(prior["health"]))


def native_heavy_damage(source, native, profile, expected_group):
    frame, seat, target = (native[key] for key in ("frame", "source", "target"))
    row = source["collision"][frame]["chars"][target]
    hitdefs = [item for item in source["hitdefs"] if item["frame"] <= frame and item["hitDefOwner"] == seat + 1
               and item["chars"][seat]["state"] == 215]
    if not hitdefs:
        raise ValueError(f"missing native 215 HitDef at F{frame}, seat {seat + 1}")
    hitdef = hitdefs[-1]
    attacker = hitdef["chars"][seat]
    tick = attacker["time"]
    assert_equal(expected_group, 1 if tick < 6 else 2, "native HitDef contact group")
    draws = [item for item in source["draws"] if item["frame"] <= hitdef["frame"] and item["seat"] == seat + 1]
    if not draws:
        raise ValueError(f"missing native variance draw at F{hitdef['frame']}, seat {seat + 1}")
    draw = draws[-1]
    variance = attacker["vars"]["var21"]
    assert_equal(variance, draw["value"], "native damage variance")
    assert_equal(damage_roll(draw["rng"], profile["damageRules"]["varianceMaximum"]), variance, "native RNG roll")
    move = next(item for item in profile["moves"] if item["moveId"] == HEAVY)
    attack = move["attacks"][expected_group - 1]
    divisor = max(1, attacker["vars"]["var23"])
    base = raw_damage(attack["dynamicDamage"], variance, attacker["vars"]["var6"], divisor, attacker["vars"]["fvar7"])
    assert_equal(attacker["hitDef"]["damage"], base, "native raw HitDef damage")
    prior = source["input"][frame]["chars"][target]["life"]
    amount = 0 if native["kind"] == "Blocked" else committed_damage(base, attacker["attackMul"], row["finalDefense"], prior)
    assert_equal(amount, prior - source["collision"][frame + 1]["chars"][target]["life"], "native reaction-phase HP")
    return {"frame": frame, "tick": tick, "rng": draw["rng"], "divisor": divisor, "amount": amount}


def audit_contacts(source, scene, events, moves, damage, profile, *, known_second_light=None, strict_native):
    differences = verify_contacts(scene, events, moves, damage, profile,
                                  known_second_light=known_second_light, strict_native=strict_native,
                                  guard_position_tolerance=0.005)
    move = next(item for item in profile["moves"] if item["moveId"] == HEAVY)
    pairs = zip(sorted(scene.contacts, key=lambda row: (row["frame"], row["target"])),
                sorted((row for row in events if row["kind"] in CONTACTS),
                       key=lambda row: (row["frame"], row["target"])), strict=True)
    samples = []
    for native, event in pairs:
        if event["move"] != HEAVY:
            continue
        logic, seat, target = (event[key] for key in ("frame", "source", "target"))
        tick = int(moves[seat, logic]["elapsed"])
        group = 1 if tick < 6 else 2
        attack = move["attacks"][group - 1]
        outcome = "block" if event["kind"] == "Blocked" else "hit"
        stops = attack["hitstopByOutcome"][outcome]
        assert_equal((stops["attackerFrames"], stops["defenderFrames"]),
                     (int(moves[seat, logic]["stop"]), int(moves[target, logic]["stop"])), "both contact freezes")
        divisor = int(damage[seat, logic]["divisor"])
        native_seat = seat ^ source["mirror"]
        native_sample = native_heavy_damage(source, {**native, "source": native_seat, "target": 1 - native_seat},
                                            profile, group)
        assert_equal(native_sample["divisor"], divisor, "native and Godot damage divisors")
        amount = 0 if outcome == "block" else normal_damage(profile, damage, moves, event, attack)
        assert_equal(amount, event["amount"], "host dynamic damage")
        assert_equal(amount, int(moves[target, logic - 1]["health"]) - int(moves[target, logic]["health"]),
                     "host atomic HP")
        samples.append({"native": native_sample, "logic": logic, "group": group, "amount": amount,
                        "rng": int(damage[seat, logic]["rng"]), "variance": int(damage[seat, logic]["variance"]),
                        "attackerPause": stops["attackerFrames"], "defenderPause": stops["defenderFrames"]})
    differences.extend({"heavyDamageComparison": sample} for sample in samples)
    return differences


def audit_lifecycle(scene, _phases, events, moves, reentry, lines):
    starts = sorted((event for event in events if event["kind"] == "MoveStarted"),
                    key=lambda row: (row["frame"], row["source"]))
    contacts = [event for event in events if event["kind"] in ("Hit", "Blocked") and event["move"] == HEAVY]
    damage = indexed(audit_rows(lines, "DamageAudit"))
    pauses = {int(row["logic"]) for row in audit_rows(lines, "ScriptedPauseAudit") if row["active"] == "True"}
    assert_equal(set(moves), set(damage), "complete damage audit for both seats")
    assert_equal(set(moves), set(reentry), "complete instance audit for both seats")
    for seat in (0, 1):
        states = sorted(frame for index, frame in damage if index == seat)
        rng = 1337 + 104729 * seat
        for logic in states:
            previous = damage.get((seat, logic - 1))
            if logic not in pauses and (previous is None or previous["pause"] == "0"):
                rng = rng * RNG_MULTIPLIER % RNG_MODULUS
            current = damage[seat, logic]
            assert_equal(rng, int(current["rng"]), f"seat {seat} RNG at logic {logic}")
            assert_equal(damage_roll(rng, 2), int(current["variance"]), f"seat {seat} variance at logic {logic}")

    groups = {}
    contact_groups = []
    rendered = indexed(audit_rows(lines, "SpriteRenderAudit"))
    health_events = {(event["target"], event["frame"]): event for event in events if event["kind"] in CONTACTS}
    for (seat, logic), row in moves.items():
        previous = moves.get((seat, logic - 1))
        if previous is None:
            continue
        event = health_events.get((seat, logic))
        delta = int(previous["health"]) - int(row["health"])
        if event is None or event["kind"] in ("Hit", "Blocked", "ProjectileHit"):
            assert_equal(0 if event is None else event["amount"], delta, "no hidden HP mutation")
    for contact in (event for event in events if event["kind"].endswith("Blocked")):
        target, logic = (contact[key] for key in ("target", "frame"))
        if (pose := rendered.get((target, logic + 1))) is not None:
            assert_equal(str(scene.guard_action), pose["action"], "actual guard contact pose")
    for contact in contacts:
        seat, target, logic = (contact[key] for key in ("source", "target", "frame"))
        started = max(row["frame"] for row in starts if row["source"] == seat and row["frame"] <= logic)
        tick = int(moves[seat, logic]["elapsed"])
        group = 1 if tick < 6 else 2
        identity = (seat, started, target)
        mask = groups.get(identity, 0)
        bit = 1 << (group - 1)
        if mask & bit:
            raise ValueError(f"duplicate contact group {group} from {identity} at logic {logic}")
        groups[identity] = mask | bit
        assert_equal(str(started), reentry[target, logic]["lastHitStart"], "target deduplication source instance")
        assert_equal(str(groups[identity]), reentry[target, logic]["lastHitGroups"], "target contact group mask")
        confirm = "blockConfirm" if contact["kind"] == "Blocked" else "hitConfirm"
        assert_equal(str(logic), reentry[seat, logic][confirm], "old instance confirmation commits")
        contact_groups.append({**contact, "sourceStarted": started, "sourceTick": tick, "group": group})

    routes = []
    for start, native in zip(starts, sorted(scene.moves, key=lambda row: (row["frame"], row["hostSeat"])), strict=True):
        if native["from"] != 215 or start["move"] != MOVE:
            continue
        seat, logic = (start[key] for key in ("source", "frame"))
        old = moves[seat, logic - 1]
        tick = int(old["elapsed"]) + 1
        assert_equal(HEAVY, old["move"], "source before cancel")
        assert_equal(native["stateTime"], tick, "native cancellation tick")
        if tick not in range(4, 13) and tick != 28:
            raise ValueError(f"unexpected 215 cancel tick {tick}")
        assert_equal("0", old["stop"], "cancel after source HitPause")
        assert_equal("", moves[seat, logic]["attacks"], "new Wave starts without an old attack box")
        assert_equal(("1", "0"), (damage[seat, logic]["divisor"], damage[seat, logic]["pause"]),
                     "new instance damage-local state")
        following = next((row["frame"] for row in starts if row["source"] == seat and row["frame"] > logic), float("inf"))
        assert_equal([], [row for row in events if row["source"] == seat and row["move"] == HEAVY
                          and logic <= row["frame"] < following], "no old contact or attack on/after cancel")
        previous = reentry[seat, logic - 1]
        prior_group1 = next((row for row in contact_groups if row["source"] == seat and row["group"] == 1
                             and row["sourceStarted"] == int(previous["started"])), None)
        planned_second = int(previous["started"]) + 6 + (prior_group1 is not None) * 11
        routes.append({"seat": seat, "logic": logic, "nativeFrame": native["frame"], "sourceTick": tick,
                       "sourceStarted": int(previous["started"]), "hitConfirm": int(previous["hitConfirm"]),
                       "blockConfirm": int(previous["blockConfirm"]), "plannedSecondGroupLogic": planned_second,
                       "plannedSecondGroupSuppressed": tick <= 6})
    return {"heavyContactGroups": contact_groups, "heavyCancelRoutes": routes, "rngTransitions": len(damage)}


def realtime_scene(scene):
    # At 30 FPS the elapsed logic time between edges differs from a fixed replay. Buffer a compact
    # motion from the first heavy release, using exactly the same physical edge order.
    first = min(int(step.split(":", 1)[0]) for step in scene.keys)
    keys = []
    for step in scene.keys:
        render, action, key = step.split(":")
        render = int(render)
        keys.append(f"{render - (2 if render >= first + 3 else 0)}:{action}:{key}")
    return replace(scene, keys=tuple(keys))


def check_realtime(starts, events):
    assert_equal([HEAVY, MOVE], [row["move"] for row in starts], "real 215 and Wave sequence")
    hit, = [row for row in events if row["kind"] == "Hit" and row["move"] == HEAVY]
    wave, = [row for row in events if row["kind"] == "ProjectileHit"]
    assert_equal(3, hit["frame"] - starts[0]["frame"], "realtime first group contact")
    assert_equal(12, starts[1]["frame"] - hit["frame"], "realtime thaw before cancel")
    assert_equal(16, wave["frame"] - starts[1]["frame"], "realtime Wave contact")
    return wave


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--godot", required=True, type=Path)
    parser.add_argument("--reference", required=True, type=Path, action="append")
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--profile-dir", default=PROFILE_DIR)
    parser.add_argument("--scene", required=True, action="append")
    parser.add_argument("--mode", choices=("default", "mode2"), default="default")
    parser.add_argument("--compare-plain", action="store_true")
    parser.add_argument("--legacy-stage20", action="store_true", help="Replay the Stage 20 inputs with the Stage 21 preset")
    parser.add_argument("--repeat-from", type=Path)
    parser.add_argument("--fixed-window", nargs=2, type=int, metavar=("FIRST", "LAST"))
    parser.add_argument("--fps", choices=("fixed", "30", "60"), default="fixed")
    parser.add_argument("--realtime-window", nargs=2, type=int, metavar=("FIRST", "LAST"))
    parser.add_argument("--realtime-resolution", default="320x180")
    parser.add_argument("--realtime-warmup-frames", type=int, default=150)
    options = parser.parse_args()
    options.recovery = False
    options.require_clean_sample = True
    options.require_no_backlog = options.fps != "fixed"
    options.disable_vsync = True
    options.realtime_input_spacing = 1
    options.output = options.output.resolve()
    if options.fixed_window:
        first, last = options.fixed_window
        if options.fps != "fixed" or options.legacy_stage20 or not 0 < first <= last < first + 90:
            parser.error("--fixed-window requires an increasing range of up to 90 Stage 21 fixed renders")
    if options.fps != "fixed":
        if (options.scene != ["hit-held-11"] or options.mode != "default" or options.compare_plain or options.repeat_from
                or options.legacy_stage20
                or not options.realtime_window or not 0 < options.realtime_window[0] <= options.realtime_window[1] < options.realtime_window[0] + 3):
            parser.error("realtime requires default hit-held-11 and a window of up to three renders")
    options.output.mkdir(parents=True, exist_ok=False)
    manifest = json.loads((ROOT / "data/imported-characters/terry98/manifest.json").read_text(encoding="utf-8-sig"))
    timelines = {action: visual_timeline(manifest, action) for action in (200, 205, 210, 211, 215, 1000, 1020)}
    profile = json.loads((ROOT / options.profile_dir / f"{options.mode}.fighter.json").read_text(encoding="utf-8-sig"))
    alias = f"terry98-stage21-{options.mode}"
    failed = []
    for name in options.scene:
        try:
            family = "near-wave" if options.legacy_stage20 else "heavy-wave"
            file = f"{options.mode}-{family}-{name}.jsonl"
            path = next((folder / file for folder in options.reference if (folder / file).is_file()), None)
            if path is None:
                raise ValueError(f"native source missing: {file}")
            if options.legacy_stage20:
                reference, last, prepared = stage20_fixture(path.parent, name, options.mode)
                run_case(options, name, reference, timelines, profile, mode=options.mode, alias=alias, prepared_scene=prepared,
                         fixed_checks=verify_routes, contact_check=partial(verify_stage20_contacts, name),
                         minimum_render_frames=last + 25, partial_capture="mixed-" in name)
                continue
            last, prepared = fixture(path, name, options.mode)
            scene, phases, _ = prepared
            if options.fps != "fixed":
                capture_sparse_realtime(options, name, realtime_scene(scene), alias, check_realtime)
                continue
            if options.fixed_window:
                scene = replace(scene, captures=tuple(options.fixed_window))
                prepared = scene, phases, prepared[2]
            damage_path = (path.with_name("mode2-heavy-wave-hit-none-0.jsonl")
                           if "native-mode2-1050-outside-scope" in scene.differences else path)
            rows = [json.loads(line) for line in damage_path.read_text(encoding="utf-8-sig").splitlines()]
            native = {"mirror": int(name.startswith("mirror-")), "collision": phases["after-collision"],
                      "input": phases["before-command"], "draws": [row for row in rows if row["phase"] == "var21-draw"],
                      "hitdefs": [row for row in rows if row["phase"] == "hitdef"]}
            run_case(options, name, path, timelines, profile, mode=options.mode, alias=alias, prepared_scene=prepared,
                     fixed_checks=audit_lifecycle, contact_check=partial(audit_contacts, native),
                     recovery_check=partial(verify_grounded_wave_recovery, position_tolerance=0.01, crouch_recovery=True),
                     minimum_render_frames=last + 25, partial_capture=True)
            if options.repeat_from:
                compare_repeat(options.output, name, options.repeat_from)
        except (KeyError, OSError, ValueError, TimeoutExpired) as error:
            print(f"{name}: FAIL {error!r}", flush=True)
            failed.append(name)
    if failed:
        parser.exit(1, "Stage 21 failed fixtures: " + ", ".join(failed) + "\n")


if __name__ == "__main__":
    main()
