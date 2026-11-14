"""Import the audited Terry State 440 sweep and its weak Wave cancel."""

import argparse
import copy
from fractions import Fraction
import json
from pathlib import Path
import re
import zipfile

from . import state200, state210, state_crouch_heavy_wave, state_power_wave as wave
from . import state_power_wave_routes as routes, state_reentry
from .air import parse_air
from .state200 import fraction, normalized, number_in, require, unique
from .state205215 import action_profile, hit_window, move_scope


MOVE = 'TerryCrouchHeavyKick440'
COMMAND = '2D'
RESERVED_LIGHT_KICK = 'ReservedCrouchLightKick'
RESERVED_STANDING_HEAVY_KICK = 'ReservedStandHeavyKick'
PRESETS = {'default': ('TerrySweepWave', 'terry98-stage24-default'),
           'mode2': ('TerrySweepWaveMode2', 'terry98-stage24-mode2')}
FROZEN_BASE = {
    'default': {
        'fighter': '861432ee56fb8db00ecfe4a742b10c57893ddc54ad73abe8621430c429e4231c',
        'binding': '9139018b89024f1a3abeb99261f88cda77bb0b5a518018931c078fa8e59d01de',
        'source': '19ebabe4da527dc7b3107da6af1b0ee7f40c67e707b4e13131878b84ed1cb353',
    },
    'mode2': {
        'fighter': '7201a3663556d6cbd1af81c4b2800c53229bba194badc62089d8e1f64d618c3f',
        'binding': '6a4b8876e068916354c12066f7b7062fb18b636d07a6a5386f1236ff09cb7f10',
        'source': 'e34823e31c6a317ab429b4ec290a76f5ade2291906760dbbb5f1de82797bf0b3',
    },
}


def require_fields(section, fields, message):
    require(all(normalized(section.one(key)) == value for key, value in fields.items()), message)


def source_attack(archive_member):
    definition, controls = move_scope(state_reentry.script_sections(archive_member), 440)
    require_fields(definition, {'type': 'c', 'movetype': 'a', 'physics': 'n', 'ctrl': '0', 'velset': '0,0',
                                'anim': '440', 'facep2': '1'}, 'unsupported State 440 StateDef')
    require([normalized(c.one('type')) for c in controls] ==
            ['playsnd', 'playsnd', 'hitdef', 'hitdef', 'varadd', 'varadd', 'statetypeset', 'changestate'],
            'unsupported State 440 controller sequence')
    hit, inactive = controls[2:4]
    require(set(map(normalized, hit.values('triggerall'))) == {'var(12)=0', '!movecontact&&!movereversed'}
            and set(map(normalized, inactive.values('triggerall'))) == {'var(12)>0'}
            and inactive.one('attr') == '', 'unsupported State 440 contact or inactive HitDef')
    require_fields(hit, {'attr': 'c,na', 'animtype': 'hard', 'ground.type': 'trip', 'guardflag': 'l',
                         'hitflag': 'maf', 'ground.cornerpush.veloff': '0', 'air.cornerpush.veloff': '0',
                         'fall': '1', 'fall.recover': '1', 'fall.recovertime': '600'},
                   'unsupported State 440 ground trip or defense rule')
    bounds = hit_window(hit)
    require(len(hit.values('trigger1')) == 2 and 'var(28):=1' in map(normalized, hit.values('trigger1'))
            and hit_window(inactive) == bounds and len(inactive.values('trigger1')) == 1,
            'State 440 HitDef triggers differ from the audited window')
    counter, normal = number_in(hit.one('damage'), r'floor\(\(ifelse\(var\(6\),(\d+),(\d+)\)-var\(21\)\)\*fvar\(7\)\),0',
                                'State 440 dynamic damage')
    recovery, exit_state = controls[-2:]
    require_fields(recovery, {'trigger1': f'animelem={bounds[1]}', 'movetype': 'i'}, 'unsupported State 440 recovery')
    require_fields(exit_state, {'trigger1': 'animtime=0', 'value': '11', 'ctrl': '1'}, 'unsupported State 440 exit')
    return definition, controls, hit, bounds, {'normalBase': normal, 'counterBase': counter, 'useContactDivisor': False}


def source_entry(contents, default_wave_line):
    commands = state_reentry.script_sections(contents['cmd'])
    defaults = unique((section for section in commands if section.title == 'defaults'), 'CMD defaults')
    remap = unique((section for section in commands if section.title == 'remap'), 'CMD remap')
    require_fields(remap, {'a': 'a', 'b': 'b', 'x': 'x', 'y': 'y'}, 'unsupported raw button remap')
    single = unique((section for section in commands if section.title == 'command' and section.one('name') == '"b"'),
                    'physical b command')
    require_fields(single, {'command': 'b', 'time': '1'}, 'unsupported physical b command')
    require(int(defaults.one('command.buffer.time')) == 1, 'unsupported physical b command buffer')
    _, entries = move_scope(commands, -1)
    entry = unique((section for section in entries if section.one('value', '') == '440'
                    and 'command="holddown"' in map(normalized, section.values('triggerall'))), 'human State 440 command')
    require(set(map(normalized, entry.values('triggerall'))) == {
        'var(59)!=1', '!ishelper', 'roundstate=2', 'command="b"||helper(99999),var(3)>0',
        'command="holddown"', 'statetype!=a',
    } and normalized(entry.one('trigger1')) == 'ctrl||(stateno=[100,101])||var(2)>=1'
        and not entry.values('trigger2'), 'unsupported State 440 command eligibility')
    earlier = []
    for number in ('310', '400', '410', '430'):
        candidates = [item for item in entries if item.one('value', '') == number
                      and 'var(59)!=1' in map(normalized, item.values('triggerall'))]
        earlier.append(unique(candidates, f'human CMD entry {number}').line)
    require(default_wave_line < earlier[0] < earlier[1] < earlier[2] < earlier[3] < entry.line,
            'State 440 CMD priority differs from the audited sequence')
    for number in ('245', '240'):
        standing = unique((item for item in entries if item.one('value', '') == number
                           and 'var(59)!=1' in map(normalized, item.values('triggerall'))), f'human CMD entry {number}')
        require(standing.line < entry.line and {'command="b"||helper(99999),var(3)>0', 'command!="holddown"',
                                               'statetype!=a'}.issubset(map(normalized, standing.values('triggerall')))
                and normalized(standing.one('trigger1')) == normalized(entry.one('trigger1')),
                'unsupported standing heavy kick competition')
    _, helper = move_scope(state_reentry.script_sections(contents['Helper.cns']), 99999)
    latch, decay = [c for c in helper if c.values('var(3)')]
    require_fields(latch, {'type': 'varset', 'trigger1': 'command="b"'}, 'unsupported State 440 Helper latch')
    require_fields(decay, {'type': 'varadd', 'trigger1': 'var(3)>0', 'var(3)': '-1'}, 'unsupported State 440 Helper decay')
    require(latch.line < decay.line and not latch.values('ignorehitpause') and not decay.values('ignorehitpause'),
            'unsupported State 440 Helper clock')
    return entry, {'time': int(single.one('time')), 'bufferFrames': int(latch.one('var(3)')) - 1,
                   'helperLatchLine': latch.line, 'helperDecayLine': decay.line}


def source_trip(common, selected, hit):
    sections = state_reentry.script_sections(common)
    shake, shake_controls = move_scope(sections, 5070)
    falling, fall_controls = move_scope(sections, 5071)
    require_fields(shake, {'type': 'a', 'movetype': 'h', 'physics': 'n', 'velset': '0,0'}, 'unsupported State 5070')
    require_fields(falling, {'type': 'a', 'movetype': 'h', 'physics': 'n'}, 'unsupported State 5071')
    change_anim, leave_shake = shake_controls
    set_vel, gravity, land = fall_controls
    require_fields(change_anim, {'type': 'changeanim', 'trigger1': '1', 'value': '5070'}, 'unsupported trip Action')
    require_fields(leave_shake, {'type': 'changestate', 'trigger1': 'hitshakeover', 'value': '5071'}, 'unsupported trip freeze')
    require_fields(set_vel, {'type': 'hitvelset', 'trigger1': 'time=0', 'x': '1', 'y': '1'}, 'unsupported trip velocity start')
    require_fields(gravity, {'type': 'veladd', 'trigger1': '1', 'y': 'gethitvar(yaccel)'}, 'unsupported trip gravity')
    require_fields(land, {'type': 'changestate', 'value': '5100'}, 'unsupported trip landing')
    require(set(map(normalized, land.values('trigger1'))) == {'vely>0', 'posy>=0'}, 'unsupported trip landing threshold')
    return {**wave.launch_reaction(common, selected, hit), 'startInFall': True}


def source_ground_tech(contents, selected):
    commands = state_reentry.script_sections(contents['cmd'])
    recovery = unique((c for c in commands if c.title == 'command' and c.one('name') == '"recovery"'), 'recovery CMD')
    require_fields(recovery, {'command': 'x+a', 'time': '1'}, 'unsupported recovery command')
    global_sections = state_reentry.script_sections(contents['Terry-2-3.cns'])
    _, negative = move_scope(global_sections, -2)
    reset, latch, decay = [c for c in negative if c.values('var(4)')]
    require_fields(reset, {'type': 'varset', 'var(4)': '0', 'trigger1': 'movetype!=h',
                           'trigger2': 'stateno=5120||stateno=5201', 'trigger3': 'alive=0'}, 'unsupported recovery reset')
    require(set(map(normalized, latch.values('triggerall'))) == {'!ishelper', 'movetype=h', 'var(4)=0'}
            and set(map(normalized, latch.values('trigger1'))) == {'var(59)!=1', 'command="recovery"||helper(99999),var(6)>0'},
            'unsupported recovery input permission')
    require_fields(decay, {'type': 'varadd', 'var(4)': '-1', 'trigger1': 'var(4)>0'}, 'unsupported recovery countdown')
    require(reset.line < latch.line < decay.line and not any(c.values('ignorehitpause') for c in (reset, latch, decay)),
            'unsupported recovery buffer clock')
    common = state_reentry.script_sections(contents['stcommon'])
    _, impact = move_scope(common, 5100)
    enter = unique((c for c in impact if c.one('value', '') == '5201'), 'ground tech branch')
    require_fields(enter, {'type': 'changestate', 'trigger1': 'var(4)>=1'}, 'unsupported ground tech branch')
    require(set(map(normalized, enter.values('triggerall'))) == {'time=0', 'alive=1', 'gethitvar(fall.recover)=1'},
            'unsupported ground tech landing permission')
    tech, controls = move_scope(common, 5201)
    require_fields(tech, {'type': 's', 'movetype': 'i', 'physics': 'n', 'velset': '0,0', 'anim': '5200', 'ctrl': '0'},
                   'unsupported ground tech StateDef')
    turn, position, _, immunity, _, speed, retain, stop, push, exit_state = controls
    require_fields(turn, {'type': 'turn'}, 'unsupported ground tech turn')
    require(set(map(normalized, turn.values('trigger1'))) == {'time=0', 'velx>0'}, 'unsupported ground tech turn trigger')
    require_fields(position, {'type': 'posset', 'trigger1': 'time=0', 'y': '0'}, 'unsupported ground tech position')
    require_fields(immunity, {'type': 'nothitby', 'trigger1': '1', 'value': 'sca'}, 'unsupported ground tech protection')
    require_fields(speed, {'type': 'velset', 'trigger1': 'animelem=1'}, 'unsupported ground tech speed')
    require_fields(retain, {'type': 'velmul', 'trigger1': 'animelemtime(1)>0'}, 'unsupported ground tech slowdown')
    require_fields(stop, {'type': 'velset', 'x': '0'}, 'unsupported ground tech stop')
    require_fields(push, {'type': 'playerpush', 'trigger1': '1', 'value': '0'}, 'unsupported ground tech player push')
    require_fields(exit_state, {'type': 'changestate', 'trigger1': 'animtime=0', 'value': '0', 'ctrl': '1'},
                   'unsupported ground tech exit')
    durations = selected[5200][1]
    (stop_element,) = number_in(stop.one('trigger1'), r'animelem=(\d+)', 'ground tech stop element')
    require(1 < stop_element <= len(durations), 'ground tech stops outside its AIR Action')
    return {'inputBufferFrames': int(latch.one('var(4)')) - 1,
            'durationFrames': sum(durations), 'slideFrames': sum(durations[:stop_element - 1]),
            'initialSpeed': fraction(-Fraction(speed.one('x'))), 'velocityRetention': fraction(retain.one('x'))}


def source_juggle_limit(negative, helper_script, attack):
    hit_id = int(attack.one('id'))
    reset, cap = [c for c in negative if c.values('var(12)')]
    require_fields(reset, {'type': 'varset', 'var(12)': '0', 'ignorehitpause': '1'}, 'unsupported juggle reset')
    require({normalized(reset.one(f'trigger{number}')) for number in range(1, 6)} ==
            {'movecontact=1', 'p2movetype!=h', 'p2statetype!=a', 'enemynear,ctrl=1', 'p2stateno=5120'},
            'unsupported juggle reset conditions')
    require_fields(cap, {'type': 'varset', 'var(12)': '1', 'ignorehitpause': '1', 'trigger1': 'var(13)=1',
                         'triggerall': 'p2movetype=h&&p2statetype=a'}, 'unsupported juggle cap')
    registration = unique((c for c in negative if 'var(13):=1' in map(normalized, c.values('trigger1'))),
                          'air hit juggle classification')
    require_fields(registration, {'type': 'null', 'ignorehitpause': '1'}, 'unsupported juggle registration')
    require(set(map(normalized, registration.values('triggerall'))) == {'p2movetype=h&&p2statetype=a', 'movehit=1'}
            and set(map(normalized, registration.values('trigger1'))) ==
            {'var(13):=1', f'numtarget(10)&&var(6)=0||numtarget({hit_id})||numtarget(30)&&var(6)=0'}
            and reset.line < cap.line < registration.line, 'State 440 does not register native aerial hit cap')
    _, controls = move_scope(state_reentry.script_sections(helper_script), 1020)
    weak_hits = [c for c in controls if normalized(c.one('type')) == 'hitdef' and
                 normalized(c.one('attr', '')) == 's,sp' and 'var(1)=1' in map(normalized, c.values('trigger1'))]
    require(len(weak_hits) == 2 and all({'root,var(12)=0', 'root,var(13)=0||root,var(13)>=10'} <=
                set(map(normalized, c.values('triggerall'))) for c in weak_hits), 'unsupported weak Wave juggle permission')
    return {'hitId': hit_id, 'resetLine': reset.line, 'capLine': cap.line, 'registrationLine': registration.line,
            'helperHitDefs': [c.line for c in weak_hits]}


def inspect_source(archive, manifest):
    prior = state_crouch_heavy_wave.inspect_source(archive, manifest)
    members = prior['members']
    keys = ('anim', 'cmd', 'st1', 'stcommon', 'Terry-2-3.cns', 'Terry-S.cns', 'Helper.cns')
    with zipfile.ZipFile(archive) as source:
        contents = {key: state200.archive_member(source, members[key]['name']) for key in keys}
    require(all(state200.digest(raw) == members[key]['sha256'] for key, raw in contents.items()), 'source archive changed')
    definition, controls, hit, bounds, damage = source_attack(contents['st1'])
    entry, command = source_entry(contents, prior['sourceLines']['defaultWaveEntry'])
    actions, diagnostics = parse_air(contents['anim'].decode('latin-1'))
    wanted = (440, 5070, 5100, 5035, 5120, 5170, 5200)
    require(not any(f'duplicate action {number} ' in warning for warning in diagnostics for number in wanted),
            'duplicate Stage 24 AIR action')
    selected = {number: wave.source_action(actions, number, hold_last=number == 5070) for number in wanted}
    source_hashes = state200.manifest_hashes(prior['zipSha256'], members)
    sprite_indices = {number: state200.validate_manifest(manifest, source_hashes, action)
                      for number, (action, _) in selected.items()}
    action, durations = selected[440]
    trip, trip_ticks = selected[5070]
    require(trip_ticks[-1] == -1, 'trip Action must hold its final pose')
    _, negative = move_scope(state_reentry.script_sections(contents['Terry-2-3.cns']), -2)
    permission = unique((c for c in negative if c.one('var(1)', '') == '1'
                         and normalized(c.one('trigger8', '')).startswith('stateno=440&&')), 'State 440 cancel permission')
    require_fields(permission, {'type': 'varset', 'triggerall': '!ishelper'}, 'unsupported State 440 cancel controller')
    require(not permission.values('ignorehitpause'), 'unsupported State 440 cancel timing during HitPause')
    hit_pause = number_in(hit.one('pausetime'), r'(\d+)\+\(helper\(40000\),var\(0\)\),(\d+)', 'State 440 hit pause')
    guard_pause = number_in(hit.one('guard.pausetime'), r'(\d+)\+\(helper\(40000\),var\(0\)\),(\d+)', 'State 440 guard pause')
    boost = r'-ifelse\(var\(40\),0\.5,0\)'
    ground_speed = re.fullmatch(r'-(\d+(?:\.\d+)?)' + boost + r',-(\d+(?:\.\d+)?)' + boost,
                                normalized(hit.one('ground.velocity')))
    guard_speed = re.fullmatch(r'-(\d+(?:\.\d+)?)' + boost, normalized(hit.one('guard.velocity')))
    require(ground_speed is not None and guard_speed is not None, 'unsupported State 440 hit velocity or guard push')
    juggle = source_juggle_limit(negative, contents['Terry-S.cns'], hit)
    launch = {**source_trip(contents['stcommon'], selected, hit), 'suppressOwnerProjectilesDuringFall': True}
    return {**{key: prior[key] for key in ('zipSha256', 'manifestSha256', 'members', 'defaultWave', 'mode2Wave')},
            'action': action_profile(action, [bounds]), 'spriteIndices': sprite_indices, 'damage': damage,
            'command': command, 'launchReaction': launch, 'techAction': 5200,
            'groundTech': source_ground_tech(contents, selected), 'juggleLimit': juggle,
            'trip': {'action': 5070, 'durations': trip_ticks, 'clsn2': state200.collision_boxes(trip, 2, normalize_edges=True)},
            'groundDefaults': {'guardPush': fraction(guard_speed[1]), 'hitPause': hit_pause, 'guardPause': guard_pause,
                               'hitTime': int(hit.one('ground.hittime')), 'launchX': fraction(ground_speed[1]),
                               'launchY': fraction(ground_speed[2]), 'gravity': fraction(hit.one('yaccel'))},
            'hitDef': dict(hit.entries),
            'cancelWindow': routes.cancel_window(permission.one('trigger8'), durations, source_state=440),
            'guardThreatFrames': wave.guard_threat(controls, durations, 440),
            'sourceLines': {'state': definition.line, 'hitdef': hit.line, 'command': entry.line,
                            'cancel': permission.line, 'exit': controls[-1].line,
                            'defaultWaveEntry': prior['sourceLines']['defaultWaveEntry'],
                            'mode2WaveEntry': prior['sourceLines']['mode2WaveEntry']}}


def build_profile(audit, native, fighter, binding, mode, *, with_cancel=True):
    fighter, binding = copy.deepcopy((fighter, binding))
    fighter['fighterId'], binding['visualId'] = PRESETS[mode]
    fighter['displayName'] = f'Terry Crouching Heavy Kick ({mode})'
    commands = {item['commandId']: item for item in fighter['commands']}
    require(fighter.get('useCommandPriority') and not {COMMAND, RESERVED_LIGHT_KICK, RESERVED_STANDING_HEAVY_KICK} & commands.keys()
            and all('HeavyKick' not in item['buttons'] for item in commands.values() if 'buttons' in item),
            'frozen command priority or distinct heavy kick is unavailable')
    require(native['lightKickPriorityVerified'], 'State 440 requires verified native kick priority')
    require(native['groundTechVerified'], 'State 440 requires verified native ground tech')
    command = audit['command']
    last_priority = min(c['priority'] for c in commands.values())
    common_command = {'maxSequenceFrames': command['time'], 'buttonToleranceFrames': 0,
                      'chargeHoldFrames': 0, 'chargeReleaseToleranceFrames': 0}
    fighter['commands'].extend([
        {**common_command, 'commandId': RESERVED_LIGHT_KICK, 'directions': ['Down', 'DownForward', 'DownBack'],
         'buttons': ['LightKick'], 'bufferLifetimeFrames': 0, 'priority': last_priority - 1, 'reserveGroundInput': True},
        {**common_command, 'commandId': RESERVED_STANDING_HEAVY_KICK, 'directions': ['Neutral', 'Back', 'Forward'],
         'buttons': ['HeavyKick'], 'bufferLifetimeFrames': 0, 'priority': last_priority - 2, 'reserveGroundInput': True},
        {**common_command, 'commandId': COMMAND, 'directions': [], 'buttons': ['HeavyKick'],
         'bufferLifetimeFrames': command['bufferFrames'], 'priority': last_priority - 2},
    ])
    action = audit['action']
    hit, guard = native['hit'], native['guard']
    defaults = audit['groundDefaults']
    require(all(fraction(str(hit[key])) == defaults[key] for key in ('launchX', 'launchY'))
            and tuple(hit['pause'][key] for key in ('attackerFrames', 'defenderFrames')) == defaults['hitPause'],
            'State 440 native launch or pause differs from the authored rule')
    require(fraction(str(guard['initialPush'])) == defaults['guardPush']
            and tuple(guard['pause'][key] for key in ('attackerFrames', 'defenderFrames')) == defaults['guardPause'],
            'State 440 native guard push or pause differs from the authored rule')
    movement = fighter['movement']['groundReaction']
    attack = state210.attack_fields({'strike': action['strikes'][0]['box'], 'active': action['active']},
                                    {**hit, 'initialPush': guard['initialPush'], 'effectiveStun': defaults['hitTime']},
                                    guard, movement['velocityRetention'])
    trip, tech = audit['trip'], audit['groundTech']
    launch = {**audit['launchReaction'], 'groundTech': tech}
    measured = native['trip']
    require(measured['shakeAction'] == trip['action'] and measured['fallTicks'] == hit['fallTicks']
            and measured['lieImpactFrames'] + measured['lieDownFrames'] == launch['lieDownFrames']
            and all(measured[key] == launch[key] for key in ('impactFrames', 'lieImpactFrames', 'wakeupFrames')),
            'State 440 native trip phases differ from authored reaction')
    require(native['tech'] == {'commandAgeFrames': tech['inputBufferFrames'] - 1,
                               'duration': tech['durationFrames'], 'action': audit['techAction'],
                               'stopTick': tech['slideFrames']}, 'State 440 native tech differs from authored reaction')
    attack.update(height='Low', damage=0, dynamicDamage=audit['damage'], groundKnockback=fraction(0),
                  launchVelocityX=defaults['launchX'], launchVelocityY=defaults['launchY'], launchGravity=defaults['gravity'],
                  launchReaction=launch, knockdownFrames=launch['impactFrames'] + launch['lieDownFrames'],
                  wakeupFrames=launch['wakeupFrames'])
    move = {'moveId': MOVE, 'commandId': COMMAND, 'allowedFrom': 'Crouching', 'returnsToCrouch': True,
            'startupFrames': action['startup'], 'activeFrames': action['active'], 'recoveryFrames': action['recovery'],
            'attacks': [attack], 'hurtboxTimeline': state210.hurtbox_timeline(action),
            'guardThreatFrames': audit['guardThreatFrames'], 'cancels': [], 'throws': [], 'projectiles': []}
    fighter['moves'].append(move)
    durations = trip['durations'][:-1]
    fighter['groundHurtboxes']['tripFall'] = state210.hurtbox_timeline({**trip,
        'durations': [*durations, max(1, hit['fallTicks'] - sum(durations) + 1)]})
    binding['clips']['state440'] = {'action': action['id'], 'clock': 'move', 'loop': False}
    binding['clips']['launch_trip'] = {'action': trip['action'], 'clock': 'phase', 'loop': False}
    binding['clips']['launch_Tech'] = {'action': audit['techAction'], 'clock': 'phase', 'loop': False}
    binding['moves'][MOVE] = {phase: 'state440' for phase in ('Startup', 'Active', 'Recovery')}
    if mode == 'mode2':
        require(wave.COMMAND_ID not in commands and not any(m['moveId'] == wave.MOVE_ID for m in fighter['moves']),
                'mode 2 cannot select State 1000')
        return fighter, binding

    target = unique((m for m in fighter['moves'] if m['moveId'] == wave.MOVE_ID), 'frozen State 1000')
    projectile, = target['projectiles']
    require(target['commandId'] == wave.COMMAND_ID and projectile['projectileTypeId'] == wave.ENTITY_ID,
            'frozen State 1000 lacks its weak Wave command or Helper')
    projectile['timeline']['respectsAirFollowupRestriction'] = True
    if with_cancel:
        require(native['cancelVerified'], 'State 440 requires a verified native cancel window')
        first, last = audit['cancelWindow']
        require(action['startup'] <= first <= last < action['startup'] + action['active'],
                'State 440 cancel window must be within Active')
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
        if args.output is None:
            print(json.dumps(audit, indent=2, sort_keys=True))
            return
        if not args.base or not args.reference:
            parser.error('--output requires --base and --reference')
        references, native = routes.verify_native_references(args.reference, audit, reference_stage=24, include_contract=True)
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
        parser.exit(2, f'Stage 24 conversion: {error}\n')


if __name__ == '__main__':
    main()
