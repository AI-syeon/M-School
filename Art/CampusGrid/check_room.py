"""Verify the shipped room through its layout, tile definitions and placements."""

import argparse
import base64
from collections import deque
import hashlib
import io
import json
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser()
parser.add_argument("room_id", help="Room id, for example dorm or room-dorm")
args = parser.parse_args()
ROOM = args.room_id.removeprefix("room-")
assert ROOM.isalnum() or all(c.isalnum() or c == "-" for c in ROOM)
RUN = ROOT / "Art/CampusGrid/review" / ("room-" + ROOM)
OUTPUT = ROOT / "Assets/MagicCampus/CampusGrid/Rooms" / ("room-" + ROOM)
REVIEW = RUN


def hashes(folder):
    return {
        str(path.relative_to(folder)): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in sorted(folder.rglob("*"))
        if path.is_file() and path.name != "verification.json"
    }


def check_lounge():
    layout = json.loads((OUTPUT / "layout.json").read_text(encoding="utf-8"))
    assert (layout["width"], layout["height"], layout["tileSize"]) == (10, 8, 32)
    assert layout["camera"] == [0, 0, 10, 8]
    props = {prop["id"]: prop for prop in layout["props"]}
    blocked = set()
    collisions = []
    for placement in layout["placements"]:
        if placement.get("decorative", False):
            continue
        cells = {(placement["x"] + cell["x"], placement["y"] + cell["y"])
                 for cell in props[placement["id"]].get("blocked", [])}
        assert not blocked.intersection(cells), placement["id"]
        blocked.update(cells)
        collisions.append({"id": placement["id"], "cells": sorted(cells)})
    door = (4, 0)
    walls = {(x, y) for x in range(10) for y in range(8)
             if x in (0, 9) or y in (0, 7)} - {door}
    assert walls <= blocked
    assert door not in blocked
    assert all(0 <= x < 10 and 0 <= y < 8 for x, y in blocked)
    table = [p for p in layout["placements"] if p["id"] == "lounge-tea-table"]
    assert len(table) == 1
    table_cells = {(table[0]["x"] + c["x"], table[0]["y"] + c["y"])
                   for c in props["lounge-tea-table"]["blocked"]}
    assert table_cells == {(4, 3)}
    reachable = {door}
    queue = deque(reachable)
    while queue:
        x, y = queue.popleft()
        for cell in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
            if (0 <= cell[0] < 10 and 0 <= cell[1] < 8
                    and cell not in blocked and cell not in reachable):
                reachable.add(cell)
                queue.append(cell)
    targets = {"table_west": (3, 3), "table_east": (5, 3),
               "table_south": (4, 2), "table_north": (4, 4),
               "fireplace_front": (6, 4), "noticeboard_front": (3, 5)}
    assert set(targets.values()) <= reachable, targets
    walkable = {(x, y) for x in range(10) for y in range(8)} - blocked
    assert reachable == walkable, sorted(walkable - reachable)
    images = {}
    tiles = list(layout["tiles"])
    if "packedAtlas" in layout:
        spec = json.loads((OUTPUT / layout["packedAtlas"]).read_text(encoding="utf-8"))
        atlas = Image.open(io.BytesIO(base64.b64decode(spec["png"]))).convert("RGBA")
        for rect in spec["frames"]:
            images[rect["name"]] = atlas.crop((rect["x"], rect["y"],
                                              rect["x"] + rect["w"], rect["y"] + rect["h"]))
        tiles.extend(spec["tiles"])
    for tile in layout["tiles"]:
        for name in tile["frames"]:
            images[name] = Image.open(OUTPUT / "Textures" / name).convert("RGBA")
    lookup = {tile["id"]: tile for tile in tiles}
    assert len(layout["ground"]) == 80
    assert len({(c["x"], c["y"]) for c in layout["ground"]}) == 80
    assert all(image.size == (32, 32) for image in images.values())
    alpha = set()
    counts = []
    for image in images.values():
        alpha.update(image.getchannel("A").tobytes())
        counts.append(len(image.getcolors(maxcolors=1024)))
    assert alpha <= {0, 255}
    for cell in layout["ground"]:
        assert images[lookup[cell["tile"]]["frames"][0]].getchannel("A").getextrema() == (255, 255)
    assert Image.open(REVIEW / "courtyard.png").size == (320, 256)
    assert Image.open(REVIEW / "room-lounge-grid.png").size == (640, 512)
    assert (REVIEW / "room-lounge-vs-keyart.png").is_file()
    fire = Image.open(REVIEW / "fireplace.apng")
    assert 4 <= fire.n_frames <= 6
    durations = []
    for index in range(fire.n_frames):
        fire.seek(index)
        durations.append(fire.info.get("duration"))
    assert all(duration == 125 for duration in durations)
    result = {"status": "PASS", "mapPixels": [320, 256], "cellPixels": [32, 32],
              "outerWallCells": len(walls), "blockedCells": len(blocked),
              "walkableCells": len(walkable), "reachableCells": len(reachable),
              "door": door, "tableCollision": sorted(table_cells), "targets": targets,
              "alphaValues": sorted(alpha), "cellColorCountRange": [min(counts), max(counts)],
              "uniqueCellImages": len(images), "fireFrames": fire.n_frames,
              "fireFrameDurationsMs": durations, "collisions": collisions,
              "outputHashes": hashes(OUTPUT), "reviewHashes": hashes(REVIEW),
              "runtimeUnityCheck": "not run"}
    (REVIEW / "verification.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: v for k, v in result.items()
                      if k not in ("collisions", "outputHashes", "reviewHashes")}, indent=2))


def check():
    layout = json.loads((OUTPUT / "layout.json").read_text(encoding="utf-8"))
    assert (layout["width"], layout["height"], layout["tileSize"]) == (12, 9, 32)
    assert layout["camera"] == [0, 0, 12, 9]
    props = {prop["id"]: prop for prop in layout["props"]}
    blocked = set()
    collisions = []
    for placement in layout["placements"]:
        if placement.get("decorative", False):
            continue
        cells = {
            (placement["x"] + cell["x"], placement["y"] + cell["y"])
            for cell in props[placement["id"]].get("blocked", [])
        }
        assert not blocked.intersection(cells), placement["id"]
        blocked.update(cells)
        collisions.append({"id": placement["id"], "cells": sorted(cells)})
    assert (5, 0) not in blocked
    assert {(1, 5), (1, 6)} <= blocked
    assert (2, 5) not in blocked
    walls = {(x, y) for x in range(12) for y in range(9)
             if x in (0, 11) or y in (0, 8)} - {(5, 0)}
    assert walls <= blocked
    assert all(0 <= x < 12 and 0 <= y < 9 for x, y in blocked)
    reachable = {(5, 0)}
    queue = deque(reachable)
    while queue:
        x, y = queue.popleft()
        for cell in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
            if (0 <= cell[0] < 12 and 0 <= cell[1] < 9
                    and cell not in blocked and cell not in reachable):
                reachable.add(cell)
                queue.append(cell)
    targets = {"sleep": (2, 5), "desk_front": (7, 2), "wardrobe_front": (9, 4)}
    assert set(targets.values()) <= reachable
    walkable = {(x, y) for x in range(12) for y in range(9)} - blocked
    assert reachable == walkable, sorted(walkable - reachable)

    images = {}
    tiles = list(layout["tiles"])
    if "packedAtlas" in layout:
        atlas_spec = json.loads((OUTPUT / layout["packedAtlas"]).read_text(encoding="utf-8"))
        atlas = Image.open(io.BytesIO(base64.b64decode(atlas_spec["png"]))).convert("RGBA")
        for rect in atlas_spec["frames"]:
            images[rect["name"]] = atlas.crop((rect["x"], rect["y"],
                                              rect["x"] + rect["w"], rect["y"] + rect["h"]))
        tiles.extend(atlas_spec["tiles"])
    for tile in layout["tiles"]:
        for name in tile["frames"]:
            images[name] = Image.open(OUTPUT / "Textures" / name).convert("RGBA")
    tile_lookup = {tile["id"]: tile for tile in tiles}
    assert len(layout["ground"]) == 108
    assert len({(cell["x"], cell["y"]) for cell in layout["ground"]}) == 108
    assert all(image.size == (32, 32) for image in images.values())
    alpha_values = set()
    color_counts = []
    for image in images.values():
        alpha_values.update(image.getchannel("A").tobytes())
        color_counts.append(len(image.getcolors(maxcolors=1024)))
    assert alpha_values <= {0, 255}
    for cell in layout["ground"]:
        assert images[tile_lookup[cell["tile"]]["frames"][0]].getchannel("A").getextrema() == (255, 255)
    bed_placements = [p for p in layout["placements"] if "bed" in p["id"]]
    assert len(bed_placements) == 1
    bed = bed_placements[0]
    bed_blocked = {(bed["x"] + c["x"], bed["y"] + c["y"])
                   for c in props[bed["id"]]["blocked"]}
    assert bed_blocked == {(1, 5), (1, 6)}
    for placement in layout["placements"]:
        prop = props[placement["id"]]
        with Image.open(OUTPUT / prop["image"]) as sheet:
            for rect in prop["rectangles"]:
                dx = (2 - placement["x"]) * 32
                dy = rect["h"] - (6 - placement["y"]) * 32
                if 0 <= dx < rect["w"] and 0 <= dy < rect["h"]:
                    x, y = rect["x"] + dx, rect["y"] + dy
                    assert sheet.getchannel("A").crop((x, y, x + 32, y + 32)).getbbox() is None, (
                        "Sleep cell visually covered", placement["id"])
    required_reviews = {"room-dorm-vs-keyart.png", "room-dorm-grid.png"}
    assert all((REVIEW / name).is_file() for name in required_reviews)
    assert Image.open(REVIEW / "courtyard.png").size == (384, 288)
    assert Image.open(REVIEW / "room-dorm-grid.png").size == (768, 576)
    result = {
        "status": "PASS", "mapPixels": [384, 288], "cellPixels": [32, 32],
        "outerWallCells": len(walls), "blockedCells": len(blocked),
        "walkableCells": len(walkable), "reachableCells": len(reachable),
        "targets": targets, "door": [5, 0], "bedCollision": sorted(bed_blocked),
        "alphaValues": sorted(alpha_values), "cellColorCountRange": [min(color_counts), max(color_counts)],
        "uniqueCellImages": len(images), "collisions": collisions,
        "outputHashes": hashes(OUTPUT), "reviewHashes": hashes(REVIEW),
        "runtimeUnityCheck": "not run",
    }
    (RUN / "verification.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: v for k, v in result.items()
                      if k not in ("collisions", "outputHashes", "reviewHashes")}, indent=2))


if __name__ == "__main__":
    if ROOM == "lounge":
        check_lounge()
    else:
        check()
