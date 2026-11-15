"""Deterministic inventory conversion. Run with pipeline .venv/Scripts/python.exe.

Uses local pipeline check_image, color, and save_json helpers. Item-specific
BOX resampling, fixed palette, hard alpha, and contour cleanup live here.
"""
from __future__ import annotations
import hashlib
import json
import sys
from collections import deque
from pathlib import Path
sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parents[1]
PIPELINE = PROJECT.parent / '2D-pixel-asset-pipeline'
sys.path.insert(0, str(PIPELINE / 'src'))
from pixel_pipeline.cli import check_image, color, save_json
from pixel_pipeline.normal import save_normal_frames
from PIL import Image, ImageDraw, ImageOps
OUT = PROJECT / 'Assets/MagicCampus/Art/Items'
REVIEW = ROOT / 'review'
RUN = PROJECT / 'Art/_runs/20261111-0922-items'
PALETTE = [
    '#493d38', '#6b5444', '#91714c', '#bd914e',
    '#dfb568', '#f4d99b', '#9b6541', '#cd8c45',
    '#efe0c5', '#d3bea4', '#ad9682', '#fff0d8',
    '#303e4d', '#536579', '#8394ab', '#b5c6d2',
    '#28575c', '#377982', '#66a2ad', '#b6d6d6',
    '#244a49', '#315b41', '#57794e', '#91aa73',
    '#d8ece0', '#645375', '#9476a5', '#c1a0c7',
    '#e0c4df', '#77655f', '#aa7b56', '#e5bd80',
]
RGB = [color(v)[:3] for v in PALETTE]
OUTLINE = color(PALETTE[0])

def nearest(rgb):
    return min(RGB, key=lambda p: sum((p[k]-rgb[k])**2 for k in range(3)))

def remove_specks(mask):
    pixels, seen = mask.load(), set()
    for y in range(mask.height):
        for x in range(mask.width):
            if (x,y) in seen or not pixels[x,y]:
                continue
            q, component = deque([(x,y)]), []
            seen.add((x,y))
            while q:
                a,b = q.popleft(); component.append((a,b))
                for dx,dy in ((1,0),(-1,0),(0,1),(0,-1)):
                    xx,yy=a+dx,b+dy
                    if 0 <= xx < mask.width and 0 <= yy < mask.height and pixels[xx,yy] and (xx,yy) not in seen:
                        seen.add((xx,yy)); q.append((xx,yy))
            if len(component)<2:
                for a,b in component: pixels[a,b]=0

def pixelize(crop):
    bbox=crop.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
    if not bbox: raise ValueError('Empty source crop')
    tight=crop.crop(bbox)
    ratio=28/max(tight.size)
    size=tuple(max(1,round(n*ratio)) for n in tight.size)
    small=tight.resize(size,Image.Resampling.BOX)
    mask=small.getchannel('A').point(lambda a:255 if a>=128 else 0)
    remove_specks(mask)
    result=Image.new('RGBA',(32,32))
    ox,oy=(32-size[0])//2,(32-size[1])//2
    for y in range(size[1]):
        for x in range(size[0]):
            if mask.getpixel((x,y)):
                result.putpixel((x+ox,y+oy),nearest(small.getpixel((x,y))[:3])+(255,))
    before=result.copy()
    for y in range(1,31):
        for x in range(1,31):
            if before.getpixel((x,y))[3] and any(not before.getpixel((x+dx,y+dy))[3] for dx,dy in ((1,0),(-1,0),(0,1),(0,-1))):
                result.putpixel((x,y),OUTLINE)
    return result,bbox

def checker(size):
    canvas=Image.new('RGBA',size,'#e2d8c7'); d=ImageDraw.Draw(canvas)
    for y in range(0,size[1],4):
        for x in range(0,size[0],4):
            if (x//4+y//4)%2: d.rectangle((x,y,x+3,y+3),fill='#c8bead')
    return canvas

def touch_up(key,im):
    """One reviewed target-pixel cleanup pass, reproducible from the source."""
    if key=='item-moonbean-seed':
        # Keep the beans silver-blue; avoid lilac contamination shared with dust.
        silver=[RGB[i] for i in (0,12,13,14,15)]
        for y in range(32):
            for x in range(32):
                p=im.getpixel((x,y))
                if p[3]:
                    c=min(silver,key=lambda c:sum((c[k]-p[k])**2 for k in range(3)))
                    im.putpixel((x,y),c+(255,))
        # Tiny open-right crescents survive at actual inventory size.
        for x,y in ((13,8),(23,14),(10,19)):
            for dx,dy in ((1,0),(0,1),(0,2),(1,3),(2,3)):
                im.putpixel((x+dx,y+dy),color('#efe0c5'))
    return im

def tiled(tile,size):
    result=Image.new('RGBA',size)
    for y in range(0,size[1],tile.height):
        for x in range(0,size[0],tile.width): result.alpha_composite(tile,(x,y))
    return result

def reviews(items,crops):
    slot=Image.open(PROJECT/'Assets/MagicCampus/Art/UI/ui-slot-normal.png').convert('RGBA')
    ground=Image.open(PROJECT/'Assets/MagicCampus/CampusGrid/Textures/ground-paving-00.png').convert('RGBA')
    sheet=Image.new('RGBA',(920,52+174*len(items)),'#2d2927'); d=ImageDraw.Draw(sheet)
    d.text((12,8),'MAGICCAMPUS | INVENTORY ITEMS | actual pixels + nearest-neighbor zoom',fill='#efe0c5')
    for x,label in [(168,'1x'),(224,'4x'),(374,'slot 1x'),(446,'slot 3x'),(608,'paving 1x'),(704,'paving 3x')]: d.text((x,30),label,fill='#d3bea4')
    comparison=Image.new('RGBA',(640,40+180*len(items)),'#2d2927'); cd=ImageDraw.Draw(comparison)
    cd.text((16,12),'GENERATED SOURCE CROP',fill='#efe0c5'); cd.text((350,12),'32px RESULT / 4x NEAREST',fill='#efe0c5')
    for i,(key,im) in enumerate(items):
        top=52+i*174; d.text((12,top+8),key,fill='#efe0c5')
        base=checker((32,32)); base.alpha_composite(im)
        for x,scale in [(168,1),(224,4)]: sheet.alpha_composite(base.resize((32*scale,32*scale),Image.Resampling.NEAREST),(x,top+28))
        base=slot.copy(); base.alpha_composite(im,((slot.width-32)//2,(slot.height-32)//2))
        for x,scale in [(374,1),(446,3)]: sheet.alpha_composite(base.resize((base.width*scale,base.height*scale),Image.Resampling.NEAREST),(x,top+28))
        base=tiled(ground,(48,48)); base.alpha_composite(im,(8,8))
        for x,scale in [(608,1),(704,3)]: sheet.alpha_composite(base.resize((48*scale,48*scale),Image.Resampling.NEAREST),(x,top+22))
        ct=40+i*180; original=ImageOps.contain(crops[i],(148,148),Image.Resampling.LANCZOS)
        comparison.alpha_composite(original,(80+(148-original.width)//2,ct))
        base=checker((32,32)); base.alpha_composite(im)
        comparison.alpha_composite(base.resize((128,128),Image.Resampling.NEAREST),(382,ct+10))
        cd.text((16,ct+154),key,fill='#efe0c5')
    sheet.save(REVIEW/'items-preview.png'); comparison.save(REVIEW/'source-vs-pixel.png')
    one=Image.new('RGBA',(9*48,48),'#d3bea4')
    for i,(_,im) in enumerate(items): one.alpha_composite(im,(i*48+8,8))
    one.save(REVIEW/'items-1x.png')

def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()

def build():
    for folder in (OUT,REVIEW,RUN): folder.mkdir(parents=True,exist_ok=True)
    spec=json.loads((ROOT/'source/items-prompt.json').read_text(encoding='utf-8'))
    source=Image.open(ROOT/'source/items-original.png').convert('RGBA')
    items,crops,checks=[],[],[]; all_colors=set()
    for entry in spec['items']:
        x,y,w,h=entry['crop']
        assert 0<=x<x+w<=source.width and 0<=y<y+h<=source.height
        crop=source.crop((x,y,x+w,y+h)); im,bbox=pixelize(crop)
        im=touch_up(entry['id'],im)
        check_image(im,(32,32),True)
        alpha=im.getchannel('A'); assert set(alpha.get_flattened_data())=={0,255}
        assert not any(alpha.getpixel((x,y)) for x in range(32) for y in (0,31))
        assert not any(alpha.getpixel((x,y)) for y in range(32) for x in (0,31))
        occupied=alpha.getbbox(); assert 24<=max(occupied[2]-occupied[0],occupied[3]-occupied[1])<=28
        colors={p[:3] for p in im.get_flattened_data() if p[3]}; all_colors.update(colors)
        key=entry['id']; im.save(OUT/f'{key}.png'); items.append((key,im)); crops.append(crop)
        checks.append({'id':key,'size':[32,32],'mode':'RGBA','alphaValues':[0,255],'transparentEdges':True,'occupiedBounds':list(occupied),'sourceAlphaBoundsInCrop':list(bbox),'opaqueColors':len(colors)})
    assert len(all_colors)<=32
    save_normal_frames(OUT, ((f'{key}.png', im) for key, im in items))
    save_json(OUT/'manifest.json',{'schema':1,'pixelsPerUnit':32,'filter':'point','items':{key:{'file':f'{key}.png','normal':f'normals/{key}.png','size':[32,32]} for key,_ in items}})
    reviews(items,crops)
    outputs=sorted(OUT.rglob('*.png'))+[OUT/'manifest.json']+sorted(REVIEW.glob('*.png'))
    report={'schema':1,'sourceSha256':sha(ROOT/'source/items-original.png'),'sourceSize':list(source.size),'pipeline':'2D-pixel-asset-pipeline: check_image/color/save_json; local venv Pillow; custom BOX/fixed palette/outline processing','sharedOpaqueColors':len(all_colors),'palette':PALETTE,'items':checks,'outputSha256':{str(p.relative_to(PROJECT)).replace('\\','/'):sha(p) for p in outputs}}
    save_json(RUN/'validation.json',report)
    print(json.dumps({'items':len(items),'sharedOpaqueColors':len(all_colors),'sourceSha256':report['sourceSha256']},indent=2))

def verify_rebuild():
    report=json.loads((RUN/'validation.json').read_text(encoding='utf-8'))
    before={path:sha(PROJECT/path) for path in report['outputSha256']}
    before_validation=sha(RUN/'validation.json')
    build()
    after={path:sha(PROJECT/path) for path in before}
    assert before==after,'Output changed during identical rebuild'
    assert before_validation==sha(RUN/'validation.json'),'Validation record changed'
    save_json(RUN/'rebuild-check.json',{'identicalBytes':True,'checkedOutputCount':len(before),'validationRecordIdentical':True,'outputSha256':after})
    print('PASS: all PNGs, manifest, reviews, and validation report rebuilt byte-identically')

if __name__=='__main__':
    build()
    if '--verify' in sys.argv: verify_rebuild()
