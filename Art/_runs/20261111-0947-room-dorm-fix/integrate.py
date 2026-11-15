"""Record the authored enrichment placements in permanent room recipes."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
ART = ROOT / "Art/CampusGrid"
metadata = json.loads((Path(__file__).parent / "art-decor-metadata.json").read_text(encoding="utf-8-sig"))
interior = json.loads((ART / "interior.json").read_text(encoding="utf-8-sig"))
lookup = {p["id"]: p for p in interior["props"]}
bed = lookup["canopy-bed"]
bed.update(size=[2, 3], frames=[{"source": "prepared/room-dorm/canopy-bed-rich.png"}],
           blocked=[[1, 0], [1, 1]])
bed["comparisonSource"] = {"source": metadata["source"], "crop": metadata["assets"]["canopy-bed-rich"]["crop"]}
lookup["wall-rear"].update(size=[12, 2], blocked=[[x, 1] for x in range(12)])
for name, size, blocked in [
    ("moon-banner", [1, 2], []),
    ("trailing-ivy", [1, 2], []),
    ("star-garland", [2, 1], []),
    ("curtain-pair", [3, 2], []),
    ("green-runner", [1, 4], []),
    ("oak-chest", [2, 1], [[0, 0], [1, 0]]),
    ("star-stool", [1, 1], [[0, 0]]),
    ("stacked-books", [1, 1], [[0, 0]]),
    ("brass-lamp", [1, 1], []),
    ("sleeping-cat", [1, 1], []),
]:
    spec = {"id": name, "size": size, "blocked": blocked,
            "frames": [{"source": "prepared/room-dorm/" + name + ".png"}],
            "comparisonSource": {"source": metadata["source"], "crop": metadata["assets"][name]["crop"]}}
    if name in lookup:
        lookup[name].update(spec)
    else:
        interior["props"].append(spec)
mapdata = json.loads((ART / "room-dorm-map.json").read_text(encoding="utf-8-sig"))
mapdata.pop("groundImage", None)
mapdata.update(legend={"f": "room-floor"}, rows=["f" * 12 for _ in range(9)])
for p in mapdata["props"]:
    if p["id"] == "wall-rear":
        p["at"] = [0, 7]
    if p["id"] == "canopy-bed":
        p["at"] = [0, 5]
    if p["id"] == "star-rug":
        p["at"] = [2, 1]
    if p["id"] == "candle":
        p["at"] = [2, 3]
mapdata["props"] += [
    {"id": "green-runner", "at": [5, 0], "decorative": True, "order": 4},
    {"id": "moon-banner", "at": [2, 6], "decorative": True, "order": 850},
    {"id": "star-garland", "at": [0, 8], "decorative": True, "order": 920},
    {"id": "trailing-ivy", "at": [4, 7], "decorative": True, "order": 910},
    {"id": "trailing-ivy", "at": [10, 7], "decorative": True, "order": 910},
    {"id": "curtain-pair", "at": [5, 7], "decorative": True, "order": 905},
    {"id": "oak-chest", "at": [2, 3]},
    {"id": "star-stool", "at": [8, 1]},
    {"id": "stacked-books", "at": [2, 1]},
    {"id": "brass-lamp", "at": [1, 4], "decorative": True, "order": 1000},
    {"id": "sleeping-cat", "at": [7, 6], "decorative": True, "order": 920},
    {"id": "candle", "at": [8, 1], "decorative": True, "order": 1000},
    {"id": "plant", "at": [9, 1], "decorative": False},
]
mapdata["glows"] = [
    {"x": 3.5, "y": 7.5, "radius": 0.9, "power": 0.55},
    {"x": 9.5, "y": 7.5, "radius": 0.9, "power": 0.55},
    {"x": 1.5, "y": 4.6, "radius": 0.8, "power": 0.55},
    {"x": 7, "y": 7.5, "radius": 2.0, "power": 0.35},
    {"x": 2.5, "y": 3.6, "radius": 0.5, "power": 0.35},
    {"x": 8.5, "y": 1.6, "radius": 0.6, "power": 0.4},
]
art = json.loads((ART / "room-dorm-art.json").read_text(encoding="utf-8-sig"))
art["tiles"] = [{"id": "room-floor", "source": "prepared/room-dorm/oak-floor-native.png"}]
for name, data in [("interior.json", interior), ("room-dorm-map.json", mapdata), ("room-dorm-art.json", art)]:
    (ART / name).write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
