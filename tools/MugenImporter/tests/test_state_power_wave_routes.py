import copy
import json
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import patch

from mugen_importer.state_power_wave import MOVE_ID
from mugen_importer.state_power_wave_routes import (PRESETS, build_profile, cancel_window,
                                                     verify_native_references, verify_reentry_order)


NEAR = 'TerryStandLightPunch205'
FAR = 'TerryStandLightPunch200'
AUDIT = {'cancelWindow': [4, 6], 'nearBodyDistance': 16, 'phaseDurations': [3, 4, 5]}


def profile(mode):
    return ({'fighterId': 'TerryPowerWave' if mode == 'default' else 'TerryReentryMode2',
             'displayName': 'Terry', 'commands': [{'commandId': '5A'}],
             'moves': [{'moveId': NEAR, 'commandId': '5A', 'maximumStartBodyDistance': {'numerator': 16, 'denominator': 1},
                        'startupFrames': 3, 'activeFrames': 4, 'recoveryFrames': 5,
                        'cancels': [{'targetMoveId': target, 'windowFirstFrame': 7, 'windowLastFrame': 11}
                                    for target in (NEAR, FAR)]},
                       {'moveId': FAR, 'commandId': '5A', 'cancels': []}] +
                      ([{'moveId': MOVE_ID, 'commandId': '236A', 'projectiles': [{}]}] if mode == 'default' else [])},
            {'visualId': 'terry98-stage19-default' if mode == 'default' else 'terry98-stage18-mode2',
             'clips': {'state205': {'action': 205}}})


class StatePowerWaveRouteTests(unittest.TestCase):
    def test_strict_anim_elem_boundaries_apply_to_the_actual_near_action(self):
        condition = 'StateNo = 205 && AnimElemTime(2) > 0 && AnimElemTime(3) < 0'
        self.assertEqual([4, 6], cancel_window(condition, [3, 4, 5]))
        self.assertEqual([3, 4], cancel_window(condition, [2, 3, 8]))
        with self.assertRaisesRegex(ValueError, 'State 205'):
            cancel_window(condition.replace('> 0', '>= 0'), [3, 4, 5])
        with self.assertRaisesRegex(ValueError, 'AIR'):
            cancel_window(condition.replace('(3)', '(4)'), [3, 4])

    def test_default_adds_only_205_wave_and_keeps_near_far_fallback_order(self):
        base = profile('default')
        original = copy.deepcopy(base)
        converted, binding = build_profile(AUDIT, *base, 'default')
        self.assertEqual(base, original)
        self.assertEqual((converted['fighterId'], binding['visualId']), PRESETS['default'])
        self.assertEqual(converted['moves'][1:], base[0]['moves'][1:])
        self.assertEqual(converted['moves'][0]['cancels'][1:], base[0]['moves'][0]['cancels'])
        self.assertEqual(converted['moves'][0]['cancels'][0], {
            'targetMoveId': MOVE_ID, 'windowFirstFrame': 4, 'windowLastFrame': 6, 'phases': 'Active',
            'requiresHitConfirm': False, 'requiresBlockConfirm': False, 'allowWhiff': True,
        })
        self.assertEqual(binding['clips'], base[1]['clips'])

    def test_mode2_is_a_control_with_no_state1000_alias(self):
        base = profile('mode2')
        original = copy.deepcopy(base)
        converted, binding = build_profile(AUDIT, *base, 'mode2')
        self.assertEqual(base, original)
        self.assertEqual((converted['fighterId'], binding['visualId']), PRESETS['mode2'])
        self.assertEqual(converted['moves'], base[0]['moves'])
        self.assertEqual(converted['commands'], base[0]['commands'])

    def test_rejects_misordered_or_mutated_fallback(self):
        fighter, _ = profile('default')
        fighter['moves'][0]['cancels'].reverse()
        with self.assertRaisesRegex(ValueError, 're-entry order'):
            verify_reentry_order(fighter, 16)

        fighter, binding = profile('default')
        fighter['moves'][0]['recoveryFrames'] = 6
        with self.assertRaisesRegex(ValueError, 'source phases'):
            build_profile(AUDIT, fighter, binding, 'default')

    def test_reference_requires_expected_source_and_verified_cases(self):
        source = {'Terry.air': 'air-digest', 'Terry.cmd': 'cmd-digest', 'Terry-N.cns': 'normal-digest',
                  'Constants.cns': 'constants-digest', 'Terry-S.cns': 'special-digest',
                  'Terry-2-3.cns': 'global-digest', 'Helper.cns': 'helper-digest'}
        audit = {'zipSha256': 'archive-digest', 'members': {name: {'name': name, 'sha256': sha}
                                                         for name, sha in source.items()}}
        with TemporaryDirectory() as temporary:
            directory = Path(temporary)
            metadata = directory / 'run-metadata.json'
            fields = {'terry_archive_sha256': 'archive-digest', 'source_sha256': source,
                      'executed_cases': ['case']}
            metadata.write_text(json.dumps(fields), encoding='utf-8')
            def check(cases):
                self.assertEqual({'case': 'verified trace'}, cases)

            with patch('mugen_importer.state_power_wave_routes.runpy.run_path', return_value={
                    'read_cases': lambda _: [('case', 'verified trace')], 'verify_cases': check}):
                evidence, = verify_native_references([directory], audit)
                self.assertEqual(['case'], evidence['cases'])
                self.assertEqual(64, len(evidence['metadataSha256']))
                legacy_hashes = {name: sha for name, sha in source.items() if name != 'Constants.cns'}
                metadata.write_text(json.dumps({**fields, 'source_sha256': legacy_hashes}), encoding='utf-8')
                verify_native_references([directory], audit)
                metadata.write_text(json.dumps({**fields, 'source_sha256': {'Terry.air': source['Terry.air']}}),
                                    encoding='utf-8')
                with self.assertRaisesRegex(ValueError, 'native reference source mismatch'):
                    verify_native_references([directory], audit)
                metadata.write_text(json.dumps({**fields, 'source_sha256': {**source, 'Constants.cns': 'wrong-digest'}}),
                                    encoding='utf-8')
                with self.assertRaisesRegex(ValueError, 'native reference source mismatch'):
                    verify_native_references([directory], audit)
