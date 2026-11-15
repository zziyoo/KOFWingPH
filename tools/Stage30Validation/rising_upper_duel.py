"""Replay signed Terry State 310 physical inputs and verify real Vulkan submissions."""

from functools import cache, partial
from hashlib import file_digest
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'Stage29Validation'))
from ground_blowback_duel import (
    ACTIONS, MOVES, RESERVATIONS as OLD_RESERVATIONS, check_blowback_recovery,
    check_knuckle_to_wave, check_wave_relaunch, source_damage,
)
from standing_light_kick_duel import (
    Q16, ROOT, assert_equal, audit_rows, check_poses as check_ground_poses, check_recovery as check_ground_recovery,
    check_rng, check_wave_eligibility, fixture, indexed, local_boxes, locate, native_boxes, normal_damage,
    run_validation, validation_options, verify_commands,
)
from crouch_heavy_duel import LAUNCH_ACTIONS, launch_phase
from power_wave_duel import verify_contacts


FAMILY = 'standing-heavy-kick'
PROFILE = 'data/imported-characters/terry98-stage30-final'
UPPER = 'TerryRisingUpper310'
STATE = 310
LENGTH = 33
ACTIVE = range(3, 9)
START_WINDOWS = {
    200: range(4, 7), 205: range(4, 7), 215: range(4, 13), 235: range(5, 9),
    245: range(4, 9), 250: range(24, 26), 400: range(4, 5), 410: range(6, 13), 440: range(11, 18),
}
NATIVE_MOVES = {**MOVES, STATE: UPPER}
RESERVATIONS = {state: command for state, command in OLD_RESERVATIONS.items() if state != STATE}
RESERVATIONS[1200] = 'ReservedRisingTackle1200'
CONTACTS = {'Hit', 'Blocked'}


def candidate_identity(options):
    root = ROOT / options.profile_dir
    paths = [
        ROOT / '.godot/mono/temp/bin/Debug/NewKOF.dll',
        ROOT / '.godot/mono/temp/bin/Debug/NewKOF.BattleCore.dll',
        ROOT / 'project.godot',
        ROOT / 'data/imported-characters/terry98/manifest.json',
        *(root / f'{mode}.{kind}.json' for mode in ('default', 'mode2') for kind in ('fighter', 'binding', 'source')),
    ]
    hashes = {}
    for path in paths:
        with path.open('rb') as source:
            hashes[path.relative_to(ROOT).as_posix()] = file_digest(source, 'sha256').hexdigest()
    return hashes


def seat_index(change, mirrored):
    return (change['seat'] - 1) ^ mirrored


def body_samples(scene, phases, moves, reentry, limit):
    mirrored = phases['before-command'][0]['chars'][0]['facing'] < 0
    samples = []
    for entry in scene.moves:
        if entry['to'] != STATE:
            continue
        first = entry['frame']
        seat = entry['hostSeat']
        body = []
        native_start = phases['after-collision'][first]['chars'][seat ^ mirrored]['pos'][0]
        host_start = int(moves[seat, first + 14]['x']) / Q16
        for frame in range(first, limit):
            logic = frame + 14
            row = moves[seat, logic]
            if row['move'] != UPPER or reentry[seat, logic]['started'] != str(first + 14):
                break
            native = phases['after-collision'][frame]['chars'][seat ^ mirrored]
            tick = int(row['elapsed'])
            assert_equal((STATE, STATE, tick + 1), (native['state'], native['anim'], native['time']),
                         f'State/Action {STATE} time at F{frame}')
            assert_equal((0, 0, 0), tuple(int(row[axis]) for axis in ('y', 'vx', 'vy')),
                         f'grounded stationary velocity at F{frame}')
            assert_equal('Right' if native['facing'] > 0 else 'Left', row['facing'], f'310 facing at F{frame}')
            relative_x = int(row['x']) / Q16 - host_start - (native['pos'][0] - native_start)
            if abs(relative_x) > 0.005:
                raise ValueError(f'310 relative X differs at F{frame} by {relative_x}')
            assert_equal(tick in ACTIVE, bool(row['attacks']), f'310 active window at F{frame}')
            for native_key, host_key in (('clsn1', 'attacks'), ('clsn2', 'hurt')):
                assert_equal(native_boxes(native, native_key), local_boxes(row, host_key),
                             f'310 {native_key} at F{frame}')
            body.append({'frame': frame, 'elapsed': tick, 'action': native['anim'], 'x': int(row['x']) / Q16,
                         'hitpause': int(row['stop']), 'facing': row['facing']})
        if not body:
            raise ValueError(f'no State 310 body frames at F{first}')
        samples.append({'seat': seat, 'entry': first, 'frames': body})
    return samples


def attack_contacts(profile, phases, attacks, moves, reentry, damage, events):
    mirrored = phases['before-command'][0]['chars'][0]['facing'] < 0
    contacts = []
    instances = set()
    for event in events:
        if event['move'] != UPPER or event['kind'] not in CONTACTS:
            continue
        logic, seat, target = (event[key] for key in ('frame', 'source', 'target'))
        frame = logic - 14
        blocked = event['kind'] == 'Blocked'
        actor, victim = moves[seat, logic], moves[target, logic]
        interrupted = actor['move'] != UPPER
        clock_logic = logic - interrupted
        start = int(reentry[seat, clock_logic]['started'])
        assert_equal(False, (seat, start) in instances, f'one contact group per 310 instance at F{frame}')
        instances.add((seat, start))
        tick = int(moves[seat, clock_logic]['elapsed']) + interrupted
        assert_equal(True, tick in ACTIVE, f'310 contact occurs in active AIR elements at F{frame}')
        attack, = (attack for attack in attacks if attack['firstActiveFrame'] <= tick <= attack['lastActiveFrame'])
        stop = attack['hitstopByOutcome']['block' if blocked else 'hit']
        assert_equal(stop['defenderFrames'], int(victim['stop']), f'310 defender HitPause at F{frame}')
        if not interrupted:
            assert_equal(stop['attackerFrames'], int(actor['stop']), f'310 attacker HitPause at F{frame}')
        amount = 0 if blocked else normal_damage(profile, damage, moves, event, attack)
        assert_equal(amount, event['amount'], f'310 dynamic damage at F{frame}')
        assert_equal(amount, int(moves[target, logic - 1]['health']) - int(victim['health']),
                     f'310 atomic HP at F{frame}')
        oracle = source_damage(profile, phases, attack, frame, seat ^ mirrored, blocked)
        contacts.append({**event, 'native': oracle, 'effectiveTick': tick, 'instance': start})
    return contacts


def verify_routes(phases, changes, commands, moves, reentry, damage, events, limit):
    mirrored = phases['before-command'][0]['chars'][0]['facing'] < 0
    entrances = []
    for match in commands['commits']:
        entry, host = match['native'], match['host']
        if entry['to'] != STATE:
            continue
        seat, frame = entry['hostSeat'], entry['frame']
        logic = frame + 14
        if entry['var1'] == 1:
            source = entry['from']
            old = moves[seat, logic - 1]
            assert_equal(NATIVE_MOVES[source], old['move'], '310 cancels the audited source instance')
            tick = int(old['elapsed']) + 1
            assert_equal(entry['stateTime'], tick, f'310 old action time at F{frame}')
            assert_equal(True, tick in START_WINDOWS[source], f'310 permission window from {source}')
            assert_equal('0', host['hitpause'], f'310 commits on runnable tick at F{frame}')
            assert_equal('1', damage[seat, logic]['divisor'], '310 enters with fresh local damage state')
            assert_equal('', moves[seat, logic]['attacks'], '310 initial pose has no inherited attack boxes')
        if 'rootY' in entry:
            root = entry['rootY'] and entry['rootForward'] and entry['rootDown']
            assert_equal(True, root or entry['helperRisingY'] > 0, 'native 310 uses the root combination or Helper')
            if not root:
                button_frames = [int(candidate[2]) for value in host['candidates'].split(';')
                                 if (candidate := value.split(','))[0] == 'RisingUpper' and candidate[-1] == 'True']
                assert_equal(True, any(1 <= logic - edge <= 3 for edge in button_frames),
                             '310 consumes one unexpired Helper edge')
        entrances.append(match)

    exits = []
    for change in changes:
        if change['from'] != STATE or change['frame'] > limit:
            continue
        frame, seat, target = change['frame'], seat_index(change, mirrored), change['to']
        logic = frame + 14
        tick = change['stateTime']
        previous = moves[seat, logic - 1]
        if target in NATIVE_MOVES or target in RESERVATIONS or target in (0, 10, 20):
            assert_equal(tick, int(previous['elapsed']) + 1, f'310 exit clock at F{frame}')
            assert_equal([], [event for event in events if event['frame'] == logic and event['source'] == seat
                              and event['move'] == UPPER and event['kind'] in CONTACTS],
                         f'310 exit emits no stale contact at F{frame}')
        if target in (1000, 1050, 1200) and tick < LENGTH:
            assert_equal((2, 0, True), (change['var1'], change['var2'],
                                       change['moveContact'] == 1 or change.get('moveReversed') == 1),
                         f'310 first effective contact tick permits special at F{frame}')
            instance = reentry[seat, logic - 1]
            confirm = max(int(instance[field]) for field in ('hitConfirm', 'blockConfirm'))
            assert_equal(1, len([event for event in events if event['source'] == seat and event['frame'] == confirm
                                 and event['move'] == UPPER and event['kind'] in CONTACTS]),
                         '310 special consumes an actual contact from the previous instance')
            assert_equal(tick - 1, int(moves[seat, confirm]['elapsed']), 'contact age excludes frozen ticks')
        elif target in NATIVE_MOVES or target in RESERVATIONS or target in (0, 10, 20):
            assert_equal(LENGTH, tick, f'310 exits naturally to {target} at F{frame}')
            if target in (0, 10, 20):
                restored = phases['end'][frame]['chars'][seat ^ mirrored]
                expected = 'Crouching' if restored['stateType'] == 2 else 'Standing'
                actual = moves[seat, logic]
                assert_equal(('-', expected), (actual['move'], actual['state']), f'310 natural recovery at F{frame}')
                if restored['state'] == 0:
                    assert_equal(int(previous['x']), int(actual['x']), f'310 neutral recovery displacement at F{frame}')
        if target in (5000, 5010, 5070):
            assert_equal(('-', ''), (moves[seat, logic]['move'], moves[seat, logic]['attacks']),
                         f'cleared 310 after interruption at F{frame}')
        exits.append({'frame': frame, 'sourceTick': tick, 'target': target, 'seat': seat,
                      'explicit': change['var1'], 'natural': change['var2']})
    return entrances, exits


def air_recovery(scene, phases, moves, lines, limit):
    mirrored = phases['before-command'][0]['chars'][0]['facing'] < 0
    launch = indexed(audit_rows(lines, 'LaunchReactionAudit'))
    results = []
    for contact in scene.contacts:
        if contact['kind'] != 'Hit':
            continue
        first, seat = contact['frame'], contact['target']
        defender = seat ^ mirrored
        before = phases['before-command'][first]['chars']
        if before[defender]['stateType'] != 4 or before[defender ^ 1]['state'] != STATE:
            continue
        end = min(limit, next((row['frame'] for row in scene.contacts
                               if row['target'] == seat and row['frame'] > first), limit))
        initial_native = phases['end'][first]['chars'][defender]['pos']
        initial_host = moves[seat, first + 14]
        origin = [int(initial_host['x']) / Q16 - initial_native[0], int(initial_host['y']) / Q16 + initial_native[1]]
        native_phases, host_phases = {}, {}
        native_control = host_control = None
        comparing_flight = True
        for frame in range(first, end):
            native = phases['end'][frame]['chars'][defender]
            logic = frame + 14
            host = moves[seat, logic]
            actual = launch.get((seat, logic))
            if native['ctrl']:
                native_control = frame if native_control is None else native_control
            else:
                phase = 'Shake' if native['state'] == 5020 else launch_phase(native)
                native_phases.setdefault(phase, frame)
            if host['state'] in ('Standing', 'Crouching'):
                host_control = frame if host_control is None else host_control
            else:
                if actual is None:
                    raise ValueError(f'no 310 launch reaction at F{frame}')
                host_phases.setdefault(actual['phase'], frame)
            # The original jump and NewKOF's jump have different Y at contact. Verify
            # equal flight integration until the earlier trajectory actually crosses Y=0.
            if comparing_flight:
                expected_x = native['pos'][0] + origin[0]
                expected_y = max(0, -native['pos'][1] + origin[1])
                assert_equal(phase, actual['phase'], f'aerial target phase at F{frame}')
                if max(abs(want - int(host[axis]) / Q16)
                       for axis, want in zip(('x', 'y'), (expected_x, expected_y), strict=True)) > 0.02:
                    raise ValueError(f'aerial target flight differs relative to contact at F{frame}')
                if phase == 'Fall' and min(-native['pos'][1], int(host['y']) / Q16) <= 0:
                    comparing_flight = False
            if host_control is not None and native_control is not None:
                break
        for sources, label in ((native_phases, 'original'), (host_phases, 'NewKOF')):
            if 'Impact' in sources:
                expected_order = ('Shake', 'Lift', 'Rise', 'Fall', 'Impact', 'Bounce', 'LyingImpact', 'Lying', 'Wakeup')
                assert_equal(expected_order[:len(sources)], tuple(sources),
                             f'{label} aerial landing phases in authored order')
                landing = sources['Impact']
                bounced = sources.get('Bounce')
                if bounced is not None:
                    assert_equal(4, bounced - landing, f'{label} ground impact before bounce')
                if 'LyingImpact' in sources:
                    assert_equal(12, sources['LyingImpact'] - bounced, f'{label} bounce integration to ground')
                if 'Wakeup' in sources:
                    assert_equal(13, sources['Wakeup'] - sources['LyingImpact'], f'{label} grounded recovery')
        for phases_seen, control in ((native_phases, native_control), (host_phases, host_control)):
            if control is not None:
                assert_equal(25, control - phases_seen['Wakeup'], 'full 310 target wakeup window')
        if 'Impact' in host_phases:
            landing = host_phases['Impact']
            assert_equal(('0', '0'), tuple(moves[seat, landing + 14][axis] for axis in ('y', 'vy')),
                         'NewKOF grounds and stops on impact')
            assert_equal('0', moves[seat, landing + 13]['y'], 'NewKOF flight crossed the floor before impact')
        if 'Impact' in native_phases:
            landing = native_phases['Impact']
            assert_equal(0, phases['end'][landing]['chars'][defender]['pos'][1], 'original grounds on impact')
            assert_equal(True, phases['end'][landing - 1]['chars'][defender]['pos'][1] >= 0,
                         'original flight crossed the floor before impact')
        results.append({'contact': first, 'seat': seat, 'nativePhases': native_phases, 'hostPhases': host_phases,
                        'nativeRecovered': native_control, 'hostRecovered': host_control,
                        'lastCompared': frame, 'preContactJumpDelta': origin})
    return results


def verify_body(profile, changes, name, scene, phases, events, moves, reentry, lines):
    move = next(move for move in profile['moves'] if move['moveId'] == UPPER)
    damage = indexed(audit_rows(lines, 'DamageAudit'))
    assert_equal(set(moves), set(damage), 'complete damage audit')
    assert_equal(set(moves), set(reentry), 'complete move-instance audit')
    check_rng(profile, damage, lines)
    commands = verify_commands(profile, scene, phases, changes, events, moves, damage, lines,
                               reservations=RESERVATIONS)
    limit = min((row['frame'] for row in changes if row['to'] in RESERVATIONS),
                default=max(phases['end']) + 1)
    active_helpers = {frame: {**world, 'chars': [{**char, 'helpers': [h for h in char['helpers'] or []
                                                                   if h['moveContact'] == 0]}
                                               for char in world['chars']]} for frame, world in phases['end'].items()}
    entrances, exits = verify_routes(phases, changes, commands, moves, reentry, damage, events, limit)
    return {
        'upperWindows': body_samples(scene, phases, moves, reentry, limit),
        'upperContacts': attack_contacts(profile, phases, move['attacks'], moves, reentry, damage, events),
        'upperEntrances': entrances, 'upperExits': exits, 'commands': commands,
        'upperAirRecovery': air_recovery(scene, phases, moves, lines, limit),
        'groundContactOffsets': [{'frame': frame, 'seat': seat, 'x': offset}
                                 for (frame, seat), offset in prior_ground_offsets(scene, phases, moves).items()],
        'blowbackRecovery': check_blowback_recovery(name, scene, phases, moves, limit, lines),
        'knuckleToWave': check_knuckle_to_wave(scene, moves, reentry, events),
        'waveRelaunch': check_wave_relaunch(scene, phases, moves, lines),
        'waveEligibility': check_wave_eligibility({**phases, 'end': active_helpers}, lines),
    }


def prior_ground_offsets(scene, phases, moves):
    """Carry only measured fixed-point reaction displacement into consecutive contacts."""
    mirrored = phases['before-command'][0]['chars'][0]['facing'] < 0
    offsets = {}
    for before, after in zip(scene.contacts, scene.contacts[1:]):
        valid = ((before['kind'] == 'Blocked' and after['kind'].endswith('Blocked')) or
                 (before['kind'] == 'Hit' and after['kind'] == 'ProjectileHit' and not before['fall'] and not after['fall']))
        if not valid or before['target'] != after['target']:
            continue
        first, last, seat = before['frame'], after['frame'], before['target']
        origin = offsets.get((first, seat), 0.0)
        for frame in range(first, last + 1):
            x = phases['end'][frame]['chars'][seat ^ mirrored]['pos'][0]
            host = int(moves[seat, frame + 14]['x']) / Q16
            delta = host - x
            if abs(delta - origin) > 0.005:
                raise ValueError(f'ground contact {first}–{last} differs at F{frame} by {delta - origin}')
        offsets[last, seat] = delta
    return offsets


def contacts_for_case(phases_for_scene, name, scene, events, moves, damage, profile, **checks):
    guards = sum(contact['kind'].endswith('Blocked') for contact in scene.contacts)
    offsets = prior_ground_offsets(scene, phases_for_scene(name), moves) if guards > 1 else {}
    return verify_contacts(scene, events, moves, damage, profile,
                           guard_contact_origin=lambda native, host: offsets.get((native['frame'], native['target']), 0.0),
                           **checks)


def ground_recovery(deferred, name, scene, phases, moves):
    offsets = prior_ground_offsets(scene, phases, moves)
    origin = lambda phases, moves, frame, seat: offsets.get((frame, seat), 0.0)
    return check_ground_recovery(deferred, scene, phases, moves, states={STATE: UPPER},
                                 contact_origins=origin, wave_origins=origin)


@cache
def launch_frames():
    manifest = json.loads((ROOT / 'data/imported-characters/terry98/manifest.json').read_bytes())
    return {action['action']: action['frames'] for action in manifest['actions']
            if action['action'] in set(LAUNCH_ACTIONS.values())}


def check_poses(actions, phases, limit, scene, events, committed, timelines, modes):
    windows = check_ground_poses(phases, limit, scene, events, committed, timelines, modes,
                                 states={STATE: UPPER}, actions=actions, reactions=(150, 151, 5002, 5007, 5022, 5027))
    mirrored = phases['before-command'][0]['chars'][0]['facing'] < 0
    authored = launch_frames()
    for window in windows:
        if window['move'] != UPPER:
            continue
        state = window['end']
        frame = state - 15
        if frame >= limit:
            continue
        seat = window['seat']
        native = phases['end'][frame]['chars'][seat ^ mirrored]
        if native['state'] == 0 and native['ctrl'] and native['anim'] in (0, 1):
            assert_equal(('Standing', str(native['anim'])),
                         tuple(committed[seat, state][key] for key in ('state', 'action')),
                         f'310 returns to its original neutral action at F{frame}')
    for event in events:
        if event['kind'] != 'Hit' or event['move'] != UPPER:
            continue
        first, seat = event['frame'] - 14, event['target']
        defender = seat ^ mirrored
        if phases['before-command'][first]['chars'][defender]['stateType'] != 4:
            continue
        sample = []
        prior_clip = None
        rendered_tick = 0
        for frame in range(first, min(limit, max(phases['after-collision']) + 1)):
            native = phases['after-collision'][frame]['chars'][defender]
            rendered = committed[seat, frame + 15]
            native_action = native['anim']
            clip = rendered['clip']
            if not clip.startswith('launch_'):
                break
            action = int(rendered['action'])
            phase = clip.removeprefix('launch_')
            assert_equal(LAUNCH_ACTIONS[phase], action, f'aerial reaction binding at F{frame}')
            if clip != prior_clip or phase in ('Shake', 'Lift'):
                rendered_tick = 0
            elif rendered['frozen'] == 'False':
                rendered_tick += 1
            assert_equal(rendered_tick, int(rendered['tick']), f'310 launch starts each rendered phase at zero, F{frame}')
            remaining = rendered_tick
            sprites = authored[action]
            for index, sprite in enumerate(sprites):
                if sprite['time'] < 0 or remaining < sprite['time'] or index == len(sprites) - 1:
                    break
                remaining -= sprite['time']
            assert_equal((str(index), f"{sprite['group']},{sprite['image']}", str(sprite['sprite_index'])),
                         tuple(rendered[key] for key in ('frame', 'sprite', 'sprite_index')),
                         f'310 committed launch AIR sprite at F{frame}')
            if (native_action == action and native_action in (5030, 5035, 5050)
                    and native['state'] in (5020, 5030, 5035, 5050)):
                assert_equal(','.join(map(str, native['frame']['sprite'])), rendered['sprite'],
                             f'310 original vs committed launch sprite at F{frame}')
            prior_clip = clip
            sample.append({'frame': frame, 'action': action, 'tick': rendered_tick, 'nativeAction': native_action,
                           'nativeSprite': native['frame']['sprite'], 'committedSprite': rendered['sprite']})
        if not sample:
            raise ValueError(f'no committed 310 launch presentation after F{first}')
        windows.append({'aerialReaction': sample, 'seat': seat, 'action': sample[0]['action'],
                        'begin': first + 15, 'end': sample[-1]['frame'] + 16})
    return windows


def main():
    parser, options = validation_options(__doc__, PROFILE)
    identity = candidate_identity(options)
    (options.output / 'candidate.json').write_text(json.dumps(identity, indent=2) + '\n', encoding='utf-8')
    print(f'Stage 30 candidate files: {identity}', flush=True)
    actions = {**ACTIONS, **{move: state for state, move in NATIVE_MOVES.items() if state in (240, 245, 250, 300, 310)}}
    if options.mode == 'mode2':
        actions[NATIVE_MOVES[240]] = 241
    @cache
    def phases_for_scene(name):
        return fixture(locate(options.reference, options.mode, name, FAMILY), name, capture_profile='Stage30',
                       native_moves=NATIVE_MOVES, reservations=RESERVATIONS)[1]

    run_validation(parser, options, family=FAMILY, capture_profile='Stage30',
                   alias=options.visual_alias or f'terry98-stage30-{options.mode}', native_moves=NATIVE_MOVES,
                   reservations=RESERVATIONS, actions=actions, states={STATE: STATE}, body_check=verify_body,
                   contact_check=partial(contacts_for_case, phases_for_scene), recovery_check=ground_recovery,
                   pose_check=partial(check_poses, actions))
    assert_equal(identity, candidate_identity(options), 'candidate files remained stable during replay')


if __name__ == '__main__':
    main()
