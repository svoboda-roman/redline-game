"""Brute: armoured sumo-mass juggernaut (body 1.5 x 2.1). Slab pauldrons, a riveted gunmetal
breastplate, a head sunk between the shoulders behind an amber visor.

States: idle, walk, windup (head lowers, visor + vents flare), charge, crashed (breastplate split open
on a glowing core, sparks), hurt, death.  Visibility groups: intact (closed plate), flare, crack,
spark0..2.
Run:  blender -b --factory-startup -P brute.py -- --out brute.blend   (see tools/art/common/make_char.sh)
"""
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'common'))
import kit  # noqa: E402
from kit import part, cat, box, tube, ellipsoid, ecyl, shell, lathe, extrude2d, S, C, TAU  # noqa: E402


def build():
    rp = kit.humanoid(thigh=0.45, shin=0.41, ankle=0.11, hip_w=0.18, spine=0.20, chest=0.30, neck=0.17, head=0.04,
                      sh_w=0.36, sh_z=0.06, upper=0.32, fore=0.30)
    kit.set_rig(rp)
    kit.legs(thigh_mat='olive', shin_mat='gunmetal', r=(0.15, 0.12, 0.11, 0.095), boot=(0.30, 0.17, 0.11),
             knee_mat='gunmetal', shin_guard='gunmetal')
    kit.arms({'R': ('olive', 'gunmetal'), 'L': ('olive', 'gunmetal')}, r=(0.10, 0.09, 0.11, 0.10),
             hand=(0.15, 0.13, 0.13), hand_mat='gunmetal')
    # gauntlet strips (tech light) on both forearms
    for s in 'RL':
        part('gstrip_' + s, 'el_' + s, 'el_' + s, 'strip', box((0.105, -0.03, -0.15), (0.012, 0.03, 0.12)))
    # mass: belly + chest
    part('hips', 'pelvis_geo', 'pelvis', 'olive', ellipsoid((0.02, 0, 0.0), (0.32, 0.30, 0.19)))
    part('mawashi', 'pelvis_geo', 'pelvis', 'helmet', ecyl((0.02, 0), -0.08, 0.07, 0.33, 0.32, 22))
    part('tassets', 'pelvis_geo', 'pelvis', 'gunmetal', shell((0, 0), -110, 110, [(-0.02, 0.34, 0.33), (-0.34, 0.39, 0.38)], 12))
    part('belly', 'spine_geo', 'spine', 'olive', ellipsoid((0.10, 0, 0.10), (0.42, 0.36, 0.30)))
    part('chest_m', 'chest_geo', 'chest', 'gunmetal', ellipsoid((0.04, 0, 0.0), (0.38, 0.38, 0.27)))
    # breastplate: intact vs cracked open
    plate = lambda x0: [(x0, -0.24), (x0 + 0.02, 0.18), (x0 - 0.02, 0.24)]
    part('plate', 'chest_geo', 'chest', 'gunmetal', box((0.36, 0, -0.06), (0.12, 0.44, 0.40), (0, -8, 0)), bevel=0.03, group='intact')
    part('plate_rivets', 'chest_geo', 'chest', 'helmet', cat(box((0.43, 0, 0.06), (0.02, 0.42, 0.03), (0, -8, 0)),
                                                         box((0.43, 0, -0.14), (0.02, 0.42, 0.03), (0, -8, 0))), group='intact')
    part('core_strip', 'chest_geo', 'chest', 'strip', box((0.43, -0.12, -0.04), (0.02, 0.06, 0.12), (0, -8, 0)), group='intact')
    part('plate_top', 'chest_geo', 'chest', 'gunmetal', box((0.40, 0, 0.07), (0.11, 0.44, 0.20), (0, -26, 0)), bevel=0.025, group='crack')
    part('plate_bot', 'chest_geo', 'chest', 'gunmetal', box((0.41, 0, -0.22), (0.11, 0.44, 0.16), (0, 22, 0)), bevel=0.025, group='crack')
    part('core', 'chest_geo', 'chest', 'glow', ellipsoid((0.40, 0, -0.07), (0.07, 0.17, 0.09)), group='crack')
    # pauldrons: slab shoulders that tower over the head
    for s, sy in (('R', -1), ('L', 1)):
        part('pauldron_' + s, 'sh_' + s, 'sh_' + s, 'gunmetal', cat(
            ellipsoid((0.0, sy * 0.06, 0.0), (0.24, 0.17, 0.16)),
            shell((0.0, sy * 0.06), 0, 360, [(-0.02, 0.25, 0.19), (-0.17, 0.28, 0.22)], 20)), bevel=0.0)
        part('vent_' + s, 'sh_' + s, 'sh_' + s, 'strip', box((0.17, sy * 0.06, 0.09), (0.05, 0.08, 0.03), (0, -30, 0)))
        part('ventflare_' + s, 'sh_' + s, 'sh_' + s, 'glow', box((0.19, sy * 0.06, 0.11), (0.09, 0.12, 0.08), (0, -30, 0)), group='flare')
    # head: sunk, armoured, amber visor
    H = 'head_geo'
    part('head_m', H, 'head', 'helmet', ellipsoid((0.05, 0, 0.08), (0.15, 0.14, 0.14)))
    part('brow', H, 'head', 'gunmetal', box((0.14, 0, 0.15), (0.14, 0.27, 0.06), (0, -15, 0)), bevel=0.015)
    part('jaw', H, 'head', 'gunmetal', box((0.15, 0, 0.0), (0.12, 0.22, 0.10), (0, 10, 0)), bevel=0.015)
    part('visor', H, 'head', 'eye', box((0.205, 0, 0.085), (0.03, 0.20, 0.035)))
    part('visorflare', H, 'head', 'eye', box((0.215, 0, 0.085), (0.05, 0.24, 0.07)), group='flare')
    # sparks (crashed): three alternating sets of short bright streaks
    sp = [[((0.50, -0.1, 0.02), 30), ((0.52, -0.05, -0.15), -20), ((0.25, -0.3, 0.2), 60)],
          [((0.55, -0.1, -0.05), -40), ((0.38, -0.2, 0.18), 15), ((0.56, -0.05, 0.09), 70)],
          [((0.52, -0.15, 0.07), 10), ((0.53, -0.1, -0.23), -55), ((0.30, -0.3, -0.05), 35)]]
    for k, st in enumerate(sp):
        part('spark%d' % k, 'chest_geo', 'chest', 'spark',
             cat(*[box(c, (0.09, 0.02, 0.018), (0, -a, 0)) for c, a in st]), smooth=False, group='spark%d' % k)
    return rp


BASE = dict(dz=-0.02, lean=6, spine=2, chest=2, head=0, hipR=16, knR=18, hipL=-14, knL=16, anR=0, anL=0,
            shR=8, elR=22, shL=2, elL=22, show=['intact'])


def P(**kw):
    d = dict(BASE)
    d.update(kw)
    return d


def st(**kw):
    return kit.stand(BASE, **kw)


def idle(i, n):
    # heavy 8-frame breath: chest swells, shoulders rise, a slow sway of the weight between the feet
    b = S(TAU * i / n)
    w = S(TAU * i / n - 0.9)
    return st(dx=0.02 * w, dz=-0.03 + 0.018 * b, lean=6 + 1.5 * w, chest=2 + 4 * b, head=-2 * b,
              shR=8 + 5 * b, elR=22 + 4 * b, shL=2 + 5 * b + 2 * w, elL=22 + 4 * b)


# Stomp: 10 frames at a fps solved from bruteSpeed 1.6 (planted feet, no sliding): contact 0/5, down 1/6
# (heavy dip), passing 2-3/7-8, up 4/9.
WALK_N, SPEED, HALF_STRIDE, DUTY = 10, 1.6, 0.38, 0.6
WALK_FPS, WALK_D = kit.gait_fps(WALK_N, SPEED, HALF_STRIDE, DUTY)


def walk(i, n):
    g = kit.gait(i, n, kit.HUMANP, SPEED, WALK_FPS, duty=DUTY, lift=0.14, bob=0.05, lean=9)
    u = g.pop('u')
    sw = C(TAU * u)
    bob2 = C(2 * TAU * (u - 0.08))
    p = P(lean=9, chest=3 - 3 * bob2, head=2 + 3 * bob2, pelvis=-4 * sw, spine=2,
          shL=4 + 26 * sw, elL=26 + 10 * bob2, shR=6 - 26 * sw, elR=26 + 10 * bob2)
    p.update(g)
    return p


# Windup: anticipation (rears up and back, fists drawn) -> drops into a low coiled crouch, visor flares.
def WINDUP_POSES():
    return [
        st(dx=-0.06, dz=0.0, lean=-6, chest=-6, head=-8, shR=-20, elR=70, shL=-14, elL=70),
        st(dx=-0.10, dz=-0.06, lean=4, chest=0, head=6, shR=-34, elR=90, shL=-24, elL=88, show=['intact', 'flare']),
        st(dx=-0.08, dz=-0.14, lean=24, chest=8, head=34, shR=-30, elR=82, shL=40, elL=84, fL=-0.30, show=['intact', 'flare']),
        st(dx=-0.06, dz=-0.17, lean=30, chest=9, head=40, shR=-32, elR=84, shL=44, elL=90, fL=-0.32, show=['intact', 'flare']),
        st(dx=-0.07, dz=-0.17, lean=30, chest=9, head=40, shR=-33, elR=84, shL=45, elL=90, fL=-0.32, show=['intact', 'flare']),
    ]


# Charge: 8-frame sprint at bruteChargeSpeed 11 (fps solved from it, long flight frames).
CH_N, CH_SPEED, CH_HALF, CH_DUTY = 8, 11.0, 0.5, 0.3
CH_FPS, CH_D = kit.gait_fps(CH_N, CH_SPEED, CH_HALF, CH_DUTY)


def charge(i, n):
    g = kit.gait(i, n, kit.HUMANP, CH_SPEED, CH_FPS, duty=CH_DUTY, lift=0.22, bob=0.05, lean=32)
    u = g.pop('u')
    sw = C(TAU * u)
    p = P(lean=32, chest=8, head=36, shR=-30 - 10 * sw, elR=85, shL=45 + 10 * sw, elL=95, pelvis=-5 * sw,
          show=['intact', 'flare'])
    p.update(g)
    return p


def crashed(i, n):
    b = S(TAU * i / n)
    return P(dx=-0.10, dz=-0.12 + 0.01 * b, lean=-6 + 2 * b, chest=-8, head=-18 + 6 * b, hipR=30, knR=45, hipL=-6, knL=40,
             anL=10, shR=40 + 8 * b, elR=20, shL=55 - 8 * b, elL=25, show=['crack', 'spark%d' % (i % 3)])


def HURT_POSES():
    return [st(dx=-0.08, dz=-0.05, lean=-10, chest=-10, head=-22, shR=40, elR=60, shL=36, elL=56),
            st(dx=-0.09, dz=-0.06, lean=-11, chest=-10, head=-18, shR=38, elR=58, shL=34, elL=54),
            st(dx=-0.05, dz=-0.04, lean=-3, chest=-4, head=-8, shR=24, elR=40, shL=18, elL=38),
            st(dx=-0.01, dz=-0.02, lean=5, chest=1, head=-1, shR=11, elR=25, shL=5, elL=25)]


def DEATH_POSES():
    return [st(dx=-0.08, dz=-0.05, lean=-10, chest=-10, head=-22, shR=40, elR=50, shL=35, elL=50, show=['crack', 'spark0']),
            P(dx=-0.02, dz=-0.10, lean=0, chest=4, head=10, hipR=30, knR=50, hipL=-4, knL=55, shR=30, elR=30, shL=20, elL=30,
              show=['crack', 'spark1']),
            P(dz=-0.38, lean=8, chest=8, head=24, hipR=10, knR=95, anR=70, hipL=0, knL=95, anL=70, shR=20, elR=15, shL=10, elL=15,
              show=['crack', 'spark2']),
            P(dx=0.08, dz=-0.50, lean=40, chest=10, head=50, hipR=30, knR=100, anR=10, hipL=20, knL=100, anL=10,
              shR=60, elR=20, shL=60, elL=20, show=['crack']),
            P(dx=0.12, dz=-0.67, lean=86, spine=4, chest=4, head=88, hipR=-80, knR=8, anR=10, hipL=-78, knL=10, anL=10,
              shR=104, elR=30, shL=99, elL=25, show=['crack', 'spark0']),
            P(dx=0.12, dz=-0.63, lean=82, spine=4, chest=4, head=84, hipR=-76, knR=10, anR=10, hipL=-74, knL=12, anL=10,
              shR=96, elR=30, shL=92, elL=25, show=['crack']),
            P(dx=0.12, dz=-0.67, lean=84, spine=3, chest=3, head=88, hipR=-82, knR=6, anR=10, hipL=-80, knL=8, anL=10,
              shR=100, elR=30, shL=96, elL=25, show=['crack'])]


ANIMS = [
    dict(name='idle', n=8, fps=7, loop=True, pose=idle),
    dict(name='walk', n=WALK_N, fps=WALK_FPS, loop=True, pose=walk),
    dict(name='windup', n=5, fps=10, loop=False, pose=lambda i, n: WINDUP_POSES()[i]),
    dict(name='charge', n=CH_N, fps=CH_FPS, loop=True, pose=charge),
    dict(name='crashed', n=6, fps=10, loop=True, pose=crashed),
    dict(name='hurt', n=4, fps=16, loop=False, pose=lambda i, n: HURT_POSES()[i]),
    dict(name='death', n=7, fps=10, loop=False, pose=lambda i, n: DEATH_POSES()[i]),
]

META = dict(cell_w=4.4, cell_h=3.2, pad_bottom=0.25,
            pivot_note='feet centre = bottom-centre of the 1.5 x 2.1 collision body; sprites face right',
            emissive_encoding='R = attack glow: shoulder-vent flare (windup/charge) + exposed core (crashed, death) + sparks (crashed), G = visor (wider flare in windup/charge), B = amber light strips (gauntlets, shoulder vents, breastplate); A = 255 where any mask is set')

if __name__ == '__main__':
    a = kit.args()
    kit.init()
    build()
    kit.animate(ANIMS, META, a['out'])
