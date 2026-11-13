"""Extend frozen Terry profiles with the reviewed State 205 Power Wave route."""

import argparse
import copy
import io
import json
from pathlib import Path
import runpy
import zipfile
from contextlib import redirect_stdout

from . import state200, state_power_wave, state_reentry
from .air import parse_air
from .state200 import normalized, number_in, require, unique
from .state205215 import action_profile, move_scope, source_move


PRESETS = {'default': ('TerryPowerWaveRoutes', 'terry98-stage20-default'),
           'mode2': ('TerryPowerWaveRoutesMode2', 'terry98-stage20-mode2')}
FROZEN_BASE = {
    'default': {
        'fighter': 'f9e5de9b8e9c4a2c628f4adef2eae98f8e542d5f9f10dee254e41030493baed5',
        'binding': '80df7490ab0bf1f737fdceeb1898dad56bfe8724877c2e579a89e5d83c346d7f',
        'source': '7f2423f2f3575ddc7b87c112eaf7f9278ba3fd3c262f579e704dd7dd6dad9c8f',
    },
    'mode2': {
        'fighter': '5bd6cb577001b941838606f8181ef8ed0ab903f50335e5319d33ba1008cce5a4',
        'binding': '736a103e69f7ca1906e10889247c2a878f636b3f51fc8db1292105a20a471aa2',
        'source': 'a80acdc33a0180d24b8136ca2f05ae586c1b6f8e24b70887f029829e5c4519e7',
    },
}
REQUIRED_REFERENCE_MEMBERS = ('Terry.air', 'Terry.cmd', 'Terry-N.cns', 'Terry-S.cns', 'Terry-2-3.cns', 'Helper.cns')


def cancel_window(condition, durations, *, source_state=205):
    first, last = number_in(condition, rf'stateno={source_state}&&animelemtime\((\d+)\)>0&&animelemtime\((\d+)\)<0',
                            f'State {source_state} Power Wave permission')
    require(1 <= first < last <= len(durations) and all(type(tick) is int and tick > 0 for tick in durations),
            f'State {source_state} AIR element window outside finite action')
    start, end = sum(durations[:first - 1]) + 1, sum(durations[:last - 1]) - 1
    require(start <= end, f'State {source_state} AIR element window is empty')
    return [start, end]


def verify_reentry_order(fighter, body_distance):
    near, far = (state_reentry.MOVE_IDS[number] for number in (205, 200))
    source = unique((move for move in fighter['moves'] if move['moveId'] == near), 'State 205')
    require(source['commandId'] == '5A' and source['maximumStartBodyDistance'] == state200.fraction(body_distance),
            'Stage 20 near-light distance gate does not match the frozen profile')
    require([cancel['targetMoveId'] for cancel in source['cancels']] == [near, far],
            'Stage 20 near-light re-entry order must remain State 205, then State 200')
    return source


def build_profile(audit, base_fighter, base_binding, mode):
    fighter, binding = copy.deepcopy((base_fighter, base_binding))
    source = verify_reentry_order(fighter, audit['nearBodyDistance'])
    fighter['fighterId'], binding['visualId'] = PRESETS[mode]
    if mode == 'default':
        fighter['displayName'] = 'Terry Weak Power Wave Routes (default)'
        wave = unique((move for move in fighter['moves'] if move['moveId'] == state_power_wave.MOVE_ID), 'State 1000')
        require(wave['commandId'] == state_power_wave.COMMAND_ID and bool(wave['projectiles']),
                'frozen State 1000 profile lacks the original command or entity')
        first, last = audit['cancelWindow']
        startup, active, recovery = (source[f'{phase}Frames'] for phase in ('startup', 'active', 'recovery'))
        require([startup, active, recovery] == audit['phaseDurations'],
                'State 205 source phases do not match the frozen profile')
        require(startup <= first <= last < startup + active,
                'State 205 source cancel window is outside the frozen Active phase')
        source['cancels'].insert(0, {'targetMoveId': state_power_wave.MOVE_ID,
                                     'windowFirstFrame': first, 'windowLastFrame': last, 'phases': 'Active',
                                     'requiresHitConfirm': False, 'requiresBlockConfirm': False, 'allowWhiff': True})
    else:
        fighter['displayName'] = 'Terry Standing Light Re-entry (mode 2 control)'
        require(not any(move['moveId'] == state_power_wave.MOVE_ID for move in fighter['moves'])
                and not any(command['commandId'] == state_power_wave.COMMAND_ID for command in fighter['commands']),
                'State 1000 must not be bound in the mode 2 control')
    return fighter, binding


def inspect_source(source_zip, manifest):
    wave = state_power_wave.inspect_source(source_zip, manifest)
    members = wave['members']
    keys = ('anim', 'cmd', 'st1', 'Terry-2-3.cns')
    with zipfile.ZipFile(source_zip) as archive:
        contents = {key: state200.archive_member(archive, members[key]['name']) for key in keys}
    require(all(state200.digest(contents[key]) == members[key]['sha256'] for key in keys),
            'source archive changed during inspection')
    actions, diagnostics = parse_air(contents['anim'].decode('latin-1'))
    action, durations = state_power_wave.source_action(actions, 205)
    require(not any('duplicate action 205 ' in message for message in diagnostics), 'duplicate AIR Action 205')
    sprites = state200.validate_manifest(manifest, state200.manifest_hashes(wave['zipSha256'], members), action)
    bounds, near_state = source_move(state_reentry.script_sections(contents['st1']), 205)
    timed = action_profile(action, bounds)
    phases = [timed[phase] for phase in ('startup', 'active', 'recovery')]
    _, controllers = move_scope(state_reentry.script_sections(contents['Terry-2-3.cns']), -2)
    active = unique((part for part in controllers if part.one('var(1)', '') == '1' and
                     normalized(part.one('trigger2', '')).startswith('stateno=205&&animelemtime')), 'State 205 cancel controller')
    require(normalized(active.one('triggerall')) == '!ishelper' and normalized(active.one('type')) == 'varset',
            'unsupported State 205 global cancel controller')
    window = cancel_window(active.one('trigger2'), durations)

    commands = state_reentry.script_sections(contents['cmd'])
    near = state_reentry.inspect_command(commands)
    _, entries = move_scope(commands, -1)
    default_entry = unique((part for part in entries if part.line == wave['command']['sourceLines']['entry']),
                           'default State 1000 entry')
    command = unique((term for term in map(normalized, default_entry.values('triggerall')) if term.startswith('command=')),
                     'weak Wave command and Helper latch')
    mode2_entry = unique((part for part in entries if part.one('value', '') == '1050'
                         and command in map(normalized, part.values('triggerall'))), 'mode 2 State 1050 entry')
    require(set(map(normalized, mode2_entry.values('triggerall'))) ==
            (set(map(normalized, default_entry.values('triggerall'))) - {'var(10)=0'}) |
            {'var(10)=1||var(10)=2', '!numproj'} and
            all(normalized(mode2_entry.one(key)) == normalized(default_entry.one(key))
                for key in ('type', 'trigger1', 'trigger2', 'trigger3')),
            'mode 2 must retain its separate State 1050 CMD guard')
    return {'zipSha256': wave['zipSha256'], 'manifestSha256': wave['manifestSha256'], 'members': wave['members'],
            'cancelWindow': window, 'nearBodyDistance': near['nearMaximumBodyDistance'], 'action': 205,
            'durations': durations, 'phaseDurations': phases, 'visuals': sprites, 'defaultWave': 1000, 'mode2Wave': 1050,
            'sourceLines': {'state205': near_state['definitionLine'], 'cancel': active.line, 'defaultWaveEntry': default_entry.line,
                            'mode2WaveEntry': mode2_entry.line, 'nearEntry': near['nearEntryLine']}}


def load_frozen_base(directory, mode, audit, *, frozen_base=FROZEN_BASE):
    docs = {}
    for kind, sha256 in frozen_base[mode].items():
        path = directory / f'{mode}.{kind}.json'
        raw = path.read_bytes()
        require(state200.digest(raw) == sha256, f'{path.name}: frozen {mode} profile SHA-256 mismatch')
        docs[kind] = json.loads(raw)
    require(all(docs['source'][key] == audit[key] for key in ('zipSha256', 'manifestSha256')),
            f'frozen {mode} profile does not use the audited ZIP and Manifest')
    return docs['fighter'], docs['binding']


def verify_native_references(directories, audit, *, reference_stage=20, include_contract=False):
    require(__debug__, 'native reference assertions require Python without optimization')
    checker_path = Path(__file__).resolve().parents[2] / f'Stage{reference_stage}Reference/Check-Traces.py'
    checker = runpy.run_path(str(checker_path))
    source_hashes = {member['name']: member['sha256'] for member in audit['members'].values()}
    references, cases = [], {}
    with redirect_stdout(io.StringIO()):
        for directory in directories:
            metadata_path = directory / 'run-metadata.json'
            metadata = state200.load_json(metadata_path)
            require(metadata['terry_archive_sha256'] == audit['zipSha256'], 'native reference archive mismatch')
            hashes = metadata['source_sha256']
            require(all(name in hashes for name in REQUIRED_REFERENCE_MEMBERS) and
                    all(source_hashes.get(name) == sha for name, sha in hashes.items()),
                    'native reference source mismatch')
            names = []
            for case, data in checker['read_cases'](directory):
                require(case not in cases, f'duplicate native case {case}')
                cases[case] = data
                names.append(case)
            require(names == metadata['executed_cases'], 'native metadata and checked case names differ')
            references.append({'metadataSha256': state200.archive_digest(metadata_path), 'cases': names})
        checked = checker['verify_cases'](cases)
    return (references, checked) if include_contract else references


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source_zip', type=Path)
    parser.add_argument('manifest', type=Path)
    parser.add_argument('--stage19-profile-dir', type=Path)
    parser.add_argument('--stage18-profile-dir', type=Path)
    parser.add_argument('--reference', type=Path, action='append', help='Stage 20 native results directory')
    parser.add_argument('--output-dir', type=Path)
    parser.add_argument('--audit-only', action='store_true')
    args = parser.parse_args()
    conversion = (args.stage19_profile_dir, args.stage18_profile_dir, args.reference, args.output_dir)
    if args.audit_only and any(conversion) or not args.audit_only and not all(conversion):
        parser.error('use --audit-only or supply both frozen profile directories, --reference and --output-dir')
    try:
        audit = inspect_source(args.source_zip, args.manifest)
        if args.audit_only:
            print(json.dumps(audit, indent=2, sort_keys=True))
            return
        references = verify_native_references(args.reference, audit)
        documents = {}
        for mode, directory in (('default', args.stage19_profile_dir), ('mode2', args.stage18_profile_dir)):
            fighter, binding = build_profile(audit, *load_frozen_base(directory, mode, audit), mode)
            documents.update({f'{mode}.fighter.json': fighter, f'{mode}.binding.json': binding,
                              f'{mode}.source.json': {**audit, 'mode': mode, 'frozenBaseSha256': FROZEN_BASE[mode],
                                                     'references': references,
                                                     'importsState1000': mode == 'default'}})
        state200.output_documents(args.output_dir, documents)
    except (AssertionError, ValueError, OSError, TypeError, KeyError, IndexError, zipfile.BadZipFile) as error:
        parser.exit(2, f'Stage 20 Power Wave route conversion: {error}\n')


if __name__ == '__main__':
    main()
