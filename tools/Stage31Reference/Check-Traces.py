"""Verify signed IKEMEN GO evidence for Terry's default weak Rising Tackle."""

import argparse
from contextlib import redirect_stdout
import io
import json
import math
from pathlib import Path
import runpy


HERE = Path(__file__).resolve().parent
STAGE30 = runpy.run_path(str(HERE.parent / 'Stage30Reference/Check-Traces.py'))
STAGE29 = STAGE30['STAGE29']
STAGE27 = STAGE30['STAGE27']
STAGE20 = STAGE30['STAGE20']
FIXTURES = json.loads((HERE / 'Cases.json').read_bytes())
PREFIX = 'default-standing-heavy-kick-1200-'
MODE2 = 'mode2-standing-heavy-kick-1200-'
AUDIT_ENGINE_SHA = 'cc101cd82e6cb3c31b37b15ae7ba1104e5758fe835d6ce6c31da4bb4c62a7c9b'
AUDIT_FIELDS = ('rootRisingTackle', 'helperRisingTackle', 'moveReversed', 'reversalAttr')
ENTRY_DURATIONS = (2, 3, 4, 1, 2, 2, 2, 2, 2, 1, 5, 6, 9)
LAND_DURATIONS = (2, 12)
SPRITES = (0, 1, 2, 3, 4, 5, 6, 3, 4, 5, 7, 8, 9)
STEPS = {0: 8, 2: 8, 5: 8}
ATTACK_GROUPS = ((5, 8), (9, 9), (12, 13), (16, 17), (20, 20))
REVERSE_BOX = [[20, -72, 51, -25]]
GROUND_BOX = [[12, -52, 59, -21]]
AIR_BOX = [[-4, -116, 35, -29]]
CLSN1 = (None, REVERSE_BOX, GROUND_BOX, AIR_BOX, None, AIR_BOX, None, AIR_BOX, None, AIR_BOX, None, None, None)
AIR_HURT = [[-16, -108, 15, -13]]
CLSN2 = ([[-16, -80, 15, -1]], [[-12, -56, 27, -1]], [[-12, -76, 27, -29]],
         AIR_HURT, AIR_HURT, AIR_HURT, AIR_HURT, AIR_HURT, AIR_HURT, AIR_HURT,
         [[-20, -84, 19, -21]], [[-20, -84, 19, -21]], [[-20, -100, 19, -37]])
BODY_KEYS = {PREFIX + name for name in ('whiff', 'hit', 'guard', 'crouch-guard', 'air', 'seat2', 'trade')}
BODY_KEYS.add('default-standing-heavy-kick-mirror-1200-hit')
char_at = STAGE30['char_at']
starts = STAGE30['starts']
assert_start = STAGE30['assert_start']
f32 = STAGE27['STAGE17']['f32']


def read_cases(directory, *, audit=False):
    results = directory if (directory / 'run-metadata.json').is_file() else directory / 'results'
    run = json.loads((results / 'run-metadata.json').read_bytes())
    engine_sha = run['executable_sha256']
    if engine_sha not in (AUDIT_ENGINE_SHA, STAGE29['ENGINE_SHA']):
        raise ValueError(f'Unpinned IKEMEN executable {engine_sha}: {results}')
    audit = engine_sha == AUDIT_ENGINE_SHA
    scripts = run['source_sha256']
    assert scripts['Terry-KOF98.def'] == STAGE30['DEF_SHA'], results
    assert scripts['common_kof.cns'] == '3b934b5390e0b805df549db35601b4882d6c28b9165b56e3dcff72d3bd86c65b', results
    with redirect_stdout(io.StringIO()):
        samples = list(STAGE20['read_cases'](results, capture_profile='Stage31',
                                            extra_phases=('hitdef', 'rng-draw', 'var21-draw', 'reversal')))
    for name, case in samples:
        sample = json.loads((results / f'{name}-metadata.json').read_bytes())
        fixture = FIXTURES[name]
        assert sample['physical_inputs'] == fixture, name
        for row in case[0]['before-command']:
            for seat, char in enumerate(row['chars']):
                want = STAGE27['STAGE25']['physical_bits'](fixture, seat, row['frame'], '-mirror-' in name)
                assert char['raw'] == want, (name, row['frame'], seat, char['raw'], want)
        yield name, case


def check_contact(case, frame, source, *, guarded=False, facing=1):
    target = 3 - source
    contact, = [row for row in case[2] if row[:2] == (frame, target)]
    attack = char_at(case[0], frame, source - 1)
    hit = char_at(case[0], frame, target - 1)
    info = attack['hitDef']
    variables = attack['vars']
    ground = attack['elem'] == 2
    assert attack['state'] == 1200 and attack['elem'] in (2, 3, 5, 7, 9), attack
    assert (info['attr'], info['guardFlag'], info['groundVel'], info['guardVel']) == (
        513 if ground else 516, 3 if ground else 7, [-2, -10, 0], [-10.5, 0, 0]), info
    assert (info['hitPause'], info['guardPause']) == (
        ([11, 13], [11, 11]) if ground else ([3, 5], [3, 3])), info
    assert (info['hitTime'], info['guardHitTime'], info['guardCtrlTime']) == (17, 17, 17), info
    raw = math.floor(f32(((7 if variables['var6'] else 4) - variables['var21']) * f32(variables['fvar7'])))
    assert (info['damage'], info['guardDamage'], variables['var40']) == (raw, math.floor(variables['fvar7']), 0), info
    base_damage = info['guardDamage'] if guarded else raw
    scaled = base_damage * f32(attack['attackMul']) * attack['attackBase'] / 100 / hit['finalDefense']
    expected_damage = 0 if guarded and base_damage == 0 else math.floor(max(1, scaled) + 0.5)
    assert contact[2:] == (expected_damage, guarded), (
        contact, expected_damage, base_damage, attack['attackMul'], attack['attackBase'], hit['finalDefense'])
    assert (hit['hitId'], hit['hitOwner']) == (50, source), hit
    if guarded and hit['stateType'] in (1, 2):
        assert hit['hitVel'] == [10.5 * facing, 0] and not hit['hitFall'], hit
    elif not guarded:
        assert hit['hitVel'] == [2 * facing, -10] and hit['hitFall'], hit
        assert (hit['fallRecover'], hit['fallRecoverTime']) == (True, 600), hit
    return contact


def expect_complete_move(case, first, land, end, seat=1):
    assert any(r['frame'] == first for r in starts(case, {1200}, seat))
    assert_start(case, 1205, land, 1200, 41, seat=seat)
    exit_row = assert_start(case, 0, end, 1205, 14, seat=seat)
    assert exit_row['var2'] == 2, exit_row
    fighter = char_at(case[0], land, seat - 1)
    assert (fighter['state'], fighter['anim'], fighter['stateType'], fighter['ctrl'], fighter['pos'][1], fighter['vel']) == (
        1205, 1205, 1, False, 0, [0, 0, 0]), fighter
    assert char_at(case[0], end, seat - 1, 'end')['ctrl']


def verify_body(cases):
    if not BODY_KEYS <= cases.keys():
        return False
    whiff = cases[PREFIX + 'whiff']
    assert not whiff[2]
    expect_complete_move(whiff, 5, 46, 60)
    position_x = char_at(whiff[0], 5, phase='after-command')['pos'][0]
    position_y, velocity_x, velocity_y = 0, 0, 0
    tick = 0
    for element, duration in enumerate(ENTRY_DURATIONS):
        for elem_time in range(duration):
            frame = tick + 5
            fighter = char_at(whiff[0], frame)
            pose = fighter['frame']
            assert (fighter['state'], fighter['anim'], fighter['time'], fighter['elem'], fighter['elemTime']) == (
                1200, 1200, tick + 1, element, elem_time), (frame, fighter)
            assert (pose['sprite'], pose['duration'], pose['offset'], pose['clsn1'], pose['clsn2']) == (
                [1200, SPRITES[element]], duration, [0, 0], CLSN1[element], CLSN2[element]), (frame, pose)
            assert (fighter['stateType'], fighter['moveType'], fighter['ctrl']) == (
                1 if tick < 9 else 4, 131072 if tick < 21 else 32768, False), (frame, fighter)
            position_x = f32(position_x + STEPS.get(tick, 0))
            if tick == 9:
                velocity_x, velocity_y = 1, -8
            elif tick > 9:
                velocity_x, velocity_y = f32(velocity_x * f32(.9)), velocity_y + .5
            position_x, position_y = f32(position_x + velocity_x), position_y + velocity_y
            assert abs(fighter['pos'][0] - position_x) < .00002 and fighter['pos'][1] == position_y, (frame, fighter)
            assert abs(fighter['vel'][0] - velocity_x) < .000002 and fighter['vel'][1] == velocity_y, (frame, fighter)
            tick += 1
    assert tick == 41
    for tick in range(14):
        fighter = char_at(whiff[0], 46 + tick)
        element = int(tick >= 2)
        pose = fighter['frame']
        assert (fighter['state'], fighter['time'], fighter['elem'], fighter['elemTime'], fighter['ctrl']) == (
            1205, tick + 1, element, tick - 2 * element, False), fighter
        assert (pose['sprite'], pose['duration'], pose['offset'], pose['clsn1'], pose['clsn2']) == (
            [10, element], LAND_DURATIONS[element], [0, 0], None,
            [[-20, -80, 19, -1], [-8, -92, 15, -69]] if element == 0 else [[-20, -64, 19, -1], [-8, -76, 15, -53]]), pose
        assert fighter['pos'][1] == 0 and fighter['vel'] == [0, 0, 0], fighter
    for name, seat, facing in ((PREFIX + 'hit', 1, 1), (PREFIX + 'seat2', 2, -1),
                                ('default-standing-heavy-kick-mirror-1200-hit', 1, -1)):
        case = cases[name]
        assert [(r[0], r[1]) for r in case[2]] == [(f, 3 - seat) for f in (10, 25, 31, 38, 45)], name
        for frame, damage in zip((10, 25, 31, 38, 45), (22, 22, 15, 22, 29), strict=True):
            assert check_contact(case, frame, seat, facing=facing)[2] == damage
        expect_complete_move(case, 5, 69, 83, seat=seat)
        assert_start(case, 0, 153, 5120, 27, seat=3 - seat)
    for name, stance in (('guard', 151), ('crouch-guard', 153)):
        case = cases[PREFIX + name]
        assert case[2] == [(10, 2, 7, True)], case[2]
        check_contact(case, 10, 1, guarded=True)
        assert char_at(case[0], 22, 1, 'end')['state'] == stance
        expect_complete_move(case, 5, 57, 71)
    air = cases[PREFIX + 'air']
    assert [r[0] for r in air[2]] == [14, 20, 27, 34], air[2]
    for frame in (14, 20, 27, 34):
        check_contact(air, frame, 1)
    expect_complete_move(air, 5, 58, 72)
    trade = cases[PREFIX + 'trade']
    assert trade[2] == [(10, 1, 46, False), (10, 2, 46, False)], trade[2]
    assert check_contact(trade, 10, 1)[2] == check_contact(trade, 10, 2, facing=-1)[2]
    return True


def verify_cases(cases):
    body = verify_body(cases)
    def has(*suffixes):
        return all(PREFIX + suffix in cases for suffix in suffixes)

    def check_entry(suffix, frame, source, age, permission, seat=1):
        case = cases[PREFIX + suffix]
        row = assert_start(case, 1200, frame, source, age, seat=seat)
        assert (row['var1'], row['var2']) == permission, (suffix, row)
        return row

    def no_entry(suffix, seat=1):
        assert not starts(cases[PREFIX + suffix], {1200}, seat), suffix

    melee = has('reversal-310', 'reversal-miss')
    if melee:
        turned = cases[PREFIX + 'reversal-310']
        assert [(row['frame'], row['sourceState'], row['targetState'], row['incomingAttr'])
                for row in turned[0]['reversal']] == [(8, 1200, 310, 65)]
        after = char_at(turned[0], 8, 1, 'end')
        assert (after['state'], after['moveReversed'], after['moveContact'], after['hitPause']) == (310, 1, 0, 11)
        assert all(row[0] != 8 for row in turned[2])
        assert char_at(turned[0], 8, 0, 'end')['hitPause'] == 11
        miss = cases[PREFIX + 'reversal-miss']
        assert char_at(miss[0], 8, 0, 'end')['state'] == 5000 and miss[2][0][:2] == (8, 1)

    contact = has('contact-310-hit', 'contact-310-guard', 'contact-310-whiff', 'reversal-cancel',
                  '310-hit-input-8', '310-hit-input-9', '310-hit-input-21',
                  '310-freeze-held-0', '310-freeze-held-1')
    if contact:
        for suffix in ('contact-310-hit', 'contact-310-guard', '310-hit-input-9',
                       '310-freeze-held-0', '310-freeze-held-1'):
            assert check_entry(suffix, 20, 310, 4, (2, 0))['moveContact'] == 1, suffix
        reversed_entry = check_entry('reversal-cancel', 20, 310, 4, (2, 0))
        assert (reversed_entry['moveReversed'], reversed_entry['moveContact']) == (1, 0)
        reversal_case = cases[PREFIX + 'reversal-cancel']
        for frame in range(8, 20):
            char = char_at(reversal_case[0], frame, 0, 'end')
            assert (char['moveReversed'], char['moveContact']) == (1, 0), (frame, char)
        for suffix in ('contact-310-whiff', '310-hit-input-8', '310-hit-input-21'):
            no_entry(suffix)

    projectile = has('projectile-140-20', 'projectile-100-21')
    if projectile:
        success = cases[PREFIX + 'projectile-140-20']
        assert [(row['frame'], row['sourceState'], row['targetState'], row['incomingAttr'])
                for row in success[0]['reversal']] == [(22, 1200, 1020, 2049)]
        assert char_at(success[0], 22, 0, 'end')['hitPause'] == 11
        assert char_at(success[0], 22, 1, 'end')['state'] == 1000
        assert char_at(success[0], 22, 1, 'end')['hitPause'] == 0
        assert all(row[:2] != (22, 1) for row in success[2])
        failure = cases[PREFIX + 'projectile-100-21']
        assert not failure[0]['reversal']
        assert char_at(failure[0], 23, 0, 'end')['state'] == 5000
        assert failure[2][0][:2] == (23, 1)

    priority = has('priority-20', 'priority-21') and MODE2 + 'priority-20' in cases
    if priority:
        check_entry('priority-20', 20, 0, 0, (0, 0))
        no_entry('priority-21')
        assert_start(cases[PREFIX + 'priority-21'], 1000, 21, 0, 0)
        assert_start(cases[MODE2 + 'priority-20'], 1050, 20, 0, 0)
        assert not starts(cases[MODE2 + 'priority-20'], {1200, 1000}, 1)

    natural = has('310-natural-35', '310-natural-37', '310-natural-38', '310-natural-39',
                  '1205-helper-boundary-56', '1205-helper-boundary-57', 'held')
    if natural:
        for suffix in ('310-natural-35', '310-natural-37', '310-natural-38'):
            check_entry(suffix, 38, 310, 33, (0, 2))
        check_entry('310-natural-39', 39, 0, 1, (0, 0))
        check_entry('1205-helper-boundary-57', 60, 1205, 14, (0, 2))
        for suffix in ('held', '1205-helper-boundary-56'):
            assert [row['frame'] for row in starts(cases[PREFIX + suffix], {1200}, 1)] == [5], suffix
            expect_complete_move(cases[PREFIX + suffix], 5, 46, 60)

    normals = has('200-window-8', '200-window-9', '200-window-11', '200-window-12',
                  '205-window-20', '205-window-22', '205-window-23')
    if normals:
        for suffix, frame, source, age in (('200-window-8', 9, 200, 4), ('200-window-9', 9, 200, 4),
                                           ('200-window-11', 11, 200, 6), ('205-window-20', 20, 205, 4),
                                           ('205-window-22', 22, 205, 6)):
            check_entry(suffix, frame, source, age, (1, 0))
        for suffix in ('200-window-12', '205-window-23'):
            no_entry(suffix)

    return {'bodyVerified': body, 'completeVerified': all((body, melee, contact, projectile,
                                                           priority, natural, normals)),
            'reversal310Verified': melee, 'reversal310CancelVerified': contact,
            'projectileReversalVerified': projectile, 'wavePriorityVerified': priority,
            'naturalRecoveryVerified': natural, 'normalCancelVerified': normals, 'cases': len(cases),
            'move1200': {'durations': list(ENTRY_DURATIONS), 'landingDurations': list(LAND_DURATIONS),
                         'positionSteps': [{'tick': tick, 'x': x} for tick, x in STEPS.items()],
                         'invulnerableFrames': [0, 1], 'reversalFrames': [2, 4], 'reversalPause': [11, 11],
                         'attackGroups': [list(group) for group in ATTACK_GROUPS],
                         'groundPause': [11, 13], 'groundGuardPause': [11, 11],
                         'airPause': [3, 5], 'airGuardPause': [3, 3],
                         'airMotion': {'start': 9, 'x': 1, 'y': -8, 'gravity': .5, 'xMul': .9},
                         'nextState': 1205, 'recoveryState': 0, 'launchX': 2, 'launchY': 10,
                         'hitId': 50, 'guardPush': 10.5, 'guardTicks': 17}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directories', type=Path, nargs='+')
    options = parser.parse_args()
    cases = {}
    for directory in sorted(options.directories, key=reference_priority):
        for name, case in read_cases(directory):
            cases[name] = case
    result = verify_cases(cases)
    print(json.dumps(result, indent=2))
    if not result['completeVerified']:
        parser.exit(1, 'Missing Stage 31 reference controls\n')


def reference_priority(directory):
    path = directory / 'run-metadata.json'
    if not path.is_file():
        path = directory / 'results/run-metadata.json'
    return json.loads(path.read_bytes())['executable_sha256'] == AUDIT_ENGINE_SHA


if __name__ == '__main__':
    main()
