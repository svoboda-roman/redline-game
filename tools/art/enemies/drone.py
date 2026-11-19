"""Drone: flying security drone (body 1.0 x 1.0, pivot = body centre). Gunmetal hull slung between two
ducted fans, one big red eye in front, amber running strip.

States: hover (loop), windup (eye flares, tilts back), dive (nose down), hurt, death (sparks, drops).
Visibility groups: bladeA/bladeB (alternating rotor blades), flare, spark0/spark1.
Run:  blender -b --factory-startup -P drone.py -- --out drone.blend   (see tools/art/common/make_char.sh)
"""
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'common'))
import kit  # noqa: E402
from kit import part, cat, box, tube, ellipsoid, ecyl, shell, lathe, extrude2d, S, C, TAU, D  # noqa: E402


def build():
    kit.joint('root', None, (0, 0, 0))
    kit.joint('body', 'root', (0, 0, 0), 'XYZ', -20)
    B = 'body'
    part('hull', B, B, 'gunmetal', ellipsoid((0.0, 0, 0.0), (0.24, 0.20, 0.16)))
    part('belly', B, B, 'helmet', ellipsoid((0.0, 0, -0.08), (0.18, 0.16, 0.10)))
    part('band', B, B, 'strip', ecyl((0, 0), 0.015, 0.035, 0.235, 0.195, 24))
    # cyberpunk reskin: a living brain riding on top, in a chrome cage, wired into the hull
    part('brain', B, B, 'flesh', ellipsoid((-0.02, 0, 0.17), (0.19, 0.15, 0.12)))
    part('brain_folds', B, B, 'flesh_dk', cat(box((-0.02, -0.06, 0.2), (0.22, 0.02, 0.02)), box((-0.04, -0.09, 0.16), (0.18, 0.02, 0.02)),
                                              box((0.0, -0.03, 0.235), (0.14, 0.02, 0.015))))
    part('brain_cage', B, B, 'chrome', cat(*[box((x, 0, 0.17), (0.012, 0.26, 0.012), (0, 0, 0)) for x in (-0.12, -0.04, 0.04)],
                                           box((-0.04, 0, 0.255), (0.2, 0.012, 0.012))))
    part('brain_wires', B, B, 'cable', cat(tube((0.08, -0.05, 0.12), (0.14, -0.08, 0.05), 0.012, 0.012, 6),
                                           tube((-0.12, -0.05, 0.12), (-0.18, -0.08, 0.05), 0.012, 0.012, 6)))
    part('eye_ring', B, B, 'helmet', tube((0.17, 0, -0.01), (0.25, 0, -0.01), 0.10, 0.09, 18))
    part('eye', B, B, 'red', ellipsoid((0.25, 0, -0.01), (0.03, 0.07, 0.07)))
    part('eyeflare', B, B, 'red', ellipsoid((0.27, 0, -0.01), (0.05, 0.11, 0.11)), group='flare')
    part('glowflare', B, B, 'glow', ellipsoid((0.30, 0, -0.01), (0.02, 0.15, 0.15)), group='flare')
    for nm, x in (('F', 0.30), ('B', -0.30)):
        part('arm_' + nm, B, B, 'cyber', box((x * 0.6, 0, 0.04), (0.22, 0.05, 0.04)))
        part('duct_' + nm, B, B, 'gunmetal', lathe((x, 0), [(0.0, 0.15), (0.0, 0.17), (0.09, 0.17), (0.09, 0.15), (0.0, 0.15)], 22))
        part('duct_strip_' + nm, B, B, 'strip', ecyl((x, 0), 0.03, 0.045, 0.172, 0.172, 22))
        part('blur_' + nm, B, B, 'blur', ecyl((x, 0), 0.07, 0.075, 0.14, 0.14, 20))
        part('bladeA_' + nm, B, B, 'steel', box((x, 0, 0.08), (0.28, 0.03, 0.012), (0, 0, 20)), group='bladeA')
        part('bladeB_' + nm, B, B, 'steel', box((x, 0, 0.08), (0.28, 0.03, 0.012), (0, 0, 110)), group='bladeB')
    part('claws', B, B, 'cyber', cat(tube((0.06, 0, -0.14), (0.12, 0, -0.26), 0.018, 0.012, 8),
                                    tube((-0.06, 0, -0.14), (-0.12, 0, -0.26), 0.018, 0.012, 8)))
    sp = [[((0.1, -0.2, 0.1), 30), ((-0.15, -0.2, -0.05), -40), ((0.2, -0.2, -0.12), 70)],
          [((-0.05, -0.2, 0.15), -20), ((0.18, -0.2, 0.05), 50), ((-0.2, -0.2, -0.1), 10)]]
    for k, st in enumerate(sp):
        part('spark%d' % k, B, B, 'spark', cat(*[box(c, (0.09, 0.02, 0.02), (0, -a, 0)) for c, a in st]),
             smooth=False, group='spark%d' % k)


def blades(i):
    return ['bladeA' if i % 2 == 0 else 'bladeB']


def hover(i, n):
    # 8 frames: smoother bob and a lagging pitch sway; blades cycle every frame
    return dict(dz=0.05 * S(TAU * i / n), lean=4 * S(TAU * i / n - 1.2), show=blades(i))


def windup(i, n):
    # anticipation: rises and pitches back (nose up), then a small dip forward before the dive
    k = [0.4, 0.8, 1.0, 1.0, 0.85][i]
    tip = [0, 0, 0, 0, 10][i]
    return dict(dz=0.08 * k, dx=-0.07 * k, lean=-18 * k + tip, show=blades(i) + (['flare'] if i >= 1 else []))


def dive(i, n):
    return dict(dz=-0.03 * i, dx=0.04 * i, lean=42 + 4 * i, show=blades(i) + ['flare'])


def hurt(i, n):
    k = [1.0, 1.1, 0.5, 0.15][i]
    return dict(dx=-0.10 * k, dz=0.03 * k, lean=-26 * k, show=blades(i) + (['spark%d' % (i % 2)] if i < 2 else []))


# Death: sparks, tumbles and drops onto its belly (dz -0.32 puts the hull on the floor of its 1.0 body when
# the renderer lands the corpse), bounces once and holds.
DEATH = [dict(dx=-0.06, lean=-25, show=['bladeA', 'spark0']),
         dict(dx=-0.08, dz=-0.06, lean=-60, show=['spark1']),
         dict(dx=-0.10, dz=-0.16, lean=-120, show=['spark0']),
         dict(dx=-0.12, dz=-0.26, lean=-170, show=['spark1']),
         dict(dx=-0.14, dz=-0.32, lean=-195, show=[]),
         dict(dx=-0.15, dz=-0.27, lean=-188, show=['spark0']),
         dict(dx=-0.15, dz=-0.32, lean=-192, show=[])]

ANIMS = [
    dict(name='hover', n=8, fps=12, loop=True, pose=hover),
    dict(name='windup', n=5, fps=10, loop=False, pose=windup),
    dict(name='dive', n=3, fps=12, loop=True, pose=dive),
    dict(name='hurt', n=4, fps=16, loop=False, pose=hurt),
    dict(name='death', n=7, fps=12, loop=False, pose=lambda i, n: DEATH[i]),
]

META = dict(cell_w=2.0, cell_h=2.0, pivot='centre', pad_bottom=1.0,
            pivot_note='flier: pivot = body centre = centre of the 1.0 x 1.0 collision body (cell centre); sprites face right',
            emissive_encoding='R = attack glow: lens flare in windup/dive + sparks (hurt, death), G = red eye (widened in windup/dive), B = amber running strips (hull band, duct rims); A = 255 where any mask is set')

if __name__ == '__main__':
    a = kit.args()
    kit.init()
    build()
    kit.animate(ANIMS, META, a['out'])
