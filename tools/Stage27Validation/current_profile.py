"""Replay accepted old moves with the new Stage 27 fighter configuration."""

import argparse
import json
from pathlib import Path
import subprocess

from frozen_replay import (ROOT, archived_variant, build_hashes, crouch_light_history, sweep_history)
from standing_heavy_kick_duel import NATIVE
from power_wave_duel import MOVE, audit_rows, battle_events


WAVE_CASES = {
    ('old-wave-final', 'coexistence-sources200-205/mixed-sequential'): (205, 200),
    ('resume-old-wave', 'coexistence-source215/hit-22'): (215,),
    ('resume-old-wave', 'coexistence-source400-final/hit-held-11'): (400,),
    ('resume-old-wave', 'coexistence-source410/hit-22'): (410,),
    ('resume-old-wave', 'routes-final/whiff-13'): (440,),
    ('verified', 'wave-extended/near-wave-whiff-9'): (235,),
    ('verified', 'wave-extended/near-wave-whiff-13'): (235,),
}
OTHER_CASES = {
    ('current-legacy', Path(folder).name) for folder, _ in crouch_light_history.CURRENT_PROFILE_CASES
} | {('verified', 'final-430-last/near-to-crouch-kick-grounded-last')}


def verify_waves(data, sources, case, sprites):
    lines = data.decode('utf-8').splitlines()
    moves = [event for event in battle_events(lines) if event['kind'] == 'MoveStarted']
    expected = [move for state in sources for move in (NATIVE[state], MOVE)]
    sweep_history.require_same(expected, (event['move'] for event in moves), 'old Wave source sequence')
    entities = audit_rows(lines, 'WaveEntityAudit')
    if len({row['id'] for row in entities}) != len(sources) or not audit_rows(lines, 'WaveRestrictionAudit'):
        raise ValueError(f'expected {len(sources)} real Wave entities and pursuit audit')
    if sprites != any(row['sprite_selected'] == 'True' for row in audit_rows(lines, 'SpriteRenderAudit')):
        raise ValueError('old Wave visual overlay does not match the requested variant')
    if sources == (235,):
        started = moves[-1]
        candidates = audit_rows(lines, 'CommandAudit')
        command, = [row for row in candidates if row['logic'] == str(started['frame'])
                    and row['fighter'] == str(started['source'])]
        expected = 5 if case.name == 'near-wave-whiff-9' else 8
        sweep_history.require_same((NATIVE[235], expected), (command['old'], int(command['tick']) + 1),
                                   '235 authored Wave edge')


def replay(options):
    fighter = Path(options.profile) / 'default.fighter.json'
    fighter_id = json.loads((ROOT / fighter).read_bytes())['fighterId']
    reference_id = json.loads((ROOT / 'data/imported-characters/terry98-stage26-final/default.fighter.json')
                              .read_bytes())['fighterId']
    scene_list = (*WAVE_CASES, *sorted(OTHER_CASES))
    total_frames = shared_records = current_records = 0
    for batch, name in scene_list:
        scene = Path(name)
        archive = options.archive / f'NewKOF-stage26-{batch}'
        expected = None
        actual = []
        shared_count = 0
        for variant in ('sprite', 'plain'):
            log, original = archived_variant((archive,), scene, variant)
            length, stored = sweep_history.rows(log, original)
            if expected is None:
                expected = stored
            else:
                sweep_history.require_same(expected, stored, f'{scene} archived {variant}')
            wave_audited = any(row.startswith(sweep_history.WAVE_RESTRICTION_AUTHORITY) for row in stored)
            shared = sweep_history.shared_wave_rows(stored, reference_id, wave_audited=wave_audited)
            target = options.output / batch / scene / variant
            output, data = sweep_history.candidate(options, log, target, profile=fighter.as_posix())
            _, current = sweep_history.rows(output, data)
            if sources := WAVE_CASES.get((batch, name)):
                verify_waves(data, sources, scene, sprites=variant == 'sprite')
            sweep_history.require_same(shared, sweep_history.shared_wave_rows(current, fighter_id,
                                                                                wave_audited=wave_audited),
                                       f'{scene} shared Stage 27 authority {variant}')
            shared_count = len(shared)
            actual.append(current)
        sweep_history.require_same(*actual, f'{scene} Stage 27 sprite/plain authority')
        total_frames += length
        shared_records += shared_count
        current_records += len(actual[0])
        print(f'{scene.as_posix()}: {length} logic frames, {shared_count} shared rows, '
              f'{len(actual[0])} current authority rows', flush=True)
    print(f'Matched {len(scene_list)} Stage 27 compatibility pairs, {total_frames} frames, '
          f'{shared_records} shared rows and {current_records} current authority rows; '
          f'{len({state for source in WAVE_CASES.values() for state in source})} old Wave sources', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--godot', type=Path, required=True)
    parser.add_argument('--archive', type=Path, default=Path('D:/'))
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--profile', default='data/imported-characters/terry98-stage27-final')
    parser.add_argument('--visual-alias', default='terry98-stage27-default')
    options = parser.parse_args()
    options.resolution = '320x180'
    try:
        candidate = build_hashes()
        options.output.mkdir(parents=True, exist_ok=False)
        replay(options)
        if build_hashes() != candidate:
            raise ValueError('candidate Godot Debug build changed during the replay')
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        parser.exit(1, f'Stage 27 compatibility replay failed: {error}\n')


if __name__ == '__main__':
    main()
