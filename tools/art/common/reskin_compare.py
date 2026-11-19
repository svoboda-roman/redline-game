"""Before/after contact sheet for an enemy reskin: lit previews (albedo x simple light from the normal sheet,
emissive masks tinted on top) of a few frames per character, old sheets left, new sheets right.

python reskin_compare.py --old OLD_ROOT --new NEW_ROOT --out sheet.png grunt shooter ...
OLD_ROOT / NEW_ROOT contain <Name>/<name>.json + _color/_normal/_emissive pngs (Assets/Resources/Art/Characters layout).
"""
import argparse, json, os
import numpy as np
from PIL import Image, ImageDraw, ImageFont

L = np.array([-0.45, 0.55, 0.7]); L = L / np.linalg.norm(L)
TINT = {0: np.array([1.0, 0.35, 0.15]), 1: np.array([1.0, 0.1, 0.18]), 2: np.array([1.0, 0.62, 0.25])}  # R attack, G eyes, B strips
BG = (22, 16, 30)


def load(root, name):
    d = os.path.join(root, name[0].upper() + name[1:])
    meta = json.load(open(os.path.join(d, name + '.json')))
    tex = {k: np.asarray(Image.open(os.path.join(d, v)).convert('RGBA')).astype(np.float32) / 255.0
           for k, v in meta['textures'].items()}
    return meta, tex


def frame(meta, tex, fname):
    fr = next(f for f in meta['frames'] if f['name'] == fname)
    r = fr['rect']
    H = tex['color'].shape[0]
    y0 = H - r['y'] - r['h']
    sl = (slice(y0, y0 + r['h']), slice(r['x'], r['x'] + r['w']))
    col, nrm = tex['color'][sl], tex['normal'][sl]
    em = tex['emissive'][sl] if 'emissive' in tex else None
    n = nrm[..., :3] * 2 - 1
    lam = np.clip((n * L).sum(-1), 0, 1)
    rgb = col[..., :3] ** 2.2 * (0.55 + 1.6 * lam[..., None])
    rgb = np.clip(rgb, 0, 1) ** (1 / 2.2) * 1.15
    if em is not None:
        for ch in range(3):
            m = em[..., ch][..., None]
            rgb = rgb * (1 - m * 0.85) + TINT[ch] * m
    a = col[..., 3:4]
    out = np.concatenate([np.clip(rgb, 0, 1), a], -1)
    return Image.fromarray((out * 255).astype(np.uint8), 'RGBA')


def picks(meta):
    an = meta['animations']
    sel = [an[0]['frames'][0]]
    for a in an[1:3]:
        sel.append(a['frames'][len(a['frames']) // 2])
    sel.append(an[-1]['frames'][-1])
    return sel


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--old', required=True)
    ap.add_argument('--new', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--zoom', type=int, default=3)
    ap.add_argument('names', nargs='+')
    a = ap.parse_args()
    z = a.zoom
    rows = []
    for nm in a.names:
        cells = []
        for root in (a.old, a.new):
            meta, tex = load(root, nm)
            ims = [frame(meta, tex, f) for f in picks(meta)]
            cells.append(ims)
        rows.append((nm, cells))
    cw = max(im.width for _, c in rows for side in c for im in side) * z
    ch = max(im.height for _, c in rows for side in c for im in side) * z
    W = 140 + 8 * cw + 60
    H = 50 + len(rows) * (ch + 16)
    sheet = Image.new('RGB', (W, H), BG)
    d = ImageDraw.Draw(sheet)
    try:
        f = ImageFont.truetype('segoeuib.ttf', 22)
        fs = ImageFont.truetype('segoeuib.ttf', 16)
    except OSError:
        f = fs = ImageFont.load_default()
    d.text((140 + 2 * cw - 40, 10), 'PREDTIM', fill=(200, 200, 210), font=f)
    d.text((140 + 4 * cw + 60 + 2 * cw - 30, 10), 'POTOM', fill=(255, 210, 90), font=f)
    d.line([(140 + 4 * cw + 30, 40), (140 + 4 * cw + 30, H - 6)], fill=(90, 80, 110), width=2)
    for ri, (nm, cells) in enumerate(rows):
        y = 50 + ri * (ch + 16)
        d.text((10, y + ch // 2 - 10), nm, fill=(230, 230, 240), font=fs)
        for si, ims in enumerate(cells):
            x0 = 140 + si * (4 * cw + 60)
            for k, im in enumerate(ims):
                big = im.resize((im.width * z, im.height * z), Image.NEAREST)
                sheet.paste(big, (x0 + k * cw + (cw - big.width) // 2, y + ch - big.height), big)
    sheet.save(a.out)
    print('saved', a.out, sheet.size)


if __name__ == '__main__':
    main()
