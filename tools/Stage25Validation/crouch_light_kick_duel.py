"""Compare physical-input State 430 duels against native traces in Godot Vulkan."""

import argparse
from dataclasses import replace
from functools import partial
from hashlib import sha256
import json
from pathlib import Path
from subprocess import TimeoutExpired
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'Stage24Validation'))
from crouch_kick_duel import (ACTIONS, AUTHORITY, COMMAND_AUTHORITY, CONTACTS, MOVE, POSE_KEYS, Q16,
                            ROOT, SOURCE_KEYS, SOURCE_MOVES, WAVE_RESTRICTION_AUTHORITY, assert_equal,
                            audit_rows, check_rng, check_wave_eligibility, check_wave_launch, fighter_poses,
                            indexed, local_boxes, locate, native_boxes, native_case, native_damage,
                            normal_damage, run_case, verify_grounded_wave_recovery, verify_wave_ground_poses,
                            visual_timeline)
from power_wave_duel import (VISUAL_FRAME, battle_events, require_same, run_godot, runtime_capture,
                             validate_captures, verify_move_instances)


KICK = 'TerryCrouchLightKick430'
QCF = ['Down', 'DownForward', 'Forward']
NATIVE = {**SOURCE_MOVES, 430: KICK}
VISUALS = {**ACTIONS, KICK: 430}
FAMILY = 'crouch-light-kick'
PROFILE = 'data/imported-characters/terry98-stage25-final'
EXTRA_AUTHORITY = (COMMAND_AUTHORITY, WAVE_RESTRICTION_AUTHORITY)
RESERVATIONS = {230: 'ReservedStandLightKick', 235: 'ReservedStandLightKick', 245: 'ReservedStandHeavyKick',
                300: 'ReservedForwardLightPunch', 310: 'ReservedRisingUpper', 1050: 'ReservedMode2Wave'}
REACTIONS = {5000, 5001, 5010, 5011, 5070}


def fixture(reference, name, *, capture_profile='Stage25', native_moves=NATIVE, reservations=RESERVATIONS):
    run = json.loads((reference.parent / 'run-metadata.json').read_bytes())
    meta = json.loads(reference.with_name(f'{reference.stem}-metadata.json').read_bytes())
    assert_equal(capture_profile, run['capture_profile'], 'native capture profile')
    assert_equal(True, reference.stem in run['executed_cases'], 'native case appears in run metadata')
    assert_equal((run['rng_seed'], run['executable_sha256'], run['config_sha256']),
                 (meta['rng_seed'], meta['executable_sha256'], meta['config_sha256']), 'native run identity')
    assert_equal(meta['trace_sha256'], sha256(reference.read_bytes()).hexdigest(), 'native trace SHA-256')

    def is_wave(target, owner):
        return any(helper['anim'] == 1020 and helper['id'] == target['hitPlayerId']
                   for helper in owner['helpers'] or [])

    scene, phases, changes = native_case(reference, name, last_frame=run.get('last_frame', 75), native_moves=native_moves,
                                         raw_keys=SOURCE_KEYS, contact_is_wave=is_wave)
    mirror = name.startswith('mirror-')
    assert_equal(mirror, phases['before-command'][0]['chars'][0]['facing'] < 0, 'native mirrored start')
    known_bits = sum(bit for bit, _, _ in SOURCE_KEYS)
    assert_equal(meta['initial_distance'], scene.half_distance * 2, 'native initial center distance')
    for frame, world in phases['before-command'].items():
        if any(char['raw'] & ~known_bits for char in world['chars']):
            raise ValueError(f'unsupported raw input at F{frame} in {reference.name}')
    phases['damage-rows'] = [row for row in map(json.loads, reference.read_text(encoding='utf-8').splitlines())
                             if row['phase'] in ('hitdef', 'var21-draw')]
    deferred = [change for change in changes if change['to'] in reservations]
    if deferred:
        first = min(row['frame'] for row in deferred)
        assert_equal([], [row for row in scene.moves if row['frame'] > first], 'no later in-scope move after deferred entry')
        scene = replace(scene, contacts=tuple(row for row in scene.contacts if row['frame'] < first),
                        differences=tuple(f'native-state-{state}-deferred' for state in sorted({r['to'] for r in deferred})))
        if any(row['to'] == 1050 for row in deferred):
            # Stop native Pause and entity comparisons at the reserved mode 2 entry.
            phases['end'] = {frame: world for frame, world in phases['end'].items() if frame <= first}
    return scene, phases, changes


def verify_commands(profile, scene, phases, changes, events, moves, damage, lines, *, reservations=RESERVATIONS,
                    sweep_kick_targets=(430,)):
    command_by_move = {move['moveId']: move['commandId'] for move in profile['moves']}
    table = indexed(audit_rows(lines, 'CommandAudit'))
    started = {(row['source'], row['frame']): row['move'] for row in events if row['kind'] == 'MoveStarted'}
    assert_equal(started, {key: row['committed'] for key, row in table.items() if row['committed'] != '-'},
                 'command commits match MoveStarted')
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    commits = []
    for entry in scene.moves:
        seat, logic = entry['hostSeat'], entry['frame'] + 14
        row = table[seat, logic]
        move = started[seat, logic]
        candidates = [candidate.split(',') for candidate in row['candidates'].split(';')]
        command = command_by_move[move]
        if not any(candidate[0] == command and candidate[-1] == 'True' for candidate in candidates):
            raise ValueError(f'{move} started without consuming {command} at L{logic}')
        if entry['to'] == 430:
            assert_equal(True, bool(entry['raw'] & 2), f'430 reads native down direction at L{logic}')
            assert_equal(True, entry['aBuffer'] or entry['helperA'] > 0, f'430 consumes native a at L{logic}')
            if not entry['aBuffer'] and entry['helperA'] > 0:
                age = 3 - entry['helperA']
                if not any(candidate[0] == command and candidate[-1] == 'True' and
                           1 <= logic - int(candidate[2]) <= 2 for candidate in candidates):
                    raise ValueError(f'430 did not consume a buffered physical a at L{logic} (native helper age {age})')
            if entry['from'] == 440:
                assert_equal((40, 1), (entry['stateTime'], entry['var2']), 'native sweep recovery into 430')
                assert_equal(40, int(moves[seat, logic - 1]['elapsed']) + 1, 'old sweep action time')
        if entry['from'] == 430:
            assert_equal(entry['stateTime'], int(moves[seat, logic - 1]['elapsed']) + 1, 'old 430 action time')
            assert_equal(('0', '1'), (row['hitpause'], damage[seat, logic]['divisor']),
                         'follow-up commits on runnable tick with fresh damage state')
            assert_equal('', moves[seat, logic]['attacks'], 'follow-up starts with no inherited attack box')
            if entry['to'] == 1000:
                assert_equal((14, 0, 1), (entry['stateTime'], entry['var1'], entry['var2']),
                             '430 → Wave only through natural AnimTime boundary')
        commits.append({'native': entry, 'host': row})
    reserved = []
    for entry in changes:
        if entry['to'] not in reservations:
            continue
        seat, logic = (entry['seat'] - 1) ^ mirror, entry['frame'] + 14
        assert_equal(None, started.get((seat, logic)), f'deferred State {entry["to"]} not replaced with a different move')
        row = table[seat, logic]
        command = reservations[entry['to']]
        if not any(candidate[0] == command and candidate[-2:] == ['True', 'True']
                   for candidate in (value.split(',') for value in row['candidates'].split(';'))):
            raise ValueError(f'deferred State {entry["to"]} did not reserve {command} at L{logic}')
        reserved.append({'native': entry, 'host': row})
    motion = []
    wave_commands = {command['commandId'] for command in profile['commands']
                     if command['directions'] == QCF}
    for frame, world in phases['after-command'].items():
        for native_seat, source in enumerate(world['chars']):
            if not (source['powerWaveCommand'] or source['helperWave'] > 0):
                continue
            seat, logic = native_seat ^ mirror, frame + 14
            if source['state'] == 430 and source['time'] < 14:
                assert_equal(0, source['vars']['var1'], f'no native State 430 Wave cancel at F{frame}')
                assert_equal(False, started.get((seat, logic)) == MOVE, f'no host State 430 Wave cancel at F{frame}')
                if source['powerWaveCommand'] and (source['time'] < 3 or source['raw'] & 2):
                    row = table[seat, logic]
                    assert_equal(KICK, row['old'], 'motion attempted during State 430')
                    startup = source['time'] < 3
                    if startup:
                        assert_equal(('Startup', '-'), (row['before'], row['committed']), 'startup holds State 430')
                    if not any(parts[0] in wave_commands and parts[2] == str(logic) and (not startup or parts[-1] == 'False')
                               for parts in (candidate.split(',') for candidate in row['candidates'].split(';'))):
                        raise ValueError(f'no recognized Wave motion for the physical x during State 430 at F{frame}')
            motion.append({'frame': frame, 'seat': seat, 'state': source['state'], 'tick': source['time'],
                           'rootWave': source['powerWaveCommand'], 'helperWave': source['helperWave'],
                           'host': table.get((seat, logic))})
    buffered_kick_rival = []
    for entry in scene.moves:
        if entry['from'] != 440 or entry['to'] not in sweep_kick_targets or entry['aBuffer'] or not entry['bBuffer']:
            continue
        seat, logic = entry['hostSeat'], entry['frame'] + 14
        rival = command_by_move[NATIVE[440]]
        pending = []
        for frame in range(logic, max(key[1] for key in moves) + 1):
            row = table.get((seat, frame))
            if row is None:
                break
            candidates = [candidate.split(',') for candidate in row['candidates'].split(';')]
            newer = [candidate for candidate in candidates if candidate[:3] == [rival, str(logic), str(logic)]]
            if not newer:
                break
            assert_equal(['False'], [candidate[-1] for candidate in newer], 'new b remains pending after older a')
            if frame != logic:
                assert_equal('-', row['committed'], 'new b cannot ghost during kick startup')
            pending.append(row)
        assert_equal(True, len(pending) > 0, 'trace includes preserved b before expiry')
        assert_equal('-', table[seat, logic + len(pending)]['committed'], 'no sweep on b expiry')
        buffered_kick_rival.extend(pending)
    return {'commits': commits, 'deferred': reserved, 'waveMotion': motion, 'buffer': list(table.values()),
            'bufferedKickRival': buffered_kick_rival}


def verify_body(profile, changes, name, scene, phases, events, moves, reentry, lines):
    definition = next(move for move in profile['moves'] if move['moveId'] == KICK)
    attack, = definition['attacks']
    damage = indexed(audit_rows(lines, 'DamageAudit'))
    assert_equal(set(moves), set(damage), 'complete damage audit')
    assert_equal(set(moves), set(reentry), 'complete attack instance audit')
    check_rng(profile, damage, lines)
    mirror = name.startswith('mirror-')
    starts = [row for row in events if row['kind'] == 'MoveStarted' and row['move'] == KICK]
    last = min(max(phases['after-collision']) + 14, max(logic for _, logic in moves))
    checked = 0
    for entry in starts:
        logic, seat = entry['frame'], entry['source']
        for frame in range(logic, last + 1):
            row = moves[seat, frame]
            if row['move'] != KICK or reentry[seat, frame]['started'] != str(logic):
                break
            source = phases['after-collision'][frame - 14]['chars'][seat ^ mirror]
            elapsed = int(row['elapsed'])
            assert_equal((430, 2, elapsed + 1), (source['state'], source['stateType'], source['time']),
                         f'native 430 clock at L{frame}')
            assert_equal(3 <= elapsed <= 5, bool(row['attacks']), f'430 active window at L{frame}')
            assert_equal(native_boxes(source, 'clsn1'), local_boxes(row, 'attacks'), f'430 Clsn1 at L{frame}')
            assert_equal(native_boxes(source, 'clsn2'), local_boxes(row, 'hurt'), f'430 Clsn2 at L{frame}')
            checked += 1
    contacts = []
    for event in events:
        if event['move'] != KICK or event['kind'] not in ('Hit', 'Blocked'):
            continue
        frame, seat, target = (event[key] for key in ('frame', 'source', 'target'))
        blocked = event['kind'] == 'Blocked'
        stop = attack['hitstopByOutcome']['block' if blocked else 'hit']
        assert_equal((stop['attackerFrames'], stop['defenderFrames']),
                     (int(moves[seat, frame]['stop']), int(moves[target, frame]['stop'])), '430 independent HitPause')
        amount = 0 if blocked else normal_damage(profile, damage, moves, event, attack)
        assert_equal(amount, event['amount'], '430 dynamic damage')
        assert_equal(amount, int(moves[target, frame - 1]['health']) - int(moves[target, frame]['health']), '430 atomic HP')
        assert_equal(str(frame), reentry[seat, frame]['blockConfirm' if blocked else 'hitConfirm'], '430 contact ownership')
        original = native_damage(profile, phases, attack, frame - 14, seat ^ mirror, blocked, state=430)
        contacts.append({**event, 'native': original, 'tick': int(moves[seat, frame]['elapsed'])})
    exits = []
    for change in changes:
        if change['from'] != 430:
            continue
        frame, seat = change['frame'] + 14, (change['seat'] - 1) ^ mirror
        target = change['to']
        if frame > last:
            raise ValueError(f'430 exit not covered at L{frame}')
        if target == 11:
            assert_equal('Crouching', moves[seat, frame]['state'], '430 natural crouch exit')
            assert_equal(14, change['stateTime'], '430 natural boundary')
            for logic in range(frame, last + 1):
                original = phases['after-collision'][logic - 14]['chars'][seat ^ mirror]
                if original['state'] not in (11, 12):
                    break
                row = moves[seat, logic]
                assert_equal('Crouching' if original['state'] == 11 else 'Standing', row['state'],
                             f'430 crouch exit at L{logic}')
                assert_equal(native_boxes(original, 'clsn2'), local_boxes(row, 'hurt'), f'430 exit Clsn2 at L{logic}')
        elif target in REACTIONS:
            assert_equal(('-', ''), (moves[seat, frame]['move'], moves[seat, frame]['attacks']),
                         '430 interruption removes the old instance and attack boxes')
        if target != 430:
            assert_equal([], [row for row in events if row['frame'] == frame and row['source'] == seat
                              and row['kind'] in CONTACTS and row['move'] == KICK], 'no stale 430 contact')
        exits.append({'logic': frame, 'seat': seat, 'sourceTick': change['stateTime'], 'nativeDestination': target})
    # Eligibility matters while the entity can still contact. The shared entity audit
    # already compares the committed contact of each Helper and its remaining lifetime.
    eligible = {frame: {**world, 'chars': [{**char, 'helpers': [helper for helper in char['helpers'] or []
                                                              if helper['moveContact'] == 0]}
                                            for char in world['chars']]}
                for frame, world in phases['end'].items()}
    result = {'kickStarts': starts, 'kickClsnSamples': checked, 'kickContacts': contacts, 'kickExits': exits,
              'commands': verify_commands(profile, scene, phases, changes, events, moves, damage, lines),
              'waveLaunch': check_wave_launch(name, scene, phases, moves, lines),
              'waveEligibility': check_wave_eligibility({**phases, 'end': eligible}, lines)}
    if name == 'wave-20':
        result['recoveryWalk'] = check_recovery_walk(phases, moves)
    return result


def check_recovery_walk(phases, moves):
    recovery, displacement = 19, 207616
    native_before = phases['end'][recovery - 1]['chars'][0]
    native_after = phases['end'][recovery]['chars'][0]
    host_before, host_after = (moves[0, frame + 14] for frame in (recovery - 1, recovery))
    assert_equal((430, 20), (native_before['state'], native_after['state']), 'native walks on natural 430 exit')
    assert_equal(('Recovery', 'Standing'), (host_before['state'], host_after['state']), 'host walks on natural 430 exit')
    assert_equal(displacement, int(host_after['x']) - int(host_before['x']), 'host forward step on natural 430 exit')
    assert_equal(displacement, round((native_after['pos'][0] - native_before['pos'][0]) * Q16),
                 'source forward step on natural 430 exit')
    assert_equal(round(native_after['pos'][0] * Q16), int(host_after['x']), 'exact position after recovery walk')
    return {'recovery': recovery, 'step': displacement / Q16, 'x': int(host_after['x']) / Q16}


def check_recovery(name, scene, phases, moves):
    result = verify_grounded_wave_recovery(scene, phases, moves, crouch_recovery=True)
    mirror = name.startswith('mirror-')
    for contact in scene.contacts:
        frame, seat, source_seat = (contact[key] for key in ('frame', 'target', 'source'))
        attacker = phases['after-collision'][frame]['chars'][source_seat ^ mirror]
        if attacker['state'] != 430 or contact['kind'] not in ('Hit', 'Blocked'):
            continue
        end = min((other['frame'] for other in scene.contacts if other['frame'] > frame and other['target'] == seat),
                  default=min(max(phases['end']) + 1, max(logic for _, logic in moves) - 13))
        expected = 'Blockstun' if contact['kind'] == 'Blocked' else 'Hitstun'
        for tick in range(frame, end):
            original = phases['end'][tick]['chars'][seat ^ mirror]
            row = moves[seat, tick + 14]
            state = ('Crouching' if original['stateType'] == 2 else 'Standing') if original['ctrl'] else expected
            assert_equal(state, row['state'], f'430 defender at F{tick}')
            error = abs(original['pos'][0] - int(row['x']) / Q16)
            next_world = phases['end'].get(tick + 1)
            next_native = next_world['chars'][seat ^ mirror] if next_world else None
            if original['ctrl'] and original['state'] == 0 and next_native and next_native['state'] == 20:
                # The host applies a held walk as control returns. The source changes
                # to neutral State 0 here and begins its walk on the next tick.
                previous = moves[seat, tick + 13]
                walk_step = int(row['x']) - int(previous['x'])
                native_step = next_native['pos'][0] - original['pos'][0]
                assert_equal(round(native_step * Q16), walk_step, 'same held walk step one tick earlier on host')
                if abs(original['pos'][0] - int(previous['x']) / Q16) > 0.005:
                    raise ValueError(f'430 defender X differs before held walk at F{tick}')
                result.append({'contact': frame, 'recovered': tick, 'seat': seat, 'nativeState': original['state'],
                               'positionErrorX': error, 'hostRecoveryWalkStep': walk_step / Q16})
                break
            if error > 0.005:
                raise ValueError(f'430 defender X differs at F{tick} by {error}')
            if tick > frame and not original['ctrl'] and original['state'] in REACTIONS:
                assert_equal(native_boxes(original, 'clsn2'), local_boxes(row, 'hurt'), f'430 reaction Clsn2 at F{tick}')
            if original['ctrl']:
                result.append({'contact': frame, 'recovered': tick, 'seat': seat, 'nativeState': original['state'],
                               'positionErrorX': error})
                break
    return result


def check_poses(phases, scene, events, committed, timelines, modes):
    windows = fighter_poses(scene, events, committed, timelines, modes, actions=VISUALS)
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    for event in events:
        if event['move'] != KICK or event['kind'] not in ('Hit', 'Blocked'):
            continue
        frame, seat = event['frame'] - 14, event['target']
        if event['kind'] == 'Blocked':
            assert_equal('151', committed[seat, frame + 15]['action'], f'430 low guard image at F{frame}')
            continue
        following = next((row['frame'] for row in scene.contacts if row['frame'] > frame and row['target'] == seat),
                         max(phases['after-collision']) + 1)
        for tick in range(frame + 1, following):
            original = phases['after-collision'][tick]['chars'][seat ^ mirror]
            row = committed.get((seat, tick + 15))
            if row is None or original['state'] not in REACTIONS:
                break
            assert_equal((str(original['anim']), ','.join(map(str, original['frame']['sprite']))),
                         (row['action'], row['sprite']), f'430 defender image at F{tick}')
    for seat in (0, 1):
        after_kick = False
        for frame, world in phases['after-collision'].items():
            original = world['chars'][seat ^ mirror]
            row = committed.get((seat, frame + 15))
            if row is None:
                continue
            if original['state'] == 430:
                after_kick = True
            elif original['state'] == 12 and after_kick:
                tick = original['time'] - 1
                assert_equal((str(tick), timelines[12][tick]), (row['tick'], tuple(row[key] for key in POSE_KEYS)),
                             f'430 to Action 12 at F{frame}')
            elif original['state'] != 11:
                after_kick = False
    verify_wave_ground_poses(scene, phases, committed)
    return windows


def sparse_realtime(options, name, scene, alias, timelines, *, actions=VISUALS, sample_actions=(430,)):
    output = options.output / name / 'sprite'
    output.mkdir(parents=True)
    args, capture = runtime_capture(options, scene, output, True, alias=alias)
    lines = run_godot(options.godot, args, output / 'duel.log', output / 'end.png').decode('utf-8').splitlines()
    events = battle_events(lines)
    starts = verify_move_instances(scene, events, {}, {}, fixed=False)
    assert_equal([(row['kind'], row['source'], row['target']) for row in scene.contacts],
                 [(row['kind'], row['source'], row['target']) for row in events if row['kind'] in CONTACTS],
                 'realtime contact outcomes')
    windows, images = validate_captures(scene, events, lines, output, capture, timelines, ('default', 'default'), False,
                                       move_validator=partial(fighter_poses, actions=actions), check_reactions=False)
    if not any(int(row['action']) in sample_actions for image in images for row in image['fighters']
               if row['action'].isdigit()):
        raise ValueError(f'sparse capture did not present the requested actions {sample_actions}')
    backlog, rates = [], []
    logic = -1
    captured = False
    for line in lines:
        if match := VISUAL_FRAME.match(line):
            logic = int(match[1])
        elif line.startswith('WARNING: [BattleScene] 帧积压'):
            backlog.append(logic)
        elif line.startswith('[FrameCapture] '):
            captured = True
        elif line.startswith('Project FPS:'):
            rates.append((captured, int(line.split()[2])))
    before = [rate for captured, rate in rates if not captured]
    after = [rate for captured, rate in rates if captured]
    if not before or len(after) < 2 or min(before[-1], after[-1]) < int(options.fps) - 5:
        raise ValueError(f'capture lacked settled {options.fps} FPS evidence: {rates}')
    if any(starts[0]['frame'] <= tick < images[-1]['state'] for tick in backlog):
        raise ValueError(f'backlog before sampled version: {backlog}')
    result = {'events': events, 'windows': windows, 'captures': images, 'backlogAfterLogic': backlog,
              'fpsRequested': int(options.fps), 'fpsAroundCapture': [before[-1], after[0], after[-1]],
              'rendererFps': rates}
    (output / 'trace.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f'{name}: realtime {options.fps} FPS, samples {result["fpsAroundCapture"]}, capture {images[0]["state"]}', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--godot', required=True, type=Path)
    parser.add_argument('--reference', required=True, type=Path, action='append')
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--profile-dir', default=PROFILE)
    parser.add_argument('--mode', choices=('default', 'mode2'), default='default')
    parser.add_argument('--scene', required=True, action='append')
    parser.add_argument('--compare-plain', action='store_true')
    parser.add_argument('--repeat-from', type=Path)
    parser.add_argument('--fps', choices=('fixed', '30', '60'), default='fixed')
    parser.add_argument('--fixed-resolution', choices=('640x360', '320x180'), default='640x360')
    parser.add_argument('--fixed-window', type=int, nargs=2)
    parser.add_argument('--realtime-render', type=int)
    options = parser.parse_args()
    fixed = options.fps == 'fixed'
    if not fixed and (not options.realtime_render or options.mode != 'default' or options.compare_plain
                      or options.repeat_from or options.fixed_window):
        parser.error('realtime requires one sparse render with the default mode')
    if fixed and options.fixed_window and not 0 < options.fixed_window[0] <= options.fixed_window[1] < options.fixed_window[0] + 90:
        parser.error('fixed window must include 1–90 renders')
    options.output = options.output.resolve()
    options.output.mkdir(parents=True, exist_ok=False)
    options.recovery = False
    options.disable_vsync = True
    options.require_no_backlog = False
    options.require_clean_sample = True
    options.realtime_resolution = '320x180'
    options.realtime_warmup_frames = 150
    options.realtime_settle_frames = 75
    options.realtime_tail_frames = 150
    options.realtime_input_spacing = 1
    options.realtime_window = (options.realtime_render, options.realtime_render)
    manifest = json.loads((ROOT / 'data/imported-characters/terry98/manifest.json').read_bytes())
    timelines = {action: visual_timeline(manifest, action) for action in (12, 200, 205, 210, 211, 215, 400, 410, 430, 440, 1000, 1020)}
    profile = json.loads((ROOT / options.profile_dir / f'{options.mode}.fighter.json').read_bytes())
    alias = f'terry98-stage25-{options.mode}'
    failures = []
    for name in options.scene:
        try:
            reference = locate(options.reference, options.mode, name, FAMILY)
            scene, phases, changes = fixture(reference, name)
            if not fixed:
                sparse_realtime(options, name, scene, alias, timelines)
                continue
            first = min((row['frame'] for row in scene.moves), default=5)
            next_start = max((row['frame'] for row in scene.moves), default=first)
            waves = [row['frame'] for row in scene.moves if row['move'] == MOVE]
            captures = tuple(options.fixed_window or ((waves[0] + 38, waves[-1] + 50) if waves else
                                                      (max(1, first + 23), min(first + 50, next_start + 36))))
            scene = replace(scene, captures=captures)
            # Compare through the end of the recorded native trace, including any Wave disposal.
            replay_length = max(phases['after-collision']) + 24
            run_case(options, name, reference, timelines, profile, mode=options.mode, alias=alias,
                     prepared_scene=(scene, phases, changes), fixed_checks=partial(verify_body, profile, changes, name),
                     recovery_check=partial(check_recovery, name), pose_check=partial(check_poses, phases),
                     partial_capture=True, allow_interrupted_spawn=True, minimum_render_frames=replay_length,
                     extra_arguments=('--command-audit',), extra_authority=EXTRA_AUTHORITY)
            if options.repeat_from:
                prefixes = (*AUTHORITY, *EXTRA_AUTHORITY)
                old, new = ([row for row in (folder / name / 'sprite/duel.log').read_bytes().splitlines()
                             if row.startswith(prefixes)] for folder in (options.repeat_from, options.output))
                if not old:
                    raise ValueError('baseline repeat has no authority records')
                require_same(old, new, f'Stage 25 same-seed {name}')
                print(f'{name}: {len(old)} same-seed authority rows', flush=True)
        except (ValueError, KeyError, IndexError, OSError, StopIteration, TimeoutExpired) as error:
            failures.append(name)
            print(f'{name}: FAIL {error!r}', flush=True)
    if failures:
        parser.exit(1, 'Failed: ' + ', '.join(failures) + '\n')


if __name__ == '__main__':
    main()
