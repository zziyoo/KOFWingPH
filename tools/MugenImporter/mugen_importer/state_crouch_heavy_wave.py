"""Convert the audited Terry crouching heavy and its native weak Wave route."""

import argparse
import copy
from fractions import Fraction
import json
from pathlib import Path
import re
import zipfile

from . import state200, state210, state_power_wave as wave, state_power_wave_heavy as heavy
from . import state_power_wave_routes as routes, state_reentry
from .air import parse_air
from .state200 import normalized, number_in, require, unique
from .state205215 import action_profile, hit_window, move_scope


MOVE = 'TerryCrouchHeavyPunch410'
INTERRUPT_MOVE = 'TerryStandLightPunch205'
RESERVED_RISING_UPPER = 'ReservedRisingUpper'
PRESETS = {'default': ('TerryCrouchHeavyWave', 'terry98-stage23-default'),
           'mode2': ('TerryCrouchHeavyWaveMode2', 'terry98-stage23-mode2')}
FROZEN_BASE = {
    'default': {
        'fighter': 'c64dc5514470cf062ee1821f7740ca8abcf7eac9f79a7da88b7f54eb36515ff2',
        'binding': '8547520de868bdf21d90c1a1290ea07fa3926a5372a82c90595f738563d1ee4d',
        'source': '532ed8cd4b2dc41b45051dc533c45e125978705eee80ccf7a1d2f61f154070bd',
    },
    'mode2': {
        'fighter': '86f3c53cc32c3c82d879e283ccda6e774631c26e1674a1f1d45b715e6787e7b1',
        'binding': '1b4f7945b829a133ea6fb9ede9d4f8dc76b8ce520e226e75a0731c1e446b35b2',
        'source': '70e620ba2d04b499c0f080b536e59314c83c5654df2177671120ce20f85b0937',
    },
}


def source_attack(archive_member):
    definition, controls = move_scope(state_reentry.script_sections(archive_member), 410)
    require(all(normalized(definition.one(key)) == value for key, value in
                {'type': 'c', 'movetype': 'a', 'physics': 'n', 'ctrl': '0', 'velset': '0,0',
                 'anim': '410', 'facep2': '1'}.items()), 'unsupported State 410 StateDef')
    require([normalized(c.one('type')) for c in controls] ==
            ['playsnd', 'playsnd', 'hitdef', 'hitdef', 'varadd', 'varadd', 'statetypeset', 'changestate'],
            'unsupported State 410 controller sequence')
    hit, inactive = controls[2:4]
    require(set(map(normalized, hit.values('triggerall'))) == {'var(12)=0', '!movecontact&&!movereversed'}
            and set(map(normalized, inactive.values('triggerall'))) == {'var(12)>0'}
            and inactive.one('attr') == '', 'unsupported State 410 contact or inactive HitDef')
    require(all(normalized(hit.one(key)) == value for key, value in
                {'attr': 'c,na', 'animtype': 'hard', 'ground.type': 'high', 'guardflag': 'm',
                 'hitflag': 'maf', 'ground.cornerpush.veloff': '0'}.items())
            and normalized(hit.one('fall', '0')) == '0',
            'unsupported State 410 ground or guard reaction')
    bounds = hit_window(hit)
    require(len(hit.values('trigger1')) == 2 and 'var(28):=1' in map(normalized, hit.values('trigger1'))
            and hit_window(inactive) == bounds and len(inactive.values('trigger1')) == 1,
            'State 410 HitDef triggers differ from the audited window')
    counter, normal = number_in(hit.one('damage'), r'floor\(\(ifelse\(var\(6\),(\d+),(\d+)\)-var\(21\)\)\*fvar\(7\)\),0',
                                'State 410 dynamic damage')
    recovery, exit_state = controls[-2:]
    require(normalized(recovery.one('trigger1')) == f'animelem={bounds[1]}'
            and normalized(recovery.one('movetype')) == 'i'
            and normalized(exit_state.one('trigger1')) == 'animtime=0'
            and (exit_state.one('value'), exit_state.one('ctrl')) == ('11', '1'),
            'unsupported State 410 recovery or crouch exit')
    return definition, controls, hit, bounds, {'normalBase': normal, 'counterBase': counter, 'useContactDivisor': False}


def source_entry(commands):
    _, entries = move_scope(state_reentry.script_sections(commands), -1)
    entry = unique((c for c in entries if c.one('value', '') == '410'
                    and 'command="holddown"' in map(normalized, c.values('triggerall'))), 'human State 410 command')
    require(set(map(normalized, entry.values('triggerall'))) == {
        'var(59)!=1', '!ishelper', 'roundstate=2', 'command="y"||helper(99999),var(2)>0',
        'command="holddown"', 'statetype!=a',
    } and normalized(entry.one('trigger1')) == 'ctrl||(stateno=[100,101])||var(2)>=1'
        and not entry.values('trigger2'), 'unsupported State 410 command eligibility')
    upper = unique((c for c in entries if c.one('value', '') == '310'
                    and 'command="y"&&command="holdfwd"&&command="holddown"||helper(99999),var(41)>0'
                    in map(normalized, c.values('triggerall'))), 'human State 310 priority')
    require(upper.line < entry.line and normalized(upper.one('trigger1')) == normalized(entry.one('trigger1'))
            and normalized(upper.one('trigger2')) == 'var(1)=1',
            'State 310 command does not preempt State 410')
    return entry, upper


def inspect_source(archive, manifest):
    prior = heavy.inspect_source(archive, manifest)
    members = prior['members']
    with zipfile.ZipFile(archive) as source:
        contents = {key: state200.archive_member(source, members[key]['name'])
                    for key in ('anim', 'cmd', 'cns', 'st1', 'stcommon', 'Terry-2-3.cns', 'Terry-S.cns')}
    require(all(state200.digest(raw) == members[key]['sha256'] for key, raw in contents.items()), 'source archive changed')
    definition, controls, hit, bounds, damage = source_attack(contents['st1'])
    entry, upper = source_entry(contents['cmd'])
    actions, diagnostics = parse_air(contents['anim'].decode('latin-1'))
    guard_actions = {'standingGuard': 150, 'crouchingGuard': 151,
                     'standingGuardReady': 130, 'crouchingGuardReady': 131}
    wanted = (*guard_actions.values(), 410, 1020, 5002, 5007, 5012, 5017, 5020, 5022, 5025, 5027)
    require(not any(f'duplicate action {number} ' in message for message in diagnostics for number in wanted),
            'duplicate Stage 23 AIR action')
    source_hashes = state200.manifest_hashes(prior['zipSha256'], members)
    action, durations = wave.source_action(actions, 410)
    sprite_indices = {410: state200.validate_manifest(manifest, source_hashes, action)}
    common = state_reentry.script_sections(contents['stcommon'])
    movement = state200.movement_rules(state_reentry.script_sections(contents['cns']), common, actions)['groundReaction']
    _, crouch_slide = move_scope(common, 5011)
    brake = unique((c for c in crouch_slide if normalized(c.one('type')) == 'velmul'
                    and normalized(c.one('trigger1')) == 'time>=gethitvar(slidetime)'), 'crouching slide brake')
    restore = unique((c for c in crouch_slide if normalized(c.one('type')) == 'changestate'), 'crouching slide exit')
    require(state200.fraction(brake.one('x')) == movement['lastHitStepScale']
            and (normalized(restore.one('trigger1')), restore.one('value'), restore.one('ctrl')) == ('hitover', '11', '1'),
            'unsupported crouching slide braking or return')
    reactions = {}
    for posture, initial, slide in (('standing', 5002, 5007), ('crouching', 5022, 5027),
                                    ('crouchingLight', 5020, 5025), ('waveLow', 5012, 5017)):
        selected = {}
        for kind, number in (('initial', initial), ('slide', slide)):
            reaction, ticks = wave.source_action(actions, number, hold_last=kind == 'slide')
            require(bool(ticks) and (ticks[-1] == -1) == (kind == 'slide'), f'unsupported Action {number} reaction')
            sprite_indices[number] = state200.validate_manifest(manifest, source_hashes, reaction)
            selected[kind] = {'action': number, 'durations': ticks,
                              'clsn2': state200.collision_boxes(reaction, 2, normalize_edges=True)}
        reactions[posture] = selected
    guards = {}
    for field, number in guard_actions.items():
        guard, ticks = wave.source_action(actions, number)
        require(len(ticks) == 1, f'Action {number} guard pose is not static')
        sprite_indices[number] = state200.validate_manifest(manifest, source_hashes, guard)
        guards[field] = state210.hurtbox_timeline({
            'durations': ticks, 'clsn2': state200.collision_boxes(guard, 2, normalize_edges=True),
        })[0]['boxes']
        require(bool(guards[field]), f'Action {number} has no guard hurtboxes')
    _, negative = move_scope(state_reentry.script_sections(contents['Terry-2-3.cns']), -2)
    permission = unique((c for c in negative if c.one('var(1)', '') == '1'
                         and normalized(c.one('trigger7', '')).startswith('stateno=410&&')), 'State 410 cancel permission')
    require(normalized(permission.one('type')) == 'varset' and normalized(permission.one('triggerall')) == '!ishelper'
            and not permission.values('ignorehitpause'), 'unsupported State 410 cancel controller')
    fields = ('pausetime', 'guard.pausetime', 'ground.velocity', 'ground.slidetime', 'ground.hittime', 'hitflag',
              'guardflag', 'animtype', 'ground.type', 'air.type', 'air.animtype', 'air.velocity', 'air.hittime',
              'fall.yvelocity', 'yaccel', 'priority')
    push = re.fullmatch(r'-(\d+(?:\.\d+)?)-ifelse\(var\(40\),0\.5,0\)', normalized(hit.one('ground.velocity')))
    require(push is not None, 'unsupported State 410 ground push formula')
    hit_pause = number_in(hit.one('pausetime'), r'(\d+)\+\(helper\(40000\),var\(0\)\),(\d+)', 'State 410 hit pause')
    guard_pause = number_in(hit.one('guard.pausetime'), r'(\d+)\+\(helper\(40000\),var\(0\)\),(\d+)', 'State 410 guard pause')
    qcf_entry = prior['sourceLines']['defaultWaveEntry']
    require(qcf_entry < upper.line < entry.line, 'State 1000 must precede directional and normal heavy commands')
    _, helper = move_scope(state_reentry.script_sections(contents['Terry-S.cns']), 1020)
    late = unique((c for c in helper if normalized(c.one('type')) == 'hitdef'
                   and normalized(c.one('attr', '')) == 's,sp' and 'var(1)=1' in map(normalized, c.values('trigger1'))
                   and normalized(c.one('ground.type')) == 'low'), 'late weak Power Wave HitDef')
    require(normalized(late.one('animtype')) == 'hard' and normalized(late.one('fall', '0')) == '0',
            'unsupported late Wave ground reaction')
    late_first, late_exit = hit_window(late)
    _, wave_ticks = wave.source_action(actions, 1020)
    require(1 <= late_first < late_exit <= len(wave_ticks), 'late Wave element window is out of range')
    return {**{key: prior[key] for key in ('zipSha256', 'manifestSha256', 'members', 'defaultWave', 'mode2Wave')},
            'action': action_profile(action, [bounds]), 'spriteIndices': sprite_indices, 'damage': damage,
            'groundReactions': reactions, 'hitDef': {key: normalized(hit.one(key)) for key in fields},
            'groundGuardHurtboxes': guards,
            'groundDefaults': {
                'initialPush': state200.fraction(push[1]), 'hitPause': hit_pause, 'guardPause': guard_pause,
            },
            'crouchHitVelocityRetention': movement['crouchGuardVelocityRetention'],
            'lastHitStepScale': movement['lastHitStepScale'],
            'cancelWindow': routes.cancel_window(permission.one('trigger7'), durations, source_state=410),
            'guardThreatFrames': wave.guard_threat(controls, durations, 410),
            'lateWave': {'firstFrame': sum(wave_ticks[:late_first - 1]), 'lastFrame': sum(wave_ticks[:late_exit - 1]) - 1},
            'sourceLines': {'state': definition.line, 'hitdef': hit.line, 'command': entry.line,
                            'priority310': upper.line, 'cancel': permission.line, 'exit': controls[-1].line,
                            'defaultWaveEntry': prior['sourceLines']['defaultWaveEntry'],
                            'mode2WaveEntry': prior['sourceLines']['mode2WaveEntry'], 'lateWaveHitdef': late.line}}


def ground_reaction(source, measured, posture):
    initial, slide = source['initial'], source['slide']
    initial_ticks, stun = measured['initialTicks'], measured['effectiveStun']
    first_hold = initial_ticks - sum(initial['durations'])
    tail = stun - initial_ticks - sum(slide['durations'][:-1])
    require(first_hold >= 0 and tail > 0
            and measured.get('initialAction', initial['action']) == initial['action']
            and measured.get('slideAction', slide['action']) == slide['action'],
            f'State 410 {posture} reaction timeline differs from native')
    durations = [initial['durations'][0] + first_hold, *initial['durations'][1:], *slide['durations'][:-1], tail]
    return state210.hurtbox_timeline({'durations': durations, 'clsn2': initial['clsn2'] + slide['clsn2']}), first_hold


def bind_ground_reaction(audit, native, fighter, binding, variant, timeline_key):
    source = audit['groundReactions'][variant]
    timing = native['groundReactions'][variant]
    timeline, hold = ground_reaction(source, timing, variant)
    require(hold == 1, f'unsupported {variant} visual reaction hold: {hold}')
    fighter['groundHurtboxes'][timeline_key] = timeline
    clips = {}
    for phase in ('initial', 'slide'):
        clips[phase] = f'stage23_{variant}_{phase}'
        binding['clips'][clips[phase]] = {'action': source[phase]['action'], 'clock': 'phase', 'loop': False}
    return {'initial': clips['initial'], 'followup': clips['slide'], 'followupTick': timing['initialTicks']}


def build_profile(audit, native, fighter, binding, mode, *, with_cancel=True):
    fighter, binding = copy.deepcopy((fighter, binding))
    fighter['fighterId'], binding['visualId'] = PRESETS[mode]
    fighter['displayName'] = f'Terry Crouching Heavy ({mode})'
    fighter['useCommandPriority'] = True
    commands = {command['commandId']: command for command in fighter['commands']}
    require(commands['5B']['buttons'] == ['HeavyPunch'] and commands['5A']['priority'] > commands['5B']['priority'],
            'frozen normal CMD priority or heavy punch input differs')
    reserve_priority = commands['5A']['priority'] + 1
    if mode == 'default':
        require(commands[wave.COMMAND_ID]['priority'] == reserve_priority, 'unexpected frozen Weak Wave CMD priority')
        commands[wave.COMMAND_ID]['priority'] = reserve_priority + 1
    fighter['commands'].append({
        'commandId': RESERVED_RISING_UPPER, 'directions': ['DownForward'], 'buttons': ['HeavyPunch'],
        'maxSequenceFrames': 1, 'buttonToleranceFrames': 0, 'priority': reserve_priority,
        'bufferLifetimeFrames': 0, 'chargeHoldFrames': 0, 'chargeReleaseToleranceFrames': 0,
        'reserveGroundInput': True,
    })
    require(not any(m['moveId'] == MOVE or m['commandId'] == '5B' and m['allowedFrom'] != 'Standing'
                    for m in fighter['moves']), 'frozen crouching heavy punch route already exists')
    action = audit['action']
    defaults = audit['groundDefaults']
    expected_push = Fraction(defaults['initialPush']['numerator'], defaults['initialPush']['denominator'])
    for outcome in ('hit', 'guard'):
        measured = native[outcome]
        require(Fraction(str(measured['initialPush'])) == expected_push
                and tuple(measured['pause'][key] for key in ('attackerFrames', 'defenderFrames')) == defaults[f'{outcome}Pause'],
                f'State 410 {outcome} sample differs from the default source push or pause')
    movement = fighter['movement']['groundReaction']
    require(movement['lastHitStepScale'] == audit['lastHitStepScale'], 'frozen ground slide brake differs from source')
    movement['crouchHitVelocityRetention'] = audit['crouchHitVelocityRetention']
    fighter['groundHurtboxes'].update(audit['groundGuardHurtboxes'])
    attack = state210.attack_fields({'strike': action['strikes'][0]['box'], 'active': action['active']},
                                    native['hit'], native['guard'], movement['velocityRetention'])
    attack.update(damage=0, dynamicDamage=audit['damage'], groundHitReaction='Heavy')
    move = {'moveId': MOVE, 'commandId': '5B', 'allowedFrom': 'Crouching', 'returnsToCrouch': True,
            'startupFrames': action['startup'], 'activeFrames': action['active'], 'recoveryFrames': action['recovery'],
            'attacks': [attack], 'hurtboxTimeline': state210.hurtbox_timeline(action),
            'guardThreatFrames': audit['guardThreatFrames'], 'cancels': [], 'throws': [], 'projectiles': []}
    fighter['moves'].append(move)
    binding['clips']['state410'] = {'action': 410, 'clock': 'move', 'loop': False}
    binding['moves'][MOVE] = {phase: 'state410' for phase in ('Startup', 'Active', 'Recovery')}
    for posture in ('standing', 'crouching'):
        timing = native['groundReactions'][posture]
        require(timing['effectiveStun'] == native['hit']['effectiveStun'], f'unsupported {posture} reaction stun')
    standing = bind_ground_reaction(audit, native, fighter, binding, 'standing', 'heavyHitstun')
    crouching = bind_ground_reaction(audit, native, fighter, binding, 'crouching', 'crouchingHeavyHitstun')
    binding['hitReactions'][MOVE] = {**standing, 'crouching': crouching}
    interrupt = unique((m for m in fighter['moves'] if m['moveId'] == INTERRUPT_MOVE), 'frozen State 205')
    interrupt_hit, = interrupt['attacks']
    require(interrupt_hit['hitstunFrames'] == native['groundReactions']['crouchingLight']['effectiveStun'],
            'frozen State 205 stun differs from native crouching interruption')
    interrupt_hit['groundHitReaction'] = 'CrouchingLight'
    binding['hitReactions'][INTERRUPT_MOVE]['crouching'] = bind_ground_reaction(
        audit, native, fighter, binding, 'crouchingLight', 'crouchingLightHitstun')
    if mode != 'default':
        require(not any(m['moveId'] == wave.MOVE_ID for m in fighter['moves'])
                and not any(c['commandId'] == wave.COMMAND_ID for c in fighter['commands']),
                'mode 2 cannot select State 1000')
        return fighter, binding
    if not with_cancel:
        return fighter, binding
    require(native['cancelVerified'], 'State 410 requires a verified native cancel window')
    first, last = audit['cancelWindow']
    require(action['startup'] <= first <= last < action['startup'] + action['active'],
            'State 410 cancel window must be within Active')
    target = unique((m for m in fighter['moves'] if m['moveId'] == wave.MOVE_ID), 'frozen State 1000')
    require(target['commandId'] == wave.COMMAND_ID and
            [p['projectileTypeId'] for p in target['projectiles']] == [wave.ENTITY_ID],
            'frozen State 1000 lacks its weak Wave command or Helper')
    spans = target['projectiles'][0]['timeline']['hitSpans']
    late = unique((span for span in spans if all(span[key] == value for key, value in audit['lateWave'].items())),
                  'frozen grounded late Wave span')
    require('launchReaction' not in late and late['hit']['launchVelocityY']['numerator'] == 0
            and late['hit']['hitstunFrames'] == native['groundReactions']['waveLow']['effectiveStun'],
            'frozen late Wave launch or stun differs from native')
    late['hit']['groundHitReaction'] = 'HeavyLow'
    wave_low = bind_ground_reaction(audit, native, fighter, binding, 'waveLow', 'lowHeavyHitstun')
    binding['hitReactions'][wave.ENTITY_ID] = {**wave_low, 'crouching': crouching}
    move['cancels'].append({'targetMoveId': wave.MOVE_ID, 'windowFirstFrame': first, 'windowLastFrame': last,
                            'phases': 'Active', 'requiresHitConfirm': False, 'requiresBlockConfirm': False, 'allowWhiff': True})
    return fighter, binding


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive', type=Path)
    parser.add_argument('manifest', type=Path)
    parser.add_argument('--base', type=Path)
    parser.add_argument('--reference', type=Path, action='append')
    parser.add_argument('--output', type=Path)
    parser.add_argument('--body-only', action='store_true')
    args = parser.parse_args()
    try:
        audit = inspect_source(args.archive, args.manifest)
        if not args.output:
            print(json.dumps(audit, indent=2, sort_keys=True))
            return
        if not args.base or not args.reference:
            parser.error('--output requires --base and --reference')
        references, native = routes.verify_native_references(args.reference, audit, reference_stage=23, include_contract=True)
        documents = {}
        for mode in PRESETS:
            base = routes.load_frozen_base(args.base, mode, audit, frozen_base=FROZEN_BASE)
            fighter, binding = build_profile(audit, native, *base, mode, with_cancel=not args.body_only)
            documents.update({f'{mode}.fighter.json': fighter, f'{mode}.binding.json': binding,
                              f'{mode}.source.json': {**audit, 'native': native, 'references': references,
                                                     'frozenBaseSha256': FROZEN_BASE[mode], 'mode': mode,
                                                     'bodyOnly': args.body_only}})
        state200.output_documents(args.output, documents)
    except (AssertionError, OSError, ValueError, KeyError, TypeError, IndexError, zipfile.BadZipFile) as error:
        parser.exit(2, f'Stage 23 conversion: {error}\n')


if __name__ == '__main__':
    main()
