"""Crop generated dorm enrichment without changing the preserved source artwork."""

from pathlib import Path

from PIL import Image

from build_grid import pixelize, write_png

ROOT = Path(__file__).resolve().parent
SOURCE = ROOT / "source/room-dorm-fix-decor-original.png"
OUT = ROOT / "prepared/room-dorm"
# Absolute rectangles in the unchanged 1536x1024 generator output: x,y,w,h.
ASSETS = {
    "canopy-bed-rich": ([44, 3, 226, 523], [64, 96], "right"),
    "moon-banner": ([46, 538, 139, 268], [32, 64], "center"),
    "trailing-ivy": ([266, 538, 169, 282], [32, 64], "center"),
    "star-garland": ([481, 588, 341, 164], [64, 32], "center"),
    "curtain-pair": ([849, 534, 359, 267], [96, 64], "center"),
    "green-runner": ([1268, 538, 233, 452], [32, 128], "stretch"),
    "oak-chest": ([47, 842, 233, 165], [64, 32], "center"),
    "star-stool": ([359, 837, 137, 171], [32, 32], "center"),
    "stacked-books": ([555, 846, 159, 149], [32, 32], "center"),
    "brass-lamp": ([803, 814, 112, 192], [32, 32], "center"),
    "sleeping-cat": ([985, 861, 205, 139], [32, 32], "center"),
}


def build():
    original = Image.open(SOURCE).convert("RGBA")
    OUT.mkdir(parents=True, exist_ok=True)
    for name, (rect, size, align) in ASSETS.items():
        x, y, w, h = rect
        crop = original.crop((x, y, x + w, y + h))
        # Alpha bounding-box avoids invisible RGB in the transparent source gutters.
        visible = crop.getchannel("A").point(lambda a: 255 if a >= 128 else 0).getbbox()
        crop = crop.crop(visible)
        scale = min(size[0] / crop.width, size[1] / crop.height)
        dimensions = tuple(size) if align == "stretch" else (max(1, round(crop.width * scale)), max(1, round(crop.height * scale)))
        sprite = pixelize(crop, dimensions, 48)
        canvas = Image.new("RGBA", tuple(size))
        offset = (size[0] - dimensions[0] if align == "right" else (size[0] - dimensions[0]) // 2, size[1] - dimensions[1])
        canvas.alpha_composite(sprite, offset)
        write_png(OUT / (name + ".png"), canvas)
    for name, rect, size in [
        ("wall-panel-rich", [348, 154, 968, 251], [384, 64]),
        ("timber-post-rich", [1395, 47, 90, 408], [32, 224]),
    ]:
        x, y, w, h = rect
        write_png(OUT / (name + ".png"), pixelize(original.crop((x, y, x + w, y + h)), tuple(size), 48))
    # Four plank courses per native tile, cropped away from all perimeter walls.
    # This is a crop of the existing generated base, not newly painted artwork.
    base = Image.open(ROOT / "source/room-dorm-base-original.png").convert("RGBA")
    floor_rect = [620, 458, 320, 160]
    x, y, w, h = floor_rect
    write_png(OUT / "oak-floor-native.png", pixelize(base.crop((x, y, x + w, y + h)), (32, 32), 48))
    print("Prepared 14 dorm enrichment assets.")


if __name__ == "__main__":
    build()
