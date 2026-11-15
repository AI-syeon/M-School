"""Rebuild lounge crops from unchanged generated originals using the local pixel pipeline.

Run with 2D-pixel-asset-pipeline/.venv/Scripts/python.exe. Crops are absolute
rectangles in the preserved source PNGs. No new source painting is performed.
"""
import hashlib
import json
import math
from pathlib import Path

from PIL import Image
from build_grid import comparison, pixelize, write_png
from pixel_pipeline.cli import check_image

ROOT = Path(__file__).resolve().parent
OUT = ROOT / 'prepared/lounge'
REVIEW = ROOT / 'review/room-lounge'
SOURCE = 'source/room-lounge-furniture-original.png'
ASSETS = {
    'lounge-sofa': ([45, 40, 465, 300], [96, 64]),
    'lounge-tea-table': ([600, 45, 270, 285], [32, 32]),
    'lounge-fireplace': ([1010, 25, 350, 320], [64, 64]),
    'lounge-noticeboard': ([115, 365, 325, 345], [64, 64]),
    'lounge-armchair': ([600, 365, 270, 350], [32, 64]),
    'lounge-rug': ([1010, 375, 350, 340], [96, 96]),
    'lounge-star-banner': ([175, 730, 210, 345], [32, 64]),
}
WALLS = {
    'lounge-wall-rear': ([46, 0, 1310, 330], [320, 64]),
    'lounge-wall-side': ([0, 330, 46, 690], [32, 192]),
    'lounge-wall-front-left': ([46, 1020, 490, 100], [128, 32]),
    'lounge-wall-front-right': ([750, 1020, 646, 100], [160, 32]),
    'lounge-floor': ([500, 450, 400, 160], [32, 32]),
}


def region(image, rect):
    x, y, w, h = rect
    return image.crop((x, y, x + w, y + h))


def build():
    OUT.mkdir(parents=True, exist_ok=True)
    REVIEW.mkdir(parents=True, exist_ok=True)
    sheet = Image.open(ROOT / SOURCE).convert('RGBA')
    base = Image.open(ROOT / 'source/room-lounge-base-original.png').convert('RGBA')
    comparisons, prepared = [], {}
    for key, (rect, size) in ASSETS.items():
        crop = region(sheet, rect)
        bounds = crop.getchannel('A').point(lambda a: 255 if a >= 128 else 0).getbbox()
        assert bounds, key
        crop = crop.crop(bounds)
        scale = min(size[0] / crop.width, size[1] / crop.height)
        dimensions = (max(1, round(crop.width * scale)), max(1, round(crop.height * scale)))
        sprite = pixelize(crop, dimensions, 48)
        canvas = Image.new('RGBA', tuple(size))
        canvas.alpha_composite(sprite, ((size[0] - dimensions[0]) // 2, size[1] - dimensions[1]))
        check_image(canvas, tuple(size), True)
        write_png(OUT / (key + '.png'), canvas)
        prepared[key] = canvas
        comparisons.append((key, crop, canvas))
    for key, (rect, size) in WALLS.items():
        crop = region(base, rect)
        sprite = pixelize(crop, tuple(size), 48)
        # Architecture remains visually opaque; a transparent exterior corner
        # is necessary for the existing prop asset alpha contract.
        if key != 'lounge-floor':
            sprite.putpixel((0, sprite.height - 1), (0, 0, 0, 0))
        write_png(OUT / (key + '.png'), sprite)
        comparisons.append((key, crop, sprite))
    # Animation uses only generated firebox pixels, keeping surround and logs fixed.
    # Every transformed pixel stays inside this explicitly reviewed fire rectangle.
    fire = prepared['lounge-fireplace']
    frames = []
    box = (22, 27, 43, 49)
    for phase in range(6):
        frame = fire.copy()
        for y in range(box[1], box[3]):
            shift = round(math.sin(phase * math.tau / 6 + y * 0.55))
            for x in range(box[0], box[2]):
                sx = max(box[0], min(box[2] - 1, x + shift))
                frame.putpixel((x, y), fire.getpixel((sx, y)))
        frames.append(frame)
        write_png(OUT / f'lounge-fireplace-{phase:02}.png', frame)
    assert len({frame.tobytes() for frame in frames}) == 6
    write_png(REVIEW / 'lounge-source-vs-pixel.png', comparison(comparisons))
    large = [f.resize((256, 256), Image.Resampling.NEAREST) for f in frames]
    large[0].save(REVIEW / 'lounge-fireplace.apng', save_all=True, append_images=large[1:], duration=125, loop=0, disposal=1)
    report = {'tileSize': 32, 'tool': 'image_gen.imagegen (built-in)', 'source': SOURCE,
              'assets': ASSETS, 'architecture': WALLS, 'fire': {'frames': 6, 'fps': 8, 'changedRectangle': box},
              'sourceSha256': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in
                  [ROOT / SOURCE, ROOT / 'source/room-lounge-base-original.png']}}
    (REVIEW / 'lounge-art-preparation.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print('Prepared lounge: 7 generated furniture sprites, 4 walls, 32px floor, 6 fire frames at 8fps.')


if __name__ == '__main__':
    build()
