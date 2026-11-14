"""Replay native Terry State 410 and Power Wave traces using Godot Vulkan."""

import argparse
from dataclasses import replace
from functools import partial
from hashlib import sha256
import json
from pathlib import Path
from subprocess import TimeoutExpired
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'Stage22Validation'))
from crouch_wave_duel import CROUCH, local_boxes, native_boxes, verify_body as verify_light_body, verify_poses as verify_light_poses
from heavy_wave_duel import normal_damage
from near_wave_duel import capture_sparse_realtime, compare_repeat
from power_wave_duel import (ACTIONS, MOVE, NATIVE_MOVES, POSE_KEYS, Q16, ROOT, assert_equal,
                             LAUNCH_ACTIONS, audit_rows, fighter_poses, indexed, native_case, run_case,
                             verify_contacts, verify_grounded_wave_recovery, visual_timeline)
from compare_damage import (RNG_MODULUS, RNG_MULTIPLIER, committed_damage, damage_roll, raw_damage)


HEAVY = 'TerryCrouchHeavyPunch410'
PROFILE_DIR = 'data/imported-characters/terry98-stage23-data'
NATIVE = {**NATIVE_MOVES, 400: CROUCH, 410: HEAVY}
VISUALS = {**ACTIONS, CROUCH: 400, HEAVY: 410}
CONTACTS = ('Hit', 'Blocked', 'ProjectileHit', 'ProjectileBlocked')
GROUND_REACTION = {5000, 5001, 5010, 5011}
HEAVY_REACTION_ACTIONS = {5002, 5007, 5022, 5027}
INTERRUPT_REACTION_ACTIONS = {5020, 5025}
WAVE_GROUND_REACTION_ACTIONS = {5012, 5017}
LAUNCH_STATES = {5000: 'Shake', 5010: 'Shake', 5030: 'Lift', 5035: 'Rise', 5050: 'Fall',
                 5100: 'Impact', 5101: 'Bounce', 5120: 'Wakeup'}
REFERENCE_FAMILY = 'crouch-heavy-wave'


def fixture(reference, name, mode, *, stage22=False):
    run = json.loads((reference.parent / 'run-metadata.json').read_bytes())
    metadata = json.loads(reference.with_name(f'{reference.stem}-metadata.json').read_bytes())
    assert_equal('Stage22' if stage22 else 'Stage23', run['capture_profile'], 'native research profile')
    assert_equal(True, reference.stem in run['executed_cases'], 'recorded native fixture')
    assert_equal(metadata['trace_sha256'], sha256(reference.read_bytes()).hexdigest(), 'native trace SHA-256')
    last = run['last_frame']
    scene, phases, changes = native_case(reference, name, last_frame=last, native_moves=NATIVE)
    if mode == 'mode2' and any(row['to'] == 1050 for row in changes):
        control_path = reference.with_name(f'{mode}-{REFERENCE_FAMILY}-hit-0.jsonl')
        control, control_phases, changes, _ = fixture(control_path, 'hit-0', mode)
        boundary = next(row['frame'] for row in phases['end'].values() if row['chars'][0]['state'] == 1050)
        assert_equal(tuple(row for row in control.contacts if row['frame'] < boundary),
                     tuple(row for row in scene.contacts if row['frame'] < boundary), 'mode 2 shared contact prefix')
        phases['end'] = control_phases['end']
        phases['after-collision'] = control_phases['after-collision']
        scene = replace(scene, contacts=control.contacts, differences=('native-mode2-1050-outside-scope',))
    if name in ('down-forward-heavy-0', 'down-forward-both-0'):
        reserved, = [row for row in changes if row['to'] == 310]
        assert_equal(5, reserved['frame'], 'native reserved diagonal enters 310')
        assert_equal([], list(scene.moves), 'native diagonal does not enter State 410')
        scene = replace(scene, contacts=tuple(row for row in scene.contacts if row['frame'] < reserved['frame']),
                        differences=('native-command-normal-310-outside-scope',))
    phases['damage-rows'] = [row for row in map(json.loads, reference.read_text(encoding='utf-8').splitlines())
                             if row['phase'] in ('hitdef', 'var21-draw')]
    return scene, phases, changes, last


def assert_position(source, row, label, tolerance=0.02):
    for axis, want in zip(('x', 'y'), (source['pos'][0], -source['pos'][1]), strict=True):
        actual = int(row[axis]) / Q16
        if abs(want - actual) > tolerance:
            raise ValueError(f'{label} {axis}: expected {want}, got {actual}')


def check_rng(profile, damage, lines):
    if 'damageRules' not in profile:
        raise ValueError('missing dynamic damage profile')
    paused = {int(row['logic']) for row in audit_rows(lines, 'ScriptedPauseAudit') if row['active'] == 'True'}
    for seat in (0, 1):
        rng = 1337 + 104729 * seat
        for frame in sorted(frame for index, frame in damage if index == seat):
            previous = damage.get((seat, frame - 1))
            if frame not in paused and (previous is None or previous['pause'] == '0'):
                rng = rng * RNG_MULTIPLIER % RNG_MODULUS
            row = damage[seat, frame]
            assert_equal(rng, int(row['rng']), f'seat {seat} RNG at L{frame}')
            assert_equal(damage_roll(rng, profile['damageRules']['varianceMaximum']),
                         int(row['variance']), f'seat {seat} variance at L{frame}')


def native_damage(profile, phases, attack, frame, seat, blocked, *, state=410):
    rows = phases['damage-rows']
    definition = next((row for row in reversed(rows) if row['phase'] == 'hitdef' and row['frame'] <= frame
                       and row['hitDefOwner'] == seat + 1 and row['chars'][seat]['state'] == state), None)
    if definition is None:
        raise ValueError(f'missing native State {state} HitDef at F{frame}, seat {seat + 1}')
    source = definition['chars'][seat]
    stats = source['vars']
    draw = next((row for row in reversed(rows) if row['phase'] == 'var21-draw'
                 and row['seat'] == seat + 1 and row['frame'] <= definition['frame']), None)
    if draw is None:
        raise ValueError(f'missing native variance sample at F{definition["frame"]}')
    assert_equal(damage_roll(draw['rng'], profile['damageRules']['varianceMaximum']), stats['var21'],
                 f'native variance at F{frame}')
    raw = raw_damage(attack['dynamicDamage'], stats['var21'], stats['var6'],
                     max(1, stats['var23']), stats['fvar7'])
    assert_equal(raw, source['hitDef']['damage'], f'native State {state} raw damage at F{frame}')
    target = seat ^ 1
    collision = phases['after-collision'][frame]['chars'][target]
    hp = phases['before-command'][frame]['chars'][target]['life']
    amount = 0 if blocked else committed_damage(raw, source['attackMul'], collision['finalDefense'], hp)
    assert_equal(amount, hp - phases['end'][frame + 1]['chars'][target]['life'],
                 f'native reaction-phase HP at F{frame + 1}')
    return {'amount': amount, 'rng': draw['rng'], 'variance': stats['var21'], 'counter': stats['var6']}


def audit_body(profile, changes, name, scene, phases, events, moves, reentry, lines):
    move = next(move for move in profile['moves'] if move['moveId'] == HEAVY)
    attack, = move['attacks']
    damage = indexed(audit_rows(lines, 'DamageAudit'))
    assert_equal(set(moves), set(damage), 'complete damage audit')
    assert_equal(set(moves), set(reentry), 'complete attack instance audit')
    check_rng(profile, damage, lines)
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    starts = [row for row in events if row['kind'] == 'MoveStarted' and row['move'] == HEAVY]
    last = min(max(phases['after-collision']) + 14, max(frame for _, frame in moves))
    for entry in starts:
        start, seat = entry['frame'], entry['source']
        for logic in range(start, last):
            row = moves[seat, logic]
            if row['move'] != HEAVY or int(reentry[seat, logic]['started']) != start:
                break
            original = phases['after-collision'][logic - 14]['chars'][seat ^ mirror]
            tick = int(row['elapsed'])
            assert_equal((410, 2, tick + 1), (original['state'], original['stateType'], original['time']),
                         f'native State 410 animation clock at L{logic}')
            assert_equal(5 <= tick <= 12, bool(row['attacks']), f'410 active window at L{logic}')
            assert_equal(native_boxes(original, 'clsn1'), local_boxes(row, 'attacks'), f'410 Clsn1 at L{logic}')
            assert_equal(native_boxes(original, 'clsn2'), local_boxes(row, 'hurt'), f'410 Clsn2 at L{logic}')
    contacts = []
    for event in events:
        if event['kind'] not in ('Hit', 'Blocked') or event['move'] != HEAVY:
            continue
        logic, seat, target = (event[key] for key in ('frame', 'source', 'target'))
        blocked = event['kind'] == 'Blocked'
        phase = 'block' if blocked else 'hit'
        stop = attack['hitstopByOutcome'][phase]
        assert_equal((stop['attackerFrames'], stop['defenderFrames']),
                     (int(moves[seat, logic]['stop']), int(moves[target, logic]['stop'])), 'independent HitPause')
        amount = 0 if blocked else normal_damage(profile, damage, moves, event, attack)
        assert_equal(amount, event['amount'], 'host State 410 dynamic damage')
        assert_equal(amount, int(moves[target, logic - 1]['health']) - int(moves[target, logic]['health']),
                     'host atomic HP')
        assert_equal(str(logic), reentry[seat, logic]['blockConfirm' if blocked else 'hitConfirm'],
                     'contact confirmation stays with its original instance')
        native = native_damage(profile, phases, attack, logic - 14, seat ^ mirror, blocked)
        contacts.append({**event, 'native': native, 'tick': int(moves[seat, logic]['elapsed']),
                         'rng': int(damage[seat, logic]['rng']), 'variance': int(damage[seat, logic]['variance'])})
    exits = []
    for entry in changes:
        if entry['from'] != 410:
            continue
        logic, seat = entry['frame'] + 14, (entry['seat'] - 1) ^ mirror
        target, tick = entry['to'], entry['stateTime']
        old, new = moves[seat, logic - 1], moves[seat, logic]
        if target in NATIVE:
            assert_equal(tick, int(old['elapsed']) + 1, f'old State 410 cancel time at L{logic}')
            assert_equal('0', old['stop'], 'cancel occurs after source HitPause')
            assert_equal((NATIVE[target], '', '0'), (new['move'], new['attacks'], new['elapsed']),
                         f'fresh follow-up instance at L{logic}')
            assert_equal(('1', '0'), (damage[seat, logic]['divisor'], damage[seat, logic]['pause']),
                         'new instance damage-local state')
        elif target == 11:
            assert_equal('Crouching', new['state'], 'State 410 exits through crouch')
            for boundary in range(logic, last):
                source = phases['after-collision'][boundary - 14]['chars'][seat ^ mirror]
                if source['state'] not in (11, 12):
                    break
                row = moves[seat, boundary]
                assert_equal('Crouching' if source['state'] == 11 else 'Standing', row['state'],
                             f'crouch exit state at L{boundary}')
                assert_equal(native_boxes(source, 'clsn2'), local_boxes(row, 'hurt'),
                             f'crouch exit Clsn2 at L{boundary}')
        assert_equal([], [row for row in events if row['frame'] == logic and row['source'] == seat
                          and row['move'] == HEAVY and row['kind'] in CONTACTS],
                     'no stale source contact on the transition')
        exits.append({'logic': logic, 'seat': seat, 'sourceTick': tick, 'nativeDestination': target})
    result = {'crouchHeavyStarts': starts, 'crouchHeavyExits': exits, 'crouchHeavyContacts': contacts,
              'waveLaunchRecovery': check_wave_launch(name, scene, phases, moves, lines)}
    if name == 'mixed-22':
        result['fixedPointWaveBirth'] = verify_mixed_birth(scene, phases, moves, lines)
    return result


def verify_mixed_birth(scene, phases, moves, lines):
    first, second = [row for row in scene.contacts if row['target'] == 1]
    starts = [row for row in scene.moves if row['hostSeat'] == 1 and row['move'] == MOVE]
    wave, = starts
    birth = wave['frame'] + 15
    tick = birth + 14
    x_start = int(moves[1, first['frame'] + 14]['x'])
    velocity = 21 * Q16 // 2
    retention = 85 * Q16 // 100
    displacement = 0
    while velocity >= Q16:
        displacement += velocity
        velocity = velocity * retention // Q16
    assert_equal(x_start + displacement, int(moves[1, second['frame'] + 14]['x']),
                 'Q16 position after first 410 block')
    x_owner = x_start + 2 * displacement
    assert_equal(x_owner, int(moves[1, tick]['x']), 'Q16 owner position at second Wave birth')
    host, = [row for row in audit_rows(lines, 'WaveEntityAudit') if int(row['logic']) == tick and row['owner'] == '1']
    assert_equal(x_owner - 5 * Q16 // 2, int(host['x']), 'Q16 mirrored Wave birth after two blocks')
    source, = [row for row in phases['end'][birth]['chars'][1]['helpers'] if row['anim'] == 1020]
    delta = int(host['x']) / Q16 - source['pos'][0]
    if abs(delta) > 0.01:
        raise ValueError(f'combined guard slide exceeds the Q16 bound: {delta}')
    return {'nativeFrame': birth, 'hostFrame': tick, 'nativeX': source['pos'][0],
            'hostX': int(host['x']) / Q16, 'delta': delta}


def check_recovery(scene, phases, moves):
    results = verify_grounded_wave_recovery(scene, phases, moves, position_tolerance=0.02, crouch_recovery=True)
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    for index, contact in enumerate(scene.contacts):
        frame, seat = contact['frame'], contact['target']
        if contact['kind'] == 'ProjectileHit' and not contact['fall']:
            for tick in range(frame + 1, max(phases['after-collision']) + 1):
                source = phases['after-collision'][tick]['chars'][seat ^ mirror]
                if source['anim'] not in WAVE_GROUND_REACTION_ACTIONS:
                    break
                assert_equal(native_boxes(source, 'clsn2'), local_boxes(moves[seat, tick + 14], 'hurt'),
                             f'grounded late Wave reaction Clsn2 at F{tick}')
        if contact['kind'] not in ('Hit', 'Blocked'):
            continue
        source_seat = contact['source'] ^ mirror
        source_state = phases['after-collision'][frame]['chars'][source_seat]['state']
        interrupted = source_state == 205 and phases['after-collision'][frame]['chars'][seat ^ mirror]['state'] == 410
        if source_state != 410 and not interrupted:
            continue
        end = next((other['frame'] for other in scene.contacts[index + 1:] if other['target'] == seat),
                   min(max(phases['end']) + 1, max(tick for _, tick in moves) - 13))
        expected = 'Hitstun' if contact['kind'] == 'Hit' else 'Blockstun'
        recovered = False
        for tick in range(frame + 1, end):
            source = phases['end'][tick]['chars'][seat ^ mirror]
            row = moves[seat, tick + 14]
            assert_position(source, row, f'State 410 defender at F{tick}')
            if source['ctrl']:
                assert_equal('Crouching' if source['stateType'] == 2 else 'Standing', row['state'],
                             f'State 410 defender control at F{tick}')
                results.append({'contact': frame, 'recovered': tick, 'state': source['state'], 'seat': seat})
                recovered = True
                break
            assert_equal(expected, row['state'], f'State 410 defender reaction at F{tick}')
            if expected == 'Hitstun' and source['state'] in GROUND_REACTION:
                assert_equal(native_boxes(source, 'clsn2'), local_boxes(row, 'hurt'),
                             f'State 410 defender reaction Clsn2 at F{tick}')
        if not recovered and end not in {row['frame'] for row in scene.contacts}:
            raise ValueError(f'native trace ended before State 410 defender recovery from F{frame}')
    return results


def check_wave_launch(name, scene, phases, moves, lines, *, camera_clip=None):
    """Pair every post-Wave launch tick to its original phase and position."""
    active = indexed(audit_rows(lines, 'LaunchReactionAudit'))
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    paired = []
    for index, contact in enumerate(scene.contacts):
        if contact['kind'] != 'ProjectileHit' or not contact['fall']:
            continue
        frame, seat = contact['frame'], contact['target']
        native = phases['end'][frame]['chars'][seat ^ mirror]
        host = moves[seat, frame + 14]
        x_offset = int(host['x']) / Q16 - native['pos'][0]
        next_contact = next((row['frame'] for row in scene.contacts[index + 1:] if row['target'] == seat),
                            max(phases['end']) + 1)
        stages = {}
        boundary_delta = []
        first_camera_clip = camera_clip or {'incoming-wave-22': 87, 'incoming-wave-28': 91}.get(name)
        ballistics_x = None
        for tick in range(frame, next_contact):
            original = phases['end'][tick]['chars'][seat ^ mirror]
            phase = launch_phase(original)
            logic = tick + 14
            current = active.get((seat, logic))
            assert_equal(phase, current['phase'] if current else None, f'Wave launch phase at F{tick}')
            if phase is None:
                assert_equal(True, original['ctrl'], f'original control recovered at F{tick}')
                assert_equal('Crouching' if original['stateType'] == 2 else 'Standing',
                             moves[seat, logic]['state'], f'Wave target control at F{tick}')
                if ballistics_x is not None:
                    assert_equal(ballistics_x, int(moves[seat, logic]['x']), 'host rest position at restored control')
                stages['Control'] = tick
                break
            stages.setdefault(phase, tick)
            row = moves[seat, logic]
            expected_x = original['pos'][0] + x_offset
            expected_y = max(0, -original['pos'][1])
            if first_camera_clip is not None and tick >= first_camera_clip:
                vx = -27 * Q16 // 8
                if ballistics_x is None:
                    ballistics_x = int(moves[seat, logic - 1]['x'])
                if phase == 'Bounce':
                    native_step = original['pos'][0] - phases['end'][tick - 1]['chars'][seat ^ mirror]['pos'][0]
                    assert_equal(-3.375, original['vel'][0], 'native retained bounce velocity')
                    assert_equal(vx, int(row['vx']), 'host retained bounce velocity')
                    if not -3.375 < native_step <= 0:
                        raise ValueError(f'native camera clamp not observed at F{tick}: dx={native_step}')
                    ballistics_x += vx
                assert_equal(ballistics_x, int(row['x']), f'host bounce and rest X at F{tick}')
                boundary_delta.append({'frame': tick, 'nativeX': original['pos'][0], 'hostX': ballistics_x / Q16,
                                       'delta': ballistics_x / Q16 - expected_x})
                expected_x = ballistics_x / Q16
            for axis, expected in (('x', expected_x), ('y', expected_y)):
                actual = int(row[axis]) / Q16
                if abs(expected - actual) > 0.02:
                    raise ValueError(f'Wave launch {axis} at F{tick}: expected {expected}, got {actual}')
        if next_contact == max(phases['end']) + 1:
            assert_equal(set(LAUNCH_ACTIONS) | {'Control'}, set(stages), 'complete Wave launch through control')
        comparison = {'contact': frame, 'seat': seat, 'phaseStarts': stages, 'contactXOffset': x_offset}
        if boundary_delta:
            comparison['nativeStageCameraClamp'] = [row for row in boundary_delta
                                                   if row['frame'] <= first_camera_clip + 2]
            if camera_clip is not None:
                comparison['nativeStageCameraClamp'].append(boundary_delta[-1])
        paired.append(comparison)
    return paired


def launch_phase(original):
    if original['state'] == 5110:
        return 'LyingImpact' if original['anim'] == 5170 else 'Lying'
    return LAUNCH_STATES.get(original['state'])


def check_poses(phases, scene, events, committed, timelines, modes):
    windows = fighter_poses(scene, events, committed, timelines, modes, actions=VISUALS)
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    for event in events:
        if event['kind'] not in ('Hit', 'Blocked', 'ProjectileBlocked'):
            continue
        seat, frame = event['target'], event['frame']
        victim = phases['after-collision'][frame - 14]['chars'][seat ^ mirror]
        interrupted = event['move'] == NATIVE[205] and victim['state'] == 410
        if event['kind'] == 'ProjectileBlocked':
            assert_equal('151' if victim['stateType'] == 2 else '150', committed[seat, frame + 1]['action'],
                         f'Wave guard sprite at L{frame}')
            continue
        if event['move'] != HEAVY and not interrupted:
            continue
        if event['kind'] == 'Blocked':
            original = phases['before-command'][frame - 14]['chars'][seat ^ mirror]
            assert_equal('151' if original['raw'] & 2 else '150', committed[seat, frame + 1]['action'],
                         f'guard sprite at L{frame}')
            continue
        for tick in range(frame + 1, max(phases['after-collision']) + 15):
            original = phases['after-collision'][tick - 14]['chars'][seat ^ mirror]
            row = committed[seat, tick + 1]
            reactions = INTERRUPT_REACTION_ACTIONS if interrupted else HEAVY_REACTION_ACTIONS
            if original['anim'] not in reactions or row['state'] != 'Hitstun':
                break
            assert_equal((str(original['anim']), ','.join(map(str, original['frame']['sprite']))),
                         (row['action'], row['sprite']), f'State 410 grounded reaction sprite at L{tick}')
    for seat in (0, 1):
        after_heavy = False
        for frame, rows in phases['after-collision'].items():
            original = rows['chars'][seat ^ mirror]
            row = committed.get((seat, frame + 15))
            if row is None:
                continue
            if original['state'] == 410:
                after_heavy = True
            elif original['state'] == 12 and after_heavy:
                tick = original['time'] - 1
                assert_equal((str(tick), timelines[12][tick]),
                             (row['tick'], tuple(row[key] for key in POSE_KEYS)), f'Action 12 at F{frame}')
            elif original['state'] not in (11, 5010, 5011):
                after_heavy = False
    for contact in scene.contacts:
        if contact['kind'] != 'ProjectileHit':
            continue
        seat, frame = contact['target'], contact['frame']
        if not contact['fall']:
            for tick in range(frame + 1, max(phases['after-collision']) + 1):
                original = phases['after-collision'][tick]['chars'][seat ^ mirror]
                if original['anim'] not in WAVE_GROUND_REACTION_ACTIONS:
                    break
                row = committed[seat, tick + 15]
                assert_equal((str(original['anim']), ','.join(map(str, original['frame']['sprite']))),
                             (row['action'], row['sprite']), f'grounded late Wave reaction at F{tick}')
            continue
        for tick in range(frame, max(phases['end']) + 1):
            original = phases['end'][tick]['chars'][seat ^ mirror]
            if original['ctrl']:
                break
            phase = launch_phase(original)
            if phase:
                assert_equal(str(LAUNCH_ACTIONS[phase]), committed[seat, tick + 15]['action'],
                             f'Wave reaction sprite at F{tick}')
    return windows


def realtime_route(starts, events):
    assert_equal([HEAVY, MOVE], [row['move'] for row in starts], 'realtime State 410 into Wave')
    first, = [row for row in events if row['kind'] in ('Hit', 'Blocked') and row['move'] == HEAVY]
    assert_equal(5, first['frame'] - starts[0]['frame'], 'realtime State 410 first active contact')
    tick = starts[1]['frame'] - starts[0]['frame'] - 11
    if not 6 <= tick <= 12:
        raise ValueError(f'realtime State 410 cancel outside source window at tick {tick}')
    contact, = [row for row in events if row['kind'] in ('ProjectileHit', 'ProjectileBlocked')]
    return contact


def minimum_replay_frames(scene, phases, changes):
    last_input = max((int(key.split(':', 1)[0]) for key in scene.keys), default=23) - 23
    boundaries = [last_input, *(row['frame'] for row in changes)]
    boundaries.extend(frame for frame, row in phases['end'].items() if row['globalPause'])
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    for contact in scene.contacts:
        frame = contact['frame']
        native_target = contact['target'] ^ mirror
        if not phases['after-collision'][frame]['chars'][native_target]['gethit']:
            raise ValueError(f'missing native target at F{frame}')
        recovery = next((tick for tick, row in phases['end'].items()
                         if tick > frame and row['chars'][native_target]['ctrl']), max(phases['end']))
        boundaries.append(recovery)
    # Keep the normal post-capture tail available even when a separate window
    # reads back the final native recovery frames.
    return min(max(phases['end']) + 25, max(boundaries) + 42)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--godot', required=True, type=Path)
    parser.add_argument('--reference', required=True, type=Path, action='append')
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--profile-dir', default=PROFILE_DIR)
    parser.add_argument('--alias-prefix', default='terry98-stage23')
    parser.add_argument('--scene', action='append', required=True)
    parser.add_argument('--mode', default='default', choices=('default', 'mode2'))
    parser.add_argument('--compare-plain', action='store_true')
    parser.add_argument('--repeat-from', type=Path)
    parser.add_argument('--mode2-shared-input', action='store_true',
                        help='Replay mode-independent reserved diagonal input with mode 2')
    parser.add_argument('--stage22-light', action='store_true', help='Replay one or more Stage 22 default light fixtures')
    parser.add_argument('--fps', default='fixed', choices=('fixed', '30', '60'))
    parser.add_argument('--fixed-window', type=int, nargs=2)
    parser.add_argument('--realtime-window', type=int, nargs=2)
    parser.add_argument('--realtime-input-spacing', type=int, choices=(1, 2), default=1)
    options = parser.parse_args()
    if options.mode2_shared_input and (options.mode != 'mode2' or options.fps != 'fixed' or
                                       not set(options.scene) <= {'down-forward-heavy-0', 'down-forward-both-0'}):
        parser.error('--mode2-shared-input requires fixed mode 2 and a reserved diagonal fixture')
    if options.stage22_light and (options.fps != 'fixed' or options.mode != 'default'):
        parser.error('--stage22-light requires fixed mode and the default profile')
    if options.fps != 'fixed':
        if options.mode != 'default' or options.compare_plain or options.repeat_from or options.fixed_window:
            parser.error('realtime requires a default profile without fixed comparisons')
        if not options.realtime_window or not 0 < options.realtime_window[0] <= options.realtime_window[1] < options.realtime_window[0] + 3:
            parser.error('realtime requires a window of up to three renders')
    elif options.fixed_window and not 0 < options.fixed_window[0] <= options.fixed_window[1] < options.fixed_window[0] + 90:
        parser.error('fixed window must include 1–90 renders')
    options.output = options.output.resolve()
    options.output.mkdir(parents=True, exist_ok=False)
    options.recovery = False
    options.disable_vsync = True
    options.require_clean_sample = True
    options.require_no_backlog = options.fps != 'fixed'
    options.realtime_resolution = '320x180'
    options.realtime_warmup_frames = 150
    manifest = json.loads((ROOT / 'data/imported-characters/terry98/manifest.json').read_text(encoding='utf-8-sig'))
    timelines = {action: visual_timeline(manifest, action) for action in (12, 200, 205, 210, 211, 215, 400, 410, 1000, 1020)}
    profile = json.loads((ROOT / options.profile_dir / f'{options.mode}.fighter.json').read_bytes())
    alias = f'{options.alias_prefix}-{options.mode}'
    failed = []
    for name in options.scene:
        try:
            family = 'crouch-wave' if options.stage22_light else REFERENCE_FAMILY
            source_mode = 'default' if options.mode2_shared_input else options.mode
            file = f'{source_mode}-{family}-{name}.jsonl'
            reference = next((path / file for path in options.reference if (path / file).is_file()), None)
            if reference is None:
                raise ValueError(f'missing native reference {file}')
            scene, phases, changes, _ = fixture(reference, name, source_mode, stage22=options.stage22_light)
            if options.mode2_shared_input:
                scene = replace(scene, differences=(*scene.differences, 'native-default-shared-CMD-310'))
            if options.fps != 'fixed':
                capture_sparse_realtime(options, name, scene, alias, realtime_route)
                continue
            waves = [row['frame'] for row in scene.moves if row['move'] == MOVE]
            first = min((row['frame'] for row in scene.moves), default=5)
            final = max(waves) + 50 if waves else first + 65
            captures = tuple(options.fixed_window or (max(first + 23, final - 89), final))
            scene = replace(scene, captures=captures)
            run_case(options, name, reference, timelines, profile, mode=options.mode, alias=alias,
                     prepared_scene=(scene, phases, changes),
                     fixed_checks=(partial(verify_light_body, profile, changes) if options.stage22_light
                                   else partial(audit_body, profile, changes, name)),
                     contact_check=partial(verify_contacts, guard_position_tolerance=0.02),
                     recovery_check=check_recovery,
                     pose_check=partial(verify_light_poses if options.stage22_light else check_poses, phases),
                     minimum_render_frames=minimum_replay_frames(scene, phases, changes),
                     partial_capture=True, allow_interrupted_spawn=True,
                     entity_position_tolerance=0.01 if name == 'mixed-22' else 0.005)
            if options.repeat_from:
                compare_repeat(options.output, name, options.repeat_from)
        except (ValueError, KeyError, IndexError, OSError, TimeoutExpired) as error:
            print(f'{name}: FAIL {error!r}', flush=True)
            failed.append(name)
    if failed:
        parser.exit(1, 'Failed: ' + ', '.join(failed) + '\n')


if __name__ == '__main__':
    main()
