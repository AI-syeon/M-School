"""Independent delivery checks, run with the pipeline virtualenv Python."""
import json
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
OUTPUT = ROOT / "Assets/MagicCampus/Art/Characters"
results = {}
for character in ("player", "npc-serin", "npc-mira"):
    folder = OUTPUT / character
    manifest = json.loads((folder / "manifest.json").read_text(encoding="utf-8"))
    assert manifest["size"] == [32, 64]
    assert manifest["pivot"] == [16, 62]
    assert manifest["pixelsPerUnit"] == 32
    assert manifest["origin"] == "top-left"
    assert manifest["type"] == "character"
    sheet = Image.open(folder / manifest["game_input"])
    assert list(sheet.size) == manifest["sheetSize"]
    assert sheet.mode == "RGBA"
    character_colors = set()
    heights = []
    frame_count = 0
    for direction in ("south", "north", "east", "west"):
        for clip, count in (("idle", 1), ("walk", 4)):
            animation = manifest["animations"][direction][clip]
            assert animation["frames"] == count
            assert animation["loop"] is True
            if clip == "walk":
                assert animation["fps"] == 8
            assert len(animation["rectangles"]) == count
            clip_bytes = []
            for index, rect in enumerate(animation["rectangles"]):
                frame = Image.open(folder / f"{direction}_{clip}_{index:03}.png")
                assert frame.mode == "RGBA" and frame.size == (32, 64)
                alpha = frame.getchannel("A")
                assert set(alpha.get_flattened_data()) == {0, 255}
                assert alpha.getbbox()[3] - 1 == 62
                assert alpha.crop((0, 0, 32, 1)).getbbox() is None
                assert alpha.crop((0, 63, 32, 64)).getbbox() is None
                assert alpha.crop((0, 0, 1, 64)).getbbox() is None
                assert alpha.crop((31, 0, 32, 64)).getbbox() is None
                heights.append(alpha.getbbox()[3] - alpha.getbbox()[1])
                character_colors.update(p[:3] for p in frame.get_flattened_data() if p[3])
                x, y, w, h = (rect[key] for key in ("x", "y", "w", "h"))
                assert (w, h) == frame.size
                assert 0 <= x <= sheet.width - w and 0 <= y <= sheet.height - h
                assert sheet.crop((x, y, x+w, y+h)).tobytes() == frame.tobytes()
                clip_bytes.append(frame.tobytes())
                frame_count += 1
            if clip == "walk":
                assert len(set(clip_bytes)) == 4, (character, direction, "duplicate walk")
    results[character] = {
        "frames": frame_count,
        "size": [32, 64],
        "sheetSize": list(sheet.size),
        "opaqueColorCount": len(character_colors),
        "figureHeightRange": [min(heights), max(heights)],
        "lowestOpaqueRow": 62,
        "binaryAlphaAndTransparentEdges": True,
        "allSheetRectanglesMatch": True,
        "fourDistinctWalkFramesPerDirection": True,
    }
results["external_metadata_count"] = len(list(OUTPUT.rglob("*.meta")))
review = ROOT / "Art/Characters/review"
loops = list(review.glob("*.apng"))
assert len(loops) == 12
for path in loops:
    animation = Image.open(path)
    assert animation.n_frames == 4 and animation.size == (32, 64)
    assert animation.info["loop"] == 0
    for index in range(4):
        animation.seek(index)
        assert animation.info["duration"] == 125
results["apngLoops"] = {"count": 12, "framesEach": 4, "frameDurationMs": 125}
build_report = json.loads((review / "verification.json").read_text(encoding="utf-8"))
assert build_report["checks"]["identical_rebuild"]
results["identicalRebuildDeliverableCount"] = len(build_report["output_sha256"])
print(json.dumps(results, indent=2))
(Path(__file__).parent / "independent-verification.json").write_text(
    json.dumps(results, indent=2) + "\n", encoding="utf-8"
)
