import copy
import json
import tempfile
import unittest
from pathlib import Path
from zipfile import ZipFile

from mugen_importer.png import encode_png
from mugen_importer.state200 import (build_documents, collision_boxes, inspect_reference, inspect_source,
                                    output_documents, read_sections, source_move, state, timeline,
                                    validate_manifest, verify_attack_timeline, verify_boundary)


class State200Tests(unittest.TestCase):
    def test_sections_preserve_controller_order_and_repeated_triggers(self):
        source = '''[Statedef 200]
type = S
[State 200, first]
type = HitDef
trigger1 = condition1
trigger1 = condition2 ; discarded comment
[State 200, final]
type = ChangeState
[Statedef 201]
type = S
'''
        definition, controllers = state(read_sections(source), 200)
        self.assertEqual(1, definition.line)
        self.assertEqual(['HitDef', 'ChangeState'], [x.one('type') for x in controllers])
        self.assertEqual(['condition1', 'condition2'], controllers[0].values('trigger1'))

    def test_rejects_duplicate_state_definition(self):
        with self.assertRaisesRegex(ValueError, 'one statedef 200'):
            state(read_sections('[Statedef 200]\n[Statedef 200]'), 200)

    def test_timeline_uses_air_durations_and_one_based_controller_elements(self):
        action = {'frames': [{'time': value} for value in (3, 4, 5)], 'loop_start': None}
        self.assertEqual((3, 4, 5), timeline(action, 2, 3))
        for replacement in (0, -1):
            broken = copy.deepcopy(action)
            broken['frames'][2]['time'] = replacement
            with self.assertRaisesRegex(ValueError, 'positive finite'):
                timeline(broken, 2, 3)
        with self.assertRaisesRegex(ValueError, 'element bounds'):
            timeline(action, 2, 5)

    def test_boxes_are_local_to_element_and_preserve_source_edges(self):
        action = {'frames': [{}, {}, {}], 'collisions': [
            {'before_frame': 0, 'source': 'Clsn2Default: 1'},
            {'before_frame': 0, 'source': 'Clsn2[0] = -20,-88,19,-1'},
            {'before_frame': 1, 'source': 'Clsn1: 1'},
            {'before_frame': 1, 'source': 'Clsn1[0] = 4,-88,59,-73'},
            {'before_frame': 1, 'source': 'Clsn2: 0'},
        ]}
        self.assertEqual([[], [[4, -88, 59, -73]], []], collision_boxes(action, 1))
        self.assertEqual([[[-20, -88, 19, -1]], [], [[-20, -88, 19, -1]]], collision_boxes(action, 2))

    def test_rejects_missing_or_degenerate_air_box(self):
        action = {'frames': [{}], 'collisions': [
            {'before_frame': 0, 'source': 'Clsn1: 1'},
        ]}
        with self.assertRaisesRegex(ValueError, 'declared 1'):
            collision_boxes(action, 1)
        action['collisions'].append({'before_frame': 0, 'source': 'Clsn1[0] = 0,-1,0,1'})
        with self.assertRaisesRegex(ValueError, 'positive area'):
            collision_boxes(action, 1)

    def test_default_and_local_hurtboxes_may_be_declared_before_same_element(self):
        action = {'frames': [{}, {}], 'collisions': [
            {'before_frame': 0, 'source': 'Clsn2Default: 1'},
            {'before_frame': 0, 'source': 'Clsn2[0] = -4,-9,5,-1'},
            {'before_frame': 0, 'source': 'Clsn2: 1'},
            {'before_frame': 0, 'source': 'Clsn2[0] = -6,-15,7,-1'},
        ]}
        self.assertEqual([[[-6, -15, 7, -1]], [[-4, -9, 5, -1]]], collision_boxes(action, 2))

    def test_wrong_archive_hash_is_rejected_before_reading_source(self):
        with tempfile.TemporaryDirectory() as folder:
            source = Path(folder) / 'unreviewed.zip'
            with ZipFile(source, 'w') as archive:
                archive.writestr('unreviewed.cns', 'arbitrary fixture')
            with self.assertRaisesRegex(ValueError, 'unreviewed character archive SHA-256'):
                inspect_source(source, Path(folder) / 'unused.json')

    def test_unknown_controller_is_rejected(self):
        definition = read_sections('''[Statedef 200]
type = S
movetype = A
physics = N
velset = 0,0
ctrl = 0
anim = 200
facep2 = 1
[State 200, unfamiliar behavior]
type = TargetState
''')
        with self.assertRaisesRegex(ValueError, "unsupported State 200 controller sequence.*targetstate"):
            source_move(definition)

    def test_manifest_requires_exact_air_frames_and_real_visible_region(self):
        frame = {'group': 6, 'image': 2, 'x': 0, 'y': 0, 'time': 3, 'flip': ''}
        action = {'action': 200, 'loop_start': None, 'collisions': [], 'frames': [frame]}
        manifest = {'format': 1, 'character': 'terry98', 'localcoord': 320,
                    'sources': {'air_sha256': 'fixture'}, 'actions': [{**action, 'frames': [{**frame, 'sprite_index': 0}]}],
                    'sprites': [{'group': 6, 'image': 2, 'origin_x': 0, 'origin_y': 0,
                                 'region': {'page': 0, 'x': 0, 'y': 0, 'w': 1, 'h': 1}}]}
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'manifest.json'
            page = Path(folder) / 'atlas-000.png'
            save = lambda content: path.write_text(json.dumps(content), encoding='utf-8')
            verify = lambda: validate_manifest(path, {'air_sha256': 'fixture'}, action)
            save(manifest)
            with self.assertRaisesRegex(ValueError, 'missing_atlas'):
                verify()
            page.write_bytes(encode_png(1, 1, bytes([0, 0, 0, 0])))
            with self.assertRaisesRegex(ValueError, 'transparent_sprite'):
                verify()
            page.write_bytes(encode_png(1, 1, bytes([128, 40, 60, 255])))
            self.assertEqual([0], verify())
            duplicate = copy.deepcopy(manifest)
            duplicate['actions'].append(duplicate['actions'][0])
            save(duplicate)
            with self.assertRaisesRegex(ValueError, 'expected one Manifest Action 200, found 2'):
                verify()
            incorrect = copy.deepcopy(manifest)
            incorrect['actions'][0]['frames'][0]['time'] = 4
            save(incorrect)
            with self.assertRaisesRegex(ValueError, 'frames differ from AIR'):
                verify()
            manifest['sources']['air_sha256'] = 'other fixture'
            save(manifest)
            with self.assertRaisesRegex(ValueError, 'Manifest source hash mismatch'):
                verify()

    def test_reference_metadata_cannot_select_unreviewed_engine(self):
        with tempfile.TemporaryDirectory() as folder:
            metadata = Path(folder) / 'reference.json'
            metadata.write_text('{"engine": "IKEMEN GO", "commit": "a-different-engine-build"}')
            with self.assertRaisesRegex(ValueError, 'reviewed IKEMEN GO commit'):
                inspect_reference({}, metadata)

    def test_reference_cursor_holds_first_post_contact_pose_during_pause(self):
        action = {'startup': 2, 'durations': [2, 2, 1], 'spriteKeys': [[1, 0], [1, 1], [1, 2]],
                  'clsn1': [[], [[2, -10, 7, -3]], []], 'clsn2': [[], [], []]}
        observed = [(0, 0), (0, 1), (1, 0), (1, 1), (1, 1), (1, 1), (2, 0)]
        rows = [{'frame': frame, 'chars': [{'state': 200, 'anim': 200, 'elem': element, 'elemTime': tick,
                                           'frame': {'sprite': action['spriteKeys'][element],
                                                     'duration': action['durations'][element],
                                                     'clsn1': action['clsn1'][element],
                                                     'clsn2': action['clsn2'][element]}}]}
                for frame, (element, tick) in enumerate(observed)]
        verify_attack_timeline(action, rows, 0, len(rows), 2)
        rows[3]['chars'][0]['frame']['clsn1'] = [[2, -10, 9, -3]]
        with self.assertRaisesRegex(ValueError, 'collision boxes disagree with AIR'):
            verify_attack_timeline(action, rows, 0, len(rows), 2)
        rows[3]['chars'][0]['frame']['clsn1'] = action['clsn1'][1]
        rows[3]['chars'][0]['elemTime'] = 0
        with self.assertRaisesRegex(ValueError, 'timeline differs from AIR'):
            verify_attack_timeline(action, rows, 0, len(rows), 2)

    def test_build_uses_observed_damage_and_outcome_specific_pause(self):
        audit = {'action': {'startup': 2, 'active': 3, 'recovery': 5,
                            'clsn1': [[], [[2, -10, 7, -3]], []]},
                 'command': {'time': 1, 'bufferTime': 1}, 'movement': {},
                 'groundHurtboxes': {'standing': [[-3, -12, 4, -2]], 'crouching': [[-3, -5, 4, -2]]}}
        hit = {'initialPush': 4, 'pushRetention': .75, 'damage': 19, 'effectiveStun': 7,
               'pause': {'attackerFrames': 3, 'defenderFrames': 4}}
        guard = {'initialPush': 4, 'pushRetention': .75, 'damage': 0, 'effectiveStun': 4,
                 'pause': {'attackerFrames': 3, 'defenderFrames': 2}}
        reference = {'cases': {'hit': {'outcome': hit}, 'guard': {'outcome': guard}}}
        documents = build_documents(audit, reference)
        move = documents['state200.fighter.json']['moves'][0]
        attack = move['attacks'][0]
        self.assertEqual((2, 3, 5), (move['startupFrames'], move['activeFrames'], move['recoveryFrames']))
        self.assertEqual((0, 2), (attack['firstActiveFrame'], attack['lastActiveFrame']))
        self.assertEqual((19, 7, 4), (attack['damage'], attack['hitstunFrames'], attack['blockstunFrames']))
        self.assertEqual({'hit': hit['pause'], 'block': guard['pause']}, attack['hitstopByOutcome'])
        self.assertIs(attack['contactOnEdge'], True)
        self.assertEqual({'numerator': 9, 'denominator': 2}, attack['offsetForward'])
        self.assertEqual({'numerator': 13, 'denominator': 2}, attack['offsetUp'])
        standing = documents['state200.fighter.json']['groundHurtboxes']['standing']
        self.assertIs(documents['state200.fighter.json']['groundHurtboxes']['standingOnCrouchEntry'], True)
        self.assertEqual({'minX': {'numerator': -3, 'denominator': 1}, 'maxX': {'numerator': 4, 'denominator': 1},
                          'minY': {'numerator': 2, 'denominator': 1}, 'maxY': {'numerator': 12, 'denominator': 1}},
                         standing[0])
        clips = documents['state200.binding.json']['clips']
        self.assertEqual({'action': 200, 'clock': 'move', 'loop': False}, clips['state200'])

    def test_boundary_requires_one_confirmed_equality_and_both_outside_misses(self):
        audit = {'action': {'clsn1': [[], [[4, -10, 10, -5]], []]},
                 'groundHurtboxes': {'standing': [[-3, -10, 2, -1]]}}
        hit = {'damage': 19}
        cases = {'hit': {'outcome': hit},
                 'boundary-78': {'distance': 12, 'outcome': hit},
                 'boundary-79': {'distance': 13, 'outcome': None},
                 'boundary-80': {'distance': 14, 'outcome': None}}
        verify_boundary(audit, cases)
        cases['boundary-79']['outcome'] = hit
        with self.assertRaisesRegex(ValueError, 'do not confirm a single edge contact'):
            verify_boundary(audit, cases)

    def test_output_is_repeatable_and_refuses_to_overwrite(self):
        with tempfile.TemporaryDirectory() as folder:
            base = Path(folder)
            content = {'a.json': {'b': 2, 'a': 1}, 'other.json': {}}
            output_documents(base / 'first', content)
            output_documents(base / 'second', content)
            for name in content:
                self.assertEqual((base / 'first' / name).read_bytes(), (base / 'second' / name).read_bytes())
            with self.assertRaisesRegex(ValueError, 'will not overwrite'):
                output_documents(base / 'first', content)


if __name__ == '__main__':
    unittest.main()
