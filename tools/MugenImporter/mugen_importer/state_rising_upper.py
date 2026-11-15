"""Import Terry's source- and trace-verified State 310 into the frozen Stage 29 presets."""

import argparse
from copy import deepcopy
import json
from pathlib import Path
import re
import zipfile

from . import state200, state210, state_reentry
from . import state_crouch_light_kick as crouch, state_ground_blowback as blowback
from . import state_forward_light_punch as forward, state_power_wave as wave
from . import state_power_wave_routes as routes, state_standing_heavy_kicks as heavy
from . import state_standing_light_kicks as kicks
from . import state_sweep_wave as sweep
from .air import parse_air
from .state200 import fraction, normalized, number_in, require, unique
from .state205215 import action_profile, move_scope


PLACEHOLDER = kicks.RESERVED_RISING_UPPER
COMMAND = 'RisingUpper'
MOVE_ID = 'TerryRisingUpper310'
RISING_TACKLE = 'ReservedRisingTackle1200'
PRESETS = {'default': ('TerryRisingUpper', 'terry98-stage30-default'),
           'mode2': ('TerryRisingUpperMode2', 'terry98-stage30-mode2')}
FROZEN_BASE = {
    'default': {
        'fighter': '3d962ac3e3697f378577d8f34c91d70604a66145ce276d15d7a431c164d064f1',
        'binding': 'abbf16b62642011f156e358c09b4ab4f4d997a6625445809f72be5458714a522',
        'source': '581cade48b8dcdd5dcfa3aa636ea44742e3c29f20d2e8a18010bcd3aa064b872',
    },
    'mode2': {
        'fighter': 'e6321de7b112b8e9fa39caa9cf3290a818af0ad51ef4b77c3452a6a844b52aaa',
        'binding': '8037830930ca6712dd01336db4751180a4b7b100bd43b3c2adf66bb53ce923e6',
        'source': 'd4be315a8c6b3699eebfdf778a453e7bb36598a90a67650d835254d53f340085',
    },
}
NORMAL_SOURCES = {int(move_id[-3:]): move_id for move_id in
                  (*forward.CANCEL_WINDOWS, kicks.MOVE_IDS[235], heavy.MOVE_IDS[245], blowback.MOVE_ID)}
FROZEN_RESERVATIONS = {235, 245, 250}


def inspect_command(raw_cmd, raw_helper, negative):
    commands = state_reentry.script_sections(raw_cmd)
    _, entries = move_scope(commands, -1)
    _, helper = move_scope(state_reentry.script_sections(raw_helper), 99999)
    entry = forward.crouch_command(entries, 310)
    shared = {'var(59)!=1', '!ishelper', 'roundstate=2', 'statetype!=a'}
    combo = 'command="y"&&command="holdfwd"&&command="holddown"'
    require(normalized(entry.one('type')) == 'changestate' and
            set(map(normalized, entry.values('triggerall'))) == shared | {f'{combo}||helper(99999),var(41)>0'}
            and list(map(normalized, entry.values('trigger1'))) == ['ctrl||(stateno=[100,101])||var(2)>=1'],
            'unsupported State 310 human CMD conditions')
    policy = kicks.edge_reservations(entries, helper)[PLACEHOLDER]
    single = unique((part for part in commands if part.title == 'command'
                     and normalized(part.one('name')) == '"y"'), 'physical y command')
    require(normalized(single.one('command')) == 'y' and normalized(single.one('time')) == '1',
            'unsupported State 310 root button command')
    defaults = unique((part for part in commands if part.title == 'defaults'), 'CMD defaults')
    require(normalized(defaults.one('command.buffer.time')) == '1' and not single.values('buffer.time'),
            'unsupported State 310 root buffer')
    creator = unique((part for part in negative if normalized(part.one('stateno', '')) == '99999'
                      and normalized(part.one('type')) == 'helper'), 'command Helper creation')
    sweep.require_fields(creator, {'id': '99999', 'keyctrl': '1', 'pausemovetime': '2147483647'},
                         'unsupported command Helper global Pause privilege')
    lines = {target: forward.crouch_command(entries, target).line
             for target in (1000, 1050, 720, 250, 805, 300, 310, 400, 410, 430, 440)}
    require(list(lines.values()) == sorted(lines.values()), 'State 310 human CMD priority changed')
    latch, decay = (part.line for part in helper if part.values('var(41)'))
    return {'commandId': COMMAND, 'buttons': ['HeavyPunch'], 'directions': ['DownForward'],
            'maxSequenceFrames': 1, 'buttonToleranceFrames': 0, 'bufferLifetimeFrames': 0,
            'chargeHoldFrames': 0, 'chargeReleaseToleranceFrames': 0, 'priority': 3,
            'reserveGroundInput': True, 'reserveOnEdgeDirection': True, 'bufferPolicy': policy}, entries, {
                'command': entry.line, 'helper': creator.line, 'latch': latch, 'decay': decay}


def inspect_rising_tackle(raw_cmd, raw_helper):
    commands = state_reentry.script_sections(raw_cmd)
    _, entries = move_scope(commands, -1)
    _, helper = move_scope(state_reentry.script_sections(raw_helper), 99999)
    target = unique((part for part in entries if part.one('value', '') == '1200'
                     and 'helper(99999),var(14)>0' in ''.join(map(normalized, part.values('triggerall')))),
                    'default Rising Tackle command entry')
    condition = unique((text for text in target.values('triggerall') if 'var(14)' in normalized(text)),
                       'default Rising Tackle command name')
    matched = re.fullmatch(r'command=("[^"]+")\|\|helper\(99999\),var\(14\)>0', normalized(condition))
    require(matched is not None, 'unsupported default Rising Tackle root or Helper command')
    command_name = matched[1]
    wave_entry = forward.crouch_command(entries, 1000)
    require(target.line < wave_entry.line and normalized(target.one('type')) == 'changestate'
            and set(map(normalized, target.values('triggerall'))) ==
                {'var(59)!=1', '!ishelper', 'roundstate=2', 'var(10)=0', 'statetype!=a', normalized(condition)}
            and normalized(target.one('trigger1')) == normalized(wave_entry.one('trigger1'))
            and normalized(target.one('trigger2')) == 'var(1)=1'
            and normalized(target.one('trigger3')) == 'var(1)=2', 'unsupported Rising Tackle permissions')
    pattern = unique((part for part in commands if part.title == 'command'
                      and normalized(part.one('name')) == command_name), 'weak Rising Tackle motion')
    require(normalized(pattern.one('command')) == '~f,d,df,x' and not pattern.values('buffer.time'),
            'unsupported Rising Tackle motion')
    slot = 'var(14)'
    latch, decay = [part for part in helper if part.values(slot)]
    sweep.require_fields(latch, {'type': 'varset', 'trigger1': f'command={command_name}', slot: '12'},
                         'unsupported weak Rising Tackle Helper latch')
    sweep.require_fields(decay, {'type': 'varadd', 'trigger1': f'{slot}>0', slot: 'ifelse(command="holdx",-1,-4)'},
                         'unsupported weak Rising Tackle Helper decay')
    require(latch.line < decay.line and not any(part.values('ignorehitpause') for part in (latch, decay)),
            'unsupported weak Rising Tackle Helper timing')
    # The weak default overlap is in scope. The strong and charge entries have separate commands.
    return {'commandId': RISING_TACKLE, 'buttons': ['LightPunch'], 'directions': ['Forward', 'Down', 'DownForward'],
            'maxSequenceFrames': int(pattern.one('time')), 'buttonToleranceFrames': 0,
            'bufferLifetimeFrames': 0, 'chargeHoldFrames': 0, 'chargeReleaseToleranceFrames': 0,
            'reserveGroundInput': True, 'matchHeldMotion': True, 'matchCompletedMotion': True,
            'matchInitialCardinalRelease': True,
            'bufferPolicy': {'helperTicks': int(latch.one(slot)), 'helperHeldDecay': 1,
                             'helperReleasedDecay': 4, 'preserveDuringHitPause': True}}, {
                                 'risingTackle': target.line, 'risingTackleMotion': pattern.line,
                                 'risingTackleLatch': latch.line, 'risingTackleDecay': decay.line}


def inspect_permissions(entries, negative, actions, body):
    def controller(variable, value):
        return unique((part for part in negative if normalized(part.one(variable, '')) == value
                       and any(re.search(r'\bstateno=310\b', normalized(text)) for key, text in part.entries
                               if key.startswith('trigger')) == (value == '2')),
                      f'{variable}={value} permission controller')

    entry = controller('var(1)', '1')
    contact = controller('var(1)', '2')
    natural = controller('var(2)', '2')
    require(all(normalized(part.one('type')) == 'varset' and not part.values('ignorehitpause')
                for part in (entry, contact, natural)), 'unsupported State 310 permission clock')
    require(normalized(entry.one('triggerall')) == '!ishelper'
            and normalized(contact.one('triggerall')) == '!ishelper'
            and set(map(normalized, natural.values('triggerall'))) == {'!ishelper', 'animtime=0'}
            and normalized(contact.one('trigger2')) == 'stateno=310&&(movecontact=1||movereversed=1)'
            and normalized(natural.one('trigger1')) == 'stateno=300||stateno=310',
            'unsupported State 310 contact or recovery permissions')
    windows = {}
    for key, condition in entry.entries:
        if not re.fullmatch(r'trigger\d+', key):
            continue
        (source,) = number_in(condition, r'stateno=(\d+)&&.*', 'State 310 permission source')
        require(source in NORMAL_SOURCES and source not in windows, f'unreviewed 310 source State {source}')
        _, durations = wave.source_action(actions, source)
        windows[source] = routes.cancel_window(condition, durations, source_state=source)
    require(windows.keys() == NORMAL_SOURCES.keys(), 'State 310 early source list differs')
    targets = {target: forward.crouch_command(entries, target) for target in (1000, 1050)}
    require(all(normalized(part.one('trigger3')) == 'var(1)=2'
                and 'var(2)>=1' in normalized(part.one('trigger1')) for part in targets.values())
            and 'var(10)=0' in map(normalized, targets[1000].values('triggerall'))
            and {'var(10)=1||var(10)=2', '!numproj'} <= set(map(normalized, targets[1050].values('triggerall'))),
            'unsupported State 310 weak Wave modes')
    ordinary_exits = [target for target in (200, 205, 210, 215, 230, 235, 240, 245, 250, 300, 310, 400, 410, 430, 440)
                      if any(re.search(r'\bstateno=310\b', normalized(text)) or 'var(1)=2' in normalized(text)
                             for key, text in forward.crouch_command(entries, target).entries if key.startswith('trigger'))]
    require(not ordinary_exits, f'unsupported early ordinary exits from State 310: {ordinary_exits}')
    return {'entryWindows': windows, 'earlyOrdinaryExits': ordinary_exits,
            'contactAge': 1, 'contactWindow': [body['startup'] + 1, body['startup'] + body['active']],
            'naturalEntryTick': sum(body['durations']),
            'cancelTargets': {target: part.line for target, part in targets.items()},
            'sourceLines': {'entryPermission': entry.line, 'contactPermission': contact.line,
                            'naturalPermission': natural.line}}


def inspect_air_followup(negative):
    limit = unique((part for part in negative if normalized(part.one('var(12)', '')) == '1'),
                   'source air target restriction')
    require(normalized(limit.one('triggerall')) == 'p2movetype=h&&p2statetype=a',
            'unsupported State 310 target eligibility gate')
    (limit_ticks,) = number_in(limit.one('trigger6'), r'\(var\(13\)=6\|\|var\(13\)=16\)&&var\(14\)>(\d+)',
                               'State 310 airborne follow-up age')
    registration = unique((part for part in negative if 'numtarget(60)&&var(6)=0' in
                           map(normalized, part.values('trigger5'))), 'State 310 ID 60 registration')
    require(set(map(normalized, registration.values('triggerall'))) ==
            {'p2movetype=h&&p2statetype=a', 'movehit=1'} and
            set(map(normalized, registration.values('trigger5'))) ==
            {'numtarget(60)&&var(6)=0', 'var(13):=6+((var(6)>=1||stateno>=3000)*10)+(var(40)>0)'} and
            set(map(normalized, registration.values('trigger6'))) ==
            {'(numtarget(40)||numtarget(50)||numtarget(60))&&var(6)>=1||numtarget(70)',
             'var(13):=8+((var(6)>=1||stateno>=3000)*10)'}, 'unsupported State 310 source ID 60 air lock')
    # The base profile supplies a shared prior-launch restriction. ID 60's own longer air age is
    # recorded here as source provenance because the measured neutral-jump launch lands before it.
    return {'authoredAgeThreshold': limit_ticks, 'sourceLine': registration.line, 'limitLine': limit.line}


def inspect_body(contents, action, durations, selected):
    definition, controls = move_scope(state_reentry.script_sections(contents['st1']), 310)
    fields = {'type': 's', 'movetype': 'a', 'physics': 'n', 'juggle': '0', 'poweradd': '0',
              'ctrl': '0', 'velset': '0,0', 'anim': '310', 'sprpriority': '2', 'facep2': '1'}
    sweep.require_fields(definition, fields, 'unsupported State 310 StateDef')
    require(set(key for key, _ in definition.entries) == fields.keys()
            and [normalized(part.one('type')) for part in controls] ==
                ['playsnd', 'playsnd', 'hitdef', 'hitdef', 'varadd', 'varadd', 'statetypeset', 'changestate'],
            'unsupported State 310 controllers or alternate mode')
    hit, suppressed = controls[2:4]
    active = unique((text for text in hit.values('trigger1') if 'animelemtime' in normalized(text)),
                    'State 310 HitDef active span')
    first, following = number_in(active, r'animelemtime\((\d+)\)>=0&&animelemtime\((\d+)\)<0',
                                 'State 310 HitDef AIR elements')
    require(set(map(normalized, hit.values('triggerall'))) == {'var(12)=0', '!movecontact&&!movereversed'}
            and set(map(normalized, hit.values('trigger1'))) == {normalized(active), 'var(28):=1'}
            and not hit.values('trigger2'), 'unsupported State 310 attack gate')
    sweep.require_fields(suppressed, {'triggerall': 'var(12)>0', 'trigger1': normalized(active), 'attr': ''},
                         'unsupported State 310 suspended HitDef')
    sweep.require_fields(hit, {'attr': 's,na', 'animtype': 'hard', 'air.animtype': 'back',
                               'guardflag': 'm', 'hitflag': 'maf', 'priority': '4,hit',
                               'ground.type': 'high', 'air.type': 'high', 'id': '60',
                               'air.fall': '1', 'ground.cornerpush.veloff': '0', 'air.cornerpush.veloff': '0'},
                         'unsupported State 310 contact')
    require(not any(hit.values(key) for key in ('fall', 'guard.velocity', 'guard.hittime', 'guard.slidetime'))
            and hit.one('ground.hittime') == hit.one('ground.slidetime'), 'unsupported State 310 grounded hit')
    counter, normal = number_in(hit.one('damage'),
                                r'floor\(\(ifelse\(var\(6\),(\d+),(\d+)\)-var\(21\)\)\*fvar\(7\)\),0',
                                'State 310 dynamic damage')
    push = r'-(\d+(?:\.\d+)?)-ifelse\(var\(40\),0\.5,0\)'
    ground = re.fullmatch(push, normalized(hit.one('ground.velocity')))
    air = re.fullmatch(f'{push},{push}', normalized(hit.one('air.velocity')))
    require(ground and air, 'unsupported State 310 velocity')
    pauses = [tuple(number_in(hit.one(key), r'(\d+)\+\(helper\(40000\),var\(0\)\),(\d+)', f'State 310 {key}'))
              for key in ('pausetime', 'guard.pausetime')]
    sweep.require_fields(controls[-2], {'trigger1': f'animelem={following}', 'movetype': 'i'},
                         'unsupported State 310 threat end')
    sweep.require_fields(controls[-1], {'trigger1': 'animtime=0', 'value': '0', 'ctrl': '1'},
                         'unsupported State 310 natural exit')
    # A shared HitDef uses a different attack box in each active AIR element.
    strikes = [(element, element + 1) for element in range(first, following)]
    return {'state': 310, 'stateDef': fields, 'action': action_profile(action, strikes),
            'guardThreatFrames': wave.guard_threat(controls, durations, 310),
            'damage': {'normalBase': normal, 'counterBase': counter, 'useContactDivisor': False},
            'hitPause': pauses[0], 'guardPause': pauses[1],
            'groundPush': fraction(ground[1]), 'airLaunchX': fraction(air[1]), 'airLaunchY': fraction(air[2]),
            'airGravity': fraction(hit.one('yaccel')), 'groundHitTime': int(hit.one('ground.hittime')),
            'airHitTime': int(hit.one('air.hittime')),
            'launchReaction': {**wave.launch_reaction(contents['stcommon'], selected, hit, fall_field='air.fall'),
                               'groundTech': sweep.source_ground_tech(contents, selected)},
            'hitDef': {key: normalized(value) for key, value in hit.entries if not key.startswith('trigger')},
            'sourceLines': {'state': definition.line, 'hitdef': hit.line, 'exit': controls[-1].line}}


def inspect_source(archive, manifest):
    prior, members, contents = blowback.read_source(archive, manifest)
    definition = unique((section for section in state_reentry.script_sections(contents['def'])
                         if section.title == 'files'), 'DEF files')
    require((definition.one('st1'), definition.one('st4'), definition.one('st5')) ==
            tuple(members[key]['name'] for key in ('st1', 'Terry-2-3.cns', 'Helper.cns')),
            'unsupported State 310 DEF file mapping')
    actions, diagnostics = parse_air(contents['anim'].decode('latin-1'))
    wanted = (310, *sorted(set(wave.LAUNCH_ACTIONS.values())), 5200)
    require(not any(f'duplicate action {number} ' in warning for warning in diagnostics for number in wanted),
            'duplicate State 310 or reaction AIR action')
    selected = {number: wave.source_action(actions, number, hold_last=number in (5050, 5160)) for number in wanted}
    hashes = state200.manifest_hashes(prior['zipSha256'], members)
    indices = {number: state200.validate_manifest(manifest, hashes, action)
               for number, (action, _) in selected.items()}
    action, durations = selected[310]
    body = inspect_body(contents, action, durations, selected)
    body['action']['spriteIndices'] = indices[310]
    _, negative = move_scope(state_reentry.script_sections(contents['Terry-2-3.cns']), -2)
    command, entries, command_lines = inspect_command(contents['cmd'], contents['Helper.cns'], negative)
    tackle, tackle_lines = inspect_rising_tackle(contents['cmd'], contents['Helper.cns'])
    permissions = inspect_permissions(entries, negative, actions, body['action'])
    return {**{key: prior[key] for key in ('zipSha256', 'manifestSha256', 'damageRules', 'groundMovement')},
            **body, **permissions, 'members': members, 'command': command, 'spriteIndices': indices,
            'risingTackleCommand': tackle, 'airFollowup': inspect_air_followup(negative),
            'sourceLines': {**body['sourceLines'], **permissions['sourceLines'], **command_lines, **tackle_lines}}


def install_command(audit, fighter, *, runs_during_global_pause=False):
    placeholder = unique((command for command in fighter['commands'] if command['commandId'] == PLACEHOLDER),
                         'frozen State 310 reservation')
    command = audit['command']
    require(placeholder == {**command, 'commandId': PLACEHOLDER}
            and not any(move['commandId'] == PLACEHOLDER for move in fighter['moves']),
            'frozen State 310 reservation differs from the audited command')
    replacement = deepcopy(command)
    if runs_during_global_pause:
        replacement['bufferPolicy']['helperRunsDuringGlobalPause'] = True
    fighter['commands'][fighter['commands'].index(placeholder)] = replacement


def install_entries(audit, verified_windows, fighter):
    windows = {int(key): value for key, value in verified_windows.items()}
    require(all(audit['entryWindows'].get(source) == bounds for source, bounds in windows.items()),
            'native State 310 entry window differs from the authored source')
    frozen_sources, installed = set(), set()
    for move in fighter['moves']:
        reservations = move.get('reservedCancels', [])
        placeholders = [rule for rule in reservations if rule['commandId'] == PLACEHOLDER]
        source = next((key for key, value in NORMAL_SOURCES.items() if value == move['moveId']), None)
        if placeholders:
            require(source in FROZEN_RESERVATIONS and len(placeholders) == 1, 'unreviewed frozen State 310 route')
            frozen_sources.add(source)
            require(placeholders[0] == {'commandId': PLACEHOLDER, 'phases': 'Active',
                                       'windowFirstFrame': audit['entryWindows'][source][0],
                                       'windowLastFrame': audit['entryWindows'][source][1]},
                    'unreviewed frozen State 310 route bounds')
        if source in windows:
            first, last = windows[source]
            require(move['startupFrames'] <= first <= last < move['startupFrames'] + move['activeFrames'],
                    f'State {source} to 310 exceeds the active window')
            new = {'commandId': COMMAND, 'phases': 'Active', 'windowFirstFrame': first, 'windowLastFrame': last}
            move['reservedCancels'] = ([new if rule['commandId'] == PLACEHOLDER else rule for rule in reservations]
                                       if placeholders else [*reservations, new])
            installed.add(source)
        elif placeholders:
            move['reservedCancels'] = [rule for rule in reservations if rule['commandId'] != PLACEHOLDER]
    require(frozen_sources == FROZEN_RESERVATIONS and installed == windows.keys(),
            'State 310 frozen or verified source moves missing')


def add_body(audit, native, fighter, binding):
    body = audit['action']
    measured = native['move310']
    require(native.get('bodyVerified') and measured['durations'] == body['durations']
            and measured['attackWindow'] == [body['startup'], body['startup'] + body['active'] - 1]
            and tuple(measured['hitPause']) == audit['hitPause'] and tuple(measured['guardPause']) == audit['guardPause']
            and all(fraction(str(measured[key])) == audit[key] for key in ('groundPush', 'airLaunchX', 'airLaunchY'))
            and fraction(str(measured['guardPush'])) == audit['groundPush']
            and measured['guardTicks'] == audit['groundHitTime']
            and measured['groundHitTicks'] == audit['groundHitTime'],
            'State 310 native hurt, guard or attack timing differs from the authored state')

    inherited = unique((move for move in fighter['moves'] if move['moveId'] == forward.HEAVY_REACTION_MOVE),
                       'frozen grounded heavy reaction')
    reaction, = inherited['attacks']
    require((reaction['hitstunFrames'], reaction['blockstunFrames']) ==
            (measured['groundHitEffectiveTicks'], measured['guardEffectiveTicks']),
            'State 310 effective stun differs from the frozen heavy reaction')
    hit = {'damage': 0, 'initialPush': measured['groundPush'], 'effectiveStun': reaction['hitstunFrames'],
           'pause': dict(zip(('attackerFrames', 'defenderFrames'), measured['hitPause'], strict=True))}
    guard = {'damage': 0, 'initialPush': measured['guardPush'], 'effectiveStun': reaction['blockstunFrames'],
             'pause': dict(zip(('attackerFrames', 'defenderFrames'), measured['guardPause'], strict=True))}
    retention = fighter['movement']['groundReaction']['velocityRetention']
    launch = audit['launchReaction']
    attacks = []
    for strike in body['strikes']:
        attack = state210.attack_fields({'strike': strike['box'], 'active': body['active']}, hit, guard, retention)
        attack.update(firstActiveFrame=strike['first'], lastActiveFrame=strike['last'], contactGroup=1,
                      dynamicDamage=audit['damage'], groundHitReaction='Heavy', groundGuardOnly=True,
                      rejectsLyingTargets=True, respectsAirFollowupRestriction=True,
                      launchOnAirborneOnly=True, launchVelocityX=audit['airLaunchX'],
                      launchVelocityY=audit['airLaunchY'], launchGravity=audit['airGravity'],
                      launchReaction=launch, knockdownFrames=launch['impactFrames'] + launch['lieDownFrames'],
                      wakeupFrames=launch['wakeupFrames'])
        if native.get('followupGuardVerified'):
            attack['retainsGroundGuardOnFollowup'] = True
        attacks.append(attack)
    move = {'moveId': MOVE_ID, 'commandId': COMMAND, 'allowedFrom': 'GroundIdle',
            'startupFrames': body['startup'], 'activeFrames': body['active'], 'recoveryFrames': body['recovery'],
            'attacks': attacks, 'hurtboxTimeline': state210.hurtbox_timeline(body),
            'guardThreatFrames': audit['guardThreatFrames'], 'faceOpponentOnStart': audit['stateDef']['facep2'] == '1',
            'deferCrouchOnNaturalExit': True,
            'cancels': [], 'throws': [], 'projectiles': []}
    require(MOVE_ID not in binding['moves'] and 'state310' not in binding['clips'],
            'frozen profile already has State 310 visual binding')
    fighter['moves'].append(move)
    binding['clips']['state310'] = {'action': 310, 'clock': 'move', 'loop': False}
    binding['moves'][MOVE_ID] = dict.fromkeys(('Startup', 'Active', 'Recovery'), 'state310')
    binding['hitReactions'][MOVE_ID] = {**deepcopy(binding['hitReactions'][inherited['moveId']]),
                                       'zeroBasedLaunchClock': True}
    for phase, action in wave.LAUNCH_ACTIONS.items():
        require(binding['clips'].get(f'launch_{phase}') == {'action': action, 'clock': 'phase', 'loop': False},
                f'frozen launch clip {phase} changed')
    return move


def install_exit(audit, native, fighter, move, mode):
    commands = {command['commandId']: command for command in fighter['commands']}
    wave_moves = [entry for entry in fighter['moves'] if entry['moveId'] == wave.MOVE_ID]
    if mode == 'mode2':
        require(not wave_moves and wave.COMMAND_ID not in commands and
                commands[crouch.RESERVED_MODE2_WAVE]['reserveGroundInput'],
                'mode 2 must retain only the State 1050 reservation')
    first, last = audit['contactWindow']
    permission = {'windowFirstFrame': first, 'windowLastFrame': last, 'phases': 'Active',
                  'maximumContactAge': audit['contactAge'], 'requiresHitConfirm': True, 'requiresBlockConfirm': True}
    wave_verified = native.get('defaultContactWaveVerified' if mode == 'default' else 'mode2ContactWaveVerified')
    tackle_verified = mode == 'default' and native.get('risingTacklePriorityVerified')
    require(not (wave_verified or tackle_verified) or native.get('contactWaveAge') == audit['contactAge'],
            'State 310 native contact age changed')
    if mode == 'default':
        target, = wave_moves
        require(target['commandId'] == wave.COMMAND_ID and
                [entity['projectileTypeId'] for entity in target['projectiles']] == [wave.ENTITY_ID],
                'frozen State 1000 command or helper changed')
        if wave_verified:
            move['cancels'].append({**permission, 'targetMoveId': wave.MOVE_ID, 'allowWhiff': False})
        if tackle_verified:
            priority = commands[wave.COMMAND_ID]['priority'] + 1
            require(RISING_TACKLE not in commands and all(candidate['priority'] < priority for candidate in commands.values()),
                    'frozen Rising Tackle priority conflict')
            fighter['commands'].append({**deepcopy(audit['risingTackleCommand']), 'priority': priority})
            move['reservedCancels'] = [{**permission, 'commandId': RISING_TACKLE}]
    elif wave_verified:
        move['reservedCancels'] = [{**permission, 'commandId': crouch.RESERVED_MODE2_WAVE}]


def build_profile(audit, native, base_fighter, base_binding, mode, *, body_only=False):
    windows = native.get('entryWindows', {})
    if not body_only:
        require(native.get('completeVerified') and native.get('defaultContactWaveVerified')
                and native.get('mode2ContactWaveVerified') and native.get('followupGuardVerified')
                and native.get('risingTacklePriorityVerified') and native.get('globalPauseHelperVerified')
                and native.get('naturalEntryTick') == audit['naturalEntryTick']
                and {int(key): value for key, value in windows.items()} == audit['entryWindows'],
                'State 310 requires complete native body, entry, contact, pause and recovery references')
    fighter, binding = deepcopy((base_fighter, base_binding))
    fighter['fighterId'], binding['visualId'] = PRESETS[mode]
    fighter['resetPauseClockOnCrouchEntry'] = True
    if body_only:
        binding['visualId'] = f'terry98-stage30-body-{mode}'
    fighter['displayName'] = f'Terry Rising Upper ({mode})'
    install_command(audit, fighter, runs_during_global_pause=native.get('globalPauseHelperVerified', False))
    install_entries(audit, windows, fighter)
    move = add_body(audit, native, fighter, binding)
    install_exit(audit, native, fighter, move, mode)
    return fighter, binding


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive', type=Path)
    parser.add_argument('manifest', type=Path)
    parser.add_argument('--base', type=Path)
    parser.add_argument('--reference', type=Path, action='append')
    parser.add_argument('--output', type=Path)
    parser.add_argument('--body-only', action='store_true', help='emit a local profile from the verified body proof')
    args = parser.parse_args()
    try:
        audit = inspect_source(args.archive, args.manifest)
        if not args.output:
            print(json.dumps(audit, indent=2, sort_keys=True))
            return
        if not args.base or not args.reference:
            parser.error('--output requires --base and --reference')
        references, native = routes.verify_native_references(args.reference, audit, reference_stage=30,
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
        parser.exit(2, f'Stage 30 conversion: {error}\n')


if __name__ == '__main__':
    main()
