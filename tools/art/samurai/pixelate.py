#!/usr/bin/env python3
"""Reduce the high-res id/normal renders to authored-looking pixel art and pack the atlases.

python3 pixelate.py --render DIR --out ASSET_DIR [--evidence DIR] [--preview DIR] [--style samurai_style.json] [--name samurai]

Per sprite pixel (one S x S block of the render):
  1. material = the majority material in the block, except thin "priority" materials (blade, visor,
     light strips, crest, pistons) which win at >= coverage.priority so 1-px features survive;
  2. opaque if covered >= coverage.alpha (hard alpha, no partial pixels);
  3. normal / AO = mean over the block's samples of the chosen material only (no cross-material bleed);
  4. tone band = toon quantisation of (ambient + diffuse * N.L) * AO -> an index into the material ramp,
     so every colour comes from one shared authored palette;
  5. cleanup: same-material band speckles removed, lone pixels removed, a light rim on top edges;
  6. a 1-px outline outside the silhouette in the darkened colour of the adjacent material.
"""
import argparse, json, os
import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
DIRS4 = [(-1, 0), (0, -1), (0, 1), (1, 0)]


def hexrgb(h):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def nb(a, dy, dx, fill):
    """out[y, x] = a[y+dy, x+dx] (fill outside)."""
    out = np.full_like(a, fill)
    H, W = a.shape[:2]
    out[max(0, -dy):H - max(0, dy), max(0, -dx):W - max(0, dx)] = \
        a[max(0, dy):H - max(0, -dy), max(0, dx):W - max(0, -dx)]
    return out


class Style:
    def __init__(self, path):
        st = json.load(open(path))
        self.st = st
        self.mats = {m['id']: m for m in st['materials']}
        self.maxid = max(self.mats)
        self.prio = sorted([m for m in st['materials'] if 'priority' in m], key=lambda m: m['priority'])
        self.emis = {m['id']: m.get('emissive') for m in st['materials']}
        self.is_emis = np.zeros(self.maxid + 1, bool)
        for i, e in self.emis.items():
            self.is_emis[i] = bool(e)
        self.nband = np.ones(self.maxid + 1, int)
        for i, m in self.mats.items():
            self.nband[i] = len(m['ramp'])
        self.lut = np.zeros(((self.maxid + 1) * 4, 3), np.uint8)  # key = mat*4 + band
        for i, m in self.mats.items():
            for b in range(4):
                self.lut[i * 4 + b] = hexrgb(m['ramp'][min(b, len(m['ramp']) - 1)])
        self.out_lut = np.zeros((self.maxid + 1, 3), np.uint8)
        for i, m in self.mats.items():
            self.out_lut[i] = hexrgb(st['outlines'][m['outline']])
        L = np.array(st['light']['dir'], float)
        self.L = L / np.linalg.norm(L)

    def palette(self):
        cols = set()
        for m in self.mats.values():
            cols |= {hexrgb(c) for c in m['ramp']}
        cols |= {hexrgb(c) for c in self.st['outlines'].values()}
        return cols


def process(id_png, n_png, S, st):
    A = np.asarray(Image.open(id_png).convert('RGBA'))
    N = np.asarray(Image.open(n_png).convert('RGBA'))
    hit = A[..., 3] > 127
    mid = np.where(hit, np.rint(A[..., 0] / 10.0).astype(int), 0)
    mid[mid > st.maxid] = 0
    ao = A[..., 1] / 255.0
    n = N[..., :3].astype(float) / 127.5 - 1.0
    n[n[..., 2] < 0] *= -1  # back faces seen through open shells: face the camera
    Hh, Ww = mid.shape
    H, W = Hh // S, Ww // S
    cov = np.zeros((st.maxid + 1, H, W))
    for m in range(1, st.maxid + 1):
        cov[m] = (mid == m).reshape(H, S, W, S).mean(axis=(1, 3))
    total = cov[1:].sum(0)
    choose = cov[1:].argmax(0) + 1
    pri_hit = np.zeros((H, W), bool)
    thr = st.st['coverage']['priority']
    for m in reversed(st.prio):  # lowest priority first, highest overwrites
        h = cov[m['id']] >= thr
        choose[h] = m['id']
        pri_hit |= h
    opaque = (total >= st.st['coverage']['alpha']) | pri_hit
    choose[~opaque] = 0
    up = np.repeat(np.repeat(choose, S, 0), S, 1)
    msk = (mid == up) & (up > 0)
    w = msk.reshape(H, S, W, S).sum((1, 3)).astype(float)
    ns = (n * msk[..., None]).reshape(H, S, W, S, 3).sum((1, 3))
    ln = np.linalg.norm(ns, axis=-1)
    nrm = np.where(ln[..., None] > 1e-6, ns / np.maximum(ln, 1e-6)[..., None], np.array([0, 0, 1.0]))
    aob = (ao * msk).reshape(H, S, W, S).sum((1, 3)) / np.maximum(w, 1)

    lt = st.st['light']
    shade = (lt['ambient'] + lt['diffuse'] * (nrm @ st.L)) * (1 - lt['ao_strength'] * (1 - aob))
    band = np.zeros((H, W), int)
    for nbc in (2, 3):
        sel = st.nband[choose] == nbc
        band[sel] = np.searchsorted(st.st['bands'][str(nbc)], shade[sel])
    for i, m in st.mats.items():
        if 'flat_band' in m:
            band[choose == i] = m['flat_band']
    band = np.minimum(band, st.nband[choose] - 1)
    band[~opaque] = 0
    key = np.where(opaque, choose * 4 + band, -1)

    # despeckle: a pixel whose >=3 of 4 neighbours share one key of the same material takes that key
    neigh = np.stack([nb(key, dy, dx, -1) for dy, dx in DIRS4])
    for j in range(4):
        cnt = (neigh == neigh[j]).sum(0)
        fix = opaque & (cnt >= 3) & (neigh[j] != key) & (neigh[j] >= 0) & (neigh[j] // 4 == key // 4)
        key[fix] = neigh[j][fix]
    # lone pixels (no opaque 4-neighbour) that are not thin priority features
    on = np.stack([nb(opaque, dy, dx, False) for dy, dx in DIRS4]).any(0)
    prio_ids = [m['id'] for m in st.prio]
    lone = opaque & ~on & ~np.isin(choose, prio_ids)
    opaque &= ~lone
    key[lone] = -1
    choose[lone] = 0
    # rim: one band lighter on edges open toward the configured directions
    if st.st['rim']['enabled']:
        for dy, dx in st.st['rim']['dirs']:
            edge = opaque & ~nb(opaque, dy, dx, False)
            mat = np.maximum(key // 4, 0)
            ok = edge & ~st.is_emis[mat] & (key % 4 < st.nband[mat] - 1)
            key[ok] += 1
    # outline outside the silhouette, coloured by the adjacent (preferably non-emissive) material
    ring = ~opaque & np.stack([nb(opaque, dy, dx, False) for dy, dx in DIRS4]).any(0)
    omat = np.zeros((H, W), int)
    for allow_emis in (False, True):
        for dy, dx in DIRS4:
            nm = nb(choose, dy, dx, 0)
            ok = ring & (omat == 0) & (nm > 0) & (allow_emis | ~st.is_emis[nm])
            omat[ok] = nm[ok]
    vec = np.zeros((H, W, 2))
    for dy, dx in DIRS4 + [(-1, -1), (-1, 1), (1, -1), (1, 1)]:
        o = nb(opaque, dy, dx, False)
        vec[o, 0] += -dx
        vec[o, 1] += dy
    color = np.zeros((H, W, 4), np.uint8)
    color[opaque, :3] = st.lut[key[opaque]]
    color[opaque, 3] = 255
    color[ring, :3] = st.out_lut[omat[ring]]
    color[ring, 3] = 255
    normal = np.zeros((H, W, 4), np.uint8)
    normal[..., :3] = (128, 128, 255)
    normal[opaque, :3] = np.clip(np.rint((nrm[opaque] * 0.5 + 0.5) * 255), 0, 255)
    rv = np.concatenate([vec, np.full((H, W, 1), 0.45)], -1)
    rv /= np.linalg.norm(rv, axis=-1, keepdims=True)
    normal[ring, :3] = np.clip(np.rint((rv[ring] * 0.5 + 0.5) * 255), 0, 255)
    normal[opaque | ring, 3] = 255
    emissive = np.zeros((H, W, 4), np.uint8)
    for i, ch in st.emis.items():
        if ch:
            sel = opaque & (choose == i)
            emissive[sel, 'rgb'.index(ch)] = 255
            emissive[sel, 3] = 255
    return color, normal, emissive


def label_font(size):
    try:
        return ImageFont.load_default(size=size)
    except TypeError:
        return ImageFont.load_default()


def sheet(rows, zoom, bg, path, label_w=190):
    """rows: [(label, [rgba arrays])] -> nearest-neighbour contact sheet, one labelled row each."""
    ch, cw = rows[0][1][0].shape[:2]
    cols = max(len(r[1]) for r in rows)
    gap = 4 * zoom
    img = Image.new('RGBA', (label_w + cols * (cw * zoom + gap), len(rows) * (ch * zoom + gap) + gap), bg)
    d = ImageDraw.Draw(img)
    font = label_font(max(14, 6 * zoom))
    for r, (lab, frames) in enumerate(rows):
        y = gap + r * (ch * zoom + gap)
        d.text((8, y + ch * zoom // 2), lab, fill=(200, 210, 225, 255), font=font)
        for c, f in enumerate(frames):
            im = Image.fromarray(f).resize((cw * zoom, ch * zoom), Image.NEAREST)
            img.alpha_composite(im, (label_w + c * (cw * zoom + gap), y))
    img.convert('RGB').save(path)


def crop_rows(rows, margin=2):
    allf = [f for _, fr in rows for f in fr]
    a = np.zeros(allf[0].shape[:2], bool)
    for f in allf:
        a |= f[..., 3] > 0
    ys, xs = np.nonzero(a)
    y0, y1 = max(ys.min() - margin, 0), ys.max() + 1 + margin
    x0, x1 = max(xs.min() - margin, 0), xs.max() + 1 + margin
    return [(l, [f[y0:y1, x0:x1] for f in fr]) for l, fr in rows]


def lit(color, normal, light_px, amb=0.22, power=1.25):
    """Fake Unity 2D point light: colour * (ambient + N.L), L toward a point above the sprite plane."""
    H, W = color.shape[:2]
    yy, xx = np.mgrid[0:H, 0:W]
    n = normal[..., :3].astype(float) / 127.5 - 1
    L = np.stack([light_px[0] - xx, -(light_px[1] - yy), np.full((H, W), float(light_px[2]))], -1).astype(float)
    L /= np.linalg.norm(L, axis=-1, keepdims=True)
    k = amb + power * np.clip((n * L).sum(-1), 0, None)
    out = color.copy()
    out[..., :3] = np.clip(color[..., :3] * k[..., None], 0, 255).astype(np.uint8)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--render', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--evidence')
    ap.add_argument('--preview')
    ap.add_argument('--style', default=os.path.join(HERE, 'samurai_style.json'))
    ap.add_argument('--name', default='samurai')
    ap.add_argument('--pad', type=int, default=2)
    a = ap.parse_args()
    v2 = json.load(open(a.style)).get('shading') == 2
    if v2:  # opt-in v2 shading; enemies (common/pixelate_char.py) import the v1 Style/process below
        from shade_v2 import StyleV2, process_v2
        st, proc = StyleV2(a.style), process_v2
    else:
        st, proc = Style(a.style), process
    man = json.load(open(os.path.join(a.render, 'manifest.json')))
    S, cw, chh, padb = man['scale'], man['cell_w'], man['cell_h'], man['pad_bottom']
    anims = man['anims']
    res = {}
    for an in anims:
        for i in range(an['count']):
            base = os.path.join(a.render, '%s_%02d' % (an['name'], i))
            res[(an['name'], i)] = c = proc(base + '_id.png', base + '_n.png', S, st)
            al = c[0][..., 3]
            if al[0].any() or al[-1].any() or al[:, 0].any() or al[:, -1].any():
                print('WARNING: %s_%02d touches the cell edge (clipped)' % (an['name'], i))
    cols = max(an['count'] for an in anims)
    p = a.pad
    AW, AH = p + cols * (cw + p), p + len(anims) * (chh + p)
    atl = [np.zeros((AH, AW, 4), np.uint8) for _ in range(3)]
    atl[1][..., :3] = (128, 128, 255)
    frames, meta = [], []
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
        meta.append(dict(name=an['name'], fps=an['fps'], loop=an['loop'], frames=names))
    os.makedirs(a.out, exist_ok=True)
    for k, suf in enumerate(('color', 'normal', 'emissive')):
        Image.fromarray(atl[k]).save(os.path.join(a.out, '%s_%s.png' % (a.name, suf)), optimize=True)
    used = {tuple(int(v) for v in c) for c in atl[0][atl[0][..., 3] > 0][:, :3]}
    doc = dict(
        name=a.name, ppu=man['ppu'], cell=dict(w=cw, h=chh), atlas=dict(w=AW, h=AH), padding=p,
        rect_origin='bottom-left (Unity sprite rect convention); pivot normalised within the rect',
        pivot_note='feet centre = bottom-centre of the 0.8 x 1.6 collision body; sprites face right',
        textures=dict(color='%s_color.png' % a.name, normal='%s_normal.png' % a.name, emissive='%s_emissive.png' % a.name),
        normal_encoding='tangent space in the sprite plane: R=+X right, G=+Y up, B=toward camera; transparent texels (128,128,255,0)',
        emissive_encoding='R = katana blade + slash smear (tint by speed tier; smear core 255, smear edge band 136 when shading v2), G = visor slit, B = cyber-arm light strips; A = 255 where any mask is set',
        palette=sorted('#%02x%02x%02x' % c for c in used),
        animations=meta, frames=frames)
    json.dump(doc, open(os.path.join(a.out, '%s.json' % a.name), 'w'), indent=1)
    print('atlas %dx%d cell %dx%d frames %d palette used %d (style %d)' %
          (AW, AH, cw, chh, len(frames), len(used), len(st.palette())))
    bg = (0x1b, 0x22, 0x30, 255)
    rows = [(an['name'], [res[(an['name'], i)][0] for i in range(an['count'])]) for an in anims]
    if a.preview:
        os.makedirs(a.preview, exist_ok=True)
        cr = crop_rows(rows)
        sheet(cr[:6], 3, bg, os.path.join(a.preview, 'prev_a.png'), 110)
        sheet(cr[6:], 3, bg, os.path.join(a.preview, 'prev_b.png'), 110)
        sheet(cr, 1, bg, os.path.join(a.preview, 'prev_1x.png'), 70)
    if a.evidence and not v2 and ('idle', 0) in res and ('attack', 3) in res:
        os.makedirs(a.evidence, exist_ok=True)
        sheet(rows, 4, bg, os.path.join(a.evidence, '%s-sheet.png' % a.name))
        pair = crop_rows([('', [res[('idle', 0)][0], res[('attack', 3)][0]])])
        sheet(pair, 8, bg, os.path.join(a.evidence, '%s-closeup.png' % a.name), 8)
        litf = [lit(res[k][0], res[k][1], (cw * 0.15, chh * 0.1, 45.0)) for k in (('idle', 0), ('attack', 3))]
        pair = crop_rows([('', litf)])
        sheet(pair, 8, (10, 12, 18, 255), os.path.join(a.evidence, '%s-normal-preview.png' % a.name), 8)
        print('evidence written to', a.evidence)


if __name__ == '__main__':
    main()
