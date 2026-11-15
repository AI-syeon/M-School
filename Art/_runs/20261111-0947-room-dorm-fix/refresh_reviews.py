"""Refresh fixed-room comparisons from the assembled builder output."""
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageOps

ROOT = Path(__file__).resolve().parents[3]
RUN = Path(__file__).resolve().parent
REVIEW = ROOT / "Art/CampusGrid/review/room-dorm"
room = Image.open(REVIEW / "courtyard.png").convert("RGBA")
room.save(REVIEW / "room-dorm.png")
room.resize((1152, 864), Image.Resampling.NEAREST).save(REVIEW / "room-dorm-3x.png")
layout = json.loads((ROOT / "Assets/MagicCampus/CampusGrid/Rooms/room-dorm/layout.json").read_text())
props = {p["id"]: p for p in layout["props"]}
blocked = set()
for placement in layout["placements"]:
    if not placement.get("decorative"):
        blocked.update((placement["x"] + c["x"], placement["y"] + c["y"])
                       for c in props[placement["id"]]["blocked"])
grid = room.resize((768, 576), Image.Resampling.NEAREST)
overlay = Image.new("RGBA", grid.size)
draw = ImageDraw.Draw(overlay)
for y in range(9):
    for x in range(12):
        left, top = x * 64, (8 - y) * 64
        color = (227, 89, 89, 74) if (x, y) in blocked else None
        if (x, y) == (5, 0):
            color = (75, 241, 140, 105)
        if (x, y) == (2, 5):
            color = (75, 181, 255, 105)
        if color:
            draw.rectangle((left, top, left + 63, top + 63), fill=color)
grid.alpha_composite(overlay)
draw = ImageDraw.Draw(grid)
for y in range(9):
    for x in range(12):
        left, top = x * 64, (8 - y) * 64
        color = "#e35959" if (x, y) in blocked else "white"
        if (x, y) == (5, 0):
            color = "#4bf18c"
        if (x, y) == (2, 5):
            color = "#4bb5ff"
        draw.rectangle((left, top, left + 63, top + 63), outline=color, width=2)
        draw.text((left + 3, top + 3), f"{x},{y}", fill=color,
                  stroke_width=1, stroke_fill="#30231b")
grid.save(REVIEW / "room-dorm-grid.png")


def comparison(left, right, labels, path):
    canvas = Image.new("RGBA", (left.width + right.width, max(left.height, right.height) + 32), "#211b17")
    canvas.alpha_composite(left, (0, 32))
    canvas.alpha_composite(right, (left.width, 32))
    d = ImageDraw.Draw(canvas)
    d.text((10, 10), labels[0], fill="white")
    d.text((left.width + 10, 10), labels[1], fill="white")
    canvas.save(path)


enlarged = room.resize((768, 576), Image.Resampling.NEAREST)
key = ImageOps.contain(Image.open(ROOT / "design/screens/06-room.png").convert("RGBA"),
                       (1024, 576), Image.Resampling.LANCZOS)
comparison(key, enlarged, ("KEYART 06-room", "ROOM DORM - 32px tiles at 2x"),
           REVIEW / "room-dorm-vs-keyart.png")
before = Image.open(RUN / "before.png").convert("RGBA").resize((768, 576), Image.Resampling.NEAREST)
comparison(before, enlarged, ("BEFORE", "AFTER"), REVIEW / "room-dorm-before-after.png")
