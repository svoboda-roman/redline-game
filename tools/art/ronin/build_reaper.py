# Reaper girl build script (Blender 4.2, background) - replaces the Ronin's look on the SAME armature.
#
#   blender -b --factory-startup -P tools/art/ronin/build_reaper.py -- [--preview DIR] [--no-export]
#
# Reads the original ronin.fbx (armature + rest pose; the 17 clips in Assets/Art3D/Ronin/Animations stay valid because
# the bone hierarchy is untouched), throws away the Ronin outfit and the katana, and builds:
#   * Ronin_Body: a girl with a black bob (bangs over the left eye, glowing red eyes), sleeveless dark navy dress with an
#     A-line pleated skirt and a white ruffled petticoat hem, lace neckline, crimson choker, knee-high black stockings,
#     black ankle boots, a hair band on Ronin_Rim (runtime tier tint). The skirt is skinned to the Coat_* spring chains,
#     so it sways and follows the thighs like the old coat did.
#   * Katana (name kept - RoninView and the baker look it up): a war scythe. The snath lies on the katana's old axis
#     (pommel at the old pommel, so the grip and every pose stay the same); the curved blade sits at the top, pointing
#     along the old cutting-edge direction. Only the blade uses the emissive cell (tier glow).
# Writes Assets/Art3D/Ronin/ronin.fbx and Assets/Art3D/Ronin/Materials/Ronin_Palette_{Base,MS,Emit}.png.
# Modelling space = armature space: centimetres, +Y up, +Z character front, +X character left.
import bpy, sys, math, os
import numpy as np
from mathutils import Vector, Matrix

REPO = r"C:\Users\jirik\Desktop\ggg\redline-build"
SRC_FBX = r"C:\Users\jirik\Desktop\ggg\backup-ronin-2026-11-20\Art3D\Ronin\ronin.fbx"   # untouched original
OUT = REPO + "\\Assets\\Art3D\\Ronin\\"
ANIMS = OUT + "Animations\\"
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
PREVIEW = args[args.index("--preview") + 1] if "--preview" in args else None
EXPORT = "--no-export" not in args


def log(*a):
    print("REAPER", *a, flush=True)


# ---------------------------------------------------------------- palette (cell, sRGB, metallic, smoothness, emission)
PALETTE = {
    "Dress":    (0,  (34, 32, 52),    0.00, 0.35, None),
    "Ruffle":   (1,  (226, 222, 232), 0.00, 0.30, None),
    "Skin":     (2,  (236, 200, 176), 0.00, 0.35, None),
    "Hair":     (3,  (20, 18, 26),    0.00, 0.55, None),
    "Boots":    (4,  (26, 24, 30),    0.20, 0.60, None),
    "Stocking": (5,  (28, 26, 34),    0.00, 0.30, None),
    "Choker":   (6,  (150, 24, 36),   0.00, 0.40, None),
    "DressHi":  (7,  (58, 58, 86),    0.00, 0.35, None),
    "Eyes":     (8,  (255, 90, 96),   0.00, 0.50, (1.0, 0.18, 0.22)),
    "Lips":     (9,  (180, 80, 96),   0.00, 0.40, None),
    "Sole":     (10, (64, 60, 70),    0.10, 0.40, None),
    "Belt":     (11, (40, 30, 46),    0.10, 0.40, None),
    "Ferrule":  (12, (224, 208, 172), 0.00, 0.35, None),   # pale bone
    "Shaft":    (13, (176, 142, 104), 0.00, 0.30, None),   # aged bone snath
    "BladeC":   (14, (150, 26, 36),   0.60, 0.80, (1.0, 1.0, 1.0)),   # dark crimson; emission mask 1 = tier glow
    "Rim":      (15, (30, 60, 70),    0.00, 0.50, None),
}
PAL_N, PAL_CELL = 4, 4


def srgb_to_lin(c):
    c = c / 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def lin_to_srgb8(c):
    c = max(0.0, min(1.0, c))
    s = c * 12.92 if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055
    return int(round(s * 255))


def cell_uv(key):
    i = PALETTE[key][0]
    return ((i % PAL_N + 0.5) / PAL_N, (i // PAL_N + 0.5) / PAL_N)


def write_png(path, rows):
    import zlib, struct
    h, w = len(rows), len(rows[0])
    raw = b"".join(b"\x00" + bytes(v for px in row for v in px) for row in rows)

    def chunk(t, d): return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) + \
        chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f: f.write(png)


def write_palette(dirp):
    n = PAL_N * PAL_CELL
    base = [[(0, 0, 0, 255)] * n for _ in range(n)]
    ms = [[(0, 0, 0, 255)] * n for _ in range(n)]
    emit = [[(0, 0, 0, 255)] * n for _ in range(n)]
    for key, (i, rgb, metal, smooth, em) in PALETTE.items():
        cx, cy = i % PAL_N, i // PAL_N
        for y in range(cy * PAL_CELL, (cy + 1) * PAL_CELL):
            row = n - 1 - y
            for x in range(cx * PAL_CELL, (cx + 1) * PAL_CELL):
                base[row][x] = (rgb[0], rgb[1], rgb[2], 255)
                ms[row][x] = (int(round(metal * 255)), 0, 0, int(round(smooth * 255)))
                if em: emit[row][x] = (lin_to_srgb8(em[0]), lin_to_srgb8(em[1]), lin_to_srgb8(em[2]), 255)
    os.makedirs(dirp, exist_ok=True)
    write_png(dirp + "Ronin_Palette_Base.png", base)
    write_png(dirp + "Ronin_Palette_MS.png", ms)
    write_png(dirp + "Ronin_Palette_Emit.png", emit)


# ---------------------------------------------------------------- import the original rig
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC_FBX, automatic_bone_orientation=False, primary_bone_axis='Y',
                         secondary_bone_axis='X', axis_forward='-Z', axis_up='Y')
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
body = bpy.data.objects["Ronin_Body"]
katana = bpy.data.objects["Katana"]
MAT_ATLAS = bpy.data.materials["Ronin_Atlas"]
MAT_RIM = bpy.data.materials["Ronin_Rim"]
MAT_BLADE = bpy.data.materials["Ronin_Blade"]
B = lambda n: "mixamorig:" + n


def bh(n): return arm.data.bones[B(n)].head_local.copy()


# ---------------------------------------------------------------- mesh accumulator
class Acc:
    def __init__(self):
        self.V, self.W, self.F, self.K = [], [], [], []

    def vert(self, p, w):
        self.V.append(Vector(p)); self.W.append(w)
        return len(self.V) - 1

    def face(self, idx, key, inside):
        """inside: point the face normal must point AWAY from (None = keep winding)."""
        if inside is not None:
            ps = [self.V[i] for i in idx]
            n = (ps[1] - ps[0]).cross(ps[2] - ps[0])
            if n.length < 1e-9 and len(ps) > 3: n = (ps[2] - ps[0]).cross(ps[3] - ps[0])
            c = sum(ps, Vector()) / len(ps)
            if n.dot(c - inside) < 0: idx = idx[::-1]
        self.F.append(tuple(idx)); self.K.append(key)


def ring_pts(c, d, r1, r2, n, up=Vector((0, 1, 0)), phase=0.0, rfun=None):
    d = d.normalized()
    u = up - d * up.dot(d)
    if u.length < 1e-6: u = Vector((0, 0, 1)) - d * d.z
    u.normalize()
    v = d.cross(u).normalized()
    pts = []
    for k in range(n):
        a = phase + 2 * math.pi * k / n
        s = rfun(a) if rfun else 1.0
        pts.append(c + u * (math.cos(a) * r1 * s) + v * (math.sin(a) * r2 * s))
    return pts


def tube(acc, rings, centers, wf, kf, cap0=False, cap1=False, inner=0.0, inner_key=None):
    """rings: list of point lists (same length); kf(r, c) -> key. inner > 0 adds a reversed inner layer."""
    n = len(rings[0])
    for layer in ((0, 1) if inner > 0 else (0,)):
        ids = []
        for ri, ring in enumerate(rings):
            row = []
            for p in ring:
                q = p if layer == 0 else p + (centers[ri] - p).normalized() * inner
                row.append(acc.vert(q, wf(p)))
            ids.append(row)
        for r in range(len(rings) - 1):
            for c in range(n):
                a, b = ids[r][c], ids[r][(c + 1) % n]
                d, e = ids[r + 1][c], ids[r + 1][(c + 1) % n]
                mid = (centers[r] + centers[r + 1]) * 0.5
                key = kf(r, c) if layer == 0 else (inner_key or kf(r, c))
                if layer == 0:
                    acc.face([a, b, e, d], key, mid)
                else:
                    f = [a, b, e, d]
                    ps = [acc.V[i] for i in f]
                    nrm = (ps[1] - ps[0]).cross(ps[2] - ps[0])
                    cc = sum(ps, Vector()) / 4
                    if nrm.dot(cc - mid) > 0: f = f[::-1]   # inner layer faces the centre
                    acc.F.append(tuple(f)); acc.K.append(key)
        if layer == 0:
            for cap, ri, sgn in ((cap0, 0, -1), (cap1, len(rings) - 1, 1)):
                if not cap: continue
                ci = acc.vert(centers[ri], wf(centers[ri]))
                other = centers[1] if ri == 0 else centers[-2]
                for c in range(n):
                    acc.face([ids[ri][c], ids[ri][(c + 1) % n], ci], kf(max(0, ri - 1), c), other)


def ellipsoid(acc, c, rx, ry, rz, wf, key, lat=14, lon=20, keep=None, inner=0.0, shape=None):
    """keep(p, nrel) -> bool filters faces (by centre). shape(p_rel) -> scale tweak of the radius."""
    grid = []
    for i in range(lat + 1):
        th = math.pi * i / lat
        row = []
        for j in range(lon):
            ph = 2 * math.pi * j / lon
            rel = Vector((math.sin(th) * math.sin(ph), math.cos(th), math.sin(th) * math.cos(ph)))
            s = shape(rel) if shape else 1.0
            row.append(c + Vector((rel.x * rx * s, rel.y * ry * s, rel.z * rz * s)))
        grid.append(row)
    for layer in ((0, 1) if inner > 0 else (0,)):
        ids = [[acc.vert(p if layer == 0 else p + (c - p).normalized() * inner, wf(p)) for p in row] for row in grid]
        for i in range(lat):
            for j in range(lon):
                quad = [ids[i][j], ids[i][(j + 1) % lon], ids[i + 1][(j + 1) % lon], ids[i + 1][j]]
                pc = sum((grid[i][j], grid[i][(j + 1) % lon], grid[i + 1][(j + 1) % lon], grid[i + 1][j]), Vector()) / 4
                if keep and not keep(pc, Vector(((pc - c).x / rx, (pc - c).y / ry, (pc - c).z / rz))): continue
                k = key(pc) if callable(key) else key
                if layer == 0:
                    acc.face(quad, k, c)
                else:
                    ps = [acc.V[q] for q in quad]
                    nrm = (ps[1] - ps[0]).cross(ps[2] - ps[0])
                    if nrm.length < 1e-9: nrm = (ps[2] - ps[0]).cross(ps[3] - ps[0])
                    if nrm.dot(sum(ps, Vector()) / 4 - c) > 0: quad = quad[::-1]
                    acc.F.append(tuple(quad)); acc.K.append(k)


def box(acc, c, sx, sy, sz, wf, key):
    pts = [c + Vector((sx * a, sy * b, sz * d)) for a in (-.5, .5) for b in (-.5, .5) for d in (-.5, .5)]
    ids = [acc.vert(p, wf(p)) for p in pts]
    for f in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)):
        acc.face([ids[i] for i in f], key, c)


def lerp1d(v, anchors):
    """anchors ascending by value: [(value, bone), ...] -> {bone: w}."""
    if v <= anchors[0][0]: return {anchors[0][1]: 1.0}
    for (a, ba), (b, bb) in zip(anchors, anchors[1:]):
        if a <= v <= b:
            t = (v - a) / (b - a) if b > a else 0
            w = {}
            w[ba] = w.get(ba, 0) + 1 - t
            w[bb] = w.get(bb, 0) + t
            return w
    return {anchors[-1][1]: 1.0}


def mix(*pairs):
    w = {}
    for f, d in pairs:
        for k, v in d.items(): w[k] = w.get(k, 0) + f * v
    return w


# ---------------------------------------------------------------- the girl
acc = Acc()
HEAD_C = Vector((0, 170.0, 0.8))
HEAD_R = (10.6, 13.0, 11.6)      # a touch bigger than life: reads better at sprite scale

for side, sx in (("Left", 1), ("Right", -1)):
    X = 8.2 * sx
    up, lg, ft, toe = B(side + "UpLeg"), B(side + "Leg"), B(side + "Foot"), B(side + "ToeBase")
    legw = lambda p, up=up, lg=lg, ft=ft: lerp1d(p.y, [(8, ft), (13, lg), (50, lg), (58, up), (92, up), (100, B("Hips"))])
    # thigh + shin: skin above the stocking top (y 57), stockings below
    prof = [(97, 6.6), (92, 7.1), (80, 6.8), (68, 6.2), (58, 5.3), (56, 5.25), (50, 4.8), (40, 5.0), (28, 4.1), (18, 3.4), (14, 3.3)]
    rings, cents = [], []
    for y, r in prof:
        z = 0.3 + (y - 53) * (-0.02 if y < 53 else 0.0)
        c = Vector((X, y, z - (2.6 if y < 20 else 0) * (20 - min(y, 20)) / 6))
        rings.append(ring_pts(c, Vector((0, -1, 0)), r, r * 1.05, 14, up=Vector((0, 0, 1))))
        cents.append(c)
    tube(acc, rings, cents, legw, lambda r, c: "Skin" if prof[r][0] > 57 else "Stocking")
    # stocking top band
    c = Vector((X, 57.0, 0.3))
    tube(acc, [ring_pts(Vector((X, y, 0.3)), Vector((0, -1, 0)), 5.6, 5.8, 14, up=Vector((0, 0, 1))) for y in (58.2, 56.4)],
         [Vector((X, 58.2, 0.3)), Vector((X, 56.4, 0.3))], legw, lambda r, c: "Stocking")
    # ankle boot: shaft ring + foot block (heel to toe), sole
    bootw = lambda p, ft=ft, toe=toe, lg=lg: lerp1d(p.z, [(5.5, ft), (10.0, toe)]) if p.y < 12 else lerp1d(p.y, [(12, ft), (16, lg)])
    bc = [Vector((X, 17.5, -2.4)), Vector((X, 11, -3.0)), Vector((X, 7.5, -3.2))]
    tube(acc, [ring_pts(bc[0], Vector((0, -1, 0)), 4.0, 4.3, 12, up=Vector((0, 0, 1))),
               ring_pts(bc[1], Vector((0, -1, 0)), 4.4, 4.8, 12, up=Vector((0, 0, 1))),
               ring_pts(bc[2], Vector((0, -1, 0)), 4.6, 5.4, 12, up=Vector((0, 0, 1)))], bc, bootw, lambda r, c: "Boots", cap0=True)
    foot = [(-7.5, 4.0, 7.0, 4.2), (-2.0, 4.6, 8.5, 4.6), (5.0, 4.6, 6.4, 3.4), (11.0, 4.2, 4.6, 2.5), (15.5, 3.0, 3.0, 1.6), (17.0, 1.6, 1.6, 1.0)]
    rings, cents = [], []
    for z, hw, hh, yc in foot:
        c = Vector((X, yc, z))
        rings.append(ring_pts(c, Vector((0, 0, 1)), hw, hh * 0.5, 12, up=Vector((1, 0, 0))))
        cents.append(c)
    tube(acc, rings, cents, bootw, lambda r, c: "Boots", cap0=True, cap1=True)
    box(acc, Vector((X, 0.35, 4.5)), 8.8, 0.7, 23.0, bootw, "Sole")
    box(acc, Vector((X, 2.2, -5.6)), 6.0, 4.4, 3.6, bootw, "Sole")      # heel
    for z in (4.0, 7.5):                                                  # laces
        box(acc, Vector((X, 6.6 - (z - 4) * 0.35, z)), 4.2, 0.6, 1.0, bootw, "Sole")

# bloomers (dark) under the skirt
ellipsoid(acc, Vector((0, 98.5, 0.6)), 12.6, 7.6, 9.6,
          lambda p: mix((1 - min(1, abs(p.x) / 12), {B("Hips"): 1}),
                        (min(1, abs(p.x) / 12), {B("LeftUpLeg" if p.x > 0 else "RightUpLeg"): 1})), "Stocking", lat=8, lon=16)

# bodice + bare chest/shoulders
torso = [(100, 12.8, 9.4, 1.0, "Dress"), (108, 11.6, 8.8, 1.4, "Dress"), (116, 10.8, 8.4, 1.0, "Dress"),
         (126, 12.2, 9.4, 1.6, "Dress"), (133, 13.0, 10.2, 2.2, "Dress"), (139, 12.6, 9.0, 0.6, "Dress"),
         (139.01, 12.6, 9.0, 0.6, "Skin"), (145, 13.6, 7.8, -2.4, "Skin"), (149, 7.4, 6.0, -3.2, "Skin")]
def tw(p):
    w = lerp1d(p.y, [(104, B("Hips")), (114.5, B("Spine")), (124.4, B("Spine1")), (133.6, B("Spine2"))])
    s = min(1, max(0, (abs(p.x) - 8) / 5)) * min(1, max(0, (p.y - 140) / 4))
    return mix((1 - s, w), (s, {B("LeftShoulder" if p.x > 0 else "RightShoulder"): 1})) if s > 0 else w
rings, cents = [], []
for y, rx, rz, z, k in torso:
    c = Vector((0, y, z))
    rings.append(ring_pts(c, Vector((0, -1, 0)), rx, rz, 20, up=Vector((1, 0, 0))))
    cents.append(c)
tube(acc, rings, cents, tw, lambda r, c: torso[r][4] if not (torso[r][4] == "Dress" and c % 5 == 0 and r < 5) else "DressHi")
# lace trim on the neckline, dress straps, belt
tube(acc, [ring_pts(Vector((0, y, 0.7)), Vector((0, -1, 0)), 13.1, 9.5, 20, up=Vector((1, 0, 0)),
                    rfun=lambda a: 1 + 0.03 * math.cos(10 * a)) for y in (140.0, 137.6)],
     [Vector((0, 140, 0.7)), Vector((0, 137.6, 0.7))], tw, lambda r, c: "Ruffle")
for sx in (1, -1):
    pts = [Vector((6.5 * sx, 139, 9.6)), Vector((9.0 * sx, 145.5, 4.0)), Vector((10.0 * sx, 147.5, -3.5)),
           Vector((8.5 * sx, 145, -9.5)), Vector((6.5 * sx, 139, -9.4))]
    rings = [ring_pts(p, (pts[min(i + 1, 4)] - pts[max(i - 1, 0)]), 1.1, 0.6, 6) for i, p in enumerate(pts)]
    tube(acc, rings, pts, lambda p: {B("Spine2"): 1}, lambda r, c: "Dress")
tube(acc, [ring_pts(Vector((0, y, 1.3)), Vector((0, -1, 0)), 12.0, 9.2, 20, up=Vector((1, 0, 0))) for y in (110.5, 106.5)],
     [Vector((0, 110.5, 1.3)), Vector((0, 106.5, 1.3))], lambda p: lerp1d(p.y, [(104, B("Hips")), (114.5, B("Spine"))]),
     lambda r, c: "Belt")
box(acc, Vector((0, 108.5, -9.8)), 6.0, 3.6, 1.6, lambda p: {B("Hips"): 1}, "Choker")     # small bow at the back
box(acc, Vector((0, 108.5, -10.4)), 1.6, 1.6, 1.8, lambda p: {B("Hips"): 1}, "Belt")

# skirt: A-line, pleated, skinned to the Coat_* chains (front panels follow the thighs, back panels on the hips)
PANEL_ANG = [(-180, "BC"), (-115, "BR"), (-71, "FR"), (71, "FL"), (115, "BL"), (180, "BC")]
Y0, Y1 = 107.5, 78.0


def skirt_w(p):
    th = math.degrees(math.atan2(p.x, p.z))
    if -71 < th < 71:   # front centre: share the two front panels
        t = (th + 71) / 142
        panel = {"FR": 1 - t, "FL": t}
    else:
        panel = {}
        for (a0, n0), (a1, n1) in zip(PANEL_ANG, PANEL_ANG[1:]):
            if a0 <= th <= a1:
                t = (th - a0) / (a1 - a0)
                panel = {n0: 1 - t, n1: t}
                break
    hips = min(1, max(0, (p.y - 96) / (Y0 - 96)))
    w = {B("Hips"): hips}
    for nm, f in panel.items():
        lo = min(1, max(0, (84 - p.y) / 24))
        w["Coat_%s_1" % nm] = w.get("Coat_%s_1" % nm, 0) + f * (1 - hips) * (1 - lo)
        w["Coat_%s_2" % nm] = w.get("Coat_%s_2" % nm, 0) + f * (1 - hips) * lo
    return w


def skirt_ring(y, extra=0.0, scallop=0.0):
    t = (Y0 - y) / (Y0 - Y1)
    rx = 12.4 + t * 10.5 + t * t * 2.5 + extra
    rz = 9.8 + t * 8.5 + t * t * 2.5 + extra
    c = Vector((0, y, 1.0 - t * 0.8))
    return c, ring_pts(c, Vector((0, -1, 0)), rx, rz, 32, up=Vector((1, 0, 0)),
                       rfun=lambda a: 1 + 0.035 * t * math.sin(16 * a) + scallop * math.cos(24 * a))


ys = [Y0 - i * (Y0 - Y1) / 7 for i in range(8)]
sk = [skirt_ring(y) for y in ys]
tube(acc, [r for _, r in sk], [c for c, _ in sk], skirt_w, lambda r, c: "DressHi" if c % 4 == 0 else "Dress",
     inner=0.5, inner_key="Ruffle")
ruf = [skirt_ring(Y1 + 0.6, 0.6, 0.0), skirt_ring(Y1 - 2.4, 2.2, 0.035), skirt_ring(Y1 - 4.2, 2.8, 0.05)]
tube(acc, [r for _, r in ruf], [c for c, _ in ruf], skirt_w, lambda r, c: "Ruffle", inner=0.4, inner_key="Ruffle")

# arms (T-pose along +-X), bare, with fist-ish hands
for side, sx in (("Left", 1), ("Right", -1)):
    names = [B(side + n) for n in ("Shoulder", "Arm", "ForeArm", "Hand")]
    armw = lambda p, n=names: lerp1d(abs(p.x), [(10, n[0]), (16, n[1]), (40, n[1]), (46, n[2]), (67, n[2]), (71, n[3])])
    prof = [(12.5, 5.0, 4.6), (18, 4.5, 4.2), (28, 4.0, 3.8), (38, 3.4, 3.3), (43, 3.2, 3.1), (52, 3.4, 3.2),
            (62, 2.9, 2.6), (69.5, 2.4, 2.1)]
    rings, cents = [], []
    for x, ry, rz in prof:
        c = Vector((x * sx, 144.0, -5.5))
        rings.append(ring_pts(c, Vector((sx, 0, 0)), ry, rz, 12))
        cents.append(c)
    tube(acc, rings, cents, armw, lambda r, c: "Skin", cap0=True)
    ellipsoid(acc, Vector((76.0 * sx, 143.6, -5.0)), 5.8, 2.6, 4.2, lambda p, n=names: {n[3]: 1}, "Skin", lat=6, lon=10)
    ellipsoid(acc, Vector((72.5 * sx, 143.8, -0.6)), 2.6, 1.6, 2.0, lambda p, n=names: {n[3]: 1}, "Skin", lat=5, lon=8)  # thumb

# neck + choker
nw = lambda p: lerp1d(p.y, [(146, B("Spine2")), (151, B("Neck")), (161, B("Neck")), (164, B("Head"))])
nc = [Vector((0, 146, -3.2)), Vector((0, 154, -3.6)), Vector((0, 162, -3.0))]
tube(acc, [ring_pts(c, Vector((0, -1, 0)), r, r * 0.95, 12, up=Vector((1, 0, 0))) for c, r in zip(nc, (5.6, 4.6, 4.4))], nc, nw,
     lambda r, c: "Skin")
cc = [Vector((0, 154.6, -3.5)), Vector((0, 152.4, -3.5))]
tube(acc, [ring_pts(c, Vector((0, -1, 0)), 5.1, 4.9, 12, up=Vector((1, 0, 0))) for c in cc], cc, nw, lambda r, c: "Choker")
box(acc, Vector((0, 153.4, 1.6)), 1.4, 1.4, 0.8, nw, "Ruffle")       # small pendant

# head: face, eyes, mouth
hw = lambda p: {B("Head"): 1}
ellipsoid(acc, HEAD_C, *HEAD_R, hw, "Skin", lat=12, lon=18,
          shape=lambda r: 1.0 - 0.14 * max(0.0, -r.y) * max(0.0, 1 - abs(r.z) * 0.3))


def on_face(x, y, out=0.25):
    rx, ry, rz = HEAD_R
    dx, dy = x / rx, (y - HEAD_C.y) / ry
    s = 1.0 - 0.14 * max(0.0, -dy)
    z = HEAD_C.z + rz * s * math.sqrt(max(0.0, 1 - dx * dx / (s * s) - dy * dy / (s * s)))
    return Vector((x, y, z + out))


for ex in (3.9, -3.9):
    box(acc, on_face(ex, 170.0, 0.15), 2.6, 1.8, 1.0, hw, "Eyes")
box(acc, on_face(0, 161.6, 0.1), 2.2, 0.7, 0.8, hw, "Lips")

# hair: black bob shell (double sided) with a face opening; bangs hang over the LEFT eye (the right eye faces the camera)
HC, HR = Vector((0, 171.4, -0.6)), (12.0, 14.4, 13.0)


def hair_keep(p, r):
    if p.y < 157.0: return False
    front = r.z > 0.30
    if front and p.y < 175.5 and abs(p.x) < 8.6:
        return p.x > 0.8 and p.y > 166.5          # bangs over the left eye
    if front and p.y < 162.0 and abs(p.x) < 9.8: return False
    return True


ellipsoid(acc, HC, *HR, hw, "Hair", lat=16, lon=24, keep=hair_keep, inner=0.5,
          shape=lambda r: 1.0 + (0.06 if r.y < -0.55 else 0.0) + 0.04 * max(0.0, -r.z))
# hair band (Ronin_Rim: runtime tier tint)
band = []
for i in range(15):
    a = math.radians(-82 + i * 164 / 14)
    band.append(HC + Vector((math.sin(a) * HR[0] * 1.04, math.cos(a) * HR[1] * 1.04, 3.2)))
rings = [ring_pts(p, band[min(i + 1, 14)] - band[max(i - 1, 0)], 1.0, 0.45, 6, up=Vector((0, 0, 1))) for i, p in enumerate(band)]
tube(acc, rings, band, hw, lambda r, c: "Rim")

log("girl verts", len(acc.V), "faces", len(acc.F))


# ---------------------------------------------------------------- scythe on the katana's axis
M = arm.matrix_world.inverted() @ katana.matrix_world      # katana local -> armature space
KV = np.array([tuple(M @ v.co) for v in katana.data.vertices])
ctr = KV.mean(axis=0)
_, _, vt = np.linalg.svd(KV - ctr)
ax = Vector(vt[0]).normalized()
proj = (KV - ctr) @ np.array(ax)
hand = bh("RightHand")
pA, pB = Vector(ctr) + ax * proj.min(), Vector(ctr) + ax * proj.max()
if (pA - hand).length > (pB - hand).length: pA, pB, ax = pB, pA, -ax
L_old = (pB - pA).length
tip_v = Vector(KV[int(np.argmax(proj if ax.dot(Vector(vt[0])) > 0 else -proj))])
off = (tip_v - pA) - ax * (tip_v - pA).dot(ax)
edge = (-off).normalized() if off.length > 0.2 else Vector(vt[1]).normalized()
edge = (edge - ax * edge.dot(ax)).normalized()
flat = ax.cross(edge).normalized()
log("katana length", round(L_old, 1), "axis", tuple(round(v, 3) for v in ax), "edge", tuple(round(v, 3) for v in edge))

sc = Acc()
HUB_U = 215.0          # skull hub on top of the snath: the scythe stands taller than she does
P0 = pA - ax * 1.0


def sp(u, e=0.0, f=0.0): return P0 + ax * u + edge * e + flat * f


def wob(u):
    """Crooked, gnarled bone snath: small lateral wobble, kept tiny around the grip (u 15..38)."""
    k = 0.35 if 12 < u < 40 else 1.0
    return edge * (1.1 * k * math.sin(u / 17.0)) + flat * (0.8 * k * math.sin(u / 11.0 + 1.0))


def spc(u): return sp(u) + wob(u)


def taper(acc, cl, r0, r1, key, n=6, cap=True):
    """Tapered tube along a centreline (bones, ribs, prongs)."""
    rings, cents = [], []
    for i, c in enumerate(cl):
        t = i / (len(cl) - 1)
        d = cl[min(i + 1, len(cl) - 1)] - cl[max(i - 1, 0)]
        r = r0 + (r1 - r0) * t
        rings.append(ring_pts(c, d, r, r, n, up=edge if abs(d.normalized().dot(edge)) < 0.9 else ax))
        cents.append(c)
    tube(acc, rings, cents, lambda p: {}, lambda r, c: key, cap0=cap, cap1=cap)


def curved_blade(acc, cl, widths, thick, key):
    """Flat curved blade: triangular section, spine on the +ax side of the a-e plane, cutting edge opposite."""
    rings, cents = [], []
    for i, c in enumerate(cl):
        t = (cl[min(i + 1, len(cl) - 1)] - cl[max(i - 1, 0)]).normalized()
        wd = flat.cross(t).normalized()
        if wd.dot(ax) < 0: wd = -wd
        w, th = widths[i], thick[i]
        spine, cut = c + wd * (w * 0.35), c - wd * (w * 0.65)
        rings.append([spine + flat * th, cut, spine - flat * th])
        cents.append(c)
    tube(acc, rings, cents, lambda p: {}, lambda r, c: key, cap0=True, cap1=True)


# gnarled bone snath
us = [0.0] + [2.0 + i * 3.5 for i in range(int((HUB_U - 6) / 3.5))] + [HUB_U - 3.0]
rings = [ring_pts(spc(u), ax, 1.75 + 0.35 * math.sin(u / 5.0) + 0.2 * math.sin(u / 2.3),
                  1.6 + 0.3 * math.sin(u / 4.1 + 2.0), 7, up=edge) for u in us]
tube(sc, rings, [spc(u) for u in us], lambda p: {}, lambda r, c: "Shaft", cap0=True, cap1=True)
# vertebra knuckles under the hub and at the grip ends
for u, r in ((HUB_U - 8, 2.5), (HUB_U - 13, 2.3), (HUB_U - 18, 2.1), (14.0, 2.2), (40.0, 2.2)):
    taper(sc, [spc(u - 1.2), spc(u), spc(u + 1.2)], r, r * 0.9, "Ferrule", n=7)
# sinew spiral wrapped round the snath + the dense grip wrap
hel = []
u = 42.0
while u < HUB_U - 20:
    a_ = u * 0.55
    hel.append(spc(u) + (edge * math.cos(a_) + flat * math.sin(a_)) * 2.0)
    u += 1.4
taper(sc, hel, 0.5, 0.5, "Choker", n=5, cap=False)
for i in range(9):
    u = 16 + i * 2.6
    taper(sc, [spc(u - 0.7), spc(u + 0.7)], 2.05, 2.05, "Choker", n=7)
# forked bone foot
taper(sc, [spc(1.0), sp(-3.0, 1.4, 0.4), sp(-7.0, 2.8, 0.9)], 1.2, 0.2, "Shaft")
taper(sc, [spc(1.0), sp(-2.5, -1.2, -0.3), sp(-5.5, -2.4, -0.7)], 1.0, 0.2, "Shaft")

# skull hub: lumpy oriented bone ellipsoid with dark sockets
HC_ = sp(HUB_U, 1.8)
RA, RE, RF = 8.5, 11.0, 5.0
lat, lon = 9, 14
grid = []
for i in range(lat + 1):
    th = math.pi * i / lat
    row = []
    for j in range(lon):
        ph = 2 * math.pi * j / lon
        lump = 1 + 0.13 * math.sin(3 * th) * math.cos(2 * ph + 0.5) + 0.06 * math.sin(5 * ph)
        row.append(HC_ + ax * (math.cos(th) * RA * lump) + edge * (math.sin(th) * math.cos(ph) * RE * lump)
                   + flat * (math.sin(th) * math.sin(ph) * RF * lump))
    grid.append(row)
ids = [[sc.vert(p, {}) for p in row] for row in grid]
for i in range(lat):
    for j in range(lon):
        sc.face([ids[i][j], ids[i][(j + 1) % lon], ids[i + 1][(j + 1) % lon], ids[i + 1][j]], "Ferrule", HC_)
for fs in (1, -1):
    for (uu, ee, r1, r2) in ((HUB_U + 1.8, 4.6, 1.6, 1.1), (HUB_U - 1.2, 0.2, 1.2, 0.9), (HUB_U + 2.4, -2.6, 1.0, 0.8)):
        c0, c1 = sp(uu, ee, fs * (RF - 0.6)), sp(uu, ee, fs * (RF + 0.5))
        tube(sc, [ring_pts(c0, flat, r1, r2, 7, up=ax), ring_pts(c1, flat, r1, r2, 7, up=ax)], [c0, c1],
             lambda p: {}, lambda r, c: "Hair", cap1=True)
# backward bone horns on the hub
taper(sc, [sp(HUB_U + 2.0, -8.0), sp(HUB_U + 8.0, -19.0), sp(HUB_U + 9.0, -32.0)], 2.4, 0.2, "Ferrule", n=6)
taper(sc, [sp(HUB_U - 2.0, -8.5), sp(HUB_U - 4.0, -17.0), sp(HUB_U - 9.0, -25.0)], 1.9, 0.2, "Ferrule", n=6)
# bone fringe: ribs / vertebrae hanging under the hub
for k in range(7):
    e0 = -4.0 + k * 2.1
    f0 = 1.6 if k % 2 else -1.6
    L = 9.0 + (k % 3) * 4.0
    base = sp(HUB_U - 6.0, e0 * 1.3, f0)
    taper(sc, [base, base - ax * (L * 0.5) - edge * 0.8 + flat * (f0 * 0.4), base - ax * L - edge * 2.0 + flat * (f0 * 0.9)],
          0.95, 0.15, "Ferrule", n=5)
# main blade: long, sweeping forward along +edge, sagging a little toward the tip
BL = 122.0
cl, wd, tk = [], [], []
for i in range(19):
    s_ = i / 18
    cl.append(sp(HUB_U - 2.0 - 16.0 * s_ * s_, 8.0 + BL * s_))
    wd.append(14.0 * (1 - s_) ** 0.8 + 0.6)
    tk.append(0.95 * (1 - 0.65 * s_))
curved_blade(sc, cl, wd, tk, "BladeC")
# three hooked spikes riding the blade's spine (point forward like the main blade)
for s0, L, w0 in ((0.02, 44.0, 7.0), (0.20, 35.0, 5.8), (0.38, 26.0, 4.6)):
    i0 = int(round(s0 * 18))
    base = cl[i0] + ax * (wd[i0] * 0.25)
    pts, ws, ts = [], [], []
    for i in range(9):
        t = i / 8
        ang = math.radians(55 - 45 * t)
        d = edge * math.cos(ang) + ax * math.sin(ang)
        pts.append(base + d * (L * t) - edge * 1.5 * (1 - t))
        ws.append(w0 * (1 - t) ** 0.9 + 0.3)
        ts.append(0.7 * (1 - 0.6 * t))
    curved_blade(sc, pts, ws, ts, "BladeC")
log("scythe verts", len(sc.V), "faces", len(sc.F))


# ---------------------------------------------------------------- build meshes
def make_mesh(name, a, keymap_mat, to_local=None):
    me = bpy.data.meshes.new(name)
    verts = [tuple(to_local @ v) if to_local else tuple(v) for v in a.V]
    me.from_pydata(verts, [], a.F)
    me.update()
    uv = me.uv_layers.new(name="UVMap")
    for p in me.polygons:
        u, v = cell_uv(a.K[p.index])
        for li in p.loop_indices: uv.data[li].uv = (u, v)
        p.use_smooth = False
    return me


def preview_mats(me, a):
    me.materials.clear()
    keys = sorted(set(a.K))
    for k in keys:
        m = bpy.data.materials.get("PV_" + k) or bpy.data.materials.new("PV_" + k)
        _, rgb, _, _, em = PALETTE[k]
        lin = [srgb_to_lin(c) for c in rgb]
        m.diffuse_color = (lin[0], lin[1], lin[2], 1)
        me.materials.append(m)
    for p in me.polygons: p.material_index = keys.index(a.K[p.index])


def atlas_mats(me, a, mats, rim_key=None):
    me.materials.clear()
    for m in mats: me.materials.append(m)
    for p in me.polygons: p.material_index = 1 if (rim_key and a.K[p.index] == rim_key) else 0


bme = make_mesh("Ronin_Body", acc, None)
old = body.data
body.data = bme
bpy.data.meshes.remove(old)
body.vertex_groups.clear()
groups = {}
for vi, w in enumerate(acc.W):
    tot = sum(v for v in w.values() if v > 0) or 1.0
    for bn, wt in w.items():
        if wt <= 1e-4: continue
        if bn not in arm.data.bones: raise RuntimeError("unknown bone " + bn)
        g = groups.get(bn) or body.vertex_groups.new(name=bn)
        groups[bn] = g
        g.add([vi], wt / tot, 'REPLACE')
log("body groups", len(groups))

Minv = M.inverted()
d_loc = (Minv.to_3x3() @ ax).normalized()
e_loc = (Minv.to_3x3() @ edge)
e_loc = (e_loc - d_loc * e_loc.dot(d_loc)).normalized()
# columns x', y'=edge, z'=-snath (the baker reads the weapon along local Z; with +Z it picked the wrong end - angles 180 deg off)
R3 = Matrix((e_loc.cross(-d_loc).normalized(), e_loc, -d_loc)).transposed()
R = R3.to_4x4()
kme = make_mesh("Katana", sc, None, to_local=R.inverted() @ Minv)
oldk = katana.data
katana.data = kme
bpy.data.meshes.remove(oldk)
mw = katana.matrix_world.copy()
katana.matrix_world = mw @ R
bpy.context.view_layer.update()
chk = katana.matrix_world @ kme.vertices[0].co
log("reorient check (should be ~0)", round((chk - (arm.matrix_world @ sc.V[0])).length, 5))
loc = [Vector(v.co) for v in kme.vertices]
mn = [min(v[i] for v in loc) for i in range(3)]
mx = [max(v[i] for v in loc) for i in range(3)]
log("scythe local extents", [round(mx[i] - mn[i], 1) for i in range(3)])


# ---------------------------------------------------------------- previews (workbench, per-key colours)
def load_clip(name):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=ANIMS + name + ".fbx", automatic_bone_orientation=False, primary_bone_axis='Y',
                             secondary_bone_axis='X', axis_forward='-Z', axis_up='Y')
    new = [o for o in bpy.data.objects if o not in before]
    act = None
    for o in new:
        if o.animation_data and o.animation_data.action: act = o.animation_data.action
        if o.type == 'ARMATURE':
            for pb in o.pose.bones:
                if pb.name in arm.pose.bones: arm.pose.bones[pb.name].rotation_mode = pb.rotation_mode
    for o in new: bpy.data.objects.remove(o, do_unlink=True)
    return act


def preview(dirp):
    os.makedirs(dirp, exist_ok=True)
    preview_mats(body.data, acc)
    preview_mats(katana.data, sc)
    s = bpy.context.scene
    s.render.engine = 'BLENDER_WORKBENCH'
    s.display.shading.light = 'STUDIO'
    s.display.shading.color_type = 'MATERIAL'
    s.display.shading.show_shadows = False
    s.display.shading.show_backface_culling = True
    s.render.resolution_x, s.render.resolution_y = 900, 900
    s.world = bpy.data.worlds.new("w")
    cam = bpy.data.cameras.new("c"); cam.type = 'ORTHO'; cam.ortho_scale = 3.0
    co = bpy.data.objects.new("cam", cam); s.collection.objects.link(co); s.camera = co
    arm.animation_data_create()
    shots = [("rest", None, 0.0)]
    for clip, frac in (("idle", 0.3), ("run", 0.3), ("attack_1", 0.25), ("jump_attack", 0.5)):
        shots.append((clip, clip, frac))
    for tag, clip, frac in shots:
        if clip:
            act = load_clip(clip)
            arm.animation_data.action = act
            f0, f1 = act.frame_range
            s.frame_set(int(f0 + (f1 - f0) * frac))
        else:
            arm.animation_data.action = None
            s.frame_set(0)
        for vn, loc, rot in (("front", (0, -6, 1.1), (90, 0, 0)), ("side", (-6, 0, 1.1), (90, 0, -90))):
            co.location = loc; co.rotation_euler = [math.radians(a) for a in rot]
            s.render.filepath = os.path.join(dirp, "%s_%s.png" % (tag, vn))
            bpy.ops.render.render(write_still=True)
    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()
    log("previews", dirp)


if PREVIEW:
    preview(PREVIEW)

atlas_mats(body.data, acc, [MAT_ATLAS, MAT_RIM], rim_key="Rim")
atlas_mats(katana.data, sc, [MAT_BLADE])

# ---------------------------------------------------------------- export (same settings as build_ronin.py)
FBX_KW = dict(add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X', armature_nodetype='NULL',
              apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', use_armature_deform_only=False,
              mesh_smooth_type='FACE', use_mesh_modifiers=True)

if EXPORT:
    write_palette(OUT + "Materials\\")
    if arm.animation_data: arm.animation_data.action = None
    for o in bpy.context.scene.objects: o.select_set(False)
    for o in (arm, body, katana): o.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(filepath=OUT + "ronin.fbx", use_selection=True, object_types={'ARMATURE', 'MESH'},
                             bake_anim=False, **FBX_KW)
    log("exported", OUT + "ronin.fbx")
log("DONE")
