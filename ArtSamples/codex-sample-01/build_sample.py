"""Convert preserved generated original using the local pixel pipeline helpers."""
import sys
sys.dont_write_bytecode = True
import importlib.util
import hashlib
import json
from pathlib import Path
from PIL import Image

OUT = Path(__file__).resolve().parent
PIPELINE = Path('C:/Users/syeon/Workspace/2D-pixel-asset-pipeline')
spec = importlib.util.spec_from_file_location('pipeline_static', PIPELINE / 'scripts/build_godot_style_studies_static_32x64.py')
pipeline = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pipeline)
source = OUT / 'original.png'
source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
im = Image.open(source).convert('RGBA')
alpha = im.getchannel('A').point(lambda a: 255 if a >= 128 else 0)
bounds = alpha.getbbox()
crop = im.crop(bounds)
scale = min(28 / crop.width, 29 / crop.height)
size = tuple(max(1, round(v * scale)) for v in crop.size)
small = crop.resize(size, Image.Resampling.NEAREST)
small.putalpha(small.getchannel('A').point(lambda a: 255 if a >= 128 else 0))
sprite = Image.new('RGBA', (32, 32), (0, 0, 0, 0))
sprite.alpha_composite(small, ((32 - size[0]) // 2, 30 - size[1]))
sprite = pipeline.quantize_shared([sprite], 15)[0]
# One QA cleanup pass at final resolution: remove the tiny contact shadow,
# keep the sampled page fold connected, and normalize the exterior outline.
for y in (28, 29):
    for x in range(32):
        if sprite.getpixel((x, y))[:3] == (119, 137, 99):
            sprite.putpixel((x, y), (0, 0, 0, 0))
for xy in [(16,6), (16,7), (15,8), (15,9), (15,10), (14,11)]:
    sprite.putpixel(xy, (209, 134, 62, 255))
mask = sprite.getchannel('A')
for y in range(32):
    for x in range(32):
        if mask.getpixel((x,y)) and any(nx < 0 or nx >= 32 or ny < 0 or ny >= 32 or not mask.getpixel((nx,ny)) for nx,ny in [(x-1,y),(x+1,y),(x,y-1),(x,y+1)]):
            sprite.putpixel((x,y), (62, 27, 19, 255))
sprite.save(OUT / 'prop-lectern-spellbook-00.png')
preview = Image.new('RGBA', (256, 256), (128, 128, 128, 255))
preview.alpha_composite(sprite.resize((256, 256), Image.Resampling.NEAREST))
preview.convert('RGB').save(OUT / 'preview-x8.png')
compare = Image.new('RGBA', (512, 256), (128, 128, 128, 255))
source_preview = im.resize((256, 256), Image.Resampling.NEAREST)
compare.alpha_composite(source_preview)
compare.alpha_composite(preview, (256, 0))
compare.convert('RGB').save(OUT / 'compare.png')
opaque = {p[:3] for p in sprite.get_flattened_data() if p[3]}
corners = [sprite.getpixel(xy)[3] for xy in [(0,0), (31,0), (0,31), (31,31)]]
assert sprite.size == (32,32) and sprite.mode == 'RGBA'
assert corners == [0,0,0,0]
assert len(opaque) <= 15
assert set(sprite.getchannel('A').get_flattened_data()) == {0,255}
assert hashlib.sha256(source.read_bytes()).hexdigest() == source_hash
report = {'original_size': im.size, 'source_sha256': source_hash, 'source_alpha_crop': bounds, 'sampled_size':size, 'final_size':sprite.size, 'mode':sprite.mode, 'opaque_colors':len(opaque), 'total_rgba_colors':len(set(sprite.get_flattened_data())), 'corner_alpha':corners, 'alpha_values': [0,255], 'pipeline_helper':str(PIPELINE / 'scripts/build_godot_style_studies_static_32x64.py'), 'pipeline_function':'quantize_shared', 'resampling':'NEAREST only', 'dithering':'NONE'}
(OUT / 'verification.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print(json.dumps(report))
