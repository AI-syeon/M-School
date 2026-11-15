"""Build the courtyard foliage-shadow mask with the local pixel pipeline Python."""

import random

from PIL import Image
from pixel_pipeline.cli import check_image

from build_grid import ROOT, TILE, read_recipe, write_png


def noise_grid(randomizer, resolution, size):
    noise = Image.new("L", (resolution, resolution))
    noise.putdata([randomizer.randrange(256) for _ in range(resolution**2)])
    return noise.resize(size, Image.Resampling.BILINEAR)


def build_canopy_mask():
    garden = read_recipe(ROOT / "garden.json")
    tree = next(prop for prop in garden if prop["id"] == "autumn-tree")["frames"][0]
    left, top, width, _ = tree["crop"]
    with Image.open(ROOT / tree["source"]) as image:
        # Keep the crown and its inner branches above the exposed trunk.
        crown = image.crop((left, top, left + width, top + 433)).convert("RGBA")

    size = (4 * TILE, 4 * TILE)
    padding = 4
    inset = (size[0] - padding * 2, size[1] - padding * 2)
    crown = crown.resize(inset, Image.Resampling.BOX)
    randomizer = random.Random(6141)
    clusters = noise_grid(randomizer, 26, inset)
    fine_detail = noise_grid(randomizer, 70, inset)
    alpha = Image.new("L", inset)
    values = []
    for index, ((red, green, blue, coverage), cluster, detail) in enumerate(
        zip(crown.get_flattened_data(), clusters.get_flattened_data(), fine_detail.get_flattened_data())
    ):
        # Leaf color preserves the ragged lower edge and lets exposed wood fade out.
        y = index // inset[0]
        foliage = max(0.0, min(1.0, (red - blue - 20) / 75))
        lower_edge = max(0.0, min(1.0, (y / inset[1] - 0.73) / 0.27))
        density = 0.76 * cluster + 0.24 * detail
        dapple = max(0.0, min(1.0, (density - 99) / 63))
        opacity = coverage * (1 - lower_edge * (1 - foliage)) * dapple
        values.append(round(opacity * 6 / 255) * 255 // 6)
    alpha.putdata(values)

    mask = Image.new("RGBA", size, (255, 255, 255, 0))
    channel = mask.getchannel("A")
    channel.paste(alpha, (padding, padding))
    mask.putalpha(channel)
    check_image(mask, size, True)
    write_png(ROOT / "source/autumn-canopy-mask.png", mask)


if __name__ == "__main__":
    build_canopy_mask()
