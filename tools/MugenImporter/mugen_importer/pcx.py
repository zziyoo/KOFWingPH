"""Decode bounded 8-bit single-plane PCX and MUGEN ACT palettes."""
from dataclasses import dataclass
import struct

@dataclass(frozen=True)
class Image:
    width: int
    height: int
    rgba: bytes
    palette: bytes

def read_act(data):
    if len(data) not in (768, 772):
        raise ValueError('ACT palette must contain 768 or 772 bytes')
    return b''.join(data[i:i+3] for i in range(765, -1, -3))

def decode_pcx(data, palette=None):
    if len(data) < 128 or data[0] != 10 or data[2] != 1 or data[3] != 8 or data[65] != 1:
        raise ValueError('unsupported or invalid PCX (expected 8-bit RLE, one plane)')
    xmin, ymin, xmax, ymax = struct.unpack_from('<HHHH', data, 4)
    width, height = xmax - xmin + 1, ymax - ymin + 1
    stride = struct.unpack_from('<H', data, 66)[0]
    if width <= 0 or height <= 0 or stride < width or stride * height > 16_777_216:
        raise ValueError('invalid PCX dimensions')
    own = data[-768:] if len(data) >= 897 and data[-769] == 12 else None
    selected = palette if palette is not None else own
    if selected is None or len(selected) != 768:
        raise ValueError('missing PCX palette')
    # A shared-palette PCX has no trailer; an RLE byte may coincidentally
    # equal the trailer marker at precisely length-769.
    end = len(data) - 769 if own is not None and palette is None else len(data)
    pos, pixels = 128, bytearray()
    required = stride * height
    while len(pixels) < required:
        if pos >= end:
            raise ValueError('truncated PCX RLE')
        value = data[pos]; pos += 1
        if value >= 192:
            run = value & 63
            if run == 0 or pos >= end:
                raise ValueError('invalid PCX RLE run')
            value = data[pos]; pos += 1
        else:
            run = 1
        if len(pixels) + run > required:
            raise ValueError('PCX RLE exceeds image')
        pixels.extend(bytes([value]) * run)
    rgba = bytearray(width * height * 4)
    for y in range(height):
        for x in range(width):
            index = pixels[y*stride+x]
            out = (y*width+x)*4
            rgba[out:out+3] = selected[index*3:index*3+3]
            rgba[out+3] = 255 if index else 0
    return Image(width, height, bytes(rgba), selected)
