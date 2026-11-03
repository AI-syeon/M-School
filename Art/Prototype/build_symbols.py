"""Project-owned procedural symbol recipe; reuse pipeline packing and image checks."""
import json
from pathlib import Path
from PIL import Image
from pixel_pipeline.cli import grid_atlas, check_image, save_json

root = Path(__file__).resolve().parent
recipe = json.loads((root / 'symbols.recipe.json').read_text('utf-8'))
out = root / 'FirstConnection-symbols-v1'
out.mkdir(exist_ok=True)
frames = []
for spec in recipe['symbols']:
    rows = [row.replace(' ', '') for row in spec['rows']]
    assert len(rows) == 16 and all(len(row) == 16 for row in rows), spec['id']
    im = Image.new('RGBA', (16, 16))
    for y, row in enumerate(rows):
        for x, symbol in enumerate(row):
            value = recipe['palette'][symbol].lstrip('#')
            rgba = tuple(int(value[i:i+2], 16) for i in range(0, len(value), 2))
            im.putpixel((x, y), rgba if len(rgba) == 4 else (*rgba, 255))
    check_image(im, (16, 16), True)
    im.save(out / (spec['id'] + '.png'))
    frames.append(im)
atlas, rectangles = grid_atlas(frames, 2)
atlas.save(out / 'atlas.png')
save_json(out / 'manifest.json', {'quality':'prototype-only','image':'atlas.png','size':[16,16],'frames':[{'id':s['id'],**r} for s,r in zip(recipe['symbols'],rectangles)]})
atlas.resize((256, 128), Image.Resampling.NEAREST).save(out / 'preview.png')
print('PASS: door and notice RGBA 16x16; absolute atlas rectangles')
