"""Redline set-dressing kit: structural props for the mid-ground / foreground / per-level dressing.

Run (see make_dressing.sh):
  blender -b --factory-startup -P build_dressing.py -- --out /abs/Assets/Render3D/Resources/Props3D [--only a,b]

Conventions: Blender Z up, -Y toward the camera. Exported Y up, front facing Unity -Z. 1 unit = 1 Unity unit.
Material slot names (resolved by Props3D.Palette): metal, gunmetal, steel, dark, rust, concrete, hazard, brick, warm, neon.
Nothing thinner than 0.1 unit where it must read (2+ texels at 40 texels/unit is 0.05; we keep >= 0.1).
Budgets: hero <= 2000 tris, filler <= 500, occluder <= 800.
"""
import bpy, math, os, sys

OBS = []


def mat(name):
    m = bpy.data.materials.get(name)
    if not m:
        m = bpy.data.materials.new(name)
    return m


def box(mname, lo, hi, rot=None):
    """Axis box from corner lo to hi (Blender coords). rot = (rx,ry,rz) radians about its centre."""
    c = [(a + b) / 2 for a, b in zip(lo, hi)]
    s = [abs(b - a) for a, b in zip(lo, hi)]
    bpy.ops.mesh.primitive_cube_add(size=1, location=c)
    o = bpy.context.object
    o.scale = s
    if rot:
        o.rotation_euler = rot
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    o.data.materials.append(mat(mname))
    OBS.append(o)
    return o


def beam(mname, p0, p1, t=0.12, d=None):
    """Square-section bar between two points (for diagonals)."""
    d = d or t
    dx, dy, dz = (p1[i] - p0[i] for i in range(3))
    L = math.sqrt(dx * dx + dy * dy + dz * dz)
    c = [(p0[i] + p1[i]) / 2 for i in range(3)]
    bpy.ops.mesh.primitive_cube_add(size=1, location=c)
    o = bpy.context.object
    o.scale = (t, d, L)
    # point local Z along the direction
    from mathutils import Vector
    o.rotation_euler = Vector((dx, dy, dz)).to_track_quat('Z', 'Y').to_euler()
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    o.data.materials.append(mat(mname))
    OBS.append(o)
    return o


def cyl(mname, c, r, h, axis='Z', seg=12):
    bpy.ops.mesh.primitive_cylinder_add(vertices=seg, radius=r, depth=h, location=c)
    o = bpy.context.object
    if axis == 'X':
        o.rotation_euler = (0, math.pi / 2, 0)
    elif axis == 'Y':
        o.rotation_euler = (math.pi / 2, 0, 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    o.data.materials.append(mat(mname))
    OBS.append(o)
    return o


def ibeam(mname, x, y, z0, z1, w=0.7, dpt=0.7, f=0.14, web=0.12):
    """Vertical H column centred at x,y from z0..z1."""
    box(mname, (x - w / 2, y - dpt / 2, z0), (x + w / 2, y - dpt / 2 + f, z1))
    box(mname, (x - w / 2, y + dpt / 2 - f, z0), (x + w / 2, y + dpt / 2, z1))
    box(mname, (x - web / 2, y - dpt / 2, z0), (x + web / 2, y + dpt / 2, z1))


# ------------------------------------------------------------------------------------------------ pieces
def girder_col():
    """Steel H column 12 tall with base plate, two cross-braces and rivet bands."""
    ibeam('metal', 0, 0, 0, 12, 0.8, 0.8)
    box('gunmetal', (-0.7, -0.7, 0), (0.7, 0.7, 0.2))
    for z in (3, 6.5, 10):
        box('rust', (-0.45, -0.45, z), (0.45, 0.45, z + 0.14))
    return {}


def girder_x():
    """Brace frame: two columns 5 apart with an X-brace and top beam, 9 tall."""
    for x in (-2.5, 2.5):
        ibeam('metal', x, 0, 0, 9, 0.6, 0.6)
    box('metal', (-2.9, -0.3, 8.6), (2.9, 0.3, 9.1))
    beam('gunmetal', (-2.5, 0, 0.3), (2.5, 0, 8.6), 0.18, 0.3)
    beam('gunmetal', (2.5, 0, 0.3), (-2.5, 0, 8.6), 0.18, 0.3)
    box('rust', (-2.5, -0.3, 4.2), (2.5, 0.3, 4.45))
    return {}


def truss_span():
    """Lattice truss 10 long x 2.2 tall (horizontal member for crossing the frame)."""
    L, H = 10.0, 2.2
    box('metal', (-L / 2, -0.25, 0), (L / 2, 0.25, 0.22))
    box('metal', (-L / 2, -0.25, H - 0.22), (L / 2, 0.25, H))
    n = 8
    for i in range(n):
        x0 = -L / 2 + i * L / n
        x1 = x0 + L / n
        if i % 2 == 0:
            beam('gunmetal', (x0, 0, 0.2), (x1, 0, H - 0.2), 0.14, 0.2)
        else:
            beam('gunmetal', (x0, 0, H - 0.2), (x1, 0, 0.2), 0.14, 0.2)
        box('metal', (x0 - 0.07, -0.1, 0.2), (x0 + 0.07, 0.1, H - 0.2))
    box('metal', (L / 2 - 0.07, -0.1, 0.2), (L / 2 + 0.07, 0.1, H - 0.2))
    return {}


def crane():
    """Tower crane: lattice mast 18 tall, 16 m jib, counter-jib with weight, hook cable. Hero (~1.4k tris)."""
    M = 1.6
    for sx in (-1, 1):
        for sy in (-1, 1):
            box('metal', (sx * M / 2 - 0.08, sy * M / 2 - 0.08, 0), (sx * M / 2 + 0.08, sy * M / 2 + 0.08, 18))
    for i in range(9):
        z0 = i * 2
        box('gunmetal', (-M / 2, -M / 2 - 0.05, z0), (M / 2, -M / 2 + 0.05, z0 + 0.1))
        beam('gunmetal', (-M / 2, -M / 2, z0), (M / 2, -M / 2, z0 + 2), 0.1, 0.1)
        beam('gunmetal', (M / 2, -M / 2, z0), (-M / 2, -M / 2, z0 + 2), 0.1, 0.1)
    box('rust', (-1.1, -1.1, 18), (1.1, 1.1, 19.2))               # slew ring / cab
    box('dark', (-0.6, -1.5, 18.2), (0.6, -1.1, 19.0))            # cab window
    # jib (toward +X) and counter-jib
    box('metal', (-1, -0.2, 19.6), (16, 0.2, 19.9))
    box('metal', (-1, -0.2, 21.0), (16, 0.2, 21.2))
    for i in range(15):
        x0 = -1 + i * 1.0
        beam('gunmetal', (x0, 0, 19.8), (x0 + 1, 0, 21.1), 0.1, 0.12)
        box('gunmetal', (x0 + 1 - 0.05, -0.1, 19.8), (x0 + 1 + 0.05, 0.1, 21.1))
    box('metal', (-7, -0.2, 19.6), (-1, 0.2, 19.9))
    box('concrete', (-7, -0.7, 18.7), (-4.6, 0.7, 19.6))          # counterweight
    beam('steel', (0, 0, 24.2), (15.5, 0, 21.2), 0.08, 0.08)       # pendant stays
    beam('steel', (0, 0, 24.2), (-6.5, 0, 19.9), 0.08, 0.08)
    box('metal', (-0.3, -0.3, 21.2), (0.3, 0.3, 24.2))            # apex mast
    box('warm', (-0.2, -0.2, 24.2), (0.2, 0.2, 24.6))             # aircraft lamp
    box('dark', (11.9, -0.04, 9), (12.05, 0.04, 19.6))            # hook cable
    box('hazard', (11.6, -0.3, 8.2), (12.3, 0.3, 9))
    return {}


def pillar():
    """Concrete highway pillar 2.4x2.4, 10 tall, chamfered capital and hazard band."""
    box('concrete', (-1.2, -1.2, 0), (1.2, 1.2, 8.8))
    box('concrete', (-1.7, -1.7, 8.8), (1.7, 1.7, 10))
    box('dark', (-1.2, -1.21, 5.0), (1.2, -1.19, 5.1))
    box('hazard', (-1.22, -1.22, 1.2), (1.22, 1.22, 1.9))
    box('dark', (-1.4, -1.4, 0), (1.4, 1.4, 0.35))
    box('rust', (-1.0, -1.22, 2.6), (-0.75, -1.2, 7.8))          # water stain
    return {}


def overpass_span():
    """Elevated highway segment: deck 12 x 7 with parapets, edge hazard stripe, underside girders."""
    box('concrete', (-6, -3.5, 0.9), (6, 3.5, 1.5))
    for x in (-5, -2.5, 0, 2.5, 5):
        box('gunmetal', (x - 0.25, -3.4, 0), (x + 0.25, 3.4, 0.9))
    box('metal', (-6, -3.5, 0.3), (6, -3.2, 0.9))
    box('concrete', (-6, -3.5, 1.5), (6, -3.0, 2.5))              # front parapet
    box('hazard', (-6, -3.52, 1.55), (6, -3.5, 1.7))
    box('dark', (-6, -3.52, 1.9), (6, -3.5, 1.95))
    box('concrete', (-6, 3.0, 1.5), (6, 3.5, 2.5))
    for x in range(-6, 7, 3):
        box('concrete', (x - 0.2, -3.6, 1.5), (x + 0.2, -3.4, 2.7))
    return {}


def jersey_barrier():
    box('concrete', (-2, -0.4, 0), (2, 0.4, 0.2))
    box('concrete', (-2, -0.28, 0.2), (2, 0.28, 0.9))
    box('hazard', (-2, -0.3, 0.55), (2, -0.28, 0.7))
    box('concrete', (-2, -0.16, 0.9), (2, 0.16, 1.05))
    return {}


def guard_rail():
    """Highway guardrail 6 long with posts (reads at 0.15 thickness)."""
    box('steel', (-3, -0.1, 0.55), (3, 0.0, 0.95))
    box('steel', (-3, -0.1, 0.95), (3, 0.0, 1.0))
    for x in (-2.8, -0.9, 0.9, 2.8):
        box('gunmetal', (x - 0.07, 0, 0), (x + 0.07, 0.16, 1.0))
    return {}


def facade_block():
    """Brick facade slab 9 wide x 10 tall x 3 deep: window recesses (lit panes), cornice, drainpipe, fire escape."""
    box('brick', (-4.5, 0, 0), (4.5, 3, 10))
    box('concrete', (-4.7, -0.15, 9.5), (4.7, 3.1, 10.2))        # cornice
    box('concrete', (-4.6, -0.1, 0), (4.6, 0.1, 0.8))            # plinth
    for r in range(3):
        for c in range(4):
            x = -3.4 + c * 2.3
            z = 2.0 + r * 2.6
            box('dark', (x - 0.55, -0.12, z - 0.05), (x + 0.55, 0.0, z + 1.35))
            box('warm' if (r * 4 + c) % 5 == 1 else 'dark', (x - 0.42, -0.14, z + 0.08), (x + 0.42, -0.12, z + 1.22))
            box('concrete', (x - 0.65, -0.18, z - 0.12), (x + 0.65, -0.1, z - 0.02))   # sill
    box('gunmetal', (4.0, -0.25, 0.8), (4.18, -0.07, 9.5))        # drainpipe
    for r in range(3):                                             # fire escape landings
        z = 2.0 + r * 2.6 - 0.1
        box('metal', (-1.2, -0.9, z), (1.2, -0.1, z + 0.08))
        box('metal', (-1.2, -0.9, z), (-1.15, -0.1, z + 0.9))
        box('metal', (1.15, -0.9, z), (1.2, -0.1, z + 0.9))
    return {}


def gallery_arcade():
    """Gallery facade: 3 arches, colonnade, balcony rail, banner holders. 12 wide, 12 tall."""
    box('concrete', (-6, 0, 0), (6, 3, 3))
    box('concrete', (-6, 0, 3), (6, 3, 12))
    for i in range(4):
        x = -4.5 + i * 3
        box('concrete', (x - 0.4, -0.9, 0), (x + 0.4, -0.1, 3.4))   # pier / column in front
        box('concrete', (x - 0.6, -1.0, 3.2), (x + 0.6, -0.0, 3.6))
    for i in range(3):
        x = -3 + i * 3
        box('dark', (x - 1.0, -0.11, 0.1), (x + 1.0, 0.0, 2.6))     # doorway recess
        box('dark', (x - 0.8, -0.11, 2.6), (x + 0.8, 0.0, 2.9))
        box('warm', (x - 0.7, -0.12, 0.3), (x + 0.7, -0.11, 2.4))
    box('metal', (-6, -1.0, 3.6), (6, -0.9, 4.5))                   # balcony rail
    for x in [i * 0.5 - 6 for i in range(25)]:
        box('metal', (x - 0.04, -1.0, 3.6), (x + 0.04, -0.94, 4.5))
    for r in range(2):
        for c in range(6):
            x = -5 + c * 2
            z = 5.4 + r * 3
            box('dark', (x - 0.5, -0.1, z), (x + 0.5, 0.0, z + 1.8))
            box('warm' if (r + c) % 3 == 0 else 'dark', (x - 0.4, -0.12, z + 0.1), (x + 0.4, -0.1, z + 1.6))
    box('concrete', (-6.2, -0.4, 11.4), (6.2, 3.1, 12.2))
    return {}


def statue():
    """Blocky robed statue on a plinth, 6 tall."""
    box('concrete', (-1, -1, 0), (1, 1, 1.6))
    box('concrete', (-0.8, -0.8, 1.6), (0.8, 0.8, 1.9))
    box('gunmetal', (-0.5, -0.4, 1.9), (0.5, 0.4, 4.2))            # robe
    box('gunmetal', (-0.62, -0.3, 3.2), (-0.5, 0.3, 4.1))
    box('gunmetal', (0.5, -0.3, 3.2), (1.0, 0.3, 3.4))
    box('gunmetal', (0.9, -0.2, 3.4), (1.0, 0.2, 5.4))             # raised arm
    box('gunmetal', (-0.28, -0.28, 4.2), (0.28, 0.28, 4.8))        # head
    box('gunmetal', (-0.34, -0.34, 4.8), (0.34, 0.34, 4.9))
    return {}


def banner():
    """Pole + hanging cloth banner 1.6 x 5 with a stripe."""
    box('metal', (-1.0, -0.06, 5.0), (1.0, 0.06, 5.12))
    box('rust', (-0.8, -0.04, 0.0), (0.8, 0.04, 5.0))
    box('warm', (-0.8, -0.06, 2.2), (0.8, -0.04, 2.6))
    box('dark', (-0.8, -0.06, 3.4), (0.8, -0.04, 4.2))
    return {}


def chimney():
    """Brick chimney stack 10 tall, 1.8 wide, bands + rim + ladder."""
    box('brick', (-0.9, -0.9, 0), (0.9, 0.9, 10))
    for z in (2.5, 5.5, 8.5):
        box('concrete', (-1.02, -1.02, z), (1.02, 1.02, z + 0.2))
    box('concrete', (-1.15, -1.15, 9.8), (1.15, 1.15, 10.3))
    box('dark', (-0.7, -0.7, 10.25), (0.7, 0.7, 10.35))
    box('metal', (-0.4, -1.1, 0.5), (-0.3, -0.9, 9.3))
    box('metal', (0.3, -1.1, 0.5), (0.4, -0.9, 9.3))
    for z in [0.8 + i * 0.5 for i in range(17)]:
        box('metal', (-0.4, -1.08, z), (0.4, -0.95, z + 0.07))
    return {}


def pipe_run():
    """Industrial pipe bundle 10 long: 3 pipes, flanges, brackets."""
    for i, (y, z, r) in enumerate(((0, 0.5, 0.3), (0.1, 1.15, 0.22), (-0.1, 1.8, 0.16))):
        cyl('metal' if i != 1 else 'rust', (0, y, z), r, 10, 'X', 10)
        for x in (-4.5, -1.5, 1.5, 4.5):
            cyl('gunmetal', (x, y, z), r * 1.25, 0.2, 'X', 10)
    for x in (-4, 0, 4):
        box('gunmetal', (x - 0.07, -0.3, 0), (x + 0.07, 0.3, 2.1))
    return {}


def cable_sag():
    """Two catenary cables 12 long (dark, 0.1 thick) with a hanger."""
    for off in (0.0, 0.5):
        pts = [(-6 + 12 * i / 12.0, off, 4 - 1.2 * math.sin(math.pi * i / 12.0)) for i in range(13)]
        for a, b in zip(pts, pts[1:]):
            beam('dark', a, b, 0.1, 0.1)
    box('metal', (-6.1, -0.1, 0), (-5.9, 0.6, 4.2))
    box('metal', (5.9, -0.1, 0), (6.1, 0.6, 4.2))
    return {}


def fg_girder():
    """Foreground occluder: tall I-beam, 0.9 wide, 14 tall, slight bevel look from bolts (<= 300 tris)."""
    ibeam('dark', 0, 0, 0, 14, 1.0, 1.0, 0.2, 0.2)
    for z in (2, 6, 10):
        box('dark', (-0.75, -0.75, z), (0.75, 0.75, z + 0.25))
    return {}


def fg_pipe():
    """Foreground occluder: long overhead pipe + hangers, 14 long (<= 250 tris)."""
    cyl('dark', (0, 0, 0), 0.45, 14, 'X', 12)
    for x in (-5, 0, 5):
        cyl('dark', (x, 0, 0), 0.58, 0.3, 'X', 12)
        box('dark', (x - 0.06, -0.06, 0.4), (x + 0.06, 0.06, 6))
    return {}


def fg_fence():
    """Foreground occluder: chain-link fence panel (frame + diagonal wires) 6 x 3."""
    box('dark', (-3, -0.05, 0), (-2.9, 0.05, 3))
    box('dark', (2.9, -0.05, 0), (3, 0.05, 3))
    box('dark', (-3, -0.05, 2.9), (3, 0.05, 3))
    box('dark', (-3, -0.05, 0), (3, 0.05, 0.1))
    for i in range(-5, 6):
        x = i * 0.5
        beam('dark', (x - 1.5, 0, 0.1), (x + 1.5, 0, 2.9), 0.04, 0.04)
        beam('dark', (x + 1.5, 0, 0.1), (x - 1.5, 0, 2.9), 0.04, 0.04)
    return {}


def fg_cables():
    """Foreground occluder: three hanging cables, 10 wide, thick enough to read as soft silhouettes."""
    for off, sag in ((0, 1.6), (0.4, 2.1), (-0.3, 1.2)):
        pts = [(-5 + 10 * i / 10.0, off, 3 - sag * math.sin(math.pi * i / 10.0)) for i in range(11)]
        for a, b in zip(pts, pts[1:]):
            beam('dark', a, b, 0.14, 0.14)
    return {}


EXPORT = [girder_col, girder_x, truss_span, crane, pillar, overpass_span, jersey_barrier, guard_rail,
          facade_block, gallery_arcade, statue, banner, chimney, pipe_run, cable_sag,
          fg_girder, fg_pipe, fg_fence, fg_cables]


def args():
    a = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    return {a[i].lstrip('-'): a[i + 1] for i in range(0, len(a) - 1, 2)}


def export_one(fn, out):
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    OBS.clear()
    fn()
    bpy.ops.object.select_all(action='DESELECT')
    for ob in OBS:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = OBS[0]
    bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    # triangulate for an honest tri count
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.quads_convert_to_tris()
    bpy.ops.object.mode_set(mode='OBJECT')
    ob.name = fn.__name__
    ob.data.name = fn.__name__
    path = os.path.join(out, fn.__name__ + '.fbx')
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='Z', axis_up='Y', bake_space_transform=True,
                             use_mesh_modifiers=True, mesh_smooth_type='FACE', add_leaf_bones=False,
                             path_mode='STRIP')
    print('EXPORTED', fn.__name__, len(ob.data.polygons), 'tris', [m.name for m in ob.data.materials])


def main():
    a = args()
    out = os.path.abspath(a['out'])
    only = set(a['only'].split(',')) if a.get('only') else None
    os.makedirs(out, exist_ok=True)
    for fn in EXPORT:
        if only and fn.__name__ not in only:
            continue
        try:
            export_one(fn, out)
        except Exception as ex:  # noqa: BLE001
            import traceback
            traceback.print_exc()
            print('FAILED', fn.__name__, ex)
    print('EXPORT_DONE', out)


main()
