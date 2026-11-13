"""Add the reviewed Terry State 215 cancel to frozen Stage 20 profiles."""

import argparse
import copy
import json
from pathlib import Path
import zipfile

from . import state200, state210, state_power_wave, state_power_wave_routes, state_reentry
from .air import parse_air
from .state200 import normalized, require, unique
from .state205215 import MOVE_IDS, action_profile, move_scope, source_move


PRESETS = {'default': ('TerryPowerWaveHeavyRoutes', 'terry98-stage21-default'),
           'mode2': ('TerryPowerWaveHeavyRoutesMode2', 'terry98-stage21-mode2')}
FROZEN_BASE = {
    'default': {
        'fighter': '16381b07cbff00c5ac7122a1dcc06e1e70b696d3d61ae09b5aa955309e465546',
        'binding': 'f824ef1a7f46f39f55073bf92a84f7c33e1f48689cb69c0f265d65f7629f18dc',
        'source': '296414f976a38fff0aa63f14c711b20f1c9e3e54fbccd9449804dea048f0c6a0',
    },
    'mode2': {
        'fighter': 'eb7578e3430deb7d8adf3cf8f3af2738465632d265a9cc9210c6f938398af0af',
        'binding': '680df7e5c3ec210b36a251483387e90bb0502a7abbbd3ee3288c798e03bf462e',
        'source': '1c113e1150474c82ff6483146a51b8aecc833abbeebde17639763092ac7f80b9',
    },
}


def inspect_source(source_zip, manifest):
    # Reuse the audited CMD targets, Helper, and State -2 context from the frozen routes.
    wave = state_power_wave_routes.inspect_source(source_zip, manifest)
    members = wave['members']
    keys = ('anim', 'st1', 'Terry-2-3.cns')
    with zipfile.ZipFile(source_zip) as archive:
        contents = {key: state200.archive_member(archive, members[key]['name']) for key in keys}
        common = state200.archive_member(archive, members['stcommon']['name'])
    require(all(state200.digest(contents[key]) == members[key]['sha256'] for key in keys),
            'source archive changed during inspection')
    require(state200.digest(common) == members['stcommon']['sha256'], 'source common CNS changed during inspection')
    actions, diagnostics = parse_air(contents['anim'].decode('latin-1'))
    action, durations = state_power_wave.source_action(actions, 215)
    require(not any('duplicate action 215 ' in message for message in diagnostics), 'duplicate AIR Action 215')
    sprites = state200.validate_manifest(manifest, state200.manifest_hashes(wave['zipSha256'], members), action)
    reaction, reaction_durations = state_power_wave.source_action(actions, 5007, hold_last=True)
    require(reaction_durations[-1:] == [-1] and
            not any('duplicate action 5007 ' in message for message in diagnostics),
            'Action 5007 must end in the reviewed held reaction pose')
    reaction_boxes = state200.collision_boxes(reaction, 2, normalize_edges=True)[-1]
    require(bool(reaction_boxes), 'Action 5007 final pose has no hurtboxes')
    reaction_profile = state210.hurtbox_timeline({'durations': [1], 'clsn2': [reaction_boxes]})[0]['boxes']
    _, guard_end = state_power_wave.source_action(actions, 140)
    _, crouch_guard_end = state_power_wave.source_action(actions, 141)
    _, hitstun = move_scope(state_reentry.script_sections(common), 5001)
    exit_protection = unique((part for part in hitstun if normalized(part.one('type')) == 'nothitby'),
                             'State 5001 HitOver protection')
    require(all(normalized(exit_protection.one(key)) == value for key, value in
                {'trigger1': 'hitover', 'value': 'sca', 'time': '1'}.items()),
            'unsupported State 5001 exit protection')
    bounds, heavy = source_move(state_reentry.script_sections(contents['st1']), 215)
    timeline = action_profile(action, bounds)
    phases = [timeline[phase] for phase in ('startup', 'active', 'recovery')]
    _, controls = move_scope(state_reentry.script_sections(contents['Terry-2-3.cns']), -2)
    permission = unique((control for control in controls if control.one('var(1)', '') == '1' and
                         normalized(control.one('trigger3', '')).startswith('stateno=215&&animelemtime')),
                        'State 215 cancel controller')
    require(normalized(permission.one('triggerall')) == '!ishelper' and
            normalized(permission.one('type')) == 'varset' and not permission.values('ignorehitpause'),
            'unsupported State 215 global cancel controller')
    return {**{key: wave[key] for key in ('zipSha256', 'manifestSha256', 'members', 'defaultWave', 'mode2Wave')},
            'action': 215, 'durations': durations, 'phaseDurations': phases, 'visuals': sprites,
            'lateGroundHitstun': {'action': 5007, 'element': len(reaction_durations), 'boxes': reaction_profile},
            'groundHitstunExitProtectionLine': exit_protection.line,
            'guardEndFrames': {'standing': sum(guard_end), 'crouching': sum(crouch_guard_end)},
            'cancelWindow': state_power_wave_routes.cancel_window(permission.one('trigger3'), durations, source_state=215),
            'hitDefWindows': [[sum(durations[:first - 1]), sum(durations[:last - 1]) - 1] for first, last in bounds],
            'sourceLines': {'state215': heavy['definitionLine'], 'moveHitReset': heavy['resetLine'],
                            'hitDefs': [hit['line'] for hit in heavy['hitDefs']], 'cancel': permission.line,
                            'defaultWaveEntry': wave['sourceLines']['defaultWaveEntry'],
                            'mode2WaveEntry': wave['sourceLines']['mode2WaveEntry']}}


def build_profile(audit, base_fighter, base_binding, mode):
    fighter, binding = copy.deepcopy((base_fighter, base_binding))
    source = unique((move for move in fighter['moves'] if move['moveId'] == MOVE_IDS[215]), 'State 215')
    require(source['commandId'] == '5B' and binding['clips']['state215']['action'] == 215,
            'frozen State 215 command or Action binding differs')
    require([hit['contactGroup'] for hit in source['attacks']] == [1, 2],
            'frozen State 215 must use two distinct contact groups')
    require(not source['cancels'], 'frozen State 215 has an existing cancel')
    first, last = audit['cancelWindow']
    startup, active, recovery = (source[f'{phase}Frames'] for phase in ('startup', 'active', 'recovery'))
    require([startup, active, recovery] == audit['phaseDurations'], 'State 215 source phases differ from frozen profile')
    require(startup <= first <= last < startup + active, 'State 215 cancel window is outside the frozen Active phase')
    fighter['fighterId'], binding['visualId'] = PRESETS[mode]
    if mode == 'default':
        fighter['displayName'] = 'Terry Weak Power Wave Light and Heavy Routes (default)'
        wave = unique((move for move in fighter['moves'] if move['moveId'] == state_power_wave.MOVE_ID), 'State 1000')
        require(wave['commandId'] == state_power_wave.COMMAND_ID and
                [projectile['projectileTypeId'] for projectile in wave['projectiles']] == [state_power_wave.ENTITY_ID],
                'frozen State 1000 profile lacks the original command or entity')
        source['cancels'].append({'targetMoveId': state_power_wave.MOVE_ID,
                                  'windowFirstFrame': first, 'windowLastFrame': last, 'phases': 'Active',
                                  'requiresHitConfirm': False, 'requiresBlockConfirm': False, 'allowWhiff': True})
    else:
        fighter['displayName'] = 'Terry Standing Normal Attacks (mode 2 control)'
        require(not any(move['moveId'] == state_power_wave.MOVE_ID for move in fighter['moves'])
                and not any(command['commandId'] == state_power_wave.COMMAND_ID for command in fighter['commands']),
                'State 1000 must not be bound in the mode 2 control')
    return fighter, binding


def apply_ground_reactions(fighter, audit, native):
    require(native['guardReleaseVerified'], 'Stage 21 ground guard needs both native release probes')
    # The contact samples select the held end pose and record native hitTime 4 through -1.
    fighter['groundHurtboxes']['hitstun'] = audit['lateGroundHitstun']['boxes']
    fighter['groundHurtboxes']['hitstunRecoveryFrames'] = native['lateHitstunFrames']
    fighter['retainProjectileGuard'] = True
    fighter['preserveGuardEntryMomentum'] = True
    fighter['projectileGuardRecoveryFrames'] = audit['guardEndFrames']['standing']
    fighter['protectGroundHitstunExit'] = True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source_zip', type=Path)
    parser.add_argument('manifest', type=Path)
    parser.add_argument('--stage20-profile-dir', type=Path)
    parser.add_argument('--reference', type=Path, action='append', help='Stage 21 native results directory')
    parser.add_argument('--output-dir', type=Path)
    parser.add_argument('--audit-only', action='store_true')
    args = parser.parse_args()
    conversion = (args.stage20_profile_dir, args.reference, args.output_dir)
    if args.audit_only and any(conversion) or not args.audit_only and not all(conversion):
        parser.error('use --audit-only or supply --stage20-profile-dir, --reference and --output-dir')
    try:
        audit = inspect_source(args.source_zip, args.manifest)
        if args.audit_only:
            print(json.dumps(audit, indent=2, sort_keys=True))
            return
        references, native = state_power_wave_routes.verify_native_references(
            args.reference, audit, reference_stage=21, include_contract=True)
        documents = {}
        for mode in PRESETS:
            base = state_power_wave_routes.load_frozen_base(args.stage20_profile_dir, mode, audit, frozen_base=FROZEN_BASE)
            fighter, binding = build_profile(audit, *base, mode)
            if mode == 'default':
                apply_ground_reactions(fighter, audit, native)
            documents.update({f'{mode}.fighter.json': fighter, f'{mode}.binding.json': binding,
                              f'{mode}.source.json': {**audit, 'mode': mode, 'frozenBaseSha256': FROZEN_BASE[mode],
                                                     'references': references, 'nativeContracts': native,
                                                     'importsHeavyCancel': mode == 'default'}})
        state200.output_documents(args.output_dir, documents)
    except (AssertionError, ValueError, OSError, TypeError, KeyError, IndexError, zipfile.BadZipFile) as error:
        parser.exit(2, f'Stage 21 Power Wave route conversion: {error}\n')


if __name__ == '__main__':
    main()
