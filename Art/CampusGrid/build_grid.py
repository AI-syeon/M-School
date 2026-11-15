"""Build 32 px terrain tiles and whole-object Unity sprites."""

import argparse
import base64
import hashlib
import io
import json
import re
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageOps
from pixel_pipeline.cli import check_image, grid_atlas, save_json
from pixel_pipeline.normal import normal_map, pack_normal_atlas

TILE = 32
ROOT = Path(__file__).resolve().parent
UNITY = ROOT.parents[1] / "Assets/MagicCampus/CampusGrid"
sys.path.insert(0, str(ROOT.parent))
from sprite_grid import pack_frames
from unity_sprite_sheet import write_sprite_meta


def pixelize(image, size, colors=None):
    image = image.convert("RGBA").resize(size, Image.Resampling.BOX)
    alpha = image.getchannel("A").point(lambda value: 255 if value >= 128 else 0)
    if colors is not None:
        if type(colors) is not int or not 2 <= colors <= 256:
            raise ValueError("colors must be an integer in 2..256")
        image = image.convert("RGB").quantize(colors=colors, dither=Image.Dither.NONE).convert("RGBA")
    image.putalpha(alpha)
    return Image.composite(image, Image.new("RGBA", size), alpha)


def write_bytes(path, data):
    if not path.exists() or path.read_bytes() != data:
        path.parent.mkdir(parents=True, exist_ok=True)
        pending = path.with_name(path.name + ".pending")
        pending.write_bytes(data)
        pending.replace(path)


def png_bytes(image):
    buffer = io.BytesIO()
    image.save(buffer, "PNG")
    return buffer.getvalue()


def write_png(path, image):
    write_bytes(path, png_bytes(image))


def read_recipe(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def asset_id(value):
    if not isinstance(value, str) or not re.fullmatch(r"[a-z][a-z0-9-]{0,47}", value):
        raise ValueError(f"Invalid asset ID: {value!r}")
    return value


def pair(value, label):
    if not isinstance(value, list) or len(value) != 2 or any(type(n) is not int for n in value):
        raise ValueError(f"{label} must be [integer, integer]")
    return tuple(value)


def require_in_bounds(cells, size, label):
    if any(not (0 <= x < size[0] and 0 <= y < size[1]) for x, y in cells):
        raise ValueError(f"{label} extends outside {size}")


def source_region(root, spec, size, sources):
    path = (root / spec["source"]).resolve()
    data = path.read_bytes()
    sources[spec["source"]] = hashlib.sha256(data).hexdigest()
    with Image.open(io.BytesIO(data)) as source:
        region = source.convert("RGBA")
    if "crop" in spec:
        x, y, w, h = spec["crop"]
        if any(type(n) is not int for n in (x, y, w, h)) or not (
            0 <= x < x + w <= region.width and 0 <= y < y + h <= region.height
        ):
            raise ValueError(f"Invalid crop for {path.name}: {spec['crop']}")
        region = region.crop((x, y, x + w, y + h))
    fit = spec.get("fit", "stretch")
    if fit == "stretch":
        return pixelize(region, size, spec.get("colors")), region
    if fit != "contain":
        raise ValueError(f"Unsupported fit: {fit}")
    ratio = min(size[0] / region.width, size[1] / region.height)
    w, h = max(1, round(region.width * ratio)), max(1, round(region.height * ratio))
    result = Image.new("RGBA", size)
    result.alpha_composite(pixelize(region, (w, h), spec.get("colors")), ((size[0] - w) // 2, size[1] - h))
    return result, region


def split_cell(image, x, y):
    left, top = x * TILE, image.height - (y + 1) * TILE
    return image.crop((left, top, left + TILE, top + TILE))


def register_tile(key, frames, fps, tiles, images, normal_images, normals=None):
    names = []
    seen = {}
    for index, frame in enumerate(frames):
        check_image(frame, (TILE, TILE), False)
        normal = normals[index] if normals is not None else None
        pixels = (frame.tobytes(), normal.tobytes() if normal is not None else None)
        if pixels not in seen:
            name = f"{key}-{len(seen):02}.png"
            seen[pixels] = name
            images[name] = frame
            if normal is not None:
                normal_images[name] = normal
        names.append(seen[pixels])
    animated = len(seen) > 1
    tiles.append({"id": key, "frames": names if animated else names[:1], "fps": fps if animated else 0})
    return animated


def comparison(rows):
    width, height = 256, 160
    canvas = Image.new("RGBA", (width * 2, height * len(rows) + 26), "#252a32")
    draw = ImageDraw.Draw(canvas)
    draw.text((8, 7), "SOURCE CROP", fill="white")
    draw.text((width + 8, 7), "32 PX GRID", fill="white")
    for index, (key, source, result) in enumerate(rows):
        top = 26 + height * index
        for column, item in enumerate((source, result)):
            limit = (width - 12, height - 24)
            scale = min(limit[0] // item.width, limit[1] // item.height)
            if column and scale >= 1:
                preview = item.resize((item.width * scale, item.height * scale), Image.Resampling.NEAREST)
            else:
                sampling = Image.Resampling.NEAREST if column else Image.Resampling.LANCZOS
                preview = ImageOps.contain(item, limit, sampling)
            canvas.alpha_composite(preview, (width * column + (width - preview.width) // 2, top))
        draw.text((8, top + height - 17), key, fill="white")
    return canvas


def build(art_path, map_path, unity, review):
    art, map_spec = read_recipe(art_path), read_recipe(map_path)
    if art.get("schema") != 1 or map_spec.get("schema") != 1:
        raise ValueError("art and map recipes must use schema 1")
    use_normals = map_spec.get("normalMaps", art.get("normalMaps", True))
    if type(use_normals) is not bool:
        raise ValueError("normalMaps must be true or false")
    previous = read_recipe(unity / "layout.json") if (unity / "layout.json").exists() else {}
    sources, images, ground, props = {}, {}, {}, {}
    normal_images = {}
    tiles, comparisons = [], []
    library = []
    for name in art.get("libraries", []):
        recipe = read_recipe(art_path.parent / name)
        library.extend(recipe["props"] if isinstance(recipe, dict) else recipe)
    for spec in art["tiles"]:
        key = asset_id(spec["id"])
        if key in ground:
            raise ValueError(f"Duplicate ground tile: {key}")
        result, original = source_region(art_path.parent, spec, (TILE, TILE), sources)
        if result.getchannel("A").getextrema() != (255, 255):
            raise ValueError(f"Ground tile must be opaque: {key}")
        ground[key] = result
        normals = [normal_map(result)] if use_normals else None
        register_tile("ground-" + key, [result], 0, tiles, images, normal_images, normals)
        comparisons.append((key, original, result))

    animations, prop_report, packed_ids = {}, {}, set()
    for spec in art["props"] + library:
        key = asset_id(spec["id"])
        packed = spec in library
        size = pair(spec["size"], f"{key} size")
        if key in props or not all(1 <= n <= 64 for n in size):
            raise ValueError(f"Invalid or duplicate prop: {key}")
        blocked = [pair(cell, f"{key} blocked cell") for cell in spec.get("blocked", [])]
        require_in_bounds(blocked, size, key + " collision")
        if len(set(blocked)) != len(blocked):
            raise ValueError(f"Duplicate collision cells: {key}")
        pixels = tuple(n * TILE for n in size)
        converted = [source_region(art_path.parent, frame, pixels, sources) for frame in spec["frames"]]
        fps = spec.get("fps", 0)
        if not converted or (len(converted) > 1 and (type(fps) not in (int, float) or not 0 < fps <= 60)):
            raise ValueError(f"{key} needs frames and animated props need 0 < fps <= 60")
        frames = [frame for frame, _ in converted]
        for frame in frames:
            check_image(frame, pixels, True)
        if len(frames) > 1 and len({frame.tobytes() for frame in frames}) == 1:
            raise ValueError(f"All animation frames are identical: {key}")
        if "comparisonSource" in spec:
            _, original = source_region(art_path.parent, spec["comparisonSource"], pixels, sources)
        else:
            original = converted[0][1]
        comparisons.append((key, original, frames[0]))
        sheet, normal_sheet, rects = pack_frames(frames, spec.get("columns"), use_normals)
        image = f"Sprites/{key}.png"
        normal = f"Normals/Sprites/{key}.png" if use_normals else None
        write_png(unity / image, sheet)
        if normal is not None:
            write_png(unity / normal, normal_sheet)
        named = [(f"{key}_{i:03d}", rect["x"], rect["y"], rect["w"], rect["h"]) for i, rect in enumerate(rects)]
        write_sprite_meta(unity / image, sheet.size, TILE, named, normal=unity / normal if normal else None)
        # Retain the existing sort bounds: library props previously omitted fully transparent cells.
        bounds = [0, 0, *size]
        if packed:
            boxes = [frame.getchannel("A").getbbox() for frame in frames]
            left = min(b[0] for b in boxes) // TILE
            bottom = size[1] - (max(b[3] for b in boxes) + TILE - 1) // TILE
            right = (max(b[2] for b in boxes) + TILE - 1) // TILE
            top = size[1] - min(b[1] for b in boxes) // TILE
            bounds = [left, bottom, right - left, top - bottom]
        props[key] = {"id": key, "image": image, "normal": normal, "rectangles": rects, "fps": fps,
                      "bounds": bounds, "blocked": [{"x": x, "y": y} for x, y in blocked]}
        animations[key] = (frames, fps, size)
        prop_report[key] = {"size": size, "frames": len(frames), "fps": fps, "sheetSize": sheet.size}

    terrain = []
    if "groundImage" in map_spec:
        width, height = pair(map_spec["size"], "map size")
        if not (1 <= width <= 64 and 1 <= height <= 64):
            raise ValueError("Map dimensions must be between 1 and 64 cells")
        canvas, original = source_region(map_path.parent, map_spec["groundImage"], (width * TILE, height * TILE), sources)
        if canvas.getchannel("A").getextrema() != (255, 255):
            raise ValueError("Map ground image must be opaque")
        comparisons.insert(0, ("plaza ground", original, canvas.copy()))
    else:
        rows, legend = map_spec["rows"], map_spec["legend"]
        if not rows or not rows[0] or any(len(row) != len(rows[0]) for row in rows):
            raise ValueError("The map must have nonempty rows of equal width")
        width, height = len(rows[0]), len(rows)
        canvas = Image.new("RGBA", (width * TILE, height * TILE))
        for row, symbols in enumerate(rows):
            for x, symbol in enumerate(symbols):
                key = legend[symbol]
                canvas.paste(ground[key], (x * TILE, row * TILE))
                terrain.append({"tile": "ground-" + key, "x": x, "y": height - 1 - row})
    ground_normal = None
    if use_normals:
        spec = map_spec.get("groundNormal", {})
        heights = None
        if "height" in spec:
            heights, _ = source_region(map_path.parent, spec["height"], canvas.size, sources)
        ground_normal = normal_map(canvas, height_map=heights, strength=spec.get("strength", 3.0))
    if "groundImage" in map_spec:
        for y in range(height):
            for x in range(width):
                key = f"ground-plaza-{x:02}-{y:02}"
                normals = [split_cell(ground_normal, x, y)] if ground_normal is not None else None
                register_tile(key, [split_cell(canvas, x, y)], 0, tiles, images, normal_images, normals)
                packed_ids.add(key)
                terrain.append({"tile": key, "x": x, "y": y})

    placements, occupied, blocked_cells = [], set(), set()
    allow_overlap = map_spec.get("allowVisualOverlap", False)
    for entry in map_spec.get("props", []):
        key = entry["id"]
        x, y = pair(entry["at"], f"{key} placement")
        _, _, size = animations[key]
        footprint = {(x + dx, y + dy) for dx in range(size[0]) for dy in range(size[1])}
        if not allow_overlap:
            require_in_bounds(footprint, (width, height), key + " placement")
            if occupied & footprint:
                raise ValueError(f"Prop bounds overlap at {key} ({x}, {y})")
            occupied.update(footprint)
        elif x >= width or y >= height or x + size[0] <= 0 or y + size[1] <= 0:
            raise ValueError(f"Prop is entirely outside the map: {key} ({x}, {y})")
        decorative = entry.get("decorative", False)
        blockers = set() if decorative else {(x + cell["x"], y + cell["y"]) for cell in props[key]["blocked"]}
        require_in_bounds(blockers, (width, height), key + " collision placement")
        if blocked_cells & blockers:
            raise ValueError(f"Prop collision overlaps at {key} ({x}, {y})")
        blocked_cells.update(blockers)
        order = entry.get("order", 1000 - y * 20)
        if type(order) is not int or not 1 <= order <= 32767:
            raise ValueError(f"Invalid sorting order for {key}: {order}")
        placements.append({"id": key, "x": x, "y": y, "order": order, "decorative": decorative})
    for entry in sorted(placements, key=lambda item: item["order"]):
        frames, _, size = animations[entry["id"]]
        canvas.alpha_composite(frames[0], (entry["x"] * TILE, (height - entry["y"] - size[1]) * TILE))

    camera = map_spec.get("camera", [0, 0, width, height])
    vx, vy, vw, vh = camera
    if any(type(n) is not int for n in camera) or not (0 <= vx < vx + vw <= width and 0 <= vy < vy + vh <= height):
        raise ValueError("Camera must be an in-bounds [x, y, width, height] rectangle in cells")
    glows = map_spec.get("glows", [])
    if len(glows) > 8 or any(not (0 < glow["radius"] and 0 <= glow["power"] <= 1) for glow in glows):
        raise ValueError("Use up to eight glows with positive radius and power in 0..1")
    shadows = map_spec.get("shadows", [])
    if any(not (0 < shadow["width"] and 0 < shadow["height"] and 0 <= shadow["opacity"] <= 1) for shadow in shadows):
        raise ValueError("Shadows need positive dimensions and opacity in 0..1")
    daylight = map_spec.get("daylight")
    mask_bytes = None
    if daylight is not None:
        canopy = daylight["canopy"]
        count = sum(entry["id"] == canopy["prop"] for entry in placements) + len(daylight.get("offscreen", []))
        if canopy["prop"] not in props or not 1 <= count <= 12:
            raise ValueError("Daylight needs 1–12 casters referencing a known prop")
        if min(canopy["width"], canopy["depth"], canopy["height"]) <= 0 or not 0 <= canopy["opacity"] <= 1:
            raise ValueError("Daylight canopy needs positive dimensions and opacity in 0..1")
        if not (0 <= daylight["startHour"] < 24 and daylight["secondsPerDay"] >= 0):
            raise ValueError("Invalid daylight preview clock")
        mask_bytes = (map_path.parent / daylight["mask"]).read_bytes()
        sources[daylight["mask"]] = hashlib.sha256(mask_bytes).hexdigest()
        with Image.open(io.BytesIO(mask_bytes)) as mask:
            check_image(mask, (4 * TILE, 4 * TILE), True)

    # Validate packaging by reconstructing each cell from the local pipeline's atlas.
    atlas, rectangles = grid_atlas(list(images.values()), 8)
    for frame, rect in zip(images.values(), rectangles):
        x, y = rect["x"], rect["y"]
        if atlas.crop((x, y, x + TILE, y + TILE)).tobytes() != frame.tobytes():
            raise ValueError(f"Packed cell differs at ({x}, {y})")

    packed_tiles = [tile for tile in tiles if tile["id"] in packed_ids]
    standalone = [tile for tile in tiles if tile["id"] not in packed_ids]
    layout = {"schema": 1, "tileSize": TILE, "width": width, "height": height, "tiles": standalone,
              "camera": camera, "glows": glows, "shadows": shadows,
              "ground": terrain, "props": list(props.values()), "placements": placements}
    if packed_tiles:
        names = list(dict.fromkeys(name for tile in packed_tiles for name in tile["frames"]))
        packed_atlas, packed_rects = grid_atlas([images[name] for name in names], 32)
        payload = {"tileSize": TILE, "png": base64.b64encode(png_bytes(packed_atlas)).decode("ascii"),
                   "frames": [{"name": name, **rect} for name, rect in zip(names, packed_rects)], "tiles": packed_tiles}
        layout["packedAtlas"] = "scenic.campusatlas"
        write_bytes(unity / layout["packedAtlas"], (json.dumps(payload, separators=(",", ":")) + "\n").encode())
        if use_normals:
            normal_atlas = pack_normal_atlas(packed_atlas.size, ((normal_images[name], rect) for name, rect in zip(names, packed_rects)))
            layout["packedNormalAtlas"] = "Normals/scenic.png"
            write_png(unity / layout["packedNormalAtlas"], normal_atlas)
    if mask_bytes is not None:
        layout["daylight"] = daylight
        write_bytes(unity / "CanopyShadow.png", mask_bytes)
    if ground_normal is not None:
        layout["groundNormal"] = "GroundNormal.png"
        write_png(unity / layout["groundNormal"], ground_normal)
    standalone_frames = dict.fromkeys(name for tile in standalone for name in tile["frames"])
    for name in standalone_frames:
        frame = images[name]
        write_png(unity / "Textures" / name, frame)
        if use_normals:
            write_png(unity / "Normals" / name, normal_images[name])
    if use_normals:
        layout["normalDirectory"] = "Normals"
    write_bytes(unity / "layout.json", (json.dumps(layout, separators=(",", ":")) + "\n").encode())

    obsolete = {folder + "/" + name for spec in previous.get("tiles", []) if spec["id"].startswith("prop-")
                for name in spec["frames"] for folder in ("Textures", "Normals")}
    if not packed_tiles:
        obsolete.update(path for key in ("packedAtlas", "packedNormalAtlas") if (path := previous.get(key)))
    active = {p[field] for p in props.values() for field in ("image", "normal") if p[field]}
    obsolete.update(p[field] for p in previous.get("props", []) for field in ("image", "normal")
                    if p.get(field) and p[field] not in active)
    project_art = UNITY.resolve()
    if unity.resolve().is_relative_to(project_art):
        mirror = ROOT.parents[1] / "Assets/StreamingAssets/CampusGrid" / unity.resolve().relative_to(project_art)
        paths = active | {"layout.json"} | {layout[k] for k in ("packedAtlas", "packedNormalAtlas", "groundNormal")
                                             if k in layout}
        if mask_bytes is not None:
            paths.add("CanopyShadow.png")
        paths.update(f"Textures/{name}" for name in standalone_frames)
        if use_normals:
            paths.update(f"Normals/{name}" for name in standalone_frames)
        for path in sorted(paths - {"layout.json"}) + ["layout.json"]:
            write_bytes(mirror / path, (unity / path).read_bytes())
    else:
        mirror = None
    for folder in filter(None, (unity, mirror)):
        for path in obsolete:
            target = (folder / path).resolve()
            if target.is_relative_to(folder.resolve()):
                target.unlink(missing_ok=True)
                target.with_name(target.name + ".meta").unlink(missing_ok=True)

    review.mkdir(parents=True, exist_ok=True)
    write_png(review / "source-vs-grid.png", comparison(comparisons))
    top = height - vy - vh
    write_png(review / "courtyard.png", canvas.crop((vx * TILE, top * TILE, (vx + vw) * TILE, (top + vh) * TILE)))
    write_png(review / "cell-atlas.png", atlas)
    for key, (frames, fps, size) in animations.items():
        if len(frames) > 1:
            preview_size = (size[0] * TILE * 3, size[1] * TILE * 3)
            # APNG keeps the full RGBA palette and frame durations for comparison.
            preview = [frame.resize(preview_size, Image.Resampling.NEAREST) for frame in frames]
            preview[0].save(review / (key + ".apng"), save_all=True, append_images=preview[1:],
                            duration=round(1000 / fps), loop=0, disposal=1)
    save_json(review / "build-report.json", {
        "tileSize": TILE, "sourceSha256": sources, "uniqueCellPngs": len(images),
        "atlas": [{"image": name, **rect} for name, rect in zip(images, rectangles)],
        "mapCells": len(terrain), "propCount": len(placements),
        "props": prop_report,
    })
    print(f"Built {width}×{height} map, {len(tiles)} tile definitions and {len(images)} unique cell PNGs.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--art", type=Path, default=ROOT / "art.json")
    parser.add_argument("--map", type=Path, default=ROOT / "map.json")
    parser.add_argument("--unity-out", type=Path, default=UNITY)
    parser.add_argument("--review-out", type=Path, default=ROOT / "review")
    args = parser.parse_args()
    build(args.art, args.map, args.unity_out, args.review_out)
