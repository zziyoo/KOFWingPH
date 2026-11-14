"""Check State 440 walk and command selection on the actual recovery tick."""

from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'Stage26Validation'))
from standing_light_kick_duel import (ACTIONS, DEFERRED, KICKS, NATIVE_MOVES, Q16, assert_equal,
                                     check_poses, check_recovery, run_validation, validation_options,
                                     verify_body)


def verify_sweep(profile, changes, name, scene, phases, events, moves, reentry, lines):
    recovery, = [row for row in changes if row['from'] == 440 and row['seat'] == 1]
    assert_equal((45, 40), (recovery['frame'], recovery['stateTime']), 'natural sweep exit')
    expected = {'sweep-walk-forward': 20, 'sweep-walk-back': 20, 'sweep-walk-idle': 11,
                'sweep-walk-down-forward': 11, 'sweep-walk-attack': 230}
    assert_equal(expected[name], recovery['to'], 'native State -1 and action priority')
    samples = []
    for frame in range(43, 49):
        native = phases['end'][frame]['chars'][0]
        host = moves[0, frame + 14]
        assert_equal(round(native['pos'][0] * Q16), int(host['x']), f'State 440 X at F{frame}')
        samples.append({'nativeFrame': frame, 'nativeState': native['state'], 'nativeX': native['pos'][0],
                        'logicFrame': frame + 14, 'hostState': host['state'], 'hostX': int(host['x']) / Q16})
    result = verify_body(profile, changes, name, scene, phases, events, moves, reentry, lines)
    result['sweepRecovery'] = samples
    return result


if __name__ == '__main__':
    parser, options = validation_options(__doc__, 'data/imported-characters/terry98-stage27-final')
    run_validation(parser, options, family='standing-heavy-kick', capture_profile='Stage27',
                   alias=options.visual_alias or f'terry98-stage27-{options.mode}', native_moves=NATIVE_MOVES,
                   reservations=DEFERRED, actions=ACTIONS, states={state: state for state in KICKS},
                   body_check=verify_sweep, recovery_check=lambda deferred, name, *args: check_recovery(deferred, *args),
                   pose_check=check_poses)
