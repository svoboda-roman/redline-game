"""Render the id/AO pass and the camera-space normal pass for every keyed sprite frame.

Run:  blender -b samurai.blend -P render.py -- --ppu 40 --scale 8 --out /path/render [--only idle,run]
Cycles CPU, 1 sample per pixel, no pixel filter blending, Raw view transform: every rendered pixel
holds exact material ids / normals, which pixelate.py then reduces S x S blocks -> 1 sprite pixel.
"""
import bpy, json, math, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
STYLE = json.load(open(os.path.join(HERE, 'samurai_style.json')))


def args():
    a = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    return {a[i].lstrip('-'): a[i + 1] for i in range(0, len(a) - 1, 2)}


def main():
    a = args()
    ppu = float(a.get('ppu', 40))
    S = int(a.get('scale', 8))
    out = os.path.abspath(a['out'])
    only = set(a['only'].split(',')) if a.get('only') else None
    os.makedirs(out, exist_ok=True)
    rs = STYLE['render']
    w_px = 2 * int(round(rs['cell_w'] * ppu / 2))
    h_px = int(round(rs['cell_h'] * ppu))
    pad_px = int(round(rs['cell_pad_bottom'] * ppu))
    w_world, h_world, bottom = w_px / ppu, h_px / ppu, -pad_px / ppu

    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = 1
    sc.cycles.use_adaptive_sampling = False
    sc.cycles.use_denoising = False
    sc.cycles.seed = 0
    sc.cycles.use_animated_seed = False
    sc.cycles.max_bounces = 0
    sc.cycles.filter_width = 0.01
    sc.view_settings.view_transform = 'Raw'
    sc.view_settings.look = 'None'
    sc.view_settings.exposure = 0
    sc.view_settings.gamma = 1
    try:
        sc.render.dither_intensity = 0
    except Exception:
        pass
    sc.render.film_transparent = True
    sc.render.resolution_x, sc.render.resolution_y = w_px * S, h_px * S
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGBA'
    sc.render.image_settings.color_depth = '8'

    cam = bpy.data.cameras.new('sprite_cam')
    cam.type = 'ORTHO'
    cam.sensor_fit = 'HORIZONTAL'
    cam.ortho_scale = w_world
    co = bpy.data.objects.new('sprite_cam', cam)
    sc.collection.objects.link(co)
    co.location = (0, -10, bottom + h_world / 2)
    co.rotation_euler = (math.pi / 2, 0, 0)
    sc.camera = co

    vl = bpy.context.view_layer
    nmat = bpy.data.materials['NORMAL_PASS']
    anims = json.loads(sc['redline_anims'])
    for an in anims:
        if only and an['name'] not in only:
            continue
        for i in range(an['count']):
            sc.frame_set(an['start'] + i)
            base = os.path.join(out, '%s_%02d' % (an['name'], i))
            vl.material_override = None
            sc.render.filepath = base + '_id.png'
            bpy.ops.render.render(write_still=True)
            vl.material_override = nmat
            sc.render.filepath = base + '_n.png'
            bpy.ops.render.render(write_still=True)
            vl.material_override = None
    json.dump(dict(ppu=ppu, scale=S, cell_w=w_px, cell_h=h_px, pad_bottom=pad_px,
                   anims=[an for an in anims if not only or an['name'] in only]),
              open(os.path.join(out, 'manifest.json'), 'w'), indent=1)
    print('RENDERED', out)


main()
