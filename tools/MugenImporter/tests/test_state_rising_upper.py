from copy import deepcopy
import os
from pathlib import Path
import unittest


@unittest.skipUnless(os.environ.get('NEWKOF_TERRY_AUDIT'), 'requires the local Terry ZIP and Manifest')
class OriginalRisingUpperTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        from mugen_importer import state_rising_upper as upper
        cls.upper = upper
        cls.root = Path(os.environ['NEWKOF_TERRY_AUDIT'])
        cls.audit = upper.inspect_source(cls.root / 'local-mugen/terry.zip',
                                         cls.root / 'data/imported-characters/terry98/manifest.json')

    def frozen(self, mode):
        return self.upper.routes.load_frozen_base(
            self.root / 'data/imported-characters/terry98-stage29-final', mode,
            self.audit, frozen_base=self.upper.FROZEN_BASE)

    def test_audits_stationary_state_and_two_boxes_under_one_hitdef(self):
        body = self.audit['action']
        self.assertEqual('Terry-N.cns', self.audit['members']['st1']['name'])
        self.assertEqual((310, 3, 6, 24), tuple(body[key] for key in ('id', 'startup', 'active', 'recovery')))
        self.assertEqual([3, 3, 3, 11, 6, 7], body['durations'])
        self.assertEqual([[320, 0], [320, 1], [320, 2], [320, 3], [320, 1], [10, 1]], body['spriteKeys'])
        self.assertEqual([{'first': 0, 'last': 2, 'box': [28, -84, 67, -45]},
                          {'first': 3, 'last': 5, 'box': [36, -116, 67, -61]}], body['strikes'])
        self.assertEqual([2] * 6, [len(boxes) for boxes in body['clsn2']])
        self.assertEqual((13, 16, False), tuple(self.audit['damage'].values()))
        self.assertEqual((11, 13), self.audit['hitPause'])
        self.assertEqual((11, 11), self.audit['guardPause'])
        self.assertEqual('60', self.audit['hitDef']['id'])
        self.assertEqual('1', self.audit['hitDef']['air.fall'])
        self.assertNotIn('fall', self.audit['hitDef'])
        self.assertEqual(9, self.audit['guardThreatFrames'])
        self.assertEqual(35, self.audit['airFollowup']['authoredAgeThreshold'])

    def test_audits_every_static_permission_and_natural_limit(self):
        self.assertEqual({200: [4, 6], 205: [4, 6], 215: [4, 12], 235: [5, 8], 245: [4, 8],
                          250: [24, 25], 400: [4, 4], 410: [6, 12], 440: [11, 17]},
                         self.audit['entryWindows'])
        self.assertEqual([4, 9], self.audit['contactWindow'])
        self.assertEqual(1, self.audit['contactAge'])
        self.assertEqual(33, self.audit['naturalEntryTick'])
        self.assertEqual([1000, 1050], sorted(self.audit['cancelTargets']))
        self.assertEqual(self.audit['earlyOrdinaryExits'], [])
        self.assertEqual(['DownForward'], self.audit['command']['directions'])
        self.assertEqual(['HeavyPunch'], self.audit['command']['buttons'])
        self.assertTrue(self.audit['command']['reserveOnEdgeDirection'])
        self.assertEqual(4, self.audit['command']['bufferPolicy']['helperTicks'])

    def test_only_replaces_the_310_reservation_and_converts_all_proven_sources(self):
        for mode in ('default', 'mode2'):
            with self.subTest(mode=mode):
                fighter, _ = self.frozen(mode)
                unchanged = deepcopy(fighter)
                command_id = self.upper.COMMAND
                placeholder = self.upper.PLACEHOLDER
                self.upper.install_command(self.audit, fighter)
                self.assertEqual([{**cmd, 'commandId': command_id} if cmd['commandId'] == placeholder else cmd
                                  for cmd in unchanged['commands']], fighter['commands'])
                self.upper.install_entries(self.audit, self.audit['entryWindows'], fighter)
                routes = {int(move['moveId'][-3:]): [rule['windowFirstFrame'], rule['windowLastFrame']]
                          for move in fighter['moves'] for rule in move.get('reservedCancels', ())
                          if rule['commandId'] == command_id}
                self.assertEqual(self.audit['entryWindows'], routes)
                self.assertFalse(any(rule['commandId'] == placeholder for move in fighter['moves']
                                     for rule in move.get('reservedCancels', ())))
                for move, prior in zip(fighter['moves'], unchanged['moves'], strict=True):
                    self.assertEqual(prior.get('cancels'), move.get('cancels'))

    def test_rejects_an_unreviewed_placeholder_source(self):
        fighter, _ = self.frozen('default')
        by_id = {move['moveId']: move for move in fighter['moves']}
        by_id['TerryCrouchLightKick430']['reservedCancels'] = [
            {'commandId': self.upper.PLACEHOLDER, 'phases': 'Active', 'windowFirstFrame': 3, 'windowLastFrame': 3}]
        self.upper.install_command(self.audit, fighter)
        with self.assertRaisesRegex(ValueError, 'unreviewed frozen'):
            self.upper.install_entries(self.audit, self.audit['entryWindows'], fighter)

    def test_build_preserves_single_contact_and_mode_specific_wave_permissions(self):
        for mode in ('default', 'mode2'):
            with self.subTest(mode=mode):
                base, base_binding = self.frozen(mode)
                fighter, binding = self.upper.build_profile(self.audit, self.native(), base, base_binding, mode)
                move, = (entry for entry in fighter['moves'] if entry['moveId'] == self.upper.MOVE_ID)
                self.assertEqual((3, 6, 24), tuple(move[key] for key in ('startupFrames', 'activeFrames', 'recoveryFrames')))
                self.assertNotIn('positionSteps', move)
                self.assertTrue(move['faceOpponentOnStart'])
                self.assertTrue(move['deferCrouchOnNaturalExit'])
                self.assertTrue(fighter['resetPauseClockOnCrouchEntry'])
                self.assertEqual([(0, 2, 1), (3, 5, 1)], [
                    tuple(attack[key] for key in ('firstActiveFrame', 'lastActiveFrame', 'contactGroup'))
                    for attack in move['attacks']])
                for attack in move['attacks']:
                    self.assertEqual(('Mid', 'Heavy', 19, 18), tuple(attack[key] for key in
                                     ('height', 'groundHitReaction', 'hitstunFrames', 'blockstunFrames')))
                    self.assertTrue(all(attack[key] for key in ('groundGuardOnly', 'launchOnAirborneOnly',
                                                               'respectsAirFollowupRestriction')))
                    self.assertEqual(self.audit['damage'], attack['dynamicDamage'])
                    self.assertEqual(self.audit['launchReaction'], attack['launchReaction'])
                clip = binding['moves'][self.upper.MOVE_ID]['Active']
                self.assertEqual({'action': 310, 'clock': 'move', 'loop': False}, binding['clips'][clip])
                self.assertTrue(binding['hitReactions'][self.upper.MOVE_ID]['zeroBasedLaunchClock'])
                if mode == 'default':
                    rule, = move['cancels']
                    self.assertEqual('TerryPowerWave1000', rule['targetMoveId'])
                    self.assertFalse(rule['allowWhiff'])
                    dunk, = (entry for entry in fighter['commands']
                             if entry['commandId'] == 'ReservedRisingTackle1200')
                    self.assertEqual(['Forward', 'Down', 'DownForward'], dunk['directions'])
                    self.assertEqual((14, 9), (dunk['maxSequenceFrames'], dunk['priority']))
                    self.assertTrue(dunk['matchInitialCardinalRelease'])
                    reserve, = move['reservedCancels']
                    self.assertEqual({**{key: value for key, value in rule.items()
                                         if key not in ('allowWhiff', 'targetMoveId')},
                                      'commandId': dunk['commandId']}, reserve)
                    self.assertFalse(any(entry['commandId'] == dunk['commandId'] for entry in fighter['moves']))
                else:
                    self.assertEqual([], move['cancels'])
                    rule, = move['reservedCancels']
                    self.assertEqual('ReservedMode2Wave', rule['commandId'])
                    self.assertFalse(any(entry['commandId'] == 'ReservedRisingTackle1200' for entry in fighter['commands']))
                    self.assertFalse(any(entry['moveId'] == 'TerryPowerWave1000' for entry in fighter['moves']))
                self.assertEqual((4, 9, 1, True, True), tuple(rule[key] for key in
                                 ('windowFirstFrame', 'windowLastFrame', 'maximumContactAge',
                                  'requiresHitConfirm', 'requiresBlockConfirm')))
                self.assertEqual((base['groundHurtboxes'], base['movement']),
                                 (fighter['groundHurtboxes'], fighter['movement']))
                self.assertNotIn(self.upper.MOVE_ID, base_binding['moves'])
                new_command, = (entry for entry in fighter['commands'] if entry['commandId'] == self.upper.COMMAND)
                self.assertTrue(new_command['bufferPolicy']['helperRunsDuringGlobalPause'])

    def test_production_profile_rejects_missing_or_conflicting_native_proof(self):
        base, binding = self.frozen('default')
        for changes in ({'globalPauseHelperVerified': False}, {'naturalEntryTick': 32},
                        {'entryWindows': {250: [24, 25]}}, {'contactWaveAge': 2},
                        {'risingTacklePriorityVerified': False},
                        {'move310': {**self.native()['move310'], 'guardTicks': 18}}):
            with self.subTest(changes=changes), self.assertRaises(ValueError):
                self.upper.build_profile(self.audit, {**self.native(), **changes}, base, binding, 'default')

    def test_body_only_profile_uses_only_verified_entry_windows(self):
        base, binding = self.frozen('default')
        native = {**self.native(), 'completeVerified': False, 'entryWindows': {'250': [24, 25]}}
        fighter, visual = self.upper.build_profile(self.audit, native, base, binding, 'default', body_only=True)
        routes = [(move['moveId'], rule['commandId']) for move in fighter['moves']
                  for rule in move.get('reservedCancels', ())
                  if rule['commandId'] in (self.upper.COMMAND, self.upper.PLACEHOLDER)]
        self.assertEqual([('TerryGroundBlowback250', self.upper.COMMAND)], routes)
        self.assertEqual('terry98-stage30-body-default', visual['visualId'])

    def native(self):
        return {'bodyVerified': True, 'completeVerified': True, 'followupGuardVerified': True,
                'globalPauseHelperVerified': True, 'naturalEntryTick': 33, 'contactWaveAge': 1,
                'risingTacklePriorityVerified': True,
                'defaultContactWaveVerified': True, 'mode2ContactWaveVerified': True,
                'entryWindows': self.audit['entryWindows'],
                'move310': {'durations': [3, 3, 3, 11, 6, 7], 'attackWindow': [3, 8],
                            'hitPause': [11, 13], 'guardPause': [11, 11], 'groundPush': 10.5,
                            'guardPush': 10.5, 'guardTicks': 17, 'groundHitTicks': 17,
                            'groundHitEffectiveTicks': 19, 'guardEffectiveTicks': 18, 'airLaunchX': 4.5,
                            'airLaunchY': 6.5}}


if __name__ == '__main__':
    unittest.main()
