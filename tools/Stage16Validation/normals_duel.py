"""Exercise Terry's near and far grounded normals in real Godot duels."""

import argparse
from dataclasses import dataclass
import json
from pathlib import Path
from subprocess import TimeoutExpired
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Stage15Validation"))
from state210_duel import (AUTHORITY, BOOT_KEYS, HEAVY, LIGHT, audit_rows, battle_events, press,
                           require_same, ROOT, run_godot, runtime_args, validate_captures,
                           verify_scripted_keys, visual_timeline)


NEAR_LIGHT = "TerryStandLightPunch205"
NEAR_HEAVY = "TerryStandHeavyPunch215"
MODES = {"default": ("TerryNormals", 210), "mode2": ("TerryNormalsMode2", 211)}
ACTIONS = {LIGHT: 200, NEAR_LIGHT: 205, NEAR_HEAVY: 215}
REACTIONS = {NEAR_LIGHT: (5000, 5005, 6), HEAVY: (5002, 5007, 4), NEAR_HEAVY: (5002, 5007, 4)}


@dataclass(frozen=True)
class Move:
    seat: int
    move_id: str
    results: tuple[str, ...] = ()
    interrupted_at: int | None = None
    heavy_pulses: tuple[int, ...] = ()


@dataclass(frozen=True)
class Scene:
    half_distance: int | float
    keys: tuple[str, ...]
    moves: tuple[Move, ...]
    guard_action: int | None = None
    captures: tuple[int, int] = (26, 87)
    guard_actions: tuple[int, ...] = ()


def hold(start, end, *keys):
    return tuple(step for key in keys for step in (f"{start}:down:{key}", f"{end}:up:{key}"))


def direct(distance, key, move, results=(), *, seat=0, extra_keys=(), guard_action=None, heavy_pulses=(), guard_actions=()):
    return Scene(distance / 2, press(28, key) + extra_keys, (Move(seat, move, results, heavy_pulses=heavy_pulses),),
                 guard_action=guard_action, guard_actions=guard_actions)


SCENES = {
    "light-hit": direct(44, "J", NEAR_LIGHT, ("Hit",)),
    "light-guard": direct(44, "J", NEAR_LIGHT, ("Blocked",), extra_keys=hold(30, 69, "Right"), guard_action=150),
    "light-crouch-guard": direct(44, "J", NEAR_LIGHT, ("Blocked",), extra_keys=hold(28, 69, "Down", "Right"), guard_action=151),
    "light-crouch-late": direct(44, "J", NEAR_LIGHT, ("Blocked",), extra_keys=hold(31, 69, "Down", "Right"), guard_action=151),
    "light-mirror": direct(44, "Comma", NEAR_LIGHT, ("Hit",), seat=1),
    "light-boundary-46": direct(46, "J", NEAR_LIGHT, ("Hit",)),
    "light-boundary-46p5": direct(46.5, "J", LIGHT, ("Hit",)),
    "light-far-hit": direct(60, "J", LIGHT, ("Hit",)),
    "light-whiff": Scene(18, hold(23, 29, "Up") + press(42, "J"), (Move(0, NEAR_LIGHT),)),
    "heavy-hit": direct(62, "K", NEAR_HEAVY, ("Hit", "Hit"), heavy_pulses=(3, 6)),
    "heavy-guard": direct(62, "K", NEAR_HEAVY, ("Blocked", "Blocked"), extra_keys=hold(31, 79, "Right"),
                          guard_action=150, heavy_pulses=(3, 6)),
    "heavy-crouch-guard": direct(62, "K", NEAR_HEAVY, ("Blocked", "Blocked"), extra_keys=hold(28, 79, "Down", "Right"),
                                 guard_action=151, heavy_pulses=(3, 6)),
    "heavy-crouch-late": direct(62, "K", NEAR_HEAVY, ("Blocked", "Blocked"), extra_keys=hold(31, 79, "Down", "Right"),
                                guard_action=151, heavy_pulses=(3, 6)),
    "heavy-guard-release": direct(62, "K", NEAR_HEAVY, ("Blocked", "Blocked"), extra_keys=hold(31, 32, "Right"),
                                   guard_action=150, heavy_pulses=(3, 6)),
    "heavy-crouch-release": direct(62, "K", NEAR_HEAVY, ("Blocked", "Blocked"), extra_keys=hold(28, 32, "Down", "Right"),
                                    guard_action=151, heavy_pulses=(3, 6), guard_actions=(151, 150)),
    "heavy-crouch-to-stand": direct(62, "K", NEAR_HEAVY, ("Blocked", "Blocked"),
                                     extra_keys=hold(28, 32, "Down") + hold(28, 79, "Right"),
                                     guard_action=151, heavy_pulses=(3, 6), guard_actions=(151, 150)),
    "heavy-mirror": direct(62, "Period", NEAR_HEAVY, ("Hit", "Hit"), seat=1, heavy_pulses=(3, 6)),
    "heavy-boundary-64": direct(64, "K", NEAR_HEAVY, ("Hit", "Hit"), heavy_pulses=(3, 6)),
    "heavy-boundary-64p5": direct(64.5, "K", HEAVY, ("Hit",)),
    "heavy-far-hit": direct(72, "K", HEAVY, ("Hit",)),
    "heavy-second-only": direct(64, "K", NEAR_HEAVY, ("Hit",), extra_keys=hold(28, 29, "Right"), heavy_pulses=(6,)),
    "heavy-second-guard": direct(64, "K", NEAR_HEAVY, ("Blocked",), extra_keys=hold(28, 79, "Right"),
                                 guard_action=150, heavy_pulses=(6,)),
    "heavy-whiff": Scene(27, hold(23, 29, "Up") + press(42, "K"), (Move(0, NEAR_HEAVY),)),
    "sequence": Scene(70, press(28, "K") + press(28, "Comma") + press(42, "J") + hold(61, 77, "D", "Left")
                      + press(79, "Comma") + press(80, "K"),
                      (Move(0, HEAVY), Move(1, LIGHT), Move(1, NEAR_LIGHT, ("Hit",)),
                       Move(0, NEAR_HEAVY, interrupted_at=2)), captures=(26, 114)),
    "sequence-close-far": Scene(22, press(28, "J") + press(40, "K") + press(65, "K"),
                                (Move(0, NEAR_LIGHT, ("Hit",)), Move(0, HEAVY, ("Hit",))), captures=(26, 114)),
}
EXTRA_SCENES = {
    "light-trade": Scene(22, press(28, "J") + press(28, "Period"),
                         (Move(0, NEAR_LIGHT, ("Hit",), 3), Move(1, NEAR_HEAVY, ("Hit",), 3))),
    "heavy-trade": Scene(31, press(28, "K") + press(28, "Comma"),
                         (Move(0, NEAR_HEAVY, ("Hit",), 3), Move(1, LIGHT, ("Hit",), 3))),
    "light-both-near": direct(44, "J", NEAR_LIGHT, ("Hit",), extra_keys=press(28, "K")),
    "light-both-far": direct(70, "J", LIGHT, ("Hit",), extra_keys=press(28, "K")),
}


def mode_alias(mode):
    return f"terry98-stage16-{mode}"


def scene_modes(mode, name):
    if name.endswith("mirror"):
        return "default", mode
    if name == "sequence":
        return mode, "mode2" if mode == "default" else "default"
    return mode, "default"


def validate_events(scene, events, committed):
    starts = [event for event in events if event["kind"] == "MoveStarted"]
    if [(e["source"], e["move"]) for e in starts] != [(m.seat, m.move_id) for m in scene.moves]:
        raise ValueError(f"unexpected move starts: {starts}")
    expected = [(m.seat, m.move_id, outcome) for m in scene.moves for outcome in m.results]
    contacts = [event for event in events if event["kind"] in ("Hit", "Blocked")]
    if sorted((e["source"], e["move"], e["kind"]) for e in contacts) != sorted(expected):
        raise ValueError(f"expected ordered contacts {expected}, got {contacts}")
    actives = [event for event in events if event["kind"] == "AttackActive"]
    expected_active = []
    for move, start in zip(scene.moves, starts):
        tick = 5 if move.move_id == HEAVY else 3
        if move.interrupted_at is None or move.interrupted_at >= tick:
            expected_active.append((start["frame"] + tick, move.seat, move.move_id))
    actual_active = [(e["frame"], e["source"], e["move"]) for e in actives]
    if sorted(actual_active) != sorted(expected_active):
        raise ValueError(f"expected first active events {expected_active}, got {actual_active}")
    pulse_ticks = []
    for contact in contacts:
        if not committed:
            break
        row = committed[contact["source"], contact["frame"]]
        elapsed = int(row["elapsed"])
        if contact["move"] == NEAR_HEAVY:
            if row["move"] != NEAR_HEAVY:
                # A simultaneous hit settles from the pre-impact move, then clears both committed moves.
                spec = next(move for move in scene.moves if move.seat == contact["source"] and move.move_id == NEAR_HEAVY)
                if spec.interrupted_at is None or not any(e["kind"] == "Hit" and e["target"] == contact["source"]
                                                         and e["frame"] == contact["frame"] for e in contacts):
                    raise ValueError(f"heavy contact without an active or simultaneously interrupted attack: {row}")
                continue
            if row["move"] != NEAR_HEAVY or not 3 <= elapsed <= 12:
                raise ValueError(f"heavy contact outside active interval: {row}")
            pulse_ticks.append(elapsed)
    expected_pulses = [tick for move in scene.moves for tick in move.heavy_pulses]
    if committed and pulse_ticks != expected_pulses:
        raise ValueError(f"near-heavy contacts at {pulse_ticks}, expected one settlement at each of {expected_pulses}")
    return pulse_ticks


def validate_moves(scene, events, committed, timelines, modes, *, check_close_light=False):
    starts = [event for event in events if event["kind"] == "MoveStarted"]
    windows = []
    for spec, start in zip(scene.moves, starts):
        seat = spec.seat
        action = ACTIONS.get(spec.move_id, MODES[modes[seat]][1])
        first = start["frame"] + 1
        end = first
        clock = []
        expected = timelines[action]
        while committed[seat, end]["move"] == spec.move_id:
            row = committed[seat, end]
            tick = int(row["tick"])
            if tick >= len(expected) or tuple(row[key] for key in ("action", "frame", "sprite", "sprite_index", "duration")) != expected[tick]:
                raise ValueError(f"Action {action} wrong source frame at state {end}: {row}")
            if clock and tick - clock[-1] != (0 if row["frozen"] == "True" else 1):
                raise ValueError(f"Action {action} jumped or advanced incorrectly at state {end}: {clock} -> {tick}")
            clock.append(tick)
            end += 1
        after = committed[seat, end]
        length = spec.interrupted_at if spec.interrupted_at is not None else len(expected)
        state = "Hitstun" if spec.interrupted_at is not None else "Standing"
        if sorted(set(clock)) != list(range(length)) or after["state"] != state or after["action"] == str(action):
            raise ValueError(f"Action {action}: wrong exit after {clock}: {after}")
        if state == "Standing" and after["action"] != ("1" if modes[seat] == "mode2" else "0"):
            raise ValueError(f"Action {action}: expected mode-specific idle after exit: {after}")
        windows.append({"seat": seat, "move": spec.move_id, "action": action, "begin": first, "end": end, "clock": clock})
    guards = [event for event in events if event["kind"] == "Blocked"]
    actions = scene.guard_actions or (scene.guard_action,) * len(guards)
    for event, action in zip(guards, actions, strict=True):
        pose = committed[event["target"], event["frame"] + 1]
        if action is not None and (pose["state"], pose.get("action")) != ("Blockstun", str(action)):
            raise ValueError(f"guard event {event} expected Action {action}, got {pose}")
    for (event, action), following in zip(zip(guards, actions), guards[1:]):
        target = event["target"]
        for state in range(event["frame"] + 1, following["frame"] + 1):
            pose = committed[target, state]
            if action is not None and pose["state"] == "Blockstun" and pose.get("action") != str(action):
                raise ValueError(f"guard Action {action} changed before a new event at state {state}: {pose}")
    hits = [event for event in events if event["kind"] == "Hit"]
    for index, event in enumerate(hits):
        if event["move"] not in REACTIONS or event["move"] == NEAR_LIGHT and not check_close_light:
            continue
        target = event["target"]
        first, followup, transition = REACTIONS[event["move"]]
        state = event["frame"] + 1
        next_hit = next((other["frame"] + 1 for other in hits[index + 1:] if other["target"] == target), None)
        reaction = committed[target, state]
        if (reaction["state"], reaction.get("action"), reaction.get("frame")) != ("Hitstun", str(first), "0"):
            raise ValueError(f"contact {event} did not reset Action {first}: {reaction}")
        elapsed = 0
        state += 1
        while state != next_hit and committed[target, state]["state"] == "Hitstun":
            reaction = committed[target, state]
            elapsed += reaction["frozen"] == "False"
            expected = first if elapsed < transition else followup
            if reaction.get("action") != str(expected):
                raise ValueError(f"reaction to {event}: expected Action {expected} at state {state}, got {reaction}")
            state += 1
    return windows


def run(options):
    manifest = json.loads((ROOT / "data/imported-characters/terry98/manifest.json").read_text(encoding="utf-8-sig"))
    timelines = {action: visual_timeline(manifest, action) for action in (200, 205, 210, 211, 215)}
    failures = []
    for mode in options.mode:
        for name in options.scene:
            try:
                run_scene(options, mode, name, timelines)
            except (KeyError, OSError, ValueError, TimeoutExpired) as error:
                failures.append(f"{mode}-{name}")
                print(f"{mode}-{name}: FAIL {error!r}", flush=True)
    if failures:
        raise ValueError("failed fixtures: " + ", ".join(failures))


def run_scene(options, mode, name, timelines):
    scene = (SCENES | EXTRA_SCENES)[name]
    modes = scene_modes(mode, name)
    logs = {}
    for variant in ("sprite", "plain") if options.compare_plain else ("sprite",):
        output = options.output / f"{mode}-{name}" / variant
        output.mkdir(parents=True)
        sprites = variant == "sprite"
        args, captures = runtime_args(options, scene, output, sprites, modes, visual_alias=mode_alias, realtime_tail=36)
        log = run_godot(options.godot, args, output / "duel.log", output / "end.png")
        lines = log.decode("utf-8", errors="replace").splitlines()
        if options.require_no_backlog and any(line.startswith("WARNING: [BattleScene] 帧积压") for line in lines):
            raise ValueError("realtime capture exceeded the battle time budget")
        events = battle_events(lines)
        moves = audit_rows(lines, "MoveAudit")
        committed = {(int(row["fighter"]), int(row["logic"])): row for row in moves}
        if options.fps == "fixed":
            verify_scripted_keys(lines, BOOT_KEYS + scene.keys)
            for seat, preset in enumerate(modes):
                seat_rows = [row for row in moves if row["fighter"] == str(seat)]
                if {row["id"] for row in seat_rows} != {MODES[preset][0]}:
                    raise ValueError(f"seat {seat + 1} has wrong experimental profile")
                if int(seat_rows[0]["x"]) != (2 * seat - 1) * scene.half_distance * 65536:
                    raise ValueError(f"seat {seat + 1} started at the wrong distance")
        pulses = validate_events(scene, events, committed)
        if sprites:
            windows, images = validate_captures(scene, events, lines, output, captures, timelines, modes,
                                                options.fps == "fixed", move_validator=validate_reactions, check_reactions=False)
            evidence = {"events": events, "moves": moves, "windows": windows, "captures": images,
                        "modes": modes, "heavy_pulse_ticks": pulses}
            (output / "trace.json").write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
        logs[variant] = [line for line in log.splitlines() if line.startswith(AUTHORITY)]
    if options.compare_plain:
        require_same(logs["sprite"], logs["plain"], f"{mode}-{name} sprite vs plain")
    print(f"{mode}-{name}: {options.fps} FPS, events and images verified; {len(logs['sprite'])} authority rows", flush=True)


def validate_reactions(scene, events, committed, timelines, modes):
    return validate_moves(scene, events, committed, timelines, modes, check_close_light=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--godot", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--profile-dir", default="data/imported-characters/terry98-stage16-data")
    parser.add_argument("--fps", choices=("fixed", "30", "60"), default="fixed")
    parser.add_argument("--realtime-window", nargs=2, type=int, metavar=("FIRST", "LAST"))
    parser.add_argument("--realtime-resolution", default="640x360")
    parser.add_argument("--require-no-backlog", action="store_true")
    parser.add_argument("--mode", choices=MODES, action="append")
    parser.add_argument("--scene", choices=SCENES | EXTRA_SCENES, action="append")
    parser.add_argument("--compare-plain", action="store_true")
    options = parser.parse_args()
    options.mode = options.mode or list(MODES)
    options.scene = options.scene or (list(SCENES | EXTRA_SCENES) if options.fps == "fixed" else ["heavy-hit"])
    if options.fps != "fixed" and (options.compare_plain or set(options.scene) - {"light-hit", "heavy-hit", "light-mirror", "heavy-mirror"}):
        parser.error("realtime permits direct or mirrored hits with visual cover enabled")
    if options.realtime_window is not None:
        first, last = options.realtime_window
        if options.fps == "fixed" or not 1 <= first <= last or last - first >= 90:
            parser.error("--realtime-window requires an increasing capture range in realtime mode")
    options.output = options.output.resolve()
    if (ROOT / "local-mugen").resolve() not in options.output.parents:
        parser.error("--output must be a fresh directory under local-mugen")
    try:
        options.godot = options.godot.resolve(strict=True)
        options.output.mkdir(parents=True, exist_ok=False)
        run(options)
    except (KeyError, OSError, ValueError, TimeoutExpired) as error:
        parser.exit(1, f"Duel validation failed: {error!r}\n")


if __name__ == "__main__":
    main()
