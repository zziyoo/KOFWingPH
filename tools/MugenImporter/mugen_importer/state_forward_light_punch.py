"""Convert Terry's forward light punch (Back Knuckle, State 300) onto its real command."""

import argparse
import copy
import json
import re
import zipfile
from fractions import Fraction
from pathlib import Path

from . import state200, state210, state_power_wave as wave
from . import state_power_wave_routes as routes, state_reentry, state_sweep_wave as sweep
from . import state_standing_heavy_kicks as heavy, state_standing_light_kicks as light
from .air import parse_air
from .state200 import fraction, normalized, number_in, require, unique
from .state205215 import move_scope

COMMAND = 'ForwardLightPunch'
PLACEHOLDER = 'ReservedForwardLightPunch'
MOVE_ID = 'TerryBackKnuckle300'
HEAVY_REACTION_MOVE = heavy.HEAVY_REACTION_MOVE
WAVE_WINDOW = (18, 19)
# var(1)=1 cancel windows into State 300 (stateTime units, inclusive), native-verified per source.
CANCEL_WINDOWS = {
    'TerryStandLightPunch200': (4, 6),
    'TerryStandLightPunch205': (4, 6),
    'TerryStandHeavyPunch215': (4, 12),
    'TerryCrouchLightPunch400': (4, 4),
    'TerryCrouchHeavyPunch410': (6, 12),
    'TerryCrouchHeavyKick440': (11, 17),
}
PRESETS = {'default': ('TerryForwardLightPunch', 'terry98-stage28-default'),
           'mode2': ('TerryForwardLightPunchMode2', 'terry98-stage28-mode2')}
FROZEN_BASE = {
    'default': {
        'fighter': 'e7c85992bf39d80a51f7f3e4e3ea1e731fe2ae91b45b1dd46552043f193d9491',
        'binding': '0db404bfa493cbe984f680196c70dcb00b118c9ea73f593b21418e6c58e25a13',
        'source': 'ad54e867453e5b5dc73af0c94429b283afefae1b69dd98448ac98fa80cae5f95',
    },
    'mode2': {
        'fighter': '8c689b21ee58ffe18c62861d6050edab93f5d53f2795a6506baa938ca3ea747a',
        'binding': 'f07234334c241184f4285312ffad63a41ea77004c7016ee27b57f68b218f9827',
        'source': 'e114de9a9e99b29aba2d63ae138402863d6a82baf4cf8a41130a47a8c0dbb474',
    },
}
ELEMENT_DURATIONS = (3, 4, 4, 3, 3, 3, 8, 6, 6, 7)
SEGMENT_WINDOWS = ((4, 5), (6, 7))
STRIKE_BOXES = ((36, -92, 83, -61), (36, -88, 99, -57))


def element_starts(durations):
    starts, total = [], 0
    for duration in durations:
        starts.append(total)
        total += duration
    return starts


def source_steps(controls, starts):
    steps = {}
    for controller in controls:
        require(set(key for key, _ in controller.entries) == {'type', 'trigger1', 'x'},
                f'line {controller.line}: unsupported State 300 PosAdd fields')
        elements = [int(value) for value in
                    re.findall(r'animelem=(\d+)', normalized(controller.one('trigger1')))]
        (x,) = number_in(controller.one('x'), r'(-?\d+)', f'line {controller.line} PosAdd distance')
        require(elements and len(elements) == len(set(elements)) and
                all(1 <= element <= len(ELEMENT_DURATIONS) for element in elements),
                f'line {controller.line}: unsupported State 300 PosAdd elements')
        for element in elements:
            tick = starts[element - 1]
            steps[tick] = steps.get(tick, 0) + int(x)
    return [{'frame': frame, 'x': fraction(x)} for frame, x in sorted(steps.items())]


def source_command(commands, helper, entries):
    by_name = {name: crouch_command(entries, number) for name, number in
               (('forwardLightPunch', 300), ('risingUpper', 310), ('kickThrow', 805))}
    lines = {name: part.line for name, part in by_name.items()}
    require(lines['kickThrow'] < lines['forwardLightPunch'] < lines['risingUpper'],
            'State 300 human CMD priority changed')
    entry = by_name['forwardLightPunch']
    require(normalized(entry.one('type')) == 'changestate', 'unsupported State 300 CMD entry type')
    shared = {'var(59)!=1', '!ishelper', 'roundstate=2', 'statetype!=a'}
    disjunct = 'command="x"&&command="holdfwd"&&command!="holddown"||helper(99999),var(40)>0'
    require(set(map(normalized, entry.values('triggerall'))) == shared | {disjunct}
            and list(map(normalized, entry.values('trigger1'))) == ['ctrl||(stateno=[100,101])||var(2)>=1']
            and list(map(normalized, entry.values('trigger2'))) == ['var(1)=1'],
            'unsupported State 300 human CMD conditions')
    variable = 'var(40)'
    latch, decay = [part for part in helper if part.values(variable)]
    combo = 'command="x"&&command="holdfwd"&&command!="holddown"'
    sweep.require_fields(latch, {'type': 'varset', 'trigger1': combo, variable: '4'},
                         'unsupported forward light punch Helper latch')
    sweep.require_fields(decay, {'type': 'varadd', 'trigger1': f'{variable}>0', variable: '-1'},
                         'unsupported forward light punch Helper decay')
    require(latch.line < decay.line and not any(part.values('ignorehitpause') for part in (latch, decay)),
            'unsupported forward light punch Helper clock')
    ordinary = [normalized(value) for part in entries
                if normalized(part.one('type', '')) == 'changestate'
                and 'var(59)!=1' in map(normalized, part.values('triggerall'))
                for key, value in part.entries if key.startswith('trigger')
                and re.search(r'\bstateno=300\b', normalized(value))]
    require(not ordinary, 'State 300 early ordinary chain appeared in the human CMD')
    return {'commandId': COMMAND, 'buttons': ['LightPunch'], 'directions': ['Forward'],
            'maxSequenceFrames': 1, 'buttonToleranceFrames': 0, 'bufferLifetimeFrames': 0,
            'chargeHoldFrames': 0, 'chargeReleaseToleranceFrames': 0, 'reserveGroundInput': True,
            'reserveOnEdgeDirection': True, 'priority': 4,
            'bufferPolicy': {'preserveDuringHitPause': True, 'helperTicks': int(latch.one(variable)),
                             'helperHeldDecay': 1, 'helperReleasedDecay': 1,
                             'helperRunsDuringGlobalPause': True},
            'sourceLines': {'cmd': entry.line, 'latch': latch.line, 'decay': decay.line}}


def crouch_command(entries, target):
    return unique((part for part in entries if part.one('value', '') == str(target)
                   and 'var(59)!=1' in map(normalized, part.values('triggerall'))),
                  f'human State {target} command')


def source_body(sections, action300, manifest, hashes):
    definition, controllers = move_scope(sections, 300)
    sweep.require_fields(definition, {'type': 's', 'movetype': 'a', 'physics': 'n', 'ctrl': '0', 'velset': '0,0',
                                      'anim': '300', 'facep2': '1', 'sprpriority': '2', 'juggle': '0',
                                      'poweradd': '0'}, 'unsupported State 300 StateDef')
    types = [normalized(part.one('type')) for part in controllers]
    require(types == ['posadd', 'posadd', 'playsnd', 'playsnd', 'movehitreset', 'hitdef', 'hitdef',
                      'hitdef', 'varadd', 'varadd', 'statetypeset', 'changestate'],
            'unsupported State 300 controller sequence')
    starts = element_starts(ELEMENT_DURATIONS)
    steps = source_steps([part for part, kind in zip(controllers, types, strict=True) if kind == 'posadd'], starts)
    reset, hit_one, hit_two, inactive, _, _, recovery, exit_state = controllers[4:]
    require(normalized(reset.one('trigger1')) == 'animelem=6', 'unsupported State 300 MoveHitReset')
    conditions = []
    strikes, windows = [], []
    for index, (hit, window, box) in enumerate(zip((hit_one, hit_two), SEGMENT_WINDOWS, STRIKE_BOXES), 1):
        require(set(map(normalized, hit.values('triggerall'))) == {'var(12)=0', '!movecontact&&!movereversed'},
                f'unsupported State 300 segment {index} HitDef gate')
        terms = list(map(normalized, hit.values('trigger1')))
        condition, = tuple(term for term in terms if 'animelemtime' in term)
        bounds = number_in(condition, r'animelemtime\((\d+)\)>=0&&animelemtime\((\d+)\)<0',
                           f'State 300 segment {index} HitDef window')
        require(tuple(bounds) == window and sorted(terms) == sorted([condition, 'var(28):=1'])
                and not hit.values('trigger2'),
                f'unsupported State 300 segment {index} window')
        conditions.append(condition)
        sweep.require_fields(hit, {'attr': 's,na', 'animtype': 'hard', 'guardflag': 'm', 'hitflag': 'maf',
                                   'ground.cornerpush.veloff': '0', 'id': '30', 'air.animtype': 'back',
                                   'fall.recover': '1', 'fall.recovertime': '600', 'yaccel': '.5',
                                   'air.fall': '1'}, f'unsupported State 300 segment {index} reaction')
        require(normalized(hit.one('ground.type')) == ('high' if index == 1 else 'low'),
                f'unsupported State 300 segment {index} guard height')
        counter, normal = number_in(hit.one('damage'), r'floor\(\(ifelse\(var\(6\),(\d+),(\d+)\)-var\(21\)\)\*fvar\(7\)\),0',
                                    f'State 300 segment {index} dynamic damage')
        require((counter, normal) == (9, 5), f'unsupported State 300 segment {index} damage base')
        push = re.fullmatch(r'-(\d+(?:\.\d+)?)-ifelse\(var\(40\),0\.5,0\)',
                            normalized(hit.one('ground.velocity')))
        require(push is not None and float(push.group(1)) == 10.5,
                f'unsupported State 300 segment {index} push')
        for outcome, field, defender in (('hitPause', 'pausetime', 13), ('guardPause', 'guard.pausetime', 11)):
            values = number_in(hit.one(field), r'(\d+)\+\(helper\(40000\),var\(0\)\),(\d+)',
                               f'State 300 segment {index} {outcome}')
            require(tuple(values) == (11, defender), f'unsupported State 300 segment {index} {outcome}')
        require(normalized(hit.one('ground.hittime')) == '17' and normalized(hit.one('ground.slidetime')) == '17',
                f'unsupported State 300 segment {index} stun times')
        strikes.append({'box': box, 'contactGroup': index})
        windows.append(window)
    require(list(map(normalized, inactive.values('triggerall'))) == ['var(12)>0']
            and inactive.one('attr') == ''
            and list(map(normalized, inactive.values('trigger1'))) == [conditions[0]]
            and list(map(normalized, inactive.values('trigger2'))) == [conditions[1]],
            'unsupported State 300 inactivation HitDef')
    sweep.require_fields(recovery, {'trigger1': 'animelem=7', 'movetype': 'i'}, 'unsupported State 300 idle switch')
    sweep.require_fields(exit_state, {'trigger1': 'animtime=0', 'value': '0', 'ctrl': '1'},
                         'unsupported State 300 natural exit')
    require(tuple(frame['time'] for frame in action300['frames']) == ELEMENT_DURATIONS,
            'Action 300 AIR durations changed')
    guard_crush = [part for part, kind in zip(controllers, types, strict=True) if kind == 'varadd']
    require(all(normalized(part.one('fvar(10)')).startswith('floor((6-var(21))*fvar(12))') for part in guard_crush),
            'State 300 guard crush is part of the source and stays unmodeled')
    startup = starts[SEGMENT_WINDOWS[0][0] - 1]
    # The second window's last element is the exclusive end: active ends when element 7 starts.
    active = starts[SEGMENT_WINDOWS[1][1] - 1] - startup
    return {'state': 300, 'action': 300, 'startup': startup, 'active': active,
            'recovery': sum(ELEMENT_DURATIONS) - startup - active,
            'positionSteps': steps, 'strikes': strikes, 'windows': windows,
            'guardThreatFrames': sum(ELEMENT_DURATIONS[:6]),
            'clsn2': state200.collision_boxes(action300, 2, normalize_edges=True),
            'spriteIndices': state200.validate_manifest(manifest, hashes, action300),
            'sourceLines': {'state': definition.line, 'hitdefs': [hit_one.line, hit_two.line],
                            'reset': reset.line, 'exit': exit_state.line}}


def source_permissions(cmd_contents, terry23_contents):
    _, negative = move_scope(state_reentry.script_sections(terry23_contents), -2)
    window = unique((part for part in negative if normalized(part.one('var(1)', '')) == '1'
                     and any(re.search(r'\bstateno=245\b', normalized(value)) for key, value in part.entries
                             if key.startswith('trigger'))), 'var(1)=1 cancel permission block')
    contact = unique((part for part in negative if normalized(part.one('var(1)', '')) == '2'),
                     'var(1)=2 contact permission block')
    natural_one = unique((part for part in negative if normalized(part.one('var(2)', '')) == '1'),
                         'var(2)=1 natural permission block')
    natural_two = unique((part for part in negative if normalized(part.one('var(2)', '')) == '2'),
                         'var(2)=2 natural permission block')
    sources = sorted({int(match) for key, value in window.entries if key.startswith('trigger')
                      for match in re.findall(r'stateno=(\d+)', normalized(value))} |
                     {int(bound) for key, value in window.entries if key.startswith('trigger')
                      for bound in re.findall(r'stateno=\[(\d+),(\d+)\]', normalized(value)) for bound in bound})
    require(sources == [200, 205, 215, 235, 245, 250, 400, 410, 440], f'var(1)=1 source list changed: {sources}')
    require(any(re.search(r'\bstateno=300\b', normalized(value)) for key, value in contact.entries
                if key.startswith('trigger')), 'var(1)=2 lost the State 300 contact window')
    require(all(re.search(r'\bstateno=300\b', normalized(value)) is None
                for key, value in natural_one.entries if key.startswith('trigger')),
            'var(2)=1 must not cover State 300')
    require(any(re.search(r'\bstateno=300\b', normalized(value)) for key, value in natural_two.entries
                if key.startswith('trigger')), 'var(2)=2 lost State 300')
    require(set(map(normalized, natural_one.values('triggerall'))) == {'!ishelper', 'animtime=0'}
            and set(map(normalized, natural_two.values('triggerall'))) == {'!ishelper', 'animtime=0'}
            and set(map(normalized, contact.values('triggerall'))) == {'!ishelper'},
            'unsupported permission block gates')
    _, cmd_negative = move_scope(state_reentry.script_sections(cmd_contents), -1)
    wave_entry = unique((part for part in cmd_negative if normalized(part.one('value', '')) == '1000'
                         and 'var(59)!=1' in map(normalized, part.values('triggerall'))),
                        'human weak Wave CMD entry')
    require(list(map(normalized, wave_entry.values('trigger3'))) == ['var(1)=2'],
            'weak Wave lost the State 300 contact cancel permission')
    mode2_entry = unique((part for part in cmd_negative if normalized(part.one('value', '')) == '1050'
                          and 'var(59)!=1' in map(normalized, part.values('triggerall'))),
                         'mode 2 weak Wave CMD entry')
    require(list(map(normalized, mode2_entry.values('trigger3'))) == ['var(1)=2'],
            'mode 2 weak Wave lost the State 300 contact cancel permission')
    return {'var1Sources': sources, 'sourceLines': {'var1': window.line, 'var1Contact': contact.line,
                                                    'natural': natural_one.line, 'naturalSpecial': natural_two.line,
                                                    'waveEntry': wave_entry.line, 'mode2WaveEntry': mode2_entry.line}}


def inspect_source(archive, manifest):
    prior = heavy.inspect_source(archive, manifest)
    members = prior['members']
    keys = ('anim', 'cmd', 'st1', 'Terry-2-3.cns', 'Helper.cns')
    with zipfile.ZipFile(archive) as source:
        contents = {key: state200.archive_member(source, members[key]['name']) for key in keys}
    require(all(state200.digest(raw) == members[key]['sha256'] for key, raw in contents.items()),
            'source archive changed')
    actions, diagnostics = parse_air(contents['anim'].decode('latin-1'))
    require(not any(re.search(r'duplicate action 300\b', message) for message in diagnostics),
            'duplicate Action 300')
    action300, durations = wave.source_action(actions, 300)
    hashes = state200.manifest_hashes(prior['zipSha256'], members)
    normals = state_reentry.script_sections(contents['st1'])
    body = source_body(normals, action300, manifest, hashes)
    _, helper = move_scope(state_reentry.script_sections(contents['Helper.cns']), 99999)
    _, cmd_entries = move_scope(state_reentry.script_sections(contents['cmd']), -1)
    command = source_command(contents['cmd'], helper, cmd_entries)
    permissions = source_permissions(contents['cmd'], contents['Terry-2-3.cns'])
    return {**{key: prior[key] for key in ('zipSha256', 'manifestSha256', 'members', 'damageRules',
                                           'groundMovement')},
            'command': command, 'action': body, **permissions}


def install_command(audit, fighter):
    commands = {part['commandId']: part for part in fighter['commands']}
    require(PLACEHOLDER in commands and commands[PLACEHOLDER]['reserveGroundInput']
            and commands[PLACEHOLDER]['reserveOnEdgeDirection']
            and commands[PLACEHOLDER]['priority'] == 4
            and not any(move['commandId'] == PLACEHOLDER for move in fighter['moves']),
            'frozen forward light punch placeholder changed')
    for move in fighter['moves']:
        for rule in move.get('reservedCancels', ()):
            if rule['commandId'] == PLACEHOLDER:
                rule['commandId'] = COMMAND
    # The strict runtime DTO rejects audit-only provenance keys.
    replacement = {key: value for key, value in audit['command'].items() if key != 'sourceLines'}
    fighter['commands'][fighter['commands'].index(commands[PLACEHOLDER])] = replacement


def add_body(audit, native, fighter, binding):
    body = audit['action']
    require(native.get('bodyVerified') and native.get('positionStepsVerified') == [300]
            and body['positionSteps'] == [{'frame': sample['tick'], 'x': fraction(str(sample['x']))}
                                          for sample in native['posAdds'][300]],
            'Action 300 native motion or steps differ from the audited source')
    heavy_move = unique((move for move in fighter['moves'] if move['moveId'] == HEAVY_REACTION_MOVE),
                        'frozen State 410 reaction source')
    heavy_attack, = heavy_move['attacks']
    reaction = binding['hitReactions'][HEAVY_REACTION_MOVE]
    measured = native['moves'][300]['contacts']
    require(len(measured) == 2 and all(contact['pause'] == {'attackerFrames': 11, 'defenderFrames': 13}
                                       and contact['initialPush'] == 10.5 and contact['damage'] > 0
                                       for contact in measured),
            'State 300 native contact metrics differ from the audited HitDef')
    retention = fighter['movement']['groundReaction']['velocityRetention']
    coefficient = Fraction(retention['numerator'], retention['denominator'])
    friction = fraction(Fraction(21, 2) * (1 - coefficient))
    attacks = []
    for index, (strike, window) in enumerate(zip(body['strikes'], body['windows']), 1):
        first = sum(ELEMENT_DURATIONS[:window[0] - 1]) - body['startup']
        last = sum(ELEMENT_DURATIONS[:window[1] - 1]) - 1 - body['startup']
        left, top, right, bottom = strike['box']
        attacks.append({
            'offsetForward': fraction((left + right) / 2), 'offsetUp': fraction(-(top + bottom) / 2),
            'halfWidth': fraction((right - left) / 2), 'halfHeight': fraction((bottom - top) / 2),
            'firstActiveFrame': first, 'lastActiveFrame': last, 'contactGroup': strike['contactGroup'],
            'height': 'High' if index == 1 else 'Low', 'damage': 0,
            'dynamicDamage': {'normalBase': 5, 'counterBase': 9, 'useContactDivisor': False},
            'hitstopFrames': 13, 'contactOnEdge': True,
            'hitstopByOutcome': {'hit': {'attackerFrames': 11, 'defenderFrames': 13},
                                 'block': {'attackerFrames': 11, 'defenderFrames': 11}},
            'hitstunFrames': heavy_attack['hitstunFrames'], 'blockstunFrames': heavy_attack['blockstunFrames'],
            'knockdownFrames': 0, 'wakeupFrames': 0,
            'groundKnockback': fraction('10.5'), 'groundKnockbackFriction': friction,
            'blockKnockback': fraction('10.5'),
            'launchVelocityX': fraction('0'), 'launchVelocityY': fraction('0'),
            'rejectsLyingTargets': True, 'groundHitReaction': 'Heavy',
        })
    move = {'moveId': MOVE_ID, 'commandId': COMMAND, 'allowedFrom': 'GroundIdle',
            'startupFrames': body['startup'], 'activeFrames': body['active'],
            'recoveryFrames': body['recovery'], 'attacks': attacks,
            'hurtboxTimeline': state210.hurtbox_timeline(
                {'durations': list(ELEMENT_DURATIONS), 'clsn2': body['clsn2']}),
            'guardThreatFrames': body['guardThreatFrames'],
            'cancels': [], 'throws': [], 'projectiles': [], 'positionSteps': body['positionSteps']}
    fighter['moves'].append(move)
    clip = 'state300'
    binding['clips'][clip] = {'action': 300, 'clock': 'move', 'loop': False}
    binding['moves'][MOVE_ID] = dict.fromkeys(('Startup', 'Active', 'Recovery'), clip)
    binding['hitReactions'][MOVE_ID] = reaction
    return move


def apply_exits(native, fighter, move, mode):
    require(native.get('waveExitVerified'), 'State 300 native exit permissions missing')
    if mode == 'default':
        target = unique((candidate for candidate in fighter['moves'] if candidate['moveId'] == wave.MOVE_ID),
                        'frozen State 1000')
        require(target['commandId'] == wave.COMMAND_ID, 'frozen State 1000 command changed')
        move['cancels'].append({'targetMoveId': wave.MOVE_ID, 'windowFirstFrame': WAVE_WINDOW[0],
                                'windowLastFrame': WAVE_WINDOW[1], 'phases': 'Active', 'allowWhiff': False,
                                'requiresHitConfirm': True, 'requiresBlockConfirm': True})
    else:
        require(not any(candidate['moveId'] == wave.MOVE_ID for candidate in fighter['moves']),
                'mode 2 cannot select State 1000')


def install_cancel_windows(fighter):
    """Wire the var(1)=1 windows of every implemented source into the real 300 command."""
    for move in fighter['moves']:
        window = CANCEL_WINDOWS.get(move['moveId'])
        if window is None:
            continue
        reserved = move.setdefault('reservedCancels', [])
        if not any(rule['commandId'] == COMMAND for rule in reserved):
            reserved.append({'commandId': COMMAND, 'phases': 'Active',
                             'windowFirstFrame': window[0], 'windowLastFrame': window[1]})


def build_profile(audit, native, base_fighter, base_binding, mode):
    require(native.get('completeVerified'), 'State 300 requires the complete native reference')
    fighter, binding = copy.deepcopy((base_fighter, base_binding))
    fighter['fighterId'], binding['visualId'] = PRESETS[mode]
    fighter['displayName'] = f'Terry Forward Light Punch ({mode})'
    install_command(audit, fighter)
    install_cancel_windows(fighter)
    move = add_body(audit, native, fighter, binding)
    apply_exits(native, fighter, move, mode)
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
        references, native = routes.verify_native_references(args.reference, audit, reference_stage=28,
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
        parser.exit(2, f'Stage 28 conversion: {error}\n')


if __name__ == '__main__':
    main()
