"""Audit and convert Terry's near standing normal attacks from source and reference runs."""

import argparse
import copy
import json
import math
import re
import zipfile
from fractions import Fraction
from pathlib import Path

from . import state200, state210
from .air import parse_air
from .state200 import archive_digest, collision_boxes, normalized, number_in, read_sections, require, timeline, unique, validate_manifest


PRESETS = {'default': ('TerryNormals', 'terry98-stage16-default'),
           'mode2': ('TerryNormalsMode2', 'terry98-stage16-mode2')}
MOVE_IDS = {205: 'TerryStandLightPunch205', 215: 'TerryStandHeavyPunch215'}
CONTROLLERS = {
    205: ['playsnd', 'playsnd', 'hitdef', 'hitdef', 'varadd', 'varadd', 'statetypeset', 'changestate'],
    215: ['varset', 'varadd', 'playsnd', 'playsnd', 'movehitreset', 'hitdef', 'hitdef', 'hitdef',
          'varadd', 'varadd', 'varadd', 'varadd', 'statetypeset', 'changestate'],
}
CASES = {
    205: {'hit': 44, 'guard': 44, 'crouch-guard': 44, 'crouch-late': 44, 'mirror': 44,
          'whiff': 36, 'trade': 44, 'boundary-46': 46, 'far-46p5': 46.5, 'far-47': 47},
    215: {'hit': 62, 'guard': 62, 'crouch-guard': 62, 'crouch-late': 62, 'mirror': 62,
          'whiff': 54, 'trade': 62, 'sample-60': 60, 'boundary-64': 64, 'far-64p5': 64.5,
          'far-65': 65, 'second-only': 64, 'second-guard': 64},
}
GUARDS = {'guard', 'crouch-guard', 'crouch-late', 'second-guard'}


def move_scope(sections, number):
    index = unique((i for i, section in enumerate(sections) if section.title == f'statedef {number}'),
                   f'StateDef {number}')
    end = next((i for i in range(index + 1, len(sections)) if sections[i].title.startswith('statedef ')),
               len(sections))
    # MUGEN scopes a controller to the last StateDef, even if its comment label names another state.
    return sections[index], [section for section in sections[index+1:end] if section.title.startswith('state ')]


def hit_window(controller):
    condition = unique((value for value in controller.values('trigger1') if 'animelemtime' in value.lower()),
                       f'line {controller.line} HitDef animation window')
    return number_in(condition, r'animelemtime\((\d+)\)>=0&&animelemtime\((\d+)\)<0',
                     f'line {controller.line} HitDef animation window')


def source_move(sections, number):
    definition, controllers = move_scope(sections, number)
    expected = {'type': 's', 'movetype': 'a', 'physics': 'n', 'velset': '0,0',
                'ctrl': '0', 'anim': str(number), 'facep2': '1'}
    require(all(normalized(definition.one(key)) == value for key, value in expected.items()),
            f'State {number} entry does not match the reviewed StateDef')
    types = [control.one('type').lower() for control in controllers]
    require(types == CONTROLLERS[number], f'unsupported State {number} controller sequence: {types}')
    hitdefs = [control for control in controllers if control.one('type').lower() == 'hitdef']
    inactive = hitdefs.pop()
    require(normalized(inactive.one('triggerall')) == 'var(12)>0' and inactive.one('attr') == '',
            f'State {number}: unsupported inactive HitDef branch')
    bounds = []
    for hit in hitdefs:
        require({normalized(value) for value in hit.values('triggerall')} == {'var(12)=0', '!movecontact&&!movereversed'}
                and normalized(hit.one('attr')) == 's,na' and normalized(hit.one('guardflag')) == 'm'
                and normalized(hit.one('hitflag')) == 'maf',
                f'line {hit.line}: unsupported HitDef eligibility')
        bounds.append(hit_window(hit))
    first, following = bounds[0][0], bounds[-1][1]
    require(hit_window(inactive) == (first, following), f'State {number}: inactive HitDef window differs')
    recovery, exit_state = controllers[-2:]
    require(normalized(recovery.one('trigger1')) == f'animelem={following}'
            and normalized(recovery.one('movetype')) == 'i' and normalized(exit_state.one('trigger1')) == 'animtime=0'
            and exit_state.one('value') == '0' and exit_state.one('ctrl') == '1',
            f'State {number}: unexpected recovery or exit condition')
    reset_line = None
    if len(bounds) > 1:
        reset = unique((control for control in controllers if control.one('type').lower() == 'movehitreset'),
                       'State 215 MoveHitReset')
        require(normalized(reset.one('trigger1')) == f'animelem={bounds[1][0]}' and reset.line < hitdefs[1].line,
                'State 215 reset must precede the second HitDef')
        reset_line = reset.line
        initial, increment = controllers[:2]
        require(normalized(initial.one('trigger1')) == 'time=0' and initial.one('var(23)') == '1'
                and {normalized(value) for value in increment.values('trigger1')} == {'var(6)=0', 'movecontact=1'}
                and increment.one('var(23)') == '1', 'unsupported State 215 damage scaling controller')
    fields = ('damage', 'pausetime', 'guard.pausetime', 'ground.velocity', 'ground.slidetime', 'ground.hittime',
              'guardflag', 'hitflag', 'animtype', 'id')
    return bounds, {
        'id': number, 'definitionLine': definition.line, 'resetLine': reset_line,
        'recoveryLine': recovery.line, 'exitLine': exit_state.line,
        'hitDefs': [{'line': hit.line, **{key: hit.one(key) for key in fields}} for hit in hitdefs],
    }


def action_profile(action, bounds):
    require(bounds and all(first < following for first, following in bounds)
            and all(previous[1] == following[0] for previous, following in zip(bounds, bounds[1:])),
            f"Action {action['action']}: hit windows must be ordered and adjacent")
    first, following = bounds[0][0], bounds[-1][1]
    startup, active, recovery = timeline(action, first, following)
    durations = [frame['time'] for frame in action['frames']]
    clsn1 = collision_boxes(action, 1, normalize_edges=True)
    strikes = []
    for start, stop in bounds:
        boxes = clsn1[start-1:stop-1]
        require(len(boxes[0]) == 1 and all(box == boxes[0] for box in boxes),
                f"Action {action['action']}: each HitDef requires one constant box within its window")
        strikes.append({'first': sum(durations[first-1:start-1]), 'last': sum(durations[first-1:stop-1]) - 1,
                        'box': boxes[0][0]})
    require(not any(clsn1[:first-1] + clsn1[following-1:]),
            f"Action {action['action']}: stray Clsn1 outside HitDef windows")
    return {'id': action['action'], 'startup': startup, 'active': active, 'recovery': recovery, 'strikes': strikes,
            'durations': durations, 'clsn1': clsn1, 'clsn2': collision_boxes(action, 2, normalize_edges=True),
            'spriteKeys': [[frame['group'], frame['image']] for frame in action['frames']]}


def source_commands(sections, constants):
    result = {}
    defaults = unique((section for section in sections if section.title == 'defaults'), 'command defaults')
    for key, near_state, far_state in (('x', 205, 200), ('y', 215, 210)):
        button = unique((section for section in sections if section.title == 'command'
                         and section.one('name', '').lower() == f'"{key}"'), f'single-button command {key}')
        require(normalized(button.one('command')) == key, f'unsupported physical command {key}')
        candidates = [section for section in sections if section.title.startswith('state -1,')
                      and any(normalized(value).startswith(f'command="{key}"||') for value in section.values('triggerall'))]
        near, far = (unique((section for section in candidates if section.one('type', '').lower() == 'changestate'
                            and section.one('value', '') == str(number)), f'human command entry {number}')
                     for number in (near_state, far_state))
        require(near.line < far.line and {'roundstate=2', 'command!="holddown"', 'statetype!=a'}.issubset(
                map(normalized, near.values('triggerall')))
                and any(normalized(value).startswith('ctrl||') for value in near.values('trigger1')),
                f'cannot verify State {near_state} standing near command priority')
        condition = unique((value for value in near.values('triggerall') if 'p2bodydist' in value.lower()),
                           f'State {near_state} distance condition')
        (maximum,) = number_in(condition, r'p2bodydistx<=(\d+)', f'State {near_state} distance condition')
        result[near_state] = {'physicalButton': key, 'buttonLine': button.line, 'nearEntryLine': near.line,
                              'farEntryLine': far.line, 'maximumStartBodyDistance': maximum,
                              'time': int(button.one('time', defaults.one('command.time'))),
                              'bufferTime': int(button.one('buffer.time', defaults.one('command.buffer.time')))}
    require(result[205]['farEntryLine'] < result[215]['nearEntryLine'], 'source light must precede both heavy branches')
    size = unique((section for section in constants if section.title == 'size'), 'Size constants')
    widths = {posture: {side: state200.fraction(size.one(f'{posture}.{side}')) for side in ('front', 'back')}
              for posture in ('ground', 'air')}
    return result, widths


def neutral_air_hurtboxes(actions):
    frames = [boxes for action in actions for boxes in collision_boxes(action, 2, normalize_edges=True)]
    require(frames and frames[0] and all(boxes == frames[0] for boxes in frames),
            'neutral Actions 41 and 49 must share common constant Clsn2')
    return frames[0]


def inspect_source(zip_path, manifest):
    zip_sha, members, contents = state200.read_terry_archive(zip_path)
    actions, _ = parse_air(contents['anim'].decode('latin-1'))
    hashes = state200.manifest_hashes(zip_sha, members)
    sections = read_sections(contents['st1'].decode('latin-1'))
    commands, widths = source_commands(read_sections(contents['cmd'].decode('latin-1')),
                                        read_sections(contents['cns'].decode('latin-1')))
    result = {}
    for number in MOVE_IDS:
        bounds, source = source_move(sections, number)
        raw = unique((action for action in actions if action['action'] == number), f'AIR Action {number}')
        action = action_profile(raw, bounds)
        action['spriteIndices'] = validate_manifest(manifest, hashes, raw)
        result[number] = {'state': source, 'command': commands[number], 'action': action}
    air_actions = [unique((action for action in actions if action['action'] == number), f'AIR Action {number}')
                   for number in (41, 49)]
    air_sprites = {action['action']: validate_manifest(manifest, hashes, action) for action in air_actions}
    reaction_sprites = {
        number: validate_manifest(manifest, hashes,
                                  unique((a for a in actions if a['action'] == number), f'AIR Action {number}'))
        for number in (5000, 5005)
    }
    return {'format': 1, 'zipSha256': zip_sha, 'manifestSha256': archive_digest(manifest), 'members': members,
            'groundBodyWidths': widths['ground'], 'airBodyWidths': widths['air'], 'moves': result,
            'neutralAirHurtboxes': neutral_air_hurtboxes(air_actions), 'neutralAirActionSprites': air_sprites,
            'lightReactionActionSprites': reaction_sprites}


def attack_cursor(action, contacts):
    cursor = [(element, tick) for element, count in enumerate(action['durations']) for tick in range(count)]
    for tick, pause in sorted(contacts, reverse=True):
        require(0 <= tick < len(cursor) - 1 and pause >= 0, 'contact falls outside the animation cursor')
        after_contact = tick + 1
        cursor[after_contact:after_contact] = [cursor[after_contact]] * pause
    return cursor


def verify_timeline(action, collisions, entry, exit_frame, contacts, interrupted):
    observed = []
    for row in collisions[entry:exit_frame]:
        fighter = row['chars'][0]
        element = fighter['elem']
        require(fighter['state'] == fighter['anim'] == action['id'] and 0 <= element < len(action['durations']),
                f"frame {row['frame']}: expected State/Action {action['id']} element")
        sprite = fighter['frame']
        require(sprite['sprite'] == action['spriteKeys'][element] and sprite['duration'] == action['durations'][element]
                and all((sprite[key] or []) == action[key][element] for key in ('clsn1', 'clsn2')),
                f"frame {row['frame']}: State {action['id']} AIR sprite or collision mismatch")
        observed.append((element, fighter['elemTime']))
    expected = attack_cursor(action, [(hit['moveTick'], hit['pause']['attackerFrames']) for hit in contacts])
    require(observed == (expected[:len(observed)] if interrupted else expected),
            f"State {action['id']}: reference cursor differs from AIR and measured contact pauses")


def trace_contact(name, groups, row, following, strike, expected_blocked):
    frame = row['frame']
    attacker, target = row['chars']
    hit = unique((hit_row['chars'][0] for hit_row in groups['hitdef']
                  if hit_row['frame'] == frame and hit_row['hitDefOwner'] == 1), f'{name} F{frame} HitDef')
    definition = hit['hitDef']
    blocked = target['guarded']
    require(blocked is expected_blocked, f'{name} F{frame}: unexpected guard outcome')
    atk_pause, target_pause = definition['guardPause' if blocked else 'hitPause']
    require(type(atk_pause) is int and type(target_pause) is int and 0 < atk_pause <= 30 and 0 < target_pause <= 30
            and target['hitShake'] == target_pause, f'{name} F{frame}: invalid HitDef pause or target hit shake')
    trade = attacker['gethit']
    require((trade or attacker['hitPause'] == atk_pause + 1), f'{name} F{frame}: attacker pause mismatch')
    end = groups['end']
    for i in range(1, min(atk_pause + 1, following - frame)):
        if not trade:
            require(end[frame+i]['chars'][0]['hitPause'] == atk_pause - i, f'{name} F{frame+i}: attacker pause mismatch')
    for i in range(1, min(target_pause + 1, following - frame)):
        require(end[frame+i]['chars'][1]['hitShake'] == target_pause - i, f'{name} F{frame+i}: defender pause mismatch')
    damage = target['life'] - end[frame+1]['chars'][1]['life']
    require(damage == target['damage'] and (damage == 0 if blocked else damage > 0),
            f'{name} F{frame}: applied damage does not match the following tick life reduction')
    raw_vel = definition['guardVel' if blocked else 'groundVel']
    speed = attacker['facing'] * target['hitVel'][0]
    require(speed >= 0 and speed == abs(raw_vel[0]) and target['hitVel'][1] == raw_vel[1] == 0,
            f'{name} F{frame}: unsupported ground hit velocity')
    restored = next((item['frame'] for item in end[frame+1:following] if item['chars'][1]['ctrl']), None)
    effective_stun = restored - frame - target_pause if restored is not None else None
    raw_stun = target['hitTime']
    if restored is not None:
        require(effective_stun > 0, f'{name} F{frame}: invalid defender control interval')
    movements = [(i, end[i]['chars'][1]['pos'][0] - end[i-1]['chars'][1]['pos'][0])
                 for i in range(frame+1, restored or following)
                 if end[i]['chars'][1]['pos'][0] != end[i-1]['chars'][1]['pos'][0]]
    first_movement = movements[0][0] if movements else None
    if speed:
        require(first_movement == frame + target_pause + 1
                and math.isclose(attacker['facing'] * movements[0][1], speed, abs_tol=0.0001),
                f'{name} F{frame}: first ground push disagrees with HitDef')
    else:
        require(first_movement is None, f'{name} F{frame}: zero-push hit unexpectedly moved target')
    initial_action = end[frame+1]['chars'][1]['anim']
    expected_reaction = (5002, 5007) if attacker['state'] == 215 else (5000, 5005)
    reaction = None
    if not blocked:
        require(initial_action == expected_reaction[0], f'{name} F{frame}: unexpected victim reaction Action')
        followup = next((item['frame'] for item in groups['after-collision'][frame+1:restored or following]
                         if item['chars'][1]['anim'] == expected_reaction[1]), None)
        if restored is not None:
            require(followup is not None and followup > frame + target_pause,
                    f'{name} F{frame}: missing original followup reaction Action')
            reaction = {'initialAction': initial_action, 'followupAction': expected_reaction[1],
                        'followupTick': followup - frame - target_pause}
    return {'frame': frame, 'moveTick': attacker['time'] - 1, 'strike': strike, 'blocked': blocked,
            'damage': damage, 'rawDamage': definition['damage'], 'rawHitTime': definition['hitTime'],
            'rawGuardHitTime': definition['guardHitTime'], 'contactHitTime': raw_stun,
            'vars': {key: hit['vars'][key] for key in ('var6', 'var21', 'var23')},
            'initialPush': speed, 'firstMovementFrame': first_movement, 'controlFrame': restored,
            'effectiveStun': effective_stun, 'pause': {'attackerFrames': atk_pause, 'defenderFrames': target_pause},
            'targetGuardedInState': target['state'], 'targetAction': initial_action, 'reaction': reaction}


def reference_case(audit, directory, metadata, mode, number, scenario):
    normal = 'light' if number == 205 else 'heavy'
    name = f'{mode}-{normal}-{scenario}'
    path = directory / f'{name}.jsonl'
    details = state200.load_json(directory / f'{name}-metadata.json')
    distance = CASES[number][scenario]
    digest = archive_digest(path)
    require(details['case'] == name and details['initial_distance'] == metadata['scenario_distance'][name] == distance
            and details['executable_sha256'] == metadata['executable_sha256']
            and details['config_sha256'] == metadata['config_sha256'] and details['trace_sha256'] == digest,
            f'{name}: per-case reference metadata or trace SHA mismatch')
    groups = state200.trace_rows(path, name, frame_count=76)
    before, collision, end = (groups[phase] for phase in ('before-command', 'after-collision', 'end'))
    first = before[0]['chars']
    facing = -1 if scenario == 'mirror' else 1
    require(first[0]['facing'] == facing and first[1]['facing'] == -facing
            and abs(first[1]['pos'][0] - first[0]['pos'][0]) == distance,
            f'{name}: reference distance or facing differs from fixture')
    expected_mode = 2 if mode == 'mode2' else 0
    require(first[0]['vars']['var10'] == expected_mode, f'{name}: unexpected original mode')
    mode_intro = state210.verify_mode_intro(directory / f'{name}-mode.jsonl', name) if mode == 'mode2' else None
    selected = number - 5 if scenario.startswith('far-') else number
    entry = next((row['frame'] for row in collision if row['chars'][0]['state'] in (200, 205, 210, 215)), None)
    require(entry is not None and collision[entry]['chars'][0]['state'] == selected, f'{name}: wrong source branch')
    command = before[entry]['chars'][0]
    attacker_front = audit['groundBodyWidths']['front']
    target_posture = 'air' if before[entry]['chars'][1]['pos'][1] < 0 else 'ground'
    target_front = audit[f'{target_posture}BodyWidths']['front']
    combined_front = sum(value['numerator'] / value['denominator'] for value in (attacker_front, target_front))
    require(command['ctrl'] and command['raw'] != 0
            and command['bodyDistX'] == distance - combined_front
            and (command['bodyDistX'] <= audit['moves'][number]['command']['maximumStartBodyDistance']) == (selected == number),
            f'{name}: source command branch disagrees with physical button and body distance')
    result = {'trace': path.name, 'sha256': digest, 'distance': distance, 'bodyDistance': command['bodyDistX'],
              'state': selected, 'entryFrame': entry, 'attackerFacing': facing, 'modeIntro': mode_intro}
    if selected != number:
        return result
    action = audit['moves'][number]['action']
    exit_frame = next((row['frame'] for row in collision[entry:] if row['chars'][0]['state'] != number), None)
    require(exit_frame is not None, f'{name}: missing move exit')
    interrupted = collision[exit_frame]['chars'][0]['state'] != 0
    if not interrupted:
        require(collision[exit_frame]['chars'][0]['ctrl'], f'{name}: missing State 0 control recovery')
    rows = [row for row in collision if row['chars'][1]['gethit']]
    if scenario == 'whiff':
        require(not rows and all(row['chars'][1]['life'] == first[1]['life'] for row in end), f'{name}: whiff settled')
    if scenario not in ('whiff', 'trade'):
        expected_count = 2 if number == 215 and not scenario.startswith('second-') else 1
        require(len(rows) == expected_count, f'{name}: expected {expected_count} contacts, observed {len(rows)}')
    contacts = []
    for i, row in enumerate(rows):
        elapsed = row['chars'][0]['time'] - 1 - action['startup']
        strike = unique((idx for idx, box in enumerate(action['strikes']) if box['first'] <= elapsed <= box['last']),
                        f'{name} F{row["frame"]} attack window')
        following = rows[i+1]['frame'] if i+1 < len(rows) else len(end)
        contacts.append(trace_contact(name, groups, row, following, strike, scenario in GUARDS))
    require([hit['strike'] for hit in contacts] == sorted({hit['strike'] for hit in contacts}),
            f'{name}: hit group repeated or reversed')
    verify_timeline(action, collision, entry, exit_frame, contacts, interrupted)
    return {**result, 'exitFrame': exit_frame, 'interrupted': interrupted, 'contacts': contacts}


def build_profile(audit, reference, base_fighter, base_binding, mode):
    fighter, binding = copy.deepcopy((base_fighter, base_binding))
    fighter['fighterId'], binding['visualId'] = PRESETS[mode]
    fighter['displayName'] = f"Terry Standing Normals ({'default' if mode == 'default' else 'mode 2'})"
    fighter['groundBodyWidths'] = audit['groundBodyWidths']
    fighter['airBodyWidths'] = audit['airBodyWidths']
    fighter['neutralAirHurtboxes'] = [
        {'minX': state200.fraction(left), 'maxX': state200.fraction(right),
         'minY': state200.fraction(-bottom), 'maxY': state200.fraction(-top)}
        for left, top, right, bottom in audit['neutralAirHurtboxes']
    ]
    retention = fighter['movement']['groundReaction']['velocityRetention']
    coefficient = Fraction(retention['numerator'], retention['denominator'])
    near = []
    for number, command_id in ((205, '5A'), (215, '5B')):
        reviewed = audit['moves'][number]
        command = unique((entry for entry in fighter['commands'] if entry['commandId'] == command_id),
                         f'inherited command {command_id}')
        source_command = reviewed['command']
        require(command['maxSequenceFrames'] == source_command['time']
                and command['bufferLifetimeFrames'] == source_command['bufferTime'],
                f'inherited command {command_id} timing differs from the native source')
        command['priority'] = int(number == 205)
        action = reviewed['action']
        move_id = MOVE_IDS[number]
        attacks = []
        for group, (strike, measured) in enumerate(zip(action['strikes'], reference[number], strict=True), 1):
            hit, guard = measured['hit'], measured['guard']
            attack = state210.attack_fields({'strike': strike['box'], 'active': action['active']}, hit, guard, retention)
            # A block-only push, if ever observed, still needs a positive legacy additive fallback.
            push = max(hit['initialPush'], guard['initialPush'])
            attack['groundKnockbackFriction'] = state200.fraction(Fraction(str(push)) * (1 - coefficient))
            attack.update({'firstActiveFrame': strike['first'], 'lastActiveFrame': strike['last']})
            if len(action['strikes']) > 1:
                attack['contactGroup'] = group
            for optional in ('damageAfterPriorContact', 'counterDamage'):
                if optional in measured:
                    attack[optional] = measured[optional]
            attacks.append(attack)
        near.append({
            'moveId': move_id, 'commandId': command_id, 'allowedFrom': 'Standing',
            'maximumStartBodyDistance': state200.fraction(reviewed['command']['maximumStartBodyDistance']),
            'startupFrames': action['startup'], 'activeFrames': action['active'], 'recoveryFrames': action['recovery'],
            'attacks': attacks, 'hurtboxTimeline': state210.hurtbox_timeline(action),
            'cancels': [], 'throws': [], 'projectiles': [],
        })
        clip = f'state{number}'
        binding['clips'][clip] = {'action': action['id'], 'clock': 'move', 'loop': False}
        binding['moves'][move_id] = {phase: clip for phase in ('Startup', 'Active', 'Recovery')}
    far_heavy = unique((move for move in fighter['moves'] if move['moveId'] == state210.MOVE_ID), 'Stage 15 far heavy move')
    far_heavy.pop('minimumStartDistance')
    fighter['moves'] = near + fighter['moves']
    for number, strike, initial in ((205, 0, 'light_hurt'), (215, 1, 'heavy_hurt')):
        reaction = reference[number][strike].get('reaction')
        if reaction is None:
            continue
        followup = initial + '_followup'
        binding['clips'][initial] = {'action': reaction['initialAction'], 'clock': 'phase', 'loop': False}
        binding['clips'][followup] = {'action': reaction['followupAction'], 'clock': 'phase', 'loop': False}
        binding['hitReactions'][MOVE_IDS[number]] = {'initial': initial, 'followup': followup,
                                                    'followupTick': reaction['followupTick']}
    return fighter, binding


def settlement(hit, *, continuation=None):
    stun = hit['effectiveStun']
    if stun is None:
        require(continuation is not None and continuation['effectiveStun'] is not None
                and hit['blocked'] == continuation['blocked']
                and hit['contactHitTime'] == continuation['contactHitTime'],
                'interrupted first reaction requires a verified equivalent source timer')
        stun = continuation['effectiveStun']
    return {key: hit[key] for key in ('damage', 'initialPush', 'pause')} | {'effectiveStun': stun}


def project_mode(cases):
    light, heavy = (cases[number] for number in (205, 215))
    light_hit, = light['hit']['contacts']
    light_guard, = light['guard']['contacts']
    first_hit, both_second = heavy['hit']['contacts']
    first_guard, both_second_guard = heavy['guard']['contacts']
    second_hit, = heavy['second-only']['contacts']
    second_guard, = heavy['second-guard']['contacts']
    require(first_hit['vars']['var23'] == first_guard['vars']['var23'] == second_hit['vars']['var23']
            == second_guard['vars']['var23'] == 1
            and both_second['vars']['var23'] == both_second_guard['vars']['var23'] == 2
            and first_hit['initialPush'] == first_guard['initialPush'] == 0,
            'State 215 first contact and conditional second-hit source values disagree')
    require(settlement(both_second) | {'damage': second_hit['damage']} == settlement(second_hit)
            and settlement(both_second_guard) == settlement(second_guard)
            and both_second['reaction'] == second_hit['reaction'],
            'State 215 second reaction differs between initial contact and recontact')
    for number, group in ((205, light), (215, heavy)):
        hit_sample = group['hit']['contacts']
        guard_sample = group['guard']['contacts']
        for name in ('mirror', 'boundary-46' if number == 205 else 'boundary-64'):
            require(group[name]['contacts'] == hit_sample, f'State {number} mirror or boundary hit changes settlement')
        for name in ('crouch-guard', 'crouch-late'):
            def same_guard(actual, expected):
                ignored = {'targetGuardedInState', 'targetAction'}
                return all(actual[key] == expected[key] for key in actual.keys() - ignored) and actual['targetAction'] == 151
            require(all(same_guard(actual, expected) for actual, expected in
                        zip(group[name]['contacts'], guard_sample, strict=True)),
                    f'State {number} crouch guard changes settlement or loses its original Action 151')
    light_counter, = light['trade']['contacts']
    heavy_counter, = heavy['trade']['contacts']
    require(light_counter['vars']['var6'] == heavy_counter['vars']['var6'] == 1,
            'mixed samples did not exercise source counter state')
    return {205: [{'hit': settlement(light_hit), 'guard': settlement(light_guard),
                   'counterDamage': light_counter['damage'], 'reaction': light_hit['reaction']}],
            215: [{'hit': settlement(first_hit, continuation=both_second),
                   'guard': settlement(first_guard, continuation=both_second_guard),
                   'counterDamage': heavy_counter['damage']},
                  {'hit': settlement(second_hit), 'guard': settlement(second_guard),
                   'damageAfterPriorContact': both_second['damage'], 'reaction': second_hit['reaction']}]}


def inspect_reference(audit, path):
    metadata = state200.load_json(path)
    require(metadata['engine'] == 'IKEMEN GO' and metadata['commit'] == state200.REFERENCE_COMMIT
            and metadata['capture_profile'] == 'Stage16', 'reference must be the reviewed Stage 16 IKEMEN GO capture')
    require(metadata['terry_archive_sha256'] == audit['zipSha256'], 'reference source archive hash mismatch')
    for key in ('cmd', 'st1', 'anim'):
        member = audit['members'][key]
        require(metadata['source_sha256'].get(member['name']) == member['sha256'],
                f"reference member hash mismatch: {member['name']}")
    for key in ('executable_sha256', 'config_sha256'):
        require(re.fullmatch(r'[0-9a-f]{64}', metadata[key]) is not None, f'invalid reference {key}')
    require('-p1 terry/Terry-KOF98.def -p2 terry/Terry-KOF98.def' in metadata['arguments'],
            'reference must use reviewed Terry source for both seats')
    cases = {mode: {number: {scenario: reference_case(audit, path.parent, metadata, mode, number, scenario)
                            for scenario in scenarios}
                    for number, scenarios in CASES.items()}
             for mode in PRESETS}
    projection = {mode: project_mode(mode_cases) for mode, mode_cases in cases.items()}
    require(projection['default'] == projection['mode2'], 'near normal settlement differs between mode presets')
    return {'metadataSha256': archive_digest(path), 'metadata': metadata, 'cases': cases, 'projection': projection}


def stage15_profiles(zip_path, manifest, state200_reference, state210_reference, fractional_reference):
    light = state200.inspect_source(zip_path, manifest)
    light_reference = state200.inspect_reference(light, state200_reference)
    light_documents = state200.build_documents(light, light_reference)
    heavy = state210.inspect_source(zip_path, manifest)
    heavy_reference = state210.inspect_reference(heavy, state210_reference, light)
    if fractional_reference:
        state210.inspect_fractional_reference(heavy, heavy_reference, fractional_reference)
    return ({mode: state210.build_profile(heavy, heavy_reference['cases'][mode],
                                         light_documents['state200.fighter.json'], light_documents['state200.binding.json'],
                                         mode, light['action']) for mode in PRESETS},
            heavy_reference['metadataSha256'])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source_zip', type=Path)
    parser.add_argument('manifest', type=Path)
    parser.add_argument('--audit-only', action='store_true')
    parser.add_argument('--state200-reference', type=Path, help='Stage 14 run metadata')
    parser.add_argument('--state210-reference', type=Path, help='Stage 15 run metadata')
    parser.add_argument('--fractional-reference', type=Path, help='Stage 15 fractional boundary supplement')
    parser.add_argument('--reference', type=Path, help='Stage 16 run metadata')
    parser.add_argument('--output-dir', type=Path, help='fresh directory for both preset document sets')
    args = parser.parse_args()
    required = (args.state200_reference, args.state210_reference, args.reference, args.output_dir)
    if args.audit_only and (any(required) or args.fractional_reference) or not args.audit_only and not all(required):
        parser.error('use --audit-only or provide all three stage reference paths and --output-dir')
    try:
        audit = inspect_source(args.source_zip, args.manifest)
        if args.audit_only:
            print(json.dumps(audit, ensure_ascii=True, indent=2, sort_keys=True))
            return
        base, base_reference = stage15_profiles(args.source_zip, args.manifest, args.state200_reference,
                                                args.state210_reference, args.fractional_reference)
        reference = inspect_reference(audit, args.reference)
        documents = {}
        for mode in PRESETS:
            fighter, binding = build_profile(audit, reference['projection'][mode], *base[mode], mode)
            documents[f'{mode}.fighter.json'] = fighter
            documents[f'{mode}.binding.json'] = binding
            documents[f'{mode}.source.json'] = {
                **audit, 'preset': mode, 'stage15ReferenceSha256': base_reference,
                'reference': {**reference, 'cases': reference['cases'][mode], 'projection': reference['projection'][mode]},
                'limits': ['Near/far profiles use original body-distance priority and fixed pre-match mode.',
                           'Stage 215 contact groups follow the original MoveHitReset rather than its shared HitDef id.',
                           'Neutral airborne hurtboxes are the constant source Clsn2 of Actions 41 and 49. Other airborne states retain the base rules.',
                           'The first reaction is interrupted by the second contact in the reference. Its effective stun uses the equivalent source HitDef timer as the measured second reaction.',
                           'Second-hit damage is the measured prior-contact or second-only sample. Original var(21) randomizes per tick.'],
            }
        state200.output_documents(args.output_dir, documents)
    except (ValueError, OSError, TypeError, KeyError, IndexError, zipfile.BadZipFile) as error:
        parser.exit(2, f'State 205/215 conversion: {error}\n')


if __name__ == '__main__':
    main()
