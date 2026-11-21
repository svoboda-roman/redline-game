"""v2 shading for pixelate.py, selected by "shading": 2 in the style json (v1 process() is untouched,
so tools/art/common/pixelate_char.py and the enemies keep the v1 look until they opt in).

Per sprite pixel, on top of v1's majority downscale / thin-feature priority / hard alpha:
  * per-material coverage threshold for priority features ("cov"), so 1-px lacing lines don't double;
  * 1-5 tone bands per material from hue-shifted ramps (gen_ramps.py), thresholds per band count;
  * ordered Bayer dither ONLY on materials flagged "dither", only >= dither.inset px inside a
    same-material region, only within +-amp of a band threshold (large soft transitions);
  * "metal": one 1-2 px specular cluster per highlight region from a fixed upper-front light;
  * "lacquer": a 1-px highlight line (topmost pixel per column of the broad highlight region);
  * selective inner AA: a pixel sitting in a 1-px stair step between two non-emissive materials
    takes the palette colour nearest the mix of both; the outer silhouette stays crisp;
  * v1 rim, selective dark outline, normal and emissive packing ("emissive_value" sets mask alpha).
"""
import numpy as np
from PIL import Image

from pixelate import Style, hexrgb, nb, DIRS4

BAYER2 = np.array([[0, 2], [3, 1]]) / 4.0
BAYER4 = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) / 16.0
K = 8  # key = material * K + band; band index nband = specular colour


class StyleV2(Style):
    def __init__(self, path):
        super().__init__(path)
        st = self.st
        n = self.maxid + 1
        self.lut8 = np.zeros((n * K, 3), np.uint8)
        self.has_spec = np.zeros(n, bool)
        by = {m['name']: m for m in st['materials']}
        for i, m in self.mats.items():
            for b in range(K):
                self.lut8[i * K + b] = hexrgb(m['ramp'][min(b, len(m['ramp']) - 1)])
            sp = m.get('spec') or by.get(m.get('spec_from', ''), {}).get('spec')
            if sp:
                self.lut8[i * K + len(m['ramp'])] = hexrgb(sp)
                self.has_spec[i] = True
        self.cov = {m['id']: m.get('cov', st['coverage']['priority']) for m in st['materials']}
        self.dither = np.zeros(n, bool)
        for i, m in self.mats.items():
            self.dither[i] = bool(m.get('dither'))
        self.evalue = {i: m.get('emissive_value', 255) for i, m in self.mats.items()}
        Ls = np.array(st.get('spec_light', [0.35, 0.75, 0.6]), float)
        self.Ls = Ls / np.linalg.norm(Ls)
        self.pal = np.array(sorted(self.palette()), float)

    def palette(self):
        cols = super().palette()
        for m in self.mats.values():
            if m.get('spec'):
                cols.add(hexrgb(m['spec']))
        return cols


def components(mask):
    """4-connected labels of a small boolean image (no scipy)."""
    H, W = mask.shape
    lab = np.zeros((H, W), int)
    cur = 0
    for y0, x0 in zip(*np.nonzero(mask)):
        if lab[y0, x0]:
            continue
        cur += 1
        st = [(y0, x0)]
        lab[y0, x0] = cur
        while st:
            y, x = st.pop()
            for dy, dx in DIRS4:
                yy, xx = y + dy, x + dx
                if 0 <= yy < H and 0 <= xx < W and mask[yy, xx] and not lab[yy, xx]:
                    lab[yy, xx] = cur
                    st.append((yy, xx))
    return lab, cur


def srgb2lin(c):
    c = c / 255.0
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def lin2srgb(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, 12.92 * c, 1.055 * c ** (1 / 2.4) - 0.055) * 255


def process_v2(id_png, n_png, S, st):
    A = np.asarray(Image.open(id_png).convert('RGBA'))
    N = np.asarray(Image.open(n_png).convert('RGBA'))
    hit = A[..., 3] > 127
    mid = np.where(hit, np.rint(A[..., 0] / 10.0).astype(int), 0)
    mid[mid > st.maxid] = 0
    ao = A[..., 1] / 255.0
    n = N[..., :3].astype(float) / 127.5 - 1.0
    n[n[..., 2] < 0] *= -1
    Hh, Ww = mid.shape
    H, W = Hh // S, Ww // S
    cov = np.zeros((st.maxid + 1, H, W))
    for m in range(1, st.maxid + 1):
        cov[m] = (mid == m).reshape(H, S, W, S).mean(axis=(1, 3))
    total = cov[1:].sum(0)
    choose = cov[1:].argmax(0) + 1
    pri_hit = np.zeros((H, W), bool)
    for m in reversed(st.prio):
        h = cov[m['id']] >= st.cov[m['id']]
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
    nbm = st.nband[choose]
    band = np.zeros((H, W), int)
    for nbc in range(2, 6):
        sel = nbm == nbc
        if sel.any():
            band[sel] = np.searchsorted(st.st['bands'][str(nbc)], shade[sel])
    for i, m in st.mats.items():
        if 'flat_band' in m:
            band[choose == i] = m['flat_band']
    band = np.minimum(band, nbm - 1)
    band[~opaque] = 0
    key = np.where(opaque, choose * K + band, -1)

    # despeckle (same-material band islands) and lone pixels, as v1
    neigh = np.stack([nb(key, dy, dx, -1) for dy, dx in DIRS4])
    for j in range(4):
        cnt = (neigh == neigh[j]).sum(0)
        fix = opaque & (cnt >= 3) & (neigh[j] != key) & (neigh[j] >= 0) & (neigh[j] // K == key // K)
        key[fix] = neigh[j][fix]
    on = np.stack([nb(opaque, dy, dx, False) for dy, dx in DIRS4]).any(0)
    prio_ids = [m['id'] for m in st.prio]
    lone = opaque & ~on & ~np.isin(choose, prio_ids)
    opaque &= ~lone
    key[lone] = -1
    choose[lone] = 0
    mat = np.maximum(key // K, 0)

    # sliver cleanup ("sliver_min": N on a material): 1-px-wide runs and components smaller than N px
    # take the neighbouring material's key, so a vivid lining can't streak as thin stripes through the coat
    for i, m in st.mats.items():
        smin = m.get('sliver_min')
        if not smin:
            continue
        for _ in range(2):
            me = opaque & (mat == i)
            if not me.any():
                break
            other = lambda dy, dx: nb(opaque, dy, dx, False) & (nb(mat, dy, dx, 0) != i)
            thin = me & ((other(0, -1) & other(0, 1)) | (other(-1, 0) & other(1, 0)))
            lab, k = components(me)
            if k:
                sizes = np.bincount(lab.ravel(), minlength=k + 1)
                thin |= me & (sizes[lab] < smin)
            if not thin.any():
                break
            for y, x in zip(*np.nonzero(thin)):
                cand = [key[y + dy, x + dx] for dy, dx in DIRS4
                        if 0 <= y + dy < H and 0 <= x + dx < W and key[y + dy, x + dx] >= 0
                        and key[y + dy, x + dx] // K != i]
                if cand:
                    key[y, x] = max(set(cand), key=cand.count)
            mat = np.maximum(key // K, 0)
            choose = np.where(opaque, mat, 0)
            nbm = st.nband[choose]

    # ordered dither on large soft transitions only
    dz = st.st.get('dither', {})
    if dz:
        r = dz.get('inset', 2)
        inner = opaque.copy()
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                inner &= nb(choose, dy, dx, 0) == choose
        bay = BAYER4 if dz.get('bayer', 4) == 4 else BAYER2
        bs = bay.shape[0]
        yy, xx = np.mgrid[0:H, 0:W]
        thr = bay[yy % bs, xx % bs] - 0.5 + 0.5 / bay.size
        sel = inner & st.dither[mat] & (nbm >= 4)
        for nbc in (4, 5):
            s2 = sel & (nbm == nbc)
            if s2.any():
                b2 = np.searchsorted(st.st['bands'][str(nbc)], shade[s2] + thr[s2] * 2 * dz.get('amp', 0.07))
                base = key[s2] % K
                # only move one band, and only where the undithered band agrees with the threshold region
                b2 = np.clip(b2, base - 1, base + 1)
                key[s2] = mat[s2] * K + np.minimum(b2, nbc - 1)

    # rim: one band lighter on edges open toward the configured directions (before specular)
    if st.st['rim']['enabled']:
        for dy, dx in st.st['rim']['dirs']:
            edge = opaque & ~nb(opaque, dy, dx, False)
            ok = edge & ~st.is_emis[mat] & (key % K < st.nband[mat] - 1)
            key[ok] += 1

    # specular clusters (metal) and lacquer lines, from a fixed upper-front light, view = +z
    NdL = nrm @ st.Ls
    Rz = 2 * NdL * nrm[..., 2] - st.Ls[2]
    for i, m in st.mats.items():
        if not st.has_spec[i]:
            continue
        sm = opaque & (choose == i)
        if 'metal' in m:
            sp = np.clip(Rz, 0, 1) ** m['metal']['power'] * (0.5 + 0.5 * aob)
            reg = sm & (sp >= m['metal']['thr'] * sp[sm].max() if sm.any() else sm) & (sp > 0.25)
            lab, k = components(reg)
            for c in range(1, k + 1):
                ys, xs = np.nonzero(lab == c)
                v = sp[ys, xs] + 1e-4 * xs - 1e-4 * ys
                j = int(np.argmax(v))
                y, x = ys[j], xs[j]
                key[y, x] = i * K + st.nband[i]
                best, bv = None, -1
                for dy, dx in DIRS4:
                    yy, xx = y + dy, x + dx
                    if 0 <= yy < H and 0 <= xx < W and lab[yy, xx] == c and sp[yy, xx] > bv:
                        best, bv = (yy, xx), sp[yy, xx]
                if best and bv >= 0.85 * sp[y, x] and len(ys) >= 4:
                    key[best] = i * K + st.nband[i] - 1
        if 'lacquer' in m:
            sp = np.clip(Rz, 0, 1) ** m['lacquer']['power']
            reg = sm & (sp >= m['lacquer']['thr'])
            # thin line: topmost highlight pixel in each column, not on the silhouette top edge
            first = reg & ~nb(reg, -1, 0, False)
            first &= nb(opaque, -1, 0, False)
            key[first] = i * K + st.nband[i]

    color = np.zeros((H, W, 4), np.uint8)
    color[opaque, :3] = st.lut8[key[opaque]]
    color[opaque, 3] = 255

    # selective inner AA: 1-px stair steps between two non-emissive materials
    if st.st.get('inner_aa'):
        base = color[..., :3].astype(float)
        out = color.copy()
        ok = opaque & ~st.is_emis[choose]
        for (ay, ax), (by_, bx) in (((-1, 0), (0, -1)), ((-1, 0), (0, 1)), ((1, 0), (0, -1)), ((1, 0), (0, 1))):
            m1, m2 = nb(choose, ay, ax, 0), nb(choose, by_, bx, 0)
            o1, o2 = nb(choose, -ay, -ax, 0), nb(choose, -by_, -bx, 0)
            step = ok & (m1 == m2) & (m1 > 0) & (m1 != choose) & (o1 == choose) & (o2 == choose)
            step &= ~st.is_emis[m1] & nb(opaque, ay, ax, False) & nb(opaque, by_, bx, False)
            # the diagonal partner must also be the other material (a real staircase, not a notch)
            dg = nb(choose, ay + by_, ax + bx, 0)
            step &= dg == m1
            if not step.any():
                continue
            c1 = nb(color[..., :3], ay, ax, 0).astype(float)
            mix = lin2srgb(0.5 * (srgb2lin(base) + srgb2lin(c1)))
            ys, xs = np.nonzero(step)
            for y, x in zip(ys, xs):
                d = ((st.pal - mix[y, x]) ** 2).sum(1)
                out[y, x, :3] = st.pal[int(np.argmin(d))]
        color = out

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
            emissive[sel, 'rgb'.index(ch)] = st.evalue[i]
            emissive[sel, 3] = 255
    return color, normal, emissive
