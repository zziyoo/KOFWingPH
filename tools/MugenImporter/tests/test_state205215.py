import unittest

from mugen_importer.state200 import read_sections
from mugen_importer.state205215 import action_profile, attack_cursor, build_profile, move_scope, neutral_air_hurtboxes


class State205215Tests(unittest.TestCase):
    def test_controller_scope_does_not_depend_on_comment_labels(self):
        sections = read_sections('''
[Statedef 205]
type = S
[State 200, copied label]
type = HitDef
[State 205, second]
type = ChangeState
[Statedef 210]
[State 205, different scope]
type = Null
''')
        definition, controls = move_scope(sections, 205)
        self.assertEqual('s', definition.one('type').lower())
        self.assertEqual(['HitDef', 'ChangeState'], [control.one('type') for control in controls])

    def test_close_heavy_has_distinct_hit_ranges_and_boxes(self):
        action = {'action': 215, 'loop_start': None,
                  'frames': [{'time': n, 'group': 215, 'image': i}
                             for i, n in enumerate((3, 3, 7, 7, 8))],
                  'collisions': [
                      {'before_frame': 1, 'source': 'Clsn1: 1'},
                      {'before_frame': 1, 'source': 'Clsn1[0] = 14,-80,47,-26'},
                      {'before_frame': 2, 'source': 'Clsn1: 1'},
                      {'before_frame': 2, 'source': 'Clsn1[0] = 30,-80,75,-44'},
                  ]}
        result = action_profile(action, [(2, 3), (3, 4)])
        self.assertEqual((3, 10, 15), tuple(result[k] for k in ('startup', 'active', 'recovery')))
        self.assertEqual([(0, 2), (3, 9)], [(w['first'], w['last']) for w in result['strikes']])
        self.assertEqual([[14, -80, 47, -26], [30, -80, 75, -44]],
                         [w['box'] for w in result['strikes']])
        with self.assertRaisesRegex(ValueError, 'adjacent'):
            action_profile(action, [(2, 3), (4, 5)])

    def test_profile_gives_shared_buttons_near_first_and_preserves_stage15_base(self):
        light = {'id': 205, 'startup': 3, 'active': 4, 'recovery': 5, 'durations': [12], 'clsn2': [[]],
                 'strikes': [{'first': 0, 'last': 3, 'box': [3, -91, 49, -67]}]}
        heavy = {'id': 215, 'startup': 3, 'active': 10, 'recovery': 15, 'durations': [28], 'clsn2': [[]],
                 'strikes': [{'first': 0, 'last': 2, 'box': [14, -80, 47, -26]},
                             {'first': 3, 'last': 9, 'box': [30, -80, 75, -44]}]}
        audit = {'groundBodyWidths': {'front': {'numerator': 15, 'denominator': 1}, 'back': {'numerator': 16, 'denominator': 1}},
                 'airBodyWidths': {'front': {'numerator': 11, 'denominator': 1}, 'back': {'numerator': 12, 'denominator': 1}},
                 'neutralAirHurtboxes': [[-20, -96, 19, -33]],
                 'moves': {205: {'action': light, 'command': {'maximumStartBodyDistance': 16, 'time': 1, 'bufferTime': 1}},
                           215: {'action': heavy, 'command': {'maximumStartBodyDistance': 34, 'time': 1, 'bufferTime': 1}}}}
        base = {'fighterId': 'TerryS210',
                'commands': [{'commandId': command, 'maxSequenceFrames': 1, 'bufferLifetimeFrames': 1, 'priority': 0}
                             for command in ('5A', '5B')],
                'moves': [{'moveId': 'TerryStandLightPunch200', 'commandId': '5A'},
                          {'moveId': 'TerryStandHeavyPunch210', 'commandId': '5B', 'minimumStartDistance': {}}],
                'movement': {'groundReaction': {'velocityRetention': {'numerator': 17, 'denominator': 20}}}}
        binding = {'visualId': 'terry98-stage15-default',
                   'clips': {'heavy_hurt': {'action': 5002}, 'heavy_hurt_followup': {'action': 5007}},
                   'moves': {}, 'hitReactions': {'TerryStandHeavyPunch210': {'initial': 'heavy_hurt',
                                                                           'followup': 'heavy_hurt_followup',
                                                                           'followupTick': 4}}}
        hit = {'damage': 61, 'effectiveStun': 19, 'initialPush': 0,
               'pause': {'attackerFrames': 11, 'defenderFrames': 13}}
        guard = {'damage': 0, 'effectiveStun': 18, 'initialPush': 0,
                 'pause': {'attackerFrames': 11, 'defenderFrames': 11}}
        light_reaction = {'initialAction': 5000, 'followupAction': 5005, 'followupTick': 6}
        reference = {205: [{'hit': {**hit, 'damage': 30, 'initialPush': 6}, 'guard': {**guard, 'initialPush': 6},
                            'reaction': light_reaction}],
                     215: [{'hit': hit, 'guard': guard}, {'hit': {**hit, 'damage': 61, 'initialPush': 10.5},
                            'guard': {**guard, 'initialPush': 10.5}, 'damageAfterPriorContact': 29}]}
        fighter, visuals = build_profile(audit, reference, base, binding, 'default')
        self.assertEqual(['5A', '5B', '5A', '5B'], [move['commandId'] for move in fighter['moves']])
        self.assertEqual([1, 0], [command['priority'] for command in fighter['commands']])
        self.assertEqual([0, 0], [command['priority'] for command in base['commands']])
        self.assertEqual([1, 2], [attack['contactGroup'] for attack in fighter['moves'][1]['attacks']])
        self.assertEqual(29, fighter['moves'][1]['attacks'][1]['damageAfterPriorContact'])
        self.assertEqual({'numerator': 0, 'denominator': 1}, fighter['moves'][1]['attacks'][0]['groundKnockbackFriction'])
        self.assertNotIn('minimumStartDistance', fighter['moves'][3])
        self.assertIn('minimumStartDistance', base['moves'][1])
        self.assertEqual(215, visuals['clips']['state215']['action'])
        self.assertEqual({'initial': 'light_hurt', 'followup': 'light_hurt_followup', 'followupTick': 6},
                         visuals['hitReactions']['TerryStandLightPunch205'])
        self.assertEqual({'action': 5000, 'clock': 'phase', 'loop': False}, visuals['clips']['light_hurt'])
        self.assertEqual({'action': 5005, 'clock': 'phase', 'loop': False}, visuals['clips']['light_hurt_followup'])
        self.assertNotIn('TerryStandLightPunch200', visuals['hitReactions'])
        self.assertNotIn('TerryStandLightPunch205', binding['hitReactions'])
        self.assertEqual([{'minX': {'numerator': -20, 'denominator': 1}, 'maxX': {'numerator': 19, 'denominator': 1},
                           'minY': {'numerator': 33, 'denominator': 1}, 'maxY': {'numerator': 96, 'denominator': 1}}],
                         fighter['neutralAirHurtboxes'])

    def test_ikemen_collision_cursor_freezes_after_each_contact(self):
        action = {'durations': [2, 2, 3]}
        self.assertEqual([(0, 0), (0, 1), (1, 0), (1, 1), (1, 1), (2, 0),
                          (2, 1), (2, 1), (2, 1), (2, 2)],
                         attack_cursor(action, [(2, 1), (4, 2)]))

    def test_neutral_air_boxes_must_be_constant_across_high_and_short_hops(self):
        def action(number, box):
            return {'action': number, 'frames': [{}, {}], 'collisions': [
                {'before_frame': 0, 'source': 'Clsn2Default: 1'},
                {'before_frame': 0, 'source': f'Clsn2[0] = {box}'},
            ]}

        high = action(41, '-20,-96,19,-33')
        short = action(49, '-20,-96,19,-33')
        self.assertEqual([[-20, -96, 19, -33]], neutral_air_hurtboxes([high, short]))
        short['collisions'].extend([
            {'before_frame': 1, 'source': 'Clsn2: 1'},
            {'before_frame': 1, 'source': 'Clsn2[0] = -20,-96,19,-20'},
        ])
        with self.assertRaisesRegex(ValueError, 'common constant Clsn2'):
            neutral_air_hurtboxes([high, short])


if __name__ == '__main__':
    unittest.main()
