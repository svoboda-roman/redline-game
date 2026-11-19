"""Export Redline rooftop props as FBX meshes for the 2.5D (3D-pixelated) render prototype.

Run:  blender -b --factory-startup -P export_props_fbx.py -- --out /abs/Assets/Render3D/Models [--only a,b]

Reuses the prop geometry from tools/art/props/build_props.py without modifying it: the source is executed
with its trailing main() call removed, so every prop function (ac_unit, water_tank, ...) builds the same
objects it builds for the sprite pipeline. Materials keep their style names (metal, gunmetal, rust, red, ...);
Unity remaps them by name to URP Lit materials (see Assets/Scripts/Game/Render3D/Proto3DMaterials.cs).

Conventions: Blender Z up, -Y is the prop's front (toward the sprite camera). Exported with Y up and the
prop front facing Unity -Z (toward a camera at negative z). 1 Blender unit = 1 tile = 1 Unity unit.
"""
import bpy, os, sys, math

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, '..', 'props', 'build_props.py')

ns = {'__file__': os.path.abspath(SRC), '__name__': 'build_props_src'}
code = open(SRC).read()
# Drop the module-level main() call at the end of the file.
lines = code.rstrip().split('\n')
if lines[-1].strip() == 'main()':
    lines = lines[:-1]
exec(compile('\n'.join(lines), SRC, 'exec'), ns)

EXPORT = ['ac_unit', 'water_tank', 'antenna_mast', 'fence', 'container', 'stair_hut', 'cable_posts',
          'railing', 'vent_stack', 'fan_tower', 'exhaust_elbow', 'debris', 'barrels', 'crates', 'sat_dish',
          'billboard', 'neon_sign']


def args():
    a = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    return {a[i].lstrip('-'): a[i + 1] for i in range(0, len(a) - 1, 2)}


def export_one(fn, out):
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    ns['OBS'].clear()
    meta = fn()
    obs = list(ns['OBS'])
    bpy.ops.object.select_all(action='DESELECT')
    for ob in obs:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = obs[0]
    # Apply modifiers (bevels) and join into one mesh with several material slots.
    for ob in obs:
        bpy.context.view_layer.objects.active = ob
        for md in list(ob.modifiers):
            try:
                bpy.ops.object.modifier_apply(modifier=md.name)
            except Exception as ex:  # noqa: BLE001
                print('MODIFIER_SKIP', fn.__name__, ob.name, ex)
    bpy.ops.object.select_all(action='DESELECT')
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    for ob in meshes:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = fn.__name__
    ob.data.name = fn.__name__
    path = os.path.join(out, fn.__name__ + '.fbx')
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'},
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='Z', axis_up='Y', bake_space_transform=True,
                             use_mesh_modifiers=True, mesh_smooth_type='FACE', add_leaf_bones=False,
                             path_mode='STRIP')
    print('EXPORTED', fn.__name__, len(ob.data.polygons), [m.name for m in ob.data.materials], meta.get('anchor'))


def main():
    a = args()
    out = os.path.abspath(a['out'])
    only = set(a['only'].split(',')) if a.get('only') else None
    os.makedirs(out, exist_ok=True)
    ns['init']()
    for name in EXPORT:
        if only and name not in only:
            continue
        fn = ns.get(name)
        if fn is None:
            print('MISSING', name)
            continue
        try:
            export_one(fn, out)
        except Exception as ex:  # noqa: BLE001
            import traceback
            traceback.print_exc()
            print('FAILED', name, ex)
    print('EXPORT_DONE', out)


main()
