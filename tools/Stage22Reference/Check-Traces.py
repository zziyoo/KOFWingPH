"""Verify native Terry State 400 ground, input, and Power Wave fixtures."""

import argparse
import json
import math
from pathlib import Path
import runpy


stage20 = runpy.run_path(str(Path(__file__).resolve().parents[1] / 'Stage20Reference/Check-Traces.py'))
stage17 = runpy.run_path(str(Path(__file__).resolve().parents[1] / 'Stage17Reference/Check-Traces.py'))
PREFIX = 'default-crouch-wave-'
MODE2 = 'mode2-crouch-wave-'
PHASES = ('hitdef', 'rng-draw', 'var21-draw')
BODY = {'hit-0', 'held-down-0', 'guard-0', 'crouch-guard-0', 'interrupt-0', 'mirror-hit-0', 'whiff-0', 'stand-0'}


def read_cases(directory):
    yield from stage20['read_cases'](directory, capture_profile='Stage22', extra_phases=PHASES)


def measure(case, blocked):
    phases, _, contacts = case
    frame, seat, damage, actual_guard = contacts[0]
    assert (frame, seat, actual_guard) == (8, 2, blocked)
    samples = phases['after-collision']
    end = phases['end']
    assert [samples[f]['chars'][0]['hitPause'] for f in range(9, 20)] == list(range(11, 0, -1))
    target_pause = samples[frame]['chars'][1]['hitShake']
    restored = next(row['frame'] for row in end[frame + 1:] if row['chars'][1]['ctrl'])
    first_movement = frame + target_pause + 1
    push = end[first_movement]['chars'][1]['pos'][0] - end[first_movement - 1]['chars'][1]['pos'][0]
    assert (target_pause, push, restored) == ((11, 6, 29) if blocked else (13, 6, 32))
    return {'damage': damage, 'initialPush': push, 'effectiveStun': restored - frame - target_pause,
            'pause': {'attackerFrames': 11, 'defenderFrames': target_pause}}


def verify_cases(cases):
    assert all(PREFIX + name in cases for name in BODY), 'missing native State 400 body controls'
    for name, (phases, changes, contacts) in cases.items():
        rows = phases['after-collision']
        starts = [r for r in changes if r['to'] == 400]
        for row in starts:
            frame, seat = row['frame'], row['seat'] - 1
            source = rows[frame]['chars'][seat]
            assert (source['state'], source['stateType'], source['anim'], source['time']) == (400, 2, 400, 1), name
            assert phases['before-command'][frame]['chars'][seat]['raw'] & 2, name
        stream = phases['rng-draw']
        assert stream and stream[0]['before'] == 1337, name
        assert all(row['after'] == row['before'] * 16807 % stage17['MODULUS'] for row in stream), name
        for hitdef in phases['hitdef']:
            source = hitdef['chars'][hitdef['hitDefOwner'] - 1]
            if source['state'] != 400:
                continue
            v = source['vars']
            raw = math.floor(stage17['f32'](((7 if v['var6'] else 6) - v['var21']) * stage17['f32'](v['fvar7'])))
            assert source['hitDef']['damage'] == raw and source['elem'] == 1, (name, hitdef['frame'])
        for row in (r for r in changes if r['from'] == 400 and r['to'] in (1000, 1050)):
            frame, seat = row['frame'], row['seat'] - 1
            assert row['hitPause'] == 0 and (row['powerWaveCommand'] or row['helperWave'] > 0), name
            assert (row['stateTime'], row['var1'], row['var2']) in ((4, 1, 0), (13, 0, 1)), name
            assert rows[frame]['chars'][seat]['anim'] == row['to'], name
            if row['to'] == 1000:
                entities = rows[frame + 15]['chars'][seat]['helpers'] or []
                assert any(h['anim'] == 1020 and h['time'] == 1 for h in entities), name
        for frame, seat, amount, _ in contacts:
            life = rows[frame]['chars'][seat - 1]['life']
            assert phases['end'][frame + 1]['chars'][seat - 1]['life'] == life - amount, name
    hit, guard = (measure(cases[PREFIX + name], blocked) for name, blocked in [('hit-0', False), ('guard-0', True)])
    for name, exit_frame in [('hit-0', 29), ('held-down-0', 29), ('whiff-0', 18)]:
        phases, transitions, _ = cases[PREFIX + name]
        assert [(r['frame'], r['to'], r['stateTime']) for r in transitions if r['from'] == 400] == [(exit_frame, 11, 13)], name
        assert phases['end'][exit_frame + 1]['chars'][0]['state'] == (11 if name == 'held-down-0' else 12), name
        if name != 'held-down-0':
            assert [(phases['end'][f]['chars'][0]['state'], phases['end'][f]['chars'][0]['ctrl'])
                    for f in range(exit_frame + 1, exit_frame + 6)] == [(12, True)] * 4 + [(0, True)], name
    assert not any(r['to'] == 400 for r in cases[PREFIX + 'stand-0'][1])
    assert cases[PREFIX + 'interrupt-0'][2] == [(6, 1, 30, False)]
    if any(name not in BODY for name in (key.removeprefix(PREFIX) for key in cases)):
        expected = {'hit-20': (20,1000,4), 'hit-held-11': (20,1000,4), 'hit-21': (22,200,6),
                    'hit-29': (29,1000,13), 'guard-20': (20,1000,4), 'crouch-guard-20': (20,1000,4),
                    'mirror-hit-20': (20,1000,4), 'crouch-chain-22': (22,400,6), 'crouch-chain-23': (23,400,7),
                    'whiff-8': (9,1000,4), 'whiff-9': (9,1000,4), 'whiff-10': (11,200,6),
                    'whiff-18': (18,1000,13), 'mixed-20': (20,1000,4)}
        for name, want in expected.items():
            _, transitions, _ = cases[PREFIX + name]
            assert [(r['frame'],r['to'],r['stateTime']) for r in transitions if r['from'] == 400][0] == want, name
        assert not any(r['to'] == 400 for r in cases[PREFIX + 'neutral-motion-5'][1])
        phases, transitions, _ = cases[PREFIX + 'whiff-8']
        wave = next(r for r in transitions if r['to'] == 1000)
        assert wave['frame'] == 9 and not wave['powerWaveCommand'] and wave['helperWave'] > 0
        assert phases['after-collision'][8]['chars'][0]['vars']['var1'] == 0
        phases, _, contacts = cases[PREFIX + 'hit-21']
        assert contacts == [(8, 2, 38, False)]
        assert [phases['after-collision'][f]['chars'][1]['frame']['sprite']
                for f in range(22, 28)] == [[5010, 0], *([[5010, 10]] * 4), [5010, 0]]
        assert [(r['frame'],r['to']) for r in cases[PREFIX + 'stand-chain-23'][1] if r['from'] == 200] == [(23,400)]
        assert not any(r['to'] == 1000 for r in cases[MODE2 + 'hit-20'][1])
        assert any(r['to'] == 1050 for r in cases[MODE2 + 'hit-20'][1])
    return {'hit': hit, 'guard': guard, 'lightWindowStart': 6, 'cancelVerified': PREFIX+'whiff-9' in cases}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directories', type=Path, nargs='+')
    opts = parser.parse_args()
    cases = {}
    for directory in opts.directories:
        for name, data in read_cases(directory):
            assert name not in cases, name
            cases[name] = data
    print(json.dumps(verify_cases(cases), sort_keys=True))
    print(f'Verified {len(cases)} native Stage 22 traces')
