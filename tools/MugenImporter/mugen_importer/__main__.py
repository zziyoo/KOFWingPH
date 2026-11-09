"""Import named visual members from a ZIP without extracting archive paths."""
import argparse
import hashlib
import json
import re
import zipfile
from pathlib import Path
from .air import parse_air
from .atlas import Atlas
from .pcx import decode_pcx, read_act
from .sff import read_sff

def localcoord(definition):
    match = re.search(r'^\s*localcoord\s*=\s*(\d+)', definition.decode('latin1'), re.I | re.M)
    value = int(match.group(1)) if match else 320
    if not 1 <= value <= 8192:
        raise ValueError('invalid localcoord width')
    return value

def member(archive, suffix, required=True):
    matches = [i for i in archive.infolist() if i.filename.lower().endswith(suffix.lower()) and not i.is_dir()]
    if not matches and not required:
        return None
    if len(matches) != 1:
        raise ValueError(f'expected one ZIP member ending in {suffix}, found {len(matches)}')
    info = matches[0]
    if info.file_size > 128 * 1024 * 1024:
        raise ValueError(f'ZIP member too large: {suffix}')
    return archive.read(info)

def run(zip_path, output, character, act='act/act01.act', atlas_size=2048,
        sff_suffix='.sff', air_suffix='.air', def_suffix='.def'):
    if not re.fullmatch(r'[a-z0-9_-]+', character):
        raise ValueError('character ID must contain only lowercase ASCII letters, digits, _ or -')
    if atlas_size < 32 or atlas_size > 4096:
        raise ValueError('atlas size must be between 32 and 4096')
    if output.exists() and any(output.iterdir()):
        raise ValueError('output directory must be empty to avoid stale generated files')
    output.mkdir(parents=True, exist_ok=True)
    archive_digest = hashlib.sha256()
    with zip_path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            archive_digest.update(chunk)
    with zipfile.ZipFile(zip_path) as archive:
        sff_bytes = member(archive, sff_suffix)
        air_bytes = member(archive, air_suffix)
        def_bytes = member(archive, def_suffix)
        act_bytes = member(archive, act) if act else None
    act_palette = read_act(act_bytes) if act_bytes else None
    sprites = read_sff(sff_bytes)
    actions, diagnostics = parse_air(air_bytes.decode('latin1'))
    atlas = Atlas(output, atlas_size)
    records, keys, shared_palette = [], {}, None
    for i, sprite in enumerate(sprites):
        key = (sprite.group, sprite.image)
        if key in keys:
            diagnostics.append(f'duplicate sprite {key[0]},{key[1]} at {i}; first index {keys[key]} used')
        else:
            keys[key] = i
        if sprite.linked_to is not None:
            region = records[sprite.linked_to]['region']
        else:
            palette = (act_palette or shared_palette) if sprite.same_palette else None
            image = decode_pcx(sprite.pcx, palette)
            if not sprite.same_palette:
                shared_palette = image.palette
            region = atlas.add(image)
        records.append({'index': i, 'group': sprite.group, 'image': sprite.image,
                        'origin_x': sprite.x, 'origin_y': sprite.y, 'link': sprite.linked_to,
                        'same_palette': sprite.same_palette, 'region': region})
    atlas.flush()
    for action in actions:
        for index, frame in enumerate(action['frames']):
            key = (frame['group'], frame['image'])
            frame['sprite_index'] = keys.get(key) if key[0] >= 0 and key[1] >= 0 else None
            if frame['sprite_index'] is None and key[0] >= 0 and key[1] >= 0:
                diagnostics.append(f"action {action['action']} frame {index}: missing sprite {key[0]},{key[1]}")
    digest = lambda b: hashlib.sha256(b).hexdigest() if b is not None else None
    manifest = {'format': 1, 'importer': '0.2', 'character': character, 'localcoord': localcoord(def_bytes),
                'sources': {'zip_sha256': archive_digest.hexdigest(), 'sff_sha256': digest(sff_bytes), 'air_sha256': digest(air_bytes),
                            'act_sha256': digest(act_bytes), 'def_sha256': digest(def_bytes)},
                'options': {'palette': act, 'atlas_size': atlas.size, 'sff_suffix': sff_suffix,
                            'air_suffix': air_suffix, 'def_suffix': def_suffix},
                'sprites': records, 'actions': actions, 'diagnostics': diagnostics}
    (output / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=True, indent=2) + '\n', encoding='utf8', newline='\n')
    print(f'{len(sprites)} sprites, {sum(s.linked_to is not None for s in sprites)} links, '
          f'{len(actions)} actions, {sum(len(a["frames"]) for a in actions)} frames; '
          f'{len(diagnostics)} diagnostics, {atlas.page+1} atlas pages')

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('zip_path', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--character', required=True)
    parser.add_argument('--act', default='act/act01.act', help='ZIP suffix, or empty for embedded palette')
    parser.add_argument('--sff', default='.sff', help='unique ZIP member suffix')
    parser.add_argument('--air', default='.air', help='unique ZIP member suffix')
    parser.add_argument('--def-file', default='.def', help='unique ZIP member suffix')
    parser.add_argument('--atlas-size', type=int, default=2048)
    args = parser.parse_args()
    run(args.zip_path, args.output, args.character, args.act, args.atlas_size,
        args.sff, args.air, args.def_file)

if __name__ == '__main__':
    main()
