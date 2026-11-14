"""Convert Terry's audited standing heavy kicks and their native command routes."""

import argparse
import copy
import json
import re
import zipfile
from pathlib import Path

from . import state200, state210, state_crouch_light_kick as crouch, state_power_wave as wave
from . import state_power_wave_routes as routes, state_reentry
from . import state_standing_light_kicks as light, state_sweep_wave as sweep
from .air import parse_air
from .state200 import fraction, normalized, number_in, require, unique
from .state205215 import action_profile, move_scope


COMMAND = '5D'
RESERVED_BLOWBACK = 'ReservedGroundBlowback'
RESERVED_KICK_THROW = 'ReservedHeavyKickThrow'
HEAVY_REACTION_MOVE = 'TerryCrouchHeavyPunch410'
MOVE_IDS = {240: 'TerryStandHeavyKick240', 245: 'TerryStandHeavyKick245'}
PRESETS = {'default': ('TerryStandingHeavyKicks', 'terry98-stage27-default'),
           'mode2': ('TerryStandingHeavyKicksMode2', 'terry98-stage27-mode2')}
FROZEN_BASE = {
    'default': {
        'fighter': '08c016725b08f206aa2c6eac9f4bc03ad418f95f95c10b4d354dec23aeb71434',
        'binding': 'f5001d2bb305299439210d2e03384b5f0e3d4d883fd3df8435eb4249c435b491',
        'source': '4835e9e911a1b1965038085a342d3186b01897857f5ba9d1f0a1afd140b252ce',
    },
    'mode2': {
        'fighter': 'bdec9aa9095652861862414d4a1260814f509515eed32d1c385c4694868c21d4',
        'binding': '4ff816ad00896fa91f8e0a959fe37c41e2f0fb948f07de11b9a99402230bb3c8',
        'source': 'aa30ea3e49c946ce243e3c5a950bd460ca023caa1341d0a5c261bfdd9ef569fb',
    },
}


def source_command(commands, helper):
    _, entries = move_scope(commands, -1)
    by_state = {number: crouch.command_entry(entries, number) for number in
                (205, 200, 215, 210, 235, 230, 245, 240, 400, 410, 430, 440)}
    require(list(part.line for part in by_state.values()) == sorted(part.line for part in by_state.values()),
            'human standing and crouching normal CMD priority changed')
    shared = {'var(59)!=1', '!ishelper', 'roundstate=2', 'command="b"||helper(99999),var(3)>0',
              'command!="holddown"', 'statetype!=a'}
    control = 'ctrl||(stateno=[100,101])||var(2)>=1'
    near = unique((value for value in by_state[245].values('triggerall') if 'p2bodydist' in normalized(value)),
                  'State 245 body distance condition')
    (maximum_distance,) = number_in(near, r'p2bodydistx<=(\d+)', 'State 245 body distance')
    for number in (245, 240):
        entry = by_state[number]
        conditions = [normalized(value) for key, value in entry.entries if key == 'triggerall']
        expected = shared | ({normalized(near)} if number == 245 else set())
        require(normalized(entry.one('type')) == 'changestate' and len(conditions) == len(expected)
                and set(conditions) == expected
                and [(key, normalized(value)) for key, value in entry.entries
                     if key.startswith('trigger') and key != 'triggerall'] == [('trigger1', control)],
                f'unsupported State {number} human CMD conditions')
    ordinary = {number: [normalized(value) for key, value in entry.entries
                        if key.startswith('trigger') and re.search(r'\bstateno=(?:240|245)\b', normalized(value))]
                for number, entry in by_state.items()}
    require(not any(ordinary.values()), 'new standing heavy early ordinary input path')
    b, helper_lines, _ = buffered_command(commands, helper, 'b', 3, ['HeavyKick'], name='"b"')
    return {'maximumStartBodyDistance': maximum_distance, 'bufferFrames': b['bufferPolicy']['helperTicks'] - 1,
            'bufferPolicy': b['bufferPolicy'],
            'time': b['maxSequenceFrames'], 'control': control, 'earlyOrdinary': {},
            'sourceLines': {number: part.line for number, part in by_state.items()}, 'helperLines': helper_lines,
            **deferred_candidates(commands, entries, helper, b, control, by_state[205].line)}


def buffered_command(commands, helper, physical, slot, buttons, *, name=None):
    command = unique((part for part in commands if part.title == 'command' and part.one('command') == physical
                      and (name is None or part.one('name') == name)),
                     f'physical {physical} command')
    name = command.one('name')
    require(set(command.entries) == {('name', name), ('command', physical), ('time', '1')},
            f'physical {physical} command has an unsupported buffer override')
    variable = f'var({slot})'
    latch, decay = [part for part in helper if part.values(variable)]
    sweep.require_fields(latch, {'type': 'varset', 'trigger1': f'command={normalized(name)}'},
                         f'unsupported {physical} Helper latch')
    sweep.require_fields(decay, {'type': 'varadd', 'trigger1': f'{variable}>0', variable: '-1'},
                         f'unsupported {physical} Helper decay')
    require(latch.line < decay.line and not any(part.values('ignorehitpause') for part in (latch, decay)),
            f'unsupported {physical} Helper clock')
    return {'buttons': buttons, 'maxSequenceFrames': int(command.one('time')), 'buttonToleranceFrames': 0,
            'bufferLifetimeFrames': 0, 'chargeHoldFrames': 0,
            'bufferPolicy': {'preserveDuringHitPause': True, 'helperTicks': int(latch.one(variable)),
                             'helperHeldDecay': 1, 'helperReleasedDecay': 1, 'helperRunsDuringGlobalPause': True},
            'chargeReleaseToleranceFrames': 0}, {'latch': latch.line, 'decay': decay.line}, name


def deferred_candidates(commands, entries, helper, b, root_control, first_standing_line):
    by_name = {name: crouch.command_entry(entries, number) for name, number in
               (('groundRecovery', 720), ('blowback', 250), ('punchThrow', 800), ('kickThrow', 805),
                ('forwardPunch', 300), ('risingUpper', 310))}
    lines = {name: part.line for name, part in by_name.items()}
    require(list(lines.values()) == sorted(lines.values()) and lines['risingUpper'] < first_standing_line,
            'reserved ground command priority changed')
    blowback = by_name['blowback']
    chord, helper_lines, original_chord = buffered_command(commands, helper, 'y+b', 7, ['HeavyPunch', 'HeavyKick'])
    shared = {'var(59)!=1', '!ishelper', 'roundstate=2', 'command!="holddown"', 'statetype!=a'}
    require(set(map(normalized, blowback.values('triggerall'))) == shared |
            {f'command={normalized(original_chord)}||helper(99999),var(7)>0'}
            and list(map(normalized, blowback.values('trigger1'))) == [root_control]
            and not blowback.values('trigger2'), 'unsupported State 250 ground command')
    kick_throw = by_name['kickThrow']
    distance = unique((value for value in kick_throw.values('trigger1') if 'p2bodydist' in normalized(value)),
                      'State 805 body distance condition')
    (maximum_throw_distance,) = number_in(distance, r'p2bodydistx<=(\d+)', 'State 805 body distance')
    require(set(map(normalized, kick_throw.values('triggerall'))) == shared |
            {'command="b"||helper(99999),var(3)>0', 'command="holdfwd"||command="holdback"'}
            and set(map(normalized, kick_throw.values('trigger1'))) ==
            {normalized(distance), 'p2statetype!=a&&p2movetype!=h', 'ctrl||var(2)>=1'}
            and not kick_throw.values('trigger2'), 'unsupported State 805 ground throw command')
    for name in ('blowback', 'kickThrow'):
        sweep.require_fields(by_name[name], {'type': 'changestate'}, f'unsupported {name} command')
    return {'deferredBlowback': {**chord, 'commandId': RESERVED_BLOWBACK, 'reserveGroundInput': True,
                                 'directions': ['Neutral', 'Back', 'Forward']},
            'deferredBlowbackHelperLines': helper_lines,
            'deferredKickThrow': {**b, 'commandId': RESERVED_KICK_THROW, 'reserveGroundInput': True,
                                  'directions': ['Back', 'Forward'],
                                  'groundReservationTarget': {'maximumBodyDistance': fraction(maximum_throw_distance),
                                                              'requiresGroundedNonHitstun': True}},
            'deferredSourceLines': lines}


def source_steps(controls, durations):
    totals = {number: {} for number in durations}
    for controller in controls:
        conditions = list(map(normalized, controller.values('trigger1')))
        require(set(key for key, _ in controller.entries) == {'type', 'trigger1', 'x'},
                f'line {controller.line}: unsupported PosAdd fields')
        if len(conditions) == 1:
            action, element = number_in(conditions[0], r'anim=(\d+)&&animelem=(\d+)',
                                        f'line {controller.line} PosAdd condition')
            elems = [element]
        else:
            require(len(conditions) == 2 and re.fullmatch(r'animelem=\d+(?:\|\|animelem=\d+)*', conditions[1]),
                    f'line {controller.line}: unsupported PosAdd element group')
            (action,) = number_in(conditions[0], r'anim=(\d+)', f'line {controller.line} PosAdd action')
            elems = [int(value) for value in re.findall(r'animelem=(\d+)', conditions[1])]
        require(action in totals and len(elems) == len(set(elems))
                and all(1 <= element <= len(durations[action]) for element in elems),
                f'line {controller.line}: unsupported PosAdd action or element')
        for element in elems:
            tick = sum(durations[action][:element - 1])
            totals[action][tick] = totals[action].get(tick, 0) + int(controller.one('x'))
    return {number: [{'frame': frame, 'x': fraction(x)} for frame, x in sorted(steps.items())]
            for number, steps in totals.items()}


def source_attack(sections, number, actions):
    definition, controllers = move_scope(sections, number)
    sweep.require_fields(definition, {'type': 's', 'movetype': 'a', 'physics': 'n', 'ctrl': '0', 'velset': '0,0',
                                      'anim': str(number), 'facep2': '1'}, f'unsupported State {number} StateDef')
    prefix = ['changeanim', 'posadd', 'playsnd', 'playsnd', 'posadd', 'posadd'] if number == 240 else ['playsnd', 'playsnd']
    types = [normalized(part.one('type')) for part in controllers]
    require(types == [*prefix, 'hitdef', 'hitdef', 'varadd', 'varadd', 'statetypeset', 'changestate'],
            f'unsupported State {number} controller sequence')
    branches = (240, 241) if number == 240 else (245,)
    if number == 240:
        transition = controllers[0]
        require(transition.one('value') == '241'
                and set(map(normalized, transition.values('trigger1'))) == {'time=0', 'var(10)=2'}
                and not transition.values('triggerall') and not transition.values('trigger2'),
                'State 240 mode 2 animation branch changed')
    hit, inactive, _, _, recovery, exit_state = controllers[len(prefix):]
    require(set(map(normalized, hit.values('triggerall'))) == {'var(12)=0', '!movecontact&&!movereversed'}
            and list(map(normalized, inactive.values('triggerall'))) == ['var(12)>0']
            and inactive.one('attr') == '', f'unsupported State {number} contact or inactive HitDef')
    sweep.require_fields(hit, {'attr': 's,na', 'animtype': 'hard', 'ground.type': 'high', 'guardflag': 'm',
                               'hitflag': 'maf', 'ground.cornerpush.veloff': '0', 'id': '10'},
                         f'unsupported State {number} ground or guard reaction')
    require(normalized(hit.one('fall', '0')) == '0', f'unsupported State {number} ground fall')
    counter, normal = number_in(hit.one('damage'), r'floor\(\(ifelse\(var\(6\),(\d+),(\d+)\)-var\(21\)\)\*fvar\(7\)\),0',
                                f'State {number} dynamic damage')
    push = re.fullmatch(r'-(\d+(?:\.\d+)?)-ifelse\(var\(40\),0\.5,0\)', normalized(hit.one('ground.velocity')))
    require(push is not None, f'unsupported State {number} ground push formula')
    expected = {outcome: number_in(hit.one(field), r'(\d+)\+\(helper\(40000\),var\(0\)\),(\d+)',
                                   f'State {number} {outcome}') for outcome, field in
                (('hitPause', 'pausetime'), ('guardPause', 'guard.pausetime'))}
    sweep.require_fields(exit_state, {'trigger1': 'animtime=0', 'value': '0', 'ctrl': '1'},
                         f'unsupported State {number} natural exit')
    durations = {action: actions[action][1] for action in branches}
    steps = source_steps([part for part, kind in zip(controllers, types, strict=True) if kind == 'posadd'], durations)
    result = {}
    for index, action in enumerate(branches, 1):
        trigger = f'trigger{index}'
        terms = list(map(normalized, hit.values(trigger)))
        condition, = tuple(term for term in terms if 'animelemtime' in term)
        pattern = f'anim={action}&&' if number == 240 else ''
        bounds = number_in(condition, pattern + r'animelemtime\((\d+)\)>=0&&animelemtime\((\d+)\)<0',
                           f'Action {action} HitDef window')
        require(sorted(terms) == sorted((condition, 'var(28):=1'))
                and list(map(normalized, inactive.values(trigger))) == [condition]
                and normalized(recovery.one(trigger)) == f'{pattern}animelem={bounds[1]}'
                and normalized(recovery.one('movetype')) == 'i',
                f'unsupported Action {action} contact or recovery condition')
        require(not hit.values(f'trigger{len(branches) + 1}') and not inactive.values(f'trigger{len(branches) + 1}'),
                f'unsupported State {number} extra HitDef condition')
        result[action] = {'state': number, 'action': action_profile(actions[action][0], [bounds]),
                          'positionSteps': steps[action], 'guardThreatFrames': sum(durations[action][:bounds[1] - 1]),
                          'damage': {'normalBase': normal, 'counterBase': counter, 'useContactDivisor': False},
                          'groundDefaults': {**expected, 'initialPush': fraction(push[1]),
                                             'hitTime': int(hit.one('ground.hittime')), 'slideTime': int(hit.one('ground.slidetime'))},
                          'hitDef': {key: normalized(value) for key, value in hit.entries if not key.startswith('trigger')},
                          'sourceLines': {'state': definition.line, 'hitdef': hit.line, 'exit': exit_state.line,
                                          'positions': [part.line for part, kind in zip(controllers, types, strict=True)
                                                        if kind == 'posadd']}}
    return result


def inspect_source(archive, manifest):
    prior = light.inspect_source(archive, manifest)
    members = prior['members']
    keys = ('anim', 'cmd', 'st1', 'Terry-2-3.cns', 'Helper.cns')
    with zipfile.ZipFile(archive) as source:
        contents = {key: state200.archive_member(source, members[key]['name']) for key in keys}
    require(all(state200.digest(raw) == members[key]['sha256'] for key, raw in contents.items()), 'source archive changed')
    actions, diagnostics = parse_air(contents['anim'].decode('latin-1'))
    require(not any(re.search(r'duplicate action (240|241|245)\b', message) for message in diagnostics),
            'duplicate standing heavy kick AIR action')
    picked = {number: wave.source_action(actions, number) for number in (240, 241, 245)}
    normals = state_reentry.script_sections(contents['st1'])
    reviewed = {**source_attack(normals, 240, picked), **source_attack(normals, 245, picked)}
    _, helper = move_scope(state_reentry.script_sections(contents['Helper.cns']), 99999)
    command = source_command(state_reentry.script_sections(contents['cmd']), helper)
    _, negative = move_scope(state_reentry.script_sections(contents['Terry-2-3.cns']), -2)
    permission = unique((part for part in negative if normalized(part.one('var(1)', '')) == '1'
                         and any(re.search(r'\bstateno=245\b', normalized(value)) for key, value in part.entries
                                 if key.startswith('trigger'))), 'State 245 explicit cancel permission')
    natural = unique((part for part in negative if normalized(part.one('var(2)', '')) == '1'),
                     'standing heavy natural-entry permission')
    sweep.require_fields(permission, {'type': 'varset', 'triggerall': '!ishelper'}, 'unsupported explicit Wave permission')
    require(set(map(normalized, natural.values('triggerall'))) == {'!ishelper', 'animtime=0'}
            and not any(part.values('ignorehitpause') for part in (permission, natural)),
            'unsupported natural-entry permission or global cancel clock')
    hashes = state200.manifest_hashes(prior['zipSha256'], members)
    for action, review in reviewed.items():
        number = review['state']
        source, durations = picked[action]
        require(any(re.search(rf'\bstateno={number}\b', normalized(value)) for key, value in natural.entries
                    if key.startswith('trigger')), f'State {number} missing from natural permission')
        explicit = [value for part in negative if normalized(part.one('var(1)', '')) in ('1', '2')
                    for key, value in part.entries if key.startswith('trigger')
                    and re.search(rf'\bstateno={number}\b', normalized(value))]
        require(len(explicit) == (number == 245), f'State {number} direct Wave permission changed')
        review['wavePermission'] = {'explicitWindow': routes.cancel_window(explicit[0], durations, source_state=number)
                                    if explicit else None, 'naturalEntryTick': sum(durations)}
        review['action']['spriteIndices'] = state200.validate_manifest(manifest, hashes, source)
    return {**{key: prior[key] for key in ('zipSha256', 'manifestSha256', 'members', 'damageRules', 'groundMovement',
                                         'defaultWave', 'mode2Wave')},
            'command': command, 'actions': reviewed,
            'sourceLines': {**prior['sourceLines'], 'explicitHeavyKickPermission': permission.line,
                            'naturalHeavyKickPermission': natural.line}}


def install_commands(audit, native, fighter):
    commands = {part['commandId']: part for part in fighter['commands']}
    old_name = sweep.RESERVED_STANDING_HEAVY_KICK
    reserved = commands[old_name]
    body_command = audit['command']
    require(reserved['reserveGroundInput'] and reserved['buttons'] == ['HeavyKick']
            and reserved['directions'] == ['Neutral', 'Back', 'Forward']
            and reserved['maxSequenceFrames'] == body_command['time'] and reserved['bufferLifetimeFrames'] == 0
            and commands[sweep.COMMAND]['bufferLifetimeFrames'] == body_command['bufferFrames']
            and not any(move['commandId'] == old_name or any(rule['commandId'] == old_name
                    for rule in move.get('reservedCancels', ())) for move in fighter['moves']),
            'frozen standing heavy kick input reservation changed')
    replacement = {key: value for key, value in reserved.items() if key != 'reserveGroundInput'}
    fighter['commands'][fighter['commands'].index(reserved)] = dict(
        replacement, commandId=COMMAND, directions=[], bufferLifetimeFrames=0, bufferPolicy=body_command['bufferPolicy'])
    commands[sweep.COMMAND].update(bufferLifetimeFrames=0, bufferPolicy=body_command['bufferPolicy'])
    deferred = [body_command[source] for source, verified in (
        ('deferredBlowback', 'blowbackVerified'), ('deferredKickThrow', 'kickThrowVerified')) if native.get(verified)]
    if deferred:
        baseline = commands[crouch.RESERVED_FORWARD_LIGHT_PUNCH]['priority']
        require(commands[light.RESERVED_GROUND_RECOVERY]['priority'] == baseline + 1,
                'frozen command priority between 720 and 300 changed')
        for existing in fighter['commands']:
            if existing['priority'] > baseline:
                existing['priority'] += len(deferred)
        for index, reservation in enumerate(reversed(deferred), 1):
            fighter['commands'].append({**reservation, 'priority': baseline + index})


def add_body(review, measured, native, fighter, binding, ground_movement):
    state, action = review['state'], review['action']
    action_id = action['id']
    steps = review['positionSteps']
    require(not steps or (action_id in native['positionStepsVerified'] and steps == [
        {'frame': sample['tick'], 'x': fraction(str(sample['x']))} for sample in native['posAdds'][action_id]
    ]), f'Action {action_id} position steps differ from verified native motion')
    move_id = MOVE_IDS[state]
    heavy = unique((move for move in fighter['moves'] if move['moveId'] == HEAVY_REACTION_MOVE), 'frozen State 410')
    heavy_attack, = heavy['attacks']
    reaction = binding['hitReactions'][HEAVY_REACTION_MOVE]
    for posture, branch in (('standing', reaction), ('crouching', reaction['crouching'])):
        expected = {'initialAction': binding['clips'][branch['initial']]['action'],
                    'slideAction': binding['clips'][branch['followup']]['action'],
                    'initialTicks': branch['followupTick'], 'effectiveStun': heavy_attack['hitstunFrames']}
        require(all(measured['groundReactions'][posture][key] == value for key, value in expected.items()),
                f'Action {action_id} {posture} reaction differs from the frozen heavy response')
    require(measured['hit']['effectiveStun'] == heavy_attack['hitstunFrames']
            and measured['guard']['effectiveStun'] == heavy_attack['blockstunFrames'],
            f'Action {action_id} native stun differs from the frozen heavy response')
    strike = crouch.ground_attack({**review, 'groundMovement': ground_movement}, measured,
                                  fighter['movement']['groundReaction'], number=state, height='Mid',
                                  rejects_falling=False)
    strike['groundHitReaction'] = 'Heavy'
    move = {'moveId': move_id, 'commandId': COMMAND, 'allowedFrom': 'Standing',
            'startupFrames': action['startup'], 'activeFrames': action['active'], 'recoveryFrames': action['recovery'],
            'attacks': [strike], 'hurtboxTimeline': state210.hurtbox_timeline(action),
            'guardThreatFrames': review['guardThreatFrames'], 'cancels': [], 'throws': [], 'projectiles': []}
    if steps:
        move['positionSteps'] = steps
    fighter['moves'].append(move)
    clip = f'state{action_id}'
    binding['clips'][clip] = {'action': action_id, 'clock': 'move', 'loop': False}
    binding['moves'][move_id] = dict.fromkeys(('Startup', 'Active', 'Recovery'), clip)
    binding['hitReactions'][move_id] = reaction
    return move


def apply_cancels(review, native, fighter, near, mode):
    first, last = review['wavePermission']['explicitWindow']
    require(near['startupFrames'] <= first <= last < near['startupFrames'] + near['activeFrames'],
            'State 245 cancel window is outside Active')
    shared = {'windowFirstFrame': first, 'windowLastFrame': last, 'phases': 'Active'}
    reserved = []
    if mode == 'default':
        target = unique((move for move in fighter['moves'] if move['moveId'] == wave.MOVE_ID), 'frozen State 1000')
        require(target['commandId'] == wave.COMMAND_ID
                and [part['projectileTypeId'] for part in target['projectiles']] == [wave.ENTITY_ID],
                'frozen State 1000 command or Helper differs')
        near['cancels'].append({**shared, 'targetMoveId': wave.MOVE_ID, 'allowWhiff': True,
                                'requiresHitConfirm': False, 'requiresBlockConfirm': False})
    else:
        require(not any(move['moveId'] == wave.MOVE_ID for move in fighter['moves']),
                'mode 2 cannot select State 1000')
        reserved.append(crouch.RESERVED_MODE2_WAVE)
    reserved.extend(command for command, verified in light.EDGE_RESERVATIONS if native.get(verified))
    if reserved:
        require(all(any(entry['commandId'] == command and entry['reserveGroundInput']
                        for entry in fighter['commands']) for command in reserved),
                'frozen State 245 deferred cancel command unavailable')
        near['reservedCancels'] = [{**shared, 'commandId': command} for command in reserved]


def build_profile(audit, native, base_fighter, base_binding, mode, *, body_only=False):
    require(body_only or native.get('waveVerified'), 'standing heavy kicks require verified native Wave permissions')
    fighter, binding = copy.deepcopy((base_fighter, base_binding))
    fighter['fighterId'], binding['visualId'] = PRESETS[mode]
    if body_only:
        binding['visualId'] = f'terry98-stage27-body-{mode}'
    fighter['displayName'] = f'Terry Standing Heavy Kicks ({mode})'
    sweep_move = unique((move for move in fighter['moves'] if move['moveId'] == sweep.MOVE), 'frozen State 440')
    require(sweep_move['returnsToCrouch'] and not sweep_move.get('walkOnCrouchRecovery'),
            'frozen State 440 natural recovery changed')
    sweep_move['walkOnCrouchRecovery'] = True
    install_commands(audit, native, fighter)
    reviewed = audit['actions']
    near = add_body(reviewed[245], native['moves'][245], native, fighter, binding, audit['groundMovement'])
    near['maximumStartBodyDistance'] = fraction(audit['command']['maximumStartBodyDistance'])
    far_action, far_body = (240, native['moves'][240]) if mode == 'default' else (241, native['mode2Far'])
    add_body(reviewed[far_action], far_body, native, fighter, binding, audit['groundMovement'])
    if not body_only:
        apply_cancels(reviewed[245], native, fighter, near, mode)
    return fighter, binding


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive', type=Path)
    parser.add_argument('manifest', type=Path)
    parser.add_argument('--base', type=Path)
    parser.add_argument('--reference', type=Path, action='append')
    parser.add_argument('--output', type=Path)
    parser.add_argument('--body-only', action='store_true', help='emit a local replay profile from native body proof')
    args = parser.parse_args()
    try:
        audit = inspect_source(args.archive, args.manifest)
        if not args.output:
            print(json.dumps(audit, indent=2, sort_keys=True))
            return
        if not args.base or not args.reference:
            parser.error('--output requires --base and --reference')
        references, native = routes.verify_native_references(args.reference, audit, reference_stage=27, include_contract=True)
        require(native.get('bodyVerified'), 'standing heavy kicks require verified native body outcomes')
        require(args.body_only or native.get('completeVerified'),
                'standing heavy kicks require the complete native reference')
        documents = {}
        for mode in PRESETS:
            base = routes.load_frozen_base(args.base, mode, audit, frozen_base=FROZEN_BASE)
            fighter, binding = build_profile(audit, native, *base, mode, body_only=args.body_only)
            documents.update({f'{mode}.fighter.json': fighter, f'{mode}.binding.json': binding,
                              f'{mode}.source.json': {**audit, 'native': native, 'references': references,
                                                     'frozenBaseSha256': FROZEN_BASE[mode], 'mode': mode,
                                                     'bodyOnly': args.body_only}})
        state200.output_documents(args.output, documents)
    except (AssertionError, OSError, ValueError, KeyError, TypeError, IndexError, zipfile.BadZipFile) as error:
        parser.exit(2, f'Stage 27 conversion: {error}\n')


if __name__ == '__main__':
    main()
