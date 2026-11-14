import os
from pathlib import Path
import unittest

from mugen_importer import state_crouch_light_kick as kick
from mugen_importer import state_power_wave as wave


@unittest.skipUnless(os.environ.get('NEWKOF_TERRY_AUDIT'), 'requires the local original Terry archive and manifest')
class OriginalCrouchLightKickTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.root = Path(os.environ['NEWKOF_TERRY_AUDIT'])
        cls.audit = kick.inspect_source(cls.root / 'local-mugen/terry.zip',
                                        cls.root / 'data/imported-characters/terry98/manifest.json')

    def test_action_geometry_comes_from_430_instead_of_the_sweep(self):
        action = self.audit['action']
        self.assertEqual((430, 3, 3, 8), tuple(action[key] for key in ('id', 'startup', 'active', 'recovery')))
        self.assertEqual([1, 2, 3, 3, 2, 3], action['durations'])
        self.assertEqual([{'first': 0, 'last': 2, 'box': [0, -16, 63, -1]}], action['strikes'])
        self.assertEqual([[-20, -72, 19, -1], [-12, -24, 51, -1]], action['clsn2'][2])
        self.assertEqual([[-20, -72, 19, -1], [12, -32, 43, -1]], action['clsn2'][3])
        self.assertEqual(6, self.audit['guardThreatFrames'])

    def test_ground_low_contact_does_not_import_the_trip(self):
        self.assertEqual({'counterBase': 8, 'normalBase': 7, 'useContactDivisor': False}, self.audit['damage'])
        self.assertEqual(('c,na', 'ma', 'l', 'low'), tuple(self.audit['hitDef'][key] for key in
                         ('attr', 'hitflag', 'guardflag', 'ground.type')))
        self.assertEqual((11, 13), self.audit['groundDefaults']['hitPause'])
        self.assertEqual((11, 11), self.audit['groundDefaults']['guardPause'])
        self.assertEqual({'numerator': 6, 'denominator': 1}, self.audit['groundDefaults']['initialPush'])
        self.assertEqual(9, self.audit['groundDefaults']['hitTime'])

    def test_cmd_chain_windows_use_air_elements_and_wave_only_has_natural_permission(self):
        self.assertEqual({'200': [7, 11], '205': [7, 11], '400': [6, 12], '430': [6, 13]},
                         self.audit['command']['chainWindows'])
        self.assertEqual(2, self.audit['command']['bufferFrames'])
        self.assertEqual([230, 235], self.audit['command']['deferredTargets'])
        self.assertEqual({'explicitCancel': False, 'naturalEntryTick': 14}, self.audit['wavePermission'])

    def test_real_light_kick_replaces_reservation_with_both_ground_reactions(self):
        native = self.native_body()
        for mode in ('default', 'mode2'):
            with self.subTest(mode=mode):
                frozen = self.frozen(mode)
                fighter, binding = kick.build_profile(self.audit, native, *frozen, mode, with_chains=False)
                command, = (entry for entry in fighter['commands'] if entry['buttons'] == ['LightKick'])
                self.assertEqual(('2C', -1, 2), tuple(command[key] for key in
                                 ('commandId', 'priority', 'bufferLifetimeFrames')))
                self.assertFalse(command.get('reserveGroundInput'))
                self.assertIn('ReservedCrouchLightKick', [item['commandId'] for item in frozen[0]['commands']])
                move = fighter['moves'][-1]
                self.assertEqual(('TerryCrouchLightKick430', '2C', 'Crouching', True), tuple(move[key] for key in
                                 ('moveId', 'commandId', 'allowedFrom', 'returnsToCrouch')))
                self.assertIs(move.get('walkOnCrouchRecovery'), True)
                attack, = move['attacks']
                self.assertEqual(('Low', 'LightLow', 11, 10, 0, 0, True, True), tuple(attack[key] for key in
                                 ('height', 'groundHitReaction', 'hitstunFrames', 'blockstunFrames', 'knockdownFrames',
                                  'wakeupFrames', 'rejectsLyingTargets', 'rejectsFallingTargets')))
                self.assertEqual(frozen[0]['groundHurtboxes'], fighter['groundHurtboxes'])
                self.assertEqual(frozen[0]['moves'], fighter['moves'][:-1])
                self.assertEqual(430, binding['clips']['state430']['action'])
                reaction = binding['hitReactions'][move['moveId']]
                self.assertEqual((5010, 5015, 6), (binding['clips'][reaction['initial']]['action'],
                                 binding['clips'][reaction['followup']]['action'], reaction['followupTick']))
                crouching = reaction['crouching']
                self.assertEqual((5020, 5025, 6), (binding['clips'][crouching['initial']]['action'],
                                 binding['clips'][crouching['followup']]['action'], crouching['followupTick']))
                self.assertEqual(mode == 'default', any(m['moveId'] == wave.MOVE_ID for m in fighter['moves']))

    def test_body_rejects_measured_reactions_that_disagree_with_the_source(self):
        native = self.native_body()
        native['hit']['initialPush'] = 7
        with self.assertRaisesRegex(ValueError, 'State 430 hit sample'):
            kick.build_profile(self.audit, native, *self.frozen('default'), 'default', with_chains=False)

    def test_normal_routes_use_the_audited_window_and_never_grant_430_wave_cancel(self):
        native = {**self.native_body(), 'chainsVerified': True}
        fighter, _ = kick.build_profile(self.audit, native, *self.frozen('default'), 'default')
        by_id = {move['moveId']: move for move in fighter['moves']}
        crouch_kick = by_id['TerryCrouchLightKick430']
        self.assertEqual(['TerryStandLightPunch205', 'TerryStandLightPunch200',
                          'TerryCrouchLightPunch400', 'TerryCrouchLightKick430'],
                         [route['targetMoveId'] for route in crouch_kick['cancels']])
        for route in crouch_kick['cancels']:
            self.assertEqual((6, 13, 'Active,Recovery', 2, False, False, True), tuple(route[key] for key in
                             ('windowFirstFrame', 'windowLastFrame', 'phases', 'maximumInputAge',
                              'requiresHitConfirm', 'requiresBlockConfirm', 'allowWhiff')))
            self.assertEqual(route['targetMoveId'] != kick.MOVE, route.get('retainBufferedMotionHelper', False))
        for source, window in [('TerryStandLightPunch200', (7, 11)), ('TerryStandLightPunch205', (7, 11)),
                               ('TerryCrouchLightPunch400', (6, 12))]:
            route, = (c for c in by_id[source]['cancels'] if c['targetMoveId'] == 'TerryCrouchLightKick430')
            self.assertEqual(window, (route['windowFirstFrame'], route['windowLastFrame']))
        self.assertEqual({'commandId': 'ReservedStandLightKick', 'windowFirstFrame': 6, 'windowLastFrame': 13,
                          'phases': 'Active,Recovery'}, crouch_kick['reservedCancels'][0])
        command_order = {command['commandId']: command['priority'] for command in fighter['commands']}
        self.assertEqual([wave.COMMAND_ID, 'ReservedForwardLightPunch', 'ReservedRisingUpper', '5A', '5B',
                          'ReservedStandLightKick', '2C'],
                         sorted((key for key in command_order if command_order[key] >= -1),
                                key=command_order.get, reverse=True))
        self.assertGreater(command_order['2C'], command_order['2D'])
        for move in fighter['moves']:
            for attack in move['attacks']:
                self.assertEqual(move['moveId'] in ('TerryCrouchLightPunch400', kick.MOVE),
                                 attack.get('retainsGroundGuardOnFollowup', False))
        frozen_wave = [(move['moveId'], c) for move in self.frozen('default')[0]['moves'] for c in move['cancels']
                       if c['targetMoveId'] == wave.MOVE_ID]
        self.assertEqual(frozen_wave, [(move['moveId'], c) for move in fighter['moves'] for c in move['cancels']
                                      if c['targetMoveId'] == wave.MOVE_ID])
        self.assertEqual(6, len(frozen_wave))
        with self.assertRaisesRegex(ValueError, 'native ordinary transitions'):
            kick.build_profile(self.audit, self.native_body(), *self.frozen('default'), 'default')

    def test_mode2_reserves_the_motion_only_on_natural_ground_entry(self):
        native = {**self.native_body(), 'chainsVerified': True, 'mode2NaturalWaveVerified': True}
        base, _ = self.frozen('default')
        default_wave, = (entry for entry in base['commands'] if entry['commandId'] == wave.COMMAND_ID)
        fighter, _ = kick.build_profile(self.audit, native, *self.frozen('mode2'), 'mode2',
                                        default_wave_command=default_wave)
        command, = (entry for entry in fighter['commands'] if entry['commandId'] == 'ReservedMode2Wave')
        self.assertEqual({**default_wave, 'commandId': 'ReservedMode2Wave', 'reserveGroundInput': True,
                          'priority': default_wave['priority'] + 2}, command)
        self.assertFalse(any(move['moveId'] == wave.MOVE_ID for move in fighter['moves']))
        crouch_kick, = (move for move in fighter['moves'] if move['moveId'] == kick.MOVE)
        self.assertTrue(all(route['commandId'] != 'ReservedMode2Wave' for route in crouch_kick['reservedCancels']))
        frozen_moves = {move['moveId']: move for move in self.frozen('mode2')[0]['moves']}
        for move in fighter['moves'][:-1]:
            legacy = [route for route in move['cancels'] if route['targetMoveId'] != kick.MOVE]
            self.assertEqual(frozen_moves[move['moveId']]['cancels'], legacy)

    def frozen(self, mode):
        return kick.routes.load_frozen_base(self.root / 'data/imported-characters/terry98-stage24-final',
                                            mode, self.audit, frozen_base=kick.FROZEN_BASE)

    @staticmethod
    def native_body():
        return {
            'hit': {'damage': 46, 'initialPush': 6, 'effectiveStun': 11,
                    'pause': {'attackerFrames': 11, 'defenderFrames': 13}},
            'guard': {'damage': 0, 'initialPush': 6, 'effectiveStun': 10,
                      'pause': {'attackerFrames': 11, 'defenderFrames': 11}},
            'groundReactions': {
                'standing': {'initialTicks': 6, 'effectiveStun': 11, 'initialAction': 5010, 'slideAction': 5015},
                'crouching': {'initialTicks': 6, 'effectiveStun': 11, 'initialAction': 5020, 'slideAction': 5025},
            },
        }
