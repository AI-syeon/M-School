"""Prepare the fountain's water loop from the saved ImageGen source."""

from math import hypot

from PIL import Image
from pixel_pipeline.cli import check_image

from build_grid import ROOT, TILE, read_recipe, source_region, write_png


def is_water(pixel):
    red, green, blue, alpha = pixel
    return alpha == 255 and green >= red + 12 and blue >= red + 12


def moving_light(x, y, frame, cycle, jets):
    # Phase travels down the existing narrow streams, then out along the pool surface.
    amount = 0.0
    for jet in jets:
        distance = abs(x - jet["x"])
        start, end = jet["startY"], jet["endY"]
        if distance <= 1 and start <= y <= end:
            phase = (y - start - frame + jet["phase"]) % cycle
            amount += {0: 0.46, 1: 0.22, 4: -0.09, 5: -0.09}.get(phase, 0)
        ripple = hypot(distance / 2, y - end)
        if y >= end - 1 and 1 <= ripple <= 4:
            age = (frame - jet["phase"]) % cycle
            if abs(ripple - (1 + age * 0.55)) < 0.55:
                amount += 0.28 - age * 0.032
    return amount


def build_fountain():
    recipe = read_recipe(ROOT / "art.json")
    art = next(prop for prop in recipe["props"] if prop["id"] == "fountain")
    motion = read_recipe(ROOT / "fountain.json")
    size = tuple(cells * TILE for cells in art["size"])
    source = {**art["comparisonSource"], "colors": motion["colors"]}
    sprite, _ = source_region(ROOT, source, tuple(motion["fitSize"]), {})
    base = Image.new("RGBA", size)
    base.alpha_composite(sprite, tuple(motion["offset"]))
    water = [(x, y, base.getpixel((x, y)))
             for y in range(motion["waterTop"], size[1])
             for x in range(size[0]) if is_water(base.getpixel((x, y)))]

    images = []
    for index, entry in enumerate(art["frames"]):
        frame = base.copy()
        for x, y, pixel in water:
            amount = moving_light(x, y, index, len(art["frames"]), motion["jets"])
            target = (201, 247, 255) if amount > 0 else (11, 73, 99)
            rgb = tuple(round(value + (light - value) * abs(amount))
                        for value, light in zip(pixel[:3], target))
            frame.putpixel((x, y), (*rgb, 255))
        check_image(frame, size, True)
        images.append((ROOT / entry["source"], frame))

    if len({frame.tobytes() for _, frame in images}) != len(images):
        raise ValueError("Fountain animation should contain distinct frames")
    for path, frame in images:
        write_png(path, frame)


if __name__ == "__main__":
    build_fountain()
