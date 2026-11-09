import struct
import unittest
from mugen_importer.sff import read_sff
from mugen_importer.pcx import decode_pcx, read_act


def pcx():
    h = bytearray(128)
    h[0:4] = bytes([10, 5, 1, 8])
    struct.pack_into('<HHHH', h, 4, 0, 0, 1, 0)
    h[65] = 1
    struct.pack_into('<H', h, 66, 2)
    pal = bytearray(768)
    pal[3:6] = bytes([255, 4, 9])
    return bytes(h) + bytes([0, 1, 12]) + pal


def fixture():
    p = pcx()
    header = bytearray(512)
    header[:12] = b'ElecbyteSpr\0'
    header[12:16] = bytes([0, 1, 0, 1])
    struct.pack_into('<IIII', header, 16, 1, 2, 512, 32)
    first = struct.pack('<IIhhHHHB13x', 512+32+len(p), len(p), -3, 4, 7, 8, 0, 0)
    second = struct.pack('<IIhhHHHB13x', 0, 0, 5, -6, 7, 9, 0, 1)
    return bytes(header) + first + p + second


class SffTests(unittest.TestCase):
    def test_invalid_header_and_unsupported_v2_are_explicit(self):
        with self.assertRaisesRegex(ValueError, 'signature'):
            read_sff(b'invalid')
        data = bytearray(fixture())
        data[15] = 2
        with self.assertRaisesRegex(ValueError, 'v2 is not implemented'):
            read_sff(data)

    def test_index_links_origins_and_color(self):
        sprites = read_sff(fixture())
        self.assertEqual([(s.group, s.image, s.x, s.y) for s in sprites], [(7, 8, -3, 4), (7, 9, 5, -6)])
        self.assertEqual(sprites[1].linked_to, 0)
        image = decode_pcx(sprites[0].pcx)
        self.assertEqual(image.rgba, bytes([0, 0, 0, 0, 255, 4, 9, 255]))

    def test_invalid_link_is_rejected(self):
        data = bytearray(fixture())
        struct.pack_into('<H', data, len(data) - 32 + 16, 2)
        with self.assertRaises(ValueError):
            read_sff(data)

    def test_last_pointer_may_point_to_end_of_file(self):
        data = bytearray(fixture())
        struct.pack_into('<I', data, len(data) - 32, len(data))
        self.assertEqual(len(read_sff(data)), 2)

    def test_act_is_reversed_and_bounded(self):
        data = bytes(range(256))*3
        palette = read_act(data)
        self.assertEqual(palette[:3], data[-3:])
        with self.assertRaises(ValueError):
            read_act(b'bad')

    def test_shared_palette_does_not_misread_pixel_data_as_palette_marker(self):
        data = bytearray(pcx()[:128])
        struct.pack_into('<HHHH', data, 4, 0, 0, 767, 0)
        struct.pack_into('<H', data, 66, 768)
        data.extend(bytes([12]) + bytes([1]) * 768)
        image = decode_pcx(bytes(data), bytes(768))
        self.assertEqual(image.width, 768)

    def test_sixty_eight_link_records_preserve_prior_indices(self):
        first = fixture()[:-32]
        data = bytearray(first)
        struct.pack_into('<I', data, 20, 69)
        for i in range(68):
            offset = len(data)
            following = offset + 32 if i < 67 else 0
            data.extend(struct.pack('<IIhhHHHB13x', following, 0, i, -i, 7, i + 1, i, 1))
        result = read_sff(data)
        self.assertEqual([s.linked_to for s in result[1:]], list(range(68)))

    def test_overlapping_sprite_payload_is_rejected(self):
        data = bytearray(fixture())
        struct.pack_into('<I', data, 512, 544)
        data[544:576] = struct.pack('<IIhhHHHB13x', 0, 0, 0, 0, 2, 3, 0, 1)
        with self.assertRaises(ValueError):
            read_sff(data)

if __name__ == '__main__':
    unittest.main()
