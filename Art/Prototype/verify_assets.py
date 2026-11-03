"""Check recipe outputs and create a nearest-neighbor review contact sheet."""
import hashlib
import json
import sys
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parent
compact = '--compact' in sys.argv
mira = '--mira' in sys.argv
recipe = json.loads((root / ('first-connection-mira.recipe.json' if mira else 'first-connection-compact.recipe.json' if compact else 'first-connection.recipe.json')).read_text('utf-8'))
build = root / ('FirstConnection-mira-v1' if mira else 'FirstConnection-compact-v1' if compact else 'FirstConnection-v1')
qa_path = root / ('asset-qa-mira.json' if mira else 'asset-qa-compact.json' if compact else 'asset-qa.json')
checks = []
previews = []
atlas_checks = []
def check_rect(atlas, rect, frame):
    x, y, w, h = [rect[k] for k in ('x', 'y', 'w', 'h')]
    assert x >= 0 and y >= 0 and x + w <= atlas.width and y + h <= atlas.height
    assert atlas.crop((x, y, x + w, y + h)).tobytes() == frame.tobytes()
for spec in recipe['tiles']:
    folder = build / 'tiles' / spec['id']
    frames = sorted(p for p in folder.glob('*.png') if p.name != 'atlas.png')
    assert len(frames) == spec['count'], (spec['id'], len(frames))
    manifest = json.loads((folder / 'manifest.json').read_text('utf-8'))
    atlas = Image.open(folder / manifest['image'])
    assert len(manifest['frames']) == len(frames)
    for p in frames:
        with Image.open(p) as im:
            assert im.mode == 'RGBA' and im.size == (spec['size'], spec['size'])
            check_rect(atlas, manifest['frames'][frames.index(p)], im)
            checks.append({'path': str(p.relative_to(root)), 'size': list(im.size), 'alpha': list(im.getchannel('A').getextrema()), 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()})
    previews.append((spec['id'], Image.open(frames[0]).copy()))
    atlas_checks.append(spec['id'])
for spec in recipe['characters']:
    folder = build / 'characters' / spec['id']
    manifest = json.loads((folder / 'manifest.json').read_text('utf-8'))
    atlas = Image.open(folder / manifest['game_input'])
    for state, count in spec['states'].items():
        frames = sorted(folder.glob(state + '_*.png'))
        assert len(frames) == count, (spec['id'], state, len(frames))
        assert manifest['animation']['rows'][state]['frames'] == count
        assert manifest['animation']['rows'][state]['fps'] == spec['fps'][state]
        assert len(manifest['frame_layout']['rows'][state]) == count
        for p in frames:
            with Image.open(p) as im:
                assert im.mode == 'RGBA' and im.size == tuple(spec['size'])
                assert im.getchannel('A').getextrema() == (0, 255)
                check_rect(atlas, manifest['frame_layout']['rows'][state][frames.index(p)], im)
                checks.append({'path': str(p.relative_to(root)), 'size': list(im.size), 'alpha': [0, 255], 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()})
    previews.append((spec['id'], Image.open(folder / 'idle_000.png').copy()))
    atlas_checks.append(spec['id'])
sheet = Image.new('RGBA', (840, 360), '#e7e0d5')
d = ImageDraw.Draw(sheet)
for i, (label, frame) in enumerate(previews):
    x = i * 120
    d.text((x + 4, 8), label.replace('_', '\n'), fill='#303449')
    scale = 3 if frame.height == 64 else 5
    sheet.alpha_composite(frame.resize((frame.width * scale, frame.height * scale), Image.Resampling.NEAREST), (x + 8, 65))
sheet.save(root / ('first-connection-mira-review.png' if mira else 'first-connection-compact-review.png' if compact else 'first-connection-review.png'))
prior = json.loads(qa_path.read_text('utf-8')) if qa_path.exists() else {}
qa_path.write_text(json.dumps({'quality':'prototype-only','generator':'2D-pixel-asset-pipeline 0.1.0','frames':len(checks),'checks':checks,'atlasRectanglesMatchIndividualFrames':atlas_checks,'visualReview':prior.get('visualReview','pending')}, ensure_ascii=False, indent=2), 'utf-8')
print(f'PASS: {len(checks)} RGBA frames, expected sizes, transparent characters, expected animation counts')
