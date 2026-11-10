import unittest
import copy
import tempfile
from pathlib import Path
from mugen_importer.action_audit import summarize
from mugen_importer.action_audit import audit_assets
from mugen_importer.png import encode_png


def sample():
    return {'character': 'sample', 'sprites': [
        {'index': 0, 'group': 1, 'image': 2, 'origin_x': 1, 'origin_y': 2,
         'link': None, 'region': {'page': 0, 'x': 0, 'y': 0, 'w': 2, 'h': 2}}],
        'actions': [{'action': 8, 'loop_start': None, 'frames': [
            {'group': 1, 'image': 2, 'sprite_index': 0, 'time': 3, 'x': 0, 'y': 0, 'flip': ''}]}]}


class AssetAuditTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        self.atlas = self.directory / 'atlas-000.png'
        self.atlas.write_bytes(encode_png(2, 2, bytes([40, 50, 60, 255]) * 4))
        self.manifest = sample()

    def codes(self):
        return [d['code'] for d in audit_assets(self.manifest, self.directory)]

    def test_valid_image_and_shared_sprite_are_not_errors(self):
        second = copy.deepcopy(self.manifest['actions'][0])
        second['action'] = 9
        self.manifest['actions'].append(second)
        self.assertEqual(self.codes(), [])
        self.assertEqual(summarize(self.manifest)['actions'][0]['frames'][0]['shared_with'], [9])

    def test_transparent_image_is_diagnosed(self):
        self.atlas.write_bytes(encode_png(2, 2, bytes(16)))
        self.assertIn('transparent_sprite', self.codes())

    def test_corrupt_png_and_truncated_png_are_diagnosed(self):
        for data in (b'not a png', b'\x89PNG\r\n\x1a\n', self.atlas.read_bytes()[:-5], self.atlas.read_bytes()[:-12]):
            with self.subTest(length=len(data)):
                self.atlas.write_bytes(data)
                self.assertIn('invalid_atlas', self.codes())

    def test_region_bounds_and_dimensions(self):
        for field, value in [('x', -1), ('y', 2), ('w', 0), ('h', -1), ('w', 4097)]:
            with self.subTest(field=field, value=value):
                self.manifest = sample()
                self.manifest['sprites'][0]['region'][field] = value
                self.assertIn('invalid_region', self.codes())

    def test_unusual_origin_is_reviewable_warning(self):
        self.manifest['sprites'][0]['origin_x'] = -9000
        diagnostic = audit_assets(self.manifest, self.directory)[0]
        self.assertEqual(diagnostic['code'], 'unusual_origin')
        self.assertEqual(diagnostic['origin'], [-9000, 2])
        self.assertEqual(diagnostic['severity'], 'warning')

    def test_missing_index_and_blank_are_different(self):
        frame = self.manifest['actions'][0]['frames'][0]
        frame['sprite_index'] = None
        self.assertIn('missing_sprite', self.codes())
        frame['image'] = -1
        self.assertEqual(self.codes(), ['intentional_blank'])

    def test_duplicate_loop_and_duration_errors(self):
        action = self.manifest['actions'][0]
        action['frames'][0]['time'] = -2
        action['loop_start'] = 99
        self.manifest['actions'].append(copy.deepcopy(action))
        self.assertEqual(self.codes().count('duplicate_action'), 2)
        self.assertIn('invalid_loop', self.codes())
        self.assertIn('invalid_duration', self.codes())

    def test_malformed_frame_has_location_and_does_not_crash(self):
        self.manifest['actions'][0]['frames'].append({'time': 'invalid'})
        diagnostics = audit_assets(self.manifest, self.directory)
        self.assertEqual(diagnostics[-1]['code'], 'malformed_frame')
        self.assertEqual(diagnostics[-1]['frame'], 1)

    def test_linked_sprite_region_sharing_is_allowed(self):
        linked = copy.deepcopy(self.manifest['sprites'][0])
        linked.update(index=1, group=2, link=0)
        self.manifest['sprites'].append(linked)
        second = copy.deepcopy(self.manifest['actions'][0])
        second['action'] = 9
        second['frames'][0].update(group=2, sprite_index=1)
        self.manifest['actions'].append(second)
        self.assertEqual(self.codes(), [])
        self.assertEqual(summarize(self.manifest)['actions'][0]['frames'][0]['shared_with'], [9])

    def test_duplicate_sprite_and_invalid_link_are_reported(self):
        linked = copy.deepcopy(self.manifest['sprites'][0])
        linked['link'] = 99
        self.manifest['sprites'].append(linked)
        self.assertIn('duplicate_sprite', self.codes())
        self.assertIn('invalid_sprite_link', self.codes())

    def test_large_but_in_bounds_size_is_a_warning(self):
        self.manifest['localcoord'] = 1
        self.atlas.write_bytes(encode_png(5, 1, bytes([40,50,60,255])*5))
        self.manifest['sprites'][0]['region'].update(w=5, h=1)
        self.assertIn('unusual_size', self.codes())

    def test_bom_binding_name_cli_and_repeated_results(self):
        import json, subprocess, sys
        source, output = self.directory/'manifest.json', self.directory/'audit.json'
        preview = self.directory/'preview.html'
        source.write_text(json.dumps(self.manifest), encoding='utf8')
        (self.directory/'binding.json').write_text(json.dumps({'clips': {'idle': {'action': 8}}}), encoding='utf-8-sig')
        command = [sys.executable,'-m','mugen_importer.action_audit',str(source),str(output),'--preview',str(preview)]
        subprocess.run(command,check=True)
        first = output.read_bytes(), preview.read_bytes(), self.atlas.read_bytes()
        subprocess.run(command,check=True)
        self.assertEqual(first,(output.read_bytes(),preview.read_bytes(),self.atlas.read_bytes()))
        self.assertEqual(json.loads(output.read_text())['actions'][0]['name'],'binding: idle')

    def test_cli_refuses_source_overwrite(self):
        import json, subprocess, sys
        source = self.directory/'manifest.json'
        source.write_text(json.dumps(self.manifest), encoding='utf8')
        original = source.read_bytes(), self.atlas.read_bytes()
        for output in (source, self.atlas):
            result = subprocess.run([sys.executable,'-m','mugen_importer.action_audit',str(source),str(output)],capture_output=True)
            self.assertNotEqual(result.returncode,0)
            self.assertIn(b'must not overwrite',result.stderr)
        self.assertEqual(original,(source.read_bytes(),self.atlas.read_bytes()))

class AuditTests(unittest.TestCase):
    def test_action_details(self):
        f = lambda g, i, s, t: {'group':g, 'image':i, 'sprite_index':s, 'time':t, 'x':0, 'y':0, 'flip':''}
        sprite = {'group':0, 'image':0, 'region':{'page':0,'x':0,'y':0,'w':7,'h':9}, 'origin_x':-2, 'origin_y':8}
        a = lambda n, frames: {'action':n, 'loop_start':None, 'frames':frames}
        result = summarize({'character':'x', 'sprites':[sprite], 'actions':[a(1,[f(0,0,0,2),f(-1,-1,None,-1),f(5,6,None,1)]),a(1,[]),a(2,[f(0,0,0,1)])]})['actions'][0]
        self.assertTrue(result['duplicate'])
        self.assertTrue(result['infinite'])
        self.assertEqual(result['frames'][0]['shared_with'], [2])
        self.assertEqual(result['frames'][0]['origin'], [-2,8])
        self.assertTrue(result['frames'][1]['blank'])
        self.assertFalse(result['frames'][1]['missing'])
        self.assertTrue(result['frames'][2]['missing'])

    def test_index_mismatch_and_shared_images_are_distinguished(self):
        frame = {'group': 3, 'image': 4, 'sprite_index': 0, 'time': 2, 'x': 0, 'y': 0, 'flip': ''}
        sprite = {'group': 9, 'image': 9, 'region': {'page': 0, 'x': 0, 'y': 0, 'w': 7, 'h': 9}, 'origin_x': -2, 'origin_y': 8}
        action = lambda n: {'action': n, 'loop_start': None, 'frames': [frame]}
        result = summarize({'character': 'x', 'sprites': [sprite], 'actions': [action(1), action(2)]})
        self.assertTrue(result['actions'][0]['frames'][0]['index_mismatch'])
        self.assertEqual(result['actions'][0]['frames'][0]['shared_with'], [2])

    def test_atlas_diagnostics_identify_frame_and_missing_file(self):
        import tempfile
        from pathlib import Path
        from mugen_importer.action_audit import audit_assets
        frame = {'group': 1, 'image': 2, 'sprite_index': 0, 'time': 1, 'x': 0, 'y': 0, 'flip': ''}
        sprite = {'group': 1, 'image': 2, 'region': {'page': 3, 'x': 0, 'y': 0, 'w': 4, 'h': 5}, 'origin_x': 0, 'origin_y': 0}
        manifest = {'character': 'x', 'sprites': [sprite], 'actions': [{'action': 8, 'loop_start': None, 'frames': [frame]}]}
        with tempfile.TemporaryDirectory() as directory:
            result = audit_assets(manifest, Path(directory))
        self.assertEqual(result[0]['action'], 8)
        self.assertEqual(result[0]['frame'], 0)
        self.assertEqual(result[0]['code'], 'missing_atlas')
        self.assertEqual(result[0]['path'], 'atlas-003.png')

    def test_cli_includes_atlas_diagnostics(self):
        import json, subprocess, sys, tempfile
        from pathlib import Path
        frame = {'group': 1, 'image': 2, 'sprite_index': 0, 'time': 1, 'x': 0, 'y': 0, 'flip': ''}
        sprite = {'group': 1, 'image': 2, 'region': {'page': 3, 'x': 0, 'y': 0, 'w': 4, 'h': 5}, 'origin_x': 0, 'origin_y': 0}
        manifest = {'character': 'x', 'sprites': [sprite], 'actions': [{'action': 8, 'loop_start': None, 'frames': [frame]}]}
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / 'manifest.json'
            output = Path(directory) / 'audit.json'
            source.write_text(json.dumps(manifest), encoding='utf8')
            subprocess.run([sys.executable, '-m', 'mugen_importer.action_audit', str(source), str(output)], check=True)
            result = json.loads(output.read_text(encoding='utf8'))
        self.assertEqual(result['diagnostics'][0]['code'], 'missing_atlas')

    def test_preview_contains_frames_and_uses_manifest_atlas(self):
        from pathlib import Path
        from mugen_importer.action_audit import render_preview
        frame = {'group': 1, 'image': 2, 'sprite_index': 0, 'time': 3, 'x': 0, 'y': 0, 'flip': ''}
        sprite = {'group': 1, 'image': 2, 'region': {'page': 0, 'x': 0, 'y': 0, 'w': 4, 'h': 5}, 'origin_x': 0, 'origin_y': 0}
        manifest = {'character': 'x', 'sprites': [sprite], 'actions': [{'action': 8, 'loop_start': None, 'frames': [frame]}]}
        html = render_preview(manifest, Path('C:/assets'))
        self.assertIn('file:///C:/assets/', html)
        self.assertIn('padStart(3', html)
        self.assertIn('canvas', html)
        self.assertIn('"time": 3', html)
        self.assertIn('"action": 8', html)

