#!/usr/bin/env python3
"""Evidence sheets for the enemy sprites, read back from the packed atlases + json (what the game loads).

python3 enemy_evidence.py [--chars DIR] [--out DIR]
  enemies-sheet.png   every enemy animation at 4x, one labelled row per animation, on #1b2230
  enemies-lineup.png  samurai idle_00 + one idle frame of each enemy at 4x on a shared ground line (scale check)
  enemies-1x.png      the same lineup at 1x and 2x (readability check)
"""
import argparse, json, os, sys
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..', 'samurai'))
from pixelate import label_font  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
BG = (0x1b, 0x22, 0x30, 255)
ENEMIES = [('grunt', 'Grunt', 'idle'), ('brute', 'Brute', 'idle'), ('drone', 'Drone', 'hover'), ('shooter', 'Shooter', 'idle'),
           ('shield', 'Shield', 'idle'), ('kamikaze', 'Kamikaze', 'idle'), ('summoner', 'Summoner', 'idle')]


def load(chars, d, name):
    doc = json.load(open(os.path.join(chars, d, name + '.json')))
    img = np.asarray(Image.open(os.path.join(chars, d, doc['textures']['color'])).convert('RGBA'))
    H = img.shape[0]
    fr = {}
    for f in doc['frames']:
        r = f['rect']
        fr[(f['anim'], f['index'])] = (img[H - r['y'] - r['h']:H - r['y'], r['x']:r['x'] + r['w']], f['pivot_px'])
    return doc, fr


def bbox(frames):
    a = np.zeros(frames[0].shape[:2], bool)
    for f in frames:
        a |= f[..., 3] > 0
    ys, xs = np.nonzero(a)
    return max(ys.min() - 2, 0), ys.max() + 3, max(xs.min() - 2, 0), xs.max() + 3


def sheet(chars, out, z=4):
    font = label_font(22)
    blocks = []
    for name, d, _ in ENEMIES:
        doc, fr = load(chars, d, name)
        allf = [v[0] for v in fr.values()]
        y0, y1, x0, x1 = bbox(allf)
        ch, cw = (y1 - y0) * z, (x1 - x0) * z
        cols = max(len(a['frames']) for a in doc['animations'])
        lw, gap = 230, 8
        im = Image.new('RGBA', (lw + cols * (cw + gap), len(doc['animations']) * (ch + gap) + gap), BG)
        dr = ImageDraw.Draw(im)
        for r, an in enumerate(doc['animations']):
            y = gap + r * (ch + gap)
            dr.text((8, y + ch // 2 - 11), '%s / %s' % (name, an['name']), fill=(200, 210, 225, 255), font=font)
            for c in range(len(an['frames'])):
                f = fr[(an['name'], c)][0][y0:y1, x0:x1]
                im.alpha_composite(Image.fromarray(f).resize((cw, ch), Image.NEAREST), (lw + c * (cw + gap), y))
        blocks.append(im)
    W = max(b.size[0] for b in blocks)
    H = sum(b.size[1] for b in blocks) + 6 * (len(blocks) - 1)
    out_im = Image.new('RGBA', (W, H), BG)
    dr = ImageDraw.Draw(out_im)
    y = 0
    for b in blocks:
        out_im.alpha_composite(b, (0, y))
        y += b.size[1]
        dr.rectangle((0, y + 2, W, y + 3), fill=(60, 70, 90, 255))
        y += 6
    out_im.convert('RGB').save(os.path.join(out, 'enemies-sheet.png'))


def lineup(chars, out, zs=(4,), fname='enemies-lineup.png', labels=True):
    items = [('samurai', 'Samurai', 'idle')] + ENEMIES
    frames = []
    for name, d, an in items:
        doc, fr = load(chars, d, name)
        f, piv = fr[(an, 0)]
        y0, y1, x0, x1 = bbox([f])
        frames.append((name, f[y0:y1, x0:x1], piv['y'] - (f.shape[0] - y1), doc))
    rows = []
    for z in zs:
        font = label_font(max(12, 5 * z))
        up = max(f.shape[0] - b for _, f, b, _ in frames)  # tallest above the pivot line
        down = max(b for _, f, b, _ in frames) + 2
        W = sum(f.shape[1] for _, f, _, _ in frames) * z + 12 * z * (len(frames) + 1)
        Hh = (up + down + 6) * z + (40 if labels else 0)
        im = Image.new('RGBA', (W, Hh), BG)
        dr = ImageDraw.Draw(im)
        ground = 3 * z + up * z
        dr.line((0, ground, W, ground), fill=(60, 70, 90, 255), width=1)
        x = 12 * z
        for name, f, b, doc in frames:
            h, w = f.shape[:2]
            # feet pivot rows sit on the ground line; the drone (centre pivot) floats at its pivot
            top = ground - (h - b) * z
            im.alpha_composite(Image.fromarray(f).resize((w * z, h * z), Image.NEAREST), (x, top))
            if labels:
                dr.text((x, Hh - 34), name, fill=(200, 210, 225, 255), font=font)
            x += w * z + 12 * z
        rows.append(im)
    W = max(r.size[0] for r in rows)
    im = Image.new('RGBA', (W, sum(r.size[1] for r in rows)), BG)
    y = 0
    for r in rows:
        im.alpha_composite(r, (0, y))
        y += r.size[1]
    im.convert('RGB').save(os.path.join(out, fname))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--chars', default=os.path.join(REPO, 'Assets', 'Resources', 'Art', 'Characters'))
    ap.add_argument('--out', default=os.path.join(REPO, 'production', 'qa', 'evidence'))
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    sheet(a.chars, a.out)
    lineup(a.chars, a.out)
    lineup(a.chars, a.out, zs=(1, 2), fname='enemies-1x.png')
    print('evidence written to', a.out)


if __name__ == '__main__':
    main()
