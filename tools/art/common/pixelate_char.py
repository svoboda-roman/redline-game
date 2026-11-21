#!/usr/bin/env python3
"""Pixelate a character's renders and pack color/normal/emissive atlases + json (samurai format).

python3 pixelate_char.py --render DIR --out ASSET_DIR --name grunt [--style enemy_style.json] [--preview DIR]

The per-pixel reduction is tools/art/samurai/pixelate.py's process() unchanged (imported, not copied),
so enemies get exactly the samurai's majority downscale, thin-feature priority, hard alpha, toon
bands, despeckle, rim and selective outline. Only the packing differs: the json notes (pivot,
emissive encoding) come from the character's render manifest instead of being samurai-specific.
"""
import argparse, json, os, sys
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..', 'samurai'))
from pixelate import Style, process, sheet, crop_rows  # noqa: E402


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--render', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--name', required=True)
    ap.add_argument('--style', default=os.path.join(HERE, 'enemy_style.json'))
    ap.add_argument('--preview')
    ap.add_argument('--pad', type=int, default=2)
    a = ap.parse_args()
    st = Style(a.style)
    man = json.load(open(os.path.join(a.render, 'manifest.json')))
    S, cw, chh, padb = man['scale'], man['cell_w'], man['cell_h'], man['pad_bottom']
    meta = man['meta']
    anims = man['anims']
    res = {}
    for an in anims:
        for i in range(an['count']):
            base = os.path.join(a.render, '%s_%02d' % (an['name'], i))
            res[(an['name'], i)] = c = process(base + '_id.png', base + '_n.png', S, st)
            al = c[0][..., 3]
            if al[0].any() or al[-1].any() or al[:, 0].any() or al[:, -1].any():
                print('WARNING: %s %s_%02d touches the cell edge (clipped)' % (a.name, an['name'], i))
    cols = max(an['count'] for an in anims)
    p = a.pad
    AW, AH = p + cols * (cw + p), p + len(anims) * (chh + p)
    atl = [np.zeros((AH, AW, 4), np.uint8) for _ in range(3)]
    atl[1][..., :3] = (128, 128, 255)
    frames, metas = [], []
    for r, an in enumerate(anims):
        names = []
        for i in range(an['count']):
            x, ytop = p + i * (cw + p), p + r * (chh + p)
            for k in range(3):
                atl[k][ytop:ytop + chh, x:x + cw] = res[(an['name'], i)][k]
            nm = '%s_%s_%02d' % (a.name, an['name'], i)
            names.append(nm)
            frames.append(dict(name=nm, anim=an['name'], index=i,
                               rect=dict(x=x, y=AH - ytop - chh, w=cw, h=chh),
                               pivot=dict(x=0.5, y=round(padb / chh, 6)), pivot_px=dict(x=cw // 2, y=padb)))
        metas.append(dict(name=an['name'], fps=an['fps'], loop=an['loop'], frames=names))
    os.makedirs(a.out, exist_ok=True)
    for k, suf in enumerate(('color', 'normal', 'emissive')):
        Image.fromarray(atl[k]).save(os.path.join(a.out, '%s_%s.png' % (a.name, suf)), optimize=True)
    used = {tuple(int(v) for v in c) for c in atl[0][atl[0][..., 3] > 0][:, :3]}
    doc = dict(
        name=a.name, ppu=man['ppu'], cell=dict(w=cw, h=chh), atlas=dict(w=AW, h=AH), padding=p,
        rect_origin='bottom-left (Unity sprite rect convention); pivot normalised within the rect',
        pivot_note=meta['pivot_note'],
        textures=dict(color='%s_color.png' % a.name, normal='%s_normal.png' % a.name, emissive='%s_emissive.png' % a.name),
        normal_encoding='tangent space in the sprite plane: R=+X right, G=+Y up, B=toward camera; transparent texels (128,128,255,0)',
        emissive_encoding=meta['emissive_encoding'],
        palette=sorted('#%02x%02x%02x' % c for c in used),
        animations=metas, frames=frames)
    json.dump(doc, open(os.path.join(a.out, '%s.json' % a.name), 'w'), indent=1)
    print('%s: atlas %dx%d cell %dx%d frames %d palette used %d (style %d)' %
          (a.name, AW, AH, cw, chh, len(frames), len(used), len(st.palette())))
    if a.preview:
        os.makedirs(a.preview, exist_ok=True)
        rows = [(an['name'], [res[(an['name'], i)][0] for i in range(an['count'])]) for an in anims]
        cr = crop_rows(rows)
        sheet(cr, 3, (0x1b, 0x22, 0x30, 255), os.path.join(a.preview, '%s_prev.png' % a.name), 110)


if __name__ == '__main__':
    main()
