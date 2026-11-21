#!/usr/bin/env python3
"""Bake a character's Blender renders into 3D-lit sprite sheets (albedo + view-space normal + emissive), the same
material language as the Tech Ronin (Assets/Editor/Ronin/RoninSpriteBaker.cs). Packs the same atlas + json schema
as pixelate_char.py, so Enemy3DRenderer loads it unchanged.

python3 bake_char.py --render DIR --out ASSET_DIR --name grunt [--albedo enemy_albedo.json] [--preview DIR]

Per sprite texel (one S x S block of the x S render):
  1. coverage = share of the block's samples that hit the model; opaque (hard alpha) at >= coverage.opaque, or at
     >= coverage.opaque_emissive when an emissive channel is set (thin glows survive);
  2. albedo = area mean of the covered samples' unlit material colour (linear light, encoded sRGB) - no toon bands,
     no AO, no rim, no outline; the in-game URP lamps shade it through the normal map;
  3. normal = mean of the covered samples' view-space normals (R=+X right, G=+Y up, B=toward camera), renormalised;
  4. emissive channel set when its materials cover >= coverage.<ch> of the block (unless the character folds it);
  5. legibility clean-up (art bible section 5 / 8): lone texels removed, 1-texel-thin runs grown to 2 texels toward
     the side with more coverage, eye masks grown to at least 2x2, other emissive blobs to at least 3x3 and lines to
     2 texels, colour outliers averaged with their neighbours (the ronin baker's despeckle).
Material ids come from the id pass (R = id*10/255, see kit.id_material); the albedo is looked up per sample, which is
exactly an unlit flat-colour render of the model, without a third Blender pass.
"""
import argparse, json, os, sys
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..', 'samurai'))
from pixelate import sheet, crop_rows  # noqa: E402  (preview contact sheet only)

N4 = [(-1, 0), (1, 0), (0, -1), (0, 1)]
N8 = [(dy, dx) for dy in (-1, 0, 1) for dx in (-1, 0, 1) if dy or dx]


def hexrgb(h):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def to_lin(c):
    c = np.asarray(c, float) / 255.0
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def to_srgb8(c):
    c = np.clip(c, 0, 1)
    s = np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)
    return np.clip(np.rint(s * 255), 0, 255).astype(np.uint8)


def nb(a, dy, dx, fill):
    """out[y, x] = a[y+dy, x+dx] (fill outside)."""
    out = np.full_like(a, fill)
    H, W = a.shape[:2]
    out[max(0, -dy):H - max(0, dy), max(0, -dx):W - max(0, dx)] = \
        a[max(0, dy):H - max(0, -dy), max(0, dx):W - max(0, -dx)]
    return out


class Look:
    def __init__(self, path, style_path, name):
        cfg = json.load(open(path))
        ids = {m['name']: m['id'] for m in json.load(open(style_path))['materials']}
        self.cov = cfg['coverage']
        fold = set(cfg.get('fold', {}).get(name, []))
        self.maxid = max(ids.values())
        self.lin = np.zeros((self.maxid + 1, 3))
        self.ch = np.full(self.maxid + 1, -1, int)  # emissive channel per material id (-1 none)
        for nm, m in cfg['materials'].items():
            i = ids[nm]
            self.lin[i] = to_lin(hexrgb(m['albedo']))
            if m.get('emissive') and nm not in fold:
                self.ch[i] = 'rgb'.index(m['emissive'])


def bake(id_png, n_png, S, lk):
    A = np.asarray(Image.open(id_png).convert('RGBA'))
    N = np.asarray(Image.open(n_png).convert('RGBA'))
    hit = A[..., 3] > 127
    mid = np.where(hit, np.rint(A[..., 0] / 10.0).astype(int), 0)
    mid[(mid < 0) | (mid > lk.maxid)] = 0
    hit &= mid > 0
    n = N[..., :3].astype(float) / 127.5 - 1.0
    n[n[..., 2] < 0] *= -1  # back faces seen through open shells: face the camera
    Hh, Ww = mid.shape
    H, W = Hh // S, Ww // S
    blk = lambda a: a.reshape(H, S, W, S, *a.shape[2:]).sum((1, 3))
    cnt = blk(hit.astype(float))
    cov = cnt / (S * S)
    alb = blk(lk.lin[mid] * hit[..., None]) / np.maximum(cnt, 1)[..., None]
    ns = blk(n * hit[..., None])
    ln = np.linalg.norm(ns, axis=-1)
    nrm = np.where(ln[..., None] > 1e-6, ns / np.maximum(ln, 1e-6)[..., None], np.array([0, 0, 1.0]))
    chm = lk.ch[mid]
    em = np.zeros((H, W, 3), bool)
    for c, key in enumerate('rgb'):
        em[..., c] = blk(((chm == c) & hit).astype(float)) / (S * S) >= lk.cov[key]
    op = (cov >= lk.cov['opaque']) | (em.any(-1) & (cov >= lk.cov['opaque_emissive']))
    em &= op[..., None]
    f = dict(op=op, alb=alb, nrm=nrm, em=em, cov=cov)
    clean(f)
    color = np.zeros((H, W, 4), np.uint8)
    color[f['op'], :3] = to_srgb8(f['alb'][f['op']])
    color[f['op'], 3] = 255
    normal = np.zeros((H, W, 4), np.uint8)
    normal[..., :3] = (128, 128, 255)
    normal[f['op'], :3] = np.clip(np.rint((f['nrm'][f['op']] * 0.5 + 0.5) * 255), 0, 255)
    normal[f['op'], 3] = 255
    emissive = np.zeros((H, W, 4), np.uint8)
    emissive[..., :3] = f['em'] * 255
    emissive[f['em'].any(-1), 3] = 255
    return color, normal, emissive


def copy_texel(f, src, dst):
    f['op'][dst] = True
    f['alb'][dst] = f['alb'][src]
    f['nrm'][dst] = f['nrm'][src]


def clean(f):
    op, H, W = f['op'], *f['op'].shape
    # 1. lone texels (at most one opaque 8-neighbour) that carry no glow
    nop = sum(nb(op, dy, dx, False).astype(int) for dy, dx in N8)
    lone = op & (nop <= 1) & ~f['em'].any(-1)
    op[lone] = False
    f['em'][lone] = False
    # 2. 1-texel-thin runs -> 2 texels, grown toward the side the model covers more (deterministic per frame)
    for axis in ((0, -1, 0, 1), (-1, 0, 1, 0)):
        dy0, dx0, dy1, dx1 = axis
        thin = op & ~nb(op, dy0, dx0, False) & ~nb(op, dy1, dx1, False)
        ca, cb = nb(f['cov'], dy0, dx0, 0.0), nb(f['cov'], dy1, dx1, 0.0)
        for y, x in zip(*np.nonzero(thin)):
            dy, dx = (dy0, dx0) if ca[y, x] > cb[y, x] else (dy1, dx1)
            ty, tx = y + dy, x + dx
            if 0 <= ty < H and 0 <= tx < W and not op[ty, tx]:
                copy_texel(f, (y, x), (ty, tx))
                f['em'][ty, tx] = f['em'][y, x]
    # 3. emissive legibility: eyes >= 2x2, other glows >= 3x3 blobs or 2-texel lines
    for c in range(3):
        m = f['em'][..., c]
        seen = np.zeros_like(m)
        for sy, sx in zip(*np.nonzero(m)):
            if seen[sy, sx]:
                continue
            comp, st = [], [(sy, sx)]
            seen[sy, sx] = True
            while st:
                y, x = st.pop()
                comp.append((y, x))
                for dy, dx in N8:
                    ny, nx = y + dy, x + dx
                    if 0 <= ny < H and 0 <= nx < W and m[ny, nx] and not seen[ny, nx]:
                        seen[ny, nx] = True
                        st.append((ny, nx))
            ys, xs = [p[0] for p in comp], [p[1] for p in comp]
            y0, x0, h, w = min(ys), min(xs), max(ys) - min(ys) + 1, max(xs) - min(xs) + 1
            need = 2 if c == 1 else 3
            if h < need and w < need:  # small blob -> solid need x need block
                for dy in range(need):
                    for dx in range(need):
                        grow(f, comp[0], y0 + dy, x0 + dx, c)
            else:  # line: make every 1-texel-wide stretch 2 wide
                for y, x in comp:
                    hN = any(0 <= x + d < W and m[y, x + d] for d in (-1, 1))
                    vN = any(0 <= y + d < H and m[y + d, x] for d in (-1, 1))
                    if not hN:
                        grow(f, (y, x), y, x + 1, c)
                    if not vN:
                        grow(f, (y, x), y + 1, x, c)
                    if not hN and not vN:
                        grow(f, (y, x), y + 1, x + 1, c)
    # 4. colour outliers (no similar 8-neighbour) take the neighbour mean; glows keep their texel colour
    srgb = to_srgb8(f['alb']).astype(int)
    opn = f['op'].copy()
    for y, x in zip(*np.nonzero(opn & ~f['em'].any(-1))):
        nbr = [(y + dy, x + dx) for dy, dx in N8 if 0 <= y + dy < H and 0 <= x + dx < W and opn[y + dy, x + dx]]
        if len(nbr) >= 3 and not any(np.abs(srgb[p] - srgb[y, x]).sum() < 48 for p in nbr):
            f['alb'][y, x] = np.mean([f['alb'][p] for p in nbr], axis=0)


def grow(f, src, y, x, c):
    H, W = f['op'].shape
    if not (0 <= y < H and 0 <= x < W):
        return
    if not f['op'][y, x]:
        copy_texel(f, src, (y, x))
    f['em'][y, x, c] = True


def thin_share(color):
    """Share of opaque texels that are 1 texel thin horizontally or vertically (legibility check, lower is better)."""
    op = color[..., 3] > 0
    t = op & ((~nb(op, 0, -1, False) & ~nb(op, 0, 1, False)) | (~nb(op, -1, 0, False) & ~nb(op, 1, 0, False)))
    return t.sum() / max(1, op.sum())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--render', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--name', required=True)
    ap.add_argument('--albedo', default=os.path.join(HERE, 'enemy_albedo.json'))
    ap.add_argument('--style', default=os.path.join(HERE, 'enemy_style.json'), help='material id table')
    ap.add_argument('--preview')
    ap.add_argument('--pad', type=int, default=2)
    a = ap.parse_args()
    lk = Look(a.albedo, a.style, a.name)
    man = json.load(open(os.path.join(a.render, 'manifest.json')))
    S, cw, chh, padb = man['scale'], man['cell_w'], man['cell_h'], man['pad_bottom']
    meta, anims = man['meta'], man['anims']
    res, thin = {}, []
    for an in anims:
        for i in range(an['count']):
            base = os.path.join(a.render, '%s_%02d' % (an['name'], i))
            res[(an['name'], i)] = c = bake(base + '_id.png', base + '_n.png', S, lk)
            thin.append(thin_share(c[0]))
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
    folded = [nm for nm in json.load(open(a.albedo)).get('fold', {}).get(a.name, [])]
    doc = dict(
        name=a.name, ppu=man['ppu'], cell=dict(w=cw, h=chh), atlas=dict(w=AW, h=AH), padding=p,
        rect_origin='bottom-left (Unity sprite rect convention); pivot normalised within the rect',
        pivot_note=meta['pivot_note'],
        bake_note='3D-lit bake (tools/art/common/bake_char.py): unlit material albedo, area-filtered, hard alpha; no toon '
                  'bands, rim or outline (same material language as the ronin)' +
                  ('; folded to albedo trim (no glow): ' + ', '.join(folded) if folded else ''),
        textures=dict(color='%s_color.png' % a.name, normal='%s_normal.png' % a.name, emissive='%s_emissive.png' % a.name),
        normal_encoding='view-space normal: R=+X right, G=+Y up, B=toward camera; transparent texels (128,128,255,0)',
        emissive_encoding=meta['emissive_encoding'] + ('; folded, no longer emissive: ' + ', '.join(folded) if folded else ''),
        animations=metas, frames=frames)
    json.dump(doc, open(os.path.join(a.out, '%s.json' % a.name), 'w'), indent=1)
    print('%s: atlas %dx%d cell %dx%d frames %d thin-texel share max %.3f mean %.3f' %
          (a.name, AW, AH, cw, chh, len(frames), max(thin), sum(thin) / len(thin)))
    if a.preview:
        os.makedirs(a.preview, exist_ok=True)
        rows = [(an['name'], [res[(an['name'], i)][0] for i in range(an['count'])]) for an in anims]
        sheet(crop_rows(rows), 3, (0x1b, 0x22, 0x30, 255), os.path.join(a.preview, '%s_prev.png' % a.name), 110)


if __name__ == '__main__':
    main()
