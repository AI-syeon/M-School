"""Rebuild generated character originals into 32px-scale runtime sprites.

Read-only imports from the local pixel pipeline. No API calls at rebuild time.
"""
import sys
sys.dont_write_bytecode = True
import argparse
import hashlib
import importlib.util
import json
import re
import shutil
from pathlib import Path
from PIL import Image, ImageDraw, ImageOps, ImageFilter
ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parents[1]
sys.path.insert(0, str(ROOT.parent))
from unity_sprite_sheet import write_sprite_meta
SIZE = (32, 64)
IDS = ('player', 'npc-serin', 'npc-mira')
DIRS = ('south', 'north', 'east', 'west')

def save_json(path, data):
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')

def load_pipeline(path):
    """Import the local pipeline shared pixel quantizer and atlas helpers read-only."""
    sys.path.insert(0, str(path / 'src'))
    spec = importlib.util.spec_from_file_location('pipeline_pixelizer', path / 'scripts/build_godot_style_studies_static_32x64.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    from pixel_pipeline.cli import grid_atlas, check_image
    from pixel_pipeline.normal import normal_map, pack_normal_atlas
    return (module, grid_atlas, check_image, normal_map, pack_normal_atlas)

def normalize(im):
    """Remove the generated low-opacity alpha haze, retaining the source figure."""
    alpha = im.getchannel('A').point(lambda p: 255 if p >= 200 else 0)
    im.putalpha(alpha)
    bounds = alpha.getbbox()
    if not bounds:
        raise ValueError('Empty source')
    return im.crop(bounds)

def outline(im):
    """Add a one-pixel warm dark silhouette, without a contact or cast shadow."""
    a = im.getchannel('A')
    dil = a.filter(ImageFilter.MaxFilter(3))
    out = Image.new('RGBA', SIZE, (57, 39, 38, 255))
    out.putalpha(dil)
    out.alpha_composite(im)
    return out

def place(source):
    """Reduce each figure to a 48-pixel body, align its feet, and outline it."""
    ratio = min(26 / source.width, 48 / source.height)
    w, h = (round(source.width * ratio), round(source.height * ratio))
    im = source.resize((w, h), Image.Resampling.BOX)
    im.putalpha(im.getchannel('A').point(lambda p: 255 if p >= 128 else 0))
    canvas = Image.new('RGBA', SIZE)
    canvas.alpha_composite(im, ((32 - w) // 2, 62 - h))
    return outline(canvas)

def relight_mirror(im):
    """Mirror a view and gently restore upper-left material lighting."""
    im = ImageOps.mirror(im)
    p = im.load()
    for y in range(64):
        for x in range(32):
            r, g, b, a = p[x, y]
            if a:
                gain = 1 + (15.5 - x) * 0.018
                p[x, y] = (min(255, round(r * gain)), min(255, round(g * gain)), min(255, round(b * gain)), a)
    return im

def shift_part(src, box, dx, dy, dst):
    part = src.crop(box)
    dst.alpha_composite(part, (box[0] + dx, box[1] + dy))

def walk(base, direction, contact_pose=None):
    """Articulate source cutouts in contact/pass/contact/pass order.

Upper body bobs one pixel. Sleeve overlays preserve long hair rather than
cutting rectangular holes. The lower legs overlap the hem to prevent gaps.
East/west contacts reuse generated walking legs; other phases use idle legs.
The supporting boot always reaches row 62."""
    frames = []
    for phase in range(4):
        contact = phase % 2 == 0
        sign = 1 if phase < 2 else -1
        bob = 0 if contact else -1
        dst = Image.new('RGBA', SIZE)
        split = 16
        legtop = 52
        upper = base.crop((0, 0, 32, 54))
        body = upper.copy()
        dst.alpha_composite(body, (0, bob))
        for side, box in enumerate(((7, 40, 11, 49), (21, 40, 25, 49))):
            shift_part(base, box, (sign if side == 0 else -sign) if contact else 0, bob, dst)
        legs = contact_pose if contact and contact_pose is not None else base
        for side, box in enumerate(((0, legtop, split, 63), (split, legtop, 32, 63))):
            if direction in ('south', 'north'):
                dx = (sign if side == 0 else -sign) if contact else 0
            elif contact and contact_pose is not None:
                dx = (7 if side == 0 else -7) if sign == -1 else 0
            else:
                dx = (sign * 2 if side == 0 else -sign * 2) if contact else 0
            dy = 0 if (side == 0) == (sign == 1) else -1 if contact else -2
            if contact and contact_pose is not None:
                dy = 0
            shift_part(legs, box, dx, dy, dst)
        if contact:
            box = (7 if sign == 1 else 17, 49, 15 if sign == 1 else 25, 54)
            shift_part(base, box, sign, 0, dst)
        frames.append(dst)
    return frames

def backdrop(im, scale=1):
    bg = Image.new('RGBA', im.size, (98, 100, 104, 255))
    bg.alpha_composite(im)
    return bg.resize((bg.width * scale, bg.height * scale), Image.Resampling.NEAREST)

def action_frames(recipe, idle_source, idle, palette):
    """Keep idle identity; sample only generated limbs, then cluster readable props."""
    source = Image.open(ROOT / recipe['source']).convert('RGBA')
    crops = [normalize(source.crop(box)) for box in recipe['crops']]
    assert len(crops) == len(recipe['offsets']) == 4
    assert min(crop.height for crop in crops) >= 380
    idle_scale = min(26 / idle_source.width, 48 / idle_source.height)
    scale = idle_scale * idle_source.height / recipe['body_height']
    colors = sorted(palette)
    cache = {}
    result = []
    for phase, (crop, (dx, dy)) in enumerate(zip(crops, recipe['offsets'])):
        reduced = crop.resize((round(crop.width * scale), round(crop.height * scale)), Image.Resampling.NEAREST)
        reduced.putalpha(reduced.getchannel('A').point(lambda p: 255 if p >= 128 else 0))
        # Anchor the center of the boots, independent of raised hands or props.
        boot_box = reduced.getchannel('A').crop((0, max(0, reduced.height - 5), reduced.width, reduced.height)).getbbox()
        idle_boots = idle.getchannel('A').crop((0, 58, 32, 63)).getbbox()
        boot_center = (boot_box[0] + boot_box[2]) / 2
        idle_center = (idle_boots[0] + idle_boots[2]) / 2
        canvas = Image.new('RGBA', SIZE)
        canvas.alpha_composite(reduced, (round(idle_center - boot_center) + dx, 62 - reduced.height + dy))
        canvas = outline(canvas)
        mapped = []
        for r, g, b, a in canvas.get_flattened_data():
            rgb = (r, g, b)
            if rgb not in cache:
                cache[rgb] = min(colors, key=lambda c: sum((v - t) ** 2 for v, t in zip(rgb, c)))
            mapped.append((*cache[rgb], 255) if a else (0, 0, 0, 0))
        canvas.putdata(mapped)
        result.append(compose_action(idle, canvas, recipe, phase, colors))
    return result, crops, scale

def compose_action(idle, generated, recipe, phase, colors):
    """Masks never include the head or hem; all source sampling stays deterministic."""
    name = recipe['action']
    dst = idle.copy()
    dark = min(colors, key=lambda c: sum(((a - b) ** 2 for a, b in zip(c, (57, 39, 38)))))
    skin = max((p[:3] for p in idle.get_flattened_data() if p[3]), key=lambda c: sum(c))
    raised = [0, 1] if name in ('read', 'write') else [0]
    for side in raised:
        x0, y0, x1, y1 = recipe['ghost_arm_boxes'][side]
        for y in range(y0, y1):
            for x in range(x0, x1):
                if recipe['character'] == 'npc-mira' and (x < 9 or x > 22):
                    dst.putpixel((x, y), (0, 0, 0, 0))
                else:
                    sample = idle.getpixel((x, y - 5 if recipe['character'] == 'npc-serin' else y))
                    if sum(sample[:3]) > 400:
                        coat_target = (68, 32, 36) if recipe['character'] == 'npc-serin' else (58, 63, 49)
                        coat = min(colors, key=lambda c: sum(((a - b) ** 2 for a, b in zip(c, coat_target))))
                        sample = (*coat, 255)
                    dst.putpixel((x, y), sample)
    arm_mask = Image.new('L', SIZE)
    md = ImageDraw.Draw(arm_mask)
    for polygon in recipe['arm_masks']:
        md.polygon([tuple(p) for p in polygon], fill=255)
    h = recipe['head']
    bottom = h['bottom']
    bob = h['bob'][phase]
    md.rectangle((0, 0, 31, bottom + 1), fill=0)
    md.rectangle((0, 47, 31, 63), fill=0)
    arm_mask = Image.frombytes('L', SIZE, bytes((min(a, b) for a, b in zip(arm_mask.tobytes(), generated.getchannel('A').tobytes()))))
    if name != 'eat':
        dst.paste(generated, (0, 0), arm_mask)
    head = idle.crop((0, 0, 32, bottom + 1))
    dst.paste((0, 0, 0, 0), (0, 0, 32, bottom + 1 + bob))
    dst.alpha_composite(head, (0, bob))
    allowed = set()
    draw = ImageDraw.Draw(dst)
    if name in ('read', 'write'):
        for x in h['eye_x']:
            y = h['eye_y'] + bob
            for ey in (y - 1, y):
                dst.putpixel((x, ey), (*skin, 255))
                allowed.add((x, ey))
            draw.line((x - 1, y + 1, x, y + 1), fill=(*dark, 255))
            allowed.update([(x - 1, y + 1), (x, y + 1)])
    mx, my = h['mouth']
    my += bob
    if name in ('read', 'write'):
        x0, y0, x1, y1 = h['closed_mouth_box']
        for y in range(y0 + bob, y1 + bob):
            for x in range(x0, x1):
                dst.putpixel((x, y), (*skin, 255))
                allowed.add((x, y))
    if name == 'chat' and phase in (1, 3):
        for y in (my - 1, my, my + 1):
            for x in (mx - 1, mx):
                dst.putpixel((x, y), (*(skin if y == my - 1 else dark), 255))
                allowed.add((x, y))
    if name == 'eat' and phase == 2:
        for x in (mx - 1, mx):
            for y in (my, my + 1):
                dst.putpixel((x, y), (*dark, 255))
                allowed.add((x, y))
        dst.putpixel((mx + 3, my), (*skin, 255))
        allowed.add((mx + 3, my))
    for y in range(bottom + 1):
        for x in range(32):
            if (x, y + bob) not in allowed:
                assert dst.getpixel((x, y + bob)) == idle.getpixel((x, y)), ('head', name, phase, x, y)
    cream, paper_shadow, teal, teal_dark, crust, bread_light = [tuple(c) for c in recipe['extra_colors'][:6]]
    # Record exactly the painted prop pixels, excluding hands, cuffs and clothing.
    prop_mask = Image.new('L', SIZE)
    body_draw = draw
    class PropDraw:
        def __getattr__(self, method):
            def paint(*args, **kwargs):
                getattr(body_draw, method)(*args, **kwargs)
                mask_kwargs = dict(kwargs)
                mask_kwargs['fill'] = 255
                getattr(ImageDraw.Draw(prop_mask), method)(*args, **mask_kwargs)
            return paint
    draw = PropDraw()
    def polygon(points, color):
        draw.polygon(points, fill=(*color, 255))
    p = recipe['prop_recipe']
    if name == 'read':
        x, y, x1, y1 = p['book_box']
        lift = p['page_lift'][phase]
        polygon([(x, y), (x + 4, y + 1), (x + 6, y), (x1, y), (x1, y1 - 1), (x + 6, y1), (x, y1 - 1)], dark)
        polygon([(x + 1, y + 1), (x + 5, y + 2), (x + 5, y1 - 1), (x + 1, y1 - 2)], teal)
        polygon([(x + 7, y + 1), (x1 - 1, y + 1), (x1 - 1, y1 - 2), (x + 7, y1 - 1)], teal)
        if lift:
            polygon([(x + 6, y + 1 - lift), (x1, y - lift), (x1, y + 2), (x + 6, y + 3)], dark)
        polygon([(x + 1, y + 1), (x + 4, y + 2), (x + 5, y1 - 2), (x + 1, y1 - 3)], cream)
        polygon([(x + 7, y + 2 - lift), (x1 - 1, y + 1 - lift), (x1 - 1, y1 - 3), (x + 7, y1 - 2)], cream)
        draw.line((x + 6, y + 2, x + 6, y1 - 1), fill=(*teal_dark, 255))
        draw.point((x + 9, y1 - 3), fill=(*paper_shadow, 255))
        body_draw.line((x - 1, y + 3, x - 1, y + 5), fill=(*dark, 255))
        body_draw.point((x - 1, y + 4), fill=(*skin, 255))
        body_draw.line((x1 + 1, y + 3, x1 + 1, y + 5), fill=(*dark, 255))
        body_draw.point((x1 + 1, y + 4), fill=(*skin, 255))
    elif name == 'eat':
        x, y = p['bread_positions'][phase]
        # A bent sleeve and distinct cuff/skin cluster connect the roll to the arm.
        coat_target = (68, 32, 36) if recipe['character'] == 'npc-serin' else (58, 63, 49)
        coat = min(colors, key=lambda c: sum((a-b)**2 for a,b in zip(c,coat_target)))
        cuff = min(colors, key=lambda c: sum((a-b)**2 for a,b in zip(c,(173,145,95))))
        hx, hy = x + 2, y + 6
        body_draw.line((9, 37, hx, hy), fill=(*dark,255), width=4)
        body_draw.line((9, 37, hx, hy), fill=(*coat,255), width=2)
        body_draw.rectangle((hx-2,hy-1,hx-1,hy+1),fill=(*cuff,255))
        body_draw.rectangle((hx,hy,hx+1,hy+1),fill=(*skin,255))
        roll_colors = {k: tuple(v) for k,v in p['roll_colors'].items()}
        roll_colors['o'] = dark
        notch = {tuple(point) for point in p['bite_notches'][phase]}
        for ry, row in enumerate(p['roll_pixels']):
            for rx, symbol in enumerate(row):
                if symbol != '.' and (rx,ry) not in notch:
                    draw.point((x+rx,y+ry),fill=(*roll_colors[symbol],255))
        # New outline at each bitten edge; missing pixels expose the pre-prop body.
        if phase in (2,3):
            draw.point((x+4,y+1),fill=(*dark,255))
        if phase == 3:
            draw.point((x+5,y+2),fill=(*dark,255))
    elif name == 'write':
        x, y, x1, y1 = p['notebook_box']
        draw.rectangle((x, y, x1, y1), fill=(*dark, 255))
        draw.rectangle((x + 1, y + 1, x1 - 1, y1 - 1), fill=(*cream, 255))
        draw.line((x + 1, y1 - 1, x1 - 1, y1 - 1), fill=(*teal, 255))
        draw.line((x + 2, y + 3, x + 4, y + 3), fill=(*paper_shadow, 255))
        px = p['pen_tip_x'][phase]
        py = y + 2 + phase % 2
        draw.line((px, py, px + 3, py - 4), fill=(*dark, 255), width=1)
        draw.point((px + 2, py - 3), fill=(*crust, 255))
        body_draw.line((px + 4, py - 4, px + 5, py - 3), fill=(*skin, 255))
    dst.paste(idle.crop((0, 47, 32, 64)), (0, 47))
    assert dst.crop((0, 47, 32, 64)).tobytes() == idle.crop((0, 47, 32, 64)).tobytes()
    final_face_mask = set(allowed)
    final_face_mask.update((x,y) for y in range(64) for x in range(32) if prop_mask.getpixel((x,y)))
    for y in range(bottom + 1):
        for x in range(32):
            if (x, y + bob) not in final_face_mask:
                assert dst.getpixel((x, y + bob)) == idle.getpixel((x, y)), ('final head/hair', name, phase, x, y)
    permitted = arm_mask.copy()
    pd = ImageDraw.Draw(permitted)
    for side in raised:
        x0, y0, x1, y1 = recipe['ghost_arm_boxes'][side]
        pd.rectangle((x0, y0, x1 - 1, y1 - 1), fill=255)
    if name == 'read':
        pd.rectangle((9, 33, 23, 42), fill=255)
    elif name == 'eat':
        pd.rectangle((7, 28, 20, 45), fill=255)
    elif name == 'write':
        pd.rectangle((11, 34, 22, 44), fill=255)
    for y in range(bottom + 2, 47):
        for x in range(32):
            if not permitted.getpixel((x, y)):
                assert dst.getpixel((x, y)) == idle.getpixel((x, y)), ('torso', name, phase, x, y)
    non_prop = [(x,y) for y in range(64) for x in range(32)
                if dst.getpixel((x,y))[3] and not prop_mask.getpixel((x,y))]
    violations = [(x,y,dst.getpixel((x,y))[:3]) for x,y in non_prop
                  if dst.getpixel((x,y))[:3] not in colors]
    assert not violations, ('non-prop palette leak', recipe['character'], name, phase, violations)
    dst.info['prop_mask_pixels'] = sum(bool(v) for v in prop_mask.tobytes())
    dst.info['non_prop_palette_violations'] = len(violations)
    return dst

def action_reviews(actions, idle_frames, review):
    """Produce native-size, enlarged, source comparison and timed loop reviews."""
    sheet = Image.new('RGBA', (704, 10 * 288), (98, 100, 104, 255))
    native = Image.new('RGBA', (704, 10 * 160), (98, 100, 104, 255))
    comparison = Image.new('RGBA', (1024, 8 * 512), (98, 100, 104, 255))
    versus = Image.new('RGBA', (1152, 8 * 416), (98, 100, 104, 255))
    vd = ImageDraw.Draw(versus)
    sd, nd, cd = (ImageDraw.Draw(im) for im in (sheet, native, comparison))
    row = 0
    source_row = 0
    loops = {}
    for id, clips in actions.items():
        for name, frames in [('idle', [idle_frames[id]] * 4)] + [(name, data['frames']) for name, data in clips.items()]:
            sd.text((8, row * 288 + 4), f'{id} {name} / 4x', fill='white')
            nd.text((8, row * 160 + 4), f'{id} {name} / 1x and 2x', fill='white')
            for col, im in enumerate(frames):
                sheet.alpha_composite(backdrop(im, 4), (144 + col * 136, row * 288 + 24))
                native.alpha_composite(im, (180 + col * 36, row * 160 + 30))
                native.alpha_composite(backdrop(im, 2), (352 + col * 72, row * 160 + 24))
            row += 1
            if name == 'idle':
                continue
            data = clips[name]
            vd.text((8, source_row * 416 + 4), f'{id} {name} / idle then four action frames at 6x', fill='white')
            for col, im in enumerate([idle_frames[id]] + frames):
                versus.alpha_composite(backdrop(im, 6), (96 + col * 208, source_row * 416 + 24))
            cd.text((8, source_row * 512 + 4), f'{id} {name} / source above, pixel 3x below', fill='white')
            for col, (crop, im) in enumerate(zip(data['crops'], frames)):
                small = ImageOps.contain(crop, (224, 270), Image.Resampling.NEAREST)
                comparison.alpha_composite(small, (col * 256 + (256 - small.width) // 2, source_row * 512 + 24))
                comparison.alpha_composite(backdrop(im, 3), (col * 256 + 80, source_row * 512 + 308))
            source_row += 1
            duration = round(1000 / data['fps'])
            enlarged = [backdrop(im, 5) for im in frames]
            enlarged[0].save(review / f'{id}-{name}-south.gif', save_all=True, append_images=enlarged[1:], duration=duration, loop=0, disposal=2, optimize=False)
            frames[0].save(review / f'{id}-{name}-south.apng', save_all=True, append_images=frames[1:], duration=duration, loop=0, disposal=0, blend=0)
            for extension, expected_size in [('gif', (160, 320)), ('apng', SIZE)]:
                path = review / f'{id}-{name}-south.{extension}'
                with Image.open(path) as animation:
                    assert animation.n_frames == 4 and animation.size == expected_size
                    assert animation.info['loop'] == 0
                    durations = []
                    for n in range(4):
                        animation.seek(n)
                        durations.append(animation.info['duration'])
                        if extension == 'apng':
                            assert animation.convert('RGBA').tobytes() == frames[n].tobytes()
                    loops[path.name] = {'frames': 4, 'size': list(expected_size), 'duration_ms': durations, 'loop': True}
    sheet.save(review / 'npc-actions-sheet.png')
    native.save(review / 'npc-actions-1x.png')
    comparison.save(review / 'npc-actions-source-vs-pixel.png')
    versus.save(review / 'npc-actions-compare-idle.png')
    return loops

def compare_baseline(outroot):
    """Audit the job's original copies when the local baseline is available."""
    baseline = PROJECT / 'Art/_runs/20261111-1622-npc-actions/baseline'
    if not baseline.exists():
        return {'available': False}
    counts = {'sheet_regions': 0, 'clips': 0}
    for label, current in [('runtime', outroot), ('mirror', PROJECT / 'Assets/StreamingAssets/UiArt/Characters')]:
        for old in sorted((baseline / label).rglob('*')):
            if not old.is_file():
                continue
            relative = old.relative_to(baseline / label)
            if old.name not in ('sprite-sheet-alpha.png', 'manifest.json') or len(relative.parts) != 2:
                continue
            new = current / relative
            if old.suffix == '.png':
                before = Image.open(old).convert('RGBA')
                after = Image.open(new).convert('RGBA')
                assert after.crop((0, 0, 160, 256)).tobytes() == before.tobytes(), new
                counts['sheet_regions'] += 1
            if old.name == 'manifest.json':
                before = json.loads(old.read_text(encoding='utf-8'))
                after = json.loads(new.read_text(encoding='utf-8'))
                for facing in DIRS:
                    for clip in ('idle', 'walk'):
                        assert before['animations'][facing][clip] == after['animations'][facing][clip], new
                        counts['clips'] += 1
    return {'available': True, 'all_identical': True, **counts}

def build(pipeline):
    """Pixelize originals, package runtime frames, and build visual QA artifacts."""
    quant, atlas_fn, check, normal_map, pack_normal_atlas = load_pipeline(pipeline)
    outroot = PROJECT / 'Assets/MagicCampus/Art/Characters'
    review = ROOT / 'review'
    outroot.mkdir(parents=True, exist_ok=True)
    review.mkdir(exist_ok=True)
    all_frames = {}
    sources = {}
    reports = {}
    source_crops = {}
    recipes = json.loads((ROOT / 'actions.json').read_text(encoding='utf-8'))['characters']
    actions = {}
    idle_frames = {}
    for id in IDS:
        sourcepath = ROOT / 'source' / f'{id}-original.png'
        sources[str(sourcepath.relative_to(PROJECT)).replace('\\', '/')] = hashlib.sha256(sourcepath.read_bytes()).hexdigest()
        source = Image.open(sourcepath).convert('RGBA')
        crops = []
        for col in range(4):
            crops.append(normalize(source.crop((col * 384, 100, (col + 1) * 384, 920))))
        source_crops[id] = crops
        poses = [place(c) for c in crops]
        west_pose = relight_mirror(poses[2])
        if id == 'npc-serin':
            west_pose = poses[2].copy()
            poses[2] = relight_mirror(poses[2])
        bases = dict(zip(DIRS, poses[:3] + [west_pose]))
        raw = []
        for direction in DIRS:
            contact_pose = poses[3] if direction == 'east' else relight_mirror(poses[3]) if direction == 'west' else None
            raw.extend([bases[direction]] + walk(bases[direction], direction, contact_pose))
        frames = quant.quantize_shared(raw, 32)
        out = outroot / id
        out.mkdir(exist_ok=True)
        sheet, rects = atlas_fn(frames, 5)
        original_sheet = sheet.copy()
        animations = {}
        all_frames[id] = {}
        report = []
        normal_sources = []
        normal_rects = []
        for row, direction in enumerate(DIRS):
            animations[direction] = {}
            all_frames[id][direction] = frames[row * 5:row * 5 + 5]
            for clip, start, count, fps in [('idle', 0, 1, 0), ('walk', 1, 4, 8)]:
                rr = []
                for n in range(count):
                    idx = row * 5 + start + n
                    im = frames[idx]
                    check(im, SIZE, True)
                    name = f'{direction}_{clip}_{n:03}.png'
                    normal_sources.append((name, im))
                    normal_rects.append(rects[idx])
                    b = im.getbbox()
                    colors = len({p[:3] for p in im.get_flattened_data() if p[3]})
                    assert b[3] == 63 and 44 <= b[3] - b[1] <= 52, (id, name, b)
                    assert all((im.getpixel((x, y))[3] == 0 for x, y in [(x, 0) for x in range(32)] + [(x, 63) for x in range(32)] + [(0, y) for y in range(64)] + [(31, y) for y in range(64)]))
                    assert set(im.getchannel('A').get_flattened_data()) == {0, 255}
                    assert sheet.crop((rects[idx]['x'], rects[idx]['y'], rects[idx]['x'] + 32, rects[idx]['y'] + 64)).tobytes() == im.tobytes()
                    rr.append(rects[idx])
                    report.append({'file': name, 'bbox': list(b), 'opaque_colors': colors})
                animations[direction][clip] = {'frames': count, 'fps': fps, 'loop': True, 'rectangles': rr}
            walk_frames = frames[row * 5 + 1:row * 5 + 5]
            assert len({frame.tobytes() for frame in walk_frames}) == 4
            anim = [backdrop(f, 5) for f in walk_frames]
            anim[0].save(review / f'{id}-walk-{direction}.gif', save_all=True, append_images=anim[1:], duration=125, loop=0, disposal=2, optimize=False)
            native = frames[row * 5 + 1:row * 5 + 5]
            native[0].save(review / f'{id}-walk-{direction}.apng', save_all=True, append_images=native[1:], duration=125, loop=0, disposal=0, blend=0)
        logical = id.replace('npc-', 'npc.')
        idle_frames[id] = frames[0]
        if id in recipes:
            assert list(recipes[id]) == ['read', 'eat', 'write', 'chat']
            assert [r['fps'] for r in recipes[id].values()] == [4, 5, 6, 5]
            actions[id] = {}
            palette = {p[:3] for im in frames for p in im.get_flattened_data() if p[3]}
            extra = {tuple(c) for recipe in recipes[id].values() for c in recipe.get('extra_colors', [])} - palette
            assert len(extra) <= 8
            sheet = Image.new('RGBA', (160, 512))
            sheet.paste(original_sheet, (0, 0))
            for action_row, (name, recipe) in enumerate(recipes[id].items(), start=4):
                sourcepath = ROOT / recipe['source']
                sources[str(sourcepath.relative_to(PROJECT)).replace('\\', '/')] = hashlib.sha256(sourcepath.read_bytes()).hexdigest()
                action, crops, scale = action_frames(recipe, source_crops[id][0], frames[0], palette)
                assert len(action) == 4
                assert len({im.tobytes() for im in action}) == 4, (id, name, 'duplicate action poses')
                rr = []
                action_report = []
                for n, im in enumerate(action):
                    check(im, SIZE, True)
                    bbox = im.getbbox()
                    assert bbox[3] == 63 and 44 <= bbox[3] - bbox[1] <= 52, (id, name, n, bbox)
                    assert 0 < bbox[0] and bbox[2] < 32 and bbox[1] > 0
                    assert set(im.getchannel('A').get_flattened_data()) == {0, 255}
                    seam = recipe['idle_lower_from_y']
                    assert im.crop((0, seam, 32, 64)).tobytes() == frames[0].crop((0, seam, 32, 64)).tobytes()
                    filename = f'south_{name}_{n:03}.png'
                    x, y = n * 32, action_row * 64
                    sheet.paste(im, (x, y))
                    assert sheet.crop((x, y, x + 32, y + 64)).tobytes() == im.tobytes()
                    rect = {'x': x, 'y': y, 'w': 32, 'h': 64}
                    rr.append(rect)
                    normal_sources.append((filename, im))
                    normal_rects.append(rect)
                    action_report.append({'file': filename, 'bbox': list(bbox), 'opaque_colors': len({p[:3] for p in im.get_flattened_data() if p[3]}), 'prop_mask_pixels': im.info['prop_mask_pixels'], 'non_prop_palette_violations': im.info['non_prop_palette_violations']})
                animations['south'][name] = {'frames': 4, 'fps': recipe['fps'], 'loop': True, 'rectangles': rr}
                actions[id][name] = {'frames': action, 'crops': crops, 'fps': recipe['fps'], 'scale': scale}
                report.extend(action_report)
            assert sheet.crop((0, 0, 160, 256)).tobytes() == original_sheet.tobytes()
        sheet.save(out / 'sprite-sheet-alpha.png')
        normal = 'normals/atlas.png'
        (out / normal).parent.mkdir(exist_ok=True)
        normal_sheet = pack_normal_atlas(sheet.size, ((normal_map(frame), rect) for (_, frame), rect in zip(normal_sources, normal_rects, strict=True)))
        normal_sheet.save(out / normal)
        named = [(Path(name).stem, rect['x'], rect['y'], rect['w'], rect['h'])
                 for (name, _), rect in zip(normal_sources, normal_rects, strict=True)]
        write_sprite_meta(out / 'sprite-sheet-alpha.png', sheet.size, 32, named, (0.5, 0.03125), out / normal)
        save_json(out / 'manifest.json', {'id': logical, 'type': 'character', 'game_input': 'sprite-sheet-alpha.png', 'normal': normal, 'size': list(SIZE), 'sheetSize': list(sheet.size), 'origin': 'top-left', 'pivot': [16, 62], 'pixelsPerUnit': 32, 'animations': animations})
        mirror = PROJECT / 'Assets/StreamingAssets/UiArt/Characters' / id
        mirror.mkdir(parents=True, exist_ok=True)
        (mirror / 'normals').mkdir(exist_ok=True)
        for filename in (normal, 'sprite-sheet-alpha.png', 'manifest.json'):
            shutil.copyfile(out / filename, mirror / filename)
            assert (out / filename).read_bytes() == (mirror / filename).read_bytes()
        for folder in (out, mirror):
            for directory in (folder, folder / 'normals'):
                for file in directory.glob('*.png'):
                    if re.fullmatch(r'(north|south|east|west)_[a-z]+_\d{3}\.png', file.name):
                        file.unlink()
                        file.with_suffix('.png.meta').unlink(missing_ok=True)
        palette_count = len({p[:3] for im in frames for p in im.get_flattened_data() if p[3]})
        assert 24 <= palette_count <= 32
        reports[id] = {'logical_id': logical, 'palette_colors': palette_count, 'frames': report}
        if id in actions:
            action_palette = {p[:3] for clip in actions[id].values() for im in clip['frames'] for p in im.get_flattened_data() if p[3]}
            reports[id]['action_palette_colors'] = len(action_palette)
            reports[id]['additional_action_colors'] = sorted(action_palette - palette)
            reports[id]['action_frame_counts'] = {name: len(data['frames']) for name, data in actions[id].items()}
            reports[id]['action_source_scales'] = {name: data['scale'] for name, data in actions[id].items()}
            assert len(action_palette - palette) <= 8
    action_loop_checks = action_reviews(actions, idle_frames, review)
    baseline_check = compare_baseline(outroot)
    contact = Image.new('RGBA', (600, 12 * 204), (98, 100, 104, 255))
    d = ImageDraw.Draw(contact)
    native = Image.new('RGBA', (200, 12 * 68), (98, 100, 104, 255))
    nd = ImageDraw.Draw(native)
    row = 0
    for id in IDS:
        for direction in DIRS:
            d.text((4, row * 204 + 4), f'{id} {direction} | idle / contact / pass / contact / pass', fill='white')
            for col, im in enumerate(all_frames[id][direction]):
                contact.alpha_composite(im.resize((96, 192), Image.Resampling.NEAREST), (100 + col * 98, row * 204 + 12))
                native.alpha_composite(im, (35 + col * 32, row * 68 + 4))
            row += 1
    contact.save(review / 'characters-sheet.png')
    native.save(review / 'characters-sheet-1x.png')
    comparison = Image.new('RGBA', (960, 3 * 300), (98, 100, 104, 255))
    cd = ImageDraw.Draw(comparison)
    for i, id in enumerate(IDS):
        cd.text((8, i * 300 + 4), id + ' source south / pixel south / source walk / pixel walk', fill='white')
        for col, im in enumerate((source_crops[id][0], all_frames[id]['south'][0], source_crops[id][3], all_frames[id]['east'][1])):
            resized = ImageOps.contain(im, (210, 270), Image.Resampling.NEAREST if col % 2 else Image.Resampling.LANCZOS)
            comparison.alpha_composite(resized, (col * 240 + (240 - resized.width) // 2, i * 300 + 25))
    comparison.save(review / 'source-vs-pixel.png')
    oldnew = Image.new('RGBA', (480, 3 * 220), (98, 100, 104, 255))
    od = ImageDraw.Draw(oldnew)
    for i, id in enumerate(IDS):
        oldid = {'player': 'player', 'npc-serin': 'npc.serin', 'npc-mira': 'npc.mira'}[id]
        op = PROJECT / 'Art/Stage5-v1/characters' / oldid / 'pose_south.png'
        if not op.exists():
            op = PROJECT / 'Art/Stage5-v1/characters/player/south_idle_000.png'
        if op.exists():
            old = Image.open(op).convert('RGBA')
            oldnew.alpha_composite(old.resize((old.width * 3, old.height * 3), Image.Resampling.NEAREST), (70, i * 220 + 20))
        oldnew.alpha_composite(all_frames[id]['south'][0].resize((96, 192), Image.Resampling.NEAREST), (280, i * 220 + 20))
        od.text((8, i * 220 + 4), id + ' OLD 3x / NEW 3x', fill='white')
    oldnew.save(review / 'old-vs-new.png')
    courtyard = Image.open(PROJECT / 'Art/CampusGrid/review/courtyard-unity.png').convert('RGBA')
    for id, (x, y) in zip(IDS, ((164, 248), (264, 248), (344, 248))):
        courtyard.alpha_composite(all_frames[id]['south'][0], (x - 16, y - 62))
    campus = Image.new('RGBA', (1280, 384 + 768 + 44), (55, 55, 58, 255))
    campus.alpha_composite(courtyard, (0, 20))
    campus.alpha_composite(courtyard.resize((1280, 768), Image.Resampling.NEAREST), (0, 424))
    c = ImageDraw.Draw(campus)
    c.text((4, 4), '1x composite: generated characters near existing students; no Unity scene changes', fill='white')
    c.text((4, 407), '2x nearest-neighbor composite', fill='white')
    campus.save(review / 'in-courtyard.png')
    files = {
        str(p.relative_to(PROJECT)).replace('\\', '/'):
        hashlib.sha256(p.read_bytes()).hexdigest()
        for folder in (outroot, PROJECT / 'Assets/StreamingAssets/UiArt/Characters', review)
        for p in sorted(folder.rglob('*'))
        if p.is_file()
        and p.suffix in ('.png', '.json', '.gif', '.apng')
        and p.name != 'verification.json'
    }
    save_json(review / 'verification.json', {
        'source_sha256': sources,
        'recipe_sha256': hashlib.sha256((ROOT / 'actions.json').read_bytes()).hexdigest(),
        'output_sha256': files,
        'characters': reports,
        'action_review_loops': action_loop_checks,
        'baseline_comparison': baseline_check,
        'checks': {
            'dimensions': True, 'binary_alpha': True, 'transparent_edges': True,
            'lowest_opaque_row': 62, 'figure_height_range': [44, 52],
            'sheet_rectangles_match': True, 'existing_sheet_regions_unchanged': True,
            'action_frames': 32, 'action_frame_counts': True, 'action_poses_distinct': True,
            'idle_lower_pixels_preserved': True, 'action_extra_colors_at_most_8': True,
            'head_pixels_equal_bobbed_idle_except_recipe_face_edits': True,
            'final_head_hair_equal_bobbed_idle_outside_face_and_bread_masks': True,
            'source_arm_masks_exclude_head_and_hem': True,
            'idle_hem_and_lower_preserved_from_y47': True,
            'idle_torso_preserved_outside_limb_cleanup_prop_masks': True,
            'mirrored_png_and_manifests_identical': True,
            'non_prop_pixels_use_existing_palette_only': True,
            'extra_colors_confined_to_actual_painted_prop_masks': True,
        },
        'notes': [
            'NPC logical manifest IDs retain npc.serin / npc.mira; output folder IDs use hyphens.',
            'Existing frames retain their original independent 32-color quantization.',
            'Generated actions use nearest-neighbor sampling at fixed per-sheet scales for masked arms/hands only.',
            'Action heads and bodies derive from south idle; head-only bob and explicit facial edits preserve hair identity.',
            'Source limbs map only to the idle/walk palette; extra colors are confined to exact painted book/notebook/pen/roll masks.',
            'Bread is a recipe pixel sprite with muted tan crust, one highlight, visible skin hand and cuff; idle hem/lower body from y47 remain unchanged.',
            'West mirrors east with gentle material relight before shared palette quantization.',
            'Walk uses source-derived cutout articulation, with generated walking lower legs on east/west contacts.',
            'Courtyard review is a composited scale review, not a Unity runtime capture.',
        ],
    })
    return files
if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--pipeline', type=Path, default=Path('C:/Users/syeon/Workspace/2D-pixel-asset-pipeline'))
    parser.add_argument('--verify-rebuild', action='store_true')
    args = parser.parse_args()
    first = build(args.pipeline)
    if args.verify_rebuild:
        second = build(args.pipeline)
        assert first == second, 'Non-deterministic runtime rebuild'
        p = ROOT / 'review/verification.json'
        data = json.loads(p.read_text(encoding='utf-8'))
        data['checks']['identical_rebuild'] = True
        save_json(p, data)
    print('Built three color and normal sprite sheets with 92 frames, manifests, and review loops.')
