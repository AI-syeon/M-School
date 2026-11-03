from PIL import Image, ImageDraw
from pathlib import Path
root=Path(__file__).resolve().parents[3]
files=sorted((root/'screens').glob('*.png'))
for batch in range(2):
    sheet=Image.new('RGB',(1280,800),'#f5f1e9')
    d=ImageDraw.Draw(sheet)
    for j,p in enumerate(files[batch*8:batch*8+8]):
        im=Image.open(p).convert('RGB'); im.thumbnail((630,165))
        x=(j%2)*640; y=(j//2)*200
        d.text((x+12,y+5),p.stem,fill='black')
        sheet.paste(im,(x+10,y+27))
    sheet.save(Path(__file__).resolve().parent.parent/'source'/f'screens-{batch+1}.jpg')
