import copy
import unittest

from mugen_importer.state_power_wave import (ENTITY_ID, MOVE_ID, build_profile, parse_helper_damage,
                                            projectile_hit_spans)


class StatePowerWaveTests(unittest.TestCase):
    def test_parses_the_helper_formula_and_rejects_wrong_rounding_order(self):
        expression = 'floor(((ifelse(root,var(6),14,12)-root,var(21))*root,fvar(7))*root,fvar(0)),floor(1*root,fvar(7))'
        self.assertEqual((12, 14, 1), parse_helper_damage(expression))
        with self.assertRaisesRegex(ValueError, 'damage'):
            parse_helper_damage(expression.replace('*root,fvar(0)', ') *root,fvar(0)'))

    def test_converts_air_boxes_and_splits_early_launch_from_late_contact(self):
        boxes = [[], [[16, -48, 79, -1]], [[56, -64, 87, -1]],
                 [[64, -80, 95, -1]], [[64, -80, 95, -1]], [[80, -88, 119, -1]],
                 [[80, -88, 119, -1]], [], [], [], []]
        durations = [1, 2, 2, 2, 2, 2, 2, 2, 3, 3, 4]
        spans = projectile_hit_spans(durations, boxes, early_element=2, late_element=6, exit_element=8)
        self.assertEqual([(1, 2, 'early'), (3, 4, 'early'), (5, 8, 'early'), (9, 12, 'late')],
                         [(s['firstFrame'], s['lastFrame'], s['reaction']) for s in spans])
        self.assertEqual((80, 119, 1, 88), spans[-1]['localBox'])

    def test_profile_is_additive_and_has_independent_move_and_helper_clocks(self):
        fixture = {
            'command': {'maxSequenceFrames': 16, 'buttonToleranceFrames': 0,
                        'helperTicks': 12, 'helperHeldDecay': 1, 'helperReleasedDecay': 4},
            'cancelWindow': [4, 6],
            'forwardGuardDistance': {'numerator': 230, 'denominator': 1},
            'guardThreatFrames': {200: 7, 205: 7, 1000: 17, 1020: 13},
            'move': {'durations': [15, 2, 35], 'spawnTick': 15,
                     'hurtboxes': [{'frames': 52, 'boxes': []}]},
            'entity': {'speed': {'numerator': 5, 'denominator': 2}, 'lifetime': 25,
                       'movementFrames': 11, 'guardDamage': 9, 'hitSpans': [
                           {'firstFrame': 1, 'lastFrame': 2, 'localBox': (16, 79, 1, 48), 'reaction': 'early'}],
                       'normalBase': 12, 'counterBase': 14,
                       'hitPause': [0, 13], 'guardPause': [0, 11], 'hitStun': 19, 'guardStun': 18,
                       'earlyLaunch': [4.5, 6.5], 'launchGravity': {'numerator': 1, 'denominator': 2},
                       'launchReaction': {'impactFrames': 4, 'lieDownFrames': 13},
                       'latePush': 10.5, 'guardPush': 10.5,
                       'knockdownFrames': 20, 'wakeupFrames': 25},
        }
        fighter = {'fighterId': 'TerryReentry', 'displayName': 'Terry', 'commands': [
            {'commandId': '5A', 'priority': 1}, {'commandId': '5B', 'priority': 0}],
            'moves': [{'moveId': 'TerryStandLightPunch200', 'cancels': [{'targetMoveId': 'old'}]},
                      {'moveId': 'TerryStandLightPunch205', 'cancels': [{'targetMoveId': 'old'}]}]}
        binding = {'visualId': 'terry98-stage18-default', 'clips': {'idle': {'action': 0}}, 'moves': {}}
        original = copy.deepcopy((fighter, binding))
        converted, visual = build_profile(fixture, fighter, binding)
        self.assertEqual(original, (fighter, binding))
        self.assertTrue(converted['useCommandPriority'])
        self.assertFalse(converted['allowStrikeClash'])
        self.assertEqual((4, 6), tuple(converted['moves'][0]['cancels'][0][key]
                                      for key in ('windowFirstFrame', 'windowLastFrame')))
        self.assertEqual(converted['moves'][0]['cancels'][0]['targetMoveId'], MOVE_ID)
        self.assertEqual(converted['moves'][1], {**fighter['moves'][1], 'guardThreatFrames': 7})
        self.assertEqual(fixture['forwardGuardDistance'], converted['forwardGuardDistance'])
        self.assertEqual([15, 2, 35], [converted['moves'][-1][key]
                                      for key in ('startupFrames', 'activeFrames', 'recoveryFrames')])
        projectile, = converted['moves'][-1]['projectiles']
        self.assertEqual(0, projectile['spawnAtActiveFrame'])
        self.assertEqual(25, projectile['lifetimeFrames'])
        self.assertEqual(11, projectile['timeline']['movementFrames'])
        self.assertEqual(13, projectile['timeline']['guardThreatFrames'])
        self.assertEqual(17, converted['moves'][-1]['guardThreatFrames'])
        self.assertEqual(19, projectile['timeline']['hitSpans'][0]['hit']['hitstunFrames'])
        self.assertEqual(18, projectile['timeline']['hitSpans'][0]['hit']['blockstunFrames'])
        self.assertEqual(fixture['entity']['launchReaction'], projectile['timeline']['hitSpans'][0]['launchReaction'])
        self.assertEqual(1000, visual['clips']['state1000']['action'])
        self.assertEqual(1020, visual['clips'][f'projectile_{ENTITY_ID}']['action'])


if __name__ == '__main__':
    unittest.main()
