"""Generate the reviewed default Terry State 1000 and Helper 1020 profile."""

import argparse
import copy
from fractions import Fraction
import io
import json
from pathlib import Path
import re
import runpy
import zipfile
from contextlib import redirect_stdout

from . import state200, state210, state_reentry
from .air import parse_air
from .state200 import fraction, normalized, number_in, require, unique
from .state205215 import MOVE_IDS, move_scope


FIGHTER_ID = 'TerryPowerWave'
VISUAL_ID = 'terry98-stage19-default'
MOVE_ID = 'TerryPowerWave1000'
ENTITY_ID = 'TerryPowerWave1020'
COMMAND_ID = '236A'
LAUNCH_ACTIONS = {'Shake': 5030, 'Lift': 5030, 'Rise': 5035, 'Fall': 5050, 'Impact': 5100,
                  'Bounce': 5160, 'LyingImpact': 5170, 'Lying': 5110, 'Wakeup': 5120}
FROZEN_BASE = {
    'default.fighter.json': 'cff51e70c676a6f19a1f252a4099038fbaa9bdb30be1ca499169ea1694275235',
    'default.binding.json': '684888faa0aa862b4b613ac3800b139652aa861f11a5f3d4b57e9b052844050f',
}


def parse_helper_damage(expression):
    pattern = (r'floor\(\(\(ifelse\(root,var\(6\),(\d+),(\d+)\)-root,var\(21\)\)'
               r'\*root,fvar\(7\)\)\*root,fvar\(0\)\),floor\((\d+)\*root,fvar\(7\)\)')
    counter, normal, chip = number_in(expression, pattern, 'Power Wave damage expression')
    return normal, counter, chip


def projectile_hit_spans(durations, boxes, *, early_element, late_element, exit_element):
    spans = []
    age = 0
    for element, (duration, frame_boxes) in enumerate(zip(durations, boxes, strict=True), 1):
        require(type(duration) is int and duration > 0, 'Power Wave frame durations must be positive')
        if early_element <= element < exit_element:
            (box,) = frame_boxes
            x0, y0, x1, y1 = box
            local = (x0, x1, -y1, -y0)
            reaction = 'early' if element < late_element else 'late'
            if spans and (spans[-1]['localBox'], spans[-1]['reaction']) == (local, reaction):
                spans[-1]['lastFrame'] += duration
            else:
                spans.append({'firstFrame': age, 'lastFrame': age + duration - 1, 'localBox': local, 'reaction': reaction})
        else:
            require(not frame_boxes, f'Power Wave unexpectedly has an attack box on AIR element {element}')
        age += duration
    return spans


def inspect_command(command_bytes, helper_bytes, globals_bytes, light_durations):
    sections = state_reentry.script_sections(command_bytes)
    qcf = unique((section for section in sections if section.title == 'command'
                  and normalized(section.one('command', '')) == '~d,df,f,x'), 'weak Power Wave command')
    label = qcf.one('name')
    _, commands = move_scope(sections, -1)
    entry = unique((section for section in commands if section.one('value', '') == '1000'
                    and section.one('type', '').lower() == 'changestate'
                    and any(label in value for value in section.values('triggerall'))), 'State 1000 human CMD entry')
    require(set(map(normalized, entry.values('triggerall'))) == {
        'var(59)!=1', '!ishelper', 'roundstate=2', 'statetype!=a', 'var(10)=0',
        f'command={normalized(label)}||helper(99999),var(10)>0',
    } and normalized(entry.one('trigger1')) == 'ctrl||stateno=40||(stateno=[100,101])||var(2)>=1'
        and normalized(entry.one('trigger2')) == 'var(1)=1' and normalized(entry.one('trigger3')) == 'var(1)=2',
        'unsupported State 1000 command eligibility')
    require(entry.line < state200.command_rules(sections)['nearEntryLine'], 'Power Wave priority no longer precedes light punch')

    _, helpers = move_scope(state_reentry.script_sections(helper_bytes), 99999)
    latch = unique((section for section in helpers if section.one('type', '').lower() == 'varset'
                    and section.values('var(10)') and section.one('trigger1', '') == f'command = {label}'),
                   'Power Wave Helper latch')
    decay = unique((section for section in helpers if section.one('type', '').lower() == 'varadd'
                    and section.values('var(10)') and normalized(section.one('trigger1', '')) == 'var(10)>0'),
                   'Power Wave Helper decay')
    held, released = number_in(decay.one('var(10)'), r'ifelse\(command="holdx",-(\d+),-(\d+)\)', 'Power Wave latch decay')
    require(latch.line < decay.line and not latch.values('ignorehitpause') and not decay.values('ignorehitpause'),
            'unsupported Power Wave Helper timing')

    _, globals_sections = move_scope(state_reentry.script_sections(globals_bytes), -2)
    active = unique((section for section in globals_sections if section.values('var(1)')
                     and normalized(section.one('trigger1', '')).startswith('stateno=200&&animelemtime')), 'light cancel permission')
    element, exit_element = number_in(active.one('trigger1'), r'stateno=200&&animelemtime\((\d+)\)>0&&animelemtime\((\d+)\)<0',
                                      'Power Wave light cancel window')
    require(normalized(active.one('trigger2')) == f'stateno=205&&animelemtime({element})>0&&animelemtime({exit_element})<0'
            and active.one('var(1)') == '1', 'unsupported light cancel state sources')
    window = [sum(light_durations[:element - 1]) + 1, sum(light_durations[:exit_element - 1]) - 1]
    return {'maxSequenceFrames': int(qcf.one('time')), 'buttonToleranceFrames': 0,
            'helperTicks': int(latch.one('var(10)')), 'helperHeldDecay': held, 'helperReleasedDecay': released,
            'sourceLines': {'command': qcf.line, 'entry': entry.line, 'latch': latch.line, 'decay': decay.line,
                            'lightCancel': active.line}}, window


def source_action(actions, number, *, hold_last=False):
    action = unique((entry for entry in actions if entry['action'] == number), f'AIR Action {number}')
    durations = [frame['time'] for frame in action['frames']]
    timed = durations[:-1] if hold_last and durations[-1:] == [-1] else durations
    require(not action['loop_start'] and all(type(duration) is int and duration > 0 for duration in timed),
            f'Action {number} must have positive frames or a supported terminal hold')
    return action, durations


def launch_reaction(common, selected, attack):
    sections = state_reentry.script_sections(common)
    _, impact = move_scope(sections, 5100)
    _, bounce = move_scope(sections, 5101)
    _, lie = move_scope(sections, 5110)
    retain = unique((item for item in bounce if normalized(item.one('type', '')) == 'velmul'), 'bounce horizontal retention')
    gravity = unique((item for item in bounce if normalized(item.one('type', '')) == 'veladd'), 'bounce gravity')
    leave_impact = unique((item for item in impact if item.one('value', '') == '5101'), 'impact-to-bounce transition')
    require(normalized(leave_impact.one('trigger1')) == 'animtime=0', 'unsupported impact duration')
    leave_lie = unique((item for item in lie if item.one('value', '') == '5120'), 'lie-down recovery threshold')
    (wait,) = number_in(leave_lie.one('trigger1'), r'sysvar\(2\)>=(\d+)&&time>0', 'lie-down duration')
    add = unique((item for item in lie if normalized(item.one('sysvar(2)', '')) == '1'), 'lie-down timer increment')
    require(normalized(add.one('trigger1')) == 'time>=0', 'unsupported lie-down timer')
    require(attack.one('fall') == '1' and (attack.one('fall.recover'), attack.one('fall.recovertime')) == ('1', '600'),
            'unsupported Power Wave air recovery')
    return {'impactFrames': sum(selected[5100][1]),
            'bounceHorizontalRetention': fraction(retain.one('x')),
            'bounceVerticalVelocity': fraction(-Fraction(attack.one('fall.yvelocity'))),
            'bounceGravity': fraction(gravity.one('y')),
            'lieDownFrames': wait + 1,
            'wakeupFrames': sum(selected[5120][1]),
            'transitionGravitySteps': 2,
            'riseFrames': sum(selected[5035][1]), 'lieImpactFrames': sum(selected[5170][1])}


def guard_threat(controls, durations, action):
    transition = unique((item for item in controls if normalized(item.one('type', '')) == 'statetypeset'
                         and normalized(item.one('movetype', '')) == 'i'), f'Action {action} guard threat end')
    pattern = rf'(?:anim={action}&&)?animelem=(\d+)'
    (element,) = number_in(transition.one('trigger1'), pattern, f'Action {action} guard threat end')
    return sum(durations[:element - 1])


def inspect_source(source_zip, manifest):
    zip_sha, members, contents = state200.read_terry_archive(source_zip)
    with zipfile.ZipFile(source_zip) as archive:
        definition = state200.archive_member(archive, members['def']['name'])
        files = unique((section for section in state_reentry.script_sections(definition) if section.title == 'files'), 'DEF files')
        for name in ('Terry-S.cns', files.one('st4'), files.one('st5')):
            contents[name] = state200.archive_member(archive, name)
            members[name] = {'name': name, 'sha256': state200.digest(contents[name])}
    actions, diagnostics = parse_air(contents['anim'].decode('latin-1'))
    selected = {number: source_action(actions, number, hold_last=number in (5050, 5160))
                for number in (200, 205, 210, 215, 1000, 1020, 5000, 5030, 5035, 5050, 5100, 5101, 5110, 5120, 5160, 5170, 6002, 6012)}
    require(not any(f'duplicate action {number} ' in item for item in diagnostics for number in selected), 'duplicate reviewed Action')
    sprites = {number: state200.validate_manifest(manifest, state200.manifest_hashes(zip_sha, members), selected[number][0])
               for number in selected if number not in (200, 205, 210, 215, 5101)}
    size = unique((section for section in state_reentry.script_sections(contents['cns']) if section.title == 'size'), 'CNS Size')
    normals = state_reentry.script_sections(contents['st1'])
    threats = {}
    for number in (200, 205, 210, 215):
        state, controls = move_scope(normals, number)
        require(normalized(state.one('movetype')) == 'a', f'State {number} must start as an attack')
        threats[number] = guard_threat(controls, selected[number][1], number)
    cmd, window = inspect_command(contents['cmd'], contents[files.one('st5')], contents[files.one('st4')], selected[200][1])
    sections = state_reentry.script_sections(contents['Terry-S.cns'])
    move, move_controls = move_scope(sections, 1000)
    require(all(normalized(move.one(key)) == value for key, value in {'type': 's', 'movetype': 'a', 'physics': 'n',
                                                                     'velset': '0,0', 'anim': '1000', 'ctrl': '0', 'facep2': '1'}.items()),
            'unsupported State 1000 root contract')
    spawn = unique((item for item in move_controls if item.one('type').lower() == 'helper'), 'State 1000 Helper spawn')
    spawn_element, spawn_offset = number_in(spawn.one('trigger1'), r'animelem=(\d+),=(\d+)', 'Power Wave Helper spawn')
    require(all(normalized(spawn.one(key)) == value for key, value in {'stateno': '1020', 'id': '1000', 'pos': '0,0',
                                                                       'postype': 'p1', 'keyctrl': '0', 'persistent': '0'}.items()),
            'unsupported State 1000 Helper spawn behavior')
    recovery = unique((item for item in move_controls if item.one('type').lower() == 'statetypeset'), 'State 1000 recovery')
    (recovery_element,) = number_in(recovery.one('trigger1'), r'animelem=(\d+)', 'Power Wave recovery element')
    exit_state = unique((item for item in move_controls if item.one('type').lower() == 'changestate'), 'State 1000 exit')
    require(normalized(recovery.one('movetype')) == 'i' and normalized(exit_state.one('trigger1')) == 'animtime=0'
            and (exit_state.one('value'), exit_state.one('ctrl')) == ('0', '1'), 'unsupported State 1000 recovery')
    cast, cast_durations = selected[1000]
    spawn_tick = sum(cast_durations[:spawn_element - 1]) + spawn_offset
    recovery_tick = sum(cast_durations[:recovery_element - 1])
    require(not any(state200.collision_boxes(cast, 1)), 'State 1000 cast unexpectedly has a direct attack')
    root_boxes = state210.hurtbox_timeline({'durations': cast_durations, 'clsn2': state200.collision_boxes(cast, 2, normalize_edges=True)})

    helper, helper_controls = move_scope(sections, 1020)
    require(helper.one('anim') == '1020', 'Helper State 1020 does not use Action 1020')
    start_speed = unique((item for item in helper_controls if item.one('type').lower() == 'velset'
                          and normalized(item.one('trigger1', '')) == 'var(1)=1'), 'weak Helper speed')
    end_speed = unique((item for item in helper_controls if item.one('type').lower() == 'velset'
                        and item.one('x', '') == '0' and normalized(item.one('trigger1', '')).startswith('animelem=')), 'Helper stop')
    (stop_element,) = number_in(end_speed.one('trigger1'), r'animelem=(\d+)', 'Power Wave stop element')
    destroy = unique((item for item in helper_controls if item.one('type').lower() == 'destroyself'), 'Helper destruction')
    no_hit = unique((item for item in helper_controls if item.one('type').lower() == 'nothitby'), 'Helper hit immunity')
    require(normalized(destroy.one('trigger1')) == 'animtime=0' and normalized(no_hit.one('value')) == 'sca',
            'unsupported Helper destruction or hit immunity')
    strong = [item for item in helper_controls if item.one('type').lower() == 'hitdef'
              and any(normalized(trigger) == 'var(1)=1' for trigger in item.values('trigger1')) and item.one('attr', '')]
    require(len(strong) == 2, 'expected two weak Helper HitDef spans')
    early, late = strong
    def bounds(item):
        return number_in(unique((trigger for trigger in item.values('trigger1') if 'animelemtime' in trigger.lower()),
                                'Helper hit element window'), r'animelemtime\((\d+)\)>=0&&animelemtime\((\d+)\)<0', 'Helper hit window')
    first, split = bounds(early)
    late_start, finish = bounds(late)
    normal, counter, chip = parse_helper_damage(early.one('damage'))
    require(late_start == split and parse_helper_damage(late.one('damage')) == (normal, counter, chip)
            and all(normalized(item.one('guardflag')) == 'ma' and '!movecontact&&!movereversed' in
                    tuple(map(normalized, item.values('triggerall'))) for item in strong), 'unsupported Helper damage or shared contact gate')
    require(early.one('fall') == '1' and late.one('fall', '0') == '0', 'unsupported Helper launch rules')
    hit_pause = list(number_in(early.one('pausetime'), r'(\d+),(\d+)', 'Helper hit pause'))
    guard_pause = list(number_in(early.one('guard.pausetime'), r'(\d+),(\d+)', 'Helper guard pause'))
    require(all(normalized(early.one(key)) == normalized(late.one(key)) for key in ('pausetime', 'guard.pausetime', 'ground.hittime')),
            'Helper branches disagree on pause or reaction timing')
    velocity_term = r'-([\d.]+)-ifelse\(root,var\(40\),0\.5,0\)'
    def velocity(key, item, dimensions=1):
        value = item.one(key)
        match = re.fullmatch(','.join([velocity_term] * dimensions), normalized(value))
        require(match is not None, f'unsupported Helper {key}: {value}')
        return [float(part) for part in match.groups()]
    entity, entity_durations = selected[1020]
    threats[1000] = recovery_tick
    threats[1020] = guard_threat(helper_controls, entity_durations, 1020)
    spans = projectile_hit_spans(entity_durations, state200.collision_boxes(entity, 1, normalize_edges=True),
                                 early_element=first, late_element=split, exit_element=finish)
    damage_rules = state200.load_json(manifest)  # Manifest hash also covers the source atlas identity.
    require(damage_rules['localcoord'] == 320 and chip == 1, 'unsupported Helper chip scale or coordinate system')
    return {'zipSha256': zip_sha, 'members': members, 'manifestSha256': state200.archive_digest(manifest),
            'command': cmd, 'cancelWindow': window, 'visuals': sprites,
            'forwardGuardDistance': fraction(size.one('attack.dist')), 'guardThreatFrames': threats,
            'move': {'durations': [spawn_tick, recovery_tick - spawn_tick, sum(cast_durations) - recovery_tick],
                     'spawnTick': spawn_tick, 'hurtboxes': root_boxes, 'sourceLine': move.line, 'spawnLine': spawn.line},
            'entity': {'speed': fraction(start_speed.one('x')), 'lifetime': sum(entity_durations),
                       'movementFrames': sum(entity_durations[:stop_element - 1]), 'guardDamage': 9, 'hitSpans': spans,
                       'normalBase': normal, 'counterBase': counter, 'hitPause': hit_pause, 'guardPause': guard_pause,
                       'hitStun': int(early.one('ground.hittime')) + 1, 'guardStun': int(early.one('ground.hittime')),
                       'earlyLaunch': velocity('ground.velocity', early, 2), 'launchGravity': fraction(early.one('yaccel')),
                       'launchReaction': launch_reaction(contents['stcommon'], selected, early),
                       'latePush': velocity('ground.velocity', late)[0],
                       'guardPush': velocity('guard.velocity', early)[0], 'sourceLine': helper.line,
                       'hitDefLines': [early.line, late.line],
                       'knockdownFrames': sum(selected[5100][1] + selected[5101][1]) + 12,
                       'wakeupFrames': sum(selected[5120][1])}}


def attack_payload(entity, local, early, *, legacy=False):
    x0, x1, y0, y1 = local
    zero = fraction(0)
    launch = entity['earlyLaunch'] if early else (0, 0)
    payload = {
        'offsetForward': zero, 'offsetUp': zero,
        'halfWidth': fraction(Fraction(x1 - x0, 2)), 'halfHeight': fraction(Fraction(y1 - y0, 2)),
        'firstActiveFrame': 0, 'lastActiveFrame': 0, 'height': 'Mid', 'damage': 0,
        'hitstunFrames': entity['hitStun'], 'blockstunFrames': entity['guardStun'],
        'groundKnockback': zero if early else fraction(str(entity['latePush'])),
        'blockKnockback': fraction(str(entity['guardPush'])),
        'groundKnockbackFriction': fraction(Fraction(str(entity['guardPush'])) * Fraction(3, 20)),
        'launchVelocityX': fraction(str(launch[0])), 'launchVelocityY': fraction(str(launch[1])),
        'knockdownFrames': entity['knockdownFrames'] if early else 0,
        'wakeupFrames': entity['wakeupFrames'] if early else 0, 'hitstopFrames': entity['hitPause'][1],
    }
    if not legacy:
        payload['hitstopByOutcome'] = {
            kind: dict(zip(('attackerFrames', 'defenderFrames'), entity[field], strict=True))
            for kind, field in (('hit', 'hitPause'), ('block', 'guardPause'))}
    return payload


def build_profile(audit, base_fighter, base_binding):
    fighter, binding = copy.deepcopy((base_fighter, base_binding))
    fighter['fighterId'] = FIGHTER_ID
    fighter['displayName'] = 'Terry Weak Power Wave (default)'
    fighter['useCommandPriority'] = True
    fighter['allowStrikeClash'] = False
    fighter['forwardGuardDistance'] = audit['forwardGuardDistance']
    threats = audit['guardThreatFrames']
    normal_actions = {move_id: action for action, move_id in MOVE_IDS.items()}
    normal_actions.update({state200.MOVE_ID: 200, state210.MOVE_ID: 210})
    for existing in fighter['moves']:
        action = normal_actions[existing['moveId']]
        existing['guardThreatFrames'] = threats[action]
    binding['visualId'] = VISUAL_ID
    command = audit['command']
    fighter['commands'].append({'commandId': COMMAND_ID, 'directions': ['Down', 'DownForward', 'Forward'],
                                'buttons': ['LightPunch'], 'maxSequenceFrames': command['maxSequenceFrames'], 'matchHeldMotion': True,
                                'buttonToleranceFrames': command['buttonToleranceFrames'], 'bufferLifetimeFrames': 0,
                                'priority': max(item['priority'] for item in fighter['commands']) + 1,
                                'chargeHoldFrames': 0, 'chargeReleaseToleranceFrames': 0,
                                'bufferPolicy': {'preserveDuringHitPause': True, **{name: command[name] for name in
                                                ('helperTicks', 'helperHeldDecay', 'helperReleasedDecay')}}})
    source = unique((move for move in fighter['moves'] if move['moveId'] == state200.MOVE_ID), 'State 200')
    first, last = audit['cancelWindow']
    source['cancels'].insert(0, {'targetMoveId': MOVE_ID, 'windowFirstFrame': first, 'windowLastFrame': last,
                                 'phases': 'Active', 'requiresHitConfirm': False, 'requiresBlockConfirm': False,
                                 'allowWhiff': True})
    move, entity = audit['move'], audit['entity']
    startup, active, recovery = move['durations']
    spans = []
    for span in entity['hitSpans']:
        local = span['localBox']
        spans.append({**{name: span[name] for name in ('firstFrame', 'lastFrame')},
                      'localHitbox': {name: fraction(value) for name, value in zip(('minX', 'maxX', 'minY', 'maxY'), local, strict=True)},
                      'hit': attack_payload(entity, local, span['reaction'] == 'early'), 'guardDamage': entity['guardDamage'],
                      'dynamicDamage': {'normalBase': entity['normalBase'], 'counterBase': entity['counterBase'], 'useContactDivisor': False},
                      'contactOnEdge': True,
                      **({'launchGravity': entity['launchGravity'], 'launchReaction': entity['launchReaction']}
                         if span['reaction'] == 'early' else {})})
    projectile = {'projectileTypeId': ENTITY_ID, 'hit': attack_payload(entity, entity['hitSpans'][0]['localBox'], False, legacy=True),
                  'speedX': entity['speed'], 'speedY': fraction(0), 'lifetimeFrames': entity['lifetime'], 'maxAlive': 8,
                  'spawnAtActiveFrame': move['spawnTick'] - startup,
                  'timeline': {'movementFrames': entity['movementFrames'], 'requestPauseOnHit': True,
                               'guardThreatFrames': threats[1020], 'hitSpans': spans}}
    fighter['moves'].append({'moveId': MOVE_ID, 'commandId': COMMAND_ID, 'startupFrames': startup, 'activeFrames': active,
                             'recoveryFrames': recovery, 'allowedFrom': 'GroundIdle', 'attacks': [], 'cancels': [], 'throws': [],
                             'projectiles': [projectile], 'hurtboxTimeline': move['hurtboxes'], 'guardThreatFrames': threats[1000]})
    binding['clips']['state1000'] = {'action': 1000, 'clock': 'move', 'loop': False}
    binding['clips'][f'projectile_{ENTITY_ID}'] = {'action': 1020, 'clock': 'phase', 'loop': False}
    for phase, action in LAUNCH_ACTIONS.items():
        binding['clips'][f'launch_{phase}'] = {'action': action, 'clock': 'phase', 'loop': False}
    binding['moves'][MOVE_ID] = {phase: 'state1000' for phase in ('Startup', 'Active', 'Recovery')}
    return fighter, binding


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source_zip', type=Path)
    parser.add_argument('manifest', type=Path)
    parser.add_argument('--base-profile-dir', type=Path, required=True)
    parser.add_argument('--reference', type=Path, action='append', required=True, help='Native results directory')
    parser.add_argument('--output-dir', type=Path, required=True)
    args = parser.parse_args()
    try:
        audit = inspect_source(args.source_zip, args.manifest)
        checker = runpy.run_path(str(Path(__file__).resolve().parents[2] / 'Stage19Reference/Check-Traces.py'))
        cases = {}
        references = []
        with redirect_stdout(io.StringIO()):
            for directory in args.reference:
                metadata_path = directory / 'run-metadata.json'
                metadata = state200.load_json(metadata_path)
                require(metadata['terry_archive_sha256'] == audit['zipSha256'], 'native reference archive mismatch')
                source_hashes = {member['name']: member['sha256'] for member in audit['members'].values()}
                require(all(source_hashes.get(name) == sha for name, sha in metadata['source_sha256'].items()), 'native reference source mismatch')
                for case, data in checker['read_cases'](directory):
                    require(case not in cases, f'duplicate native case {case}')
                    cases[case] = data
                references.append({'metadataSha256': state200.archive_digest(metadata_path), 'cases': metadata['executed_cases']})
            checker['verify_primary'](cases)
            checker['verify_supplement'](cases)
        base = []
        for name, expected in FROZEN_BASE.items():
            path = args.base_profile_dir / name
            require(state200.archive_digest(path) == expected, f'{name}: frozen Stage 18 SHA-256 mismatch')
            base.append(state200.load_json(path))
        fighter, binding = build_profile(audit, *base)
        state200.output_documents(args.output_dir, {'default.fighter.json': fighter, 'default.binding.json': binding,
                                                    'default.source.json': {**audit, 'frozenBaseSha256': FROZEN_BASE, 'references': references}})
    except (AssertionError, ValueError, OSError, TypeError, KeyError, IndexError, zipfile.BadZipFile) as error:
        parser.exit(2, f'Stage 19 Power Wave conversion: {error}\n')


if __name__ == '__main__':
    main()
