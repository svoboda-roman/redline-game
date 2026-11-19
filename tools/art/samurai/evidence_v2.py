#!/usr/bin/env python3
"""v1-vs-v2 evidence for the samurai sheet, read back from the packed atlases (what the game loads).

python3 evidence_v2.py --v1 DIR --v2 DIR --out DIR [--prefix samurai-v2] [--v1-run 0] [--v2-run 0]
Writes <prefix>-compare.png (v1 | v2 at 6x and 1x/2x strips: idle 0, run contact, attack impact),
<prefix>-sheet.png (all v2 animations at 4x) and <prefix>-lit.png (v2 frames lit by a fake point
light through the normal map, two light positions) on #1b2230.
"""
import argparse, json, os, sys
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from pixelate import sheet, crop_rows, lit, label_font  # noqa: E402

BG = (0x1b, 0x22, 0x30, 255)


def load(d, name='samurai'):
    js = json.load(open(os.path.join(d, name + '.json')))
    tex = {k: np.asarray(Image.open(os.path.join(d, v)).convert('RGBA')) for k, v in js['textures'].items()}
    AH = js['atlas']['h']
    fr = {}
    for f in js['frames']:
        r = f['rect']
        y0 = AH - r['y'] - r['h']
        fr[(f['anim'], f['index'])] = {k: t[y0:y0 + r['h'], r['x']:r['x'] + r['w']] for k, t in tex.items()}
    return js, fr


def compose(cols, zoom, path, title):
    """cols: [(label, [frames])] stacked as rows; frames already cropped."""
    sheet(cols, zoom, BG, path, 120)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--v1', required=True)
    ap.add_argument('--v2', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--prefix', default='samurai-v2')
    ap.add_argument('--v1-run', type=int, default=0)
    ap.add_argument('--v2-run', type=int, default=0)
    ap.add_argument('--atk', type=int, default=3)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    j1, f1 = load(a.v1)
    j2, f2 = load(a.v2)
    pick1 = [('idle', 0), ('run', a.v1_run), ('attack', a.atk)]
    pick2 = [('idle', 0), ('run', a.v2_run), ('attack', a.atk)]
    rows = crop_rows([('v1', [f1[k]['color'] for k in pick1]), ('v2', [f2[k]['color'] for k in pick2])])
    big = os.path.join(a.out, '_cmp6.png')
    sheet(rows, 6, BG, big, 120)
    s1 = os.path.join(a.out, '_cmp1.png')
    s2 = os.path.join(a.out, '_cmp2.png')
    sheet(rows, 1, BG, s1, 30)
    sheet(rows, 2, BG, s2, 40)
    im6, im1, im2 = Image.open(big), Image.open(s1), Image.open(s2)
    W = max(im6.width, im1.width + im2.width + 40)
    out = Image.new('RGB', (W, im6.height + max(im1.height, im2.height) + 40), BG[:3])
    out.paste(im6, (0, 0))
    out.paste(im1, (10, im6.height + 30))
    out.paste(im2, (im1.width + 40, im6.height + 30))
    d = ImageDraw.Draw(out)
    d.text((10, im6.height + 6), '1x and 2x (same frames)', fill=(200, 210, 225), font=label_font(18))
    out.save(os.path.join(a.out, a.prefix + '-compare.png'))
    for p in (big, s1, s2):
        os.remove(p)
    anims = [an['name'] for an in j2['animations']]
    rows = [(n, [f2[(n, i)]['color'] for i in range(len(an['frames']))]) for n, an in zip(anims, j2['animations'])]
    sheet(rows, 4, BG, os.path.join(a.out, a.prefix + '-sheet.png'))
    cw, ch = j2['cell']['w'], j2['cell']['h']
    litrows = []
    for lab, lp in (('light front-up', (cw * 0.85, ch * 0.1, 45.0)), ('light back-up', (cw * 0.15, ch * 0.1, 45.0))):
        litrows.append((lab, [lit(f2[k]['color'], f2[k]['normal'], lp) for k in pick2]))
    litrows.append(('v1 back-up', [lit(f1[k]['color'], f1[k]['normal'], (cw * 0.15, ch * 0.1, 45.0)) for k in pick1]))
    sheet(crop_rows(litrows), 5, (10, 12, 18, 255), os.path.join(a.out, a.prefix + '-lit.png'), 160)
    print('evidence', a.out)


if __name__ == '__main__':
    main()
