"""Convert the two reviewed State 210 branches into separate Terry presets."""

import argparse
import copy
import json
import math
import re
import zipfile
from fractions import Fraction
from pathlib import Path

from . import state200
from .air import parse_air
from .state200 import (archive_digest, collision_boxes, fraction, normalized, number_in, output_documents,
                       read_sections, require, state, timeline, unique, validate_manifest)


MOVE_ID = 'TerryStandHeavyPunch210'
Q16_SCALE = 1 << 16
PRESETS = {'default': ('TerryS210', 'terry98-stage15-default'),
           'mode2': ('TerryS210Mode2', 'terry98-stage15-mode2')}
CONTROLLERS = ['changeanim', 'playsnd', 'playsnd', 'hitdef', 'hitdef',
               'varadd', 'varadd', 'statetypeset', 'changestate']
DISTANCES = {'whiff': 240, 'hit': 72, 'guard': 72, 'crouch-guard': 72, 'crouch-guard-late': 72, 'mirror': 72,
             'close-60': 60, 'close-64': 64, 'far-65': 65, 'boundary-86': 86, 'boundary-87': 87,
             'trade': 72, 'interrupt': 72}
FRACTIONAL_CASE = 'far-64p5'
GUARDS = {'guard', 'crouch-guard', 'crouch-guard-late'}
HITS = {'hit', 'mirror', 'far-65', 'boundary-86', FRACTIONAL_CASE}


def source_move(sections):
    definition, controllers = state(sections, 210)
    expected = {'type': 's', 'movetype': 'a', 'physics': 'n', 'velset': '0,0',
                'ctrl': '0', 'anim': '210', 'facep2': '1'}
    require(all(normalized(definition.one(key)) == value for key, value in expected.items()),
            'State 210 entry does not match reviewed StateDef')
    types = [controller.one('type').lower() for controller in controllers]
    require(types == CONTROLLERS, f'unsupported State 210 controller sequence at line {definition.line}: {types}')
    mode, _, _, hit, inactive, _, _, recovery, exit_state = controllers
    require({normalized(value) for value in mode.values('trigger1')} == {'time=0', 'var(10)=2'}
            and mode.one('value') == '211', 'unsupported State 210 mode branch')
    require({normalized(value) for value in hit.values('triggerall')} == {'var(12)=0', '!movecontact&&!movereversed'}
            and {normalized(value) for value in inactive.values('triggerall')} == {'var(12)>0'}
            and inactive.one('attr') == '', 'unsupported State 210 HitDef branch conditions')
    require(normalized(hit.one('attr')) == 's,na' and normalized(hit.one('guardflag')) == 'm'
            and normalized(hit.one('hitflag')) == 'maf', 'unsupported State 210 hit eligibility')
    bounds = {}
    for trigger, expected_action in (('trigger1', 210), ('trigger2', 211)):
        condition = unique((v for v in hit.values(trigger) if 'animelemtime' in v.lower()), f'HitDef {trigger}')
        action, first, following = number_in(condition, r'anim=(\d+)&&animelemtime\((\d+)\)>=0&&animelemtime\((\d+)\)<0',
                                             f'State 210 HitDef {trigger}')
        require(action == expected_action and normalized(inactive.one(trigger)) == normalized(condition)
                and normalized(recovery.one(trigger)) == f'anim={action}&&animelem={following}',
                f'State 210 activity/recovery disagreement for Action {expected_action}')
        bounds[action] = (first, following)
    require(normalized(recovery.one('movetype')) == 'i' and normalized(exit_state.one('trigger1')) == 'animtime=0'
            and exit_state.one('value') == '0' and exit_state.one('ctrl') == '1', 'unsupported State 210 exit')
    return bounds, {'id': 210, 'definitionLine': definition.line, 'modeChangeLine': mode.line, 'hitDefLine': hit.line,
                    'recoveryLine': recovery.line, 'exitLine': exit_state.line,
                    'hitDef': {key: hit.one(key) for key in ('damage', 'pausetime', 'guard.pausetime',
                                                            'ground.velocity', 'ground.slidetime', 'ground.hittime',
                                                            'guardflag', 'hitflag')}}


def command_rules(sections, constants):
    key = unique((section for section in sections if section.title == 'command'
                  and section.one('name', '').lower() == '"y"'), 'single-button command y')
    defaults = unique((section for section in sections if section.title == 'defaults'), 'command defaults')
    require(key.one('command').lower() == 'y', 'reviewed y command must be a single button')
    commands = [section for section in sections if section.title.startswith('state -1,')
                and any(normalized(value).startswith('command="y"||') for value in section.values('triggerall'))]
    far, near = (unique((section for section in commands if section.one('type', '').lower() == 'changestate'
                         and section.one('value', '') == str(number)), f'human command entry {number}')
                 for number in (210, 215))
    require(near.line < far.line and {'roundstate=2', 'command!="holddown"', 'statetype!=a'}.issubset(
            map(normalized, far.values('triggerall')))
            and any(normalized(value).startswith('ctrl||') for value in far.values('trigger1')),
            'cannot verify State 210 standing command entry and near precedence')
    condition = unique((value for value in near.values('triggerall') if 'p2bodydist' in value.lower()),
                       'State 215 distance condition')
    (max_body_distance,) = number_in(condition, r'p2bodydistx<=(\d+)', 'State 215 distance condition')
    size = unique((section for section in constants if section.title == 'size'), 'Size constants')
    front = int(size.one('ground.front'))
    max_near = max_body_distance + 2 * front
    return {'keyLine': key.line, 'farEntryLine': far.line, 'nearEntryLine': near.line,
            'time': int(key.one('time', defaults.one('command.time'))),
            'bufferTime': int(key.one('buffer.time', defaults.one('command.buffer.time'))),
            'nearBodyDistance': max_body_distance, 'groundFront': front,
            'maximumNearCenterDistance': max_near,
            'minimumStartDistance': fraction(Fraction(max_near * Q16_SCALE + 1, Q16_SCALE))}


def action_branch(action, first, following):
    startup, active, recovery = timeline(action, first, following)
    clsn1 = collision_boxes(action, 1, normalize_edges=True)
    active_boxes = clsn1[first-1:following-1]
    require(len(active_boxes[0]) == 1 and all(boxes == active_boxes[0] for boxes in active_boxes)
            and not any(clsn1[:first-1] + clsn1[following-1:]),
            f"Action {action['action']}: requires one unchanged Clsn1 box only during the active interval")
    return {'id': action['action'], 'startup': startup, 'active': active, 'recovery': recovery,
            'strike': active_boxes[0][0], 'clsn1': clsn1, 'clsn2': collision_boxes(action, 2, normalize_edges=True),
            'durations': [frame['time'] for frame in action['frames']]}


def inspect_source(zip_path, manifest):
    zip_sha, members, contents = state200.read_terry_archive(zip_path)
    actions, _ = parse_air(contents['anim'].decode('latin-1'))
    bounds, move = source_move(read_sections(contents['st1'].decode('latin-1')))
    selected = {}
    for name, number in (('default', 210), ('mode2', 211)):
        action = unique((item for item in actions if item['action'] == number), f'AIR Action {number}')
        selected[name] = action_branch(action, *bounds[number])
        selected[name]['spriteKeys'] = [[frame['group'], frame['image']] for frame in action['frames']]
        selected[name]['spriteIndices'] = validate_manifest(manifest, state200.manifest_hashes(zip_sha, members), action)
    related = {}
    for number in (0, 1, 5002, 5007):
        action = unique((item for item in actions if item['action'] == number), f'AIR Action {number}')
        related[number] = {'durations': [frame['time'] for frame in action['frames']],
                           'spriteIndices': validate_manifest(manifest, state200.manifest_hashes(zip_sha, members), action)}
    idle_actions = [unique((a for a in actions if a['action'] == number), f'idle AIR Action {number}') for number in (0, 1)]
    require(collision_boxes(idle_actions[0], 2) == collision_boxes(idle_actions[1], 2),
            'mode 2 idle geometry differs from the State 200 base')
    command = command_rules(read_sections(contents['cmd'].decode('latin-1')), read_sections(contents['cns'].decode('latin-1')))
    return {'format': 1, 'zipSha256': zip_sha, 'manifestSha256': archive_digest(manifest), 'members': members,
            'state': move, 'command': command, 'actions': selected, 'relatedActions': related}


def verify_mode_intro(path, name):
    with path.open(encoding='utf-8') as stream:
        rows = [json.loads(line) for line in stream]
    require(rows and all(row['case'] == name and row['palNo'] == 7 for row in rows), f'{name}: missing mode intro trace')
    first_mode = next((i for i, row in enumerate(rows) if row['var10'] == 2), None)
    require(first_mode is not None and first_mode > 0, f'{name}: mode 2 did not activate after intro input')
    grant = [row for row in rows[:first_mode] if row['phase'] == 'after-command' and row['raw']]
    require([(row['introFrame'], row['raw']) for row in grant] == [(1, 1), (3, 1), (5, 2), (7, 2), (9, 1), (11, 2)]
            and grant[-1]['grantBuffer'] > 0 and grant[-1]['var10'] == 1
            and rows[first_mode]['phase'] == 'end' and rows[first_mode]['introFrame'] == grant[-1]['introFrame'],
            f'{name}: missing original Grant command evidence')
    return {'trace': path.name, 'sha256': archive_digest(path), 'activationFrame': rows[first_mode]['introFrame']}


def reference_case(audit, directory, metadata, mode, scenario):
    name = f'{mode}-{scenario}'
    path = directory / f'{name}.jsonl'
    detail = state200.load_json(directory / f'{name}-metadata.json')
    distance = 64.5 if scenario == FRACTIONAL_CASE else DISTANCES[scenario]
    trace_sha = archive_digest(path)
    require(detail['case'] == name and detail['initial_distance'] == metadata['scenario_distance'][name] == distance
            and detail['executable_sha256'] == metadata['executable_sha256']
            and detail['config_sha256'] == metadata['config_sha256'] and detail['trace_sha256'] == trace_sha,
            f'{name}: per-case reference metadata or trace SHA mismatch')
    groups = state200.trace_rows(path, name, frame_count=76)
    first = groups['before-command'][0]['chars']
    facing = -1 if scenario == 'mirror' else 1
    require(first[0]['facing'] == facing and first[1]['facing'] == -facing
            and abs(first[1]['pos'][0] - first[0]['pos'][0]) == distance
            and first[0]['pos'][1] == first[1]['pos'][1] == 0,
            f'{name}: reference distance, facing or ground position differs from fixture')
    expected_mode = 2 if mode == 'mode2' else 0
    require(first[0]['vars']['var10'] == expected_mode, f'{name}: expected original var(10)={expected_mode}')
    mode_intro = verify_mode_intro(directory / f'{name}-mode.jsonl', name) if mode == 'mode2' else None
    collisions, end = groups['after-collision'], groups['end']
    state_id = 215 if scenario in ('close-60', 'close-64') else 210
    entry = next((row['frame'] for row in collisions if row['chars'][0]['state'] == state_id), None)
    require(entry is not None, f'{name}: did not enter State {state_id}')
    require(all(row['chars'][0]['state'] not in (210, 215) for row in collisions[:entry]),
            f'{name}: unexpectedly entered another normal state before expected entry')
    before = groups['before-command'][entry]['chars'][0]
    require(before['commands']['y'] and before['raw'] != 0 and before['ctrl'], f'{name}: missing controllable physical y entry')
    command = audit['command']
    body_dist = before['bodyDistX']
    require(body_dist == distance - 2 * command['groundFront']
            and (body_dist <= command['nearBodyDistance']) == (state_id == 215),
            f'{name}: distance branch does not agree with the source State 215 priority')
    result = {'trace': path.name, 'sha256': trace_sha, 'distance': distance,
              'attackerFacing': facing, 'state': state_id, 'entryFrame': entry,
              'bodyDistance': body_dist, 'modeIntro': mode_intro}
    if state_id == 215:
        require(all(row['chars'][0]['state'] != 210 for row in collisions), f'{name}: close branch did not suppress State 210')
        return result
    branch = audit['actions'][mode]
    if scenario in ('trade', 'interrupt'):
        return {**result, **verify_mixed(name, scenario, branch, groups, entry)}
    exit_frame = next((row['frame'] for row in collisions[entry:] if row['chars'][0]['state'] != 210), None)
    require(exit_frame is not None and collisions[exit_frame]['chars'][0]['state'] == 0
            and collisions[exit_frame]['chars'][0]['ctrl']
            and collisions[exit_frame]['chars'][0]['anim'] == (1 if mode == 'mode2' else 0),
            f'{name}: missing State 0 control recovery or wrong idle action')
    require(all(row['chars'][0]['vars']['var10'] == expected_mode for row in collisions[entry:exit_frame]),
            f'{name}: mode changed during the sampled move')
    contacts = [row for row in collisions if row['chars'][1]['gethit']]
    outcome = None
    if scenario in GUARDS | HITS:
        contact = unique(contacts, f'{name} contact')
        require(contact['frame'] == entry + branch['startup'], f'{name}: expected first-active contact')
        crouching = scenario.startswith('crouch-guard')
        outcome = state200.verify_contact(name, groups, contact, end, blocked=scenario in GUARDS,
                                         crouching=crouching, verify_late_crouch=False)
        if scenario in HITS:
            outcome['reaction'] = verify_heavy_reaction(name, collisions, contact['frame'], outcome)
        if crouching:
            target = end[contact['frame']+1]['chars'][1]
            require(target['stateType'] == 2 and target['anim'] == 151, f'{name}: expected Action 151 crouch guard')
    else:
        require(not contacts and end[-1]['chars'][1]['life'] == first[1]['life'], f'{name}: unexpected contact')
        active = range(entry + branch['startup'], entry + branch['startup'] + branch['active'])
        require([row['frame'] for row in groups['hitdef'] if row['hitDefOwner'] == 1] == list(active),
                f'{name}: original HitDef window disagrees with AIR')
    state200.verify_attack_timeline(branch, collisions, entry, exit_frame, outcome['pause']['attackerFrames'] if outcome else 0,
                                    state_id=210, normalize_edges=True)
    return {**result, 'exitFrame': exit_frame, 'outcome': outcome}


def verify_heavy_reaction(name, collisions, contact, outcome):
    shake_end = contact + outcome['pause']['defenderFrames']
    initial = collisions[contact+1]['chars'][1]
    followup = next((row for row in collisions[shake_end+1:outcome['controlFrame']]
                     if row['chars'][1]['anim'] == 5007), None)
    require(initial['anim'] == 5002 and followup is not None and followup['chars'][1]['elem'] == 0
            and all(row['chars'][1]['anim'] == 5002 for row in collisions[contact+1:followup['frame']]),
            f'{name}: heavy victim animation was not 5002 then 5007')
    return {'initialAction': initial['anim'], 'followupAction': followup['chars'][1]['anim'],
            'followupTick': followup['frame'] - shake_end}


def verify_mixed(name, scenario, branch, groups, entry):
    trade = scenario == 'trade'
    collision, end = groups['after-collision'], groups['end']
    hits = [(row['frame'], seat) for row in collision for seat, character in enumerate(row['chars'])
            if character['gethit']]
    expected_frame = entry + (branch['startup'] if trade else 3)
    expected_hits = [(expected_frame, 0), (expected_frame, 1)] if trade else [(expected_frame, 0)]
    require(hits == expected_hits, f'{name}: wrong simultaneous or interrupt contact')
    pair = collision[expected_frame]['chars']
    require(pair[0]['state'] == 210 and pair[0]['anim'] == branch['id'] and pair[1]['state'] == 200,
            f'{name}: wrong attack instances at contact')
    require(all(row['chars'][0]['state'] != 210 for row in collision[expected_frame+1:]),
            f'{name}: interrupted heavy attack leaked a later move state')
    expected_owners = {1, 2} if trade else {2}
    require(len(groups['hitdef']) == len(expected_owners)
            and {row['hitDefOwner'] for row in groups['hitdef'] if row['frame'] == expected_frame} == expected_owners,
            f'{name}: interrupted attack executed an unexpected HitDef')
    outcomes = {}
    for row in groups['hitdef']:
        attacker = row['hitDefOwner'] - 1
        character = row['chars'][attacker]
        target = 1 - attacker
        raw = character['hitDef']['damage']
        hp_loss = pair[target]['life'] - end[expected_frame+1]['chars'][target]['life']
        require(character['vars']['var6'] == 1 and hp_loss == pair[target]['damage'] and hp_loss > 0
                and not pair[target]['guarded'], f'{name}: missing counter hit damage and script variable evidence')
        outcomes['heavy' if attacker == 0 else 'light'] = {
            'damage': hp_loss, 'rawHitDefDamage': raw, 'var6': character['vars']['var6'],
            'var21': character['vars']['var21'], 'hitPause': character['hitDef']['hitPause'],
            'nextAction': end[expected_frame+1]['chars'][target]['anim'],
        }
    return {'contactFrame': expected_frame, 'outcomes': outcomes}


def inspect_reference(audit, path, base_audit):
    metadata = state200.load_json(path)
    require(metadata['engine'] == 'IKEMEN GO' and metadata['commit'] == state200.REFERENCE_COMMIT
            and metadata['capture_profile'] == 'Stage15', 'reference must use the reviewed Stage 15 IKEMEN GO capture')
    require(metadata['terry_archive_sha256'] == audit['zipSha256'], 'reference source archive hash mismatch')
    for key in ('cmd', 'st1', 'anim'):
        member = audit['members'][key]
        require(metadata['source_sha256'].get(member['name']) == member['sha256'],
                f"reference member hash mismatch: {member['name']}")
    for key in ('executable_sha256', 'config_sha256'):
        require(re.fullmatch(r'[0-9a-f]{64}', metadata[key]) is not None, f'missing reference {key}')
    require('-p1 terry/Terry-KOF98.def -p2 terry/Terry-KOF98.def' in metadata['arguments'],
            'reference must use the reviewed Terry character for both seats')
    cases = {}
    for mode in PRESETS:
        cases[mode] = {scenario: reference_case(audit, path.parent, metadata, mode, scenario) for scenario in DISTANCES}
        hit = cases[mode]['hit']['outcome']
        guard = cases[mode]['guard']['outcome']
        require(all(same_settlement(cases[mode][name]['outcome'], hit) for name in ('mirror', 'far-65', 'boundary-86')),
                f'{mode}: mirrored or edge hit differs from direct hit')
        ground = base_audit['movement']['groundReaction']
        expected = {key: float(Fraction(ground[key]['numerator'], ground[key]['denominator'])) for key in
                    ('velocityRetention', 'crouchGuardVelocityRetention')}
        require(math.isclose(hit['pushRetention'], expected['velocityRetention'])
                and math.isclose(guard['pushRetention'], expected['velocityRetention']),
                f'{mode}: standing ground friction differs from source')
        for name in ('crouch-guard', 'crouch-guard-late'):
            crouch = cases[mode][name]['outcome']
            require(math.isclose(crouch['pushRetention'], expected['crouchGuardVelocityRetention'])
                    and all(crouch[key] == guard[key] for key in ('damage', 'pause', 'effectiveStun', 'initialPush')),
                    f'{mode}: unsupported crouch guard reaction')
        edge = cases[mode]['boundary-86']['distance']
        near_body = max(box[2] for box in base_audit['groundHurtboxes']['standing'])
        require(edge == audit['actions'][mode]['strike'][2] + near_body,
                f'{mode}: measured edge does not match AIR strike and idle defender geometry')
        require(cases[mode]['trade']['outcomes']['light'] == cases[mode]['interrupt']['outcomes']['light'],
                f'{mode}: counter light settlement differs between trade and interrupt')
    for scenario in ('trade', 'interrupt'):
        require(cases['default'][scenario]['outcomes'] == cases['mode2'][scenario]['outcomes'],
                f'{scenario}: default and mode 2 counter settlements differ')
    return {'metadataSha256': archive_digest(path), 'metadata': metadata, 'cases': cases}


def same_settlement(first, second):
    return all(first[key] == second[key] for key in first.keys() - {'targetTravel'}) \
        and math.isclose(first['targetTravel'], second['targetTravel'], abs_tol=0.0001)


def inspect_fractional_reference(audit, main_reference, path):
    metadata = state200.load_json(path)
    identity = ('engine', 'commit', 'executable_sha256', 'terry_archive_sha256',
                'source_sha256', 'config_sha256', 'arguments', 'capture_profile')
    require(all(metadata[key] == main_reference['metadata'][key] for key in identity),
            'fractional reference metadata differs from the canonical engine, source or configuration')
    cases = {}
    for mode in PRESETS:
        candidate = reference_case(audit, path.parent, metadata, mode, FRACTIONAL_CASE)
        standard = main_reference['cases'][mode]['hit']['outcome']
        require(candidate['state'] == 210 and same_settlement(candidate['outcome'], standard),
                f'{mode}: fractional far hit differs from direct hit')
        cases[mode] = candidate
    return {'metadataSha256': archive_digest(path), 'cases': cases}


def hurtbox_timeline(action):
    return [
        {'frames': duration, 'boxes': [
            {'minX': fraction(x1), 'maxX': fraction(x2), 'minY': fraction(-y2), 'maxY': fraction(-y1)}
            for x1, y1, x2, y2 in boxes
        ]}
        for duration, boxes in zip(action['durations'], action['clsn2'], strict=True)
    ]


def attack_fields(branch, hit, guard, retention):
    require(guard['damage'] == 0, 'nonzero chip is outside the State 210 contract')
    left, top, right, bottom = branch['strike']
    coefficient = Fraction(retention['numerator'], retention['denominator'])
    return {
        'offsetForward': fraction(Fraction(left + right, 2)), 'offsetUp': fraction(Fraction(-top - bottom, 2)),
        'halfWidth': fraction(Fraction(right - left, 2)), 'halfHeight': fraction(Fraction(bottom - top, 2)),
        'firstActiveFrame': 0, 'lastActiveFrame': branch['active'] - 1, 'height': 'Mid',
        'damage': hit['damage'], 'hitstunFrames': hit['effectiveStun'], 'blockstunFrames': guard['effectiveStun'],
        'groundKnockback': fraction(hit['initialPush']), 'blockKnockback': fraction(guard['initialPush']),
        'groundKnockbackFriction': fraction(Fraction(str(hit['initialPush'])) * (1 - coefficient)),
        'launchVelocityX': fraction(0), 'launchVelocityY': fraction(0), 'knockdownFrames': 0, 'wakeupFrames': 0,
        'hitstopFrames': max(hit['pause'].values()), 'hitstopByOutcome': {'hit': hit['pause'], 'block': guard['pause']},
        'contactOnEdge': True,
    }


def build_profile(audit, reference, base_fighter, base_binding, mode, base_action=None):
    branch = audit['actions'][mode]
    fighter, binding = copy.deepcopy((base_fighter, base_binding))
    fighter['fighterId'], binding['visualId'] = PRESETS[mode]
    fighter['displayName'] = f"Terry State 200 + 210 ({'default' if mode == 'default' else 'mode 2'})"
    command = audit['command']
    fighter['commands'].append({
        'commandId': '5B', 'directions': [], 'buttons': ['HeavyPunch'],
        'maxSequenceFrames': command['time'], 'buttonToleranceFrames': 0, 'priority': 0,
        'bufferLifetimeFrames': command['bufferTime'], 'chargeHoldFrames': 0, 'chargeReleaseToleranceFrames': 0,
    })
    reaction = base_fighter['movement']['groundReaction']
    counter = reference['trade']['outcomes']
    hit = reference['hit']['outcome']
    attack = attack_fields(branch, hit, reference['guard']['outcome'], reaction['velocityRetention'])
    attack['counterDamage'] = counter['heavy']['damage']
    fighter['moves'][0]['attacks'][0]['counterDamage'] = counter['light']['damage']
    if base_action is not None:
        fighter['moves'][0]['hurtboxTimeline'] = hurtbox_timeline(base_action)
    fighter['moves'].append({
        'moveId': MOVE_ID, 'commandId': '5B', 'allowedFrom': 'Standing',
        'minimumStartDistance': command['minimumStartDistance'],
        'startupFrames': branch['startup'], 'activeFrames': branch['active'], 'recoveryFrames': branch['recovery'],
        'attacks': [attack],
        'cancels': [], 'throws': [], 'projectiles': [], 'hurtboxTimeline': hurtbox_timeline(branch),
    })
    binding['clips']['state210'] = {'action': branch['id'], 'clock': 'move', 'loop': False}
    if mode == 'mode2':
        binding['clips']['idle']['action'] = 1
    binding['moves'][MOVE_ID] = {phase: 'state210' for phase in ('Startup', 'Active', 'Recovery')}
    binding['states'] = {'Blockstun': 'guard', 'Walking': 'walk', 'PreJump': 'jump', 'Airborne': 'jump'}
    timing = hit['reaction']
    binding['clips']['heavy_hurt'] = {'action': timing['initialAction'], 'clock': 'phase', 'loop': False}
    binding['clips']['heavy_hurt_followup'] = {'action': timing['followupAction'], 'clock': 'phase', 'loop': False}
    binding['hitReactions'] = {MOVE_ID: {'initial': 'heavy_hurt', 'followup': 'heavy_hurt_followup',
                                        'followupTick': timing['followupTick']}}
    return fighter, binding


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source_zip', type=Path)
    parser.add_argument('manifest', type=Path)
    parser.add_argument('--audit-only', action='store_true', help='print static provenance without converting')
    parser.add_argument('--state200-reference', type=Path, help='canonical Stage 14 reference run metadata')
    parser.add_argument('--reference', type=Path, help='canonical Stage 15 reference run metadata')
    parser.add_argument('--fractional-reference', type=Path, help='same-engine supplemental 64.5 distance metadata')
    parser.add_argument('--output-dir', type=Path, help='directory for both generated fighter and binding presets')
    args = parser.parse_args()
    inputs = (args.state200_reference, args.reference, args.output_dir)
    if args.audit_only and (any(inputs) or args.fractional_reference) or not args.audit_only and not all(inputs):
        parser.error('use either --audit-only, or all of --state200-reference, --reference and --output-dir')
    try:
        audit = inspect_source(args.source_zip, args.manifest)
        if args.audit_only:
            print(json.dumps(audit, ensure_ascii=True, indent=2, sort_keys=True))
            return
        base = state200.inspect_source(args.source_zip, args.manifest)
        reference = inspect_reference(audit, args.reference, base)
        fractional = inspect_fractional_reference(audit, reference, args.fractional_reference) if args.fractional_reference else None
        base_reference = state200.inspect_reference(base, args.state200_reference)
        base_docs = state200.build_documents(base, base_reference)
        documents = {}
        for mode in PRESETS:
            fighter, binding = build_profile(audit, reference['cases'][mode], base_docs['state200.fighter.json'],
                                             base_docs['state200.binding.json'], mode, base['action'])
            documents[f'{mode}.fighter.json'] = fighter
            documents[f'{mode}.binding.json'] = binding
            documents[f'{mode}.source.json'] = {
                **audit, 'preset': mode, 'reference': {**reference, 'cases': reference['cases'][mode]},
                **({'fractionalReference': {'metadataSha256': fractional['metadataSha256'],
                                            'case': fractional['cases'][mode]}} if fractional else {}),
                'state200ReferenceSha256': base_reference['metadataSha256'],
                'projection': {'mode': 'Select the original action as a fixed preset before the duel.',
                               'distance': 'MUGEN 1.0 compares P2BodyDist X without integer truncation. Terry-versus-Terry near priority includes center separation 64. The far preset accepts the next representable Q16 distance.',
                               'damage': 'Use the measured life reduction on normal first-active ground contact.',
                               'counterDamage': 'Use the sampled life reduction against opposing Startup/Active. Original script var(21) is 1 for the light sample and 0 for the heavy sample.',
                               'limits': ['Native mode selection and close State 215 are not playable in these presets.',
                                          'Original dynamic damage and counter grace after MoveType changes are outside this sample.',
                                          'Other states keep the Stage 14 runtime rules.']},
            }
        output_documents(args.output_dir, documents)
    except (ValueError, OSError, TypeError, KeyError, IndexError, zipfile.BadZipFile) as error:
        parser.exit(2, f'State 210 conversion: {error}\n')


if __name__ == '__main__':
    main()
