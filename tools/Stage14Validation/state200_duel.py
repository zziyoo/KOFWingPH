"""Collect Stage 14 Vulkan duel evidence through the real scene and input path."""

import argparse
import json
from pathlib import Path
import re
from subprocess import TimeoutExpired

from frozen_authority import ROOT, run_godot, require_same


FIGHTER = "res://data/imported-characters/terry98/state200.fighter.json"
VISUAL = "terry98-stage14"
MOVE = "TerryStandLightPunch200"
SCENARIOS = {
    "whiff": (120, 1, None),
    "hit": (30, 1, "Hit"),
    "guard": (30, 1, "Blocked"),
    "mirror": (30, 2, "Hit"),
    "crouch-guard": (30, 1, None),
    "crouch-guard-late": (30, 1, "Blocked"),
    "boundary-78": (39, 1, "Hit"),
    "boundary-80": (40, 1, None),
}
BOOT_KEYS = (
    "1:down:Key1", "5:down:J", "7:down:Comma", "9:down:Enter",
    "11:up:J", "11:up:Comma", "11:up:Enter",
)


def godot_args(options, scene, output, sprites):
    distance, seat, _ = SCENARIOS[scene]
    args = ["--path", ".", "--rendering-method", "forward_plus", "--rendering-driver", "vulkan",
            "--resolution", "960x540"]
    if options.fps == "fixed":
        args += ["--fixed-fps", "60"]
    args += ["--", "--visual-audit", "--entity-audit", "--move-audit", "--sprite-frame-audit",
             f"--duel={distance}", f"--p1-fighter-file={options.fighter}", f"--p2-fighter-file={options.fighter}",
             f"--shot={output.joinpath('end.png').as_posix()}", "--shotframes=89"]
    keys = list(BOOT_KEYS)
    if options.fps != "fixed":
        args.append(f"--maxfps={options.fps}")
        keys.append("12:down:F2")
    if sprites:
        args += [f"--p1-visual={options.visual}", f"--p2-visual={options.visual}"]
        first, last = (26, 67) if options.fps == "fixed" else (30, 33)
        args += [f"--capture-dir={output.joinpath('frames').as_posix()}",
                 f"--capture-start={first}", f"--capture-end={last}"]
    attack = "J" if seat == 1 else "Comma"
    keys += [f"28:down:{attack}", f"30:up:{attack}"]
    if scene == "guard":
        away = "Right" if seat == 1 else "A"
        keys += [f"28:down:{away}", "49:up:" + away]
    if scene in ("crouch-guard", "crouch-guard-late"):
        start = 28 if scene == "crouch-guard" else 31
        keys += [f"{start}:down:Down", f"{start}:down:Right", "49:up:Down", "49:up:Right"]
    return args + [f"--renderkey={key}" for key in keys]


def audit_rows(lines, prefix):
    return [dict(token.split("=", 1) for token in line.split()[1:] if "=" in token)
            for line in lines if line.startswith(f"[{prefix}] ")]


def validate_events(lines, scene, move):
    _, seat, outcome = SCENARIOS[scene]
    events = []
    pattern = re.compile(r"Frame = (\d+), EventId = \d+, Type = (\w+), SourceFighterId = (\d+), "
                         r"TargetFighterId = (\d+), MoveId = ([^,]+), Amount = (\d+)")
    for line in lines:
        if line.startswith("[VisualAudit] "):
            events.extend((int(frame), event, int(source), int(target), int(amount))
                          for frame, event, source, target, move_id, amount in pattern.findall(line)
                          if move_id == move)
    actual = [event for _, event, source, _, _ in events if source == seat - 1]
    expected = ["MoveStarted", "AttackActive"]
    if outcome == "Hit":
        expected += ["Hit", "HitstunStarted"]
    elif outcome == "Blocked":
        expected += ["Blocked", "BlockstunStarted"]
    if len(events) != len(expected) or actual != expected:
        raise ValueError(f"{scene}: expected {expected} for P{seat}, got {events}")
    return events


def sprite_keys(row):
    return tuple(row.get(key) for key in ("state", "clip", "action", "tick", "frame", "sprite", "sprite_index"))


def validate_captures(lines, scene, move, output, fixed):
    _, seat, _ = SCENARIOS[scene]
    fighter = str(seat - 1)
    logical = {int(row["logic"]) + 1: row for row in audit_rows(lines, "SpriteFrameAudit")
               if row["fighter"] == fighter}
    rendered = {int(row["logic"]): row for row in audit_rows(lines, "SpriteRenderAudit")
                if row["fighter"] == fighter}
    move_rows = [row for row in logical.values() if row.get("move") == move]
    ticks = [int(row["tick"]) for row in move_rows]
    if sorted(set(ticks)) != list(range(12)) or ticks != sorted(ticks):
        raise ValueError(f"{scene}: Action 200 ticks should run 0 through 11 in order, got {ticks}")
    for row in move_rows:
        tick = int(row["tick"])
        index = 0 if tick < 3 else 1 if tick < 7 else 2
        if (row.get("action"), row.get("frame"), row.get("sprite_index"), row.get("duration")) != (
            "200", str(index), ("165", "166", "165")[index], ("3", "4", "5")[index]
        ):
            raise ValueError(f"{scene}: incorrect AIR sprite frame for tick {tick}: {row}")
    begin = int(move_rows[0]["logic"]) + 1
    end = int(move_rows[-1]["logic"]) + 2
    if logical.get(end, {}).get("state") != "Standing" or logical[end].get("action") == "200":
        raise ValueError(f"{scene}: missing Standing/idle boundary at state {end}")

    captures = []
    pattern = re.compile(r"^\[FrameCapture\] render=(\d+) state=(\d+) path=(.+) result=Ok$")
    for line in lines:
        if not (match := pattern.match(line)):
            continue
        draw, state = (int(value) for value in match.group(1, 2))
        path = Path(match[3]).resolve()
        expected = output / "frames" / f"render-{draw:05d}-state-{state:05d}.png"
        if path != expected or not path.is_file():
            raise ValueError(f"{scene}: missing capture for render {draw} state {state}")
        current = rendered.get(state)
        if not current or current.get("sprite_selected") != "True" or sprite_keys(current) != sprite_keys(logical[state]):
            raise ValueError(f"{scene}: render {draw}, state {state} differs from committed sprite projection")
        captures.append({"render": draw, "state": state, "file": str(path), "sprite": current})
    if not captures or [row["state"] for row in captures] != sorted(row["state"] for row in captures):
        raise ValueError(f"{scene}: FramePostDraw captures missing or out of order")
    expected_draws = list(range(26, 68) if fixed else range(30, 34))
    if [row["render"] for row in captures] != expected_draws:
        raise ValueError(f"{scene}: incomplete FramePostDraw capture range")
    captured_states = {row["state"] for row in captures}
    if fixed and not set(range(begin, end + 1)).issubset(captured_states):
        raise ValueError(f"{scene}: fixed capture missed states from {begin} to {end}")
    if not fixed and not any(begin <= state < end for state in captured_states):
        raise ValueError(f"{scene}: realtime capture did not include any displayed State 200 frame")
    if scene in ("guard", "crouch-guard-late"):
        expected_action = "150" if scene == "guard" else "151"
        guard_states = {int(row["logic"]) for row in audit_rows(lines, "SpriteRenderAudit")
                        if row["fighter"] != fighter and row["state"] == "Blockstun"
                        and row.get("sprite_selected") == "True" and row.get("action") == expected_action}
        if not captured_states.intersection(guard_states):
            raise ValueError(f"{scene}: no captured defender Action {expected_action} during Blockstun")
    return {"fighter": fighter, "first_move_state": begin, "standing_state": end, "captures": captures}


def run(options):
    scenes = options.scene or list(SCENARIOS)
    for scene in scenes:
        versions = {}
        for variant in ("sprite", "plain") if options.compare_plain else ("sprite",):
            output = options.output / scene / variant
            output.mkdir(parents=True)
            sprites = variant == "sprite"
            log = run_godot(options.godot, godot_args(options, scene, output, sprites),
                            output / "duel.log", output / "end.png")
            lines = log.decode("utf-8", errors="replace").splitlines()
            events = validate_events(lines, scene, options.move)
            moves = audit_rows(lines, "MoveAudit")
            if not moves or {row["id"] for row in moves} != {"TerryS200"}:
                raise ValueError(f"{scene}: --move-audit did not prove two active TerryS200 profiles")
            if sprites:
                evidence = validate_captures(lines, scene, options.move, output, options.fps == "fixed")
                evidence.update(events=events, moves=moves)
                (output / "trace.json").write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
            versions[variant] = [line for line in log.splitlines()
                                 if line.startswith((b"[VisualAudit] ", b"[EntityAudit] ", b"[MoveAudit] "))]
        if options.compare_plain:
            require_same(versions["sprite"], versions["plain"], f"{scene} sprite vs plain")
        print(f"{scene}: {options.fps} FPS, outcomes and captured state projection verified", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--godot", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--fighter", default=FIGHTER)
    parser.add_argument("--visual", default=VISUAL)
    parser.add_argument("--move", default=MOVE)
    parser.add_argument("--fps", choices=("fixed", "30", "60"), default="fixed")
    parser.add_argument("--scene", choices=SCENARIOS, action="append")
    parser.add_argument("--compare-plain", action="store_true", help="Compare sprite on/off authority in fixed runs")
    options = parser.parse_args()
    if options.compare_plain and options.fps != "fixed":
        parser.error("--compare-plain requires fixed FPS for identical input timing")
    if options.fps != "fixed" and "crouch-guard-late" in (options.scene or SCENARIOS):
        parser.error("late crouch requires fixed FPS for a reproducible first-contact input")
    options.output = options.output.resolve()
    if (ROOT / "local-mugen").resolve() not in options.output.parents:
        parser.error("--output must be a new directory under local-mugen")
    try:
        options.godot = options.godot.resolve(strict=True)
        options.output.mkdir(parents=True, exist_ok=False)
        run(options)
    except (OSError, ValueError, TimeoutExpired) as error:
        parser.exit(1, f"Duel validation failed: {error}\n")


if __name__ == "__main__":
    main()
