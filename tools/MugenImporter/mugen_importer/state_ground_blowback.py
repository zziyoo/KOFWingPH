"""Import Terry's source-verified State 250 into the frozen Stage 28 presets."""

import argparse
import copy
from fractions import Fraction
import json
from pathlib import Path
import re
import zipfile

from . import state200, state210, state_crouch_light_kick as crouch
from . import state_forward_light_punch as forward, state_power_wave as wave
from . import state_power_wave_routes as routes, state_reentry
from . import state_standing_heavy_kicks as heavy, state_standing_light_kicks as light
from . import state_sweep_wave as sweep
from .air import parse_air
from .state200 import fraction, normalized, number_in, require, unique
from .state205215 import action_profile, move_scope


PLACEHOLDER = heavy.RESERVED_BLOWBACK
COMMAND = 'GroundBlowback'
MOVE_ID = 'TerryGroundBlowback250'
PRESETS = {'default': ('TerryGroundBlowback', 'terry98-stage29-default'),
           'mode2': ('TerryGroundBlowbackMode2', 'terry98-stage29-mode2')}
FROZEN_BASE = {
    'default': {
        'fighter': 'bf1d3e48225123493341bd4a84afa6cb2a876cea2e49036dc3a277a70d273b9c',
        'binding': '11c31b7c84450576f0bdc2fce106f16f5d8a91b36682829603d31a4e5282f3b5',
        'source': '75f8e2c156403ce3542b4933f1a9c8df5527cd82ab39079bc2e9b25b35a5974b',
    },
    'mode2': {
        'fighter': '503e614d93a7541d1eafc8036aa99f4f0ab4a75a1107e024aeb04e17efa6332d',
        'binding': '73d97f17e280c1f416cdd0494c2af351d37390dbef21e9182c0deef8667fa3a5',
        'source': '15483f7a36ece1830e3404dfb84f67433be0e98ee7bd307513dd31c0b8a7ff0b',
    },
}


def read_source(archive, manifest):
    prior = forward.inspect_source(archive, manifest)
    members = dict(prior['members'])
    keys = ('def', 'anim', 'cmd', 'st1', 'stcommon', 'Terry-2-3.cns', 'Helper.cns')
    with zipfile.ZipFile(archive) as source:
        contents = {key: state200.archive_member(source, members[key]['name']) for key in keys}
        files = unique((part for part in state_reentry.script_sections(contents['def'])
                        if part.title == 'files'), 'DEF Files section')
        root_state_file = files.one('st')
        contents['st'] = state200.archive_member(source, root_state_file)
        members['st'] = {'name': root_state_file, 'sha256': state200.digest(contents['st'])}
    require(all(state200.digest(contents[key]) == members[key]['sha256'] for key in keys),
            'source archive changed during Stage 250 inspection')
    return prior, members, contents


def inspect_command(cmd, helper):
    commands = state_reentry.script_sections(cmd)
    _, entries = move_scope(commands, -1)
    _, latch = move_scope(state_reentry.script_sections(helper), 99999)
    source = heavy.source_command(commands, latch)
    command = dict(source['deferredBlowback'], commandId=COMMAND)
    defaults = unique((part for part in commands if part.title == 'defaults'), 'CMD defaults')
    require(normalized(defaults.one('command.buffer.time')) == '1', 'unsupported y+b root command buffer')
    entry = crouch.command_entry(entries, 250)
    require(not any(part.one('value', '') == '250' for part in entries if part is not entry
                    and 'var(59)!=1' in map(normalized, part.values('triggerall'))),
            'additional human State 250 CMD entry')
    # The State 250 entry does not accept var(1), so it has no early ordinary entry.
    require(not any(re.search(r'\bstateno=\d+&&animelem', normalized(value))
                    for key, value in entry.entries if key.startswith('trigger')),
            'unsupported early State 250 entry')
    return command, entries, {'command': entry.line, **source['deferredBlowbackHelperLines']}


def inspect_permissions(entries, global_states, durations):
    normal_states = (200, 205, 210, 215, 230, 235, 240, 245, 400, 410, 430, 440)
    early_ordinary = [target for target in normal_states
                      if any(re.search(r'\bstateno=250\b', normalized(value))
                             for key, value in crouch.command_entry(entries, target).entries
                             if key.startswith('trigger'))]
    require(not early_ordinary, f'unsupported early ordinary exits from State 250: {early_ordinary}')
    _, negative = move_scope(state_reentry.script_sections(global_states), -2)
    window = unique((part for part in negative if normalized(part.one('var(1)', '')) == '1'
                     and any(re.search(r'\bstateno=250\b', normalized(value))
                             for key, value in part.entries if key.startswith('trigger'))),
                    'State 250 source cancellation permission')
    natural = unique((part for part in negative if normalized(part.one('var(2)', '')) == '1'),
                     'State 250 natural permission')
    condition = unique((value for key, value in window.entries if key.startswith('trigger')
                        and re.search(r'\bstateno=250\b', normalized(value))), 'State 250 cancel timing')
    require(normalized(window.one('triggerall')) == '!ishelper' and not window.values('ignorehitpause')
            and set(map(normalized, natural.values('triggerall'))) == {'animtime=0', '!ishelper'}
            and not natural.values('ignorehitpause')
            and any(re.search(r'\bstateno=250\b', normalized(value))
                    for key, value in natural.entries if key.startswith('trigger')),
            'unsupported State 250 common permission gates')
    targets = {target: crouch.command_entry(entries, target) for target in (300, 310, 1000, 1050)}
    require(all(list(map(normalized, part.values('trigger2'))) == ['var(1)=1'] for part in targets.values()),
            'unsupported State 250 target cancellation permissions')
    require(targets[1000].line < targets[1050].line < targets[300].line < targets[310].line and
            'var(10)=0' in map(normalized, targets[1000].values('triggerall')) and
            {'var(10)=1||var(10)=2', '!numproj'} <= set(map(normalized, targets[1050].values('triggerall'))),
            'unsupported weak Power Wave mode conditions or priority')
    juggle = unique((part for part in negative if 'var(13):=1' in map(normalized, part.values('trigger1'))),
                    'State 250 contact ID 30 juggle registration')
    require('numtarget(10)&&var(6)=0||numtarget(20)||numtarget(30)&&var(6)=0' in
            map(normalized, juggle.values('trigger1')) and
            set(map(normalized, juggle.values('trigger4'))) == {
                'numtarget(30)&&var(6)>=1||numtarget(50)&&var(6)=0',
                'var(13):=4+((var(6)>=1||stateno>=3000)*10)+(var(40)>0)'},
            'unsupported State 250 launch follow-up restriction')
    return {'cancelWindow': routes.cancel_window(condition, durations, source_state=250),
            'cancelTargets': {target: part.line for target, part in targets.items()},
            'earlyOrdinaryEntries': [], 'earlyOrdinaryExits': early_ordinary,
            'naturalEntryTick': sum(durations), 'sourceLines': {'cancel': window.line, 'natural': natural.line,
                                                               'juggle': juggle.line}}


def position_steps(controllers, durations):
    totals = {}
    for part in controllers:
        require(all(key in ('type', 'trigger1', 'trigger2', 'x') for key, _ in part.entries)
                and len(part.values('trigger1')) == 1 and len(part.values('trigger2')) <= 1,
                f'line {part.line}: unsupported State 250 PosAdd controller')
        conditions = list(map(normalized, [*part.values('trigger1'), *part.values('trigger2')]))
        require(all(re.fullmatch(r'animelem=\d+(?:\|\|animelem=\d+)*', value) for value in conditions),
                f'line {part.line}: unsupported State 250 PosAdd condition')
        elements = [int(element) for condition in conditions for element in re.findall(r'animelem=(\d+)', condition)]
        require(len(elements) == len(set(elements)) and all(1 <= element <= len(durations) for element in elements),
                f'line {part.line}: invalid State 250 PosAdd AIR element')
        for element in elements:
            tick = sum(durations[:element - 1])
            totals[tick] = totals.get(tick, 0) + int(part.one('x'))
    return [{'frame': frame, 'x': fraction(x)} for frame, x in sorted(totals.items())]


def launch_air_hurtboxes(selected):
    poses = [boxes for phase in ('Lift', 'Rise', 'Fall')
             for boxes in state200.collision_boxes(selected[wave.LAUNCH_ACTIONS[phase]][0], 2, normalize_edges=True)]
    require(poses and poses[0] and all(boxes == poses[0] for boxes in poses),
            'airborne launch reactions require common AIR hurtboxes')
    return [{'minX': fraction(left), 'maxX': fraction(right), 'minY': fraction(-bottom), 'maxY': fraction(-top)}
            for left, top, right, bottom in poses[0]]


def inspect_body(contents, action, durations, selected):
    definition, controllers = move_scope(state_reentry.script_sections(contents['st']), 250)
    state_def = {'type': 's', 'movetype': 'a', 'physics': 'n', 'juggle': '0', 'poweradd': '0',
                 'ctrl': '0', 'velset': '0,0', 'anim': '250', 'sprpriority': '3'}
    sweep.require_fields(definition, state_def, 'unsupported State 250 StateDef')
    require(set(key for key, _ in definition.entries) == state_def.keys(), 'unexpected State 250 mode or facing override')
    require([normalized(part.one('type')) for part in controllers] == [
        'playsnd', 'playsnd', 'posadd', 'posadd', 'hitdef', 'varadd', 'varadd', 'statetypeset', 'changestate'],
        'unsupported State 250 controller order')
    hit, recovery, exit_state = controllers[4], controllers[-2], controllers[-1]
    require(set(map(normalized, hit.values('triggerall'))) == {'var(12)=0', '!movecontact&&!movereversed'},
            'unsupported State 250 HitDef gates')
    bounds = number_in(hit.one('trigger1'), r'animelemtime\((\d+)\)>=0&&animelemtime\((\d+)\)<0',
                       'State 250 HitDef AIR elements')
    require(not hit.values('trigger2'), 'unsupported additional State 250 HitDef conditions')
    sweep.require_fields(hit, {'attr': 's,na', 'animtype': 'hard', 'air.animtype': 'back', 'guardflag': 'm',
                               'hitflag': 'maf', 'ground.type': 'high', 'air.type': 'high', 'id': '30',
                               'fall': '1', 'fall.recover': '1', 'fall.recovertime': '600',
                               'ground.cornerpush.veloff': '0', 'air.cornerpush.veloff': '0'}, 'unsupported State 250 HitDef')
    counter, normal = number_in(hit.one('damage'), r'floor\(\(ifelse\(var\(6\),(\d+),(\d+)\)-var\(21\)\)\*fvar\(7\)\),0',
                                'State 250 dynamic damage')
    pair = r'-(\d+(?:\.\d+)?)-ifelse\(var\(40\),0\.5,0\),-(\d+(?:\.\d+)?)-ifelse\(var\(40\),0\.5,0\)'
    ground = re.fullmatch(pair, normalized(hit.one('ground.velocity')))
    air = re.fullmatch(pair, normalized(hit.one('air.velocity')))
    guard = re.fullmatch(r'-(\d+(?:\.\d+)?)-ifelse\(var\(40\),0\.5,0\)', normalized(hit.one('guard.velocity')))
    require(ground and air and guard and ground.groups() == air.groups(), 'unsupported State 250 push or launch velocity')
    pauses = [tuple(number_in(hit.one(key), r'(\d+)\+\(helper\(40000\),var\(0\)\),(\d+)', f'State 250 {key}'))
              for key in ('pausetime', 'guard.pausetime')]
    require(not hit.values('guard.hittime') and not hit.values('guard.slidetime')
            and int(hit.one('ground.hittime')) == int(hit.one('ground.slidetime')),
            'unsupported State 250 ground/guard duration fields')
    sweep.require_fields(recovery, {'trigger1': f'animelem={bounds[1]}', 'movetype': 'i'},
                         'unsupported State 250 threat end')
    sweep.require_fields(exit_state, {'trigger1': 'animtime=0', 'value': '0', 'ctrl': '1'},
                         'unsupported State 250 natural exit')
    return {'action': action_profile(action, [bounds]), 'positionSteps': position_steps(controllers[2:4], durations),
            'guardThreatFrames': wave.guard_threat(controllers, durations, 250),
            'damage': {'normalBase': normal, 'counterBase': counter, 'useContactDivisor': False},
            'hitPause': pauses[0], 'guardPause': pauses[1], 'guardPush': fraction(guard[1]),
            'launchX': fraction(ground[1]), 'launchY': fraction(ground[2]), 'gravity': fraction(hit.one('yaccel')),
            'groundHitTime': int(hit.one('ground.hittime')), 'airHitTime': int(hit.one('air.hittime')),
            'launchReaction': wave.launch_reaction(contents['stcommon'], selected, hit),
            'launchAirHurtboxes': launch_air_hurtboxes(selected),
            'groundTech': sweep.source_ground_tech(contents, selected),
            'hitDef': {key: normalized(value) for key, value in hit.entries if not key.startswith('trigger')},
            'sourceLines': {'state': definition.line, 'hitdef': hit.line, 'exit': exit_state.line}}


def inspect_source(archive, manifest):
    prior, members, contents = read_source(archive, manifest)
    actions, diagnostics = parse_air(contents['anim'].decode('latin-1'))
    wanted = (250, *sorted(set(wave.LAUNCH_ACTIONS.values())), 5200)
    require(not any(f'duplicate action {number} ' in warning for warning in diagnostics for number in wanted),
            'duplicate State 250 or reaction AIR action')
    selected = {number: wave.source_action(actions, number, hold_last=number in (5050, 5160)) for number in wanted}
    hashes = state200.manifest_hashes(prior['zipSha256'], members)
    sprite_indices = {number: state200.validate_manifest(manifest, hashes, action)
                      for number, (action, _) in selected.items()}
    action, durations = selected[250]
    body = inspect_body(contents, action, durations, selected)
    command, entries, command_lines = inspect_command(contents['cmd'], contents['Helper.cns'])
    permissions = inspect_permissions(entries, contents['Terry-2-3.cns'], durations)
    body['action']['spriteIndices'] = sprite_indices[250]
    return {**{key: prior[key] for key in ('zipSha256', 'manifestSha256', 'damageRules', 'groundMovement')},
            **body, **permissions, 'members': members, 'command': command, 'spriteIndices': sprite_indices,
            'sourceLines': {**body['sourceLines'], **permissions['sourceLines'], **command_lines}}


def install_command(audit, fighter):
    placeholder = unique((item for item in fighter['commands'] if item['commandId'] == PLACEHOLDER),
                         'frozen State 250 ground reservation')
    command = audit['command']
    require(placeholder == {**command, 'commandId': PLACEHOLDER, 'priority': placeholder['priority']}
            and not any(move['commandId'] == PLACEHOLDER for move in fighter['moves']),
            'frozen State 250 command differs from the audited ground reservation')
    for move in fighter['moves']:
        for rule in move.get('reservedCancels', ()):
            if rule['commandId'] == PLACEHOLDER:
                rule['commandId'] = COMMAND
    fighter['commands'][fighter['commands'].index(placeholder)] = {**placeholder, 'commandId': COMMAND}


def add_body(audit, native, fighter, binding, *, body_only):
    body = audit['action']
    require(native.get('bodyVerified'), 'State 250 requires the verified native body reference')
    measured = native['move250']
    require(all(fraction(str(measured[key])) == audit[key] for key in ('launchX', 'launchY', 'guardPush'))
            and all(tuple(measured[key]) == audit[key] for key in ('hitPause', 'guardPause'))
            and measured['durations'] == body['durations']
            and measured['attackWindow'] == [body['startup'], body['startup'] + body['active'] - 1]
            and [{'frame': step['tick'], 'x': fraction(str(step['x']))} for step in measured['positionSteps']]
            == audit['positionSteps']
            and measured['guardTicks'] == audit['groundHitTime'],
            'State 250 native launch, guard or motion differs from the audited body')
    hit_pause, guard_pause = ({'attackerFrames': attacker, 'defenderFrames': defender}
                             for attacker, defender in (audit['hitPause'], audit['guardPause']))
    guard_speed = Fraction(str(measured['guardPush']))
    attack = state210.attack_fields({'strike': body['strikes'][0]['box'], 'active': body['active']},
                                    {'damage': 0, 'initialPush': guard_speed, 'pause': hit_pause,
                                     'effectiveStun': audit['groundHitTime']},
                                    {'damage': 0, 'initialPush': guard_speed, 'pause': guard_pause,
                                     'effectiveStun': measured['guardTicks'] + 1},
                                    fighter['movement']['groundReaction']['velocityRetention'])
    ground_guard = native.get('airGuardRejected')
    restrict_followup = native.get('launchWaveRestricted') and native.get('launchWaveCounterAllowed')
    restrict_melee = native.get('launchMeleeRestricted')
    require(body_only or (ground_guard and restrict_followup and restrict_melee),
            'State 250 requires verified air guard, melee and Wave follow-up references')
    launch = {**audit['launchReaction'], 'groundTech': audit['groundTech']}
    if restrict_followup:
        launch.update(suppressOwnerProjectilesDuringFall=True, allowOwnerProjectilesOnCounter=True)
    if ground_guard:
        attack['groundGuardOnly'] = True
    if restrict_melee:
        require('launchAir' not in fighter['groundHurtboxes'], 'frozen grounded hurtbox profile changed')
        fighter['groundHurtboxes']['launchAir'] = audit['launchAirHurtboxes']
        attack['respectsAirFollowupRestriction'] = True
    attack.update(dynamicDamage=audit['damage'], groundKnockback=fraction(0), rejectsLyingTargets=True,
                  launchVelocityX=audit['launchX'], launchVelocityY=audit['launchY'], launchGravity=audit['gravity'],
                  knockdownFrames=launch['impactFrames'] + launch['lieDownFrames'],
                  wakeupFrames=launch['wakeupFrames'], launchReaction=launch)
    move = {'moveId': MOVE_ID, 'commandId': COMMAND, 'allowedFrom': 'GroundIdle',
            'startupFrames': body['startup'], 'activeFrames': body['active'], 'recoveryFrames': body['recovery'],
            'attacks': [attack], 'hurtboxTimeline': state210.hurtbox_timeline(body),
            'positionSteps': audit['positionSteps'], 'guardThreatFrames': audit['guardThreatFrames'],
            'cancels': [], 'throws': [], 'projectiles': []}
    fighter['moves'].append(move)
    binding['clips']['state250'] = {'action': 250, 'clock': 'move', 'loop': False}
    binding['moves'][MOVE_ID] = dict.fromkeys(('Startup', 'Active', 'Recovery'), 'state250')
    # Mode 2 already binds the landing/tech phases for State 440; the new launch adds the ascent and fall phases.
    for phase, action in wave.LAUNCH_ACTIONS.items():
        clip = {'action': action, 'clock': 'phase', 'loop': False}
        key = f'launch_{phase}'
        require(binding['clips'].get(key, clip) == clip, f'frozen launch clip {key} changed')
        binding['clips'][key] = clip
    return move


def install_cancels(audit, native, fighter, move, mode, *, body_only):
    back_knuckle = native.get('backKnuckleVerified')
    upper = native.get('risingUpperVerified')
    wave_exit = native.get('waveVerified' if mode == 'default' else 'mode2WaveVerified')
    require(body_only or (back_knuckle and upper and wave_exit), f'State 250 {mode} cancel reference incomplete')
    commands = {entry['commandId']: entry for entry in fighter['commands']}
    if mode == 'mode2':
        require(wave.COMMAND_ID not in commands and all(entry['moveId'] != wave.MOVE_ID for entry in fighter['moves']),
                'State 1000 cannot be imported into mode 2')
    if not any((back_knuckle, upper, wave_exit)):
        return
    first, last = audit['cancelWindow']
    require(audit['cancelWindow'] == native.get('cancelWindow') and
            move['startupFrames'] <= first <= last < move['startupFrames'] + move['activeFrames'],
            'State 250 native cancel window differs from its authored active phases')
    window = {'windowFirstFrame': first, 'windowLastFrame': last, 'phases': 'Active'}
    reserved = []
    if back_knuckle:
        target = unique((entry for entry in fighter['moves'] if entry['moveId'] == forward.MOVE_ID), 'frozen State 300')
        require(target['commandId'] == forward.COMMAND, 'frozen State 300 command changed')
        reserved.append(forward.COMMAND)
    if upper:
        reserved.append(light.RESERVED_RISING_UPPER)
    if mode == 'default' and wave_exit:
        target = unique((entry for entry in fighter['moves'] if entry['moveId'] == wave.MOVE_ID), 'frozen State 1000')
        require(target['commandId'] == wave.COMMAND_ID
                and [entry['projectileTypeId'] for entry in target['projectiles']] == [wave.ENTITY_ID],
                'frozen State 1000 command or Helper 1020 changed')
        move['cancels'].append({**window, 'targetMoveId': wave.MOVE_ID, 'allowWhiff': True,
                                'requiresHitConfirm': False, 'requiresBlockConfirm': False})
    if mode == 'mode2' and wave_exit:
        reserved.append(crouch.RESERVED_MODE2_WAVE)
    if reserved:
        require(all(commands[command]['reserveGroundInput'] for command in reserved),
                'frozen State 250 cancel command unavailable')
        move['reservedCancels'] = [{**window, 'commandId': command} for command in reserved]


def configure_back_knuckle_followup(native, fighter, *, body_only):
    verified = native.get('followupGuardVerified')
    require(body_only or verified, 'State 250 requires the verified State 300 follow-up guard reference')
    if not verified:
        return
    followup = unique((move for move in fighter['moves'] if move['moveId'] == forward.MOVE_ID), 'frozen State 300')
    attacks = followup['attacks']
    require([attack['height'] for attack in attacks] == ['High', 'Low'] and
            not any(attack.get('retainsGroundGuardOnFollowup') for attack in attacks),
            'frozen State 300 guard fields changed')
    # The State 300 source audit verifies guardflag=M on each HitDef. Its ground.type fields
    # select reaction type, not guard height. Preserve ongoing ground guard across fresh contact groups.
    for attack in attacks:
        attack.update(height='Mid', retainsGroundGuardOnFollowup=True, groundGuardOnly=True)
        if native.get('launchMeleeRestricted'):
            attack['respectsAirFollowupRestriction'] = True


def build_profile(audit, native, base_fighter, base_binding, mode, *, body_only=False):
    require(body_only or native.get('completeVerified'), 'State 250 requires the complete native reference')
    fighter, binding = copy.deepcopy((base_fighter, base_binding))
    fighter['fighterId'], binding['visualId'] = PRESETS[mode]
    if body_only:
        binding['visualId'] = f'terry98-stage29-body-{mode}'
    fighter['displayName'] = f'Terry Standing Blowback ({mode})'
    install_command(audit, fighter)
    move = add_body(audit, native, fighter, binding, body_only=body_only)
    install_cancels(audit, native, fighter, move, mode, body_only=body_only)
    configure_back_knuckle_followup(native, fighter, body_only=body_only)
    return fighter, binding


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive', type=Path)
    parser.add_argument('manifest', type=Path)
    parser.add_argument('--base', type=Path)
    parser.add_argument('--reference', type=Path, action='append')
    parser.add_argument('--output', type=Path)
    parser.add_argument('--body-only', action='store_true', help='emit a local replay profile from the native body proof')
    args = parser.parse_args()
    try:
        audit = inspect_source(args.archive, args.manifest)
        if not args.output:
            print(json.dumps(audit, indent=2, sort_keys=True))
            return
        if not args.base or not args.reference:
            parser.error('--output requires --base and --reference')
        references, native = routes.verify_native_references(args.reference, audit, reference_stage=29,
                                                             include_contract=True)
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
        parser.exit(2, f'Stage 29 conversion: {error}\n')


if __name__ == '__main__':
    main()
