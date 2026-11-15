"""Replay Stage 29 pursuit and guard controls with the final Stage 30 build."""

import argparse
from functools import partial
from hashlib import file_digest
import json
from pathlib import Path
import subprocess
import sys

TOOLS = Path(__file__).resolve().parents[1]
sys.path[:0] = [str(TOOLS / name) for name in ('Stage30Validation', 'Stage29Validation', 'Stage27Validation')]
from frozen_replay import ROOT, archived_variant, build_hashes, replay_headless, sweep_history
from ground_blowback_duel import MOVES
from standing_light_kick_duel import AUTHORITY, EXTRA_AUTHORITY, assert_equal, require_same
from rising_upper_duel import PROFILE, candidate_identity
from current_profile import replay
from wave_coexistence import compare_shared


STAGE29_PROFILE = 'data/imported-characters/terry98-stage29-final'
WAVE = MOVES[1000]
KNUCKLE = MOVES[300]
CD = MOVES[250]
CASES = (
    ('directed', '250-hit-wave-24', 'default', (CD, WAVE), ('Hit',)),
    ('directed', '250-counter-hit-wave', 'default', (CD, MOVES[215], WAVE), ('Hit', 'ProjectileHit')),
    ('directed', '250-hit-cancel-24', 'default', (CD, KNUCKLE), ('Hit',)),
    ('directed-recheck', '250-guard-300-wave', 'default', (CD, KNUCKLE, WAVE),
     ('Blocked', 'Blocked', 'Blocked', 'ProjectileBlocked')),
    ('input', '250-cd-plus-wave', 'default', (WAVE,), ()),
    ('routes', '250-wave-25', 'default', (CD, WAVE), ()),
    ('mode2-window', '250-wave-24', 'mode2', (CD,), ()),
)


def authority(log):
    rows = [line for line in log.splitlines() if line.startswith((*AUTHORITY, *EXTRA_AUTHORITY))]
    if not rows:
        raise ValueError('empty Stage 29 authority log')
    return rows


def branch(scene, rows, expected_moves, expected_contacts):
    events = sweep_history.battle_events(rows.decode().splitlines())
    assert_equal(list(expected_moves), [event['move'] for event in events if event['kind'] == 'MoveStarted'],
                 f'{scene}: prior MoveStarted sequence')
    contacts = {'Hit', 'Blocked', 'ProjectileHit', 'ProjectileBlocked'}
    assert_equal(list(expected_contacts), [event['kind'] for event in events if event['kind'] in contacts],
                 f'{scene}: prior contact sequence')


def profile_rows(lines, fighter_id, old_id):
    before, after = f' id={fighter_id} '.encode(), f' id={old_id} '.encode()
    return [line.replace(before, after, 1) if line.startswith(b'[MoveAudit] ') else line for line in lines]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--godot', required=True, type=Path)
    parser.add_argument('--archive', type=Path, default=Path('D:/'))
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--profile', default=PROFILE)
    parser.add_argument('--case', choices=tuple(row[1] for row in CASES), action='append',
                        help='Select individual Stage 29 controls for a targeted rerun')
    scope = parser.add_mutually_exclusive_group()
    scope.add_argument('--frozen-only', action='store_true', help='Replay the Stage 29 profiles headlessly only')
    scope.add_argument('--old-wave-only', action='store_true',
                       help='Replay earlier Wave, light-link and sweep controls on the current profile')
    options = parser.parse_args()
    if options.old_wave_only and options.case:
        parser.error('--case is for the Stage 29 controls')
    options.resolution = '320x180'
    options.profile_dir = options.profile
    options.output = options.output.resolve()
    options.output.mkdir(parents=True, exist_ok=False)
    build = build_hashes()
    identified = candidate_identity(options)
    (options.output / 'candidate.json').write_text(json.dumps(identified, indent=2) + '\n', encoding='utf-8')
    print(f'Stage 29 regression with Stage 30 candidate {build}', flush=True)
    if options.old_wave_only:
        old_source = json.loads((ROOT / STAGE29_PROFILE / 'default.source.json').read_bytes())
        fighter = json.loads((ROOT / options.profile / 'default.fighter.json').read_bytes())
        boxes = old_source['launchAirHurtboxes']
        assert_equal(boxes, fighter['groundHurtboxes']['launchAir'], 'frozen authored launch boxes')
        options.visual_alias = 'terry98-stage30-default'
        replay(options, compare_shared=partial(compare_shared, boxes), stage_label='Stage 30')
        assert_equal(identified, candidate_identity(options), 'candidate remained stable during old Wave replay')
        return
    total = 0
    cases = tuple(row for row in CASES if not options.case or row[1] in options.case)
    for batch, name, mode, entries, contacts in cases:
        frozen = []
        current = []
        profile = Path(options.profile) / f'{mode}.fighter.json'
        fighter_id = json.loads((ROOT / profile).read_bytes())['fighterId']
        old_id = json.loads((ROOT / STAGE29_PROFILE / f'{mode}.fighter.json').read_bytes())['fighterId']
        options.visual_alias = f'terry98-stage30-{mode}'
        archive = options.archive / f'NewKOF-stage29-delivery-{batch}'
        archived_identity = json.loads((archive / 'candidate.json').read_bytes())
        for kind in ('fighter', 'binding'):
            relative = f'{STAGE29_PROFILE}/{mode}.{kind}.json'
            with (ROOT / relative).open('rb') as source:
                assert_equal(archived_identity[relative], file_digest(source, 'sha256').hexdigest(),
                             f'{name}: frozen {kind} identity')
        for variant in ('sprite', 'plain'):
            saved, archived = archived_variant((archive,), Path(name), variant)
            frozen_result, replayed = replay_headless(options, saved, archived,
                                                      options.output / 'stage29-headless' / name / variant)
            branch(name, replayed, entries, contacts)
            frozen.append(authority(replayed))
            require_same(authority(archived), frozen[-1], f'{name} frozen authority at {frozen_result}')
            if options.frozen_only:
                continue
            target = options.output / 'stage30-vulkan' / name / variant
            current_result, replayed = sweep_history.candidate(options, saved, target, profile=profile.as_posix())
            branch(name, replayed, entries, contacts)
            current.append(authority(replayed))
            require_same(frozen[-1], profile_rows(current[-1], fighter_id, old_id),
                         f'{name} Stage 30 profile authority at {current_result}')
        require_same(*frozen, f'{name} Stage 29 frozen visual/plain authority')
        if current:
            require_same(*current, f'{name} Stage 30 current visual/plain authority')
        assert_equal(build, build_hashes(), 'Debug build remained stable during Stage 29 replay')
        total += len(frozen[0])
        print(f'{name}: matched {len(frozen[0])} rows in each authority variant', flush=True)
    assert_equal(identified, candidate_identity(options), 'Stage 30 profiles remained stable during replay')
    print(f'Matched {len(cases)} Stage 29 pairs, {total} authority rows, '
          f'{"Stage 29 frozen profiles" if options.frozen_only else "Stage 29 frozen and Stage 30 current profiles"}',
          flush=True)


if __name__ == '__main__':
    try:
        main()
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        raise SystemExit(str(error)) from error
