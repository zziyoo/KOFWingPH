"""Compare physical-input State 250 duels against signed IKEMEN GO and Vulkan traces."""

from functools import partial
from hashlib import file_digest
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'Stage27Validation'))
from standing_heavy_kick_duel import ACTIONS, DEFERRED_STATES, NATIVE
from standing_light_kick_duel import (
    Q16, ROOT, assert_equal, audit_rows, check_rng, check_wave_eligibility, fighter_poses, fixture, indexed,
    local_boxes, locate, native_boxes, normal_damage, run_validation, validation_options,
    verify_commands, verify_grounded_wave_recovery, verify_wave_ground_poses,
)
from crouch_heavy_duel import LAUNCH_ACTIONS, launch_phase
from compare_damage import committed_damage, damage_roll, raw_damage
from power_wave_duel import verify_contacts


PROFILE = 'data/imported-characters/terry98-stage29-final'
FAMILY = 'standing-heavy-kick'
BLOWBACK = 'TerryGroundBlowback250'
KNUCKLE = 'TerryBackKnuckle300'
MOVES = {**NATIVE, 250: BLOWBACK, 300: KNUCKLE}
RESERVATIONS = {state: command for state, command in DEFERRED_STATES.items() if state not in MOVES}
LENGTH = 49
ACTIVE = range(23, 26)
COMMAND_WINDOW = range(24, 26)
STEPS = dict.fromkeys((8, 11, 13, 15, 17, 19, 21, 23, 26, 30), 8) | {33: 5}
CHORD = 32 | 256  # Native absolute b and y bits.


def attack_samples(name, scene, phases, moves, reentry, deferred):
    mirrored = phases['before-command'][0]['chars'][0]['facing'] < 0
    samples = []
    for entry in scene.moves:
        if entry['to'] != 250:
            continue
        seat, first = entry['hostSeat'], entry['frame'] + 14
        native_seat = seat ^ mirrored
        steps = []
        elapsed = -1
        for frame in range(entry['frame'], min(deferred, max(phases['after-collision']) + 1)):
            logic = frame + 14
            row = moves[seat, logic]
            if row['move'] != BLOWBACK or reentry[seat, logic]['started'] != str(first):
                break
            native = phases['after-collision'][frame]['chars'][native_seat]
            tick = int(row['elapsed'])
            assert_equal((250, 250, tick + 1), (native['state'], native['anim'], native['time']),
                         f'State/Action 250 clock at F{frame}')
            assert_equal(tick in ACTIVE, bool(row['attacks']), f'250 active boxes at F{frame}')
            assert_equal(native_boxes(native, 'clsn1'), local_boxes(row, 'attacks'), f'250 Clsn1 at F{frame}')
            assert_equal(native_boxes(native, 'clsn2'), local_boxes(row, 'hurt'), f'250 Clsn2 at F{frame}')
            assert_equal(0, int(row['vy']), f'250 grounded pose velocity at F{frame}')
            if tick != elapsed and tick in STEPS:
                previous = phases['end'][frame - 1]['chars'][native_seat]
                observed = round((native['pos'][0] - previous['pos'][0]) * Q16) * native['facing']
                host = (int(row['x']) - int(moves[seat, logic - 1]['x'])) * native['facing']
                # A nearby target can displace the attacker by player push on the same tick.
                # Apply the exact authored-step assertion whenever the source reports pure PosAdd.
                push = {}
                if observed == STEPS[tick] * Q16:
                    if name == '250-lying-cd' and frame in (114, 117) and host != observed:
                        defender = native_seat ^ 1
                        native_target = phases['end'][frame]['chars'][defender]
                        prior_target = phases['end'][frame - 1]['chars'][defender]
                        delta = (int(moves[seat ^ 1, logic]['x']) - int(moves[seat ^ 1, logic - 1]['x'])) * native['facing']
                        assert_equal((True, 0.0), (native_target['ctrl'], native_target['pos'][0] - prior_target['pos'][0]),
                                     'native defender has regained control and remains stationary')
                        assert_equal((observed // 2, observed // 2), (host, delta),
                                     f'host preserves the authored movement with paired player push at F{frame}')
                        assert_equal('', row['attacks'], 'player push comparison occurs in inactive recovery')
                        push = {'hostTargetDelta': delta / Q16, 'nativeTargetDelta': 0}
                    else:
                        assert_equal(observed, host, f'native 250 PosAdd at F{frame}')
                steps.append({'frame': frame, 'tick': tick, 'configured': STEPS[tick],
                              'nativeDelta': observed / Q16, 'hostDelta': host / Q16, **push})
            elapsed = tick
        if elapsed < 0:
            raise ValueError(f'no State 250 body frames at L{first}')
        samples.append({'seat': seat, 'first': first, 'lastElapsed': elapsed, 'steps': steps})
    return samples


def inspect_routes(phases, moves, reentry, commands, changes, events):
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    results = []
    for change in changes:
        if change['from'] != 250:
            continue
        frame, seat, target = change['frame'], (change['seat'] - 1) ^ mirror, change['to']
        logic = frame + 14
        current = moves[seat, logic]
        previous = moves[seat, logic - 1]
        tick = change['stateTime']
        if target in MOVES or target in RESERVATIONS:
            assert_equal(tick, int(previous['elapsed']) + 1, f'250 exit tick at F{frame}')
        if target in (300, 310, 1000, 1050):
            natural = tick == LENGTH
            assert_equal(True, natural or tick in COMMAND_WINDOW, f'native 250→{target} permission window')
            assert_equal((0, 1) if natural else (1, 0), (change['var1'], change['var2']),
                         f'native 250→{target} permission source')
        if target == 300 and tick < LENGTH:
            committed = next(result['host'] for result in commands['commits']
                             if result['native']['frame'] == frame and result['native']['hostSeat'] == seat)
            assert_equal((KNUCKLE, '0', '-1', '-1', '', str(logic)),
                         (committed['committed'], current['elapsed'], reentry[seat, logic]['hitConfirm'],
                          reentry[seat, logic]['blockConfirm'], current['attacks'], reentry[seat, logic]['started']),
                         '250→300 uses a new attack instance and clears old HitDef')
        if target in (0, 10, 20, 250):
            assert_equal(LENGTH, tick, f'250 natural exit or restart at F{frame}')
        if target in MOVES or target in RESERVATIONS or target in (0, 10, 20):
            assert_equal([], [row for row in events if row['kind'] in ('Hit', 'Blocked') and row['move'] == BLOWBACK
                              and row['frame'] == logic and row['source'] == seat], '250 exit emits no stale contact')
        if target in (0, 10, 20):
            assert_equal(('-', 'Crouching' if target == 10 else 'Standing'), (current['move'], current['state']),
                         f'250 natural control recovery at F{frame}')
        if target in (5000, 5010, 5070):
            assert_equal(('-', ''), (current['move'], current['attacks']), f'250 interruption at F{frame}')
        results.append({'frame': frame, 'seat': seat, 'stateTime': tick, 'destination': target})
    return results


def source_damage(profile, phases, attack, frame, owner, blocked):
    source = phases['after-collision'][frame]['chars'][owner]
    operands = source['vars']
    roll = next(row for row in reversed(phases['damage-rows']) if row['phase'] == 'var21-draw'
                and row['seat'] == owner + 1 and row['frame'] <= frame)
    variance = operands['var21']
    assert_equal(damage_roll(roll['rng'], profile['damageRules']['varianceMaximum']), variance,
                 f'native State 250 variance at F{frame}')
    raw = raw_damage(attack['dynamicDamage'], variance, operands['var6'], max(1, operands['var23']),
                     operands['fvar7'])
    assert_equal(raw, source['hitDef']['damage'], f'native State 250 raw HitDef at F{frame}')
    target = owner ^ 1
    defense = phases['after-collision'][frame]['chars'][target]['finalDefense']
    before = phases['before-command'][frame]['chars'][target]['life']
    amount = 0 if blocked else committed_damage(raw, source['attackMul'], defense, before)
    assert_equal(amount, before - phases['end'][frame + 1]['chars'][target]['life'],
                 f'native State 250 reaction-phase HP at F{frame + 1}')
    return {'amount': amount, 'rng': roll['rng'], 'variance': variance, 'counter': operands['var6']}


def chord_consumption(phases, commands, command_id):
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    consumed = []
    for match in commands['commits']:
        entry, host = match['native'], match['host']
        if entry['to'] != 250:
            continue
        edge = max(int(parts[2]) for candidate in host['candidates'].split(';')
                   if (parts := candidate.split(','))[0] == command_id and parts[-1] == 'True')
        native_seat = entry['hostSeat'] ^ mirror
        buttons = phases['before-command'][edge - 14]['chars'][native_seat]['raw']
        previous = phases['before-command'][edge - 15]['chars'][native_seat]['raw']
        assert_equal(CHORD, buttons & ~previous & CHORD, '250 consumes both new edges of its physical chord')
        assert_equal(False, 'Down' in host['direction'], '250 consumes with a grounded standing direction')
        consumed.append({'submitLogic': int(host['logic']), 'buttonLogic': edge, 'seat': entry['hostSeat'],
                         'age': int(host['logic']) - edge, 'direction': host['direction']})
    return consumed


def check_knuckle_to_wave(scene, moves, reentry, events):
    exits = []
    for entry in scene.moves:
        if (entry['from'], entry['to']) != (300, 1000) or entry['stateTime'] >= 47:
            continue
        seat, logic = entry['hostSeat'], entry['frame'] + 14
        tick = int(moves[seat, logic - 1]['elapsed']) + 1
        assert_equal(True, tick == entry['stateTime'] and tick in (18, 19), '300 second-contact Wave window')
        assert_equal((2, 0, True), (entry['var1'], entry['var2'], entry['moveContact'] > 0),
                     'native 300 contact permission')
        previous = reentry[seat, logic - 1]
        start = int(previous['started'])
        confirmed = max(int(previous[field]) for field in ('hitConfirm', 'blockConfirm'))
        assert_equal(True, confirmed >= start + 17, 'Wave uses this instance’s second-segment confirmation')
        assert_equal(1, len([row for row in events if row['source'] == seat and row['frame'] == confirmed
                            and row['move'] == KNUCKLE and row['kind'] in ('Hit', 'Blocked')]),
                     'Wave contact confirmation has a unique committed 300 contact')
        exits.append({'seat': seat, 'logic': logic, 'stateTime': tick, 'instance': start, 'confirmation': confirmed})
    return exits


def verify_body(profile, changes, name, scene, phases, events, moves, reentry, lines):
    move = next(move for move in profile['moves'] if move['moveId'] == BLOWBACK)
    attack, = move['attacks']
    damage = indexed(audit_rows(lines, 'DamageAudit'))
    assert_equal(set(moves), set(damage), 'complete damage audit')
    assert_equal(set(moves), set(reentry), 'complete move-instance audit')
    check_rng(profile, damage, lines)
    commands = verify_commands(profile, scene, phases, changes, events, moves, damage, lines,
                               reservations=RESERVATIONS)
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    contacts = []
    for event in events:
        if event['kind'] not in ('Hit', 'Blocked') or event['move'] != BLOWBACK:
            continue
        logic, seat, victim = (event[key] for key in ('frame', 'source', 'target'))
        blocked = event['kind'] == 'Blocked'
        source, target = moves[seat, logic], moves[victim, logic]
        stop = attack['hitstopByOutcome']['block' if blocked else 'hit']
        trade = any(row['frame'] == logic and row['source'] == victim and row['target'] == seat
                    and row['kind'] == 'Hit' and row['move'] == BLOWBACK for row in events)
        expected_stop = stop['defenderFrames'] if trade else stop['attackerFrames']
        assert_equal((expected_stop, stop['defenderFrames']), (int(source['stop']), int(target['stop'])),
                     f'250 independent HitPause at L{logic}')
        amount = 0 if blocked else normal_damage(profile, damage, moves, event, attack)
        assert_equal(amount, event['amount'], f'250 committed dynamic damage at L{logic}')
        assert_equal(amount, int(moves[victim, logic - 1]['health']) - int(target['health']), '250 atomic HP')
        oracle = source_damage(profile, phases, attack, logic - 14, seat ^ mirror, blocked)
        contacts.append({**event, 'native': oracle,
                         'tick': int(moves[seat, logic - 1]['elapsed']) + 1 if trade else int(source['elapsed'])})
    deferred = min((row['frame'] for row in changes if row['to'] in RESERVATIONS), default=max(phases['end']) + 1)
    live_wave = {frame: {**world, 'chars': [{**char, 'helpers': [helper for helper in char['helpers'] or []
                                                               if helper['moveContact'] == 0]}
                                            for char in world['chars']]} for frame, world in phases['end'].items()}
    return {'blowbackBody': attack_samples(name, scene, phases, moves, reentry, deferred), 'blowbackContacts': contacts,
            'blowbackExits': inspect_routes(phases, moves, reentry, commands, changes, events), 'commands': commands,
            'chordConsumption': chord_consumption(phases, commands, move['commandId']),
            'knuckleToWave': check_knuckle_to_wave(scene, moves, reentry, events),
            'waveEligibility': check_wave_eligibility({**phases, 'end': live_wave}, lines),
            'waveRelaunch': check_wave_relaunch(scene, phases, moves, lines),
            'guardChain': guard_chain(name, scene, phases, moves),
            'blowbackRecovery': check_blowback_recovery(name, scene, phases, moves, deferred, lines)}


def check_wave_relaunch(scene, phases, moves, lines):
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    launch = indexed(audit_rows(lines, 'LaunchReactionAudit'))
    windows = []
    for contact in scene.contacts:
        frame, seat = contact['frame'], contact['target']
        native_seat = seat ^ mirror
        if (contact['kind'], phases['end'][frame]['chars'][native_seat]['state']) != ('ProjectileHit', 5020):
            continue
        for tick in range(frame, max(phases['end']) + 1):
            source = phases['end'][tick]['chars'][native_seat]
            logic = tick + 14
            phase = 'Shake' if source['state'] == 5020 else 'Lift'
            assert_equal(phase, launch[seat, logic]['phase'], f'Wave relaunch phase at F{tick}')
            assert_equal((round(source['pos'][0] * Q16), round(-source['pos'][1] * Q16)),
                         tuple(int(moves[seat, logic][axis]) for axis in ('x', 'y')),
                         f'Wave relaunch position through the first lift at F{tick}')
            if phase == 'Lift':
                assert_equal(5030, source['state'], 'native Wave relaunch resumes with airborne lift')
                windows.append({'contact': frame, 'seat': seat, 'firstLift': tick})
                break
        else:
            raise ValueError('native trace ends before first Wave relaunch lift')
    return windows


def check_blowback_recovery(name, scene, phases, moves, deferred, lines):
    result = []
    launch = indexed(audit_rows(lines, 'LaunchReactionAudit'))
    mirrored = phases['before-command'][0]['chars'][0]['facing'] < 0
    for impact in scene.contacts:
        source, target, first = (impact[key] for key in ('source', 'target', 'frame'))
        if not any(entry['to'] == 250 and entry['hostSeat'] == source and entry['frame'] + 23 <= first
                   for entry in scene.moves):
            continue
        native_owner = phases['before-command'][first]['chars'][source ^ mirrored]
        if native_owner['state'] != 250:
            continue
        end = min(deferred, next((row['frame'] for row in scene.contacts
                                 if row['target'] == target and row['frame'] > first), max(phases['end']) + 1))
        boundary = None
        blocked = impact['kind'] == 'Blocked'
        launched_from_jump = name == '250-air-held-back'
        origin = None
        if launched_from_jump:
            assert_equal(4, phases['before-command'][first]['chars'][target ^ mirrored]['stateType'],
                         'held-back control reaches contact while the native defender is already airborne')
            assert_equal('Airborne', moves[target, first + 13]['state'], 'host target is also already airborne')
            source = phases['end'][first]['chars'][target ^ mirrored]
            host = moves[target, first + 14]
            origin = {'native': [source['pos'][0], -source['pos'][1]],
                      'host': [int(host[axis]) / Q16 for axis in ('x', 'y')]}
            end = min(end, first + 15)
        reaction = 'Blockstun' if blocked else 'AirHitstun'
        phases_seen = {}
        max_x = max_y = 0.0
        camera = []
        for frame in range(first, end):
            native = phases['end'][frame]['chars'][target ^ mirrored]
            host = moves[target, frame + 14]
            phase = 'Guard' if blocked else {5020: 'Shake', 5201: 'Tech'}.get(native['state'], launch_phase(native))
            if native['ctrl']:
                assert_equal('Crouching' if native['stateType'] == 2 else 'Standing', host['state'],
                             f'250 target control at F{frame}')
                boundary = frame
                break
            if not blocked:
                committed = launch.get((target, frame + 14))
                assert_equal(phase, committed['phase'] if committed else None, f'250 launch phase at F{frame}')
            expected = {'Impact': 'Knockdown', 'LyingImpact': 'Knockdown', 'Lying': 'Knockdown',
                        'Wakeup': 'Wakeup', 'Tech': 'Wakeup'}.get(phase, reaction)
            assert_equal(expected, host['state'], f'250 target posture {phase} at F{frame}')
            phases_seen.setdefault(phase, frame)
            tolerance = 0.005 if blocked else 0.02
            # The fixed-point thresholds match the existing guard and launch checks.
            expected_x, expected_y = native['pos'][0], max(0, -native['pos'][1])
            if origin:
                dx, dy = (host_value - native_value for native_value, host_value in zip(origin['native'], origin['host']))
                expected_x, expected_y = expected_x + dx, expected_y + dy
            if name == '250-trade' and frame >= 76:
                direction = 1 if target == 1 else -1
                assert_equal(direction * 145, expected_x, f'native camera boundary at F{frame}')
                velocity = direction * 3.375 if phase == 'Bounce' else 0
                assert_equal(round(velocity * Q16), int(host['vx']), f'host trade velocity beyond camera at F{frame}')
                previous = moves[target, frame + 13]
                expected_x = int(previous['x']) / Q16 + velocity
                camera.append({'frame': frame, 'nativeX': native['pos'][0], 'hostX': int(host['x']) / Q16})
            y_error = abs(expected_y - int(host['y']) / Q16)
            x_error = abs(expected_x - int(host['x']) / Q16)
            if max(x_error, y_error) > tolerance:
                raise ValueError(f'250 target position at F{frame} differs by {x_error} X, {y_error} Y')
            max_x, max_y = max(max_x, x_error), max(max_y, y_error)
        result.append({'source': 250, 'contact': first, 'target': target, 'recovered': boundary,
                       'lastCompared': boundary if boundary is not None else end - 1,
                       'reactionPhases': phases_seen, 'maxXError': max_x, 'maxYError': max_y,
                       **({'differentPreContactJumpOrigins': origin} if origin else {}),
                       **({'nativeCameraBoundary': [camera[0], camera[-1]]} if camera else {})})
    return result


def guard_chain(name, scene, phases, moves):
    if name != '250-guard-300-wave':
        return []
    guards = list(scene.contacts)
    assert_equal(['Blocked'] * 3 + ['ProjectileBlocked'], [contact['kind'] for contact in guards],
                 'follow-up fixture has three ground guard slides before Wave')
    result = []
    for start, finish in zip(guards, guards[1:]):
        first, last, seat = start['frame'], finish['frame'], start['target']
        assert_equal(seat, finish['target'], 'same guard defender throughout the chain')
        native = phases['end'][first]['chars'][seat]['pos'][0]
        host = int(moves[seat, first + 14]['x']) / Q16
        if not result:
            assert_equal(native, host, 'guard chain starts at the same X before fixed-point sliding')
        greatest = 0.0
        for tick in range(first, last + 1):
            row = phases['end'][tick]['chars'][seat]
            current = moves[seat, tick + 14]
            delta = int(current['x']) / Q16 - row['pos'][0] - (host - native)
            if abs(delta) > 0.005:
                raise ValueError(f'guard segment {first}–{last} differs at F{tick} by {delta} relative to its contact')
            greatest = max(greatest, abs(delta))
        result.append({'start': first, 'end': last, 'maxRelativeError': greatest,
                       'cumulativeOffsetAtNextContact': int(current['x']) / Q16 - row['pos'][0]})
    return result


def check_contacts(guard_phases, name, scene, events, moves, damage, profile, **checks):
    chain = guard_chain(name, scene, guard_phases, moves)
    origin = None if not chain else lambda native, host: chain[-1]['cumulativeOffsetAtNextContact']
    return verify_contacts(scene, events, moves, damage, profile, guard_contact_origin=origin, **checks)


def check_recovery(deferred, name, scene, phases, moves):
    chain = guard_chain(name, scene, phases, moves)
    origin = None if not chain else lambda phases, moves, frame, seat: chain[-1]['cumulativeOffsetAtNextContact']
    return verify_grounded_wave_recovery(scene, phases, moves, crouch_recovery=True, contact_origins=origin)


def check_poses(actions, phases, deferred, scene, events, committed, timelines, modes):
    windows = fighter_poses(scene, events, committed, timelines, modes, actions=actions)
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    launch_actions = set(LAUNCH_ACTIONS.values())
    for event in events:
        if event['kind'] not in ('Hit', 'Blocked') or event['move'] != BLOWBACK:
            continue
        first, seat = event['frame'] - 14, event['target']
        end = min(deferred, next((row['frame'] for row in scene.contacts
                                 if row['target'] == seat and row['frame'] > first), max(phases['end']) + 1))
        if phases['before-command'][first]['chars'][seat ^ mirror]['stateType'] == 4:
            end = min(end, first + 15)
        for frame in range(first + 1, end):
            source = phases['end'][frame]['chars'][seat ^ mirror]
            host = committed[seat, frame + 15]
            if source['ctrl']:
                break
            if event['kind'] == 'Blocked':
                expected = 151 if source['stateType'] == 2 else 150
                assert_equal(str(expected), host['action'], f'250 target guard action at F{frame}')
            elif source['anim'] in launch_actions:
                assert_equal(str(source['anim']), host['action'], f'250 target launch action at F{frame}')
    verify_wave_ground_poses(scene, phases, committed)
    return windows


def main():
    parser, options = validation_options(__doc__, PROFILE)
    identity_paths = (
        ROOT / '.godot/mono/temp/bin/Debug/NewKOF.dll',
        ROOT / '.godot/mono/temp/bin/Debug/NewKOF.BattleCore.dll',
        ROOT / options.profile_dir / f'{options.mode}.fighter.json',
        ROOT / options.profile_dir / f'{options.mode}.binding.json',
        ROOT / 'data/imported-characters/terry98/manifest.json',
    )

    def identity():
        hashes = {}
        for path in identity_paths:
            with path.open('rb') as source:
                hashes[path.relative_to(ROOT).as_posix()] = file_digest(source, 'sha256').hexdigest()
        return hashes

    hashes = identity()
    (options.output / 'candidate.json').write_text(json.dumps(hashes, indent=2) + '\n', encoding='utf-8')
    print(f'Stage 29 Candidate files: {hashes}', flush=True)
    actions = {**ACTIONS, **{move: state for state, move in MOVES.items() if state in (240, 245, 250, 300)}}
    if options.mode == 'mode2':
        actions[MOVES[240]] = 241
    guard_phases = None
    if '250-guard-300-wave' in options.scene:
        reference = locate(options.reference, options.mode, '250-guard-300-wave', FAMILY)
        _, guard_phases, _ = fixture(reference, '250-guard-300-wave', capture_profile='Stage29',
                                     native_moves=MOVES, reservations=RESERVATIONS)
    run_validation(parser, options, family=FAMILY, capture_profile='Stage29',
                   alias=options.visual_alias or f'terry98-stage29-{options.mode}', native_moves=MOVES,
                   reservations=RESERVATIONS, actions=actions, states={250: 250}, body_check=verify_body,
                   recovery_check=check_recovery, pose_check=partial(check_poses, actions),
                   contact_check=partial(check_contacts, guard_phases))
    assert_equal(hashes, identity(), 'candidate files remained stable during replay')


if __name__ == '__main__':
    main()
