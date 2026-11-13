"""Audit and convert the reviewed Terry-KOF98 State 200 ground sample offline."""

import argparse
import hashlib
import json
import math
import re
import zipfile
from dataclasses import dataclass, field
from fractions import Fraction
from pathlib import Path

from .__main__ import localcoord
from .action_audit import audit_assets
from .air import parse_air


SOURCE_SHA256 = '299203338248952735fd33b4951aba84b39580123290abd8ac70387580ff4698'
REFERENCE_COMMIT = '5dac5caf9a02a9b81bf2264e88a1bbf68e21bba0'
REFERENCE_DISTANCES = {'whiff': 240, 'hit': 60, 'guard': 60, 'mirror': 60, 'crouch-guard': 60,
                       'boundary-78': 78, 'boundary-79': 79, 'boundary-80': 80, 'crouch-guard-late': 60}
MOVE_ID = 'TerryStandLightPunch200'
FRAME_KEYS = ('group', 'image', 'x', 'y', 'time', 'flip')
CONTROLLERS = ['playsnd', 'playsnd', 'hitdef', 'hitdef', 'varadd', 'varadd', 'statetypeset', 'changestate']


@dataclass
class Section:
    title: str
    line: int
    entries: list = field(default_factory=list)

    def values(self, key):
        return [value for name, value in self.entries if name == key.lower()]

    def one(self, key, default=None):
        entries = self.values(key)
        if not entries and default is not None:
            return default
        require(len(entries) == 1, f'line {self.line}: expected one {key} in [{self.title}]')
        return entries[0]


def require(condition, message):
    if not condition:
        raise ValueError(message)


def read_sections(source):
    sections = []
    for line_no, raw in enumerate(source.splitlines(), 1):
        line = raw.partition(';')[0].strip()
        if line.startswith('['):
            sections.append(Section(line[1:-1].strip().lower(), line_no))
        elif sections and '=' in line:
            key, _, value = line.partition('=')
            sections[-1].entries.append((key.strip().lower(), value.strip()))
    return sections


def unique(matches, label):
    matches = list(matches)
    require(len(matches) == 1, f'expected one {label}, found {len(matches)}')
    return matches[0]


def state(sections, number):
    index = unique((i for i, s in enumerate(sections) if s.title == f'statedef {number}'), f'statedef {number}')
    controllers = []
    for section in sections[index+1:]:
        if section.title.startswith('statedef '):
            break
        if section.title.startswith(f'state {number},'):
            controllers.append(section)
    return sections[index], controllers


def timeline(action, first_element, next_element):
    times = [frame['time'] for frame in action['frames']]
    require(action.get('loop_start') is None and all(type(value) is int and value > 0 for value in times),
            f"Action {action.get('action', 200)} must have positive finite durations and no explicit loop")
    require(1 < first_element < next_element <= len(times), 'invalid HitDef element bounds')
    return (sum(times[:first_element-1]), sum(times[first_element-1:next_element-1]), sum(times[next_element-1:]))


def collision_boxes(action, number, *, normalize_edges=False):
    declaration = re.compile(rf'Clsn{number}(Default)?\s*:\s*(\d+)', re.I)
    box = re.compile(rf'Clsn{number}\[\s*(\d+)\s*\]\s*=\s*(.*)', re.I)
    groups, current = {}, None
    for entry in action['collisions']:
        line = entry['source']
        if not line.lower().startswith(f'clsn{number}'):
            continue
        element = entry['before_frame']
        require(0 <= element < len(action['frames']), 'collision block has no associated AIR frame')
        matched = declaration.fullmatch(line)
        if matched:
            default, count = matched.groups()
            current = (element, bool(default))
            require(current not in groups, f'duplicate Clsn{number} declaration on element {element+1}')
            groups[current] = (int(count), [])
            continue
        matched = box.fullmatch(line)
        require(matched is not None and current is not None and current[0] == element,
                f'invalid Clsn{number} declaration on element {element+1}')
        index, edges = matched.groups()
        target = groups[current][1]
        require(int(index) == len(target), f'Clsn{number} indices must be contiguous')
        values = [int(edge.strip()) for edge in edges.split(',')]
        if normalize_edges and len(values) == 4:
            left, right = sorted(values[::2])
            top, bottom = sorted(values[1::2])
            values = [left, top, right, bottom]
        require(len(values) == 4 and values[0] < values[2] and values[1] < values[3],
                f'Clsn{number} must have positive area on element {element+1}')
        target.append(values)
    result, default_boxes = [], []
    for (element, _), (count, boxes) in groups.items():
        require(count == len(boxes), f'Clsn{number} element {element+1}: declared {count}, found {len(boxes)}')
    for element in range(len(action['frames'])):
        if (element, True) in groups:
            default_boxes = groups[element, True][1]
        result.append(groups[element, False][1] if (element, False) in groups else default_boxes)
    return result


def digest(data):
    return hashlib.sha256(data).hexdigest()


def archive_digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def archive_member(archive, name):
    info = unique((item for item in archive.infolist()
                   if not item.is_dir() and Path(item.filename).name.lower() == name.lower()), f'ZIP member {name}')
    require(info.file_size <= 8 * 1024 * 1024, f'oversized script member {name}')
    return archive.read(info)


def normalized(text):
    return re.sub(r'\s+', '', text).lower()


def number_in(text, pattern, label):
    found = re.fullmatch(pattern, normalized(text))
    require(found is not None, f'unsupported {label}: {text}')
    return tuple(map(int, found.groups()))


def source_move(sections):
    definition, controllers = state(sections, 200)
    required = {'type': 's', 'movetype': 'a', 'physics': 'n', 'velset': '0,0', 'ctrl': '0', 'anim': '200', 'facep2': '1'}
    require(all(normalized(definition.one(key)) == value for key, value in required.items()),
            'State 200 does not match the reviewed standing, stationary, no-control entry')
    types = [controller.one('type').lower() for controller in controllers]
    require(types == CONTROLLERS, f'unsupported State 200 controller sequence at line {definition.line}: {types}')
    hit, inactive_hit, recovery, exit_state = (controllers[i] for i in (2, 3, 6, 7))
    hit_window = unique((value for value in hit.values('trigger1') if 'animelemtime' in value.lower()), 'HitDef element window')
    first, next_element = number_in(hit_window, r'animelemtime\((\d+)\)>=0&&animelemtime\((\d+)\)<0', 'HitDef element window')
    require({normalized(value) for value in hit.values('triggerall')} == {'var(12)=0', '!movecontact&&!movereversed'}
            and {normalized(value) for value in inactive_hit.values('triggerall')} == {'var(12)>0'}
            and inactive_hit.one('attr') == '' and normalized(inactive_hit.one('trigger1')) == normalized(hit_window),
            'unsupported State 200 HitDef branch conditions')
    require(normalized(hit.one('attr')) == 's,na' and normalized(hit.one('guardflag')) == 'm'
            and normalized(hit.one('hitflag')) == 'maf', 'unsupported State 200 hit eligibility')
    require(normalized(recovery.one('trigger1')) == f'animelem={next_element}'
            and normalized(recovery.one('movetype')) == 'i', 'unsupported State 200 recovery transition')
    require(normalized(exit_state.one('trigger1')) == 'animtime=0'
            and exit_state.one('value') == '0' and exit_state.one('ctrl') == '1', 'unsupported State 200 exit')
    return first, next_element, {
        'definitionLine': definition.line, 'hitDefLine': hit.line, 'recoveryLine': recovery.line, 'exitLine': exit_state.line,
        'hitDef': {key: hit.one(key) for key in ('damage', 'pausetime', 'guard.pausetime', 'ground.velocity',
                                                   'ground.slidetime', 'ground.hittime', 'guardflag', 'hitflag')},
    }


def command_rules(sections):
    key = unique((section for section in sections if section.title == 'command'
                  and section.one('name', '').lower() == '"x"'), 'single-button command x')
    defaults = unique((section for section in sections if section.title == 'defaults'), 'command defaults')
    require(key.one('command').lower() == 'x', 'reviewed x command must be a single button')
    state_commands = [section for section in sections if section.title.startswith('state -1,')
                      and any(normalized(value).startswith('command="x"||') for value in section.values('triggerall'))]
    entry, near = (unique((section for section in state_commands if section.one('type', '').lower() == 'changestate'
                           and section.one('value', '') == str(number)), f'human command entry {number}') for number in (200, 205))
    require(near.line < entry.line and 'p2bodydistx<=16' in [normalized(value) for value in near.values('triggerall')],
            'cannot verify close-light-punch precedence')
    require({'roundstate=2', 'command!="holddown"', 'statetype!=a'}.issubset(map(normalized, entry.values('triggerall')))
            and any(normalized(value).startswith('command="x"||') for value in entry.values('triggerall'))
            and any(normalized(value).startswith('ctrl||') for value in entry.values('trigger1')),
            'cannot verify controllable standing command entry')
    return {'keyLine': key.line, 'farEntryLine': entry.line, 'nearEntryLine': near.line,
            'time': int(key.one('time', defaults.one('command.time'))),
            'bufferTime': int(key.one('buffer.time', defaults.one('command.buffer.time')))}


def movement_rules(constants, common, actions):
    velocity = unique((section for section in constants if section.title == 'velocity'), 'Velocity constants')
    movement = unique((section for section in constants if section.title == 'movement'), 'Movement constants')
    pre_jump = unique((action for action in actions if action['action'] == 40), 'pre-jump Action 40')
    require(all(frame['time'] > 0 for frame in pre_jump['frames']), 'invalid pre-jump action duration')
    _, jump_controls = state(common, 40)
    require(any(control.one('type').lower() == 'changestate' and control.one('value') in ('50', '55')
                and 'animtime=0' in map(normalized, control.values('triggerall') + control.values('trigger1'))
                for control in jump_controls), 'cannot verify Action 40 pre-jump exit')
    _, hop_controls = state(common, 55)
    hop = unique((control for control in hop_controls if control.one('type').lower() == 'velset'
                  and normalized(control.one('trigger1')) == 'time=0'), 'short-hop entry velocity')
    require(Fraction(velocity.one('jump.fwd')) == -Fraction(velocity.one('jump.back')),
            'directional jump speeds differ and cannot share the schema field')
    _, reaction = state(common, 5001)
    stop_slide = unique((control for control in reaction if control.one('type').lower() == 'velmul'
                         and normalized(control.one('trigger1')) == 'time>=gethitvar(slidetime)'), 'hit slide braking')
    return {'walkForwardSpeed': fraction(velocity.one('walk.fwd')), 'walkBackwardSpeed': fraction(-Fraction(velocity.one('walk.back'))),
            'preJumpFrames': fraction(sum(frame['time'] for frame in pre_jump['frames'])),
            'shortHopInitialVelocity': fraction(-Fraction(hop.one('y'))),
            'highJumpInitialVelocity': fraction(-Fraction(velocity.one('jump.neu').split(',')[1].strip())),
            'jumpGravity': fraction(movement.one('yaccel')), 'horizontalJumpSpeed': fraction(velocity.one('jump.fwd')),
            'groundReaction': {'velocityRetention': fraction(movement.one('stand.friction')),
                               'crouchGuardVelocityRetention': fraction(movement.one('crouch.friction')),
                               'lastHitStepScale': fraction(stop_slide.one('x'))}}


def idle_hurtboxes(actions):
    result = {}
    for posture, number in (('standing', 0), ('crouching', 11)):
        action = unique((item for item in actions if item['action'] == number), f'{posture} Action {number}')
        frames = collision_boxes(action, 2)
        require(frames and frames[0] and all(boxes == frames[0] for boxes in frames),
                f'Action {number}: idle collision must be constant over all frames')
        result[posture] = frames[0]
    return result


def fraction(value):
    result = Fraction(value)
    return {'numerator': result.numerator, 'denominator': result.denominator}


def load_json(path):
    return json.loads(path.read_text(encoding='utf-8'))


def validate_manifest(path, source_hashes, action):
    manifest = load_json(path)
    require(manifest.get('format') == 1 and manifest.get('character') == 'terry98' and manifest.get('localcoord') == 320,
            'expected terry98 Manifest v1 in localcoord 320')
    require(all(manifest['sources'].get(key) == value for key, value in source_hashes.items()), 'Manifest source hash mismatch')
    number = action['action']
    target = unique((entry for entry in manifest['actions'] if entry['action'] == number), f'Manifest Action {number}')
    require(target['collisions'] == action['collisions'] and target.get('loop_start') == action['loop_start'],
            f'Manifest Action {number} collision or loop data differs from AIR')
    require([{key: frame[key] for key in FRAME_KEYS} for frame in target['frames']] == action['frames'],
            f'Manifest Action {number} frames differ from AIR')
    for index, frame in enumerate(target['frames']):
        require(frame['group'] >= 0 and frame['image'] >= 0, f'Manifest Action {number} frame {index}: blank sprite')
    indexes = {frame.get('sprite_index') for frame in target['frames']}
    diagnostics = [item for item in audit_assets({**manifest, 'actions': [target]}, path.parent)
                   if item.get('action') == number or item.get('sprite_index') in indexes or item.get('first_index') in indexes]
    require(not diagnostics, f'Manifest Action {number} resource audit failed: {json.dumps(diagnostics, ensure_ascii=True)}')
    return [frame['sprite_index'] for frame in target['frames']]


def read_terry_archive(zip_path, expected_zip=SOURCE_SHA256):
    zip_sha = archive_digest(zip_path)
    require(zip_sha == expected_zip, f'unreviewed character archive SHA-256: {zip_sha}')
    with zipfile.ZipFile(zip_path) as archive:
        def_file = archive_member(archive, 'Terry-KOF98.def')
        definition = read_sections(def_file.decode('latin-1'))
        files = unique((section for section in definition if section.title == 'files'), 'DEF Files section')
        source_files = {key: files.one(key) for key in ('cmd', 'cns', 'st1', 'anim', 'stcommon', 'sprite')}
        contents = {key: archive_member(archive, name) for key, name in source_files.items()}
    require(localcoord(def_file) == 320, 'Terry conversion requires source localcoord 320')
    hashes = {key: digest(value) for key, value in contents.items()}
    members = {'def': {'name': 'Terry-KOF98.def', 'sha256': digest(def_file)},
               **{key: {'name': name, 'sha256': hashes[key]} for key, name in source_files.items()}}
    return zip_sha, members, contents


def manifest_hashes(zip_sha, members):
    return {'zip_sha256': zip_sha, 'def_sha256': members['def']['sha256'],
            'sff_sha256': members['sprite']['sha256'], 'air_sha256': members['anim']['sha256']}


def inspect_source(zip_path, manifest_path, expected_zip=SOURCE_SHA256):
    zip_sha, members, contents = read_terry_archive(zip_path, expected_zip)
    actions, diagnostics = parse_air(contents['anim'].decode('latin-1'))
    action = unique((entry for entry in actions if entry['action'] == 200), 'AIR Action 200')
    require(not any(re.search(r'duplicate action 200\b', item) for item in diagnostics), 'duplicate AIR Action 200')
    first, following, move = source_move(read_sections(contents['st1'].decode('latin-1')))
    startup, active, recovery = timeline(action, first, following)
    strike = collision_boxes(action, 1)
    require(following == first+1 and len(strike[first-1]) == 1 and sum(map(len, strike)) == 1,
            'State 200 requires one Clsn1 rectangle on the sole active element')
    indexes = validate_manifest(manifest_path, manifest_hashes(zip_sha, members), action)
    movement = movement_rules(read_sections(contents['cns'].decode('latin-1')),
                              read_sections(contents['stcommon'].decode('latin-1')), actions)
    command = command_rules(read_sections(contents['cmd'].decode('latin-1')))
    return {
        'format': 1, 'zipSha256': zip_sha, 'manifestSha256': archive_digest(manifest_path), 'localcoord': 320,
        'members': members,
        'state': {'id': 200, **move}, 'command': command, 'movement': movement,
        'groundHurtboxes': idle_hurtboxes(actions),
        'action': {'id': 200, 'startup': startup, 'active': active, 'recovery': recovery,
                   'durations': [frame['time'] for frame in action['frames']], 'spriteIndices': indexes,
                   'spriteKeys': [[frame['group'], frame['image']] for frame in action['frames']],
                   'clsn1': strike, 'clsn2': collision_boxes(action, 2)},
    }


def trace_rows(path, name, frame_count=66):
    with path.open(encoding='utf-8') as stream:
        rows = [json.loads(line) for line in stream]
    phases = ('before-command', 'after-collision', 'end', 'hitdef')
    require(all(row['case'] == name and row['roundState'] == 2 and len(row['chars']) == 2
                and [character['pn'] for character in row['chars']] == [1, 2] for row in rows),
            f'{name}: trace must be the two-seat active round scenario')
    groups = {phase: [row for row in rows if row['phase'] == phase] for phase in phases}
    for phase in phases[:-1]:
        ticks = [row['frame'] for row in groups[phase]]
        require(ticks == list(range(frame_count)), f'{name}: {phase} trace incomplete or out of order')
    return groups


def verify_attack_timeline(action, collisions, entry, exit_frame, pause, *, state_id=200, normalize_edges=False):
    action_id = action.get('id', state_id)
    expected = [(element, tick) for element, duration in enumerate(action['durations'])
                for tick in range(duration)]
    freeze_start = action['startup'] + 1
    expected[freeze_start:freeze_start] = [expected[freeze_start]] * pause
    actual = []
    for row in collisions[entry:exit_frame]:
        character = row['chars'][0]
        element = character['elem']
        require(character['state'] == state_id and character['anim'] == action_id
                and type(element) is int and 0 <= element < len(action['durations']),
                f"frame {row['frame']}: unexpected State {state_id}/Action {action_id} visual element")
        require(character['frame']['sprite'] == action['spriteKeys'][element]
                and character['frame']['duration'] == action['durations'][element],
                f"frame {row['frame']}: reference Action {action_id} sprite or duration disagrees with AIR")
        for key in ('clsn1', 'clsn2'):
            actual_boxes = character['frame'][key] or []
            if normalize_edges:
                actual_boxes = [[min(x1, x2), min(y1, y2), max(x1, x2), max(y1, y2)]
                                for x1, y1, x2, y2 in actual_boxes]
            require(actual_boxes == action[key][element],
                    f"frame {row['frame']}: reference collision boxes disagree with AIR")
        actual.append((element, character['elemTime']))
    require(actual == expected, f'Action {action_id} reference timeline differs from AIR and observed pause: {actual}')


def verify_contact(name, groups, contact, end, *, blocked=None, crouching=None, verify_late_crouch=True):
    frame = contact['frame']
    attacker, target = contact['chars']
    definition = unique((row['chars'][0]['hitDef'] for row in groups['hitdef'] if row['frame'] == frame),
                        f'{name} contact HitDef')
    crouching = name == 'crouch-guard-late' if crouching is None else crouching
    blocked = (name == 'guard' or crouching) if blocked is None else blocked
    require(target['guarded'] is blocked and target['damage'] >= 0, f'{name}: unexpected contact outcome')
    atk_pause, target_pause = definition['guardPause' if blocked else 'hitPause']
    require(type(atk_pause) is int and type(target_pause) is int and 0 < atk_pause <= 60 and 0 < target_pause <= 60,
            f'{name}: invalid reference pause values')
    require(attacker['hitPause'] == atk_pause + 1 and target['hitShake'] == target_pause
            and [row['chars'][0]['hitPause'] for row in end[frame:frame+atk_pause+1]] == list(range(atk_pause, -1, -1))
            and [row['chars'][1]['hitShake'] for row in end[frame:frame+target_pause+1]] == list(range(target_pause, -1, -1)),
            f'{name}: applied pauses disagree with evaluated HitDef')
    restored = next((row for row in end[frame+1:] if row['chars'][1]['ctrl']), None)
    require(restored is not None and not end[frame]['chars'][1]['ctrl'], f'{name}: defender control recovery missing')
    restored_frame = restored['frame']
    stun = restored_frame - frame - target_pause
    require(stun > 0, f'{name}: invalid post-pause defender control interval')
    hp_loss = target['life'] - restored['chars'][1]['life']
    require(hp_loss == target['damage'] and (hp_loss == 0 if blocked else hp_loss > 0),
            f'{name}: life loss disagrees with resolved damage')
    raw_push = definition['guardVel' if blocked else 'groundVel']
    horizontal, vertical = target['hitVel']
    sign = attacker['facing']
    require(abs(horizontal) == abs(raw_push[0]) and sign * horizontal > 0 and vertical == 0 and raw_push[1] == 0,
            f'{name}: unsupported ground contact velocity')
    speed = abs(horizontal)
    origin = target['pos'][0]
    movement = [row for row in end[frame+1:restored_frame]
                if row['chars'][1]['pos'][0] != end[row['frame']-1]['chars'][1]['pos'][0]]
    require(len(movement) >= 2, f'{name}: missing ground push trajectory')
    first = movement[0]['chars'][1]
    coefficient = first['friction'][1 if crouching else 0]
    require(0 < coefficient < 1 and math.isclose(sign * (first['pos'][0] - origin), speed, abs_tol=0.0001)
            and math.isclose(abs(first['vel'][0]), speed * coefficient, abs_tol=0.0001),
            f'{name}: ground push initial displacement or friction disagrees with reference')
    last_moved = movement[-1]['chars'][1]['pos'][0]
    previous = movement[-2]['chars'][1]
    last_step = abs(last_moved - previous['pos'][0])
    last_step_scale = Fraction(str(last_step / abs(previous['vel'][0]))).limit_denominator(1000)
    if crouching and verify_late_crouch:
        require(groups['before-command'][frame-1]['chars'][1]['state'] == 0
                and groups['before-command'][frame-1]['chars'][1]['raw'] == 0
                and end[frame]['chars'][1]['state'] == 152 and end[frame+1]['chars'][1]['anim'] == 151
                and end[frame+1]['chars'][1]['stateType'] == 2,
                f'{name}: did not guard on first crouch input from idle standing')
    return {'contactFrame': frame, 'pause': {'attackerFrames': atk_pause, 'defenderFrames': target_pause},
            'rawHitDefDamage': definition['damage'], 'rawGuardDamage': definition['guardDamage'],
            'rawHitTime': definition['hitTime'], 'rawGuardHitTime': definition['guardHitTime'],
            'damage': hp_loss, 'controlFrame': restored_frame, 'effectiveStun': stun,
            'initialPush': speed, 'firstPushFrame': movement[0]['frame'], 'pushRetention': coefficient,
            'lastPushStepScale': fraction(last_step_scale),
            'targetTravel': round(abs(restored['chars'][1]['pos'][0] - origin), 6)}


def verify_reference_case(action, path, name, distance):
    groups = trace_rows(path, name)
    start = groups['before-command'][0]['chars']
    mirrored = name == 'mirror'
    require(start[0]['facing'] == (-1 if mirrored else 1) and start[1]['facing'] == -start[0]['facing']
            and abs(start[1]['pos'][0] - start[0]['pos'][0]) == distance
            and start[0]['pos'][1] == start[1]['pos'][1] == 0,
            f'{name}: trace position/facing differs from fixture contract')
    collisions, end = groups['after-collision'], groups['end']
    entry = next((row['frame'] for row in collisions if row['chars'][0]['state'] == 200), None)
    require(entry is not None, f'{name}: missing State 200 entry')
    exit_frame = next((row['frame'] for row in collisions[entry:] if row['chars'][0]['state'] != 200), None)
    require(exit_frame is not None and collisions[exit_frame]['chars'][0]['state'] == 0
            and collisions[exit_frame]['chars'][0]['ctrl'], f'{name}: missing State 0 control recovery')
    contacts = [row for row in collisions if row['chars'][1]['gethit']]
    outcome = None
    if name in ('hit', 'guard', 'mirror', 'boundary-78', 'crouch-guard-late'):
        contact = unique(contacts, f'{name} contact')
        require(contact['frame'] == entry + action['startup'], f'{name}: expected first-active contact')
        outcome = verify_contact(name, groups, contact, end)
    else:
        require(not contacts and end[-1]['chars'][1]['life'] == start[1]['life'], f'{name}: unexpected hit or block')
        if name == 'crouch-guard':
            target_states = {row['chars'][1]['state'] for row in collisions[entry:exit_frame]}
            require(131 in target_states, 'crouch-guard: crouching guard state was not exercised')
        expected_hitdefs = list(range(entry + action['startup'], entry + action['startup'] + action['active']))
        require([row['frame'] for row in groups['hitdef']] == expected_hitdefs,
                f'{name}: HitDef window disagrees with AIR')
    verify_attack_timeline(action, collisions, entry, exit_frame, outcome['pause']['attackerFrames'] if outcome else 0)
    return {'trace': path.name, 'sha256': archive_digest(path), 'entryFrame': entry, 'exitFrame': exit_frame,
            'distance': distance, 'attackerFacing': start[0]['facing'], 'outcome': outcome}


def verify_boundary(audit, cases):
    _, _, strike_front, _ = next(boxes[0] for boxes in audit['action']['clsn1'] if boxes)
    # The target faces left, so its positive local X edge is the closest to the attacker.
    hurtbox_front = max(box[2] for box in audit['groundHurtboxes']['standing'])
    edge = cases['boundary-78']
    outside = [cases[name] for name in ('boundary-79', 'boundary-80')]
    require(edge['distance'] == strike_front + hurtbox_front and edge['outcome'] == cases['hit']['outcome']
            and all(case['distance'] > edge['distance'] and case['outcome'] is None for case in outside),
            'the source geometry and measured boundary do not confirm a single edge contact')


def inspect_reference(audit, path):
    metadata = load_json(path)
    require(metadata['engine'] == 'IKEMEN GO' and metadata['commit'] == REFERENCE_COMMIT,
            'reference must use the reviewed IKEMEN GO commit')
    require(metadata['terry_archive_sha256'] == audit['zipSha256'], 'reference source archive hash mismatch')
    for key in ('cmd', 'st1', 'anim'):
        member = audit['members'][key]
        require(metadata['source_sha256'].get(member['name']) == member['sha256'],
                f"reference member hash mismatch: {member['name']}")
    for key in ('executable_sha256', 'config_sha256'):
        require(re.fullmatch(r'[0-9a-f]{64}', metadata[key]) is not None, f'missing reference {key}')
    require('-p1 terry/Terry-KOF98.def -p2 terry/Terry-KOF98.def' in metadata['arguments'],
            'reference must use the reviewed Terry character for both seats')
    cases = {}
    for name, distance in REFERENCE_DISTANCES.items():
        require(metadata['scenario_distance'].get(name) == distance, f'{name}: metadata distance mismatch')
        cases[name] = verify_reference_case(audit['action'], path.parent / f'{name}.jsonl', name, distance)
    require(cases['hit']['outcome'] == cases['mirror']['outcome'], 'mirror reference differs from forward hit')
    verify_boundary(audit, cases)
    hit = cases['hit']['outcome']
    block = cases['guard']['outcome']
    crouch_block = cases['crouch-guard-late']['outcome']
    expected = audit['movement']['groundReaction']
    require(hit['lastPushStepScale'] == expected['lastHitStepScale']
            and block['lastPushStepScale'] == crouch_block['lastPushStepScale'] == fraction(1)
            and fraction(str(hit['pushRetention'])) == fraction(str(block['pushRetention'])) == expected['velocityRetention']
            and fraction(str(crouch_block['pushRetention'])) == expected['crouchGuardVelocityRetention'],
            'applied ground reaction differs from the reviewed standing and crouch friction or hit slide stop')
    require(all(crouch_block[key] == block[key] for key in ('effectiveStun', 'pause', 'initialPush', 'damage')),
            'crouch guard requires a different attack settlement')
    return {'metadataSha256': archive_digest(path), 'metadata': metadata, 'cases': cases}


def build_documents(audit, reference):
    action = audit['action']
    left, top, right, bottom = action['clsn1'][next(i for i, boxes in enumerate(action['clsn1']) if boxes)][0]
    hit = reference['cases']['hit']['outcome']
    block = reference['cases']['guard']['outcome']
    require(hit['initialPush'] == block['initialPush'] and hit['pushRetention'] == block['pushRetention'],
            'the ground projection requires matching first-step hit and block friction')
    # The legacy additive value is the first-step decrement for recipients without groundReaction.
    coefficient = Fraction(str(hit['pushRetention']))
    friction = hit['initialPush'] * (1 - coefficient)
    command = {'commandId': '5A', 'directions': [], 'buttons': ['LightPunch'],
               'maxSequenceFrames': audit['command']['time'], 'buttonToleranceFrames': 0, 'priority': 0,
               'bufferLifetimeFrames': audit['command']['bufferTime'], 'chargeHoldFrames': 0,
               'chargeReleaseToleranceFrames': 0}
    attack = {
        'offsetForward': fraction(Fraction(left + right, 2)), 'offsetUp': fraction(Fraction(-top - bottom, 2)),
        'halfWidth': fraction(Fraction(right - left, 2)), 'halfHeight': fraction(Fraction(bottom - top, 2)),
        'firstActiveFrame': 0, 'lastActiveFrame': action['active'] - 1,
        'height': 'Mid', 'damage': hit['damage'], 'hitstunFrames': hit['effectiveStun'],
        'blockstunFrames': block['effectiveStun'], 'groundKnockback': fraction(hit['initialPush']),
        'groundKnockbackFriction': fraction(friction), 'blockKnockback': fraction(block['initialPush']),
        'launchVelocityX': fraction(0), 'launchVelocityY': fraction(0), 'knockdownFrames': 0, 'wakeupFrames': 0,
        'hitstopFrames': max(hit['pause'].values()),
        'hitstopByOutcome': {'hit': hit['pause'], 'block': block['pause']},
        'contactOnEdge': True,
    }
    move = {'moveId': MOVE_ID, 'commandId': '5A', 'allowedFrom': 'Standing',
            'startupFrames': action['startup'], 'activeFrames': action['active'], 'recoveryFrames': action['recovery'],
            'attacks': [attack], 'cancels': [], 'throws': [], 'projectiles': []}
    hurtboxes = {posture: [
        {'minX': fraction(x1), 'maxX': fraction(x2), 'minY': fraction(-y2), 'maxY': fraction(-y1)}
        for x1, y1, x2, y2 in boxes
    ] for posture, boxes in audit['groundHurtboxes'].items()}
    hurtboxes['standingOnCrouchEntry'] = True
    fighter = {'schemaVersion': 1, 'fighterId': 'TerryS200', 'displayName': 'Terry State 200 (Experimental)',
               'movement': audit['movement'], 'commands': [command], 'moves': [move], 'groundHurtboxes': hurtboxes}
    binding = {'schemaVersion': 2, 'visualId': 'terry98-stage14', 'scale': 1.0,
               'clips': {'idle': {'action': 0}, 'walk': {'action': 20}, 'walk_back': {'action': 21},
                         'crouch': {'action': 11}, 'jump': {'action': 41, 'loop': False},
                         'guard': {'action': 150}, 'guard_crouch': {'action': 151},
                         'hurt': {'action': 5000, 'loop': False},
                         'state200': {'action': 200, 'clock': 'move', 'loop': False}},
               'moves': {MOVE_ID: {'Startup': 'state200', 'Active': 'state200', 'Recovery': 'state200'}}}
    provenance = {**audit, 'reference': reference, 'projection': {
        'normalization': 'State 200 entry is tick 0. Read animation at after-collision before end advances the cursor.',
        'stun': 'First restored defender control frame minus contact frame minus defender pause.',
        'damage': 'Observed recipient HP loss on fresh first-active Terry-versus-Terry ground contact.',
        'knockback': 'Terry groundReaction uses source standing and crouch friction with last hit step braking. The additive attack field is the first decrement fallback for recipients without this profile.',
        'hitstopFallback': 'Legacy symmetric value is max(hit attacker pause, hit defender pause); melee uses outcome pairs.',
        'contactOnEdge': 'The unexpanded AIR edges meet at separation 78, and the measured contact lands. Separations 79 and 80 miss.',
        'crouchGuard': 'The first down-back sample can still collide against standing geometry and lock crouch guard. An already-crouched target clears the strike.',
        'limits': ['Other modes, opponents, and late contact can change source damage.',
                   'Close punch priority, same-move reentry, other cancels, per-action hurtboxes and air follow-ups are outside this sample.',
                   'Idle ground hurtboxes use constant AIR Clsn2 of Actions 0 and 11. Other states retain existing NewKOF geometry.',
                   'NewKOF latches the guard reaction picture. Original guard pictures may change with later directional input.',
                   'NewKOF commits damage with contact; IKEMEN applies recorded HP loss on the following tick.'],
    }}
    return {'state200.fighter.json': fighter, 'state200.source.json': provenance, 'state200.binding.json': binding}


def output_documents(directory, documents):
    paths = {directory / name: json.dumps(document, ensure_ascii=True, indent=2, sort_keys=True, allow_nan=False) + '\n'
             for name, document in documents.items()}
    for path in paths:
        require(not path.exists() and not path.is_symlink(), f'will not overwrite {path}; use a fresh output directory')
    directory.mkdir(parents=True, exist_ok=True)
    for path, content in paths.items():
        with path.open('x', encoding='utf-8', newline='\n') as stream:
            stream.write(content)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source_zip', type=Path)
    parser.add_argument('manifest', type=Path)
    parser.add_argument('--audit-only', action='store_true', help='print static provenance without converting')
    parser.add_argument('--reference', type=Path, help='run-metadata.json alongside original engine JSONL traces')
    parser.add_argument('--output-dir', type=Path, help='directory for the generated fighter, binding and provenance')
    args = parser.parse_args()
    if args.audit_only and (args.reference or args.output_dir) or not args.audit_only and not (args.reference and args.output_dir):
        parser.error('use either --audit-only, or both --reference and --output-dir')
    try:
        audit = inspect_source(args.source_zip, args.manifest)
        if args.audit_only:
            print(json.dumps(audit, ensure_ascii=True, indent=2, sort_keys=True))
            return
        reference = inspect_reference(audit, args.reference)
        output_documents(args.output_dir, build_documents(audit, reference))
    except (ValueError, OSError, TypeError, KeyError, IndexError, zipfile.BadZipFile) as error:
        parser.exit(2, f'State 200 conversion: {error}\n')


if __name__ == '__main__':
    main()
