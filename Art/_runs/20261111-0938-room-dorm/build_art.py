"""Rebuild dorm assets using the local pipeline environment; preserves source PNGs."""
import hashlib
import importlib.util
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageOps
ROOT = Path(__file__).resolve().parents[3]
ART = ROOT / 'Art/CampusGrid'
RUN = Path(__file__).resolve().parent
OUT = ROOT / 'Assets/MagicCampus/CampusGrid/Rooms/room-dorm'
REV = ART / 'review/room-dorm'
base = 'source/room-dorm-base-original.png'
sheet = 'source/room-dorm-furniture-original.png'


def frame(source, crop, fit='contain', colors=64):
    return dict(source=source, crop=crop, fit=fit, colors=colors)
items = [('canopy-bed', [1, 3], [125, 15, 140, 380], [[0, 0], [0, 1]]),
 ('bookshelf', [2, 3], [416, 30, 225, 365], [[0, 0], [1, 0]]),
 ('desk',
  [3, 3],
  [738, 90, 300, 310],
  [[0, 0], [0, 1], [1, 0], [1, 1], [2, 0], [2, 1]]),
 ('window-seat', [4, 3], [1100, 30, 315, 370], [[0, 0], [1, 0], [2, 0], [3, 0]]),
 ('wardrobe', [2, 3], [100, 420, 190, 300], [[0, 0], [1, 0]]),
 ('star-rug', [3, 3], [390, 450, 295, 270], []),
 ('nightstand', [1, 1], [815, 530, 140, 180], [[0, 0]]),
 ('mirror', [1, 2], [1175, 430, 150, 290], [[0, 0]]),
 ('plant', [1, 2], [105, 755, 160, 280], [[0, 0]]),
 ('lantern', [1, 1], [475, 825, 115, 200], []),
 ('candle', [1, 1], [830, 855, 130, 170], []),
 ('flower-plant', [1, 2], [1150, 755, 200, 280], [[0, 0]])]
props = [dict(id=k, size=s, frames=[frame(sheet, c)], blocked=b) for k, s, c, b in items]
walls = [('wall-rear',
  [12, 1],
  [47, 1, 1354, 333],
  [[0, 0],
   [1, 0],
   [2, 0],
   [3, 0],
   [4, 0],
   [5, 0],
   [6, 0],
   [7, 0],
   [8, 0],
   [9, 0],
   [10, 0],
   [11, 0]]),
 ('wall-side',
  [1, 7],
  [0, 335, 47, 645],
  [[0, 0], [0, 1], [0, 2], [0, 3], [0, 4], [0, 5], [0, 6]]),
 ('wall-front-left',
  [5, 1],
  [48, 983, 455, 103],
  [[0, 0], [1, 0], [2, 0], [3, 0], [4, 0]]),
 ('wall-front-right',
  [6, 1],
  [800, 983, 600, 103],
  [[0, 0], [1, 0], [2, 0], [3, 0], [4, 0], [5, 0]])]
props += [dict(id=k, size=s, frames=[frame(base, c, 'stretch', 48)], blocked=b) for k, s, c, b in walls]
art = dict(
    schema=1,
    libraries=['interior.json'],
    tiles=[dict(id='room-floor', **frame(base, [50, 335, 1348, 644], 'stretch', 48))],
    props=[],
)
interior = dict(schema=1, props=props)
placements = [{'id': 'wall-rear', 'at': [0, 8], 'order': 10},
 {'id': 'wall-side', 'at': [0, 1], 'order': 10},
 {'id': 'wall-side', 'at': [11, 1], 'order': 10},
 {'id': 'wall-front-left', 'at': [0, 0], 'order': 1100},
 {'id': 'wall-front-right', 'at': [6, 0], 'order': 1100},
 {'id': 'canopy-bed', 'at': [1, 5]},
 {'id': 'bookshelf', 'at': [3, 5]},
 {'id': 'desk', 'at': [6, 3]},
 {'id': 'window-seat', 'at': [5, 6]},
 {'id': 'wardrobe', 'at': [9, 5]},
 {'id': 'star-rug', 'at': [3, 1], 'decorative': True, 'order': 5},
 {'id': 'nightstand', 'at': [1, 4]},
 {'id': 'mirror', 'at': [10, 2]},
 {'id': 'plant', 'at': [1, 1]},
 {'id': 'flower-plant', 'at': [10, 1]},
 {'id': 'lantern', 'at': [3, 7], 'decorative': True, 'order': 900},
 {'id': 'lantern', 'at': [9, 7], 'decorative': True, 'order': 900},
 {'id': 'candle', 'at': [1, 4], 'decorative': True, 'order': 1000}]
mapdata = dict(
    schema=1,
    size=[12, 9],
    camera=[0, 0, 12, 9],
    allowVisualOverlap=True,
    groundImage=dict(source=base, crop=[50, 335, 1348, 644], colors=64),
    props=placements,
    glows=[dict(x=3.5, y=7.5, radius=0.7, power=0.45), dict(x=9.5, y=7.5, radius=0.7, power=0.45), dict(x=1.5, y=4.6, radius=0.5, power=0.35), dict(x=7, y=7.5, radius=1.8, power=0.3)],
    shadows=[],
)
for name, data in [('room-dorm-art.json', art), ('interior.json', interior), ('room-dorm-map.json', mapdata)]:
    (ART / name).write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
spec = importlib.util.spec_from_file_location('campus_grid_build', ART / 'build_grid.py')
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)
# Architectural bands need visible and empty alpha pixels. One corner
# pixel is transparent and is composited over opaque floor.
prepared = RUN / 'prepared'
prepared.mkdir(exist_ok=True)
for k, s, c, b in walls:
    source = Image.open(ART / base).convert('RGBA').crop((c[0], c[1], c[0] + c[2], c[1] + c[3]))
    band = mod.pixelize(source, (s[0] * 32, s[1] * 32), 48)
    band.putpixel((0, band.height - 1), (0, 0, 0, 0))
    band.save(prepared / (k + '.png'))
    target = next((p for p in props if p['id'] == k))
    target['frames'] = [dict(source='../../Art/_runs/20261111-0938-room-dorm/prepared/' + k + '.png')]
(ART / 'interior.json').write_text(json.dumps(interior, indent=2) + '\n', encoding='utf-8')


def hashes(path):
    return {str(p.relative_to(path)): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(path.rglob('*')) if p.is_file()}


def build_reviews():
    room = Image.open(REV / 'courtyard.png').convert('RGBA')
    room.save(REV / 'room-dorm.png')
    room.resize((1152, 864), Image.Resampling.NEAREST).save(REV / 'room-dorm-3x.png')
    grid = room.resize((768, 576), Image.Resampling.NEAREST)
    d = ImageDraw.Draw(grid)
    blocked = set()
    lookup = {p['id']: p for p in props}
    for p in placements:
        if not p.get('decorative'):
            blocked.update(((p['at'][0] + x, p['at'][1] + y) for x, y in lookup[p['id']]['blocked']))
    overlay = Image.new('RGBA', grid.size)
    od = ImageDraw.Draw(overlay)
    for y in range(9):
        for x in range(12):
            xx, yy = (x * 64, (8 - y) * 64)
            tint = None
            if (x, y) in blocked:
                tint = (227, 89, 89, 74)
            if (x, y) == (5, 0):
                tint = (75, 241, 140, 105)
            if (x, y) == (2, 5):
                tint = (75, 181, 255, 105)
            if tint:
                od.rectangle((xx, yy, xx + 63, yy + 63), fill=tint)
    grid.alpha_composite(overlay)
    d = ImageDraw.Draw(grid)
    for y in range(9):
        for x in range(12):
            xx, yy = (x * 64, (8 - y) * 64)
            color = '#e35959' if (x, y) in blocked else '#FFFFFF'
            if (x, y) == (5, 0):
                color = '#4bf18c'
            if (x, y) == (2, 5):
                color = '#4bb5ff'
            d.rectangle((xx, yy, xx + 63, yy + 63), outline=color, width=2)
            d.text((xx + 3, yy + 3), f'{x},{y}', fill=color, stroke_width=1, stroke_fill='#30231b')
    grid.save(REV / 'room-dorm-grid.png')
    keyart = Image.open(ROOT / 'design/screens/06-room.png').convert('RGBA')
    keyart = ImageOps.contain(keyart, (1024, 576), Image.Resampling.LANCZOS)
    comp = Image.new('RGBA', (keyart.width + 768, 608), '#211b17')
    comp.alpha_composite(keyart, (0, 32))
    comp.alpha_composite(room.resize((768, 576), Image.Resampling.NEAREST), (keyart.width, 32))
    d = ImageDraw.Draw(comp)
    d.text((10, 10), 'KEYART 06-room', fill='white')
    d.text((keyart.width + 10, 10), 'ROOM DORM - 32px tiles at 2x', fill='white')
    comp.save(REV / 'room-dorm-vs-keyart.png')
(RUN / 'crop-manifest.json').write_text(json.dumps(dict(originalDimensions=[1448, 1086], furniture=[dict(id=k, crop=c, size=s) for k, s, c, b in items], wallBands=[dict(id=k, crop=c, size=s) for k, s, c, b in walls], floor=[50, 335, 1348, 644]), indent=2))


def build_all():
    mod.build(ART / 'room-dorm-art.json', ART / 'room-dorm-map.json', OUT, REV)
    build_reviews()
    return {'unity': hashes(OUT), 'review': hashes(REV)}
first = build_all()
second = build_all()
assert first == second
(RUN / 'reproducibility.json').write_text(json.dumps(dict(identical=True, first=first, second=second), indent=2) + '\n', encoding='utf-8')
print('All assets and every final review match across two complete builds.')
