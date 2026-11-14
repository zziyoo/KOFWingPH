"""Compare Terry State 240/245 physical inputs and Vulkan poses with IKEMEN GO."""

from functools import partial
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'Stage26Validation'))
from standing_light_kick_duel import (ACTIONS, DEFERRED, NATIVE_MOVES, Q16, assert_equal, body_distance,
                                     check_poses, check_recovery, check_wave_launch, run_validation,
                                     validation_options, verify_body)
from power_wave_duel import verify_contacts


KICKS = {240: 'TerryStandHeavyKick240', 245: 'TerryStandHeavyKick245'}
NATIVE = {**NATIVE_MOVES, **KICKS}
DEFERRED_STATES = {**{state: command for state, command in DEFERRED.items() if state not in KICKS},
                   250: 'ReservedGroundBlowback', 805: 'ReservedHeavyKickThrow'}
FAMILY = 'standing-heavy-kick'
PROFILE = 'data/imported-characters/terry98-stage27-final'
NEAR_MAX = 48 * Q16
LENGTH = {'default': {240: 38, 245: 29}, 'mode2': {240: 43, 245: 29}}
ACTIVE = {'default': {240: range(13, 16), 245: range(3, 9)},
          'mode2': {240: range(19, 23), 245: range(3, 9)}}
STEP = {'default': {28: 32}, 'mode2': dict.fromkeys((1, 4, 7, 10, 12), 8)}
REACTION_ACTIONS = (150, 151, 5002, 5007, 5022, 5027)
EXACT_DISTANCE = {'threshold-below', 'threshold-exact', 'threshold-above', 'air-threshold-below',
                  'air-threshold-exact', 'air-threshold-above', 'mirror-threshold-exact',
                  'p2-threshold-exact', 'crouch-threshold-exact'}
CROSS_DISTANCE = {'sweep-b-buffer-toward', 'sweep-b-buffer-away-ground', 'sweep-b-buffer-away-ground-guard',
                  'sweep-b-buffer-release-down'}
SWEEP_DISTANCE = {'sweep-walk-threshold-a-near': (235, 26.140625),
                  'sweep-walk-threshold-a-far': (230, 29.30859375),
                  'sweep-walk-threshold-b-near': (245, 45.1484375),
                  'sweep-walk-threshold-b-far': (240, 48.31640625)}
WAVE_CAMERA_TICKS = {'near-wave-hit-4': 109, 'near-wave-pause-3': 109}
DEFERRED_FOLLOWUPS = {'wakeup-reservation-95': [(1, 805, 245)]}


def verify_heavy_selection(profile, phases, moves, commands, name):
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    selected = []
    for commit in commands['commits']:
        entry, row = commit['native'], commit['host']
        if entry['to'] not in KICKS:
            continue
        logic, seat = int(row['logic']), entry['hostSeat']
        consumed = body_distance(profile, moves, seat, logic)
        native_distance = entry['bodyDistX']
        assert_equal(245 if native_distance <= 48 else 240, entry['to'], 'native body distance selection')
        assert_equal(245 if consumed['raw'] <= NEAR_MAX else 240, entry['to'], f'host body distance at L{logic}')
        assert_equal(False, bool(entry['raw'] & 2), 'native b consumes with no Down')
        assert_equal(False, 'Down' in row['direction'], 'host b consumes with no Down')
        move = next(item for item in profile['moves'] if item['moveId'] == KICKS[entry['to']])
        candidate_edges = [int(button) for command, _, button, _, _, valid in
                           (candidate.split(',') for candidate in row['candidates'].split(';'))
                           if command == move['commandId'] and valid == 'True']
        button_frame = max(candidate_edges)
        age = logic - button_frame
        if entry['bBuffer'] and age:
            countdown = [phases['before-command'][tick - 14]['chars'][seat ^ mirror]
                         for tick in range(button_frame + 1, logic)]
            assert_equal(True, any(char['hitPause'] > 0 and char['commands']['b'][0] > 0 for char in countdown),
                         'aged root b must have been kept by native HitPause')
        else:
            assert_equal(True, 0 <= age <= 2, 'standing b consumes a live physical edge')
            assert_equal(0 if entry['bBuffer'] else 3 - entry['helperB'], age, 'native and host b buffer age')
        native_edge = phases['before-command'][button_frame - 14]['chars'][seat ^ mirror]
        assert_equal(True, bool(native_edge['raw'] & 32), 'native physical b was pressed on the host edge')
        host_edge = body_distance(profile, moves, seat, button_frame)
        edge_world = phases['after-command'][button_frame - 14]
        if name.startswith('far-global-pause-b-') and age:
            assert_equal(1, edge_world['globalPause'], 'physical edge sampled during global Pause')
            assert_equal('False', row['globalpause'], 'deferred standing b commits after global Pause')
        if name in EXACT_DISTANCE:
            assert_equal(round(native_distance * Q16), consumed['raw'], 'exact Q16 threshold sample')
        if name in CROSS_DISTANCE:
            assert_equal(True, age > 0, 'threshold cross uses a buffered b')
            assert_equal((native_edge['bodyDistX'] <= 48, native_distance <= 48),
                         (host_edge['raw'] <= NEAR_MAX, consumed['raw'] <= NEAR_MAX),
                         'native and host b cross the same body-distance boundary')
            assert_equal(False, (native_edge['bodyDistX'] <= 48) == (native_distance <= 48),
                         'fixture crosses 48 between edge and consumption')
        selected.append({'logic': logic, 'seat': seat, 'source': entry['from'], 'sourceTick': entry['stateTime'],
                         'target': entry['to'], 'button': button_frame, 'age': age, 'direction': row['direction'],
                         'edgeNative': native_edge['bodyDistX'], 'edgeHost': host_edge,
                         'consumeNative': native_distance, 'consumeHost': consumed, 'candidates': row['candidates']})
    return selected


def verify_attacker_steps(mode, native_actions, phases, moves, windows):
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    results = []
    for window in windows:
        state, seat = window['state'], window['seat']
        previous_elapsed = -1
        for logic in range(window['start'], window['lastCompared'] + 1):
            frame = logic - 14
            native = phases['after-collision'][frame]['chars'][seat ^ mirror]
            host = moves[seat, logic]
            elapsed = int(host['elapsed'])
            assert_equal(native_actions[state], native['anim'], f'original Action for State {state} at F{frame}')
            if state == 240 and elapsed != previous_elapsed and elapsed in STEP[mode]:
                original_prior = phases['end'][frame - 1]['chars'][seat ^ mirror]
                facing = native['facing']
                native_delta = round((native['pos'][0] - original_prior['pos'][0]) * Q16) * facing
                host_delta = (int(host['x']) - int(moves[seat, logic - 1]['x'])) * facing
                assert_equal(STEP[mode][elapsed] * Q16, native_delta, f'original standing b step at F{frame}')
                assert_equal(native_delta, host_delta, f'host standing b step at F{frame}')
                results.append({'seat': seat, 'frame': frame, 'elapsed': elapsed, 'delta': native_delta / Q16})
            previous_elapsed = elapsed
    return results


def verify_sweep_distance(name, profile, phases, moves, commands):
    if name not in SWEEP_DISTANCE:
        return None
    target, distance = SWEEP_DISTANCE[name]
    match, = [entry for entry in commands['commits'] if entry['native']['to'] == target]
    native, host = match['native'], match['host']
    logic, seat = int(host['logic']), native['hostSeat']
    assert_equal(90, logic - 14, 'same physical kick after State 440 recovery')
    selected = body_distance(profile, moves, seat, logic)
    assert_equal(round(distance * Q16), selected['raw'], 'exact distance after sweep recovery')
    assert_equal(round(distance * Q16), round(native['bodyDistX'] * Q16), 'native distance after sweep recovery')
    positions = []
    for frame in (55, 56, 57):
        original = phases['end'][frame]['chars'][seat]
        actual = int(moves[seat, frame + 14]['x'])
        assert_equal(round(original['pos'][0] * Q16), actual, f'440 recovery authored position at F{frame}')
        positions.append({'frame': frame, 'state': original['state'], 'x': actual / Q16})
    return {'target': target, 'logic': logic, 'bodyDistance': distance, 'positions': positions}


def verify_heavy_wave_launch(name, scene, phases, moves, lines):
    camera_tick = WAVE_CAMERA_TICKS.get(name)
    if camera_tick:
        victim = phases['end'][camera_tick]['chars'][1]
        assert_equal((5101, 252.25), (victim['state'], victim['pos'][0]), 'authored stage camera limit')
    return check_wave_launch(name, scene, phases, moves, lines, camera_clip=camera_tick, camera_direction=1)


def verify_heavy_contacts(name, scene, events, moves, damage, profile, **checks):
    if name != 'near-wave-guard-8':
        return verify_contacts(scene, events, moves, damage, profile, **checks)
    positions = []

    def prior_guard_origin(native, actual):
        assert_equal((48, 1, 108.98032), (native['frame'], native['target'], native['targetX']),
                     'native Wave follows quantified guard drift')
        assert_equal(108.97679138183594, actual, 'NewKOF Wave follows the fixed-point guard displacement')
        positions.append({'frame': native['frame'], 'nativeGuardX': native['targetX'], 'hostGuardX': actual})
        return actual - native['targetX']

    result = verify_contacts(scene, events, moves, damage, profile, guard_contact_origin=prior_guard_origin, **checks)
    return [*result, *positions]


def check_heavy_recovery(deferred, name, scene, phases, moves):
    if name == 'near-wave-guard-8':
        def prior_block_anchor(phases, moves, frame, seat):
            assert_equal((48, 1), (frame, seat), 'Wave guard follows the first contact')
            native = phases['end'][frame - 1]['chars'][seat]
            host = moves[seat, frame + 13]
            assert_equal((True, 'Standing'), (native['ctrl'], host['state']), 'target recovered before Wave block')
            offset = int(host['x']) / Q16 - native['pos'][0]
            assert_equal(True, abs(offset) < 0.005, 'first guard push uses the existing fixed-point bound')
            return offset

        return check_recovery(deferred, scene, phases, moves, states=KICKS, wave_origins=prior_block_anchor)
    repeated_contacts = {'near-natural-b': (8, 58), 'near-to-far-new-instance': (8, 58),
                         'far-early-b': (18, 67), 'far-natural-b': (18, 67),
                         'far-to-near-new-instance': (18, 75)}
    if name in repeated_contacts:
        first, second = repeated_contacts[name]

        def previous_hit_anchor(phases, moves, frame, seat):
            if frame == first:
                return 0
            assert_equal((second, 1), (frame, seat), 'second hit of a new standing-heavy instance')
            native = phases['end'][frame - 1]['chars'][seat]
            host = moves[seat, frame + 13]
            assert_equal((True, 'Standing'), (native['ctrl'], host['state']), 'target recovered before the new hit')
            offset = int(host['x']) / Q16 - native['pos'][0]
            assert_equal(True, abs(offset) < 0.005, 'first hit displacement uses the existing fixed-point bound')
            return offset

        return check_recovery(deferred, scene, phases, moves, states=KICKS, contact_origins=previous_hit_anchor)
    if name != 'sweep-b-buffer-away-ground':
        return check_recovery(deferred, scene, phases, moves, states=KICKS)

    def legacy_guard_release(phases, moves, frame, seat):
        assert_equal((58, 1), (frame, seat), 'documented outward crossing contact')
        original = [phases['end'][tick]['chars'][seat] for tick in range(46, frame)]
        current = [int(moves[seat, tick + 14]['x']) for tick in range(46, frame)]
        assert_equal([120, 140, 140, 140, 140, 0], [row['state'] for row in original[:6]],
                     'legacy standing-guard release phases')
        assert_equal([775511] * len(current), current, 'legacy host stops on guard release')
        original_late = original[-1]['pos'][0]
        assert_equal(True, abs(original_late - 19.12713) < 0.000005, 'legacy native residual guard drift')
        assert_equal(True, abs(original[0]['pos'][0] - current[0] / Q16) < 0.005,
                     'both engines still aligned before guard release')
        return current[-1] / Q16 - original_late

    return check_recovery(deferred, scene, phases, moves, states=KICKS, contact_origins=legacy_guard_release)


def verify_mode2_guard_exit(mode, name, phases, moves):
    if mode != 'mode2' or name not in {'far-guard', 'far-crouch-guard'}:
        return None
    expected = {'far-guard': (151, 20, 'Standing', 207616),
                'far-crouch-guard': (153, 11, 'Crouching', 0)}
    last, following, next_logic, first_step = expected[name]
    original = [phases['end'][frame]['chars'][1] for frame in (52, 53, 54)]
    host = [moves[1, frame + 14] for frame in (52, 53, 54)]
    assert_equal([last, 140, following], [row['state'] for row in original], 'native guard exit phases')
    assert_equal(['Blockstun', next_logic, next_logic], [row['state'] for row in host], 'host guard exit phases')
    native_x = [round(row['pos'][0] * Q16) for row in original]
    host_x = [int(row['x']) for row in host]
    assert_equal((0, first_step), (native_x[1] - native_x[0], native_x[2] - native_x[1]),
                 'native waits until after guard exit to move')
    assert_equal((0, first_step), (host_x[1] - host_x[0], host_x[2] - host_x[1]),
                 'host waits until after guard exit to move')
    return {'frames': (52, 53, 54), 'nativeX': [raw / Q16 for raw in native_x], 'hostX': [raw / Q16 for raw in host_x]}


def verify_wakeup_reservation(name, phases, moves, commands):
    if name not in {'wakeup-reservation-84', 'wakeup-reservation-95'}:
        return None
    frame = int(name.rsplit('-', 1)[1])
    target = phases['before-command'][frame]['chars'][1]
    expected_native = (5110, 65536) if frame == 84 else (5120, 32768)
    assert_equal(expected_native, (target['state'], target['moveType']), 'native target posture and move type')
    logic = frame + 14
    if frame == 84:
        assert_equal(('Knockdown', KICKS[245]), (moves[1, logic - 1]['state'], moves[0, logic]['move']),
                     'lying Hit target blocks reservation, so physical b starts 245')
    else:
        assert_equal(('Wakeup', '-'), (moves[1, logic - 1]['state'], moves[0, logic]['move']),
                     'idle wakeup target permits reservation without installing 805')
        entries = [row['native'] for row in commands['deferred'] if row['native']['to'] == 805]
        assert_equal([frame], [row['frame'] for row in entries], '805 reservation consumes physical b once')
        assert_equal('-', moves[0, logic + 1]['move'], 'native 805 body owns its deferred follow-up')
    return {'frame': frame, 'nativeTargetState': target['state'], 'nativeTargetMoveType': target['moveType'],
            'hostTargetStateBefore': moves[1, logic - 1]['state'], 'hostMove': moves[0, logic]['move']}


def verify_heavy_body(mode, native_actions, profile, changes, name, scene, phases, events, moves, reentry, lines):
    result = verify_body(profile, changes, name, scene, phases, events, moves, reentry, lines,
                         states=KICKS, lengths=LENGTH[mode], active=ACTIVE[mode], reservations=DEFERRED_STATES,
                         native_moves=NATIVE, explicit_windows={245: range(4, 9)},
                         selection_check=verify_heavy_selection, wave_launch_check=verify_heavy_wave_launch)
    for route in result['commands']['commits']:
        native, host = route['native'], route['host']
        if native['to'] in KICKS and native['from'] in NATIVE:
            permission = 2 if native['from'] == 1000 else 1
            assert_equal(permission, native['var2'], 'standing b after an old attack uses natural permission')
            assert_equal('0', host['hitpause'], 'natural standing b resolves on a runnable tick')
        if native['from'] in KICKS and native['to'] in NATIVE and native['to'] != 1000:
            assert_equal(LENGTH[mode][native['from']], native['stateTime'], 'no early ordinary route from heavy b')
    result['attackerSteps'] = verify_attacker_steps(mode, native_actions, phases, moves, result['kickWindows'])
    result['sweepDistanceBoundary'] = verify_sweep_distance(name, profile, phases, moves, result['commands'])
    result['mode2GuardExit'] = verify_mode2_guard_exit(mode, name, phases, moves)
    result['wakeupReservation'] = verify_wakeup_reservation(name, phases, moves, result['commands'])
    return result


def main():
    parser, options = validation_options(__doc__, PROFILE)
    native_actions = {240: 240 if options.mode == 'default' else 241, 245: 245}
    actions = {**ACTIONS, **{move: native_actions[state] for state, move in KICKS.items()}}
    run_validation(parser, options, family=FAMILY, capture_profile='Stage27',
                   alias=options.visual_alias or f'terry98-stage27-{options.mode}',
                   native_moves=NATIVE, reservations=DEFERRED_STATES, actions=actions, states=native_actions,
                   body_check=partial(verify_heavy_body, options.mode, native_actions),
                   recovery_check=check_heavy_recovery,
                   pose_check=partial(check_poses, states=KICKS, actions=actions, reactions=REACTION_ACTIONS),
                   contact_check=verify_heavy_contacts, deferred_followups=DEFERRED_FOLLOWUPS)


if __name__ == '__main__':
    main()
