import unittest
from fractions import Fraction
from itertools import product

from mugen_importer.state_damage import MOVE_IDS, build_profile, parse_formula, resolved_damage, verify_damage_variables, verify_random


class StateDamageTests(unittest.TestCase):
    def test_normal_counter_formula_and_integer_quotient_are_distinct(self):
        self.assertEqual({'normalBase': 5, 'counterBase': 6, 'useContactDivisor': False},
                         parse_formula('floor((ifelse(var(6),6,5)-var(21))*fvar(7)), 0'))
        self.assertEqual({'normalBase': 9, 'counterBase': 9, 'useContactDivisor': True},
                         parse_formula('floor(((9-var(21))/var(23))*fvar(7)), 0'))
        with self.assertRaisesRegex(ValueError, 'unsupported damage expression'):
            parse_formula('floor(((9-var(21))/var(24))*fvar(7)), 0')

    def test_ikemen_floor_multiplier_reciprocal_defense_rounding(self):
        heavy = {'normalBase': 9, 'counterBase': 9, 'useContactDivisor': True}
        values = dict(scale=Fraction('9.615'), attack_multiplier=Fraction('.8'))
        self.assertEqual((76, 61), resolved_damage(heavy, 1, False, 1, target_hurt=False, **values))
        self.assertEqual((67, 54), resolved_damage(heavy, 2, True, 1, target_hurt=False, **values))
        self.assertEqual((38, 29), resolved_damage(heavy, 0, False, 2, target_hurt=True, **values))
        self.assertEqual((38, 30), resolved_damage(heavy, 1, False, 2, target_hurt=False, **values))
        self.assertEqual((86, 69), resolved_damage(heavy, 0, False, 1, target_hurt=False, **values))

    def test_opt_in_profile_replaces_fixed_damage_without_mutating_frozen_base(self):
        base = {'fighterId': 'TerryNormals', 'commands': [{'commandId': '5A'}],
                'moves': [{'moveId': move, 'attacks': [{'damage': 5, 'counterDamage': 6,
                                                         'damageAfterPriorContact': 7, 'hitstopFrames': 11}]}
                          for move in MOVE_IDS.values()]}
        visual = {'visualId': 'terry98-stage16-default', 'clips': {'state205': {'action': 205}}}
        globals_audit = {'scale': {'numerator': 1923, 'denominator': 200},
                         'attackMultiplier': {'numerator': 4, 'denominator': 5},
                         'defenseController': {'numerator': 97, 'denominator': 100},
                         'randomMin': 0, 'randomMax': 2, 'counterGraceTicks': 2}
        formula = {'line': 123, 'normalBase': 9, 'counterBase': 9, 'useContactDivisor': True}
        audit = {'globals': globals_audit, 'formulas': {number: [formula] for number in MOVE_IDS}}
        fighter, binding = build_profile(audit, base, visual, 'default')
        self.assertEqual('TerryDamage', fighter['fighterId'])
        self.assertEqual('terry98-stage17-default', binding['visualId'])
        self.assertEqual(globals_audit['defenseController'], fighter['damageRules']['hitstunDefenseMultiplier'])
        self.assertEqual({'damage': 0, 'hitstopFrames': 11,
                          'dynamicDamage': {'normalBase': 9, 'counterBase': 9, 'useContactDivisor': True}},
                         fighter['moves'][0]['attacks'][0])
        self.assertEqual(5, base['moves'][0]['attacks'][0]['damage'])
        self.assertEqual('terry98-stage16-default', visual['visualId'])

    def test_rejects_changed_draw_bucket_and_disconnected_rng(self):
        rows = [
            {'phase': 'rng-draw', 'before': 1, 'after': 16807},
            {'phase': 'var21-draw', 'seat': 1, 'min': 0, 'max': 2, 'value': 0, 'rng': 16807},
            {'phase': 'rng-draw', 'before': 16807, 'after': 282475249},
            {'phase': 'var21-draw', 'seat': 2, 'min': 0, 'max': 2, 'value': 0, 'rng': 282475249},
        ]
        self.assertEqual({'rngDraws': 2, 'characterDraws': 2}, verify_random(rows, 1))
        rows[-1]['value'] = 2
        with self.assertRaisesRegex(ValueError, 'invalid native var21'):
            verify_random(rows, 1)
        rows[-1]['value'] = 0
        rows[2]['before'] = 1
        with self.assertRaisesRegex(ValueError, 'disconnected native RNG'):
            verify_random(rows, 1)

    def test_exact_rational_core_agrees_with_native_for_every_reviewed_operand(self):
        scale, attack, hit_defense = map(Fraction, ('9.615', '.8', '.97'))
        formulas = [(7, 8, False), (5, 6, False), (16, 20, False), (9, 9, True)]
        for normal, counter_base, grouped in formulas:
            formula = {'normalBase': normal, 'counterBase': counter_base, 'useContactDivisor': grouped}
            for variance, counter, divisor, target_hurt in product(range(3), (False, True), (1, 2), (False, True)):
                strength = (counter_base if counter else normal) - variance
                raw = int((strength // divisor if grouped else strength) * scale)
                damage = int(raw * attack * (hit_defense if target_hurt else 1) + Fraction(1, 2))
                self.assertEqual((raw, damage), resolved_damage(formula, variance, counter, divisor,
                                                               scale=scale, attack_multiplier=attack,
                                                               target_hurt=target_hurt))

    def test_counter_ages_and_resets_before_hitdef_and_divisor_changes_only_after_contact(self):
        def event(frame, variable, op, before, after, target):
            return {'phase': 'damage-var', 'frame': frame, 'seat': 1, 'variable': variable,
                    'operation': op, 'before': before, 'after': after, 'state': 215,
                    'opponentMoveType': target, 'hitPause': 0}

        attack, idle, hurt = 131072, 32768, 65536
        samples = [event(5, 6, 'set', 0, 1, attack), event(5, 23, 'set', 0, 1, idle),
                   event(6, 6, 'add', 1, 2, idle), event(7, 6, 'add', 2, 3, idle),
                   event(8, 6, 'add', 3, 4, idle), event(8, 6, 'set', 4, 0, idle),
                   event(20, 23, 'add', 1, 2, hurt)]
        result = verify_damage_variables(samples)
        self.assertEqual((5, 2), (result['counterUpdates'], result['divisorUpdates']))
        samples[-1]['hitPause'] = 3
        with self.assertRaisesRegex(ValueError, 'divisor update during hit pause'):
            verify_damage_variables(samples)


if __name__ == '__main__':
    unittest.main()
