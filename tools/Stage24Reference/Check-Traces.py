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

    if PREFIX + 'hit-27' in cases:
        phases, _, contacts = default('hit-27')
        assert contacts == [(15, 2, 115, False)]
        row = wave_cancel(default('hit-27'), 27, 11)
        assert row['powerWaveCommand']
        assert any(h['anim'] == 1020 and h['time'] == 1 for h in c_at(phases, 42, 0)['helpers'])
        assert compressed_target(default('hit-27')) == compressed_target(default('hit-0'))
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
