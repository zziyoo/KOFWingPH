"""Verify original State 440 input, trip recovery, and Power Wave transitions."""

import argparse
import json
from pathlib import Path
import runpy


TOOLS = Path(__file__).resolve().parents[1]
stage20 = runpy.run_path(str(TOOLS / 'Stage20Reference/Check-Traces.py'))
stage23 = runpy.run_path(str(TOOLS / 'Stage23Reference/Check-Traces.py'))
PREFIX = 'default-crouch-kick-wave-'
MODE2 = 'mode2-crouch-kick-wave-'
BODY = {'hit-0', 'crouch-hit-0', 'guard-0', 'crouch-guard-0', 'mirror-hit-0', 'whiff-0', 'interrupt-0',
        'tech-55', 'tech-56', 'tech-57'}
PHASES = ('hitdef', 'rng-draw', 'var21-draw')
PRIORITIES = {'down-light-kicks-0': (50, 430), 'down-light-and-kick-0': (162, 400),
              'down-heavy-and-kick-0': (290, 410), 'down-forward-heavy-and-kick-0': (298, 310)}


def read_cases(directory):
    yield from stage20['read_cases'](directory, capture_profile='Stage24', extra_phases=PHASES)


def compressed_target(case, target=1):
    phases, _, _ = case
    result = []
    for row in phases['end']:
        char = row['chars'][target]
        marker = (char['state'], char['anim'], char['ctrl'])
        if not result or result[-1][1:] != marker:
            result.append((row['frame'], *marker))
    return result


def c_at(phases, frame, seat=1, phase='end'):
    return phases[phase][frame]['chars'][seat]


def body_measure(case, *, blocked, mirrored=False):
    phases, changes, contacts = case
    (frame, seat, damage, guard), = contacts
    assert (frame, seat, guard) == (15, 2, blocked), contacts
    entry, exit_ = [r for r in changes if r['seat'] == 1 and (r['to'] == 440 or r['from'] == 440)]
    assert (entry['frame'], entry['raw'], entry['to'], exit_['frame'], exit_['to'], exit_['stateTime']) == (5, 34, 440, 56, 11, 40)
    attack = [r['chars'][0]['hitDef'] for r in phases['hitdef']
              if r['hitDefOwner'] == 1 and r['chars'][0]['state'] == 440 and r['chars'][0]['hitDef']['attr']]
    assert attack and all(row['guardFlag'] == 2 for row in attack), attack
    assert [c_at(phases, f, 0, 'after-collision')['hitPause'] for f in range(16, 27)] == list(range(11, 0, -1))
    target = c_at(phases, frame, phase='after-collision')
    sign = -1 if mirrored else 1
    assert (target['hitId'], target['hitOwner'], target['hitPlayerId']) == (20, 1, c_at(phases, frame, 0)['id'])
    pause = 11 if blocked else 13
    assert target['hitShake'] == pause
    restored = next(f for f in range(frame + 1, len(phases['end'])) if c_at(phases, f)['ctrl'])
    initial = frame + pause + 1
    push = (c_at(phases, initial)['pos'][0] - c_at(phases, initial - 1)['pos'][0]) * sign
    stats = {'damage': damage, 'initialPush': push, 'contactFrame': frame, 'restoredFrame': restored,
             'pause': {'attackerFrames': 11, 'defenderFrames': pause}}
    if blocked:
        assert (damage, restored, push) == (0, 44, 10.5)
        assert not target['hitFall'] and abs(c_at(phases, restored)['pos'][0] - 83.33462) < 0.0001
        stats['effectiveStun'] = restored - frame - pause
        return stats
    assert (damage, restored, push) == (115, 112, 2)
    assert (target['hitVel'][:2], target['hitFall'], target['fallRecover'], target['fallRecoverTime']) == ([2 * sign, -8], True, True, 600)
    phases_expected = [(16, 5070, 5070, False), (29, 5071, 5070, False), (60, 5100, 5100, False),
                       (64, 5101, 5160, False), (74, 5110, 5170, False), (77, 5110, 5110, False),
                       (87, 5120, 5120, False), (112, 0, 0, True)]
    assert [row for row in compressed_target(case) if row[0] >= 16] == phases_expected
    for f in range(29, 60):
        n = f - 28
        x, y = c_at(phases, f)['pos'][:2]
        assert (x, y) == (sign * (27 + 2 * n), -8 * n + n * (n + 1) / 4), (f, x, y)
    for f in range(60, 65):
        assert c_at(phases, f)['pos'][:2] == [sign * 89, 0]
    for f in range(65, 74):
        n = f - 64
        assert c_at(phases, f)['pos'][:2] == [sign * (89 + 1.5 * n), -2.5 * n + n * (n + 1) / 4]
    assert c_at(phases, 112)['pos'][:2] == [sign * 102.5, 0]
    stats.update(launchX=2, launchY=8, fallTicks=31, effectiveStun=attack[0]['hitTime'])
    assert stats['effectiveStun'] == 17
    return stats


def verify_ground_tech(case, input_frame, success, *, held=False):
    phases, changes, contacts = case
    assert contacts == [(15, 2, 115, False)]
    assert c_at(phases, input_frame)['raw'] == 144
    assert c_at(phases, input_frame + 1)['raw'] == (144 if held else 0)
    assert c_at(phases, input_frame)['commands']['recovery'][0] == 1
    branch = [r for r in changes if r['seat'] == 2 and r['to'] == 5201]
    if not success:
        assert not branch and c_at(phases, 60)['state'] == 5100 and c_at(phases, 112)['ctrl']
        return
    row, = branch
    assert (row['frame'], row['from'], row['stateTime'], row['var4']) == (60, 5100, 0, 5 - (60 - input_frame))
    start = c_at(phases, 60)
    assert (start['state'], start['time'], start['anim'], start['pos'][:2], start['vel'][:2]) == (5201, 1, 5200, [97, 0], [-8, 0])
    assert all(c_at(phases, f)['state'] == 5201 and not c_at(phases, f)['ctrl'] for f in range(60, 88))
    assert c_at(phases, 88)['state'] == 0 and c_at(phases, 88)['ctrl']
    velocity, position = 8, 89
    for f in range(60, 76):
        position += velocity
        assert abs(c_at(phases, f)['pos'][0] - position) < 0.00005, f
        assert abs(c_at(phases, f)['vel'][0] + velocity) < 0.00001, f
        velocity *= 0.85
    assert c_at(phases, 76)['vel'][0] == 0 and c_at(phases, 88)['pos'][0] == c_at(phases, 76)['pos'][0]


def wave_cancel(case, frame, old_tick, *, destination=1000, source=440, natural=False):
    phases, changes, _ = case
    row, = [r for r in changes if r['seat'] == 1 and r['to'] in (1000, 1050)]
    assert (row['frame'], row['from'], row['to'], row['stateTime'], row['hitPause']) == (
        frame, source, destination, old_tick, 0), row
    assert (row['var1'], row['var2']) == (int(not natural), int(natural)), row
    assert row['powerWaveCommand'] or row['helperWave'] > 0, row
    committed = c_at(phases, frame, 0, 'after-collision')
    assert (committed['state'], committed['anim'], committed['time'], committed['elem']) == (destination, destination, 1, 0)
    assert not (committed['moveHit'] or committed['moveGuarded'] or committed['moveContact'])
    return row


def kick_changes(case):
    return [(row['frame'], row['to'], row['stateTime']) for row in case[1] if row['from'] == 440]


def verify_wave_entity(case, birth, *, owner=0):
    phases, _, _ = case
    def live(frame):
        return [h for h in c_at(phases, frame, owner)['helpers'] or [] if h['anim'] == 1020 and not h['destroyed']]

    assert not live(birth - 1)
    first, = live(birth)
    assert first['time'] == 1 and abs(first['pos'][0] - c_at(phases, birth, owner)['pos'][0] - 2.5 * first['facing']) < 0.00002
    age = 1
    for frame in range(birth + 1, len(phases['end'])):
        age += int(phases['end'][frame]['globalPause'] == 0)
        if age == 26:
            assert not live(frame), frame
            return
        helper, = live(frame)
        assert helper['id'] == first['id'] and helper['time'] == age, (frame, helper)
        assert abs(helper['pos'][0] - first['pos'][0] - 2.5 * first['facing'] * min(age - 1, 10)) < 0.00002, frame
    raise AssertionError(f'Helper born at F{birth} did not reach disposal')


def verify_cases(cases):
    def default(name):
        return cases[PREFIX + name]

    assert {PREFIX + name for name in BODY} <= cases.keys(), ({PREFIX + name for name in BODY} - cases.keys())
    hit = body_measure(default('hit-0'), blocked=False)
    assert body_measure(default('guard-0'), blocked=False) == hit
    crouch_hit = body_measure(default('crouch-hit-0'), blocked=False)
    assert body_measure(default('mirror-hit-0'), blocked=False, mirrored=True) == hit
    guard = body_measure(default('crouch-guard-0'), blocked=True)
    assert default('whiff-0')[2] == []
    assert [(r['frame'], r['to'], r['stateTime']) for r in default('whiff-0')[1] if r['from'] == 440] == [(45, 11, 40)]
    assert default('interrupt-0')[2] == [(6, 1, 30, False)]
    assert [(r['frame'], r['to'], r['stateTime']) for r in default('interrupt-0')[1] if r['from'] == 440] == [(6, 5010, 2)]
    for name, success, held in (('tech-55', False, False), ('tech-56', True, False), ('tech-57', True, False),
                                 ('tech-60', True, False), ('tech-61', False, False), ('tech-held-55', False, True)):
        if PREFIX + name in cases:
            verify_ground_tech(default(name), int(name.rsplit('-', 1)[1]), success, held=held)

    for name, (raw, state) in PRIORITIES.items():
        if PREFIX + name in cases:
            phases, changes, _ = default(name)
            assert c_at(phases, 5, 0)['state'] == state
            row = next(r for r in changes if r['seat'] == 1)
            assert (row['frame'], row['raw'], row['to']) == (5, raw, state), row
            assert not any(r['to'] == 440 for r in changes), name

    if MODE2 + 'hit-0' in cases:
        assert body_measure(cases[MODE2 + 'hit-0'], blocked=False) == hit
    if MODE2 + 'hit-27' in cases:
        wave_cancel(cases[MODE2 + 'hit-27'], 27, 11, destination=1050)
        assert cases[MODE2 + 'hit-27'][2] == [(15, 2, 115, False)]

    for name, frame, old, control in (('hit-27', 27, 11, 'hit-0'), ('hit-33', 33, 17, 'hit-0'),
                                      ('hit-19', 27, 11, 'hit-0'), ('hit-held-19', 27, 11, 'hit-0'),
                                      ('guard-27', 27, 11, 'guard-0'), ('crouch-hit-27', 27, 11, 'crouch-hit-0'),
                                      ('mirror-hit-27', 27, 11, 'mirror-hit-0')):
        if PREFIX + name not in cases:
            continue
        case = default(name)
        assert case[2] == [(15, 2, 115, False)], name
        row = wave_cancel(case, frame, old)
        assert row['powerWaveCommand'], row
        verify_wave_entity(case, frame + 15)
        assert compressed_target(case) == compressed_target(default(control)), name
        phases = case[0]
        assert [c_at(phases, f, 0)['vars']['var12'] for f in range(17, 62)] == [0] + [1] * 43 + [0], name
        for f in range(frame + 15, frame + 28):
            helper, = [h for h in c_at(phases, f, 0)['helpers'] or [] if h['anim'] == 1020 and not h['destroyed']]
            assert helper['hitDef']['attr'] == 0, (name, f, helper)
        if name in ('hit-19', 'hit-held-19'):
            phases, _, _ = case
            assert c_at(phases, 19, 0, 'before-command')['raw'] == 136
            assert c_at(phases, 19, 0, 'after-command')['hitPause'] > 0
            assert row['helperWave'] == (-1 if name == 'hit-19' else 4), row

    if PREFIX + 'crouch-guard-27' in cases:
        case = default('crouch-guard-27')
        wave_cancel(case, 27, 11)
        verify_wave_entity(case, 42)
        assert case[2] == [(15, 2, 0, True), (45, 2, 9, True)]
        guarded = c_at(case[0], 45, phase='after-collision')
        assert (guarded['state'], guarded['hitId'], guarded['hitShake']) == (131, 0, 11)
        assert abs(guarded['pos'][0] - 83.33462) < 0.0001

    for name, opening, old, release, helper_count in (
        ('whiff-13', 16, 11, 13, 3), ('whiff-14', 16, 11, 14, 7), ('whiff-15', 16, 11, 15, 11),
        ('whiff-16', 16, 11, 16, 0), ('whiff-17', 17, 12, 17, 0), ('whiff-22', 22, 17, 22, 0),
        ('whiff-45', 45, 40, 45, 0), ('compete-kick-16', 16, 11, 16, 0), ('compete-heavy-16', 16, 11, 16, 0),
    ):
        if PREFIX + name not in cases:
            continue
        case = default(name)
        row = wave_cancel(case, opening, old, natural=name == 'whiff-45')
        assert (row['powerWaveCommand'], row['helperWave']) == (release == opening, helper_count), row
        verify_wave_entity(case, opening + 15)
        (frame, seat, _, blocked), = case[2]
        assert (frame, seat, blocked) == (opening + 24, 2, False), (name, case[2])
        target = c_at(case[0], frame, phase='after-collision')
        assert (target['hitId'], target['hitFall'], target['pos'][1]) == (0, False, 0), target
        assert (c_at(case[0], frame + 1)['anim'], c_at(case[0], frame + 19)['anim'], c_at(case[0], frame + 41)['ctrl']) == (
            5012, 5017, True), name
        assert not c_at(case[0], frame + 40)['ctrl'], name

    for name, edge, old, exit_ in (('hit-34', 34, 18, 56), ('whiff-12', 12, 7, 45), ('whiff-23', 23, 18, 45)):
        if PREFIX + name not in cases:
            continue
        case = default(name)
        assert kick_changes(case) == [(exit_, 11, 40)], (name, kick_changes(case))
        source = c_at(case[0], edge, 0, 'after-command')
        assert (source['state'], source['raw'], source['powerWaveCommand']) == (440, 136, True), (name, source)
        assert source['time'] in (old, old + 1), (name, old, source['time'])
        assert not any(r['to'] in (1000, 1050, 200, 205, 400) for r in case[1]), name

    for name, raw, state in (('stand-0', 32, 245), ('air-0', 34, 55), ('light-kick-0', 18, 430),
                              ('down-forward-kick-0', 42, 440), ('down-back-kick-0', 38, 440),
                              ('held-down-0', 34, 440)):
        if PREFIX + name in cases:
            src = c_at(default(name)[0], 5, 0)
            assert (src['raw'], src['state']) == (raw, state), (name, src)
            if name == 'held-down-0':
                assert all(c_at(default(name)[0], f, 0)['state'] == 11 for f in range(56, 70)), name
    for name, want in (('repeat-kick-23', [(45, 11, 40)]),
                       ('repeat-kick-45', [(45, 440, 40), (85, 11, 40)])):
        if PREFIX + name in cases:
            assert kick_changes(default(name)) == want and not default(name)[2], name
    if PREFIX + 'mixed-27' in cases:
        case = default('mixed-27')
        assert [(row['frame'], row['seat'], row['from'], row['stateTime']) for row in case[1] if row['to'] == 1000] == [
            (27, 1, 440, 11), (117, 2, 400, 4)]
        assert case[2] == [(15, 2, 115, False), (138, 1, 84, False)]
        verify_wave_entity(case, 42)
        verify_wave_entity(case, 132, owner=1)
    for name, frame, dest, tick in (('incoming-wave-26', 25, 5000, 11), ('incoming-wave-34', 29, 5010, 7),
                                    ('incoming-wave-35', 29, 5010, 6)):
        if PREFIX + name in cases:
            case = default(name)
            assert kick_changes(case) == [(frame, dest, tick)] and case[2] == [(frame, 1, 107, False)], name
            assert c_at(case[0], frame, 0, 'after-collision')['hitId'] == 0
            assert not any(row['seat'] == 1 and row['to'] == 1000 for row in case[1]), name
            assert any(h['anim'] == 1020 and not h['destroyed'] for h in c_at(case[0], frame + 1)['helpers']), name
    for name, raw in (('stand-to-down-0', 32), ('jump-and-kick-0', 33)):
        if PREFIX + name in cases:
            case = default(name)
            assert (c_at(case[0], 5, 0)['raw'], c_at(case[0], 5, 0)['state']) == (raw, 245), name
            assert not any(c_at(case[0], f, 0)['state'] == 440 for f in range(5, 12)), name
    if PREFIX + 'edge-16' in cases:
        control = default('edge-0')
        canceled = default('edge-16')
        assert all(c_at(control[0], f, phase='before-command')['raw'] == c_at(canceled[0], f, phase='before-command')['raw']
                   for f in range(len(control[0]['end'])))
        assert control[2] == [(16, 2, 122, False)] and canceled[2] == [(34, 2, 84, False)]
        assert c_at(control[0], 16, phase='after-collision')['hitId'] == 20
        assert c_at(canceled[0], 34, phase='after-collision')['hitFall']
        assert c_at(canceled[0], 140)['ctrl'] and not c_at(canceled[0], 139)['ctrl']
        wave_cancel(canceled, 16, 11)
        verify_wave_entity(canceled, 31)
    if PREFIX + 'incoming-wave-16' in cases:
        case = default('incoming-wave-16')
        assert kick_changes(case) == [(16, 1000, 11)] and case[2] == [(28, 1, 107, False)]
        assert any(row['frame'] == 28 and row['seat'] == 1 and (row['from'], row['to']) == (1000, 5000) for row in case[1])
        assert all(not c_at(case[0], f, 0)['helpers'] for f in range(len(case[0]['end'])))
        verify_wave_entity(case, 20, owner=1)
    if PREFIX + 'incoming-wave-22' in cases:
        case = default('incoming-wave-22')
        assert kick_changes(case) == [(22, 1000, 11)] and case[2] == [(46, 2, 76, False)]
        assert all(any(h['anim'] == 1020 and not h['destroyed'] for h in c_at(case[0], 37, seat)['helpers']) for seat in (0, 1))
        verify_wave_entity(case, 20, owner=1)
        verify_wave_entity(case, 37)
    cancel_cases = {'hit-27', 'hit-33', 'hit-34', 'hit-held-19', 'hit-19', 'crouch-guard-27',
                    'whiff-16', 'whiff-22', 'whiff-23', 'whiff-45'}
    verify_cancel = {PREFIX + name for name in cancel_cases} <= cases.keys()
    return {'hit': hit, 'crouchHit': crouch_hit, 'guard': guard, 'crouchGuard': guard,
            'trip': {'shakeAction': 5070, 'airState': 5071, 'fallTicks': 31, 'impactFrames': 4,
                     'bounceFrames': 10, 'lieImpactFrames': 3, 'lieDownFrames': 10, 'wakeupFrames': 25},
            'tech': {'commandAgeFrames': 4, 'duration': 28, 'action': 5200, 'stopTick': 16},
            'groundTechVerified': True, 'cancelVerified': verify_cancel,
            'lightKickPriorityVerified': PREFIX + 'down-light-kicks-0' in cases}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directories', type=Path, nargs='+')
    parser.add_argument('--extract-body', type=Path)
    args = parser.parse_args()
    if args.extract_body:
        assert len(args.directories) == 1, 'body extraction requires one source directory'
        stage23['copy_cases'](args.directories[0], args.extract_body, {PREFIX + name for name in BODY})
        args.directories = [args.extract_body]
    cases = {}
    for directory in args.directories:
        for name, data in read_cases(directory):
            assert name not in cases, name
            cases[name] = data
    print(json.dumps(verify_cases(cases), sort_keys=True))
    print(f'Verified {len(cases)} native Stage 24 traces')
