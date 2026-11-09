"""Read AIR actions in source order, preserving collision declarations as reference."""
import re

_ACTION = re.compile(r'^\[\s*begin\s+action\s+(-?\d+)\s*\]$', re.I)
_CLSN = re.compile(r'^clsn[12](?:default)?(?:\s*\[\s*\d+\s*\])?\s*[:=]', re.I)


def parse_air(text):
    actions, diagnostics, seen = [], [], set()
    current = None
    for line_no, raw in enumerate(text.splitlines(), 1):
        line = raw.split(';', 1)[0].strip()
        if not line:
            continue
        match = _ACTION.match(line)
        if match:
            number = int(match.group(1))
            if number in seen:
                diagnostics.append(f'line {line_no}: duplicate action {number}')
            seen.add(number)
            current = {'action': number, 'frames': [], 'loop_start': None, 'collisions': []}
            actions.append(current)
        elif line.startswith('['):
            current = None
        elif current is None:
            continue
        elif line.lower() == 'loopstart':
            current['loop_start'] = len(current['frames'])
        elif _CLSN.match(line):
            current['collisions'].append({'before_frame': len(current['frames']), 'source': line})
        elif ',' in line:
            parts = [p.strip() for p in line.split(',')]
            try:
                group, image, x, y, time = map(int, parts[:5])
                if time < -1:
                    raise ValueError('time below -1')
                flip = parts[5].upper() if len(parts) > 5 and parts[5].upper() in ('H', 'V', 'HV', 'VH') else ''
                current['frames'].append({'group': group, 'image': image, 'x': x, 'y': y,
                                          'time': time, 'flip': flip})
            except (ValueError, IndexError) as ex:
                diagnostics.append(f'line {line_no}: invalid frame: {ex}: {line}')
    return actions, diagnostics
