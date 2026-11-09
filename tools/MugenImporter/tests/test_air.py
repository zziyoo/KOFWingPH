import unittest
from mugen_importer.air import parse_air


class AirTests(unittest.TestCase):
    def test_frames_loop_flips_and_duplicates(self):
        actions, diagnostics = parse_air('''[Begin Action 0]
0, 1, -2, 3, 4, HV
Clsn2: 1
Clsn2[0] = -10,-20,10,0
LoopStart
-1, -1, 0, 0, -1
[Begin Action 0]
1, 2, 0, 0, 3, H
''')
        self.assertEqual(len(actions), 2)
        self.assertEqual(actions[0]['loop_start'], 1)
        self.assertEqual(actions[0]['frames'][0]['flip'], 'HV')
        self.assertEqual(actions[0]['frames'][1]['time'], -1)
        self.assertEqual(len(actions[0]['collisions']), 2)
        self.assertTrue(any('duplicate action 0' in d for d in diagnostics))

if __name__ == '__main__':
    unittest.main()
