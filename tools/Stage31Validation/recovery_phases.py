"""Export signed native phases alongside unmodified before/after runtime audit rows."""
import argparse
import hashlib
import json
from pathlib import Path
import re


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument('--native', required=True, type=Path)
    parser.add_argument('--before', required=True, type=Path)
    parser.add_argument('--after', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    report = {'mapping': 'L=F+14; submitted state version S=L+1', 'sha256': {}, 'frames': {}}
    for path in (args.native, args.before, args.after):
        report['sha256'][str(path)] = hashlib.sha256(path.read_bytes()).hexdigest()
    for line in args.native.read_text(encoding='utf-8').splitlines():
        row = json.loads(line)
        if row['phase'] not in ('before-command', 'after-collision', 'end'):
            continue
        f = row['frame']
        frame = report['frames'].setdefault(f, {'F': f, 'L': f + 14, 'S': f + 15})
        frame.setdefault('native', {})[row['phase']] = row['chars']
    for key in ('before', 'after'):
        for line in getattr(args, key).read_text(encoding='utf-8-sig').splitlines():
            if line.startswith('[FrameCapture]'):
                capture = re.search(r'render=(\d+) state=(\d+) path=(.*?) result=(\w+)', line)
                if capture and (f := int(capture[2]) - 15) in report['frames']:
                    path = Path(capture[3])
                    report['frames'][f].setdefault(key + 'Submissions', []).append({
                        'render': int(capture[1]), 'S': int(capture[2]), 'path': str(path),
                        'result': capture[4], 'pngSha256': hashlib.sha256(path.read_bytes()).hexdigest()})
                continue
            if not line.startswith(('[MoveAudit]', '[ReentryAudit]', '[SpriteFrameAudit]', '[SpriteRenderAudit]', '[VisualAudit]', '[CommandAudit]')):
                continue
            match = re.search(r'(?:logic|frame)=(\d+)', line)
            # Render projection receives BattleState.Frame (S); ObserveFrame and
            # settlement diagnostics receive PresentationFrame.LogicFrame (L).
            offset = 15 if line.startswith('[SpriteRenderAudit]') else 14
            if match and (f := int(match[1]) - offset) in report['frames']:
                report['frames'][f].setdefault(key, []).append(line)
    # Exclusive creation keeps prior failure evidence intact.
    with args.output.open('x', encoding='utf-8') as output:
        json.dump(report, output, indent=2)
        output.write('\n')


if __name__ == '__main__':
    main()
