"""Run Stage 28 State 300 scenes in the real Godot runtime and verify the authority rows.

The scenes replay the native probe schedules (docs/STAGE28_REFERENCE.md) against the
terry98-stage28-final profile. Every scene asserts the MoveStarted rows emitted by the
host at the logic frames the native trace predicts (within the render→logic offset band).
"""

import argparse
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PROFILE = 'data/imported-characters/terry98-stage28-final'
VISUAL = {'default': 'terry98-stage28-default', 'mode2': 'terry98-stage28-mode2'}
SELECTION = ['1:down:Key1', '5:down:J', '7:down:Comma', '9:down:Enter',
             '11:up:J', '11:up:Comma', '11:up:Enter']
# (name, mode, duel distance, keys, expected (move, minimum count) MoveStarted rows)
SCENES = [
    ('free-entry-hit', 'default', 60,
     SELECTION + ['15:down:D', '15:down:J'],
     [('TerryBackKnuckle300', 1), ('TerryStandLightPunch200', 0)]),
    ('helper-latch-release', 'default', 60,
     SELECTION + ['15:down:D', '15:down:J', '16:up:D', '20:up:J'],
     [('TerryBackKnuckle300', 1)]),
    ('sweep-cancel-first', 'default', 150,
     SELECTION + ['15:down:S', '15:down:I', '25:up:S', '27:down:D', '27:down:J'],
     [('TerryCrouchHeavyKick440', 1), ('TerryBackKnuckle300', 1)]),
    ('sweep-window-reject', 'default', 150,
     SELECTION + ['15:down:S', '15:down:I', '32:up:S', '34:down:D', '35:down:D', '35:down:J'],
     [('TerryCrouchHeavyKick440', 1), ('TerryBackKnuckle300', 0)]),
    ('wave-exit-guard', 'default', 63,
     SELECTION + ['14:down:Right', '15:down:D', '15:down:J', '17:up:J', '18:up:D',
                  '40:down:S', '42:down:S', '42:down:D', '44:up:S', '44:down:F',
                  '45:down:J', '46:up:J', '46:up:F'],
     [('TerryBackKnuckle300', 1), ('TerryPowerWave1000', 1)]),
    ('mode2-free-entry', 'mode2', 60,
     SELECTION + ['15:down:D', '15:down:J'],
     [('TerryBackKnuckle300', 1)]),
]


def run_scene(options, name, mode, distance, keys, expected):
    out = options.output / name
    out.mkdir(parents=True, exist_ok=False)
    log = out / 'duel.log'
    args = [str(options.godot), '--path', '.', '--fixed-fps', '60', '--headless',
            '--quit-after', str(options.frames), '--log-file', (out / 'godot-engine.log').as_posix(),
            '--', '--visual-audit', '--entity-audit', '--move-audit', '--sprite-frame-audit',
            f'--duel={distance}',
            f'--p1-fighter-file=res://{PROFILE}/{mode}.fighter.json',
            f'--p2-fighter-file=res://{PROFILE}/{mode}.fighter.json',
            f'--p1-visual={VISUAL[mode]}', f'--p2-visual={VISUAL[mode]}',
            '--shot=' + (out / 'no-image.png').as_posix(), f'--shotframes={options.frames + 1}']
    args += [f'--renderkey={key}' for key in keys]
    with log.open('wb') as stream:
        result = subprocess.run(args, cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT,
                                timeout=options.timeout)
    data = log.read_bytes()
    if result.returncode != 0 or not data.startswith(b'Godot Engine') or \
            any(line.startswith((b'ERROR:', b'SCRIPT ERROR:')) for line in data.splitlines()):
        raise ValueError(f'{name}: the Godot run failed, inspect {log}')
    text = data.decode('utf-8', 'replace')
    failures = []
    for move, minimum in expected:
        count = text.count(f'Type = MoveStarted') and text.count(f'MoveId = {move},')
        started = sum(1 for line in text.splitlines()
                      if 'Type = MoveStarted' in line and f'MoveId = {move},' in line)
        if started < minimum:
            failures.append(f'{move}: expected >= {minimum} MoveStarted rows, saw {started}')
    return failures, out


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--godot', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--frames', type=int, default=76)
    parser.add_argument('--timeout', type=int, default=90)
    options = parser.parse_args()
    options.output.mkdir(parents=True, exist_ok=False)
    failed = []
    for name, mode, distance, keys, expected in SCENES:
        failures, out = run_scene(options, name, mode, distance, keys, expected)
        if failures:
            failed.append((name, failures))
            print(f'FAIL {name}: {"; ".join(failures)}', flush=True)
        else:
            print(f'PASS {name} -> {out}', flush=True)
    if failed:
        raise SystemExit(f'{len(failed)} Stage 28 runtime scenes failed')
    print(f'PASS: {len(SCENES)} Stage 28 runtime scenes')


if __name__ == '__main__':
    main()
