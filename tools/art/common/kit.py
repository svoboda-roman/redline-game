"""Shared Blender-side kit for the Redline 3D-to-pixel character pipeline (enemies).

Generalised copy of tools/art/samurai/build_model.py + animate.py (which stay untouched):
ID/normal materials, frame-local primitives, a joint hierarchy of empties, rigid parts, a
parameterised humanoid rig, pose keying in screen-plane degrees, group visibility keys,
and the scene metadata render_char.py / pixelate_char.py read.

Conventions (same as the samurai): character faces +X, up is +Z, the camera looks along +Y
(-Y = near side = the character's right). Joints rotate about Y = swing in the screen plane.

Pose keys (degrees, see samurai animate.py):
  lean/pelvis/spine/chest/neck : forward lean (+ = top tips forward)
  head                         : ABSOLUTE head pitch (+ = looking down)
  shR/shL, hipR/hipL           : ABSOLUTE limb angle from straight down (+ = forward)
  elR/elL (+ = forearm forward), knR/knL (+ = shin back), anR/anL ABSOLUTE foot pitch
  weapon keys (per character)  : ABSOLUTE direction of the weapon's +X axis, math angle
  dx/dz                        : root offset, world units
  rot  {joint: deg}            : extra relative rotation about Y for any other joint
  loc  {joint: (dx, dz)}       : extra location offset for any other joint
  show [group, ...]            : part groups visible this frame (all others hidden)
"""
import bpy, bmesh, math, json, os, sys
from mathutils import Vector, Euler

COMMON = os.path.dirname(os.path.abspath(__file__))
STYLE = json.load(open(os.path.join(COMMON, 'enemy_style.json')))
D = math.radians
TAU = 2 * math.pi
S, C = math.sin, math.cos


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


def id_material(name, idv):
    """Emission = (id*10/255, AO, 0)."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    m.use_fake_user = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    em = nt.nodes.new('ShaderNodeEmission')
    ao = nt.nodes.new('ShaderNodeAmbientOcclusion')
    ao.samples = STYLE['render']['ao_samples']
    ao.inputs['Distance'].default_value = STYLE['render']['ao_distance']
    cc = nt.nodes.new('ShaderNodeCombineColor')
    cc.inputs[0].default_value = idv * 10 / 255
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
            V.append(p + (u * math.cos(th) + v * math.sin(th)) * max(r, 1e-4))
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


def lathe(c, rows, n=20):
    """Closed solid of revolution about Z at c; rows = [(z, r)] bottom->top (r may be 0 at ends)."""
    V = []
    for z, r in rows:
        for j in range(n):
            th = 2 * math.pi * j / n
            V.append(Vector((c[0] + max(r, 1e-4) * math.cos(th), c[1] + max(r, 1e-4) * math.sin(th), z)))
    F = []
    for i in range(len(rows) - 1):
        for j in range(n):
            a, b = i * n + j, i * n + (j + 1) % n
            F.append((a, b, b + n, a + n))
    F += [tuple(range(n - 1, -1, -1)), tuple(range((len(rows) - 1) * n, len(rows) * n))]
    return V, F, True


def box(c, s, rd=(0, 0, 0)):
    hx, hy, hz = s[0] / 2, s[1] / 2, s[2] / 2
    P = [(-hx, -hy, -hz), (hx, -hy, -hz), (hx, hy, -hz), (-hx, hy, -hz),
         (-hx, -hy, hz), (hx, -hy, hz), (hx, hy, hz), (-hx, hy, hz)]
    F = [(0, 1, 2, 3), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return [Vector(c) + R(p, rd) for p in P], F, True


def extrude2d(pts, y0, t):
    """Polygon in the XZ plane (list of (x, z)), extruded t thick around y = y0."""
    n = len(pts)
    V = [Vector((x, y0 - t / 2, z)) for x, z in pts] + [Vector((x, y0 + t / 2, z)) for x, z in pts]
    F = [tuple(range(n)), tuple(range(2 * n - 1, n - 1, -1))]
    F += [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
    return V, F, True


def shell(c, a0, a1, rows, nseg=10, jag=None):
    """Open single-sided surface around a vertical axis at c; rows = [(z, rx, ry)], angles in deg
    from +X toward +Y. Faces are wound so the front face points away from the axis."""
    V = []
    W = nseg + 1
    for ri, (z, rx, ry) in enumerate(rows):
        for k in range(W):
            a = D(a0 + (a1 - a0) * k / nseg)
            zz = z + (jag[k] if (jag and ri == len(rows) - 1) else 0.0)
            V.append(Vector((c[0] + rx * math.cos(a), c[1] + ry * math.sin(a), zz)))
    F = []
    for ri in range(len(rows) - 1):
        for k in range(nseg):
            a = ri * W + k
            f = (a, a + 1, a + 1 + W, a + W)
            p = [V[i] for i in f]
            nrm = (p[1] - p[0]).cross(p[3] - p[0])
            cen = sum(p, Vector()) / 4
            if nrm.dot(Vector((cen.x - c[0], cen.y - c[1], 0))) < 0:
                f = f[::-1]
            F.append(f)
    return V, F, False


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
GROUPS = {}
WEAPONS = []  # (joint, side, pose key)


def init():
    global COL
    reset()
    J.clear(); MATS.clear(); GROUPS.clear(); WEAPONS.clear()
    COL = bpy.context.scene.collection
    for m in STYLE['materials']:
        MATS[m['name']] = id_material(m['name'], m['id'])
    normal_material()


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


def part(name, frame, jnt, mat, geo, smooth=True, bevel=0.0, group=None):
    verts, faces, closed = geo
    bpy.context.view_layer.update()
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
    if group:
        GROUPS.setdefault(group, []).append(ob)
    return ob


HUMAN = dict(thigh=0.38, shin=0.36, ankle=0.08, hip_w=0.09, spine=0.12, chest=0.18, neck=0.20, head=0.07,
             sh_w=0.20, sh_z=0.15, upper=0.27, fore=0.25)


def humanoid(**kw):
    """Samurai-topology rig; any HUMAN length may be overridden. Returns the parameter dict."""
    rp = dict(HUMAN)
    rp.update(kw)
    yaw = dict(STYLE['rig']['yaw'])
    yaw.update(rp.get('yaw', {}))
    rp['hip_h'] = rp['ankle'] + rp['shin'] + rp['thigh'] + 0.04
    joint('root', None, (0, 0, rp['hip_h']))
    joint('pelvis', 'root', (0, 0, 0))
    joint('pelvis_geo', 'pelvis', (0, 0, 0), 'XYZ', yaw['pelvis'])
    joint('spine', 'pelvis', (0, 0, rp['spine']))
    joint('spine_geo', 'spine', (0, 0, 0), 'XYZ', yaw['spine'])
    joint('chest', 'spine', (0, 0, rp['chest']))
    joint('chest_geo', 'chest', (0, 0, 0), 'XYZ', yaw['chest'])
    joint('neck', 'chest', (0, 0, rp['neck']))
    joint('head', 'neck', (0, 0, rp['head']))
    joint('head_geo', 'head', (0, 0, 0), 'XYZ', yaw['head'])
    for s, sy in (('R', -1), ('L', 1)):
        joint('sh_' + s, 'chest_geo', (0.0, rp['sh_w'] * sy, rp['sh_z']), 'YXZ', -yaw['chest'])
        joint('el_' + s, 'sh_' + s, (0, 0, -rp['upper']))
        joint('wr_' + s, 'el_' + s, (0, 0, -rp['fore']))
        joint('hip_' + s, 'pelvis_geo', (0, rp['hip_w'] * sy, -0.04), 'YXZ', -yaw['pelvis'])
        joint('kn_' + s, 'hip_' + s, (0, 0, -rp['thigh']))
        joint('an_' + s, 'kn_' + s, (0, 0, -rp['shin']))
    bpy.context.view_layer.update()
    return rp


def weapon(name, side, key, offset=(0, 0, -0.065)):
    """Weapon joint on wrist `side`; its +X axis points along pose key `key` (absolute math angle)."""
    joint(name, 'wr_' + side, offset)
    WEAPONS.append((name, side, key))


# ---------------------------------------------------------------- standard limb dressing
def legs(thigh_mat='cloth', shin_mat='cloth', boot_mat='boots', r=(0.08, 0.06, 0.055, 0.042), boot=(0.20, 0.09, 0.075),
         knee_mat=None, shin_guard=None):
    rp = HUMANP
    for s, sy in (('R', -1), ('L', 1)):
        hp, kn, an = 'hip_' + s, 'kn_' + s, 'an_' + s
        part('thigh_' + s, hp, hp, thigh_mat, cat(ellipsoid((0, 0, 0), (r[0], r[0], r[0])),
                                                  tube((0, 0, 0), (0, 0, -rp['thigh']), r[0], r[1])))
        part('shin_' + s, kn, kn, shin_mat, tube((0, 0, 0), (0, 0, -rp['shin']), r[2], r[3]))
        if knee_mat:
            part('knee_' + s, kn, kn, knee_mat, ellipsoid((0.035, 0, 0.0), (0.035, 0.05, 0.045)))
        if shin_guard:
            part('guard_' + s, kn, kn, shin_guard,
                 shell((0, 0), -75, 75, [(-0.04, r[2] + 0.012, r[2] + 0.012), (-rp['shin'] + 0.05, r[3] + 0.012, r[3] + 0.012)], 10))
        part('boot_' + s, an, an, boot_mat, cat(box((boot[0] * 0.25, 0, -boot[2] * 0.6), boot),
                                               ecyl((0, 0), -0.03, 0.05, r[3] + 0.004, r[3] + 0.004)), bevel=0.016)


def arms(side_mats=None, r=(0.05, 0.042, 0.045, 0.038), hand=(0.07, 0.06, 0.08), hand_mat='boots', sides='RL'):
    rp = HUMANP
    side_mats = side_mats or {}
    for s in sides:
        um, fm = side_mats.get(s, ('cloth', 'cloth'))
        part('upper_' + s, 'sh_' + s, 'sh_' + s, um, cat(ellipsoid((0, 0, 0), (r[0] + 0.004,) * 3),
                                                         tube((0, 0, 0), (0, 0, -rp['upper']), r[0], r[1])))
        part('fore_' + s, 'el_' + s, 'el_' + s, fm, cat(ellipsoid((0, 0, 0), (r[2],) * 3),
                                                        tube((0, 0, 0), (0, 0, -rp['fore']), r[2], r[3])))
        part('hand_' + s, 'wr_' + s, 'wr_' + s, hand_mat, box((0.01, 0, -hand[2] / 2), hand), bevel=0.012)


HUMANP = {}


def set_rig(rp):
    HUMANP.clear()
    HUMANP.update(rp)


# ---------------------------------------------------------------- gait (planted-foot walk/run cycles)
def leg_ik(hx, hz, ax, az, thigh, shin):
    """2-bone IK in the screen plane: hip at (hx, hz), ankle target (ax, az). Returns (hip, kn) in pose-key
    degrees (hip ABSOLUTE from straight down, + = forward; kn = knee bend, + = shin back). Out-of-reach targets
    straighten the leg toward them (the knee keeps 2 deg so it never pops backwards)."""
    dx, dz = ax - hx, az - hz
    d = max(1e-4, min(math.hypot(dx, dz), (thigh + shin) * 0.9994))
    base = math.atan2(dx, -dz)
    a = math.acos(max(-1.0, min(1.0, (thigh * thigh + d * d - shin * shin) / (2 * thigh * d))))
    k = math.acos(max(-1.0, min(1.0, (thigh * thigh + shin * shin - d * d) / (2 * thigh * shin))))
    return math.degrees(base + a), max(2.0, math.degrees(math.pi - k))


def gait_fps(n, speed, half_stride, duty):
    """Frames-per-second that makes an n-frame cycle plant its feet at `speed` (world units/s): the stance foot
    sweeps 2*half_stride while the body covers duty * cycle distance, so cycle distance D = 2*half_stride/duty
    and cycle time T = D / speed. Rounded to a whole fps; returns (fps, D)."""
    D = 2 * half_stride / duty
    fps = max(1, int(round(n * speed / D)))
    return fps, speed * n / fps


def gait(i, n, rp, speed, fps, duty=0.6, lift=0.12, bob=0.025, drop=None, centre=0.03, toe=30.0, heel=-14.0,
         lean=0.0, yaw_pelvis=None):
    """Leg keys + root bob for frame i of an n-frame planted-foot cycle (frame 0 = right heel contact, n/2 = left).
    Stance: the ankle slides back at exactly `speed` (no foot sliding at the authored fps), heel peels off
    over the last quarter of stance (toes down). Swing: eased forward arc `lift` high, toes up for the next
    contact. The pelvis drops `bob` at the down pose (just after each contact) and rises at the up pose; `drop`
    lowers the whole cycle so the front foot is reachable at contact (auto when None).
    Returns dict(hipR, knR, anR, hipL, knL, anL, dz, u) where u = cycle phase 0..1 (for arm swing etc.)."""
    T = n / fps
    D = speed * T
    s = duty * D                      # stance sweep of the foot relative to the body
    thigh, shin, ank = rp['thigh'], rp['shin'], rp['ankle']
    L = thigh + shin
    hip0 = rp['hip_h'] - 0.04         # hip joint height above the ground with the legs straight
    if drop is None:                  # reach the contact foot with ~4% knee bend left
        need = math.sqrt(max(0.05, (0.96 * L) ** 2 - (s / 2 + 0.02) ** 2)) + ank
        drop = max(0.0, hip0 - need) + 0.01
    u = (i / n) % 1.0
    # pelvis: lowest at the down pose (0.08 cycle after a contact), highest at the up pose (two bobs per cycle)
    dz = -drop - bob * 0.5 * (1 + math.cos(4 * math.pi * (u - 0.08)))
    yp = D2R(STYLE['rig']['yaw']['pelvis'] if yaw_pelvis is None else yaw_pelvis)
    th = D2R(lean)
    out = dict(dz=dz, u=u)
    for s_, sy, off in (('R', -1, 0.0), ('L', 1, 0.5)):
        q = (u - off) % 1.0
        # hip joint in the screen plane: (0, sy*hip_w, -0.04) under the pelvis yaw, then the forward lean
        hx0 = -sy * rp['hip_w'] * math.sin(yp)
        hx = hx0 * math.cos(th) - 0.04 * math.sin(th)
        hz = hip0 + 0.04 + dz - 0.04 * math.cos(th)
        if q < duty:                  # stance
            w = q / duty
            ax = centre + s / 2 - w * s
            peel = max(0.0, (w - 0.72) / 0.28)
            az = ank + 0.05 * peel * peel
            an = heel * max(0.0, 1 - w / 0.15) + toe * peel
        else:                         # swing
            w = (q - duty) / (1 - duty)
            e = 0.5 - 0.5 * math.cos(math.pi * w)
            ax = centre - s / 2 + e * s
            az = ank + 0.05 * (1 - w) + lift * math.sin(math.pi * min(1.0, w * 1.15))
            an = toe * (1 - w) ** 1.5 + heel * w * w
        hip, kn = leg_ik(hx, hz, ax, az, thigh, shin)
        out['hip' + s_], out['kn' + s_], out['an' + s_] = hip, kn, an
    return out


def plant(rp, dx=0.0, dz=0.0, fR=0.12, fL=-0.12, lean=0.0, zR=0.0, zL=0.0, yaw_pelvis=None):
    """Leg keys that keep the ankles at fixed ground positions (x relative to the sprite pivot, z = lift above
    the planted height) while the root moves by (dx, dz) and leans `lean`: weight shifts, recoils and lunges
    without the feet skating. Returns dict(hipR, knR, hipL, knL)."""
    yp = D2R(STYLE['rig']['yaw']['pelvis'] if yaw_pelvis is None else yaw_pelvis)
    th = D2R(lean)
    hip0 = rp['hip_h'] - 0.04
    out = {}
    for s_, sy, fx, fz in (('R', -1, fR, zR), ('L', 1, fL, zL)):
        hx0 = -sy * rp['hip_w'] * math.sin(yp)
        hx = dx + hx0 * math.cos(th) - 0.04 * math.sin(th)
        hz = hip0 + 0.04 + dz - 0.04 * math.cos(th)
        out['hip' + s_], out['kn' + s_] = leg_ik(hx, hz, fx, rp['ankle'] + fz, rp['thigh'], rp['shin'])
    return out


def base_feet(rp, base):
    """Ankle x of each leg in a base pose (FK from its hip/kn keys) - the planted spots for stand()."""
    out = []
    yp, th = D2R(STYLE['rig']['yaw']['pelvis']), D2R(base.get('lean', 0.0))
    for s, sy in (('R', -1), ('L', 1)):
        h, k = D2R(base.get('hip' + s, 0.0)), D2R(base.get('kn' + s, 0.0))
        hx = base.get('dx', 0.0) + (-sy * rp['hip_w'] * math.sin(yp)) * math.cos(th) - 0.04 * math.sin(th)
        out.append(hx + rp['thigh'] * math.sin(h) + rp['shin'] * math.sin(h - k))
    return tuple(out)


def stand(base, dx=0.0, dz=None, lean=None, fR=None, fL=None, zR=0.0, zL=0.0, **kw):
    """base pose + overrides with the feet planted where the base pose puts them (or at fR/fL): IK legs, so
    breathing, weight shifts, recoils and lunges move the body without the feet skating."""
    rp = HUMANP
    bR, bL = base_feet(rp, base)
    p = dict(base)
    p.update(kw)
    p['dx'] = dx
    p['dz'] = base.get('dz', 0.0) if dz is None else dz
    p['lean'] = base.get('lean', 0.0) if lean is None else lean
    p.update(plant(rp, dx, p['dz'], bR if fR is None else fR, bL if fL is None else fL, p['lean'], zR, zL))
    return p


def D2R(d):
    return math.radians(d)


def ease(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def blend(a, b, t, keys=None):
    """Linear blend of two pose dicts (numeric keys; non-numeric keys come from b when t >= 0.5 else a)."""
    out = {}
    for k in set(a) | set(b) if keys is None else keys:
        va, vb = a.get(k, 0.0), b.get(k, 0.0)
        if isinstance(va, (int, float)) and isinstance(vb, (int, float)):
            out[k] = va + (vb - va) * t
        else:
            out[k] = vb if t >= 0.5 else va
    return out


# ---------------------------------------------------------------- posing
def apply_pose(p, frame, loc_joints, groups):
    g = lambda k, d=0.0: float(p.get(k, d))
    Rr = {}
    if 'pelvis' in J and 'sh_R' in J:
        Rr = {'root': g('lean'), 'pelvis': g('pelvis'), 'spine': g('spine'), 'chest': g('chest'), 'neck': g('neck')}
        torso = Rr['root'] + Rr['pelvis'] + Rr['spine'] + Rr['chest']
        Rr['head'] = g('head') - (torso + Rr['neck'])
        hipb = Rr['root'] + Rr['pelvis']
        for s in 'RL':
            Rr['sh_' + s] = -g('sh' + s) - torso
            Rr['el_' + s] = -g('el' + s)
            Rr['wr_' + s] = -g('wr' + s)
            Rr['hip_' + s] = -g('hip' + s) - hipb
            Rr['kn_' + s] = g('kn' + s)
            Rr['an_' + s] = g('an' + s) - (hipb + Rr['hip_' + s] + Rr['kn_' + s])
        for w, s, key in WEAPONS:
            Rr[w] = -g(key) - (torso + Rr['sh_' + s] + Rr['el_' + s] + Rr['wr_' + s])
    else:
        Rr['root'] = g('lean')
    for k, v in p.get('rot', {}).items():
        Rr[k] = Rr.get(k, 0.0) + v
    for name, e in J.items():
        yz = D(g('yaw')) if name == 'root' else 0.0  # yaw: whole-body turn about world Z (turn anims)
        e.rotation_euler = (0.0, D(Rr.get(name, 0.0)), e['rest_z'] + yz)
        e.keyframe_insert('rotation_euler', frame=frame)
    root = J['root']
    rl = root['rest_loc']
    root.location = (rl[0] + g('dx'), rl[1], rl[2] + g('dz'))
    root.keyframe_insert('location', frame=frame)
    lo = p.get('loc', {})
    for name in loc_joints:
        e = J[name]
        rl = e['rest_loc']
        d = lo.get(name, (0.0, 0.0))
        e.location = (rl[0] + d[0], rl[1], rl[2] + d[1])
        e.keyframe_insert('location', frame=frame)
    show = set(p.get('show', []))
    for gname, obs in groups.items():
        for ob in obs:
            ob.hide_render = gname not in show
            ob.keyframe_insert('hide_render', frame=frame)


def animate(anims, meta, out):
    """anims: [dict(name, n, fps, loop, pose=fn(i, n) -> pose dict)]; meta: cell + json notes."""
    sc = bpy.context.scene
    frame = 1
    manifest = []
    poses = []
    for an in anims:
        start = frame
        for i in range(an['n']):
            poses.append((an['pose'](i, an['n']), frame))
            frame += 1
        manifest.append(dict(name=an['name'], start=start, count=an['n'], fps=an['fps'], loop=an['loop']))
    loc_joints = sorted({k for p, _ in poses for k in p.get('loc', {})})
    for p, f in poses:
        apply_pose(p, f, loc_joints, GROUPS)
    for act in bpy.data.actions:  # constant interpolation: frames are discrete poses
        try:
            fcs = act.fcurves
        except AttributeError:
            fcs = [fc for layer in act.layers for strip in layer.strips for cb in strip.channelbags for fc in cb.fcurves]
        for fc in fcs:
            for kp in fc.keyframe_points:
                kp.interpolation = 'CONSTANT'
    sc.frame_start, sc.frame_end = 1, frame - 1
    sc['redline_anims'] = json.dumps(manifest)
    sc['redline_meta'] = json.dumps(meta)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(out), compress=True)
    print('SAVED', out, 'frames', frame - 1)
