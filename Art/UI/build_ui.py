"""Build the UI kit from its preserved generated concept palette.

Run with 2D-pixel-asset-pipeline/.venv/Scripts/python.exe Art/UI/build_ui.py.
All rectangles use top-left coordinates. Runtime slice borders are named.
"""

from hashlib import sha256
import json
from pathlib import Path

from PIL import Image, ImageDraw
from pixel_pipeline.normal import save_normal_frames


ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / "Art/UI"
OUT = ROOT / "Assets/MagicCampus/Art/UI"
RUN = ROOT / "Art/_runs/20261111-0916-ui-kit"
SOURCE = ART / "source/ui-kit-original.png"
TRANSPARENT = (0, 0, 0, 0)
with Image.open(SOURCE) as source:
    PALETTE = [tuple(source.convert("RGB").getpixel((120 + 110 * i, 822))) for i in range(14)]


def color(index, alpha=255):
    return (*PALETTE[index], alpha)


def borders(left, top, right, bottom):
    return dict(left=left, top=top, right=right, bottom=bottom)


def stepped(draw, size, inset, cut, fill):
    width, height = size
    left, top = inset, inset
    right, bottom = width - 1 - inset, height - 1 - inset
    draw.polygon([(left + cut, top), (right - cut, top),
                  (right - cut, top + cut), (right, top + cut),
                  (right, bottom - cut), (right - cut, bottom - cut),
                  (right - cut, bottom), (left + cut, bottom),
                  (left + cut, bottom - cut), (left, bottom - cut),
                  (left, top + cut), (left + cut, top + cut)], fill=fill)


def frame(size, state="normal", panel=False):
    image = Image.new("RGBA", size, TRANSPARENT)
    draw = ImageDraw.Draw(image)
    # Nested solid polygons produce crisp one-pixel framing and a flat body.
    indices = {"normal": [0, 5, 10, 8],
               "selected": [0, 9, 12, 11],
               "disabled": [0, 3, 4, 7]}[state]
    for inset, index in enumerate(indices):
        stepped(draw, size, inset, max(0, 3 - inset), color(index))
    stepped(draw, size, 4, 0, color(2 if state == "disabled" else 1, 228))
    width, height = size
    # Upper/lower highlights keep the antique gold two-tone at native scale.
    draw.line((4, 2, width - 5, 2), fill=color(11 if state == "selected" else 10 if state == "normal" else 4))
    draw.line((4, height - 3, width - 5, height - 3), fill=color(9 if state == "selected" else 6 if state == "normal" else 3))
    if state == "selected":
        draw.point((3, 3), fill=color(13))
        draw.point((width - 4, height - 4), fill=color(13))
    if panel:
        for flip_x, flip_y in [(False, False), (True, False), (False, True), (True, True)]:
            def point(x, y):
                return (width - 1 - x if flip_x else x, height - 1 - y if flip_y else y)
            draw.line([point(5, 13), point(10, 13), point(10, 10), point(13, 10), point(13, 5)], fill=color(8))
            for x, y in [(7, 6), (6, 7), (8, 7), (7, 8)]:
                draw.point(point(x, y), fill=color(10))
            draw.point(point(7, 7), fill=color(11))
    return image


def nine_slice(image, size, sliced):
    left, top, right, bottom = [sliced[key] for key in ("left", "top", "right", "bottom")]
    width, height = image.size
    target_width, target_height = size
    assert target_width >= left + right and target_height >= top + bottom
    xs, ys = [0, left, width - right, width], [0, top, height - bottom, height]
    tx, ty = [0, left, target_width - right, target_width], [0, top, target_height - bottom, target_height]
    result = Image.new("RGBA", size)
    for row in range(3):
        for column in range(3):
            patch = image.crop((xs[column], ys[row], xs[column + 1], ys[row + 1]))
            patch = patch.resize((tx[column + 1] - tx[column], ty[row + 1] - ty[row]), Image.Resampling.NEAREST)
            result.paste(patch, (tx[column], ty[row]))
    return result


def build():
    OUT.mkdir(parents=True, exist_ok=True)
    specs = [
        ("ui-panel", (48, 48), ["normal"], borders(16, 16, 16, 16), borders(16, 16, 16, 16)),
        ("ui-button", (48, 24), ["normal", "selected", "disabled"], borders(8, 8, 8, 8), borders(8, 6, 8, 6)),
        ("ui-slot", (40, 40), ["normal", "selected"], None, borders(4, 4, 4, 4)),
        ("ui-quickslot", (40, 40), ["normal", "selected"], None, borders(4, 4, 4, 4)),
        ("ui-namebox", (48, 20), ["normal"], borders(8, 6, 8, 6), borders(8, 4, 8, 4)),
    ]
    elements, images = {}, {}
    for element_id, size, states, sliced, padding in specs:
        element = dict(size=list(size), states={}, contentPadding=padding)
        if sliced:
            element["slice"] = sliced
        if element_id == "ui-slot":
            element["iconRect"] = [4, 4, 32, 32]
        if element_id == "ui-quickslot":
            element["digitRect"] = [4, 4, 9, 9]
        for state in states:
            image = frame(size, state, panel=element_id == "ui-panel")
            if element_id == "ui-quickslot":
                draw = ImageDraw.Draw(image)
                # Empty digit reservation is delineated below and to the right.
                draw.line((4, 13, 13, 13, 13, 4), fill=color(11 if state == "selected" else 8))
            filename = f"{element_id}.png" if len(states) == 1 else f"{element_id}-{state}.png"
            image.save(OUT / filename)
            element["states"][state] = filename
            images[filename] = image
        elements[element_id] = element
    manifest = dict(schema=1, pixelsPerUnit=32, filter="point", elements=elements)
    manifest["normal"] = save_normal_frames(OUT, images.items())
    (OUT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return manifest, images


def review(manifest, images):
    folder = ART / "review"
    folder.mkdir(parents=True, exist_ok=True)
    with Image.open(ROOT / "Art/CampusGrid/review/courtyard-unity.png") as background:
        preview = background.convert("RGBA").crop((80, 40, 560, 340))
    by_id = manifest["elements"]

    def paste(element_id, state, position, size=None):
        element = by_id[element_id]
        image = images[element["states"][state]]
        if size:
            image = nine_slice(image, size, element["slice"])
        preview.alpha_composite(image, position)

    paste("ui-panel", "normal", (16, 22), (300, 120))
    paste("ui-namebox", "normal", (30, 10), (80, 20))
    draw = ImageDraw.Draw(preview)
    # White text rectangles stand in for separately rendered runtime typography.
    for rectangle in [(45, 18, 88, 20), (36, 43, 204, 46), (36, 54, 258, 57), (36, 65, 227, 68)]:
        draw.rectangle(rectangle, fill="white")
    for index, state in enumerate(["normal", "selected", "disabled"]):
        x = 20 + index * 98
        paste("ui-button", state, (x, 100), (96, 24))
        ImageDraw.Draw(preview).rectangle((x + 32, 110, x + 63, 112), fill="white")
    for index in range(6):
        paste("ui-slot", "selected" if index == 1 else "normal", (332 + index % 3 * 44, 22 + index // 3 * 44))
    for index in range(9):
        paste("ui-quickslot", "selected" if index == 4 else "normal", (44 + index * 44, 200))
    preview.resize((960, 600), Image.Resampling.NEAREST).save(folder / "ui-kit-preview.png")
    board = Image.new("RGBA", (780, 720), (112, 112, 112, 255))
    for index, image in enumerate(images.values()):
        x, y = index % 3 * 260, index // 3 * 240
        board.alpha_composite(image, (x + 12, y + 12))
        board.alpha_composite(image.resize((image.width * 4, image.height * 4), Image.Resampling.NEAREST), (x + 60, y + 40))
    board.save(folder / "ui-kit-1x.png")
    with Image.open(SOURCE) as original:
        original = original.convert("RGBA")
        original.thumbnail((780, 720), Image.Resampling.NEAREST)
    comparison = Image.new("RGBA", (1560, 720), (60, 60, 60, 255))
    comparison.alpha_composite(original, (0, (720 - original.height) // 2))
    comparison.alpha_composite(board, (780, 0))
    comparison.save(folder / "source-vs-final.png")


def verify(manifest, images):
    checks = []
    all_colors = set()
    for element_id, element in manifest["elements"].items():
        for state, filename in element["states"].items():
            with Image.open(OUT / filename) as image:
                assert image.mode == "RGBA" and list(image.size) == element["size"]
                colors = set(image.get_flattened_data())
                all_colors.update(colors)
                assert all(pixel == TRANSPARENT or pixel[:3] in PALETTE for pixel in colors)
                assert image.getpixel((image.width // 2, image.height // 2))[3] == 228
                assert set(image.getchannel("A").get_flattened_data()) == {0, 228, 255}
                if element_id == "ui-slot":
                    assert len(set(image.crop((4, 4, 36, 36)).get_flattened_data())) == 1
                if "slice" in element:
                    sliced = element["slice"]
                    left, top, right, bottom = [sliced[key] for key in ("left", "top", "right", "bottom")]
                    assert left + right < image.width and top + bottom < image.height
                    # Edge strips are translationally constant so stretching has no seams.
                    for x in range(left + 1, image.width - right):
                        assert list(image.crop((x, 0, x + 1, top)).get_flattened_data()) == list(image.crop((left, 0, left + 1, top)).get_flattened_data())
                        assert list(image.crop((x, image.height - bottom, x + 1, image.height)).get_flattened_data()) == list(image.crop((left, image.height - bottom, left + 1, image.height)).get_flattened_data())
                    for y in range(top + 1, image.height - bottom):
                        assert list(image.crop((0, y, left, y + 1)).get_flattened_data()) == list(image.crop((0, top, left, top + 1)).get_flattened_data())
                        assert list(image.crop((image.width - right, y, image.width, y + 1)).get_flattened_data()) == list(image.crop((image.width - right, top, image.width, top + 1)).get_flattened_data())
                    stretched = nine_slice(image, (300, 120), sliced)
                    for source_box, target_box in [
                        ((0, 0, left, top), (0, 0, left, top)),
                        ((image.width - right, 0, image.width, top), (300 - right, 0, 300, top)),
                        ((0, image.height - bottom, left, image.height), (0, 120 - bottom, left, 120)),
                        ((image.width - right, image.height - bottom, image.width, image.height), (300 - right, 120 - bottom, 300, 120)),
                    ]:
                        assert stretched.crop(target_box).tobytes() == image.crop(source_box).tobytes()
                checks.append(dict(file=filename, size=list(image.size), mode=image.mode, colors=len(colors), alpha=[0, 228, 255], passed=True))
    assert len(images) == 9 and 12 <= len(all_colors) <= 16
    first = {path.name: sha256(path.read_bytes()).hexdigest() for path in sorted(OUT.iterdir()) if path.suffix in (".png", ".json")}
    review_first = {path.name: sha256(path.read_bytes()).hexdigest() for path in sorted((ART / "review").glob("*.png"))}
    rebuilt_manifest, rebuilt_images = build()
    review(rebuilt_manifest, rebuilt_images)
    second = {path.name: sha256(path.read_bytes()).hexdigest() for path in sorted(OUT.iterdir()) if path.suffix in (".png", ".json")}
    review_second = {path.name: sha256(path.read_bytes()).hexdigest() for path in sorted((ART / "review").glob("*.png"))}
    assert first == second
    assert review_first == review_second
    RUN.mkdir(parents=True, exist_ok=True)
    result = dict(passed=True, files=checks, sharedRgbaColorCount=len(all_colors),
                  paletteRgb=[list(rgb) for rgb in PALETTE], transparentRgba=list(TRANSPARENT),
                  bodyAlpha=228, sourceSha256=sha256(SOURCE.read_bytes()).hexdigest(),
                  deterministicRebuild=True, sha256=second, reviewSha256=review_second,
                  sliceChecks="constant edge strips, valid center dimensions, all four stretched corners unchanged",
                  coordinateConvention="top-left rectangles; slice and padding use named edges")
    (RUN / "checks.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(dict(passed=True, assets=len(images), colors=len(all_colors), deterministic=True)))


if __name__ == "__main__":
    manifest, images = build()
    review(manifest, images)
    verify(manifest, images)

