"""Deterministic source-order shelf atlas packing."""
from .png import encode_png

class Atlas:
    def __init__(self, directory, size=2048):
        self.directory, self.size = directory, size
        self.page = 0
        self.pixels = bytearray(size * size * 4)
        self.x = self.y = self.row = 0
        self.used = False

    def add(self, image):
        w, h, size = image.width, image.height, self.size
        if w > size or h > size:
            raise ValueError(f'image {w}x{h} exceeds atlas size {size}')
        if self.x + w > size:
            self.x, self.y, self.row = 0, self.y + self.row, 0
        if self.y + h > size:
            self.flush()
            self.page += 1
            self.pixels = bytearray(size * size * 4)
            self.x = self.y = self.row = 0
        x, y = self.x, self.y
        for row in range(h):
            dest = ((y + row) * size + x) * 4
            self.pixels[dest:dest+w*4] = image.rgba[row*w*4:(row+1)*w*4]
        self.x += w
        self.row = max(self.row, h)
        self.used = True
        return {'page': self.page, 'x': x, 'y': y, 'w': w, 'h': h}

    def flush(self):
        if self.used:
            (self.directory / f'atlas-{self.page:03d}.png').write_bytes(
                encode_png(self.size, self.size, self.pixels))
            self.used = False
