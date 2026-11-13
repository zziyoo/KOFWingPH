"""Capture Stage 17 damage and animation from real Godot duel inputs."""

import argparse
from functools import partial
import json
from pathlib import Path
from subprocess import TimeoutExpired
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Stage16Validation"))
from normals_duel import (ACTIONS, AUTHORITY, BOOT_KEYS, HEAVY, LIGHT, Move, NEAR_HEAVY, NEAR_LIGHT,
                          ROOT, Scene, audit_rows, battle_events, hold, press, require_same, run_godot,
                          runtime_args, validate_captures, validate_events, validate_moves,
                          verify_scripted_keys, visual_timeline)


MODES = {"default": "TerryDamage", "mode2": "TerryDamageMode2"}
DAMAGE_AUTHORITY = (*AUTHORITY, b"[DamageAudit] ")


def direct(distance, key, move, *, seat=0, results=("Hit",), pulses=(), extra=()):
    return Scene(distance / 2, press(28, key) + extra, (Move(seat, move, results, heavy_pulses=pulses),))


def carry(light_start, heavy_start, light_interruption):
    return Scene(40, press(light_start, "Comma") + press(heavy_start, "K"),
                 (Move(1, LIGHT, interrupted_at=light_interruption), Move(0, HEAVY, ("Hit",))), captures=(23, 87))


SCENES = {
    "light-near": direct(44, "J", NEAR_LIGHT),
    "light-far": direct(60, "J", LIGHT),
    "heavy-near": direct(62, "K", NEAR_HEAVY, results=("Hit", "Hit"), pulses=(3, 6)),
    "heavy-far": direct(72, "K", HEAVY),
    "far-trade": Scene(36, press(28, "K") + press(30, "Comma"),
                       (Move(0, HEAVY, ("Hit",), 5), Move(1, LIGHT, ("Hit",), 3))),
    "near-trade": Scene(22, press(28, "J") + press(28, "Period"),
                        (Move(0, NEAR_LIGHT, ("Hit",), 3), Move(1, NEAR_HEAVY, ("Hit",), 3))),
    "near-heavy-trade": Scene(31, press(28, "K") + press(28, "Comma"),
                              (Move(0, NEAR_HEAVY, ("Hit",), 3), Move(1, LIGHT, ("Hit",), 3))),
    "interrupt": Scene(36, press(28, "K") + press(28, "Comma"),
                       (Move(0, HEAVY, interrupted_at=3), Move(1, LIGHT, ("Hit",)))),
    "second-only": direct(64, "K", NEAR_HEAVY, pulses=(6,), extra=hold(28, 29, "Right")),
    "carry-live": carry(25, 28, 8),
    "carry-edge": carry(24, 28, 9),
    "carry-last": carry(24, 29, 10),
    "carry-expired": carry(24, 31, 12),
    "mirror-heavy-near": direct(62, "Period", NEAR_HEAVY, seat=1, results=("Hit", "Hit"), pulses=(3, 6)),
    "mirror-heavy-far": direct(72, "Period", HEAVY, seat=1),
    "mirror-far-trade": Scene(36, press(28, "Period") + press(30, "J"),
                              (Move(1, HEAVY, ("Hit",), 5), Move(0, LIGHT, ("Hit",), 3))),
    "mirror-second-only": direct(64, "Period", NEAR_HEAVY, seat=1, pulses=(6,), extra=hold(28, 29, "A")),
}

GUARD_SCENES = {
    f"{prefix}{kind}-guard": Scene(31, press(28, attack_key) + hold(start, 79, back_key),
                                  (Move(seat, move, ("Blocked",) * count, heavy_pulses=(3, 6) if count == 2 else ()),),
                                  guard_action=150)
    for prefix, seat, attack_key, back_key in (("", 0, "K", "Right"), ("mirror-", 1, "Period", "A"))
    for kind, start, move, count in (("heavy", 31, NEAR_HEAVY, 2), ("heavy-far", 27, HEAVY, 1))
}


def alias(mode):
    return f"terry98-stage17-{mode}"


def fixture(mode, name):
    scene = (SCENES | GUARD_SCENES)[name]
    if mode == "mode2" and name in ("carry-live", "carry-edge", "carry-last"):
        # Action 211's wider startup hurtbox is reached by P2's far light, unlike Action 210.
        return Scene(scene.half_distance, scene.keys,
                     (Move(1, LIGHT, ("Hit",)), Move(0, HEAVY, interrupted_at=1)), captures=scene.captures)
    return scene


def run_scene(options, mode, name, timelines):
    scene = fixture(mode, name)
    modes = ("default", mode) if name.startswith("mirror-") else (mode, "default")
    authority = []
    for variant in ("sprite", "plain") if options.compare_plain else ("sprite",):
        output = options.output / f"{mode}-damage-{name}" / variant
        output.mkdir(parents=True)
        sprites = variant == "sprite"
        args, capture = runtime_args(options, scene, output, sprites, modes, visual_alias=alias, realtime_tail=36)
        args.append(f"--damage-seed={options.damage_seed}")
        if options.fps == "fixed":
            args.append("--damage-audit")
        log = run_godot(options.godot, args, output / "duel.log", output / "end.png")
        lines = log.decode("utf-8", errors="replace").splitlines()
        if options.require_no_backlog and any(line.startswith("WARNING: [BattleScene] 帧积压") for line in lines):
            raise ValueError("realtime capture exceeded the battle time budget")
        events = battle_events(lines)
        moves = audit_rows(lines, "MoveAudit")
        damage = audit_rows(lines, "DamageAudit")
        committed = {(int(row["fighter"]), int(row["logic"])): row for row in moves}
        fixed = options.fps == "fixed"
        if fixed:
            verify_scripted_keys(lines, BOOT_KEYS + scene.keys)
            for seat, preset in enumerate(modes):
                seat_rows = [row for row in moves if row["fighter"] == str(seat)]
                actual = {row["id"] for row in seat_rows}
                if actual != {MODES[preset]} or int(seat_rows[0]["x"]) != (2 * seat - 1) * scene.half_distance * 65536:
                    raise ValueError(f"seat {seat + 1}: unexpected profile or initial position {actual}")
            keys = [(int(row["fighter"]), int(row["logic"])) for row in damage]
            if len(keys) != len(committed) or set(keys) != set(committed):
                raise ValueError("damage audit and move audit do not describe the same committed seat states")
        elif any(row["kind"] == "Hit" and row["amount"] <= 0 for row in events):
            raise ValueError("realtime contact produced no damage")
        pulses = validate_events(scene, events, committed)
        if sprites:
            windows, images = validate_captures(scene, events, lines, output, capture, timelines, modes, fixed,
                                                move_validator=partial(validate_moves, check_close_light=True), check_reactions=False)
            if not fixed:
                visible = [image for image in images if any(window["begin"] <= image["state"] < window["end"]
                                                           for window in windows)]
                if not visible or len({image["state"] for image in visible}) < 2:
                    raise ValueError("realtime samples lack distinct committed states in the attack window")
            evidence = {"seed": options.damage_seed, "modes": modes, "events": events, "moves": moves,
                        "damage": damage, "windows": windows, "captures": images, "heavy_pulse_ticks": pulses}
            (output / "trace.json").write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
        rows = [line for line in log.splitlines() if line.startswith(DAMAGE_AUTHORITY)]
        if options.repeat_of:
            original = options.repeat_of / output.parent.name / variant / "duel.log"
            expected = [line for line in original.read_bytes().splitlines() if line.startswith(DAMAGE_AUTHORITY)]
            require_same(expected, rows, f"{mode}-{name} {variant} repeated authority")
        authority.append(rows)
    if options.compare_plain:
        require_same(*authority, f"{mode}-{name} sprite vs plain")
    print(f"{mode}-{name}: Vulkan images and events verified, {len(authority[0])} authority rows", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--godot", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--profile-dir", default="data/imported-characters/terry98-stage17-data")
    parser.add_argument("--damage-seed", type=int, default=1337)
    parser.add_argument("--fps", choices=("fixed", "30", "60"), default="fixed")
    parser.add_argument("--realtime-window", nargs=2, type=int, metavar=("FIRST", "LAST"))
    parser.add_argument("--realtime-resolution", default="640x360")
    parser.add_argument("--require-no-backlog", action="store_true")
    parser.add_argument("--mode", choices=MODES, action="append")
    parser.add_argument("--scene", choices=SCENES | GUARD_SCENES, action="append")
    parser.add_argument("--compare-plain", action="store_true")
    parser.add_argument("--repeat-of", type=Path, help="Compare every authority row against a prior fixed run")
    options = parser.parse_args()
    options.mode = options.mode or list(MODES)
    options.scene = options.scene or (list(SCENES) if options.fps == "fixed" else ["heavy-near"])
    if not 0 <= options.damage_seed <= 0xffffffff:
        parser.error("--damage-seed must be an unsigned 32-bit integer")
    if options.fps != "fixed" and (options.compare_plain or options.repeat_of or
                                   set(options.scene) - {"heavy-near", "mirror-heavy-far"}):
        parser.error("realtime permits direct near heavy and mirrored far heavy with visual cover enabled")
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
        manifest = json.loads((ROOT / "data/imported-characters/terry98/manifest.json").read_text(encoding="utf-8-sig"))
        timelines = {action: visual_timeline(manifest, action) for action in (*ACTIONS.values(), 210, 211)}
        failed = []
        for mode in options.mode:
            for scene in options.scene:
                try:
                    run_scene(options, mode, scene, timelines)
                except (KeyError, OSError, ValueError, TimeoutExpired) as error:
                    print(f"{mode}-{scene}: FAIL {error!r}", flush=True)
                    failed.append(f"{mode}-{scene}")
        if failed:
            raise ValueError("failed fixtures: " + ", ".join(failed))
    except (OSError, ValueError) as error:
        parser.exit(1, f"Stage 17 duel validation failed: {error}\n")


if __name__ == "__main__":
    main()
