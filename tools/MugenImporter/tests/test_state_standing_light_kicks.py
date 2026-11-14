import os
from pathlib import Path
import unittest

from mugen_importer import state_crouch_light_kick as crouch
from mugen_importer import state_power_wave as wave


@unittest.skipUnless(os.environ.get('NEWKOF_TERRY_AUDIT'), 'requires the local Terry ZIP and Manifest')
class OriginalStandingLightKickTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        from mugen_importer import state_standing_light_kicks as kicks
        cls.kicks = kicks
        cls.root = Path(os.environ['NEWKOF_TERRY_AUDIT'])
        cls.audit = kicks.inspect_source(cls.root / 'local-mugen/terry.zip',
                                         cls.root / 'data/imported-characters/terry98/manifest.json')

    def test_derives_distinct_air_timelines_and_shared_damage_from_original(self):
        far, near = (self.audit['moves'][number] for number in (230, 235))
        self.assertEqual([2, 3, 3, 3, 4, 4], far['action']['durations'])
        self.assertEqual([1, 3, 5, 3, 4], near['action']['durations'])
        self.assertEqual([36, -72, 107, -49], far['action']['strikes'][0]['box'])
        self.assertEqual([17, -75, 62, -17], near['action']['strikes'][0]['box'])
        for reviewed in (far, near):
            self.assertEqual(('s,na', 'm', 'maf'), tuple(reviewed['hitDef'][key]
                             for key in ('attr', 'guardflag', 'hitflag')))
            self.assertEqual({'normalBase': 5, 'counterBase': 6, 'useContactDivisor': False}, reviewed['damage'])
        self.assertEqual(28, self.audit['command']['maximumStartBodyDistance'])
        self.assertEqual({'200': [7, 11], '205': [7, 11], '235': [9, 15], '400': [6, 12], '430': [6, 13]},
                         self.audit['command']['chainWindows'])
        self.assertEqual({'explicitWindow': None, 'naturalEntryTick': 19}, far['wavePermission'])
        self.assertEqual({'explicitWindow': [5, 8], 'naturalEntryTick': 16}, near['wavePermission'])

    def test_replaces_standing_reservation_and_keeps_near_before_far(self):
        for mode in ('default', 'mode2'):
            with self.subTest(mode=mode):
                fighter, binding = self.profile(mode)
                old_fighter, _ = self.frozen(mode)
                self.assertTrue(fighter['useOrderedGroundHitstunExit'])
                self.assertNotIn('useOrderedGroundHitstunExit', old_fighter)
                self.assertTrue(fighter['protectGroundBlockstunExit'])
                self.assertNotIn('protectGroundBlockstunExit', old_fighter)
                old_command = next(c for c in old_fighter['commands'] if c['commandId'] == crouch.RESERVED_STANDING_LIGHT_KICK)
                commands = {command['commandId']: command for command in fighter['commands']}
                self.assertNotIn(crouch.RESERVED_STANDING_LIGHT_KICK, commands)
                new_command = commands['5C']
                self.assertEqual((['LightKick'], [], old_command['priority'], 2), tuple(new_command[key] for key in
                                 ('buttons', 'directions', 'priority', 'bufferLifetimeFrames')))
                self.assertFalse(new_command.get('reserveGroundInput'))
                reserved_commands = (crouch.RESERVED_FORWARD_LIGHT_PUNCH, 'ReservedRisingUpper')
                old_commands = {command['commandId']: command for command in old_fighter['commands']}
                for command_id in reserved_commands:
                    self.assertEqual({**old_commands[command_id], 'reserveOnEdgeDirection': True,
                                      'bufferPolicy': {'helperTicks': 4, 'helperHeldDecay': 1, 'helperReleasedDecay': 1,
                                                       'preserveDuringHitPause': True}}, commands[command_id])
                old_wave = wave.COMMAND_ID if mode == 'default' else crouch.RESERVED_MODE2_WAVE
                recovery = commands['ReservedGroundRecovery']
                self.assertEqual((['LightPunch', 'LightKick'], ['Neutral', 'Back', 'Forward'], 2, True),
                                 tuple(recovery[key] for key in ('buttons', 'directions', 'bufferLifetimeFrames', 'reserveGroundInput')))
                self.assertGreater(commands[old_wave]['priority'], recovery['priority'])
                self.assertGreater(recovery['priority'], commands[crouch.RESERVED_FORWARD_LIGHT_PUNCH]['priority'])
                replaced = (crouch.RESERVED_STANDING_LIGHT_KICK, '5C', 'ReservedGroundRecovery', old_wave, *reserved_commands)
                self.assertEqual([c for c in old_fighter['commands'] if c['commandId'] not in replaced],
                                 [c for c in fighter['commands'] if c['commandId'] not in replaced])
                near, far = (move for move in fighter['moves'] if move['commandId'] == '5C')
                self.assertEqual('TerryStandLightKick235', near['moveId'])
                self.assertEqual('TerryStandLightKick230', far['moveId'])
                self.assertEqual({'numerator': 28, 'denominator': 1}, near['maximumStartBodyDistance'])
                self.assertNotIn('maximumStartBodyDistance', far)
                for move, number, timeline in ((near, 235, (4, 5, 7)), (far, 230, (5, 3, 11))):
                    self.assertEqual(timeline, tuple(move[key] for key in ('startupFrames', 'activeFrames', 'recoveryFrames')))
                    self.assertEqual('Standing', move['allowedFrom'])
                    attack, = move['attacks']
                    self.assertEqual(('Mid', 'LightLow', True, False),
                                     (attack['height'], attack['groundHitReaction'], attack['rejectsLyingTargets'],
                                      attack.get('rejectsFallingTargets', False)))
                    self.assertEqual((11, 10), (attack['hitstunFrames'], attack['blockstunFrames']))
                    self.assertEqual(number == 235, attack.get('retainsGroundGuardOnFollowup', False))
                    self.assertEqual(number, binding['clips'][binding['moves'][move['moveId']]['Active']]['action'])
                    self.assertEqual(binding['hitReactions'][crouch.MOVE], binding['hitReactions'][move['moveId']])
                self.assertEqual(old_fighter['groundHurtboxes'], fighter['groundHurtboxes'])

    def test_keeps_distinct_source_windows_and_rejects_unobserved_far_routes(self):
        for mode in ('default', 'mode2'):
            with self.subTest(mode=mode):
                fighter, _ = self.profile(mode)
                moves = {move['moveId']: move for move in fighter['moves']}
                near, far = 'TerryStandLightKick235', 'TerryStandLightKick230'
                old = ('TerryStandLightPunch200', 'TerryStandLightPunch205', 'TerryCrouchLightPunch400', crouch.MOVE)
                for source, window in zip((*old, near), ((7, 11), (7, 11), (6, 12), (6, 13), (9, 15)), strict=True):
                    added = [route for route in moves[source]['cancels'] if route['targetMoveId'] in (near, far)]
                    self.assertEqual([near, far], [route['targetMoveId'] for route in added])
                    for route in added:
                        self.assertEqual((*window, True, True, 2), tuple(route[key] for key in
                                         ('windowFirstFrame', 'windowLastFrame', 'standingInputOnly', 'allowWhiff', 'maximumInputAge')))
                        self.assertEqual(route['targetMoveId'] == source, route['allowSameMove'])
                    self.assertTrue(all(route['commandId'] != crouch.RESERVED_STANDING_LIGHT_KICK
                                        for route in moves[source].get('reservedCancels', [])))
                self.assertFalse(moves[far]['cancels'])
                near_wave = [route for route in moves[near]['cancels'] if route['targetMoveId'] == wave.MOVE_ID]
                if mode == 'default':
                    route, = near_wave
                    self.assertEqual((5, 8, 'Active', True), tuple(route[key] for key in
                                     ('windowFirstFrame', 'windowLastFrame', 'phases', 'allowWhiff')))
                    self.assertEqual([('ReservedForwardLightPunch', 5, 8), ('ReservedRisingUpper', 5, 8)],
                                     self.reserved_windows(moves[near]))
                else:
                    self.assertFalse(near_wave)
                    self.assertEqual([('ReservedMode2Wave', 5, 8), ('ReservedForwardLightPunch', 5, 8),
                                      ('ReservedRisingUpper', 5, 8)],
                                     self.reserved_windows(moves[near]))
                punches = [route for route in moves[near]['cancels'] if route.get('retainBufferedMotionHelper')]
                self.assertEqual(['TerryStandLightPunch205', 'TerryStandLightPunch200', 'TerryCrouchLightPunch400'],
                                 [route['targetMoveId'] for route in punches])

    def test_rejects_trace_contract_with_wrong_push_or_unverified_wave(self):
        measurements = self.native()
        measurements['moves'][235]['guard']['initialPush'] = 7
        with self.assertRaisesRegex(ValueError, 'State 235 guard sample'):
            self.kicks.build_profile(self.audit, measurements, *self.frozen('default'), 'default')
        measurements = self.native()
        measurements['waveVerified'] = False
        with self.assertRaisesRegex(ValueError, 'native.*Wave'):
            self.kicks.build_profile(self.audit, measurements, *self.frozen('default'), 'default')

    def frozen(self, mode):
        return self.kicks.routes.load_frozen_base(self.root / 'data/imported-characters/terry98-stage25-final',
                                                 mode, self.audit, frozen_base=self.kicks.FROZEN_BASE)

    def profile(self, mode):
        return self.kicks.build_profile(self.audit, self.native(), *self.frozen(mode), mode)

    @staticmethod
    def reserved_windows(move):
        return [tuple(rule[key] for key in ('commandId', 'windowFirstFrame', 'windowLastFrame'))
                for rule in move['reservedCancels']]

    @staticmethod
    def native():
        def body():
            return {
                'hit': {'damage': 30, 'initialPush': 6, 'effectiveStun': 11,
                        'pause': {'attackerFrames': 11, 'defenderFrames': 13}},
                'guard': {'damage': 0, 'initialPush': 6, 'effectiveStun': 10,
                          'pause': {'attackerFrames': 11, 'defenderFrames': 11}},
                'groundReactions': {
                    'standing': {'initialTicks': 6, 'effectiveStun': 11, 'initialAction': 5010, 'slideAction': 5015},
                    'crouching': {'initialTicks': 6, 'effectiveStun': 11, 'initialAction': 5020, 'slideAction': 5025},
                },
            }
        return {'moves': {230: body(), 235: body()}, 'chainsVerified': True, 'waveVerified': True,
                'guardFollowupVerified': [235], 'forwardReserveVerified': True, 'risingReserveVerified': True,
                'groundRecoveryReserveVerified': True, 'farGuardControlVerified': True,
                'retainHelperTargets': [200, 400, 205]}
