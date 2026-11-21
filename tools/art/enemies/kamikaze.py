"""Kamikaze: small hunched runner (body 0.9 x 1.0) strapped with glowing red charges. Gas-mask hood
with amber eyes, a rack of red canisters on the back and a belt of charges across the chest.

States: idle (twitchy), rush (fast run), lunge (leap), fuse (trembling, red glow pulsing), death.
Visibility groups: fuse (enlarged charge glow), spark0/spark1.
Run:  blender -b --factory-startup -P kamikaze.py -- --out kamikaze.blend   (see tools/art/common/make_char.sh)
"""
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'common'))
import kit  # noqa: E402
from kit import part, cat, box, tube, ellipsoid, ecyl, shell, lathe, extrude2d, S, C, TAU  # noqa: E402


def build():
    rp = kit.humanoid(thigh=0.24, shin=0.24, ankle=0.06, hip_w=0.07, spine=0.09, chest=0.13, neck=0.12, head=0.05,
                      sh_w=0.14, sh_z=0.10, upper=0.19, fore=0.18)
    kit.set_rig(rp)
    kit.legs(thigh_mat='cloth', shin_mat='cloth', r=(0.06, 0.048, 0.045, 0.035), boot=(0.15, 0.07, 0.06), shin_guard='olive')
    kit.arms({'R': ('cloth', 'cloth'), 'L': ('cloth', 'cloth')}, r=(0.04, 0.034, 0.036, 0.03), hand=(0.055, 0.05, 0.06))
    part('hips', 'pelvis_geo', 'pelvis', 'cloth', ellipsoid((0, 0, -0.01), (0.10, 0.11, 0.08)))
    part('belly', 'spine_geo', 'spine', 'olive', ellipsoid((0.0, 0, 0.05), (0.10, 0.11, 0.09)))
    part('torso', 'chest_geo', 'chest', 'olive', ellipsoid((0.0, 0, 0.03), (0.12, 0.14, 0.12)))
    part('harness', 'chest_geo', 'chest', 'helmet', cat(ecyl((0, 0), -0.05, -0.02, 0.125, 0.145, 18),
                                                      box((0.05, -0.06, 0.03), (0.2, 0.025, 0.03), (0, 50, 0))))
    # charges: back rack (3 canisters) + chest belt (2) — the red read of this enemy
    rack = [tube((-0.16, y, -0.08), (-0.16, y, 0.16), 0.042, 0.042, 12) for y in (-0.07, 0.0, 0.07)]
    part('rack_frame', 'chest_geo', 'chest', 'gunmetal', box((-0.12, 0, 0.04), (0.03, 0.22, 0.26)))
    part('charges_back', 'chest_geo', 'chest', 'charge', cat(*rack))
    part('rack_caps', 'chest_geo', 'chest', 'gunmetal', cat(*[ecyl((-0.16, y), 0.15, 0.19, 0.046, 0.046, 12) for y in (-0.07, 0.0, 0.07)]))
    part('charges_front', 'chest_geo', 'chest', 'charge', cat(box((0.12, -0.05, -0.02), (0.04, 0.05, 0.08)),
                                                            box((0.11, 0.05, -0.02), (0.04, 0.05, 0.08))))
    part('fuse_glow', 'chest_geo', 'chest', 'charge', cat(ellipsoid((-0.17, 0, 0.04), (0.08, 0.16, 0.17)),
                                                        ellipsoid((0.13, 0, -0.02), (0.05, 0.10, 0.08))), group='fuse')
    part('wire', 'chest_geo', 'chest', 'strip', tube((-0.14, -0.08, 0.17), (0.02, -0.1, 0.22), 0.008, 0.008, 6))
    H = 'head_geo'
    part('neck_m', H, 'head', 'cloth', tube((0, 0, -0.08), (0, 0, 0.0), 0.035, 0.035))
    part('hood', H, 'head', 'cloth', ellipsoid((-0.01, 0, 0.04), (0.085, 0.08, 0.085)))
    part('mask', H, 'head', 'gunmetal', ellipsoid((0.04, 0, 0.02), (0.06, 0.065, 0.06)))
    part('filter', H, 'head', 'helmet', tube((0.08, -0.01, -0.02), (0.13, -0.01, -0.04), 0.028, 0.024, 10))
    part('eyes', H, 'head', 'eye', cat(box((0.09, -0.03, 0.04), (0.02, 0.025, 0.022)), box((0.09, 0.03, 0.04), (0.02, 0.025, 0.022))))
    sp = [[((-0.2, -0.15, 0.22), 60), ((0.15, -0.1, 0.1), 20)], [((-0.1, -0.15, 0.25), 110), ((0.18, -0.1, -0.05), -30)]]
    for k, st in enumerate(sp):
        part('spark%d' % k, 'chest_geo', 'chest', 'spark', cat(*[box(c, (0.07, 0.02, 0.016), (0, -a, 0)) for c, a in st]),
             smooth=False, group='spark%d' % k)
    return rp


BASE = dict(dz=-0.03, lean=22, spine=6, chest=10, neck=-12, head=-8, hipR=26, knR=40, anR=0, hipL=-6, knL=36, anL=0,
            shR=20, elR=60, shL=10, elL=70)


def P(**kw):
    d = dict(BASE)
    d.update(kw)
    return d


def st(**kw):
    return kit.stand(BASE, **kw)


def idle(i, n):
    # twitchy 6-frame breath: fast shallow panting with a head tic
    b = S(TAU * i / n)
    tw = [0, 1, 0, -1, 0, 1][i % 6] * (1 if i < 4 else 0.5)
    return st(dz=-0.03 + 0.012 * b, chest=10 + 3 * b, head=-8 + 6 * tw, shR=20 + 4 * b + 3 * tw, shL=10 + 4 * b - 3 * tw)


# Rush: 8-frame bounding sprint for kamikazeRushSpeed 12. Planting the feet at 12 u/s with 0.5-unit legs would
# need ~46 fps, so the cycle runs at 24 fps with the longest stride the legs reach: the stance foot sweeps back
# at ~half the ground speed (motion reads as a blur at that speed; the renderer scales the rate by |Vx|/12).
RUSH_N, RUSH_FPS, RUSH_HALF, RUSH_DUTY = 8, 24, 0.27, 0.3
RUSH_VIRTUAL_SPEED = 2 * RUSH_HALF / RUSH_DUTY * RUSH_FPS / RUSH_N


def rush(i, n):
    g = kit.gait(i, n, kit.HUMANP, RUSH_VIRTUAL_SPEED, RUSH_FPS, duty=RUSH_DUTY, lift=0.18, bob=0.04, lean=38)
    u = g.pop('u')
    sw = C(TAU * u)
    p = P(lean=38, chest=10, neck=-20, head=-12, shL=-45 * sw, elL=80, shR=45 * sw, elR=80, pelvis=-5 * sw)
    p.update(g)
    return p


def LUNGE_POSES():
    return [st(dz=-0.13, lean=44, chest=12, head=-16, shR=-50, elR=50, shL=-60, elL=50),
            P(dz=-0.10, lean=40, hipR=60, knR=95, hipL=10, knL=90, shR=-40, elR=40, shL=-50, elL=40),
            P(dz=0.08, lean=55, chest=6, neck=-25, head=-20, hipR=10, knR=20, anR=40, hipL=-50, knL=30, anL=60,
              shR=110, elR=10, shL=100, elL=15, show=['fuse']),
            P(dz=0.04, lean=48, chest=6, neck=-22, head=-18, hipR=40, knR=70, hipL=-20, knL=60, shR=90, elR=20, shL=85, elL=20,
              show=['fuse'])]


def fuse(i, n):
    jit = [(0.02, 0.0), (-0.02, 0.01), (0.015, -0.005)][i]
    return P(dx=jit[0], dz=-0.06 + jit[1], lean=10, chest=4 + 6 * (i % 2), head=-4, hipR=20, knR=50, hipL=-10, knL=48,
             shR=60 + 10 * (i % 2), elR=90, shL=50 - 10 * (i % 2), elL=95, show=(['fuse'] if i != 1 else []) + ['spark%d' % (i % 2)])


def DEATH_POSES():
    return [st(dx=-0.08, dz=-0.05, lean=2, chest=-8, head=-26, shR=46, elR=50, shL=56, elL=40),
            P(dx=-0.06, dz=-0.08, lean=10, head=10, hipR=40, knR=70, hipL=10, knL=70, shR=30, elR=30, shL=20, elL=30),
            P(dx=-0.02, dz=-0.24, lean=40, head=40, hipR=50, knR=110, hipL=30, knL=110, shR=50, elR=40, shL=50, elL=30),
            P(dx=0.04, dz=-0.37, lean=82, spine=3, chest=3, neck=0, head=84, hipR=-77, knR=12, anR=10, hipL=-75, knL=14, anL=10,
              shR=96, elR=30, shL=92, elL=30),
            P(dx=0.04, dz=-0.40, lean=86, spine=3, chest=3, neck=0, head=88, hipR=-80, knR=10, anR=10, hipL=-78, knL=12, anL=10,
              shR=100, elR=30, shL=95, elL=30)]


ANIMS = [
    dict(name='idle', n=6, fps=10, loop=True, pose=idle),
    dict(name='rush', n=RUSH_N, fps=RUSH_FPS, loop=True, pose=rush),
    dict(name='lunge', n=4, fps=12, loop=False, pose=lambda i, n: LUNGE_POSES()[i]),
    dict(name='fuse', n=3, fps=12, loop=True, pose=fuse),
    dict(name='death', n=5, fps=12, loop=False, pose=lambda i, n: DEATH_POSES()[i]),
]

META = dict(cell_w=2.6, cell_h=1.8, pad_bottom=0.25,
            pivot_note='feet centre = bottom-centre of the 0.9 x 1.0 collision body; sprites face right',
            emissive_encoding='R = attack glow: red charges (always) + swollen charge glow (lunge, fuse frames 0 and 2) + sparks (fuse), G = gas-mask eyes, B = amber fuse wire; A = 255 where any mask is set')

if __name__ == '__main__':
    a = kit.args()
    kit.init()
    build()
    kit.animate(ANIMS, META, a['out'])
