"""Audit the four Terry standing-normal damage formulas for Stage 17."""

import argparse
import copy
import json
import math
import re
import struct
import zipfile
from fractions import Fraction
from pathlib import Path

from . import state200, state205215, state210
from .state200 import archive_digest, normalized, number_in, read_sections, require, unique


MOVE_IDS = {200: state200.MOVE_ID, 205: state205215.MOVE_IDS[205],
            210: state210.MOVE_ID, 215: state205215.MOVE_IDS[215]}
PRESETS = {'default': ('TerryDamage', 'terry98-stage17-default'),
           'mode2': ('TerryDamageMode2', 'terry98-stage17-mode2')}
MODULUS = (1 << 31) - 1
CASE_DISTANCES = {
    'light-near': 44, 'light-far': 60, 'heavy-near': 62, 'heavy-far': 72,
    'far-trade': 72, 'near-trade': 44, 'near-heavy-trade': 62, 'interrupt': 72, 'second-only': 64,
    'carry-live': 80, 'carry-last': 80, 'carry-expired': 80,
    'mirror-heavy-near': 62, 'mirror-heavy-far': 72, 'mirror-far-trade': 72, 'mirror-second-only': 64,
}
OPTIONAL_CASE_DISTANCES = {
    'carry-edge': 80,
    'heavy-guard': 62, 'mirror-heavy-guard': 62,
    'heavy-far-guard': 62, 'mirror-heavy-far-guard': 62,
}


def parse_formula(expression):
    source = normalized(expression)
    ordinary = re.fullmatch(r'floor\(\(ifelse\(var\(6\),(\d+),(\d+)\)-var\(21\)\)\*fvar\(7\)\),0', source)
    if ordinary:
        counter, normal = map(int, ordinary.groups())
        return {'normalBase': normal, 'counterBase': counter, 'useContactDivisor': False}
    grouped = re.fullmatch(r'floor\(\(\((\d+)-var\(21\)\)/var\(23\)\)\*fvar\(7\)\),0', source)
    require(grouped is not None, f'unsupported damage expression: {expression}')
    base = int(grouped.group(1))
    return {'normalBase': base, 'counterBase': base, 'useContactDivisor': True}


def float32(value):
    return struct.unpack('<f', struct.pack('<f', float(value)))[0]


def resolved_damage(formula, random_value, counter, divisor, *, scale, attack_multiplier,
                    target_hurt, defense_controller=Fraction('.97')):
    base = formula['counterBase' if counter else 'normalBase'] - random_value
    require(base >= 0 and divisor >= 1, 'unsupported damage operands')
    # Both operands of the CNS divide are integers. Multiplication by fvar(7) is float32.
    quotient = base // divisor if formula['useContactDivisor'] else base
    raw = math.floor(float32(float32(quotient) * float32(scale)))
    # MUGEN DefenceMulSet stores the reciprocal, and applies it only in MoveType H.
    defense = float32(1 / float32(defense_controller)) if target_hurt else 1.0
    value = raw * (float32(attack_multiplier) / defense)
    settled = math.floor(max(value, 1) + 0.5) if value > 0 else 0
    return raw, settled


def controller(controllers, kind, key, expected_trigger):
    selected = unique((item for item in controllers if item.one('type', '').lower() == kind
                       and item.values(key) and set(map(normalized, item.values('trigger1'))) == expected_trigger),
                      f'{kind} {key}')
    return selected


def source_globals(controllers):
    counter_set = controller(controllers, 'varset', 'var(6)', {'p2movetype=a'})
    counter_add = controller(controllers, 'varadd', 'var(6)', {'p2movetype!=a', 'var(6)>=1'})
    counter_reset = unique((item for item in controllers if item.one('type', '').lower() == 'varset'
                            and item.one('var(6)', '') == '0' and item.one('trigger1', '').startswith('var(6)')),
                           'counter reset')
    (counter_max,) = number_in(counter_reset.one('trigger1'), r'var\(6\)>(\d+)', 'counter reset threshold')
    require(counter_set.one('var(6)') == counter_add.one('var(6)') == '1'
            and counter_set.one('ignorehitpause', '0') == counter_add.one('ignorehitpause', '0') == '0'
            and counter_reset.one('ignorehitpause') == '1' and counter_max == 3
            and counter_set.line < counter_add.line < counter_reset.line,
            'unsupported counter sampling, ordering, or hit-pause policy')
    random = controller(controllers, 'varrandom', 'range', {'1'})
    low, high = number_in(random.one('range'), r'(\d+),(\d+)', 'damage random range')
    require(random.one('v') == '21' and random.one('ignorehitpause', '0') == '0' and (low, high) == (0, 2),
            'unsupported damage random controller')
    scale = controller(controllers, 'varset', 'fvar(7)', {'1'})
    attack_calc = controller(controllers, 'varset', 'fvar(0)', {'1'})
    attack_apply = controller(controllers, 'attackmulset', 'value', {'1'})
    defense = controller(controllers, 'defencemulset', 'value', {'1'})
    attack_expression = re.fullmatch(r'fvar\(1\)\*fvar\(2\)\*fvar\(3\)\*(\d+(?:\.\d+)?)',
                                     normalized(attack_calc.one('fvar(0)')))
    require(attack_expression is not None and attack_apply.one('value') == 'fvar(0)'
            and attack_calc.line < attack_apply.line, 'unsupported attack multiplier expression')
    require(not defense.values('onhit') and not defense.values('multype'),
            'unsupported DefenceMulSet overrides')
    return {
        'randomMin': low, 'randomMax': high, 'counterGraceTicks': counter_max - 1,
        'scale': state200.fraction(scale.one('fvar(7)')),
        'attackMultiplier': state200.fraction(attack_expression.group(1)),
        'defenseController': state200.fraction(defense.one('value')),
        'lines': {'counterSet': counter_set.line, 'counterAdd': counter_add.line, 'counterReset': counter_reset.line,
                  'varRandom': random.line, 'scale': scale.line, 'attackCalculation': attack_calc.line,
                  'attackApply': attack_apply.line, 'defense': defense.line},
    }


def inspect_source(zip_path):
    zip_sha, members, contents = state200.read_terry_archive(zip_path)
    with zipfile.ZipFile(zip_path) as archive:
        definition = state200.archive_member(archive, members['def']['name'])
        files = unique((section for section in read_sections(definition.decode('latin-1')) if section.title == 'files'),
                       'DEF Files section')
        globals_name = files.one('st4')
        globals_source = state200.archive_member(archive, globals_name)
    _, controllers = state205215.move_scope(read_sections(globals_source.decode('latin-1')), -2)
    globals_rules = source_globals(controllers)
    constants = unique((section for section in read_sections(contents['cns'].decode('latin-1')) if section.title == 'data'),
                       'Data constants')
    require(constants.one('attack') == constants.one('defence') == '100',
            'damage converter expects the reviewed neutral 100/100 attack and defence constants')
    source_sections = read_sections(contents['st1'].decode('latin-1'))
    formulas = {}
    for number in MOVE_IDS:
        _, state_controls = state205215.move_scope(source_sections, number)
        attacks = [item for item in state_controls if item.one('type').lower() == 'hitdef'
                   and normalized(item.one('attr', '')) == 's,na']
        require(len(attacks) == (2 if number == 215 else 1), f'State {number}: unsupported damage controller count')
        formulas[number] = [{'line': item.line, **parse_formula(item.one('damage'))} for item in attacks]
    require(all(formula['useContactDivisor'] == (number == 215)
                for number, attacks in formulas.items() for formula in attacks), 'unexpected damage divisor scope')
    return {
        'format': 1, 'zipSha256': zip_sha,
        'members': {**members, 'st4': {'name': globals_name, 'sha256': state200.digest(globals_source)}},
        'globals': globals_rules, 'formulas': formulas,
    }


def build_profile(audit, base_fighter, base_binding, mode):
    fighter, binding = copy.deepcopy((base_fighter, base_binding))
    fighter['fighterId'], binding['visualId'] = PRESETS[mode]
    fighter['displayName'] = f"Terry Standing Normals with Damage Rules ({'default' if mode == 'default' else 'mode 2'})"
    rules = audit['globals']
    fighter['damageRules'] = {
        'baseScale': rules['scale'], 'attackMultiplier': rules['attackMultiplier'],
        'defenseMultiplier': state200.fraction(1), 'hitstunDefenseMultiplier': rules['defenseController'],
        'varianceMaximum': rules['randomMax'], 'counterGraceTicks': rules['counterGraceTicks'],
    }
    for number, move_id in MOVE_IDS.items():
        move = unique((move for move in fighter['moves'] if move['moveId'] == move_id), f'Stage 16 move {move_id}')
        for attack, formula in zip(move['attacks'], audit['formulas'][number], strict=True):
            attack['damage'] = 0
            attack.pop('counterDamage', None)
            attack.pop('damageAfterPriorContact', None)
            attack['dynamicDamage'] = {key: formula[key] for key in ('normalBase', 'counterBase', 'useContactDivisor')}
    return fighter, binding


def stage16_profiles(zip_path, manifest, state200_reference, state210_reference,
                     fractional_reference, state205215_reference):
    near = state205215.inspect_source(zip_path, manifest)
    near_reference = state205215.inspect_reference(near, state205215_reference)
    far_profiles, far_reference = state205215.stage15_profiles(zip_path, manifest, state200_reference,
                                                              state210_reference, fractional_reference)
    profiles = {mode: state205215.build_profile(near, near_reference['projection'][mode], *far_profiles[mode], mode)
                for mode in PRESETS}
    return profiles, {'manifestSha256': near['manifestSha256'],
                      'stage15ReferenceSha256': far_reference, 'stage16ReferenceSha256': near_reference['metadataSha256']}


def as_fraction(field):
    return Fraction(field['numerator'], field['denominator'])


def verify_random(rows, seed):
    value = seed
    draws = character_draws = 0
    pending = False
    for row in rows:
        if row['phase'] == 'rng-draw':
            require(row['before'] == value, 'disconnected native RNG sequence')
            value = (value * 16807) % MODULUS
            require(row['after'] == value, 'invalid native Park–Miller transition')
            draws += 1
            pending = True
        elif row['phase'] == 'var21-draw':
            require(pending and row['seat'] in (1, 2) and row['rng'] == value
                    and row['min'] == 0 and row['max'] == 2 and row['value'] == value // (MODULUS // 3 + 1),
                    'invalid native var21 draw')
            character_draws += 1
            pending = False
    require(draws and character_draws, 'native trace has no damage random evidence')
    return {'rngDraws': draws, 'characterDraws': character_draws}


def verify_damage_variables(rows):
    attack_type, hurt_type = 131072, 65536
    counts = {'counterUpdates': 0, 'divisorUpdates': 0}
    counter = {}
    for row in rows:
        if row['phase'] != 'damage-var':
            continue
        variable, seat, operation = row['variable'], row['seat'], row['operation']
        before, after, pause = row['before'], row['after'], row['hitPause']
        target_type = row['opponentMoveType']
        require(seat in (1, 2), 'damage variable observed for an invalid seat')
        if variable == 6:
            if operation == 'add':
                require(pause == 0 and target_type != attack_type and before in (1, 2, 3) and after == before + 1,
                        'invalid native counter age increment')
            elif operation == 'set' and after == 1:
                require(pause == 0 and target_type == attack_type, 'invalid native counter startup')
            else:
                require(operation == 'set' and before == 4 and after == 0, 'invalid native counter expiration')
            counter[seat] = after
            counts['counterUpdates'] += 1
        elif variable == 23:
            require(pause == 0, 'native divisor update during hit pause')
            require(row['state'] == 215, 'native divisor update outside State 215')
            if operation == 'set':
                require(after == 1, 'invalid native divisor initialization')
            else:
                require(operation == 'add' and after == before + 1 and target_type == hurt_type
                        and counter.get(seat, 0) == 0, 'invalid native prior-contact divisor update')
            counts['divisorUpdates'] += 1
    return counts


def formula_for(audit, state, element):
    formulas = audit['formulas'][state]
    if state == 215:
        require(element in (1, 2), 'State 215 HitDef must be inside one of its two strike elements')
        return formulas[element - 1]
    return formulas[0]


def expected_contact(audit, row, attacker, target, hitdef):
    source = hitdef['chars'][attacker]
    victim = row['chars'][target]
    vars_ = source['vars']
    formula = formula_for(audit, source['state'], source['elem'])
    rules = audit['globals']
    scale = as_fraction(rules['scale'])
    attack = as_fraction(rules['attackMultiplier'])
    defense = as_fraction(rules['defenseController'])
    target_hurt = victim['moveType'] == 65536
    expected_defense = float32(1 / float32(defense)) if target_hurt else 1.0
    require(source['attackBase'] == 100 and source['attackMul'] == float(attack) and vars_['fvar7'] == float(scale)
            and vars_['var40'] == vars_['var12'] == 0 and victim['finalDefense'] == expected_defense,
            f"F{row['frame']}: unsupported source modifier or defense")
    divisor = vars_['var23'] if formula['useContactDivisor'] else 1
    require(divisor in (1, 2) and vars_['var21'] in (0, 1, 2), f"F{row['frame']}: unsupported source divisor or variance")
    raw, damage = resolved_damage(formula, vars_['var21'], vars_['var6'] != 0, divisor,
                                  scale=scale, attack_multiplier=attack, target_hurt=target_hurt,
                                  defense_controller=defense)
    require(raw == source['hitDef']['damage'], f"F{row['frame']}: native HitDef damage differs from the source formula")
    if victim['guarded']:
        require(source['hitDef']['guardDamage'] == 0, f"F{row['frame']}: unsupported nonzero chip")
        damage = 0
    damage = min(damage, victim['life'])
    require(damage == victim['damage'], f"F{row['frame']}: native settled damage differs from the source formula")
    return {'frame': row['frame'], 'attacker': attacker + 1, 'state': source['state'], 'action': source['anim'],
            'element': source['elem'], 'var6': vars_['var6'], 'var21': vars_['var21'], 'var23': vars_['var23'],
            'rawDamage': raw, 'damage': damage, 'targetHurt': target_hurt, 'guarded': victim['guarded']}


def inspect_case(audit, metadata, directory, name):
    path = directory / f'{name}.jsonl'
    info = state200.load_json(directory / f'{name}-metadata.json')
    sha = archive_digest(path)
    mode, _, scenario = name.partition('-damage-')
    distance = (CASE_DISTANCES | OPTIONAL_CASE_DISTANCES).get(scenario)
    require(mode in PRESETS and distance is not None and info['case'] == name
            and info['trace_sha256'] == sha and info['initial_distance'] == distance
            and metadata['scenario_distance'][name] == distance
            and all(info[key] == metadata[key] for key in ('rng_seed', 'executable_sha256', 'config_sha256')),
            f'{name}: native case metadata mismatch')
    with path.open(encoding='utf-8') as stream:
        rows = [json.loads(line) for line in stream]
    require(all(row['case'] == name for row in rows), f'{name}: unexpected case identifier')
    draws = verify_random(rows, metadata['rng_seed'])
    variables = verify_damage_variables(rows)
    collision = [row for row in rows if row['phase'] == 'after-collision']
    end = [row for row in rows if row['phase'] == 'end']
    require(all([row['frame'] for row in series] == list(range(76)) for series in (collision, end)),
            f'{name}: missing native committed frames')
    expected_mode = 2 if mode == 'mode2' else 0
    require(collision[0]['chars'][0]['vars']['var10'] == expected_mode, f'{name}: wrong native mode')
    intro = state210.verify_mode_intro(directory / f'{name}-mode.jsonl', name) if mode == 'mode2' else None
    hitdefs = {}
    latest_draws = {}
    contacts = []
    for row in rows:
        if row['phase'] == 'var21-draw':
            latest_draws[row['seat']] = row
        elif row['phase'] == 'hitdef':
            owner = row['hitDefOwner'] - 1
            sampled = latest_draws.get(owner + 1)
            require(sampled is not None and sampled['frame'] == row['frame']
                    and sampled['value'] == row['chars'][owner]['vars']['var21'],
                    f'{name} F{row["frame"]}: HitDef does not use its current native var21 draw')
            hitdefs[(row['frame'], owner)] = row, sampled
        elif row['phase'] == 'after-collision':
            for target, victim in enumerate(row['chars']):
                if not victim['gethit']:
                    continue
                attacker = 1 - target
                evaluation = hitdefs.get((row['frame'], attacker))
                require(evaluation is not None, f'{name} F{row["frame"]}: missing matching native HitDef')
                hitdef, sampled = evaluation
                expected = expected_contact(audit, row, attacker, target, hitdef)
                require(victim['life'] - end[row['frame'] + 1]['chars'][target]['life'] == expected['damage'],
                        f'{name} F{row["frame"]}: native HP delta does not match settlement')
                contacts.append({**expected, 'var21Rng': sampled['rng']})
    require(contacts, f'{name}: no native damage contact')
    if scenario == 'carry-edge':
        require(len(contacts) == 1, f'{name}: expected one contact at the grace boundary')
        contact = contacts[0]
        if mode == 'default':
            require(contact['attacker'] == 1 and contact['state'] == 210 and contact['var6'] == 3,
                    f'{name}: final counter grace tick not observed')
        else:
            require(contact['attacker'] == 2 and contact['state'] == 200 and contact['var6'] == 1
                    and collision[contact['frame']]['chars'][0]['anim'] == 211,
                    f'{name}: expected the earlier interruption of Action 211')
    elif scenario.endswith('-guard'):
        expected_contacts = [(10, 210, 0)] if '-far-' in scenario else [(8, 215, 1), (22, 215, 2)]
        require([(contact['frame'], contact['state'], contact['var23']) for contact in contacts] == expected_contacts
                and all(contact['attacker'] == 1 and contact['guarded'] and contact['damage'] == 0 for contact in contacts),
                f'{name}: expected zero chip and the measured guarded contact sequence')
    return {'trace': path.name, 'sha256': sha, 'seed': metadata['rng_seed'], 'contacts': contacts,
            'modeIntro': intro, **draws, **variables}


def inspect_reference(audit, path):
    metadata = state200.load_json(path)
    require(metadata['engine'] == 'IKEMEN GO' and metadata['commit'] == state200.REFERENCE_COMMIT
            and metadata['capture_profile'] == 'Stage17', 'reference must be the reviewed Stage 17 IKEMEN GO capture')
    require(metadata['terry_archive_sha256'] == audit['zipSha256'], 'reference source archive hash mismatch')
    require(type(metadata['rng_seed']) is int and 0 < metadata['rng_seed'] < MODULUS, 'invalid native RNG seed')
    for key in ('cmd', 'st1', 'anim'):
        member = audit['members'][key]
        require(metadata['source_sha256'].get(member['name']) == member['sha256'],
                f"reference member hash mismatch: {member['name']}")
    globals_member = audit['members']['st4']
    if globals_member['name'] in metadata['source_sha256']:
        require(metadata['source_sha256'][globals_member['name']] == globals_member['sha256'],
                f"reference member hash mismatch: {globals_member['name']}")
    for key in ('executable_sha256', 'config_sha256'):
        require(re.fullmatch(r'[0-9a-f]{64}', metadata[key]) is not None, f'invalid reference {key}')
    names = metadata['executed_cases']
    require(len(set(names)) == len(names) and names, 'reference executed case list is empty or contains duplicates')
    cases = {name: inspect_case(audit, metadata, path.parent, name) for name in names}
    return {'metadataSha256': archive_digest(path), 'metadata': metadata, 'cases': cases}


def verify_coverage(references):
    covered = set().union(*(reference['cases'] for reference in references))
    required = {f'{mode}-damage-{scenario}' for mode in PRESETS for scenario in CASE_DISTANCES}
    require(required <= covered, f'missing native cases: {", ".join(sorted(required - covered))}')
    contacts = [contact for reference in references for case in reference['cases'].values() for contact in case['contacts']]
    require({contact['state'] for contact in contacts} == set(MOVE_IDS)
            and {contact['var21'] for contact in contacts} == {0, 1, 2}
            and {contact['var23'] for contact in contacts if contact['state'] == 215} == {1, 2}
            and any(contact['var6'] in (2, 3) for contact in contacts),
            'native cases do not cover all formula variants and counter grace')
    repeated = {}
    interruption = {}
    for reference in references:
        for name, case in reference['cases'].items():
            identity = case['seed'], name
            previous = repeated.setdefault(identity, case['contacts'])
            require(previous == case['contacts'], f'{name}: same-seed native contact samples changed')
            if name == 'default-damage-interrupt':
                contact, = case['contacts']
                require(contact['state'] == 200 and contact['var6'] == 1 and not contact['targetHurt'],
                        f'{name}: variance comparison must retain the same counter and defense state')
                interruption[case['seed']] = (contact['var21'], contact['damage'])
    require(len(interruption) >= 2 and len({sample[0] for sample in interruption.values()}) >= 2
            and len({sample[1] for sample in interruption.values()}) >= 2,
            'additional native seed must show changed variance and damage in the same interrupt scenario')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source_zip', type=Path)
    parser.add_argument('manifest', type=Path, nargs='?')
    parser.add_argument('--audit-only', action='store_true')
    parser.add_argument('--state200-reference', type=Path, help='Stage 14 run metadata')
    parser.add_argument('--state210-reference', type=Path, help='Stage 15 run metadata')
    parser.add_argument('--fractional-reference', type=Path, help='Stage 15 fractional boundary supplement')
    parser.add_argument('--state205215-reference', type=Path, help='Stage 16 run metadata')
    parser.add_argument('--reference', type=Path, action='append', help='Stage 17 run metadata, repeat for additional seeds')
    parser.add_argument('--output-dir', type=Path, help='fresh directory for the two generated Stage 17 presets')
    args = parser.parse_args()
    required = (args.manifest, args.state200_reference, args.state210_reference,
                args.state205215_reference, args.reference, args.output_dir)
    if args.audit_only and (any(required) or args.fractional_reference) or not args.audit_only and not all(required):
        parser.error('use SOURCE --audit-only or provide the Manifest, all four stage reference paths and --output-dir')
    try:
        audit = inspect_source(args.source_zip)
        if args.audit_only:
            print(json.dumps(audit, ensure_ascii=True, indent=2, sort_keys=True))
            return
        native = [inspect_reference(audit, path) for path in args.reference]
        verify_coverage(native)
        base, provenance = stage16_profiles(args.source_zip, args.manifest, args.state200_reference,
                                            args.state210_reference, args.fractional_reference, args.state205215_reference)
        docs = {}
        for mode in PRESETS:
            fighter, binding = build_profile(audit, *base[mode], mode)
            docs[f'{mode}.fighter.json'] = fighter
            docs[f'{mode}.binding.json'] = binding
            docs[f'{mode}.source.json'] = {
                **audit, **provenance, 'preset': mode,
                'references': [{**run, 'cases': {name: case for name, case in run['cases'].items()
                                                 if name.startswith(mode + '-')}} for run in native],
                'limits': [
                    'Native traces verify the formula and sampled variables. The runtime damage stream intentionally omits RNG draws unrelated to damage.',
                    'The opt-in rules model normal mode, neutral attack/defense constants, and delayed defense during hit reactions.',
                    'Other MUGEN states, Lua, helpers, full engine scheduling and damage-controller execution remain outside this preset.',
                ],
            }
        state200.output_documents(args.output_dir, docs)
    except (ValueError, OSError, TypeError, KeyError, IndexError, zipfile.BadZipFile) as error:
        parser.exit(2, f'Stage 17 damage conversion: {error}\n')


if __name__ == '__main__':
    main()
