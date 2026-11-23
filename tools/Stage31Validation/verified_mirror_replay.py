"""Replay all 542 frozen pairs from the retained, fully matched Stage 31 mirror.

Deleted graphic archives are not recreated. The reference is the old verified
authority copy; current-build runs and reused current-build runs are distinct.
"""
import argparse
import ast
import hashlib
import json
from pathlib import Path
import re
import shlex
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'Stage27Validation'))
import frozen_replay as frozen

ROOT = frozen.ROOT
REFERENCE_BUILD = {'NewKOF.dll': '02f2b5d19187e240e27b87ba98e3c79837dbd0d243107ad28cde4451c0b3ceeb',
                   'NewKOF.BattleCore.dll': '8cbf9e057e5f4bd1a3b841999aff3108a830cba277c6bd8bfb103d7141f934c9'}


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def build_header(path):
    first = path.read_text(encoding='utf-8-sig').splitlines()[0]
    prefix = 'Candidate Godot Debug build '
    if not first.startswith(prefix):
        raise ValueError(f'missing build identity: {path}')
    return ast.literal_eval(first[len(prefix):])


def authority(stage, path, data):
    if stage <= 16:
        return frozen.ground_history.audit_rows(data, path.parent.name == 'sprite', path)
    if stage == 17:
        return frozen.damage_history.authority(data, path.parent.name == 'sprite', path)
    if stage == 18:
        rows = frozen.reentry_history.authority(data)
        return len(rows) // 8, rows
    if stage == 19:
        return frozen.wave_history.stage19_rows(data)
    if stage <= 22:
        return frozen.wave_history.authority(path, data)
    if stage == 23:
        return frozen.crouch_heavy_history.authority(path, data)
    return frozen.sweep_history.rows(path, data)


def tokens(path):
    return [token.strip('"') for token in shlex.split(path.read_text(encoding='utf-8-sig'), posix=False)]


def input_arguments(command):
    normalized = []
    skip = False
    for arg in command[1:]:
        if skip:
            skip = False
            continue
        if arg == '--log-file':
            skip = True
        elif not arg.startswith('--shot='):
            normalized.append(arg)
    return normalized


def references(proof):
    if build_header(proof) != REFERENCE_BUILD:
        raise ValueError('reference proof is not the pinned, previously accepted candidate')
    entries, pending, reused, matched = [], [], None, set()
    for line in proof.read_text(encoding='utf-8-sig').splitlines()[1:]:
        if line.startswith('Reused verified replay: '):
            reused = Path(line.removeprefix('Reused verified replay: '))
        elif match := re.fullmatch(r'(.+)/(sprite|plain) <- (.+)', line):
            scene, variant, original = match.groups()
            if Path(scene).is_absolute() or '..' in Path(scene).parts:
                raise ValueError('unsafe scene name')
            pending.append({'scene': scene, 'variant': variant, 'original': original, 'reused': reused})
            reused = None
        elif match := re.fullmatch(r'Matched Stage (\d+): (\d+) pairs, (\d+) logic frames, (\d+) authority rows', line):
            stage, pairs, frames, rows = map(int, match.groups())
            if stage in matched or (pairs, frames, rows) != frozen.TOTALS[stage] or len(pending) != pairs * 2:
                raise ValueError(f'incomplete reference marker for Stage {stage}')
            matched.add(stage)
            if len({(r['scene'], r['variant']) for r in pending}) != pairs * 2:
                raise ValueError('duplicate reference variants')
            for entry in pending:
                entry['stage'] = stage
                entry['path'] = entry.pop('reused') or proof.with_suffix('') / f'stage{stage}' / entry['scene'] / entry['variant'] / 'duel.log'
                path = entry['path'].resolve(strict=True)
                run_root = next(parent for parent in path.parents if parent.parent == ROOT / 'local-mugen')
                if build_header(run_root.with_suffix('.log')) != REFERENCE_BUILD:
                    raise ValueError(f'reference source run has another build: {path}')
                entry['path'] = path
                entries.append(entry)
            pending = []
    if pending or matched != set(range(14, 27)) or len(entries) != 1084:
        raise ValueError('reference proof must cover all 13 stages and 1,084 variants')
    return entries


def main():
    parser = argparse.ArgumentParser(__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument('--godot', type=Path)
    mode.add_argument('--verify-only', action='store_true')
    parser.add_argument('--proof', type=Path, default=ROOT / 'local-mugen/stage31-frozen-resume-final.log')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--resume', type=Path, action='append', default=[])
    args = parser.parse_args()
    args.output = args.output.resolve()
    args.output.mkdir(parents=True, exist_ok=False)
    current = frozen.build_hashes()
    print(f'Candidate Godot Debug build {current}', flush=True)
    refs = references(args.proof)
    manifest = {'referenceBuild': REFERENCE_BUILD, 'proof': str(args.proof), 'proofSha256': sha(args.proof), 'variants': []}
    old_totals = {stage: [0, 0, 0] for stage in range(14, 27)}
    previous, configs = {}, {}
    for entry in refs:
        stage, scene, variant, path = (entry[k] for k in ('stage', 'scene', 'variant', 'path'))
        data, command = path.read_bytes(), tokens(path.with_suffix('.args.txt'))
        if not frozen.valid_headless_log(data) or '--headless' not in command or command[command.index('--fixed-fps') + 1] != '60':
            raise ValueError(f'invalid reference replay: {path}')
        frozen.verify_headless_keys(path, data, data.splitlines(), command)
        length, rows = authority(stage, path, data)
        key = stage, scene
        if variant == 'sprite':
            old_totals[stage] = [a + b for a, b in zip(old_totals[stage], (1, length, len(rows)))]
            previous[key] = rows
        else:
            frozen.sweep_history.require_same(previous.pop(key), rows, f'{scene} retained reference variants')
        for arg in command:
            if arg.startswith(('--p1-fighter-file=res://', '--p2-fighter-file=res://')):
                profile = ROOT / arg.partition('res://')[2]
                configs[str(profile.relative_to(ROOT))] = sha(profile)
        manifest['variants'].append({**{k: str(v) for k, v in entry.items()}, 'logSha256': sha(path),
                                     'argumentsSha256': sha(path.with_suffix('.args.txt')),
                                     'authoritySha256': hashlib.sha256(b'\n'.join(rows)).hexdigest()})
    for stage, totals in old_totals.items():
        if tuple(totals) != frozen.TOTALS[stage]:
            raise ValueError(f'reference totals disagree for Stage {stage}: {totals}')
    manifest.update(frozenConfigs=configs, totals=old_totals)
    (args.output / 'references.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    print('Verified retained reference: 542 pairs, 65069 logic frames, 538586 authority rows; 1084 variants', flush=True)
    if args.verify_only:
        return
    resumed = []
    for root in args.resume:
        if build_header(root.with_suffix('.log')) != current:
            raise ValueError('cannot reuse current runs from another candidate')
        resumed.append((root, set(root.with_suffix('.log').read_text(encoding='utf-8-sig').splitlines())))
    (args.output / 'candidate.json').write_text(json.dumps(current, indent=2), encoding='utf-8')
    for stage in range(14, 27):
        for entry in (r for r in refs if r['stage'] == stage):
            path, scene, variant = (entry[k] for k in ('path', 'scene', 'variant'))
            command, golden = tokens(path.with_suffix('.args.txt')), path.read_bytes()
            expected = authority(stage, path, golden)[1]
            marker = f'{scene}/{variant} <- {entry["original"]}'
            cached = None
            for root, markers in resumed:
                log = root / f'stage{stage}' / scene / variant / 'duel.log'
                if marker in markers and log.is_file() and frozen.valid_headless_log(log.read_bytes()):
                    if input_arguments(tokens(log.with_suffix('.args.txt'))) != input_arguments(command):
                        raise ValueError(f'resumed input arguments differ: {log}')
                    cached = log
                    break
            if cached:
                log, data = cached, cached.read_bytes()
                print(f'Reused current-build replay: {log}', flush=True)
            else:
                folder = args.output / f'stage{stage}' / scene / variant
                folder.mkdir(parents=True, exist_ok=False)
                log = folder / 'duel.log'
                command[0] = str(args.godot.resolve(strict=True))
                command[command.index('--log-file') + 1] = str(folder / 'godot-engine.log')
                command = ['--shot=' + (folder / 'no-image.png').as_posix() if a.startswith('--shot=') else a for a in command]
                log.with_suffix('.args.txt').write_text(subprocess.list2cmdline(command) + '\n', encoding='utf-8')
                data = frozen.run_logged(command, log)
            frozen.verify_headless_keys(path, golden, data.splitlines(), command)
            frozen.sweep_history.require_same(expected, authority(stage, log, data)[1], f'{scene}/{variant} current vs retained reference')
            print(marker, flush=True)
        p, f, r = frozen.TOTALS[stage]
        print(f'Matched Stage {stage}: {p} pairs, {f} logic frames, {r} authority rows', flush=True)
        if frozen.build_hashes() != current or any(sha(ROOT / p) != h for p, h in configs.items()):
            raise ValueError('candidate DLLs or frozen configurations changed during replay')


if __name__ == '__main__':
    main()
