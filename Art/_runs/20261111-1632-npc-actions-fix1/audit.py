"""Independent preservation and packaging checks for the action correction."""
import hashlib
import json
from pathlib import Path
from PIL import Image

PROJECT = Path(__file__).resolve().parents[3]
RUN = Path(__file__).resolve().parent


def digest(data):
    return hashlib.sha256(data).hexdigest()


def main():
    result = {'existing_frames': 0, 'sheet_regions': 0, 'player_files': 0,
              'source_files': 0, 'action_frames': 0, 'mirrored_files': 0}
    roots = {'runtime': PROJECT / 'Assets/MagicCampus/Art/Characters',
             'mirror': PROJECT / 'Assets/StreamingAssets/UiArt/Characters'}
    for label, current in roots.items():
        baseline = RUN / 'baseline' / label
        for old in baseline.rglob('*'):
            if not old.is_file():
                continue
            relative = old.relative_to(baseline)
            new = current / relative
            assert new.exists(), str(new)
            if relative.parts[0] == 'player':
                assert digest(old.read_bytes()) == digest(new.read_bytes()), str(new)
                result['player_files'] += 1
            if old.suffix == '.png':
                before = Image.open(old).convert('RGBA')
                after = Image.open(new).convert('RGBA')
                if old.name == 'sprite-sheet-alpha.png':
                    assert digest(before.tobytes()) == digest(after.crop((0, 0, 160, 256)).tobytes())
                    result['sheet_regions'] += 1
                else:
                    assert old.read_bytes() == new.read_bytes(), str(new)
                    assert before.tobytes() == after.tobytes(), str(new)
                    result['existing_frames'] += 1
            if old.suffix == '.meta':
                assert old.read_bytes() == new.read_bytes(), str(new)
    for item in json.loads((RUN / 'source-hashes-before.json').read_text(encoding='utf-8-sig')):
        assert digest(Path(item['Path']).read_bytes()).upper() == item['Hash']
        result['source_files'] += 1
    for character in ('player', 'npc-serin', 'npc-mira'):
        folder = roots['runtime'] / character
        for file in folder.iterdir():
            if file.suffix == '.png' or file.name == 'manifest.json':
                assert file.read_bytes() == (roots['mirror'] / character / file.name).read_bytes()
                result['mirrored_files'] += 1
        if character == 'player':
            continue
        manifest = json.loads((folder / 'manifest.json').read_text())
        assert manifest['sheetSize'] == [160, 512]
        assert list(manifest['animations']['south']) == ['idle', 'walk', 'read', 'eat', 'write', 'chat']
        idle = Image.open(folder / 'south_idle_000.png').convert('RGBA')
        base_palette = set()
        for file in folder.glob('*.png'):
            if file.stem.split('_')[1:2] in (['idle'], ['walk']):
                base_palette.update(p[:3] for p in Image.open(file).convert('RGBA').get_flattened_data() if p[3])
        palette = set()
        per_frame_colors = []
        for row, (action, fps) in enumerate((('read', 4), ('eat', 5), ('write', 6), ('chat', 5)), 4):
            entry = manifest['animations']['south'][action]
            assert entry['fps'] == fps and entry['frames'] == 4 and entry['loop']
            for frame in range(4):
                image = Image.open(folder / f'south_{action}_{frame:03}.png').convert('RGBA')
                assert image.size == (32, 64)
                assert set(image.getchannel('A').get_flattened_data()) == {0, 255}
                assert image.getbbox()[3] == 63
                assert image.crop((0, 52, 32, 64)).tobytes() == idle.crop((0, 52, 32, 64)).tobytes()
                assert all(image.getpixel((x, y))[3] == 0 for x, y in
                           [(x, y) for x in range(32) for y in (0, 63)] +
                           [(x, y) for y in range(64) for x in (0, 31)])
                assert entry['rectangles'][frame] == {'x': frame * 32, 'y': row * 64, 'w': 32, 'h': 64}
                sheet = Image.open(folder / 'sprite-sheet-alpha.png').convert('RGBA')
                assert image.tobytes() == sheet.crop((frame * 32, row * 64, frame * 32 + 32, row * 64 + 64)).tobytes()
                colors = {p[:3] for p in image.get_flattened_data() if p[3]}
                palette.update(colors)
                per_frame_colors.append(len(colors))
                result['action_frames'] += 1
        assert len(palette - base_palette) <= 8
        result[character] = {'base_palette': len(base_palette), 'action_palette': len(palette),
                             'additional_colors': len(palette - base_palette),
                             'frame_color_range': [min(per_frame_colors), max(per_frame_colors)]}
    result['passed'] = True
    (RUN / 'independent-audit.json').write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
