import copy
import unittest

from mugen_importer.state_power_wave import MOVE_ID
from mugen_importer.state_power_wave_heavy import PRESETS, apply_ground_reactions, build_profile
from mugen_importer.state_power_wave_routes import cancel_window


HEAVY = 'TerryStandHeavyPunch215'
CONDITION = 'StateNo = 215 && AnimElemTime(2) > 0 && AnimElemTime(4) < 0'
AUDIT = {'cancelWindow': [4, 12], 'phaseDurations': [3, 10, 15]}


def profile(mode):
    heavy = {'moveId': HEAVY, 'commandId': '5B', 'startupFrames': 3, 'activeFrames': 10,
             'recoveryFrames': 15, 'attacks': [{'contactGroup': 1}, {'contactGroup': 2}], 'cancels': []}
    others = [{'moveId': f'TerryStandLightPunch{state}', 'cancels': [{'targetMoveId': MOVE_ID}]}
              for state in (200, 205)]
    commands = [{'commandId': '5A'}, {'commandId': '5B'}]
    if mode == 'default':
        commands.append({'commandId': '236A'})
        others.append({'moveId': MOVE_ID, 'commandId': '236A', 'projectiles': [{'projectileTypeId': 'TerryPowerWave1020'}]})
    else:
        for move in others:
            move['cancels'] = []
    return {'moves': [heavy, *others], 'commands': commands}, {'clips': {'state215': {'action': 215}}}


class StatePowerWaveHeavyTests(unittest.TestCase):
    def test_strict_air_window_spans_both_contact_groups(self):
        self.assertEqual([4, 12], cancel_window(CONDITION, [3, 3, 7, 7, 8], source_state=215))
        self.assertEqual([3, 11], cancel_window(CONDITION, [2, 3, 7, 7, 8], source_state=215))
        with self.assertRaisesRegex(ValueError, 'State 215'):
            cancel_window(CONDITION.replace('> 0', '>= 0'), [3, 3, 7, 7, 8], source_state=215)
        with self.assertRaisesRegex(ValueError, 'AIR'):
            cancel_window(CONDITION, [3, 3, 7], source_state=215)

    def test_default_adds_one_route_without_mutating_the_two_hit_defs_or_other_moves(self):
        base = profile('default')
        frozen = copy.deepcopy(base)
        fighter, binding = build_profile(AUDIT, *base, 'default')
        self.assertEqual(frozen, base)
        self.assertEqual(PRESETS['default'], (fighter['fighterId'], binding['visualId']))
        self.assertEqual(base[0]['moves'][1:], fighter['moves'][1:])
        self.assertEqual(base[0]['moves'][0]['attacks'], fighter['moves'][0]['attacks'])
        self.assertEqual(base[0]['commands'], fighter['commands'])
        self.assertEqual(base[1]['clips'], binding['clips'])
        self.assertEqual([{'targetMoveId': MOVE_ID, 'windowFirstFrame': 4, 'windowLastFrame': 12,
                           'phases': 'Active', 'requiresHitConfirm': False, 'requiresBlockConfirm': False,
                           'allowWhiff': True}], fighter['moves'][0]['cancels'])

    def test_mode2_preserves_all_moves_and_commands(self):
        fighter, binding = profile('mode2')
        converted, visuals = build_profile(AUDIT, fighter, binding, 'mode2')
        self.assertEqual(PRESETS['mode2'], (converted['fighterId'], visuals['visualId']))
        self.assertEqual(fighter['moves'], converted['moves'])
        self.assertEqual(fighter['commands'], converted['commands'])

    def test_rejects_inconsistent_contact_groups_or_existing_cancel(self):
        fighter, binding = profile('default')
        source = fighter['moves'][0]
        source['attacks'][1]['contactGroup'] = 1
        with self.assertRaisesRegex(ValueError, 'contact groups'):
            build_profile(AUDIT, fighter, binding, 'default')
        source['attacks'][1]['contactGroup'] = 2
        source['cancels'].append({'targetMoveId': MOVE_ID})
        with self.assertRaisesRegex(ValueError, 'existing cancel'):
            build_profile(AUDIT, fighter, binding, 'default')

    def test_rejects_window_outside_active_or_mode2_with_weak_wave(self):
        with self.assertRaisesRegex(ValueError, 'Active phase'):
            build_profile({**AUDIT, 'cancelWindow': [4, 13]}, *profile('default'), 'default')
        with self.assertRaisesRegex(ValueError, 'mode 2'):
            build_profile(AUDIT, *profile('default'), 'mode2')

    def test_reaction_configuration_requires_native_release_evidence(self):
        fighter = {'groundHurtboxes': {'standing': [], 'crouching': []}}
        audit = {'lateGroundHitstun': {'boxes': [{'minX': -20}]}, 'guardEndFrames': {'standing': 4}}
        native = {'lateHitstunFrames': 6, 'guardReleaseVerified': False}
        with self.assertRaisesRegex(ValueError, 'native release probes'):
            apply_ground_reactions(fighter, audit, native)
        self.assertEqual({'groundHurtboxes': {'standing': [], 'crouching': []}}, fighter)
        apply_ground_reactions(fighter, audit, {**native, 'guardReleaseVerified': True})
        self.assertEqual(6, fighter['groundHurtboxes']['hitstunRecoveryFrames'])
        self.assertEqual([{'minX': -20}], fighter['groundHurtboxes']['hitstun'])
        self.assertEqual(4, fighter['projectileGuardRecoveryFrames'])
        self.assertTrue(fighter['retainProjectileGuard'])
        self.assertTrue(fighter['preserveGuardEntryMomentum'])
        self.assertTrue(fighter['protectGroundHitstunExit'])
