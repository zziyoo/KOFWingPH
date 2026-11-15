"""Verify the previously connected Wave sources with the State 250 profile."""

import argparse
from functools import partial
import json
from pathlib import Path
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'Stage27Validation'))
from current_profile import build_hashes, replay, ROOT, sweep_history
from power_wave_duel import Q16, assert_equal, audit_rows, indexed


def q16(value):
    return int(value['numerator'] * Q16 / value['denominator'])


def authored_launch_hurt(row, boxes):
    x, y = int(row['x']), int(row['y'])
    right = row['facing'] == 'Right'
    result = []
    for box in boxes:
        left, bottom, right_edge, top = (q16(box[edge]) for edge in ('minX', 'minY', 'maxX', 'maxY'))
        if not right:
            left, right_edge = -right_edge, -left
        result.append(','.join(map(str, (x + left, y + bottom, x + right_edge, y + top))))
    return ';'.join(result).encode()


def compare_shared(boxes, expected, observed, description):
    assert_equal(len(expected), len(observed), f'{description}: same authority row count')
    launch = indexed(audit_rows([line.decode() for line in observed], 'LaunchReactionAudit'))
    flight = {'Shake', 'Lift', 'Rise', 'Fall'}
    replaced = 0
    for before, after in zip(expected, observed, strict=True):
        if before == after:
            continue
        if not (before.startswith(b'[MoveAudit] ') and after.startswith(b'[MoveAudit] ')):
            sweep_history.require_same([before], [after], description)
        head, _, new_hurt = after.partition(b' hurt=')
        assert_equal(before.partition(b' hurt=')[0], head, f'{description}: authority outside launch hurtboxes')
        row, = audit_rows([after.decode()], 'MoveAudit')
        key = int(row['fighter']), int(row['logic'])
        assert_equal(True, row['state'] == 'AirHitstun' and key in launch and launch[key]['phase'] in flight,
                     f'{description}: authored initial air reaction at seat/frame {key}')
        assert_equal(authored_launch_hurt(row, boxes), new_hurt, f'{description}: authored Clsn2 at seat/frame {key}')
        replaced += 1
    print(f'{description}: {replaced} authored-air hurtbox rows compared to original Clsn2', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--godot', required=True, type=Path)
    parser.add_argument('--archive', type=Path, default=Path('D:/'))
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--profile', default='data/imported-characters/terry98-stage29-final')
    parser.add_argument('--visual-alias', default='terry98-stage29-default')
    options = parser.parse_args()
    options.resolution = '320x180'
    try:
        root = ROOT / options.profile
        fighter = json.loads((root / 'default.fighter.json').read_bytes())
        source = json.loads((root / 'default.source.json').read_bytes())
        assert_equal(source['launchAirHurtboxes'], fighter['groundHurtboxes']['launchAir'], 'authored launch source')
        hashes = build_hashes()
        print(f'Stage 29 coexistence Debug build {hashes}', flush=True)
        options.output.mkdir(parents=True, exist_ok=False)
        replay(options, compare_shared=partial(compare_shared, source['launchAirHurtboxes']), stage_label='Stage 29')
        assert_equal(hashes, build_hashes(), 'candidate build remained stable')
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        parser.exit(1, f'Stage 29 compatibility replay failed: {error}\n')


if __name__ == '__main__':
    main()
