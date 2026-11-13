import copy
import json
import tempfile
import unittest
from pathlib import Path

from mugen_importer.state200 import read_sections
from mugen_importer.state210 import action_branch, attack_fields, build_profile, command_rules, hurtbox_timeline, verify_mode_intro


class State210Tests(unittest.TestCase):
    def test_mugen_1_distance_accepts_first_fixed_point_after_near_threshold(self):
        sections = read_sections('''
[Defaults]
command.time = 15
command.buffer.time = 1
[Command]
name = "y"
command = y
time = 1
[State -1, near]
type = ChangeState
value = 215
triggerall = command = "y" || foo
triggerall = p2bodydist X <= 20
[State -1, far]
type = ChangeState
value = 210
triggerall = command = "y" || foo
triggerall = roundstate = 2
triggerall = command != "holddown"
triggerall = statetype != A
trigger1 = ctrl || foo
''')
        constants = read_sections('[Size]\nground.front = 15\n')
        result = command_rules(sections, constants)
        self.assertEqual(50, result['maximumNearCenterDistance'])
        self.assertEqual({'numerator': 3276801, 'denominator': 65536}, result['minimumStartDistance'])

    def test_source_box_can_reverse_endpoints_and_span_adjacent_elements(self):
        action = {
            'action': 211, 'loop_start': None,
            'frames': [{'time': time} for time in (3, 2, 5, 4)],
            'collisions': [
                {'before_frame': 1, 'source': 'Clsn1: 1'},
                {'before_frame': 1, 'source': 'Clsn1[0] = 12,-61,67,-84'},
                {'before_frame': 2, 'source': 'Clsn1: 1'},
                {'before_frame': 2, 'source': 'Clsn1[0] = 12,-84,67,-61'},
            ],
        }
        branch = action_branch(action, 2, 4)
        self.assertEqual((3, 7, 4), tuple(branch[key] for key in ('startup', 'active', 'recovery')))
        self.assertEqual([12, -84, 67, -61], branch['strike'])
        action['collisions'][-1]['source'] = 'Clsn1[0] = 12,-84,12,-61'
        with self.assertRaisesRegex(ValueError, 'positive area'):
            action_branch(action, 2, 4)

    def test_attack_uses_measured_settlement_and_normalized_source_box(self):
        branch = {'strike': [12, -84, 67, -61], 'active': 7}
        hit = {'damage': 123, 'effectiveStun': 19, 'initialPush': 10.5,
               'pause': {'attackerFrames': 11, 'defenderFrames': 13}}
        guard = {'damage': 0, 'effectiveStun': 18, 'initialPush': 8,
                 'pause': {'attackerFrames': 11, 'defenderFrames': 11}}
        attack = attack_fields(branch, hit, guard, {'numerator': 17, 'denominator': 20})
        self.assertEqual(123, attack['damage'])
        self.assertEqual({'numerator': 145, 'denominator': 2}, attack['offsetUp'])
        self.assertEqual({'numerator': 21, 'denominator': 2}, attack['groundKnockback'])
        self.assertEqual(6, attack['lastActiveFrame'])
        self.assertEqual({'hit': hit['pause'], 'block': guard['pause']}, attack['hitstopByOutcome'])
        self.assertTrue(attack['contactOnEdge'])
        with self.assertRaisesRegex(ValueError, 'nonzero chip'):
            attack_fields(branch, hit, {**guard, 'damage': 1}, {'numerator': 17, 'denominator': 20})

    def test_presets_keep_state200_and_select_own_action_without_mutation(self):
        base_fighter = {'fighterId': 'TerryS200', 'movement': {'groundReaction': {
            'velocityRetention': {'numerator': 17, 'denominator': 20}}},
            'commands': [{'commandId': '5A', 'buttons': ['LightPunch']}],
            'moves': [{'moveId': 'TerryStandLightPunch200', 'activeFrames': 4, 'attacks': [{'damage': 21}]}]}
        base_binding = {'visualId': 'terry98-stage14', 'clips': {'state200': {'action': 200}, 'idle': {'action': 0}},
                        'moves': {'TerryStandLightPunch200': {'Active': 'state200'}}}
        distance = {'numerator': 4194305, 'denominator': 65536}
        audit = {'command': {'time': 1, 'bufferTime': 1, 'minimumStartDistance': distance}, 'actions': {
            'default': {'id': 210, 'strike': [12, -84, 67, -61], 'startup': 5, 'active': 8, 'recovery': 17,
                        'durations': [5, 8, 17], 'clsn2': [[], [], []]},
            'mode2': {'id': 211, 'strike': [12, -84, 67, -61], 'startup': 5, 'active': 7, 'recovery': 18,
                      'durations': [5, 7, 18], 'clsn2': [[], [], []]}}}
        hit = {'damage': 123, 'effectiveStun': 19, 'initialPush': 10.5,
               'pause': {'attackerFrames': 11, 'defenderFrames': 13},
               'reaction': {'initialAction': 5002, 'followupAction': 5007, 'followupTick': 4}}
        guard = {'damage': 0, 'effectiveStun': 18, 'initialPush': 8,
                 'pause': {'attackerFrames': 11, 'defenderFrames': 11}}
        reference = {'hit': {'outcome': hit}, 'guard': {'outcome': guard},
                     'trade': {'outcomes': {'light': {'damage': 33}, 'heavy': {'damage': 159}}}}
        snapshot = copy.deepcopy((base_fighter, base_binding))
        base_action = {'durations': [1, 2], 'clsn2': [[[-12, -90, 11, -1]], [[-9, -87, 10, -1]]]}
        for mode, action, active in (('default', 210, 8), ('mode2', 211, 7)):
            fighter, binding = build_profile(audit, reference, base_fighter, base_binding, mode, base_action)
            original = copy.deepcopy(fighter['moves'][0])
            del original['hurtboxTimeline']
            self.assertEqual(33, original['attacks'][0].pop('counterDamage'))
            self.assertEqual(base_fighter['moves'][0], original)
            self.assertEqual(hurtbox_timeline(base_action), fighter['moves'][0]['hurtboxTimeline'])
            self.assertEqual(base_binding['moves']['TerryStandLightPunch200'],
                             binding['moves']['TerryStandLightPunch200'])
            self.assertEqual(['HeavyPunch'], fighter['commands'][1]['buttons'])
            self.assertEqual(active, fighter['moves'][1]['activeFrames'])
            self.assertEqual(159, fighter['moves'][1]['attacks'][0]['counterDamage'])
            self.assertEqual(distance, fighter['moves'][1]['minimumStartDistance'])
            self.assertEqual(30, sum(span['frames'] for span in fighter['moves'][1]['hurtboxTimeline']))
            self.assertEqual(action, binding['clips']['state210']['action'])
            self.assertEqual(0 if mode == 'default' else 1, binding['clips']['idle']['action'])
            self.assertEqual({'initial': 'heavy_hurt', 'followup': 'heavy_hurt_followup', 'followupTick': 4},
                             binding['hitReactions']['TerryStandHeavyPunch210'])
            self.assertEqual({'Blockstun': 'guard', 'Walking': 'walk', 'PreJump': 'jump', 'Airborne': 'jump'},
                             binding['states'])
        self.assertEqual(snapshot, (base_fighter, base_binding))

    def test_hurtbox_projection_preserves_each_element_duration_and_flips_y(self):
        result = hurtbox_timeline({'durations': [3, 4], 'clsn2': [[[-9, -72, 13, -3]], []]})
        self.assertEqual([3, 4], [span['frames'] for span in result])
        self.assertEqual({'minX': {'numerator': -9, 'denominator': 1}, 'maxX': {'numerator': 13, 'denominator': 1},
                          'minY': {'numerator': 3, 'denominator': 1}, 'maxY': {'numerator': 72, 'denominator': 1}},
                         result[0]['boxes'][0])
        self.assertEqual([], result[1]['boxes'])

    def test_native_mode_requires_ordered_intro_inputs_and_grant_before_transition(self):
        name = 'mode2-hit'
        frames = (1, 3, 5, 7, 9, 11)
        inputs = (1, 1, 2, 2, 1, 2)
        rows = [
            {'case': name, 'introFrame': frame, 'phase': 'after-command', 'raw': raw,
             'palNo': 7, 'var10': 1, 'grantBuffer': int(frame == 11)}
            for frame, raw in zip(frames, inputs, strict=True)
        ]
        rows.append({**rows[-1], 'phase': 'end', 'var10': 2})
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'mode.jsonl'
            path.write_text(''.join(json.dumps(row) + '\n' for row in rows), encoding='utf-8')
            self.assertEqual(11, verify_mode_intro(path, name)['activationFrame'])
            rows[2]['raw'] = 1
            path.write_text(''.join(json.dumps(row) + '\n' for row in rows), encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'missing original Grant command evidence'):
                verify_mode_intro(path, name)


if __name__ == '__main__':
    unittest.main()
