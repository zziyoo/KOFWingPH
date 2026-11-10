"""Generate a deterministic, visual-only summary of an imported MUGEN manifest."""
import argparse
import base64
import copy
import json
import struct
import zlib
from collections import Counter, defaultdict
from pathlib import Path


def valid_frame(frame):
    return (isinstance(frame, dict) and
            all(type(frame.get(k)) is int for k in ('group', 'image', 'time', 'x', 'y')) and
            isinstance(frame.get('flip'), str))


def valid_sprite(sprite):
    return (isinstance(sprite, dict) and
            all(type(sprite.get(k)) is int for k in ('group', 'image', 'origin_x', 'origin_y')) and
            isinstance(sprite.get('region'), dict) and
            all(type(sprite['region'].get(k)) is int for k in ('page', 'x', 'y', 'w', 'h')))


def summarize(manifest):
    actions = manifest['actions']
    sprites = manifest['sprites']
    counts = Counter(a['action'] for a in actions)
    owners = defaultdict(set)
    region_owners = defaultdict(set)
    for action in actions:
        for frame in action['frames']:
            if not valid_frame(frame):
                continue
            index = frame.get('sprite_index')
            if type(index) is int:
                owners[index].add(action['action'])
                if 0 <= index < len(sprites) and valid_sprite(sprites[index]):
                    region_owners[tuple(sprites[index]['region'][k] for k in ('page','x','y','w','h'))].add(action['action'])
    result = []
    for action in actions:
        frames = []
        for frame in action['frames']:
            if not valid_frame(frame):
                frames.append({'malformed': True, 'time': 0})
                continue
            index = frame.get('sprite_index')
            blank = frame['group'] < 0 or frame['image'] < 0
            sprite = sprites[index] if type(index) is int and 0 <= index < len(sprites) else None
            if sprite is not None and not valid_sprite(sprite):
                sprite = None
            frames.append({'group': frame['group'], 'image': frame['image'],
                           'time': frame['time'], 'offset': [frame['x'], frame['y']],
                           'flip': frame['flip'], 'blank': blank,
                           'missing': sprite is None and not blank,
                           'index_mismatch': bool(sprite and 'group' in sprite and
                                                  (sprite['group'], sprite['image']) !=
                                                  (frame['group'], frame['image'])),
                           'path': f"atlas-{sprite['region'].get('page', 0):03d}.png" if sprite else None,
                           'size': [sprite['region']['w'], sprite['region']['h']] if sprite else None,
                           'origin': [sprite['origin_x'], sprite['origin_y']] if sprite else None,
                           'shared_with': sorted((owners[index] | region_owners[tuple(sprite['region'][k] for k in ('page','x','y','w','h'))]) - {action['action']}) if sprite else []})
        result.append({'action': action['action'], 'name': action.get('name', 'unnamed (Manifest has no label)'), 'duplicate': counts[action['action']] > 1,
                       'frame_count': len(frames), 'finite_ticks': sum(max(0, f['time']) for f in frames),
                       'infinite': any(f['time'] == -1 for f in frames),
                       'loop_start': action['loop_start'], 'frames': frames})
    return {'character': manifest['character'], 'actions': result}


def audit_assets(manifest, directory):
    """Check importer-produced RGBA atlas pages without modifying source files."""
    diagnostics = []
    sprites = manifest['sprites']
    pages = {}
    sprite_keys = {}
    for index, sprite in enumerate(sprites):
        if not valid_sprite(sprite):
            continue  # Referencing frames receive a located malformed_sprite diagnostic.
        key = sprite['group'], sprite['image']
        if key in sprite_keys:
            diagnostics.append({'code': 'duplicate_sprite', 'sprite_index': index, 'first_index': sprite_keys[key],
                                'group': key[0], 'image': key[1]})
        else:
            sprite_keys[key] = index
        link = sprite.get('link')
        if link is not None and (type(link) is not int or not 0 <= link < index or
                                 not valid_sprite(sprites[link]) or sprite['region'] != sprites[link]['region']):
            diagnostics.append({'code': 'invalid_sprite_link', 'sprite_index': index, 'link': link})
    counts = Counter(a['action'] for a in manifest['actions'])
    for occurrence, action in enumerate(manifest['actions']):
        def report(code, number=None, **details):
            diagnostics.append({'action': action['action'], 'occurrence': occurrence,
                                'frame': number, 'code': code, **details})
        if counts[action['action']] > 1:
            report('duplicate_action')
        if not action['frames']:
            report('empty_action')
        loop = action.get('loop_start')
        if loop is not None and (type(loop) is not int or not 0 <= loop < len(action['frames'])):
            report('invalid_loop')
        for number, frame in enumerate(action['frames']):
            if not valid_frame(frame):
                report('malformed_frame', number)
                continue
            if frame['time'] < -1:
                report('invalid_duration', number)
            if frame['group'] < 0 or frame['image'] < 0:
                report('intentional_blank', number, severity='info')
                continue
            index = frame.get('sprite_index')
            if type(index) is not int or not 0 <= index < len(sprites):
                report('missing_sprite', number, group=frame['group'], image=frame['image'])
                continue
            sprite = sprites[index]
            if not valid_sprite(sprite):
                report('malformed_sprite', number, sprite_index=index)
                continue
            if (sprite['group'], sprite['image']) != (frame['group'], frame['image']):
                report('sprite_index_mismatch', number, sprite_index=index)
            region = sprite['region']
            page = region['page']
            if page < 0 or page > 10000:
                report('invalid_page', number, page=page)
                continue
            path = f'atlas-{page:03d}.png'
            if not (directory / path).is_file():
                report('missing_atlas', number, path=path)
                continue
            if page not in pages:
                try:
                    pages[page] = read_atlas(directory / path)
                except (ValueError, OSError, zlib.error, struct.error) as error:
                    pages[page] = str(error)
            image = pages[page]
            if isinstance(image, str):
                report('invalid_atlas', number, path=path, detail=image)
                continue
            width, height, pixels = image
            x, y, w, h = (region[k] for k in ('x', 'y', 'w', 'h'))
            if min(x, y) < 0 or min(w, h) <= 0 or x+w > width or y+h > height:
                report('invalid_region', number, path=path, region=region)
                continue
            if max(w, h) > manifest.get('localcoord', 320) * 4:
                report('unusual_size', number, severity='warning', size=[w,h])
            if not any(any(pixels[(row*width+x)*4+3:(row*width+x+w)*4:4]) for row in range(y,y+h)):
                report('transparent_sprite', number, path=path, sprite_index=index)
            if abs(sprite['origin_x']) > w*4 or abs(sprite['origin_y']) > h*4:
                report('unusual_origin', number, severity='warning', sprite_index=index,
                       origin=[sprite['origin_x'], sprite['origin_y']])
    return diagnostics


def read_atlas(path):
    """Read the deterministic RGBA/filter-0 PNG format produced by this importer."""
    raw = path.read_bytes()
    if raw[:8] != b'\x89PNG\r\n\x1a\n':
        raise ValueError('invalid PNG signature')
    width, height, depth, color, compression, filtering, interlace = struct.unpack('>IIBBBBB', raw[16:29])
    if not 0 < width <= 4096 or not 0 < height <= 4096 or (depth,color,compression,filtering,interlace) != (8,6,0,0,0):
        raise ValueError('unsupported atlas PNG format or size')
    offset, chunks, ended = 8, [], False
    while offset < len(raw):
        length = struct.unpack('>I',raw[offset:offset+4])[0]
        kind, payload = raw[offset+4:offset+8], raw[offset+8:offset+8+length]
        crc = struct.unpack('>I',raw[offset+8+length:offset+12+length])[0]
        if zlib.crc32(kind+payload) != crc:
            raise ValueError('PNG CRC mismatch')
        if kind == b'IDAT': chunks.append(payload)
        offset += length+12
        if kind == b'IEND':
            ended = True
            break
    if not ended:
        raise ValueError('missing PNG IEND')
    stride = width*4+1
    decoder = zlib.decompressobj()
    decoded = decoder.decompress(b''.join(chunks), stride*height+1)
    if not decoder.eof:
        raise ValueError('truncated or oversized PNG pixel stream')
    if len(decoded) != stride*height or any(decoded[i*stride] for i in range(height)):
        raise ValueError('invalid atlas scanlines (expected importer filter 0)')
    return width,height,b''.join(decoded[i*stride+1:(i+1)*stride] for i in range(height))


def render_preview(manifest, directory):
    """Render a local, self-contained action browser using the original atlases."""
    assets = {}
    for sprite in manifest['sprites']:
        if valid_sprite(sprite) and 0 <= sprite['region']['page'] <= 10000:
            name = f"atlas-{sprite['region']['page']:03d}.png"
            path = directory / name
            if name not in assets and path.is_file():
                assets[name] = 'data:image/png;base64,' + base64.b64encode(path.read_bytes()).decode('ascii')
    actions = copy.deepcopy(manifest['actions'])
    for action in actions:
        action['frames'] = [frame if valid_frame(frame) else
                            {'group': -1, 'image': -1, 'sprite_index': None, 'time': -2,
                             'x': 0, 'y': 0, 'flip': '', 'malformed': True}
                            for frame in action['frames']]
    data = {'actions': actions, 'sprites': manifest['sprites'], 'assets': assets,
            'base': directory.resolve().as_uri() + '/',
            'diagnostics': audit_assets(manifest, directory)}
    encoded = json.dumps(data, ensure_ascii=True).replace('<', '\\u003c')
    return Path(__file__).with_name('action_preview.html').read_text(encoding='utf8').replace('DATA', encoded)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('manifest', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--preview', type=Path)
    args = parser.parse_args()
    protected = {args.manifest.resolve(), (args.manifest.parent / 'binding.json').resolve()}
    protected.update(path.resolve() for path in args.manifest.parent.glob('atlas-*'))
    destinations = [args.output.resolve()] + ([args.preview.resolve()] if args.preview else [])
    if any(path in protected for path in destinations) or len(set(destinations)) != len(destinations):
        parser.error('output and preview must be distinct and must not overwrite source resources')
    data = json.loads(args.manifest.read_text(encoding='utf8'))
    binding_path = args.manifest.parent / 'binding.json'
    if binding_path.is_file():
        binding = json.loads(binding_path.read_text(encoding='utf-8-sig'))
        names = defaultdict(list)
        for name, clip in binding.get('clips', {}).items():
            names[clip if isinstance(clip, int) else clip['action']].append(name)
        for action in data['actions']:
            if action['action'] in names:
                action['name'] = 'binding: ' + ', '.join(names[action['action']])
    result = summarize(data)
    result['source_diagnostics'] = data.get('diagnostics', [])
    result['diagnostics'] = audit_assets(data, args.manifest.parent)
    args.output.write_text(json.dumps(result, indent=2, ensure_ascii=True) + '\n', encoding='utf8', newline='\n')
    if args.preview:
        args.preview.write_text(render_preview(data, args.manifest.parent), encoding='utf8', newline='\n')


if __name__ == '__main__':
    main()
