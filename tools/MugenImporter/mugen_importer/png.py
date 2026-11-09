"""Minimal deterministic RGBA PNG writer using only Python's standard library."""
import struct
import zlib

def _chunk(kind, payload):
    return struct.pack('>I', len(payload)) + kind + payload + struct.pack('>I', zlib.crc32(kind + payload))

def encode_png(width, height, rgba):
    if width <= 0 or height <= 0 or len(rgba) != width * height * 4:
        raise ValueError('invalid PNG dimensions or pixel length')
    rows = b''.join(b'\0' + rgba[y*width*4:(y+1)*width*4] for y in range(height))
    return (b'\x89PNG\r\n\x1a\n' +
            _chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0)) +
            _chunk(b'IDAT', zlib.compress(rows, 9)) + _chunk(b'IEND', b''))
