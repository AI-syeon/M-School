"""Run the requested room builds and retain byte-comparison evidence."""
import hashlib
import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[3]
RUN = Path(__file__).resolve().parent
PYTHON = Path('C:/Users/syeon/Workspace/2D-pixel-asset-pipeline/.venv/Scripts/python.exe')

def hashes(folder):
    return {str(p.relative_to(folder)): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in sorted(folder.rglob('*')) if p.is_file()}

def build(room):
    subprocess.run([str(PYTHON), '-B', 'Art/CampusGrid/build_grid.py',
        '--art', f'Art/CampusGrid/{room}-art.json',
        '--map', f'Art/CampusGrid/{room}-map.json',
        '--unity-out', f'Assets/MagicCampus/CampusGrid/Rooms/{room}',
        '--review-out', f'Art/CampusGrid/review/{room}'], cwd=ROOT, check=True)

subprocess.run(['powershell', '-ExecutionPolicy', 'Bypass', '-File',
                'Art/CampusGrid/rebuild-rooms.ps1'], cwd=ROOT, check=True)
folders = [ROOT / 'Assets/MagicCampus/CampusGrid/Rooms/room-lounge',
           ROOT / 'Art/CampusGrid/review/room-lounge']
build('room-lounge')
first = [hashes(f) for f in folders]
build('room-lounge')
second = [hashes(f) for f in folders]
assert first == second, 'Lounge build output changed on second invocation'
build('room-dorm')
before = json.loads((RUN / 'dorm-before.json').read_text(encoding='utf-8-sig'))
changed = [entry['path'] for entry in before
           if hashlib.sha256((ROOT / entry['path']).read_bytes()).hexdigest().upper() != entry['hash']]
after_paths = {str(p.relative_to(ROOT)).replace('/', '\\')
               for p in (ROOT / 'Assets/MagicCampus/CampusGrid/Rooms/room-dorm').rglob('*') if p.is_file()}
assert not changed, changed
assert after_paths == {entry['path'] for entry in before}, 'Dorm file inventory changed'
result = {'loungeDoubleBuild': 'PASS', 'loungeOutputFiles': len(first[0]),
          'loungeReviewFiles': len(first[1]), 'dormByteIdentical': True,
          'dormFilesCompared': len(before), 'loungeOutputHashes': first[0],
          'loungeReviewHashes': first[1]}
(RUN / 'reproducibility.json').write_text(json.dumps(result, indent=2)+'\n',encoding='utf-8')
print(json.dumps({k:v for k,v in result.items() if 'Hashes' not in k},indent=2))
