"""Compare physical-input State 230/235 duels against native and Vulkan traces."""

import argparse
from dataclasses import replace
from functools import partial
import json
from pathlib import Path
from subprocess import TimeoutExpired
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'Stage25Validation'))
from crouch_light_kick_duel import (AUTHORITY, CONTACTS, EXTRA_AUTHORITY, NATIVE, REACTIONS, RESERVATIONS,
                                   ROOT, VISUALS, assert_equal, audit_rows, check_rng, check_wave_eligibility,
                                   check_wave_launch, fighter_poses, fixture, indexed, local_boxes, locate,
                                   native_boxes, native_damage, normal_damage, require_same, run_case,
                                   sparse_realtime, verify_commands, verify_grounded_wave_recovery,
                                   verify_wave_ground_poses, visual_timeline)
from power_wave_duel import Q16


KICKS = {230: 'TerryStandLightKick230', 235: 'TerryStandLightKick235'}
NATIVE_MOVES = {**NATIVE, **KICKS}
ACTIONS = {**VISUALS, **{move: state for state, move in KICKS.items()}}
FAMILY = 'standing-light-kick'
PROFILE = 'data/imported-characters/terry98-stage26-final'
DEFERRED = {**{state: name for state, name in RESERVATIONS.items() if state not in KICKS},
            720: 'ReservedGroundRecovery'}
AIRBORNE = {'Airborne', 'AirHitstun', 'AirBlockstun'}
LENGTH = {230: 19, 235: 16}
ACTIVE = {230: range(5, 8), 235: range(4, 9)}


def body_distance(profile, moves, seat, logic, *, target_acted=False):
    own = moves[seat, logic - 1]
    target = moves[seat ^ 1, logic if target_acted else logic - 1]
    facing = 1 if own['facing'] == 'Right' else -1
    target_facing = 1 if target['facing'] == 'Right' else -1
    delta = (int(target['x']) - int(own['x'])) * facing
    use_front = (delta >= 0) == (facing != target_facing)
    target_widths = profile['airBodyWidths' if target['state'] in AIRBORNE else 'groundBodyWidths']

    def width(widths, edge):
        value = widths[edge]
        return value['numerator'] * Q16 // value['denominator']

    target_edge = width(target_widths, 'front' if use_front else 'back')
    raw = delta - width(profile['groundBodyWidths'], 'front') + target_edge * facing * target_facing * (
        1 if use_front else -1)
    return {'raw': raw, 'distance': raw / Q16, 'targetState': target['state']}


def verify_selection(profile, phases, moves, commands, name):
    selected = []
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    for commit in commands['commits']:
        entry, row = commit['native'], commit['host']
        if entry['to'] not in KICKS:
            continue
        logic, seat = int(row['logic']), entry['hostSeat']
        frame_start = body_distance(profile, moves, seat, logic)
        current = frame_start
        if name == 'recovery-backstep-own-kick':
            assert_equal((1, 'Hitstun'), (seat, row['before']), 'recovery probe consumes in P2 after P1 acted')
            current = body_distance(profile, moves, seat, logic, target_acted=True)
            assert_equal(207616, frame_start['raw'] - current['raw'], 'P1 approach on the recovery tick')
            assert_equal((True, True), (frame_start['raw'] > 28 * Q16, current['raw'] <= 28 * Q16),
                         'same-tick approach changes the close-kick decision')
        native = entry['bodyDistX']
        assert_equal(235 if native <= 28 else 230, entry['to'], 'native distance selection')
        assert_equal(235 if current['raw'] <= 28 * Q16 else 230, entry['to'], f'host distance selection at L{logic}')
        assert_equal(False, bool(entry['raw'] & 2), 'standing kick consumes current direction without Down')
        assert_equal(False, 'Down' in row['direction'], 'host standing kick does not consume a crouch direction')
        move = next(item for item in profile['moves'] if item['moveId'] == KICKS[entry['to']])
        consumed = [parts for candidate in row['candidates'].split(';') if
                    (parts := candidate.split(','))[0] == move['commandId'] and parts[-1] == 'True']
        button_frame = max(int(parts[2]) for parts in consumed)
        assert_equal(True, 0 <= logic - button_frame <= 2, 'standing kick consumed a live physical edge')
        if entry['aBuffer']:
            assert_equal(logic, button_frame, 'native and host fresh kick edge')
        else:
            assert_equal(3 - entry['helperA'], logic - button_frame, 'native and host buffered kick age')
        before_edge = phases['before-command'][button_frame - 14]['chars'][seat ^ mirror]
        assert_equal(True, bool(before_edge['raw'] & 16), 'native physical kick was pressed at its host edge')
        edge = body_distance(profile, moves, seat, button_frame)
        if name in ('threshold-below', 'threshold-exact', 'threshold-above', 'mirror-threshold-exact',
                    'p2-threshold-exact', 'crouch-threshold-exact', 'air-threshold-low',
                    'air-threshold-exact', 'air-threshold-high', 'air-body-threshold-below',
                    'air-body-threshold-exact', 'air-body-threshold-above'):
            assert_equal(round(native * Q16), current['raw'], 'exact native fixed-point threshold sample')
        if name in ('recovery-backstep-distance', 'recovery-backstep-own-kick') and abs(native - current['distance']) > 0.005:
            raise ValueError(f'ground recovery changed body distance at selection: native {native}, host {current}')
        if name in ('buffer-cross-toward', 'buffer-cross-away', 'sweep-buffer-ground-cross'):
            near_on_press = name == 'buffer-cross-away'
            assert_equal(True, button_frame < logic, 'light kick was buffered before consumption')
            assert_equal((near_on_press, not near_on_press), (before_edge['bodyDistX'] <= 28, native <= 28),
                         'native buffered distance crosses the close threshold')
            assert_equal((near_on_press, not near_on_press), (edge['raw'] <= 28 * Q16, current['raw'] <= 28 * Q16),
                         'host buffered distance crosses the close threshold')
        if name == 'sweep-buffer-ground-remain-far':
            assert_equal((True, True), (before_edge['bodyDistX'] > 28, native > 28), 'native remains outside 28')
            assert_equal((True, True), (edge['raw'] > 28 * Q16, current['raw'] > 28 * Q16), 'host remains outside 28')
        selected.append({'logic': logic, 'source': entry['from'], 'oldTick': entry['stateTime'],
                         'target': entry['to'], 'seat': seat, 'edgeLogic': button_frame,
                         'edgeNative': before_edge['bodyDistX'], 'edgeHost': edge,
                         'consumeNative': native, 'consumeHost': current, 'hostFrameStart': frame_start,
                         'candidates': row['candidates']})
    return selected


def verify_recovery_boundary(name, phases, moves):
    if name not in ('recovery-backstep-distance', 'recovery-backstep-own-kick'):
        return None
    recovered = 32
    native_p2 = [phases['end'][frame]['chars'][1]['pos'][0] for frame in (recovered - 1, recovered, recovered + 1)]
    host_p2 = [int(moves[1, frame + 14]['x']) for frame in (recovered - 1, recovered, recovered + 1)]
    assert_equal(native_p2[0], native_p2[1], 'native leaves hitstun without a same-tick walk')
    assert_equal(host_p2[0], host_p2[1], 'host leaves hitstun without a same-tick walk')
    step = 207616 if name == 'recovery-backstep-distance' else 0
    assert_equal(step, round((native_p2[2] - native_p2[1]) * Q16), 'native post-recovery movement')
    assert_equal(step, host_p2[2] - host_p2[1], 'host post-recovery movement')
    return {'nativeFrame': recovered, 'nativeP2X': native_p2, 'hostP2X': [raw / Q16 for raw in host_p2]}


def wave_attempts(profile, phases, commands, *, lengths=LENGTH, explicit_windows=None):
    if explicit_windows is None:
        explicit_windows = {235: range(5, 9)}
    wave_ids = {item['commandId'] for item in profile['commands'] if item.get('matchHeldMotion')}
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    host = indexed(commands['buffer'])
    attempts = []
    for frame, world in phases['after-command'].items():
        for native_seat, fighter in enumerate(world['chars']):
            state = fighter['state']
            if state not in lengths or not fighter['powerWaveCommand']:
                continue
            seat, logic = native_seat ^ mirror, frame + 14
            row = host[seat, logic]
            candidates = [value.split(',') for value in row['candidates'].split(';')]
            prior = phases['after-command'].get(frame - 1)
            fresh_x = fighter['raw'] & 128 and (prior is None or not prior['chars'][native_seat]['raw'] & 128)
            if fresh_x:
                assert_equal(True, any(item[0] in wave_ids and item[2] == str(logic) for item in candidates),
                             f'host recognizes the native root Wave gesture at F{frame}')
            if fighter['time'] < lengths[state] and fighter['time'] not in explicit_windows.get(state, ()):
                assert_equal(False, row['committed'] == NATIVE_MOVES[1000], f'no direct Wave permission at F{frame}')
            attempts.append({'frame': frame, 'seat': seat, 'source': state, 'sourceTick': fighter['time'],
                             'helperWave': fighter['helperWave'], 'host': row})
    return attempts


def verify_body(profile, changes, name, scene, phases, events, moves, reentry, lines, *,
                states=KICKS, lengths=LENGTH, active=ACTIVE, reservations=DEFERRED, native_moves=NATIVE_MOVES,
                explicit_windows=None, selection_check=verify_selection, recovery_boundary=verify_recovery_boundary,
                wave_launch_check=check_wave_launch):
    if explicit_windows is None:
        explicit_windows = {235: range(5, 9)}
    attacks = {move['moveId']: move['attacks'][0] for move in profile['moves'] if move['moveId'] in states.values()}
    damage = indexed(audit_rows(lines, 'DamageAudit'))
    assert_equal(set(moves), set(damage), 'complete damage audit')
    assert_equal(set(moves), set(reentry), 'complete attack instance audit')
    check_rng(profile, damage, lines)
    commands = verify_commands(profile, scene, phases, changes, events, moves, damage, lines, reservations=reservations,
                               sweep_kick_targets=(430, *KICKS))
    commits = {(commit['native']['hostSeat'], int(commit['host']['logic'])): commit['host']
               for commit in commands['commits']}
    mirror = name.startswith('mirror-')
    initial = phases['before-command'][0]['chars']
    for seat in (0, 1):
        native_x = initial[seat ^ mirror]['pos'][0]
        assert_equal(round(native_x * Q16), int(moves[seat, 13]['x']), 'exact native initial coordinate')
    last = min(max(phases['after-collision']) + 14, max(logic for _, logic in moves))
    comparable_end = min((change['frame'] + 13 for change in changes if change['to'] in reservations), default=last)
    samples = []
    for entry in scene.moves:
        if entry['to'] not in states:
            continue
        state, start, seat = entry['to'], entry['frame'] + 14, entry['hostSeat']
        compared_through = start - 1
        for logic in range(start, min(last, comparable_end) + 1):
            row = moves[seat, logic]
            if row['move'] != states[state] or reentry[seat, logic]['started'] != str(start):
                break
            source = phases['after-collision'][logic - 14]['chars'][seat ^ mirror]
            elapsed = int(row['elapsed'])
            assert_equal((state, 1, elapsed + 1), (source['state'], source['stateType'], source['time']),
                         f'native standing kick clock at L{logic}')
            assert_equal(elapsed in active[state], bool(row['attacks']), f'State {state} active window at L{logic}')
            assert_equal(native_boxes(source, 'clsn1'), local_boxes(row, 'attacks'), f'State {state} Clsn1 at L{logic}')
            assert_equal(native_boxes(source, 'clsn2'), local_boxes(row, 'hurt'), f'State {state} Clsn2 at L{logic}')
            compared_through = logic
        samples.append({'state': state, 'seat': seat, 'start': start, 'lastCompared': compared_through})

    contacts = []
    for event in events:
        if event['move'] not in attacks or event['kind'] not in ('Hit', 'Blocked'):
            continue
        logic, seat, target = (event[key] for key in ('frame', 'source', 'target'))
        attack = attacks[event['move']]
        blocked = event['kind'] == 'Blocked'
        stop = attack['hitstopByOutcome']['block' if blocked else 'hit']
        assert_equal((stop['attackerFrames'], stop['defenderFrames']),
                     (int(moves[seat, logic]['stop']), int(moves[target, logic]['stop'])), 'standing kick HitPause')
        amount = 0 if blocked else normal_damage(profile, damage, moves, event, attack)
        assert_equal(amount, event['amount'], 'standing kick dynamic damage')
        assert_equal(amount, int(moves[target, logic - 1]['health']) - int(moves[target, logic]['health']),
                     'standing kick atomic HP')
        assert_equal(str(logic), reentry[seat, logic]['blockConfirm' if blocked else 'hitConfirm'], 'contact ownership')
        source = phases['after-collision'][logic - 14]['chars'][seat ^ mirror]
        original = native_damage(profile, phases, attack, logic - 14, seat ^ mirror, blocked, state=source['state'])
        contacts.append({**event, 'native': original, 'tick': int(moves[seat, logic]['elapsed'])})

    exits = []
    for change in changes:
        if change['from'] not in states:
            continue
        logic, seat, target = change['frame'] + 14, (change['seat'] - 1) ^ mirror, change['to']
        assert_equal(True, logic <= last, 'standing kick exit covered')
        if target in (0, 10, 20):
            next_state = 'Crouching' if target == 10 else 'Standing'
            assert_equal((lengths[change['from']], next_state), (change['stateTime'], moves[seat, logic]['state']),
                         'natural standing kick exit')
        if target in REACTIONS:
            assert_equal(('-', ''), (moves[seat, logic]['move'], moves[seat, logic]['attacks']), 'interruption clears attack')
        if target in native_moves:
            assert_equal(change['stateTime'], int(moves[seat, logic - 1]['elapsed']) + 1, 'source effective move tick')
            assert_equal(('0', '1', ''), (commits[seat, logic]['hitpause'], damage[seat, logic]['divisor'],
                                          moves[seat, logic]['attacks']), 'new action clears the previous instance')
        if target in (1000, 1050):
            source = change['from']
            tick = change['stateTime']
            natural = tick == lengths[source]
            assert_equal((0, 1) if natural else (1, 0), (change['var1'], change['var2']), 'Wave permission source')
            if not natural:
                assert_equal(True, tick in explicit_windows.get(source, ()), 'only the authored active window permits Wave')
            assert_equal(True, change['powerWaveCommand'] or change['helperWave'] > 0, 'Wave command or Helper buffer')
        assert_equal([], [event for event in events if event['frame'] == logic and event['source'] == seat and
                          event['kind'] in CONTACTS and event['move'] == states[change['from']]], 'no stale kick contact')
        exits.append({'logic': logic, 'seat': seat, 'source': change['from'], 'sourceTick': change['stateTime'],
                      'nativeDestination': target, 'explicitWavePermission': change['var1'],
                      'naturalPermission': change['var2']})
    eligible = {frame: {**world, 'chars': [{**char, 'helpers': [helper for helper in char['helpers'] or []
                                                              if helper['moveContact'] == 0]}
                                         for char in world['chars']]} for frame, world in phases['end'].items()}
    return {'kickWindows': samples, 'kickContacts': contacts, 'kickExits': exits, 'commands': commands,
            'distanceSelections': selection_check(profile, phases, moves, commands, name),
            'recoveryMovement': recovery_boundary(name, phases, moves),
            'standingWaveAttempts': wave_attempts(profile, phases, commands, lengths=lengths,
                                                 explicit_windows=explicit_windows),
            'waveLaunch': wave_launch_check(name, scene, phases, moves, lines),
            'waveEligibility': check_wave_eligibility({**phases, 'end': eligible}, lines)}


def kick_contacts(scene, phases, *, states=KICKS):
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    return (row for row in scene.contacts if row['kind'] in ('Hit', 'Blocked') and
            phases['after-collision'][row['frame']]['chars'][row['source'] ^ mirror]['state'] in states)


def check_recovery(deferred_frame, scene, phases, moves, *, states=KICKS, contact_origins=None, wave_origins=None):
    result = verify_grounded_wave_recovery(scene, phases, moves, crouch_recovery=True, contact_origins=wave_origins)
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    for contact in kick_contacts(scene, phases, states=states):
        first, seat = contact['frame'], contact['target']
        defender = seat ^ mirror
        if phases['before-command'][first]['chars'][defender]['stateType'] == 4:
            continue
        following = min(deferred_frame, next((row['frame'] for row in scene.contacts
                                             if row['frame'] > first and row['target'] == seat),
                                            max(phases['end']) + 1))
        blocked = contact['kind'] == 'Blocked'
        x_offset = 0 if contact_origins is None else contact_origins(phases, moves, first, seat)
        for frame in range(first, following):
            native = phases['end'][frame]['chars'][defender]
            actual = moves[seat, frame + 14]
            state = ('Crouching' if native['stateType'] == 2 else 'Standing') if native['ctrl'] else (
                'Blockstun' if blocked else 'Hitstun')
            assert_equal(state, actual['state'], f'standing kick defender at F{frame}')
            error = abs(native['pos'][0] + x_offset - int(actual['x']) / Q16)
            if error > 0.005:
                raise ValueError(f'standing kick defender X differs at F{frame} by {error}')
            if frame > first and not native['ctrl'] and native['state'] in REACTIONS:
                assert_equal(native_boxes(native, 'clsn2'), local_boxes(actual, 'hurt'), f'reaction Clsn2 at F{frame}')
            if native['ctrl']:
                result.append({'contact': first, 'recovered': frame, 'seat': seat, 'nativeState': native['state'],
                               'positionErrorX': error, **({'legacyContactXOffset': x_offset} if x_offset else {})})
                break
        else:
            if following > max(phases['end']):
                raise ValueError('native trace ends before standing kick recovery')
    return result


def check_poses(phases, deferred_frame, scene, events, committed, timelines, modes, *,
                states=KICKS, actions=ACTIONS, reactions=(150, 151, 5010, 5015, 5020, 5025)):
    windows = fighter_poses(scene, events, committed, timelines, modes, actions=actions)
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    for event in kick_contacts(scene, phases, states=states):
        first, seat = event['frame'], event['target']
        following = min(deferred_frame, next((row['frame'] for row in scene.contacts
                                             if row['frame'] > first and row['target'] == seat),
                                            max(phases['after-collision']) + 1))
        for frame in range(first + 1, following):
            native = phases['after-collision'][frame]['chars'][seat ^ mirror]
            rendered = committed.get((seat, frame + 15))
            if rendered is None or native['anim'] not in reactions:
                break
            assert_equal((str(native['anim']), ','.join(map(str, native['frame']['sprite']))),
                         (rendered['action'], rendered['sprite']), f'standing kick defender image at F{frame}')
    verify_wave_ground_poses(scene, phases, committed)
    return windows


def verify_captured_actions(path, scene, states):
    trace = json.loads(path.read_bytes())
    captured = {int(fighter['action']) for image in trace['captures'] for fighter in image['fighters']
                if fighter['action'].isdigit()}
    needed = {entry['to'] for entry in scene.moves} & states.keys()
    for state in needed:
        if (action := states[state]) not in captured:
            raise ValueError(f'no actual Vulkan image includes Action {action}; select its render window')


def validation_options(description, profile_dir):
    parser = argparse.ArgumentParser(description=description)
    parser.add_argument('--godot', required=True, type=Path)
    parser.add_argument('--reference', required=True, type=Path, action='append')
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--profile-dir', default=profile_dir)
    parser.add_argument('--mode', choices=('default', 'mode2'), default='default')
    parser.add_argument('--visual-alias', help='Local alias for the selected profile and mode')
    parser.add_argument('--scene', required=True, action='append')
    parser.add_argument('--compare-plain', action='store_true')
    parser.add_argument('--repeat-from', type=Path)
    parser.add_argument('--fps', choices=('fixed', '30', '60'), default='fixed')
    parser.add_argument('--fixed-resolution', choices=('640x360', '320x180'), default='640x360')
    parser.add_argument('--fixed-window', type=int, nargs=2)
    parser.add_argument('--timeout', type=int, default=50, help='Maximum seconds per fixed Godot process')
    parser.add_argument('--realtime-render', type=int)
    options = parser.parse_args()
    fixed = options.fps == 'fixed'
    if not fixed and (not options.realtime_render or options.mode != 'default' or options.compare_plain
                      or options.repeat_from or options.fixed_window or options.timeout != 50):
        parser.error('realtime requires one sparse render with the default mode')
    if options.timeout <= 0:
        parser.error('timeout must be positive')
    if fixed and options.fixed_window and not 0 < options.fixed_window[0] <= options.fixed_window[1] < options.fixed_window[0] + 90:
        parser.error('fixed window must include 1–90 renders')
    options.output = options.output.resolve()
    options.output.mkdir(parents=True, exist_ok=False)
    options.recovery = False
    options.disable_vsync = True
    options.require_no_backlog = False
    options.realtime_resolution = '320x180'
    options.realtime_warmup_frames = 150
    options.realtime_settle_frames = 75
    options.realtime_tail_frames = 150
    options.realtime_input_spacing = 1
    options.realtime_window = (options.realtime_render, options.realtime_render)
    return parser, options


def run_validation(parser, options, *, family, capture_profile, alias, native_moves, reservations, actions, states,
                   body_check, recovery_check, pose_check, contact_check=None, deferred_followups=None):
    fixed = options.fps == 'fixed'
    manifest = json.loads((ROOT / 'data/imported-characters/terry98/manifest.json').read_bytes())
    timelines = {action: visual_timeline(manifest, action)
                 for action in (*sorted(set(actions.values())), 210, 211, 1000, 1020)}
    profile = json.loads((ROOT / options.profile_dir / f'{options.mode}.fighter.json').read_bytes())
    failures = []
    for name in options.scene:
        try:
            reference = locate(options.reference, options.mode, name, family)
            scene, phases, changes = fixture(reference, name, capture_profile=capture_profile,
                                             native_moves=native_moves, reservations=reservations,
                                             deferred_followups=deferred_followups)
            if not fixed:
                sparse_realtime(options, name, scene, alias, timelines, actions=actions,
                                sample_actions=tuple(states.values()))
                continue
            kick_frames = [row['frame'] for row in scene.moves if row['to'] in states]
            first = min(kick_frames, default=5)
            last = max((row['frame'] for row in scene.moves if row['frame'] >= first), default=first)
            # Capture key poses locally, while retaining full audit through the final contact and recovery.
            captures = tuple(options.fixed_window or (first + 23, min(first + 100, last + 52)))
            scene = replace(scene, captures=captures)
            deferred_frame = min((row['frame'] for row in changes if row['to'] in reservations),
                                 default=max(phases['after-collision']) + 1)
            run_case(options, name, reference, timelines, profile, mode=options.mode, alias=alias,
                     prepared_scene=(scene, phases, changes), fixed_checks=partial(body_check, profile, changes, name),
                     recovery_check=partial(recovery_check, deferred_frame, name),
                     pose_check=partial(pose_check, phases, deferred_frame), partial_capture=True,
                     allow_interrupted_spawn=True, minimum_render_frames=max(phases['after-collision']) + 24,
                     extra_arguments=('--command-audit',), extra_authority=EXTRA_AUTHORITY,
                     **({'contact_check': partial(contact_check, name)} if contact_check else {}))
            verify_captured_actions(options.output / name / 'sprite/trace.json', scene, states)
            if options.repeat_from:
                prefixes = (*AUTHORITY, *EXTRA_AUTHORITY)
                old, new = ([row for row in (folder / name / 'sprite/duel.log').read_bytes().splitlines()
                             if row.startswith(prefixes)] for folder in (options.repeat_from, options.output))
                if not old:
                    raise ValueError('baseline repeat has no authority records')
                require_same(old, new, f'{capture_profile} same-seed {name}')
                print(f'{name}: {len(old)} same-seed authority rows', flush=True)
        except (ValueError, KeyError, IndexError, OSError, StopIteration, TimeoutExpired) as error:
            failures.append(name)
            print(f'{name}: FAIL {error!r}', flush=True)
    if failures:
        parser.exit(1, 'Failed: ' + ', '.join(failures) + '\n')


def main():
    parser, options = validation_options(__doc__, PROFILE)
    run_validation(parser, options, family=FAMILY, capture_profile='Stage26',
                   alias=options.visual_alias or f'terry98-stage26-{options.mode}',
                   native_moves=NATIVE_MOVES, reservations=DEFERRED, actions=ACTIONS,
                   states={state: state for state in KICKS}, body_check=verify_body,
                   recovery_check=lambda deferred, name, *args: check_recovery(deferred, *args), pose_check=check_poses)


if __name__ == '__main__':
    main()
