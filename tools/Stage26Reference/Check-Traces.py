"""Check the pinned IKEMEN GO standing light kick captures."""

import argparse
import json
import math
from pathlib import Path
import runpy

HERE = Path(__file__).resolve().parent
STAGE25 = runpy.run_path(str(HERE.parent / 'Stage25Reference/Check-Traces.py'))
STAGE20 = STAGE25['STAGE20']
STAGE17 = STAGE25['STAGE17']
FIXTURES = json.loads((HERE / 'Cases.json').read_bytes())
PREFIX = 'default-standing-light-kick-'
MOVES = {230: (5, 3, 11), 235: (4, 5, 7)}
BODY_HURT = [[-16, -88, 23, -1], [4, -100, 27, -77]]
FAR_EXTENSION = [[-16, -88, 23, -1], [16, -96, 103, -57]]
NEAR_EXTENSION = [[-20, -88, 19, -1], [-8, -104, 15, -81]]
ANIMATION = {
    230: [(215, 3, 2, BODY_HURT), (230, 0, 3, BODY_HURT), (230, 1, 3, FAR_EXTENSION),
          (230, 1, 3, FAR_EXTENSION), (230, 0, 4, BODY_HURT), (215, 3, 4, BODY_HURT)],
    235: [(215, 3, 1, BODY_HURT), (235, 1, 3, BODY_HURT), (235, 2, 5, NEAR_EXTENSION),
          (235, 1, 3, BODY_HURT), (235, 0, 4, NEAR_EXTENSION)],
}
ATTACK_BOXES = {230: [[36, -72, 107, -49]], 235: [[17, -75, 62, -17]]}


def read_cases(directory):
    metadata = json.loads((directory / 'run-metadata.json').read_bytes())
    assert metadata['source_sha256']['common_kof.cns'] == '3b934b5390e0b805df549db35601b4882d6c28b9165b56e3dcff72d3bd86c65b'
    for name, case in STAGE20['read_cases'](directory, capture_profile='Stage26',
                                          extra_phases=('hitdef', 'rng-draw', 'var21-draw')):
        fixture = FIXTURES[name]
        case_metadata = json.loads((directory / f'{name}-metadata.json').read_bytes())
        assert fixture == case_metadata['physical_inputs'], name
        mirrored = '-mirror-' in name
        for row in case[0]['before-command']:
            for seat, char in enumerate(row['chars']):
                assert char['raw'] == STAGE25['physical_bits'](fixture, seat, row['frame'], mirrored), (name, row['frame'], seat)
        yield name, case


def char_at(phases, frame, seat=0, phase='after-collision'):
    return phases[phase][frame]['chars'][seat]


def starts(case, states, seat=1):
    return [row for row in case[1] if row['seat'] == seat and row['to'] in states]


def verify_case(name, case):
    phases, changes, _ = case
    assert phases['rng-draw'] and phases['rng-draw'][0]['before'] == 1337, name
    assert all(row['after'] == row['before'] * 16807 % STAGE17['MODULUS'] for row in phases['rng-draw']), name
    for row in phases['hitdef']:
        source = row['chars'][row['hitDefOwner'] - 1]
        hit = source['hitDef']
        if source['state'] not in MOVES or hit['attr'] == 0:
            continue
        variables = source['vars']
        base = 6 if variables['var6'] else 5
        damage = math.floor(STAGE17['f32']((base - variables['var21']) * STAGE17['f32'](variables['fvar7'])))
        assert hit['damage'] == damage and hit['guardDamage'] == 0, (name, row['frame'])
        assert hit['groundVel'][:2] == [-6.5 if variables['var40'] else -6, 0], (name, row['frame'])
        assert hit['hitTime'] == 9, (name, row['frame'])
    for row in changes:
        source, target, tick = row['from'], row['to'], row['stateTime']
        if target in MOVES:
            assert (target == 235) == (row['bodyDistX'] <= 28), (name, row)
            assert not row['raw'] & 2 and (row['aBuffer'] or row['helperA'] > 0), (name, row)
        if source not in MOVES:
            continue
        natural = sum(MOVES[source])
        if target in (1000, 1050):
            expected = (0, 1) if tick == natural else (1, 0)
            assert (tick == natural or source == 235 and 5 <= tick <= 8) and row['hitPause'] == 0, (name, row)
            assert (row['var1'], row['var2']) == expected and (row['powerWaveCommand'] or row['helperWave'] > 0), (name, row)
        if target in (200, 205, 230, 235, 400, 430):
            assert tick == natural or source == 235 and 9 <= tick <= 15, (name, row)


def measure(case, state, *, blocked=False, crouching=False, seat=0):
    phases, _, contacts = case
    target = 1 - seat
    started, = starts(case, MOVES, seat + 1)
    frame, target_pn, damage, guarded = contacts[0]
    assert (frame, target_pn, guarded) == (started['frame'] + MOVES[state][0], target + 1, blocked)
    assert damage == 0 if blocked else damage > 0
    source_hit = char_at(phases, frame, seat)
    target_hit = char_at(phases, frame, target)
    pause = 11 if blocked else 13
    assert (target_hit['hitShake'], target_hit['hitFall'], target_hit['hitId']) == (pause, False, 10)
    push = abs(target_hit['hitVel'][0])
    assert push == 6 and target_hit['hitVel'][1] == 0
    attacker_pause = char_at(phases, frame + 1, seat)['hitPause']
    assert attacker_pause == 11
    assert source_hit['anim'] == state
    first_move = frame + pause + 1
    restored = next(f for f in range(frame + 1, len(phases['end'])) if char_at(phases, f, target)['ctrl'])
    effective = restored - frame - pause
    result = {'damage': damage, 'initialPush': push, 'effectiveStun': effective, 'contactFrame': frame,
              'restoredFrame': restored, 'pause': {'attackerFrames': attacker_pause, 'defenderFrames': pause}}
    if blocked:
        expected = 151 if crouching else 150
        assert all(char_at(phases, f, target)['anim'] == expected for f in range(frame + 1, restored))
        return result, None
    initial, slide = (5020, 5025) if crouching else (5010, 5015)
    switch = next(f for f in range(frame + 1, restored) if char_at(phases, f, target)['anim'] == slide)
    assert [char_at(phases, f, target)['anim'] for f in range(frame + 1, restored)] == (
        [initial] * (switch - frame - 1) + [slide] * (restored - switch))
    return result, {'initialTicks': switch - first_move + 1, 'effectiveStun': effective,
                    'initialAction': initial, 'slideAction': slide}


def verify_cases(cases):
    for name, case in cases.items():
        verify_case(name, case)
    moves = {}
    for label, state in (('near', 235), ('far', 230)):
        names = [PREFIX + f'{label}-{kind}' for kind in ('hit', 'guard', 'crouch-hit', 'crouch-guard')]
        if not all(name in cases for name in names):
            continue
        hit, standing = measure(cases[names[0]], state)
        guard, _ = measure(cases[names[1]], state, blocked=True)
        crouch_hit, crouching = measure(cases[names[2]], state, crouching=True)
        crouch_guard, _ = measure(cases[names[3]], state, blocked=True, crouching=True)
        assert crouch_guard == guard and crouch_hit['effectiveStun'] == hit['effectiveStun']
        moves[state] = {'hit': hit, 'guard': guard, 'groundReactions': {'standing': standing, 'crouching': crouching}}
    routes = verify_routes(cases)
    body_verified = set(moves) == MOVES.keys()
    return {'moves': moves, 'bodyVerified': body_verified, **routes,
            'completeVerified': body_verified and routes['boundariesVerified'] and routes['chainsVerified'] and
            routes['waveVerified'] and routes['forwardReserveVerified'] and routes['risingReserveVerified'] and
            routes['splitRootDirectionsVerified'] and routes['groundRecoveryReserveVerified'] and routes['farGuardControlVerified'] and
            routes['groundedCloseJabWaveVerified'] and
            routes['guardFollowupVerified'] == [235] and set(routes['retainHelperTargets']) == {200, 205, 400}}


def verify_whiff(case, state):
    phases, changes, contacts = case
    assert not contacts
    entry, = starts(case, {state})
    at = entry['frame']
    exit_row, = [r for r in changes if r['from'] == state and r['seat'] == 1]
    assert (exit_row['frame'], exit_row['stateTime'], exit_row['to'], exit_row['var2']) == (
        at + sum(MOVES[state]), sum(MOVES[state]), 0, 1)
    for index, (group, sprite, duration, hurt) in enumerate(ANIMATION[state]):
        for frame in range(at, at + duration):
            char = char_at(phases, frame)
            box = char['frame']
            assert (char['state'], char['anim'], char['elem'], char['moveType']) == (
                state, state, index, 131072 if index < 3 else 32768)
            assert (box['sprite'], box['duration'], box['offset'], box['clsn2'], box['clsn1'] or []) == (
                [group, sprite], duration, [0, 0], hurt, ATTACK_BOXES[state] if index == 2 else [])
        at += duration


def verify_routes(cases):
    def fixture(name):
        return cases.get(name if name.startswith('mode2-') else PREFIX + name)

    def route(name, source, target, frame, tick, *, seat=1, permissions=None):
        case = fixture(name)
        if case is None:
            return False
        rows = [r for r in starts(case, {target}, seat) if r['from'] == source and r['frame'] == frame]
        assert len(rows) == 1 and rows[0]['stateTime'] == tick, (name, rows)
        if permissions is not None:
            assert (rows[0]['var1'], rows[0]['var2']) == permissions, (name, rows[0])
        assert rows[0]['hitPause'] == 0, (name, rows[0])
        return True

    chain_checks = [
        route('buffer-cross-toward', 430, 235, 11, 6),
        route('buffer-cross-away', 430, 230, 11, 6),
        route('near-self-whiff-8', 235, 235, 24, 9),
        route('near-self-whiff-15', 235, 235, 30, 15),
        route('near-self-whiff-16', 235, 235, 31, 16, permissions=(0, 1)),
        route('near-continuation-a-x', 235, 205, 24, 9),
        route('near-continuation-d-a-x', 235, 400, 24, 9),
        route('near-continuation-d-a-y', 235, 430, 24, 9),
        route('near-to-jab-wave-clean-held-58', 235, 200, 25, 9),
        route('mode2-standing-light-kick-near-self-whiff-9', 235, 235, 24, 9),
        route('far-jab-to-stand-kick-hit-first', 200, 230, 23, 7),
        route('far-jab-to-stand-kick-hit-last', 200, 230, 27, 11),
        route('near-jab-to-stand-kick-hit-first', 205, 235, 23, 7),
        route('near-jab-to-stand-kick-hit-last', 205, 230, 27, 11),
        route('crouch-jab-to-stand-kick-hit-first', 400, 235, 22, 6),
        route('crouch-jab-to-stand-kick-hit-last', 400, 230, 28, 12),
        route('crouch-kick-to-stand-kick-hit-first', 430, 235, 22, 6),
        route('crouch-kick-to-stand-kick-hit-last', 430, 230, 29, 13),
        route('near-to-far-ground-guard-44-25', 235, 230, 25, 9),
        route('near-to-far-ground-hit-34-31', 235, 230, 31, 15),
    ]
    wave_checks = [
        route('near-wave-whiff-9', 235, 1000, 20, 5, permissions=(1, 0)),
        route('near-wave-whiff-10', 235, 1000, 20, 5, permissions=(1, 0)),
        route('near-wave-whiff-13', 235, 1000, 23, 8, permissions=(1, 0)),
        route('near-wave-whiff-14', 235, 205, 24, 9, permissions=(0, 0)),
        route('near-wave-mixed-natural', 235, 1000, 31, 16, permissions=(0, 1)),
        route('far-wave-mixed-natural', 230, 1000, 24, 19, permissions=(0, 1)),
        route('mode2-standing-light-kick-near-wave-mode2-10', 235, 1050, 20, 5, permissions=(1, 0)),
        route('mode2-standing-light-kick-far-wave-mode2-natural', 230, 1050, 24, 19, permissions=(0, 1)),
    ]
    far_reject = fixture('far-wave-whiff-13')
    if far_reject is not None:
        assert not starts(far_reject, {1000, 1050}), '230 unexpectedly accepted an early Wave'
    forward_checks = [
        route('near-forward-x-hit-20', 235, 300, 21, 5),
        route('near-forward-x-hit-21', 235, 300, 21, 5),
        route('near-forward-x-hit-24', 235, 300, 24, 8),
        route('near-forward-x-hit-25', 235, 200, 25, 9),
    ]
    rising_checks = [
        route('near-whiff-forward-down-y-before', 235, 310, 20, 5),
        route('near-whiff-forward-down-y-first', 235, 310, 20, 5),
        route('near-whiff-forward-down-y-last', 235, 310, 23, 8),
    ]
    rising_reject = fixture('near-whiff-forward-down-y-after')
    if rising_reject is not None:
        assert not starts(rising_reject, {310}), '235 unexpectedly entered 310 after the window'

    split_checks = []
    for name, target, raw in (('button-first', 300, 8), ('direction-first', 300, 128),
                              ('button-neutral-first', 310, 10), ('diagonal-first', 310, 256)):
        name = f'near-frozen-direction-split-{name}'
        valid = route(name, 235, target, 21, 5, permissions=(1, 0))
        if valid:
            row, = starts(fixture(name), {target})
            assert row['raw'] == raw and row['helperForwardX'] == 0
            assert row['xBuffer' if target == 300 else 'yBuffer']
        split_checks.append(valid)

    retain_helper = []
    for target, pattern, start, wave in ((200, 'near-to-jab-wave-clean-{}-58', 25, 29),
                                         (205, 'near-to-close-jab-wave-apex-early-{}', 17, 21),
                                         (400, 'near-to-crouch-jab-wave-free-{}', 24, 28)):
        held, released = fixture(pattern.format('held')), fixture(pattern.format('release'))
        if held is not None and released is not None:
            assert route(pattern.format('held'), 235, target, start, 9)
            assert route(pattern.format('held'), target, 1000, wave, 4)
            assert route(pattern.format('release'), 235, target, start, 9)
            assert not starts(released, {1000})
            retain_helper.append(target)

    grounded_close = []
    for label in ('held', 'release'):
        name = f'near-to-close-jab-wave-grounded-{label}'
        case = fixture(name)
        if case is None:
            continue
        assert route(name, 235, 205, 95, 9)
        assert all(char_at(case[0], frame, 1)['stateType'] == 8 and char_at(case[0], frame, 1)['pos'][1] == 0
                   for frame in range(86, 100)), name
        if label == 'held':
            assert route(name, 205, 1000, 99, 4, permissions=(1, 0))
            assert case[2] == [(15, 2, 115, False), (115, 2, 92, False)], case[2]
        else:
            assert not starts(case, {1000})
            assert case[2] == [(15, 2, 115, False)], case[2]
        grounded_close.append(label)

    guard_followup = []
    for state, pattern, frame in ((235, 'near-reenter-guard-release-{}-23', 29),):
        if not all(fixture(pattern.format(direction)) for direction in ('back', 'down')):
            continue
        for direction, animation in (('back', 151), ('down', 150)):
            case = fixture(pattern.format(direction))
            assert route(pattern.format(direction), 235, state, 25, 9)
            assert case[2] == [(9, 2, 0, True), (frame, 2, 0, True)], (state, direction, case[2])
            assert char_at(case[0], frame + 1, 1)['anim'] == animation, (state, direction)
        guard_followup.append(state)

    far_guard_controls = []
    for direction, blocked in (('back', False), ('down', True), ('both', True)):
        name = f'near-to-far-guard-release-{direction}-40.25'
        case = fixture(name)
        if case is not None:
            assert route(name, 235, 230, 25, 9)
            assert case[2] == [(9, 2, 0, True), (31, 2, 0 if blocked else 30, blocked)], (name, case[2])
            assert char_at(case[0], 30, 1)['ctrl'], name
            far_guard_controls.append(direction)

    ground_recovery = fixture('fresh-boundary-a-x')
    far_no_chain = fixture('far-no-chain-13')
    if ground_recovery is not None:
        recovery, = starts(ground_recovery, {720})
        assert recovery['frame'] == 5 and recovery['recoveryBuffer'] and not recovery['raw'] & 2
        assert not starts(ground_recovery, MOVES)
    if far_no_chain is not None:
        assert not starts(far_no_chain, {720}), '230 unexpectedly entered grounded recovery early'
    dodge_checks = [route(f'{label}-dodge-recovery-edge', state, 720, frame, tick, permissions=(0, 1))
                    for label, state, frame, tick in (('near', 235, 31, 16), ('far', 230, 24, 19))]
    for label, state, frame, tick in (('near', 235, 31, 16), ('far', 230, 24, 19)):
        dodge_checks.append(route(f'{label}-dodge-recovery-down-edge', state, 400, frame, tick))

    boundaries = []
    for label, state in (('near', 235), ('far', 230)):
        whiff = fixture(f'{label}-whiff')
        if whiff is not None:
            verify_whiff(whiff, state)
        boundaries.append(whiff is not None)
        for prefix, seat, mode in (('mirror-', 1, ''), ('p2-', 2, ''), ('', 1, 'mode2-standing-light-kick-')):
            case = fixture(f'{mode}{prefix}{label}-hit')
            if case is not None:
                measure(case, state, seat=seat - 1)
            boundaries.append(case is not None)
        interrupted = fixture(f'{label}-interrupt')
        if interrupted is not None:
            assert interrupted[2] == [(6, 1, 46, False)], (label, interrupted[2])
        boundaries.append(interrupted is not None)
    for label, distance, state in (('below', 27.5, 235), ('exact', 28, 235), ('above', 28.5, 230)):
        for family, frame in (('threshold', 5), ('air-body-threshold', 15)):
            case = fixture(f'{family}-{label}')
            if case is not None:
                row, = starts(case, MOVES)
                assert (row['frame'], row['to'], row['bodyDistX']) == (frame, state, distance), (family, label, row)
            boundaries.append(case is not None)
    for label, state, at_edge, at_commit in (('toward', 235, 28.5, 25), ('away', 230, 27.5, 31)):
        case = fixture(f'buffer-cross-{label}')
        if case is not None:
            assert char_at(case[0], 10, phase='before-command')['bodyDistX'] == at_edge
            first, second = starts(case, {430, *MOVES})
            assert (first['to'], second['to'], second['bodyDistX'], second['helperA'], second['aBuffer']) == (
                430, state, at_commit, 2, False), (label, second)
        boundaries.append(case is not None)
    sweep = fixture('sweep-buffer-ground-cross')
    if sweep is not None:
        phases, _, _ = sweep
        row, = starts(sweep, MOVES)
        assert (row['frame'], row['from'], row['to'], row['stateTime'], row['bodyDistX'], row['helperA']) == (
            45, 440, 235, 40, 27.886719, 2), row
        assert char_at(phases, 44, phase='before-command')['bodyDistX'] == 31.054688
        assert [char_at(phases, frame, phase='before-command')['helperB'] for frame in (46, 47, 48)] == [2, 1, 0]
        assert len(starts(sweep, {440})) == 1
    boundaries.append(sweep is not None)
    for name, seat, frame in (('recovery-backstep-distance', 1, 33), ('recovery-backstep-own-kick', 2, 32)):
        case = fixture(name)
        if case is not None:
            row, = starts(case, MOVES, seat)
            assert (row['frame'], row['to'], row['bodyDistX']) == (frame, 235, 26.065239), (name, row)
        boundaries.append(case is not None)

    return {'chainsVerified': all(chain_checks), 'waveVerified': all(wave_checks) and far_reject is not None,
            'forwardReserveVerified': all(forward_checks),
            'risingReserveVerified': all(rising_checks) and rising_reject is not None,
            'splitRootDirectionsVerified': all(split_checks),
            'groundRecoveryReserveVerified': ground_recovery is not None and far_no_chain is not None and all(dodge_checks),
            'guardFollowupVerified': guard_followup, 'retainHelperTargets': retain_helper,
            'groundedCloseJabWaveVerified': len(grounded_close) == 2,
            'farGuardControlVerified': len(far_guard_controls) == 3, 'boundariesVerified': all(boundaries)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directories', nargs='+', type=Path)
    options = parser.parse_args()
    cases = {}
    for directory in options.directories:
        for name, case in read_cases(directory):
            assert name not in cases, name
            cases[name] = case
    print(json.dumps(verify_cases(cases), indent=2))
    print(f'PASS: {len(cases)} native Stage 26 fixtures')


if __name__ == '__main__':
    main()
