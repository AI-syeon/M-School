"""Recreate draft PNGs using the local pixel pipeline environment and packaging APIs."""
from pathlib import Path
from io import BytesIO
import hashlib
import json
import sys

from PIL import Image, ImageDraw, ImageFont

PIPELINE = Path('C:/Users/syeon/Workspace/2D-pixel-asset-pipeline')
sys.dont_write_bytecode = True
sys.path.insert(0, str(PIPELINE / 'src'))
from pixel_pipeline.cli import check_image, grid_atlas, save_json

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parents[1]
PALETTE_LIMIT = 24
SIZE = (32, 96)


def prepared_source(number):
    source = Image.open(ROOT / f'original-{number:02}.png').convert('RGBA')
    alpha = source.getchannel('A').point(lambda value: 255 if value >= 128 else 0)
    source.putalpha(alpha)
    source = source.crop(alpha.getbbox())
    ratio = min(30 / source.width, 94 / source.height)
    target = (max(1, round(source.width * ratio)), max(1, round(source.height * ratio)))
    source = source.resize(target, Image.Resampling.NEAREST)
    frame = Image.new('RGBA', SIZE)
    frame.alpha_composite(source, ((32 - target[0]) // 2, 95 - target[1]))
    return frame


def png_bytes(image):
    buffer = BytesIO()
    image.save(buffer, format='PNG', optimize=False)
    return buffer.getvalue()


def build():
    prepared = [prepared_source(number) for number in range(1, 4)]
    visible = [pixel[:3] for frame in prepared for pixel in frame.get_flattened_data() if pixel[3]]
    training = Image.new('RGB', (len(visible), 1))
    training.putdata(visible)
    palette = training.quantize(colors=PALETTE_LIMIT, method=Image.Quantize.MEDIANCUT,
                                dither=Image.Dither.NONE)
    frames = []
    for frame in prepared:
        mapped = frame.convert('RGB').quantize(palette=palette, dither=Image.Dither.NONE).convert('RGBA')
        mapped.putalpha(frame.getchannel('A'))
        mapped = Image.composite(mapped, Image.new('RGBA', SIZE), frame.getchannel('A'))
        check_image(mapped, SIZE, True)
        frames.append(mapped)
    return frames, palette.getpalette()[:PALETTE_LIMIT * 3]


def review_images(frames):
    sheet = Image.new('RGB', (576, 574), '#808080')
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 17)
    small = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 13)
    draw.text((16, 8), 'Banner drafts / 4x nearest-neighbor', fill='white', font=font)
    tile = Image.open(PROJECT / 'Assets/MagicCampus/CampusGrid/Textures/ground-paving-00.png').convert('RGBA')
    assert tile.size == (32, 32)
    for index, frame in enumerate(frames):
        x = 32 + index * 184
        sheet.paste(frame.resize((128, 384), Image.Resampling.NEAREST), (x, 38),
                    frame.resize((128, 384), Image.Resampling.NEAREST))
        draw.text((x + 53, 426), f'{index + 1:02}', fill='white', font=font)
        patch = Image.new('RGBA', (96, 96))
        for row in range(3):
            for col in range(3):
                patch.alpha_composite(tile, (col * 32, row * 32))
        patch.alpha_composite(frame, (32, 0))
        sheet.paste(patch, (x + 16, 466))
    draw.text((16, 448), '1x / each on a 3x3 existing paving patch', fill='white', font=small)
    sheet.save(ROOT / 'sheet.png')

    comparison = Image.new('RGB', (768, 660), '#808080')
    draw = ImageDraw.Draw(comparison)
    for index, frame in enumerate(frames):
        original = Image.open(ROOT / f'original-{index + 1:02}.png').convert('RGBA')
        ratio = min(112 / original.width, 576 / original.height)
        original = original.resize((round(original.width * ratio), round(original.height * ratio)),
                                   Image.Resampling.NEAREST)
        x = index * 256
        comparison.paste(original, (x + (112 - original.width) // 2, 40), original)
        enlarged = frame.resize((128, 384), Image.Resampling.NEAREST)
        comparison.paste(enlarged, (x + 120, 40), enlarged)
        draw.text((x + 10, 8), f'{index + 1:02} source / pixel 4x', fill='white', font=font)
    comparison.save(ROOT / 'source-vs-pixel.png')


def main():
    frames, palette = build()
    repeat, repeated_palette = build()
    assert palette == repeated_palette
    checks = []
    for number, (frame, second) in enumerate(zip(frames, repeat), 1):
        data = png_bytes(frame)
        assert data == png_bytes(second), 'Repeated PNG bytes differ'
        (ROOT / f'pixel-{number:02}.png').write_bytes(data)
        opaque_colors = {pixel[:3] for pixel in frame.get_flattened_data() if pixel[3]}
        assert len(opaque_colors) <= PALETTE_LIMIT
        assert set(frame.getchannel('A').get_flattened_data()) == {0, 255}
        checks.append({'file': f'pixel-{number:02}.png', 'mode': frame.mode, 'size': list(frame.size),
                       'alpha_values': sorted(set(frame.getchannel('A').get_flattened_data())),
                       'opaque_rgb_colors': len(opaque_colors),
                       'rgba_colors_including_transparent': len(set(frame.get_flattened_data())),
                       'visible_bbox': list(frame.getbbox()), 'repeat_png_bytes_equal': True,
                       'sha256': hashlib.sha256(data).hexdigest()})
    atlas, rectangles = grid_atlas(frames, 3)
    for frame, rect in zip(frames, rectangles):
        assert atlas.crop((rect['x'], rect['y'], rect['x'] + 32, rect['y'] + 96)).tobytes() == frame.tobytes()
    review_images(frames)
    save_json(ROOT / 'checks.json', {'palette_limit': PALETTE_LIMIT, 'shared_palette_rgb':
              [palette[index:index + 3] for index in range(0, len(palette), 3)],
              'pipeline_atlas_roundtrip': True, 'draft_recipe_registered': False,
              'production_rebuild_run': False, 'variants': checks})
    print(json.dumps(checks, indent=2))


if __name__ == '__main__':
    main()

