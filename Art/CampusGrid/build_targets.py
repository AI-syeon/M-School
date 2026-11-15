"""Prepare saved ImageGen target pairs; never invokes a generation API.

Whole prop frames are pixelized before build_grid splits them into cells.
The A frame is the common structural base; explicit state rectangles alone
are replaced from B. All crop/placement decisions are deterministic.
"""
from PIL import Image, ImageDraw
from build_grid import ROOT, read_recipe, pixelize, write_png

PAIRS = [
    ('signboard', ('dark','lit'), (1,2), (80,0,220,362), (30,60)),
    ('pillar', ('broken','mended'), (1,2), (105,0,180,362), (30,60)),
    ('seedpot', ('dry','sprout'), (1,1), (80,90,204,260), (28,28)),
    ('moss', ('plain','bloom'), (1,1), (30,105,300,240), (28,26)),
    ('plaque', ('dim','glow'), (1,1), (35,40,295,300), (28,28)),
    ('rune-stone', ('off','on'), (1,1), (35,5,295,340), (28,28)),
]

def prepare():
    source=Image.open(ROOT/'source/targets-original.png').convert('RGBA')
    outputs=[]
    for index,(name,states,size,crop,limit) in enumerate(PAIRS):
        frames=[]
        row=index//2; col=(index%2)*2
        for state in range(2):
            x,y,w,h=crop; x+=(col+state)*362; y+=row*362
            region=source.crop((x,y,x+w,y+h))
            ratio=min(limit[0]/w,limit[1]/h)
            dim=(round(w*ratio),round(h*ratio))
            small=pixelize(region,dim,40)
            frame=Image.new('RGBA',(size[0]*32,size[1]*32))
            frame.alpha_composite(small,((frame.width-small.width)//2,frame.height-small.height-1))
            frames.append(frame)
        # Keep source A as the exact shared structure. State changes are limited
        # to documented pixel-space windows/features. Coordinates are top-left.
        base,active=frames; changed=base.copy()
        def patch(box): changed.paste(active.crop(box),box)
        if name=='signboard':
            patch((22,30,31,43))
            for yy in range(32,45):
                for xx in range(3,21):
                    r,g,b,a=base.getpixel((xx,yy))
                    if a and r>g*1.12 and g>b*1.6 and r>130:
                        changed.putpixel((xx,yy),(min(255,r+35),min(240,g+34),min(160,b+23),255))
        elif name=='pillar':
            patch((8,24,25,55)); patch((7,55,31,63))
        elif name=='seedpot':
            patch((10,3,23,11))
            for yy in range(11,15):
                for xx in range(8,24):
                    if ((xx-15.5)/8)**2+((yy-12.5)/2)**2<=1:
                        changed.putpixel((xx,yy),active.getpixel((xx,yy)))
        elif name=='moss':
            # Extract the generated petals BEFORE palette reduction: BOX
            # filtering otherwise mixes the tiny pale petals into olive moss.
            # Bin source petals into final pixels, retaining their mean color.
            bins={}
            for box,kind in (((1190,545,1240,590),'white'),((1290,480,1365,555),'lavender')):
                for sy in range(box[1],box[3]):
                    for sx in range(box[0],box[2]):
                        r,g,b,a=source.getpixel((sx,sy))
                        petal=(r>200 and g>195 and b>165) if kind=='white' else (b-g>12 and r>110 and b>125)
                        if a>=128 and petal:
                            target=(2+int((sx-1116)*28/300),9+int((sy-467)*22/240))
                            bins.setdefault(target,[]).append((r,g,b))
            for xy,pixels in bins.items():
                if len(pixels)>=10:
                    rgb=tuple(round(sum(p[c] for p in pixels)/len(pixels)) for c in range(3))
                    changed.putpixel(xy,(*rgb,255))
        elif name in ('plaque','rune-stone'):
            for yy in range(2,30):
                for xx in range(2,30):
                    r,g,b,a=active.getpixel((xx,yy))
                    magic=(g-r>16 and b-r>16) if name=='plaque' else (b-r>15 and b-g>8)
                    if a and magic: changed.putpixel((xx,yy),(r,g,b,a))
        # Quantize each PAIR together: equal input pixels map to equal output
        # colors, while keeping all state colors within a 40-color palette.
        joined=Image.new('RGBA',(base.width*2,base.height))
        joined.paste(base,(0,0)); joined.paste(changed,(base.width,0))
        joined=pixelize(joined,joined.size,40)
        frames=[joined.crop((0,0,base.width,base.height)),joined.crop((base.width,0,base.width*2,base.height))]
        for state,frame in zip(states,frames):
            key=f'target-{name}-{state}'
            write_png(ROOT/f'prepared/targets/{key}.png',frame)
            outputs.append((key,frame))
    sheet=Image.new('RGBA',(520,6*280),(45,43,41,255)); d=ImageDraw.Draw(sheet)
    for i in range(6):
        for j in range(2):
            key,im=outputs[i*2+j]; x=10+j*256; y=i*280
            d.text((x,y+3),key,fill='white'); sheet.alpha_composite(im,(x,y+22))
            sheet.alpha_composite(im.resize((im.width*4,im.height*4),Image.Resampling.NEAREST),(x+70,y+22))
    write_png(ROOT/'review/targets-initial.png',sheet)

if __name__=='__main__': prepare()
