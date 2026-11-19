#!/usr/bin/env python3
"""Pixelate the prop renders and pack the prop atlas: props_color/_normal/_emissive.png + props.json.

python3 pack_props.py --render DIR --out ASSET_DIR [--style props_style.json] [--sheet PNG] [--zoom 4]

The per-pixel reduction is tools/art/samurai/pixelate.py's process() unchanged (imported, not copied):
majority downscale, thin-feature priority, hard alpha, toon bands from one shared palette, despeckle,
rim and the selective darkened outline, exactly as for the characters.

Outputs (rect origin bottom-left, Unity convention):
  props_color.png     sRGB albedo (lit sprite)
  props_normal.png    tangent-space normal, transparent texels (128,128,255,0)
  props_emissive.png  LINEAR-encoded emissive colour (the importer turns sRGB off for *_emissive), A = 255 on lit
                      texels; draw it unlit on top of the lit sprite, same rect and pivot.
  props.json          per prop: rect, pivot (normalised; bottom-centre for 'floor', top-centre for 'ceiling'),
                      layer hint, anchor, emissive texel count, centroid (world units from the pivot) and colour.
"""
import argparse, json, os, sys
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..', 'samurai'))
from pixelate import Style, process, label_font  # noqa: E402


def srgb_to_linear(c):
    c = c / 255.0
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def hexf(h):
    h = h.lstrip('#')
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], float)


def finish(c, e, name, sty):
    """Final-polish passes on one pixelated prop (colour only; emissive is derived from colour later).

    panel_fx: the billboard panel (emissive colour panel_fx.color) is dimmed to `base`, every third row
    to `scan`, vertical seams every `seam_every` px to `seam`, and a few hashed "text" bars to `text`,
    so the sign reads as a dim scanlined screen rather than a flat glaring slab.
    sky_rim: a 1-px cool rim (blend toward sky_rim.color by `mix`) on the first non-outline pixel
    under/right of the top and left silhouette outline, i.e. sky glow catching the top/left edges.
    """
    c = c.copy()
    pf = sty.get('panel_fx')
    if pf:
        col = hexf(pf['color']).astype(np.uint8)
        pm = (c[..., 3] > 0) & np.all(c[..., :3] == col, -1)
        if pm.any():
            H, W = pm.shape
            yy, xx = np.mgrid[0:H, 0:W]
            ys, xs = np.nonzero(pm)
            y0, x0 = ys.min(), xs.min()
            f = np.full((H, W), pf['base'])
            ry, rx = yy - y0, xx - x0
            band = ry // 5
            txt = ((band * 7919 + (rx // 9) * 104729) % 5 < 2) & (ry % 5 < 2) & (rx % 9 < 7) & (band % 3 != 0)
            f[txt] = pf['text']
            f[ry % 3 == 2] = pf['scan']
            f[(rx % pf['seam_every']) == pf['seam_every'] - 1] = pf['seam']
            c[pm, :3] = np.clip(np.rint(col[None, :] * f[pm][:, None]), 0, 255).astype(np.uint8)
    rim = sty.get('sky_rim')
    if rim and name not in rim.get('skip', []):
        A = c[..., 3] > 0
        def sh(m, dy, dx):
            o = np.zeros_like(m)
            H, W = m.shape
            o[max(dy, 0):H + min(dy, 0), max(dx, 0):W + min(dx, 0)] = m[max(-dy, 0):H + min(-dy, 0), max(-dx, 0):W + min(-dx, 0)]
            return o
        # outline ring = opaque with a transparent 4-neighbour
        ring = A & ~(sh(A, 1, 0) & sh(A, -1, 0) & sh(A, 0, 1) & sh(A, 0, -1))
        inner = A & ~ring & ~(e[..., 3] > 0)
        top = inner & sh(ring, 1, 0) & ~sh(A, 2, 0)      # pixel above is outline, two above is sky
        left = inner & sh(ring, 0, 1) & ~sh(A, 0, 2)
        m = top | left
        rc = hexf(rim['color'])
        c[m, :3] = np.clip(np.rint(c[m, :3] * (1 - rim['mix']) + rc * rim['mix']), 0, 255).astype(np.uint8)
    return c


def shelf_pack(sizes, max_w, pad):
    order = sorted(range(len(sizes)), key=lambda i: -sizes[i][1])
    pos = [None] * len(sizes)
    x, y, row_h, used_w = pad, pad, 0, 0
    for i in order:
        w, h = sizes[i]
        if x + w + pad > max_w:
            x, y, row_h = pad, y + row_h + pad, 0
        pos[i] = (x, y)
        x += w + pad
        row_h = max(row_h, h)
        used_w = max(used_w, x)
    return pos, used_w, y + row_h + pad


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--render', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--style', default=os.path.join(HERE, 'props_style.json'))
    ap.add_argument('--sheet')
    ap.add_argument('--zoom', type=int, default=4)
    ap.add_argument('--pad', type=int, default=2)
    a = ap.parse_args()
    st = Style(a.style)
    man = json.load(open(os.path.join(a.render, 'manifest.json')))
    S, ppu = man['scale'], man['ppu']
    res = []
    for p in man['props']:
        base = os.path.join(a.render, p['name'])
        c, n, e = process(base + '_id.png', base + '_n.png', S, st)
        al = c[..., 3]
        if al[0].any() or al[-1].any() or al[:, 0].any() or al[:, -1].any():
            print('WARNING: %s touches the cell edge (clipped)' % p['name'])
        c = finish(c, e, p['name'], st.st)
        res.append((p, c, n, e))
    pos, AW, AH = shelf_pack([(r[1].shape[1], r[1].shape[0]) for r in res], 1024, a.pad)
    atl = [np.zeros((AH, AW, 4), np.uint8) for _ in range(3)]
    atl[1][..., :3] = (128, 128, 255)
    doc_props = []
    for (p, c, n, e), (x, ytop) in zip(res, pos):
        h, w = c.shape[:2]
        lit = e[..., 3] > 0
        em = np.zeros_like(c)
        em[lit, :3] = np.clip(np.rint(srgb_to_linear(c[lit, :3].astype(float)) * 255), 0, 255)
        em[lit, 3] = 255
        for k, img in enumerate((c, n, em)):
            atl[k][ytop:ytop + h, x:x + w] = img
        pad = p['pad_bottom']
        pivot_y = pad / h if p['anchor'] == 'floor' else pad / h  # origin row: floor contact or ceiling hook
        entry = dict(name=p['name'], layer=p['layer'], anchor=p['anchor'],
                     rect=dict(x=x, y=AH - ytop - h, w=w, h=h),
                     pivot=dict(x=0.5, y=round(pivot_y, 6)),
                     size=dict(w=round(w / ppu, 4), h=round(h / ppu, 4)),
                     emissive=int(lit.sum()))
        if lit.any():
            ys, xs = np.nonzero(lit)
            entry['emissive_centroid'] = dict(x=round(((xs + 0.5).mean() - w / 2) / ppu, 4),
                                              y=round(((h - ys - 0.5).mean() - pad) / ppu, 4))
            col = c[lit, :3].mean(0)
            entry['emissive_color'] = '#%02x%02x%02x' % tuple(int(v) for v in col)
        doc_props.append(entry)
    os.makedirs(a.out, exist_ok=True)
    for k, suf in enumerate(('color', 'normal', 'emissive')):
        Image.fromarray(atl[k]).save(os.path.join(a.out, 'props_%s.png' % suf), optimize=True)
    used = {tuple(int(v) for v in q) for q in atl[0][atl[0][..., 3] > 0][:, :3]}
    doc = dict(name='props', ppu=ppu, atlas=dict(w=AW, h=AH), padding=a.pad, camera_tilt_deg=man['tilt'],
               rect_origin='bottom-left (Unity sprite rect convention); pivot normalised within the rect',
               pivot_note="'floor' props: pivot = ground contact at the bottom-centre; 'ceiling' props: pivot = hook at the top-centre",
               layer_note="background-decor: behind the actors at mid-dark value; foreground-occluder: may also be drawn large, dark and "
                          "low-opacity in front at the very bottom of the screen; hanging: hangs under an overhang",
               textures=dict(color='props_color.png', normal='props_normal.png', emissive='props_emissive.png'),
               normal_encoding='tangent space in the sprite plane: R=+X right, G=+Y up, B=toward camera; transparent texels (128,128,255,0)',
               emissive_encoding='RGB = emissive colour, linear-encoded (import with sRGB off); A = 255 on lit texels',
               palette=sorted('#%02x%02x%02x' % q for q in used), props=doc_props)
    json.dump(doc, open(os.path.join(a.out, 'props.json'), 'w'), indent=1)
    print('props: atlas %dx%d, %d props, palette used %d (style %d)' % (AW, AH, len(res), len(used), len(st.palette())))
    if a.sheet:
        z, gap, lab = a.zoom, 10, 22
        cols = min(5, len(res))
        cells = [(r[0]['name'], r[1]) for r in res]
        rows = [cells[i:i + cols] for i in range(0, len(cells), cols)]
        cw = [max(r[1].shape[1] for r in cells[i::cols]) * z for i in range(cols)]
        rh = [max(c.shape[0] for _, c in row) * z for row in rows]
        W = sum(cw) + gap * (cols + 1)
        H = sum(rh) + (gap + lab) * len(rows) + gap
        img = Image.new('RGBA', (W, H), (0x1b, 0x22, 0x30, 255))
        d = ImageDraw.Draw(img)
        font = label_font(16)
        y = gap
        for ri, row in enumerate(rows):
            x = gap
            for ci, (name, c) in enumerate(row):
                im = Image.fromarray(c).resize((c.shape[1] * z, c.shape[0] * z), Image.NEAREST)
                img.alpha_composite(im, (x, y + lab + rh[ri] - c.shape[0] * z))
                d.text((x, y), name, fill=(200, 210, 225, 255), font=font)
                x += cw[ci] + gap
            y += rh[ri] + lab + gap
        img.convert('RGB').save(a.sheet)
        print('sheet', a.sheet)


if __name__ == '__main__':
    main()
