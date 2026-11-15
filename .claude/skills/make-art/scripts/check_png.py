"""Checks PNG assets against MagicCampus 32x32 grid rules.

Usage: python -I check_png.py --kind prop|ground|any <png or folder>...
  prop   : RGBA, width/height multiples of 32, all edge pixels fully transparent
  ground : RGBA, exactly 32x32, fully opaque
  any    : RGBA and multiples of 32 only
  ui     : RGBA only (UI parts use their own sizes from the manifest)
Prints one JSON line per file and exits 1 if any file fails.
"""
import argparse
import json
import sys
from pathlib import Path

from PIL import Image


def check(path, kind):
    img = Image.open(path)
    info = {"file": str(path), "size": list(img.size), "mode": img.mode, "problems": []}
    if img.mode != "RGBA":
        info["problems"].append("not RGBA")
        img = img.convert("RGBA")
    w, h = img.size
    if kind != "ui" and (w % 32 or h % 32):
        info["problems"].append("size not a multiple of 32")
    alpha = img.getchannel("A")
    lo, hi = alpha.getextrema()
    colors = img.getcolors(maxcolors=1 << 16)
    info["opaqueColors"] = None if colors is None else len([c for n, c in colors if c[3] > 0])
    if kind == "ground":
        if (w, h) != (32, 32):
            info["problems"].append("ground tile must be 32x32")
        if lo != 255:
            info["problems"].append("ground tile must be fully opaque")
    if kind == "prop":
        px = alpha.load()
        edge = [px[x, 0] for x in range(w)] + [px[x, h - 1] for x in range(w)]
        edge += [px[0, y] for y in range(h)] + [px[w - 1, y] for y in range(h)]
        if max(edge) != 0:
            info["problems"].append("edge pixels not fully transparent")
        if hi == 0:
            info["problems"].append("image is empty")
        semi = sum(alpha.histogram()[1:255])
        info["semiTransparentPixels"] = semi
    info["ok"] = not info["problems"]
    return info


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--kind", choices=["prop", "ground", "any", "ui"], default="any")
    ap.add_argument("paths", nargs="+")
    args = ap.parse_args()
    files = []
    for p in map(Path, args.paths):
        files.extend(sorted(p.glob("*.png")) if p.is_dir() else [p])
    ok = True
    sys.stdout.reconfigure(encoding="utf-8")
    for f in files:
        r = check(f, args.kind)
        ok &= r["ok"]
        print(json.dumps(r, ensure_ascii=False))
    if not files:
        print("no png files")
        ok = False
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
