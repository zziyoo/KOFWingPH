"""Verify the signed IKEMEN GO State 310 physical-input fixtures."""

import argparse
import json
import math
from pathlib import Path
import runpy


HERE = Path(__file__).resolve().parent
STAGE29 = runpy.run_path(str(HERE.parent / 'Stage29Reference/Check-Traces.py'))
STAGE27 = STAGE29['STAGE27']
STAGE20 = STAGE29['STAGE20']
FIXTURES = json.loads((HERE / 'Cases.json').read_bytes())
PREFIX = 'default-standing-heavy-kick-310-'
MODE2 = 'mode2-standing-heavy-kick-310-'
AUDIT_ENGINE_SHA = 'dfb136020711f75240b5910fce3d3c2145f49caecd92468cf5ad26bbf78e910f'
DEF_SHA = 'eb6507b8e5fed6e4f71107a539616c7c6f0d4a568e67f7b9c8fc5cd7445471d7'
DURATIONS = (3, 3, 3, 11, 6, 7)
SPRITES = ((320, 0), (320, 1), (320, 2), (320, 3), (320, 1), (10, 1))
CLSN1 = (None, [[28, -84, 67, -45]], [[36, -116, 67, -61]], None, None, None)
CLSN2 = (
    [[-8, -72, 31, -1], [12, -84, 35, -61]],
    [[0, -80, 39, -1], [8, -100, 47, -61]],
    [[0, -80, 39, -1], [16, -108, 55, -53]],
    [[0, -80, 39, -1], [16, -100, 47, -53]],
    [[0, -80, 39, -1], [16, -100, 47, -53]],
    [[-20, -64, 19, -1], [-4, -76, 19, -53]],
)
ENTRY_WINDOWS = {200: (4, 6), 205: (4, 6), 215: (4, 12), 235: (5, 8), 245: (4, 8),
                 250: (24, 25), 400: (4, 4), 410: (6, 12), 440: (11, 17)}
char_at = STAGE29['char_at']
starts = STAGE29['starts']
assert_start = STAGE29['assert_start']


def read_cases(directory, *, audit=False):
    results = directory if (directory / 'run-metadata.json').is_file() else directory / 'results'
    run = json.loads((results / 'run-metadata.json').read_bytes())
    allowed = {STAGE29['ENGINE_SHA'], AUDIT_ENGINE_SHA} if audit is None else {
        AUDIT_ENGINE_SHA if audit else STAGE29['ENGINE_SHA']}
    assert run['executable_sha256'] in allowed, results
    assert run['source_sha256']['Terry.cns'] == STAGE29['TERRY_CNS_SHA'], results
    assert run['source_sha256']['Terry-KOF98.def'] == DEF_SHA, results
    assert run['source_sha256']['common_kof.cns'] == '3b934b5390e0b805df549db35601b4882d6c28b9165b56e3dcff72d3bd86c65b', results
    for name, case in STAGE20['read_cases'](results, capture_profile='Stage30',
                                            extra_phases=('hitdef', 'rng-draw', 'var21-draw')):
        sample = json.loads((results / f'{name}-metadata.json').read_bytes())
        fixture = FIXTURES[name]
        assert sample['physical_inputs'] == fixture, name
        for row in case[0]['before-command']:
            for seat, char in enumerate(row['chars']):
                want = STAGE27['STAGE25']['physical_bits'](fixture, seat, row['frame'], '-mirror-' in name)
                assert char['raw'] == want, (name, row['frame'], seat, char['raw'], want)
        yield name, case


def assert_contact(case, frame, kind, *, seat=1, facing=1):
    target = 3 - seat
    contact, = [row for row in case[2] if row[:2] == (frame, target)]
    attack = char_at(case[0], frame, seat - 1)
    hit = char_at(case[0], frame, target - 1)
    assert attack['state'] == 310 and attack['elem'] in (1, 2), attack
    variables = attack['vars']
    spec = attack['hitDef']
    f32 = STAGE27['STAGE17']['f32']
    raw_damage = math.floor(f32(((16 if variables['var6'] else 13) - variables['var21']) * f32(variables['fvar7'])))
    assert (variables['var40'], spec['attr'], spec['guardFlag'], spec['damage'], spec['guardDamage']) == (
        0, 65, 3, raw_damage, 0), (variables, spec)
    assert (spec['hitPause'], spec['guardPause']) == ([11, 13], [11, 11]), spec
    assert spec['groundVel'] == spec['guardVel'] == [-10.5, 0, 0], spec
    assert (spec['hitTime'], spec['guardHitTime'], spec['guardCtrlTime']) == (17, 17, 17), spec
    assert hit['hitId'] == 60 and hit['hitOwner'] == seat, hit
    guard = kind == 'guard'
    air = kind == 'air'
    attack_mul = f32(f32(attack['attackMul']) * f32(attack['attackBase'] / 100))
    assert contact[2:] == (0 if guard else int(raw_damage * attack_mul / hit['finalDefense']), guard), contact
    assert (hit['hitShake'], hit['hitFall'], hit['hitVel']) == (
        11 if guard else 13, air, [facing * 4.5, -6.5] if air else [facing * 10.5, 0]), hit
    if air:
        assert (hit['fallRecover'], hit['fallRecoverTime']) == (True, 600), hit
    return attack, hit


def verify_body(cases):
    required = {PREFIX + name for name in ('hit', 'whiff', 'crouch-hit', 'crouch-guard', 'air')} | {MODE2 + 'hit'}
    if not required <= cases.keys():
        return False
    for suffix, kind, contact_frame, entry, exit_frame in (
        ('hit', 'ground', 8, 5, 49), ('crouch-hit', 'ground', 8, 5, 49),
        ('crouch-guard', 'guard', 8, 5, 49), ('air', 'air', 15, 12, 56),
    ):
        case = cases[PREFIX + suffix]
        assert_start(case, 310, entry, 10, 0)
        assert_contact(case, contact_frame, kind)
        assert assert_start(case, 0, exit_frame, 310, 33)['var2'] == 2
        permitted = [row for row in case[0]['end'] if row['chars'][0]['state'] == 310
                     and row['chars'][0]['vars']['var1'] == 2]
        assert [row['frame'] for row in permitted] == [contact_frame + 12], (suffix, permitted)
        assert permitted[0]['chars'][0]['moveContact'] == 2
    mode2 = cases[MODE2 + 'hit']
    assert_start(mode2, 310, 5, 10, 0)
    assert_contact(mode2, 8, 'ground')
    assert mode2[2] == cases[PREFIX + 'hit'][2]
    assert_start(mode2, 0, 49, 310, 33)

    whiff = cases[PREFIX + 'whiff']
    assert not whiff[2]
    assert_start(whiff, 310, 5, 10, 0)
    assert assert_start(whiff, 0, 38, 310, 33)['var2'] == 2
    clock = 5
    for index, duration in enumerate(DURATIONS):
        for frame in range(clock, clock + duration):
            char = char_at(whiff[0], frame)
            pose = char['frame']
            assert (char['state'], char['anim'], char['elem'], char['elemTime'], char['time']) == (
                310, 310, index, frame - clock, frame - 4), (frame, char)
            assert (pose['duration'], pose['sprite'], pose['offset'], pose['clsn1'], pose['clsn2']) == (
                duration, list(SPRITES[index]), [0, 0], CLSN1[index], CLSN2[index]), (frame, pose)
            assert (char['moveType'], char['stateType'], char['ctrl'], char['vel']) == (
                131072 if frame < 14 else 32768, 1, False, [0, 0, 0]), (frame, char)
            assert char['pos'] == char_at(whiff[0], 5, phase='after-command')['pos'], (frame, char)
        clock += duration
    assert clock == 38

    grounded = cases[PREFIX + 'hit']
    grounded_target = grounded[0]['end']
    assert [(char_at(grounded[0], frame, 1, 'end')['state'], char_at(grounded[0], frame, 1, 'end')['anim'])
            for frame in (8, 9, 22, 25, 40)] == [(5000, 0), (5000, 5002), (5001, 5002), (5001, 5007), (0, 0)]
    crouch_guard = cases[PREFIX + 'crouch-guard'][0]['end']
    assert next(row['frame'] for row in grounded_target[9:] if row['chars'][1]['ctrl']) == 40
    assert next(row['frame'] for row in crouch_guard[9:] if row['chars'][1]['ctrl']) == 37
    air = cases[PREFIX + 'air']
    actual_air = [(r['frame'], r['to']) for r in air[1] if r['seat'] == 2 and r['to'] >= 5000]
    assert actual_air == [(15, 5020), (29, 5030), (30, 5035), (38, 5050), (60, 5100),
                          (64, 5101), (76, 5110), (88, 5120), (88, 5110), (89, 5120)], actual_air
    assert_start(air, 0, 114, 5120, 25, seat=2)

    for key, seat, facing, kind in (
        (PREFIX + 'guard-near', 1, 1, 'guard'), (MODE2 + 'crouch-guard', 1, 1, 'guard'),
        ('default-standing-heavy-kick-mirror-310-hit', 1, -1, 'ground'),
        (PREFIX + 'seat2-hit', 2, -1, 'ground'),
    ):
        if key in cases:
            assert_start(cases[key], 310, 5, 10, 0, seat=seat)
            attack, _ = assert_contact(cases[key], 8, kind, seat=seat, facing=facing)
            assert attack['facing'] == facing, attack
            assert_start(cases[key], 0, 49, 310, 33, seat=seat)
    if PREFIX + 'trade' in cases:
        trade = cases[PREFIX + 'trade']
        for seat, facing in ((1, 1), (2, -1)):
            attack, _ = assert_contact(trade, 8, 'ground', seat=seat, facing=facing)
            assert attack['vars']['var6'] == 1, attack
        assert trade[2] == [(8, 1, 107, False), (8, 2, 115, False)], trade[2]
    if PREFIX + 'interrupt' in cases:
        interrupt = cases[PREFIX + 'interrupt']
        assert_start(interrupt, 310, 5, 10, 0)
        hit = assert_start(interrupt, 5000, 6, 310, 2)
        assert hit['moveContact'] == 0 and [row[:2] for row in interrupt[2]] == [(6, 1)], interrupt[2]

    return True


def verify_wave(cases):
    def checked(prefix, suffix, target, frame, tick, contact, natural=False):
        case = cases.get(prefix + suffix)
        if case is None:
            return False
        row = assert_start(case, target, frame, 310, tick)
        assert (row['var1'], row['var2'], row['minus'], row['hitPause'], row['moveContact']) == (
            0 if natural else 2, 2 if natural else 0, -1, 0, contact), row
        assert row['powerWaveCommand'] or row['helperWave'] > 0, row
        char = char_at(case[0], frame)
        assert (char['state'], char['anim'], char['time'], char['elem'], char['moveContact']) == (
            target, target, 1, 0, 0), char
        return True

    default_wave = checked(PREFIX, 'wave-250-guard-54', 1000, 55, 4, 1)
    default_root = checked(PREFIX, 'wave-250-guard-55', 1000, 55, 4, 1)
    default_hit = checked(PREFIX, 'wave-205-air-35', 1000, 35, 7, 1)
    grounded_hit = checked(PREFIX, 'wave-walking-hit-22', 1000, 22, 6, 1)
    mode2_wave = checked(MODE2, 'wave-hit-20', 1050, 20, 4, 1)
    mode2_guard = checked(MODE2, 'wave-250-guard-54', 1050, 55, 4, 1)
    natural = checked(PREFIX, 'wave-natural', 1000, 38, 33, 0, natural=True)
    natural_buffer = checked(PREFIX, 'wave-natural-buffer', 1000, 38, 33, 0, natural=True)
    natural_mode2 = checked(MODE2, 'wave-natural', 1050, 38, 33, 0, natural=True)
    if default_wave:
        case = cases[PREFIX + 'wave-250-guard-54']
        assert_start(case, 310, 40, 250, 24)
        assert_contact(case, 43, 'guard')
        assert case[2] == [(28, 2, 0, True), (43, 2, 0, True), (75, 2, 9, True)], case[2]
    if default_hit:
        case = cases[PREFIX + 'wave-205-air-35']
        assert_start(case, 310, 17, 205, 4)
        attack, _ = assert_contact(case, 23, 'air')
        assert attack['elem'] == 2
    if grounded_hit:
        case = cases[PREFIX + 'wave-walking-hit-22']
        assert_contact(case, 10, 'ground')
        assert case[2] == [(10, 2, 92, False), (46, 2, 92, False)], case[2]
    for name in (PREFIX + 'wave-250-guard-56', MODE2 + 'wave-250-guard-56',
                 PREFIX + 'wave-205-air-36', PREFIX + 'wave-walking-hit-23',
                 PREFIX + 'wave-natural-buffer-old', PREFIX + 'wave-whiff-early'):
        if name in cases:
            assert not starts(cases[name], {1000, 1050}), name
    if PREFIX + 'wave-natural-new' in cases:
        row = assert_start(cases[PREFIX + 'wave-natural-new'], 1000, 40, 0)
        assert row['var1'] == row['var2'] == 0 and row['ctrl'], row
    return default_wave and default_root and default_hit and grounded_hit, mode2_wave and mode2_guard, (
        natural and natural_buffer and natural_mode2), default_wave


def assert_entry(case, frame, source, tick, *, natural=False):
    row = assert_start(case, 310, frame, source, tick)
    assert (row['var1'], row['var2'], row['minus'], row['hitPause']) == (
        (0, 2, -1, 0) if natural else (1, 0, -1, 0)), row
    char = char_at(case[0], frame)
    assert (char['state'], char['anim'], char['time'], char['elem'], char['moveContact'], char['moveHit'],
            char['moveGuarded'], char['hitDef']['attr']) == (310, 310, 1, 0, 0, 0, 0, 0), char
    return row


def verify_entries(cases):
    verified = {}
    for state, (lo, hi) in ENTRY_WINDOWS.items():
        start = 13 if state in (205, 215, 235) else 5
        if state == 245:
            keys = [PREFIX + f'contact-entry-245-{name}' for name in ('hit-first', 'guard-last', 'hit-after')]
            if not all(key in cases for key in keys):
                continue
            first, last, after = (cases[key] for key in keys)
            assert_entry(first, 5 + 11 + lo, state, lo)
            assert_entry(last, 5 + 11 + hi, state, hi)
            assert not starts(after, {310}), keys[-1]
            verified[str(state)] = [lo, hi]
            continue
        names = ('before', 'first', 'after') if lo == hi else ('before', 'first', 'last', 'after')
        keys = {name: PREFIX + f'entry-{state}-{name}' for name in names}
        if not all(key in cases for key in keys.values()):
            continue
        for name, key in keys.items():
            case = cases[key]
            assert any(r['frame'] == start for r in starts(case, {state})), (key, case[1])
            if name == 'after':
                assert not starts(case, {310}), key
            else:
                tick = hi if name == 'last' else lo
                assert_entry(case, start + tick, state, tick)
        verified[str(state)] = [lo, hi]
    for suffix, frame in (('entry-250-hit', 40), ('entry-250-guard', 40), ('entry-250-counter', 40),
                          ('entry-250-frozen', 40), ('entry-250-split-direction-first', 40),
                          ('entry-250-split-button-first', 40)):
        if PREFIX + suffix in cases:
            assert_entry(cases[PREFIX + suffix], frame, 250, 24)
    if MODE2 + 'entry-250-mode2' in cases:
        assert_entry(cases[MODE2 + 'entry-250-mode2'], 29, 250, 24)
    for edge in range(25, 29):
        if (case := cases.get(PREFIX + f'entry-250-helper-{edge}')) is not None:
            if edge == 25:
                assert not starts(case, {310}), edge
            else:
                assert_entry(case, 29, 250, 24)
    if PREFIX + 'entry-250-hit' in cases:
        case = cases[PREFIX + 'entry-250-hit']
        assert case[2] == [(28, 2, 107, False)], case[2]
        for frame in range(43, 49):
            attack = char_at(case[0], frame)
            assert attack['frame']['clsn1'] and attack['vars']['var12'] > 0 and attack['hitDef']['attr'] == 0, attack
    if PREFIX + 'entry-250-counter' in cases:
        case = cases[PREFIX + 'entry-250-counter']
        assert [row[:2] for row in case[2]] == [(28, 2), (43, 2)], case[2]
        assert_contact(case, 43, 'air')
    for state, total in ((230, 19), (240, 38), (430, 14)):
        if (case := cases.get(PREFIX + f'entry-{state}-rejected')) is not None:
            assert starts(case, {state}) and not starts(case, {310}), state
        key = (PREFIX + f'exact-natural-from-{state}') if state != 430 else (PREFIX + 'entry-430-recovered')
        if key in cases:
            row = assert_start(cases[key], 310, 5 + total, state, total)
            assert (row['var1'], row['var2']) == (0, 1), row
    return verified


def verify_input(cases):
    expected = {
        'input-back-down': (5, 410), 'input-only-down': (5, 410), 'input-only-forward': (5, 210),
        'input-y-before-df': (5, 210), 'input-df-before-y': (5, 310), 'input-held': (5, 310),
        'input-neutral-chord': (5, 250), 'input-df-chord': (5, 310), 'input-down-chord': (5, 410),
        'input-fxy': (5, 300), 'input-dfxy': (5, 310), 'input-finish-wave': (5, 1000),
        'input-ordinary-throw': (5, 800), 'input-beats-throw': (5, 310),
        'global-pause-102': (102, 310), 'global-pause-103': (104, 310), 'global-pause-104': (104, 310),
    }
    for suffix, (frame, target) in expected.items():
        if (case := cases.get(PREFIX + suffix)) is not None:
            actions = [r for r in starts(case, {210, 250, 300, 310, 410, 800, 805, 1000}) if r['frame'] >= frame]
            assert [(r['frame'], r['to']) for r in actions] == [(frame, target)], (suffix, actions)
    if (held := cases.get(PREFIX + 'input-held')) is not None:
        before, step, crouch = (char_at(held[0], frame) for frame in (37, 38, 39))
        assert (before['state'], step['state'], step['anim'], step['ctrl'], crouch['state']) == (310, 0, 0, True, 10)
        assert before['pos'] == step['pos'] and before['raw'] == step['raw'] == crouch['raw']
    for edge, expected_pauses in ((103, (107,)), (104, (105, 108))):
        if (case := cases.get(PREFIX + f'global-pause-{edge}')) is not None:
            actual = tuple(row['frame'] for row in case[0]['end'] if 105 <= row['frame'] <= 108 and row['globalPause'])
            assert actual == expected_pauses, (edge, actual)
    for edge in range(33, 40):
        if (case := cases.get(PREFIX + f'natural-reentry-{edge}')) is not None:
            actual = [r for r in starts(case, {310}) if r['frame'] > 5]
            if edge < 35:
                assert not actual, (edge, actual)
            elif edge < 39:
                assert_entry(case, 38, 310, 33, natural=True)
                assert len(actual) == 1, (edge, actual)
            else:
                assert [(r['frame'], r['from'], r['var2']) for r in actual] == [(39, 10, 0)], (edge, actual)
    for suffix in ('negative-self-early', 'negative-ordinary-early', 'negative-300-contact', 'negative-reentry-contact'):
        if (case := cases.get(PREFIX + suffix)) is not None:
            assert not [r for r in starts(case, {200, 205, 300, 310}) if r['frame'] > 5], suffix
    if PREFIX + 'input-run' in cases:
        case = cases[PREFIX + 'input-run']
        assert_start(case, 110, 4, 20, 0)
        assert not starts(case, {310}), case[1]
    return all(PREFIX + k in cases for k in expected)


def verify_tackle(cases):
    case_keys = [PREFIX + f'tackle-priority-{frame}' for frame in (20, 21)]
    if not all(key in cases for key in case_keys):
        return False
    for key, frame, target in zip(case_keys, (20, 21), (1200, 1000), strict=True):
        row, = starts(cases[key], {1200, 1000})
        assert (row['frame'], row['to'], row['powerWaveCommand'], row['ctrl']) == (frame, target, True, True), row
    if PREFIX + 'wave-hit-19' in cases:
        row = assert_start(cases[PREFIX + 'wave-hit-19'], 1200, 20, 310, 4)
        assert row['moveContact'] == 1 and row['var1'] == 2 and row['powerWaveCommand'], row
    return True


AUDIT_FIELDS = ('helperRisingY', 'rootY', 'rootForward', 'rootDown', 'moveReversed')


def verify_input_audit(audited, baseline):
    def normalized(row):
        row = {key: value for key, value in row.items() if key not in AUDIT_FIELDS}
        if 'chars' in row:
            row['chars'] = [normalized(char) for char in row['chars']]
            # At those two pre-update phases F0 still contains the random value
            # from the unseeded round intro. The seeded draw is checked at F0 collision.
            if row['frame'] == 0 and row['phase'] in ('before-command', 'after-command'):
                for char in row['chars']:
                    char['vars'] = {k: v for k, v in char['vars'].items() if k != 'var21'}
        return row

    for name, case in audited.items():
        original = baseline[name]
        common = min(len(case[0]['end']), len(original[0]['end']))
        for phase in STAGE20['PHASES']:
            for before, recorded in zip(original[0][phase][:common], case[0][phase][:common], strict=True):
                assert normalized(before) == normalized(recorded), (name, phase, before['frame'])
        all_transitions = [normalized(row) for row in case[1] if row['frame'] < common]
        assert all_transitions == [row for row in original[1] if row['frame'] < common], name
    observed = {
        'hit': (5, True, True, True, 0),
        'entry-250-helper-26': (29, False, False, False, 1),
        'entry-250-helper-27': (29, False, False, False, 2),
        'entry-250-helper-28': (29, False, False, False, 3),
        'entry-250-frozen': (40, True, True, True, 2),
        'entry-250-split-direction-first': (40, True, True, True, 0),
        'entry-250-split-button-first': (40, True, True, True, 0),
        'global-pause-102': (102, True, True, True, 0),
        'global-pause-103': (104, False, False, False, 3),
        'global-pause-104': (104, True, True, True, 0),
    }
    for suffix, expected in observed.items():
        case = audited[PREFIX + suffix]
        row, = starts(case, {310})
        assert (row['frame'], *(row[key] for key in AUDIT_FIELDS[1:4]), row['helperRisingY']) == expected, row
    hit = audited[PREFIX + 'hit']
    assert [char_at(hit[0], frame, phase='end')['helperRisingY'] for frame in range(5, 9)] == [3, 2, 1, 0]
    assert not starts(audited[PREFIX + 'entry-250-helper-25'], {310})
    paused = audited[PREFIX + 'global-pause-103']
    assert paused[0]['end'][103]['globalPause'] == 1
    assert char_at(paused[0], 103, phase='end')['helperRisingY'] == 3
    frozen = audited[PREFIX + 'entry-250-frozen']
    assert [char_at(frozen[0], frame, phase='end')['helperRisingY'] for frame in (38, 39)] == [3, 2]
    return len(audited)


def verify_special_controls(controls):
    for suffix, source, old in (('exact-natural-from-230', 230, 19), ('exact-natural-from-240', 240, 38)):
        row = assert_start(controls[PREFIX + suffix], 310, 5 + old, source, old)
        assert (row['var1'], row['var2']) == (0, 1), row
    assert_start(controls[PREFIX + 'crouched-input'], 310, 5, 10, 4)
    assert_start(controls[PREFIX + 'input-805'], 805, 5, 20, 0)
    assert_start(controls[PREFIX + 'input-df-over-805'], 310, 5, 10, 0)
    assert not starts(controls[PREFIX + 'input-df-over-805'], {805})
    for entry, contact in ((5, 8), (6, 8), (7, 9)):
        case = controls[PREFIX + f'reversed-by-1200-{entry}']
        assert_start(case, 1200, entry, 12, 0, seat=2)
        assert_contact(case, contact, 'ground')
        assert all(row['chars'][0]['moveReversed'] == 0 for row in case[0]['end'])
    for distance in (70, 74, 80, 84):
        case = controls[PREFIX + f'reversal-distance-{distance}']
        assert_start(case, 1200, 5, 12, 0, seat=2)
        for frame in range(8, 20):
            char = char_at(case[0], frame, phase='end')
            assert (char['state'], char['time'], char['moveReversed'], char['moveContact'], char['hitPause'],
                    char['life'], char['vars']['var1']) == (310, 4, 1, 0, 19 - frame, 1000, 0), char
        char = char_at(case[0], 20, phase='end')
        assert (char['state'], char['time'], char['moveReversed'], char['moveContact'], char['vars']['var1']) == (
            310, 5, 2, 0, 2), char
        assert char_at(case[0], 21, phase='end')['state'] == 5000
    for command, exits in ((39, True), (40, False)):
        case = controls[PREFIX + f'entry-215-two-blocks-{command}']
        assert case[2][:2] == [(8, 2, 0, True), (22, 2, 0, True)], case[2]
        if exits:
            assert_entry(case, 39, 215, 12)
        else:
            assert not starts(case, {310})
    case = controls[PREFIX + 'entry-200-true-guard-last']
    assert case[2][:2] == [(8, 2, 0, True), (25, 2, 0, True)], case[2]
    assert_entry(case, 22, 200, 6)
    projectile_edges = {
        (100, 20): (21, 2, 99), (100, 24): (21, None, 76),
        (140, 20): (23, 4, 99), (140, 24): (25, 2, 107),
    }
    for (distance, input_frame), (contact, tick, damage) in projectile_edges.items():
        key = PREFIX + f'projectile-cross-{distance}-{input_frame}'
        if key not in controls:
            continue
        case = controls[key]
        assert_start(case, 1000, 5, 0, 0, seat=2)
        assert case[2] == [(contact, 1, damage, False)], case[2]
        helper, = char_at(case[0], 20, 1, 'end')['helpers']
        target = char_at(case[0], contact, phase='end')
        assert (helper['state'], target['hitOwner'], target['hitPlayerId']) == (1020, 2, helper['id'])
        assert helper['id'] == char_at(case[0], contact + 1, 1, 'end')['helpers'][0]['id']
        if tick is None:
            assert not starts(case, {310}), case[1]
        else:
            assert_start(case, 310, input_frame, 10, 0)
            assert_start(case, 5000, contact, 310, tick)
    for suffix, target in (('down', 0), ('forward', 20), ('up', 40), ('down-y', 410), ('down-x', 400)):
        key = PREFIX + f'recovery-{suffix}'
        if key not in controls:
            continue
        case = controls[key]
        row = assert_start(case, target, 38, 310, 33)
        assert row['var2'] == 2 and row['var1'] == 0, row
        char = char_at(case[0], 38, phase='end')
        assert char['state'] == target and char['time'] == 1, char
        if suffix == 'down':
            assert_start(case, 10, 39, 0, 1)
            assert char['ctrl'] and char['pos'][0] == -120, char
        elif suffix == 'forward':
            assert char['ctrl'] and abs(char['pos'][0] - (-116.83203)) < 0.0001, char
    for prefix, input_frames in ((PREFIX, (98, 99, 103, 104)), (MODE2, (99,))):
        for command_frame in input_frames:
            key = prefix + f'true-245-whiff-{command_frame}'
            if key not in controls:
                continue
            case = controls[key]
            assert_start(case, 245, 95, 0, 14)
            assert [row[:2] for row in case[2]] == [(15, 2)], case[2]
            if command_frame == 104:
                assert not starts(case, {310})
            else:
                start = max(99, command_frame)
                row = assert_entry(case, start, 245, start - 95)
                assert row['moveContact'] == row['moveHit'] == row['moveGuarded'] == 0, row
    return len(controls)


def verify_cases(cases):
    body = verify_body(cases)
    default_wave, mode2_wave, natural, followup_guard = verify_wave(cases)
    entries = verify_entries(cases)
    inputs = verify_input(cases)
    tackle = verify_tackle(cases)
    globally_paused = cases.get(PREFIX + 'global-pause-103')
    global_pause_helper = bool(globally_paused and globally_paused[0]['end'][103]['globalPause'] == 1 and
                               globally_paused[0]['before-command'][104]['chars'][0]['raw'] == 0 and
                               len(starts(globally_paused, {310})) == 1)
    complete = (body and default_wave and mode2_wave and natural and len(entries) == len(ENTRY_WINDOWS)
                and inputs and tackle and global_pause_helper)
    return {'bodyVerified': body, 'completeVerified': complete, 'move310': {
        'durations': list(DURATIONS), 'attackWindow': [3, 8], 'hitPause': [11, 13], 'guardPause': [11, 11],
        'groundPush': 10.5, 'guardPush': 10.5, 'guardTicks': 17, 'groundHitTicks': 17,
        'airLaunchX': 4.5, 'airLaunchY': 6.5, 'groundHitEffectiveTicks': 19, 'guardEffectiveTicks': 18,
    }, 'entryWindows': entries,
        'contactWaveAge': 1, 'defaultContactWaveVerified': default_wave, 'mode2ContactWaveVerified': mode2_wave,
        'naturalEntryTick': 33, 'naturalWaveVerified': natural, 'followupGuardVerified': followup_guard,
        'risingTacklePriorityVerified': tackle, 'inputVerified': inputs,
        'globalPauseHelperVerified': global_pause_helper,
        'cases': len(cases)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directories', type=Path, nargs='+')
    parser.add_argument('--input-audit', type=Path, action='append', default=[])
    parser.add_argument('--controls', type=Path, action='append', default=[])
    options = parser.parse_args()
    cases = {}
    for directory in options.directories:
        for name, case in read_cases(directory):
            assert name not in cases, ('duplicate fixture', name)
            cases[name] = case
    result = verify_cases(cases)
    if options.input_audit:
        audited = {}
        for directory in options.input_audit:
            for name, case in read_cases(directory, audit=True):
                assert name not in audited, ('duplicate audit fixture', name)
                audited[name] = case
        result['inputAuditCases'] = verify_input_audit(audited, cases)
    if options.controls:
        controls = {}
        for directory in options.controls:
            for name, case in read_cases(directory, audit=None):
                assert name not in controls, ('duplicate control fixture', name)
                controls[name] = case
        result['specialControlCases'] = verify_special_controls(controls)
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
