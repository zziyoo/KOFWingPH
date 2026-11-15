import os
from pathlib import Path
import unittest


@unittest.skipUnless(os.environ.get('NEWKOF_TERRY_AUDIT'), 'requires the local Terry ZIP and Manifest')
class OriginalForwardLightPunchTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        from mugen_importer import state_forward_light_punch as punch
        cls.punch = punch
        cls.root = Path(os.environ['NEWKOF_TERRY_AUDIT'])
        cls.audit = punch.inspect_source(cls.root / 'local-mugen/terry.zip',
                                         cls.root / 'data/imported-characters/terry98/manifest.json')

    def test_audits_state_300_body_and_command(self):
        body = self.audit['action']
        self.assertEqual((11, 9, 27), tuple(body[key] for key in ('startup', 'active', 'recovery')))
        self.assertEqual([(4, 5), (6, 7)], body['windows'])
        self.assertEqual([{'box': box, 'contactGroup': index} for index, box in
                          enumerate(((36, -92, 83, -61), (36, -88, 99, -57)), 1)], body['strikes'])
        self.assertEqual([{'frame': frame, 'x': {'numerator': x, 'denominator': 1}} for frame, x in
                          ((3, 8), (7, 8), (11, 8), (14, 8), (17, 8), (28, -8), (34, -8), (40, -8))],
                         body['positionSteps'])
        self.assertEqual(20, body['guardThreatFrames'])
        command = self.audit['command']
        self.assertEqual('ForwardLightPunch', command['commandId'])
        self.assertEqual((['LightPunch'], ['Forward'], 4), tuple(command[key] for key in
                         ('buttons', 'directions', 'priority')))
        self.assertTrue(command['reserveGroundInput'] and command['reserveOnEdgeDirection'])
        self.assertEqual(0, command['bufferLifetimeFrames'])
        policy = command['bufferPolicy']
        self.assertEqual((4, 1, 1, True, True), (policy['helperTicks'], policy['helperHeldDecay'],
                         policy['helperReleasedDecay'], policy['preserveDuringHitPause'],
                         policy['helperRunsDuringGlobalPause']))
        self.assertEqual([200, 205, 215, 235, 245, 250, 400, 410, 440], self.audit['var1Sources'])
        lines = command['sourceLines']
        self.assertLess(lines['cmd'], self.audit['action']['sourceLines']['state'])
        self.assertLess(lines['latch'], lines['decay'])

    def test_helper_latch_and_decay_are_the_pinned_source_blocks(self):
        from mugen_importer import state_reentry, state200
        from mugen_importer.state205215 import move_scope
        root = self.root
        with __import__('zipfile').ZipFile(root / 'local-mugen/terry.zip') as source:
            helper_raw = state200.archive_member(source, self.audit['members']['Helper.cns']['name'])
        _, helper = move_scope(state_reentry.script_sections(helper_raw), 99999)
        latch, decay = [part for part in helper if part.values('var(40)')]
        self.assertEqual(('varset', '4', 'command="x"&&command="holdfwd"&&command!="holddown"'),
                         (self.punch.state200.normalized(latch.one('type')), latch.one('var(40)'),
                          self.punch.state200.normalized(latch.one('trigger1'))))
        self.assertEqual(('varadd', '-1', 'var(40)>0'),
                         (self.punch.state200.normalized(decay.one('type')), decay.one('var(40)'),
                          self.punch.state200.normalized(decay.one('trigger1'))))
        self.assertFalse(any(part.values('ignorehitpause') for part in (latch, decay)))

    def test_build_replaces_placeholder_and_binds_the_real_move(self):
        for mode in ('default', 'mode2'):
            with self.subTest(mode=mode):
                base, base_binding = self.frozen(mode)
                fighter, binding = self.punch.build_profile(self.audit, self.native(), base, base_binding, mode)
                commands = {entry['commandId']: entry for entry in fighter['commands']}
                self.assertNotIn('ReservedForwardLightPunch', commands)
                self.assertIn('ReservedForwardLightPunch',
                              [c['commandId'] for c in base['commands']])
                replaced = commands['ForwardLightPunch']
                self.assertEqual(self.audit['command']['bufferPolicy'], replaced['bufferPolicy'])
                self.assertEqual(4, replaced['priority'])
                for source in ('TerryStandLightKick235', 'TerryStandHeavyKick245'):
                    move = next(entry for entry in fighter['moves'] if entry['moveId'] == source)
                    named = [rule['commandId'] for rule in move['reservedCancels']]
                    self.assertIn('ForwardLightPunch', named)
                    self.assertNotIn('ReservedForwardLightPunch', named)
                move = next(entry for entry in fighter['moves'] if entry['moveId'] == 'TerryBackKnuckle300')
                self.assertEqual(('ForwardLightPunch', 'GroundIdle', 11, 9, 27),
                                 tuple(move[key] for key in ('commandId', 'allowedFrom', 'startupFrames',
                                                             'activeFrames', 'recoveryFrames')))
                self.assertEqual([1, 2], [attack['contactGroup'] for attack in move['attacks']])
                self.assertEqual(('High', 'Low'), tuple(attack['height'] for attack in move['attacks']))
                self.assertEqual([(0, 2), (6, 8)], [(attack['firstActiveFrame'], attack['lastActiveFrame'])
                                                    for attack in move['attacks']])
                self.assertEqual(19, move['attacks'][0]['hitstunFrames'])
                self.assertEqual(18, move['attacks'][0]['blockstunFrames'])
                self.assertEqual({'numerator': 21, 'denominator': 2},
                                 move['attacks'][0]['groundKnockback'])
                self.assertEqual({'numerator': 63, 'denominator': 40},
                                 move['attacks'][0]['groundKnockbackFriction'])
                self.assertEqual(self.audit['action']['positionSteps'], move['positionSteps'])
                clip = binding['clips'][binding['moves']['TerryBackKnuckle300']['Active']]
                self.assertEqual(300, clip['action'])
                self.assertEqual(base_binding['hitReactions']['TerryCrouchHeavyPunch410'],
                                 binding['hitReactions']['TerryBackKnuckle300'])
                untouched = ('TerryStandHeavyPunch210', 'TerryStandLightKick230', 'TerryStandHeavyKick240',
                             'TerryCrouchLightKick430', 'TerryPowerWave1000')
                self.assertEqual([entry for entry in base['moves'] if entry['moveId'] in untouched],
                                 [entry for entry in fighter['moves'] if entry['moveId'] in untouched])

    def test_wave_exit_is_default_only_and_requires_contact(self):
        for mode in ('default', 'mode2'):
            with self.subTest(mode=mode):
                fighter, _ = self.build(mode)
                move = next(entry for entry in fighter['moves'] if entry['moveId'] == 'TerryBackKnuckle300')
                if mode == 'default':
                    route, = move['cancels']
                    self.assertEqual(('TerryPowerWave1000', 18, 19, 'Active', False, True, True),
                                     tuple(route[key] for key in ('targetMoveId', 'windowFirstFrame',
                                           'windowLastFrame', 'phases', 'allowWhiff', 'requiresHitConfirm',
                                           'requiresBlockConfirm')))
                else:
                    self.assertFalse(move['cancels'])

    def test_rejects_unverified_native_reference(self):
        for field in ('bodyVerified', 'waveExitVerified', 'completeVerified'):
            native = self.native()
            native[field] = False
            with self.assertRaises(ValueError):
                self.build('default', native=native)

    def build(self, mode, native=None):
        base, base_binding = self.frozen(mode)
        return self.punch.build_profile(self.audit, native or self.native(), base, base_binding, mode)

    def frozen(self, mode):
        return self.punch.routes.load_frozen_base(self.root / 'data/imported-characters/terry98-stage27-final',
                                                  mode, self.audit, frozen_base=self.punch.FROZEN_BASE)

    @staticmethod
    def native():
        contacts = [{'frame': frame, 'damage': damage, 'guarded': False, 'initialPush': 10.5,
                     'pause': {'attackerFrames': 11, 'defenderFrames': 13}}
                    for frame, damage in ((16, 12), (33, 9))]
        return {'moves': {300: {'contacts': contacts,
                                'crouchingContacts': contacts,
                                'guardContacts': [dict(contact, damage=0, guarded=True,
                                                       pause={'attackerFrames': 11, 'defenderFrames': 11})
                                                  for contact in contacts],
                                'crouchGuardContacts': contacts}},
                'posAdds': {300: [{'tick': frame, 'x': x} for frame, x in
                                  ((3, 8), (7, 8), (11, 8), (14, 8), (17, 8), (28, -8), (34, -8), (40, -8))]},
                'positionStepsVerified': [300], 'bodyVerified': True, 'entryVerified': True,
                'waveExitVerified': True, 'completeVerified': True}


if __name__ == '__main__':
    unittest.main()
