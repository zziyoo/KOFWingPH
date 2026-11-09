import struct
import unittest
import zlib
import tempfile
import zipfile
import json
from pathlib import Path
from mugen_importer.png import encode_png
from mugen_importer.__main__ import localcoord, run
from test_sff import fixture

class ExportTests(unittest.TestCase):
    def test_zip_import_is_deterministic_and_never_extracts_paths(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp)
            archive = base / 'input.zip'
            with zipfile.ZipFile(archive, 'w') as z:
                z.writestr('../escape.sff', fixture())
                z.writestr('../escape.air', '[Begin Action 0]\n7,8,0,0,2,H\n7,99,0,0,1,V\n-1,-1,0,0,-1,HV\n[Begin Action 0]\n7,9,0,0,1\n')
                z.writestr('../escape.def', '[Info]\nlocalcoord=320\n')
            run(archive, base/'a', 'sample', act='', atlas_size=32)
            run(archive, base/'b', 'sample', act='', atlas_size=32)
            self.assertEqual({p.name:p.read_bytes() for p in (base/'a').iterdir()},
                             {p.name:p.read_bytes() for p in (base/'b').iterdir()})
            self.assertFalse((base/'escape.sff').exists())
            manifest = json.loads((base/'a'/'manifest.json').read_text())
            self.assertEqual([f['flip'] for f in manifest['actions'][0]['frames']], ['H', 'V', 'HV'])
            self.assertIsNone(manifest['actions'][0]['frames'][2]['sprite_index'])
            self.assertTrue(any('missing sprite 7,99' in d for d in manifest['diagnostics']))
            self.assertTrue(any('duplicate action 0' in d for d in manifest['diagnostics']))
            self.assertFalse(any('missing sprite -1,-1' in d for d in manifest['diagnostics']))

    def test_localcoord_reads_declared_width(self):
        self.assertEqual(localcoord(b'[Info]\nlocalcoord = 640, 480 ; comment\n'), 640)
        self.assertEqual(localcoord(b'[Info]\nname = x\n'), 320)

    def test_png_roundtrip_and_determinism(self):
        rgba = bytes([255, 0, 1, 255, 0, 0, 0, 0])
        png = encode_png(2, 1, rgba)
        self.assertEqual(png, encode_png(2, 1, rgba))
        self.assertEqual(png[:8], b'\x89PNG\r\n\x1a\n')
        pos, compressed = 8, bytearray()
        while pos < len(png):
            size = struct.unpack_from('>I', png, pos)[0]
            kind = png[pos+4:pos+8]
            if kind == b'IDAT': compressed.extend(png[pos+8:pos+8+size])
            pos += 12 + size
        self.assertEqual(zlib.decompress(compressed), b'\0' + rgba)
