"""Verify the shipped room through its layout, tile definitions and placements."""

import base64
from collections import deque
import hashlib
import io
import json
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[3]
RUN = Path(__file__).resolve().parent
OUTPUT = ROOT / "Assets/MagicCampus/CampusGrid/Rooms/room-dorm"
REVIEW = ROOT / "Art/CampusGrid/review/room-dorm"


def hashes(folder):
    return {
        str(path.relative_to(folder)): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in sorted(folder.rglob("*")) if path.is_file()
    }


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
    for cell in props[bed["id"]]["cells"]:
        if (bed["x"] + cell["x"], bed["y"] + cell["y"]) == (2, 5):
            assert images[tile_lookup[cell["tile"]]["frames"][0]].getbbox() is None
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
    check()
