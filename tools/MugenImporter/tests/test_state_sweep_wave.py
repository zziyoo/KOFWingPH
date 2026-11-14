import copy
import os
from pathlib import Path
import unittest

from mugen_importer import state_sweep_wave as sweep
from mugen_importer.state_power_wave import COMMAND_ID as WAVE_COMMAND, MOVE_ID as WAVE_MOVE


BOX = [24, -52, 95, -29]
ZERO = {'numerator': 0, 'denominator': 1}
X = {'numerator': 2, 'denominator': 1}
Y = {'numerator': 8, 'denominator': 1}
GRAVITY = {'numerator': 1, 'denominator': 2}
ATTACK_PAUSE = {'attackerFrames': 11, 'defenderFrames': 13}
GUARD_PAUSE = {'attackerFrames': 11, 'defenderFrames': 11}
AUDIT = {
    'action': {'id': 440, 'strikes': [{'box': BOX}], 'startup': 10, 'active': 8, 'recovery': 22,
               'durations': [2, 4, 4, 8, 8, 7, 7], 'clsn2': [[BOX]] * 7},
    'command': {'time': 1, 'bufferFrames': 2},
    'damage': {'normalBase': 16, 'counterBase': 18, 'useContactDivisor': False},
    'guardThreatFrames': 18,
    'cancelWindow': [11, 17],
    'groundDefaults': {'guardPush': {'numerator': 21, 'denominator': 2}, 'hitPause': (11, 13),
                       'guardPause': (11, 11), 'hitTime': 17, 'launchX': X, 'launchY': Y, 'gravity': GRAVITY},
    'trip': {'action': 5070, 'durations': [10, 4, 4, 4, -1], 'clsn2': [[BOX]] * 5},
    'techAction': 5200,
    'groundTech': {'inputBufferFrames': 5, 'durationFrames': 28, 'slideFrames': 16,
                   'initialSpeed': {'numerator': 8, 'denominator': 1},
                   'velocityRetention': {'numerator': 17, 'denominator': 20}},
    'launchReaction': {'impactFrames': 4, 'bounceHorizontalRetention': {'numerator': 3, 'denominator': 4},
                       'bounceVerticalVelocity': {'numerator': 3, 'denominator': 1}, 'bounceGravity': GRAVITY,
                       'lieDownFrames': 13, 'wakeupFrames': 25, 'transitionGravitySteps': 2, 'riseFrames': 8,
                       'lieImpactFrames': 3, 'startInFall': True, 'suppressOwnerProjectilesDuringFall': True},
}
NATIVE = {
    'hit': {'damage': 115, 'launchX': 2, 'launchY': 8, 'pause': ATTACK_PAUSE, 'fallTicks': 33},
    'guard': {'damage': 0, 'effectiveStun': 18, 'initialPush': 10.5, 'pause': GUARD_PAUSE},
    'cancelVerified': True,
    'groundTechVerified': True,
    'lightKickPriorityVerified': True,
    'tech': {'commandAgeFrames': 4, 'duration': 28, 'action': 5200, 'stopTick': 16},
    'trip': {'shakeAction': 5070, 'fallTicks': 33, 'impactFrames': 4, 'lieImpactFrames': 3,
             'lieDownFrames': 10, 'wakeupFrames': 25},
}


def baseline(mode):
    fighter = {
        'commands': [{'commandId': '5A', 'buttons': ['LightPunch'], 'priority': 1},
                     {'commandId': '5B', 'buttons': ['HeavyPunch'], 'priority': 0},
                     {'commandId': 'ReservedRisingUpper', 'buttons': ['HeavyPunch'], 'priority': 2,
                      'reserveGroundInput': True}],
        'useCommandPriority': True,
        'moves': [{'moveId': 'TerryCrouchHeavyPunch410', 'commandId': '5B', 'cancels': []}],
        'movement': {'groundReaction': {'velocityRetention': {'numerator': 17, 'denominator': 20}}},
        'groundHurtboxes': {'standing': []},
    }
    if mode == 'default':
        fighter['commands'].append({'commandId': WAVE_COMMAND, 'priority': 3})
        fighter['moves'].append({'moveId': WAVE_MOVE, 'commandId': WAVE_COMMAND,
                                 'projectiles': [{'projectileTypeId': 'TerryPowerWave1020', 'timeline': {}}]})
    return fighter, {'clips': {'idle': {'action': 0}}, 'moves': {}}


class StateSweepWaveTests(unittest.TestCase):
    def test_sweep_uses_distinct_heavy_kick_and_loses_to_frozen_punch_candidates(self):
        for mode in ('default', 'mode2'):
            with self.subTest(mode=mode):
                base = baseline(mode)
                original = copy.deepcopy(base)
                fighter, binding = sweep.build_profile(AUDIT, NATIVE, *base, mode, with_cancel=False)
                command = next(item for item in fighter['commands'] if item['commandId'] == '2D')
                self.assertEqual(['HeavyKick'], command['buttons'])
                self.assertLess(command['priority'], 0)
                self.assertEqual(2, command['bufferLifetimeFrames'])
                reserved = next(c for c in fighter['commands'] if c['commandId'] == 'ReservedCrouchLightKick')
                self.assertEqual((['LightKick'], ['Down', 'DownForward', 'DownBack'], True),
                                 (reserved['buttons'], reserved['directions'], reserved['reserveGroundInput']))
                self.assertLess(reserved['priority'], 0)
                self.assertGreater(reserved['priority'], command['priority'])
                standing = next(c for c in fighter['commands'] if c['commandId'] == 'ReservedStandHeavyKick')
                self.assertEqual((['HeavyKick'], ['Neutral', 'Back', 'Forward'], True),
                                 (standing['buttons'], standing['directions'], standing['reserveGroundInput']))
                self.assertEqual(original, base)
                self.assertEqual(original[0]['moves'][0], fighter['moves'][0])
                self.assertEqual(original[0]['commands'], [c for c in fighter['commands'] if c['commandId'] in
                                                          {old['commandId'] for old in original[0]['commands']}])
                move = fighter['moves'][-1]
                self.assertEqual(('TerryCrouchHeavyKick440', '2D', 'Crouching', True, 10, 8, 22, []),
                                 tuple(move[k] for k in ('moveId', 'commandId', 'allowedFrom', 'returnsToCrouch',
                                                        'startupFrames', 'activeFrames', 'recoveryFrames', 'cancels')))
                self.assertEqual(440, binding['clips']['state440']['action'])

    def test_low_contact_uses_ballistic_trip_from_source_and_defender_air(self):
        fighter, binding = sweep.build_profile(AUDIT, NATIVE, *baseline('default'), 'default')
        attack, = fighter['moves'][-1]['attacks']
        self.assertEqual(('Low', X, Y, ZERO, GRAVITY, ATTACK_PAUSE, GUARD_PAUSE),
                         (attack['height'], attack['launchVelocityX'], attack['launchVelocityY'],
                          attack['groundKnockback'], attack['launchGravity'],
                          attack['hitstopByOutcome']['hit'], attack['hitstopByOutcome']['block']))
        self.assertEqual(AUDIT['damage'], attack['dynamicDamage'])
        self.assertTrue(attack['launchReaction']['startInFall'])
        self.assertTrue(attack['launchReaction']['suppressOwnerProjectilesDuringFall'])
        self.assertEqual(AUDIT['groundTech'], attack['launchReaction']['groundTech'])
        self.assertEqual(5070, binding['clips']['launch_trip']['action'])
        self.assertEqual(5200, binding['clips']['launch_Tech']['action'])
        self.assertGreaterEqual(sum(span['frames'] for span in fighter['groundHurtboxes']['tripFall']), 33)

    def test_wave_route_requires_native_proof_and_is_absent_in_mode2(self):
        fighter, _ = sweep.build_profile(AUDIT, NATIVE, *baseline('default'), 'default')
        route, = fighter['moves'][-1]['cancels']
        self.assertEqual((WAVE_MOVE, 11, 17, True, False, False), tuple(route[k] for k in
                         ('targetMoveId', 'windowFirstFrame', 'windowLastFrame', 'allowWhiff',
                          'requiresHitConfirm', 'requiresBlockConfirm')))
        with self.assertRaisesRegex(ValueError, 'native cancel'):
            sweep.build_profile(AUDIT, {**NATIVE, 'cancelVerified': False}, *baseline('default'), 'default')
        mode2, _ = sweep.build_profile(AUDIT, {**NATIVE, 'cancelVerified': False}, *baseline('mode2'), 'mode2')
        self.assertEqual([], mode2['moves'][-1]['cancels'])
        self.assertFalse(any(move['moveId'] == WAVE_MOVE for move in mode2['moves']))

    def test_reference_from_incompatible_launch_or_guard_is_rejected(self):
        for key, change, message in [('hit', {'launchY': 9}, 'launch'),
                                     ('guard', {'initialPush': 11}, 'guard')]:
            with self.subTest(key=key):
                native = {**NATIVE, key: {**NATIVE[key], **change}}
                with self.assertRaisesRegex(ValueError, message):
                    sweep.build_profile(AUDIT, native, *baseline('default'), 'default')

    def test_body_rejects_unproven_tech_or_kick_competition(self):
        for key, message in [('groundTechVerified', 'native ground tech'), ('lightKickPriorityVerified', 'native kick priority')]:
            with self.subTest(key=key):
                with self.assertRaisesRegex(ValueError, message):
                    sweep.build_profile(AUDIT, {**NATIVE, key: False}, *baseline('default'), 'default', with_cancel=False)

    def test_body_rejects_incompatible_measured_trip_and_tech(self):
        for key, change in [('trip', {'impactFrames': 5}), ('tech', {'commandAgeFrames': 6})]:
            with self.subTest(key=key):
                native = {**NATIVE, key: {**NATIVE[key], **change}}
                with self.assertRaisesRegex(ValueError, f'native {key}'):
                    sweep.build_profile(AUDIT, native, *baseline('default'), 'default', with_cancel=False)

    def test_existing_weak_wave_obeys_owner_restriction_with_or_without_sweep_cancel(self):
        for enable_cancel in (False, True):
            with self.subTest(enable_cancel=enable_cancel):
                fighter, binding = baseline('default')
                converted, _ = sweep.build_profile(AUDIT, NATIVE, fighter, binding, 'default', with_cancel=enable_cancel)
                old = next(item for item in fighter['moves'] if item['moveId'] == WAVE_MOVE)
                new = next(item for item in converted['moves'] if item['moveId'] == WAVE_MOVE)
                self.assertEqual({}, old['projectiles'][0]['timeline'])
                self.assertTrue(new['projectiles'][0]['timeline'].get('respectsAirFollowupRestriction'))

    @unittest.skipUnless(os.environ.get('NEWKOF_TERRY_AUDIT'), 'requires the local original Terry archive and manifest')
    def test_original_audit_preserves_ground_tech_reaction_and_input(self):
        root = Path(os.environ['NEWKOF_TERRY_AUDIT'])
        audit = sweep.inspect_source(root / 'local-mugen/terry.zip', root / 'data/imported-characters/terry98/manifest.json')
        self.assertEqual({'inputBufferFrames': 5, 'durationFrames': 28, 'slideFrames': 16,
                          'initialSpeed': {'numerator': 8, 'denominator': 1},
                          'velocityRetention': {'numerator': 17, 'denominator': 20}}, audit['groundTech'])
        self.assertEqual([10, 4, 4, 4, -1], audit['trip']['durations'])
        self.assertEqual([11, 17], audit['cancelWindow'])
        self.assertIs(audit['launchReaction'].get('suppressOwnerProjectilesDuringFall'), True)
