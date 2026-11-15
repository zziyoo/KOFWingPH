"""Run one headless Godot duel with the Stage 28 profile to inspect the authority rows."""

import shlex
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GODOT = Path(sys.argv[1])
OUT = ROOT / 'local-mugen/stage28-runtime-smoke'
OUT.mkdir(parents=True, exist_ok=True)
log = OUT / 'duel.log'
args = [
    str(GODOT), '--path', '.', '--fixed-fps', '60', '--headless', '--quit-after', '76',
    '--log-file', (OUT / 'godot-engine.log').as_posix(), '--',
    '--visual-audit', '--entity-audit', '--move-audit', '--sprite-frame-audit', '--duel=60',
    '--p1-fighter-file=res://data/imported-characters/terry98-stage28-final/default.fighter.json',
    '--p2-fighter-file=res://data/imported-characters/terry98-stage28-final/default.fighter.json',
    '--p1-visual=terry98-stage28-default', '--p2-visual=terry98-stage28-default',
    '--shot=' + (OUT / 'no-image.png').as_posix(), '--shotframes=77',
    '--renderkey=1:down:Key1', '--renderkey=5:down:J', '--renderkey=7:down:Comma',
    '--renderkey=9:down:Enter', '--renderkey=11:up:J', '--renderkey=11:up:Comma',
    '--renderkey=11:up:Enter', '--renderkey=15:down:D', '--renderkey=15:down:J',
]
with log.open('wb') as stream:
    result = subprocess.run(args, cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT, timeout=120)
data = log.read_bytes()
print('exit', result.returncode, 'bytes', len(data))
print(data.decode('utf-8', 'replace')[:400])
