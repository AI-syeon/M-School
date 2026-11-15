"""Pack complete equal-sized RGBA frames and their normals in row-major order."""

from math import ceil, isqrt

from PIL import Image
from pixel_pipeline.normal import normal_map


def pack_frames(frames, columns=None, normals=True, max_size=8192):
    if not frames:
        raise ValueError("A sprite sheet needs at least one frame")
    width, height = frames[0].size
    if not (0 < width <= max_size and 0 < height <= max_size):
        raise ValueError("Frame exceeds the texture size limit")
    if any(frame.mode != "RGBA" or frame.size != (width, height) for frame in frames):
        raise ValueError("All sprite frames must be RGBA and the same size")
    if columns is None:
        # Keep rectangular frames in a compact texture as their count grows.
        preferred = 1 + isqrt((len(frames) * height - 1) // width)
        minimum = ceil(len(frames) / (max_size // height))
        columns = min(len(frames), max_size // width, max(minimum, preferred))
    if type(columns) is not int or not 1 <= columns <= len(frames):
        raise ValueError("columns must be between 1 and the number of frames")
    size = (width * columns, height * ceil(len(frames) / columns))
    if max(size) > max_size:
        raise ValueError("Sprite grid exceeds the texture size limit")
    sheet = Image.new("RGBA", size)
    normal_sheet = Image.new("RGBA", size) if normals else None
    rects = []
    for i, frame in enumerate(frames):
        x, y = (i % columns) * width, (i // columns) * height
        sheet.paste(frame, (x, y))
        if normal_sheet is not None:
            normal_sheet.paste(normal_map(frame), (x, y))
        rects.append({"x": x, "y": y, "w": width, "h": height})
    return sheet, normal_sheet, rects
