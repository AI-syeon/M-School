"""Derives map collision, portals and placed content from the world spec and the CampusGrid art layouts.

Run from the repository root with the pipeline's Python:

    C:/Users/syeon/Workspace/2D-pixel-asset-pipeline/.venv/Scripts/python.exe Content/Fixtures/FirstConnection/tools/derive_world.py [--check]

Reads Content/Fixtures/FirstConnection/world/world.json and Assets/MagicCampus/CampusGrid/<layout> per map (a map whose
layout file does not exist yet uses its Stage5 fallback rows). Writes, in every bundle folder the spec lists:
runner-bundle.json sections maps, destinations, npcs (position), magicTargets (position), bed; staging-recipes.json
arrival and join points; the player block of every start-*.json. Writes generated/world-art.json for the renderer: how each map
is drawn, the average colour of every cell as drawn (maps.<id>.minimap), the sprite each magic target shows per tag state
(magicTargets[].look), the glow and spell-effect tables. Needs numpy and Pillow (the pipeline venv has both).
A room listed under "arrangeable" lets the player move some of its furniture: the listed pieces (layout placements, matched
by prop id and position) leave the static collision and the base minimap, and are written to the bundle section "arrangeable"
(cells each piece blocks, where it stands by default, the area and the cells that must stay free; "stackable" marks a small piece that
may stand on a host's "surface" cells, "lift" how many pixels it is drawn raised there) and to world-art.json
(maps.<id>.pieces: the layout parts that draw the piece, and its per-cell colour for the minimap).
A magic target stands on the cell of its x,y: the blocked cells of its sprite prop in the layout become collision, and at least
one neighbouring cell must stay walkable and connected to the map's content.
Sections it does not own are left byte for byte. --check writes nothing and exits 1 when any file would change.
"""

import base64
import io
import json
import re
import sys
from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[4]
FIXTURES = ROOT / "Content" / "Fixtures" / "FirstConnection"
GENERATED = FIXTURES / "generated"
CAMPUS_GRID = ROOT / "Assets" / "MagicCampus" / "CampusGrid"
STAGE5 = ROOT / "Art" / "Stage5-v1"
STEPS = ((1, 0), (-1, 0), (0, 1), (0, -1))


def cell_of(spot):
    return int(spot["x"] // 1), int(spot["y"] // 1)


def part_key(part):
    return part.get("prop", part.get("id")), part["x"], part["y"]


def movable_keys(arrangeable):
    """(prop, x, y) of every layout placement the player may move in this room."""
    return {part_key(part) for piece in (arrangeable or {"pieces": []})["pieces"] for part in piece["parts"]}


def layout_cells(layout, skip=frozenset()):
    """Blocked cells of every non-decorative placement, clipped to the map. Placements in skip are movable and not part of the static map."""
    props = {p["id"]: p for p in layout["props"]}
    blocked = set()
    for placement in layout["placements"]:
        if placement.get("decorative") or part_key(placement) in skip:
            continue
        for b in props[placement["id"]]["blocked"]:
            x, y = placement["x"] + b["x"], placement["y"] + b["y"]
            if 0 <= x < layout["width"] and 0 <= y < layout["height"]:
                blocked.add((x, y))
    return layout["width"], layout["height"], blocked


def fallback_cells(fallback, legend):
    rows = fallback["rows"]
    height, width = len(rows), len(rows[0])
    blocked = set()
    for r, row in enumerate(rows):
        if len(row) != width:
            raise ValueError("fallback rows must all be %d wide" % width)
        for x, ch in enumerate(row):
            if legend[ch]["blocked"]:
                blocked.add((x, height - 1 - r))
    return width, height, blocked


def seal_unreachable(width, height, blocked, doors, seeds):
    """Blocks every cell no seed can walk to. Door cells are reached but never walked through (stepping in warps)."""
    reached = set()
    queue = deque(s for s in seeds if s not in blocked)
    reached.update(queue)
    while queue:
        x, y = queue.popleft()
        for dx, dy in STEPS:
            n = (x + dx, y + dy)
            if n in reached or n in blocked or not (0 <= n[0] < width and 0 <= n[1] < height):
                continue
            reached.add(n)
            if n not in doors:
                queue.append(n)
    for door in doors:
        if door not in reached:
            raise ValueError("door %s cannot be reached from the map's content" % (door,))
    return {(x, y) for x in range(width) for y in range(height) if (x, y) not in reached}


def check_walkable_together(map_id, width, height, blocked, doors, seeds):
    """Every door and every content spot of a map must lie in one walkable region, so nobody is stranded between two doors."""
    start = next(iter(sorted(doors)), None) or seeds[0]
    reached, queue = {start}, deque([start])
    while queue:
        x, y = queue.popleft()
        for dx, dy in STEPS:
            n = (x + dx, y + dy)
            if n in reached or n in blocked or not (0 <= n[0] < width and 0 <= n[1] < height):
                continue
            reached.add(n)
            if n not in doors:
                queue.append(n)
    missing = [c for c in list(doors) + list(seeds) if c not in reached]
    if missing:
        raise ValueError("%s: cells %s are cut off from the rest of the map" % (map_id, sorted(set(missing))))


def check_campus_connected(maps):
    """Every map can be walked to from every other through portals (they pair up, so one direction is enough)."""
    first = next(iter(maps))
    seen, queue = {first}, deque([first])
    while queue:
        for portal in maps[queue.popleft()]["portals"]:
            to = portal["to"]["mapId"]
            if to not in seen:
                seen.add(to)
                queue.append(to)
    if seen != set(maps):
        raise ValueError("maps %s cannot be reached from %s" % (sorted(set(maps) - seen), first))


def check_portals_pair(maps):
    """A portal's arrival is a portal cell of the other map, and that portal leads back to this one."""
    for m in maps.values():
        for p in m["portals"]:
            back = [q for q in maps[p["to"]["mapId"]]["portals"] if (int(q["x"]), int(q["y"])) == (int(p["to"]["x"]), int(p["to"]["y"]))]
            if len(back) != 1 or back[0]["to"]["mapId"] != m["id"] or (int(back[0]["to"]["x"]), int(back[0]["to"]["y"])) != (int(p["x"]), int(p["y"])):
                raise ValueError("portal %s does not pair with a portal leading back" % p["id"])


def target_sprites(target):
    look = target["look"]
    return [look["default"]] + [s["sprite"] for s in look["states"]]


def target_footprint(layout, target):
    """The cells a target blocks: the blocked cells of its sprites' props (all states share one footprint) at its own cell."""
    props = {p["id"]: p for p in layout["props"]}
    default = props.get(target["look"]["default"])
    footprints = []
    for sprite in target_sprites(target):
        if sprite not in props:
            raise ValueError("%s look uses %s, which is not a prop of the map layout" % (target["id"], sprite))
        footprints.append(sorted((b["x"], b["y"]) for b in props[sprite]["blocked"]))
        if prop_bounds(props[sprite]) != prop_bounds(default):
            raise ValueError("%s sprites %s and %s differ in size" % (target["id"], default["id"], sprite))
    if any(f != footprints[0] for f in footprints):
        raise ValueError("%s sprites block different cells" % target["id"])
    cx, cy = cell_of(target)
    return {(cx + x, cy + y) for x, y in footprints[0]}


def target_footprints(spec, map_id, layout, blocked):
    """Blocked cells of every magic target on the map. A target may not stand on a prop's cells."""
    cells = set()
    for t in spec["magicTargets"]:
        if t["mapId"] != map_id:
            continue
        foot = target_footprint(layout, t)
        if foot & blocked:
            raise ValueError("%s overlaps blocked cell(s) %s" % (t["id"], sorted(foot & blocked)))
        if foot & cells:
            raise ValueError("%s overlaps another target" % t["id"])
        cells |= foot
    return cells


def check_target_access(spec, map_id, width, height, blocked):
    """Every target needs a walkable, connected neighbour cell to stand on while casting at it."""
    for t in spec["magicTargets"]:
        if t["mapId"] != map_id:
            continue
        cx, cy = cell_of(t)
        near = [(cx + dx, cy + dy) for dx, dy in STEPS]
        if not any(0 <= x < width and 0 <= y < height and (x, y) not in blocked for x, y in near):
            raise ValueError("%s has no walkable neighbour cell (cell %d,%d)" % (t["id"], cx, cy))


# ------------------------------------------------------------------ minimap: the mean colour of every cell as drawn

def tile_images(layout_path, layout):
    """Tile id -> first frame, the way CampusLayout reads them: cell PNGs, then the packed atlas on top."""
    folder = layout_path.parent
    images = {}
    for t in layout["tiles"]:
        path = folder / "Textures" / t["frames"][0]
        images[t["id"]] = Image.open(path if path.exists() else folder / t["frames"][0]).convert("RGBA")
    if layout.get("packedAtlas"):
        atlas = json.loads((folder / layout["packedAtlas"]).read_text(encoding="utf-8"))
        sheet = Image.open(io.BytesIO(base64.b64decode(atlas["png"]))).convert("RGBA")
        sprites = {f["name"]: sheet.crop((f["x"], f["y"], f["x"] + f["w"], f["y"] + f["h"])) for f in atlas["frames"]}
        for t in atlas["tiles"]:
            images[t["id"]] = sprites[t["frames"][0]]
    return images


def prop_bounds(prop):
    if "bounds" in prop:
        return tuple(prop["bounds"])
    cells = prop["cells"]
    x, y = min(c["x"] for c in cells), min(c["y"] for c in cells)
    return x, y, max(c["x"] for c in cells) - x + 1, max(c["y"] for c in cells) - y + 1


def prop_images(layout_path, layout, tiles):
    """Prop id -> first whole-object frame and its offset from the placement."""
    result = {}
    size = layout["tileSize"]
    for prop in layout["props"]:
        if "image" in prop:
            frame = prop["rectangles"][0]
            x, y, w, h = (frame[k] for k in ("x", "y", "w", "h"))
            with Image.open(layout_path.parent / prop["image"]) as sheet:
                result[prop["id"]] = (sheet.crop((x, y, x + w, y + h)).convert("RGBA"), 0, 0)
        else:
            x, y, w, h = prop_bounds(prop)
            frame = Image.new("RGBA", (w * size, h * size))
            for c in prop["cells"]:
                frame.paste(tiles[c["tile"]], ((c["x"] - x) * size, (y + h - 1 - c["y"]) * size))
            result[prop["id"]] = (frame, x, y)
    return result


def sort_lines(placements, props):
    """WorldMap.SortLines: each placement's foot line, lowered so a prop the layout orders in front of an overlapping one stays in front."""
    def bounds(p):
        x, y, w, h = prop_bounds(props[p["id"]])
        return p["x"] + x, p["y"] + y, w, h
    def overlaps(a, b):
        return a[0] < b[0] + b[2] and b[0] < a[0] + a[2] and a[1] < b[1] + b[3] and b[1] < a[1] + a[3]
    lines = [float(p["y"]) for p in placements]
    by_order = sorted(range(len(placements)), key=lambda i: (placements[i]["order"], i))
    for k, i in enumerate(by_order):
        for j in by_order[:k]:
            if placements[j]["order"] < placements[i]["order"] and overlaps(bounds(placements[i]), bounds(placements[j])):
                lines[i] = min(lines[i], lines[j] - 0.01)
    return lines


def draw_order(layout, props, targets):
    """Props in the order the game draws them, last on top: floor decor first, then by foot line (lower on screen in front).
    A magic target is drawn with its default sprite at its own cell."""
    placements = layout["placements"]
    lines = sort_lines(placements, props)
    front = [p["order"] for p in placements if not p.get("decorative")]
    items = []
    for i, p in enumerate(placements):
        floor_decor = p.get("decorative") and not props[p["id"]]["blocked"] and all(p["order"] < o for o in front)
        items.append((3 + p["order"] if floor_decor else 20000 - round(lines[i] * 100), len(items), p["id"], p["x"], p["y"]))
    for t in targets:
        cx, cy = cell_of(t)
        items.append((20000 - round(cy * 100), len(items), t["look"]["default"], cx, cy))
    return [(pid, x, y) for _, _, pid, x, y in sorted(items)]


def paste(canvas, tile, left, top):
    """alpha_composite clipped to the canvas (props may hang over the map edge)."""
    x0, y0 = max(left, 0), max(top, 0)
    x1, y1 = min(left + tile.width, canvas.width), min(top + tile.height, canvas.height)
    if x0 < x1 and y0 < y1:
        canvas.alpha_composite(tile.crop((x0 - left, y0 - top, x1 - left, y1 - top)), (x0, y0))


def mean_colour(pixels):
    """Alpha-weighted mean of the visible pixels of an RGBA array as '#rrggbb'; opaque art gives the plain mean."""
    alpha = pixels[..., 3].astype(np.float64).reshape(-1)
    if alpha.sum() == 0:
        return "#000000"
    rgb = (pixels[..., :3].astype(np.float64).reshape(-1, 3) * alpha[:, None]).sum(0) / alpha.sum()
    return "#%02x%02x%02x" % tuple(int(round(v)) for v in rgb)


def layout_minimap(layout_path, layout, spec, map_id, skip=frozenset()):
    """Rows top first, each row the space separated '#rrggbb' means of its cells, from the layout drawn the way the game draws it.
    Movable pieces (skip) are left out; the game paints them on top from their own per-cell colours (piece_art)."""
    layout = dict(layout, placements=[p for p in layout["placements"] if part_key(p) not in skip])
    images = tile_images(layout_path, layout)
    sprites = prop_images(layout_path, layout, images)
    props = {p["id"]: p for p in layout["props"]}
    size, width, height = layout["tileSize"], layout["width"], layout["height"]
    canvas = Image.new("RGBA", (width * size, height * size), (0, 0, 0, 0))
    def put(tile_id, x, y):
        paste(canvas, images[tile_id], x * size, (height - 1 - y) * size)
    for c in layout["ground"]:
        put(c["tile"], c["x"], c["y"])
    for pid, px, py in draw_order(layout, props, [t for t in spec["magicTargets"] if t["mapId"] == map_id]):
        frame, dx, dy = sprites[pid]
        paste(canvas, frame, (px + dx) * size, (height - py - dy) * size - frame.height)
    pixels = np.asarray(canvas)
    return [" ".join(mean_colour(pixels[r * size:(r + 1) * size, x * size:(x + 1) * size]) for x in range(width)) for r in range(height)]


def stage5_minimap(fallback, legend):
    """A Stage5 room: each cell's colour is the mean of its Stage5 tile (the legend's tile, else the room's floor)."""
    manifest = json.loads((STAGE5 / "runtime-manifest.json").read_text(encoding="utf-8"))
    sheets, means = {}, {}
    def tile_mean(name):
        t = manifest["tiles"][name]
        sheet = sheets.setdefault(t["image"], Image.open(STAGE5 / t["image"]).convert("RGBA"))
        f = t["frames"][0]
        return mean_colour(np.asarray(sheet.crop((f["x"], f["y"], f["x"] + f["w"], f["y"] + f["h"]))))
    return [" ".join(means.setdefault(n, tile_mean(n)) for n in (legend.get(ch) or fallback["floor"] for ch in row)) for row in fallback["rows"]]


# ------------------------------------------------------------------ arrangeable rooms: movable furniture

def piece_cells(piece_parts, props):
    """(footprint, bounds) of a piece relative to its anchor (the first part): the blocked cells of its non-decorative parts, and the
    box of every cell it draws as (x0, y0, w, h)."""
    ax, ay = piece_parts[0][0]["x"], piece_parts[0][0]["y"]
    foot, cells = set(), set()
    for placement, _ in piece_parts:
        ox, oy = placement["x"] - ax, placement["y"] - ay
        prop = props[placement["id"]]
        x, y, w, h = prop_bounds(prop)
        cells.update((ox + cx, oy + cy) for cx in range(x, x + w) for cy in range(y, y + h))
        if not placement.get("decorative"):
            foot |= {(ox + b["x"], oy + b["y"]) for b in prop["blocked"]}
    x0, y0 = min(x for x, _ in cells), min(y for _, y in cells)
    return sorted(foot), [x0, y0, max(x for x, _ in cells) - x0 + 1, max(y for _, y in cells) - y0 + 1]


def piece_colours(piece_parts, sprites, size, bounds):
    """'dx dy #rrggbb alpha' per cell the piece draws: its parts composited in layout order on a clear canvas, then the
    alpha weighted mean colour and the mean opacity of every cell. The game paints these over the minimap of the empty room."""
    ax, ay = piece_parts[0][0]["x"], piece_parts[0][0]["y"]
    x0, y0, w, h = bounds
    canvas = Image.new("RGBA", (w * size, h * size), (0, 0, 0, 0))
    for placement, _ in sorted(piece_parts, key=lambda pi: (pi[0]["order"], pi[1])):
        ox, oy = placement["x"] - ax, placement["y"] - ay
        frame, dx, dy = sprites[placement["id"]]
        paste(canvas, frame, (ox + dx - x0) * size, (h - (oy + dy - y0)) * size - frame.height)
    pixels = np.asarray(canvas)
    out = []
    for cy in range(h):
        for cx in range(w):
            block = pixels[(h - 1 - cy) * size:(h - cy) * size, cx * size:(cx + 1) * size]
            opacity = float(block[..., 3].mean()) / 255.0
            if opacity >= 0.02:
                out.append("%d %d %s %.2f" % (cx + x0, cy + y0, mean_colour(block), opacity))
    return out


def resolve_arrangeable(arr, layout, layout_path, blocked, door_cells, seeds, width, height):
    """Checks the room's pieces against its layout and static map, returns (bundle section row, per-piece art for world-art.json).
    blocked is the static collision without any movable piece. The pieces' default positions must already form a legal arrangement."""
    area = arr["area"]
    props = {p["id"]: p for p in layout["props"]}
    sprites = prop_images(layout_path, layout, tile_images(layout_path, layout))
    keep = sorted(set(door_cells) | set(seeds))
    placements = layout["placements"]
    claimed, pieces, art = set(), [], {}
    for piece in arr["pieces"]:
        if piece["id"] in art:
            raise ValueError("%s: piece id %s repeats" % (arr["mapId"], piece["id"]))
        parts = []
        for part in piece["parts"]:
            index = [i for i, p in enumerate(placements) if part_key(p) == part_key(part)]
            if len(index) != 1 or index[0] in claimed:
                raise ValueError("%s piece %s: part %s must match exactly one unclaimed layout placement" % (arr["mapId"], piece["id"], part_key(part)))
            claimed.add(index[0])
            parts.append((placements[index[0]], index[0]))
        foot, bounds = piece_cells(parts, props)
        ax, ay = piece["parts"][0]["x"], piece["parts"][0]["y"]
        row = {"id": piece["id"], "name": piece["name"], "at": [ax, ay], "cells": [list(c) for c in foot], "bounds": bounds}
        surface = [list(c) for c in piece.get("surface", [])]
        if piece.get("stackable"):
            row["stackable"] = True
            if surface:
                raise ValueError("%s piece %s is stackable, so it cannot be a host with a surface" % (arr["mapId"], piece["id"]))
            if not foot:
                raise ValueError("%s piece %s is stackable but has no footprint to stand on a surface with" % (arr["mapId"], piece["id"]))
        if surface:
            if any(c not in row["cells"] for c in surface):
                raise ValueError("%s piece %s: surface cells must lie inside its footprint" % (arr["mapId"], piece["id"]))
            row["surface"] = surface
        if piece.get("lift"):
            row["lift"] = int(piece["lift"])
        pieces.append(row)
        flat = not foot and all(p.get("decorative") for p, _ in parts)
        art[piece["id"]] = {"parts": [{"prop": p["id"], "x": p["x"], "y": p["y"]} for p, _ in parts], "flat": flat,
                            "cells": piece_colours(parts, sprites, layout["tileSize"], bounds)}
    # The default arrangement is the layout as drawn: it has to satisfy the rule the game enforces on the player.
    occupied, floor, stacked = set(), set(), set()
    hosts = {p["id"]: {(p["at"][0] + dx, p["at"][1] + dy) for dx, dy in p.get("surface", [])} for p in pieces if not p.get("stackable")}
    for piece in pieces:
        ax, ay = piece["at"]
        cells = {(ax + dx, ay + dy) for dx, dy in piece["cells"]} or {(ax + piece["bounds"][0] + i, ay + piece["bounds"][1] + j)
                                                                       for i in range(piece["bounds"][2]) for j in range(piece["bounds"][3])}
        x0, y0, w, h = piece["bounds"]
        if not (0 <= ax + x0 and ax + x0 + w <= width and 0 <= ay + y0 and ay + y0 + h <= height):
            raise ValueError("%s: piece %s hangs off the map" % (arr["mapId"], piece["id"]))
        if any(not (area[0] <= x <= area[2] and area[1] <= y <= area[3]) for x, y in cells):
            raise ValueError("%s: piece %s starts outside the area" % (arr["mapId"], piece["id"]))
        if not piece["cells"]:
            if cells & floor:
                raise ValueError("%s: piece %s starts on another flat piece" % (arr["mapId"], piece["id"]))
            floor |= cells
        elif piece.get("stackable") and any(cells <= surface for surface in hosts.values()):
            if cells & stacked:
                raise ValueError("%s: piece %s starts on a surface spot that is already taken" % (arr["mapId"], piece["id"]))
            stacked |= cells
        else:
            if cells & blocked or cells & set(keep) or cells & occupied:
                raise ValueError("%s: piece %s starts on a wall, a door, a content spot or another piece" % (arr["mapId"], piece["id"]))
            occupied |= cells
    check_walkable_together(arr["mapId"], width, height, blocked | occupied, set(door_cells), keep)
    return {"mapId": arr["mapId"], "area": list(area), "keep": [list(c) for c in keep], "pieces": pieces}, art


def derive_maps(spec):
    """Returns ({map id: map json}, {map id: art json}, [arrangeable section rows])."""
    doors = {d["id"]: (m["id"], d) for m in spec["maps"] for d in m["doors"]}
    content_spots = [d["position"] for d in spec["destinations"]] + [n["position"] for n in spec["npcs"]]
    content_spots += [spec["bed"]] + list(spec["starts"].values())
    maps, art, rooms = {}, {}, []
    for m in spec["maps"]:
        layout_path = CAMPUS_GRID / m["layout"] if "layout" in m else None
        arr = next((a for a in spec.get("arrangeable", []) if a["mapId"] == m["id"]), None)
        layout = None
        if layout_path is not None and layout_path.exists():
            layout = json.loads(layout_path.read_text(encoding="utf-8"))
            skip = movable_keys(arr)
            width, height, blocked = layout_cells(layout, skip)
            art[m["id"]] = {"width": width, "height": height, "layout": "CampusGrid/" + m["layout"], "minimap": layout_minimap(layout_path, layout, spec, m["id"], skip)}
        elif arr is not None:
            raise ValueError("%s is arrangeable, so it needs a CampusGrid layout" % m["id"])
        else:
            width, height, blocked = fallback_cells(m["fallback"], spec["fallbackLegend"])
            legend = {k: v["tile"] for k, v in spec["fallbackLegend"].items() if v["tile"]}
            art[m["id"]] = {"width": width, "height": height, "stage5": {"floor": m["fallback"]["floor"], "rows": m["fallback"]["rows"], "legend": legend},
                            "minimap": stage5_minimap(m["fallback"], legend)}
        door_cells = {tuple(d["cell"]) for d in m["doors"]}
        blocked -= door_cells
        if layout is not None:
            blocked |= target_footprints(spec, m["id"], layout, blocked)
        seeds = [cell_of(s) for s in content_spots if s["mapId"] == m["id"]]
        blocked |= seal_unreachable(width, height, blocked, door_cells, seeds)
        check_target_access(spec, m["id"], width, height, blocked)
        check_walkable_together(m["id"], width, height, blocked, door_cells, seeds)
        if arr is not None:
            room, pieces = resolve_arrangeable(arr, layout, layout_path, blocked, door_cells, seeds, width, height)
            rooms.append(room)
            art[m["id"]]["pieces"] = pieces
        portals = []
        for d in m["doors"]:
            to_map, to_door = doors[d["to"]]
            portals.append({"id": d["id"], "x": d["cell"][0] + 0.5, "y": d["cell"][1] + 0.5,
                            "to": {"mapId": to_map, "x": to_door["cell"][0] + 0.5, "y": to_door["cell"][1] + 0.5}, "open": True})
        maps[m["id"]] = {"id": m["id"], "name": m["name"], "width": width, "height": height,
                         "blocked": [[x, y] for y, x in sorted((y, x) for x, y in blocked)], "portals": portals}
    return maps, art, rooms


def check_spots(spec, maps):
    def passable(spot, what):
        m = maps.get(spot["mapId"])
        x, y = cell_of(spot)
        if m is None or not (0 <= x < m["width"] and 0 <= y < m["height"]) or [x, y] in m["blocked"]:
            raise ValueError("%s at %s %s,%s is not on a passable cell" % (what, spot["mapId"], spot["x"], spot["y"]))
    for d in spec["destinations"] + [d for ds in spec.get("variantDestinations", {}).values() for d in ds]:
        passable(d["position"], d["id"])
    for n in spec["npcs"]:
        passable(n["position"], n["id"])
    for t in spec["magicTargets"]:
        m = maps.get(t["mapId"])
        x, y = cell_of(t)
        if m is None or not (0 <= x < m["width"] and 0 <= y < m["height"]) or [x, y] not in m["blocked"]:
            raise ValueError("%s at %s %s,%s is not on a cell its sprite blocks" % (t["id"], t["mapId"], t["x"], t["y"]))
    for name, s in spec["starts"].items():
        passable(s, "start " + name)
    passable(spec["bed"], "bed")


# ------------------------------------------------------------------ surgical JSON writing

def compact(value):
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"))


def sections(text):
    """Top-level keys of a runner bundle as {key: (start line, end line)}; each value starts on a line `  "key":`."""
    lines = text.split("\n")
    starts = [(i, m.group(1)) for i, line in enumerate(lines) for m in [re.match(r'^  "([^"]+)":', line)] if m]
    close = max(i for i, line in enumerate(lines) if line.strip() == "}")
    spans = {}
    for k, (i, key) in enumerate(starts):
        spans[key] = (i, starts[k + 1][0] if k + 1 < len(starts) else close)
    return lines, spans


def read_section(lines, span):
    body = "\n".join(lines[span[0]:span[1]]).rstrip().rstrip(",")
    return json.loads("{" + body + "}")


def write_section(key, value, last):
    if isinstance(value, list) and value:
        text = '  "%s": [\n%s\n  ]' % (key, ",\n".join("    " + compact(v) for v in value))
    else:
        text = '  "%s": %s' % (key, compact(value))
    return text + ("" if last else ",")


def upsert(rows, updates, fields, defaults=None):
    """Copies fields of each update onto the row with its id; with defaults, an update with no row becomes a new row."""
    by_id = {r["id"]: r for r in rows}
    for u in updates:
        if u["id"] not in by_id and defaults is not None:
            by_id[u["id"]] = {"id": u["id"], **defaults}
            rows.append(by_id[u["id"]])
        if u["id"] in by_id:
            for f in fields:
                if f in u:
                    by_id[u["id"]][f] = u[f]
    return rows


def update_bundle(text, spec, maps, rooms, folder):
    lines, spans = sections(text)
    if "arrangeable" not in spans:
        lines.insert(spans["maps"][0], '  "arrangeable": [],')
        lines, spans = sections(chr(10).join(lines))
    order = sorted(spans, key=lambda k: spans[k][0])
    new = {}
    old_maps = read_section(lines, spans["maps"])["maps"]
    merged, seen = [], set()
    for m in old_maps:
        merged.append(maps.get(m["id"], m))
        if m["id"] in maps:
            seen.add(m["id"])
    insert_at = max(i for i, m in enumerate(merged) if m["id"] in seen) + 1 if seen else len(merged)
    for mid in [m["id"] for m in spec["maps"] if m["id"] not in seen]:
        merged.insert(insert_at, maps[mid])
        insert_at += 1
    new["maps"] = merged
    dests = spec["destinations"] + spec.get("variantDestinations", {}).get(folder, [])
    new["destinations"] = upsert(read_section(lines, spans["destinations"])["destinations"], dests, ("position", "facing"), {"position": None, "radius": 0.2, "facing": None})
    new["npcs"] = upsert(read_section(lines, spans["npcs"])["npcs"], spec["npcs"], ("position",))
    if "magicTargets" in spans:
        new["magicTargets"] = upsert(read_section(lines, spans["magicTargets"])["magicTargets"], spec["magicTargets"], ("mapId", "x", "y"))
    new["bed"] = spec["bed"]
    new["arrangeable"] = rooms
    for key in sorted(new, key=lambda k: spans[k][0], reverse=True):
        start, end = spans[key]
        lines[start:end] = [write_section(key, new[key], key == order[-1])]
    return "\n".join(lines)


def update_recipes(text, maps, bundle):
    recipes = json.loads(text)
    npcs = {n["id"]: n for n in bundle["npcs"]}
    home = next(d for d in bundle["destinations"] if d["id"] == "dest.courtyard_home")["position"]
    for r in recipes:
        portal = next(p for p in maps[r["incomingFromMapId"]]["portals"] if p["id"] == r["incomingPortalId"])
        r["arrival"] = dict(portal["to"])
        for j in r["join"]:
            j["joinAt"] = dict(portal["to"])
            j["entry"] = dict(npcs[j["actorId"]]["position"] if j["actorId"] in npcs else home)
    return json.dumps(recipes, ensure_ascii=False, indent=2) + "\n"


def update_start(text, start):
    def field(block, name, value):
        literal = json.dumps(value, ensure_ascii=False)
        return re.sub(r'("%s"\s*:\s*)("[^"]*"|-?[0-9.]+)' % name, lambda m: m.group(1) + literal, block, count=1)
    def player(m):
        block = m.group(0)
        for name in ("mapId", "x", "y", "facing"):
            block = field(block, name, start[name])
        return block
    return re.sub(r'"player"\s*:\s*\{[^{}]*\}', player, text, count=1)


def world_art(spec, art):
    """What the renderer reads: map looks, target sprites per tag state, glow and spell-effect tables."""
    glows, effects = spec["targetGlows"], spec["spellEffects"]
    looks = {}
    for t in spec["magicTargets"]:
        look = t["look"]
        for state in look["states"]:
            if not state["when"]:
                raise ValueError("%s has a look state with no tags" % t["id"])
            if "glow" in state and state["glow"] not in glows:
                raise ValueError("%s uses glow %s, which targetGlows does not define" % (t["id"], state["glow"]))
        looks[t["id"]] = look
    if "default" not in effects:
        raise ValueError("spellEffects needs a default")
    return {"maps": art, "targets": looks, "targetGlows": glows, "spellEffects": effects}


def preview(maps):
    out = []
    for m in maps.values():
        blocked = {tuple(b) for b in m["blocked"]}
        doors = {(int(p["x"]), int(p["y"])) for p in m["portals"]}
        out.append("%s %dx%d, %d blocked, portals %s" % (m["id"], m["width"], m["height"], len(blocked), ", ".join(p["id"] for p in m["portals"])))
        for y in range(m["height"] - 1, -1, -1):
            out.append("  %2d " % y + "".join("D" if (x, y) in doors else "#" if (x, y) in blocked else "." for x in range(m["width"])))
    return "\n".join(out)


def main():
    check = "--check" in sys.argv
    spec = json.loads((FIXTURES / "world" / "world.json").read_text(encoding="utf-8"))
    maps, art, rooms = derive_maps(spec)
    check_spots(spec, maps)
    check_campus_connected(maps)
    check_portals_pair(maps)
    outputs = {GENERATED / "world-art.json": json.dumps(world_art(spec, art), ensure_ascii=False, indent=2) + "\n"}
    for folder in spec["bundles"]:
        d = GENERATED / folder
        bundle_text = update_bundle((d / "runner-bundle.json").read_text(encoding="utf-8"), spec, maps, rooms, folder)
        outputs[d / "runner-bundle.json"] = bundle_text
        outputs[d / "staging-recipes.json"] = update_recipes((d / "staging-recipes.json").read_text(encoding="utf-8"), maps, json.loads(bundle_text))
        for start in sorted(d.glob("start-*.json")):
            outputs[start] = update_start(start.read_text(encoding="utf-8"), spec["starts"].get(start.name, spec["starts"]["*"]))
    changed = [p for p, t in outputs.items() if not p.exists() or p.read_text(encoding="utf-8") != t]
    print(preview(maps))
    for p in changed:
        print(("would change " if check else "wrote ") + str(p.relative_to(ROOT)))
        if not check:
            p.write_text(outputs[p], encoding="utf-8", newline="\n")
    if not changed:
        print("up to date")
    return 1 if check and changed else 0


if __name__ == "__main__":
    sys.exit(main())
