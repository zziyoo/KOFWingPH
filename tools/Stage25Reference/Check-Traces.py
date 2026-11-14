"""Verify the pinned IKEMEN GO State 430 captures and derive the ground contract."""
import argparse
import json
import math
from pathlib import Path
import runpy

HERE = Path(__file__).resolve().parent
STAGE20 = runpy.run_path(str(HERE.parent / 'Stage20Reference/Check-Traces.py'))
STAGE17 = runpy.run_path(str(HERE.parent / 'Stage17Reference/Check-Traces.py'))
PREFIX = 'default-crouch-light-kick-'
MODE2 = 'mode2-crouch-light-kick-'
FIXTURES = json.loads((HERE / 'Cases.json').read_bytes())
GUARD_CONTROLS = json.loads((HERE / 'GuardControls.json').read_bytes())
BODY = {'hit', 'crouch-hit', 'stand-guard', 'crouch-guard', 'whiff', 'mirror-hit', 'p2-hit', 'interrupt'}
ATTACK_BOX = [0, -16, 63, -1]
HURT_BOX = [-20, -72, 19, -1]


def physical_bits(fixture, seat, f, mirrored):
    forward, back = (8, 4) if (seat == 0) != mirrored else (4, 8)
    keys = {'D': 2, 'U': 1, 'F': forward, 'B': back, 'a': 16, 'b': 32, 'x': 128, 'y': 256}
    bits = 0
    for first, last, inputs in fixture[f'p{seat+1}']:
        if first <= f <= last:
            for key in inputs.split('+'):
                bits |= keys[key]
    return bits


def read_cases(directory, *, fixtures=FIXTURES):
    run = json.loads((directory / 'run-metadata.json').read_bytes())
    for name, case in STAGE20['read_cases'](directory, capture_profile='Stage25',
                                          extra_phases=('hitdef', 'rng-draw', 'var21-draw')):
        fixture = fixtures[name]
        metadata = json.loads((directory / f'{name}-metadata.json').read_bytes())['physical_inputs']
        assert metadata == fixture, name
        assert run['source_sha256']['common_kof.cns'] == '3b934b5390e0b805df549db35601b4882d6c28b9165b56e3dcff72d3bd86c65b'
        mirrored = '-mirror-' in name
        phases, _, _ = case
        for row in phases['before-command']:
            for seat, char in enumerate(row['chars']):
                assert char['raw'] == physical_bits(fixture, seat, row['frame'], mirrored), (name, row['frame'], seat)
        yield name, case


def char_at(phases, frame, seat=0, phase='after-collision'):
    return phases[phase][frame]['chars'][seat]


def outgoing(case, source, seat=1):
    return [row for row in case[1] if row['from'] == source and row['seat'] == seat]


def measure(case, *, blocked=False, crouching=False, seat=0, sign=1, reaction_x=None):
    phases, _, contacts = case
    target = 1 - seat
    (frame, target_pn, damage, guarded), = contacts
    assert (frame, target_pn, damage, guarded) == (8, target + 1, 0 if blocked else 46, blocked)
    hit = char_at(phases, frame, target)
    pause = 11 if blocked else 13
    assert (hit['hitShake'], hit['hitVel'], hit['hitFall'], hit['hitId']) == (pause, [6 * sign, 0], False, 10)
    assert [char_at(phases, f, seat)['hitPause'] for f in range(9, 20)] == list(range(11, 0, -1))
    first_move = frame + pause + 1
    assert char_at(phases, first_move, target)['pos'][0] - char_at(phases, first_move - 1, target)['pos'][0] == 6 * sign
    restored = next(f for f in range(frame + 1, len(phases['end'])) if char_at(phases, f, target)['ctrl'])
    assert restored == (29 if blocked else 32)
    if reaction_x is not None:
        assert abs(char_at(phases, restored, target)['pos'][0] - reaction_x * sign) < .0001
    effective = restored - frame - pause
    result = {'damage': damage, 'initialPush': 6, 'effectiveStun': effective, 'contactFrame': frame,
              'restoredFrame': restored, 'pause': {'attackerFrames': 11, 'defenderFrames': pause}}
    if blocked:
        assert all(char_at(phases, f, target)['anim'] == 151 for f in range(9, restored))
        return result, None
    initial, slide = (5020, 5025) if crouching else (5010, 5015)
    switch = 27
    assert [char_at(phases, f, target)['anim'] for f in range(9, restored)] == (
        [initial] * (switch - 9) + [slide] * (restored - switch))
    return result, {'initialTicks': switch - first_move + 1, 'effectiveStun': effective,
                    'initialAction': initial, 'slideAction': slide}


def verify_case(name, case):
    phases, changes, _ = case
    rng = phases['rng-draw']
    assert rng and rng[0]['before'] == 1337, name
    assert all(row['after'] == row['before'] * 16807 % STAGE17['MODULUS'] for row in rng), name
    for row in phases['hitdef']:
        source = row['chars'][row['hitDefOwner'] - 1]
        if source['state'] != 430 or source['hitDef']['attr'] == 0:
            continue
        h = source['hitDef']
        v = source['vars']
        base = 8 if v['var6'] else 7
        damage = math.floor(STAGE17['f32']((base - v['var21']) * STAGE17['f32'](v['fvar7'])))
        assert h['damage'] == damage and h['guardDamage'] == 0, name
        assert h['groundVel'][:2] == [-6.5 if v['var40'] else -6, 0], name
        assert h['guardFlag'] == 2 and h['hitTime'] == 9, name
    for row in changes:
        if row['from'] != 430:
            continue
        assert row['var1'] == 0, (name, row)
        if row['to'] in (1000, 1050):
            assert (row['stateTime'], row['var2'], row['hitPause']) == (14, 1, 0), (name, row)
            assert row['powerWaveCommand'] or row['helperWave'] > 0, (name, row)


def route(case, source, frame, target, tick, seat=1):
    row, = [r for r in outgoing(case, source, seat) if r['frame'] == frame]
    assert (row['to'], row['stateTime'], row['hitPause']) == (target, tick, 0), row
    return row


def starts(case, target, seat=1):
    return [r for r in case[1] if r['to'] == target and r['seat'] == seat]


def verify_routes(cases):
    def d(name):
        return cases.get(PREFIX + name)

    body = (('far-jab', 200, 12, 16, 0), ('near-high-target-jab', 205, 12, 16, 10),
            ('crouch-jab', 400, 11, 17, 0))
    outgoing_ = (('far-jab', 200), ('close-high-target-jab', 205), ('crouch-jab', 400), ('kick', 430))
    required = [f'{name}-to-kick-{f}' for name, _, start, end, _ in body for f in (start - 3, start - 1, start, end, end + 1)]
    required += [f'to-{name}-{f}' for name, _ in outgoing_ for f in (10, 11, 18, 19)]
    for name, source, start, end, offset in body:
        if case := d(f'{name}-to-kick-{start - 1}'):
            row, = [r for r in outgoing(case, source) if r['frame'] == start + offset]
            assert row['to'] in (230, 235) and (row['stateTime'], row['helperA'], row['raw']) == (start - 5, 2, 0)
        for f in (start, end):
            if case := d(f'{name}-to-kick-{f}'):
                assert route(case, source, f + offset, 430, f - 5)['var2'] == 0
        if case := d(f'{name}-to-kick-{end + 1}'):
            assert route(case, source, end + 1 + offset, 430, end - 4)['var2'] == 1
        if case := d(f'{name}-to-kick-{start - 3}'):
            assert not starts(case, 430)
    for name, target in outgoing_:
        offset = 10 if target == 205 else 0
        if case := d(f'to-{name}-10'):
            dest = 230 if target == 430 else 200 if target == 400 else target
            row = route(case, 430, 11 + offset, dest, 6)
            assert (row['helperA'] if target == 430 else row['helperX'], row['raw']) == (2, 0)
        for f in (11, 18, 19):
            if case := d(f'to-{name}-{f}'):
                assert route(case, 430, f + offset, target, f - 5)['var2'] == int(f == 19)
    for case_name, source, f in [('far-jab-early-held-down', 200, 12), ('crouch-jab-early-held-down', 400, 11),
                                 ('to-kick-early-held-down', 430, 11), ('to-kick-before-direction', 430, 11)]:
        if case := d(case_name):
            row = route(case, source, f, 430, f - 5)
            assert (row['raw'], row['aBuffer'], row['helperA']) == (2, False, 2)
    for case_name in ('hit-to-kick-18', 'hit-to-kick-19', 'hit-to-jab-18', 'hit-to-jab-19'):
        if case := d(case_name):
            route(case, 430, 30, 11, 14)
            helper = 'helperA' if '-kick-' in case_name else 'helperX'
            assert char_at(case[0], 22, phase='before-command')[helper] == 0
    for case_name, target in [('hit-to-kick-20', 235), ('hit-to-jab-20', 200),
                              ('hit-to-kick-22', 430), ('hit-to-crouch-jab-22', 400),
                              ('hit-to-near-jab-22', 205)]:
        if case := d(case_name):
            row = route(case, 430, 22, target, 6)
            assert row['moveContact'] > 0
            if target == 205:
                assert row['bodyDistX'] <= 16 and case[2] == [(8, 2, 46, False), (25, 2, 29, False)]
    for case_name, target in [('both-kicks', 430), ('light-punch-compete', 400), ('heavy-punch-compete', 410),
                              ('down-forward-heavy-compete', 310), ('forward-down', 430), ('back-down', 430)]:
        if case := d(case_name):
            row, = starts(case, target)
            assert row['frame'] == 5, case_name
    if case := d('sweep-aged-a'):
        row = route(case, 440, 45, 430, 40)
        assert (row['raw'], row['aBuffer'], row['helperA'], row['var2']) == (34, False, 2, 1)
        assert len(starts(case, 440)) == 1
        assert [char_at(case[0], f, phase='before-command')['helperB'] for f in range(45, 49)] == [0, 2, 1, 0]
    if case := d('sweep-release-down'):
        assert route(case, 440, 45, 230, 40)['helperA'] == 2
    for case_name, frame, destination, tick in [('wave-9', 11, 200, 6), ('wave-12', 12, 200, 7),
                                                ('wave-18', 18, 200, 13), ('wave-19', 19, 1000, 14),
                                                ('hit-wave-27', 27, 200, 11)]:
        if case := d(case_name):
            row = route(case, 430, frame, destination, tick)
            assert (row['var1'], row['var2']) == (0, int(tick == 14))
            if destination != 1000:
                assert not starts(case, 1000)
    if case := d('wave-18'):
        assert [(char_at(case[0], f, phase='before-command')['helperForwardX'],
                 char_at(case[0], f, phase='before-command')['helperWave']) for f in range(19, 23)] == [
            (3, 11), (2, 7), (1, 3), (0, -1)]
    if case := d('wave-startup-7'):
        attempt = char_at(case[0], 7, phase='after-command')
        assert (attempt['state'], attempt['time'], attempt['powerWaveCommand']) == (430, 2, True)
        route(case, 430, 19, 11, 14)
        assert not starts(case, 1000) and not starts(case, 200)
    if case := d('wave-forward-held-through-exit'):
        assert route(case, 430, 17, 200, 12)['powerWaveCommand']
        wave = route(case, 200, 21, 1000, 4)
        assert (wave['powerWaveCommand'], wave['helperWave'], wave['var1'], wave['var2']) == (False, 8, 1, 0)
    if case := d('wave-17-to-down'):
        assert route(case, 430, 17, 200, 12)['powerWaveCommand']
        assert not starts(case, 1000)
    if case := d('wave-20'):
        route(case, 430, 19, 20, 14)
        assert route(case, 20, 20, 1000, 1)['ctrl']
    if case := d('wave-mixed-at-exit'):
        row = route(case, 430, 19, 1000, 14)
        assert row['powerWaveCommand'] and row['aBuffer'] and row['bBuffer']
    if case := d('hit-wave-30'):
        row = route(case, 430, 30, 1000, 14)
        assert row['var2'] == 1 and row['moveHit'] > 0
        assert case[2][:2] == [(8, 2, 46, False), (46, 2, 76, False)]
    if case := d('hit-wave-10'):
        assert char_at(case[0], 10, phase='after-command')['powerWaveCommand']
        assert char_at(case[0], 20, phase='after-command')['hitPause'] == 0
        route(case, 430, 30, 11, 14)
        assert not starts(case, 1000)
    if case := d('wave-crouch-fallback'):
        row = route(case, 430, 12, 400, 7)
        assert row['powerWaveCommand'] and not starts(case, 1000)
    if case := d('through-400-wave'):
        route(case, 430, 11, 400, 6)
        assert route(case, 400, 15, 1000, 4)['var1'] == 1
    for name, target in [('mode-chain', 430), ('mode-to-crouch-jab', 400)]:
        if case := cases.get(MODE2 + name):
            route(case, 430, 11, target, 6)
    required_mode2 = (MODE2 + 'mode-chain', MODE2 + 'mode-to-crouch-jab')
    if case := d('wave-forward-neutral-boundary'):
        row = route(case, 430, 19, 300, 14)
        assert not row['powerWaveCommand'] and row['var2'] == 1 and not starts(case, 1000)
    mode2_natural = False
    if case := cases.get(MODE2 + 'wave-19-mode'):
        row = route(case, 430, 19, 1050, 14)
        assert (row['var1'], row['var2']) == (0, 1)
        mode2_natural = True
    return {'chainsVerified': all(d(name) for name in (*required, 'hit-to-near-jab-22'))
            and all(name in cases for name in required_mode2),
            'mode2NaturalWaveVerified': mode2_natural, 'completeVerified': cases.keys() == FIXTURES.keys()}


def verify_cases(cases):
    def default(name):
        return cases[PREFIX + name]

    assert all(PREFIX + name in cases for name in BODY), 'missing State 430 body controls'
    for name, case in cases.items():
        verify_case(name, case)
    hit, standing = measure(default('hit'), reaction_x=58.56914)
    _, crouching = measure(default('crouch-hit'), crouching=True, reaction_x=55.349445)
    guard, _ = measure(default('crouch-guard'), blocked=True, reaction_x=54.746014)
    measure(default('stand-guard'))
    measure(default('mirror-hit'), sign=-1, reaction_x=58.56914)
    measure(default('p2-hit'), seat=1, sign=-1, reaction_x=58.56914)
    for name in ('hit', 'crouch-hit', 'crouch-guard', 'stand-guard', 'mirror-hit', 'whiff', 'p2-hit'):
        seat = 2 if name == 'p2-hit' else 1
        row, = outgoing(default(name), 430, seat)
        assert (row['frame'], row['to'], row['stateTime'], row['var2']) == (19 if name == 'whiff' else 30, 11, 14, 1)
    assert default('interrupt')[2] == [(6, 1, 30, False)]
    if case := cases.get(PREFIX + 'counter-sweep'):
        source = next(row['chars'][0] for row in case[0]['hitdef'] if row['frame'] == 8 and row['hitDefOwner'] == 1)
        assert source['vars']['var6'] == 1 and source['hitDef']['damage'] == 57
    if case := cases.get(PREFIX + 'airborne-fall-overlap'):
        assert case[2] == [(15, 2, 115, False)]
        attacks = [row for row in case[0]['hitdef'] if row['hitDefOwner'] == 1 and row['chars'][0]['state'] == 430]
        assert [row['frame'] for row in attacks] == [73, 74, 75]
        assert all(row['chars'][0]['vars']['var12'] == 0 and row['chars'][0]['hitDef']['attr'] == 66
                   and row['chars'][1]['stateType'] == 8 for row in attacks)
    phases, _, contacts = default('whiff')
    assert not contacts
    frames = [char_at(phases, f)['frame'] for f in range(5, 19)]
    assert [frame['sprite'] for frame in frames] == [
        [430, i] for i, ticks in ((0, 1), (1, 2), (2, 3), (3, 3), (1, 2), (0, 3)) for _ in range(ticks)
    ]
    for tick, frame in enumerate(frames):
        assert frame['offset'] == [0, 0]
        assert (frame['clsn1'] or []) == ([ATTACK_BOX] if 3 <= tick <= 5 else [])
        hurt = [HURT_BOX]
        if 3 <= tick <= 5:
            hurt.append([-12, -24, 51, -1])
        if 6 <= tick <= 8:
            hurt.append([12, -32, 43, -1])
        assert frame['clsn2'] == hurt
    return {'hit': hit, 'guard': guard, 'groundReactions': {'standing': standing, 'crouching': crouching},
            **verify_routes(cases)}


def verify_guard_controls(cases):
    assert cases.keys() == GUARD_CONTROLS.keys(), cases.keys()
    for name, case in cases.items():
        verify_case(name, case)
        phases, _, contacts = case
        releases_down = name.endswith('release-down-22')
        second = (25, 2, 44, False) if releases_down else (25, 2, 0, True)
        assert contacts == [(8, 2, 0, True), second], (name, contacts)
        target_at = lambda frame: char_at(phases, frame, 1)
        assert all(target_at(frame)['anim'] == 151 and not target_at(frame)['ctrl'] for frame in range(22, 26)), name
        reaction = target_at(26)
        assert (reaction['state'], reaction['time'], reaction['anim'], reaction['hitShake']) == (
            (5000, 1, 5010, 12) if releases_down else (152, 1, 151, 10)), name
        assert next(frame for frame in range(26, 75) if target_at(frame)['ctrl']) == (
            49 if releases_down else 46), name


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directories', nargs='+', type=Path)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument('--body-only', action='store_true')
    mode.add_argument('--guard-controls', action='store_true')
    args = parser.parse_args()
    cases = {}
    fixtures = GUARD_CONTROLS if args.guard_controls else FIXTURES
    for directory in args.directories:
        for name, case in read_cases(directory, fixtures=fixtures):
            assert name not in cases, name
            cases[name] = case
    if args.guard_controls:
        verify_guard_controls(cases)
        print(f'PASS: {len(cases)} supplementary native State 430 guard controls')
        return
    if not args.body_only:
        assert cases.keys() == FIXTURES.keys(), (FIXTURES.keys() - cases.keys(), cases.keys() - FIXTURES.keys())
    print(json.dumps(verify_cases(cases), indent=2))
    print(f'PASS: {len(cases)} native Stage 25 cases')


if __name__ == '__main__':
    main()
