"""Verify pinned IKEMEN GO forward light punch (State 300) traces for Stage 28."""

import argparse
import json
import math
from pathlib import Path
import runpy


HERE = Path(__file__).resolve().parent
STAGE27 = runpy.run_path(str(HERE.parent / 'Stage27Reference/Check-Traces.py'))
STAGE20 = STAGE27['STAGE20']
FIXTURES = json.loads((HERE / 'Cases.json').read_bytes())
PREFIX = 'default-standing-heavy-kick-300-'
MODE2 = 'mode2-standing-heavy-kick-300-'
GLOBAL_PAUSE = PREFIX + 'global-pause'
# Captures shorter than the mapped frame count carry a superseded copy of the case; read_cases
# skips those so the authoritative F145 run is the only one that contributes it.
SUPERSEDED_CASES = {GLOBAL_PAUSE: 145}
MOVES = {300: (11, 9, 27)}
POSES = {300: ((3, 4, 4, 3, 3, 3, 8, 6, 6, 7), (3, 5),
               {3: 8, 7: 8, 11: 8, 14: 8, 17: 8, 28: -8, 34: -8, 40: -8})}
NORMAL_STATES = (200, 205, 210, 215, 230, 235, 240, 245, 400, 410, 430, 440)

char_at = STAGE27['char_at']
starts = STAGE27['starts']


RESULTS = 'results'


def read_cases(directory, skip=()):
    results = directory if (directory / 'run-metadata.json').is_file() else directory / RESULTS
    meta = json.loads((results / 'run-metadata.json').read_bytes())
    assert meta['source_sha256']['common_kof.cns'] == '3b934b5390e0b805df549db35601b4882d6c28b9165b56e3dcff72d3bd86c65b'
    # The F145 global-pause capture supersedes the F75 one; short captures never yield that case.
    if meta.get('last_frame', 75) < 145:
        skip = {*skip, GLOBAL_PAUSE}
    for name, case in STAGE20['read_cases'](results, capture_profile='Stage28',
                                            extra_phases=('hitdef', 'rng-draw', 'var21-draw')):
        if name in skip:
            continue
        fixture = FIXTURES[name]
        case_meta = json.loads((results / f'{name}-metadata.json').read_bytes())
        assert fixture == case_meta['physical_inputs'], name
        for row in case[0]['before-command']:
            for seat, char in enumerate(row['chars']):
                assert char['raw'] == STAGE27['STAGE25']['physical_bits'](fixture, seat, row['frame'],
                                                                         '-mirror-' in name), (name, row['frame'], seat)
        yield name, case


def entry(case, frame, source, tick, *, seat=1, permissions=None, buffers=None):
    row, = [r for r in starts(case, {300}, seat) if r['frame'] == frame and r['from'] == source]
    assert (row['stateTime'], row['hitPause']) == (tick, 0), row
    if permissions is not None:
        assert (row['var1'], row['var2']) == permissions, row
    if buffers is not None:
        assert (row['xBuffer'], row['helperForwardX']) == buffers, row
    return row


def no_entry(case, *, after=5, seat=1):
    assert not [r for r in starts(case, {300}, seat) if r['frame'] > after], 'unexpected State 300 entry'


def verify_300_hitdef(case):
    for row in case[0]['hitdef']:
        source = row['chars'][row['hitDefOwner'] - 1]
        hit = source['hitDef']
        if source['state'] != 300 or hit['attr'] == 0:
            continue
        variables = source['vars']
        base = 9 if variables['var6'] else 5
        damage = math.floor(STAGE27['STAGE17']['f32']((base - variables['var21']) *
                                                      STAGE27['STAGE17']['f32'](variables['fvar7'])))
        assert hit['damage'] == damage and hit['guardDamage'] == 0, (row['frame'], hit, variables)
        assert hit['groundVel'][:2] == [-10.5, 0], (row['frame'], hit)
        assert hit['hitTime'] == 17, (row['frame'], hit)


def contacts(case, seat=1):
    return [(frame, target, damage, guarded) for frame, target, damage, guarded in case[2] if target == seat + 1]


def measure_contacts(case, *, crouching=False):
    """Both 300 contacts share one HitDef contract; the second re-hit is inherent (Stage 28)."""
    phases, changes, contacts_ = case
    entry_row, = starts(case, {300})
    assert (entry_row['frame'], entry_row['from']) == (5, 20)
    assert len(contacts_) == 2 and [c[0] for c in contacts_] == [16, 33], contacts_
    result = []
    for frame, target, damage, guarded in contacts_:
        assert target == 2 and (damage > 0) != guarded
        target_hit = char_at(phases, frame, 1)
        pause = 11 if guarded else 13
        assert (target_hit['hitShake'], target_hit['hitFall'], target_hit['hitId']) == (pause, False, 30)
        push = abs(target_hit['hitVel'][0])
        assert push == 10.5 and target_hit['hitVel'][1] == 0, frame
        assert char_at(phases, frame + 1)['hitPause'] == 11, frame
        result.append({'frame': frame, 'damage': damage, 'guarded': guarded, 'initialPush': push,
                       'pause': {'attackerFrames': 11, 'defenderFrames': pause}})
    initial, slide = (5022, 5027) if crouching else (5002, 5007)
    if not result[0]['guarded']:
        assert char_at(phases, 17, 1)['anim'] == initial and char_at(phases, 32, 1)['anim'] == initial
        assert char_at(phases, 33, 1)['anim'] == slide
    return result


def verify_body(cases):
    hit_case = cases[PREFIX + 'free-hit']
    phases, changes, _ = hit_case
    verify_300_hitdef(hit_case)
    walk, enter = (r for r in changes if r['seat'] == 1 and r['frame'] == 5)
    assert (walk['from'], walk['to']) == (0, 20) and (enter['from'], enter['to'], enter['stateTime']) == (20, 300, 0)
    first, second = contacts(hit_case)
    assert (first[0], first[3]) == (16, False) and (second[0], second[3]) == (33, False), (first, second)
    assert first[2] > 0 and second[2] > 0, (first, second)
    for frame in (16, 33):
        assert char_at(phases, frame + 1)['hitPause'] == 11, frame
        assert char_at(phases, frame, 1)['hitShake'] == 13, frame
    exit_row, = [r for r in changes if r['seat'] == 1 and r['from'] == 300 and r['to'] == 0]
    assert (exit_row['frame'], exit_row['stateTime'], exit_row['var2']) == (74, 47, 2), exit_row
    target_ctrl = next(f for f in range(17, 80) if char_at(phases, f, 1)['ctrl'])
    assert target_ctrl == 65, target_ctrl
    return {'doubleContact': [first[0], second[0]], 'exitFrame': 74}


def verify_whiff(cases, prefix):
    if prefix + 'free-whiff' not in cases:
        return None
    case = cases[prefix + 'free-whiff']
    phases, changes, _ = case
    no_entry(case)
    enter, exit_row = (r for r in changes if r['seat'] == 1 and r['to'] in (300, 0))
    assert (enter['frame'], enter['from'], enter['to']) == (5, 20, 300)
    assert (exit_row['frame'], exit_row['stateTime'], exit_row['to'], exit_row['var2']) == (52, 47, 0, 2)
    durations, _, steps = POSES[300]
    facing = char_at(phases, 5)['facing']
    at, moved = 5, []
    for index, duration in enumerate(durations):
        for frame in range(at, at + duration):
            char = char_at(phases, frame)
            assert (char['state'], char['anim'], char['elem']) == (300, 300, index), frame
            before = char_at(phases, frame, phase='after-command')['pos'][0]
            after = char_at(phases, frame)['pos'][0]
            actual = (after - before) * facing
            assert actual == steps.get(frame - 5, 0), (frame, actual)
            if actual:
                moved.append({'tick': frame - 5, 'x': actual})
        at += duration
    assert at == 52
    return moved


def verify_entries(cases):
    free = [entry(cases[PREFIX + name], 5, 20, 0) is not None for name in ('free-hit', 'free-whiff')]
    for name, target in (('x-alone-200', 200), ('down-x-400', 400), ('down-forward-x-400', 400),
                         ('back-x-200', 200)):
        case = cases[PREFIX + name]
        assert [r['to'] for r in case[1] if r['seat'] == 1 and r['frame'] == 5][-1] == target, name
        no_entry(case)
    rise = cases[PREFIX + 'crouch-rise-fwd-x']
    rise_rows = [r for r in rise[1] if r['seat'] == 1]
    assert [(r['from'], r['to'], r['frame']) for r in rise_rows if r['to'] == 300] == [(12, 300, 7)]
    no_entry(cases[PREFIX + 'age1-late-direction'])
    no_entry(cases[PREFIX + '200-age1-entry'])
    no_entry(cases[PREFIX + '200-age2-expired'])
    no_entry(cases[PREFIX + 'helper-prewindow-persist'])
    no_entry(cases[MODE2 + 'helper-holddown'])
    recovery = cases[PREFIX + 'helper-recovery-entry']
    no_entry(recovery)
    assert [r['to'] for r in recovery[1] if r['seat'] == 1][-1] == 0
    for name in ('helper-prewindow-persist', 'helper-prewindow-245', 'helper-holddown-245',
                 'helper-recovery-entry'):
        no_entry(cases[PREFIX + name])
    no_entry(cases[MODE2 + 'helper-holddown'])
    no_entry(cases[MODE2 + 'helper-prewindow-245'])
    no_entry(cases[MODE2 + 'helper-holddown-245'])
    for name, frame, state_time, buffers in (
        ('frozen-both', 20, 4, (True, 0)), ('split-direction-first', 20, 4, (True, 0)),
        ('split-button-first', 20, 4, (True, 0)), ('245-cancel-first-frozen', 20, 4, (True, 0)),
    ):
        entry(cases[PREFIX + name], frame, 245, state_time, permissions=(1, 0), buffers=buffers)
    entry(cases[MODE2 + '245-cancel-first-frozen'], 20, 245, 4, permissions=(1, 0), buffers=(True, 0))
    for name, frame, tick in (('245-cancel-resumed', 22, 6), ('245-cancel-last-resumed', 24, 8)):
        entry(cases[PREFIX + name], frame, 245, tick, permissions=(1, 0), buffers=(True, 0))
        if MODE2 + name in cases:
            entry(cases[MODE2 + name], frame, 245, tick, permissions=(1, 0), buffers=(True, 0))
    no_entry(cases[PREFIX + '245-cancel-after-real'])
    for name, frame, tick in (('235-cancel-first-frozen', 21, 5), ('235-cancel-resumed', 22, 6),
                              ('235-cancel-last-resumed', 24, 8)):
        entry(cases[PREFIX + name], frame, 235, tick, permissions=(1, 0))
    chain = cases[PREFIX + '235-cancel-after-chain']
    assert [r['to'] for r in chain[1] if r['seat'] == 1 and r['frame'] == 25] == [200]
    no_entry(chain)
    for name, frame, tick in (('200-cancel-first', 9, 4), ('200-cancel-last', 10, 5)):
        entry(cases[PREFIX + name], frame, 200, tick, permissions=(1, 0))
    chain = cases[PREFIX + '200-cancel-after-chain']
    assert [r['to'] for r in chain[1] if r['seat'] == 1 and r['frame'] == 12] == [200]
    no_entry(chain)
    for name in ('215-cancel-first', '215-cancel-last', '215-cancel-after-reject',
                 '215-cancel-after-contact', '215-helper-prewindow',
                 '215-helper-holddown', 'helper-prewindow-245', 'helper-holddown-245',
                 '215-helper-prewindow-63', '215-helper-holddown-63'):
        no_entry(cases[PREFIX + name])
    no_entry(cases[MODE2 + '215-helper-prewindow-63'])
    for name in ('215-helper-prewindow-64', '215-helper-holddown-64'):
        no_entry(cases[PREFIX + name])
    no_entry(cases[MODE2 + '215-helper-prewindow-64'])
    entry(cases[PREFIX + 'helper-recovery-215'], 46, 210, 30, permissions=(0, 1), buffers=(False, 1))
    no_entry(cases[MODE2 + '215-helper-prewindow'])
    no_entry(cases[MODE2 + 'helper-prewindow-245'])
    no_entry(cases[MODE2 + 'helper-holddown-245'])
    entry(cases[PREFIX + '215-window-first'], 20, 215, 4, permissions=(1, 0), buffers=(True, 0))
    entry(cases[PREFIX + '215-window-last'], 34, 215, 7, permissions=(1, 0), buffers=(True, 0))
    entry(cases[PREFIX + '215-window-after'], 34, 215, 7, permissions=(1, 0), buffers=(True, 0))
    no_entry(cases[PREFIX + '215-window-reject'])
    entry(cases[PREFIX + '215-helper-recovery-late'], 55, 215, 28, permissions=(0, 1), buffers=(False, 2))
    for name, frame, tick in (('400-cancel-first', 9, 4), ('410-cancel-first', 11, 6),
                              ('410-cancel-last', 16, 11), ('440-cancel-first', 16, 11),
                              ('440-cancel-last', 21, 16), ('440-cancel-after-reject', 22, 17)):
        case = cases[PREFIX + name]
        row = entry(case, frame, {'400-cancel-first': 400, '410-cancel-first': 410, '410-cancel-last': 410,
                                  '440-cancel-first': 440, '440-cancel-last': 440,
                                  '440-cancel-after-reject': 440}[name], tick, permissions=(1, 0))
        if name == '400-cancel-first':
            assert (row['xBuffer'], row['helperForwardX']) == (False, 3), row
    chain = cases[PREFIX + '400-cancel-after-chain']
    assert [r['to'] for r in chain[1] if r['seat'] == 1 and r['frame'] == 11] == [200]
    no_entry(chain)
    chain = cases[PREFIX + '430-no-entry']
    assert [r['to'] for r in chain[1] if r['seat'] == 1 and r['frame'] == 11] == [200]
    no_entry(chain)
    mode2 = [entry(cases[MODE2 + '245-cancel-resumed'], 22, 245, 6, permissions=(1, 0)) is not None]
    mode2.append(len(contacts(cases[MODE2 + 'free-hit'])) == 2)
    mode2_case = cases[MODE2 + 'free-hit']
    assert (exit_ := [r for r in mode2_case[1] if r['seat'] == 1 and r['from'] == 300 and r['to'] == 0])[0][
        'stateTime'] == 47
    for name in (PREFIX + 'age1-late-direction', PREFIX + '200-age1-entry'):
        case = cases[name]
        assert [r['to'] for r in case[1] if r['seat'] == 1][-1] in NORMAL_STATES + (0, 20)
    return all(free) and all(mode2)


def verify_exits(cases):
    late = cases[PREFIX + 'wave-exit-late-reject']
    row, = [r for r in late[1] if r['seat'] == 1 and r['to'] == 1000]
    assert (row['frame'], row['from'], row['stateTime'], row['var1'], row['moveContact']) == (
        45, 300, 18, 2, 1), row
    post = cases[PREFIX + 'wave-exit-postpause']
    row, = [r for r in post[1] if r['seat'] == 1 and r['to'] == 1000]
    assert (row['frame'], row['stateTime'], row['var1']) == (45, 18, 2), row
    no_wave = cases[PREFIX + 'wave-exit-guard']
    assert not [r for r in no_wave[1] if r['seat'] == 1 and r['to'] in (1000, 1050)]
    mode2_wave = cases[MODE2 + 'wave-exit-guard']
    assert not [r for r in mode2_wave[1] if r['seat'] == 1 and r['to'] in (1000, 1050)]
    reentry = cases[PREFIX + 'natural-reentry']
    row, = [r for r in reentry[1] if r['seat'] == 1 and r['from'] == 300 and r['to'] == 300]
    assert (row['frame'], row['stateTime'], row['var2'], row['xBuffer']) == (52, 47, 2, False), row
    pause = cases[GLOBAL_PAUSE]
    rows = [r for r in pause[1] if r['seat'] == 1 and r['to'] == 300]
    assert len(rows) == 1 and (rows[0]['frame'], rows[0]['from'], rows[0]['xBuffer'],
                               rows[0]['helperForwardX']) == (104, 0, False, 3), rows
    phases = pause[0]
    assert char_at(phases, 103, phase='end')['helperForwardX'] == 3
    return True


def verify_cases(cases):
    for name, case in cases.items():
        for row in case[0]['rng-draw']:
            assert row['after'] == row['before'] * 16807 % STAGE27['STAGE17']['MODULUS'], name
    body = verify_body(cases)
    poses = verify_whiff(cases, PREFIX)
    mode2_poses = verify_whiff(cases, MODE2)
    assert mode2_poses is None or poses == mode2_poses
    entries = verify_entries(cases)
    exits = verify_exits(cases)
    standing = measure_contacts(cases[PREFIX + 'hit-clean'])
    crouching = measure_contacts(cases[PREFIX + 'crouch-hit-clean'], crouching=True)
    guard = measure_contacts(cases[PREFIX + 'guard-clean'])
    crouch_guard = measure_contacts(cases[PREFIX + 'crouch-guard-clean'], crouching=True)
    assert [c['guarded'] for c in guard] == [True, True]
    assert [c['guarded'] for c in crouch_guard] == [True, True]
    far = measure_contacts(cases[PREFIX + 'hit-clean-far'])
    assert far == standing
    moves = {300: {'contacts': standing, 'crouchingContacts': crouching, 'guardContacts': guard,
                   'crouchGuardContacts': crouch_guard}}
    return {'bodyVerified': body is not None and len(poses) == 8, 'posAdds': {300: poses},
            'positionStepsVerified': [300], 'entryVerified': entries, 'waveExitVerified': exits,
            'moves': moves,
            'completeVerified': body is not None and len(poses) == 8 and entries and exits}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directories', nargs='+', type=Path)
    options = parser.parse_args()
    cases = {}
    for directory in sorted(options.directories, key=lambda path: -int(
            json.loads(((path if (path / 'run-metadata.json').is_file() else path / RESULTS) /
                        'run-metadata.json').read_bytes()).get('last_frame', 75))):
        skip = {GLOBAL_PAUSE} if json.loads(
            ((directory if (directory / 'run-metadata.json').is_file() else directory / RESULTS) /
             'run-metadata.json').read_bytes()).get('last_frame', 75) < 145 else ()
        for name, case in read_cases(directory, skip=skip):
            assert name not in cases, name
            cases[name] = case
    expected = set(FIXTURES) - {GLOBAL_PAUSE} | {GLOBAL_PAUSE}
    missing = expected - cases.keys()
    if missing:
        parser.exit(1, f'Incomplete Stage 28 reference: missing {sorted(missing)}\n')
    result = verify_cases(cases)
    print(json.dumps(result, indent=2))
    if not result['completeVerified']:
        parser.exit(1, 'Stage 28 reference contract did not pass\n')
    print(f'PASS: {len(cases)} native Stage 28 fixtures')


if __name__ == '__main__':
    main()
