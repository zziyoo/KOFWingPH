"""Verify Terry light-normal move instances and their Vulkan frames against native inputs."""

import argparse
from collections import defaultdict
from dataclasses import dataclass, replace
import json
from pathlib import Path
import sys
from subprocess import TimeoutExpired

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Stage17Validation"))
from damage_duel import (DAMAGE_AUTHORITY, BOOT_KEYS, HEAVY, LIGHT, NEAR_HEAVY, NEAR_LIGHT, ROOT, audit_rows,
                         battle_events, require_same, run_godot, runtime_args, validate_captures,
                         verify_scripted_keys, visual_timeline)
from compare_damage import HIT_REACTIONS, committed_damage, f32, ratio, raw_damage


MODES = {"default": "TerryReentry", "mode2": "TerryReentryMode2"}
MOVES = {200: LIGHT, 205: NEAR_LIGHT, 210: HEAVY, 215: NEAR_HEAVY}
ACTIONS = {LIGHT: 200, NEAR_LIGHT: 205, NEAR_HEAVY: 215}
RAW_KEYS = ((1, "W", "Up"), (2, "S", "Down"), (4, "A", "Left"), (8, "D", "Right"),
            (128, "J", "Comma"), (256, "K", "Period"))
REENTRY_AUTHORITY = (*DAMAGE_AUTHORITY, b"[ReentryAudit] ")


@dataclass(frozen=True)
class Scene:
    half_distance: float
    keys: tuple[str, ...]
    moves: tuple[dict, ...]
    contacts: tuple[dict, ...]
    guard_action: int | None
    captures: tuple[int, int] = (20, 85)
    differences: tuple[str, ...] = ()
    source_has_other_attack: bool = False


def native_scene(reference, name):
    phases = defaultdict(dict)
    changes = []
    for row in map(json.loads, reference.read_text(encoding="utf-8").splitlines()):
        if row["phase"] in ("before-command", "after-collision", "end"):
            phases[row["phase"]][row["frame"]] = row["chars"]
        elif row["phase"] == "state-change":
            changes.append(row)
    before, collision, end = (phases[key] for key in ("before-command", "after-collision", "end"))
    if max(end, default=-1) != 75 or set(before) != set(range(76)):
        raise ValueError(f"native phase log incomplete: {reference}")

    mirrored = name.startswith("mirror-")
    keys = []
    raw = [0, 0]
    for frame, pair in before.items():
        for source_seat, fighter in enumerate(pair):
            seat = source_seat ^ mirrored
            current = fighter["raw"]
            if current & ~sum(bit for bit, _, _ in RAW_KEYS):
                raise ValueError(f"unsupported native input bits at F{frame}: {current}")
            for bit, *key_names in RAW_KEYS:
                if (current ^ raw[seat]) & bit:
                    direction = "down" if current & bit else "up"
                    keys.append(f"{frame + 23}:{direction}:{key_names[seat]}")
            raw[seat] = current

    moves = []
    for change in changes:
        if change["to"] in MOVES:
            moves.append({**change, "seat": (change["seat"] - 1) ^ mirrored, "move": MOVES[change["to"]]})
    if not moves:
        raise ValueError(f"native log contains no normal entry: {reference}")

    contacts = []
    for frame, pair in collision.items():
        for seat, fighter in enumerate(pair):
            if fighter["gethit"]:
                native_start = next(move for move in reversed(moves) if move["seat"] == ((1 - seat) ^ mirrored)
                                    and move["frame"] <= frame)
                contacts.append({"frame": frame, "source": (1 - seat) ^ mirrored, "target": seat ^ mirrored,
                                 "kind": "Blocked" if fighter["guarded"] else "Hit", "move": native_start["move"],
                                 "instance": native_start["frame"], "attackerPause": end[frame][1 - seat]["hitPause"],
                                 "targetShake": fighter["hitShake"]})
    distance = abs(before[0][0]["pos"][0] - before[0][1]["pos"][0])
    has_guard = any(contact["kind"] == "Blocked" for contact in contacts)
    guard = (151 if "crouch" in name else 150) if has_guard else None
    outside = any(change["to"] == 400 for change in changes)
    return Scene(distance / 2, tuple(keys), tuple(moves), tuple(contacts), guard, source_has_other_attack=outside)


def contact_key(contact, origin, *, include_time=True):
    result = (contact["source"], contact["target"], contact["kind"], contact["move"])
    return (contact["frame"] - origin, *result) if include_time else result


def adapted_scene(scene, name):
    if "approach-" in name:
        # Preserve the approach with grounded movement; directional jump kinematics are not a Stage 18 rule.
        mirrored = name.startswith("mirror-")
        forward = "D" if mirrored else "Left"
        down = "S" if mirrored else "Down"
        keys = tuple(step for step in scene.keys if not step.startswith(("23:", "24:")))
        # Start far, walk across the near edge during the old move, and crouch under its active rectangle.
        approach = (f"24:down:{forward}", f"38:up:{forward}", f"38:down:{down}", f"43:up:{down}")
        return replace(scene, keys=keys + approach, differences=("grounded-approach", "air-reaction"))
    if name in ("near-hit-23", "boundary-46-23", "boundary-46p5-23", "mirror-near-hit-23"):
        # Stage17 uses a generic target Hitstun rectangle, while Terry's source animates reaction Clsn2.
        return replace(scene, differences=("reaction-contact-tick",))
    if name in ("far-hit-21", "far-hit-22", "far-hit-23"):
        return replace(scene, differences=("generic-hitstun-reach",))
    if name == "near-guard-23":
        return replace(scene, differences=("distinct-instance-reblock",))
    if scene.source_has_other_attack:
        return replace(scene, differences=("source-crouch-normal-outside-scope",))
    return scene


def indexed(rows):
    return {(int(row["fighter"]), int(row["logic"])): row for row in rows}


def assert_equal(expected, actual, label):
    if expected != actual:
        raise ValueError(f"{label}: expected {expected!r}, got {actual!r}")


def validate_instances(scene, events, reentry, moves, damage, profiles, *, fixed):
    starts = [row for row in events if row["kind"] == "MoveStarted"]
    contacts = [row for row in events if row["kind"] in ("Hit", "Blocked")]
    assert_equal([(row["seat"], row["move"]) for row in scene.moves], [(row["source"], row["move"]) for row in starts],
                 "new move entries")
    native_origin, host_origin = scene.moves[0]["frame"], starts[0]["frame"]
    if fixed:
        assert_equal(native_origin + 14, host_origin, "fixed input/first move alignment")
        assert_equal([row["frame"] - native_origin for row in scene.moves], [row["frame"] - host_origin for row in starts],
                     "move entry clocks")
        if "air-reaction" in scene.differences:
            if any(row["frame"] < starts[-1]["frame"] for row in contacts):
                raise ValueError(f"pre-entry attack should whiff during the defender approach: {contacts}")
            assert_equal([contact_key(row, native_origin, include_time=False) for row in scene.contacts],
                         [contact_key(row, host_origin, include_time=False) for row in contacts], "grounded approach contact results")
        else:
            expected_keys = [contact_key(row, native_origin, include_time=False) for row in scene.contacts]
            if "generic-hitstun-reach" in scene.differences:
                seat = starts[-1]["source"]
                expected_keys.append((seat, 1 - seat, "Hit", LIGHT))
            if "distinct-instance-reblock" in scene.differences:
                source, target, _, move = expected_keys[-1]
                expected_keys[-1] = (source, target, "Hit", move)
            assert_equal(expected_keys, [contact_key(row, host_origin, include_time=False) for row in contacts], "contact results")
            for expected, actual in zip(scene.contacts, contacts):
                offset = (actual["frame"] - host_origin) - (expected["frame"] - native_origin)
                allowed_offsets = (0, -1) if "reaction-contact-tick" in scene.differences and expected["instance"] != native_origin else (0,)
                if offset not in allowed_offsets:
                    raise ValueError(f"contact logic offset {offset} outside {allowed_offsets}: {actual}")
            if "generic-hitstun-reach" in scene.differences or "distinct-instance-reblock" in scene.differences:
                assert_equal(starts[-1]["frame"] + 3, contacts[-1]["frame"], "fresh instance generic target contact")
    else:
        if len(starts) == 2 and not 7 <= starts[-1]["frame"] - host_origin <= 11:
            raise ValueError(f"realtime retry missed the explicit recovery window: {starts}")

        def ordered_outcomes(rows):
            return [(row["source"], row["target"], row["kind"], row["move"]) for row in rows]

        assert_equal(ordered_outcomes(scene.contacts), ordered_outcomes(contacts), "realtime contacts")
        return starts

    assert_equal(set(moves), set(reentry), "move audit/reentry audit coverage")
    assert_equal(set(moves), set(damage), "move audit/damage audit coverage")
    for seat, profile in enumerate(profiles):
        assert_equal({profile["fighterId"]}, {row["id"] for (index, _), row in moves.items() if index == seat},
                     f"seat {seat} selected profile")
    actives = [row for row in events if row["kind"] == "AttackActive"]
    assert_equal([(start["source"], start["move"]) for start in starts],
                 [(active["source"], active["move"]) for active in actives], "one first active event per instance")
    for active in actives:
        if active["move"] in (LIGHT, NEAR_LIGHT):
            assert_equal("3", moves[active["source"], active["frame"]]["elapsed"],
                         f"logic {active['frame']} light first active tick")
    for start, native in zip(starts, scene.moves, strict=True):
        seat, frame, move = start["source"], start["frame"], start["move"]
        before, after = moves[seat, frame - 1], moves[seat, frame]
        instance = reentry[seat, frame]
        assert_equal((str(frame), "-1", "-1"), tuple(instance[k] for k in ("started", "hitConfirm", "blockConfirm")),
                     f"logic {frame} new instance/confirmation reset")
        assert_equal((move, "0", "Startup"), tuple(after[k] for k in ("move", "elapsed", "state")),
                     f"logic {frame} new instance initial state")
        if before["move"] != "-":
            assert_equal("0", before["stop"], f"logic {frame} reentry after freeze")
            assert_equal(native["stateTime"], int(before["elapsed"]) + 1, f"logic {frame} old instance effective tick")
        assert_equal(("1", "0"), (damage[seat, frame]["divisor"], damage[seat, frame]["pause"]),
                     f"logic {frame} new instance local damage state")

    seen = set()
    for contact in contacts:
        frame, seat, target = contact["frame"], contact["source"], contact["target"]
        started = next(start["frame"] for start in reversed(starts) if start["source"] == seat and start["frame"] <= frame)
        source, defender = reentry[seat, frame], reentry[target, frame]
        assert_equal(str(started), source["started"], f"logic {frame} contact source instance")
        assert_equal(str(started), defender["lastHitStart"], f"logic {frame} defender dedupe instance")
        if (seat, started) in seen:
            raise ValueError(f"duplicate contact at logic {frame}: {contact}")
        if contact["move"] in (LIGHT, NEAR_LIGHT):
            assert_equal("0", defender["lastHitGroups"], f"logic {frame} ungrouped light contact")
        seen.add((seat, started))
        confirm = "blockConfirm" if contact["kind"] == "Blocked" else "hitConfirm"
        assert_equal(str(frame), source[confirm], f"logic {frame} contact confirmation")
        adapted_contact = started == starts[-1]["frame"] and any(
            label in scene.differences for label in ("generic-hitstun-reach", "distinct-instance-reblock"))
        if "air-reaction" not in scene.differences and not adapted_contact:
            expected = next(row for row in scene.contacts if contact_key(row, native_origin, include_time=False)
                            == contact_key(contact, host_origin, include_time=False)
                            and row["instance"] - native_origin == started - host_origin)
            assert_equal(expected["attackerPause"], int(moves[seat, frame]["stop"]), f"logic {frame} self pause")
            assert_equal(expected["targetShake"], int(moves[target, frame]["stop"]), f"logic {frame} target shake")
        prior_hp = int(moves[target, frame - 1]["health"])
        move = next(item for item in profiles[seat]["moves"] if item["moveId"] == contact["move"])
        if len(move["attacks"]) == 1:
            attack = move["attacks"][0]
            formula = attack["dynamicDamage"]
            if adapted_contact:
                expected_target_state = "Blockstun" if "distinct-instance-reblock" in scene.differences else "Hitstun"
                assert_equal(expected_target_state, moves[target, frame - 1]["state"], f"logic {frame} adapted contact target")
                pause = attack["hitstopByOutcome"]["hit"]
                assert_equal(pause["attackerFrames"], int(moves[seat, frame]["stop"]), f"logic {frame} self pause")
                assert_equal(pause["defenderFrames"], int(moves[target, frame]["stop"]), f"logic {frame} target shake")
            operands = damage[seat, frame]
            rules = profiles[seat]["damageRules"]
            target_rules = profiles[target]["damageRules"]
            defense = ratio(target_rules, "defenseMultiplier")
            if moves[target, frame - 1]["state"] in HIT_REACTIONS:
                defense = f32(defense / f32(ratio(target_rules, "hitstunDefenseMultiplier")))
            raw = raw_damage(formula, int(operands["variance"]), int(operands["counter"]),
                             int(operands["divisor"]), ratio(rules, "baseScale"))
            amount = 0 if contact["kind"] == "Blocked" else committed_damage(raw, ratio(rules, "attackMultiplier"), defense, prior_hp)
            assert_equal(amount, contact["amount"], f"logic {frame} dynamic damage")
        assert_equal(prior_hp - contact["amount"], int(moves[target, frame]["health"]), f"logic {frame} atomic life")
    return starts


def validate_poses(_scene, events, committed, timelines, modes):
    starts = [row for row in events if row["kind"] == "MoveStarted"]
    windows = []
    for index, start in enumerate(starts):
        seat, move = start["source"], start["move"]
        action = ACTIONS.get(move, 211 if modes[seat] == "mode2" else 210)
        begin = finish = start["frame"] + 1
        next_start = next((row["frame"] + 1 for row in starts[index + 1:] if row["source"] == seat), None)
        clock = []
        while finish != next_start and committed[seat, finish]["move"] == move:
            row = committed[seat, finish]
            tick = int(row["tick"])
            if not 0 <= tick < len(timelines[action]):
                raise ValueError(f"Action {action} clock outside Manifest at state {finish}: {row}")
            assert_equal(timelines[action][tick], tuple(row[key] for key in ("action", "frame", "sprite", "sprite_index", "duration")),
                         f"Action {action} Manifest pose at state {finish}")
            expected = 0 if not clock else clock[-1] + (row["frozen"] == "False")
            assert_equal(expected, tick, f"Action {action} clock at state {finish}")
            clock.append(tick)
            finish += 1
        if not clock:
            raise ValueError(f"missing first Action {action} pose at state {begin}")
        if finish != next_start and committed[seat, finish]["state"] == "Standing":
            assert_equal(len(timelines[action]) - 1, clock[-1], f"Action {action} final source frame")
        windows.append({"seat": seat, "move": move, "action": action, "begin": begin, "end": finish, "clock": clock})
    return windows


def alias(mode):
    return f"terry98-stage18-{mode}"


def run_fixture(options, mode, name, reference, timelines, profiles):
    native = native_scene(reference, name)
    scene = adapted_scene(native, name) if options.adapt_source else native
    modes = ("default", mode) if name.startswith("mirror-") else (mode, "default")
    authority = []
    fixed = options.fps == "fixed"
    for variant in ("sprite", "plain") if options.compare_plain else ("sprite",):
        output = options.output / f"{mode}-reentry-{name}" / variant
        output.mkdir(parents=True)
        sprites = variant == "sprite"
        scheduled = scene
        if not fixed and options.realtime_second_delay is not None:
            if len(scene.keys) != 4:
                raise ValueError("realtime second-delay override requires two isolated taps")
            first, last = int(scene.keys[0].split(":", 1)[0]), int(scene.keys[2].split(":", 1)[0])
            adjustment = last - first - options.realtime_second_delay
            shifted = tuple(f"{int(frame) - adjustment}:{step}" for frame, step in
                            (key.split(":", 1) for key in scene.keys[2:]))
            scheduled = replace(scene, keys=scene.keys[:2] + shifted)
        args, capture = runtime_args(options, scheduled, output, sprites, modes, visual_alias=alias, realtime_tail=36)
        if fixed:
            args += ["--damage-audit", "--reentry-audit"]
        log = run_godot(options.godot, args, output / "duel.log", output / "end.png")
        lines = log.decode("utf-8", errors="replace").splitlines()
        if options.require_no_backlog and any(line.startswith("WARNING: [BattleScene] 帧积压") for line in lines):
            raise ValueError("realtime capture exceeded the battle time budget")
        if fixed:
            verify_scripted_keys(lines, BOOT_KEYS + scene.keys)
        events = battle_events(lines)
        reentry = audit_rows(lines, "ReentryAudit")
        moves = audit_rows(lines, "MoveAudit")
        damage = audit_rows(lines, "DamageAudit")
        starts = validate_instances(scene, events, indexed(reentry), indexed(moves), indexed(damage),
                                    [profiles[mode] for mode in modes], fixed=fixed)
        if sprites:
            windows, images = validate_captures(scene, events, lines, output, capture, timelines, modes, fixed,
                                                move_validator=validate_poses, check_reactions=False)
            if not fixed and any(not any(window["begin"] <= image["state"] < window["end"] for image in images)
                                 for window in windows):
                raise ValueError("realtime samples must show each move instance")
            evidence = {"native": str(reference), "inputs": scheduled.keys, "reference_entries": scene.moves,
                        "reference_contacts": scene.contacts, "events": events, "starts": starts, "moves": moves,
                        "native_inputs": native.keys, "scoped_differences": scene.differences, "reentry": reentry,
                        "damage": damage, "windows": windows, "captures": images}
            (output / "trace.json").write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
        authority.append([line for line in log.splitlines() if line.startswith(REENTRY_AUTHORITY)])
    if options.compare_plain:
        require_same(*authority, f"{mode}-{name} sprite vs plain")
    contact_count = sum(row["kind"] in ("Hit", "Blocked") for row in events)
    print(f"{mode}-{name}: {len(starts)} instances, {contact_count} Godot contacts / {len(scene.contacts)} native, "
          f"{len(authority[0])} authority rows", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--godot", required=True, type=Path)
    parser.add_argument("--reference", required=True, type=Path, action="append", help="Native trace directory, in lookup order")
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--profile-dir", default="data/imported-characters/terry98-stage18-data")
    parser.add_argument("--fps", choices=("fixed", "30", "60"), default="fixed")
    parser.add_argument("--realtime-window", nargs=2, type=int, metavar=("FIRST", "LAST"))
    parser.add_argument("--realtime-second-delay", type=int, help="Override second tap spacing in realtime for a two-tap probe")
    parser.add_argument("--realtime-resolution", default="640x360")
    parser.add_argument("--require-no-backlog", action="store_true")
    parser.add_argument("--mode", choices=MODES, action="append")
    parser.add_argument("--scene", action="append", required=True)
    parser.add_argument("--compare-plain", action="store_true")
    parser.add_argument("--adapt-source", action="store_true", help="Use documented equivalents for native jump/reaction differences")
    options = parser.parse_args()
    if options.fps != "fixed" and options.compare_plain:
        parser.error("authority comparison requires fixed mode")
    if options.realtime_window is not None:
        first, last = options.realtime_window
        if options.fps == "fixed" or not 1 <= first <= last or last - first >= 90:
            parser.error("--realtime-window requires an increasing capture range in realtime mode")
    if options.realtime_second_delay is not None and (options.fps != "60" or not 2 <= options.realtime_second_delay <= 11):
        parser.error("--realtime-second-delay requires a 2–11 render frame spacing at cap 60")
    options.output = options.output.resolve()
    if (ROOT / "local-mugen").resolve() not in options.output.parents:
        parser.error("--output must be a fresh directory under local-mugen")
    try:
        options.godot = options.godot.resolve(strict=True)
        options.output.mkdir(parents=True, exist_ok=False)
        manifest = json.loads((ROOT / "data/imported-characters/terry98/manifest.json").read_text(encoding="utf-8-sig"))
        timelines = {action: visual_timeline(manifest, action) for action in (200, 205, 210, 211, 215)}
        profiles = {mode: json.loads((ROOT / options.profile_dir / f"{mode}.fighter.json").read_text(encoding="utf-8-sig"))
                    for mode in MODES}
        assert_equal(MODES, {mode: profile["fighterId"] for mode, profile in profiles.items()}, "experimental profiles")
        failed = []
        for mode in options.mode or MODES:
            for name in options.scene:
                filename = f"{mode}-reentry-{name}.jsonl"
                reference = next((root / filename for root in options.reference if (root / filename).is_file()),
                                 options.reference[0] / filename)
                try:
                    run_fixture(options, mode, name, reference, timelines, profiles)
                except (KeyError, OSError, ValueError, TimeoutExpired) as error:
                    print(f"{mode}-{name}: FAIL {error!r}", flush=True)
                    failed.append(f"{mode}-{name}")
        if failed:
            raise ValueError("failed fixtures: " + ", ".join(failed))
    except (OSError, ValueError) as error:
        parser.exit(1, f"Stage 18 duel validation failed: {error}\n")


if __name__ == "__main__":
    main()
