"""Replay signed Terry Rising Tackle inputs and audit the complete 1200/1205 sequence."""

from dataclasses import replace
from functools import partial
from hashlib import file_digest
import json
from pathlib import Path
from subprocess import TimeoutExpired
import sys
import traceback

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'Stage30Validation'))
from rising_upper_duel import (
    ACTIONS, NATIVE_MOVES as STAGE30_MOVES, RESERVATIONS as STAGE30_RESERVATIONS,
    ROOT, Q16, assert_equal, audit_rows, check_rng, fixture, indexed, local_boxes, locate,
    native_boxes, normal_damage, verify_commands,
)
from standing_light_kick_duel import EXTRA_AUTHORITY, require_same, validation_options, visual_timeline
from power_wave_duel import (
    AUTHORITY, POSE_KEYS, VISUAL_FRAME, battle_events, fighter_poses, run_case, run_godot, runtime_capture,
    validate_captures, verify_grounded_wave_recovery, verify_move_instances,
)
from compare_damage import committed_damage, damage_roll, raw_damage


PROFILE = 'data/imported-characters/terry98-stage31-final'
FAMILY = 'standing-heavy-kick'
TACKLE = 'TerryRisingTackle1200'
PRIMARY = 1200
LAND = 1205
FLIGHT_TICK = 9
PRIMARY_TICKS = 41
LAND_TICKS = 14
REVERSAL_TICKS = range(2, 5)
NATIVE_MOVES = {**STAGE30_MOVES, PRIMARY: TACKLE}
RESERVATIONS = {state: command for state, command in STAGE30_RESERVATIONS.items() if state != PRIMARY}
CONTACTS = {'Hit', 'Blocked', 'ProjectileHit', 'ProjectileBlocked'}
AUTHORITY_PREFIXES = (*AUTHORITY, *EXTRA_AUTHORITY, b'[ReversalAudit] ', b'[MovePhaseAudit] ',
                      b'[ProjectileReversalAudit] ')


def candidate_identity(options):
    paths = [ROOT / f'.godot/mono/temp/bin/Debug/{name}.dll' for name in ('NewKOF', 'NewKOF.BattleCore')]
    paths.extend((ROOT / 'project.godot', ROOT / 'data/imported-characters/terry98/manifest.json'))
    profile = ROOT / options.profile_dir
    paths.extend(profile / f'{options.mode}.{kind}.json' for kind in ('fighter', 'binding', 'source'))
    result = {}
    for path in paths:
        with path.open('rb') as source:
            result[path.relative_to(ROOT).as_posix()] = file_digest(source, 'sha256').hexdigest()
    return result


def mirrored_start(phases):
    return phases['before-command'][0]['chars'][0]['facing'] < 0


def logical_seat(change, mirrored):
    return (change['seat'] - 1) ^ mirrored


def body_frames(scene, phases, moves, reentry, phase_audit, limit):
    mirrored = mirrored_start(phases)
    windows = []
    for entry in scene.moves:
        if entry['to'] != PRIMARY:
            continue
        first, seat = entry['frame'], entry['hostSeat']
        native_seat = seat ^ mirrored
        body = []
        native_origin = phases['before-command'][first]['chars'][native_seat]['pos'][0]
        host_origin = int(moves[seat, first + 13]['x']) / Q16
        for frame in range(first, limit):
            logic = frame + 14
            host = moves[seat, logic]
            native = phases['after-collision'][frame]['chars'][native_seat]
            after = phases['end'][frame]['chars'][native_seat]
            if host['move'] != TACKLE or reentry[seat, logic]['started'] != str(first + 14):
                next_instance = (after['state'] == PRIMARY and
                                 any(other['to'] == PRIMARY and other['hostSeat'] == seat and
                                     other['frame'] == frame for other in scene.moves) and
                                 reentry[seat, logic]['started'] == str(logic))
                if after['state'] in (PRIMARY, LAND) and not next_instance:
                    raise ValueError(f'Tackle instance ended ahead of native state {after["state"]} at F{frame}')
                break
            tick = int(host['elapsed'])
            stage = PRIMARY if tick < PRIMARY_TICKS else LAND
            local_tick = tick if stage == PRIMARY else tick - PRIMARY_TICKS
            assert_equal((stage, stage, local_tick + 1), (native['state'], native['anim'], native['time']),
                         f'Tackle state, AIR action and local clock at F{frame}')
            native_facing = native['facing']
            assert_equal('Right' if native_facing > 0 else 'Left', host['facing'], f'Tackle facing at F{frame}')
            airborne = FLIGHT_TICK <= tick < PRIMARY_TICKS
            assert_equal(4 if airborne else 1, native['stateType'], f'Tackle original posture at F{frame}')
            phase = phase_audit[seat, logic]
            assert_equal((str(airborne), str(stage == LAND)), (phase['airborne'], phase['landing']),
                         f'Tackle committed posture at F{frame}')
            assert_equal(native_boxes(native, 'clsn2'), local_boxes(host, 'hurt'), f'Tackle hurtboxes at F{frame}')
            boxes = native_boxes(native, 'clsn1')
            assert_equal([] if tick in REVERSAL_TICKS else boxes, local_boxes(host, 'attacks'),
                         f'Tackle ordinary hitboxes at F{frame}')
            position = (int(host['x']) / Q16 - host_origin, int(host['y']) / Q16)
            expected = (native['pos'][0] - native_origin, -native['pos'][1])
            velocity = tuple(int(host[axis]) / Q16 for axis in ('vx', 'vy'))
            expected_v = (native['vel'][0] * native_facing, -native['vel'][1])
            for actual, want in (*zip(position, expected, strict=True), *zip(velocity, expected_v, strict=True)):
                if abs(actual - want) > 0.005:
                    raise ValueError(f'Tackle motion at F{frame}: position {position} != {expected}, '
                                     f'velocity {velocity} != {expected_v}')
            assert_equal(after['hitPause'], int(host['stop']), f'Tackle attacker HitPause at F{frame}')
            body.append({'frame': frame, 'logic': logic, 'state': stage, 'stateTime': local_tick, 'elapsed': tick,
                         'pos': position, 'vel': velocity, 'airborne': native['stateType'] == 4,
                         'reversalBox': boxes if tick in REVERSAL_TICKS else []})
        if not body:
            raise ValueError(f'missing committed Tackle body at F{first}')
        windows.append({'entry': first, 'seat': seat, 'frames': body})
    return windows


def native_damage(profile, phases, attack, frame, owner, guarded):
    source = phases['after-collision'][frame]['chars'][owner]
    operands = source['vars']
    vari = operands['var21']
    draw = next(row for row in reversed(phases['damage-rows'])
                if row['phase'] == 'var21-draw' and row['seat'] == owner + 1 and row['frame'] <= frame)
    assert_equal(damage_roll(draw['rng'], profile['damageRules']['varianceMaximum']), vari, 'Tackle native variance')
    raw = raw_damage(attack['dynamicDamage'], vari, operands['var6'], max(1, operands['var23']), operands['fvar7'])
    assert_equal(raw, source['hitDef']['damage'], f'Tackle native raw HitDef at F{frame}')
    victim = owner ^ 1
    defense = phases['after-collision'][frame]['chars'][victim]['finalDefense']
    before = phases['before-command'][frame]['chars'][victim]['life']
    chip = int(operands['fvar7'])
    assert_equal(chip, source['hitDef']['guardDamage'], f'Tackle native raw guard damage at F{frame}')
    dealt = committed_damage(chip if guarded else raw, source['attackMul'], defense, before)
    assert_equal(dealt, before - phases['end'][frame + 1]['chars'][victim]['life'],
                 f'Tackle native reaction-phase HP change at F{frame + 1}')
    return {'amount': dealt, 'base': raw, 'guardBase': chip, 'variance': vari, 'counter': operands['var6']}


def verify_contacts(profile, phases, move, events, moves, reentry, damage):
    mirrored = mirrored_start(phases)
    samples = []
    seen = set()
    for event in events:
        if event['move'] != TACKLE or event['kind'] not in ('Hit', 'Blocked'):
            continue
        logic, seat, target = (event[k] for k in ('frame', 'source', 'target'))
        guarded = event['kind'] == 'Blocked'
        landed, victim = moves[seat, logic], moves[target, logic]
        acting = landed['move'] == TACKLE
        earlier = logic if acting else logic - 1
        started = int(reentry[seat, earlier]['started'])
        tick = int(moves[seat, earlier]['elapsed']) + (not acting)
        active_tick = tick - move['startupFrames']
        attack, = (a for a in move['attacks']
                   if a['firstActiveFrame'] <= active_tick <= a['lastActiveFrame'])
        key = seat, started, attack['contactGroup']
        assert_equal(False, key in seen, f'duplicate Tackle contact group at L{logic}')
        seen.add(key)
        stop = attack['hitstopByOutcome']['block' if guarded else 'hit']
        assert_equal(stop['defenderFrames'], int(victim['stop']), f'Tackle defender HitPause at L{logic}')
        if acting:
            assert_equal(stop['attackerFrames'], int(landed['stop']), f'Tackle attacker HitPause at L{logic}')
        oracle = native_damage(profile, phases, attack, logic - 14, seat ^ mirrored, guarded)
        expected = oracle['amount'] if guarded else normal_damage(profile, damage, moves, event, attack)
        assert_equal(expected, event['amount'], f'Tackle computed HP at L{logic}')
        assert_equal(expected, int(moves[target, logic - 1]['health']) - int(victim['health']),
                     f'Tackle atomic HP at L{logic}')
        samples.append({**event, 'instance': started, 'effectiveTick': tick, 'group': attack['contactGroup'],
                        'native': oracle})
    return samples


def verify_routes(phases, changes, commands, moves, reentry, reversals, limit):
    mirrored = mirrored_start(phases)
    routes = []
    for commit in commands['commits']:
        change, host = commit['native'], commit['host']
        if change['to'] not in (PRIMARY, 1000):
            continue
        frame, seat = change['frame'], change['hostSeat']
        logic = frame + 14
        if change['from'] in NATIVE_MOVES:
            assert_equal(change['stateTime'], int(moves[seat, logic - 1]['elapsed']) + 1,
                         f'old effective time for transition at F{frame}')
        if change['to'] == PRIMARY and change['from'] == 310 and change['var1'] == 2:
            assert_equal(True, change['moveContact'] == 1 or change.get('moveReversed') == 1,
                         f'310 special from actual contact or reversal at F{frame}')
            if change.get('moveReversed') == 1 and change['moveContact'] != 1:
                old_stop = reversals[seat, logic - 1]
                old_confirm = reentry[seat, logic - 1]
                assert_equal(('-1', '-1'), (old_confirm['hitConfirm'], old_confirm['blockConfirm']),
                             'reversed 310 has no ordinary contact eligibility')
                assert_equal(int(moves[seat, logic - 1]['elapsed']), int(old_stop['tick']),
                             '310 reversal stays on the exact confirming effective tick through freeze')
                assert_equal('-1', reversals[seat, logic]['reversed'], 'new Tackle instance clears the old reversal')
        if change['to'] == PRIMARY and 'rootRisingTackle' in change:
            assert_equal(True, change['rootRisingTackle'] or change['helperRisingTackle'] > 0,
                         f'Tackle root or Helper eligibility at F{frame}')
        routes.append(commit)
    for change in changes:
        if change['from'] not in (PRIMARY, LAND) or change['frame'] >= limit:
            continue
        frame, seat = change['frame'], logical_seat(change, mirrored)
        logic = frame + 14
        previous, current = moves[seat, logic - 1], moves[seat, logic]
        if change['from'] == PRIMARY and change['to'] == LAND:
            assert_equal((PRIMARY_TICKS, TACKLE, PRIMARY_TICKS, '0', '0', '0'),
                         (change['stateTime'], current['move'], int(current['elapsed']),
                          current['y'], current['vx'], current['vy']),
                         f'Tackle lands on the 1205 action boundary at F{frame}')
            assert_equal(reentry[seat, logic - 1]['started'], reentry[seat, logic]['started'],
                         f'Tackle landing preserves the single command instance at F{frame}')
        elif change['from'] == LAND and change['to'] == PRIMARY:
            assert_equal((LAND_TICKS, PRIMARY_TICKS + LAND_TICKS - 1, TACKLE, 0, str(logic)),
                         (change['stateTime'], int(previous['elapsed']), current['move'],
                          int(current['elapsed']), reentry[seat, logic]['started']),
                         f'Tackle recovery starts a new instance at F{frame}')
        elif change['from'] == LAND and change['to'] == 0:
            assert_equal((LAND_TICKS, PRIMARY_TICKS + LAND_TICKS - 1, '-', 'Standing'),
                         (change['stateTime'], int(previous['elapsed']), current['move'], current['state']),
                         f'Tackle 1205 complete recovery at F{frame}')
        elif change['to'] in (5000, 5010, 5020, 5070):
            assert_equal(('-', ''), (current['move'], current['attacks']), f'Tackle interruption at F{frame}')
        routes.append(change)
    return routes


def native_reversals(phases):
    if not any('moveReversed' in fighter for row in phases['end'].values() for fighter in row['chars']):
        return None
    mirrored = mirrored_start(phases)
    results = []
    for frame, row in phases['end'].items():
        for victim, char in enumerate(row['chars']):
            previous = phases['before-command'][frame]['chars'][victim]
            if char.get('moveReversed', 0) <= 0 or previous.get('moveReversed', 0) > 0:
                continue
            parry = row['chars'][victim ^ 1]
            assert_equal(PRIMARY, parry['state'], f'native reversal source at F{frame}')
            results.append({'frame': frame, 'source': victim ^ 1 ^ mirrored, 'target': victim ^ mirrored,
                            'victimState': char['state'], 'victimTick': char['time'] - 1,
                            'stop': [parry['hitPause'], char['hitPause']]})
    return results


def verify_reversals(name, phases, events, moves, reentry, audit):
    expected = native_reversals(phases)
    if expected is None:
        if 'reversal' in name:
            raise ValueError('reversal fixture requires the signed read-only MoveReversed trace')
        expected = []
    actual = [event for event in events if event['kind'] == 'Reversed']
    assert_equal([(row['frame'] + 14, row['source'], row['target']) for row in expected],
                 [(row['frame'], row['source'], row['target']) for row in actual], 'native reversal timing and ownership')
    for before, event in zip(expected, actual, strict=True):
        frame, seat, target = (event[k] for k in ('frame', 'source', 'target'))
        assert_equal((TACKLE, 0), (event['move'], event['amount']), f'Tackle reversal event at L{frame}')
        assert_equal(before['stop'], [int(moves[index, frame]['stop']) for index in (seat, target)],
                     f'Tackle reversal independent freeze at L{frame}')
        assert_equal(reentry[target, frame - 1]['started'], reentry[target, frame]['started'],
                     f'reversal leaves the victim in its own attack instance at L{frame}')
        for index in (seat, target):
            assert_equal(moves[index, frame - 1]['health'], moves[index, frame]['health'],
                         f'non-damaging reversal at L{frame}')
        mark = audit[target, frame]
        assert_equal((frame, before['victimTick']), (int(mark['reversed']), int(mark['tick'])),
                     f'authoritative reversal confirmation for victim at L{frame}')
        for logic in range(frame + 1, frame + 1 + before['stop'][1]):
            frozen = audit[target, logic]
            assert_equal(tuple(mark[key] for key in ('reversed', 'tick', 'group')),
                         tuple(frozen[key] for key in ('reversed', 'tick', 'group')),
                         f'reversal confirmation persists on frozen L{logic}')
    return {'native': expected, 'events': actual, 'state': list(audit.values())}


def verify_helper_reversals(phases, events, lines, moves):
    mirror = mirrored_start(phases)
    rows = [r for r in events if r['kind'] == 'ProjectileReversed']
    expected = []
    for native_frame, world in phases['end'].items():
        for owner, fighter in enumerate(world['chars']):
            for helper in fighter['helpers'] or []:
                if (helper['state'] == 1020 and helper.get('hitPause', 0) == 11
                    and (helper.get('hitDef') or {}).get('attr', 0) < 0):
                    expected.append((native_frame + 14, owner ^ 1 ^ mirror, owner ^ mirror, helper))
    assert_equal([row[:3] for row in expected], [(r['frame'], r['source'], r['target']) for r in rows],
                 'native Helper reversal events')
    audit = audit_rows(lines, 'ProjectileReversalAudit')
    snapshots = {(int(row['logic']), int(row['owner'])): row for row in audit}
    for (logic, source, owner, helper), event in zip(expected, rows, strict=True):
        assert_equal((TACKLE, 0, helper['hitPause']),
                     (event['move'], event['amount'], int(moves[source, logic]['stop'])),
                     'reversal pauses the attacking fighter without dealing damage')
        for offset in range(12):
            state = snapshots[logic + offset, owner]
            assert_equal((snapshots[logic, owner]['id'], str(11 - offset)), (state['id'], state['stop']),
                         'the reversed Helper keeps its own freeze clock')
        base_owner = phases['end'][logic - 14]['chars'][owner ^ mirror]
        assert_equal(base_owner['hitPause'], int(moves[owner, logic]['stop']), 'Helper owner freeze is independent')
    return {'native': [(logic - 14, source, target) for logic, source, target, _ in expected], 'events': rows}


def verify_body(profile, changes, name, scene, phases, events, moves, reentry, lines):
    move = next((move for move in profile['moves'] if move['moveId'] == TACKLE), None)
    damage = indexed(audit_rows(lines, 'DamageAudit'))
    phase_audit = indexed(audit_rows(lines, 'MovePhaseAudit'))
    reversal_audit = indexed(audit_rows(lines, 'ReversalAudit'))
    check_rng(profile, damage, lines)
    commands = verify_commands(profile, scene, phases, changes, events, moves, damage, lines, reservations=RESERVATIONS)
    if move is None:
        assert_equal([], [row for row in events if row['move'] in (TACKLE, 'TerryPowerWave1000')],
                     'mode 2 keeps default Tackle and Wave isolated')
        return {'commands': commands}
    limit = min((r['frame'] for r in changes if r['to'] in RESERVATIONS), default=max(phases['after-collision']) + 1)
    return {'body': body_frames(scene, phases, moves, reentry, phase_audit, limit), 'commands': commands,
            'contacts': verify_contacts(profile, phases, move, events, moves, reentry, damage),
            'stateTransitions': verify_routes(phases, changes, commands, moves, reentry, reversal_audit, limit),
            'reversals': verify_reversals(name, phases, events, moves, reentry, reversal_audit),
            'helperReversals': verify_helper_reversals(phases, events, lines, moves)}


def pose_windows(actions, scene, events, committed, timelines, modes, *, phases=None):
    windows = fighter_poses(scene, [evt for evt in events if evt['move'] != TACKLE], committed, timelines, modes,
                            actions=actions)
    starts = [evt for evt in events if evt['kind'] == 'MoveStarted' and evt['move'] == TACKLE]
    mirror = mirrored_start(phases) if phases else False
    for index, event in enumerate(starts):
        seat, begin, tick = event['source'], event['frame'] + 1, 0
        following = next((row['frame'] + 1 for row in starts[index + 1:] if row['source'] == seat), None)
        state = begin
        window = None
        while state != following and (pose := committed.get((seat, state))) and pose['move'] == TACKLE:
            action = PRIMARY if tick < PRIMARY_TICKS else LAND
            age = tick if action == PRIMARY else tick - PRIMARY_TICKS
            if age >= len(timelines[action]):
                raise ValueError(f'Tackle 1205 left active past its AIR length at S{state}')
            assert_equal(age, int(pose['tick']), f'Tackle visual clock Action {action} at S{state}')
            assert_equal(timelines[action][age], tuple(pose[key] for key in POSE_KEYS),
                         f'Tackle committed AIR pose Action {action} at S{state}')
            if phases and (native := phases['after-collision'].get(state - 15)):
                native_fighter = native['chars'][seat ^ mirror]
                assert_equal((action, pose['sprite']), (native_fighter['anim'], ','.join(map(str, native_fighter['frame']['sprite']))),
                             f'Tackle original/committed image at S{state}')
            if window is None or window['action'] != action:
                window = {'seat': seat, 'move': TACKLE, 'action': action, 'begin': state, 'end': state, 'clock': []}
                windows.append(window)
            window['clock'].append(age)
            window['end'] = state + 1
            state += 1
            next_pose = committed.get((seat, state))
            if next_pose and next_pose['frozen'] == 'False':
                tick += 1
        if not window:
            raise ValueError(f'missing Tackle pose at S{begin}')
    return windows


def run_realtime(options, name, scene, alias, actions, timelines):
    output = options.output / name / 'sprite'
    output.mkdir(parents=True)
    args, wanted = runtime_capture(options, replace(scene, guard_action=None), output, True, alias=alias)
    lines = run_godot(options.godot, args, output / 'duel.log', output / 'end.png').decode('utf-8').splitlines()
    events = battle_events(lines)
    starts = verify_move_instances(scene, events, {}, {}, fixed=False)
    compare = lambda row: (row['kind'], row['source'], row['target'])
    assert_equal([compare(row) for row in scene.contacts], [compare(row) for row in events if row['kind'] in CONTACTS],
                 'realtime Tackle contacts')
    windows, images = validate_captures(replace(scene, guard_action=None), events, lines, output, wanted, timelines,
                                       ('default', 'default'), False, check_reactions=False,
                                       move_validator=partial(pose_windows, actions))
    if not any(fighter['action'] in ('1200', '1205') for image in images for fighter in image['fighters']):
        raise ValueError('sparse capture did not present the live Tackle actions')
    logic = -1
    backlog, before, after = [], [], []
    target = before
    for line in lines:
        if match := VISUAL_FRAME.match(line):
            logic = int(match[1])
        elif line.startswith('WARNING: [BattleScene] 帧积压'):
            backlog.append(logic)
        elif line.startswith('[FrameCapture] '):
            target = after
        elif line.startswith('Project FPS:'):
            target.append(int(line.split()[2]))
    if not before or len(after) < 2 or min(before[-1], after[-1]) < int(options.fps) - 5:
        raise ValueError(f'insufficient measured samples for {options.fps} FPS: {before}, {after}')
    if any(starts[0]['frame'] <= tick < images[-1]['state'] for tick in backlog):
        raise ValueError(f'backlog within the sampled Tackle instance: {backlog}')
    stats = [before[-1], after[0], after[-1]]
    report = {'events': events, 'windows': windows, 'captures': images, 'fpsRequested': int(options.fps),
              'fpsAroundCapture': stats, 'rendererFps': {'before': before, 'after': after}, 'backlogAfterLogic': backlog}
    (output / 'trace.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(f'{name}: realtime {options.fps} FPS, samples {stats}, capture {images[0]["state"]}', flush=True)


def main():
    parser, options = validation_options(__doc__, PROFILE)
    identity = candidate_identity(options)
    (options.output / 'candidate.json').write_text(json.dumps(identity, indent=2) + '\n', encoding='utf-8')
    print(f'Stage 31 candidate files: {identity}', flush=True)
    alias = options.visual_alias or f'terry98-stage31-{options.mode}'
    root = ROOT / options.profile_dir
    profile = json.loads((root / f'{options.mode}.fighter.json').read_bytes())
    manifest = json.loads((ROOT / 'data/imported-characters/terry98/manifest.json').read_bytes())
    actions = {**ACTIONS, **{move: state for state, move in NATIVE_MOVES.items() if state in (240, 245, 250, 300, 310, 1200)}}
    if options.mode == 'mode2':
        actions[NATIVE_MOVES[240]] = 241
    timelines = {key: visual_timeline(manifest, key) for key in {*actions.values(), 210, 211, 1000, 1020, LAND}}
    failures = []
    for name in options.scene:
        try:
            native = locate(options.reference, options.mode, name, FAMILY)
            scene, phases, changes = fixture(native, name, capture_profile='Stage31', native_moves=NATIVE_MOVES,
                                             reservations=RESERVATIONS)
            if options.fps != 'fixed':
                run_realtime(options, name, scene, alias, actions, timelines)
                continue
            entries = [row['frame'] for row in scene.moves if row['to'] == PRIMARY]
            first = min(entries, default=5)
            capture = options.fixed_window or (first + 23, first + 102)
            run_case(options, name, native, timelines, profile, mode=options.mode, alias=alias,
                     prepared_scene=(replace(scene, captures=tuple(capture)), phases, changes),
                     fixed_checks=partial(verify_body, profile, changes, name),
                     pose_check=partial(pose_windows, actions, phases=phases), partial_capture=True,
                     recovery_check=verify_grounded_wave_recovery, allow_interrupted_spawn=True,
                     minimum_render_frames=max(phases['after-collision']) + 24,
                     extra_arguments=('--command-audit', '--reversal-audit'),
                     extra_authority=AUTHORITY_PREFIXES[len(AUTHORITY):])
            trace = json.loads((options.output / name / 'sprite/trace.json').read_bytes())
            present = {row['action'] for image in trace['captures'] for row in image['fighters']}
            if entries and '1200' not in present:
                raise ValueError('no Vulkan submission presents Action 1200')
            if options.repeat_from:
                rows = [[line for line in (folder / name / 'sprite/duel.log').read_bytes().splitlines()
                         if line.startswith(AUTHORITY_PREFIXES)] for folder in (options.repeat_from, options.output)]
                if not rows[0]:
                    raise ValueError('no prior same-seed authority rows')
                require_same(*rows, f'{name} same-seed replay')
                print(f'{name}: {len(rows[0])} same-seed authority rows', flush=True)
        except (ValueError, KeyError, IndexError, OSError, StopIteration, TimeoutExpired) as error:
            failures.append(name)
            print(f'{name}: FAIL {error!r}', flush=True)
            traceback.print_exc()
    assert_equal(identity, candidate_identity(options), 'candidate files remained stable during replay')
    if failures:
        parser.exit(1, 'Failed: ' + ', '.join(failures) + '\n')


if __name__ == '__main__':
    main()
