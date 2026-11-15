import hashlib
import json
from pathlib import Path
from PIL import Image, ImageDraw

project = Path(__file__).resolve().parents[3]
run = Path(__file__).resolve().parent
runtime = project / 'Assets/MagicCampus/Art/Characters'
mirror = project / 'Assets/StreamingAssets/UiArt/Characters'
before = run / 'before-runtime'
recipes = json.loads((project / 'Art/Characters/actions.json').read_text(encoding='utf-8'))['characters']
result = {}
source_hashes = json.loads((run / 'source-before.json').read_text(encoding='utf-8'))
assert all(hashlib.sha256((project / p).read_bytes()).hexdigest() == h for p, h in source_hashes.items())
result['unchanged_source_files'] = len(source_hashes)
result['manifest_bytes_unchanged'] = []
result['prop_regions_unchanged'] = []
for ident in ('npc-serin', 'npc-mira', 'player'):
    assert (runtime / ident / 'manifest.json').read_bytes() == (before / ident / 'manifest.json').read_bytes()
    result['manifest_bytes_unchanged'].append(ident)
    for file in (runtime / ident).iterdir():
        if file.suffix == '.png' or file.name == 'manifest.json':
            assert file.read_bytes() == (mirror / ident / file.name).read_bytes()
    if ident == 'player':
        for file in (before / ident).iterdir():
            assert file.read_bytes() == (runtime / ident / file.name).read_bytes()
        continue
    for action in ('read', 'write'):
        for n in range(4):
            name = f'south_{action}_{n:03}.png'
            old = Image.open(before / ident / name).convert('RGBA')
            new = Image.open(runtime / ident / name).convert('RGBA')
            mask = Image.new('L', old.size)
            d = ImageDraw.Draw(mask)
            p = recipes[ident][action]['prop_recipe']
            if action == 'read':
                x,y,x1,y1 = p['book_box']
                d.polygon([(x,y),(x+4,y+1),(x+6,y),(x1,y),(x1,y1-1),(x+6,y1),(x,y1-1)],fill=255)
                lift = p['page_lift'][n]
                if lift:
                    d.polygon([(x+6,y+1-lift),(x1,y-lift),(x1,y+2),(x+6,y+3)],fill=255)
            else:
                x,y,x1,y1 = p['notebook_box']
                d.rectangle((x,y,x1,y1),fill=255)
                px = p['pen_tip_x'][n]
                py = y+2+n%2
                d.line((px,py,px+3,py-4),fill=255)
                d.point((px+2,py-3),fill=255)
            assert all(a == b for a,b,m in zip(old.get_flattened_data(),new.get_flattened_data(),mask.get_flattened_data()) if m), (ident,name,'prop pixels')
            result['prop_regions_unchanged'].append(f'{ident}/{name}')
    for n in range(4):
        name = f'south_chat_{n:03}.png'
        old = Image.open(before / ident / name).convert('RGBA')
        new = Image.open(runtime / ident / name).convert('RGBA')
        assert old.getchannel('A').crop((0, 33, 32, 64)).tobytes() == new.getchannel('A').crop((0, 33, 32, 64)).tobytes()
        base_palette = {p[:3] for f in (runtime / ident).glob('*.png')
                        if '_idle_' in f.name or '_walk_' in f.name
                        for p in Image.open(f).convert('RGBA').get_flattened_data() if p[3]}
        assert all(p[:3] in base_palette for p in new.get_flattened_data() if p[3])
        h = recipes[ident]['chat']['head']
        mx,my = h['mouth']
        my += h['bob'][n]
        if n in (1,3):
            patch = [new.getpixel((x,y)) for y in (my,my+1) for x in (mx-1,mx)]
            assert len(set(patch)) == 1 and sum(patch[0][:3]) < 200
            assert all(sum(new.getpixel((x,my-1))[:3]) > sum(patch[0][:3]) for x in (mx-1,mx))
verification = json.loads((project / 'Art/Characters/review/verification.json').read_text(encoding='utf-8'))
for p, h in verification['output_sha256'].items():
    assert hashlib.sha256((project / p).read_bytes()).hexdigest() == h, p
result['output_hashes_verified'] = len(verification['output_sha256'])
result['chat_body_alpha_unchanged'] = True
result['chat_all_pixels_base_palette_and_open_mouth_2x2'] = True
result['all_passed'] = True
(run / 'parent-audit.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
print(json.dumps(result, indent=2))
