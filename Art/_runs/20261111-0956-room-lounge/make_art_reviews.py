"""Produce review-only labeled comparisons and the exact collision overlay."""
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageOps

ROOT = Path(__file__).resolve().parents[3]
REVIEW = ROOT / 'Art/CampusGrid/review/room-lounge'
image = Image.open(REVIEW / 'courtyard.png').convert('RGBA')
image.save(REVIEW / 'room-lounge.png')
image.resize((960, 768), Image.Resampling.NEAREST).save(REVIEW / 'room-lounge-3x.png')
layout = json.loads((ROOT / 'Assets/MagicCampus/CampusGrid/Rooms/room-lounge/layout.json').read_text())
props = {p['id']: p for p in layout['props']}
blocked = set()
for placement in layout['placements']:
    if not placement.get('decorative', False):
        blocked.update((placement['x'] + cell['x'], placement['y'] + cell['y']) for cell in props[placement['id']]['blocked'])
grid = image.resize((640, 512), Image.Resampling.NEAREST)
layer = Image.new('RGBA', grid.size)
d = ImageDraw.Draw(layer)
for x in range(10):
    for y in range(8):
        box = (x * 64, (7 - y) * 64, (x + 1) * 64 - 1, (8 - y) * 64 - 1)
        color = (255, 25, 25, 65) if (x, y) in blocked else None
        if (x, y) == (4, 0):
            color = (30, 255, 80, 115)
        elif (x, y) == (4, 3):
            color = (255, 220, 30, 115)
        if color:
            d.rectangle(box, fill=color)
        d.rectangle(box, outline=(255, 255, 255, 100))
        d.text((box[0] + 3, box[1] + 3), f'{x},{y}', fill=(255, 255, 255, 220))
grid = Image.alpha_composite(grid, layer)
grid.save(REVIEW / 'room-lounge-grid.png')
canvas = Image.new('RGB', (1280, 820), '#252a32')
d = ImageDraw.Draw(canvas)
for i, (name, label) in enumerate([('12-club.png', 'STYLE REFERENCE - CLUB / CELESTIAL FABRIC'), ('02-classroom.png', 'STYLE REFERENCE - CLASSROOM / HONEY OAK & BRASS')]):
    source = Image.open(ROOT / 'design/screens' / name).convert('RGB')
    thumb = ImageOps.contain(source, (624, 352), Image.Resampling.LANCZOS)
    y = 28 + i * 392
    canvas.paste(thumb, (8, y))
    d.text((8, y - 20), label, fill='white')
d.text((656, 8), 'LOUNGE / 32 PX GRID / 2X NEAREST', fill='white')
canvas.paste(image.resize((640, 512), Image.Resampling.NEAREST).convert('RGB'), (640, 28))
d.text((656, 560), 'Original generated base + transparent furniture preserved.', fill='white')
d.text((656, 582), '6 fire frames at 8 fps, stone, wood and logs stay fixed.', fill='white')
d.text((656, 604), 'Collision review - red blocked / green door / yellow tea table.', fill='white')
canvas.save(REVIEW / 'room-lounge-vs-keyart.png')
with Image.open(REVIEW / 'lounge-fireplace.apng') as fire:
    frames = []
    for i in range(fire.n_frames):
        fire.seek(i)
        frames.append(fire.convert('RGBA').copy())
frames[0].save(REVIEW / 'fireplace.apng', save_all=True, append_images=frames[1:], duration=125, loop=0, disposal=1)
print('Created 1x, 3x, exact 640x512 grid, keyart comparison, fireplace APNG.')
