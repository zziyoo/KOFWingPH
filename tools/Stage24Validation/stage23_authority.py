"""Replay the frozen Stage 23 Vulkan authority pairs with a candidate build."""

import argparse
import json
from hashlib import sha256
from pathlib import Path
from subprocess import TimeoutExpired
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'Stage21Validation'))
from stage20_authority import ROOT, authority, recorded_arguments, require_same, run_godot


# Deliberately select the accepted evidence, including compact reruns that replaced pilots.
GROUPS = {
    'body': {
        'vulkan-body-v2': ('hit-0', 'held-down-0', 'guard-0', 'crouch-guard-0', 'crouch-hit-0',
                           'interrupt-0', 'mirror-hit-0', 'stand-0', 'whiff-0'),
        'vulkan-recheck/body': ('dual-button-0', 'down-back-heavy-0', 'crouch-hit-release-0'),
        'vulkan-reserved': ('down-forward-both-0',),
        'vulkan-down-forward-final': ('down-forward-heavy-0',),
    },
    'route': {
        'vulkan-routes-pilot': ('approach-0', 'approach-11', 'crouch-hit-22'),
        'vulkan-low-pilot': ('compete-11', 'incoming-wave-20', 'whiff-11'),
        'vulkan-guard-final': ('crouch-guard-22',),
        'vulkan-recheck/guards': ('guard-22', 'guard-release-22', 'crouch-guard-release-22'),
        'vulkan-reserved': ('hit-28', 'hit-29', 'hit-43', 'hit-held-14', 'mirror-hit-22', 'whiff-7',
                            'whiff-8', 'whiff-17', 'whiff-18', 'whiff-32'),
        'vulkan-replay-primary': ('hit-14', 'hit-22', 'whiff-9', 'whiff-10'),
        'vulkan-bounded': ('mixed-22', 'incoming-wave-22', 'incoming-wave-28'),
    },
    'coexistence': {
        'coexistence/source215': ('hit-22',),
        'coexistence/source400': ('hit-held-11',),
        'coexistence/sources200-205': ('mixed-sequential',),
    },
    'mode2': {
        'vulkan-mode2-final': ('hit-0', 'hit-22'),
        'vulkan-mode2-input': ('down-forward-both-0',),
    },
}
TOTALS = {'body': (14, 2326, 20934), 'route': (27, 4831, 45182),
          'coexistence': (3, 504, 4902), 'mode2': (3, 237, 2133)}
PILOT_HASHES = {
    'terry98-stage23-body-v2': '734211612fc87ff8e0e8817402b78aaad86bbf1857e87604f1a10292a086ec5f',
    'terry98-stage23-authored-guard': '143f0ec01445c6953061e2fcda2afa761a4590ea40eb61a5a5d33a1c2287c39f',
    'terry98-stage23-data': 'ca9012930189c0e17dbeadb445b004747f5f0cfe2e6f3491448398f89fe429b4',
    'terry98-stage23-low-reaction': '64015b9715395a8180d29292e662540066ac71260881f2683e4bc38ae1aa6d56',
}


def prepare_pilots(destination):
    destination.mkdir(parents=True, exist_ok=False)
    for pilot, digest in PILOT_HASHES.items():
        source = ROOT / 'data/imported-characters' / pilot / 'default.fighter.json'
        content = source.read_bytes()
        if sha256(content).hexdigest() != digest:
            raise ValueError(f'altered Stage 23 pilot source: {source}')
        profile = json.loads(content)
        moves = [move for move in profile['moves'] if 'excludedStartDirection' in move]
        move, = moves
        if move['moveId'] != 'TerryCrouchHeavyPunch410' or move.pop('excludedStartDirection') != 'DownForward':
            raise ValueError(f'unexpected retired pilot rule: {source}')
        target = destination / f'{pilot}.fighter.json'
        target.write_text(json.dumps(profile, indent=2, sort_keys=True) + '\n', encoding='utf-8', newline='\n')
        print(f'compatibility copy {pilot}: source={digest} output={sha256(target.read_bytes()).hexdigest()}', flush=True)


def frozen_arguments(path, shot, pilots):
    args = recorded_arguments(path, shot)
    migrated = False
    for i, arg in enumerate(args):
        if arg.startswith(('--p1-fighter-file=', '--p2-fighter-file=')):
            for pilot in PILOT_HASHES:
                if arg.endswith(f'/{pilot}/default.fighter.json'):
                    args[i] = arg.partition('=')[0] + f'=res://{pilots}/{pilot}.fighter.json'
                    migrated = True
                    break
    if migrated:
        verify_unused_direction_exclusion(args, path)
    return args


def verify_unused_direction_exclusion(args, path):
    keys = (('S', 'D', 'K'), ('Down', 'Left', 'Period'))
    samples = {}
    for arg in args:
        if not arg.startswith('--renderkey='):
            continue
        frame, action, key = arg.split('=', 1)[1].split(':')
        samples.setdefault(int(frame), []).append((action, key))
    held = set()
    for frame, edges in sorted(samples.items()):
        for action, key in edges:
            if action == 'down':
                held.add(key)
            else:
                held.discard(key)
        pressed = {key for action, key in edges if action == 'down'}
        if any(down in held and forward in held and heavy in pressed for down, forward, heavy in keys):
            raise ValueError(f'retired direction rule was exercised at render {frame}: {path}')


def replay(archive, godot, output, groups, pilots):
    totals = [0, 0, 0]
    for group in groups:
        cases = frames = count = 0
        for directory, scenes in GROUPS[group].items():
            for scene in scenes:
                fixture = archive / f'NewKOF-stage23-{directory}' / scene
                original = fixture / 'sprite/duel.log'
                length, expected = authority(original, original.read_bytes())
                for variant in ('sprite', 'plain'):
                    frozen = fixture / variant / 'duel.log'
                    require_same(expected, authority(frozen, frozen.read_bytes())[1], f'{fixture} archived {variant}')
                    if godot is not None:
                        destination = output / group / directory / scene / variant
                        destination.mkdir(parents=True, exist_ok=False)
                        shot = destination / 'end.png'
                        log = destination / 'duel.log'
                        args = frozen_arguments(frozen.with_suffix('.args.txt'), shot, pilots)
                        actual = run_godot(godot, args, log, shot)
                        require_same(expected, authority(log, actual)[1], f'{fixture} replayed {variant}')
                cases += 1
                frames += length
                count += len(expected)
                print(f'{group}/{directory}/{scene}: {length} frames, {len(expected)} authority rows', flush=True)
        if (cases, frames, count) != TOTALS[group]:
            raise ValueError(f'incomplete Stage 23 {group} evidence: {cases} / {frames} / {count}')
        totals = [a + b for a, b in zip(totals, (cases, frames, count), strict=True)]
    cases, frames, count = totals
    print(f'Matched {cases} Stage 23 fixtures, {frames} logic frames and {count} authority rows.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument('--archive-only', action='store_true')
    mode.add_argument('--godot', type=Path)
    parser.add_argument('--archive', type=Path, required=True, help='Parent of the frozen NewKOF-stage23-* directories')
    parser.add_argument('--output', type=Path)
    parser.add_argument('--group', choices=GROUPS, action='append')
    parser.add_argument('--pilot-dir', default='data/imported-characters/stage24-frozen23-pilots')
    options = parser.parse_args()
    try:
        if options.godot is not None:
            if options.output is None:
                parser.error('--godot requires --output')
            options.output = options.output.resolve()
            options.output.mkdir(parents=True, exist_ok=False)
            pilots = (ROOT / options.pilot_dir).resolve()
            if pilots.parent != (ROOT / 'data/imported-characters').resolve() or not pilots.name.startswith('stage24-frozen23-pilots'):
                parser.error('--pilot-dir must be a fresh stage24-frozen23-pilots directory under data/imported-characters')
            prepare_pilots(pilots)
            options.pilot_dir = pilots.relative_to(ROOT).as_posix()
        replay(options.archive, options.godot, options.output, options.group or GROUPS, options.pilot_dir)
    except (OSError, ValueError, TimeoutExpired) as error:
        parser.exit(1, f'Stage 23 authority replay failed: {error}\n')


if __name__ == '__main__':
    main()
