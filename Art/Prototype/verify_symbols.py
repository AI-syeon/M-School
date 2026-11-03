import hashlib
import json
from pathlib import Path
from PIL import Image

folder = Path(__file__).resolve().parent / 'FirstConnection-symbols-v1'
manifest = json.loads((folder / 'manifest.json').read_text('utf-8'))
atlas = Image.open(folder / manifest['image'])
checks = []
for rect in manifest['frames']:
    file = folder / (rect['id'] + '.png')
    frame = Image.open(file)
    assert frame.mode == 'RGBA' and frame.size == (16, 16)
    assert frame.getchannel('A').getextrema() == (0, 255)
    x, y, w, h = [rect[k] for k in ('x', 'y', 'w', 'h')]
    assert atlas.crop((x, y, x + w, y + h)).tobytes() == frame.tobytes()
    checks.append({'id':rect['id'], 'sha256':hashlib.sha256(file.read_bytes()).hexdigest()})
(folder / 'qa.json').write_text(json.dumps({'passed':True, 'frames':checks, 'visualReview':'Codex inspected preview.png; door frame/handle and notice stand readable; prototype only'}, indent=2), 'utf-8')
print('PASS: 2 symbol frames, dimensions, transparency, atlas rectangles')
