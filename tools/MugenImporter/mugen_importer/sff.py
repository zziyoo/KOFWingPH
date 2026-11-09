"""Bounded SFF v1 linked-list reader."""
from dataclasses import dataclass
from bisect import bisect_left, insort
import struct

@dataclass(frozen=True)
class Sprite:
    group: int
    image: int
    x: int
    y: int
    linked_to: int | None
    same_palette: bool
    pcx: bytes | None

def read_sff(data):
    if len(data) > 128 * 1024 * 1024:
        raise ValueError('SFF exceeds 128 MiB limit')
    if len(data) < 512 or data[:12] != b'ElecbyteSpr\0':
        raise ValueError('invalid SFF signature or truncated header')
    if data[15] != 1:
        raise ValueError(f'unsupported SFF major version {data[15]}; v2 is not implemented')
    return _read_v1(data)

def _read_v1(data):
    """Version dispatch stays separate so a future v2 reader can be added."""
    groups, count, offset, size = struct.unpack_from('<IIII', data, 16)
    if count > 100000 or groups > count or size < 32 or size > 4096:
        raise ValueError('invalid SFF counts or subheader size')
    sprites, visited, spans = [], set(), []
    for index in range(count):
        if offset in visited or offset < 512 or offset + size > len(data):
            raise ValueError(f'invalid or cyclic SFF offset at sprite {index}')
        visited.add(offset)
        nxt, length, x, y, group, image, previous, same = struct.unpack_from('<IIhhHHHB', data, offset)
        if length > 64 * 1024 * 1024 or offset + size + length > len(data):
            raise ValueError(f'invalid sprite length at {index}')
        end = offset + size + length
        place = bisect_left(spans, (offset,))
        if (place and spans[place - 1][1] > offset) or (place < len(spans) and spans[place][0] < end):
            raise ValueError(f'overlapping SFF sprite at {index}')
        insort(spans, (offset, end))
        if length == 0 and previous >= index:
            raise ValueError(f'invalid sprite link {index} -> {previous}')
        sprites.append(Sprite(group, image, x, y, previous if length == 0 else None,
                              bool(same), bytes(data[offset+size:offset+size+length]) if length else None))
        if index < count - 1 and nxt == 0:
            raise ValueError(f'premature end of sprite chain at {index}')
        if index == count - 1 and nxt not in (0, len(data)):
            raise ValueError('sprite chain exceeds declared count')
        offset = nxt
    return sprites
