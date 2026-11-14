"""Compare Terry State 440 and its Wave transition against native and Vulkan traces."""

import argparse
from dataclasses import replace
from functools import partial
from hashlib import sha256
import json
from pathlib import Path
from subprocess import TimeoutExpired
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'Stage23Validation'))
from crouch_heavy_duel import (CONTACTS, NATIVE, VISUALS, check_rng, check_wave_launch, launch_phase,
                               minimum_replay_frames, native_damage)
from crouch_wave_duel import local_boxes, native_boxes
from heavy_wave_duel import normal_damage
from near_wave_duel import capture_sparse_realtime
from power_wave_duel import (AUTHORITY, LAUNCH_ACTIONS, MOVE, POSE_KEYS, Q16, RAW_KEYS, ROOT, assert_equal, audit_rows,
                             fighter_poses, indexed, native_case, require_same, run_case, verify_contacts,
                             verify_grounded_wave_recovery, visual_timeline)


SWEEP = 'TerryCrouchHeavyKick440'
FAMILY = 'crouch-kick-wave'
PROFILE = 'data/imported-characters/terry98-stage24-final'
SOURCE_KEYS = (*RAW_KEYS, (16, 'U', 'Slash'), (32, 'I', 'Semicolon'))
SOURCE_MOVES = {**NATIVE, 440: SWEEP}
ACTIONS = {**VISUALS, SWEEP: 440}
TRIP_PHASES = {5070: 'Shake', 5071: 'Fall', 5100: 'Impact', 5101: 'Bounce', 5120: 'Wakeup', 5201: 'Tech'}
COMMAND_AUTHORITY = b'[CommandAudit] '
WAVE_RESTRICTION_AUTHORITY = b'[WaveRestrictionAudit] '
WAVE_CAMERA_CLAMPS = {'incoming-wave-16': 91, 'incoming-wave-26': 88}
PREVIOUS_STAGES = {'Stage22': ('crouch-wave', {'hit-held-11'}),
                   'Stage23': ('crouch-heavy-wave', {'hit-22', 'whiff-11'})}


def contact_is_wave(target, owner):
    if target['hitId'] == 20 and owner['state'] == 440:
        return False
    if 'hitPlayerId' in target:
        return any(helper['anim'] == 1020 and helper['id'] == target['hitPlayerId']
                   for helper in owner['helpers'] or [])
    return any(helper['anim'] == 1020 for helper in owner['helpers'] or [])


def coordinate_errors(row, source):
    return (abs(int(row['x']) / Q16 - source['pos'][0]), abs(int(row['y']) / Q16 - max(0, -source['pos'][1])))


def locate(references, mode, name, family=FAMILY):
    filename = f'{mode}-{family}-{name}.jsonl'
    match = next((directory / filename for directory in references if (directory / filename).is_file()), None)
    if match is None:
        raise ValueError(f'missing native fixture {filename}')
    return match


def fixture(references, reference, name, mode, *, capture_profile='Stage24'):
    run = json.loads((reference.parent / 'run-metadata.json').read_bytes())
    metadata = json.loads(reference.with_name(f'{reference.stem}-metadata.json').read_bytes())
    assert_equal(capture_profile, run['capture_profile'], 'native capture profile')
    assert_equal(True, reference.stem in run['executed_cases'], 'native case appears in run metadata')
    assert_equal(metadata['trace_sha256'], sha256(reference.read_bytes()).hexdigest(), 'native trace SHA-256')
    scene, phases, changes = native_case(reference, name, last_frame=run['last_frame'], native_moves=SOURCE_MOVES,
                                        raw_keys=SOURCE_KEYS,
                                        contact_is_wave=contact_is_wave if capture_profile == 'Stage24' else None)
    known_bits = sum(bit for bit, _, _ in SOURCE_KEYS)
    for frame, world in phases['before-command'].items():
        if any(char['raw'] & ~known_bits for char in world['chars']):
            raise ValueError(f'unsupported native input at F{frame}: {reference.name}')
    if mode == 'mode2' and any(row['to'] == 1050 for row in changes):
        control_reference = locate(references, mode, 'hit-0')
        control, control_phases, control_changes = fixture(references, control_reference, 'hit-0', mode)
        boundary = next(row['frame'] for row in changes if row['to'] == 1050)
        assert_equal(tuple(row for row in control.contacts if row['frame'] < boundary),
                     tuple(row for row in scene.contacts if row['frame'] < boundary), 'mode 2 shared contact prefix')
        phases['end'] = control_phases['end']
        phases['after-collision'] = control_phases['after-collision']
        changes = control_changes
        scene = replace(scene, contacts=control.contacts, differences=('native-mode2-1050-outside-scope',))
    reserved = [row for row in changes if row['to'] in (310, 430)]
    if reserved:
        entry, = reserved
        if entry['from'] in SOURCE_MOVES and (entry['from'], entry['to'], entry['stateTime'], entry['var2']) != (440, 430, 40, 1):
            raise ValueError(f'fixture {name} requires an unsupported State {entry["to"]} cancel')
        first = entry['frame']
        assert_equal([], [row for row in scene.moves if row['frame'] >= first], 'no later native in-scope moves')
        scene = replace(scene, contacts=tuple(row for row in scene.contacts if row['frame'] < first),
                        differences=(*scene.differences, f'native-command-normal-{entry["to"]}-outside-scope'))
    elif name in ('stand-0', 'air-0', 'stand-to-down-0', 'jump-and-kick-0'):
        other_state = 640 if name == 'air-0' else 245
        first = next(frame for frame, world in phases['end'].items() if world['chars'][0]['state'] == other_state)
        assert_equal([], list(scene.moves), f'native State {other_state} never enters State 440')
        scene = replace(scene, contacts=tuple(row for row in scene.contacts if row['frame'] < first),
                        differences=(*scene.differences, f'native-{other_state}-outside-scope'))
    phases['damage-rows'] = [row for row in map(json.loads, reference.read_text(encoding='utf-8').splitlines())
                             if row['phase'] in ('hitdef', 'var21-draw')]
    return scene, phases, changes


def trip_phase(source):
    if source['state'] == 5110:
        return 'LyingImpact' if source['anim'] == 5170 else 'Lying'
    return TRIP_PHASES.get(source['state'])


def check_ground_recovery(name, scene, phases, moves):
    tolerance = 0.006 if name == 'crouch-guard-27' else 0.005
    result = verify_grounded_wave_recovery(scene, phases, moves, crouch_recovery=True, position_tolerance=tolerance)
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    for contact in scene.contacts:
        if contact['kind'] != 'Blocked':
            continue
        frame, seat = contact['frame'], contact['target']
        attacker = phases['after-collision'][frame]['chars'][contact['source'] ^ mirror]
        if attacker['state'] != 440:
            continue
        end = next((other['frame'] for other in scene.contacts if other['frame'] > frame and other['target'] == seat),
                   max(phases['end']) + 1)
        for tick in range(frame, end):
            source = phases['end'][tick]['chars'][seat ^ mirror]
            host = moves[seat, tick + 14]
            expected = ('Crouching' if source['stateType'] == 2 else 'Standing') if source['ctrl'] else 'Blockstun'
            assert_equal(expected, host['state'], f'State 440 low block recovery at F{tick}')
            if abs(source['pos'][0] - int(host['x']) / Q16) > 0.005:
                raise ValueError(f'State 440 low block X differs at F{tick}')
            if source['ctrl']:
                result.append({'frame': tick, 'state': host['state'], 'sourceState': source['state'],
                               'x': int(host['x']) / Q16})
                break
    return result


def check_double_crouch_guard(scene, phases, moves):
    first, second = scene.contacts
    assert_equal(('Blocked', 'ProjectileBlocked'), (first['kind'], second['kind']), 'two crouch guard contacts')
    assert_equal(first['target'], second['target'], 'two crouch guard reactions belong to same target')
    seat = first['target']
    native_seat = seat ^ (phases['before-command'][0]['chars'][0]['facing'] < 0)
    expected = int(moves[seat, first['frame'] + 14]['x'])
    direction = -1 if moves[seat, first['frame'] + 14]['facing'] == 'Right' else 1
    comparisons = []
    for contact in (first, second):
        frame = contact['frame']
        push = 21 * Q16 // 2
        retention = 41 * Q16 // 50
        for tick in range(frame, frame + 30):
            if frame + 12 <= tick <= frame + 28:
                expected += direction * push
                push = push * retention // Q16
            row = moves[seat, tick + 14]
            assert_equal(expected, int(row['x']), f'authoritative crouch-guard fixed-point decay at F{tick}')
        native = phases['end'][frame + 29]['chars'][native_seat]['pos'][0]
        comparisons.append({'contact': frame, 'recovery': frame + 29, 'nativeX': native,
                            'hostX': expected / Q16, 'errorX': expected / Q16 - native})
    return comparisons


def check_commands(profile, changes, name, phases, events, lines):
    table = indexed(audit_rows(lines, 'CommandAudit'))
    command_by_move = {move['moveId']: move['commandId'] for move in profile['moves']}
    started = {(row['source'], row['frame']): row['move'] for row in events if row['kind'] == 'MoveStarted'}
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    native = {((entry['seat'] - 1) ^ mirror, entry['frame'] + 14): entry
              for entry in changes if entry['to'] in SOURCE_MOVES}
    assert_equal(started, {key: row['committed'] for key, row in table.items() if row['committed'] != '-'},
                 'command audit actual move commits')
    commits = []
    for key, move in started.items():
        row = table[key]
        candidates = [value.split(',') for value in row['candidates'].split(';')]
        if not any(parts[0] == command_by_move[move] and parts[-1] == 'True' for parts in candidates):
            raise ValueError(f'committed {move} has no consumed command at L{row["logic"]}')
        source = native[key]
        assert_equal(move, SOURCE_MOVES[source['to']], 'native actual move commit')
        commits.append({'host': row, 'native': source})
    reserved = []
    for entry in changes:
        if entry['to'] not in (310, 430):
            continue
        seat, logic = (entry['seat'] - 1) ^ mirror, entry['frame'] + 14
        row = table[seat, logic]
        candidates = [value.split(',') for value in row['candidates'].split(';')]
        reserve = {310: 'ReservedRisingUpper', 430: 'ReservedCrouchLightKick'}[entry['to']]
        if not any(parts[0] == reserve and parts[-2:] == ['True', 'True'] for parts in candidates):
            raise ValueError(f'original State {entry["to"]} candidate did not reserve its physical input at L{logic}')
        assert_equal('-', row['committed'], f'State {entry["to"]} reservation commits no move')
        reserved.append(row)
    if name in ('stand-0', 'stand-to-down-0'):
        frame = next(frame for frame, world in phases['end'].items() if world['chars'][0]['state'] == 245)
        row = table[0, frame + 14]
        assert_equal('-', row['committed'], 'standing kick reservation commits no move')
        candidates = [value.split(',') for value in row['candidates'].split(';')]
        if not any(parts[0] == 'ReservedStandHeavyKick' and parts[-2:] == ['True', 'True'] for parts in candidates):
            raise ValueError(f'standing heavy kick was not reserved at F{frame}')
        reserved.append(row)
    motion_rows = []
    for frame, world in phases['after-command'].items():
        for native_seat, source in enumerate(world['chars']):
            if not source['powerWaveCommand'] and source['helperWave'] <= 0:
                continue
            seat, logic = native_seat ^ mirror, frame + 14
            motion_rows.append({'nativeFrame': frame, 'seat': seat, 'root': source['powerWaveCommand'],
                                'helperTicks': source['helperWave'], 'permission': source['vars']['var1'],
                                'state': source['state'], 'tick': source['time'], 'host': table.get((seat, logic))})
    return {'commits': commits, 'reservedInputs': reserved, 'motionRecognition': motion_rows,
            'buffer': list(table.values())}


def trip_trajectory(scene, phases, events, moves, lines):
    launch = indexed(audit_rows(lines, 'LaunchReactionAudit'))
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    trips = []
    for event in events:
        if event['move'] != SWEEP or event['kind'] != 'Hit':
            continue
        seat, first = event['target'], event['frame'] - 14
        end = next((contact['frame'] for contact in scene.contacts
                    if contact['frame'] > first and contact['target'] == seat), max(phases['end']) + 1)
        starts = {}
        max_error_x = max_error_y = 0.0
        for frame in range(first, end):
            logic = frame + 14
            source = phases['end'][frame]['chars'][seat ^ mirror]
            phase = trip_phase(source)
            row = moves[seat, logic]
            committed = launch.get((seat, logic))
            assert_equal(phase, committed['phase'] if committed else None, f'State 440 trip phase at F{frame}')
            dx, dy = coordinate_errors(row, source)
            if dx > 0.005 or dy > 0.005:
                raise ValueError(f'State 440 trip position at F{frame}: X error {dx}, Y error {dy}')
            max_error_x, max_error_y = max(max_error_x, dx), max(max_error_y, dy)
            if phase is None:
                assert_equal(True, source['ctrl'], f'State 440 target control at F{frame}')
                assert_equal('Crouching' if source['stateType'] == 2 else 'Standing', row['state'],
                             f'host State 440 target control at F{frame}')
                starts['Control'] = frame
                break
            starts.setdefault(phase, frame)
            if frame > first and phase in ('Shake', 'Fall'):
                assert_equal(native_boxes(source, 'clsn2'), local_boxes(row, 'hurt'),
                             f'State 440 trip Clsn2 at F{frame}')
        if end == max(phases['end']) + 1:
            common = {'Shake', 'Fall', 'Control'}
            phases_required = common | ({'Tech'} if 'Tech' in starts else
                                       {'Impact', 'Bounce', 'LyingImpact', 'Lying', 'Wakeup'})
            assert_equal(phases_required, set(starts), 'full State 440 trip recovery')
        trips.append({'contact': first, 'target': seat, 'phaseStarts': starts,
                      'maxXError': max_error_x, 'maxYError': max_error_y})
    return trips


def check_wave_eligibility(phases, lines):
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    rows = []
    native = {}
    for frame, world in phases['end'].items():
        for native_seat, owner in enumerate(world['chars']):
            for helper in owner['helpers'] or []:
                if helper['anim'] == 1020 and 2 <= helper['time'] <= 13:
                    key = (frame, native_seat ^ mirror, helper['time'] - 1)
                    native[key] = (owner['vars']['var12'], helper['hitDef']['attr'])
    restriction = {(int(row['logic']), int(row['id'])): row['airFollowupRestricted'] == 'True'
                   for row in audit_rows(lines, 'WaveRestrictionAudit')}
    for record in audit_rows(lines, 'WaveEntityAudit'):
        logic, seat, age = int(record['logic']), int(record['owner']), 25 - int(record['life'])
        frame = logic - 14
        key = (frame, seat, age)
        if key not in native:
            continue
        lock, attr = native[key]
        if lock:
            assert_equal(0, attr, f'root var(12) disables native Helper HitDef at F{frame}')
        restricted = restriction[logic, int(record['id'])]
        assert_equal(bool(lock), restricted, f'host and native air-followup restriction at F{frame}')
        rows.append({'frame': frame, 'owner': seat, 'age': age, 'nativeOwnerLock': lock,
                     'nativeHitDefAttribute': attr, 'hostAirFollowupRestricted': restricted})
    return rows


def verify_cancel_source(scene, phases, moves, changes):
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    samples = []
    for change in changes:
        if (change['from'], change['to']) != (440, 1000):
            continue
        frame = change['frame']
        seat = (change['seat'] - 1) ^ mirror
        old = change['stateTime']
        if old not in range(11, 18) and old != 40:
            raise ValueError(f'unknown native State 440 Wave permission at old tick {old}')
        if old == 40:
            assert_equal(1, change['var2'], 'native natural State 440 exit permission')
        else:
            assert_equal(1, change['var1'], 'native active State 440 cancel permission')
        if not change['powerWaveCommand'] and change['helperWave'] <= 0:
            raise ValueError(f'native State 440 Wave conversion without command at F{frame}')
        prior = moves[seat ^ 1, frame + 13]
        after = moves[seat ^ 1, frame + 14]
        assert_equal(prior['health'], after['health'], 'cancel does not mutate prior defender HP')
        samples.append({'frame': frame, 'seat': seat, 'oldTick': old, 'nativeRoot': change['powerWaveCommand'],
                        'nativeHelper': change['helperWave'], 'targetBefore': prior['state'], 'targetAfter': after['state']})
    return samples


def verify_wave_ground_poses(scene, phases, committed):
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    for contact in scene.contacts:
        if contact['kind'] != 'ProjectileHit' or contact['fall']:
            continue
        seat, first = contact['target'], contact['frame']
        for frame in range(first + 1, max(phases['after-collision']) + 1):
            source = phases['after-collision'][frame]['chars'][seat ^ mirror]
            if source['anim'] not in (5012, 5017) or (seat, frame + 15) not in committed:
                break
            rendered = committed[seat, frame + 15]
            assert_equal((str(source['anim']), ','.join(map(str, source['frame']['sprite']))),
                         (rendered['action'], rendered['sprite']), f'grounded Wave image at F{frame}')


def check_body(profile, changes, name, scene, phases, events, moves, reentry, lines):
    definition = next(move for move in profile['moves'] if move['moveId'] == SWEEP)
    attack, = definition['attacks']
    damage = indexed(audit_rows(lines, 'DamageAudit'))
    assert_equal(set(moves), set(damage), 'complete damage audit')
    assert_equal(set(moves), set(reentry), 'complete attack instance audit')
    check_rng(profile, damage, lines)
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    last = min(max(phases['after-collision']) + 14, max(frame for _, frame in moves))
    starts = [row for row in events if row['kind'] == 'MoveStarted' and row['move'] == SWEEP]
    for start in starts:
        seat, logic = start['source'], start['frame']
        for frame in range(logic, last):
            row = moves[seat, frame]
            if row['move'] != SWEEP or int(reentry[seat, frame]['started']) != logic:
                break
            source = phases['after-collision'][frame - 14]['chars'][seat ^ mirror]
            tick = int(row['elapsed'])
            assert_equal((440, 2, tick + 1), (source['state'], source['stateType'], source['time']),
                         f'native State 440 clock at L{frame}')
            assert_equal(10 <= tick <= 17, bool(row['attacks']), f'State 440 attack window at L{frame}')
            assert_equal(native_boxes(source, 'clsn1'), local_boxes(row, 'attacks'), f'State 440 Clsn1 at L{frame}')
            assert_equal(native_boxes(source, 'clsn2'), local_boxes(row, 'hurt'), f'State 440 Clsn2 at L{frame}')
    contacts = []
    for event in events:
        if event['move'] != SWEEP or event['kind'] not in ('Hit', 'Blocked'):
            continue
        frame, seat, target = event['frame'], event['source'], event['target']
        blocked = event['kind'] == 'Blocked'
        pause = attack['hitstopByOutcome']['block' if blocked else 'hit']
        assert_equal((pause['attackerFrames'], pause['defenderFrames']),
                     (int(moves[seat, frame]['stop']), int(moves[target, frame]['stop'])), 'independent State 440 HitPause')
        amount = 0 if blocked else normal_damage(profile, damage, moves, event, attack)
        assert_equal(amount, event['amount'], 'host State 440 dynamic damage')
        assert_equal(amount, int(moves[target, frame - 1]['health']) - int(moves[target, frame]['health']),
                     'host atomic State 440 HP')
        assert_equal(str(frame), reentry[seat, frame]['blockConfirm' if blocked else 'hitConfirm'],
                     'State 440 contact confirmation')
        original = native_damage(profile, phases, attack, frame - 14, seat ^ mirror, blocked, state=440)
        contacts.append({**event, 'native': original, 'tick': int(moves[seat, frame]['elapsed'])})
    exits = []
    for change in changes:
        if change['from'] != 440:
            continue
        frame, seat = change['frame'] + 14, (change['seat'] - 1) ^ mirror
        old, new = moves[seat, frame - 1], moves[seat, frame]
        target = change['to']
        if target in SOURCE_MOVES:
            assert_equal(change['stateTime'], int(old['elapsed']) + 1, f'old State 440 cancel time at L{frame}')
            assert_equal('0', old['stop'], 'cancel commits on a runnable action tick')
            assert_equal((SOURCE_MOVES[target], '0', ''), (new['move'], new['elapsed'], new['attacks']),
                         'new instance starts without an inherited attack box')
            assert_equal(('1', '0'), (damage[seat, frame]['divisor'], damage[seat, frame]['pause']),
                         'new instance damage-local state')
        elif target == 11:
            assert_equal('Crouching', new['state'], 'State 440 naturally exits through crouch')
            for logic in range(frame, last):
                source = phases['after-collision'][logic - 14]['chars'][seat ^ mirror]
                if source['state'] not in (11, 12):
                    break
                row = moves[seat, logic]
                assert_equal('Crouching' if source['state'] == 11 else 'Standing', row['state'],
                             f'crouch exit posture at L{logic}')
                assert_equal(native_boxes(source, 'clsn2'), local_boxes(row, 'hurt'), f'crouch exit Clsn2 at L{logic}')
        elif target in (5000, 5010, 5070):
            assert_equal('-', new['move'], 'interruption clears State 440 attack instance')
            assert_equal('', new['attacks'], 'interruption clears State 440 attack boxes')
        assert_equal([], [event for event in events if event['frame'] == frame and event['source'] == seat
                          and event['move'] == SWEEP and event['kind'] in CONTACTS], 'no stale State 440 contact')
        exits.append({'logic': frame, 'seat': seat, 'sourceTick': change['stateTime'], 'nativeDestination': target})
    result = {'sweepStarts': starts, 'sweepContacts': contacts, 'sweepExits': exits,
              'tripRecovery': trip_trajectory(scene, phases, events, moves, lines),
              'waveLaunchRecovery': check_wave_launch(f'stage24-{name}', scene, phases, moves, lines,
                                                      camera_clip=WAVE_CAMERA_CLAMPS.get(name)),
              'waveEligibility': check_wave_eligibility(phases, lines),
              'cancelSource': verify_cancel_source(scene, phases, moves, changes),
              'commands': check_commands(profile, changes, name, phases, events, lines)}
    if name == 'crouch-guard-27':
        result['crouchGuardFixedPoint'] = check_double_crouch_guard(scene, phases, moves)
    return result


def check_coexistence(profile, changes, name, scene, phases, events, moves, reentry, lines):
    # The older attacks still use their own original reactions; only shared route state is compared here.
    check_rng(profile, indexed(audit_rows(lines, 'DamageAudit')), lines)
    restriction = audit_rows(lines, 'WaveRestrictionAudit')
    if not restriction:
        raise ValueError('legacy route must create its own Wave')
    assert_equal({'False'}, {row['airFollowupRestricted'] for row in restriction},
                 'legacy route does not inherit the State 440 air-followup restriction')
    result = {'commands': check_commands(profile, changes, name, phases, events, lines),
              'waveEligibility': restriction,
              'waveLaunchRecovery': check_wave_launch(f'coexistence-{name}', scene, phases, moves, lines)}
    return result


def check_poses(phases, scene, events, committed, timelines, modes):
    windows = fighter_poses(scene, events, committed, timelines, modes, actions=ACTIONS)
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    for event in events:
        if event['move'] != SWEEP:
            continue
        seat, first = event['target'], event['frame'] - 14
        if event['kind'] == 'Blocked':
            assert_equal('151', committed[seat, first + 15]['action'], f'State 440 low guard image at F{first}')
        if event['kind'] != 'Hit':
            continue
        assert_equal('5070', committed[seat, first + 15]['action'], f'atomic trip shake image at F{first}')
        next_hit = next((contact['frame'] for contact in scene.contacts if contact['target'] == seat and contact['frame'] > first),
                        max(phases['end']) + 1)
        for frame in range(first + 1, next_hit):
            original = phases['end'][frame]['chars'][seat ^ mirror]
            phase = trip_phase(original)
            row = committed.get((seat, frame + 15))
            if row is None or phase is None:
                break
            assert_equal((str(original['anim']), ','.join(map(str, original['frame']['sprite']))),
                         (row['action'], row['sprite']), f'actual trip image at F{frame}')
    for event in events:
        if event['move'] != NATIVE[205] or event['kind'] != 'Hit':
            continue
        seat, first = event['target'], event['frame'] - 14
        if phases['after-collision'][first]['chars'][seat ^ mirror]['state'] != 440:
            continue
        for frame in range(first + 1, max(phases['end']) + 1):
            original = phases['end'][frame]['chars'][seat ^ mirror]
            row = committed.get((seat, frame + 15))
            if row is None or original['state'] not in (5010, 5011):
                break
            assert_equal((str(original['anim']), ','.join(map(str, original['frame']['sprite']))),
                         (row['action'], row['sprite']), f'State 440 interruption pose at F{frame}')
    for seat in range(2):
        after_sweep = False
        for frame, world in phases['after-collision'].items():
            original = world['chars'][seat ^ mirror]
            row = committed.get((seat, frame + 15))
            if row is None:
                continue
            if original['state'] == 440:
                after_sweep = True
            elif original['state'] == 12 and after_sweep:
                tick = original['time'] - 1
                assert_equal((str(tick), timelines[12][tick]), (row['tick'], tuple(row[key] for key in POSE_KEYS)),
                             f'Action 12 after State 440 at F{frame}')
            elif original['state'] != 11:
                after_sweep = False
    for contact in scene.contacts:
        if contact['kind'] != 'ProjectileHit' or not contact['fall']:
            continue
        seat, frame = contact['target'], contact['frame']
        for tick in range(frame, max(phases['end']) + 1):
            source = phases['end'][tick]['chars'][seat ^ mirror]
            if source['ctrl'] or (seat, tick + 15) not in committed:
                break
            phase = launch_phase(source)
            if phase:
                assert_equal(str(LAUNCH_ACTIONS[phase]), committed[seat, tick + 15]['action'],
                             f'Wave reaction sprite at F{tick}')
    verify_wave_ground_poses(scene, phases, committed)
    return windows


def realtime_route(starts, events):
    assert_equal([SWEEP, MOVE], [row['move'] for row in starts], 'realtime State 440 into Wave')
    sweep = [row for row in events if row['kind'] in ('Hit', 'Blocked') and row['move'] == SWEEP]
    tick = starts[1]['frame'] - starts[0]['frame'] - (11 if sweep else 0)
    if not 11 <= tick <= 17:
        raise ValueError(f'realtime State 440 cancel outside source window at tick {tick}')
    if sweep:
        assert_equal(10, sweep[0]['frame'] - starts[0]['frame'], 'realtime first State 440 contact')
    contacts = [row for row in events if row['kind'] in ('ProjectileHit', 'ProjectileBlocked')]
    return contacts[0] if contacts else None


def compare_repeat(output, name, original):
    paths = [directory / name / 'sprite/duel.log' for directory in (output, original)]
    prefixes = (*AUTHORITY, COMMAND_AUTHORITY, WAVE_RESTRICTION_AUTHORITY)
    rows = [[line for line in path.read_bytes().splitlines() if line.startswith(prefixes)] for path in paths]
    if not rows[0]:
        raise ValueError(f'missing authority rows in {paths[0]}')
    require_same(*rows, f'same-seed Stage 24 replay {name}')
    print(f'same-seed {name}: {len(rows[0])} exact authority rows', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--godot', required=True, type=Path)
    parser.add_argument('--reference', required=True, type=Path, action='append')
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--profile-dir', default=PROFILE)
    parser.add_argument('--scene', action='append', required=True)
    parser.add_argument('--mode', default='default', choices=('default', 'mode2'))
    parser.add_argument('--previous-stage', choices=PREVIOUS_STAGES)
    parser.add_argument('--compare-plain', action='store_true')
    parser.add_argument('--repeat-from', type=Path)
    parser.add_argument('--fps', default='fixed', choices=('fixed', '30', '60'))
    parser.add_argument('--fixed-window', type=int, nargs=2)
    parser.add_argument('--fixed-resolution', choices=('960x540', '640x360'), default='960x540')
    parser.add_argument('--realtime-window', type=int, nargs=2)
    parser.add_argument('--realtime-input-spacing', type=int, choices=(1, 2), default=1)
    options = parser.parse_args()
    fixed = options.fps == 'fixed'
    if options.previous_stage and (not fixed or options.mode != 'default' or
                                   not set(options.scene) <= PREVIOUS_STAGES[options.previous_stage][1]):
        parser.error('--previous-stage requires a supported fixed default fixture')
    if not fixed:
        if options.mode != 'default' or options.compare_plain or options.repeat_from or options.fixed_window:
            parser.error('realtime requires the default profile without fixed comparisons')
        if not options.realtime_window or not 0 < options.realtime_window[0] <= options.realtime_window[1] < options.realtime_window[0] + 3:
            parser.error('realtime requires a capture window of up to three renders')
    elif options.fixed_window and not 0 < options.fixed_window[0] <= options.fixed_window[1] < options.fixed_window[0] + 90:
        parser.error('fixed window must include 1–90 renders')
    options.output = options.output.resolve()
    options.output.mkdir(parents=True, exist_ok=False)
    options.recovery = False
    options.disable_vsync = True
    options.require_clean_sample = True
    options.require_no_backlog = not fixed
    options.realtime_resolution = '320x180'
    options.realtime_warmup_frames = 150
    options.realtime_settle_frames = 75
    options.realtime_tail_frames = 150
    manifest = json.loads((ROOT / 'data/imported-characters/terry98/manifest.json').read_bytes())
    timelines = {action: visual_timeline(manifest, action) for action in (12, 200, 205, 210, 211, 215, 400, 410, 440, 1000, 1020)}
    profile = json.loads((ROOT / options.profile_dir / f'{options.mode}.fighter.json').read_bytes())
    alias = f'terry98-stage24-{options.mode}'
    failed = []
    for name in options.scene:
        try:
            family = PREVIOUS_STAGES[options.previous_stage][0] if options.previous_stage else FAMILY
            reference = locate(options.reference, options.mode, name, family)
            scene, phases, changes = fixture(options.reference, reference, name, options.mode,
                                             capture_profile=options.previous_stage or 'Stage24')
            if not fixed:
                capture_sparse_realtime(options, name, scene, alias, realtime_route, timelines=timelines, actions=ACTIONS)
                continue
            first = min((row['frame'] for row in scene.moves), default=5)
            waves = [row['frame'] for row in scene.moves if row['move'] == MOVE]
            final = max(waves) + 50 if waves else first + 72
            scene = replace(scene, captures=tuple(options.fixed_window or (max(first + 23, final - 89), final)))
            verify = check_coexistence if options.previous_stage else check_body
            run_case(options, name, reference, timelines, profile, mode=options.mode, alias=alias,
                     prepared_scene=(scene, phases, changes), fixed_checks=partial(verify, profile, changes, name),
                     contact_check=partial(verify_contacts, guard_position_tolerance=0.003 if name == 'crouch-guard-27' else 0.002),
                     recovery_check=partial(check_ground_recovery, name),
                     pose_check=partial(check_poses, phases), partial_capture=True, allow_interrupted_spawn=True,
                     minimum_render_frames=minimum_replay_frames(scene, phases, changes),
                     extra_arguments=('--command-audit',), extra_authority=(COMMAND_AUTHORITY, WAVE_RESTRICTION_AUTHORITY))
            if options.repeat_from:
                compare_repeat(options.output, name, options.repeat_from)
        except (ValueError, KeyError, IndexError, OSError, StopIteration, TimeoutExpired) as error:
            print(f'{name}: FAIL {error!r}', flush=True)
            failed.append(name)
    if failed:
        parser.exit(1, 'Failed: ' + ', '.join(failed) + '\n')


if __name__ == '__main__':
    main()
