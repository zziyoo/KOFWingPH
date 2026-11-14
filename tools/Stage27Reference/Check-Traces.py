"""Verify pinned IKEMEN GO stand heavy kick traces for Stage 27."""

import argparse
import json
import math
from pathlib import Path
import runpy


HERE = Path(__file__).resolve().parent
STAGE26 = runpy.run_path(str(HERE.parent / 'Stage26Reference/Check-Traces.py'))
STAGE25 = STAGE26['STAGE25']
STAGE20 = STAGE26['STAGE20']
STAGE17 = STAGE26['STAGE17']
FIXTURES = json.loads((HERE / 'Cases.json').read_bytes())
PREFIX = 'default-standing-heavy-kick-'
MODE2 = 'mode2-standing-heavy-kick-'
MOVES = {240: (13, 3, 22), 241: (19, 4, 20), 245: (3, 6, 20)}
BOXES = {240: [[56, -88, 127, -57]], 241: [[8, -91, 79, -61]], 245: [[36, -101, 79, -29]]}
POSES = {
    240: ((1, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 3, 3, 4), (7,), {28: 32}),
    241: ((1, 3, 3, 3, 2, 2, 2, 3, 4, 5, 4, 4, 3, 4), (8,), {1: 8, 4: 8, 7: 8, 10: 8, 12: 8}),
    245: ((1, 2, 3, 3, 4, 5, 5, 6), (2, 3), {}),
}


def read_cases(directory):
    meta = json.loads((directory / 'run-metadata.json').read_bytes())
    assert meta['source_sha256']['common_kof.cns'] == '3b934b5390e0b805df549db35601b4882d6c28b9165b56e3dcff72d3bd86c65b'
    for name, case in STAGE20['read_cases'](directory, capture_profile='Stage27',
                                          extra_phases=('hitdef', 'rng-draw', 'var21-draw')):
        fixture = FIXTURES[name]
        case_meta = json.loads((directory / f'{name}-metadata.json').read_bytes())
        assert fixture == case_meta['physical_inputs'], name
        for row in case[0]['before-command']:
            for seat, char in enumerate(row['chars']):
                assert char['raw'] == STAGE25['physical_bits'](fixture, seat, row['frame'], '-mirror-' in name), (
                    name, row['frame'], seat)
        yield name, case


char_at = STAGE26['char_at']
starts = STAGE26['starts']


def verify_case(name, case):
    phases, changes, _ = case
    assert phases['rng-draw'] and phases['rng-draw'][0]['before'] == 1337, name
    assert all(row['after'] == row['before'] * 16807 % STAGE17['MODULUS'] for row in phases['rng-draw']), name
    for row in phases['hitdef']:
        source = row['chars'][row['hitDefOwner'] - 1]
        hit = source['hitDef']
        if source['state'] not in (240, 245) or hit['attr'] == 0:
            continue
        variables = source['vars']
        normal, counter = (15, 17) if source['state'] == 240 else (12, 15)
        base = counter if variables['var6'] else normal
        damage = math.floor(STAGE17['f32']((base - variables['var21']) * STAGE17['f32'](variables['fvar7'])))
        assert hit['damage'] == damage and hit['guardDamage'] == 0, (name, row['frame'], hit, variables)
        assert hit['groundVel'][:2] == [-11 if variables['var40'] else -10.5, 0]
        assert hit['hitTime'] == 17, (name, row['frame'])
    for row in changes:
        source, target, tick = row['from'], row['to'], row['stateTime']
        if target in (240, 245) and source != 805:
            assert (target == 245) == (row['bodyDistX'] <= 48), (name, row)
            assert not row['raw'] & 2 and (row['bBuffer'] or row['helperB'] > 0), (name, row)
        if source not in (240, 245):
            continue
        natural = sum(MOVES[row['anim']])
        if target in (1000, 1050):
            direct = source == 245 and 4 <= tick <= 8
            assert (direct or tick == natural) and row['hitPause'] == 0, (name, row)
            assert (row['var1'], row['var2']) == (int(direct), int(not direct)), (name, row)
            assert row['powerWaveCommand'] or row['helperWave'] > 0, (name, row)
        if target in (200, 205, 210, 215, 230, 235, 240, 245, 400, 410, 430, 440):
            assert tick == natural, (name, row)


def measure(case, action, *, blocked=False, crouching=False, seat=0):
    phases, _, contacts = case
    target = 1 - seat
    state = 240 if action == 241 else action
    entry, = starts(case, {state}, seat + 1)
    frame, target_pn, damage, guarded = contacts[0]
    assert len(contacts) == 1 and (frame, target_pn, guarded) == (entry['frame'] + MOVES[action][0], target + 1, blocked)
    assert damage == 0 if blocked else damage > 0
    source_hit = char_at(phases, frame, seat)
    target_hit = char_at(phases, frame, target)
    pause = 11 if blocked else 13
    assert (target_hit['hitShake'], target_hit['hitFall'], target_hit['hitId']) == (pause, False, 10)
    push = abs(target_hit['hitVel'][0])
    assert push == 10.5 and target_hit['hitVel'][1] == 0
    attacker_pause = char_at(phases, frame + 1, seat)['hitPause']
    assert attacker_pause == 11 and source_hit['anim'] == action
    restored = next(f for f in range(frame + 1, len(phases['end'])) if char_at(phases, f, target)['ctrl'])
    effective = restored - frame - pause
    result = {'damage': damage, 'initialPush': push, 'effectiveStun': effective, 'contactFrame': frame,
              'restoredFrame': restored, 'pause': {'attackerFrames': attacker_pause, 'defenderFrames': pause}}
    if blocked:
        expected = 151 if crouching else 150
        assert all(char_at(phases, f, target)['anim'] == expected for f in range(frame + 1, restored))
        return result, None
    initial, slide = (5022, 5027) if crouching else (5002, 5007)
    switch = next(f for f in range(frame + 1, restored) if char_at(phases, f, target)['anim'] == slide)
    assert [char_at(phases, f, target)['anim'] for f in range(frame + 1, restored)] == (
        [initial] * (switch - frame - 1) + [slide] * (restored - switch))
    return result, {'initialTicks': switch - (frame + pause), 'effectiveStun': effective,
                    'initialAction': initial, 'slideAction': slide}


def verify_whiff(case, action):
    phases, _, contacts = case
    state = 240 if action == 241 else action
    start, = starts(case, {state})
    first = start['frame']
    end = first + sum(MOVES[action])
    assert not any(first <= contact[0] < end for contact in contacts)
    exit_row, = [r for r in case[1] if r['from'] == state and r['seat'] == 1]
    assert (exit_row['frame'], exit_row['stateTime'], exit_row['to'], exit_row['var2']) == (
        end, sum(MOVES[action]), 0, 1), exit_row
    durations, active, steps = POSES[action]
    facing = char_at(phases, first)['facing']
    at = first
    moved = []
    for index, duration in enumerate(durations):
        for frame in range(at, at + duration):
            char = char_at(phases, frame)
            box = char['frame']
            assert (char['state'], char['anim'], char['elem']) == (state, action, index)
            assert (box['duration'], box['offset'], box['clsn1'] or []) == (
                duration, [0, 0], BOXES[action] if index in active else [])
            assert char['moveType'] == (131072 if index <= active[-1] else 32768)
            before = char_at(phases, frame, phase='after-command')['pos'][0]
            after = char_at(phases, frame)['pos'][0]
            actual = (after - before) * facing
            assert actual == steps.get(frame - first, 0), (action, frame, actual)
            if actual:
                moved.append({'tick': frame - first, 'x': actual})
        at += duration
    assert at == end
    return moved


def body_contract(cases, prefix, label, action):
    names = [prefix + f'{label}-{kind}' for kind in ('hit', 'guard', 'crouch-hit', 'crouch-guard')]
    if not all(name in cases for name in names):
        return None
    hit, standing = measure(cases[names[0]], action)
    guard, _ = measure(cases[names[1]], action, blocked=True)
    crouch_hit, crouching = measure(cases[names[2]], action, crouching=True)
    crouch_guard, _ = measure(cases[names[3]], action, blocked=True, crouching=True)
    assert crouch_guard == guard and crouch_hit['effectiveStun'] == hit['effectiveStun']
    return {'hit': hit, 'guard': guard, 'groundReactions': {'standing': standing, 'crouching': crouching}}


def verify_mode2_guard_entry(cases):
    for crouching, name in ((False, 'far-guard'), (True, 'far-crouch-guard')):
        case = cases.get(MODE2 + name)
        if case is None:
            return False
        phases = case[0]
        start = 11 if crouching else 20
        standing_guard = 131 if crouching else 130
        expected = [start, 120, 120, 120] + [standing_guard] * 4
        positions = [49] * 8 if crouching else [
            52.16797, 54.86074, 57.149597, 59.095127, 60.748825, 62.15447, 63.349266, 64.364845,
        ]
        defender = [char_at(phases, frame, 1) for frame in range(5, 13)]
        assert [row['state'] for row in defender] == expected, name
        assert [row['anim'] for row in defender[1:4]] == [121 if crouching else 120] * 3, name
        assert [row['pos'][0] for row in defender] == positions, name
        assert defender[-1]['vel'][0] == 0, name
        attacker = char_at(phases, 5)
        assert (attacker['state'], attacker['anim'], attacker['moveType'], attacker['hitDef']['attr']) == (
            240, 241, 131072, 0), name
    return True


def verify_cases(cases):
    for name, case in cases.items():
        verify_case(name, case)
    moves = {action: contract for label, action in (('near', 245), ('far', 240))
             if (contract := body_contract(cases, PREFIX, label, action)) is not None}
    mode2_far = body_contract(cases, MODE2, 'far', 241)
    mode2_guard_entry = verify_mode2_guard_entry(cases)
    poses = {}
    for prefix, label, action in ((PREFIX, 'far', 240), (MODE2, 'far', 241), (PREFIX, 'near', 245)):
        name = prefix + f'{label}-whiff'
        if name in cases:
            poses[action] = verify_whiff(cases[name], action)
    shared = []
    for label, blocked, crouching, kind in (('near-hit', False, False, 'hit'),
                                            ('near-crouch-guard', True, True, 'guard')):
        if MODE2 + label in cases and 245 in moves:
            values, reaction = measure(cases[MODE2 + label], 245, blocked=blocked, crouching=crouching)
            assert values == moves[245][kind]
            assert reaction == (None if blocked else moves[245]['groundReactions']['standing'])
            shared.append(True)
    if MODE2 + 'near-whiff' in cases:
        assert verify_whiff(cases[MODE2 + 'near-whiff'], 245) == []
        shared.append(True)
    body_verified = len(moves) == 2 and mode2_far is not None and len(shared) == 3
    routes = verify_routes(cases)
    return {'moves': moves, 'mode2Far': mode2_far, 'posAdds': poses, 'positionStepsVerified': sorted(poses),
            'bodyVerified': body_verified, **routes,
            'completeVerified': body_verified and mode2_guard_entry and set(poses) == MOVES.keys() and all(routes.values())}


def verify_routes(cases):
    def lookup(name, mode2=False):
        return cases.get((MODE2 if mode2 else PREFIX) + name)

    def choice(name, frame, source, target, tick=None, *, mode2=False, seat=1, permissions=None):
        case = lookup(name, mode2)
        if case is None:
            return False
        row, = [r for r in starts(case, {target}, seat) if r['frame'] == frame and r['from'] == source]
        assert (tick is None or row['stateTime'] == tick) and row['hitPause'] == 0, (name, row)
        assert permissions is None or (row['var1'], row['var2']) == permissions, (name, row)
        return True

    def no_start(name, states, *, mode2=False, after=5, seat=1):
        case = lookup(name, mode2)
        if case is None:
            return False
        assert not [row for row in starts(case, states, seat) if row['frame'] > after], name
        return True

    wave = []
    for kind, base in (('whiff', 95), ('hit', 16), ('guard', 16), ('crouch-guard', 16)):
        for old in (4, 8):
            wave.append(choice(f'near-wave-{kind}-{old}', base + old, 245, 1000, old, permissions=(1, 0)))
        wave.append(no_start(f'near-wave-{kind}-9', {1000, 1050}))
    for mode2, target in ((False, 1000), (True, 1050)):
        for old in (4, 8, 29):
            wave.append(choice(f'near-wave-whiff-{old}', 95 + old, 245, target, old,
                               mode2=mode2, permissions=(1, 0) if old < 9 else (0, 1)))
        wave.append(no_start('near-wave-whiff-9', {1000, 1050}, mode2=mode2))
    wave.append(choice('near-wave-whiff-3-early', 100, 245, 1000, 4, permissions=(1, 0)))
    wave.extend(choice(f'near-wave-pause-{old}', 20, 245, 1000, 4, permissions=(1, 0))
                for old in (3, 4, 5, 6))
    wave.extend(choice(f'far-wave-whiff-{end}', 5 + end, 240, target, end,
                       mode2=mode2, permissions=(0, 1))
                for mode2, end, target in ((False, 38, 1000), (True, 43, 1050)))
    wave.extend(no_start(f'far-wave-whiff-{tick}', {1000, 1050}) for tick in (4, 13, 16, 28))
    for name, frame, source, target, old, mode2, permissions in (
        ('near-wave-whiff-28', 124, 245, 1000, 29, False, (0, 1)),
        ('near-wave-whiff-30', 125, 0, 1000, 0, False, (0, 0)),
        ('far-wave-whiff-37', 43, 240, 1000, 38, False, (0, 1)),
        ('far-wave-whiff-39', 44, 0, 1000, 0, False, (0, 0)),
        ('far-wave-whiff-42', 48, 240, 1050, 43, True, (0, 1)),
        ('far-wave-whiff-44', 49, 0, 1050, 0, True, (0, 0)),
        ('near-wave-pause-held-mixed', 20, 245, 1000, 4, False, (1, 0)),
    ):
        wave.append(choice(name, frame, source, target, old, mode2=mode2, permissions=permissions))
    for old, frame in ((4, 99), (8, 103), (29, 124)):
        wave.append(choice(f'near-wave-mixed-{old}', frame, 245, 1000, old,
                           permissions=(1, 0) if old < 9 else (0, 1)))
    wave.append(no_start('near-wave-mixed-9', {1000, 1050, 200, 205, 240, 245}, after=95))
    # Forward X and down-forward Y are valid deferred alternatives only in 245's special window.
    reservations = {}
    for key, target, proof in (('f-x', 300, 'forwardReserveVerified'), ('d-f-y', 310, 'risingReserveVerified')):
        checks = [choice(f'near-early-{tick}-{key}', 95 + tick, 245, target, tick, permissions=(1, 0))
                  for tick in (4, 8)]
        checks.append(choice(f'near-early-3-{key}', 99, 245, target, 4, permissions=(1, 0)))
        checks.append(no_start(f'near-early-9-{key}', {target}, after=95))
        reservations[proof] = all(checks)
    blowback = []
    throws = []
    for mode2 in (False, True):
        blowback.append(choice('near-mix-y-b', 5, 0, 250, mode2=mode2))
        throws.extend(choice(f'throw-range-{direction}-b', 5, 20, 805, mode2=mode2)
                      for direction in ('f', 'b'))
    blowback.append(choice('near-mix-d-y-b', 5, 10, 410))
    throws.append(choice('throw-range-b', 5, 0, 245))
    throws.append(choice('throw-range-f-b-above', 5, 20, 245))
    throws.append(choice('throw-range-f-b-air', 15, 20, 245))
    inputs = []
    for name, expected in (('a-b', 230), ('x-b', 200), ('d-a-b', 430), ('d-x-b', 400), ('d-y-b', 410)):
        for distance in ('near', 'far'):
            inputs.append(choice(f'{distance}-mix-{name}', 5, 10 if name.startswith('d-') else 0, expected))
    for label, expected, initial, consumed in (
        ('toward', 245, 51.054688, 47.886719), ('release-down', 245, 51.054688, 47.886719),
        ('down', 440, 51.054688, 47.886719), ('far', 240, 52.304688, 49.136719),
        ('away-ground', 240, 45.304688, 48.472656),
    ):
        key = f'sweep-b-buffer-{label}'
        if choice(key, 45, 440, expected, 40, permissions=(0, 1)):
            case = lookup(key)
            first = char_at(case[0], 44, phase='before-command')
            row, = [r for r in starts(case, {expected}) if r['frame'] == 45]
            assert abs(first['bodyDistX'] - initial) < 0.000005, (key, first['bodyDistX'])
            assert abs(row['bodyDistX'] - consumed) < 0.000005
            assert (row['bBuffer'], row['helperB']) == (False, 2), (key, row)
            inputs.append(True)
        else:
            inputs.append(False)
    inputs.extend(choice(f'near-b-age-{frame}', 45, 245, 240, 29, permissions=(0, 1)) for frame in (43, 44, 45))
    for frame, expected in ((43, (False, 1)), (44, (False, 2)), (45, (True, 0))):
        case = lookup(f'near-b-age-{frame}')
        if case is not None:
            row, = [r for r in starts(case, {240}) if r['frame'] == 45]
            assert (row['bBuffer'], row['helperB']) == expected, (frame, row)
    inputs.extend((choice('sweep-b-age-2', 45, 440, 245, 40, permissions=(0, 1)),
                   no_start('sweep-b-age-3', {240, 245, 440})))
    old_edge = lookup('sweep-b-age-2')
    if old_edge is not None:
        row, = starts(old_edge, {245})
        assert (row['bBuffer'], row['helperB']) == (False, 1), row
    for name in ('near', 'far'):
        inputs.extend((no_start(f'{name}-same-edge-repeat', {240, 245}),
                       choice(f'{name}-same-edge-new', 65, 0, 240)))
    inputs.extend((choice('legal-wave-mix-normal', 5, 0, 1000),
                   no_start('legal-wave-mix-normal', {240, 245}, after=0)))
    inputs.append(no_start('near-b-age-9', {240, 245}))
    frozen = lookup('near-b-age-9')
    if frozen is not None:
        original = [char_at(frozen[0], f, phase='before-command') for f in range(9, 23)]
        assert [char['helperB'] for char in original] == [0, 2, 1] + [0] * 11
        assert [char['commands']['b'][0] for char in original] == [0] + [2] * 11 + [1, 0]
    overlap = lookup('sweep-old-a-new-b')
    if overlap is not None:
        row, = [r for r in starts(overlap, {235}) if r['frame'] == 45]
        assert (row['stateTime'], row['bodyDistX'], row['helperA'], row['bBuffer']) == (40, 27.886719, 2, True)
        assert [char_at(overlap[0], f, phase='before-command')['helperB'] for f in (46, 47, 48)] == [2, 1, 0]
    inputs.extend((overlap is not None, no_start('sweep-old-a-new-b', {240, 245, 440}, after=45)))
    for label, state, x in (('forward', 20, -71.83203), ('back', 20, -78.16797), ('down-forward', 11, -75),
                             ('idle', 11, -75), ('attack', 230, -75)):
        key = f'sweep-walk-{label}'
        if choice(key, 45, 440, state, 40, permissions=(0, 1)):
            case = lookup(key)
            assert char_at(case[0], 45, phase='after-command')['pos'][0] == -75
            assert char_at(case[0], 45)['pos'][0] == x
            inputs.append(True)
        else:
            inputs.append(False)
    for label, expected, distance in (('a-near', 235, 26.140625), ('a-far', 230, 29.308594),
                                       ('b-near', 245, 45.148438), ('b-far', 240, 48.316406)):
        key = f'sweep-walk-threshold-{label}'
        if choice(key, 90, 0, expected):
            row, = starts(lookup(key), {expected})
            assert row['bodyDistX'] == distance
            inputs.append(True)
        else:
            inputs.append(False)
    for edge in (103, 106):
        key = f'far-global-pause-b-{edge}'
        if choice(key, edge + 1, 0, 240):
            case = lookup(key)
            phases = case[0]
            row, = [r for r in starts(case, {240}) if r['frame'] == edge + 1]
            assert phases['after-command'][edge]['globalPause'] == phases['end'][edge]['globalPause'] == 1
            assert char_at(phases, edge, phase='after-command')['commands']['b'] == [1, 0]
            assert char_at(phases, edge, phase='end')['helperB'] == 2
            assert (row['bBuffer'], row['helperB']) == (False, 2)
            inputs.append(True)
        else:
            inputs.append(False)
    inputs.extend((choice('far-global-pause-b-102', 102, 1000, 240, 52, permissions=(0, 2)),
                   choice('far-global-pause-b-104', 104, 0, 240)))
    boundaries = []
    for label, distance, state in (('below', 47.5, 245), ('exact', 48, 245), ('above', 48.5, 240)):
        for family, frame in (('threshold', 5), ('air-threshold', 15)):
            name = family + '-' + label
            case = lookup(name)
            if case is not None:
                row, = starts(case, {240, 245})
                assert (row['frame'], row['to'], row['bodyDistX']) == (frame, state, distance), name
            boundaries.append(case is not None)
    for name, state, distance, mode2, seat in (('mirror-threshold-exact', 245, 48, False, 1),
                                                ('p2-threshold-exact', 245, 48, False, 2),
                                                ('crouch-threshold-exact', 245, 48, False, 1),
                                                ('threshold-exact', 245, 48, True, 1),
                                                ('threshold-above', 240, 48.5, True, 1)):
        case = lookup(name, mode2)
        if case is not None:
            row, = starts(case, {240, 245}, seat)
            assert (row['frame'], row['to'], row['bodyDistX']) == (5, state, distance), name
        boundaries.append(case is not None)
    for label, expected in (('near', 245), ('far', 240)):
        for suffix, seat in (('mirror', 0), ('p2', 1)):
            name = f'{suffix}-{label}-hit'
            case = lookup(name)
            if case is not None:
                measure(case, expected, seat=seat)
            boundaries.append(case is not None)
        interrupted = lookup(f'{label}-interrupt')
        if interrupted is not None:
            assert interrupted[2] == ([(6, 1, 46, False)] if label == 'near' else [(16, 1, 122, False)])
        boundaries.append(interrupted is not None)
    chains = []
    for label, state, frame, command_targets in (
        ('near', 245, 45, (('x', 200), ('a', 230), ('y', 210), ('b', 240), ('d-x', 400),
                           ('d-y', 410), ('d-a', 430), ('d-b', 440))),
        ('far', 240, 54, (('x', 200), ('a', 230), ('b', 240), ('d-b', 440))),
    ):
        for command, target in command_targets:
            chains.append(choice(f'{label}-natural-{command}', frame, state, target,
                                 sum(MOVES[state]), permissions=(0, 1)))
    for command, target in (('x', 200), ('a', 230), ('b', 240), ('d-b', 240)):
        chains.append(choice(f'far-early-{command}', 54, 240, target, 38, permissions=(0, 1)))
    chains.extend((choice('far-to-near-new-instance', 72, 0, 245),
                   choice('near-to-far-new-instance', 45, 245, 240, 29, permissions=(0, 1))))
    for button, old in (('x', 4), ('x', 8), ('x', 9), ('a', 4), ('b', 8), ('d-b', 8)):
        chains.append(no_start(f'near-early-{old}-{button}', {200, 205, 210, 215, 230, 235, 240, 245,
                                                              400, 410, 430, 440}, after=95))
    for name in ('ordinary-from-near-light', 'ordinary-from-close-jab', 'ordinary-from-crouch-jab',
                 'ordinary-from-close-heavy'):
        chains.append(no_start(f'{name}-at-70', {240, 245}))
    return {'waveVerified': all(wave), 'blowbackVerified': all(blowback),
            'kickThrowVerified': all(throws), 'inputVerified': all(inputs), 'boundariesVerified': all(boundaries),
            'chainsVerified': all(chains), **reservations}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directories', nargs='+', type=Path)
    options = parser.parse_args()
    cases = {}
    for directory in options.directories:
        for name, case in read_cases(directory):
            assert name not in cases, name
            cases[name] = case
    batches = json.loads((HERE / 'Batches.json').read_bytes())
    expected = {name for batch in batches.values() for name in batch['cases']}
    if cases.keys() != expected:
        parser.exit(1, f'Incomplete Stage 27 reference: missing {sorted(expected - cases.keys())}, '
                    f'unexpected {sorted(cases.keys() - expected)}\n')
    result = verify_cases(cases)
    print(json.dumps(result, indent=2))
    if not result['completeVerified']:
        parser.exit(1, 'Stage 27 reference contract did not pass\n')
    print(f'PASS: {len(cases)} native Stage 27 fixtures')


if __name__ == '__main__':
    main()
