import hashlib
import json
import unittest
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest.mock import patch

from mugen_importer.state200 import read_sections
from mugen_importer.state_reentry import (MOVE_IDS, build_profile, inspect_command, inspect_helper,
                                         load_frozen_base, physical_light_presses, verify_coverage)


def command_source():
    entry = '''
type = ChangeState
value = {state}
triggerall = var(59) != 1
triggerall = !IsHelper
triggerall = roundstate = 2
triggerall = command = "x" || helper(99999),var(0) > 0
triggerall = command != "holddown"
triggerall = statetype != A
{distance}
trigger1 = ctrl || (stateno = [100,101]) || var(2) >= 1
trigger2 = stateno = 200 && AnimElem = 3,>= 0
trigger3 = stateno = 205 && AnimElem = 3,>= 0
'''
    defaults = '''
[Defaults]
command.time = 15
command.buffer.time = 1
[Command]
name = "x"
command = x
time = 1
[Statedef -1]
'''
    near = entry.format(state=205, distance='triggerall = p2bodydist X <= 16')
    far = entry.format(state=200, distance='')
    return defaults + '\n[State -1, Near]\n' + near + '\n[State -1, Far]\n' + far


class StateReentryTests(unittest.TestCase):
    def test_audits_both_light_sources_and_original_target_precedence(self):
        command = inspect_command(read_sections(command_source()))
        self.assertEqual([205, 200], command['targetOrder'])
        self.assertEqual({200: 3, 205: 3}, command['sourceElements'])
        self.assertEqual(16, command['nearMaximumBodyDistance'])
        self.assertTrue(command['requiresNoDown'])
        self.assertEqual((1, 1), (command['time'], command['bufferTime']))
        self.assertLess(command['nearEntryLine'], command['farEntryLine'])

    def test_rejects_changed_contact_condition(self):
        source = command_source().replace('AnimElem = 3,>= 0', 'AnimElem = 3,>= 0 && MoveHit', 1)
        with self.assertRaisesRegex(ValueError, 're-entry condition'):
            inspect_command(read_sections(source))

    def test_rejects_changed_priority_and_direction_restriction(self):
        source = command_source()
        earlier, later = source.split('[State -1, Far]')
        first, near = earlier.split('[State -1, Near]')
        with self.assertRaisesRegex(ValueError, 'precedence'):
            inspect_command(read_sections(first + '[State -1, Far]' + later + '[State -1, Near]' + near))
        with self.assertRaisesRegex(ValueError, 'entry guards'):
            inspect_command(read_sections(source.replace('command != "holddown"', 'command = "holddown"', 1)))

    def test_helper_latch_requires_the_exact_physical_command_and_decay(self):
        source = '''
[Statedef 99999]
[State 99999, Set]
type = VarSet
trigger1 = command = "x"
var(0) = 3
[State 99999, Decay]
type = VarAdd
trigger1 = var(0) > 0
var(0) = -1
'''
        self.assertEqual({'state': 99999, 'setLine': 3, 'decayLine': 7, 'latchValue': 3, 'decayValue': -1},
                         inspect_helper(read_sections(source)))
        with self.assertRaisesRegex(ValueError, 'unsupported command helper'):
            inspect_helper(read_sections(source.replace('var(0) > 0', 'var(0) >= 0')))

    def test_rejects_modified_frozen_base_even_when_json_is_equivalent(self):
        fighter = {'fighterId': 'TerryDamage'}
        binding = {'visualId': 'terry98-stage17-default'}
        with TemporaryDirectory() as temporary:
            directory = Path(temporary)
            expected = {}
            for kind, content in (('fighter', fighter), ('binding', binding)):
                path = directory / f'default.{kind}.json'
                path.write_text(json.dumps(content), encoding='utf-8')
                expected[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
            with patch('mugen_importer.state_reentry.FROZEN_BASE_SHA256', expected):
                self.assertEqual((fighter, binding), load_frozen_base(directory, 'default'))
                with (directory / 'default.fighter.json').open('a', encoding='utf-8') as stream:
                    stream.write('\n')
                with self.assertRaisesRegex(ValueError, 'frozen Stage 17 SHA-256 mismatch'):
                    load_frozen_base(directory, 'default')

    def test_only_opted_in_light_moves_gain_ordered_reentry_rules(self):
        near_distance = {'numerator': 16, 'denominator': 1}
        base = {'fighterId': 'TerryDamage', 'commands': [{'commandId': '5A', 'bufferLifetimeFrames': 1}],
                'moves': [{'moveId': MOVE_IDS[205], 'commandId': '5A', 'cancels': [],
                           'maximumStartBodyDistance': near_distance},
                          {'moveId': 'heavy', 'commandId': '5B', 'cancels': []},
                          {'moveId': MOVE_IDS[200], 'commandId': '5A', 'cancels': []}]}
        binding = {'visualId': 'terry98-stage17-default', 'clips': {'state205': {'action': 205}}}
        audit = {'command': inspect_command(read_sections(command_source()))}
        timing = {'windowFirstFrame': 8, 'windowLastFrame': 11, 'phases': 'Recovery', 'maximumInputAge': 2}

        fighter, visual = build_profile(audit, timing, base, binding, 'default')

        self.assertEqual('TerryReentry', fighter['fighterId'])
        self.assertEqual('terry98-stage18-default', visual['visualId'])
        self.assertEqual(binding['clips'], visual['clips'])
        for source, actual in ((205, fighter['moves'][0]), (200, fighter['moves'][2])):
            self.assertEqual([{'targetMoveId': MOVE_IDS[target], **timing,
                               'allowSameMove': source == target, 'allowWhiff': True, 'standingInputOnly': True,
                               'requiresHitConfirm': False, 'requiresBlockConfirm': False}
                              for target in (205, 200)], actual['cancels'])
        self.assertEqual(base['moves'][1], fighter['moves'][1])
        self.assertEqual([], base['moves'][0]['cancels'])
        self.assertEqual(1, base['commands'][0]['bufferLifetimeFrames'])
        self.assertEqual(2, fighter['commands'][0]['bufferLifetimeFrames'])

    def test_held_x_is_one_physical_edge_even_with_simultaneous_y(self):
        x, y = 128, 256
        raw = [0, x | y, x, x, 0, y, x | y, 0]
        before = [{'frame': frame, 'chars': [{'raw': bits}]} for frame, bits in enumerate(raw)]
        self.assertEqual([1, 6], physical_light_presses(before))

    def test_missing_or_duplicate_native_cases_cannot_generate_a_profile(self):
        with self.assertRaisesRegex(ValueError, 'missing native cases:'):
            verify_coverage({}, [])
        run = {'cases': {'default-reentry-whiff-12': {}}}
        with self.assertRaisesRegex(ValueError, 'duplicate canonical Stage 18 case'):
            verify_coverage({}, [run, run])


if __name__ == '__main__':
    unittest.main()
