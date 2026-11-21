#!/usr/bin/env python3
"""Generate hue-shifted tone ramps into samurai_style.json (v2 shading).

python3 gen_ramps.py [--style samurai_style.json] [--swatch out.png]

Each material with a "gen" block gets its "ramp" (dark -> light) rewritten from one mid colour:
  gen = {"base": "#rrggbb", "n": 5, "dl": 0.075, "cool": 0.03, "warm": 0.02, "sat": 1.0, "spec": 0.12}
In OKLab: lightness steps of dl around the base; every step below the base is pushed toward a
cool blue-violet hue by `cool` per step (and keeps / gains a little chroma), every step above it
toward a warm amber hue by `warm` per step (and loses a little chroma). "spec" (optional) adds one
extra highlight colour that much lighter than the top band, stored as "spec".
"share": "<material>" with "pick": [indices] copies colours of another material instead, so shared
tones count once in the palette. The resulting hex values are placeholders pending art-director
sign-off; edit the gen block (or the ramp by hand and drop the gen block) to retune.
"""
import argparse, json, math, os

HERE = os.path.dirname(os.path.abspath(__file__))
COOL = (0.012, -0.045)   # OKLab (a, b) direction: blue-violet
WARM = (0.010, 0.040)    # amber


def s2l(c):
    c /= 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def l2s(c):
    c = max(0.0, min(1.0, c))
    c = 12.92 * c if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055
    return int(round(c * 255))


def hex2lab(h):
    h = h.lstrip('#')
    r, g, b = (s2l(int(h[i:i + 2], 16)) for i in (0, 2, 4))
    l = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b
    m = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b
    s = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b
    l, m, s = (math.copysign(abs(v) ** (1 / 3), v) for v in (l, m, s))
    return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s)


def lab2hex(L, a, b):
    l = (L + 0.3963377774 * a + 0.2158037573 * b) ** 3
    m = (L - 0.1055613458 * a - 0.0638541728 * b) ** 3
    s = (L - 0.0894841775 * a - 1.2914855480 * b) ** 3
    r = 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s
    g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s
    bb = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s
    return '#%02x%02x%02x' % (l2s(r), l2s(g), l2s(bb))


def ramp(g):
    L0, a0, b0 = hex2lab(g['base'])
    n = g.get('n', 5)
    mid = g.get('mid', (n - 1) / 2.0)
    dl, sat = g.get('dl', 0.075), g.get('sat', 1.0)
    out = []
    for i in range(n + (1 if 'spec' in g else 0)):
        k = i - mid
        L = L0 + dl * k
        if i == n:  # specular highlight
            L = L0 + dl * (n - 1 - mid) + g['spec']
            k = n - mid
        if k < 0:
            w = -k * g.get('cool', 0.03) / 0.045 * 0.5 * min(1.0, max(L, 0.05) / 0.4)
            a, b = a0 * (1 + 0.05 * -k) + COOL[0] * w, b0 * (1 + 0.05 * -k) + COOL[1] * w
        else:
            w = k * g.get('warm', 0.02) / 0.04 * 0.6
            a, b = a0 * (1 - 0.12 * k) * sat + WARM[0] * w, b0 * (1 - 0.12 * k) * sat + WARM[1] * w
        out.append(lab2hex(max(0.05, L), a, b))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--style', default=os.path.join(HERE, 'samurai_style.json'))
    ap.add_argument('--swatch')
    a = ap.parse_args()
    st = json.load(open(a.style))
    by = {m['name']: m for m in st['materials']}
    for m in st['materials']:
        if 'gen' in m:
            r = ramp(m['gen'])
            if 'spec' in m['gen']:
                m['ramp'], m['spec'] = r[:-1], r[-1]
            else:
                m['ramp'] = r
    for m in st['materials']:
        if 'share' in m:
            src = by[m['share']]
            m['ramp'] = [src['ramp'][i] for i in m['pick']]
    pal = set()
    for m in st['materials']:
        pal |= set(m['ramp'])
        if 'spec' in m:
            pal.add(m['spec'])
    pal |= set(st['outlines'].values())
    txt = json.dumps(st, indent=1)
    open(a.style, 'w').write(txt + '\n')
    print('palette', len(pal))
    if a.swatch:
        from PIL import Image, ImageDraw
        rows = st['materials']
        im = Image.new('RGB', (7 * 24 + 90, len(rows) * 24), (27, 34, 48))
        d = ImageDraw.Draw(im)
        for r, m in enumerate(rows):
            d.text((2, r * 24 + 6), m['name'], fill=(220, 220, 220))
            cols = m['ramp'] + ([m['spec']] if 'spec' in m else [])
            for c, h in enumerate(cols):
                d.rectangle([90 + c * 24, r * 24 + 1, 90 + c * 24 + 22, r * 24 + 22], fill=h)
        im.save(a.swatch)


if __name__ == '__main__':
    main()
