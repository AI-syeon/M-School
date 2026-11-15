import base64, hashlib, io, json, sys
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
RUN = Path(__file__).resolve().parent
OUT = ROOT / 'Assets/MagicCampus/CampusGrid'
ART = ROOT / 'Art/CampusGrid'
def digest(data): return hashlib.sha256(data).hexdigest()
def cells():
    result = {p.name:digest(p.read_bytes()) for p in (OUT/'Textures').glob('*.png')}
    packed=json.loads((OUT/'scenic.campusatlas').read_text())
    atlas=Image.open(io.BytesIO(base64.b64decode(packed['png']))).convert('RGBA')
    for f in packed['frames']:
        im=atlas.crop((f['x'],f['y'],f['x']+32,f['y']+32))
        b=io.BytesIO(); im.save(b,'PNG'); result[f['name']]=digest(b.getvalue())
    return result
if sys.argv[1]=='baseline':
    baseline={'cells':cells(),'map':digest((ART/'map.json').read_bytes()),'art':json.loads((ART/'art.json').read_text()),'source':{p.name:digest(p.read_bytes()) for p in (ART/'source').glob('*') if not p.name.startswith('targets-')}}
    (RUN/'baseline.json').write_text(json.dumps(baseline,indent=2))
    print('Baseline cells',len(baseline['cells']))
elif sys.argv[1]=='first':
    for src,name in ((OUT/'layout.json','first-layout.json'),(ART/'review/build-report.json','first-build-report.json')):
        (RUN/name).write_bytes(src.read_bytes())
    (RUN/'first-cells.json').write_text(json.dumps(cells(),indent=2))
elif sys.argv[1]=='final':
    baseline=json.loads((RUN/'baseline.json').read_text()); now=cells()
    unchanged={k:v==now.get(k) for k,v in baseline['cells'].items()}
    art=json.loads((ART/'art.json').read_text()); expected=baseline['art']; expected['libraries'].append('targets.json')
    metrics={
        'existingCellPngs':len(unchanged),'existingCellPngsUnchanged':sum(unchanged.values()),
        'unchangedMap':baseline['map']==digest((ART/'map.json').read_bytes()),
        'artOnlyLibraryAddition':art==expected,
        'unchangedExistingSources':all(digest((ART/'source'/k).read_bytes())==v for k,v in baseline['source'].items()),
        'layoutByteIdentical':(RUN/'first-layout.json').read_bytes()==(OUT/'layout.json').read_bytes(),
        'buildReportByteIdentical':(RUN/'first-build-report.json').read_bytes()==(ART/'review/build-report.json').read_bytes(),
        'allCellPngsIdenticalBetweenBuilds':json.loads((RUN/'first-cells.json').read_text())==now,
        'targets':{}, 'pairs':[]}
    recipe=json.loads((ART/'targets.json').read_text())
    for p in recipe['props']:
        im=Image.open(ART/p['frames'][0]['source']).convert('RGBA'); colors=im.getcolors(65536)
        metrics['targets'][p['id']]={'size':list(im.size),'alpha':sorted(set(im.getchannel('A').getdata())),
            'opaqueColors':len({c[:3] for _,c in colors if c[3]}),'alphaBounds':im.getbbox(),
            'blocked':p['blocked'],'frames':len(p['frames']),'fpsAbsent':'fps' not in p}
    for i in range(0,12,2):
        a,b=recipe['props'][i:i+2]; ia=Image.open(ART/a['frames'][0]['source']); ib=Image.open(ART/b['frames'][0]['source'])
        diff=[(x,y) for y in range(ia.height) for x in range(ia.width) if ia.getpixel((x,y))!=ib.getpixel((x,y))]
        metrics['pairs'].append({'a':a['id'],'b':b['id'],'changedPixels':len(diff),'sameCanvas':ia.size==ib.size,
            'sameBottomAnchor':ia.getbbox()[3]==ib.getbbox()[3]})
    metrics['originalByteIdenticalToGenerated']=digest((ART/'source/targets-original.png').read_bytes())==digest(Path('C:/Users/syeon/.codex/generated_images/01a1c802-b447-78a1-9a25-ed85d256c5e1/exec-153324ad-e7a0-4ec9-8076-2fa7c2846ac4.png').read_bytes())
    metrics['targetCount']=len(recipe['props'])
    metrics['targetCellsAdded']=len(now)-len(unchanged)
    assert metrics['targetCount']==12 and metrics['targetCellsAdded']==16
    assert all(p['changedPixels']>0 and p['sameCanvas'] and p['sameBottomAnchor'] for p in metrics['pairs'])
    assert all(t['alpha']==[0,255] and t['blocked']==[[0,0]] and t['frames']==1 and t['fpsAbsent'] for t in metrics['targets'].values())
    (RUN/'validation.json').write_text(json.dumps(metrics,indent=2))
    assert all(unchanged.values()) and all(metrics[k] for k in ('unchangedMap','artOnlyLibraryAddition','unchangedExistingSources','layoutByteIdentical','buildReportByteIdentical','allCellPngsIdenticalBetweenBuilds'))
    print(json.dumps(metrics,indent=2))
