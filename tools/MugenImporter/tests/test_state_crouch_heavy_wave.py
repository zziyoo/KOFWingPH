import copy
import unittest

from mugen_importer.state_crouch_heavy_wave import INTERRUPT_MOVE, MOVE, RESERVED_RISING_UPPER, build_profile
from mugen_importer.state_power_wave import MOVE_ID


BOX = [20, -64, 75, -49]
GUARDS = {field: [{'minX': -20, 'maxX': 19, 'minY': 1, 'maxY': 88}]
          for field in ('standingGuard', 'crouchingGuard', 'standingGuardReady', 'crouchingGuardReady')}
AUDIT = {
    'action': {'strikes': [{'box': BOX}], 'startup': 5, 'active': 8, 'recovery': 14,
               'durations': [27], 'clsn2': [[BOX]]},
    'damage': {'normalBase': 13, 'counterBase': 15, 'useContactDivisor': False},
    'guardThreatFrames': 13,
    'cancelWindow': [6, 12],
    'lateWave': {'firstFrame': 9, 'lastFrame': 12},
    'groundDefaults': {'initialPush': {'numerator': 21, 'denominator': 2}, 'hitPause': (11, 13), 'guardPause': (11, 11)},
    'groundGuardHurtboxes': GUARDS,
    'crouchHitVelocityRetention': {'numerator': 41, 'denominator': 50},
    'lastHitStepScale': {'numerator': 3, 'denominator': 5},
    'groundReactions': {
        'standing': {'initial': {'action': 5002, 'durations': [1, 2], 'clsn2': [[BOX], [BOX]]},
                     'slide': {'action': 5007, 'durations': [4, 5, -1], 'clsn2': [[BOX], [BOX], [BOX]]}},
        'crouching': {'initial': {'action': 5022, 'durations': [1, 2], 'clsn2': [[BOX], [BOX]]},
                      'slide': {'action': 5027, 'durations': [4, 5, -1], 'clsn2': [[BOX], [BOX], [BOX]]}},
        'crouchingLight': {'initial': {'action': 5020, 'durations': [1, 4], 'clsn2': [[BOX], [BOX]]},
                          'slide': {'action': 5025, 'durations': [-1], 'clsn2': [[BOX]]}},
        'waveLow': {'initial': {'action': 5012, 'durations': [1, 2], 'clsn2': [[BOX], [BOX]]},
                    'slide': {'action': 5017, 'durations': [4, 5, -1], 'clsn2': [[BOX], [BOX], [BOX]]}},
    },
}
NATIVE = {
    'hit': {'damage': 84, 'initialPush': 10.5, 'effectiveStun': 19,
            'pause': {'attackerFrames': 11, 'defenderFrames': 13}},
    'guard': {'damage': 0, 'initialPush': 10.5, 'effectiveStun': 18,
              'pause': {'attackerFrames': 11, 'defenderFrames': 11}},
    'cancelVerified': True,
    'groundReactions': {
        'standing': {'initialTicks': 4, 'effectiveStun': 19},
        'crouching': {'initialTicks': 4, 'effectiveStun': 19},
        'crouchingLight': {'initialTicks': 6, 'effectiveStun': 11},
        'waveLow': {'initialTicks': 4, 'effectiveStun': 19},
    },
}


def baseline(mode):
    stand = {'moveId': 'TerryStandHeavyPunch210', 'commandId': '5B', 'allowedFrom': 'Standing', 'cancels': []}
    interrupt = {'moveId': INTERRUPT_MOVE, 'commandId': '5A', 'attacks': [{'hitstunFrames': 11}]}
    spans = [
        {'firstFrame': 1, 'lastFrame': 8,
         'hit': {'launchVelocityY': {'numerator': 13, 'denominator': 2}, 'hitstunFrames': 19}, 'launchReaction': {}},
        {'firstFrame': 9, 'lastFrame': 12, 'hit': {'launchVelocityY': {'numerator': 0, 'denominator': 1}, 'hitstunFrames': 19}},
    ]
    wave = {'moveId': MOVE_ID, 'commandId': '236A',
            'projectiles': [{'projectileTypeId': 'TerryPowerWave1020', 'timeline': {'hitSpans': spans}}]}
    commands = [{'commandId': '5A', 'buttons': ['LightPunch'], 'priority': 1},
                {'commandId': '5B', 'buttons': ['HeavyPunch'], 'priority': 0}]
    if mode == 'default':
        commands.append({'commandId': '236A', 'priority': 2})
    fighter = {
        'moves': [stand, wave, interrupt] if mode == 'default' else [stand, interrupt],
        'commands': commands,
        'useCommandPriority': mode == 'default',
        'groundHurtboxes': {'crouchRise': [{'frames': 4, 'boxes': []}]},
        'movement': {'groundReaction': {'velocityRetention': {'numerator': 85, 'denominator': 100},
                                       'lastHitStepScale': AUDIT['lastHitStepScale']}},
    }
    return fighter, {'clips': {}, 'moves': {}, 'hitReactions': {INTERRUPT_MOVE: {'followupTick': 6}}}


class StateCrouchHeavyWaveTests(unittest.TestCase):
    def test_body_uses_native_heavy_hit_and_independent_shared_command(self):
        base = baseline('default')
        original = copy.deepcopy(base)
        fighter, binding = build_profile(AUDIT, NATIVE, *base, 'default', with_cancel=False)
        self.assertEqual(original, base)
        self.assertEqual(original[0]['commands'][:2], fighter['commands'][:2])
        self.assertEqual(original[0]['moves'][:-1], fighter['moves'][:-2])
        self.assertEqual('CrouchingLight', fighter['moves'][-2]['attacks'][0]['groundHitReaction'])
        crouch = fighter['moves'][-1]
        self.assertEqual((MOVE, '5B', 'Crouching', True), tuple(crouch[k] for k in
                         ('moveId', 'commandId', 'allowedFrom', 'returnsToCrouch')))
        self.assertEqual((5, 8, 14, []), tuple(crouch[k] for k in
                         ('startupFrames', 'activeFrames', 'recoveryFrames', 'cancels')))
        self.assertEqual((19, 18, AUDIT['damage'], 'Heavy'), tuple(crouch['attacks'][0][k] for k in
                         ('hitstunFrames', 'blockstunFrames', 'dynamicDamage', 'groundHitReaction')))
        self.assertEqual([2, 2, 4, 5, 6], [span['frames'] for span in fighter['groundHurtboxes']['heavyHitstun']])
        self.assertEqual([2, 4, 5], [span['frames'] for span in fighter['groundHurtboxes']['crouchingLightHitstun']])
        self.assertEqual(410, binding['clips']['state410']['action'])
        self.assertEqual(AUDIT['crouchHitVelocityRetention'], fighter['movement']['groundReaction']['crouchHitVelocityRetention'])
        self.assertEqual(GUARDS, {key: fighter['groundHurtboxes'][key] for key in GUARDS})

    def test_route_uses_existing_wave_and_preserves_mode2(self):
        for mode in ('default', 'mode2'):
            base = baseline(mode)
            fighter, _ = build_profile(AUDIT, NATIVE, *base, mode)
            self.assertEqual(base[0]['moves'][0], fighter['moves'][0])
            if mode == 'mode2':
                self.assertEqual([], fighter['moves'][-1]['cancels'])
            else:
                route, = fighter['moves'][-1]['cancels']
                self.assertEqual((MOVE_ID, 6, 12, True), tuple(route[k] for k in
                                 ('targetMoveId', 'windowFirstFrame', 'windowLastFrame', 'allowWhiff')))

    def test_rejects_unverified_window_and_inconsistent_reaction(self):
        with self.assertRaisesRegex(ValueError, 'verified native cancel'):
            build_profile(AUDIT, {**NATIVE, 'cancelVerified': False}, *baseline('default'), 'default')
        with self.assertRaisesRegex(ValueError, 'standing.*timeline'):
            native = {**NATIVE, 'groundReactions': {**NATIVE['groundReactions'],
                      'standing': {'initialTicks': 1, 'effectiveStun': 19}}}
            build_profile(AUDIT, native, *baseline('default'), 'default')

    def test_rejects_sample_from_a_different_ground_push_mode(self):
        native = {**NATIVE, 'hit': {**NATIVE['hit'], 'initialPush': 11}}
        with self.assertRaisesRegex(ValueError, 'default source push or pause'):
            build_profile(AUDIT, native, *baseline('default'), 'default')

    def test_low_heavy_profile_uses_only_the_grounded_late_wave_hit(self):
        base = baseline('default')
        fighter, binding = build_profile(AUDIT, NATIVE, *base, 'default')
        old_spans = base[0]['moves'][1]['projectiles'][0]['timeline']['hitSpans']
        new_spans = fighter['moves'][1]['projectiles'][0]['timeline']['hitSpans']
        self.assertEqual(old_spans[0], new_spans[0])
        self.assertEqual('HeavyLow', new_spans[1]['hit']['groundHitReaction'])
        self.assertEqual([2, 2, 4, 5, 6], [span['frames'] for span in fighter['groundHurtboxes']['lowHeavyHitstun']])
        self.assertEqual('stage23_crouching_initial', binding['hitReactions']['TerryPowerWave1020']['crouching']['initial'])

    def test_reserved_310_candidate_beats_normal_buttons_but_not_weak_wave(self):
        for mode in ('default', 'mode2'):
            fighter, _ = build_profile(AUDIT, NATIVE, *baseline(mode), mode)
            self.assertIs(fighter['useCommandPriority'], True)
            commands = {command['commandId']: command for command in fighter['commands']}
            reserve = commands[RESERVED_RISING_UPPER]
            self.assertEqual((['DownForward'], ['HeavyPunch'], True, 0), tuple(reserve[key] for key in
                             ('directions', 'buttons', 'reserveGroundInput', 'bufferLifetimeFrames')))
            self.assertGreater(reserve['priority'], commands['5A']['priority'])
            self.assertGreater(reserve['priority'], commands['5B']['priority'])
            self.assertFalse(any(move['commandId'] == RESERVED_RISING_UPPER for move in fighter['moves']))
            if mode == 'default':
                self.assertGreater(commands['236A']['priority'], reserve['priority'])
