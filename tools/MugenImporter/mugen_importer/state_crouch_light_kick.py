"""Convert the reviewed Terry State 430 and its ordinary move transitions."""

import argparse
import copy
from fractions import Fraction
import json
from pathlib import Path
import re
import zipfile

from . import state200, state210, state_crouch_heavy_wave, state_damage, state_power_wave as wave
from . import state_power_wave_routes as routes, state_reentry, state_sweep_wave as sweep
from .air import parse_air
from .state200 import fraction, normalized, number_in, require, unique
from .state205215 import action_profile, hit_window, move_scope


MOVE = 'TerryCrouchLightKick430'
COMMAND = '2C'
RESERVED_STANDING_LIGHT_KICK = 'ReservedStandLightKick'
RESERVED_FORWARD_LIGHT_PUNCH = 'ReservedForwardLightPunch'
RESERVED_MODE2_WAVE = 'ReservedMode2Wave'
PRESETS = {'default': ('TerryCrouchLightKick', 'terry98-stage25-default'),
           'mode2': ('TerryCrouchLightKickMode2', 'terry98-stage25-mode2')}
MOVE_IDS = {200: 'TerryStandLightPunch200', 205: 'TerryStandLightPunch205',
            400: 'TerryCrouchLightPunch400', 430: MOVE}
DEFERRED = (230, 235)
FROZEN_BASE = {
    'default': {
        'fighter': '1e3081169e9f23f30cffea62031b142a3b5089860befb0c419bd73446e7118c0',
        'binding': 'f32d317717bff49208cb2d9b9959402544ea837626df2d057e6757b2ea41f80e',
        'source': '49912072b9c6b0d5e83e68d30e3f063b9e2c64064e223239c4a20906101c8b0b',
    },
    'mode2': {
        'fighter': 'ab57c1f1aae86f64f47dbe7797282fd79c061a3291596eb1faf411ac1a106b69',
        'binding': 'c37469d7982d3e15f38a342ff292dc87860854b05904f53bfda3d2e75141b7f1',
        'source': '4849bc125e92a03a4ae8b4997a663bd920893cb20ed3243abf57de43e716b266',
    },
}


def source_attack(raw, number=430, *, posture='c', guard_flag='l', hit_flag='ma'):
    definition, controls = move_scope(state_reentry.script_sections(raw), number)
    label = f'State {number}'
    sweep.require_fields(definition, {'type': posture, 'movetype': 'a', 'physics': 'n', 'ctrl': '0', 'velset': '0,0',
                                      'anim': str(number), 'facep2': '1'}, f'unsupported {label} StateDef')
    require([normalized(c.one('type')) for c in controls] ==
            ['playsnd', 'playsnd', 'hitdef', 'hitdef', 'varadd', 'varadd', 'statetypeset', 'changestate'],
            f'unsupported {label} controller sequence')
    hit, inactive = controls[2:4]
    require(set(map(normalized, hit.values('triggerall'))) == {'var(12)=0', '!movecontact&&!movereversed'}
            and set(map(normalized, inactive.values('triggerall'))) == {'var(12)>0'}
            and inactive.one('attr') == '', f'unsupported {label} contact or inactive HitDef')
    sweep.require_fields(hit, {'attr': f'{posture},na', 'animtype': 'light', 'ground.type': 'low', 'guardflag': guard_flag,
                               'hitflag': hit_flag, 'ground.cornerpush.veloff': '0', 'air.cornerpush.veloff': '0'},
                         f'unsupported {label} target, ground reaction or guard eligibility')
    require(not hit.values('guard.velocity') and not hit.values('guard.hittime') and not hit.values('fall'),
            f'unsupported {label} guard override or ground launch')
    bounds = hit_window(hit)
    require(len(hit.values('trigger1')) == 2 and 'var(28):=1' in map(normalized, hit.values('trigger1'))
            and hit_window(inactive) == bounds and len(inactive.values('trigger1')) == 1,
            f'{label} HitDef triggers differ from the audited window')
    counter, normal = number_in(hit.one('damage'), r'floor\(\(ifelse\(var\(6\),(\d+),(\d+)\)-var\(21\)\)\*fvar\(7\)\),0',
                                f'{label} dynamic damage')
    recovery, exit_state = controls[-2:]
    sweep.require_fields(recovery, {'trigger1': f'animelem={bounds[1]}', 'movetype': 'i'}, f'unsupported {label} recovery')
    sweep.require_fields(exit_state, {'trigger1': 'animtime=0', 'value': '11' if posture == 'c' else '0', 'ctrl': '1'},
                         f'unsupported {label} exit')
    return definition, controls, hit, bounds, {'normalBase': normal, 'counterBase': counter, 'useContactDivisor': False}


def command_entry(entries, target):
    return unique((entry for entry in entries if entry.one('value', '') == str(target)
                   and 'var(59)!=1' in map(normalized, entry.values('triggerall'))), f'human State {target} command')


def source_commands(contents, durations, prior, *, include_standing_kicks=False):
    commands = state_reentry.script_sections(contents['cmd'])
    defaults = unique((s for s in commands if s.title == 'defaults'), 'CMD defaults')
    remap = unique((s for s in commands if s.title == 'remap'), 'CMD remap')
    sweep.require_fields(remap, {'a': 'a', 'b': 'b', 'x': 'x', 'y': 'y'}, 'unsupported raw button remap')
    require(int(defaults.one('command.buffer.time')) == 1, 'unsupported root command buffer')
    _, entries = move_scope(commands, -1)
    relevant = {number: command_entry(entries, number) for number in (*MOVE_IDS, *DEFERRED, 300, 310, 410, 440)}
    root_control = 'ctrl||(stateno=[100,101])||var(2)>=1'
    conditions = {200: 'stateno=200&&animelem=3,>=0', 205: 'stateno=205&&animelem=3,>=0',
                  235: 'stateno=235&&animelem=4,>=0', 400: 'stateno=400&&animelem=3,>=1',
                  430: 'stateno=430&&animelem=4,>=0'}
    _, helper = move_scope(state_reentry.script_sections(contents['Helper.cns']), 99999)
    buttons = {}
    for button, slot in (('x', 0), ('a', 1)):
        single = unique((s for s in commands if s.title == 'command' and s.one('name') == f'"{button}"'),
                        f'physical {button} command')
        sweep.require_fields(single, {'command': button, 'time': '1'}, f'unsupported physical {button} command')
        key = f'var({slot})'
        latch, decay = [control for control in helper if control.values(key)]
        sweep.require_fields(latch, {'type': 'varset', 'trigger1': f'command="{button}"'},
                             f'unsupported {button} Helper latch')
        sweep.require_fields(decay, {'type': 'varadd', 'trigger1': f'{key}>0', key: '-1'},
                             f'unsupported {button} Helper decay')
        require(latch.line < decay.line and not any(c.values('ignorehitpause') for c in (latch, decay)),
                f'unsupported {button} Helper clock')
        buttons[button] = {'time': int(single.one('time')), 'bufferFrames': int(latch.one(key)) - 1,
                           'helperLatchLine': latch.line, 'helperDecayLine': decay.line}

    common = {'var(59)!=1', '!ishelper', 'roundstate=2', 'statetype!=a'}
    distances = {205: 16, 235: 28}
    for number in (*MOVE_IDS, *DEFERRED):
        entry = relevant[number]
        button, slot = ('a', 1) if number in (430, *DEFERRED) else ('x', 0)
        direction = 'command="holddown"' if number in (400, 430) else 'command!="holddown"'
        required = common | {f'command="{button}"||helper(99999),var({slot})>0', direction}
        if number in distances:
            required.add(f'p2bodydistx<={distances[number]}')
        require(set(map(normalized, entry.values('triggerall'))) == required
                and normalized(entry.one('trigger1')) == root_control
                and tuple(normalized(entry.one(f'trigger{i}')) for i in range(2, 7)) == tuple(conditions.values())
                and not entry.values('trigger7'), f'unsupported State {number} ordinary command eligibility')
    forward = relevant[300]
    forward_edge = 'command="x"&&command="holdfwd"&&command!="holddown"'
    require(set(map(normalized, forward.values('triggerall'))) == common | {f'{forward_edge}||helper(99999),var(40)>0'}
            and normalized(forward.one('trigger1')) == root_control
            and normalized(forward.one('trigger2')) == 'var(1)=1'
            and not forward.values('trigger3'), 'unsupported State 300 forward command eligibility')
    latch, decay = [control for control in helper if control.values('var(40)')]
    sweep.require_fields(latch, {'type': 'varset', 'trigger1': forward_edge}, 'unsupported forward punch Helper latch')
    sweep.require_fields(decay, {'type': 'varadd', 'trigger1': 'var(40)>0', 'var(40)': '-1'},
                         'unsupported forward punch Helper decay')
    require(latch.line < decay.line and not any(c.values('ignorehitpause') for c in (latch, decay)),
            'unsupported forward punch Helper clock')
    priority_states = (300, 310, 205, 200, 235, 230, 400, 410, 430, 440)
    if include_standing_kicks:
        relevant.update({number: command_entry(entries, number) for number in (215, 210, 245, 240)})
        priority_states = (300, 310, 205, 200, 215, 210, 235, 230, 245, 240, 400, 410, 430, 440)
    priority = [relevant[number].line for number in priority_states]
    require(prior['sourceLines']['defaultWaveEntry'] < prior['sourceLines']['mode2WaveEntry'] < priority[0]
            and priority == sorted(priority), 'State 430 CMD priority differs from the audited sequence')
    windows = {}
    for number in conditions if include_standing_kicks else MOVE_IDS:
        (element, offset) = number_in(conditions[number], rf'stateno={number}&&animelem=(\d+),>=(\d+)',
                                      f'State {number} ordinary chain start')
        action = durations[number]
        require(1 <= element <= len(action), f'State {number} ordinary chain element is out of range')
        windows[str(number)] = [sum(action[:element - 1]) + offset, sum(action) - 1]
    return {**buttons['a'], 'punchBufferFrames': buttons['x']['bufferFrames'],
            'forwardPunch': {'helperBufferFrames': int(latch.one('var(40)')) - 1,
                             'helperLatchLine': latch.line, 'helperDecayLine': decay.line},
            'chainWindows': windows, 'deferredTargets': list(DEFERRED),
            'sourceLines': {str(number): entry.line for number, entry in relevant.items()}}


def source_permissions(negative, total):
    explicit = [controller for controller in negative if normalized(controller.one('type', '')) == 'varset'
                and normalized(controller.one('var(1)', '')) in ('1', '2')]
    require(len(explicit) == 2 and all(not any(re.search(r'\b430\b', value) for key, value in part.entries
            if key.startswith('trigger')) for part in explicit), 'State 430 explicit Wave eligibility has changed')
    natural = unique((c for c in negative if normalized(c.one('type', '')) == 'varset'
                      and normalized(c.one('var(2)', '')) == '1'), 'natural normal-move command permission')
    sweep.require_fields(natural, {'trigger4': 'stateno=400||stateno=410||stateno=430||stateno=440'},
                         'unsupported State 430 natural permission')
    require(set(map(normalized, natural.values('triggerall'))) == {'!ishelper', 'animtime=0'}
            and not natural.values('ignorehitpause'),
            'State 430 natural permission does not follow its action clock')
    return {'explicitCancel': False, 'naturalEntryTick': total}, natural.line


def inspect_source(archive, manifest):
    prior = state_crouch_heavy_wave.inspect_source(archive, manifest)
    members = prior['members']
    keys = ('anim', 'cmd', 'cns', 'st1', 'stcommon', 'Terry-2-3.cns', 'Helper.cns')
    with zipfile.ZipFile(archive) as source:
        contents = {key: state200.archive_member(source, members[key]['name']) for key in keys}
    require(all(state200.digest(raw) == members[key]['sha256'] for key, raw in contents.items()), 'source archive changed')
    definition, controls, hit, bounds, damage = source_attack(contents['st1'])
    actions, diagnostics = parse_air(contents['anim'].decode('latin-1'))
    wanted = (*MOVE_IDS, 5010, 5015, 5020, 5025)
    require(not any(f'duplicate action {number} ' in warning for warning in diagnostics for number in wanted),
            'duplicate Stage 25 AIR action')
    selected = {number: wave.source_action(actions, number, hold_last=number in (5015, 5025)) for number in wanted}
    source_hashes = state200.manifest_hashes(prior['zipSha256'], members)
    sprite_indices = {number: state200.validate_manifest(manifest, source_hashes, action)
                      for number, (action, _) in selected.items() if number >= 430}
    action, durations = selected[430]
    command = source_commands(contents, {number: selected[number][1] for number in MOVE_IDS}, prior)
    _, negative = move_scope(state_reentry.script_sections(contents['Terry-2-3.cns']), -2)
    permission, natural_line = source_permissions(negative, sum(durations))
    damage_rules = state_damage.source_globals(negative)
    movement = state200.movement_rules(state_reentry.script_sections(contents['cns']),
                                       state_reentry.script_sections(contents['stcommon']), actions)['groundReaction']
    hit_pause = number_in(hit.one('pausetime'), r'(\d+)\+\(helper\(40000\),var\(0\)\),(\d+)', 'State 430 hit pause')
    guard_pause = number_in(hit.one('guard.pausetime'), r'(\d+)\+\(helper\(40000\),var\(0\)\),(\d+)', 'State 430 guard pause')
    (push,) = number_in(hit.one('ground.velocity'), r'-(\d+(?:\.\d+)?)-ifelse\(var\(40\),0\.5,0\)',
                        'State 430 grounded velocity')
    reactions = {}
    for posture, initial, slide in (('standing', 5010, 5015), ('crouching', 5020, 5025)):
        reactions[posture] = {}
        for kind, number in (('initial', initial), ('slide', slide)):
            raw, times = selected[number]
            reactions[posture][kind] = {'action': number, 'durations': times,
                                        'clsn2': state200.collision_boxes(raw, 2, normalize_edges=True)}
    return {**{key: prior[key] for key in ('zipSha256', 'manifestSha256', 'members', 'defaultWave', 'mode2Wave')},
            'action': action_profile(action, [bounds]), 'spriteIndices': sprite_indices, 'damage': damage,
            'damageRules': damage_rules, 'groundMovement': movement,
            'command': command, 'wavePermission': permission, 'groundReactions': reactions,
            'groundDefaults': {'initialPush': fraction(push), 'hitPause': hit_pause, 'guardPause': guard_pause,
                               'hitTime': int(hit.one('ground.hittime')), 'slideTime': int(hit.one('ground.slidetime'))},
            'hitDef': {key: normalized(value) for key, value in hit.entries if not key.startswith('trigger')},
            'guardThreatFrames': wave.guard_threat(controls, durations, 430),
            'sourceLines': {'state': definition.line, 'hitdef': hit.line, 'exit': controls[-1].line,
                            'naturalPermission': natural_line, 'defaultWaveEntry': prior['sourceLines']['defaultWaveEntry'],
                            'mode2WaveEntry': prior['sourceLines']['mode2WaveEntry']}}


def ground_attack(audit, native, movement, *, number=430, height='Low', rejects_falling=True):
    defaults = audit['groundDefaults']
    source_movement = audit['groundMovement']
    require(all(movement[key] == source_movement[key] for key in ('velocityRetention', 'crouchGuardVelocityRetention',
                                                                  'lastHitStepScale'))
            and movement['crouchHitVelocityRetention'] == source_movement['crouchGuardVelocityRetention'],
            f'frozen reaction movement differs from the State {number} source')
    push = Fraction(defaults['initialPush']['numerator'], defaults['initialPush']['denominator'])
    for name in ('hit', 'guard'):
        measured = native[name]
        pause = measured['pause']
        require(Fraction(str(measured['initialPush'])) == push
                and (pause['attackerFrames'], pause['defenderFrames']) == defaults[f'{name}Pause'],
                f'State {number} {name} sample differs from the default source push or pause')
    action = audit['action']
    attack = state210.attack_fields({'strike': action['strikes'][0]['box'], 'active': action['active']},
                                    native['hit'], native['guard'], movement['velocityRetention'])
    attack.update(height=height, damage=0, dynamicDamage=audit['damage'], groundHitReaction='LightLow',
                  rejectsLyingTargets=True)
    if rejects_falling:
        attack['rejectsFallingTargets'] = True
    return attack


def bind_reactions(audit, native, fighter, binding, *, number=430, move_id=MOVE):
    bindings = {}
    for posture, timeline_key in (('standing', 'lightHitstun'), ('crouching', 'crouchingLightHitstun')):
        measured = native['groundReactions'][posture]
        source = audit['groundReactions'][posture]
        require(measured['effectiveStun'] == native['hit']['effectiveStun'],
                f'State {number} {posture} stun differs from its normal hit')
        timeline, first_hold = state_crouch_heavy_wave.ground_reaction(source, measured, posture)
        frozen = fighter['groundHurtboxes'][timeline_key]
        require(first_hold == 1 and frozen[:-1] == timeline[:-1]
                and frozen[-1]['boxes'] == timeline[-1]['boxes']
                and frozen[-1]['frames'] >= timeline[-1]['frames'],
                f'State {number} {posture} native reaction differs from the frozen hurtbox timeline')
        initial = f'stage25_{posture}_initial'
        followup = f'stage25_{posture}_slide'
        for name, action in ((initial, source['initial']['action']), (followup, source['slide']['action'])):
            binding['clips'][name] = {'action': action, 'clock': 'phase', 'loop': False}
        bindings[posture] = {'initial': initial, 'followup': followup, 'followupTick': measured['initialTicks']}
    binding['hitReactions'][move_id] = {**bindings['standing'], 'crouching': bindings['crouching']}


def apply_chains(audit, native, fighter):
    require(native.get('chainsVerified'), 'State 430 requires verified native ordinary transitions')
    moves = {number: unique((move for move in fighter['moves'] if move['moveId'] == move_id), move_id)
             for number, move_id in MOVE_IDS.items()}
    command = audit['command']
    for number, move in moves.items():
        first, last = command['chainWindows'][str(number)]
        # The numeric tick belongs to the next AIR element while the dispatcher
        # still sees the previously committed phase on the boundary.
        phases = 'Recovery' if number == 400 else 'Active,Recovery'
        window = {'windowFirstFrame': first, 'windowLastFrame': last, 'phases': phases}
        # Both standing punches use 5A. Try the distance-limited near punch before the far fallback.
        targets = (205, 200, 400, 430) if number == 430 else (430,)
        for target in targets:
            target_id = moves[target]['moveId']
            if any(rule['targetMoveId'] == target_id for rule in move['cancels']):
                continue
            rule = {
                'targetMoveId': target_id, **window,
                'requiresHitConfirm': False, 'requiresBlockConfirm': False, 'allowWhiff': True,
                'maximumInputAge': command['bufferFrames'] if target == 430 else command['punchBufferFrames'],
                'allowSameMove': number == target, 'standingInputOnly': target in (200, 205),
            }
            if number == 430 and target != 430:
                rule['retainBufferedMotionHelper'] = True
            move['cancels'].append(rule)
        move['reservedCancels'] = [{'commandId': RESERVED_STANDING_LIGHT_KICK, **window}]


def build_profile(audit, native, fighter, binding, mode, *, with_chains=True, default_wave_command=None):
    fighter, binding = copy.deepcopy((fighter, binding))
    fighter['fighterId'], binding['visualId'] = PRESETS[mode]
    fighter['displayName'] = f'Terry Crouching Light Kick ({mode})'
    commands = fighter['commands']
    reserved = unique((c for c in commands if c['commandId'] == sweep.RESERVED_LIGHT_KICK), 'frozen light kick reservation')
    command = audit['command']
    damage = fighter['damageRules']
    require(all(damage[config] == audit['damageRules'][source] for config, source in
                (('baseScale', 'scale'), ('attackMultiplier', 'attackMultiplier'),
                 ('hitstunDefenseMultiplier', 'defenseController'), ('counterGraceTicks', 'counterGraceTicks'),
                 ('varianceMaximum', 'randomMax'))) and audit['damageRules']['randomMin'] == 0,
            'frozen dynamic damage rules differ from the State 430 source')
    require(reserved['reserveGroundInput'] and reserved['buttons'] == ['LightKick']
            and reserved['directions'] == ['Down', 'DownForward', 'DownBack']
            and reserved['maxSequenceFrames'] == command['time']
            and reserved['bufferLifetimeFrames'] == command['bufferFrames'], 'frozen light kick reservation changed')
    real_command = {key: value for key, value in reserved.items() if key != 'reserveGroundInput'}
    real_command.update(commandId=COMMAND, directions=[])
    commands[commands.index(reserved)] = real_command
    if with_chains:
        # Insert the standing kick below both punch commands without ties in the
        # reservation's direction-set length or changes to the frozen profiles.
        for candidate in commands:
            if candidate['priority'] > reserved['priority']:
                candidate['priority'] += 1
            if candidate['commandId'] == wave.COMMAND_ID:
                candidate['priority'] += 1
                candidate['matchCompletedMotion'] = True
        commands.append({**reserved, 'commandId': RESERVED_STANDING_LIGHT_KICK,
                         'directions': ['Neutral', 'Back', 'Forward'], 'priority': 0})
        commands.append({**reserved, 'commandId': RESERVED_FORWARD_LIGHT_PUNCH, 'directions': ['Forward'],
                         'buttons': ['LightPunch'], 'bufferLifetimeFrames': 0, 'priority': 4})
        if mode == 'mode2':
            require(native.get('mode2NaturalWaveVerified'), 'State 430 requires verified native mode 2 natural Wave')
            require(default_wave_command is not None and default_wave_command['commandId'] == wave.COMMAND_ID
                    and default_wave_command.get('matchHeldMotion') and default_wave_command.get('bufferPolicy')
                    and not any(m['moveId'] == wave.MOVE_ID for m in fighter['moves']),
                    'mode 2 requires the default Wave gesture without the State 1000 move')
            commands.append({**copy.deepcopy(default_wave_command), 'commandId': RESERVED_MODE2_WAVE,
                             'reserveGroundInput': True, 'matchCompletedMotion': True,
                             'priority': default_wave_command['priority'] + 2})
    action = audit['action']
    attack = ground_attack(audit, native, fighter['movement']['groundReaction'])
    move = {'moveId': MOVE, 'commandId': COMMAND, 'allowedFrom': 'Crouching', 'returnsToCrouch': True,
            'walkOnCrouchRecovery': True,
            'startupFrames': action['startup'], 'activeFrames': action['active'], 'recoveryFrames': action['recovery'],
            'attacks': [attack], 'hurtboxTimeline': state210.hurtbox_timeline(action),
            'guardThreatFrames': audit['guardThreatFrames'], 'cancels': [], 'throws': [], 'projectiles': []}
    fighter['moves'].append(move)
    binding['clips']['state430'] = {'action': 430, 'clock': 'move', 'loop': False}
    binding['moves'][MOVE] = {phase: 'state430' for phase in ('Startup', 'Active', 'Recovery')}
    bind_reactions(audit, native, fighter, binding)
    if with_chains:
        apply_chains(audit, native, fighter)
        # These are the two crouching follow-up normals measured during blockstun.
        crouch_punch = unique((m for m in fighter['moves'] if m['moveId'] == MOVE_IDS[400]), 'frozen State 400')
        for followup in (move, crouch_punch):
            followup_attack, = followup['attacks']
            followup_attack['retainsGroundGuardOnFollowup'] = True
    return fighter, binding


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive', type=Path)
    parser.add_argument('manifest', type=Path)
    parser.add_argument('--base', type=Path)
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
        references, native = routes.verify_native_references(args.reference, audit, reference_stage=25, include_contract=True)
        require(native.get('completeVerified'), 'State 430 profile requires the complete native controls')
        bases = {mode: routes.load_frozen_base(args.base, mode, audit, frozen_base=FROZEN_BASE) for mode in PRESETS}
        default_wave = unique((c for c in bases['default'][0]['commands'] if c['commandId'] == wave.COMMAND_ID),
                              'frozen default weak Wave command')
        documents = {}
        for mode, frozen in bases.items():
            fighter, binding = build_profile(audit, native, *frozen, mode, default_wave_command=default_wave)
            documents.update({f'{mode}.fighter.json': fighter, f'{mode}.binding.json': binding,
                              f'{mode}.source.json': {**audit, 'native': native, 'references': references,
                                                     'frozenBaseSha256': FROZEN_BASE[mode], 'mode': mode}})
        state200.output_documents(args.output, documents)
    except (AssertionError, OSError, ValueError, KeyError, TypeError, IndexError, zipfile.BadZipFile) as error:
        parser.exit(2, f'Stage 25 conversion: {error}\n')


if __name__ == '__main__':
    main()
