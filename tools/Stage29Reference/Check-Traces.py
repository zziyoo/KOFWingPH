"""Verify the original IKEMEN GO State 250 physical-input fixtures."""

import argparse
import json
import math
from pathlib import Path
import runpy


HERE = Path(__file__).resolve().parent
STAGE27 = runpy.run_path(str(HERE.parent / 'Stage27Reference/Check-Traces.py'))
STAGE20 = STAGE27['STAGE20']
FIXTURES = json.loads((HERE / 'Cases.json').read_bytes())
PREFIX = 'default-standing-heavy-kick-250-'
MODE2 = 'mode2-standing-heavy-kick-250-'
ENGINE_SHA = 'd319e3ab97c9f05c680c31268064bd8786b8dc72f625c97f6d1e23b45a3b98cd'
AUDIT_ENGINE_SHA = 'e95a394e16b153b795b2a15fa1486e9f7458395943ace1f3ac48eef091855867'
TERRY_CNS_SHA = 'd1c25e17949ce0322742dd41cc4aa5a5490bf123a85803dfb5c2576460eb7fc1'
DURATIONS = (2, 3, 3, 3, 2, 2, 2, 2, 2, 2, 3, 4, 3, 3, 3, 3, 7)
STEPS = {**dict.fromkeys((8, 11, 13, 15, 17, 19, 21, 23, 26, 30), 8), 33: 5}
BODY = {PREFIX + name for name in ('hit', 'whiff', 'crouch-hit', 'guard', 'crouch-guard')}
SUPERSEDED_CASES = {PREFIX + 'hit': 145}

char_at = STAGE27['char_at']
starts = STAGE27['starts']


def read_cases(directory, *, audit=False, include_short=False):
    results = directory if (directory / 'run-metadata.json').is_file() else directory / 'results'
    run = json.loads((results / 'run-metadata.json').read_bytes())
    assert run['executable_sha256'] == (AUDIT_ENGINE_SHA if audit else ENGINE_SHA), results
    assert run['source_sha256']['Terry.cns'] == TERRY_CNS_SHA, results
    assert run['source_sha256']['common_kof.cns'] == '3b934b5390e0b805df549db35601b4882d6c28b9165b56e3dcff72d3bd86c65b', results

    for name, case in STAGE20['read_cases'](results, capture_profile='Stage29',
                                            extra_phases=('hitdef', 'rng-draw', 'var21-draw')):
        if not include_short and run.get('last_frame', 75) < SUPERSEDED_CASES.get(name, 75):
            continue
        sample = json.loads((results / f'{name}-metadata.json').read_bytes())
        fixture = FIXTURES[name]
        assert sample['physical_inputs'] == fixture, name
        for row in case[0]['before-command']:
            for seat, char in enumerate(row['chars']):
                want = STAGE27['STAGE25']['physical_bits'](fixture, seat, row['frame'], '-mirror-' in name)
                assert char['raw'] == want, (name, row['frame'], seat, char['raw'], want)
        yield name, case


def assert_start(case, target, frame, source, tick=None, seat=1):
    row, = [r for r in starts(case, {target}, seat) if r['frame'] == frame]
    assert row['from'] == source and (tick is None or row['stateTime'] == tick), row
    return row


def action_track(case, seat, actions):
    for frame, state, anim in actions:
        char = char_at(case[0], frame, seat - 1)
        assert (char['state'], char['anim']) == (state, anim), (frame, char)


def assert_hit(case, guarded=False, crouching=False, seat=1, frame=28):
    target_seat = 3 - seat
    contact, = [c for c in case[2] if c[1] == target_seat]
    assert (contact[0], contact[3]) == (frame, guarded), contact
    before = char_at(case[0], contact[0], seat - 1)
    actual = char_at(case[0], contact[0], target_seat - 1, 'after-collision')
    vars_ = before['vars']
    spec = before['hitDef']
    damage = math.floor(STAGE27['STAGE17']['f32'](
        ((18 if vars_['var6'] else 16) - vars_['var21']) * STAGE27['STAGE17']['f32'](vars_['fvar7'])))
    assert vars_['var40'] == 0 and spec['attr'] == 65 and spec['damage'] == damage, (vars_, spec)
    assert spec['guardFlag'] == 3 and spec['guardDamage'] == 0, spec
    assert (spec['hitPause'], spec['guardPause']) == ([11, 13], [11, 11]), spec
    assert spec['groundVel'] == [-4.5, -6.5, 0] and spec['guardVel'] == [-10.5, 0, 0], spec
    assert (spec['hitTime'], spec['guardHitTime'], spec['guardCtrlTime']) == (21, 21, 21), spec
    assert char_at(case[0], frame, seat - 1, 'end')['hitPause'] == 11
    assert actual['hitId'] == 30
    if guarded:
        assert (actual['hitVel'], actual['hitShake'], actual['hitTime'], actual['slideTime'], actual['hitFall']) == (
            [10.5, 0], 11, 21, 21, False), actual
        assert contact[2] == 0
        guard_hit, guard_slide = ((152, 153) if crouching else (150, 151))
        for offset, state in ((0, guard_hit), (12, guard_slide), (33, 140)):
            assert char_at(case[0], frame + offset, target_seat - 1, 'end')['state'] == state, (frame, state)
    else:
        assert (actual['hitVel'], actual['hitShake'], actual['hitFall'], actual['fallRecover'],
                actual['fallRecoverTime']) == ([4.5, -6.5], 13, True, True, 600), actual
        f32 = STAGE27['STAGE17']['f32']
        assert contact[2] == int(f32(damage * f32(before['attackMul']))), (before, contact)
        action_track(case, target_seat, tuple((frame + dt, state, anim) for dt, state, anim in (
            (1, 5000, 5030), (14, 5030, 5030), (15, 5035, 5035), (23, 5050, 5050),
            (40, 5100, 5100), (44, 5101, 5160), (56, 5110, 5170), (59, 5110, 5110),
            (69, 5120, 5120),
        )))
        assert char_at(case[0], frame + 13, target_seat - 1)['pos'][1] == 0
        assert char_at(case[0], frame + 14, target_seat - 1)['pos'][1] == -6.5


def verify_body(cases):
    for name, guarded, crouching in (('hit', False, False), ('crouch-hit', False, True),
                                     ('guard', True, False), ('crouch-guard', True, True)):
        case = cases[PREFIX + name]
        assert_start(case, 250, 5, 0)
        assert_hit(case, guarded, crouching)
        assert_start(case, 0, 65, 250, 49)
    case = cases[PREFIX + 'whiff']
    assert not case[2]
    assert_start(case, 250, 5, 0)
    end = assert_start(case, 0, 54, 250, 49)
    assert end['var2'] == 1, end
    clock = 5
    steps = []
    for index, duration in enumerate(DURATIONS):
        for frame in range(clock, clock + duration):
            char = char_at(case[0], frame)
            pose = char['frame']
            assert (char['state'], char['anim'], char['elem'], char['elemTime'], char['time']) == (
                250, 250, index, frame - clock, frame - 4), (frame, char)
            assert pose['duration'] == duration and pose['sprite'] == [300, index], (frame, pose)
            assert pose['clsn1'] == ([[0, -76, 63, -45]] if index == 10 else None), (frame, pose)
            assert char['moveType'] == (131072 if frame < 31 else 32768), (frame, char)
            before = char_at(case[0], frame, phase='after-command')
            shift = char['pos'][0] - before['pos'][0]
            assert shift == STEPS.get(frame - 5, 0), (frame, shift)
            if shift:
                steps.append({'tick': frame - 5, 'x': int(shift)})
        clock += duration
    assert clock == 54
    return {'durations': list(DURATIONS), 'positionSteps': steps, 'attackWindow': [23, 25],
            'launchX': 4.5, 'launchY': 6.5, 'guardPush': 10.5, 'guardTicks': 21,
            'hitPause': [11, 13], 'guardPause': [11, 11]}


def verify_edges(cases):
    def default(name):
        return cases[PREFIX + name]

    for name, frame, target in (
        ('cd-down', 5, 410), ('cd-down-forward', 5, 310),
        ('cd-plus-x', 5, 250), ('cd-plus-wave', 5, 1000),
        ('y-then-b', 5, 210), ('y-then-b-held', 5, 210), ('y-then-b-late', 5, 210),
        ('b-then-y', 5, 240), ('b-then-y-held', 5, 240),
        ('side-cd-fore', 5, 250), ('side-cd-back', 5, 250),
        ('cd-held', 5, 250), ('cd-repress', 54, 250),
        ('cd-global-pause-102', 102, 250), ('cd-global-pause-103', 104, 250),
        ('cd-global-pause-104', 104, 250), ('cd-early-16', 17, 250), ('cd-early-17', 17, 250),
        ('cd-direction-release', 17, 250), ('cd-direction-add', 17, 410),
        ('lying-cd', 84, 250), ('wakeup-cd', 95, 250), ('dodge-priority', 5, 760),
        ('cd-old-new-single', 17, 250), ('cd-old-new-motion', 17, 1000),
        ('cd-old-single-new-chord', 17, 250),
    ):
        transitions = [r for r in default(name)[1] if r['seat'] == 1 and r['frame'] == frame]
        assert transitions and transitions[-1]['to'] == target, (name, frame, target, transitions)
        if target == 250 and name != 'cd-repress':
            assert len(starts(default(name), {250})) == 1, name
    for name in ('y-then-b', 'y-then-b-held', 'y-then-b-late', 'b-then-y', 'b-then-y-held',
                 'cd-down', 'cd-down-forward', 'cd-plus-wave', 'cd-early-15', 'cd-direction-add',
                 'cd-old-new-motion'):
        assert not starts(default(name), {250}), name
    assert [(r['frame'], r['from']) for r in starts(default('cd-repress'), {250})] == [(5, 0), (54, 250)]
    assert len(starts(default('cd-root-in-freeze'), {250})) == 1
    for name, frame, state in (('lying-cd', 84, 5110), ('wakeup-cd', 95, 5120)):
        assert char_at(default(name)[0], frame, 1, 'after-command')['state'] == state
    for name in ('cancel-20', 'cancel-df-x', '310-26'):
        assert not starts(default(name), {300, 310}), name
    upper_last = assert_start(default('310-25'), 310, 30, 250, 25)
    assert upper_last['var1'] == 1, upper_last
    terminal = ((default('exit-wave'), 1000), (cases[MODE2 + 'exit-wave'], 1050))
    for case, target in terminal:
        row = assert_start(case, target, 54, 250, 49)
        assert (row['var1'], row['var2']) == (0, 1) and row['powerWaveCommand'], row

    assert_start(default('hit'), 0, 122, 5120, 25, 2)
    for distance in (140, 146, 147, 148):
        case = default(f'contact-{distance}')
        assert [r[:2] for r in case[2]] == ([(28, 2)] if distance < 147 else []), distance
    mirror = cases['default-standing-heavy-kick-mirror-250-hit']
    assert_start(mirror, 250, 5, 0)
    assert_start(default('seat2-hit'), 250, 5, 0, seat=2)
    assert [(r[0], r[1], r[3]) for r in mirror[2]] == [(28, 2, False)]
    assert [(r[0], r[1], r[3]) for r in default('seat2-hit')[2]] == [(28, 1, False)]
    assert {(r[0], r[1]) for r in default('trade')[2]} == {(28, 1), (28, 2)}
    assert_start(default('interrupt'), 5000, 11, 250, 7)
    assert not any(row[1] == 2 for row in default('interrupt')[2])

    assert_hit(cases[MODE2 + 'hit'])
    assert_hit(cases[MODE2 + 'guard'], guarded=True)
    air = default('air-held-back')
    airborne = char_at(air[0], 28, 1, 'after-command')
    assert airborne['stateType'] == 4 and airborne['state'] == 55 and airborne['commands']['holdback'][0] > 0
    assert [(r[0], r[1], r[3]) for r in air[2]] == [(28, 2, False)]
    assert_start(air, 5020, 28, 55, seat=2)
    tech = default('ground-tech')
    assert_start(tech, 5201, 68, 5100, 0, 2)
    assert_start(tech, 0, 96, 5201, 28, 2)

    for name in ('hit-wave-24', 'near-hit-wave'):
        case = default(name)
        row = assert_start(case, 1000, 40, 250, 24)
        assert row['var1'] == 1 and row['moveContact'], row
        assert [(r[0], r[1], r[3]) for r in case[2]] == [(28, 2, False)], name
        source, target = case[0]['after-collision'][64]['chars']
        helper, = source['helpers']
        assert helper['state'] == 1020 and helper['time'] == 10 and helper['hitDef']['attr'] == 0
        left, top, right, bottom = helper['frame']['clsn1'][0]
        dx = target['pos'][0] - helper['pos'][0]
        dy = target['pos'][1] - helper['pos'][1]
        assert target['stateType'] == 4 and left <= dx <= right and top <= dy <= bottom, name

    hit_knuckle = default('hit-cancel-24')
    assert_start(hit_knuckle, 300, 40, 250, 24)
    for frame in (*range(51, 54), *range(57, 60)):
        attacker = char_at(hit_knuckle[0], frame)
        assert attacker['frame']['clsn1'], frame
        assert attacker['vars']['var12'] == 1 and attacker['hitDef']['attr'] == 0, (frame, attacker)
    assert hit_knuckle[2] == [(28, 2, 107, False)]

    counter = default('counter-hit-wave')
    assert_start(counter, 215, 27, 0, seat=2)
    assert_start(counter, 1000, 40, 250, 24)
    assert counter[2] == [(28, 2, 130, False), (60, 2, 89, False)]
    assert char_at(counter[0], 28)['vars']['var6'] == 1
    assert_start(counter, 5020, 60, 5050, seat=2)

    guarded_route = default('guard-300-wave')
    assert_start(guarded_route, 300, 40, 250, 24)
    exit_row = assert_start(guarded_route, 1000, 80, 300, 18)
    assert exit_row['var1'] == 2 and exit_row['moveGuarded'] == 1, exit_row
    assert guarded_route[2] == [(28, 2, 0, True), (51, 2, 0, True), (68, 2, 0, True), (105, 2, 9, True)]
    for frame in (51, 68):
        assert char_at(guarded_route[0], frame, 1, 'after-command')['state'] == 151
    return {'airGuardRejected': True, 'launchWaveRestricted': True, 'launchMeleeRestricted': True,
            'launchWaveCounterAllowed': True, 'followupGuardVerified': True}


def verify_cases(cases):
    assert cases.keys() <= FIXTURES.keys(), cases.keys() - FIXTURES.keys()
    body = verify_body(cases) if BODY <= cases.keys() else None
    knuckle_checks = {
        PREFIX + 'cancel-23': (29, 24, False, 3),
        PREFIX + 'cancel-24': (29, 24, True, 0),
        PREFIX + 'cancel-25': (30, 25, True, 0),
        PREFIX + 'hit-cancel-24': (40, 24, True, 0),
        PREFIX + 'guard-cancel-24': (40, 24, True, 0),
        PREFIX + 'frozen-cancel': (40, 24, True, 2),
        MODE2 + 'cancel-24': (29, 24, True, 0),
    }
    for name, helper in (('cancel-22-release', 2), ('cancel-21', 1)):
        if PREFIX + name in cases:
            knuckle_checks[PREFIX + name] = (29, 24, False, helper)
    for name, (frame, tick, root, helper) in knuckle_checks.items():
        if name in cases:
            case = cases[name]
            row = assert_start(case, 300, frame, 250, tick)
            assert (row['var1'], row['var2'], row['hitPause'], row['xBuffer'], row['helperForwardX']) == (
                1, 0, 0, root, helper), row
            new_state = char_at(case[0], frame)
            assert (new_state['state'], new_state['anim'], new_state['elem'], new_state['time']) == (300, 300, 0, 1)
            assert not (new_state['moveContact'] or new_state['moveHit'] or new_state['moveGuarded']), new_state

    wave_checks = {PREFIX + 'wave-24': (1000, 29, 24), PREFIX + 'wave-25': (1000, 30, 25),
                   MODE2 + 'wave-24': (1050, 29, 24)}
    for name, (state, frame, tick) in wave_checks.items():
        if name in cases:
            row = assert_start(cases[name], state, frame, 250, tick)
            assert (row['var1'], row['var2']) == (1, 0)
            assert row['powerWaveCommand'] or row['helperWave'] > 0
    rising_upper = PREFIX + '310-24' in cases
    if rising_upper:
        assert assert_start(cases[PREFIX + '310-24'], 310, 29, 250, 24)['var1'] == 1
    for suffix in ('cancel-26', 'wave-26', 'ordinary-24', 'reentry-24'):
        case = cases.get(PREFIX + suffix)
        if case:
            outgoing = [row for row in case[1] if row['from'] == 250 and row['seat'] == 1]
            assert [(row['frame'], row['to']) for row in outgoing] == [(54, 0)], (suffix, outgoing)
    for suffix in ('early-entry-200', 'early-entry-300'):
        case = cases.get(PREFIX + suffix)
        if case:
            assert not starts(case, {250}), suffix

    complete = cases.keys() == FIXTURES.keys()
    field_checks = verify_edges(cases) if complete else {}
    return {'cases': len(cases), 'bodyVerified': body is not None, 'completeVerified': complete,
            'move250': body, 'cancelWindow': [24, 25],
            'backKnuckleVerified': knuckle_checks.keys() <= cases.keys() and PREFIX + 'cancel-26' in cases,
            'waveVerified': {PREFIX + f'wave-{tick}' for tick in (24, 25, 26)} <= cases.keys(),
            'risingUpperVerified': rising_upper, 'mode2WaveVerified': MODE2 + 'wave-24' in cases,
            **field_checks}


def verify_command_audit(audited, canonical):
    def normalized(row):
        # F0 before the first seeded State -2 action still shows var(21) from the intro.
        omitted_vars = {'var32'}
        if row['frame'] == 0 and row['phase'] in ('before-command', 'after-command'):
            omitted_vars.add('var21')
        return {**row, 'chars': [
            {k: (v if k != 'vars' else {key: value for key, value in v.items() if key not in omitted_vars})
             for k, v in character.items() if k not in ('blowbackCommand', 'helperBlowback')}
            for character in row['chars']
        ]}

    for name, case in audited.items():
        baseline = canonical[name]
        for phase in STAGE20['PHASES']:
            count = min(len(baseline[0][phase]), len(case[0][phase]))
            for original, observed in zip(baseline[0][phase][:count], case[0][phase][:count], strict=True):
                assert normalized(original) == normalized(observed), (name, phase, original['frame'])
        until = min(len(baseline[0]['end']), len(case[0]['end']))
        clean = [{k: v for k, v in row.items() if k not in ('blowbackCommand', 'helperBlowback')}
                 for row in case[1] if row['frame'] < until]
        assert clean == [r for r in baseline[1] if r['frame'] < until], name

    expectation = {
        'hit': ([5], [(5, 2), (6, 1)], [(5, True, 0)]),
        'cd-held': ([5], [(5, 2), (6, 1)], [(5, True, 0)]),
        'cd-repress': ([5, 54], [(5, 2), (6, 1), (54, 2), (55, 1)], [(5, True, 0), (54, True, 0)]),
        'cd-root-in-freeze': ([5, 38, 39, 40], [(5, 2), (6, 1), (38, 2), (39, 1)], [(5, True, 0)]),
        'cd-early-15': ([14], [(14, 2), (15, 1)], []),
        'cd-early-16': ([15], [(15, 2), (16, 1)], [(17, False, 1)]),
        'cd-early-17': ([16], [(16, 2), (17, 1)], [(17, False, 2)]),
        'cd-direction-release': ([16], [(16, 2), (17, 1)], [(17, False, 2)]),
        'cd-direction-add': ([16], [(16, 2), (17, 1)], []),
        'cd-global-pause-102': ([102], [(102, 2), (103, 1)], [(102, True, 0)]),
        'cd-global-pause-103': ([103], [(103, 2), (104, 1)], [(104, False, 2)]),
        'cd-global-pause-104': ([104], [(104, 2), (105, 1)], [(104, True, 0)]),
        **dict.fromkeys(('y-then-b', 'y-then-b-held', 'y-then-b-late', 'b-then-y', 'b-then-y-held'),
                        ([], [], [])),
    }
    for suffix, expected in expectation.items():
        case = audited[PREFIX + suffix]
        roots = [row['frame'] for row in case[0]['after-command'] if row['chars'][0]['blowbackCommand']]
        helper = [(row['frame'], row['chars'][0]['helperBlowback']) for row in case[0]['end']
                  if row['chars'][0]['helperBlowback'] > 0]
        entries = [(row['frame'], row['blowbackCommand'], row['helperBlowback']) for row in starts(case, {250})]
        assert (roots, helper, entries) == expected, (suffix, roots, helper, entries)
    paused = audited[PREFIX + 'cd-global-pause-103'][0]['after-command'][103]
    assert paused['globalPause'] > 0, paused
    return len(audited)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directories', type=Path, nargs='+')
    parser.add_argument('--input-audit', type=Path)
    options = parser.parse_args()
    cases = {}
    for directory in options.directories:
        for name, case in read_cases(directory):
            assert name not in cases, ('duplicate fixture', name)
            cases[name] = case
    result = verify_cases(cases)
    if options.input_audit:
        result['inputAuditCases'] = verify_command_audit(dict(read_cases(options.input_audit, audit=True)), cases)
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
