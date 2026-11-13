import copy
import unittest

from mugen_importer.state_crouch_wave import MOVE, build_profile
from mugen_importer.state_power_wave import MOVE_ID


BOX = [12, -56, 67, -33]
AUDIT = {
    'action': {'strikes': [{'box': BOX}], 'startup': 3, 'active': 2, 'recovery': 8,
               'durations': [13], 'clsn2': [[BOX]]},
    'crouchRise': {'durations': [1, 3], 'clsn2': [[BOX], [BOX]]},
    'lightReaction': {'durations': [1, 4, 1], 'clsn2': [[BOX], [BOX], [BOX]]},
    'damage': {'normalBase': 6, 'counterBase': 7, 'useContactDivisor': False},
    'guardThreatFrames': 5,
    'cancelWindow': [4, 4],
    'lightWindowStart': 6,
}
NATIVE = {
    'hit': {'damage': 38, 'initialPush': 6, 'effectiveStun': 11,
            'pause': {'attackerFrames': 11, 'defenderFrames': 13}},
    'guard': {'damage': 0, 'initialPush': 6, 'effectiveStun': 10,
              'pause': {'attackerFrames': 11, 'defenderFrames': 11}},
    'cancelVerified': True,
    'lightWindowStart': 6,
}


def baseline(mode):
    lights = [{'moveId': f'TerryStandLightPunch{state}', 'cancels': []} for state in (200, 205)]
    wave = {'moveId': MOVE_ID, 'projectiles': [{'projectileTypeId': 'TerryPowerWave1020'}]}
    fighter = {'moves': [*lights, wave] if mode == 'default' else lights, 'groundHurtboxes': {},
               'movement': {'groundReaction': {'velocityRetention': {'numerator': 85, 'denominator': 100}}}}
    return fighter, {'clips': {}, 'moves': {}}


class StateCrouchWaveTests(unittest.TestCase):
    def test_body_build_does_not_enable_routes(self):
        for mode in ('default', 'mode2'):
            base = baseline(mode)
            original = copy.deepcopy(base)
            fighter, binding = build_profile(AUDIT, NATIVE, *base, mode, with_cancel=False)
            self.assertEqual(original, base)
            self.assertEqual(original[0]['moves'], fighter['moves'][:-1])
            crouch = fighter['moves'][-1]
            self.assertEqual(('5A', 'Crouching', []), (crouch['commandId'], crouch['allowedFrom'], crouch['cancels']))
            self.assertEqual([2, 4, 6], [span['frames'] for span in fighter['groundHurtboxes']['lightHitstun']])
            self.assertEqual(12, binding['clips']['crouch_rise']['action'])

    def test_default_reuses_wave_and_mode2_never_maps_its_qcf_to_weak_wave(self):
        for mode in ('default', 'mode2'):
            base = baseline(mode)
            fighter, _ = build_profile(AUDIT, NATIVE, *base, mode)
            routes = fighter['moves'][-1]['cancels']
            if mode == 'mode2':
                self.assertFalse(routes)
                self.assertEqual(base[0]['moves'], fighter['moves'][:-1])
                continue
            self.assertEqual(base[0]['moves'][2], fighter['moves'][2])
            self.assertEqual((MOVE_ID, 4, 4, True),
                             tuple(routes[0][k] for k in ('targetMoveId', 'windowFirstFrame', 'windowLastFrame', 'allowWhiff')))
            self.assertEqual((MOVE, 6, 12, True),
                             tuple(routes[1][k] for k in ('targetMoveId', 'windowFirstFrame', 'windowLastFrame', 'allowSameMove')))
            self.assertTrue(all(route['standingInputOnly'] for route in routes[2:]))

    def test_requires_reference_for_cancel_and_for_the_ground_reaction(self):
        with self.assertRaisesRegex(ValueError, 'verified native cancel'):
            build_profile(AUDIT, {**NATIVE, 'cancelVerified': False}, *baseline('default'), 'default')
        with self.assertRaisesRegex(ValueError, 'held recovery pose'):
            build_profile(AUDIT, {**NATIVE, 'hit': {**NATIVE['hit'], 'effectiveStun': 4}},
                          *baseline('default'), 'default')
