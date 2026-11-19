"""Build and render the Redline rooftop prop set (static, one frame each) for the 3D-to-pixel pipeline.

Run:  blender -b --factory-startup -P build_props.py -- --out /abs/render_dir [--ppu 48] [--scale 8] [--only a,b]
(see tools/art/props/make_props.sh). Writes <prop>_id.png (R = material id * 10, G = AO) and <prop>_n.png
(sprite-space normal) per prop plus manifest.json; tools/art/props/pack_props.py reduces and packs them.

Conventions: one world unit = one tile = 48 px (Pixel3DCamera.TexelsPerUnit). Props stand on z = 0 at the origin (anchor 'floor') or hang
from z = 0 (anchor 'ceiling'). X right, Z up, -Y toward the camera. The camera is orthographic, tilted
TILT degrees down so top faces read (puddles, roofs, crate lids); each prop is yawed a little for a 3/4 read.
Every frame is framed on its own: the origin lands on a pixel corner at the bottom-centre of the cell.
Geometry primitives come from tools/art/common/kit.py (imported, not modified); materials from props_style.json.
"""
import bpy, bmesh, math, json, os, sys, random
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..', 'common'))
import kit  # noqa: E402
from kit import box, tube, ecyl, lathe, ellipsoid, extrude2d, cat, D  # noqa: E402

STYLE = json.load(open(os.path.join(HERE, 'props_style.json')))
kit.STYLE = STYLE  # kit.id_material reads its module-level STYLE at call time
TILT = 11.0
MATS = {}
OBS = []


# ---------------------------------------------------------------- scene helpers
def init():
    kit.reset()
    MATS.clear()
    for m in STYLE['materials']:
        MATS[m['name']] = kit.id_material(m['name'], m['id'])
    MATS['_none'] = kit.id_material('_none', 0)


def cam_axes():
    t = D(TILT)
    right = Vector((1, 0, 0))
    up = Vector((0, math.sin(t), math.cos(t)))
    back = Vector((0, -math.cos(t), math.sin(t)))
    return right, up, back


def normal_material():
    """Sprite-space normal for the tilted camera: (N.right, N.up, N.back) * 0.5 + 0.5."""
    m = bpy.data.materials.new('NORMAL_PASS_PROPS')
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    em = nt.nodes.new('ShaderNodeEmission')
    geo = nt.nodes.new('ShaderNodeNewGeometry')
    comb = nt.nodes.new('ShaderNodeCombineXYZ')
    for i, axis in enumerate(cam_axes()):
        dot = nt.nodes.new('ShaderNodeVectorMath')
        dot.operation = 'DOT_PRODUCT'
        dot.inputs[1].default_value = tuple(axis)
        nt.links.new(geo.outputs['Normal'], dot.inputs[0])
        nt.links.new(dot.outputs['Value'], comb.inputs[i])
    vm = nt.nodes.new('ShaderNodeVectorMath')
    vm.operation = 'MULTIPLY_ADD'
    vm.inputs[1].default_value = (0.5, 0.5, 0.5)
    vm.inputs[2].default_value = (0.5, 0.5, 0.5)
    nt.links.new(comb.outputs[0], vm.inputs[0])
    nt.links.new(vm.outputs[0], em.inputs['Color'])
    nt.links.new(em.outputs[0], out.inputs['Surface'])
    return m


def obj(name, mat, geo, smooth=False, bevel=0.0):
    verts, faces, closed = geo
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], faces)
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
    bpy.context.scene.collection.objects.link(ob)
    if bevel > 0:
        md = ob.modifiers.new('bevel', 'BEVEL')
        md.width = bevel
        md.segments = 1
        md.limit_method = 'ANGLE'
    OBS.append(ob)
    return ob


def text(body, mat, loc, size, depth=0.012):
    """Flat text facing the camera (-Y), centred on loc, converted to a mesh."""
    cu = bpy.data.curves.new('txt', 'FONT')
    cu.body = body
    cu.size = size
    cu.extrude = depth
    cu.align_x = 'CENTER'
    cu.align_y = 'CENTER'
    tob = bpy.data.objects.new('txt', cu)
    bpy.context.scene.collection.objects.link(tob)
    tob.location = loc
    tob.rotation_euler = (math.pi / 2, 0, 0)
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(tob.evaluated_get(dg))
    me.transform(tob.matrix_world)
    bpy.data.objects.remove(tob, do_unlink=True)
    me.materials.clear()
    me.materials.append(MATS[mat])
    ob = bpy.data.objects.new('txt_mesh', me)
    bpy.context.scene.collection.objects.link(ob)
    OBS.append(ob)
    return ob


def ytube(x, z, y0, y1, r, n=18):
    """Cylinder along Y (axle toward the camera)."""
    return tube((x, y0, z), (x, y1, z), r, r, n)


def polyline(pts, r, n=6):
    return cat(*[tube(pts[i], pts[i + 1], r, r, n) for i in range(len(pts) - 1)])


def sag(p0, p1, drop, r, seg=14):
    p0, p1 = Vector(p0), Vector(p1)
    pts = []
    for i in range(seg + 1):
        k = i / seg
        p = p0.lerp(p1, k)
        p.z -= drop * 4 * k * (1 - k)
        pts.append(p)
    return polyline(pts, r)


def ring(c, z0, z1, r, n=24):
    return ecyl(c, z0, z1, r, r, n)


# ---------------------------------------------------------------- props
def ac_unit():
    obj('body', 'metal', box((0, 0, 0.5), (1.3, 0.7, 0.72)), bevel=0.02)
    obj('lid', 'gunmetal', box((0, 0, 0.88), (1.36, 0.76, 0.05)))
    for x in (-0.5, 0.5):
        obj('skid%d' % x, 'dark', box((x, 0, 0.07), (0.12, 0.8, 0.14)))
    obj('fanring', 'dark', ytube(-0.22, 0.5, -0.38, -0.3, 0.29, 28), smooth=True)
    obj('fanhole', 'cable', ytube(-0.22, 0.5, -0.4, -0.32, 0.24, 28), smooth=True)
    for k in range(4):
        a = k * 90 + 20
        obj('blade%d' % k, 'gunmetal', box((-0.22 + 0.1 * math.cos(D(a)), -0.41, 0.5 + 0.1 * math.sin(D(a))),
                                            (0.2, 0.01, 0.07), (0, -a, 0)))
    obj('hub', 'steel', ytube(-0.22, 0.5, -0.44, -0.4, 0.04, 10))
    for z in (0.32, 0.42, 0.52, 0.62):
        obj('grill%.2f' % z, 'wire', box((-0.22, -0.45, z), (0.5, 0.01, 0.012)))
    for i in range(6):
        obj('fin%d' % i, 'dark', box((0.2 + i * 0.075, -0.36, 0.5), (0.025, 0.02, 0.5)))
    obj('sticker', 'hazard', box((0.42, -0.36, 0.8), (0.24, 0.01, 0.06)))
    obj('pipe', 'cable', polyline([(0.6, 0.1, 0.7), (0.78, 0.1, 0.7), (0.82, 0.1, 0.3), (0.86, 0.1, 0.02)], 0.03))
    obj('streak', 'rust', box((-0.6, -0.355, 0.42), (0.05, 0.01, 0.4)))
    return dict(layer='background-decor', anchor='floor', yaw=-18)


def fan_tower():
    obj('housing', 'gunmetal', box((0, 0, 0.85), (0.95, 0.6, 1.6)), bevel=0.02)
    obj('cap', 'dark', box((0, 0, 1.7), (1.02, 0.66, 0.08)))
    obj('base', 'dark', box((0, 0, 0.04), (1.05, 0.7, 0.08)))
    for z in (0.5, 1.2):
        obj('ring%.1f' % z, 'metal', ytube(0, z, -0.34, -0.29, 0.36, 28), smooth=True)
        obj('hole%.1f' % z, 'cable', ytube(0, z, -0.36, -0.31, 0.31, 28), smooth=True)
        for k in range(5):
            a = k * 72 + z * 40
            obj('bl%.1f%d' % (z, k), 'metal', box((0.13 * math.cos(D(a)), -0.37, z + 0.13 * math.sin(D(a))),
                                                   (0.25, 0.01, 0.08), (0, -a - 25, 0)))
        obj('hub%.1f' % z, 'steel', ytube(0, z, -0.4, -0.36, 0.05, 10))
        for dz in (-0.2, 0.0, 0.2):
            obj('guard%.1f%.1f' % (z, dz), 'wire', box((0, -0.41, z + dz), (0.66, 0.01, 0.012)))
    obj('warn', 'hazard', box((0, -0.31, 1.62), (0.9, 0.01, 0.06)))
    obj('light', 'red', box((0.38, -0.32, 1.58), (0.06, 0.02, 0.06)))
    obj('duct', 'rust', box((0.55, 0.1, 0.35), (0.18, 0.3, 0.5)))
    return dict(layer='background-decor', anchor='floor', yaw=-14)


def vent_stack():
    for x, h, r in ((-0.25, 2.3, 0.13), (0.15, 1.6, 0.1), (0.42, 1.1, 0.08)):
        obj('pipe%.2f' % x, 'metal', tube((x, 0, 0), (x, 0, h), r, r, 18), smooth=True)
        obj('flange%.2f' % x, 'dark', ring((x, 0), h * 0.45, h * 0.45 + 0.05, r + 0.025, 18), smooth=True)
        obj('rust%.2f' % x, 'rust', ring((x, 0), h * 0.12, h * 0.3, r + 0.004, 18), smooth=True)
        obj('cap%.2f' % x, 'gunmetal', lathe((x, 0), [(h + 0.06, r * 2.0), (h + 0.08, r * 2.0), (h + 0.2, 0.0)], 18), smooth=True)
        obj('strut%.2f' % x, 'steel', tube((x - r * 1.6, 0, h + 0.07), (x + r * 1.6, 0, h + 0.07), 0.008, 0.008, 6))
        obj('base%.2f' % x, 'dark', ring((x, 0), 0, 0.06, r + 0.05, 18))
    obj('guy', 'wire', tube((-0.25, 0, 1.9), (-0.75, -0.1, 0.0), 0.006, 0.006, 6))
    return dict(layer='background-decor', anchor='floor', yaw=0)


def exhaust_elbow():
    obj('base', 'concrete', box((0, 0, 0.1), (0.7, 0.6, 0.2)), bevel=0.02)
    obj('up', 'gunmetal', tube((-0.15, 0, 0.2), (-0.15, 0, 1.3), 0.15, 0.15, 20), smooth=True)
    pts = [(-0.15 + 0.3 - 0.3 * math.cos(D(a)), 0, 1.3 + 0.3 * math.sin(D(a))) for a in range(0, 91, 15)]
    obj('elbow', 'gunmetal', polyline(pts, 0.15, 20), smooth=True)
    obj('across', 'gunmetal', tube((0.15, 0, 1.6), (0.75, 0, 1.6), 0.15, 0.15, 20), smooth=True)
    obj('down', 'gunmetal', tube((0.9, 0, 1.45), (0.9, 0, 1.15), 0.15, 0.15, 20), smooth=True)
    pts = [(0.75 + 0.15 * math.sin(D(a)), 0, 1.45 + 0.15 * math.cos(D(a))) for a in range(0, 91, 15)]
    obj('elbow2', 'gunmetal', polyline(pts, 0.15, 20), smooth=True)
    obj('mouth', 'dark', lathe((0.9, 0), [(1.0, 0.24), (1.15, 0.16)], 20), smooth=True)
    for z in (0.45, 1.0):
        obj('band%.2f' % z, 'rust', ring((-0.15, 0), z, z + 0.06, 0.165, 20), smooth=True)
    obj('clamp', 'steel', box((0.45, 0, 1.6), (0.05, 0.36, 0.36)))
    obj('soot', 'cable', ring((0.9, 0), 1.0, 1.04, 0.245, 20))
    return dict(layer='background-decor', anchor='floor', yaw=-25)


def water_tank():
    for x in (-0.5, 0.5):
        for y in (-0.5, 0.5):
            obj('leg%.1f%.1f' % (x, y), 'dark', box((x, y, 0.7), (0.07, 0.07, 1.4)))
    obj('braceA', 'steel', tube((-0.5, -0.5, 0.1), (0.5, -0.5, 1.3), 0.012, 0.012, 6))
    obj('braceB', 'steel', tube((0.5, -0.5, 0.1), (-0.5, -0.5, 1.3), 0.012, 0.012, 6))
    obj('deck', 'metal', box((0, 0, 1.43), (1.3, 1.3, 0.07)))
    obj('tank', 'wood', ecyl((0, 0), 1.46, 2.7, 0.62, 0.62, 28), smooth=True)
    for z in (1.65, 2.05, 2.45):
        obj('hoop%.2f' % z, 'dark', ring((0, 0), z, z + 0.04, 0.635, 28), smooth=True)
    obj('roof', 'metal', lathe((0, 0), [(2.7, 0.68), (2.74, 0.68), (3.05, 0.0)], 28), smooth=True)
    obj('finial', 'steel', tube((0, 0, 3.0), (0, 0, 3.2), 0.015, 0.015, 6))
    for x in (0.3, 0.46):
        obj('ladder%.2f' % x, 'wire', tube((x, -0.66, 0.0), (x, -0.66, 2.7), 0.012, 0.012, 6))
    for i in range(14):
        z = 0.15 + i * 0.18
        obj('rung%d' % i, 'wire', tube((0.3, -0.66, z), (0.46, -0.66, z), 0.01, 0.01, 6))
    obj('outlet', 'rust', polyline([(-0.3, -0.2, 1.43), (-0.3, -0.2, 0.9), (-0.7, -0.2, 0.9), (-0.7, -0.2, 0.0)], 0.04, 10))
    return dict(layer='background-decor', anchor='floor', yaw=-12)


def antenna_mast():
    h = 3.6
    for a in (90, 210, 330):
        x, y = 0.16 * math.cos(D(a)), 0.16 * math.sin(D(a))
        obj('rail%d' % a, 'gunmetal', tube((x * 1.6, y * 1.6, 0), (x * 0.4, y * 0.4, h), 0.02, 0.016, 6))
    for i in range(12):
        z0, z1 = i * h / 12, (i + 1) * h / 12
        w0, w1 = 0.25 * (1 - 0.75 * z0 / h), 0.25 * (1 - 0.75 * z1 / h)
        s = 1 if i % 2 else -1
        obj('zig%d' % i, 'wire', tube((-w0 * s, -0.1, z0), (w1 * s, -0.1, z1), 0.008, 0.008, 6))
    for z, w in ((2.2, 0.55), (2.9, 0.4), (3.3, 0.28)):
        obj('arm%.1f' % z, 'gunmetal', tube((-w, 0, z), (w, 0, z), 0.015, 0.015, 6))
        for x in (-w, w):
            obj('rod%.1f%.1f' % (z, x), 'steel', tube((x, 0, z - 0.25), (x, 0, z + 0.1), 0.01, 0.01, 6))
    obj('whip', 'steel', tube((0, 0, h), (0, 0, h + 0.4), 0.012, 0.004, 6))
    obj('tip', 'red', ellipsoid((0, -0.02, h + 0.03), (0.05, 0.05, 0.05), nu=10, nv=6))
    obj('mid', 'red', ellipsoid((0.12, -0.12, 1.9), (0.04, 0.04, 0.04), nu=10, nv=6))
    obj('box', 'metal', box((0.2, -0.12, 0.35), (0.22, 0.14, 0.3)), bevel=0.01)
    obj('foot', 'concrete', box((0, 0, 0.06), (0.7, 0.6, 0.12)))
    return dict(layer='background-decor', anchor='floor', yaw=0)


def sat_dish():
    obj('post', 'dark', tube((0, 0, 0), (0, 0, 0.9), 0.05, 0.05, 10))
    obj('foot', 'concrete', box((0, 0, 0.06), (0.5, 0.5, 0.12)))
    dish = obj('dish', 'metal', lathe((0, 0), [(0.0, 0.0), (0.02, 0.2), (0.07, 0.4), (0.15, 0.58), (0.17, 0.58),
                                               (0.09, 0.4), (0.04, 0.2), (0.03, 0.0)], 28), smooth=True)
    arm = obj('feed', 'steel', cat(tube((0, 0, 0.03), (0, 0, 0.55), 0.012, 0.012, 6),
                                   tube((0.4, 0, 0.12), (0, 0, 0.55), 0.008, 0.008, 6),
                                   tube((-0.4, 0, 0.12), (0, 0, 0.55), 0.008, 0.008, 6)))
    horn = obj('horn', 'dark', box((0, 0, 0.58), (0.08, 0.08, 0.09)))
    m = Matrix.Translation((0.05, 0, 1.0)) @ Matrix.Rotation(D(-38), 4, 'Y') @ Matrix.Rotation(D(30), 4, 'X')
    for o in (dish, arm, horn):
        o.data.transform(m)
    obj('led', 'red', box((0.0, -0.06, 0.8), (0.03, 0.02, 0.03)))
    obj('cable', 'cable', polyline([(0.0, -0.05, 0.85), (0.08, -0.08, 0.5), (0.1, -0.08, 0.1), (0.35, -0.08, 0.0)], 0.018))
    return dict(layer='background-decor', anchor='floor', yaw=-10)


def cable_spool():
    for y in (-0.3, 0.3):
        obj('disk%.1f' % y, 'wood', ytube(0, 0.55, y - 0.03, y + 0.03, 0.55, 28), smooth=False)
        obj('rim%.1f' % y, 'dark', ytube(0, 0.55, y - 0.035, y - 0.03, 0.12, 14))
    obj('core', 'cable', ytube(0, 0.55, -0.27, 0.27, 0.38, 28), smooth=True)
    for i in range(5):
        y = -0.24 + i * 0.12
        obj('wrap%d' % i, 'gunmetal', ytube(0, 0.55, y, y + 0.012, 0.385, 28), smooth=True)
    obj('bolt', 'steel', ytube(0, 0.55, -0.36, -0.33, 0.04, 8))
    obj('loose', 'cable', polyline([(0.3, -0.1, 0.8), (0.55, -0.15, 0.5), (0.6, -0.2, 0.05), (0.95, -0.25, 0.02), (1.1, -0.2, 0.06)], 0.025))
    obj('stencil', 'hazard', box((0.0, -0.335, 0.92), (0.3, 0.005, 0.05)))
    return dict(layer='foreground-occluder', anchor='floor', yaw=-25)


def cable_posts():
    for x in (-1.5, 1.5):
        obj('post%.1f' % x, 'dark', box((x, 0, 1.0), (0.09, 0.09, 2.0)))
        obj('bar%.1f' % x, 'gunmetal', box((x, 0, 1.85), (0.5, 0.06, 0.05)))
        for dx in (-0.2, 0.0, 0.2):
            obj('ins%.1f%.1f' % (x, dx), 'concrete', tube((x + dx, 0, 1.88), (x + dx, 0, 1.97), 0.025, 0.02, 8))
    for dx, drop in ((-0.2, 0.55), (0.0, 0.7), (0.2, 0.6)):
        obj('cab%.1f' % dx, 'cable', sag((-1.5 + dx, 0, 1.97), (1.5 + dx, 0, 1.97), drop, 0.016))
    obj('lowcab', 'cable', sag((-1.5, -0.05, 1.3), (1.5, -0.05, 1.1), 0.5, 0.025))
    obj('tag', 'red', box((0.2, -0.08, 1.37), (0.05, 0.02, 0.05)))
    obj('junction', 'metal', box((-1.5, -0.08, 0.8), (0.22, 0.1, 0.3)), bevel=0.01)
    return dict(layer='background-decor', anchor='floor', yaw=0)


def crates():
    def crate(cx, cz, s, mat, yaw):
        hz = s / 2
        parts = [box((0, 0, 0), (s, s, s))]
        for e in (-1, 1):
            parts.append(box((e * (hz - 0.03), -0.005, 0), (0.06, s + 0.02, s + 0.02)))
            parts.append(box((0, -0.005, e * (hz - 0.03)), (s + 0.02, s + 0.02, 0.06)))
        parts.append(box((0, -hz - 0.006, 0), (s * 1.25, 0.012, 0.05), (0, -45, 0)))
        v, f, c = cat(*parts)
        m = Matrix.Translation((cx, 0, cz + hz)) @ Matrix.Rotation(D(yaw), 4, 'Z')
        return [m @ Vector(p) for p in v], f, c
    obj('c1', 'wood', crate(-0.35, 0, 0.7, 'wood', 5))
    obj('c2', 'teal', crate(0.42, 0, 0.6, 'teal', -8))
    obj('c3', 'wood', crate(-0.15, 0.7, 0.55, 'wood', 14))
    obj('stamp', 'hazard', box((0.42, -0.33, 0.3), (0.2, 0.01, 0.08)))
    obj('strap', 'cable', box((-0.35, -0.36, 0.35), (0.04, 0.01, 0.7)))
    return dict(layer='foreground-occluder', anchor='floor', yaw=-6)


def barrels():
    def barrel(x, y, mat, h=0.9, r=0.3, tilt=0):
        rows = [(0, r * 0.92), (0.03, r), (h * 0.33, r * 1.02), (h * 0.36, r * 1.05), (h * 0.39, r * 1.02),
                (h * 0.64, r * 1.02), (h * 0.67, r * 1.05), (h * 0.7, r * 1.02), (h - 0.03, r), (h, r * 0.92)]
        o = obj('barrel%.1f' % x, mat, lathe((x, y), rows, 24), smooth=True)
        if tilt:
            o.data.transform(Matrix.Translation((x, y, 0)) @ Matrix.Rotation(D(tilt), 4, 'Y') @ Matrix.Translation((-x, -y, 0)))
        return o
    barrel(-0.38, 0.1, 'paintred')
    barrel(0.28, 0.15, 'rust')
    barrel(-0.02, -0.3, 'teal', 0.82, 0.28)
    obj('band', 'hazard', ring((-0.02, -0.3), 0.36, 0.44, 0.297, 24), smooth=True)
    obj('lid', 'dark', ecyl((0.28, 0.15), 0.9, 0.915, 0.27, 0.27, 20))
    obj('drip', 'cable', box((-0.38, -0.21, 0.6), (0.04, 0.01, 0.3)))
    return dict(layer='foreground-occluder', anchor='floor', yaw=0)


def container():
    def unit(z0, w, h, mat, ribs):
        obj('cont%.1f' % z0, mat, box((0, 0, z0 + h / 2), (w, 0.9, h)), bevel=0.015)
        for i in range(ribs):
            x = -w / 2 + 0.12 + i * (w - 0.24) / (ribs - 1)
            obj('rib%.1f%d' % (z0, i), mat, box((x, -0.46, z0 + h / 2), (0.05, 0.02, h - 0.12)))
        obj('rail%.1f' % z0, 'dark', box((0, -0.455, z0 + h - 0.04), (w, 0.03, 0.06)))
        obj('rail_b%.1f' % z0, 'dark', box((0, -0.455, z0 + 0.04), (w, 0.03, 0.06)))
        for x in (-w / 2, w / 2):
            obj('post%.1f%.1f' % (z0, x), 'dark', box((x, -0.43, z0 + h / 2), (0.08, 0.08, h)))
    unit(0.0, 2.6, 1.1, 'teal', 16)
    unit(1.1, 2.0, 1.0, 'rust', 12)
    obj('code', 'hazard', box((-0.6, -0.475, 0.85), (0.5, 0.01, 0.1)))
    text('07', 'concrete', (0.55, -0.48, 1.6), 0.42)
    obj('runoff', 'cable', box((0.95, -0.47, 0.4), (0.05, 0.01, 0.7)))
    return dict(layer='background-decor', anchor='floor', yaw=-8)


def fence():
    w, h = 2.4, 1.5
    for i in range(4):
        x = -w / 2 + i * w / 3
        obj('post%d' % i, 'gunmetal', tube((x, 0, 0), (x, 0, h + 0.05), 0.03, 0.03, 8))
        obj('foot%d' % i, 'concrete', box((x, 0, 0.05), (0.14, 0.14, 0.1)))
    obj('top', 'gunmetal', tube((-w / 2, 0, h), (w / 2, 0, h), 0.022, 0.022, 8))
    obj('bot', 'gunmetal', tube((-w / 2, 0, 0.12), (w / 2, 0, 0.12), 0.015, 0.015, 8))
    z0, z1 = 0.14, h - 0.02
    span = z1 - z0
    k = -w / 2 - span
    step = 0.34
    while k < w / 2:
        for s in (1, -1):
            xa, xb = (k, k + span) if s > 0 else (k + span, k)
            za, zb = z0, z1
            # clip the diagonal (xa,za)-(xb,zb) to -w/2..w/2 in x
            ta, tb = 0.0, 1.0
            for lim, sign in ((-w / 2, 1), (w / 2, -1)):
                dx = xb - xa
                if abs(dx) < 1e-9:
                    continue
                t = (lim - xa) / dx
                if (dx > 0) == (sign > 0):
                    ta = max(ta, t)
                else:
                    tb = min(tb, t)
            torn = 0.25 < k < 0.55 and s < 0
            if tb - ta > 0.04 and not torn:
                pa = (xa + (xb - xa) * ta, -0.01, za + (zb - za) * ta)
                pb = (xa + (xb - xa) * tb, -0.01, za + (zb - za) * tb)
                obj('mesh%.2f%d' % (k, s), 'wire', tube(pa, pb, 0.006, 0.006, 4))
        k += step
    obj('sign', 'paintred', box((-0.6, -0.04, 0.9), (0.4, 0.01, 0.28)))
    obj('signbar', 'concrete', box((-0.6, -0.05, 0.9), (0.3, 0.01, 0.05)))
    return dict(layer='foreground-occluder', anchor='floor', yaw=0)


def billboard():
    for x in (-1.1, 1.1):
        obj('leg%.1f' % x, 'dark', box((x, 0.2, 0.75), (0.12, 0.12, 1.5)))
        obj('brace%.1f' % x, 'gunmetal', tube((x, 0.2, 0.1), (x * 0.5, 0.2, 1.45), 0.02, 0.02, 6))
    obj('walk', 'gunmetal', box((0, -0.15, 1.45), (3.3, 0.5, 0.05)))
    for i in range(12):
        x = -1.6 + i * 3.2 / 11
        obj('rp%d' % i, 'wire', tube((x, -0.38, 1.47), (x, -0.38, 1.75), 0.008, 0.008, 4))
    obj('rail', 'wire', tube((-1.65, -0.38, 1.75), (1.65, -0.38, 1.75), 0.01, 0.01, 6))
    obj('frame', 'dark', box((0, 0.05, 2.25), (3.3, 0.12, 1.45)))
    obj('panel', 'panel', box((0, -0.02, 2.25), (3.1, 0.04, 1.25)))
    text('REDLINE', 'dark', (0, -0.05, 2.38), 0.62, 0.02)
    obj('bar', 'dark', box((0, -0.05, 1.86), (2.4, 0.02, 0.08)))
    obj('grime', 'dark', box((1.42, -0.045, 1.95), (0.05, 0.01, 0.5)))
    for x in (-1.1, 0, 1.1):
        obj('arm%.1f' % x, 'gunmetal', tube((x, -0.1, 3.0), (x, -0.45, 3.15), 0.015, 0.015, 6))
        obj('spot%.1f' % x, 'warm', box((x, -0.47, 3.12), (0.1, 0.06, 0.05)))
    return dict(layer='background-decor', anchor='floor', yaw=-6)


def neon_sign():
    obj('pole', 'dark', box((0.0, 0.05, 1.2), (0.08, 0.08, 2.4)))
    obj('plate', 'dark', box((0.32, 0.0, 1.55), (0.5, 0.06, 1.3)), bevel=0.01)
    obj('arm', 'gunmetal', box((0.15, 0.0, 2.25), (0.4, 0.05, 0.05)))
    r = 0.022
    strokes = [
        [(0.17, 2.0), (0.47, 2.0)],
        [(0.32, 2.12), (0.32, 1.55)],
        [(0.18, 1.78), (0.32, 1.66), (0.46, 1.8)],
        [(0.2, 1.42), (0.44, 1.42)],
        [(0.32, 1.42), (0.32, 1.0)],
        [(0.2, 1.22), (0.26, 1.08)],
        [(0.44, 1.24), (0.38, 1.08)],
    ]
    for i, s in enumerate(strokes):
        obj('neon%d' % i, 'neon', polyline([(x, -0.045, z) for x, z in s], r, 8), smooth=True)
    obj('box', 'metal', box((0.05, -0.06, 0.5), (0.16, 0.08, 0.22)))
    obj('feed', 'cable', polyline([(0.05, -0.06, 0.6), (0.12, -0.06, 0.9), (0.2, -0.04, 1.0)], 0.012))
    return dict(layer='background-decor', anchor='floor', yaw=-8)


def stair_hut():
    obj('walls', 'concrete', box((0, 0.1, 1.0), (1.7, 1.4, 2.0)), bevel=0.02)
    obj('roof', 'dark', box((0, 0.1, 2.06), (1.9, 1.6, 0.12)))
    obj('doorframe', 'gunmetal', box((-0.3, -0.6, 0.85), (0.72, 0.04, 1.6)))
    obj('door', 'warm', box((-0.3, -0.62, 0.8), (0.56, 0.02, 1.48)))
    obj('doorleaf', 'metal', box((-0.43, -0.66, 0.8), (0.28, 0.03, 1.48)))
    obj('lamp', 'warm', box((-0.3, -0.66, 1.78), (0.18, 0.06, 0.06)))
    obj('hood', 'dark', box((-0.3, -0.67, 1.83), (0.26, 0.1, 0.04)))
    obj('step', 'concrete', box((-0.3, -0.75, 0.05), (0.8, 0.3, 0.1)))
    obj('stripe', 'hazard', box((0.0, -0.605, 0.15), (1.7, 0.01, 0.12)))
    text('07', 'hazard', (0.5, -0.61, 1.3), 0.38, 0.004)
    obj('vent', 'metal', box((0.45, 0.2, 2.3), (0.4, 0.4, 0.38)))
    for i in range(4):
        obj('louvre%d' % i, 'dark', box((0.45, -0.01, 2.18 + i * 0.08), (0.34, 0.02, 0.025)))
    obj('pipe', 'rust', polyline([(0.8, -0.58, 2.0), (0.8, -0.58, 0.0)], 0.035, 10))
    obj('stain', 'cable', box((0.3, -0.605, 1.6), (0.14, 0.01, 0.5)))
    return dict(layer='background-decor', anchor='floor', yaw=-14)


def railing():
    w = 2.6
    for i in range(6):
        x = -w / 2 + i * w / 5
        obj('post%d' % i, 'hazard', box((x, 0, 0.55), (0.06, 0.06, 1.1)))
        obj('cap%d' % i, 'dark', box((x, 0, 0.05), (0.12, 0.12, 0.1)))
    obj('top', 'hazard', tube((-w / 2, 0, 1.1), (w / 2, 0, 1.1), 0.035, 0.035, 10), smooth=True)
    obj('mid', 'hazard', tube((-w / 2, 0, 0.6), (w / 2, 0, 0.6), 0.025, 0.025, 10), smooth=True)
    for i in range(10):
        x = -w / 2 + 0.13 + i * 0.26
        obj('band%d' % i, 'dark', box((x, 0, 1.1), (0.09, 0.08, 0.08)))
    obj('kick', 'gunmetal', box((0, 0, 0.12), (w, 0.03, 0.14)))
    return dict(layer='foreground-occluder', anchor='floor', yaw=0)


def puddle():
    obj('pool', 'water', ellipsoid((0, 0, 0.0), (1.0, 0.55, 0.012), nu=32, nv=6), smooth=True)
    obj('pool2', 'water', ellipsoid((0.75, 0.25, 0.0), (0.42, 0.28, 0.01), nu=24, nv=6), smooth=True)
    obj('glint', 'steel', box((-0.25, -0.2, 0.014), (0.5, 0.03, 0.004), (0, 0, 4)))
    return dict(layer='background-decor', anchor='floor', yaw=0)


def debris():
    obj('slab', 'concrete', box((-0.35, 0.0, 0.1), (0.6, 0.45, 0.2), (0, 9, 15)), bevel=0.02)
    obj('chunk', 'concrete', box((0.25, -0.15, 0.08), (0.22, 0.2, 0.16), (10, -20, 35)))
    obj('pipe', 'rust', tube((-0.1, -0.25, 0.06), (0.65, -0.05, 0.12), 0.05, 0.05, 12), smooth=True)
    for k, (a, b) in enumerate((((-0.5, 0.0, 0.15), (-0.7, -0.1, 0.55)), ((-0.35, 0.05, 0.2), (-0.25, -0.1, 0.5)))):
        obj('rebar%d' % k, 'steel', tube(a, b, 0.012, 0.012, 6))
    obj('can', 'paintred', ytube(0.45, 0.07, -0.3, -0.1, 0.07, 12))
    obj('sheet', 'gunmetal', box((0.0, 0.2, 0.05), (0.7, 0.4, 0.02), (6, -6, -10)))
    return dict(layer='foreground-occluder', anchor='floor', yaw=0)


def hang_cables():
    obj('bracket', 'dark', box((0, 0, -0.04), (0.9, 0.2, 0.08)))
    for x in (-0.35, 0.35):
        obj('hook%.2f' % x, 'gunmetal', box((x, 0, -0.12), (0.04, 0.04, 0.16)))
    for x0, x1, drop, r in ((-0.35, 0.35, 0.9, 0.02), (-0.35, 0.35, 0.6, 0.016), (-0.33, 0.37, 1.15, 0.024)):
        obj('loop%.2f' % drop, 'cable', sag((x0, -0.02, -0.18), (x1, -0.02, -0.18), drop, r))
    obj('drop', 'cable', polyline([(0.1, -0.03, -0.1), (0.12, -0.03, -0.9), (0.18, -0.04, -1.35)], 0.016))
    obj('jbox', 'metal', box((0.15, -0.06, -0.95), (0.2, 0.08, 0.24)), bevel=0.01)
    obj('jled', 'red', box((0.19, -0.11, -0.88), (0.03, 0.02, 0.03)))
    obj('tag', 'hazard', box((0.15, -0.105, -1.02), (0.14, 0.005, 0.03)))
    return dict(layer='hanging', anchor='ceiling', yaw=0)


PROPS = [ac_unit, fan_tower, vent_stack, exhaust_elbow, water_tank, antenna_mast, sat_dish, cable_spool,
         cable_posts, crates, barrels, container, fence, billboard, neon_sign, stair_hut, railing, puddle,
         debris, hang_cables]


# ---------------------------------------------------------------- render
def setup_render(S):
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = 1
    sc.cycles.use_adaptive_sampling = False
    sc.cycles.use_denoising = False
    sc.cycles.seed = 0
    sc.cycles.max_bounces = 0
    sc.cycles.filter_width = 0.01
    sc.view_settings.view_transform = 'Raw'
    sc.view_settings.look = 'None'
    try:
        sc.render.dither_intensity = 0
    except Exception:
        pass
    sc.render.film_transparent = True
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGBA'
    sc.render.image_settings.color_depth = '8'


def ground():
    """Camera-invisible ground (or ceiling) slab: contact AO only."""
    ob = obj('_ground', '_none', box((0, 0, -0.05), (12, 12, 0.1)))
    ob.visible_camera = False
    OBS.remove(ob)
    return ob


def frame(ppu, anchor):
    right, up, back = cam_axes()
    dg = bpy.context.evaluated_depsgraph_get()
    xs, ys = [], []
    for ob in OBS:
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        for v in me.vertices:
            p = ob.matrix_world @ v.co
            xs.append(p.dot(right))
            ys.append(p.dot(up))
        ev.to_mesh_clear()
    half = int(math.ceil(max(abs(min(xs)), abs(max(xs))) * ppu)) + 3
    lo = int(math.floor(min(ys) * ppu)) - 3
    hi = int(math.ceil(max(ys) * ppu)) + 3
    return 2 * half, hi - lo, -lo


def render_prop(fn, ppu, S, out, nmat):
    for o in list(bpy.data.objects):
        if o.type != 'CAMERA':
            bpy.data.objects.remove(o, do_unlink=True)
    OBS.clear()
    meta = fn()
    rz = Matrix.Rotation(D(meta['yaw']), 4, 'Z')
    for ob in OBS:
        ob.data.transform(rz)
    g = ground()
    if meta['anchor'] == 'ceiling':
        g.data.transform(Matrix.Translation((0, 0, 0.1)))
    bpy.context.view_layer.update()
    w, h, pad = frame(ppu, meta['anchor'])
    right, up, back = cam_axes()
    sc = bpy.context.scene
    cam = sc.camera
    cy = (h / 2 - pad) / ppu
    cam.location = up * cy + back * 20
    cam.rotation_euler = (math.pi / 2 - D(TILT), 0, 0)
    cam.data.ortho_scale = max(w, h) / ppu
    sc.render.resolution_x, sc.render.resolution_y = w * S, h * S
    vl = bpy.context.view_layer
    base = os.path.join(out, fn.__name__)
    vl.material_override = None
    sc.render.filepath = base + '_id.png'
    bpy.ops.render.render(write_still=True)
    vl.material_override = nmat
    sc.render.filepath = base + '_n.png'
    bpy.ops.render.render(write_still=True)
    vl.material_override = None
    print('RENDERED', fn.__name__, w, h, pad)
    return dict(name=fn.__name__, w=w, h=h, pad_bottom=pad, layer=meta['layer'], anchor=meta['anchor'])


def main():
    a = kit.args()
    ppu = float(a.get('ppu', 48))
    S = int(a.get('scale', 8))
    out = os.path.abspath(a['out'])
    only = set(a['only'].split(',')) if a.get('only') else None
    os.makedirs(out, exist_ok=True)
    init()
    nmat = normal_material()
    setup_render(S)
    cd = bpy.data.cameras.new('prop_cam')
    cd.type = 'ORTHO'
    cd.sensor_fit = 'AUTO'
    co = bpy.data.objects.new('prop_cam', cd)
    bpy.context.scene.collection.objects.link(co)
    bpy.context.scene.camera = co
    props = []
    for fn in PROPS:
        if only and fn.__name__ not in only:
            continue
        props.append(render_prop(fn, ppu, S, out, nmat))
    json.dump(dict(ppu=ppu, scale=S, tilt=TILT, props=props), open(os.path.join(out, 'manifest.json'), 'w'), indent=1)
    print('RENDERED_ALL', out, len(props))


main()
