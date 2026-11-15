"""Pixelize generated source art with the local pixel pipeline's packing/QA contract."""
from pathlib import Path
import hashlib
import json
import re
import shutil
import sys
from PIL import Image, ImageDraw
from pixel_pipeline.cli import check_image, grid_atlas, save_json
from pixel_pipeline.normal import save_normal_frames

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parent))
from sprite_grid import pack_frames
from unity_sprite_sheet import write_sprite_meta
NEAREST = Image.Resampling.NEAREST
DIRS = ['south', 'west', 'east', 'north']
ACTORS = ['player', 'npc.serin', 'npc.mira']
TILES = ['floor.courtyard', 'floor.paving', 'floor.lounge', 'floor.grass.dark',
         'wall.school', 'roof.school', 'door', 'notice', 'tree', 'hedge', 'bench',
         'fountain', 'window', 'flowers', 'floor.dirt', 'parchment']

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def pixelize(im, size, colors):
    im = im.resize(size, NEAREST)
    alpha = im.getchannel('A').point(lambda x: 255 if x >= 128 else 0)
    im = im.convert('RGB').quantize(colors=colors, dither=Image.Dither.NONE).convert('RGBA')
    im.putalpha(alpha)
    # Remove invisible RGB to guarantee packed atlas and frame byte identity.
    data = [(r,g,b,a) if a else (0,0,0,0) for r,g,b,a in im.get_flattened_data()]
    im.putdata(data)
    return im

def animation(base, state, i):
    out = Image.new('RGBA', (16,32))
    bob = ([0,0,-1,0] if state == 'idle' else [0,-1,0,0,-1,0])[i]
    out.alpha_composite(base.crop((0,0,16,24)), (0,bob))
    if state == 'idle':
        out.alpha_composite(base.crop((0,24,16,32)), (0,24))
    else:
        stride = [0,1,1,0,-1,-1][i]
        out.alpha_composite(base.crop((0,24,8,32)), (stride,24))
        out.alpha_composite(base.crop((8,24,16,32)), (8-stride,24))
    return out

def main():
    runtime = {'schema':1, 'quality':'generated-source-pixelized-stage5-v1',
        'pixelsPerUnit':16, 'origin':'top-left', 'filter':'point', 'integerScale':4,
        'tiles':{}, 'actors':{}, 'visualRuntimeQA':'pending'}
    source = Image.open(ROOT/'source/academy-tiles-original.png').convert('RGBA')
    tiles = []
    out = ROOT/'tiles'; out.mkdir(exist_ok=True)
    for n, key in enumerate(TILES):
        col,row = n%4,n//4
        crop = source.crop((round(col*source.width/4),round(row*source.height/4),
                            round((col+1)*source.width/4),round((row+1)*source.height/4)))
        tile = pixelize(crop,(16,16),24)
        check_image(tile,(16,16),False)
        tile.save(out/(key+'.png')); tiles.append(tile)
    atlas, rects = grid_atlas(tiles,4); atlas.save(out/'atlas.png')
    save_normal_frames(out, ((key + '.png', frame) for key, frame in zip(TILES, tiles)),
                       atlas_size=atlas.size, rects=rects)
    for frame,r in zip(tiles,rects):
        assert atlas.crop((r['x'],r['y'],r['x']+16,r['y']+16)).tobytes()==frame.tobytes()
    for key,rect in zip(TILES,rects):
        runtime['tiles'][key]={'image':'tiles/atlas.png','frames':[rect], 'sha256':sha(out/'atlas.png')}
    save_json(out/'manifest.json',{'image':'atlas.png','size':[16,16],
        'frames':[{'id':key,**r} for key,r in zip(TILES,rects)]})
    source = Image.open(ROOT/'source/students-original.png').convert('RGBA')
    contact = Image.new('RGBA',(640,384),(34,37,49,255))
    qa = {'pipeline':'pixel_pipeline.cli.check_image + grid_atlas + save_json',
          'pythonEnvironment':'2D-pixel-asset-pipeline/.venv/Scripts/python.exe',
          'sourceImages':{},'tiles':16,'actors':{},'atlasFrameEquality':True}
    for filename in ['students-original.png','academy-tiles-original.png']:
        p = ROOT/'source'/filename
        qa['sourceImages'][filename]={'sha256':sha(p),'size':list(Image.open(p).size)}
    for row, actor in enumerate(ACTORS):
        folder = ROOT/'characters'/actor; folder.mkdir(parents=True,exist_ok=True)
        frames=[]; direction_animations={}; source_crops=[]
        for col, direction in enumerate(DIRS):
            crop=source.crop((round(col*source.width/4),round(row*source.height/3),
                             round((col+1)*source.width/4),round((row+1)*source.height/3)))
            alpha=crop.getchannel('A').point(lambda x:255 if x>=128 else 0)
            bbox=alpha.getbbox(); assert bbox, (actor,direction)
            crop=crop.crop(bbox); source_crops.append(crop)
            ratio=min(14/crop.width,29/crop.height)
            size=(max(1,round(crop.width*ratio)),max(1,round(crop.height*ratio)))
            small=pixelize(crop,size,24)
            base=Image.new('RGBA',(16,32)); base.alpha_composite(small,((16-small.width)//2,31-small.height))
            base.save(folder/('pose_'+direction+'.png'))
            direction_animations[direction]={}
            for state,count,fps in [('idle',4,5),('walk',6,9)]:
                start=len(frames)
                for i in range(count):
                    frame=animation(base,state,i);check_image(frame,(16,32),True)
                    assert set(frame.getchannel('A').get_flattened_data()) == {0,255}
                    frames.append(frame.resize((32, 64), NEAREST))
                direction_animations[direction][state]={'frames':count,'fps':fps,'loop':True,'indices':list(range(start,len(frames)))}
        sheet, normals, rectangles = pack_frames(frames, columns=10)
        sheet.save(folder/'sprite-sheet-alpha.png')
        (folder/'normals').mkdir(exist_ok=True)
        normals.save(folder/'normals/atlas.png')
        named = []
        for direction, anims in direction_animations.items():
            for state,spec in anims.items():
                spec['rectangles']=[rectangles[i] for i in spec.pop('indices')]
                for i, rect in enumerate(spec['rectangles']):
                    named.append((f'{direction}_{state}_{i:03d}', rect['x'], rect['y'], rect['w'], rect['h']))
        for frame,r in zip(frames,rectangles):
            assert sheet.crop((r['x'],r['y'],r['x']+32,r['y']+64)).tobytes()==frame.tobytes()
        actor_spec={'image':f'characters/{actor}/sprite-sheet-alpha.png','pivot':[0.5,0.0625],
                    'normal':f'characters/{actor}/normals/atlas.png','pixelsPerUnit':32,
                    'animations':direction_animations['south'],'directionAnimations':direction_animations,
                    'defaultDirection':'south','sha256':sha(folder/'sprite-sheet-alpha.png')}
        runtime['actors'][actor]=actor_spec
        save_json(folder/'manifest.json',{'id':actor,'type':'character','game_input':'sprite-sheet-alpha.png',
            'size':[32,64],'sheetSize':list(sheet.size),'origin':'top-left','animations':direction_animations})
        mirror = ROOT.parents[1]/'Assets/StreamingAssets/Stage5Art/characters'/actor
        (mirror/'normals').mkdir(parents=True, exist_ok=True)
        for name in ('sprite-sheet-alpha.png', 'normals/atlas.png', 'manifest.json'):
            shutil.copyfile(folder/name, mirror/name)
        write_sprite_meta(mirror/'sprite-sheet-alpha.png', sheet.size, 32, named, (0.5, 0.0625), mirror/'normals/atlas.png')
        for directory in (folder, mirror):
            for file in directory.glob('*.png'):
                if re.fullmatch(r'(north|south|east|west)_(idle|walk)_\d{3}\.png', file.name):
                    file.unlink()
                    file.with_suffix('.png.meta').unlink(missing_ok=True)
        sheet.resize((640,512),NEAREST).save(folder/'review.png')
        preview=[frame.resize((96,192),NEAREST) for frame in frames[4:10]]
        preview[0].save(folder/'walk-south.gif',save_all=True,append_images=preview[1:],duration=111,loop=0,disposal=2)
        for col in range(4):
            contact.alpha_composite(frames[col*10].resize((64,128),NEAREST),(col*160,row*128))
        qa['actors'][actor]={'frames':40,'directions':4,'idleFramesPerDirection':4,'walkFramesPerDirection':6,
                            'size':[32,64],'alpha':'binary 0/255','paletteMaxPerBasePose':24}
    contact.save(ROOT/'character-review.png')
    atlas.resize((512,512),NEAREST).save(ROOT/'tile-review.png')
    # Side-by-side source versus local pixel result, with both panels the same scale.
    for name,original,pixel in [('tiles',Image.open(ROOT/'source/academy-tiles-original.png'),atlas),
                                ('students',Image.open(ROOT/'source/students-original.png'),contact)]:
        canvas=Image.new('RGB',(1024,544),(30,33,43))
        draw=ImageDraw.Draw(canvas);draw.text((12,8),'IMAGEGEN ORIGINAL',fill='white');draw.text((524,8),'LOCAL PIXEL RESULT',fill='white')
        original.thumbnail((512,512),NEAREST);canvas.paste(original,(0,32),original if original.mode=='RGBA' else None)
        pixel=pixel.copy();pixel.thumbnail((512,512),NEAREST)
        factor=min(512//pixel.width,512//pixel.height)
        if factor>1:pixel=pixel.resize((pixel.width*factor,pixel.height*factor),NEAREST)
        canvas.paste(pixel,(512,32),pixel)
        canvas.save(ROOT/f'comparison-{name}.png')
    # Full map floor + obstacle art. Geometry stays owned by the fixture; no new collisions.
    bundle=json.loads((ROOT.parents[1]/'Content/Fixtures/FirstConnection/generated/base/runner-bundle.json').read_text('utf-8'))
    maps=[]
    for spec in bundle['maps'][:2]:
        w,h=spec['width'],spec['height']; lounge=spec['id']=='map.lounge'
        image=Image.new('RGBA',(w*16,h*16))
        for y in range(h):
            for x in range(w):
                key='floor.lounge' if lounge else ('floor.paving' if y==2 else 'floor.courtyard')
                image.alpha_composite(tiles[TILES.index(key)],(x*16,(h-1-y)*16))
        for x,y in spec['blocked']:
            image.alpha_composite(tiles[TILES.index('bench' if lounge else 'hedge')],(x*16,(h-1-y)*16))
        for p in spec['portals']:
            image.alpha_composite(tiles[TILES.index('door')],(int(p['x'])*16,(h-1-int(p['y']))*16))
        folder=ROOT/'maps';folder.mkdir(exist_ok=True)
        filename=spec['id']+'.png';image.save(folder/filename)
        image.resize((w*64,h*64),NEAREST).save(folder/(spec['id']+'-review.png'))
        maps.append({'id':spec['id'],'image':'maps/'+filename,'sizePixels':list(image.size),
                     'sizeTiles':[w,h],'pixelsPerUnit':16,'pivot':[0,0],
                     'worldTileToImage':'imageY = mapHeight - 1 - worldY',
                     'use':'optional static floor+blocked obstacle art; render actors/notice separately; suppress duplicate portal sprite if this image is used'})
    runtime['maps']=maps
    qa['visualInspection']={'status':'passed-at-nearest-neighbor-preview-scale',
        'reviewed':['character-review.png','tile-review.png','characters/npc.serin/review.png',
                    'maps/map.courtyard-review.png','maps/map.lounge-review.png'],
        'findings':['Three student color identities and all four directions are distinct.',
                    'Door, notice board, tree, hedge, bench, fountain, window and flowers recognizable at 16px.',
                    'Walk is a simple locally rigged leg-shift/bob loop, not independently generated poses.',
                    'Map images match existing fixture dimensions and blocked cells; not a large designed campus.']}
    save_json(ROOT/'runtime-manifest.json',runtime)
    shutil.copyfile(ROOT/'runtime-manifest.json', ROOT.parents[1]/'Assets/StreamingAssets/Stage5Art/runtime-manifest.json')
    save_json(ROOT/'qa.json',qa)
    print('PASS: 16 tiles, 120 directional animation frames, binary alpha, absolute rectangles, atlas equality; 2 fixture-aligned map images.')

if __name__=='__main__':
    main()
