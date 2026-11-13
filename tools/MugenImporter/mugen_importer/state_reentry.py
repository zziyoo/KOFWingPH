"""Audit Terry's standing-light re-entry in the reviewed Stage 18 source."""

import argparse
import copy
import json
import re
import zipfile
from pathlib import Path

from . import state200, state205215, state210
from .air import parse_air
from .state200 import normalized, number_in, read_sections, require, unique


PRESETS = {'default': ('TerryReentry', 'terry98-stage18-default'),
           'mode2': ('TerryReentryMode2', 'terry98-stage18-mode2')}
MOVE_IDS = {200: state200.MOVE_ID, 205: state205215.MOVE_IDS[205]}
STANDING_LIGHT_COMMAND = '5A'
# IKEMEN GO InputBits are recorded verbatim by the pinned native trace.
NATIVE_DOWN = 1 << 1
NATIVE_X = 1 << 7
NATIVE_Y = 1 << 8
REQUIRED_CASES = (
    'whiff-9', 'whiff-10', 'whiff-11', 'whiff-12', 'whiff-16', 'whiff-17', 'held',
    'whiff-held-10', 'whiff-held-11', 'whiff-both-12', 'whiff-heavy-12', 'mirror-whiff-12',
    'far-hit-9', 'far-hit-17', 'far-hit-19', 'far-hit-21', 'far-hit-23', 'far-guard-23',
    'near-hit-23', 'near-tight-hit-23', 'near-guard-23', 'mirror-near-hit-23',
    'approach-90-20', 'approach-94p5-20', 'approach-95-20', 'mirror-approach-90-20',
    'whiff-down-11', 'whiff-down-12', 'far-hit-down-19',
)
FROZEN_BASE_SHA256 = {
    'default.fighter.json': '07371edabf2906c4ee241308b24576697888781983f86aeffb50466cd8ee5fa4',
    'default.binding.json': '08c3796205e69e0b1ab57505f4ef3b5ccbaf8d7fc045951d1221bcfd68468521',
    'mode2.fighter.json': '99331087cb9c55444b7f75cf9e63d10496786fe39d6c92717d1d0a06fd577ce8',
    'mode2.binding.json': '97d94fc08a3ef6eb6fb2f9d9409fd6391afbafce6db2c2f984def7652550ba24',
}


def script_sections(raw):
    # Keep one source line per CRLF. Latin-1 control characters occur inside source comments.
    return read_sections(raw.decode('latin-1').translate({code: 32 for code in (11, 12, 28, 29, 30, 133)}))


def load_frozen_base(directory, mode):
    documents = []
    for kind in ('fighter', 'binding'):
        name = f'{mode}.{kind}.json'
        content = (directory / name).read_bytes()
        require(state200.digest(content) == FROZEN_BASE_SHA256[name], f'{name}: frozen Stage 17 SHA-256 mismatch')
        documents.append(json.loads(content))
    return tuple(documents)


def build_profile(audit, timing, base_fighter, base_binding, mode):
    fighter, binding = copy.deepcopy((base_fighter, base_binding))
    fighter['fighterId'], binding['visualId'] = PRESETS[mode]
    fighter['displayName'] = f"Terry Standing Light Re-entry ({'default' if mode == 'default' else 'mode 2'})"
    command = audit['command']
    light = unique((item for item in fighter['commands'] if item['commandId'] == command['commandId']),
                   'Stage 17 standing-light command')
    light['bufferLifetimeFrames'] = max(light['bufferLifetimeFrames'], timing['maximumInputAge'])
    moves = {state_id: unique((move for move in fighter['moves'] if move['moveId'] == move_id),
                              f'Stage 17 move {move_id}') for state_id, move_id in MOVE_IDS.items()}
    require(all(move['commandId'] == command['commandId'] and not move['cancels'] for move in moves.values()),
            'Stage 17 standing-light move commands or cancel baseline do not match')
    require(moves[205]['maximumStartBodyDistance'] == state200.fraction(command['nearMaximumBodyDistance'])
            and 'maximumStartBodyDistance' not in moves[200], 'Stage 17 near/far move gates do not match CMD')
    for source, move in moves.items():
        move['cancels'] = [{'targetMoveId': MOVE_IDS[target], **timing, 'allowSameMove': source == target,
                            'allowWhiff': True, 'requiresHitConfirm': False, 'requiresBlockConfirm': False,
                            'standingInputOnly': command['requiresNoDown']}
                           for target in command['targetOrder']]
    return fighter, binding


def physical_light_presses(before_command):
    presses = []
    held = False
    for row in before_command:
        pressed = bool(row['chars'][0]['raw'] & NATIVE_X)
        if pressed and not held:
            presses.append(row['frame'])
        held = pressed
    return presses


def inspect_command(sections):
    command = state200.command_rules(sections)
    _, controllers = state205215.move_scope(sections, -1)
    entry_guards = {'var(59)!=1', '!ishelper', 'roundstate=2',
                    'command="x"||helper(99999),var(0)>0', 'command!="holddown"', 'statetype!=a'}
    source_elements = {}
    for side, state_id in (('near', 205), ('far', 200)):
        entry = unique((item for item in controllers if item.line == command[f'{side}EntryLine']),
                       f'human {side} light punch State -1 entry')
        guards = {normalized(value) for value in entry.values('triggerall')}
        if side == 'near':
            distance = unique((value for value in guards if value.startswith('p2bodydistx')),
                              'near light punch distance guard')
            (near_maximum,) = number_in(distance, r'p2bodydistx<=(\d+)', 'near light punch distance')
            guards.remove(distance)
        require(guards == entry_guards, f'line {entry.line}: unsupported State {state_id} entry guards')
        require(normalized(entry.one('trigger1')) == 'ctrl||(stateno=[100,101])||var(2)>=1',
                f'line {entry.line}: unsupported free entry condition')
        elements = dict(number_in(entry.one(key), r'stateno=(\d+)&&animelem=(\d+),>=0',
                                  f'line {entry.line} re-entry condition') for key in ('trigger2', 'trigger3'))
        require(elements.keys() == MOVE_IDS.keys() and (not source_elements or source_elements == elements),
                f'line {entry.line}: near and far light re-entry conditions disagree')
        source_elements = elements
    return {**command, 'physicalButton': 'x', 'commandId': STANDING_LIGHT_COMMAND,
            'targetOrder': [205, 200], 'sourceElements': source_elements, 'nearMaximumBodyDistance': near_maximum,
            'requiresNoDown': True}


def inspect_helper(sections):
    _, controls = state205215.move_scope(sections, 99999)
    updates = [item for item in controls if item.values('var(0)')]
    require(len(updates) == 2, 'unsupported command helper x latch count')
    latch, decay = updates
    require(normalized(latch.one('type')) == 'varset' and normalized(latch.one('trigger1')) == 'command="x"'
            and normalized(decay.one('type')) == 'varadd' and normalized(decay.one('trigger1')) == 'var(0)>0'
            and all(not item.values('ignorehitpause') for item in updates)
            and (latch.one('var(0)'), decay.one('var(0)')) == ('3', '-1'),
            'unsupported command helper x latch or decay')
    return {'state': 99999, 'setLine': latch.line, 'decayLine': decay.line,
            'latchValue': int(latch.one('var(0)')), 'decayValue': int(decay.one('var(0)'))}


def inspect_source(zip_path, manifest_path=None):
    zip_sha, members, contents = state200.read_terry_archive(zip_path)
    with zipfile.ZipFile(zip_path) as archive:
        definition = state200.archive_member(archive, members['def']['name'])
        files = unique((section for section in script_sections(definition) if section.title == 'files'),
                       'DEF Files section')
        for key in ('st4', 'st5'):
            name = files.one(key)
            contents[key] = state200.archive_member(archive, name)
            members[key] = {'name': name, 'sha256': state200.digest(contents[key])}
    command = inspect_command(script_sections(contents['cmd']))
    helper = inspect_helper(script_sections(contents['st5']))
    states = script_sections(contents['st1'])
    actions, diagnostics = parse_air(contents['anim'].decode('latin-1'))
    profiles = {}
    for state_id in MOVE_IDS:
        if state_id == 200:
            first, following, source = state200.source_move(states)
        else:
            [(first, following)], source = state205215.source_move(states, state_id)
        require(following == command['sourceElements'][state_id], f'State {state_id}: re-entry does not start at recovery')
        action = unique((item for item in actions if item['action'] == state_id), f'AIR Action {state_id}')
        require(not any(f'duplicate action {state_id} ' in item for item in diagnostics),
                f'duplicate AIR Action {state_id}')
        startup, active, recovery = state200.timeline(action, first, following)
        sprites = state200.validate_manifest(manifest_path, state200.manifest_hashes(zip_sha, members), action) if manifest_path else None
        profiles[state_id] = {'source': source, 'action': state_id, 'startup': startup, 'active': active, 'recovery': recovery,
                              'durations': [frame['time'] for frame in action['frames']], 'spriteIndices': sprites,
                              'spriteKeys': [[frame['group'], frame['image']] for frame in action['frames']]}
    return {'format': 1, 'zipSha256': zip_sha, 'members': members, 'command': command, 'helper': helper, 'moves': profiles,
            'manifestSha256': state200.archive_digest(manifest_path) if manifest_path else None}


def inspect_case(audit, metadata, directory, name):
    mode, _, scenario = name.partition('-reentry-')
    require(mode in PRESETS and re.fullmatch(r'[a-z0-9-]+', scenario) is not None, f'invalid Stage 18 case {name}')
    path = directory / f'{name}.jsonl'
    info_path = directory / f'{name}-metadata.json'
    info = state200.load_json(info_path)
    sha = state200.archive_digest(path)
    distance = metadata['scenario_distance'][name]
    require(info['case'] == name and info['trace_sha256'] == sha and info['initial_distance'] == distance
            and all(info[key] == metadata[key] for key in ('rng_seed', 'executable_sha256', 'config_sha256')),
            f'{name}: native case metadata or trace SHA-256 mismatch')
    expected_palette = 7 if mode == 'mode2' else 1
    require(info['arguments'] == f"{metadata['arguments']} -p1.pal {expected_palette} -p2.pal 2",
            f'{name}: wrong native character palette')
    with path.open(encoding='utf-8') as stream:
        rows = [json.loads(line) for line in stream]
    require(all(row['case'] == name for row in rows), f'{name}: unexpected case identifier')
    stages = ('before-command', 'after-command', 'after-collision', 'end')
    phases = {stage: [row for row in rows if row['phase'] == stage] for stage in stages}
    require(all([row['frame'] for row in phase] == list(range(76))
                and all(row['roundState'] == 2 and [char['pn'] for char in row['chars']] == [1, 2] for row in phase)
                for phase in phases.values()), f'{name}: missing committed two-seat frames')
    initial = phases['before-command'][0]['chars']
    direction = -1 if scenario.startswith('mirror-') else 1
    require([char['facing'] for char in initial] == [direction, -direction]
            and abs(initial[1]['pos'][0] - initial[0]['pos'][0]) == distance
            and all(char['pos'][1] == 0 for char in initial)
            and initial[0]['vars']['var10'] == (2 if mode == 'mode2' else 0),
            f'{name}: wrong native scene distance, facing or mode')
    intro = state210.verify_mode_intro(directory / f'{name}-mode.jsonl', name) if mode == 'mode2' else None
    presses = physical_light_presses(phases['before-command'])
    changes = [row for row in rows if row['phase'] == 'state-change' and row['seat'] == 1]
    reentries = []
    for change in changes:
        if (change['from'] not in MOVE_IDS or change['to'] not in MOVE_IDS
                or change['ctrl'] or change['var2'] > 0):
            continue
        frame = change['frame']
        last_press = max((press for press in presses if press <= frame), default=None)
        source = audit['moves'][change['from']]
        source_element = audit['command']['sourceElements'][change['from']] - 1
        require(change['anim'] == change['from'] and change['elem'] == source_element
                and source['startup'] + source['active'] <= change['stateTime'] < sum(source['durations'])
                and not change['hitPause'] and change['minus'] == -1,
                f'{name} F{frame}: re-entry must run during old State -1 recovery outside HitPause')
        require(last_press is not None and not change['raw'] & NATIVE_DOWN
                and (change['xBuffer'] or change['helperX'] > 0),
                f'{name} F{frame}: re-entry lacks native physical x or command latch evidence')
        target = 205 if change['bodyDistX'] <= audit['command']['nearMaximumBodyDistance'] else 200
        require(change['to'] == target, f'{name} F{frame}: re-entry violates CMD near/far precedence')
        move = audit['moves'][target]
        committed = phases['after-collision'][frame]['chars'][0]
        require(committed['state'] == committed['anim'] == target and committed['time'] == 1
                and committed['elem'] == committed['elemTime'] == 0
                and not any(committed[key] for key in ('moveContact', 'moveHit', 'moveGuarded'))
                and committed['frame']['sprite'] == move['spriteKeys'][0],
                f'{name} F{frame}: re-entry did not commit a fresh startup and source sprite')
        next_hitdef = next((row for row in rows if row['phase'] == 'hitdef' and row['hitDefOwner'] == 1
                           and row['frame'] >= frame), None)
        require(next_hitdef is not None and next_hitdef['frame'] == frame + move['startup']
                and next_hitdef['chars'][0]['state'] == target,
                f'{name} F{frame}: re-entry did not open a fresh HitDef window')
        reentries.append({**change, 'inputAge': frame - last_press,
                          'sprite': committed['frame']['sprite'], 'firstNewHitDefFrame': next_hitdef['frame']})
    contacts = [{'frame': row['frame'], 'state': row['chars'][0]['state'], 'guarded': row['chars'][1]['guarded'],
                 'damage': row['chars'][1]['damage'], 'attackerPause': row['chars'][0]['hitPause']}
                for row in phases['after-collision'] if row['chars'][1]['gethit']]
    inputs = {'downFrames': [row['frame'] for row in phases['before-command'] if row['chars'][0]['raw'] & NATIVE_DOWN],
              'heavyFrames': [row['frame'] for row in phases['before-command'] if row['chars'][0]['raw'] & NATIVE_Y]}
    return {'trace': path.name, 'sha256': sha, 'metadataSha256': state200.archive_digest(info_path),
            'modeIntro': intro, 'distance': distance, 'pressFrames': presses, 'transitions': changes,
            'reentries': reentries, 'contacts': contacts, **inputs}


def inspect_reference(audit, path):
    metadata = state200.load_json(path)
    require(metadata['engine'] == 'IKEMEN GO' and metadata['commit'] == state200.REFERENCE_COMMIT
            and metadata['capture_profile'] == 'Stage18', 'reference must use the reviewed Stage 18 IKEMEN GO capture')
    require(metadata['terry_archive_sha256'] == audit['zipSha256'], 'reference source archive SHA-256 mismatch')
    require(type(metadata['rng_seed']) is int and 0 < metadata['rng_seed'] < (1 << 31) - 1, 'invalid native RNG seed')
    for key in ('cmd', 'st1', 'anim', 'st4', 'st5'):
        member = audit['members'][key]
        require(metadata['source_sha256'].get(member['name']) == member['sha256'],
                f"reference member SHA-256 mismatch: {member['name']}")
    for key in ('executable_sha256', 'config_sha256'):
        require(re.fullmatch(r'[0-9a-f]{64}', metadata[key]) is not None, f'invalid native {key}')
    require(metadata['arguments'].startswith('-p1 terry/Terry-KOF98.def -p2 terry/Terry-KOF98.def '),
            'reference must use the reviewed Terry character for both seats')
    names = metadata['executed_cases']
    require(names and len(set(names)) == len(names), 'native case list must be nonempty and unique')
    return {'metadataSha256': state200.archive_digest(path), 'metadata': metadata,
            'cases': {name: inspect_case(audit, metadata, path.parent, name) for name in names}}


def verify_coverage(audit, references):
    all_cases = {}
    for reference in references:
        for name, case in reference['cases'].items():
            require(name not in all_cases, f'{name}: duplicate canonical Stage 18 case')
            all_cases[name] = case
    required = {f'{mode}-reentry-{scenario}' for mode in PRESETS for scenario in REQUIRED_CASES}
    require(required <= all_cases.keys(), f'missing native cases: {", ".join(sorted(required - all_cases.keys()))}')

    def reentry(cases, scenario, source, target):
        event = unique(cases[scenario]['reentries'], f'{scenario} single re-entry')
        require((event['from'], event['to']) == (source, target), f'{scenario}: unexpected re-entry source or target')
        return event

    timings = []
    for mode in PRESETS:
        prefix = f'{mode}-reentry-'
        cases = {name.removeprefix(prefix): case for name, case in all_cases.items() if name.startswith(prefix)}
        earliest = reentry(cases, 'whiff-12', 200, 200)
        late = reentry(cases, 'whiff-16', 200, 200)
        latched = [reentry(cases, f'whiff-{press}', 200, 200) for press in (10, 11)]
        require(earliest['inputAge'] == 0 and earliest['xBuffer'] and not cases['whiff-12']['contacts']
                and all(event['frame'] == earliest['frame'] and not event['xBuffer'] and event['helperX'] > 0
                        for event in latched), f'{mode}: native direct and helper re-entry timing differs')
        age = max(event['inputAge'] for event in latched)
        require(age > 0 and len({event['inputAge'] for event in latched}) == 2
                and cases['whiff-9']['pressFrames'][-1] == earliest['frame'] - age - 1,
                f'{mode}: missing expired physical helper command boundary')
        require(cases['whiff-17']['pressFrames'][-1] == late['frame'] + 1
                and late['inputAge'] == 0 and late['stateTime'] > earliest['stateTime'],
                f'{mode}: missing native normal recovery boundary')
        terminal = unique((event for event in cases['whiff-17']['transitions']
                           if event['from'] == event['to'] == 200), f'{mode}: terminal normal entry')
        require(terminal['frame'] == late['frame'] + 1 and terminal['stateTime'] == late['stateTime'] + 1
                and terminal['var2'] > 0, f'{mode}: terminal normal entry must use the source general action grant')
        for scenario in ('whiff-9', 'whiff-17', 'held', 'whiff-heavy-12', 'far-hit-9', 'far-hit-17', 'far-hit-19',
                         'whiff-down-12', 'far-hit-down-19'):
            require(not cases[scenario]['reentries'], f'{mode}-{scenario}: unexpected standing-light re-entry')
        require(len(cases['held']['pressFrames']) == 1 and cases['whiff-heavy-12']['heavyFrames'] == [12]
                and cases['whiff-both-12']['heavyFrames'] == [12]
                and not any(event['to'] in (210, 215) for scenario in ('whiff-heavy-12', 'whiff-both-12')
                            for event in cases[scenario]['transitions'])
                and all(cases[scenario]['downFrames'] for scenario in ('whiff-down-11', 'whiff-down-12', 'far-hit-down-19')),
                f'{mode}: invalid hold, simultaneous buttons, or Down input evidence')
        require(all(reentry(cases, scenario, 200, 200)['frame'] == earliest['frame']
                    for scenario in ('whiff-held-10', 'whiff-held-11', 'whiff-both-12', 'mirror-whiff-12')),
                f'{mode}: held second press, command priority or mirror differs from direct re-entry')
        held_samples = [reentry(cases, f'whiff-held-{press}', 200, 200) for press in (10, 11)]
        require([event['inputAge'] for event in held_samples] == [event['inputAge'] for event in latched]
                and all(event['raw'] & NATIVE_X and not event['xBuffer'] for event in held_samples),
                f'{mode}: holding x must preserve the original physical press age')
        down_release = reentry(cases, 'whiff-down-11', 200, 200)
        crouch = unique((event for event in cases['whiff-down-12']['transitions'] if event['to'] == 400),
                        f'{mode}: native crouching light route')
        require(down_release['frame'] == earliest['frame'] and down_release['inputAge'] == 1
                and cases['whiff-down-11']['downFrames'] == [earliest['frame'] - 1]
                and earliest['frame'] in cases['whiff-down-12']['downFrames']
                and crouch['frame'] == earliest['frame'] and crouch['from'] == 200,
                f'{mode}: down must be read at re-entry decision, including latch after Down release')
        hit = reentry(cases, 'far-hit-23', 200, 200)
        hit_latch = reentry(cases, 'far-hit-21', 200, 200)
        block = reentry(cases, 'far-guard-23', 200, 200)
        require(hit['stateTime'] == hit_latch['stateTime'] == block['stateTime'] == earliest['stateTime']
                and hit['moveHit'] > 0 and block['moveGuarded'] > 0 and hit_latch['inputAge'] == age
                and hit['frame'] == hit_latch['frame'] == block['frame'] > earliest['frame'],
                f'{mode}: wrong hit/guard pause continuation or helper input age')
        require(reentry(cases, 'near-hit-23', 205, 200)['moveHit'] > 0
                and reentry(cases, 'near-tight-hit-23', 205, 205)['moveHit'] > 0
                and reentry(cases, 'near-guard-23', 205, 200)['moveGuarded'] > 0
                and reentry(cases, 'mirror-near-hit-23', 205, 200)['moveHit'] > 0,
                f'{mode}: missing grounded near/far transition or confirmation cleanup evidence')
        for scenario in ('near-hit-23', 'near-tight-hit-23'):
            require(len(cases[scenario]['contacts']) == 2 and all(contact['damage'] > 0 for contact in cases[scenario]['contacts']),
                    f'{mode}-{scenario}: restarted instance did not deal its own damage')
        require(len(cases['near-guard-23']['contacts']) == 2
                and all(contact['guarded'] and contact['damage'] == 0 for contact in cases['near-guard-23']['contacts']),
                f'{mode}: restarted instance did not resolve a fresh zero-chip guard')
        reentry(cases, 'approach-90-20', 200, 205)
        reentry(cases, 'mirror-approach-90-20', 200, 205)
        boundary = audit['command']['nearMaximumBodyDistance']
        require(reentry(cases, 'approach-94p5-20', 200, 205)['bodyDistX'] == boundary
                and reentry(cases, 'approach-95-20', 200, 200)['bodyDistX'] == boundary + .5,
                f'{mode}: missing inclusive body-distance and just-outside re-entry samples')
        timings.append((earliest['stateTime'], late['stateTime'], age))
    require(timings[0] == timings[1], 'default and mode 2 native re-entry timing differs')
    first, last, age = timings[0]
    require(all(first == move['startup'] + move['active'] and last + 1 == sum(move['durations'])
                for move in audit['moves'].values()), 'native re-entry timing disagrees with source AIR recovery')
    return {'windowFirstFrame': first, 'windowLastFrame': last, 'phases': 'Active,Recovery', 'maximumInputAge': age}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source_zip', type=Path)
    parser.add_argument('manifest', type=Path, nargs='?')
    parser.add_argument('--audit-only', action='store_true', help='inspect source without converting')
    parser.add_argument('--base-profile-dir', type=Path, help='directory containing the frozen Stage 17 profiles')
    parser.add_argument('--reference', type=Path, action='append', help='canonical Stage 18 run metadata, repeat as needed')
    parser.add_argument('--output-dir', type=Path, help='fresh directory for the two Stage 18 profiles')
    args = parser.parse_args()
    conversion_args = args.base_profile_dir, args.reference, args.output_dir
    if (args.audit_only and any(conversion_args)) or (not args.audit_only and not all((args.manifest, *conversion_args))):
        parser.error('use --audit-only or supply the Manifest, --base-profile-dir, --reference and --output-dir')
    try:
        audit = inspect_source(args.source_zip, args.manifest)
        if args.audit_only:
            print(json.dumps(audit, ensure_ascii=True, indent=2, sort_keys=True))
            return
        base = {mode: load_frozen_base(args.base_profile_dir, mode) for mode in PRESETS}
        references = [inspect_reference(audit, path) for path in args.reference]
        timing = verify_coverage(audit, references)
        documents = {}
        for mode in PRESETS:
            fighter, binding = build_profile(audit, timing, *base[mode], mode)
            documents[f'{mode}.fighter.json'] = fighter
            documents[f'{mode}.binding.json'] = binding
            documents[f'{mode}.source.json'] = {
                **audit, 'preset': mode, 'window': timing, 'frozenBaseSha256': FROZEN_BASE_SHA256,
                'references': [{**run, 'cases': {name: case for name, case in run['cases'].items()
                                                 if name.startswith(f'{mode}-reentry-')}} for run in references],
                'limits': [
                    'Only human ground standing-light re-entry is projected. CMD routes involving other normals, specials, supers or AI are outside this profile.',
                    'The native Helper 99999 x latch is represented by age-limited physical press recognition and does not run a helper state machine.',
                    'Airborne approach fixtures establish body-distance and branch selection. Aerial hit and fall controller parity is outside this profile.',
                    'The existing Stage 17 damage formula and per-seat RNG scope apply to restarted moves.',
                ],
            }
        state200.output_documents(args.output_dir, documents)
    except (ValueError, OSError, TypeError, KeyError, IndexError, zipfile.BadZipFile) as error:
        parser.exit(2, f'Stage 18 re-entry conversion: {error}\n')


if __name__ == '__main__':
    main()
