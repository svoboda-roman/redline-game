#!/usr/bin/env python3
"""Side-by-side evidence: our in-game capture (left) next to a Replaced reference frame (right).

Usage: python3 -I tools/art/surfaces_vs_ref.py <ours.png> <ref.jpg> <out.png> [label]
Crops the black letterbox rows from both, scales each to 960 px wide, labels them, stacks them horizontally.
"""
import sys
from PIL import Image, ImageDraw


def crop_letterbox(im, thresh=12):
    g = im.convert("L")
    w, h = g.size
    px = g.load()
    rows = [y for y in range(h) if max(px[x, y] for x in range(0, w, 8)) > thresh]
    cols = [x for x in range(w) if max(px[x, y] for y in range(0, h, 8)) > thresh]
    if not rows or not cols:
        return im
    return im.crop((min(cols), min(rows), max(cols) + 1, max(rows) + 1))


def main():
    ours, ref, out = sys.argv[1:4]
    label = sys.argv[4] if len(sys.argv) > 4 else "ours"
    a = crop_letterbox(Image.open(ours).convert("RGB"))
    b = crop_letterbox(Image.open(ref).convert("RGB"))
    W = 960
    a = a.resize((W, round(a.height * W / a.width)), Image.NEAREST)
    b = b.resize((W, round(b.height * W / b.width)), Image.LANCZOS)
    H = max(a.height, b.height) + 20
    canvas = Image.new("RGB", (W * 2 + 8, H), (0, 0, 0))
    canvas.paste(a, (0, 0))
    canvas.paste(b, (W + 8, 0))
    d = ImageDraw.Draw(canvas)
    d.text((4, H - 16), "Redline " + label + " (ours)", fill=(255, 220, 120))
    d.text((W + 12, H - 16), "REPLACED " + ref.split("/")[-1] + " (ref)", fill=(255, 220, 120))
    canvas.save(out)


if __name__ == "__main__":
    main()
