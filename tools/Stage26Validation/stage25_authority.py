"""Replay the accepted Stage 25 fixed authority pairs with the current build."""

import argparse
import json
from pathlib import Path
from subprocess import TimeoutExpired
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'Stage25Validation'))
from stage24_authority import ROOT, candidate, require_same, rows, shared_wave_rows


GROUPS = {
    'key': ('hit', 'crouch-hit', 'stand-guard', 'crouch-guard', 'mirror-hit', 'interrupt'),
    'controls': ('whiff', 'p2-hit', 'sweep-aged-a', 'both-kicks', 'forward-down', 'back-down',
                 'light-punch-compete', 'heavy-punch-compete', 'down-forward-heavy-compete',
                 'to-kick-before-direction', 'counter-sweep', 'air-entry'),
    'falling': ('airborne-fall-locked', 'airborne-fall-overlap'),
    'chain': ('far-jab-to-kick-9', 'far-jab-to-kick-12', 'far-jab-to-kick-16', 'crouch-jab-to-kick-11',
              'crouch-jab-to-kick-17', 'to-kick-11', 'to-kick-18', 'to-crouch-jab-11', 'to-far-jab-11'),
    'grounded-final': ('hit-to-near-jab-22', 'guard-to-kick-22', 'guard-release-back-22',
                       'guard-jab-release-back-22', 'sweep-release-down'),
    'final-air-probe': ('to-close-high-target-jab-11',),
    'wave-final': ('wave-9', 'wave-18', 'wave-19', 'wave-20', 'hit-wave-10', 'wave-17-to-down',
                   'wave-forward-neutral-boundary', 'near-jab-to-kick-hit'),
    'wave-control': ('wave-forward-held-through-exit', 'through-400-wave'),
    'postfix-wave-guard': ('hit-wave-30', 'guard-release-down-22'),
    'final-completed-wave': ('wave-mixed-at-exit', 'wave-crouch-fallback', 'wave-startup-7'),
    'final-motion-through-recovery': ('wave-extra-forward-before-button', 'wave-df-hold-before-button',
                                      'wave-db-on-button', 'wave-back-on-button'),
    'mode2': ('mode-body-guard', 'mode-chain', 'wave-13-mode'),
    'final-mode2': ('wave-19-mode',),
}
TOTAL = (58, 6116, 56271)
CURRENT_PROFILE_CASES = (
    ('NewKOF-stage25-verified/grounded-final/guard-to-kick-22', 'TerryCrouchLightKick'),
    ('NewKOF-stage25-verified/final-completed-wave/wave-mixed-at-exit', 'TerryCrouchLightKick'),
    ('NewKOF-stage25-verified/postfix-wave-guard/hit-wave-30', 'TerryCrouchLightKick'),
    ('NewKOF-stage24-vulkan-final-priority/tech-split-56', 'TerrySweepWave'),
    ('NewKOF-stage24-vulkan-routes-final/incoming-wave-34', 'TerrySweepWave'),
)


def replay(options):
    cases = frames = records = 0
    for group in options.group or GROUPS:
        for scene in GROUPS[group]:
            fixture = options.archive / group / scene
            reference = fixture / 'sprite/duel.log'
            length, expected = rows(reference, reference.read_bytes())
            for variant in ('sprite', 'plain'):
                saved = fixture / variant / 'duel.log'
                require_same(expected, rows(saved, saved.read_bytes())[1], f'{group}/{scene} archived {variant}')
                if options.godot:
                    target = options.output / group / scene / variant
                    log, actual = candidate(options, saved, target)
                    require_same(expected, rows(log, actual)[1], f'{group}/{scene} replayed {variant}')
            cases += 1
            frames += length
            records += len(expected)
            print(f'{group}/{scene}: {length} logic frames, {len(expected)} rows', flush=True)
    if not options.group and (cases, frames, records) != TOTAL:
        raise ValueError(f'incomplete Stage 25 evidence: {cases} / {frames} / {records}')
    print(f'Matched {cases} Stage 25 pairs, {frames} logic frames and {records} authority rows.', flush=True)


def replay_current_profile(options):
    profile = Path(options.current_profile) / 'default.fighter.json'
    fighter_id = json.loads((ROOT / profile).read_bytes())['fighterId']
    frames = shared_count = current_count = 0
    for directory, original_id in CURRENT_PROFILE_CASES:
        fixture = options.archive / directory
        reference = fixture / 'sprite/duel.log'
        length, expected = rows(reference, reference.read_bytes())
        expected_shared = shared_wave_rows(expected, original_id, wave_audited=True)
        actual = []
        for variant in ('sprite', 'plain'):
            saved = fixture / variant / 'duel.log'
            require_same(expected, rows(saved, saved.read_bytes())[1], f'{directory} frozen {variant}')
            log, data = candidate(options, saved, options.output / fixture.name / variant, profile=profile.as_posix())
            _, records = rows(log, data)
            require_same(expected_shared, shared_wave_rows(records, fighter_id, wave_audited=True),
                         f'{directory} shared authority {variant}')
            actual.append(records)
        require_same(*actual, f'{directory} Stage 26 visual/plain authority')
        frames += length
        shared_count += len(expected_shared)
        current_count += len(actual[0])
        print(f'{fixture.name}: {length} logic frames, {len(expected_shared)} frozen and {len(actual[0])} current rows',
              flush=True)
    print(f'Matched {len(CURRENT_PROFILE_CASES)} Stage 26 compatibility pairs, {frames} logic frames, '
          f'{shared_count} frozen and {current_count} current authority rows.', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument('--archive-only', action='store_true')
    mode.add_argument('--godot', type=Path)
    parser.add_argument('--archive', type=Path, required=True)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--group', choices=GROUPS, action='append')
    parser.add_argument('--resolution', choices=('320x180',), default='320x180')
    parser.add_argument('--current-profile', help='replay 430, 440, ground tech and Wave eligibility with this profile')
    parser.add_argument('--visual-alias', default='terry98-stage26-default')
    options = parser.parse_args()
    if options.godot and options.output is None:
        parser.error('--godot requires --output')
    if options.current_profile and (not options.godot or options.group):
        parser.error('--current-profile requires --godot without --group')
    try:
        if options.godot:
            options.output = options.output.resolve()
            options.output.mkdir(parents=True, exist_ok=False)
        if options.current_profile:
            replay_current_profile(options)
        else:
            replay(options)
    except (OSError, ValueError, TimeoutExpired) as error:
        parser.exit(1, f'Stage 25 authority replay failed: {error}\n')


if __name__ == '__main__':
    main()
