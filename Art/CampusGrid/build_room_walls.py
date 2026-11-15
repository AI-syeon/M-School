"""Regenerate room architecture from permanent source art."""
from pathlib import Path

from PIL import Image
from build_grid import pixelize, write_png

ROOT = Path(__file__).resolve().parent
WALLS = [
    ("wall-rear", (12, 1), (47, 1, 1354, 333)),
    ("wall-side", (1, 7), (0, 335, 47, 645)),
    ("wall-front-left", (5, 1), (48, 983, 455, 103)),
    ("wall-front-right", (6, 1), (800, 983, 600, 103)),
]


def build():
    source = Image.open(ROOT / "source/room-dorm-base-original.png").convert("RGBA")
    decor = Image.open(ROOT / "source/room-dorm-fix-decor-original.png").convert("RGBA")
    for name, size, (x, y, w, h) in WALLS:
        if name == "wall-rear":
            band = pixelize(decor.crop((348, 154, 1316, 405)), (384, 64), 48)
        elif name == "wall-side":
            band = pixelize(decor.crop((1395, 47, 1485, 455)), (32, 224), 48)
        else:
            band = pixelize(source.crop((x, y, x + w, y + h)),
                            (size[0] * 32, size[1] * 32), 48)
        band.putpixel((0, band.height - 1), (0, 0, 0, 0))
        write_png(ROOT / "prepared/room-dorm" / (name + ".png"), band)


if __name__ == "__main__":
    build()
