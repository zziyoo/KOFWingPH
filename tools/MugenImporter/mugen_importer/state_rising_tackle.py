"""Import Terry's source- and trace-verified default weak Rising Tackle (States 1200/1205)."""

import argparse
from copy import deepcopy
from fractions import Fraction
import json
from pathlib import Path
import re
import zipfile

from . import state200, state210, state_ground_blowback as blowback
from . import state_power_wave as wave, state_power_wave_routes as routes
from . import state_reentry, state_rising_upper as upper
from .air import parse_air
from .state200 import fraction, normalized, number_in, require, unique
from .state205215 import move_scope


COMMAND = 'TerryRisingTackle1200'
RESERVED_COMMAND = 'ReservedRisingTackle1200'
MOVE_ID = 'TerryRisingTackle1200'
PRESETS = {'default': ('TerryRisingTackle', 'terry98-stage31-default'),
           'mode2': ('TerryRisingTackleMode2', 'terry98-stage31-mode2')}
FROZEN_BASE = {
    'default': {
        'fighter': 'b05ec97d1c0b3c0b47d65feae3674635311972700c5e4eda52304ac8889ecedf',
        'binding': '830ae53c050d96a219b31ba99beff137e56caf0b912f830cb8335db14e558126',
        'source': '444541f3d3fd0d680b64868d4908a63bb7779413c2a94dcced29ff87b8acd3c9',
    },
    'mode2': {
        'fighter': 'e3cfb15f1a43be9c0c78693ce9411d0b1cd63bbb6ecfc15988e6a999ba5bae14',
        'binding': 'a5b3690f854f617ef44aec9aa1975727b74f89e175cee48e1a65c1e3744e93fa',
        'source': 'ef773537d2502ddc8120cab3abe82e1b5e93144064ccd5dba78da6050502b7fe',
    },
}


def _window(controller, durations, key):
    expression = unique((value for value in controller.values(key)
                         if 'animelemtime' in value.lower() or 'animelem' in value.lower()),
                        f'line {controller.line} {key} AIR window')
    normalized_expression = normalized(expression)
    if match := re.search(r'animelemtime\((\d+)\)>=0&&animelemtime\((\d+)\)<0',
                          normalized_expression):
        first, following = map(int, match.groups())
        require(1 <= first < following <= len(durations) + 1,
                f'line {controller.line}: AIR window outside Action duration')
        return sum(durations[:first - 1]), sum(durations[:following - 1]) - 1
    if match := re.search(r'animelem=(\d+)(?:,>=0)?', normalized_expression):
        element = int(match.group(1))
        require(1 <= element <= len(durations), f'line {controller.line}: AIR element outside Action')
        start = sum(durations[:element - 1])
        return start, start
    if match := re.search(r'animelemtime\((\d+)\)>0', normalized_expression):
        element = int(match.group(1))
        require(1 <= element <= len(durations), f'line {controller.line}: AIR element outside Action')
        return sum(durations[:element - 1]) + 1, sum(durations) - 1
    raise ValueError(f'unsupported line {controller.line} {key} AIR window: {expression}')


def _windows(controller, durations):
    result = []
    for key, expression in controller.entries:
        normalized_expression = normalized(expression)
        if re.fullmatch(r'trigger\d+', key) and 'animelemtime' in normalized_expression:
            first, following = number_in(normalized_expression,
                r'animelemtime\((\d+)\)>=0&&animelemtime\((\d+)\)<0',
                f'line {controller.line} {key} AIR elements')
            require(1 <= first < following <= len(durations) + 1,
                    f'line {controller.line}: AIR window outside Action duration')
            result.append((sum(durations[:first - 1]), sum(durations[:following - 1]) - 1))
    return result


def _box_fields(box):
    left, top, right, bottom = box
    return {
        'offsetForward': fraction(Fraction(left + right, 2)),
        'offsetUp': fraction(Fraction(-top - bottom, 2)),
        'halfWidth': fraction(Fraction(right - left, 2)),
        'halfHeight': fraction(Fraction(bottom - top, 2)),
    }


def _clip_action(action, indices):
    return {'id': action['action'], 'durations': [frame['time'] for frame in action['frames']],
            'spriteIndices': indices,
            'clsn1': state200.collision_boxes(action, 1, normalize_edges=True),
            'clsn2': state200.collision_boxes(action, 2, normalize_edges=True)}


def inspect_source(archive, manifest):
    """Audit the original DEF/CNS/CMD/Helper/AIR and Manifest before producing runtime data."""
    base, members, contents = blowback.read_source(archive, manifest)
    with zipfile.ZipFile(archive) as source:
        cns = state200.archive_member(source, members['Terry-S.cns']['name'])
    sections = state_reentry.script_sections(cns)
    state1200, controls1200 = move_scope(sections, 1200)
    state1205, controls1205 = move_scope(sections, 1205)
    required1200 = {'type': 's', 'movetype': 'a', 'physics': 'n', 'juggle': '0', 'anim': '1200',
                    'ctrl': '0', 'velset': '0,0', 'facep2': '1'}
    require(all(normalized(state1200.one(key)) == value for key, value in required1200.items()),
            'unsupported State 1200 StateDef')
    require(all(normalized(state1205.one(key)) == value for key, value in
                  {'type': 's', 'movetype': 'i', 'physics': 'n', 'anim': '1205',
                   'ctrl': '0', 'velset': '0,0'}.items()), 'unsupported State 1205 StateDef')

    actions, diagnostics = parse_air(contents['anim'].decode('latin-1'))
    require(not any('duplicate action 1200 ' in row or 'duplicate action 1205 ' in row for row in diagnostics),
            'duplicate Terry AIR action 1200/1205')
    action1200 = unique((action for action in actions if action['action'] == 1200), 'AIR Action 1200')
    action1205 = unique((action for action in actions if action['action'] == 1205), 'AIR Action 1205')
    durations = [frame['time'] for frame in action1200['frames']]
    landing_durations = [frame['time'] for frame in action1205['frames']]
    require(durations == [2, 3, 4, 1, 2, 2, 2, 2, 2, 1, 5, 6, 9] and sum(durations) == 41,
            'Action 1200 source element timing changed')
    require(landing_durations == [2, 12] and sum(landing_durations) == 14,
            'Action 1205 source element timing changed')
    zip_manifest_hashes = state200.manifest_hashes(base['zipSha256'], members)
    sprite1200 = state200.validate_manifest(manifest, zip_manifest_hashes, action1200)
    sprite1205 = state200.validate_manifest(manifest, zip_manifest_hashes, action1205)
    body1200 = _clip_action(action1200, sprite1200)
    body1205 = _clip_action(action1205, sprite1205)

    not_hit = unique((row for row in controls1200 if normalized(row.one('type')) == 'nothitby'),
                     'State 1200 NotHitBy')
    invulnerable_window = _window(not_hit, durations, 'trigger1')
    require(invulnerable_window == (0, 1) and normalized(not_hit.one('value')) == 'sca',
            'unsupported State 1200 invulnerability window')
    reversal = unique((row for row in controls1200 if normalized(row.one('type')) == 'reversaldef'),
                      'State 1200 ReversalDef')
    reversal_window = _window(reversal, durations, 'trigger1')
    require(reversal_window == (2, 4) and normalized(reversal.one('triggerall')) == 'p2movetype!=h'
            and normalized(reversal.one('reversal.attr')) == 'sca,aa,ap'
            and normalized(reversal.one('pausetime')) == '11+(helper(40000),var(0)),11',
            'unsupported State 1200 ReversalDef eligibility or pause')
    require(normalized(reversal.one('numhits', '0')) == '0', 'ReversalDef must not settle ordinary damage')

    move_reset = unique((row for row in controls1200 if normalized(row.one('type')) == 'movehitreset'),
                        'State 1200 MoveHitReset')
    reset_elements = sorted(map(int, re.findall(r'animelem=(\d+)',
                                                normalized('||'.join(move_reset.values('trigger1'))))))
    require(reset_elements == [3, 4, 6, 8, 10], 'State 1200 contact-reset elements changed')
    hitdefs = [row for row in controls1200 if normalized(row.one('type')) == 'hitdef']
    active_hitdefs = [row for row in hitdefs if normalized(row.one('attr', ''))]
    require(len(active_hitdefs) == 2 and len(hitdefs) == 3, 'State 1200 active and disabled HitDef branches changed')
    disabled = next(row for row in hitdefs if row not in active_hitdefs)
    require(normalized(disabled.one('triggerall')) == 'var(12)>0' and not normalized(disabled.one('attr', '')),
            'State 1200 disabled HitDef guard changed')

    first, second = active_hitdefs
    shared = {'var(12)=0', '!movecontact&&!movereversed'}
    require(set(map(normalized, first.values('triggerall'))) == shared and
            set(map(normalized, second.values('triggerall'))) == shared,
            'State 1200 HitDef contact gates changed')
    first_window = _window(first, durations, 'trigger1')
    second_windows = _windows(second, durations)
    require(first_window == (5, 8) and second_windows == [(9, 9), (12, 13), (16, 17), (20, 20)],
            'State 1200 HitDef windows changed')
    require(normalized(first.one('attr')) == 's,sa' and normalized(first.one('guardflag')) == 'm'
            and normalized(second.one('attr')) == 'a,sa' and normalized(second.one('guardflag')) == 'ma'
            and normalized(first.one('hitflag')) == normalized(second.one('hitflag')) == 'maf',
            'State 1200 attack/guard target attributes changed')
    damage_pattern = r'floor\(\(ifelse\(var\(6\),(\d+),(\d+)\)-var\(21\)\)\*fvar\(7\)\),floor\((\d+)\*fvar\(7\)\)'
    damage = re.fullmatch(damage_pattern, normalized(first.one('damage')))
    require(damage is not None and normalized(first.one('damage')) == normalized(second.one('damage')),
            'State 1200 dynamic damage formula changed')
    counter_base, normal_base, guard_base = map(int, damage.groups())
    require((normal_base, counter_base, guard_base) == (4, 7, 1), 'State 1200 damage bases changed')
    hit_pause = [number_in(row.one('pausetime'),
                           r'(\d+)\+\(helper\(40000\),var\(0\)\),(\d+)', 'State 1200 hit pause')
                 for row in active_hitdefs]
    guard_pause = [number_in(row.one('guard.pausetime'),
                             r'(\d+)\+\(helper\(40000\),var\(0\)\),(\d+)', 'State 1200 guard pause')
                   for row in active_hitdefs]
    require(hit_pause == [(11, 13), (3, 5)] and guard_pause == [(11, 11), (3, 3)],
            'State 1200 hit/guard pause changed')
    for row in active_hitdefs:
        require(normalized(row.one('ground.slidetime')) == '17' and
                normalized(row.one('ground.hittime')) == '17' and
                normalized(row.one('air.hittime')) == '12' and
                normalized(row.one('guard.velocity')) == '-10.5-ifelse(var(40),0.5,0)' and
                normalized(row.one('ground.velocity')) == '-2-ifelse(var(40),0.5,0),-10-ifelse(var(40),0.5,0)' and
                normalized(row.one('air.velocity')) == normalized(row.one('ground.velocity')) and
                normalized(row.one('yaccel')) == '.5' and normalized(row.one('fall')) == '1' and
                normalized(row.one('fall.recover')) == '1' and normalized(row.one('fall.recovertime')) == '600',
                f'line {row.line}: State 1200 reaction parameters changed')

    pos_add = unique((row for row in controls1200 if normalized(row.one('type')) == 'posadd'), 'State 1200 PosAdd')
    pos_elements = sorted(map(int, re.findall(r'animelem=(\d+)', normalized('||'.join(pos_add.values('trigger1'))))))
    require(pos_elements == [1, 2, 3] and normalized(pos_add.one('x')) == '8', 'State 1200 initial displacement changed')
    # The StateTypeSet controller has `statetype = A`; detect its controller values directly.
    state_air = unique((row for row in controls1200 if normalized(row.one('type')) == 'statetypeset'
                        and any(normalized(value) == 'a' for key, value in row.entries if key in ('statetype', 'value'))),
                       'State 1200 air transition')
    takeoff = _window(state_air, durations, 'trigger1')
    require(takeoff == (9, 9), 'State 1200 takeoff element changed')
    vel_set = unique((row for row in controls1200 if normalized(row.one('type')) == 'velset'), 'State 1200 VelSet')
    vel_add = unique((row for row in controls1200 if normalized(row.one('type')) == 'veladd'), 'State 1200 VelAdd')
    vel_mul = unique((row for row in controls1200 if normalized(row.one('type')) == 'velmul'), 'State 1200 VelMul')
    require(normalized(vel_set.one('x')) == '1' and normalized(vel_set.one('y')) == '-8'
            and normalized(vel_add.one('y')) == '.5' and normalized(vel_mul.one('x')) == '0.9'
            and _window(vel_add, durations, 'trigger1') == (10, 40)
            and _window(vel_mul, durations, 'trigger1') == (10, 40),
            'State 1200 airborne motion changed')
    move_idle = unique((row for row in controls1200 if normalized(row.one('type')) == 'statetypeset'
                        and normalized(row.one('movetype', '')) == 'i'), 'State 1200 idle recovery')
    require(_window(move_idle, durations, 'trigger1') == (21, 21), 'State 1200 idle recovery tick changed')
    exit1200 = unique((row for row in controls1200 if normalized(row.one('type')) == 'changestate'
                       and normalized(row.one('value', '')) == '1205'), 'State 1200 successor')
    require(normalized(exit1200.one('trigger1')) == 'animtime=0', 'State 1200 successor timing changed')
    reset_y = unique((row for row in controls1205 if normalized(row.one('type')) == 'posset'), 'State 1205 ground reset')
    exit1205 = unique((row for row in controls1205 if normalized(row.one('type')) == 'changestate'), 'State 1205 exit')
    require(normalized(reset_y.one('y')) == '0' and normalized(reset_y.one('trigger1')) == 'time=0'
            and normalized(exit1205.one('trigger1')) == 'animtime=0' and normalized(exit1205.one('value')) == '0'
            and normalized(exit1205.one('ctrl')) == '1', 'State 1205 natural recovery changed')

    command, command_lines = upper.inspect_rising_tackle(contents['cmd'], contents['Helper.cns'])
    command = {**command, 'commandId': COMMAND}
    _, negative = move_scope(state_reentry.script_sections(contents['Terry-2-3.cns']), -2)
    normal_permission = unique((row for row in negative if normalized(row.one('var(1)', '')) == '1'),
                               'State -2 ordinary special permission')
    normal_windows = {}
    for key, value in normal_permission.entries:
        if re.fullmatch(r'trigger\d+', key):
            (source,) = number_in(value, r'stateno=(\d+)&&.*', 'ordinary special permission source')
            _, source_durations = wave.source_action(actions, source)
            normal_windows[source] = routes.cancel_window(value, source_durations, source_state=source)
    contact_permission = unique((row for row in negative if normalized(row.one('var(1)', '')) == '2'
                                 and any('stateno=310' in normalized(value) for key, value in row.entries
                                         if key.startswith('trigger'))), 'State 310 MoveReversed permission')
    require(normalized(contact_permission.one('trigger2')) == 'stateno=310&&(movecontact=1||movereversed=1)',
            'State 310 reversal cancel permission changed')

    strikes = []
    windows = [first_window, *second_windows]
    reversal_boxes = body1200['clsn1'][1]
    require(reversal_boxes == [[20, -72, 51, -25]], 'Action 1200 reversal Clsn1 changed')
    for group, (window, hitdef) in enumerate(zip(windows,
            [first, second, second, second, second], strict=True), 1):
        first_tick, last_tick = window
        frame_index = next(index for index, start in enumerate(
            [sum(durations[:i]) for i in range(len(durations))]) if start <= first_tick < start + durations[index])
        boxes = body1200['clsn1'][frame_index]
        require(len(boxes) == 1 and all(body1200['clsn1'][i] == boxes for i in range(frame_index,
                next((j for j, start in enumerate([sum(durations[:i]) for i in range(len(durations))])
                      if start > last_tick), len(durations)))),
                f'Action 1200 attack window {window} does not have one stable Clsn1')
        strikes.append({'first': first_tick, 'last': last_tick, 'contactGroup': group,
                        'box': boxes[0], 'guardFlag': normalized(hitdef.one('guardflag')),
                        'hitPause': hit_pause[0 if group == 1 else 1],
                        'guardPause': guard_pause[0 if group == 1 else 1]})
    require([item['box'] for item in strikes] ==
            [[12, -52, 59, -21], [-4, -116, 35, -29], [-4, -116, 35, -29],
             [-4, -116, 35, -29], [-4, -116, 35, -29]], 'Action 1200 Clsn1 source geometry changed')

    return {
        **{key: base[key] for key in ('zipSha256', 'manifestSha256', 'damageRules', 'groundMovement')},
        'members': members,
        'state1200': {'line': state1200.line, 'definition': required1200, 'duration': sum(durations),
                      'durations': durations, 'invulnerableFrames': invulnerable_window[1] + 1,
                      'reversalWindow': list(reversal_window), 'reversalHitstop': [11, 11],
                      'reversalBoxes': reversal_boxes,
                      'takeoffFrame': takeoff[0], 'landingFrame': 41, 'forwardVelocity': '1',
                      'upwardVelocity': '8', 'gravity': '1/2', 'horizontalRetention': '9/10',
                      'positionSteps': [{'frame': sum(durations[:element - 1]), 'x': 8}
                                        for element in pos_elements],
                      'attackStart': first_window[0], 'attackEnd': second_windows[-1][1],
                      'attackWindows': strikes, 'normalBase': normal_base, 'counterBase': counter_base,
                      'guardBase': guard_base, 'groundHitstun': 17, 'groundBlockstun': 17,
                      'airHitstun': 12, 'groundKnockback': '2', 'guardKnockback': '21/2',
                      'launchVelocityX': '2', 'launchVelocityY': '10', 'launchGravity': '1/2',
                      'landingAction': 1205, 'landingDurations': landing_durations,
                      'recoveryDuration': sum(landing_durations), 'nextState': 0,
                      'stateLines': {'state1200': state1200.line, 'state1205': state1205.line,
                                     'reversal': reversal.line, 'firstHitDef': first.line,
                                     'laterHitDef': second.line, 'naturalExit1200': exit1200.line,
                                     'naturalExit1205': exit1205.line}},
        'actions': {'1200': body1200, '1205': body1205},
        'command': command, 'commandSourceLines': command_lines,
        'contactPermissionLine': contact_permission.line, 'normalWindows': normal_windows,
        'contactPermission': {'sourceState': 310, 'condition': 'MoveContact || MoveReversed'},
    }


def _attack_row(strike, audit, retention, launch_reaction):
    body = audit['state1200']
    hit, guard = strike['hitPause'], strike['guardPause']
    first_attack = strike['contactGroup'] == 1
    retention = Fraction(retention['numerator'], retention['denominator'])
    return {
        **_box_fields(strike['box']),
        'firstActiveFrame': strike['first'] - body['attackStart'],
        'lastActiveFrame': strike['last'] - body['attackStart'],
        'contactGroup': strike['contactGroup'], 'height': 'Mid', 'damage': 0,
        'dynamicDamage': {'normalBase': body['normalBase'], 'counterBase': body['counterBase'],
                          'useContactDivisor': False, 'useVariance': True},
        'dynamicGuardDamage': {'normalBase': body['guardBase'], 'counterBase': body['guardBase'],
                               'useContactDivisor': False, 'useVariance': False},
        'hitstunFrames': body['groundHitstun'], 'blockstunFrames': body['groundBlockstun'],
        'groundKnockback': fraction(body['groundKnockback']), 'groundKnockbackFriction': fraction(
            Fraction(body['groundKnockback']) * (1 - retention)),
        'blockKnockback': fraction(body['guardKnockback']),
        'launchVelocityX': fraction(body['launchVelocityX']), 'launchVelocityY': fraction(body['launchVelocityY']),
        'launchGravity': fraction(body['launchGravity']),
        'launchReaction': deepcopy(launch_reaction),
        'knockdownFrames': launch_reaction['impactFrames'] + launch_reaction['lieDownFrames'],
        'wakeupFrames': launch_reaction['wakeupFrames'],
        'hitstopFrames': max(*hit),
        'hitstopByOutcome': {'hit': {'attackerFrames': hit[0], 'defenderFrames': hit[1]},
                             'block': {'attackerFrames': guard[0], 'defenderFrames': guard[1]}},
        'contactOnEdge': True,
        'groundHitReaction': 'Heavy', 'groundGuardOnly': first_attack,
        'launchOnAirborneOnly': False,
        'rejectsLyingTargets': False, 'rejectsFallingTargets': False,
    }


def build_profile(audit, native, base_fighter, base_binding, mode):
    require(mode in PRESETS, f'unknown Stage 31 profile {mode}')
    require(native.get('completeVerified') and native.get('bodyVerified') and
            native.get('reversal310Verified') and native.get('reversal310CancelVerified') and
            native.get('projectileReversalVerified') and native.get('wavePriorityVerified') and
            native.get('naturalRecoveryVerified'),
            'State 1200 requires complete body, command, reversal and recovery native proof')
    require(native.get('move1200', {}).get('durations') == audit['state1200']['durations'] and
            native['move1200'].get('reversalFrames') == audit['state1200']['reversalWindow'] and
            native['move1200'].get('attackGroups') == [[row['first'], row['last']]
                                                      for row in audit['state1200']['attackWindows']],
            'signed native State 1200 timing differs from source and AIR')
    fighter, binding = deepcopy((base_fighter, base_binding))
    fighter['fighterId'], binding['visualId'] = PRESETS[mode]
    fighter['displayName'] = f'Terry Rising Tackle ({mode})'
    if mode == 'mode2':
        require(not any(move['moveId'] == MOVE_ID for move in fighter['moves']) and
                RESERVED_COMMAND not in {command['commandId'] for command in fighter['commands']} and
                any(command['commandId'] == 'ReservedMode2Wave' for command in fighter['commands']),
                'Mode 2 must retain the State 1050 isolation')
        return fighter, binding

    require(not any(move['moveId'] == MOVE_ID for move in fighter['moves']), 'State 1200 already exists in base profile')
    commands = {command['commandId']: command for command in fighter['commands']}
    require(RESERVED_COMMAND in commands and commands[RESERVED_COMMAND]['reserveGroundInput'] and
            commands[RESERVED_COMMAND]['buttons'] == ['LightPunch'] and
            commands[RESERVED_COMMAND]['directions'] == ['Forward', 'Down', 'DownForward'] and
            commands[RESERVED_COMMAND]['maxSequenceFrames'] == audit['command']['maxSequenceFrames'] and
            commands[RESERVED_COMMAND]['priority'] > commands['236A']['priority'],
            'Stage 30 default Rising Tackle candidate changed')
    source = commands[RESERVED_COMMAND]
    require({**source, 'commandId': COMMAND, 'priority': 0} == {**audit['command'], 'priority': 0},
            'Stage 30 Rising Tackle command disagrees with original CMD and Helper')
    source['commandId'] = COMMAND
    normal_sources = set()
    for move in fighter['moves']:
        for rule in list(move.get('cancels', [])):
            if rule['targetMoveId'] != wave.MOVE_ID:
                continue
            source_state = int(move['moveId'][-3:])
            if source_state in audit['normalWindows']:
                require([rule['windowFirstFrame'], rule['windowLastFrame']] == audit['normalWindows'][source_state]
                        and rule['allowWhiff'], f'State {source_state} special window changed')
                normal_sources.add(source_state)
            else:
                require(source_state in (300, 310) and rule['requiresHitConfirm']
                        and rule['requiresBlockConfirm'], f'unreviewed State {source_state} contact route')
            if source_state == 310:
                rule['requiresReversalConfirm'] = True
            else:
                move['cancels'].append({**rule, 'targetMoveId': MOVE_ID})
        for rule in move.get('reservedCancels', []):
            if rule['commandId'] == RESERVED_COMMAND:
                require(move['moveId'] == 'TerryRisingUpper310', 'unreviewed Rising Tackle reserved route')
                rule['commandId'] = COMMAND
                rule['requiresReversalConfirm'] = True
    require(normal_sources == audit['normalWindows'].keys(), 'frozen profile missing ordinary special routes')

    move_template = next(move for move in fighter['moves'] if move['moveId'] == 'TerryRisingUpper310')
    launch_reaction = deepcopy(move_template['attacks'][0]['launchReaction'])
    body = audit['state1200']
    total = body['duration'] + body['recoveryDuration']
    startup = body['attackStart']
    active = body['attackEnd'] - body['attackStart'] + 1
    recovery = total - startup - active
    attacks = [_attack_row(strike, audit, fighter['movement']['groundReaction']['velocityRetention'], launch_reaction)
               for strike in body['attackWindows']]
    hurtbox_timeline = [
        *state210.hurtbox_timeline(audit['actions']['1200']),
        *state210.hurtbox_timeline(audit['actions']['1205']),
    ]
    move = {
        'moveId': MOVE_ID, 'commandId': COMMAND, 'allowedFrom': 'GroundIdle',
        'startupFrames': startup, 'activeFrames': active, 'recoveryFrames': recovery,
        'attacks': attacks, 'hurtboxTimeline': hurtbox_timeline,
        'positionSteps': [{'frame': row['frame'], 'x': fraction(row['x'])} for row in body['positionSteps']],
        'airMotion': {'takeoffFrame': body['takeoffFrame'], 'landingFrame': body['landingFrame'],
                      **{key: fraction(body[key]) for key in ('forwardVelocity', 'upwardVelocity',
                                                             'gravity', 'horizontalRetention')}},
        'invulnerableFrames': body['invulnerableFrames'],
        'reversals': [{'firstFrame': body['reversalWindow'][0], 'lastFrame': body['reversalWindow'][1],
                       'sourceHitstop': body['reversalHitstop'][0], 'targetHitstop': body['reversalHitstop'][1],
                       'boxes': [{'minX': fraction(x1), 'maxX': fraction(x2),
                                  'minY': fraction(-y2), 'maxY': fraction(-y1)}
                                 for x1, y1, x2, y2 in body['reversalBoxes']]}],
        'guardThreatFrames': body['attackEnd'] + 1,
        'faceOpponentOnStart': True,
        'cancels': [], 'throws': [], 'projectiles': [],
    }
    fighter['moves'].append(move)
    binding['clips']['state1200'] = {'action': 1200, 'clock': 'move', 'loop': False}
    binding['clips']['state1205'] = {'action': 1205, 'clock': 'move', 'loop': False}
    binding['moves'][MOVE_ID] = {phase: 'state1200' for phase in ('Startup', 'Active', 'Recovery')}
    binding.setdefault('moveTimelines', {})[MOVE_ID] = [
        {'startTick': 0, 'clip': 'state1200'}, {'startTick': body['duration'], 'clip': 'state1205'}]
    binding['hitReactions'][MOVE_ID] = {**deepcopy(binding['hitReactions'][move_template['moveId']]),
                                        'zeroBasedLaunchClock': True}
    return fighter, binding


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive', type=Path)
    parser.add_argument('manifest', type=Path)
    parser.add_argument('--base', type=Path, help='verified Stage 30 profile directory')
    parser.add_argument('--reference', type=Path, action='append')
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    try:
        audit = inspect_source(args.archive, args.manifest)
        if not args.output:
            print(json.dumps(audit, indent=2, sort_keys=True))
            return
        if not args.base or not args.reference:
            parser.error('--output requires --base and --reference')
        references, native = routes.verify_native_references(args.reference, audit, reference_stage=31,
                                                             include_contract=True)
        documents = {}
        for mode in PRESETS:
            base = routes.load_frozen_base(args.base, mode, audit, frozen_base=FROZEN_BASE)
            fighter, binding = build_profile(audit, native, *base, mode)
            documents.update({f'{mode}.fighter.json': fighter, f'{mode}.binding.json': binding,
                              f'{mode}.source.json': {**audit, 'native': native, 'references': references,
                                                      'frozenBaseSha256': FROZEN_BASE[mode], 'mode': mode}})
        state200.output_documents(args.output, documents)
    except (AssertionError, OSError, ValueError, KeyError, TypeError, IndexError, zipfile.BadZipFile) as error:
        parser.exit(2, f'Stage 31 conversion: {error}\n')


if __name__ == '__main__':
    main()
