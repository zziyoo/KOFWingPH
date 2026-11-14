"""Convert Terry's reviewed standing light kicks and their separate CMD routes."""

import argparse
import copy
import json
from pathlib import Path
import re
import zipfile

from . import state200, state210, state_crouch_light_kick as crouch, state_power_wave as wave
from . import state_power_wave_routes as routes, state_reentry
from .air import parse_air
from .state200 import fraction, normalized, number_in, require, unique
from .state205215 import action_profile, move_scope


COMMAND = '5C'
RESERVED_RISING_UPPER = 'ReservedRisingUpper'
RESERVED_GROUND_RECOVERY = 'ReservedGroundRecovery'
EDGE_RESERVATIONS = ((crouch.RESERVED_FORWARD_LIGHT_PUNCH, 'forwardReserveVerified'),
                     (RESERVED_RISING_UPPER, 'risingReserveVerified'))
MOVE_IDS = {**crouch.MOVE_IDS, 230: 'TerryStandLightKick230', 235: 'TerryStandLightKick235'}
PRESETS = {'default': ('TerryStandingLightKicks', 'terry98-stage26-default'),
           'mode2': ('TerryStandingLightKicksMode2', 'terry98-stage26-mode2')}
FROZEN_BASE = {
    'default': {
        'fighter': 'eecc43b01bff6ec0c4ed11c6faa97f07253aff805bfeeeee688f803641ac089e',
        'binding': '555be18b778846165ce639827d644c3b2c8bce8583f349abdf989ba3b6a84e7e',
        'source': 'aea9cce8117a7daf03dbd7cd884b4756f77bef4839697710775aaf73e9841230',
    },
    'mode2': {
        'fighter': 'b47b323f17ea1f161a3b52106c3d81e65251dc23e79dc91170d0a6cc6b85b882',
        'binding': '898776bd02527c9d7ad998fc4f223a41b8809776466a1d422addad2925ce7cb3',
        'source': 'e720c17557ae79b1ec1ffa1c898a1249cbe8151cd2fc077c186b7c57352ab669',
    },
}


def source_permissions(negative, durations):
    explicit = [part for part in negative if normalized(part.one('type', '')) == 'varset'
                and normalized(part.one('var(1)', '')) in ('1', '2')]
    natural = unique((part for part in negative if normalized(part.one('type', '')) == 'varset'
                      and normalized(part.one('var(2)', '')) == '1'), 'natural normal-move command permission')
    require(set(map(normalized, natural.values('triggerall'))) == {'!ishelper', 'animtime=0'}
            and not natural.values('ignorehitpause'), 'natural permission must follow the action clock')
    result = {}
    for number in (230, 235):
        pattern = rf'\b{number}\b'
        conditions = [value for part in explicit for key, value in part.entries
                      if key.startswith('trigger') and re.search(pattern, value)]
        require(len(conditions) == (number == 235), f'State {number} explicit Wave permissions changed')
        require(any(re.search(pattern, value) for key, value in natural.entries if key.startswith('trigger')),
                f'State {number} is missing from the natural permission controller')
        result[number] = {'explicitWindow': routes.cancel_window(conditions[0], durations[number], source_state=number)
                          if conditions else None, 'naturalEntryTick': sum(durations[number])}
    return result


def edge_reservations(entries, helper):
    specs = ((300, 40, 'x', '!='), (310, 41, 'y', '='))
    permissions = {}
    for (command_id, _), (state, slot, button, down) in zip(EDGE_RESERVATIONS, specs, strict=True):
        entry = crouch.command_entry(entries, state)
        key = f'var({slot})'
        edge = f'command="{button}"&&command="holdfwd"&&command{down}"holddown"'
        require(f'{edge}||helper(99999),{key}>0' in map(normalized, entry.values('triggerall'))
                and normalized(entry.one('trigger2')) == 'var(1)=1' and not entry.values('trigger3'),
                f'State {state} authored buffered direction or permission differs')
        latch, decay = [part for part in helper if part.values(key)]
        # Source order in the conjunction does not affect the three physical inputs.
        require(set(normalized(latch.one('trigger1')).split('&&')) == set(edge.split('&&'))
                and normalized(latch.one('type')) == 'varset' and latch.line < decay.line
                and not any(part.values('ignorehitpause') for part in (latch, decay)),
                f'State {state} unsupported Helper latch')
        crouch.sweep.require_fields(decay, {'type': 'varadd', 'trigger1': f'{key}>0', key: '-1'},
                                   f'State {state} unsupported Helper decay')
        permissions[command_id] = {'helperTicks': int(latch.one(key)), 'helperHeldDecay': 1,
                                   'helperReleasedDecay': 1, 'preserveDuringHitPause': True}
    return permissions


def ground_recovery_command(commands, entries, helper):
    command = unique((part for part in commands if part.title == 'command'
                      and part.one('name') == '"recovery"'), 'physical recovery command')
    crouch.sweep.require_fields(command, {'command': 'x+a', 'time': '1'}, 'unsupported ground recovery input')
    entry = crouch.command_entry(entries, 720)
    condition = {'var(59)!=1', '!ishelper', 'roundstate=2', 'statetype!=a', 'var(9)=1',
                 'command="recovery"||helper(99999),var(6)>0', 'command!="holddown"'}
    require(set(map(normalized, entry.values('triggerall'))) == condition and
            normalized(entry.one('trigger1')) == 'ctrl||(stateno=[100,101])||var(2)>=1' and
            not entry.values('trigger2'), 'unsupported State 720 ground recovery permission')
    latch, decay = [part for part in helper if part.values('var(6)')]
    crouch.sweep.require_fields(latch, {'type': 'varset', 'trigger1': 'command="recovery"'},
                               'unsupported recovery Helper latch')
    crouch.sweep.require_fields(decay, {'type': 'varadd', 'trigger1': 'var(6)>0', 'var(6)': '-1'},
                               'unsupported recovery Helper decay')
    require(latch.line < decay.line and not any(part.values('ignorehitpause') for part in (latch, decay)),
            'unsupported recovery Helper clock')
    return {'commandId': RESERVED_GROUND_RECOVERY, 'buttons': ['LightPunch', 'LightKick'],
            'directions': ['Neutral', 'Back', 'Forward'], 'maxSequenceFrames': int(command.one('time')),
            'buttonToleranceFrames': 0, 'bufferLifetimeFrames': int(latch.one('var(6)')) - 1,
            'chargeHoldFrames': 0, 'chargeReleaseToleranceFrames': 0, 'reserveGroundInput': True}


def inspect_source(archive, manifest):
    prior = crouch.inspect_source(archive, manifest)
    members = prior['members']
    keys = ('anim', 'cmd', 'st1', 'Terry-2-3.cns', 'Helper.cns')
    with zipfile.ZipFile(archive) as source:
        contents = {key: state200.archive_member(source, members[key]['name']) for key in keys}
    require(all(state200.digest(raw) == members[key]['sha256'] for key, raw in contents.items()), 'source archive changed')
    actions, diagnostics = parse_air(contents['anim'].decode('latin-1'))
    require(not any(re.search(r'duplicate action (230|235)\b', message) for message in diagnostics),
            'duplicate standing light kick AIR action')
    selected = {number: wave.source_action(actions, number) for number in MOVE_IDS}
    durations = {number: duration for number, (_, duration) in selected.items()}
    command = crouch.source_commands(contents, durations, prior, include_standing_kicks=True)
    cmd_sections = state_reentry.script_sections(contents['cmd'])
    _, entries = move_scope(cmd_sections, -1)
    near = crouch.command_entry(entries, 235)
    distance = unique((value for value in near.values('triggerall') if 'p2bodydist' in normalized(value)),
                      'State 235 body distance condition')
    (command['maximumStartBodyDistance'],) = number_in(distance, r'p2bodydistx<=(\d+)', 'State 235 body distance')
    _, helper = move_scope(state_reentry.script_sections(contents['Helper.cns']), 99999)
    command['edgeReservations'] = edge_reservations(entries, helper)
    command['groundRecovery'] = ground_recovery_command(cmd_sections, entries, helper)
    require(prior['sourceLines']['mode2WaveEntry'] < crouch.command_entry(entries, 720).line < command['sourceLines']['300'],
            'State 720 command priority differs from the authored Wave/command normals order')
    _, negative = move_scope(state_reentry.script_sections(contents['Terry-2-3.cns']), -2)
    permissions = source_permissions(negative, durations)
    hashes = state200.manifest_hashes(prior['zipSha256'], members)
    moves = {}
    for number in (230, 235):
        definition, controls, hit, bounds, damage = crouch.source_attack(
            contents['st1'], number, posture='s', guard_flag='m', hit_flag='maf')
        raw, times = selected[number]
        action = action_profile(raw, [bounds])
        action['spriteIndices'] = state200.validate_manifest(manifest, hashes, raw)
        hit_pause = number_in(hit.one('pausetime'), r'(\d+)\+\(helper\(40000\),var\(0\)\),(\d+)',
                              f'State {number} hit pause')
        guard_pause = number_in(hit.one('guard.pausetime'), r'(\d+)\+\(helper\(40000\),var\(0\)\),(\d+)',
                                f'State {number} guard pause')
        (push,) = number_in(hit.one('ground.velocity'), r'-(\d+(?:\.\d+)?)-ifelse\(var\(40\),0\.5,0\)',
                            f'State {number} grounded velocity')
        moves[number] = {'action': action, 'damage': damage, 'wavePermission': permissions[number],
                         'guardThreatFrames': wave.guard_threat(controls, times, number),
                         'groundDefaults': {'initialPush': fraction(push), 'hitPause': hit_pause, 'guardPause': guard_pause,
                                            'hitTime': int(hit.one('ground.hittime')), 'slideTime': int(hit.one('ground.slidetime'))},
                         'hitDef': {key: normalized(value) for key, value in hit.entries if not key.startswith('trigger')},
                         'sourceLines': {'state': definition.line, 'hitdef': hit.line, 'exit': controls[-1].line}}
    return {**{key: prior[key] for key in ('zipSha256', 'manifestSha256', 'members', 'damageRules', 'groundMovement',
                                         'groundReactions', 'defaultWave', 'mode2Wave', 'sourceLines')},
            'command': command, 'moves': moves}


def add_kick(audit, native, fighter, binding, number, *, guard_followup):
    reviewed = audit['moves'][number]
    action = reviewed['action']
    attack = crouch.ground_attack({**reviewed, 'groundMovement': audit['groundMovement']}, native,
                                  fighter['movement']['groundReaction'], number=number, height='Mid', rejects_falling=False)
    if guard_followup:
        attack['retainsGroundGuardOnFollowup'] = True
    move_id = MOVE_IDS[number]
    move = {'moveId': move_id, 'commandId': COMMAND, 'allowedFrom': 'Standing',
            'startupFrames': action['startup'], 'activeFrames': action['active'], 'recoveryFrames': action['recovery'],
            'attacks': [attack], 'hurtboxTimeline': state210.hurtbox_timeline(action),
            'guardThreatFrames': reviewed['guardThreatFrames'], 'cancels': [], 'throws': [], 'projectiles': []}
    if number == 235:
        move['maximumStartBodyDistance'] = fraction(audit['command']['maximumStartBodyDistance'])
    fighter['moves'].append(move)
    clip = f'state{number}'
    binding['clips'][clip] = {'action': number, 'clock': 'move', 'loop': False}
    binding['moves'][move_id] = dict.fromkeys(('Startup', 'Active', 'Recovery'), clip)
    crouch.bind_reactions(audit, native, fighter, binding, number=number, move_id=move_id)


def apply_routes(audit, native, fighter, mode, *, body_only):
    by_id = {move['moveId']: move for move in fighter['moves']}
    for state, (first, last) in audit['command']['chainWindows'].items():
        source = int(state)
        move = by_id[MOVE_IDS[source]]
        reserved = move.get('reservedCancels', [])
        if source != 235:
            old = unique((rule for rule in reserved if rule['commandId'] == crouch.RESERVED_STANDING_LIGHT_KICK),
                         f'State {source} frozen standing kick reservation')
            require((old['windowFirstFrame'], old['windowLastFrame']) == (first, last),
                    f'State {source} standing kick reservation no longer matches the source')
            phases = old['phases']
            reserved.remove(old)
            if not reserved:
                del move['reservedCancels']
        else:
            # The first ordinary window tick is the Active-to-Recovery transition.
            phases = 'Active,Recovery'
        if body_only:
            continue
        targets = (205, 200, 400, 235, 230, 430) if source == 235 else (235, 230)
        for target in targets:
            punch = target in (200, 205, 400)
            route = {'targetMoveId': MOVE_IDS[target], 'windowFirstFrame': first, 'windowLastFrame': last,
                     'phases': phases, 'requiresHitConfirm': False, 'requiresBlockConfirm': False,
                     'allowWhiff': True, 'allowSameMove': source == target,
                     'standingInputOnly': target not in (400, 430),
                     'maximumInputAge': audit['command']['punchBufferFrames' if punch else 'bufferFrames']}
            if source == 235 and punch and target in native.get('retainHelperTargets', ()):
                route['retainBufferedMotionHelper'] = True
            move['cancels'].append(route)
    if body_only:
        return
    near = by_id[MOVE_IDS[235]]
    first, last = audit['moves'][235]['wavePermission']['explicitWindow']
    rule = {'windowFirstFrame': first, 'windowLastFrame': last, 'phases': 'Active'}
    if mode == 'default':
        near['cancels'].insert(0, {**rule, 'targetMoveId': wave.MOVE_ID, 'allowWhiff': True,
                                   'requiresHitConfirm': False, 'requiresBlockConfirm': False})
    else:
        near['reservedCancels'] = [{**rule, 'commandId': crouch.RESERVED_MODE2_WAVE}]
    for command_id, verified in EDGE_RESERVATIONS:
        if native.get(verified):
            command = unique((c for c in fighter['commands'] if c['commandId'] == command_id),
                             f'frozen {command_id} command')
            require(command['reserveGroundInput'] and command['bufferLifetimeFrames'] == 0,
                    f'unsupported frozen {command_id} input')
            command.update(reserveOnEdgeDirection=True, bufferPolicy=audit['command']['edgeReservations'][command_id])
            near.setdefault('reservedCancels', []).append({**rule, 'commandId': command_id})


def build_profile(audit, native, base_fighter, base_binding, mode, *, body_only=False, pilot=False):
    if not body_only:
        require(native.get('chainsVerified'), 'standing light kicks require verified native ordinary transitions')
        require(native.get('waveVerified'), 'standing light kicks require verified native Wave permissions')
    fighter, binding = copy.deepcopy((base_fighter, base_binding))
    fighter['fighterId'], binding['visualId'] = PRESETS[mode]
    if body_only:
        binding['visualId'] = f'terry98-stage26-body-{mode}'
    if pilot:
        binding['visualId'] = f'terry98-stage26-route-{mode}'
    fighter['displayName'] = f'Terry Standing Light Kicks ({mode})'
    fighter['useOrderedGroundHitstunExit'] = True
    if native.get('farGuardControlVerified'):
        fighter['protectGroundBlockstunExit'] = True
    commands = fighter['commands']
    reserved = unique((command for command in commands if command['commandId'] == crouch.RESERVED_STANDING_LIGHT_KICK),
                      'frozen standing light kick reservation')
    require(reserved['reserveGroundInput'] and reserved['buttons'] == ['LightKick']
            and reserved['directions'] == ['Neutral', 'Back', 'Forward']
            and reserved['maxSequenceFrames'] == audit['command']['time']
            and reserved['bufferLifetimeFrames'] == audit['command']['bufferFrames'], 'frozen standing kick command changed')
    real = {key: value for key, value in reserved.items() if key != 'reserveGroundInput'}
    commands[commands.index(reserved)] = dict(real, commandId=COMMAND, directions=[])
    if native.get('groundRecoveryReserveVerified'):
        wave_id = wave.COMMAND_ID if mode == 'default' else crouch.RESERVED_MODE2_WAVE
        wave_command = unique((command for command in commands if command['commandId'] == wave_id), f'frozen {wave_id}')
        commands.append({**audit['command']['groundRecovery'], 'priority': wave_command['priority']})
        wave_command['priority'] += 1
    for number in (235, 230):
        add_kick(audit, native['moves'][number], fighter, binding, number,
                 guard_followup=number in native.get('guardFollowupVerified', ()))
    apply_routes(audit, native, fighter, mode, body_only=body_only)
    return fighter, binding


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive', type=Path)
    parser.add_argument('manifest', type=Path)
    parser.add_argument('--base', type=Path)
    parser.add_argument('--reference', type=Path, action='append')
    parser.add_argument('--output', type=Path)
    gate = parser.add_mutually_exclusive_group()
    gate.add_argument('--body-only', action='store_true', help='emit a local body validation profile without new cancels')
    gate.add_argument('--pilot', action='store_true', help='emit a local replay profile from verified route samples before closeout')
    args = parser.parse_args()
    try:
        audit = inspect_source(args.archive, args.manifest)
        if not args.output:
            print(json.dumps(audit, indent=2, sort_keys=True))
            return
        if not args.base or not args.reference:
            parser.error('--output requires --base and --reference')
        references, native = routes.verify_native_references(args.reference, audit, reference_stage=26, include_contract=True)
        require(native.get('bodyVerified'), 'standing light kicks require verified native body outcomes')
        require(args.body_only or args.pilot or native.get('completeVerified'),
                'standing light kicks require the complete native reference')
        documents = {}
        for mode in PRESETS:
            frozen = routes.load_frozen_base(args.base, mode, audit, frozen_base=FROZEN_BASE)
            fighter, binding = build_profile(audit, native, *frozen, mode, body_only=args.body_only, pilot=args.pilot)
            documents.update({f'{mode}.fighter.json': fighter, f'{mode}.binding.json': binding,
                              f'{mode}.source.json': {**audit, 'native': native, 'references': references,
                                                     'frozenBaseSha256': FROZEN_BASE[mode], 'mode': mode,
                                                     'bodyOnly': args.body_only, 'pilot': args.pilot}})
        state200.output_documents(args.output, documents)
    except (AssertionError, OSError, ValueError, KeyError, TypeError, IndexError, zipfile.BadZipFile) as error:
        parser.exit(2, f'Stage 26 conversion: {error}\n')


if __name__ == '__main__':
    main()
