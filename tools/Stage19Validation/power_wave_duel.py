"""Verify source-driven Terry Power Wave entries and entity presentation in Vulkan."""

import argparse
from collections import defaultdict
from dataclasses import replace
import json
from pathlib import Path
import re
from subprocess import TimeoutExpired
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Stage18Validation"))
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "MugenImporter"))
from mugen_importer.state_power_wave import LAUNCH_ACTIONS
from reentry_duel import (ACTIONS, BOOT_KEYS, MOVES, RAW_KEYS, REENTRY_AUTHORITY, ROOT, Scene, assert_equal,
                          audit_rows, battle_events, indexed, require_same, run_godot, runtime_args,
                          validate_captures, verify_scripted_keys, visual_timeline)
from compare_damage import HIT_REACTIONS, f32, ratio


MOVE = "TerryPowerWave1000"
ENTITY = "TerryPowerWave1020"
ALIAS = "terry98-stage19-default"
NATIVE_MOVES = {**MOVES, 1000: MOVE}
AUTHORITY = (*REENTRY_AUTHORITY, b"[ScriptedPauseAudit] ", b"[WaveEntityAudit] ", b"[LaunchReactionAudit] ")
POSE_KEYS = ("action", "frame", "sprite", "sprite_index", "duration")
Q16 = 65536
VISUAL_FRAME = re.compile(r"^\[VisualAudit\] scope=1 frame=(\d+)\b")


def native_case(reference, name, *, recovery=False, last_frame=None, native_moves=NATIVE_MOVES,
                raw_keys=RAW_KEYS, contact_is_wave=None):
    phases = defaultdict(dict)
    changes = []
    for row in map(json.loads, reference.read_text(encoding="utf-8").splitlines()):
        if row["phase"] in ("before-command", "after-command", "after-collision", "end"):
            phases[row["phase"]][row["frame"]] = row
        elif row["phase"] == "state-change":
            changes.append(row)
    before, collision, end = (phases[key] for key in ("before-command", "after-collision", "end"))
    frame_count = last_frame + 1 if last_frame is not None else 146 if recovery else 76
    for phase, samples in (("input", before), ("collision", collision), ("end", end)):
        assert_equal(set(range(frame_count)), set(samples), f"native {phase} trace")
    mirror = name.startswith("mirror-")
    keys = []
    raw = [0, 0]
    for frame, state in before.items():
        for native_seat, fighter in enumerate(state["chars"]):
            seat = native_seat ^ mirror
            for bit, *key_names in raw_keys:
                if (fighter["raw"] ^ raw[seat]) & bit:
                    keys.append(f"{frame + 23}:{'down' if fighter['raw'] & bit else 'up'}:{key_names[seat]}")
            raw[seat] = fighter["raw"]

    contacts = []
    for frame, row in collision.items():
        for native_seat, fighter in enumerate(row["chars"]):
            if not fighter["gethit"]:
                continue
            owner = row["chars"][1 - native_seat]
            wave = (contact_is_wave(fighter, owner) if contact_is_wave is not None else
                    any(h["anim"] == 1020 for h in owner["helpers"] or []))
            prefix = "Projectile" if wave else ""
            contacts.append({"frame": frame, "target": native_seat ^ mirror, "source": (1 - native_seat) ^ mirror,
                             "kind": prefix + ("Blocked" if fighter["guarded"] else "Hit"),
                             "damage": fighter["damage"], "targetShake": fighter["hitShake"],
                             "targetLife": fighter["life"], "targetX": fighter["pos"][0], "fall": fighter.get("hitFall")})
    moves = [{**row, "hostSeat": (row["seat"] - 1) ^ mirror, "move": native_moves[row["to"]]}
             for row in changes if row["to"] in native_moves]
    distance = abs(before[0]["chars"][0]["pos"][0] - before[0]["chars"][1]["pos"][0])
    has_guard = any(contact["kind"].endswith("Blocked") for contact in contacts)
    guard = (151 if "crouch-guard" in name else 150) if has_guard else None
    last = 112 if any(move["move"] == MOVE for move in moves) else 79
    captures = (91, 175) if recovery else (26, last)
    scene = Scene(distance / 2, tuple(keys), tuple(moves), tuple(contacts), guard, captures=captures)
    return scene, phases, changes


def runtime_capture(options, scene, output, sprites, *, mode="default", alias=ALIAS, minimum_render_frames=0):
    fixed = options.fps == "fixed"
    args, capture = runtime_args(options, scene, output, sprites, (mode, mode),
                                 visual_alias=lambda _: alias, realtime_tail=getattr(options, "realtime_tail_frames", 72))
    if not fixed:
        args.insert(args.index("--"), "--print-fps")
        if options.disable_vsync:
            args.insert(args.index("--"), "--disable-vsync")
        # Keep down, down-forward and forward distinct even when one render samples multiple logic ticks.
        args = [arg for arg in args if not arg.startswith("--renderkey=")]
        warmup_end = 42 + options.realtime_warmup_frames
        warmup = ("4:down:F2", "9:down:F1", "10:up:F1", f"{warmup_end}:down:F1", f"{warmup_end + 1}:up:F1")
        keys = (*BOOT_KEYS, *warmup)
        first_input = min(int(step.split(":", 1)[0]) for step in scene.keys)
        settle = getattr(options, "realtime_settle_frames", 0)
        for step in scene.keys:
            frame, event, key = step.split(":")
            render = first_input + (int(frame) - first_input) * options.realtime_input_spacing
            keys += (f"{render + 20 + options.realtime_warmup_frames + settle}:{event}:{key}",)
        args.extend(f"--renderkey={key}" for key in keys)
    if fixed:
        args += ["--damage-audit", "--reentry-audit", "--scripted-pause-audit"]
        if minimum_render_frames > capture[1] + 18:
            args = [f"--shotframes={minimum_render_frames}" if arg.startswith("--shotframes=") else arg for arg in args]
    return args, capture


def verify_move_instances(scene, events, moves, reentry, *, fixed, fighter_id="TerryPowerWave"):
    started = [event for event in events if event["kind"] == "MoveStarted"]
    expected = sorted(scene.moves, key=lambda row: (row["frame"], row["hostSeat"]))
    started.sort(key=lambda row: (row["frame"], row["source"]))
    assert_equal([(move["hostSeat"], move["move"]) for move in expected],
                 [(event["source"], event["move"]) for event in started], "native entry order")
    if not fixed:
        return started
    assert_equal({fighter_id}, {row["id"] for row in moves.values()}, "actual profile")
    for event, native in zip(started, expected, strict=True):
        logic, seat = event["frame"], event["source"]
        assert_equal(native["frame"] + 14, logic, "native-to-Godot entry timing")
        assert_equal((str(logic), "-1", "-1"), tuple(reentry[seat, logic][key] for key in ("started", "hitConfirm", "blockConfirm")),
                     f"fresh instance at logic {logic}")
        assert_equal((event["move"], "0", "Startup"), tuple(moves[seat, logic][key] for key in ("move", "elapsed", "state")),
                     f"fresh move clock at logic {logic}")
        if native["from"] in (200, 205, 400):
            previous = moves[seat, logic - 1]
            assert_equal(native["stateTime"], int(previous["elapsed"]) + 1, f"old action elapsed at logic {logic}")
    return started


def fighter_poses(scene, events, committed, timelines, modes, *, actions=ACTIONS):
    starts = [event for event in events if event["kind"] == "MoveStarted"]
    windows = []
    for index, event in enumerate(starts):
        move, seat = event["move"], event["source"]
        action = actions.get(move, 1000 if move == MOVE else (211 if modes[seat] == "mode2" else 210))
        begin = finish = event["frame"] + 1
        next_start = next((row["frame"] + 1 for row in starts[index + 1:] if row["source"] == seat), None)
        clock = []
        while finish != next_start and committed[seat, finish]["move"] == move:
            row = committed[seat, finish]
            tick = int(row["tick"])
            assert_equal(0 if not clock else clock[-1] + (row["frozen"] == "False"), tick,
                         f"Action {action} clock at state {finish}")
            assert_equal(timelines[action][tick], tuple(row[key] for key in POSE_KEYS), f"Action {action} at state {finish}")
            clock.append(tick)
            finish += 1
        if not clock:
            raise ValueError(f"missing Action {action} initial pose at state {begin}")
        if finish != next_start and committed[seat, finish]["state"] == "Standing":
            assert_equal(len(timelines[action]) - 1, clock[-1], f"Action {action} final pose")
        windows.append({"seat": seat, "move": move, "action": action, "begin": begin, "end": finish, "clock": clock})
    return windows


def verify_entities(lines, phases, starts, moves, *, fixed, strict_native=True, allow_interrupted_spawn=False,
                    entity_position_tolerance=0.005):
    records = defaultdict(list)
    for row in audit_rows(lines, "WaveEntityAudit"):
        records[int(row["id"])].append({**row, "age": 25 - int(row["life"])})
    if not fixed:
        return records
    scheduled = [event for event in starts if event["move"] == MOVE]
    if allow_interrupted_spawn:
        mirror = phases["before-command"][0]["chars"][0]["facing"] < 0
        uninterrupted = []
        for event in scheduled:
            start, seat = event["frame"], event["source"]
            if any(int(row["owner"]) == seat and int(row["logic"]) == start + 15
                   for row in (samples[0] for samples in records.values())):
                uninterrupted.append(event)
                continue
            if all(moves[seat, tick]["move"] == MOVE for tick in range(start, start + 16)):
                raise ValueError(f"uninterrupted Wave did not spawn at logic {start + 15}")
            seat ^= mirror
            source_frame = start - 14
            source = phases["end"][source_frame]["chars"][seat]
            assert_equal(1000, source["state"], "native interrupted Wave entered State 1000")
            assert_equal(False, any(
                h["anim"] == 1020 and h["time"] == 1 for tick in range(source_frame, source_frame + 18)
                for h in (phases["end"][tick]["chars"][seat]["helpers"] or [])),
                "native interrupted Wave did not spawn")
        scheduled = uninterrupted
    assert_equal(len(scheduled), len(records), "spawned Wave identity count")
    for entity_id, samples in records.items():
        first, last = samples[0], samples[-1]
        owner, origin = int(first["owner"]), int(first["logic"])
        start = next((event for event in scheduled if event["source"] == owner and event["frame"] + 15 == origin), None)
        if start is None:
            raise ValueError(f"unmatched Wave {entity_id} at logic {origin} for seat {owner}")
        assert_equal((start["frame"] + 15, 0, 24), (origin, first["age"], last["age"]), f"entity {entity_id} lifetime")
        facing = -1 if first["left"] == "True" else 1
        owner_start = moves[owner, origin]
        assert_equal(int(owner_start["x"]) + facing * 163840, int(first["x"]), f"entity {entity_id} initial motion")
        mirror = phases["before-command"][0]["chars"][0]["facing"] < 0
        for index, row in enumerate(samples):
            logic = int(row["logic"])
            age = row["age"]
            assert_equal(first["left"], row["left"], f"entity {entity_id} retained spawn facing")
            assert_equal(facing * 163840 if age < 11 else 0, int(row["vx"]), f"entity {entity_id} velocity at age {age}")
            assert_equal(int(first["x"]) + facing * 163840 * min(age, 10), int(row["x"]), f"entity {entity_id} X at age {age}")
            assert_equal(int(first["y"]), int(row["y"]), f"entity {entity_id} Y at age {age}")
            if index:
                assert_equal(1, logic - int(samples[index - 1]["logic"]), f"entity {entity_id} audit continuity")
                if age - samples[index - 1]["age"] not in (0, 1):
                    raise ValueError(f"entity {entity_id} skipped local tick at logic {logic}")
            if strict_native and logic - 14 in phases["end"]:
                native = phases["end"][logic - 14]["chars"][owner ^ mirror]["helpers"] or []
                helpers = [item for item in native if item["anim"] == 1020 and not item.get("destroyed")]
                if not helpers:
                    raise ValueError(f"entity {entity_id} missing native committed counterpart at logic {logic}")
                source = helpers[0]
                assert_equal(int(source["facing"]), facing, f"entity {entity_id} native facing")
                assert_equal(source["time"], age + 1, f"entity {entity_id} native local age at logic {logic}")
                if abs(source["pos"][0] - int(row["x"]) / Q16) > entity_position_tolerance:
                    raise ValueError(f"entity {entity_id} native X differs at logic {logic}: {source['pos']}, {row}")
        if strict_native:
            expiry = int(last["logic"]) - 13
            if expiry in phases["end"]:
                native = phases["end"][expiry]["chars"][owner ^ mirror]["helpers"] or []
                # Primary traces predate the destroyed flag. The terminal controller leaves an age-26 record.
                assert_equal([], [item for item in native if item["anim"] == 1020
                                  and not item.get("destroyed", item["time"] == 26)],
                             f"entity {entity_id} native disposal at frame {expiry}")
    return records


def verify_contacts(scene, events, moves, damage, profile, *, known_second_light=None, strict_native,
                    guard_position_tolerance=0.002):
    event_kinds = ("Hit", "Blocked", "ProjectileHit", "ProjectileBlocked")
    contacts = sorted((event for event in events if event["kind"] in event_kinds), key=lambda row: (row["frame"], row["target"]))
    native_contacts = sorted(scene.contacts, key=lambda row: (row["frame"], row["target"]))
    divergences = []
    if known_second_light:
        # Frozen Stage 18 uses a generic reaction rectangle and does not re-block an independent ungrouped light.
        assert_equal(1, len(native_contacts), "known second-light native contact count")
        assert_equal(2, len(contacts), "known second-light Godot contact count")
        extra = contacts.pop()
        assert_equal((40, "Hit", 0, 1, NATIVE_MOVES[200], 44),
                     tuple(extra[key] for key in ("frame", "kind", "source", "target", "move", "amount")),
                     "known second-light compatibility difference")
        assert_equal(known_second_light, moves[1, 39]["state"], "existing target reaction before second light")
        assert_equal(44, int(moves[1, 39]["health"]) - int(moves[1, 40]["health"]), "second light committed HP")
        divergences.append({"native": "no second light contact", "godot": extra, "priorReaction": known_second_light})
    assert_equal([(row["kind"], row["source"], row["target"]) for row in native_contacts],
                 [(row["kind"], row["source"], row["target"]) for row in contacts], "native contact kinds and ownership")
    rules = profile["damageRules"]
    for actual, native in zip(contacts, native_contacts, strict=True):
        logic, owner, target = actual["frame"], actual["source"], actual["target"]
        if strict_native:
            assert_equal(native["frame"] + 14, logic, "native contact tick")
        if not actual["kind"].startswith("Projectile"):
            continue
        assert_equal(ENTITY, actual["move"], "contact identity")
        assert_equal(native["targetShake"], int(moves[target, logic]["stop"]), "remote HitPause")
        before, after = int(moves[target, logic - 1]["health"]), int(moves[target, logic]["health"])
        if actual["kind"] == "ProjectileBlocked":
            assert_equal(native["damage"], before - after, "native chip damage")
            actual_x = int(moves[target, logic]["x"]) / Q16
            if abs(native["targetX"] - actual_x) > guard_position_tolerance:
                raise ValueError(f"native Wave guard position: expected {native['targetX']}, got {actual_x}")
            continue
        operands = damage[owner, logic]
        counter, variance = int(operands["counter"]), int(operands["variance"])
        basis = (14 if counter else 12) - variance
        expected = int(f32(f32(f32(basis) * f32(ratio(rules, "baseScale"))) * f32(ratio(rules, "attackMultiplier"))))
        if moves[target, logic - 1]["state"] in HIT_REACTIONS:
            expected = int(expected * ratio(rules, "hitstunDefenseMultiplier") + 0.5)
        assert_equal(min(before, expected), actual["amount"], "Wave actual dynamic damage")
        assert_equal(actual["amount"], before - after, "Wave atomic HP commit")
    return divergences


def verify_grounded_wave_recovery(scene, phases, moves, *, position_tolerance=0.005, crouch_recovery=False):
    samples = []
    mirror = phases["before-command"][0]["chars"][0]["facing"] < 0
    for contact in scene.contacts:
        if contact["kind"] == "ProjectileBlocked":
            expected_state = "Blockstun"
        elif contact["kind"] == "ProjectileHit" and not contact["fall"]:
            expected_state = "Hitstun"
        else:
            continue
        target = contact["target"]
        for frame, world in phases["end"].items():
            if frame < contact["frame"]:
                continue
            source = world["chars"][target ^ mirror]
            host = moves[target, frame + 14]
            recovered_state = "Crouching" if crouch_recovery and source.get("stateType") == 2 else "Standing"
            assert_equal(recovered_state if source["ctrl"] else expected_state, host["state"],
                         f"grounded Wave recovery at native F{frame}")
            if abs(source["pos"][0] - int(host["x"]) / Q16) > position_tolerance:
                raise ValueError(f"grounded Wave recovery X differs at native F{frame}")
            samples.append({"frame": frame, "state": host["state"], "sourceState": source["state"],
                            "x": int(host["x"]) / Q16})
            if source["ctrl"]:
                break
        else:
            raise ValueError("native trace ends before grounded Wave recovery")
    return samples


def verify_pause(phases, lines, moves, damage, entities, *, strict_native, allow_inactive=False):
    rows = audit_rows(lines, "ScriptedPauseAudit")
    if not rows:
        if allow_inactive and not entities and not any(row.get("globalPause") for row in phases["end"].values()):
            return rows
        raise ValueError("missing opt-in scripted Pause trace")
    if strict_native and "globalPause" in phases["end"][0]:
        expected = [frame + 14 for frame, row in phases["end"].items() if row["globalPause"] == 1]
        final = max(phases["end"]) + 14
        actual = [int(row["logic"]) for row in rows if int(row["logic"]) <= final and row["active"] == "True"]
        assert_equal(expected, actual, "native global Pause frames")
    entity_frames = {int(row["logic"]): [] for row in rows}
    for samples in entities.values():
        for sample in samples:
            entity_frames[int(sample["logic"])].append({key: value for key, value in sample.items() if key != "logic"})
    for row in rows:
        if row["active"] != "True":
            continue
        logic = int(row["logic"])
        assert_equal(entity_frames[logic - 1], entity_frames[logic], f"entities during global Pause {logic}")
        for table in (moves, damage):
            for seat in (0, 1):
                before, after = table[seat, logic - 1], table[seat, logic]
                assert_equal({**before, "logic": after["logic"]}, after, f"seat {seat} during global Pause {logic}")
    return rows


def verify_rendered_entities(lines, captures, records, timeline):
    rendered = defaultdict(dict)
    for line in lines:
        if not line.startswith("[ProjectileRenderAudit] "):
            continue
        fields = [item.split("=", 1) for item in line.split()[1:] if "=" in item]
        selected = next(value for key, value in fields if key == "sprite")
        row = {**dict(fields), "sprite_selected": selected}
        rendered[int(row["state"])][int(row["id"])] = row
    authority_by_state = defaultdict(dict)
    for entity, samples in records.items():
        for sample in samples:
            authority_by_state[int(sample["logic"]) + 1][entity] = sample
    for image in captures:
        state = image["state"]
        visual = rendered[state]
        if records:
            authority = authority_by_state.get(state, {})
            assert_equal(set(authority), set(visual), f"entity set in FramePostDraw state {state}")
            for entity, row in visual.items():
                expected = authority[entity]
                assert_equal((expected["owner"], expected["left"], expected["x"], expected["y"], str(expected["age"])),
                             tuple(row[key] for key in ("owner", "left", "x", "y", "age")), f"entity pose state {state}")
        for row in visual.values():
            assert_equal("True", row["sprite_selected"], f"entity sprite at state {state}")
            assert_equal(timeline[int(row["age"])], tuple(row[key] for key in POSE_KEYS), f"Action 1020 at state {state}")
        image["entities"] = list(visual.values())
    if records and not any(image["entities"] for image in captures) and captures[0]["state"] <= max(authority_by_state):
        raise ValueError("no actual Wave image in the capture window")


def verify_recovery(phases, lines, moves, captures, *, mirrored):
    native_states = {5000: "Shake", 5030: "Lift", 5035: "Rise", 5050: "Fall", 5100: "Impact",
                     5101: "Bounce", 5120: "Wakeup"}
    seat = int(not mirrored)
    launch = indexed(audit_rows(lines, "LaunchReactionAudit"))
    assert_equal({seat}, {int(row["fighter"]) for row in launch.values()}, "launch reaction victim")
    captured = {image["state"]: image["fighters"][seat] for image in captures}
    samples = []
    first_contact = next(frame for frame, world in phases["after-collision"].items()
                         if world["chars"][1]["gethit"] and world["chars"][1]["hitFall"])
    native_anchor = phases["end"][first_contact]["chars"][1]["pos"][0]
    host_anchor = int(moves[seat, first_contact + 14]["x"]) / Q16
    for frame, world in phases["end"].items():
        source = world["chars"][1]
        if frame < first_contact:
            phase = None
        elif source["state"] == 5110:
            phase = "LyingImpact" if source["anim"] == 5170 else "Lying"
        else:
            phase = native_states.get(source["state"])
        logic = frame + 14
        row = launch.get((seat, logic))
        assert_equal(phase, None if row is None else row["phase"], f"launch phase at native F{frame}")
        if phase is None:
            continue
        actual = moves[seat, logic]
        # The Godot floor is authoritative. Native crossing can transiently be below its floor before 5100.
        # The earlier light-hit push rounds in Q16. Compare the Wave launch displacement from its actual contact.
        expected_x = host_anchor + source["pos"][0] - native_anchor
        expected_y = max(0, -source["pos"][1])
        for key, expected in (("x", expected_x), ("y", expected_y)):
            if abs(expected - int(actual[key]) / Q16) > 0.005:
                raise ValueError(f"launch {key.upper()} at native F{frame}: expected {expected}, got {int(actual[key]) / Q16}")
        if logic + 1 in captured:
            sprite = captured[logic + 1]
            assert_equal(str(LAUNCH_ACTIONS[phase]), sprite["action"], f"launch image Action at F{frame}")
            if source["anim"] == LAUNCH_ACTIONS[phase] and source["animTime"] != 0:
                assert_equal(str(source["elem"]), sprite["frame"], f"launch image element at F{frame}")
        samples.append({"frame": frame, "phase": phase, "sourceAction": source["anim"],
                        "sourceX": source["pos"][0], "sourceY": source["pos"][1],
                        "hostX": int(actual["x"]) / Q16, "hostY": int(actual["y"]) / Q16})
    assert_equal(set(LAUNCH_ACTIONS), {sample["phase"] for sample in samples}, "complete launch recovery phases")
    last = samples[-1]["frame"] + 15
    assert_equal("Standing", moves[seat, last]["state"], "launch recovery returns control")
    assert_equal("0", captured[last + 1]["action"], "launch wakeup returns to actual standing image")
    return samples


def run_case(options, name, reference, timelines, profile, *, mode="default", alias=ALIAS,
             prepared_scene=None, fixed_checks=None, contact_check=verify_contacts,
             recovery_check=verify_grounded_wave_recovery, minimum_render_frames=0, partial_capture=False,
             pose_check=fighter_poses, allow_interrupted_spawn=False, entity_position_tolerance=0.005,
             extra_arguments=(), extra_authority=()):
    scene, phases, changes = prepared_scene or native_case(reference, name, recovery=options.recovery)
    fixed = options.fps == "fixed"
    logs = []
    for variant in ("sprite", "plain") if options.compare_plain else ("sprite",):
        output = options.output / name / variant
        output.mkdir(parents=True)
        sprites = variant == "sprite"
        args, capture = runtime_capture(options, scene, output, sprites, mode=mode, alias=alias,
                                        minimum_render_frames=minimum_render_frames)
        args.extend(extra_arguments)
        log = run_godot(options.godot, args, output / "duel.log", output / "end.png",
                        timeout=getattr(options, "timeout", 50))
        lines = log.decode("utf-8").splitlines()
        events = battle_events(lines)
        moves = indexed(audit_rows(lines, "MoveAudit"))
        reentry = indexed(audit_rows(lines, "ReentryAudit"))
        damage = indexed(audit_rows(lines, "DamageAudit"))
        started = verify_move_instances(scene, events, moves, reentry, fixed=fixed,
                                        fighter_id=profile["fighterId"])
        strict_native = True
        entities = verify_entities(lines, phases, started, moves, fixed=fixed, strict_native=strict_native,
                                   allow_interrupted_spawn=allow_interrupted_spawn,
                                   entity_position_tolerance=entity_position_tolerance)
        pause = []
        divergences = []
        ground_recovery = []
        if fixed:
            verify_scripted_keys(lines, BOOT_KEYS + scene.keys)
            known_second_light = {"hit-23": "Hitstun", "guard-23": "Blockstun"}.get(name)
            divergences = contact_check(scene, events, moves, damage, profile,
                                         known_second_light=known_second_light, strict_native=strict_native)
            ground_recovery = recovery_check(scene, phases, moves)
            pause = verify_pause(phases, lines, moves, damage, entities, strict_native=strict_native, allow_inactive=mode == "mode2")
            additional = fixed_checks(scene, phases, events, moves, reentry, lines) if fixed_checks else {}
        warning_states = []
        last_logic = -1
        for line in lines:
            if match := VISUAL_FRAME.match(line):
                last_logic = int(match[1])
            if line.startswith("WARNING: [BattleScene] 帧积压"):
                warning_states.append(last_logic)
        if options.require_no_backlog and warning_states:
            raise ValueError("realtime capture exceeded the battle time budget")
        if sprites:
            captures_require_full_moves = fixed and not (options.recovery or partial_capture)
            capture_scene = replace(scene, guard_action=None) if options.recovery or partial_capture else scene
            windows, images = validate_captures(capture_scene, events, lines, output, capture, timelines, (mode, mode), captures_require_full_moves,
                                                move_validator=pose_check, check_reactions=False)
            verify_rendered_entities(lines, images, entities, timelines[1020])
            if not fixed and not any(image["entities"] for image in images):
                raise ValueError("no realtime Wave image in the capture window")
            if not fixed:
                ages = defaultdict(list)
                for image in images:
                    for row in image["entities"]:
                        ages[row["id"]].append(int(row["age"]))
                if any(ticks != sorted(ticks) for ticks in ages.values()):
                    raise ValueError("realtime Wave presentation rewound")
                if options.require_clean_sample and any(started[0]["frame"] <= state < images[-1]["state"] for state in warning_states):
                    raise ValueError("battle scheduler reported backlog during the sampled action")
            result = {"native": str(reference), "nativeChanges": changes, "nativeContacts": scene.contacts, "inputs": scene.keys,
                      "events": events, "entities": entities, "pause": pause, "windows": windows, "captures": images,
                      "backlogAfterLogic": warning_states, "compatibility": divergences,
                      "groundRecovery": ground_recovery}
            if not fixed:
                result["rendererFps"] = [line for line in lines if line.startswith("Project FPS:")]
            if options.recovery:
                result["recovery"] = verify_recovery(phases, lines, moves, images, mirrored=name.startswith("mirror-"))
            if fixed:
                result.update(additional)
            if scene.differences:
                result["sourceDifferences"] = scene.differences
            (output / "trace.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
        logs.append([line for line in log.splitlines() if line.startswith((*AUTHORITY, *extra_authority))])
    if options.compare_plain:
        require_same(*logs, f"{name} visual/plain authority")
    if fixed:
        print(f"{name}: {len(started)} instances, {len(entities)} waves, {len(logs[0])} authority rows", flush=True)
    else:
        print(f"{name}: {len(started)} instances, {len(ages)} rendered waves, {len(images)} captured frames", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--godot", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path, help="Fresh output directory")
    parser.add_argument("--reference", required=True, type=Path, action="append")
    parser.add_argument("--profile-dir", default="data/imported-characters/terry98-stage19-data")
    parser.add_argument("--scene", required=True, action="append")
    parser.add_argument("--fps", choices=("fixed", "30", "60"), default="fixed")
    parser.add_argument("--realtime-window", nargs=2, type=int, metavar=("FIRST", "LAST"))
    parser.add_argument("--realtime-resolution", default="640x360")
    parser.add_argument("--realtime-warmup-frames", type=int, default=0, help="Extra paused render frames before inputs")
    parser.add_argument("--realtime-input-spacing", type=int, choices=(1, 2), default=1,
                        help="Multiply render spacing of realtime input transitions")
    parser.add_argument("--disable-vsync", action="store_true", help="Let the realtime FPS cap govern cadence")
    parser.add_argument("--require-no-backlog", action="store_true")
    parser.add_argument("--require-clean-sample", action="store_true",
                        help="Reject a scheduler warning from move start through the last captured render")
    parser.add_argument("--compare-plain", action="store_true")
    parser.add_argument("--recovery", action="store_true", help="Check the extended native launch through wakeup")
    options = parser.parse_args()
    options.output = options.output.resolve()
    if options.fps != "fixed" and options.compare_plain:
        parser.error("authority comparison requires fixed mode")
    if options.recovery and options.fps != "fixed":
        parser.error("--recovery requires fixed mode")
    if not 0 <= options.realtime_warmup_frames <= 300:
        parser.error("--realtime-warmup-frames must be between 0 and 300")
    if options.fps == "fixed" and (options.realtime_warmup_frames or options.realtime_input_spacing != 1):
        parser.error("warmup and input spacing options require realtime mode")
    if options.realtime_window is not None:
        begin, end = options.realtime_window
        if options.fps == "fixed" or not 0 < begin <= end or end - begin >= 90:
            parser.error("--realtime-window requires a realtime range of 1 to 90 captures")
    try:
        options.output.mkdir(parents=True, exist_ok=False)
        manifest = json.loads((ROOT / "data/imported-characters/terry98/manifest.json").read_text(encoding="utf-8-sig"))
        timelines = {action: visual_timeline(manifest, action) for action in (200, 205, 210, 215, 1000, 1020)}
        profile = json.loads((ROOT / options.profile_dir / "default.fighter.json").read_text(encoding="utf-8-sig"))
        failed = []
        for name in options.scene:
            file = f"default-special-{name}.jsonl"
            source = next((directory / file for directory in options.reference if (directory / file).is_file()), options.reference[0] / file)
            try:
                run_case(options, name, source, timelines, profile)
            except (OSError, ValueError, KeyError, IndexError, TimeoutExpired) as error:
                print(f"{name}: FAIL {error!r}", flush=True)
                failed.append(name)
        if failed:
            raise ValueError("failed fixtures: " + ", ".join(failed))
    except (OSError, ValueError) as error:
        parser.exit(1, f"Stage 19 validation failed: {error}\n")


if __name__ == "__main__":
    main()
