"""Replay the native Terry State 205 Power Wave fixtures in real Godot Vulkan duels."""

import argparse
from dataclasses import replace
from functools import partial
from hashlib import sha256
import json
from pathlib import Path
import re
from subprocess import TimeoutExpired
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Stage19Validation"))
from power_wave_duel import (AUTHORITY, MOVE, NATIVE_MOVES, ROOT, assert_equal, battle_events, native_case,
                             require_same, run_case, run_godot, runtime_capture, verify_contacts,
                             verify_move_instances, visual_timeline)
from state210_duel import CAPTURE


PROFILE_DIR = "data/imported-characters/terry98-stage20-data"
NORMALS = {NATIVE_MOVES[state] for state in (200, 205)}
MIXED = {"mixed-sequential", "mirror-mixed-sequential"}
REACTION_CONTACTS = {
    "whiff-20": (23, 40, "Hit", "Hit", 205, 38, "Airborne"),
    "hit-far-23": (27, 40, "Hit", "Hit", 200, 44, "Hitstun"),
    "guard-23": (26, 40, "Blocked", "Hit", 200, 44, "Blockstun"),
    "guard-far-23": (26, 40, "Blocked", "Hit", 200, 44, "Blockstun"),
}


def fixture(directory, name, mode):
    metadata = json.loads((directory / "run-metadata.json").read_text(encoding="utf-8-sig"))
    native_name = f"{mode}-near-wave-{name}"
    if native_name not in metadata["executed_cases"]:
        raise ValueError(f"case absent from reference metadata: {native_name}")
    reference = directory / f"{native_name}.jsonl"
    case_meta = json.loads((directory / f"{native_name}-metadata.json").read_text(encoding="utf-8-sig"))
    assert_equal(case_meta["trace_sha256"], sha256(reference.read_bytes()).hexdigest(), "native trace SHA-256")
    last_frame = metadata.get("last_frame", 75)
    scene, phases, changes = native_case(reference, name, last_frame=last_frame)
    if "whiff-" in name:
        # The native one-tick jump and NewKOF's short-hop heights differ. Use the Stage 16 neutral jump projection.
        up = "W" if name.startswith("mirror-") else "Up"
        jump = {f"23:down:{up}": f"17:down:{up}", f"24:up:{up}": f"23:up:{up}"}
        keys = tuple(jump.get(step, step) for step in scene.keys)
        scene = replace(scene, keys=keys, differences=("neutral-jump-timing",))
    if mode == "mode2":
        assert_equal(0, sum(move["move"] == MOVE for move in scene.moves), "mode2 source does not use weak Wave")
        if any(change["to"] == 1050 for change in changes):
            # Compare the supported normal and its initial contact. Native 1050 is a different move.
            boundary = next(change["frame"] for change in changes if change["to"] == 1050)
            scene = replace(scene, contacts=tuple(contact for contact in scene.contacts if contact["frame"] < boundary),
                            differences=("native-mode2-1050-outside-scope",))
            # The separate mode has no global 1050 support, so only the common prefix shares this clock.
            phases["end"] = {frame: row for frame, row in phases["end"].items() if frame < boundary}
    if name in MIXED:
        scene = replace(scene, captures=(100, 189))
    return reference, last_frame, (scene, phases, changes)


def realtime_fixture(prepared, name):
    if name != "hit-held-11":
        raise ValueError("the realtime fixture requires --scene hit-held-11")
    scene, phases, changes = prepared
    # Sample D on the first J-release render. At a 30 FPS cap the four following directional edges
    # otherwise stretch over too much of the 11-logic-tick HitPause. Physical ordering is unchanged.
    keys = []
    for step in scene.keys:
        render, action, key = step.split(":")
        render = int(render)
        keys.append(f"{render - (2 if render >= 31 else 0)}:{action}:{key}")
    return replace(scene, keys=tuple(keys)), phases, changes


def verify_routes(scene, phases, events, moves, reentry, _lines):
    starts = sorted((event for event in events if event["kind"] == "MoveStarted"),
                    key=lambda event: (event["frame"], event["source"]))
    checked = []
    for start in starts:
        seat, logic = start["source"], start["frame"]
        if start["move"] != MOVE:
            continue
        previous = moves[seat, logic - 1]
        if previous["move"] not in NORMALS:
            continue
        assert_equal(0, int(moves[seat, logic]["stop"]), "Wave transition can only commit after source HitPause")
        native = next(move for move in scene.moves if move["hostSeat"] == seat and move["frame"] + 14 == logic)
        old_move, old_tick = NATIVE_MOVES[native["from"]], native["stateTime"]
        assert_equal(old_move, previous["move"], "Wave source move immediately before the new instance")
        preceding = next(item for item in reversed(starts) if item["source"] == seat and item["frame"] < logic)
        if preceding["move"] != old_move:
            raise ValueError("Wave source instance does not match the preceding move entry")
        if old_tick < 12:
            if old_tick not in range(4, 7):
                raise ValueError(f"explicit Wave cancel uses invalid source tick {old_tick}")
            assert_equal([], [event for event in events if event["frame"] == logic and event["source"] == seat
                              and event["move"] == old_move], "source cannot settle another event on the cancel tick")
        assert_equal(str(logic), reentry[seat, logic]["started"], "fresh Wave attack instance")
        source_contact = [row for row in events if row["source"] == seat and row["move"] == old_move
                          and preceding["frame"] <= row["frame"] < logic and row["kind"] in ("Hit", "Blocked")]
        expected_contact = [row for row in scene.contacts if row["source"] == seat
                            and preceding["frame"] <= row["frame"] + 14 < logic]
        assert_equal([row["kind"] for row in expected_contact], [row["kind"] for row in source_contact],
                     "native and committed source confirmation")
        stop_ticks = [frame for frame in range(preceding["frame"], logic) if int(moves[seat, frame]["stop"]) > 0]
        assert_equal(11 if source_contact else 0, len(stop_ticks), "source HitPause length before legal Wave")
        native_seat = native["seat"] - 1
        old_frame = native["frame"] - 1
        native_stop = phases["end"][old_frame]["chars"][native_seat]["hitPause"]
        assert_equal(native_stop, int(moves[seat, logic - 1]["stop"]), "pre-transition native HitPause state")
        checked.append({"seat": seat, "source": old_move, "sourceTick": old_tick, "logic": logic,
                        "nativeFrame": native["frame"], "terminal": old_tick >= 12,
                        "confirmation": source_contact[0]["kind"] if source_contact else "whiff",
                        "sourceHitPause": stop_ticks})
    return {"routes": checked}


def verify_stage20_contacts(name, scene, events, moves, damage, profile, *, known_second_light=None, strict_native):
    differences = []
    if name in REACTION_CONTACTS:
        source_frame, frame, source_kind, kind, move, amount, prior = REACTION_CONTACTS[name]
        source = scene.contacts[-1]
        assert_equal((source_frame, source_kind, 0, 1),
                     tuple(source[key] for key in ("frame", "kind", "source", "target")), "native reentry contact")
        actual = next((row for row in events if row["frame"] == frame and row["kind"] == kind), {})
        assert_equal((frame, kind, 0, 1, NATIVE_MOVES[move], amount),
                     tuple(actual.get(key) for key in ("frame", "kind", "source", "target", "move", "amount")),
                     "Stage 18 reaction or native jump difference")
        assert_equal(prior, moves[1, frame - 1]["state"], "target reaction before adjusted contact")
        assert_equal(amount, int(moves[1, frame - 1]["health"]) - int(moves[1, frame]["health"]),
                     "committed second-contact HP")
        differences.append({"native": source, "godot": actual, "priorState": prior})
        scene = replace(scene, contacts=scene.contacts[:-1])
        events = [row for row in events if row is not actual]
    return differences + verify_contacts(scene, events, moves, damage, profile, strict_native=strict_native)


def verify_realtime_route(path):
    trace = json.loads(path.read_text(encoding="utf-8"))
    starts = [row for row in trace["events"] if row["kind"] == "MoveStarted"]
    assert_equal([NATIVE_MOVES[205], MOVE], [row["move"] for row in starts], "actual realtime source and destination")
    first, wave = trace["windows"]
    old_tick = first["clock"][-1] + 1
    if old_tick not in range(4, 7):
        raise ValueError(f"realtime old Action205 tick {old_tick} is outside the cancel window")
    assert_equal(0, wave["clock"][0], "realtime destination begins at action tick 0")
    assert_equal(1, sum(row["kind"] == "Hit" and row["move"] == NATIVE_MOVES[205] for row in trace["events"]),
                 "actual first contact before buffered realtime cancel")
    if len(first["clock"]) == len(set(first["clock"])):
        raise ValueError("realtime first attack did not demonstrate HitPause")
    assert_equal(1, sum(row["kind"] == "ProjectileHit" for row in trace["events"]), "actual realtime Wave contact")
    print(f"realtime 205→1000 at incoming old tick {old_tick}; {trace['rendererFps']}", flush=True)


def verify_sparse_realtime(options, name, scene):
    output = options.output / name / "sprite"
    output.mkdir(parents=True)
    args, (first, last) = runtime_capture(options, scene, output, True, alias="terry98-stage20-default")
    args.remove("--sprite-frame-audit")
    args.append("--entity-audit")
    lines = run_godot(options.godot, args, output / "duel.log", output / "end.png").decode("utf-8").splitlines()
    if any(line.startswith("WARNING: [BattleScene] 帧积压") for line in lines):
        raise ValueError("sparse realtime run exceeded the battle time budget")
    events = battle_events(lines)
    starts = verify_move_instances(scene, events, {}, {}, fixed=False)
    first_hit, = [event for event in events if event["kind"] == "Hit" and event["move"] == NATIVE_MOVES[205]]
    wave_hit, = [event for event in events if event["kind"] == "ProjectileHit"]
    assert_equal(3, first_hit["frame"] - starts[0]["frame"], "205 first active contact")
    assert_equal(12, starts[1]["frame"] - first_hit["frame"], "cancel after 11 frozen logic ticks")
    assert_equal(16, wave_hit["frame"] - starts[1]["frame"], "Wave contact after Action 1000 entry")

    entities = {}
    captures = []
    fps = []
    for line in lines:
        if entity := re.fullmatch(r"\[EntityAudit\] scope=1 state=(\d+) grab=\S+ projectiles=(.*)", line):
            entities[int(entity[1])] = entity[2]
        elif captured := CAPTURE.match(line):
            render, state = map(int, captured.group(1, 2))
            image = output / "frames" / f"render-{render:05d}-state-{state:05d}.png"
            assert_equal(image, Path(captured[3]).resolve(), "sparse capture file")
            if not image.is_file():
                raise ValueError(f"missing actual Vulkan image for render {render}, state {state}")
            captures.append({"render": render, "state": state, "path": str(image), "entities": entities[state]})
        elif rate := re.match(r"Project FPS: (\d+)\b", line):
            fps.append((len(captures), int(rate[1])))
    states = [row["state"] for row in captures]
    assert_equal(list(range(first, last + 1)), [row["render"] for row in captures], "sparse capture sequence")
    assert_equal(sorted(states), states, "sparse capture versions")
    waves = [(row["state"], entity.split(",")) for row in captures for entity in row["entities"].split(";")
             if ",TerryPowerWave1020," in entity]
    if not waves:
        raise ValueError("no Wave in the sparse sample")
    for (previous_state, previous), (state, entity) in zip(waves, waves[1:]):
        assert_equal(previous[:4], entity[:4], "sparse Wave identity and owner")
        if not 0 <= int(previous[-1]) - int(entity[-1]) <= state - previous_state:
            raise ValueError("sparse Wave lifetime advanced outside the committed state interval")
    near_capture = (next((rate for count, rate in reversed(fps) if count == 0), 0),
                    next((rate for count, rate in fps if count == len(captures)), 0))
    if min(near_capture) < int(options.fps) - 5:
        raise ValueError(f"renderer reported {near_capture} FPS around the sparse {options.fps} FPS sample")
    (output / "trace.json").write_text(json.dumps({"events": events, "captures": captures, "fpsAroundCapture": near_capture},
                                                 indent=2) + "\n", encoding="utf-8")
    print(f"sparse realtime {name}: {near_capture} FPS, Wave contact at {wave_hit['frame']}, sampled states {states}", flush=True)


def compare_repeat(output, scene, original):
    actual = output / scene / "sprite/duel.log"
    expected = original / scene / "sprite/duel.log"
    archived = [line for line in expected.read_bytes().splitlines() if line.startswith(AUTHORITY)]
    repeated = [line for line in actual.read_bytes().splitlines() if line.startswith(AUTHORITY)]
    if not archived:
        raise ValueError(f"missing authority rows in {expected}")
    require_same(archived, repeated, f"same-seed Stage 20 replay {scene}")
    print(f"same-seed {scene}: {len(repeated)} exact authority rows", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--godot", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path, help="Fresh output directory")
    parser.add_argument("--reference", required=True, type=Path, action="append")
    parser.add_argument("--profile-dir", default=PROFILE_DIR)
    parser.add_argument("--scene", required=True, action="append")
    parser.add_argument("--mode", choices=("default", "mode2"), default="default")
    parser.add_argument("--fps", choices=("fixed", "30", "60"), default="fixed")
    parser.add_argument("--realtime-window", nargs=2, type=int, metavar=("FIRST", "LAST"))
    parser.add_argument("--realtime-resolution", default="480x270")
    parser.add_argument("--realtime-warmup-frames", type=int, default=0)
    parser.add_argument("--realtime-input-spacing", type=int, choices=(1, 2), default=1)
    parser.add_argument("--disable-vsync", action="store_true")
    parser.add_argument("--require-no-backlog", action="store_true")
    parser.add_argument("--require-clean-sample", action="store_true")
    parser.add_argument("--realtime-sparse-audit", action="store_true", help="Capture at most three renders without per-sprite logging")
    parser.add_argument("--compare-plain", action="store_true")
    parser.add_argument("--repeat-from", type=Path, help="Compare same-seed authority to an earlier fixed output")
    options = parser.parse_args()
    options.recovery = False
    options.output = options.output.resolve()
    if options.fps == "fixed" and options.realtime_window:
        parser.error("--realtime-window requires realtime mode")
    if options.fps != "fixed" and (options.compare_plain or options.mode != "default" or not options.realtime_window):
        parser.error("realtime requires default mode and --realtime-window, without --compare-plain")
    if options.fps != "fixed" and options.repeat_from is not None:
        parser.error("--repeat-from requires fixed mode")
    if options.realtime_window and not 0 < options.realtime_window[0] <= options.realtime_window[1] < options.realtime_window[0] + 90:
        parser.error("--realtime-window must select 1 to 90 increasing positive renders")
    if options.realtime_sparse_audit and (options.fps == "fixed" or options.realtime_window[1] - options.realtime_window[0] > 2):
        parser.error("--realtime-sparse-audit requires 1 to 3 realtime captures")
    try:
        options.output.mkdir(parents=True, exist_ok=False)
        manifest = json.loads((ROOT / "data/imported-characters/terry98/manifest.json").read_text(encoding="utf-8-sig"))
        timelines = {action: visual_timeline(manifest, action) for action in (200, 205, 210, 211, 215, 1000, 1020)}
        profile = json.loads((ROOT / options.profile_dir / f"{options.mode}.fighter.json").read_text(encoding="utf-8-sig"))
        failed = []
        for name in options.scene:
            try:
                file = f"{options.mode}-near-wave-{name}.jsonl"
                directory = next((directory for directory in options.reference if (directory / file).is_file()), None)
                if directory is None:
                    raise ValueError(f"native source missing: {file}")
                reference, last_frame, prepared = fixture(directory, name, options.mode)
                if options.fps != "fixed":
                    prepared = realtime_fixture(prepared, name)
                if options.realtime_sparse_audit:
                    verify_sparse_realtime(options, name, prepared[0])
                    continue
                run_case(options, name, reference, timelines, profile, mode=options.mode,
                         alias=f"terry98-stage20-{options.mode}", prepared_scene=prepared, fixed_checks=verify_routes,
                         contact_check=partial(verify_stage20_contacts, name),
                         minimum_render_frames=last_frame + 25, partial_capture=name in MIXED)
                if options.fps != "fixed":
                    verify_realtime_route(options.output / name / "sprite/trace.json")
                if options.repeat_from is not None:
                    compare_repeat(options.output, name, options.repeat_from)
            except (OSError, ValueError, KeyError, IndexError, TimeoutExpired) as error:
                print(f"{name}: FAIL {error!r}", flush=True)
                failed.append(name)
        if failed:
            raise ValueError("failed fixtures: " + ", ".join(failed))
    except (OSError, ValueError) as error:
        parser.exit(1, f"Stage 20 validation failed: {error}\n")


if __name__ == "__main__":
    main()
