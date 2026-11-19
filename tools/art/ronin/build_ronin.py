# Tech Ronin build script (Blender 5.2, background).
#
#   flatpak run --filesystem=/home/roman/Projects/x --filesystem=/tmp org.blender.Blender \
#       -b --factory-startup -P /home/roman/Projects/x/tools/art/ronin/build_ronin.py -- [--preview DIR] [--no-export]
#
# Imports the Mixamo X Bot (FBX 6.1, read through Blender's ufbx importer wm.fbx_import), dresses it with
# procedurally modelled, skinned outfit meshes (tech kasa with emissive rim, high collar, mask, eyes, duster
# skirt in five panels, obi sash, forearm and shin plates, cyber forearm), adds spring-bone chains for the coat
# panels and the sash end, attaches a katana to the right hand, and exports:
#   Assets/Art3D/Ronin/ronin.fbx                      character (one skinned mesh + katana)
#   Assets/Art3D/Ronin/Animations/<clip>.fbx          clips retargeted onto the same armature (identical hierarchy,
#                                                     so Unity can copy the avatar), incl. derived fall and hang
#   tools/art/ronin/ronin.blend                       the built scene
# All modelling coordinates are armature space: centimetres, +Y up, +Z character front, +X character left.
import bpy, sys, math, os
from mathutils import Vector, Matrix, Quaternion

ROOT = "/home/roman/Projects/x"
SRC = ROOT + "/tools/art/ronin/mixamo/"
OUT = ROOT + "/Assets/Art3D/Ronin/"
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
PREVIEW = args[args.index("--preview") + 1] if "--preview" in args else None
EXPORT = "--no-export" not in args

# ---------------------------------------------------------------- clip table (clean name -> source, loop)
CLIPS = {
    "idle":        ("great sword idle", True),
    "run":         ("great sword run (2)", True),
    "slide_attack": ("great sword slide attack", False),
    "jump":        ("great sword jump", False),
    "jump_attack": ("great sword jump attack", False),
    "attack_1":    ("great sword slash", False),
    "attack_2":    ("great sword slash (3)", False),
    "attack_3":    ("great sword slash (4)", False),
    "spin_attack": ("great sword high spin attack", False),
    "hurt":        ("great sword impact (2)", False),
    "death":       ("two handed sword death", False),
}

# ---------------------------------------------------------------- palette (single source of truth for the colours)
# Modelling uses one Blender material per palette key; before export every face gets a UV at the centre of its
# key's cell and the keys collapse into three materials: Ronin_Atlas (all flat colours, one draw call),
# Ronin_Rim (hat brim band, runtime tier tint) and Ronin_Blade (whole katana; the emission mask lights the blade
# cell only, so the tier tint never reaches the grip). The palette PNGs are written next to the URP materials:
#   Ronin_Palette_Base.png  albedo, sRGB            Ronin_Palette_MS.png  R metallic, A smoothness, linear
#   Ronin_Palette_Emit.png  emission mask, sRGB (Unity multiplies it by Ronin_Atlas _EmissionColor = EMIT_SCALE)
# key: (cell, albedo sRGB 0-255, metallic, smoothness, emission linear 0-1 before EMIT_SCALE)
PALETTE = {
    "Ronin_Coat":   (0,  (52, 62, 92),    0.00, 0.40, None),   # blue-grey charcoal; wet sheen catches the lamps
    "Ronin_Lining": (1,  (150, 24, 36),   0.00, 0.38, None),   # crimson lining / lapel facings
    "Ronin_Under":  (2,  (40, 44, 58),    0.05, 0.30, None),   # techwear trousers, inner layer
    "Ronin_Hood":   (3,  (30, 32, 42),    0.00, 0.20, None),   # head, mask (dark, but not black: eyes read on it)
    "Ronin_Boots":  (4,  (30, 32, 40),    0.20, 0.55, None),
    "Ronin_Armor":  (5,  (66, 72, 84),    0.60, 0.62, None),   # plates, buckles, hat trim
    "Ronin_Cyber":  (6,  (20, 30, 34),    0.00, 0.50, (0.055, 0.34, 0.44)),
    "Ronin_Hat":    (7,  (36, 38, 48),    0.10, 0.45, None),   # dark lacquer
    "Ronin_Eyes":   (8,  (255, 214, 160), 0.00, 0.50, (1.0, 0.64, 0.26)),
    "Ronin_Sash":   (9,  (140, 18, 30),   0.00, 0.32, None),   # obi, neck wrap, scarf
    "Ronin_Wrap":   (10, (92, 90, 88),    0.00, 0.25, None),   # hand and chest wraps, chin cord
    "Ronin_Strap":  (11, (30, 32, 40),    0.15, 0.40, None),   # harness strap
    "Ronin_Tsuba":  (12, (30, 30, 34),    0.60, 0.50, None),
    "Ronin_Grip":   (13, (30, 11, 13),    0.00, 0.25, None),
    "Ronin_BladeC": (14, (255, 255, 255), 0.90, 0.85, (1.0, 1.0, 1.0)),   # katana blade (mask = 1)
    "Ronin_Rim":    (15, (30, 60, 70),    0.00, 0.50, None),   # not atlased; own material (tier tint)
}
EMIT_SCALE = 14.0     # mirrored in Ronin_Atlas _EmissionColor and RoninView.AtlasEmissionScale (Unity)
PAL_N, PAL_CELL = 4, 4   # 4x4 cells of 4x4 texels -> 16x16 PNG


def srgb_to_lin(c):
    c = c / 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def lin_to_srgb8(c):
    c = max(0.0, min(1.0, c))
    s = c * 12.92 if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055
    return int(round(s * 255))


def mat(name):
    m = bpy.data.materials.get(name)
    if m: return m
    m = bpy.data.materials.new(name)
    _, rgb, _, _, em = PALETTE[name]
    lin = [srgb_to_lin(v) for v in rgb]
    if em: lin = [min(1.0, e * 2.0) for e in em]
    m.diffuse_color = (lin[0], lin[1], lin[2], 1)
    return m


def cell_uv(name):
    i = PALETTE[name][0]
    return ((i % PAL_N + 0.5) / PAL_N, (i // PAL_N + 0.5) / PAL_N)


def write_png(path, rows):
    """rows: list (top to bottom) of lists of (r, g, b, a) bytes. Minimal RGBA8 PNG writer (no colour chunks)."""
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
    for name, (i, rgb, metal, smooth, em) in PALETTE.items():
        cx, cy = i % PAL_N, i // PAL_N
        for y in range(cy * PAL_CELL, (cy + 1) * PAL_CELL):
            row = n - 1 - y          # PNG rows run top-down, UV v runs bottom-up
            for x in range(cx * PAL_CELL, (cx + 1) * PAL_CELL):
                base[row][x] = (rgb[0], rgb[1], rgb[2], 255)
                ms[row][x] = (int(round(metal * 255)), 0, 0, int(round(smooth * 255)))
                if em: emit[row][x] = (lin_to_srgb8(em[0]), lin_to_srgb8(em[1]), lin_to_srgb8(em[2]), 255)
    os.makedirs(dirp, exist_ok=True)
    write_png(dirp + "Ronin_Palette_Base.png", base)
    write_png(dirp + "Ronin_Palette_MS.png", ms)
    write_png(dirp + "Ronin_Palette_Emit.png", emit)


def log(*a):
    print("RONIN", *a, flush=True)


# ---------------------------------------------------------------- import X Bot
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=SRC + "X Bot.fbx")
arm = bpy.data.objects["Armature"]
surf = bpy.data.objects["Beta_Surface"]
joints = bpy.data.objects["Beta_Joints"]
B = lambda n: "mixamorig:" + n
REST = {b.name: b.matrix_local.copy() for b in arm.data.bones}


def head(n): return arm.data.bones[B(n)].head_local.copy()


# ---------------------------------------------------------------- measurements from the body surface
SV = [v.co.copy() for v in surf.data.vertices] + [v.co.copy() for v in joints.data.vertices]


def extents(y0, y1, xlim=30.0, xmin=-999):
    pts = [p for p in SV if y0 <= p.y <= y1 and abs(p.x) <= xlim and p.x >= xmin]
    if not pts: return None
    return (min(p.x for p in pts), max(p.x for p in pts), min(p.z for p in pts), max(p.z for p in pts))


head_top = max(p.y for p in SV if abs(p.x) < 12)
face_eye = extents(169, 173, 3)
face_low = extents(160, 166, 3)
headE = extents(168, 178, 14)
neckE = extents(150, 158, 9)
hipE = extents(100, 108, 30)
log("headtop", round(head_top, 1), "face_eye", face_eye, "face_low", face_low, "head", headE, "neck", neckE, "hip", hipE)

# ---------------------------------------------------------------- part builder
PARTS = []


def part(name, verts, faces, weights, fmats, mats, smooth=True):
    """verts: list of Vector; weights: list of {bone: w}; fmats: material index per face into mats."""
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], faces)
    me.update()
    for m in mats: me.materials.append(mat(m))
    for i, p in enumerate(me.polygons):
        p.material_index = fmats[i] if isinstance(fmats, list) else fmats
        p.use_smooth = smooth
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.parent = arm
    groups = {}
    for vi, w in enumerate(weights):
        for bn, wt in w.items():
            if wt <= 0: continue
            g = groups.get(bn) or ob.vertex_groups.new(name=bn)
            groups[bn] = g
            g.add([vi], wt, 'REPLACE')
    mod = ob.modifiers.new("Armature", 'ARMATURE'); mod.object = arm
    PARTS.append(ob)
    return ob


def grid_shell(rows, cols, pos, wfn, thick, mat_out=0, mat_in=1, closed=False, cap_edges=True):
    """Builds a two-layer shell from a param grid. pos(r,c)->(point, outward normal). Returns verts, faces, w, fm.
    mat_out may be a function (r, c) -> material index for the outer face of cell (r, c) (lined edges)."""
    mo = mat_out if callable(mat_out) else (lambda r, c: mat_out)
    V, W, F, FM = [], [], [], []
    for layer in (0, 1):
        for r in range(rows):
            for c in range(cols):
                p, n = pos(r, c)
                V.append(p - n * (thick if layer else 0))
                W.append(wfn(r, c))
    ncol = cols if closed else cols - 1
    off = rows * cols
    for r in range(rows - 1):
        for c in range(ncol):
            a, b = r * cols + c, r * cols + (c + 1) % cols
            d, e = a + cols, b + cols
            F.append((a, d, e, b)); FM.append(mo(r, c))            # outer, faces outward
            F.append((a + off, b + off, e + off, d + off)); FM.append(mat_in)   # inner, reversed
    if cap_edges:
        for r in range(rows - 1):   # bottom/top caps omitted; side edges and bottom hem
            pass
        for c in range(ncol):       # hem (last row)
            a, b = (rows - 1) * cols + c, (rows - 1) * cols + (c + 1) % cols
            F.append((a, b, b + off, a + off)); FM.append(mo(rows - 2, c))
        if not closed:
            for r in range(rows - 1):
                for c in (0, cols - 1):
                    a, d = r * cols + c, (r + 1) * cols + c
                    F.append((a, a + off, d + off, d) if c == 0 else (a, d, d + off, a + off)); FM.append(mo(r, min(c, cols - 2)))
    return V, F, W, FM


def lerpw(y, anchors):
    """anchors: list of (y, bone) sorted by y descending. Linear blend between neighbours."""
    if y >= anchors[0][0]: return {anchors[0][1]: 1.0}
    for (ya, ba), (yb, bb) in zip(anchors, anchors[1:]):
        if ya >= y >= yb:
            t = (ya - y) / (ya - yb)
            w = {}
            w[ba] = w.get(ba, 0) + 1 - t
            w[bb] = w.get(bb, 0) + t
            return w
    return {anchors[-1][1]: 1.0}


# ---------------------------------------------------------------- body: decimate, material zones
def apply_mod(ob, mod):
    with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
        bpy.ops.object.modifier_move_to_index(modifier=mod.name, index=0)
        bpy.ops.object.modifier_apply(modifier=mod.name)


for ob, ratio in ((surf, 0.5), (joints, 0.35)):
    d = ob.modifiers.new("Dec", 'DECIMATE'); d.ratio = ratio
    apply_mod(ob, d)

ZONE = {}
for n in ("Neck", "Head", "HeadTop_End"): ZONE[B(n)] = "Ronin_Hood"
for side in ("Left", "Right"):
    for n in ("UpLeg", "Leg"): ZONE[B(side + n)] = "Ronin_Under"
    for n in ("Foot", "ToeBase", "Toe_End"): ZONE[B(side + n)] = "Ronin_Boots"
    for n in ("Shoulder", "Arm", "ForeArm"): ZONE[B(side + n)] = "Ronin_Under"   # covered by sleeves
for b in arm.data.bones:
    if "Hand" in b.name: ZONE[b.name] = "Ronin_Under"
for n in ("Hips", "Spine", "Spine1", "Spine2"): ZONE[B(n)] = "Ronin_Under"   # inner layer, shows in the coat V

zone_mats = ["Ronin_Coat", "Ronin_Under", "Ronin_Hood", "Ronin_Boots"]
me = surf.data
me.materials.clear()
for m in zone_mats: me.materials.append(mat(m))
gname = {g.index: g.name for g in surf.vertex_groups}
dom = []
for v in me.vertices:
    best, bw = None, -1
    for g in v.groups:
        if g.weight > bw: best, bw = gname[g.group], g.weight
    dom.append(ZONE.get(best, "Ronin_Under"))
for p in me.polygons:
    zs = [dom[i] for i in p.vertices]
    z = max(set(zs), key=zs.count)
    cy = sum(me.vertices[i].co.y for i in p.vertices) / len(p.vertices)
    if z == "Ronin_Coat" and cy < 100: z = "Ronin_Under"
    p.material_index = zone_mats.index(z)
joints.data.materials.clear(); joints.data.materials.append(mat("Ronin_Under"))

# ---------------------------------------------------------------- extra bones
bpy.context.view_layer.objects.active = arm
arm.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
eb = arm.data.edit_bones


def chain(prefix, parent, pts):
    prev = eb[parent]
    names = []
    for i in range(len(pts) - 1):
        b = eb.new("%s_%d" % (prefix, i + 1))
        b.head, b.tail = pts[i], pts[i + 1]
        b.parent = prev
        b.use_connect = i > 0
        prev = b
        names.append(b.name)
    return names


# coat skirt profile: radius ellipse per height, flaring
Y_TOP, Y_BOT = 113.0, 30.0
hx = max(abs(hipE[0]), abs(hipE[1]))


def skirt_r(y):
    t = (Y_TOP - y) / (Y_TOP - Y_BOT)      # 0 top .. 1 hem
    rx = (hx + 1.2) + t * 5.0 + t * t * 12.0
    rz = 14.2 + t * 2.5 + t * t * 7.0
    return rx, rz


Z_C = -1.0   # skirt ellipse centre z


def skirt_pt(theta_deg, y, extra=0.0):
    rx, rz = skirt_r(y)
    th = math.radians(theta_deg)
    p = Vector((math.sin(th) * (rx + extra), y, Z_C + math.cos(th) * (rz + extra)))
    n = Vector((math.sin(th) / rx, 0.25 * (1 if y < Y_TOP - 4 else 0), math.cos(th) / rz)).normalized()
    return p, n


# panels: (name, theta0, theta1, chain parent)
# The front opening widens toward the hem; the back tails are split by two slits. Lined edges (crimson facing
# on the outer side of the edge column) run along the front opening and both slits, so the lining reads at rest.
GAP_TOP, GAP_BOT = 9.0, 30.0
PANELS = [
    ("Coat_FL", None, 92.0, B("LeftUpLeg")),
    ("Coat_FR", -92.0, None, B("RightUpLeg")),
    ("Coat_BL", 86.0, 147.0, B("Hips")),
    ("Coat_BC", 150.0, 210.0, B("Hips")),
    ("Coat_BR", 213.0, 274.0, B("Hips")),
]
LINED = {"Coat_FL": (0,), "Coat_FR": (-1,), "Coat_BL": (-1,), "Coat_BC": (0, -1), "Coat_BR": (0,)}
CH_Y = [108.0, 84.0, 60.0, 36.0, 24.0]
chains = {}
for nm, t0, t1, par in PANELS:
    tc = ((t0 if t0 is not None else GAP_BOT) + (t1 if t1 is not None else -GAP_BOT)) / 2
    pts = [skirt_pt(tc, y, 1.0)[0] for y in CH_Y]
    pts[0] = Vector((pts[0].x * 0.7, CH_Y[0], pts[0].z * 0.7 + Z_C * 0.3))
    chains[nm] = chain(nm, par, pts)

# sash end chain
SASH_TH = 38.0
sash_root = Vector((math.sin(math.radians(SASH_TH)) * (hx + 4.5), 103.0, Z_C + math.cos(math.radians(SASH_TH)) * 17.5))
chains["Sash"] = chain("Sash", B("Hips"), [sash_root, sash_root + Vector((0.5, -14, 1.2)), sash_root + Vector((1.0, -28, 1.8))])
# scarf tail chain: from the back of the neck down over the coat's back
SCARF_Y = [151.0, 137.0, 123.0, 109.0, 95.0]


def back_z(y):
    e = extents(y - 2.0, y + 2.0, 17.5 if y > 136 else 22.0)
    return e[2] - (2.4 if y < 146 else 1.2) - 2.2


SCARF_PTS = [Vector((3.5, y, back_z(min(y, 147.0)) - (1.0 if y > 146 else 0.0))) for y in SCARF_Y]
chains["Scarf"] = chain("Scarf", B("Spine2"), SCARF_PTS)
bpy.ops.object.mode_set(mode='OBJECT')
EXTRA_BONES = [n for v in chains.values() for n in v]
log("extra bones", EXTRA_BONES)

# ---------------------------------------------------------------- coat skirt panels
ROWS = 14
for nm, t0, t1, par in PANELS:
    cols = 7
    b1, b2, b3, b4 = chains[nm]
    top_anchor = B("Hips")

    def pos(r, c, t0=t0, t1=t1):
        y = Y_TOP - (Y_TOP - Y_BOT) * r / (ROWS - 1)
        t = (Y_TOP - y) / (Y_TOP - Y_BOT)
        gap = GAP_TOP + (GAP_BOT - GAP_TOP) * t
        a0 = t0 if t0 is not None else gap
        a1 = t1 if t1 is not None else -gap
        th = a0 + (a1 - a0) * c / (cols - 1)
        return skirt_pt(th, y)

    def wfn(r, c):
        y = Y_TOP - (Y_TOP - Y_BOT) * r / (ROWS - 1)
        return lerpw(y, [(Y_TOP - 2, top_anchor), (96.0, b1), (72.0, b2), (48.0, b3), (30.0, b4)])

    def fmo(r, c, nm=nm):
        edges = [0 if e == 0 else cols - 2 for e in LINED.get(nm, ())]
        return 1 if c in edges else 0

    V, F, W, FM = grid_shell(ROWS, cols, pos, wfn, 0.9, mat_out=fmo)
    part(nm, V, F, W, FM, ["Ronin_Coat", "Ronin_Lining"])

# ---------------------------------------------------------------- torso coat shell (continuous duster silhouette)
TR_Y = [150.0, 147.0, 143.0, 138.0, 132.0, 126.0, 120.0, 114.0, 108.0]   # top-down: rows must run downward for outward winding
TCOLS = 24


def torso_ext(y):
    xl = 17.5 if y > 136 else 22.0
    e = extents(y - 2.0, y + 2.0, xl)
    return e


def t_gap(y):
    """Half-angle of the front opening: a V from the collar narrowing to the belt."""
    t = max(0.0, min(1.0, (y - 108.0) / 42.0))
    return 9.0 + 19.0 * t


def tpos(r, c):
    y = TR_Y[r]
    e = torso_ext(y)
    g = t_gap(y)
    th = math.radians(g + (360.0 - 2 * g) * c / (TCOLS - 1))
    cx, czz = 0.0, (e[2] + e[3]) / 2
    rx = max(abs(e[0]), abs(e[1])) + (2.6 if y < 146 else 0.5)
    rz = (e[3] - e[2]) / 2 + 2.4
    if y >= 146: rx = min(rx, 20.0)
    p = Vector((math.sin(th) * rx, y, czz + math.cos(th) * rz))
    return p, Vector((math.sin(th), 0, math.cos(th)))


def twt(r, c):
    y = TR_Y[r]
    w = lerpw(y, [(140, B("Spine2")), (128, B("Spine1")), (118, B("Spine")), (108, B("Hips"))])
    g = t_gap(y)
    th = math.radians(g + (360.0 - 2 * g) * c / (TCOLS - 1))
    sx = math.sin(th)
    if y >= 138 and abs(sx) > 0.6:
        k = (abs(sx) - 0.6) / 0.4 * 0.6 * min(1.0, (y - 138) / 8)
        w = {b: v * (1 - k) for b, v in w.items()}
        w[B("LeftShoulder" if sx > 0 else "RightShoulder")] = k
    return w


V, F, W, FM = grid_shell(len(TR_Y), TCOLS, tpos, twt, 0.6, lambda r, c: 1 if c in (0, TCOLS - 2) else 0, 1)
part("CoatTorso", V, F, W, FM, ["Ronin_Coat", "Ronin_Lining"])

# ---------------------------------------------------------------- high collar
nk = head("Neck")
cz = (neckE[2] + neckE[3]) / 2
nrx, nrz = max(abs(neckE[0]), abs(neckE[1])), (neckE[3] - neckE[2]) / 2
CROWS, CCOLS = 6, 22


def cpos(r, c):
    t = 1.0 - r / (CROWS - 1)      # rows run top-down (outward winding)
    y = 142.0 + t * 21.0
    gap = 18.0 + 30.0 * t
    th = math.radians(gap + (360 - 2 * gap) * c / (CCOLS - 1))
    rx = nrx + 4.5 - 2.5 * t + 2.5 * t * t
    rz = nrz + 3.5 - 1.5 * t + 2.5 * t * t
    p = Vector((math.sin(th) * rx, y, cz - 1.5 + math.cos(th) * rz))
    n = Vector((math.sin(th), -0.2, math.cos(th))).normalized()
    return p, n


V, F, W, FM = grid_shell(CROWS, CCOLS, cpos, lambda r, c: lerpw(163 - 21 * r / (CROWS - 1), [(158, B("Neck")), (146, B("Spine2"))]), 0.8)
part("Collar", V, F, W, FM, ["Ronin_Coat", "Ronin_Lining"])

# ---------------------------------------------------------------- mask (lower face) and eyes
hz = (headE[2] + headE[3]) / 2
hrx, hrz = max(abs(headE[0]), abs(headE[1])), (headE[3] - headE[2]) / 2
MR, MC = 5, 14


def mpos(r, c):
    t = r / (MR - 1)
    y = 157.0 + t * 11.0
    th = math.radians(-105 + 210 * c / (MC - 1))
    e = extents(y - 1.5, y + 1.5, 14)
    rx = max(abs(e[0]), abs(e[1])) + 0.9
    rzf = e[3] - hz + 0.9
    p = Vector((math.sin(th) * rx, y, hz + math.cos(th) * rzf))
    return p, Vector((math.sin(th), 0, math.cos(th)))


V, F, W, FM = grid_shell(MR, MC, mpos, lambda r, c: {B("Head"): 1.0}, 0.4)
part("Mask", V, F, W, FM, ["Ronin_Hood", "Ronin_Hood"])

# Eyes: two curved emissive shards standing ~2 cm in front of the face, just above the mask top (y 168) and well
# under the brim's shadow line, slanted up at the outer end. Curving around the face gives them depth; the size
# (about 6 x 2.6 cm each) makes them 1-2 bright pixels at the 640x360 game framing from the 3-degree side camera.
# v3: 1 cm lower, ~1.2 cm further forward (the outer ends sat behind the face surface) and ~35% larger.
EYE_Y = 169.4
EYE_FWD = 2.2          # cm in front of the face surface at the inner end
EYE_CURVE = 0.12       # cm pulled back per cm toward the outer end (follows the face curvature)


def face_z(x, y):
    pts = [p.z for p in SV if abs(p.y - y) < 1.0 and abs(p.x - x) < 0.8]
    return max(pts)


V, F = [], []
for s_ in (-1, 1):
    xs = [s_ * 1.2, s_ * 2.7, s_ * 4.2, s_ * 5.6]
    hs = [0.75, 1.0, 1.3, 1.0]             # half height, thickest toward the outer end
    lift = [-0.4, -0.05, 0.35, 0.7]        # slant: outer end higher
    i0 = len(V)
    for x, hh, ly in zip(xs, hs, lift):
        z = face_z(x, EYE_Y) + EYE_FWD - EYE_CURVE * abs(x)
        for dy, dz in ((-hh, 0.35), (hh, 0.35), (hh, -0.35), (-hh, -0.35)):
            V.append(Vector((x, EYE_Y + ly + dy, z + dz)))
    for k in range(len(xs) - 1):
        for j in range(4):
            j2 = (j + 1) % 4
            q = (i0 + k * 4 + j, i0 + k * 4 + j2, i0 + (k + 1) * 4 + j2, i0 + (k + 1) * 4 + j)
            F.append(q if s_ > 0 else q[::-1])
    F.append(tuple(i0 + j for j in range(4))[::-1] if s_ > 0 else tuple(i0 + j for j in range(4)))
    e0 = i0 + (len(xs) - 1) * 4
    F.append(tuple(e0 + j for j in range(4)) if s_ > 0 else tuple(e0 + j for j in range(4))[::-1])
part("Eyes", V, F, [{B("Head"): 1.0}] * len(V), 0, ["Ronin_Eyes"], smooth=False)

# ---------------------------------------------------------------- tech kasa (hat) with emissive rim
HAT_R = 29.0          # v3: was 37 (a hat on legs); ~22 % smaller so the shoulders and coat read
BRIM_Y = 174.6
APEX_Y = BRIM_Y + 15.0
IN_APEX = head_top + 1.5
SEG = 28
hat_c = Vector((0, 0, hz - 0.5))
V, F, FM = [], [], []
rings = [(HAT_R, BRIM_Y + 0.4), (HAT_R * 0.72, BRIM_Y + 5.2), (HAT_R * 0.40, BRIM_Y + 11.5), (HAT_R * 0.12, APEX_Y - 1.2), (0.0, APEX_Y)]
inner = [(HAT_R - 0.3, BRIM_Y - 0.8), (HAT_R * 0.7, BRIM_Y + 1.5), (HAT_R * 0.35, IN_APEX - 3.5), (0.0, IN_APEX)]


def ring_verts(r, y):
    return [Vector((hat_c.x + math.sin(2 * math.pi * k / SEG) * r, y, hat_c.z + math.cos(2 * math.pi * k / SEG) * r)) for k in range(SEG)]


def add_rings(rs, outward):
    base = len(V)
    idx = []
    for r, y in rs:
        if r == 0:
            V.append(Vector((hat_c.x, y, hat_c.z))); idx.append([len(V) - 1] * SEG)
        else:
            s = len(V); V.extend(ring_verts(r, y)); idx.append(list(range(s, s + SEG)))
    for a, b in zip(idx, idx[1:]):
        for k in range(SEG):
            k2 = (k + 1) % SEG
            q = [a[k], a[k2], b[k2], b[k]]
            if not outward: q.reverse()
            q2 = []
            for v in q:
                if v not in q2: q2.append(v)
            F.append(tuple(q2)); FM.append(0)
    return idx


oi = add_rings(rings, True)
ii = add_rings(inner, False)
for k in range(SEG):   # brim edge (outer ring 0 to inner ring 0) -> emissive rim band
    k2 = (k + 1) % SEG
    F.append((oi[0][k], ii[0][k], ii[0][k2], oi[0][k2])); FM.append(1)
# finial
s = len(V)
for r, y in ((1.2, APEX_Y - 0.5), (1.2, APEX_Y + 2.0)):
    V.extend([Vector((hat_c.x + math.sin(2 * math.pi * k / 8) * r, y, hat_c.z + math.cos(2 * math.pi * k / 8) * r)) for k in range(8)])
for k in range(8):
    F.append((s + k, s + (k + 1) % 8, s + 8 + (k + 1) % 8, s + 8 + k)); FM.append(0)
F.append(tuple(range(s + 8, s + 16))); FM.append(0)
# tilted back 5 degrees about the head (v3, was 6 forward): the front of the brim rides above the eye line so the
# face and the eye glints read under it
HAT_TILT = -5.0
tilt = Matrix.Translation(Vector((0, BRIM_Y, hat_c.z))) @ Matrix.Rotation(math.radians(HAT_TILT), 4, 'X') @ Matrix.Translation(Vector((0, -BRIM_Y, -hat_c.z)))
V = [tilt @ v for v in V]
part("Hat", V, F, [{B("Head"): 1.0}] * len(V), FM, ["Ronin_Hat", "Ronin_Rim"], smooth=False)
# emissive rim band: a ring just outside the brim edge so it reads as a line from any angle. It must stay at
# least ~1.5 render texels thick in game (1 texel ~3.4 cm on the model at 40 texels/unit), or it breaks into
# sparkling dots at 480x270: 1.6 cm (the first version) flickered, so it is ~5 cm tall and ~2.5 cm deep.
V, F = [], []
for k in range(SEG):
    a = 2 * math.pi * k / SEG
    for dr, dy in ((0.9, -2.2), (0.9, 2.8), (-1.6, 2.8), (-1.6, -2.2)):
        r = HAT_R + dr
        V.append(tilt @ Vector((hat_c.x + math.sin(a) * r, BRIM_Y + dy, hat_c.z + math.cos(a) * r)))
for k in range(SEG):
    k2 = (k + 1) % SEG
    for j in range(4):
        j2 = (j + 1) % 4
        F.append((k * 4 + j, k2 * 4 + j, k2 * 4 + j2, k * 4 + j2))
part("HatRim", V, F, [{B("Head"): 1.0}] * len(V), 0, ["Ronin_Rim"], smooth=False)


def cone_pt(a, t, lift=0.0):
    """Point on the outer cone at angle a, t = 0 brim .. 1 near the apex, lifted along the surface normal."""
    prof = rings[:4]
    seg = min(int(t * 3), 2)
    u = t * 3 - seg
    (r0, y0), (r1, y1) = prof[seg], prof[seg + 1]
    r, y = r0 + (r1 - r0) * u, y0 + (y1 - y0) * u
    n = Vector((math.sin(a) * (y1 - y0), r0 - r1, math.cos(a) * (y1 - y0))).normalized()
    return Vector((hat_c.x + math.sin(a) * r, y, hat_c.z + math.cos(a) * r)) + n * lift, n


# ribs: 12 low ridges from the brim toward the apex (form, not texture); trim ring: a raised band at the first
# profile break, in the armour metal, so the cone reads as built rather than a plain cone
V, F, FM = [], [], []
for k in range(12):
    a = 2 * math.pi * (k + 0.5) / 12
    side = Vector((math.cos(a), 0, -math.sin(a)))
    i0 = len(V)
    ts = [0.02, 0.3, 0.6, 0.9]
    for t in ts:
        p, n = cone_pt(a, t)
        w = 0.55 * (1 - 0.6 * t)
        V += [p + side * w + n * 0.05, p + n * 0.55, p - side * w + n * 0.05]
    for j in range(len(ts) - 1):
        b0, b1 = i0 + j * 3, i0 + (j + 1) * 3
        F += [(b0, b1, b1 + 1, b0 + 1), (b0 + 1, b1 + 1, b1 + 2, b0 + 2)]
        FM += [0, 0]
i0 = len(V)
TR = 64
for k in range(TR):
    a = 2 * math.pi * k / TR
    p, n = cone_pt(a, 0.36)
    dn = Vector((0, 1, 0)) - n * n.dot(Vector((0, 1, 0)))
    dn.normalize()
    V += [p - dn * 0.7 + n * 0.05, p - dn * 0.5 + n * 0.6, p + dn * 0.5 + n * 0.6, p + dn * 0.7 + n * 0.05]
for k in range(TR):
    k2 = (k + 1) % TR
    for j in range(3):
        F.append((i0 + k * 4 + j, i0 + k * 4 + j + 1, i0 + k2 * 4 + j + 1, i0 + k2 * 4 + j)); FM.append(1)
V = [tilt @ v for v in V]
part("HatRibs", V, F, [{B("Head"): 1.0}] * len(V), FM, ["Ronin_Hat", "Ronin_Armor"], smooth=False)

# chin cord: a thin V from under the brim at the temples to below the chin
cord = []
for s_ in (1, -1):
    cord.append([Vector((s_ * (hrx - 0.8), BRIM_Y - 2.5, hz - 1.0)), Vector((s_ * (hrx - 0.2), 166.0, hz + 2.0)),
                 Vector((s_ * (hrx - 2.0), 160.5, hz + 5.0)), Vector((s_ * 3.0, 156.5, hz + 7.5)), Vector((0, 155.8, hz + 8.2))])
V, F = [], []
for path in cord:
    i0 = len(V)
    for j, p in enumerate(path):
        d = (path[min(j + 1, len(path) - 1)] - path[max(j - 1, 0)]).normalized()
        u = d.cross(Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0))).normalized()
        w = d.cross(u)
        for k in range(4):
            a = math.pi * 0.5 * k
            V.append(p + (u * math.cos(a) + w * math.sin(a)) * 0.32)
    for j in range(len(path) - 1):
        for k in range(4):
            k2 = (k + 1) % 4
            F.append((i0 + j * 4 + k, i0 + j * 4 + k2, i0 + (j + 1) * 4 + k2, i0 + (j + 1) * 4 + k))
part("ChinCord", V, F, [{B("Head"): 1.0}] * len(V), 0, ["Ronin_Wrap"], smooth=True)

# ---------------------------------------------------------------- obi sash + hanging end
SR, SC = 4, 26


def spos(r, c):
    y = 99.0 + 10.0 * r / (SR - 1)
    th = math.radians(360.0 * c / SC)
    rx, rz = skirt_r(Y_TOP)
    p = Vector((math.sin(th) * (rx + 1.6), y, Z_C + math.cos(th) * (rz + 1.6)))
    return p, Vector((math.sin(th), 0, math.cos(th)))


V, F, W, FM = grid_shell(SR, SC, spos, lambda r, c: lerpw(99 + 10 * r / (SR - 1), [(108, B("Spine")), (101, B("Hips"))]), 1.2, 0, 0, closed=True)
part("Sash", V, F, W, FM, ["Ronin_Sash"])
s1, s2 = chains["Sash"]
dirx = Vector((math.cos(math.radians(SASH_TH)), 0, -math.sin(math.radians(SASH_TH))))
nrm = Vector((math.sin(math.radians(SASH_TH)), 0, math.cos(math.radians(SASH_TH))))


def epos(r, c):
    y = 104.0 - 34.0 * r / 8
    w = 3.4 - 0.8 * r / 8
    base = sash_root + Vector((0, y - sash_root.y, 0)) + nrm * (0.8 + 0.25 * r / 8)
    return base + dirx * (w * (c - 0.5) * 2), nrm


V, F, W, FM = grid_shell(9, 2, epos, lambda r, c: lerpw(104 - 34 * r / 8, [(102, B("Hips")), (93, s1), (78, s2)]), 0.6, 0, 0)
part("SashEnd", V, F, W, FM, ["Ronin_Sash"])


# ---------------------------------------------------------------- armour plates
def tube_plate(name, a, b, r0, r1, ang0, ang1, up, bones, mats, cols=7, rows=4, thick=0.7, mat_in=0):
    ax = (b - a).normalized()
    side = up.cross(ax).normalized()
    upn = ax.cross(side).normalized()

    def pos(rr, c):
        t = rr / (rows - 1)
        th = math.radians(ang0 + (ang1 - ang0) * c / (cols - 1))
        rad = r0 + (r1 - r0) * t
        n = upn * math.cos(th) + side * math.sin(th)
        return a + (b - a) * t + n * rad, n

    V, F, W, FM = grid_shell(rows, cols, pos, lambda rr, c: bones, thick, 0, mat_in, closed=(ang1 - ang0) >= 359)
    return part(name, V, F, W, FM, mats)


for side, sx in (("Left", 1), ("Right", -1)):
    fa, hd = head(side + "ForeArm"), head(side + "Hand")
    a = fa + (hd - fa) * 0.22
    b = fa + (hd - fa) * 0.93
    if side == "Left":   # cybernetic forearm: full sleeve + light lines
        tube_plate("CyberArm", a, b, 5.0, 4.2, 0, 360, Vector((0, 1, 0)), {B(side + "ForeArm"): 1.0}, ["Ronin_Armor"], cols=13, rows=4, thick=0.5)
        for k, ang in enumerate((25, 150, 265)):
            tube_plate("CyberLine%d" % k, a + (b - a) * 0.08, b - (b - a) * 0.05, 5.15, 4.35, ang, ang + 9, Vector((0, 1, 0)),
                       {B(side + "ForeArm"): 1.0}, ["Ronin_Cyber"], cols=2, rows=2, thick=0.1)
    else:
        tube_plate("ArmPlate" + side, a, b, 6.6, 5.9, -80, 80, Vector((0, 1, 0)), {B(side + "ForeArm"): 1.0}, ["Ronin_Armor"])
    lg, ft = head(side + "Leg"), head(side + "Foot")
    a = lg + (ft - lg) * 0.08
    b = lg + (ft - lg) * 0.80
    a = lg + (ft - lg) * 0.30
    tube_plate("ShinPlate" + side, a, b, 8.0, 7.2, -60, 60, Vector((0, 0, 1)), {B(side + "Leg"): 1.0}, ["Ronin_Armor"], rows=4)



# ---------------------------------------------------------------- clothing over the mannequin limbs
def frame_of(ax):
    ref = Vector((0, 1, 0)) if abs(ax.y) < 0.9 else Vector((0, 0, 1))
    u = (ref - ax * ax.dot(ref)).normalized()
    return u, ax.cross(u).normalized()


def measure(p, ax, u, w, rmax, xsign=0, slab=1.5):
    lu = lw = 1e9; hu = hw = -1e9; n = 0
    for v in SV:
        d = v - p
        if abs(d.dot(ax)) > slab: continue
        if xsign and v.x * xsign < 2.0: continue
        du, dw = d.dot(u), d.dot(w)
        if du * du + dw * dw > rmax * rmax: continue
        lu, hu, lw, hw = min(lu, du), max(hu, du), min(lw, dw), max(hw, dw); n += 1
    if n < 3: return 0.0, 0.0, 3.0, 3.0
    return (lu + hu) / 2, (lw + hw) / 2, (hu - lu) / 2, (hw - lw) / 2


def chain_pt(bones, s):
    """Point along a bone chain: s in [0, len) -> head of bone int(s) lerped to its tail."""
    i = min(int(s), len(bones) - 1)
    bb = arm.data.bones[B(bones[i])]
    return bb.head_local + (bb.tail_local - bb.head_local) * (s - i)


def chain_w(bones, s, blend=0.12, top=None):
    i = min(int(s), len(bones) - 1)
    f = s - i
    w = {B(bones[i]): 1.0}
    if f < blend and i > 0:
        k = 0.5 - f / blend * 0.5; w = {B(bones[i - 1]): k, B(bones[i]): 1 - k}
    elif f > 1 - blend and i + 1 < len(bones):
        k = (f - (1 - blend)) / blend * 0.5; w = {B(bones[i]): 1 - k, B(bones[i + 1]): k}
    if top and s < 0.15:
        k = 0.5 * (1 - s / 0.15)
        w = {b: v * (1 - k) for b, v in w.items()}; w[top] = w.get(top, 0) + k
    return w


def limb_tube(name, bones, ss, margin, matkey, rmax, xsign, top=None, cols=12, ring_mat=None, flat=1.0):
    """Closed tube around a limb chain. ss: sample params; margin(s) -> extra radius (cm) over the body."""
    V, F, W, FM = [], [], [], []
    rows = []
    for s_ in ss:
        p = chain_pt(bones, s_)
        ax = (chain_pt(bones, s_ + 0.02) - chain_pt(bones, s_ - 0.02)).normalized()
        u, w = frame_of(ax)
        cu, cw, ru, rw = measure(p, ax, u, w, rmax, xsign)
        m = margin(s_)
        c = p + u * cu + w * cw
        rows.append(s_)
        for k in range(cols):
            a = 2 * math.pi * k / cols
            V.append(c + u * math.cos(a) * (ru + m) + w * math.sin(a) * (rw * flat + m)); W.append(chain_w(bones, s_, top=top))
    for r in range(len(ss) - 1):
        for k in range(cols):
            k2 = (k + 1) % cols
            a_, b_ = r * cols + k, r * cols + k2
            F.append((a_, b_, b_ + cols, a_ + cols)); FM.append(ring_mat(rows[r]) if ring_mat else 0)
    mats = [matkey] + ([ "Ronin_Armor", "Ronin_Lining", "Ronin_Wrap"] if ring_mat else [])
    return part(name, V, F, W, FM, mats)


LEG = ["UpLeg", "Leg", "Foot"]
ARM = ["Arm", "ForeArm", "Hand"]
for side, sx in (("Left", 1), ("Right", -1)):
    lb = [side + n for n in LEG]
    # techwear trousers: loose at the knee, tapering into the boot
    limb_tube("Trousers" + side, lb, [0.12, 0.25, 0.4, 0.55, 0.7, 0.82, 0.9, 0.96, 1.04, 1.1, 1.2, 1.3, 1.42],
              lambda s_: 1.4 + 1.0 * math.exp(-((s_ - 1.0) / 0.25) ** 2) - (0.6 if s_ > 1.3 else 0.0),
              "Ronin_Under", 12.0, sx, top=B("Hips"))
    # boot shaft over the trouser hem, strap band near the top
    limb_tube("Boot" + side, lb, [1.28, 1.32, 1.4, 1.55, 1.7, 1.82, 1.92],
              lambda s_: 1.9 + (0.5 if s_ < 1.33 else 0.0), "Ronin_Boots", 9.0, sx,
              ring_mat=lambda s_: 1 if 1.38 <= s_ < 1.5 else 0)
    ab = [side + n for n in ARM]
    if side == "Left":   # sleeve torn at the elbow, the cyber forearm is bare
        limb_tube("Sleeve" + side, ab, [-0.12, 0.05, 0.3, 0.6, 0.85, 0.97, 1.05],
                  lambda s_: 1.5 + (0.4 if s_ > 0.95 else 0.0), "Ronin_Coat", 9.0, sx, top=B(side + "Shoulder"),
                  ring_mat=lambda s_: 2 if s_ >= 0.95 else 0)
    else:
        limb_tube("Sleeve" + side, ab, [-0.12, 0.05, 0.3, 0.6, 0.85, 0.95, 1.05, 1.3, 1.6, 1.8, 1.9],
                  lambda s_: 1.5 + (0.8 if s_ > 1.75 else 0.0), "Ronin_Coat", 9.0, sx, top=B(side + "Shoulder"),
                  ring_mat=lambda s_: 2 if s_ >= 1.75 else 0)
    # wrapped hands: palm and back of the hand to the knuckles
    limb_tube("HandWrap" + side, ab, [1.86, 1.95, 2.1, 2.35, 2.6, 2.85, 2.98], lambda s_: 0.55, "Ronin_Wrap", 5.0, sx)

# chest wrap (inner layer, visible in the coat V) and neck wrap
def ell_tube(name, ys, rfn, wfn, matkey, cols=20):
    V, F, W = [], [], []
    for y in ys:
        cx, cz, rx, rz = rfn(y)
        for k in range(cols):
            a = 2 * math.pi * k / cols
            V.append(Vector((cx + math.sin(a) * rx, y, cz + math.cos(a) * rz))); W.append(wfn(y))
    for r in range(len(ys) - 1):
        for k in range(cols):
            k2 = (k + 1) % cols
            a_, b_ = r * cols + k, r * cols + k2
            F.append((a_, a_ + cols, b_ + cols, b_))
    return part(name, V, F, W, 0, [matkey])


def chest_r(y):
    e = torso_ext(y)
    return 0.0, (e[2] + e[3]) / 2, max(abs(e[0]), abs(e[1])) + 0.7, (e[3] - e[2]) / 2 + 0.7


ell_tube("ChestWrap", [121.0, 127.0, 133.0, 139.0, 143.0], chest_r,
         lambda y: lerpw(y, [(140, B("Spine2")), (128, B("Spine1")), (118, B("Spine"))]), "Ronin_Wrap")
ell_tube("NeckWrap", [146.0, 150.0, 154.0, 158.5], lambda y: (0.0, cz - 0.5, nrx + 2.2 - (y - 146) * 0.06, nrz + 2.2 - (y - 146) * 0.06),
         lambda y: lerpw(y, [(155, B("Neck")), (148, B("Spine2"))]), "Ronin_Sash")

# harness strap over the coat: left shoulder -> right hip, with a buckle at the sternum
def coat_front(x, y):
    e = torso_ext(y)
    czz = (e[2] + e[3]) / 2
    rx = max(abs(e[0]), abs(e[1])) + (2.6 if y < 146 else 0.5)
    if y >= 146: rx = min(rx, 20.0)
    rz = (e[3] - e[2]) / 2 + 2.4
    q = max(0.0, 1 - (x / rx) ** 2)
    return czz + rz * math.sqrt(q)


V, F, W = [], [], []
N = 10
p0, p1 = Vector((13.0, 147.0, 0)), Vector((-16.0, 110.0, 0))
dirv = (p1 - p0).normalized()
perp = Vector((-dirv.y, dirv.x, 0))
for i in range(N + 1):
    p = p0 + (p1 - p0) * (i / N)
    for e_ in (-2.0, 2.0):
        q = p + perp * e_
        q.z = coat_front(q.x, q.y) + 0.7
        V.append(q); W.append(lerpw(q.y, [(140, B("Spine2")), (128, B("Spine1")), (118, B("Spine")), (108, B("Hips"))]))
for i in range(N):
    F.append((i * 2, i * 2 + 2, i * 2 + 3, i * 2 + 1))
part("Strap", V, F, W, 0, ["Ronin_Strap"], smooth=False)
bc = p0 + (p1 - p0) * 0.32
bz = coat_front(bc.x, bc.y) + 1.0
V = [Vector((bc.x + dx, bc.y + dy, bz + dz)) for dz in (0.0, 0.8) for dx, dy in ((-2.4, -2.4), (2.4, -2.4), (2.4, 2.4), (-2.4, 2.4))]
F = [(4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
part("Buckle", V, F, [lerpw(bc.y, [(140, B("Spine2")), (128, B("Spine1"))])] * 8, 0, ["Ronin_Armor"], smooth=False)

# long tattered scarf tail on the Scarf chain (notched, slightly skewed end)
sc_b = chains["Scarf"]
SR_ = 14


def scpos(r, c):
    t = r / (SR_ - 1)
    y = SCARF_Y[0] + (SCARF_Y[-1] - SCARF_Y[0]) * t
    seg = min(int(t * 4), 3); u = t * 4 - seg
    p = SCARF_PTS[seg] + (SCARF_PTS[seg + 1] - SCARF_PTS[seg]) * u
    w = 5.0 - 1.8 * t
    x = p.x + (c - 1) * w
    yy = p.y
    if r == SR_ - 1: yy += (7.0 if c == 1 else (-2.5 if c == 0 else 0.0))
    return Vector((x, yy, p.z)), Vector((0, 0, -1))


def scw(r, c):
    y = SCARF_Y[0] + (SCARF_Y[-1] - SCARF_Y[0]) * r / (SR_ - 1)
    return lerpw(y, [(149.0, B("Spine2")), (137.0, sc_b[0]), (123.0, sc_b[1]), (109.0, sc_b[2]), (96.0, sc_b[3])])


V, F, W, FM = grid_shell(SR_, 3, scpos, scw, 0.5, 0, 0)
part("Scarf", V, F, W, FM, ["Ronin_Sash"])

# ---------------------------------------------------------------- join the body into one skinned mesh
def join(objs, name):
    with bpy.context.temp_override(active_object=objs[0], selected_editable_objects=objs, selected_objects=objs, object=objs[0]):
        bpy.ops.object.join()
    objs[0].name = name
    objs[0].data.name = name
    return objs[0]


body = join([surf, joints] + PARTS, "Ronin_Body")
for m in list(body.modifiers):
    if m.type == 'ARMATURE' and m.name != body.modifiers[0].name: body.modifiers.remove(m)


def atlas(ob, slots):
    """Collapses the palette-key materials of ob into `slots` (final material names). Every face gets the UV of its
    key's palette cell; faces whose key is a slot name keep their own material (Ronin_Rim), the rest go to slot 0."""
    me = ob.data
    keys = [m.name for m in me.materials]
    while me.uv_layers: me.uv_layers.remove(me.uv_layers[0])
    uvl = me.uv_layers.new(name="UVMap")
    idx = []
    for p in me.polygons:
        k = keys[p.material_index]
        uv = cell_uv(k)
        for li in p.loop_indices: uvl.data[li].uv = uv
        idx.append(slots.index(k) if k in slots else 0)
    me.materials.clear()
    for nme in slots:
        m = bpy.data.materials.get(nme) or bpy.data.materials.new(nme)
        me.materials.append(m)
    me.polygons.foreach_set("material_index", idx)
    me.update()

log("body verts", len(body.data.vertices), "tris", sum(len(p.vertices) - 2 for p in body.data.polygons), "mats", [m.name for m in body.data.materials])


# ---------------------------------------------------------------- clip import / retarget helper
def load_action(src_name, new_name):
    before = set(bpy.data.objects)
    bpy.ops.wm.fbx_import(filepath=SRC + src_name + ".fbx")
    new = [o for o in bpy.data.objects if o not in before]
    a = [o for o in new if o.type == 'ARMATURE'][0]
    act = a.animation_data.action
    act.name = new_name
    act.use_fake_user = True
    for o in new: bpy.data.objects.remove(o)
    return act


def use_action(act):
    if arm.animation_data is None: arm.animation_data_create()
    arm.animation_data.action = act
    if hasattr(arm.animation_data, "action_slot") and len(act.slots):
        arm.animation_data.action_slot = act.slots[0]
    f0, f1 = act.frame_range
    bpy.context.scene.frame_start, bpy.context.scene.frame_end = int(f0), int(f1)


bpy.context.scene.render.fps = 30
ACTS = {k: load_action(v[0], k) for k, v in CLIPS.items()}
INPLACE = ("run", "slide_attack", "jump", "jump_attack", "spin_attack", "attack_3")


def remove_forward_drift(act):
    """Mixamo clips here carry forward root motion (slide 3.5 m). The sim moves the character, so the hips'
    forward (armature +Z) travel is subtracted per frame; lateral sway and height stay in the pose."""
    use_action(act)
    hips = arm.pose.bones[B("Hips")]
    f0, f1 = map(int, act.frame_range)
    bpy.context.scene.frame_set(f0)
    z0 = hips.matrix.translation.z
    for f in range(f0, f1 + 1):
        bpy.context.scene.frame_set(f)
        m = hips.matrix.copy()
        m.translation.z = z0
        hips.matrix = m
        bpy.context.view_layer.update()
        hips.keyframe_insert("location", frame=f)


for k in INPLACE: remove_forward_drift(ACTS[k])

# ---------------------------------------------------------------- katana grip from the idle + slash poses
PB = arm.pose.bones


def fist(side):
    pts = [PB[B(side + n)].head for n in ("Hand", "HandIndex2", "HandMiddle2", "HandRing2", "HandThumb2")]
    return sum(pts, Vector()) / len(pts)


samples = []
for clip in ("idle", "attack_1", "run"):
    use_action(ACTS[clip])
    f0, f1 = ACTS[clip].frame_range
    for f in range(int(f0), int(f1) + 1, 3):
        bpy.context.scene.frame_set(f)
        inv = PB[B("RightHand")].matrix.inverted()
        samples.append((inv @ fist("Right"), inv @ fist("Left"), PB[B("RightHand")].matrix.to_3x3() @ Vector((0, 0, 1))))
rg = sum((s[0] for s in samples), Vector()) / len(samples)
ax = sum(((s[1] - s[0]).normalized() for s in samples), Vector()).normalized()
log("grip R fist local", tuple(round(v, 2) for v in rg), "axis to L fist", tuple(round(v, 3) for v in ax))
# blade direction: away from the left fist (right hand is the guard hand). Check in the idle: should point up/forward.
use_action(ACTS["idle"]); bpy.context.scene.frame_set(10)
m = PB[B("RightHand")].matrix
log("idle blade dir (armature space)", tuple(round(v, 2) for v in (m.to_3x3() @ -ax)))

# katana in hand-local space: x' = blade axis (-ax), edge = hand +Y projected
bl = -ax
edge = (Vector((0, 1, 0)) - bl * bl.dot(Vector((0, 1, 0)))).normalized()
flat = bl.cross(edge).normalized()
GUARD = rg + bl * 5.0          # tsuba 5 cm beyond the right fist
BLADE_L, GRIP_L = 82.0, 30.0
KV, KF, KM = [], [], []


def kp(u, e, f):   # u along blade from tsuba, e toward edge, f flat
    sori = 2.2 * (max(u, 0) / BLADE_L) ** 2 if u > 0 else 0.0
    return GUARD + bl * u + edge * (e - sori) + flat * f


def kbox(segs, mi):
    """segs: list of (u, [(e,f) ring points]) -> lofted closed tube."""
    base = len(KV)
    n = len(segs[0][1])
    for u, ring in segs:
        for e, f in ring: KV.append(kp(u, e, f))
    for i in range(len(segs) - 1):
        for k in range(n):
            a, b = base + i * n + k, base + i * n + (k + 1) % n
            KF.append((a, b, b + n, a + n)); KM.append(mi)
    KF.append(tuple(base + k for k in range(n))[::-1]); KM.append(mi)
    KF.append(tuple(base + (len(segs) - 1) * n + k for k in range(n))); KM.append(mi)


def blade_ring(u):
    t = u / BLADE_L
    w = 3.1 * (1 - 0.25 * t)
    th = 0.35
    if t > 0.93: w *= max(0.05, (1 - t) / 0.07)
    return [(w * 0.5, 0), (0, th), (-w * 0.5, th * 0.8), (-w * 0.5, -th * 0.8), (0, -th)]


kbox([(u, blade_ring(u)) for u in [0.5 + i * (BLADE_L - 0.5) / 12 for i in range(13)]], 0)
oct8 = lambda r, rf: [(math.cos(2 * math.pi * k / 8) * r, math.sin(2 * math.pi * k / 8) * rf) for k in range(8)]
kbox([(-0.6, oct8(4.2, 3.6)), (0.6, oct8(4.2, 3.6))], 1)            # tsuba
kbox([(0.6, oct8(1.9, 1.3)), (2.4, oct8(1.7, 1.1))], 1)              # habaki
kbox([(-0.6 - GRIP_L * t, oct8(1.65 - 0.15 * t, 1.25 - 0.1 * t)) for t in (0, 0.5, 1)], 2)   # wrapped grip
kbox([(-0.6 - GRIP_L, oct8(1.6, 1.2)), (-2.4 - GRIP_L, oct8(1.4, 1.0))], 1)                  # kashira
rest_hand = REST[B("RightHand")]
kme = bpy.data.meshes.new("Katana")
kme.from_pydata([tuple(rest_hand @ v) for v in KV], [], KF)
for nme in ("Ronin_BladeC", "Ronin_Tsuba", "Ronin_Grip"): kme.materials.append(mat(nme))
for i, p in enumerate(kme.polygons): p.material_index = KM[i]
katana = bpy.data.objects.new("Katana", kme)
bpy.context.scene.collection.objects.link(katana)
arm.animation_data.action = None
for pb in arm.pose.bones:
    pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.rotation_euler = (0, 0, 0)
bpy.context.view_layer.update()
katana.parent = arm
katana.parent_type = 'BONE'
katana.parent_bone = B("RightHand")
bpy.context.view_layer.update()
# put the mesh back where it was modelled (armature space at rest)
katana.matrix_world = arm.matrix_world.copy()
bpy.context.view_layer.update()


# ---------------------------------------------------------------- derived clips: fall and hang
def pose_from(act, frame):
    use_action(act)
    bpy.context.scene.frame_set(frame)
    return {pb.name: (pb.location.copy(), pb.rotation_quaternion.copy()) for pb in PB}


def make_action(name, keys):
    """keys: list of (frame, {bone: (loc, quat)})."""
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data.action = act
    for pb in PB: pb.rotation_mode = 'QUATERNION'
    prev = {}
    for f, pose in keys:
        for pb in PB:
            if pb.name not in pose: continue
            loc, q = pose[pb.name]
            # Keep consecutive keys in the same quaternion hemisphere: q and -q are the same pose, but the F-curves
            # interpolate components, so a sign flip between keys swings the bone through a wrong in-between.
            q = q.copy()
            if pb.name in prev and prev[pb.name].dot(q) < 0.0: q.negate()
            prev[pb.name] = q
            pb.location, pb.rotation_quaternion = loc, q
            pb.keyframe_insert("location", frame=f)
            pb.keyframe_insert("rotation_quaternion", frame=f)
    return act


def aim(bone, target, pose):
    """Rotate bone (in the posed armature) so its direction points along target (armature space)."""
    pb = PB[bone]
    d = (pb.tail - pb.head).normalized()
    q = d.rotation_difference(target.normalized())
    M = Matrix.Translation(pb.head) @ q.to_matrix().to_4x4() @ Matrix.Translation(-pb.head) @ pb.matrix
    pb.matrix = M
    bpy.context.view_layer.update()
    pose[bone] = (pb.location.copy(), pb.rotation_quaternion.copy())


jf0, jf1 = ACTS["jump"].frame_range
fa = int(jf0 + (jf1 - jf0) * 0.62)
fb = int(jf0 + (jf1 - jf0) * 0.70)
pa, pbp = pose_from(ACTS["jump"], fa), pose_from(ACTS["jump"], fb)
ACTS["fall"] = make_action("fall", [(1, pa), (16, pbp), (31, pa)])


def hang_pose(sway):
    """One arm locked overhead gripping (left), torso hanging under it and tilted toward that side, right arm
    down with the katana, legs hanging nearly straight (right knee a little bent), toes pointing down. The extra
    turn toward the camera is applied by RoninView while hanging (symmetric for both facings)."""
    pose = pose_from(ACTS["jump"], fa)
    arm.animation_data.action = None
    for n, (l, q) in pose.items():
        PB[n].location, PB[n].rotation_quaternion = l, q
    bpy.context.view_layer.update()
    hips = PB[B("Hips")]
    loc = hips.matrix.translation.copy()
    m = Matrix.Rotation(math.radians(8.0), 4, 'Y') @ Matrix.Rotation(math.radians(-6.0), 4, 'Z') @ REST[B("Hips")].to_3x3().to_4x4()
    m.translation = loc
    hips.matrix = m
    bpy.context.view_layer.update()
    pose[B("Hips")] = (hips.location.copy(), hips.rotation_quaternion.copy())
    aim(B("Spine"), Vector((-0.06, 1.0, 0.02)), pose)
    aim(B("Spine1"), Vector((-0.04, 1.0, 0.0)), pose)
    aim(B("Spine2"), Vector((0.02, 1.0, -0.04)), pose)
    aim(B("Neck"), Vector((-0.05, 1.0, 0.12)), pose)
    aim(B("Head"), Vector((-0.08, 1.0, 0.25)), pose)
    aim(B("LeftShoulder"), Vector((0.8, 0.6, -0.05)), pose)
    aim(B("LeftArm"), Vector((0.10, 1.0, 0.06)), pose)
    aim(B("LeftForeArm"), Vector((0.02, 1.0, 0.05)), pose)
    aim(B("LeftHand"), Vector((0.0, 1.0, 0.18)), pose)
    aim(B("RightShoulder"), Vector((-1.0, -0.25, -0.1)), pose)
    aim(B("RightArm"), Vector((-0.22, -1.0, 0.12)), pose)
    aim(B("RightForeArm"), Vector((-0.1, -1.0, 0.45)), pose)
    aim(B("RightHand"), Vector((-0.05, -1.0, 0.5)), pose)
    for side, k in (("Left", 0.0), ("Right", 1.0)):
        aim(B(side + "UpLeg"), Vector((0.0, -1.0, 0.04 + 0.16 * k + sway)), pose)
        aim(B(side + "Leg"), Vector((0.0, -1.0, -0.06 - 0.28 * k + sway)), pose)
        aim(B(side + "Foot"), Vector((0.0, -0.85, 0.45)), pose)
    return pose


ACTS["hang"] = make_action("hang", [(1, hang_pose(-0.03)), (31, hang_pose(0.03)), (61, hang_pose(-0.03))])

# ---------------------------------------------------------------- procedural katana-fighter clips (v3)
# Locomotion, airborne, landing and slashes are keyposed in ronin_anims.py (the Great Sword pack reads as a heavy
# two-hander). They replace the Mixamo idle, run, jump, fall, slashes and jump attack and add sprint, land and the
# two running slashes. The Mixamo idle only lends its gripping fingers. Slide, hurt, death and hang stay as above.
sys.path.insert(0, ROOT + "/tools/art/ronin")
import ronin_anims
ACTS.update(ronin_anims.build(bpy, arm, B, REST, katana, ACTS["idle"], make_action, log))
LOOPS = {k: v[1] for k, v in CLIPS.items()}
LOOPS.update({"fall": True, "hang": True, "sprint": True, "land": False, "run_attack_1": False, "run_attack_2": False})


# ---------------------------------------------------------------- preview renders (workbench, iteration only)
def preview(dirp):
    os.makedirs(dirp, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'MATERIAL'
    sc.display.shading.show_shadows = True
    sc.display.shading.show_backface_culling = True
    sc.render.resolution_x, sc.render.resolution_y = 1200, 800
    sc.world = bpy.data.worlds.new("w")
    cam = bpy.data.cameras.new("c"); cam.type = 'ORTHO'; cam.ortho_scale = 2.6
    co = bpy.data.objects.new("cam", cam); sc.collection.objects.link(co); sc.camera = co
    for clip, frac in (("idle", 0.3), ("run", 0.3), ("slide_attack", 0.45), ("attack_1", 0.5), ("hang", 0.0), ("jump_attack", 0.5)):
        use_action(ACTS[clip])
        f0, f1 = ACTS[clip].frame_range
        sc.frame_set(int(f0 + (f1 - f0) * frac))
        for vn, loc, rot in (("front", (0, -6, 1.1), (90, 0, 0)), ("side", (6, 0, 1.1), (90, 0, 90))):
            co.location = loc; co.rotation_euler = [math.radians(a) for a in rot]
            sc.render.filepath = os.path.join(dirp, "%s_%s.png" % (clip, vn))
            bpy.ops.render.render(write_still=True)


if PREVIEW:
    preview(PREVIEW)    # per-key materials, before the atlas collapse

atlas(body, ["Ronin_Atlas", "Ronin_Rim"])
atlas(katana, ["Ronin_Blade"])
log("draw materials body", [m.name for m in body.data.materials], "katana", [m.name for m in katana.data.materials])

# ---------------------------------------------------------------- export
FBX_KW = dict(add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X', armature_nodetype='NULL',
              apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', use_armature_deform_only=False,
              mesh_smooth_type='FACE', use_mesh_modifiers=True)


def select_only(objs):
    for o in bpy.context.scene.objects: o.select_set(False)
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


if EXPORT:
    os.makedirs(OUT + "Animations", exist_ok=True)
    write_palette(OUT + "Materials/")
    # character in rest pose, no animation
    arm.animation_data.action = None
    for pb in PB:
        pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0)
    bpy.context.view_layer.update()
    select_only([arm, body, katana])
    bpy.ops.export_scene.fbx(filepath=OUT + "ronin.fbx", use_selection=True, object_types={'ARMATURE', 'MESH'},
                             bake_anim=False, **FBX_KW)
    log("exported", OUT + "ronin.fbx")
    for name, act in ACTS.items():
        use_action(act)
        select_only([arm])
        bpy.ops.export_scene.fbx(filepath=OUT + "Animations/%s.fbx" % name, use_selection=True, object_types={'ARMATURE'},
                                 bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
                                 bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, **FBX_KW)
        log("exported clip", name, tuple(act.frame_range), "loop" if LOOPS[name] else "")
    arm.animation_data.action = ACTS["idle"]
    bpy.ops.wm.save_as_mainfile(filepath=ROOT + "/tools/art/ronin/ronin.blend")
    log("DONE")
