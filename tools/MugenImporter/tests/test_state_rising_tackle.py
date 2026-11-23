import os
from pathlib import Path
import unittest

from mugen_importer import state_rising_tackle as tackle


@unittest.skipUnless(os.environ.get('NEWKOF_TERRY_AUDIT'), 'requires the local Terry ZIP and Manifest')
class OriginalRisingTackleTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.root = Path(os.environ['NEWKOF_TERRY_AUDIT'])
        cls.audit = tackle.inspect_source(cls.root / 'local-mugen/terry.zip',
                                          cls.root / 'data/imported-characters/terry98/manifest.json')

    def test_original_state_and_air_timing(self):
        self.assertFalse(self.audit['shortHopCanGuard'])
        self.assertEqual([1, 4], [row['frames'] for row in self.audit['standingLightHitstun']])
        body = self.audit['state1200']
        self.assertEqual([2, 3, 4, 1, 2, 2, 2, 2, 2, 1, 5, 6, 9], body['durations'])
        self.assertEqual((41, 14, 55), (body['duration'], body['recoveryDuration'],
                                        body['duration'] + body['recoveryDuration']))
        self.assertEqual([2, 4], body['reversalWindow'])
        self.assertEqual((9, 20, 40), (body['takeoffFrame'], body['attackEnd'], body['duration'] - 1))
        self.assertEqual([(5, 8), (9, 9), (12, 13), (16, 17), (20, 20)],
                         [(row['first'], row['last']) for row in body['attackWindows']])
        self.assertEqual([0, 2, 5], [row['frame'] for row in body['positionSteps']])
        self.assertEqual((1205, 14, 0), (body['landingAction'], body['recoveryDuration'], body['nextState']))

    def test_state310_reversal_permission_is_a_distinct_exit(self):
        self.assertEqual({'sourceState': 310, 'condition': 'MoveContact || MoveReversed'},
                         self.audit['contactPermission'])
        self.assertEqual('TerryRisingTackle1200', self.audit['command']['commandId'])

    def test_generated_profile_preserves_source_routes_and_landing_clock(self):
        base = self.root / 'data/imported-characters/terry98-stage30-final'
        fighter, binding = tackle.routes.load_frozen_base(base, 'default', self.audit,
                                                           frozen_base=tackle.FROZEN_BASE)
        body = self.audit['state1200']
        proof = dict.fromkeys(('bodyVerified', 'completeVerified', 'reversal310Verified',
                              'reversal310CancelVerified', 'projectileReversalVerified',
                              'wavePriorityVerified', 'naturalRecoveryVerified'), True)
        proof['move1200'] = {'durations': body['durations'], 'reversalFrames': body['reversalWindow'],
                             'attackGroups': [[row['first'], row['last']] for row in body['attackWindows']]}
        profile, sprites = tackle.build_profile(self.audit, proof, fighter, binding, 'default')
        self.assertFalse(profile['movement']['shortHopCanGuard'])
        self.assertEqual([2, 4, 6], [row['frames'] for row in profile['groundHurtboxes']['standingLightHitstun']])
        move = next(row for row in profile['moves'] if row['moveId'] == tackle.MOVE_ID)
        self.assertEqual(55, sum(move[key] for key in ('startupFrames', 'activeFrames', 'recoveryFrames')))
        self.assertEqual(55, sum(row['frames'] for row in move['hurtboxTimeline']))
        self.assertEqual(list(range(1, 6)), [row['contactGroup'] for row in move['attacks']])
        self.assertEqual({'numerator': 9, 'denominator': 10}, move['airMotion']['horizontalRetention'])
        self.assertEqual([{'startTick': 0, 'clip': 'state1200'}, {'startTick': 41, 'clip': 'state1205'}],
                         sprites['moveTimelines'][tackle.MOVE_ID])
        upper = next(row for row in profile['moves'] if row['moveId'] == 'TerryRisingUpper310')
        self.assertTrue(upper['reservedCancels'][0]['requiresReversalConfirm'])
        self.assertTrue(all(row['requiresReversalConfirm'] for row in upper['cancels']
                            if row['targetMoveId'] == tackle.wave.MOVE_ID))
        self.assertNotIn(tackle.RESERVED_COMMAND, {row['commandId'] for row in profile['commands']})
        mode2 = tackle.routes.load_frozen_base(base, 'mode2', self.audit, frozen_base=tackle.FROZEN_BASE)
        other, _ = tackle.build_profile(self.audit, proof, *mode2, 'mode2')
        self.assertEqual(mode2[0]['moves'], other['moves'])
        self.assertEqual(mode2[0]['commands'], other['commands'])
        with self.assertRaisesRegex(ValueError, 'requires complete'):
            tackle.build_profile(self.audit, {**proof, 'reversal310CancelVerified': False},
                                 fighter, binding, 'default')


if __name__ == '__main__':
    unittest.main()
