"""Build the Redline cyber-samurai: joint hierarchy (empties) + rigid mesh parts + ID/normal materials.

Run inside Blender:  blender -b --factory-startup -P build_model.py -- --out model.blend
Everything is procedural: no external assets. Character faces +X, up is +Z, the camera
looks along +Y (so -Y is the near side = the character's right / sword side).
Joints are empties whose rest orientation is world-aligned; rotating a joint about Y swings
it in the screen plane. "*_geo" empties carry the constant 3/4 yaw of the torso and head;
shoulder/hip/coat roots cancel that yaw so limbs swing exactly in the screen plane.
"""
import bpy, bmesh, math, json, os, sys
from mathutils import Vector, Euler

HERE = os.path.dirname(os.path.abspath(__file__))
STYLE = json.load(open(os.path.join(HERE, 'samurai_style.json')))
D = math.radians


def args():
    a = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    return {a[i].lstrip('-'): a[i + 1] for i in range(0, len(a) - 1, 2)}


# ---------------------------------------------------------------- scene / materials
def reset():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for d in list(coll):
            coll.remove(d)


def id_material(name, idv, back_id=None):
    """Emission = (id*10/255, AO, 0). back_id: material shown on back faces (coat lining)."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    m.use_fake_user = True  # keep materials only used later (smear) in the saved .blend
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    em = nt.nodes.new('ShaderNodeEmission')
    ao = nt.nodes.new('ShaderNodeAmbientOcclusion')
    ao.samples = STYLE['render']['ao_samples']
    ao.inputs['Distance'].default_value = STYLE['render']['ao_distance']
    cc = nt.nodes.new('ShaderNodeCombineColor')
    if back_id is None:
        cc.inputs[0].default_value = idv * 10 / 255
    else:
        geo = nt.nodes.new('ShaderNodeNewGeometry')
        mt = nt.nodes.new('ShaderNodeMath')
        mt.operation = 'MULTIPLY_ADD'
        nt.links.new(geo.outputs['Backfacing'], mt.inputs[0])
        mt.inputs[1].default_value = (back_id - idv) * 10 / 255
        mt.inputs[2].default_value = idv * 10 / 255
        nt.links.new(mt.outputs[0], cc.inputs[0])
    nt.links.new(ao.outputs['AO'], cc.inputs[1])
    cc.inputs[2].default_value = 0.0
    nt.links.new(cc.outputs[0], em.inputs['Color'])
    em.inputs['Strength'].default_value = 1.0
    nt.links.new(em.outputs[0], out.inputs['Surface'])
    return m


def normal_material():
    """Camera-space normal for a camera looking +Y: sprite (x, y, z) = world (X, Z, -Y), packed *0.5+0.5."""
    m = bpy.data.materials.new('NORMAL_PASS')
    m.use_nodes = True
    m.use_fake_user = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    em = nt.nodes.new('ShaderNodeEmission')
    geo = nt.nodes.new('ShaderNodeNewGeometry')
    sep = nt.nodes.new('ShaderNodeSeparateXYZ')
    neg = nt.nodes.new('ShaderNodeMath')
    neg.operation = 'MULTIPLY'
    neg.inputs[1].default_value = -1.0
    comb = nt.nodes.new('ShaderNodeCombineXYZ')
    vm = nt.nodes.new('ShaderNodeVectorMath')
    vm.operation = 'MULTIPLY_ADD'
    vm.inputs[1].default_value = (0.5, 0.5, 0.5)
    vm.inputs[2].default_value = (0.5, 0.5, 0.5)
    nt.links.new(geo.outputs['Normal'], sep.inputs[0])
    nt.links.new(sep.outputs['X'], comb.inputs['X'])
    nt.links.new(sep.outputs['Z'], comb.inputs['Y'])
    nt.links.new(sep.outputs['Y'], neg.inputs[0])
    nt.links.new(neg.outputs[0], comb.inputs['Z'])
    nt.links.new(comb.outputs[0], vm.inputs[0])
    nt.links.new(vm.outputs[0], em.inputs['Color'])
    nt.links.new(em.outputs[0], out.inputs['Surface'])
    return m


# ---------------------------------------------------------------- geometry primitives (frame-local)
def R(v, rd):
    return Euler([D(a) for a in rd], 'XYZ').to_matrix() @ Vector(v)


def ellipsoid(c, r, rd=(0, 0, 0), nu=18, nv=10):
    V = [(0, 0, 1)]
    for i in range(1, nv):
        ph = math.pi * i / nv
        for j in range(nu):
            th = 2 * math.pi * j / nu
            V.append((math.sin(ph) * math.cos(th), math.sin(ph) * math.sin(th), math.cos(ph)))
    V.append((0, 0, -1))
    bot = len(V) - 1
    F = [(0, 1 + j, 1 + (j + 1) % nu) for j in range(nu)]
    for i in range(nv - 2):
        for j in range(nu):
            a = 1 + i * nu + j
            b = 1 + i * nu + (j + 1) % nu
            F.append((a, a + nu, b + nu, b))
    last = 1 + (nv - 2) * nu
    F += [(last + j, bot, last + (j + 1) % nu) for j in range(nu)]
    V = [Vector(c) + R((x * r[0], y * r[1], z * r[2]), rd) for x, y, z in V]
    return V, F, True


def tube(p0, p1, r0, r1, n=14):
    p0, p1 = Vector(p0), Vector(p1)
    d = (p1 - p0).normalized()
    u = d.orthogonal().normalized()
    v = d.cross(u)
    V = []
    for p, r in ((p0, r0), (p1, r1)):
        for j in range(n):
            th = 2 * math.pi * j / n
            V.append(p + (u * math.cos(th) + v * math.sin(th)) * r)
    F = [(j, (j + 1) % n, n + (j + 1) % n, n + j) for j in range(n)]
    F += [tuple(range(n)), tuple(range(2 * n - 1, n - 1, -1))]
    return V, F, True


def ecyl(c, z0, z1, rx, ry, n=18):
    V = []
    for z in (z0, z1):
        for j in range(n):
            th = 2 * math.pi * j / n
            V.append(Vector((c[0] + rx * math.cos(th), c[1] + ry * math.sin(th), z)))
    F = [(j, (j + 1) % n, n + (j + 1) % n, n + j) for j in range(n)]
    F += [tuple(range(n)), tuple(range(2 * n - 1, n - 1, -1))]
    return V, F, True


def box(c, s, rd=(0, 0, 0)):
    hx, hy, hz = s[0] / 2, s[1] / 2, s[2] / 2
    P = [(-hx, -hy, -hz), (hx, -hy, -hz), (hx, hy, -hz), (-hx, hy, -hz),
         (-hx, -hy, hz), (hx, -hy, hz), (hx, hy, hz), (-hx, hy, hz)]
    F = [(0, 1, 2, 3), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return [Vector(c) + R(p, rd) for p in P], F, True


def extrude2d(pts, y0, t):
    n = len(pts)
    V = [Vector((x, y0 - t / 2, z)) for x, z in pts] + [Vector((x, y0 + t / 2, z)) for x, z in pts]
    F = [tuple(range(n)), tuple(range(2 * n - 1, n - 1, -1))]
    F += [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
    return V, F, True


def shell(c, a0, a1, rows, nseg=10, jag=None, folds=None, holes=()):
    """Open single-sided surface around a vertical axis at c; rows = [(z, rx, ry)], angles in deg
    from +X toward +Y. Faces are wound so the front face points away from the axis."""
    V = []
    W = nseg + 1
    for ri, (z, rx, ry) in enumerate(rows):
        for k in range(W):
            a = D(a0 + (a1 - a0) * k / nseg)
            zz = z + (jag[k] if (jag and ri == len(rows) - 1) else 0.0)
            f = 1.0 + (folds[1] * math.sin(folds[0] * math.pi * k / nseg + 0.7 * ri) if folds else 0.0)
            V.append(Vector((c[0] + rx * f * math.cos(a), c[1] + ry * f * math.sin(a), zz)))
    F = []
    for ri in range(len(rows) - 1):
        for k in range(nseg):
            if (ri, k) in holes:
                continue
            a = ri * W + k
            f = (a, a + 1, a + 1 + W, a + W)
            p = [V[i] for i in f]
            nrm = (p[1] - p[0]).cross(p[3] - p[0])
            cen = sum(p, Vector()) / 4
            if nrm.dot(Vector((cen.x - c[0], cen.y - c[1], 0))) < 0:
                f = f[::-1]
            F.append(f)
    return V, F, False


def ribbed(c, r, nrib=10, amp=0.06, nu=40, nv=14):
    """Ellipsoid whose upper half carries vertical ribs (suji-bachi helmet bowl)."""
    V, F, cl = ellipsoid(c, r, nu=nu, nv=nv)
    out = []
    for v in V:
        d = v - Vector(c)
        if d.z > -0.2 * r[2]:
            th = math.atan2(d.y / r[1], d.x / r[0])
            k = 1 + amp * (abs(math.cos(nrib * th / 2)) ** 3 - 0.3)
            d = Vector((d.x * k, d.y * k, d.z))
        out.append(Vector(c) + d)
    return out, F, cl


def shingles(p0, p1, r0, r1, step=0.045, lip=0.007, n=12):
    """Wrapped limb: overlapping conical bands so the cloth wrap reads as stripes."""
    p0, p1 = Vector(p0), Vector(p1)
    L = (p1 - p0).length
    m = max(1, int(L / step))
    gs = []
    for i in range(m):
        t0, t1 = i / m, (i + 1) / m
        r = r0 + (r1 - r0) * t0
        gs.append(tube(p0.lerp(p1, t0), p0.lerp(p1, min(1, t1 + 0.15 / m)), r + lip, r - 0.002, n))
    return cat(*gs)


def jag_line(n, amp, seed):
    """Deterministic frayed hem offsets."""
    out = []
    for k in range(n + 1):
        v = math.sin(k * 2.7 + seed) * 0.6 + math.sin(k * 5.3 + seed * 1.7) * 0.4
        out.append(-amp * (0.5 + 0.5 * v) * (1.6 if k % 3 == 1 else 1.0))
    return out


def cat(*gs):
    V, F = [], []
    closed = True
    for v, f, c in gs:
        o = len(V)
        V += list(v)
        F += [tuple(i + o for i in ff) for ff in f]
        closed = closed and c
    return V, F, closed


# ---------------------------------------------------------------- rig
J = {}
MATS = {}
COL = None


def joint(name, parent, loc, mode='XYZ', rz=0.0):
    e = bpy.data.objects.new(name, None)
    e.empty_display_size = 0.04
    COL.objects.link(e)
    if parent:
        e.parent = J[parent]
    e.location = loc
    e.rotation_mode = mode
    e.rotation_euler = (0, 0, D(rz))
    e['rest_z'] = D(rz)
    e['rest_loc'] = list(loc)
    J[name] = e
    return e


TAILS = [('B', 180.0, 0.76, 34.0), ('N', -116.0, 0.66, 30.0), ('F', 116.0, 0.62, 30.0)]
TAIL_SEGS = 4
TAIL_Z0 = 0.13
TAIL_R0 = 0.17


def build_rig():
    yaw = STYLE['rig']['yaw']
    joint('root', None, (0, 0, 0.86))
    joint('pelvis', 'root', (0, 0, 0))
    joint('pelvis_geo', 'pelvis', (0, 0, 0), 'XYZ', yaw['pelvis'])
    joint('spine', 'pelvis', (0, 0, 0.12))
    joint('spine_geo', 'spine', (0, 0, 0), 'XYZ', yaw['spine'])
    joint('chest', 'spine', (0, 0, 0.18))
    joint('chest_geo', 'chest', (0, 0, 0), 'XYZ', yaw['chest'])
    joint('neck', 'chest', (0, 0, 0.20))
    joint('head', 'neck', (0, 0, 0.07))
    joint('head_geo', 'head', (0, 0, 0), 'XYZ', yaw['head'])
    joint('crest_j', 'head_geo', (0.07, 0, 0.12))
    for s, sy in (('R', -1), ('L', 1)):
        joint('sh_' + s, 'chest_geo', (0.0, 0.20 * sy, 0.15), 'YXZ', -yaw['chest'])
        joint('el_' + s, 'sh_' + s, (0, 0, -0.27))
        joint('wr_' + s, 'el_' + s, (0, 0, -0.25))
        joint('hip_' + s, 'pelvis_geo', (0, 0.09 * sy, -0.04), 'YXZ', -yaw['pelvis'])
        joint('kn_' + s, 'hip_' + s, (0, 0, -0.38))
        joint('an_' + s, 'kn_' + s, (0, 0, -0.36))
    joint('sword', 'wr_R', (0.0, 0, -0.065))
    for t, ang, length, hw in TAILS:
        L = length / TAIL_SEGS
        a = D(ang)
        joint('tail_%s_0' % t, 'pelvis_geo', (TAIL_R0 * math.cos(a), TAIL_R0 * 1.15 * math.sin(a), TAIL_Z0),
              'YXZ', -yaw['pelvis'])
        for k in range(1, TAIL_SEGS):
            joint('tail_%s_%d' % (t, k), 'tail_%s_%d' % (t, k - 1), (0, 0, -L))
    bpy.context.view_layer.update()


def part(name, frame, jnt, mat, geo, smooth=True, bevel=0.0):
    verts, faces, closed = geo
    M = J[frame].matrix_world
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(M @ Vector(v)) for v in verts], [], faces)
    me.update()
    if closed:
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(me)
        bm.free()
    me.polygons.foreach_set('use_smooth', [smooth] * len(me.polygons))
    me.materials.append(MATS[mat])
    ob = bpy.data.objects.new(name, me)
    COL.objects.link(ob)
    ob.parent = J[jnt]
    ob.matrix_parent_inverse = J[jnt].matrix_world.inverted()
    if bevel > 0:
        md = ob.modifiers.new('bevel', 'BEVEL')
        md.width = bevel
        md.segments = 2
        md.limit_method = 'NONE'
    return ob


# ---------------------------------------------------------------- body parts
def bezier(p0, p1, p2, t):
    return tuple((1 - t) ** 2 * a + 2 * (1 - t) * t * b + t * t * c for a, b, c in zip(p0, p1, p2))


def crest_geo():
    P0, P1, P2 = (0.07, 0.12), (0.19, 0.36), (-0.19, 0.46)
    n = 16
    left, right = [], []
    for i in range(n + 1):
        t = i / n
        x, z = bezier(P0, P1, P2, t)
        dx = 2 * (1 - t) * (P1[0] - P0[0]) + 2 * t * (P2[0] - P1[0])
        dz = 2 * (1 - t) * (P1[1] - P0[1]) + 2 * t * (P2[1] - P1[1])
        ln = math.hypot(dx, dz)
        nx, nz = -dz / ln, dx / ln
        w = 0.095 * (1 - t) ** 0.8 * (0.55 + 0.45 * math.sin(math.pi * min(1, t * 1.5 + 0.25))) + 0.006
        left.append((x + nx * w / 2, z + nz * w / 2))
        right.append((x - nx * w / 2, z - nz * w / 2))
    return extrude2d(left + right[::-1], 0.0, 0.02)


def blade_geo():
    x0, L, n = 0.072, 0.80, 14
    top, bot = [], []
    for i in range(n + 1):
        u = i / n
        x = x0 + L * u
        sori = 0.035 * u * u
        w = 0.034 * (1 - 0.3 * u)
        if u > 0.9:  # kissaki: spine drops to the point
            k = (u - 0.9) / 0.1
            top.append((x, sori + w / 2 * (1 - k) - w * 0.3 * k))
            bot.append((x, sori - w / 2 * (1 - 0.6 * k)))
        else:
            top.append((x, sori + w / 2))
            bot.append((x, sori - w / 2))
    return extrude2d(bot + top[::-1], 0.0, 0.012)


def build_body():
    # ---- pelvis / waist
    part('hips', 'pelvis_geo', 'pelvis', 'cloth', ellipsoid((0, 0, -0.02), (0.13, 0.15, 0.10)))
    ks, kl = [], []
    for (a0, a1) in ((-95, -12), (12, 95)):
        for i in range(3):
            zt, zb = -0.02 - 0.058 * i, -0.088 - 0.058 * i
            r0, r1 = 0.15 + 0.012 * i, 0.165 + 0.012 * i
            ks.append(shell((0.0, 0), a0, a1, [(zt, r0, r0 * 1.12), (zb, r1, r1 * 1.12)], 10))
            kl.append(shell((0.0, 0), a0, a1, [(zt + 0.002, r0 + 0.006, (r0 + 0.006) * 1.12),
                                               (zt - 0.018, r0 + 0.008, (r0 + 0.008) * 1.12)], 10))
    part('kusazuri', 'pelvis_geo', 'pelvis', 'armour', cat(*ks))
    part('kusazuri_lace', 'pelvis_geo', 'pelvis', 'lacing', cat(*kl))
    part('saya', 'pelvis_geo', 'pelvis', 'helmet', box((-0.22, 0.17, -0.02), (0.62, 0.036, 0.036), (0, -20, 0)), bevel=0.01)
    part('obi', 'spine_geo', 'spine', 'wraps', ecyl((0, 0), -0.06, 0.05, 0.135, 0.158))
    part('abdomen', 'spine_geo', 'spine', 'armour', ellipsoid((0.01, 0, 0.07), (0.12, 0.14, 0.12)))
    # ---- chest + upper coat + collar
    part('cuirass', 'chest_geo', 'chest', 'armour', ellipsoid((0.03, 0, 0.06), (0.175, 0.21, 0.18)))
    part('coat_upper', 'chest_geo', 'chest', 'coat',
         shell((0, 0), 48, 312, [(-0.19, 0.17, 0.21), (0.0, 0.2, 0.245), (0.15, 0.195, 0.24), (0.225, 0.13, 0.18)], 24, folds=(7, 0.045)))
    cord = []
    pts = [(0.17, -0.17, 0.12), (0.2, -0.08, 0.07), (0.205, 0.0, 0.05), (0.2, 0.08, 0.07), (0.17, 0.17, 0.12)]
    for p0, p1 in zip(pts, pts[1:]):
        cord.append(tube(p0, p1, 0.01, 0.01, 8))
    cord += [ellipsoid((0.212, -0.01, 0.05), (0.024, 0.03, 0.022)),
             tube((0.214, -0.02, 0.04), (0.222, -0.04, -0.06), 0.009, 0.012, 8),
             tube((0.214, 0.005, 0.04), (0.226, 0.0, -0.05), 0.009, 0.012, 8)]
    part('cords', 'chest_geo', 'chest', 'lacing', cat(*cord))
    part('collar', 'chest_geo', 'chest', 'coat',
         shell((0, 0), 58, 302, [(0.18, 0.10, 0.12), (0.31, 0.13, 0.15)], 14))
    # ---- head
    H = 'head_geo'
    part('neck_m', H, 'head', 'cloth', tube((0, 0, -0.11), (0, 0, 0.0), 0.048, 0.048))
    part('head_core', H, 'head', 'helmet', ellipsoid((0.005, 0, 0.045), (0.085, 0.078, 0.095)))
    part('dome', H, 'head', 'helmet', ribbed((-0.012, 0, 0.095), (0.112, 0.106, 0.09)))
    part('hband', H, 'head', 'armour', ecyl((-0.012, 0), 0.062, 0.08, 0.116, 0.110, 24))
    part('brim', H, 'head', 'helmet', box((0.10, 0, 0.082), (0.075, 0.2, 0.014), (0, -14, 0)), bevel=0.005)
    sk, sl = [], []
    for i in range(4):
        zt, zb = 0.075 - 0.042 * i, 0.03 - 0.042 * i
        r0, r1 = 0.112 + 0.017 * i, 0.135 + 0.02 * i
        sk.append(shell((-0.012, 0), 75 + 2 * i, 285 - 2 * i, [(zt, r0, r0 * 0.96), (zb, r1, r1 * 0.96)], 16))
        sl.append(shell((-0.012, 0), 77 + 2 * i, 283 - 2 * i, [(zb + 0.014, r1 - 0.004, (r1 - 0.004) * 0.96),
                                                               (zb + 0.002, r1 + 0.003, (r1 + 0.003) * 0.96)], 16))
    part('shikoro', H, 'head', 'helmet', cat(*sk))
    part('shikoro_lace', H, 'head', 'lacing', cat(*sl))
    part('fukigaeshi', H, 'head', 'helmet', cat(box((0.04, -0.14, 0.035), (0.05, 0.02, 0.075), (0, 0, -25)),
                                              box((0.04, 0.14, 0.035), (0.05, 0.02, 0.075), (0, 0, 25))), bevel=0.006)
    part('visor', H, 'head', 'visor', shell((0.005, 0), -72, 72, [(0.034, 0.093, 0.086), (0.052, 0.093, 0.086)], 12))
    part('mask', H, 'head', 'mask', cat(ellipsoid((0.035, 0, -0.008), (0.08, 0.08, 0.055)),
                                      ellipsoid((0.11, 0, 0.018), (0.034, 0.024, 0.024)),
                                      box((0.098, 0, -0.045), (0.06, 0.085, 0.035)),
                                      ellipsoid((0.112, 0, 0.0), (0.022, 0.014, 0.042)),
                                      box((0.133, -0.02, -0.052), (0.014, 0.012, 0.03), (0, 0, 0)),
                                      box((0.133, 0.02, -0.052), (0.014, 0.012, 0.03), (0, 0, 0))))
    part('mouth', H, 'head', 'mouth', box((0.127, 0, -0.04), (0.012, 0.075, 0.017)))
    part('crest', H, 'crest_j', 'crest', crest_geo(), smooth=False)
    # ---- right (cyber) arm
    part('sh_R_ball', 'sh_R', 'sh_R', 'cyber', ellipsoid((0, 0, 0), (0.062, 0.062, 0.062)))
    part('upper_R', 'sh_R', 'sh_R', 'cyber', tube((0, 0, -0.02), (0, 0, -0.26), 0.056, 0.046))
    part('bands_R', 'sh_R', 'sh_R', 'armour', cat(ecyl((0, 0), -0.10, -0.085, 0.053, 0.053), ecyl((0, 0), -0.18, -0.165, 0.049, 0.049)))
    part('strip_R1', 'sh_R', 'sh_R', 'cyan', box((0.012, -0.05, -0.135), (0.012, 0.008, 0.09)))
    part('elbow_R', 'el_R', 'el_R', 'cyber', ellipsoid((0, 0, 0), (0.046, 0.046, 0.046)))
    part('fore_R', 'el_R', 'el_R', 'cyber', box((0.008, 0, -0.125), (0.092, 0.084, 0.21)), bevel=0.016)
    part('pistons_R', 'el_R', 'el_R', 'steel', cat(tube((-0.052, -0.022, 0.0), (-0.052, -0.022, -0.2), 0.011, 0.011, 8),
                                                 tube((-0.052, 0.022, 0.0), (-0.052, 0.022, -0.2), 0.011, 0.011, 8)))
    part('strip_R2', 'el_R', 'el_R', 'cyan', cat(box((0.02, -0.041, -0.09), (0.011, 0.008, 0.075)),
                                               box((-0.012, -0.041, -0.17), (0.011, 0.008, 0.05))))
    part('cables_R', 'sh_R', 'sh_R', 'fittings', cat(tube((-0.06, -0.02, -0.03), (-0.064, -0.02, -0.25), 0.012, 0.012, 8),
                                                    tube((0.0, -0.058, -0.21), (0.0, -0.058, -0.25), 0.03, 0.03, 10)))
    part('bolts_R', 'el_R', 'el_R', 'steel', cat(ellipsoid((0.0, -0.05, 0.0), (0.016, 0.012, 0.016), nu=8, nv=5),
                                                ellipsoid((0.04, -0.045, -0.2), (0.012, 0.01, 0.012), nu=8, nv=5)))
    part('wrist_R', 'el_R', 'el_R', 'fittings', ecyl((0.008, 0), -0.24, -0.225, 0.05, 0.046))
    part('hand_R', 'wr_R', 'wr_R', 'cyber', box((0.0, 0, -0.035), (0.06, 0.056, 0.06)), bevel=0.01)
    part('fingers_R', 'sword', 'sword', 'cyber', cat(tube((-0.045, 0, 0), (0.03, 0, 0), 0.03, 0.03, 12),
                                                    *[ellipsoid((-0.035 + 0.022 * k, -0.012, -0.028), (0.012, 0.022, 0.01), nu=8, nv=5)
                                                      for k in range(4)],
                                                    ellipsoid((0.02, -0.02, 0.028), (0.03, 0.016, 0.013))), smooth=True)
    # ---- katana (blade along +X of the sword joint)
    part('grip', 'sword', 'sword', 'wraps', box((-0.075, 0, 0), (0.24, 0.03, 0.034)), bevel=0.006)
    part('kashira', 'sword', 'sword', 'fittings', box((-0.2, 0, 0), (0.02, 0.036, 0.04)))
    part('tsuba', 'sword', 'sword', 'fittings', tube((0.048, 0, 0), (0.062, 0, 0), 0.043, 0.043, 16))
    part('blade', 'sword', 'sword', 'blade', blade_geo(), smooth=False)
    # ---- left arm (organic, armoured)
    part('sh_L_ball', 'sh_L', 'sh_L', 'cloth', ellipsoid((0, 0, 0), (0.056, 0.056, 0.056)))
    part('upper_L', 'sh_L', 'sh_L', 'cloth', tube((0, 0, 0), (0, 0, -0.26), 0.056, 0.048))
    part('elbow_L', 'el_L', 'el_L', 'wraps', ellipsoid((0, 0, 0), (0.045, 0.045, 0.045)))
    part('fore_L', 'el_L', 'el_L', 'wraps', shingles((0, 0, 0), (0, 0, -0.24), 0.05, 0.04))
    part('kote_L', 'el_L', 'el_L', 'armour', box((0.036, 0, -0.12), (0.02, 0.06, 0.18)), bevel=0.006)
    part('hand_L', 'wr_L', 'wr_L', 'boots', cat(box((0.0, 0, -0.03), (0.062, 0.058, 0.055)),
                                               box((0.022, 0, -0.07), (0.04, 0.052, 0.05), (0, 20, 0)),
                                               ellipsoid((0.04, -0.03, -0.035), (0.012, 0.012, 0.028))), bevel=0.008)
    # ---- sode (shoulder armour), layered lames, both sides
    for s, sy in (('R', -1), ('L', 1)):
        lames = [box((-0.01, sy * 0.08, 0.035), (0.25, 0.035, 0.04), (sy * 4, 0, 0))]
        for i in range(4):
            lames.append(box((-0.014 + 0.004 * i, sy * (0.088 + 0.013 * i), -0.015 - 0.05 * i),
                             (0.25 - 0.01 * i, 0.024, 0.065), (sy * (8 + 5 * i), 0, 0)))
        part('sode_' + s, 'sh_' + s, 'sh_' + s, 'armour', cat(*lames), bevel=0.007)
        lace = [box((-0.014 + 0.004 * i, sy * (0.088 + 0.013 * i), 0.008 - 0.05 * i),
                    (0.255 - 0.01 * i, 0.03, 0.016), (sy * (8 + 5 * i), 0, 0)) for i in range(4)]
        part('sodelace_' + s, 'sh_' + s, 'sh_' + s, 'lacing', cat(*lace))
    # ---- legs
    for s, sy in (('R', -1), ('L', 1)):
        hp, kn, an = 'hip_' + s, 'kn_' + s, 'an_' + s
        part('thigh_' + s, hp, hp, 'cloth', cat(ellipsoid((0, 0, 0), (0.085, 0.085, 0.085)),
                                               tube((0, 0, 0.0), (0, 0, -0.38), 0.088, 0.06)))
        a0, a1 = (-120, 25) if sy < 0 else (-25, 120)
        part('haidate_' + s, hp, hp, 'armour', cat(*[
            shell((0.0, 0), a0, a1, [(-0.04 - 0.075 * i, 0.098 - 0.008 * i, 0.098 - 0.008 * i),
                                     (-0.135 - 0.075 * i, 0.112 - 0.008 * i, 0.112 - 0.008 * i)], 10)
            for i in range(3)]))
        part('shin_' + s, kn, kn, 'wraps', shingles((0, 0, 0), (0, 0, -0.34), 0.058, 0.042))
        part('kneecop_' + s, kn, kn, 'armour', ellipsoid((0.042, 0, 0.0), (0.035, 0.052, 0.046)))
        part('suneate_' + s, kn, kn, 'armour', shell((0, 0), -75, 75, [(-0.04, 0.068, 0.068), (-0.30, 0.052, 0.052)], 10))
        part('boot_' + s, an, an, 'boots', cat(box((0.02, 0, -0.045), (0.17, 0.095, 0.075)),
                                              box((0.135, -0.026, -0.056), (0.075, 0.04, 0.052)),
                                              box((0.128, 0.022, -0.06), (0.065, 0.048, 0.042)),
                                              ecyl((0, 0), -0.03, 0.05, 0.046, 0.046)), bevel=0.014)
        part('sole_' + s, an, an, 'fittings', box((0.05, 0, -0.084), (0.24, 0.1, 0.018)), bevel=0.005)
        part('toesplit_' + s, an, an, 'mouth', box((0.14, -0.048, -0.05), (0.06, 0.006, 0.014)))
    # ---- coat tails: chains of overlapping shell segments, lining on the back faces
    for t, ang, length, hw in TAILS:
        L = length / TAIL_SEGS
        for k in range(TAIL_SEGS):
            ztop = TAIL_Z0 - k * L + (0.03 if k else 0.0)
            zbot = TAIL_Z0 - (k + 1) * L
            rows = []
            for zz in (ztop, (ztop + zbot) / 2, zbot):
                d = (TAIL_Z0 - zz) / length
                r = TAIL_R0 + 0.11 * d
                rows.append((zz, r, r * 1.15))
            jag = None
            if k == TAIL_SEGS - 1:
                jag = jag_line(16, 0.07, ang * 0.1)
            holes = {('B', 2): {(0, 11), (1, 11)}, ('N', 3): {(0, 5)}, ('F', 1): {(1, 4)}}.get((t, k), ())
            part('coat_%s_%d' % (t, k), 'pelvis_geo', 'tail_%s_%d' % (t, k), 'coat',
                 shell((0, 0), ang - hw, ang + hw, rows, 16, jag, folds=(4, 0.07), holes=holes))


def main():
    global COL
    a = args()
    reset()
    COL = bpy.context.scene.collection
    for m in STYLE['materials']:
        MATS[m['name']] = id_material(m['name'], m['id'])
    MATS['coat'] = id_material('coat2sided', 5, back_id=6)
    normal_material()
    build_rig()
    build_body()
    out = a.get('out')
    if out:
        bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(out), compress=True)
        print('SAVED', out)


main()
