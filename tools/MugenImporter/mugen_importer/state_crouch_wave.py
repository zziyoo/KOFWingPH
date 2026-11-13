"""Convert Terry's reviewed crouching light and optional weak Wave route."""

import argparse
import copy
import json
from pathlib import Path
import zipfile

from . import state200, state210, state_power_wave as wave, state_power_wave_heavy as heavy, state_power_wave_routes as routes
from .air import parse_air
from .state200 import normalized, number_in, require, unique
from .state205215 import action_profile, move_scope


MOVE = 'TerryCrouchLightPunch400'
PRESETS = {'default': ('TerryCrouchWave', 'terry98-stage22-default'),
           'mode2': ('TerryCrouchWaveMode2', 'terry98-stage22-mode2')}
BASE = {
    'default': dict(zip(('fighter', 'binding', 'source'), (
        'ff11c27509c7e86a40561b2b4cdf064ff95a9f90cf3fe6757e1581f3bcba0630',
        '9e077368bc1fbdf0b9ac829d8895e3c02fe03dea7772ab954c09a9b126b88bef',
        'eb65c9615596bfdd7073344d7938e85a56935c88cab6473a431d139f117da29a'), strict=True)),
    'mode2': dict(zip(('fighter', 'binding', 'source'), (
        '414c53f0f6081b1260b4e723418f55fc7b1a06fb063c677bfcc41fb0d3b69c6a',
        '8577ba2b419a1b9a42dbced8244267886226435438a4d62637d01f68ead8c3df',
        '41eb802ac496f593542c0d5c0729a566077953126527d3faef30edad5aebf0ce'), strict=True)),
}


def inspect_source(archive, manifest):
    audit = heavy.inspect_source(archive, manifest)
    members = audit['members']
    with zipfile.ZipFile(archive) as source:
        contents = {key: state200.archive_member(source, members[key]['name'])
                    for key in ('anim', 'cmd', 'st1', 'stcommon', 'Terry-2-3.cns')}
    require(all(state200.digest(raw) == members[key]['sha256'] for key, raw in contents.items()), 'source archive changed')
    definition, controls = move_scope(state200.read_sections(contents['st1'].decode('latin-1')), 400)
    require(all(normalized(definition.one(k)) == v for k, v in
                {'type': 'c', 'movetype': 'a', 'physics': 'n', 'ctrl': '0', 'velset': '0,0', 'anim': '400', 'facep2': '1'}.items()),
            'unsupported State 400 definition')
    attacks = [c for c in controls if normalized(c.one('type')) == 'hitdef']
    hit = unique((c for c in attacks if normalized(c.one('attr')) == 'c,na'), 'State 400 HitDef')
    require(normalized(hit.one('guardflag')) == 'm' and normalized(hit.one('hitflag')) == 'maf', 'unsupported State 400 contact')
    require(normalized(hit.one('animtype')) == 'light' and normalized(hit.one('ground.type')) == 'low',
            'unsupported State 400 ground reaction')
    bounds = number_in(unique((x for x in hit.values('trigger1') if 'animelemtime' in normalized(x)), 'HitDef window'),
                       r'animelemtime\((\d+)\)>=0&&animelemtime\((\d+)\)<0', 'State 400 HitDef')
    damage = number_in(hit.one('damage'), r'floor\(\(ifelse\(var\(6\),(\d+),(\d+)\)-var\(21\)\)\*fvar\(7\)\),0',
                       'State 400 dynamic damage')
    exit_state = unique((c for c in controls if normalized(c.one('type')) == 'changestate'), 'State 400 exit')
    require((normalized(exit_state.one('trigger1')), exit_state.one('value'), exit_state.one('ctrl')) == ('animtime=0','11','1'),
            'unsupported State 400 exit')
    actions, diagnostics = parse_air(contents['anim'].decode('latin-1'))
    action, durations = wave.source_action(actions, 400)
    require(not any('duplicate action 400 ' in d for d in diagnostics), 'duplicate AIR Action 400')
    sprites = state200.validate_manifest(manifest, state200.manifest_hashes(audit['zipSha256'], members), action)
    rise, rise_durations = wave.source_action(actions, 12)
    state200.validate_manifest(manifest, state200.manifest_hashes(audit['zipSha256'], members), rise)
    shake, shake_durations = wave.source_action(actions, 5010)
    slide, slide_durations = wave.source_action(actions, 5015, hold_last=True)
    require(slide_durations == [-1], 'State 400 light slide must hold one pose')
    _, rise_controls = move_scope(state200.read_sections(contents['stcommon'].decode('latin-1')), 12)
    rise_exit = unique((c for c in rise_controls if normalized(c.one('type')) == 'changestate'), 'State 12 exit')
    require((normalized(rise_exit.one('trigger1')), rise_exit.one('value')) == ('animtime=0', '0'), 'unsupported State 12 exit')
    reaction = {'durations': [*shake_durations, 1], 'clsn2': [
        *state200.collision_boxes(shake, 2, normalize_edges=True),
        *state200.collision_boxes(slide, 2, normalize_edges=True)]}
    timeline = action_profile(action, [bounds])
    _, negative = move_scope(state200.read_sections(contents['Terry-2-3.cns'].decode('latin-1')), -2)
    permission = unique((c for c in negative if c.one('var(1)', '') == '1' and
                         normalized(c.one('trigger6', '')).startswith('stateno=400&&')), '400 cancel permission')
    require(normalized(permission.one('type')) == 'varset' and normalized(permission.one('triggerall')) == '!ishelper',
            'unsupported 400 cancel permission')
    _, entries = move_scope(state200.read_sections(contents['cmd'].decode('latin-1')), -1)
    entry = unique((c for c in entries if c.one('value', '') == '400' and 'command="holddown"' in
                    map(normalized, c.values('triggerall'))), 'human State 400 command')
    require('command="x"||helper(99999),var(0)>0' in map(normalized, entry.values('triggerall')),
            'State 400 light edge differs')
    (light_delay,) = number_in(entry.one('trigger5'), r'stateno=400&&animelem=3,>=(\d+)', '400 light re-entry')
    return {**{key: audit[key] for key in ('zipSha256', 'manifestSha256', 'members')}, 'action': timeline,
            'spriteIndices': sprites, 'cancelWindow': routes.cancel_window(permission.one('trigger6'), durations, source_state=400),
            'crouchRise': {'durations': rise_durations, 'clsn2': state200.collision_boxes(rise, 2, normalize_edges=True)},
            'lightReaction': reaction,
            'damage': {'counterBase': damage[0], 'normalBase': damage[1], 'useContactDivisor': False},
            'lightWindowStart': sum(durations[:2]) + light_delay,
            'guardThreatFrames': wave.guard_threat(controls, durations, 400),
            'sourceLines': {'state': definition.line, 'hitdef': hit.line, 'command': entry.line, 'exit': exit_state.line}}


def build_profile(audit, native, fighter, binding, mode, *, with_cancel=True):
    fighter, binding = copy.deepcopy((fighter, binding))
    fighter['fighterId'], binding['visualId'] = PRESETS[mode]
    fighter['displayName'] = f'Terry Crouching Light ({mode})'
    action = audit['action']
    retention = fighter['movement']['groundReaction']['velocityRetention']
    attack = state210.attack_fields({'strike': action['strikes'][0]['box'], 'active': action['active']},
                                    native['hit'], native['guard'], retention)
    attack.update(damage=0, dynamicDamage=audit['damage'], lightGroundReaction=True)
    fighter['groundHurtboxes']['crouchRise'] = state210.hurtbox_timeline(audit['crouchRise'])
    reaction = copy.deepcopy(audit['lightReaction'])
    # The impact pose is already committed during freeze and is held on the first slide tick.
    reaction['durations'][0] += 1
    reaction['durations'][-1] = native['hit']['effectiveStun'] + 1 - sum(reaction['durations'][:-1])
    require(reaction['durations'][-1] > 0, 'State 400 reaction must reach the held recovery pose')
    fighter['groundHurtboxes']['lightHitstun'] = state210.hurtbox_timeline(reaction)
    crouch = {'moveId': MOVE, 'commandId': '5A', 'allowedFrom': 'Crouching', 'returnsToCrouch': True,
              'startupFrames': action['startup'], 'activeFrames': action['active'], 'recoveryFrames': action['recovery'],
              'attacks': [attack], 'hurtboxTimeline': state210.hurtbox_timeline(action),
              'guardThreatFrames': audit['guardThreatFrames'], 'cancels': [], 'throws': [], 'projectiles': []}
    fighter['moves'].append(crouch)
    binding['clips']['state400'] = {'action': 400, 'clock': 'move', 'loop': False}
    binding['clips']['crouch_rise'] = {'action': 12, 'loop': False}
    binding['moves'][MOVE] = {phase: 'state400' for phase in ('Startup','Active','Recovery')}
    if not with_cancel or mode != 'default':
        return fighter, binding
    require(native['cancelVerified'] and native['lightWindowStart'] == audit['lightWindowStart'],
            'State 400 requires verified native cancel and light-fallback windows')
    first, last = audit['cancelWindow']
    confirmation = {'requiresHitConfirm': False, 'requiresBlockConfirm': False, 'allowWhiff': True}
    crouch['cancels'].append({'targetMoveId': wave.MOVE_ID, 'windowFirstFrame': first, 'windowLastFrame': last,
                              'phases': 'Active', **confirmation})
    # Native CMD uses the same light edge and the current held direction for all three targets.
    lights = [m for m in fighter['moves'] if m['moveId'] in ('TerryStandLightPunch205','TerryStandLightPunch200')]
    for move in lights:
        move['cancels'].append({'targetMoveId': MOVE, 'windowFirstFrame': 7, 'windowLastFrame': 11,
                                'phases': 'Active,Recovery', **confirmation, 'maximumInputAge': 2})
    for target in (MOVE, *(m['moveId'] for m in lights)):
        crouch['cancels'].append({'targetMoveId': target, 'windowFirstFrame': native['lightWindowStart'],
                                  'windowLastFrame': sum(action[p] for p in ('startup','active','recovery')) - 1,
                                  'phases': 'Recovery', **confirmation, 'maximumInputAge': 2,
                                  'allowSameMove': target == MOVE, 'standingInputOnly': target != MOVE})
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
        references, native = routes.verify_native_references(args.reference, audit, reference_stage=22, include_contract=True)
        docs = {}
        for mode in PRESETS:
            base = routes.load_frozen_base(args.base, mode, audit, frozen_base=BASE)
            fighter, binding = build_profile(audit, native, *base, mode, with_cancel=not args.body_only)
            docs.update({f'{mode}.fighter.json': fighter, f'{mode}.binding.json': binding,
                         f'{mode}.source.json': {**audit, 'native': native, 'references': references, 'mode': mode,
                                                'bodyOnly': args.body_only}})
        state200.output_documents(args.output, docs)
    except (AssertionError, OSError, ValueError, KeyError, TypeError) as error:
        parser.exit(2, f'Stage 22 conversion: {error}\n')


if __name__ == '__main__':
    main()
