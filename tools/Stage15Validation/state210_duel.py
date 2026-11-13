"""Run Terry's two grounded punches through real Godot duel input and rendering."""

import argparse
from dataclasses import dataclass
import json
from pathlib import Path
import re
from subprocess import TimeoutExpired
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Stage14Validation"))
from frozen_authority import ROOT, require_same, run_godot
from state200_duel import BOOT_KEYS, audit_rows, sprite_keys


LIGHT = "TerryStandLightPunch200"
HEAVY = "TerryStandHeavyPunch210"
MODES = {"default": ("TerryS210", 210), "mode2": ("TerryS210Mode2", 211)}
AUTHORITY = (b"[VisualAudit] ", b"[EntityAudit] ", b"[MoveAudit] ")
EVENT = re.compile(r"Frame = (\d+), EventId = \d+, Type = (\w+), SourceFighterId = (\d+), "
                   r"TargetFighterId = (\d+), MoveId = ([^,]*), Amount = (\d+)")
CAPTURE = re.compile(r"^\[FrameCapture\] render=(\d+) state=(\d+) path=(.+) result=Ok$")


@dataclass(frozen=True)
class Move:
    seat: int
    move_id: str
    outcome: str | None = None
    interrupted_at: int | None = None


@dataclass(frozen=True)
class Scene:
    half_distance: int | float
    keys: tuple[str, ...]
    moves: tuple[Move, ...]
    guard_action: int | None = None
    reference_case: str | None = None
    # A light input attempted mid-recovery is intentionally discarded before the later legal press.
    captures: tuple[int, int] = (26, 86)


def press(frame, key):
    return (f"{frame}:down:{key}", f"{frame + 2}:up:{key}")


def guard(frame, *keys):
    return tuple(step for key in keys for step in (f"{frame}:down:{key}", f"63:up:{key}"))


SCENES = {
    "whiff": Scene(120, press(28, "K"), (Move(0, HEAVY),), reference_case="whiff"),
    "hit": Scene(36, press(28, "K"), (Move(0, HEAVY, "Hit"),), reference_case="hit"),
    "guard": Scene(36, press(28, "K") + guard(32, "Right"), (Move(0, HEAVY, "Blocked"),), 150, "guard"),
    "crouch-guard": Scene(36, press(28, "K") + guard(28, "Down", "Right"),
                          (Move(0, HEAVY, "Blocked"),), 151, "crouch-guard"),
    "crouch-guard-late": Scene(36, press(28, "K") + guard(33, "Down", "Right"),
                               (Move(0, HEAVY, "Blocked"),), 151, "crouch-guard-late"),
    "mirror": Scene(36, press(28, "Period"), (Move(1, HEAVY, "Hit"),), reference_case="mirror"),
    "close-64": Scene(32, press(28, "K"), (), reference_case="close-64"),
    "far-65": Scene(32.5, press(28, "K"), (Move(0, HEAVY, "Hit"),), reference_case="far-65"),
    "boundary-86": Scene(43, press(28, "K"), (Move(0, HEAVY, "Hit"),), reference_case="boundary-86"),
    "boundary-87": Scene(43.5, press(28, "K"), (Move(0, HEAVY),), reference_case="boundary-87"),
    "interrupt": Scene(36, press(28, "K") + press(28, "Comma"),
                       (Move(0, HEAVY, interrupted_at=3), Move(1, LIGHT, "Hit")), reference_case="interrupt"),
    "trade": Scene(36, press(28, "K") + press(30, "Comma"),
                   (Move(0, HEAVY, "Hit", 5), Move(1, LIGHT, "Hit", 3)), reference_case="trade"),
    "sequence": Scene(120, press(28, "K") + press(40, "J") + press(62, "J") + press(77, "Period"),
                      (Move(0, HEAVY), Move(0, LIGHT), Move(1, HEAVY)), captures=(26, 111)),
}
EXTRA_SCENES = {"far-64.5": Scene(32.25, press(28, "K"), (Move(0, HEAVY, "Hit"),), reference_case="far-64p5")}


def alias(mode):
    return f"terry98-stage15-{mode}"


def runtime_args(options, scene, output, sprites, modes):
    fixed = options.fps == "fixed"
    capture = scene.captures if fixed else tuple(options.realtime_window or (50, 55))
    args = ["--path", ".", "--rendering-method", "forward_plus", "--rendering-driver", "vulkan",
            "--resolution", "960x540" if fixed else options.realtime_resolution]
    if fixed:
        args += ["--fixed-fps", "60"]
    args += ["--", "--visual-audit", "--sprite-frame-audit", "--capture-input-only", f"--duel={scene.half_distance}",
             f"--shot={output.joinpath('end.png').as_posix()}", f"--shotframes={capture[1] + 18}"]
    if fixed:
        args += ["--entity-audit", "--move-audit", "--inputdiag"]
    for seat, mode in enumerate(modes, start=1):
        args.append(f"--p{seat}-fighter-file=res://{options.profile_dir}/{mode}.fighter.json")
        if sprites:
            args.append(f"--p{seat}-visual={alias(mode)}")
    if sprites:
        args += [f"--capture-dir={output.joinpath('frames').as_posix()}",
                 f"--capture-start={capture[0]}", f"--capture-end={capture[1]}"]
    keys = list(BOOT_KEYS)
    if fixed:
        keys.extend(scene.keys)
    else:
        # Let Vulkan compile the duel draw path while paused. Time starts again before the attack.
        args.append(f"--maxfps={options.fps}")
        keys += ["4:down:F2", "9:down:F1", "10:up:F1", "42:down:F1", "43:up:F1"]
        for step in scene.keys:
            frame, action, key = step.split(":")
            relative = int(frame) - 28
            if options.fps == "30":
                relative = (relative + 1) // 2
            if key == "Right" and action == "down":
                # The realtime probe samples visual playback. Its guard must already be held when simulation catches up.
                relative = 0
            keys.append(f"{48 + relative}:{action}:{key}")
    return [*args, *(f"--renderkey={key}" for key in keys)], capture


def battle_events(lines):
    result = []
    for line in lines:
        if not line.startswith("[VisualAudit] "):
            continue
        for frame, kind, source, target, move, amount in EVENT.findall(line):
            result.append({"frame": int(frame), "kind": kind, "source": int(source), "target": int(target),
                           "move": move, "amount": int(amount)})
    return result


def verify_scripted_keys(lines, expected):
    pattern = re.compile(r"^\[InputTrace\] key: (\S+) (down|up) echo=(True|False)\b")
    observed = [(match[1], match[2]) for line in lines if (match := pattern.match(line))]
    scheduled = sorted(expected, key=lambda item: int(item.split(":", 1)[0]))
    actual = [(key, direction) for _, direction, key in (step.split(":") for step in scheduled)]
    if observed != actual:
        raise ValueError(f"unexpected keyboard events: scripted {actual}, observed {observed}")


def validate_events(scene, events):
    starts = [(event["source"], event["move"]) for event in events if event["kind"] == "MoveStarted"]
    expected_starts = [(move.seat, move.move_id) for move in scene.moves]
    if starts != expected_starts:
        raise ValueError(f"expected move starts {expected_starts}, got {starts}")
    expected = [(move.seat, move.move_id, move.outcome) for move in scene.moves if move.outcome]
    actual = [(event["source"], event["move"], event["kind"]) for event in events
              if event["kind"] in ("Hit", "Blocked")]
    if sorted(actual) != sorted(expected):
        raise ValueError(f"expected attack results {expected}, got {actual}")
    expected_active = [(move.seat, move.move_id) for move in scene.moves if move.interrupted_at is None or move.outcome]
    actual_active = [(event["source"], event["move"]) for event in events if event["kind"] == "AttackActive"]
    if actual_active != expected_active:
        raise ValueError(f"expected first active events {expected_active}, got {actual_active}")
    for event in events:
        if event["kind"] == "AttackActive":
            start = next(start for start in reversed(events) if start["kind"] == "MoveStarted" and start["source"] == event["source"]
                         and start["move"] == event["move"] and start["frame"] <= event["frame"])
            expected_tick = 3 if event["move"] == LIGHT else 5
            if event["frame"] - start["frame"] != expected_tick:
                raise ValueError(f"first active for {event['move']} must be tick {expected_tick}: {event}")


def visual_timeline(manifest, action_id):
    actions = [a for a in manifest["actions"] if a["action"] == action_id]
    if len(actions) != 1:
        raise ValueError(f"expected exactly one Action {action_id} in Manifest")
    timeline = []
    for index, frame in enumerate(actions[0]["frames"]):
        ticks = frame["time"]
        if ticks <= 0 or frame["sprite_index"] is None:
            raise ValueError(f"Action {action_id} uses an unsupported duration or missing image")
        sample = tuple(map(str, (action_id, index, f"{frame['group']},{frame['image']}", frame["sprite_index"], ticks)))
        timeline.extend([sample] * ticks)
    return timeline


def validate_moves(scene, events, committed, timelines, modes):
    windows = []
    starts = [event for event in events if event["kind"] == "MoveStarted"]
    for spec, start in zip(scene.moves, starts):
        who = spec.seat
        action = 200 if spec.move_id == LIGHT else MODES[modes[who]][1]
        origin = start["frame"] + 1
        end = origin
        clock = []
        expected = timelines[action]
        while committed[who, end]["move"] == spec.move_id:
            row = committed[who, end]
            tick = int(row["tick"])
            if tick >= len(expected) or tuple(row[key] for key in ("action", "frame", "sprite", "sprite_index", "duration")) != expected[tick]:
                raise ValueError(f"Action {action} invalid frame at state {end}, move tick {tick}: {row}")
            if clock and tick - clock[-1] != (0 if row["frozen"] == "True" else 1):
                raise ValueError(f"Action {action} advanced incorrectly during transition to state {end}")
            clock.append(tick)
            end += 1
        following = committed[who, end]
        length = len(expected) if spec.interrupted_at is None else spec.interrupted_at
        expected_state = "Standing" if spec.interrupted_at is None else "Hitstun"
        if sorted(set(clock)) != list(range(length)) or following["state"] != expected_state or following["action"] == str(action):
            raise ValueError(f"Action {action}: incorrect clock {clock} or exit {following}")
        if spec.interrupted_at is None and following["action"] != ("1" if modes[who] == "mode2" else "0"):
            raise ValueError(f"Action {action}: incorrect standby action for {modes[who]} at state {end}")
        windows.append({"seat": who, "action": action, "begin": origin, "end": end, "clock": clock})
    return windows


def validate_captures(scene, events, lines, directory, capture_range, timelines, modes, fixed):
    committed = {(int(row["fighter"]), int(row["logic"]) + 1): row for row in audit_rows(lines, "SpriteFrameAudit")}
    rendered = {(int(row["fighter"]), int(row["logic"])): row for row in audit_rows(lines, "SpriteRenderAudit")}
    windows = validate_moves(scene, events, committed, timelines, modes)
    captures = []
    for line in lines:
        match = CAPTURE.match(line)
        if not match:
            continue
        draw, state = map(int, match.group(1, 2))
        path = Path(match[3]).resolve()
        expected = directory / "frames" / f"render-{draw:05d}-state-{state:05d}.png"
        if path != expected or not path.is_file():
            raise ValueError(f"missing actual Vulkan image for render {draw}, state {state}")
        fighters = []
        for seat in (0, 1):
            current = rendered[seat, state]
            if current["sprite_selected"] != "True" or sprite_keys(current) != sprite_keys(committed[seat, state]):
                raise ValueError(f"render {draw} state {state}, seat {seat + 1}: rendered state differs from committed projection")
            fighters.append(current)
        captures.append({"render": draw, "state": state, "path": str(path), "fighters": fighters})
    states = [frame["state"] for frame in captures]
    if [row["render"] for row in captures] != list(range(capture_range[0], capture_range[1] + 1)) or states != sorted(states):
        raise ValueError("FramePostDraw captures missing or unordered")
    for window in windows:
        visible = set(states) & set(range(window["begin"], window["end"]))
        if fixed and not set(range(window["begin"], window["end"] + 1)).issubset(states):
            raise ValueError(f"fixed capture incomplete for Action {window['action']}")
        if not visible and (fixed or capture_range == (50, 55)):
            raise ValueError(f"no displayed sample for Action {window['action']}")
    if scene.guard_action is not None and not any(
        any(row["state"] == "Blockstun" and row["action"] == str(scene.guard_action) for row in frame["fighters"])
        for frame in captures
    ):
        raise ValueError(f"no image with defender Action {scene.guard_action} during Blockstun")
    for event in events:
        if event["kind"] != "Hit" or event["move"] != HEAVY:
            continue
        defender, contact = event["target"], event["frame"] + 1
        first = committed[defender, contact]
        if first.get("action") != "5002":
            raise ValueError(f"heavy contact state {contact}: expected the Action 5002 reaction, got {first}")
        if fixed:
            # Source only changes to 5007 after three non-frozen reaction ticks.
            for state in range(contact + 1, contact + 18):
                expected = "5002" if state < contact + 17 else "5007"
                expected_frame = "1" if contact + 15 <= state < contact + 17 else "0"
                row = committed[defender, state]
                if (row.get("action"), row.get("frame")) != (expected, expected_frame):
                    raise ValueError(f"heavy reaction state {state}: expected Action {expected} element {expected_frame}, got {row}")
    return windows, captures


def run(options):
    manifest = json.loads((ROOT / "data/imported-characters/terry98/manifest.json").read_text(encoding="utf-8-sig"))
    timelines = {action: visual_timeline(manifest, action) for action in (200, 210, 211)}
    for mode in options.mode:
        for name in options.scene:
            scene = (SCENES | EXTRA_SCENES)[name]
            modes = scene_modes(mode, name)
            logs = {}
            for variant in ("sprite", "plain") if options.compare_plain else ("sprite",):
                directory = options.output / f"{mode}-{name}" / variant
                directory.mkdir(parents=True)
                sprites = variant == "sprite"
                args, captures = runtime_args(options, scene, directory, sprites, modes)
                log = run_godot(options.godot, args, directory / "duel.log", directory / "end.png")
                lines = log.decode("utf-8", errors="replace").splitlines()
                if options.require_no_backlog and any(line.startswith("WARNING: [BattleScene] 帧积压") for line in lines):
                    raise ValueError("realtime capture exceeded the battle time budget")
                if options.fps == "fixed":
                    verify_scripted_keys(lines, BOOT_KEYS + scene.keys)
                events = battle_events(lines)
                validate_events(scene, events)
                moves = audit_rows(lines, "MoveAudit")
                if options.fps == "fixed":
                    for seat, expected_mode in enumerate(modes):
                        seat_rows = [row for row in moves if row["fighter"] == str(seat)]
                        if {row["id"] for row in seat_rows} != {MODES[expected_mode][0]}:
                            raise ValueError(f"P{seat + 1} did not load the intended experimental profile")
                        if int(seat_rows[0]["x"]) != (2 * seat - 1) * scene.half_distance * 65536:
                            raise ValueError(f"P{seat + 1} initial position differs from the requested scenario")
                if sprites:
                    windows, images = validate_captures(scene, events, lines, directory, captures, timelines, modes, options.fps == "fixed")
                    trace = {"events": events, "moves": moves, "windows": windows, "captures": images, "modes": modes}
                    (directory / "trace.json").write_text(json.dumps(trace, indent=2) + "\n", encoding="utf-8")
                logs[variant] = [line for line in log.splitlines() if line.startswith(AUTHORITY)]
            if options.compare_plain:
                require_same(logs["sprite"], logs["plain"], f"{mode}-{name} sprite vs plain")
            print(f"{mode}-{name}: {options.fps} FPS, events and images verified; {len(logs['sprite'])} authority rows", flush=True)


def scene_modes(mode, name):
    if name == "mirror":
        return "default", mode
    if name == "sequence":
        return mode, "mode2" if mode == "default" else "default"
    return mode, "default"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--godot", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--profile-dir", default="data/imported-characters/terry98-stage15-data",
                        help="Project-relative local directory containing the two generated profiles")
    parser.add_argument("--fps", choices=("fixed", "30", "60"), default="fixed")
    parser.add_argument("--realtime-window", nargs=2, type=int, metavar=("FIRST", "LAST"),
                        help="Override the short FramePostDraw window when sampling realtime playback")
    parser.add_argument("--realtime-resolution", default="800x450")
    parser.add_argument("--require-no-backlog", action="store_true", help="Fail if the scene reports dropped time budget")
    parser.add_argument("--mode", choices=MODES, action="append")
    parser.add_argument("--scene", choices=SCENES | EXTRA_SCENES, action="append")
    parser.add_argument("--compare-plain", action="store_true")
    options = parser.parse_args()
    options.mode = options.mode or list(MODES)
    options.scene = options.scene or (list(SCENES) if options.fps == "fixed" else ["hit", "guard"])
    if options.fps != "fixed" and (options.compare_plain or set(options.scene) - {"hit", "guard", "mirror"}):
        parser.error("realtime permits hit, guard or mirror with visual cover enabled")
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
