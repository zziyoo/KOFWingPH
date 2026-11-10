"""Explicit, headed Edge acceptance; requires `python -m pip install playwright`.

Run from tools/MugenImporter: python browser_acceptance.py --manifest ../../data/imported-characters/terry98/manifest.json --evidence ../../shots/13c-a
No connection to the user's existing browser or URL detection is used.
"""
import argparse
import copy
import hashlib
import json
import subprocess
import sys
import tempfile
from pathlib import Path
from playwright.sync_api import sync_playwright
from mugen_importer.action_audit import render_preview, summarize, audit_assets
from mugen_importer.png import encode_png


def run(manifest_path, evidence):
    evidence.mkdir(parents=True, exist_ok=True)
    checks, errors = [], []

    def check(name, condition):
        if not condition:
            raise AssertionError(name)
        checks.append(name)

    with tempfile.TemporaryDirectory(prefix='newkof-preview-') as temporary, sync_playwright() as pw:
        directory = Path(temporary)
        # Asymmetric two-pixel frame permits exact mirror verification.
        (directory / 'atlas-000.png').write_bytes(encode_png(2, 1, bytes([255,0,0,255,0,255,0,255])))
        frame = {'group': 1, 'image': 0, 'sprite_index': 0, 'time': 2, 'x': 3, 'y': 0, 'flip': ''}
        sprite = {'group': 1, 'image': 0, 'origin_x': 1, 'origin_y': 1, 'region': {'page': 0, 'x': 0, 'y': 0, 'w': 2, 'h': 1}}
        action = {'action': 8, 'name': 'fixture binding', 'loop_start': 1, 'frames': []}
        action['frames'] = [dict(frame, time=t) for t in (0,0,2,3,-1)]
        actions = [action, dict(action, action=9, frames=[dict(frame, time=t) for t in (2,3)]),
                   dict(action, action=10, frames=[dict(frame, time=0), dict(frame, time=0)]),
                   dict(action, action=11, frames=[dict(frame, sprite_index=None)]),
                   dict(action, action=12, frames=[dict(frame, time='bad')]),
                   dict(action, action=13, frames=[dict(frame, image=-1)]),
                   dict(action, action=14, frames=[dict(frame, sprite_index=1)]),
                   dict(action, action=15, frames=[dict(frame, time=t) for t in (2,3,4)], loop_start=1)]
        actions.extend([dict(action,action=16,frames=[None]),
                        dict(action,action=17,frames=[dict(frame,sprite_index=2)]),
                        dict(action,action=18,frames=[dict(frame,sprite_index=3)]),
                        dict(action,action=19,frames=[dict(frame,group=99)]),
                        dict(action,action=20,frames=[dict(frame,time=t) for t in (2,-1,3)])])
        bad_sprite = copy.deepcopy(sprite)
        bad_sprite['region']['page'] = 3
        out_of_bounds = copy.deepcopy(sprite)
        out_of_bounds['region']['w'] = 99
        corrupt = copy.deepcopy(sprite)
        corrupt['region']['page'] = 2
        (directory/'atlas-002.png').write_bytes(b'bad png')
        manifest = {'character': 'fixture', 'sprites': [sprite, bad_sprite, out_of_bounds, corrupt], 'actions': actions}
        preview = directory / 'preview.html'
        preview.write_text(render_preview(manifest, directory), encoding='utf8')
        browser = pw.chromium.launch(channel='msedge', headless=False)
        try:
            page = browser.new_page(viewport={'width': 1200, 'height': 1000})
            page.on('pageerror', lambda error: errors.append(str(error)))
            page.clock.install()
            page.goto(preview.as_uri())
            page.locator('#pause').click()
            page.locator('#action').select_option('0')
            page.clock.run_for(32)
            page.wait_for_function("document.querySelector('#view').dataset.drawn==='true'")
            check('index and binding label', page.locator('#action option').count() == len(actions) and 'fixture binding' in page.locator('#action option').first.inner_text())
            check('consecutive zero frames skipped', page.locator('#info').get_attribute('data-frame') == '2')
            before = page.locator('#view').screenshot()
            tick = page.locator('#info').get_attribute('data-tick')
            page.clock.run_for(1000)
            check('pause holds pixels and tick', before == page.locator('#view').screenshot() and tick == page.locator('#info').get_attribute('data-tick'))
            page.locator('#pause').click()
            playing_tick = int(page.locator('#info').get_attribute('data-tick'))
            page.clock.run_for(100)
            advanced = int(page.locator('#info').get_attribute('data-tick'))-playing_tick
            check('playing clock advances at 60Hz', 5 <= advanced <= 7)
            page.locator('#pause').click()
            page.locator('#action').select_option('0')
            page.locator('#tick').click()
            check('first duration tick holds frame', page.locator('#info').get_attribute('data-frame') == '2')
            page.locator('#tick').click()
            check('duration boundary advances', page.locator('#info').get_attribute('data-frame') == '3')
            page.locator('#step').click()
            check('next frame advances exactly once', page.locator('#info').get_attribute('data-frame') == '4')
            page.locator('#tick').click()
            check('infinite final frame holds', page.locator('#info').get_attribute('data-frame') == '4')
            normal = page.evaluate("Array.from(document.querySelector('#view').getContext('2d').getImageData(322,399,2,1).data)")
            page.locator('#mirror').check()
            mirrored = page.evaluate("Array.from(document.querySelector('#view').getContext('2d').getImageData(316,399,2,1).data)")
            check('mirror pixels and offset', normal == [255,0,0,255,0,255,0,255] and mirrored == [0,255,0,255,255,0,0,255])
            page.screenshot(path=str(evidence / 'fixture-mirror.png'))
            page.locator('#action').select_option('1')
            page.locator('#loop').uncheck()
            for _ in range(8): page.locator('#tick').click()
            check('nonloop final frame holds', page.locator('#info').get_attribute('data-frame') == '1')
            page.locator('#step').click()
            check('nonloop next frame holds last', page.locator('#info').get_attribute('data-frame') == '1')
            page.locator('#loop').check()
            for _ in range(5): page.locator('#tick').click()
            check('loop boundary returns to loop start', page.locator('#info').get_attribute('data-frame') == '1')
            page.locator('#action').select_option('7')
            for _ in range(9): page.locator('#tick').click()
            check('nonzero loop start boundary', page.locator('#info').get_attribute('data-frame') == '1')
            for option, message in [('2','No playable'), ('3','Missing sprite'), ('4','No playable'), ('5','Intentional blank'), ('6','Missing or corrupt atlas'), ('8','No playable'), ('9','Invalid atlas region'), ('10','Missing or corrupt atlas'), ('11','Sprite index does not match')]:
                page.locator('#action').select_option(option)
                if option in ('6','10'): page.wait_for_function("document.querySelector('#error').textContent.includes('corrupt')")
                check('error isolation '+option, message in page.locator('#error').inner_text())
            page.locator('#action').select_option('0')
            check('valid action recovers after errors', page.locator('#view').get_attribute('data-drawn') == 'true')
            page.locator('#action').select_option('12')
            page.locator('#step').click()
            check('manual step reaches intermediate infinite hold',page.locator('#info').get_attribute('data-frame')=='1')
            page.locator('#step').click()
            check('manual step can inspect frames after infinite hold',page.locator('#info').get_attribute('data-frame')=='2')

            # Real Terry: create output twice, compare every source byte hash and output byte hash.
            source_hashes = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in manifest_path.parent.iterdir() if p.is_file()}
            output, real_html = evidence / 'terry-audit.json', evidence / 'terry-preview.html'
            command = [sys.executable, '-m', 'mugen_importer.action_audit', str(manifest_path), str(output), '--preview', str(real_html)]
            subprocess.run(command, check=True)
            first = (output.read_bytes(), real_html.read_bytes())
            subprocess.run(command, check=True)
            check('repeat output byte stability', first == (output.read_bytes(), real_html.read_bytes()))
            check('all original resources unchanged', source_hashes == {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in manifest_path.parent.iterdir() if p.is_file()})
            original = json.loads(manifest_path.read_text(encoding='utf8'))
            report = json.loads(output.read_text(encoding='utf8'))
            check('real action and frame counts match', len(original['actions']) == len(report['actions']) and all(len(a['frames']) == b['frame_count'] for a,b in zip(original['actions'],report['actions'])))
            check('real durations and IDs match', all(a['action']==b['action'] and [f['time'] for f in a['frames']]==[f['time'] for f in b['frames']] for a,b in zip(original['actions'],report['actions'])))
            check('real sprite data matches', all(
                f['offset']==[o['x'],o['y']] and f['group']==o['group'] and f['image']==o['image'] and
                (o['sprite_index'] is None or (f['origin']==[original['sprites'][o['sprite_index']]['origin_x'],original['sprites'][o['sprite_index']]['origin_y']] and
                 f['size']==[original['sprites'][o['sprite_index']]['region']['w'],original['sprites'][o['sprite_index']]['region']['h']]))
                for a,b in zip(original['actions'],report['actions']) for o,f in zip(a['frames'],b['frames'])))
            page.goto(real_html.resolve().as_uri())
            page.locator('#pause').click()
            page.wait_for_function("document.querySelector('#view').dataset.drawn==='true'")
            check('real Terry index loads', page.locator('#action option').count() == len(original['actions']))
            check('real idle binding name', 'binding: idle' in page.locator('#action option').first.inner_text())
            page.screenshot(path=str(evidence / 'terry-idle.png'))
            attack = next(i for i,a in enumerate(original['actions']) if a['action'] == 200)
            page.locator('#action').select_option(str(attack))
            page.locator('#step').click()
            check('real action switch and step', 'Action 200' in page.locator('#info').inner_text() and page.locator('#info').get_attribute('data-frame') == '1' and page.locator('#view').get_attribute('data-drawn') == 'true')
            page.locator('#mirror').check()
            page.screenshot(path=str(evidence / 'terry-attack-mirrored.png'))
            missing = next(i for i,a in enumerate(original['actions']) if any(f['sprite_index'] is None and f['group']>=0 and f['image']>=0 for f in a['frames']))
            page.locator('#action').select_option(str(missing))
            for _ in range(len(original['actions'][missing]['frames'])):
                if 'Missing sprite' in page.locator('#error').inner_text(): break
                page.locator('#step').click()
            check('real missing resource is diagnosed', 'Missing sprite' in page.locator('#error').inner_text())
            page.screenshot(path=str(evidence / 'terry-missing.png'))
            check('no JavaScript exceptions', not errors)
            result = {'url': real_html.resolve().as_uri(), 'browser': browser.version, 'headed': True,
                      'checks': checks, 'passed': len(checks), 'page_errors': errors, 'source_sha256': source_hashes,
                      'action_count': len(original['actions']), 'frame_count': sum(len(a['frames']) for a in original['actions']),
                      'diagnostic_count': len(report['diagnostics'])}
            (evidence / 'browser-results.json').write_text(json.dumps(result, indent=2), encoding='utf8')
            print(json.dumps(result, indent=2))
        finally:
            browser.close()


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', type=Path, required=True)
    parser.add_argument('--evidence', type=Path, required=True)
    args = parser.parse_args()
    run(args.manifest.resolve(), args.evidence.resolve())
