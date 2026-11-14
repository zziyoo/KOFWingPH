import os
from pathlib import Path
import unittest


@unittest.skipUnless(os.environ.get('NEWKOF_TERRY_AUDIT'), 'requires the local Terry ZIP and Manifest')
class OriginalStandingHeavyKickTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        from mugen_importer import state_standing_heavy_kicks as kicks
        cls.kicks = kicks
        cls.root = Path(os.environ['NEWKOF_TERRY_AUDIT'])
        cls.audit = kicks.inspect_source(cls.root / 'local-mugen/terry.zip',
                                         cls.root / 'data/imported-characters/terry98/manifest.json')

    def test_parses_the_two_far_actions_and_one_shared_near_action(self):
        expected = {
            240: ((13, 3, 22), [56, -88, 127, -57], [(28, 32)], (15, 17)),
            241: ((19, 4, 20), [8, -91, 79, -61], [(tick, 8) for tick in (1, 4, 7, 10, 12)], (15, 17)),
            245: ((3, 6, 20), [36, -101, 79, -29], [], (12, 15)),
        }
        for number, (timeline, box, steps, damage) in expected.items():
            with self.subTest(action=number):
                review = self.audit['actions'][number]
                action = review['action']
                self.assertEqual(timeline, tuple(action[key] for key in ('startup', 'active', 'recovery')))
                self.assertEqual([{'first': 0, 'last': timeline[1] - 1, 'box': box}], action['strikes'])
                self.assertEqual(timeline[0] + timeline[1], review['guardThreatFrames'])
                self.assertEqual([{'frame': frame, 'x': {'numerator': x, 'denominator': 1}} for frame, x in steps],
                                 review['positionSteps'])
                self.assertEqual(damage, tuple(review['damage'][key] for key in ('normalBase', 'counterBase')))
                self.assertEqual(('s,na', 'm', 'maf', 'hard', 'high'), tuple(review['hitDef'][key] for key in
                                 ('attr', 'guardflag', 'hitflag', 'animtype', 'ground.type')))
                self.assertEqual((11, 13), review['groundDefaults']['hitPause'])
                self.assertEqual((11, 11), review['groundDefaults']['guardPause'])
                self.assertTrue(all(step['frame'] < sum(timeline) for step in review['positionSteps']))

    def test_rejects_ungrouped_or_unfrozen_position_scripts(self):
        for extra in (
            'trigger1 = Anim = 241 && AnimElem = 2 || AnimElem = 3',
            'trigger1 = Anim = 241\ntrigger1 = AnimElem = 2 || AnimElem = 3\nignorehitpause = 1',
        ):
            with self.subTest(extra=extra):
                control, = self.kicks.state_reentry.script_sections(f'[State 240, PosAdd]\ntype = PosAdd\n{extra}\nx = 8\n'.encode())
                with self.assertRaisesRegex(ValueError, 'PosAdd'):
                    self.kicks.source_steps([control], {241: [1, 3, 3]})

    def test_preserves_original_precedence_and_no_early_normal_transition(self):
        command = self.audit['command']
        self.assertEqual(48, command['maximumStartBodyDistance'])
        self.assertEqual(2, command['bufferFrames'])
        self.assertEqual(1, command['time'])
        self.assertEqual('ctrl||(stateno=[100,101])||var(2)>=1', command['control'])
        lines = command['sourceLines']
        self.assertEqual(sorted(lines.values()), list(lines.values()))
        self.assertEqual([205, 200, 215, 210, 235, 230, 245, 240, 400, 410, 430, 440],
                         list(lines))
        self.assertEqual({}, command['earlyOrdinary'])
        self.assertEqual([4, 8], self.audit['actions'][245]['wavePermission']['explicitWindow'])
        self.assertIsNone(self.audit['actions'][240]['wavePermission']['explicitWindow'])
        self.assertIsNone(self.audit['actions'][241]['wavePermission']['explicitWindow'])
        self.assertEqual([38, 43, 29], [self.audit['actions'][n]['wavePermission']['naturalEntryTick']
                                       for n in (240, 241, 245)])

    def test_parses_deferred_blowback_and_throw_chords_from_the_source(self):
        command = self.audit['command']
        blowback = command['deferredBlowback']
        kick_throw = command['deferredKickThrow']
        self.assertEqual((['HeavyPunch', 'HeavyKick'], 1, 0), tuple(blowback[key] for key in
                         ('buttons', 'maxSequenceFrames', 'bufferLifetimeFrames')))
        self.assertEqual((['HeavyKick'], ['Back', 'Forward'], 0), tuple(kick_throw[key] for key in
                         ('buttons', 'directions', 'bufferLifetimeFrames')))
        self.assertEqual(command['bufferPolicy'], kick_throw['bufferPolicy'])
        self.assertTrue(blowback['bufferPolicy']['helperRunsDuringGlobalPause'])
        self.assertTrue(kick_throw['bufferPolicy']['helperRunsDuringGlobalPause'])
        self.assertEqual({'maximumBodyDistance': {'numerator': 11, 'denominator': 1},
                          'requiresGroundedNonHitstun': True}, kick_throw['groundReservationTarget'])
        lines = command['deferredSourceLines']
        self.assertEqual(['groundRecovery', 'blowback', 'punchThrow', 'kickThrow', 'forwardPunch', 'risingUpper'],
                         list(lines))
        self.assertEqual(sorted(lines.values()), list(lines.values()))

    def test_replaces_reservation_and_keeps_mode_specific_bodies_and_old_recovery(self):
        for mode, far_action in (('default', 240), ('mode2', 241)):
            with self.subTest(mode=mode):
                base, base_binding = self.frozen(mode)
                fighter, binding = self.kicks.build_profile(self.audit, self.native(), base, base_binding, mode)
                old_commands = {entry['commandId']: entry for entry in base['commands']}
                commands = {entry['commandId']: entry for entry in fighter['commands']}
                reserved = old_commands[self.kicks.sweep.RESERVED_STANDING_HEAVY_KICK]
                self.assertNotIn(reserved['commandId'], commands)
                self.assertEqual((['HeavyKick'], [], reserved['priority'], 0), tuple(commands['5D'][key] for key in
                                 ('buttons', 'directions', 'priority', 'bufferLifetimeFrames')))
                self.assertEqual(self.audit['command']['bufferPolicy'], commands['5D']['bufferPolicy'])
                self.assertEqual({**old_commands['2D'], 'bufferLifetimeFrames': 0,
                                  'bufferPolicy': commands['5D']['bufferPolicy']}, commands['2D'])
                wave_id = self.kicks.wave.COMMAND_ID if mode == 'default' else self.kicks.crouch.RESERVED_MODE2_WAVE
                ordered = (wave_id, 'ReservedGroundRecovery', self.kicks.RESERVED_BLOWBACK,
                           self.kicks.RESERVED_KICK_THROW, 'ReservedForwardLightPunch')
                self.assertEqual(sorted((commands[name]['priority'] for name in ordered), reverse=True),
                                 [commands[name]['priority'] for name in ordered])
                self.assertEqual(len(ordered), len({commands[name]['priority'] for name in ordered}))
                self.assertEqual([self.kicks.MOVE_IDS[245], self.kicks.MOVE_IDS[240]],
                                 [entry['moveId'] for entry in fighter['moves'] if entry['commandId'] == '5D'])
                moves = {move['moveId']: move for move in fighter['moves']}
                for state, action in ((245, 245), (240, far_action)):
                    move = moves[self.kicks.MOVE_IDS[state]]
                    self.assertEqual('Standing', move['allowedFrom'])
                    self.assertEqual(state == 245, 'maximumStartBodyDistance' in move)
                    clip = binding['clips'][binding['moves'][move['moveId']]['Active']]
                    self.assertEqual(action, clip['action'])
                    self.assertEqual(self.audit['actions'][action]['positionSteps'], move.get('positionSteps', []))
                    strike, = move['attacks']
                    self.assertEqual(('Heavy', 19, 18, True, False), tuple(strike.get(key, False) for key in
                        ('groundHitReaction', 'hitstunFrames', 'blockstunFrames', 'rejectsLyingTargets', 'rejectsFallingTargets')))
                    self.assertEqual(base_binding['hitReactions']['TerryCrouchHeavyPunch410'],
                                     binding['hitReactions'][move['moveId']])
                self.assertTrue(moves[self.kicks.sweep.MOVE]['walkOnCrouchRecovery'])
                changed = moves[self.kicks.sweep.MOVE]['moveId']
                self.assertNotIn('walkOnCrouchRecovery', next(move for move in base['moves'] if move['moveId'] == changed))
                self.assertEqual([move for move in base['moves'] if move['moveId'] != changed],
                                 [move for move in fighter['moves'] if move['commandId'] != '5D' and move['moveId'] != changed])
                self.assertEqual(base['groundHurtboxes'], fighter['groundHurtboxes'])

    def test_only_near_can_cancel_and_unverified_paths_are_rejected(self):
        for mode in ('default', 'mode2'):
            with self.subTest(mode=mode):
                native = self.native()
                base = self.frozen(mode)
                fighter, _ = self.kicks.build_profile(self.audit, native, *base, mode)
                far, near = (next(move for move in fighter['moves'] if move['moveId'] == self.kicks.MOVE_IDS[number])
                             for number in (240, 245))
                self.assertFalse(far['cancels'])
                self.assertFalse(far.get('reservedCancels'))
                guards = {rule['commandId']: (rule['windowFirstFrame'], rule['windowLastFrame'], rule['phases'])
                          for rule in near['reservedCancels']}
                self.assertEqual((4, 8, 'Active'), guards['ReservedForwardLightPunch'])
                self.assertEqual((4, 8, 'Active'), guards['ReservedRisingUpper'])
                if mode == 'default':
                    route, = near['cancels']
                    self.assertEqual((self.kicks.wave.MOVE_ID, 4, 8, 'Active', True),
                                     tuple(route[key] for key in ('targetMoveId', 'windowFirstFrame', 'windowLastFrame',
                                                                  'phases', 'allowWhiff')))
                else:
                    self.assertFalse(near['cancels'])
                    self.assertEqual((4, 8, 'Active'), guards['ReservedMode2Wave'])
                native['waveVerified'] = False
                with self.assertRaisesRegex(ValueError, 'verified native Wave'):
                    self.kicks.build_profile(self.audit, native, *base, mode)

    def test_mode2_enables_audited_ground_guard_without_importing_projectile_rules(self):
        guard_options = {'forwardGuardDistance', 'preserveGuardEntryMomentum', 'holdGroundBlockstunExitPosition',
                         'retainProjectileGuard', 'projectileGuardRecoveryFrames'}
        source = self.audit['mode2GroundGuard']
        self.assertEqual({'numerator': 230, 'denominator': 1}, source['forwardGuardDistance'])
        self.assertTrue(source['preserveGuardEntryMomentum'])
        self.assertTrue(source['holdGroundBlockstunExitPosition'])
        for mode in ('default', 'mode2'):
            with self.subTest(mode=mode):
                base, base_binding = self.frozen(mode)
                fighter, _ = self.kicks.build_profile(self.audit, self.native(), base, base_binding, mode)
                if mode == 'default':
                    self.assertEqual({key: base[key] for key in guard_options if key in base},
                                     {key: fighter[key] for key in guard_options if key in fighter})
                else:
                    self.assertEqual(source,
                                     {key: fighter[key] for key in guard_options if key in fighter})
                    self.assertTrue(guard_options.isdisjoint(base))
                    far = next(move for move in fighter['moves'] if move['moveId'] == self.kicks.MOVE_IDS[240])
                    self.assertEqual(23, far['guardThreatFrames'])

    def test_rejects_unproved_position_step_and_reaction_disagreement(self):
        base = self.frozen('default')
        native = self.native()
        native['positionStepsVerified'] = [241]
        with self.assertRaisesRegex(ValueError, 'Action 240.*position'):
            self.kicks.build_profile(self.audit, native, *base, 'default')
        native = self.native()
        native['posAdds'][240][0]['x'] = 16
        with self.assertRaisesRegex(ValueError, 'Action 240.*position'):
            self.kicks.build_profile(self.audit, native, *base, 'default')
        native = self.native()
        native['moves'][245]['groundReactions']['crouching']['initialTicks'] = 5
        with self.assertRaisesRegex(ValueError, 'Action 245 crouching reaction'):
            self.kicks.build_profile(self.audit, native, *base, 'default')

    def frozen(self, mode):
        return self.kicks.routes.load_frozen_base(self.root / 'data/imported-characters/terry98-stage26-final',
                                                 mode, self.audit, frozen_base=self.kicks.FROZEN_BASE)

    @staticmethod
    def native():
        def body(damage):
            return {
                'hit': {'damage': damage, 'initialPush': 10.5, 'effectiveStun': 19,
                        'pause': {'attackerFrames': 11, 'defenderFrames': 13}},
                'guard': {'damage': 0, 'initialPush': 10.5, 'effectiveStun': 18,
                          'pause': {'attackerFrames': 11, 'defenderFrames': 11}},
                'groundReactions': {
                    'standing': {'initialTicks': 4, 'effectiveStun': 19, 'initialAction': 5002, 'slideAction': 5007},
                    'crouching': {'initialTicks': 4, 'effectiveStun': 19, 'initialAction': 5022, 'slideAction': 5027},
                },
            }
        return {'moves': {240: body(107), 245: body(84)}, 'mode2Far': body(107),
                'positionStepsVerified': [240, 241], 'waveVerified': True,
                'posAdds': {240: [{'tick': 28, 'x': 32}], 241: [{'tick': tick, 'x': 8} for tick in (1, 4, 7, 10, 12)]},
                'forwardReserveVerified': True, 'risingReserveVerified': True,
                'blowbackVerified': True, 'kickThrowVerified': True}


if __name__ == '__main__':
    unittest.main()
