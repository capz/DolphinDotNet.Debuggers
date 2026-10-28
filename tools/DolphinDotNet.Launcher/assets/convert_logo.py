"""Convert the supplied PNG to GameCube XFB Y0/Cb/Y1/Cr bytes (requires Pillow).

The generated asset is checked in, so native builds need no PNG library.
Run this script from any directory after changing logo.png.
"""
from pathlib import Path

from PIL import Image


def convert(source):
    with Image.open(source) as original:
        if original.size != (300, 200):
            raise ValueError('launcher logo must be 300x200 pixels')
        image = original.convert('RGBA')
    background = Image.new('RGBA', image.size, (0, 0, 0, 255))
    pixels = Image.alpha_composite(background, image).convert('RGB').tobytes()
    output = bytearray()
    for offset in range(0, len(pixels), 6):
        pair = (pixels[offset:offset + 3], pixels[offset + 3:offset + 6])
        # BT.601 limited-range YCbCr, with chroma shared by two adjacent pixels.
        y = [(66*r + 129*g + 25*b + 128) // 256 + 16 for r, g, b in pair]
        r, g, b = (sum(p[channel] for p in pair) // 2 for channel in range(3))
        cb = (-38*r - 74*g + 112*b + 128) // 256 + 128
        cr = (112*r - 94*g - 18*b + 128) // 256 + 128
        output.extend((y[0], cb, y[1], cr))
    return output


if __name__ == '__main__':
    source = Path(__file__).with_name('logo.png')
    output = source.with_suffix('.yuyv')
    output.write_bytes(convert(source))
    print(f'{output.name}: 300x200 YUV422, {output.stat().st_size} bytes')
