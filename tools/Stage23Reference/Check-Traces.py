"""Verify native State 410 attacks, recovery, CMD windows, and successor Wave."""

import argparse
import json
import math
from pathlib import Path
import runpy


TOOLS = Path(__file__).resolve().parents[1]
stage20 = runpy.run_path(str(TOOLS / 'Stage20Reference/Check-Traces.py'))
stage17 = runpy.run_path(str(TOOLS / 'Stage17Reference/Check-Traces.py'))
PREFIX = 'default-crouch-heavy-wave-'
MODE2 = 'mode2-crouch-heavy-wave-'
PHASES = ('hitdef', 'rng-draw', 'var21-draw')
BODY = {'hit-0', 'held-down-0', 'guard-0', 'crouch-guard-0', 'crouch-hit-0', 'interrupt-0',
        'mirror-hit-0', 'whiff-0', 'stand-0', 'dual-button-0'}


def read_cases(directory):
    yield from stage20['read_cases'](directory, capture_profile='Stage23', extra_phases=PHASES)


def copy_cases(source, destination, selected):
    """Build a body-only input set without recapturing or editing a native case."""
    from shutil import copyfile

    metadata = json.loads((source / 'run-metadata.json').read_bytes())
    names = [name for name in metadata['executed_cases'] if name in selected]
    assert set(names) == set(selected), 'unexpected body-only case names'
    files = [p for name in names for p in source.glob(name + '*')]
    assert all((source / f'{name}-metadata.json').is_file() for name in names), 'native cases have not finished'
    destination.mkdir(parents=True, exist_ok=False)
    for path in files:
        copyfile(path, destination / path.name)
    metadata['executed_cases'] = names
    (destination / 'run-metadata.json').write_text(json.dumps(metadata, indent=2) + '\n', encoding='utf-8')


def measure(case, blocked):
    phases, _, contacts = case
    (frame, seat, damage, guarded), = contacts
    assert (frame, seat, guarded) == (10, 2, blocked), contacts
    collision, end = (phases[key] for key in ('after-collision', 'end'))
    attacker_pause = collision[frame + 1]['chars'][0]['hitPause']
    assert attacker_pause == 11
    assert [collision[f]['chars'][0]['hitPause'] for f in range(frame + 1, frame + 12)] == list(range(11, 0, -1))
    target_pause = collision[frame]['chars'][1]['hitShake']
    assert target_pause == (11 if blocked else 13)
    restored = next(row['frame'] for row in end[frame + 1:] if row['chars'][1]['ctrl'])
    first_movement = frame + target_pause + 1
    push = end[first_movement]['chars'][1]['pos'][0] - end[first_movement - 1]['chars'][1]['pos'][0]
    assert push == 10.5 and (not blocked or damage == 0)
    return {'damage': damage, 'initialPush': push, 'effectiveStun': restored - frame - target_pause,
            'pause': {'attackerFrames': attacker_pause, 'defenderFrames': target_pause},
            'contactFrame': frame, 'restoredFrame': restored}


def state410_transitions(case):
    return [(row['frame'], row['to'], row['stateTime']) for row in case[1] if row['from'] == 410]


def helper_live(character):
    return [h for h in character['helpers'] or [] if h['anim'] == 1020 and not h['destroyed']]


def verify_successor(case, frame, target_state, position, blocked, fall, restored):
    phases, _, contacts = case
    assert [(f, seat, guard) for f, seat, _, guard in contacts if f > 10] == [(frame, 2, blocked)]
    collision, end = (phases[key] for key in ('after-collision', 'end'))
    target = collision[frame]['chars'][1]
    assert target['state'] == target_state and target['hitFall'] is fall
    assert abs(abs(target['pos'][0]) - position) < 0.0001 and target['pos'][1] == 0
    source = collision[frame]['chars'][0]
    assert len(helper_live(source)) == 1
    before_ctrl = [r['frame'] for r in end[frame + 1:restored] if r['chars'][1]['ctrl']]
    assert not before_ctrl and end[restored]['chars'][1]['ctrl'], (frame, restored, before_ctrl)
    if fall:
        states = []
        for row in collision[frame + 1:restored + 1]:
            state = row['chars'][1]['state']
            if not states or state != states[-1]:
                states.append(state)
        assert states == [5000, 5030, 5035, 5050, 5100, 5101, 5110, 5120, 0], states
        assert collision[restored]['chars'][1]['pos'][1] == 0


def ground_reaction(case, measurement, posture, *, target=1):
    phases, _, _ = case
    hit, exit_frame = (measurement[key] for key in ('contactFrame', 'restoredFrame'))
    collision = phases['after-collision']
    expected = {'standing': (5000, 5001, 5002, 5007, 0),
                'crouching': (5010, 5011, 5022, 5027, 11),
                'crouchingLight': (5010, 5011, 5020, 5025, 11),
                'waveLow': (5000, 5001, 5012, 5017, 0)}[posture]
    shake, slide, initial, tail, idle = expected
    frozen = measurement['pause']['defenderFrames']
    unfreeze = hit + frozen + 1
    assert all(collision[f]['chars'][target]['state'] == shake for f in range(hit + 1, unfreeze)), posture
    assert all(collision[f]['chars'][target]['state'] == slide for f in range(unfreeze, exit_frame)), posture
    assert all(collision[f]['chars'][target]['pos'][1] == 0 and not collision[f]['chars'][target]['hitFall']
               for f in range(hit, exit_frame)), posture
    assert collision[exit_frame]['chars'][target]['state'] == idle, posture
    animation = [collision[f]['chars'][target]['anim'] for f in range(hit + 1, exit_frame)]
    switch = next(f for f in range(unfreeze, exit_frame) if collision[f]['chars'][target]['anim'] == tail)
    assert animation == [initial] * (switch - hit - 1) + [tail] * (exit_frame - switch), posture
    active = sum(row['globalPause'] == 0 for row in collision[unfreeze:switch])
    return {'initialTicks': active + 1, 'effectiveStun': measurement['effectiveStun'],
            'initialAction': initial, 'slideAction': tail}


def verify_case(name, phases, changes, contacts):
    collision, inputs, end = (phases[key] for key in ('after-collision', 'before-command', 'end'))
    for row in changes:
        frame, seat = row['frame'], row['seat'] - 1
        committed = collision[frame]['chars'][seat]
        if row['to'] == 410:
            assert (committed['state'], committed['stateType'], committed['anim'], committed['time']) == (410, 2, 410, 1), name
            assert inputs[frame]['chars'][seat]['raw'] & 2 and row['yBuffer'], name
        if row['from'] == 410 and row['to'] in (1000, 1050):
            assert row['hitPause'] == 0 and (row['powerWaveCommand'] or row['helperWave'] > 0), name
            assert ((6 <= row['stateTime'] <= 12 and row['var1'] == 1) or
                    (row['stateTime'] == 27 and row['var2'] >= 1)), name
            assert (committed['state'], committed['time'], committed['moveContact']) == (row['to'], 1, 0), name
    stream = phases['rng-draw']
    assert stream and stream[0]['before'] == 1337, name
    assert all(row['after'] == row['before'] * 16807 % stage17['MODULUS'] for row in stream), name
    for row in phases['hitdef']:
        source = row['chars'][row['hitDefOwner'] - 1]
        if source['state'] != 410:
            continue
        variables = source['vars']
        base = 15 if variables['var6'] else 13
        raw = math.floor(stage17['f32']((base - variables['var21']) * stage17['f32'](variables['fvar7'])))
        elapsed = source['time']
        assert source['hitDef']['damage'] == raw and source['elem'] == 2 and 5 <= elapsed <= 12, (name, row['frame'])
        assert source['hitDef']['groundVel'][:2] == [-10.5, 0], (name, row['frame'])
    for frame, seat, damage, _ in contacts:
        assert end[frame + 1]['chars'][seat - 1]['life'] == collision[frame]['chars'][seat - 1]['life'] - damage, (name, frame)


def verify_cases(cases):
    assert all(PREFIX + name in cases for name in BODY), 'missing native State 410 body controls'
    for name, case in cases.items():
        verify_case(name, *case)
    hit = measure(cases[PREFIX + 'hit-0'], False)
    guard = measure(cases[PREFIX + 'guard-0'], True)
    crouch_hit = measure(cases[PREFIX + 'crouch-hit-0'], False)
    crouch_guard = measure(cases[PREFIX + 'crouch-guard-0'], True)
    reactions = {posture: ground_reaction(cases[PREFIX + name], values, posture)
                 for posture, name, values in [('standing', 'hit-0', hit), ('crouching', 'crouch-hit-0', crouch_hit)]}
    assert [hit['effectiveStun'], guard['effectiveStun'], crouch_hit['effectiveStun'], crouch_guard['effectiveStun']] == [19, 18, 19, 18]
    for name, exit_frame, hold in [('hit-0', 43, False), ('held-down-0', 43, True), ('whiff-0', 32, False)]:
        phases, _, contacts = cases[PREFIX + name]
        assert state410_transitions(cases[PREFIX + name]) == [(exit_frame, 11, 27)], name
        collision = phases['after-collision']
        assert all(r['chars'][0]['stateType'] == 2 and r['chars'][0]['anim'] == 410
                   for r in collision[5:exit_frame]), name
        states = [collision[f]['chars'][0]['state'] for f in range(exit_frame, exit_frame + 6)]
        assert states == ([11] * 6 if hold else [11, 12, 12, 12, 12, 0]), (name, states)
        if name == 'whiff-0':
            assert not contacts
    for name, expected in [('stand-0', 215), ('dual-button-0', 400)]:
        phases, _, _ = cases[PREFIX + name]
        assert phases['after-collision'][5]['chars'][0]['state'] == expected, name
        assert not any(r['to'] == 410 for r in cases[PREFIX + name][1]), name
    interrupted = cases[PREFIX + 'interrupt-0']
    assert interrupted[2] == [(6, 1, 30, False)]
    collision = interrupted[0]['after-collision']
    contact = collision[6]['chars'][0]
    restored = next(row['frame'] for row in collision[7:] if row['chars'][0]['ctrl'])
    assert (contact['state'], contact['stateType'], contact['raw'] & 2, contact['hitShake'], restored) == (410, 2, 0, 13, 30)
    reactions['crouchingLight'] = ground_reaction(interrupted, {'contactFrame': 6, 'restoredFrame': restored,
        'pause': {'defenderFrames': contact['hitShake']}, 'effectiveStun': restored - 6 - contact['hitShake']},
        'crouchingLight', target=0)
    assert [collision[f]['chars'][0]['state'] for f in range(restored, restored + 6)] == [11, 12, 12, 12, 12, 0]
    assert cases[PREFIX + 'mirror-hit-0'][2] == cases[PREFIX + 'hit-0'][2]
    optional = [('down-forward-heavy-0', 310), ('down-forward-both-0', 310), ('down-back-heavy-0', 410)]
    for name, expected in optional:
        if PREFIX + name in cases:
            phases, changes, _ = cases[PREFIX + name]
            assert phases['after-collision'][5]['chars'][0]['state'] == expected, name
            assert any(r['frame'] == 5 and r['to'] == expected for r in changes), name
    if PREFIX + 'crouch-hit-release-0' in cases:
        case = cases[PREFIX + 'crouch-hit-release-0']
        released = ground_reaction(case, measure(case, False), 'crouching')
        assert released == reactions['crouching']
        assert [case[0]['after-collision'][f]['chars'][1]['state'] for f in range(42, 48)] == [11,12,12,12,12,0]
    expected = {
        'hit-22': (22,1000,6), 'hit-28': (28,1000,12), 'hit-29': (43,11,27), 'hit-43': (43,1000,27),
        'hit-held-14': (22,1000,6), 'hit-14': (22,1000,6), 'guard-22': (22,1000,6),
        'crouch-guard-22': (22,1000,6), 'crouch-hit-22': (22,1000,6), 'mirror-hit-22': (22,1000,6),
        'mixed-22': (22,1000,6), 'whiff-9': (11,1000,6), 'whiff-10': (11,1000,6),
        'whiff-11': (11,1000,6), 'whiff-17': (17,1000,12), 'whiff-18': (32,11,27), 'whiff-32': (32,1000,27),
        'compete-11': (11,1000,6), 'approach-11': (11,1000,6),
    }
    verify_cancel = PREFIX + 'hit-22' in cases
    if verify_cancel:
        for name, entry in expected.items():
            assert state410_transitions(cases[PREFIX + name])[0] == entry, name
        assert state410_transitions(cases[MODE2 + 'hit-22'])[0] == (22,1050,6)
        assert state410_transitions(cases[MODE2 + 'hit-0']) == [(43,11,27)]
        for name, frame, target, pos, blocked, fall, exit_frame in [
            ('hit-22', 41, 5001, 90.8852, False, True, 147),
            ('hit-28', 47, 0, 90.8852, False, True, 153),
            ('crouch-hit-22', 40, 5011, 83.33462, False, True, 146),
            ('mirror-hit-22', 41, 5001, 90.8852, False, True, 147),
            ('whiff-11', 35, 0, 70, False, False, 76),
            ('approach-11', 27, 0, 44.33203, False, True, 133),
        ]:
            verify_successor(cases[PREFIX + name], frame, target, pos, blocked, fall, exit_frame)
        low_wave = cases[PREFIX + 'whiff-11']
        collision = low_wave[0]['after-collision']
        wave_contact = collision[35]['chars'][1]
        paused = sum(row['globalPause'] > 0 for row in collision[36:76])
        reactions['waveLow'] = ground_reaction(low_wave, {'contactFrame': 35, 'restoredFrame': 76,
            'pause': {'defenderFrames': wave_contact['hitShake']},
            'effectiveStun': 76 - 35 - wave_contact['hitShake'] - paused}, 'waveLow')
        assert reactions['waveLow'] == {'initialTicks': 4, 'effectiveStun': 19, 'initialAction': 5012, 'slideAction': 5017}
        for name, frame in [('guard-22', 41), ('crouch-guard-22', 40)]:
            assert cases[PREFIX + name][2] == [(10, 2, 0, True), (frame, 2, 9, True)]
        for name, frame, helper_count in [('whiff-10', 10, 11), ('whiff-9', 9, 7), ('whiff-8', 8, 3)]:
            if PREFIX + name not in cases:
                continue
            phases, changes, _ = cases[PREFIX + name]
            cancel = next(r for r in changes if r['from'] == 410 and r['to'] == 1000)
            assert cancel['frame'] == 11 and not cancel['powerWaveCommand'] and cancel['helperWave'] == helper_count, name
            assert phases['after-collision'][frame]['chars'][0]['powerWaveCommand'], name
        assert cases[PREFIX + 'approach-0'][2] == [(11,2,99,False)]
        assert cases[PREFIX + 'approach-11'][2] == [(27,2,76,False)]
        assert not cases[PREFIX + 'whiff-18'][2]
        assert cases[PREFIX + 'hit-29'][2] == [(10,2,92,False)]
        if PREFIX + 'whiff-7' in cases:
            case = cases[PREFIX + 'whiff-7']
            collision = case[0]['after-collision']
            assert collision[7]['chars'][0]['powerWaveCommand']
            assert [collision[f]['chars'][0]['helperWave'] for f in range(7, 12)] == [11, 7, 3, -1, -1]
            assert state410_transitions(case) == [(32,11,27)] and not case[2]
        if PREFIX + 'rising-upper-11' in cases:
            assert state410_transitions(cases[PREFIX + 'rising-upper-11']) == [(11,310,6)]
        if PREFIX + 'rising-upper-18' in cases:
            assert state410_transitions(cases[PREFIX + 'rising-upper-18']) == [(32,11,27)]
        for name, frame, position in [('guard-release-22', 41, 90.8852), ('crouch-guard-release-22', 40, 81.164)]:
            if PREFIX + name not in cases:
                continue
            phases, _, contacts = cases[PREFIX + name]
            target = phases['after-collision'][frame]['chars'][1]
            assert contacts == [(10,2,0,True),(frame,2,9,True)] and target['state'] == 140, name
            assert not target['raw'] and abs(target['pos'][0] - position) < 0.0001, name
        for name in ['incoming-wave-22', 'incoming-wave-28', 'incoming-wave-20']:
            if PREFIX + name not in cases:
                continue
            phases, changes, contacts = cases[PREFIX + name]
            assert not any(helper_live(r['chars'][0]) for r in phases['after-collision']), name
            assert len(contacts) == 1 and contacts[0][1] == 1, name
            contact = contacts[0][0]
            assert helper_live(phases['after-collision'][contact + 1]['chars'][1]), name
            cancel = [r for r in changes if r['from'] == 410 and r['to'] == 1000]
            assert (len(cancel), contact) == {'incoming-wave-22': (0,21), 'incoming-wave-28': (0,25),
                                             'incoming-wave-20': (1,29)}[name], name
        phases, changes, contacts = cases[PREFIX + 'mixed-22']
        assert [(r['seat'],r['frame'],r['from']) for r in changes if r['to'] == 1000] == [(1,22,410),(2,90,200)]
        assert len(contacts) == 2 and contacts[-1] == (41,2,9,True)
    return {'hit': hit, 'guard': guard, 'crouchHit': crouch_hit, 'crouchGuard': crouch_guard,
            'groundReactions': reactions,
            'cancelVerified': verify_cancel}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directories', type=Path, nargs='+')
    parser.add_argument('--extract-body', type=Path)
    args = parser.parse_args()
    if args.extract_body:
        assert len(args.directories) == 1, 'body extraction requires one source directory'
        copy_cases(args.directories[0], args.extract_body, {PREFIX + name for name in BODY})
        args.directories = [args.extract_body]
    cases = {}
    for directory in args.directories:
        for name, data in read_cases(directory):
            assert name not in cases, name
            cases[name] = data
    print(json.dumps(verify_cases(cases), sort_keys=True))
    print(f'Verified {len(cases)} native Stage 23 traces')
