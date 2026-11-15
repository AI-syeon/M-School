import sys
from pathlib import Path
from PIL import Image, ImageDraw
ROOT=Path(__file__).resolve().parents[3]/'Art/CampusGrid'
sys.path.insert(0,str(ROOT))
from build_grid import source_region, read_recipe, write_png, comparison

art=read_recipe(ROOT/'art.json'); props=read_recipe(ROOT/'targets.json')['props']
ground,_=source_region(ROOT,art['tiles'][0],(32,32),{})
bench,_=source_region(ROOT,next(p for p in art['props'] if p['id']=='bench')['frames'][0],(64,32),{})
heights=[288,288,230,230,230,230]
sheet=Image.new('RGBA',(900,sum(heights)+38),'#2c2926'); draw=ImageDraw.Draw(sheet)
draw.text((10,10),'TARGET STATE PAIRS: A | B   1x / 4x nearest       PAVING + EXISTING BENCH: 2x',fill='#f3dfbd')
top=38; comparisons=[]
source=Image.open(ROOT/'source/targets-original.png')
crops=read_recipe(ROOT/'source/targets-prompt.json')['cropRectangles']
for pair,height in enumerate(heights):
    context=Image.new('RGBA',(192,96))
    for y in range(3):
        for x in range(6): context.paste(ground,(x*32,y*32))
    context.alpha_composite(bench,(120,51))
    for state in range(2):
        index=pair*2+state; p=props[index]; im=Image.open(ROOT/p['frames'][0]['source']).convert('RGBA')
        draw.text((10,top+state*14),p['id'],fill='#f3dfbd')
        sheet.alpha_composite(im,(10+state*48,top+36))
        sheet.alpha_composite(im.resize((im.width*4,im.height*4),Image.Resampling.NEAREST),(124+state*144,top+30))
        context.alpha_composite(im,(24+state*48,83-im.height))
        x,y,w,h=crops[index]; comparisons.append((p['id'],source.crop((x,y,x+w,y+h)),im))
    sheet.alpha_composite(context.resize((384,192),Image.Resampling.NEAREST),(448,top+30))
    top+=height
write_png(ROOT/'review/targets-sheet.png',sheet)
write_png(ROOT/'review/targets-source-vs-result.png',comparison(comparisons))
