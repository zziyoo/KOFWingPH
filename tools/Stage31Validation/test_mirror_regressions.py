"""Exercise replay command preservation and fail-closed evidence checks without Godot."""
from pathlib import Path
from tempfile import TemporaryDirectory
from types import SimpleNamespace
import subprocess
import sys
import unittest
from unittest.mock import patch

import mirror_regressions as mirror


class MirrorTests(unittest.TestCase):
    def test_tampered_pinned_evidence_is_rejected(self):
        with TemporaryDirectory() as folder:
            path = Path(folder) / 'proof.log'
            path.write_bytes(b'tampered proof')
            with self.assertRaises(ValueError):
                mirror.pin(path, '0' * 64, {})

    def test_headless_replay_keeps_every_non_output_argument(self):
        saved = Path('D:/NewKOF-stage30-delivery-r3-stage29/stage29-headless/250-hit-wave-24/sprite/duel.log')
        with TemporaryDirectory() as folder:
            target = Path(folder) / 'new-run'
            options = SimpleNamespace(godot=Path('fake-godot.exe'))
            runs = []

            def fake_run(command, stream, item):
                stream.write(saved.read_bytes())
                item['exit'] = 0
                return 0

            with patch.object(mirror, 'execute_owned', side_effect=fake_run) as process:
                mirror.run(options, saved, target, {}, runs, headless=True)
            original = mirror.arguments(saved)
            actual = process.call_args.args[0][1:]
            expected = list(original)
            expected[expected.index('--log-file') + 1] = (target / 'godot-engine.log').as_posix()
            expected = [f'--shot={(target / "no-image.png").as_posix()}'
                        if arg.startswith('--shot=') else arg for arg in expected]
            self.assertEqual(expected, actual)
            self.assertEqual(0, runs[0]['exit'])
            self.assertFalse((target / 'end.png').exists())

    def test_nonzero_exit_is_recorded_and_rejected(self):
        saved = Path('D:/NewKOF-stage30-delivery-r3-stage29/stage29-headless/250-hit-wave-24/sprite/duel.log')
        with TemporaryDirectory() as folder:
            runs = []
            def fail(command, stream, item):
                item['exit'] = 7
                return 7

            with patch.object(mirror, 'execute_owned', side_effect=fail):
                with self.assertRaisesRegex(ValueError, 'exit 7'):
                    mirror.run(SimpleNamespace(godot=Path('fake.exe')), saved, Path(folder) / 'run',
                               {}, runs, headless=True)
            self.assertEqual(7, runs[0]['exit'])

    def test_existing_output_is_never_overwritten(self):
        with TemporaryDirectory() as folder:
            with patch.object(mirror.subprocess, 'Popen') as process:
                with self.assertRaises(FileExistsError):
                    mirror.run(SimpleNamespace(), Path('unused'), Path(folder), {}, [])
                process.assert_not_called()

    def test_timeout_reaps_owned_python_child(self):
        self.check_real_child_cleanup(timeout=1.5, parent_delay=60, expected='timeout')

    def test_exited_launcher_still_reaps_tracked_child(self):
        self.check_real_child_cleanup(timeout=5, parent_delay=0.9, expected=0)

    def check_real_child_cleanup(self, *, timeout, parent_delay, expected):
        with TemporaryDirectory() as folder:
            pidfile = Path(folder) / 'child.pid'
            script = ('import subprocess,sys,time; from pathlib import Path; '
                      'child=subprocess.Popen([sys.executable,"-c","import time; time.sleep(60)"]); '
                      'Path(sys.argv[1]).write_text(str(child.pid)); time.sleep(float(sys.argv[2]))')
            item = {}
            try:
                with (Path(folder) / 'output.log').open('wb') as stream:
                    if expected == 'timeout':
                        with self.assertRaises(subprocess.TimeoutExpired):
                            mirror.execute_owned([sys.executable, '-c', script, str(pidfile), str(parent_delay)],
                                                 stream, item, timeout=timeout)
                    else:
                        mirror.execute_owned([sys.executable, '-c', script, str(pidfile), str(parent_delay)],
                                             stream, item, timeout=timeout)
                self.assertEqual(expected, item['exit'])
                self.assertEqual('completed', item['cleanup'])
                self.assertTrue(pidfile.is_file(), 'owned test child was actually launched')
                self.assertFalse(mirror.psutil.pid_exists(int(pidfile.read_text())))
            finally:
                if pidfile.exists():
                    try:
                        child = mirror.psutil.Process(int(pidfile.read_text()))
                        child.kill()
                        child.wait(timeout=5)
                    except mirror.psutil.NoSuchProcess:
                        pass

    def test_interruption_cleans_up_before_returning(self):
        process = SimpleNamespace(pid=123, returncode=-1)
        process.wait = lambda **kwargs: (_ for _ in ()).throw(KeyboardInterrupt())
        child = SimpleNamespace(pid=456)
        parent = SimpleNamespace(children=lambda **kwargs: [child])
        item = {}
        with patch.object(mirror.subprocess, 'Popen', return_value=process), \
                patch.object(mirror.psutil, 'Process', return_value=parent), \
                patch.object(mirror, 'stop_run') as cleanup:
            with self.assertRaises(KeyboardInterrupt):
                mirror.execute_owned(['fake'], None, item)
            cleanup.assert_called_once()
            self.assertIs(process, cleanup.call_args.args[0])
            self.assertEqual([child], list(cleanup.call_args.args[1]))
        self.assertEqual('interrupted', item['exit'])
        self.assertEqual('completed', item['cleanup'])

    def test_unexpected_wait_error_cleans_up(self):
        process = SimpleNamespace(pid=123, returncode=-1)
        process.wait = lambda **kwargs: (_ for _ in ()).throw(OSError('wait failed'))
        parent = SimpleNamespace(children=lambda **kwargs: [])
        item = {}
        with patch.object(mirror.subprocess, 'Popen', return_value=process), \
                patch.object(mirror.psutil, 'Process', return_value=parent), \
                patch.object(mirror, 'stop_run') as cleanup:
            with self.assertRaisesRegex(OSError, 'wait failed'):
                mirror.execute_owned(['fake'], None, item)
            cleanup.assert_called_once()
        self.assertEqual('exception', item['exit'])
        self.assertEqual('completed', item['cleanup'])


if __name__ == '__main__':
    unittest.main()
