import os
from pathlib import Path
import unittest


@unittest.skipUnless(os.environ.get('NEWKOF_TERRY_AUDIT'), 'requires the local Terry ZIP and Manifest')
class OriginalGroundBlowbackTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        from mugen_importer import state_ground_blowback as blowback
        cls.blowback = blowback
        cls.root = Path(os.environ['NEWKOF_TERRY_AUDIT'])
        cls.audit = blowback.inspect_source(cls.root / 'local-mugen/terry.zip',
                                             cls.root / 'data/imported-characters/terry98/manifest.json')

    def test_audits_the_main_cns_move_body_and_air_geometry(self):
        self.assertEqual('Terry.cns', self.audit['members']['st']['name'])
        body = self.audit['action']
        self.assertEqual((250, 23, 3, 23), tuple(body[key] for key in ('id', 'startup', 'active', 'recovery')))
        self.assertEqual([{'first': 0, 'last': 2, 'box': [0, -76, 63, -45]}], body['strikes'])
        self.assertEqual([2, 3, 3, 3, 2, 2, 2, 2, 2, 2, 3, 4, 3, 3, 3, 3, 7], body['durations'])
        self.assertEqual(list(range(221, 238)), body['spriteIndices'])
        self.assertEqual([(tick, 8) for tick in (8, 11, 13, 15, 17, 19, 21, 23, 26, 30)] + [(33, 5)],
                         [(step['frame'], step['x']['numerator']) for step in self.audit['positionSteps']])
        self.assertEqual((16, 18, False), tuple(self.audit['damage'].values()))
        self.assertEqual((11, 13), self.audit['hitPause'])
        self.assertEqual((11, 11), self.audit['guardPause'])
        self.assertEqual([{'minX': {'numerator': -28, 'denominator': 1},
                           'maxX': {'numerator': 27, 'denominator': 1},
                           'minY': {'numerator': 37, 'denominator': 1},
                           'maxY': {'numerator': 92, 'denominator': 1}}], self.audit['launchAirHurtboxes'])

    def test_audits_raw_combo_and_state_time_instead_of_element_numbers(self):
        audit = self.audit
        command = audit['command']
        self.assertEqual(['HeavyPunch', 'HeavyKick'], command['buttons'])
        self.assertEqual(['Neutral', 'Back', 'Forward'], command['directions'])
        self.assertEqual(0, command['buttonToleranceFrames'])
        self.assertEqual(0, command['bufferLifetimeFrames'])
        self.assertTrue(command['reserveGroundInput'])
        self.assertFalse(command.get('reserveOnEdgeDirection', False))
        policy = command['bufferPolicy']
        self.assertEqual((3, 1, 1), tuple(policy[key] for key in ('helperTicks', 'helperHeldDecay', 'helperReleasedDecay')))
        self.assertEqual([24, 25], audit['cancelWindow'])
        self.assertEqual([300, 310, 1000, 1050], sorted(audit['cancelTargets']))
        self.assertEqual([], audit['earlyOrdinaryEntries'])
        self.assertEqual([], audit['earlyOrdinaryExits'])
        self.assertEqual(49, audit['naturalEntryTick'])

    def test_frozen_command_replacement_preserves_candidate_order(self):
        from copy import deepcopy
        for mode in ('default', 'mode2'):
            with self.subTest(mode=mode):
                fighter, _ = self.frozen(mode)
                unchanged = deepcopy(fighter)
                self.blowback.install_command(self.audit, fighter)
                old = self.blowback.PLACEHOLDER
                new = self.blowback.COMMAND
                self.assertEqual([new if item['commandId'] == old else item['commandId']
                                  for item in unchanged['commands']],
                                 [item['commandId'] for item in fighter['commands']])
                current, = (item for item in fighter['commands'] if item['commandId'] == new)
                self.assertEqual(6, current['priority'])
                self.assertTrue(current['reserveGroundInput'])
                self.assertEqual(unchanged['moves'], fighter['moves'])

    def test_build_adds_body_and_separates_verified_routes_in_both_modes(self):
        for mode in ('default', 'mode2'):
            with self.subTest(mode=mode):
                frozen, frozen_binding = self.frozen(mode)
                fighter, binding = self.blowback.build_profile(self.audit, self.native(), frozen, frozen_binding, mode)
                move, = (entry for entry in fighter['moves'] if entry['moveId'] == self.blowback.MOVE_ID)
                allowed_changes = {self.blowback.MOVE_ID, 'TerryBackKnuckle300'}
                self.assertEqual([entry for entry in frozen['moves'] if entry['moveId'] not in allowed_changes],
                                 [entry for entry in fighter['moves'] if entry['moveId'] not in allowed_changes])
                self.assertEqual(('GroundBlowback', 'GroundIdle', 23, 3, 23),
                                 tuple(move[key] for key in ('commandId', 'allowedFrom', 'startupFrames',
                                                             'activeFrames', 'recoveryFrames')))
                attack, = move['attacks']
                self.assertEqual((0, 2, 'Mid', True, 22), tuple(attack[key] for key in ('firstActiveFrame',
                    'lastActiveFrame', 'height', 'rejectsLyingTargets', 'blockstunFrames')))
                self.assertEqual(self.audit['damage'], attack['dynamicDamage'])
                self.assertEqual(self.audit['launchX'], attack['launchVelocityX'])
                self.assertEqual(self.audit['launchY'], attack['launchVelocityY'])
                self.assertFalse(attack['launchReaction'].get('startInFall', False))
                self.assertTrue(attack['groundGuardOnly'])
                self.assertTrue(attack['respectsAirFollowupRestriction'])
                self.assertTrue(attack['launchReaction']['suppressOwnerProjectilesDuringFall'])
                self.assertTrue(attack['launchReaction']['allowOwnerProjectilesOnCounter'])
                self.assertEqual(self.audit['launchAirHurtboxes'], fighter['groundHurtboxes']['launchAir'])
                self.assertEqual(250, binding['clips'][binding['moves'][self.blowback.MOVE_ID]['Active']]['action'])
                reserved = {rule['commandId'] for rule in move['reservedCancels']}
                self.assertTrue({'ForwardLightPunch', 'ReservedRisingUpper'} <= reserved)
                self.assertEqual({'ReservedMode2Wave'} if mode == 'mode2' else set(),
                                 reserved - {'ForwardLightPunch', 'ReservedRisingUpper'})
                targets = [rule['targetMoveId'] for rule in move['cancels']]
                self.assertEqual(['TerryPowerWave1000'] if mode == 'default' else [], targets)
                for rule in (*move['reservedCancels'], *move['cancels']):
                    self.assertEqual((24, 25, 'Active'), tuple(rule[key] for key in
                                     ('windowFirstFrame', 'windowLastFrame', 'phases')))
                self.assertNotIn(self.blowback.MOVE_ID, frozen_binding['moves'])

    def test_profile_rejects_incomplete_or_conflicting_evidence(self):
        base, binding = self.frozen('default')
        for bad in ({'backKnuckleVerified': False}, {'cancelWindow': [11, 12]}, {'completeVerified': False},
                    {'followupGuardVerified': False}, {'airGuardRejected': False},
                    {'launchWaveCounterAllowed': False}, {'launchMeleeRestricted': False},
                    {'move250': {**self.native()['move250'], 'guardPush': 10}},
                    {'move250': {**self.native()['move250'], 'attackWindow': [22, 24]}}):
            with self.subTest(bad=bad), self.assertRaises(ValueError):
                self.blowback.build_profile(self.audit, {**self.native(), **bad}, base, binding, 'default')

    def test_back_knuckle_respects_mid_guard_through_the_blowback_chain(self):
        base, binding = self.frozen('default')
        fighter, _ = self.blowback.build_profile(self.audit, self.native(), base, binding, 'default')
        move_id = 'TerryBackKnuckle300'
        parent, = (move for move in base['moves'] if move['moveId'] == move_id)
        changed, = (move for move in fighter['moves'] if move['moveId'] == move_id)
        self.assertEqual(['High', 'Low'], [attack['height'] for attack in parent['attacks']])
        self.assertEqual({**parent, 'attacks': [{**attack, 'height': 'Mid', 'groundGuardOnly': True,
                                                'retainsGroundGuardOnFollowup': True,
                                                'respectsAirFollowupRestriction': True}
                                               for attack in parent['attacks']]}, changed)

    @staticmethod
    def native():
        return {'bodyVerified': True, 'completeVerified': True, 'cancelWindow': [24, 25],
                'backKnuckleVerified': True, 'risingUpperVerified': True, 'waveVerified': True,
                'mode2WaveVerified': True, 'followupGuardVerified': True,
                'airGuardRejected': True, 'launchWaveRestricted': True, 'launchWaveCounterAllowed': True,
                'launchMeleeRestricted': True,
                'move250': {
                    'launchX': 4.5, 'launchY': 6.5, 'guardPush': 10.5, 'guardTicks': 21,
                    'hitPause': [11, 13], 'guardPause': [11, 11], 'attackWindow': [23, 25],
                    'durations': [2, 3, 3, 3, 2, 2, 2, 2, 2, 2, 3, 4, 3, 3, 3, 3, 7],
                    'positionSteps': [{'tick': tick, 'x': 8} for tick in (8, 11, 13, 15, 17, 19, 21, 23, 26, 30)] +
                                     [{'tick': 33, 'x': 5}]
                }}

    def frozen(self, mode):
        return self.blowback.routes.load_frozen_base(self.root / 'data/imported-characters/terry98-stage28-final',
                                                     mode, self.audit, frozen_base=self.blowback.FROZEN_BASE)


if __name__ == '__main__':
    unittest.main()
