"""Check real Vulkan State 400 against the verified original-engine traces."""

import argparse
from dataclasses import replace
from functools import partial
import json
from pathlib import Path
from subprocess import TimeoutExpired
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'Stage21Validation'))
from heavy_wave_duel import normal_damage
from power_wave_duel import (ACTIONS, MOVE, NATIVE_MOVES, ROOT, assert_equal, audit_rows, fighter_poses,
                             POSE_KEYS, indexed, native_case, run_case, verify_contacts, verify_grounded_wave_recovery,
                             visual_timeline)
from near_wave_duel import capture_sparse_realtime, compare_repeat


CROUCH = 'TerryCrouchLightPunch400'


def local_boxes(row, field):
    x, y = int(row['x']), int(row['y'])
    facing = 1 if row['facing'] == 'Right' else -1
    result = []
    for rect in filter(None, row[field].split(';')):
        left, bottom, right, top = map(int, rect.split(','))
        a, b = sorted(((left - x) * facing, (right - x) * facing))
        result.append((a, y - top, b, y - bottom))
    return sorted(result)


def native_boxes(source, field):
    return sorted(tuple(v * 65536 for v in (min(a,c), min(b,d), max(a,c), max(b,d)))
                  for a,b,c,d in (source['frame'][field] or []))


def verify_body(profile, changes, scene, phases, events, moves, reentry, lines):
    damage = indexed(audit_rows(lines, 'DamageAudit'))
    attack = next(move['attacks'][0] for move in profile['moves'] if move['moveId'] == CROUCH)
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    starts = [e for e in events if e['kind'] == 'MoveStarted' and e['move'] == CROUCH]
    for entry in starts:
        frame, seat = entry['frame'], entry['source']
        for logic in range(frame, max(f for i, f in moves if i == seat)):
            row = moves[seat, logic]
            if row['move'] != CROUCH or int(reentry[seat, logic]['started']) != frame:
                break
            original = phases['after-collision'][logic - 14]['chars'][seat ^ mirror]
            assert_equal((400, 2, int(row['elapsed']) + 1), (original['state'], original['stateType'], original['time']),
                         f'original crouch attack clock at {logic}')
            assert_equal(int(row['elapsed']) in (3,4), bool(row['attacks']), f'400 attack box at {logic}')
            assert_equal(native_boxes(original, 'clsn1'), local_boxes(row, 'attacks'), f'400 Clsn1 at {logic}')
            assert_equal(native_boxes(original, 'clsn2'), local_boxes(row, 'hurt'), f'400 Clsn2 at {logic}')
    for event in events:
        if event['move'] != CROUCH or event['kind'] not in ('Hit', 'Blocked'):
            continue
        logic, seat, target = event['frame'], event['source'], event['target']
        outcome = 'hit' if event['kind'] == 'Hit' else 'block'
        pause = attack['hitstopByOutcome'][outcome]
        assert_equal((pause['attackerFrames'], pause['defenderFrames']),
                     (int(moves[seat, logic]['stop']), int(moves[target, logic]['stop'])), '400 independent HitPause')
        amount = normal_damage(profile, damage, moves, event, attack) if outcome == 'hit' else 0
        assert_equal(amount, event['amount'], '400 dynamic damage')
        assert_equal(amount, int(moves[target, logic - 1]['health']) - int(moves[target, logic]['health']), '400 atomic HP')
        if outcome == 'hit' and len(starts) == 1 and not scene.differences:
            # Begin after the reaction phase has committed. At contact the native target still holds its old pose.
            next_contact = min((e['frame'] for e in events if e['target'] == target and e['frame'] > logic
                                and e['kind'] in ('Hit', 'Blocked', 'ProjectileHit', 'ProjectileBlocked')),
                               default=max(frame for _, frame in moves) + 1)
            tick = logic + 1
            while tick < next_contact and (target, tick) in moves and moves[target, tick]['state'] == 'Hitstun':
                original = phases['after-collision'][tick - 14]['chars'][target ^ mirror]
                if original['anim'] not in (5010, 5015):
                    break
                assert_equal(native_boxes(original, 'clsn2'), local_boxes(moves[target, tick], 'hurt'),
                             f'400 light reaction Clsn2 at {tick}')
                tick += 1
    for logic, seat in ((r['frame'] + 14, (r['seat'] - 1) ^ mirror)
                        for r in changes if r['from'] == 400 and r['to'] == 11):
        assert_equal('Crouching', moves[seat, logic]['state'], 'State 400 exits through crouch')
        for tick in range(logic, max(f for i, f in moves if i == seat)):
            source = phases['after-collision'][tick - 14]['chars'][seat ^ mirror]
            if source['state'] not in (11, 12):
                break
            actual = moves[seat, tick]
            assert_equal('Crouching' if source['state'] == 11 else 'Standing', actual['state'],
                         f'crouch exit posture at {tick}')
            assert_equal(native_boxes(source, 'clsn2'), local_boxes(actual, 'hurt'), f'crouch exit Clsn2 at {tick}')
    return {'crouchMoveStarts': starts}


def realtime_route(starts, events):
    assert_equal([CROUCH, MOVE], [row['move'] for row in starts], 'realtime State 400 and Wave')
    first, = [row for row in events if row['kind'] == 'Hit' and row['move'] == CROUCH]
    hit, = [row for row in events if row['kind'] == 'ProjectileHit']
    assert_equal(3, first['frame'] - starts[0]['frame'], 'State 400 first active contact')
    assert_equal(12, starts[1]['frame'] - first['frame'], 'cancel on thaw')
    return hit


def verify_poses(phases, scene, events, committed, timelines, modes):
    windows = fighter_poses(scene, events, committed, timelines, modes, actions={**ACTIONS, CROUCH: 400})
    mirror = phases['before-command'][0]['chars'][0]['facing'] < 0
    for event in events:
        if event['kind'] != 'Blocked' or event['move'] != CROUCH:
            continue
        seat, state = event['target'], event['frame'] + 1
        source = phases['before-command'][state - 15]['chars'][seat ^ mirror]
        expected = 151 if source['raw'] & 2 else 150
        assert_equal(str(expected), committed[seat, state]['action'], f'guard pose at state {state}')
    for seat in range(2):
        after_crouch = False
        for frame, rows in sorted(phases['after-collision'].items()):
            source = rows['chars'][seat ^ mirror]
            state = frame + 15
            if (seat, state) not in committed:
                continue
            if source['state'] == 400:
                after_crouch = True
            elif source['state'] == 12 and after_crouch:
                row = committed[seat, state]
                tick = source['time'] - 1
                assert_equal((str(tick), timelines[12][tick]),
                             (row['tick'], tuple(row[key] for key in POSE_KEYS)), f'Action 12 at state {state}')
            elif source['state'] != 11:
                after_crouch = False
    return windows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--godot', required=True, type=Path)
    parser.add_argument('--reference', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--profile-dir', default='data/imported-characters/terry98-stage22-data')
    parser.add_argument('--scene', action='append', required=True)
    parser.add_argument('--mode', default='default', choices=('default','mode2'))
    parser.add_argument('--compare-plain', action='store_true')
    parser.add_argument('--repeat-from', type=Path)
    parser.add_argument('--fps', default='fixed', choices=('fixed','30','60'))
    parser.add_argument('--realtime-window', type=int, nargs=2)
    parser.add_argument('--realtime-input-spacing', type=int, choices=(1, 2), default=1)
    options = parser.parse_args()
    options.output = options.output.resolve()
    options.output.mkdir(parents=True, exist_ok=False)
    options.recovery = False
    options.disable_vsync = True
    options.require_clean_sample = True
    options.require_no_backlog = options.fps != 'fixed'
    options.realtime_resolution = '320x180'
    options.realtime_warmup_frames = 150
    metadata = json.loads((options.reference / 'run-metadata.json').read_bytes())
    last = metadata['last_frame']
    manifest = json.loads((ROOT / 'data/imported-characters/terry98/manifest.json').read_text(encoding='utf-8-sig'))
    timelines = {i: visual_timeline(manifest, i) for i in (12,200,205,210,211,215,400,1000,1020)}
    scene_profile = json.loads((ROOT / options.profile_dir / f'{options.mode}.fighter.json').read_bytes())
    alias = f'terry98-stage22-{options.mode}'
    failures = []
    for name in options.scene:
        try:
            reference = options.reference / f'{options.mode}-crouch-wave-{name}.jsonl'
            scene, phases, scene_changes = native_case(reference, name, last_frame=last,
                                                       native_moves={**NATIVE_MOVES, 400: CROUCH})
            if options.mode == 'mode2' and name == 'hit-20':
                control, control_phases, scene_changes = native_case(reference.with_name('mode2-crouch-wave-hit-0.jsonl'),
                    'hit-0', last_frame=last, native_moves={**NATIVE_MOVES,400:CROUCH})
                scene = replace(scene, contacts=control.contacts, differences=('native-mode2-1050-outside-scope',))
                phases['end'] = control_phases['end']
                phases['after-collision'] = control_phases['after-collision']
            if options.fps != 'fixed':
                if not options.realtime_window:
                    raise ValueError('realtime needs a capture window')
                capture_sparse_realtime(options, name, scene, alias, realtime_route)
                continue
            if name == 'mixed-20':
                scene = replace(scene, captures=(62, 151))
            else:
                scene = replace(scene, captures=(26, min(100, scene.captures[1])))
            run_case(options, name, reference, timelines, scene_profile, mode=options.mode, alias=alias,
                     prepared_scene=(scene, phases, scene_changes), fixed_checks=partial(verify_body, scene_profile, scene_changes),
                     contact_check=partial(verify_contacts, guard_position_tolerance=0.01),
                     recovery_check=partial(verify_grounded_wave_recovery, position_tolerance=0.01, crouch_recovery=True),
                     pose_check=partial(verify_poses, phases),
                     minimum_render_frames=last + 25, partial_capture=True,
                     allow_interrupted_spawn=name == 'incoming-wave-20')
            if options.repeat_from:
                compare_repeat(options.output, name, options.repeat_from)
        except (ValueError, KeyError, IndexError, OSError, TimeoutExpired) as error:
            print(f'{name}: FAIL {error!r}', flush=True)
            failures.append(name)
    if failures:
        parser.exit(1, 'Failed: ' + ', '.join(failures) + '\n')


if __name__ == '__main__':
    main()
