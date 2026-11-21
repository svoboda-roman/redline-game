"""Grunt: ashigaru-style cyber foot soldier (body 1.0 x 1.3). Jingasa conical helmet with an amber eye
slit, olive-drab tunic under a gunmetal do, short yari held forward.

Run:  blender -b --factory-startup -P grunt.py -- --out grunt.blend   (see tools/art/common/make_char.sh)
"""
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'common'))
import kit  # noqa: E402
from kit import part, cat, box, tube, ellipsoid, ecyl, shell, lathe, extrude2d, S, C, TAU  # noqa: E402


def build():
    rp = kit.humanoid(thigh=0.28, shin=0.27, ankle=0.07, hip_w=0.085, spine=0.10, chest=0.15, neck=0.15, head=0.06,
                      sh_w=0.17, sh_z=0.12, upper=0.22, fore=0.20)
    kit.set_rig(rp)
    kit.weapon('spear', 'R', 'wp')
    kit.legs(thigh_mat='olive', shin_mat='cloth', r=(0.075, 0.058, 0.052, 0.04), boot=(0.17, 0.085, 0.07),
             shin_guard='gunmetal')
    kit.arms({'R': ('olive', 'cloth'), 'L': ('olive', 'cloth')}, r=(0.048, 0.04, 0.043, 0.036), hand=(0.065, 0.055, 0.07))
    # torso
    part('hips', 'pelvis_geo', 'pelvis', 'olive', ellipsoid((0, 0, -0.02), (0.12, 0.14, 0.09)))
    part('kusazuri', 'pelvis_geo', 'pelvis', 'gunmetal', cat(
        shell((0, 0), -100, 100, [(0.02, 0.135, 0.15), (-0.17, 0.165, 0.18)], 12)))
    part('belly', 'spine_geo', 'spine', 'olive', ellipsoid((0.0, 0, 0.06), (0.11, 0.13, 0.11)))
    part('do', 'chest_geo', 'chest', 'gunmetal', ellipsoid((0.025, 0, 0.04), (0.155, 0.18, 0.16)), bevel=0.0)
    part('do_band', 'chest_geo', 'chest', 'helmet', ecyl((0.02, 0), -0.06, -0.035, 0.15, 0.172, 20))
    part('strip_do', 'chest_geo', 'chest', 'strip', box((0.165, -0.04, 0.03), (0.012, 0.03, 0.06)))
    for s, sy in (('R', -1), ('L', 1)):
        part('sode_' + s, 'sh_' + s, 'sh_' + s, 'gunmetal', cat(
            box((-0.01, sy * 0.06, 0.0), (0.16, 0.03, 0.09), (sy * 10, 0, 0)),
            box((-0.006, sy * 0.07, -0.06), (0.15, 0.025, 0.06), (sy * 16, 0, 0))), bevel=0.006)
    # head: hooded face plate + jingasa
    H = 'head_geo'
    part('neck_m', H, 'head', 'cloth', tube((0, 0, -0.10), (0, 0, 0.0), 0.045, 0.045))
    part('face', H, 'head', 'helmet', ellipsoid((0.01, 0, 0.04), (0.085, 0.08, 0.09)))
    part('eye', H, 'head', 'eye', shell((0.01, 0), -60, 60, [(0.045, 0.087, 0.082), (0.062, 0.087, 0.082)], 10))
    part('jingasa', H, 'head', 'gunmetal', lathe((0.0, 0), [(0.075, 0.27), (0.085, 0.27), (0.20, 0.03), (0.215, 0.0)], 24))
    part('jingasa_rim', H, 'head', 'helmet', ecyl((0.0, 0), 0.068, 0.082, 0.275, 0.275, 24))
    part('jingasa_strip', H, 'head', 'strip', lathe((0.0, 0), [(0.145, 0.105), (0.155, 0.09)], 24))
    # yari along +X of the spear joint
    part('shaft', 'spear', 'spear', 'helmet', tube((-0.26, 0, 0), (0.47, 0, 0), 0.016, 0.016, 8))
    part('collar', 'spear', 'spear', 'gunmetal', tube((0.44, 0, 0), (0.50, 0, 0), 0.026, 0.022, 10))
    part('collar_strip', 'spear', 'spear', 'strip', tube((0.465, 0, 0), (0.48, 0, 0), 0.028, 0.028, 10))
    part('spearhead', 'spear', 'spear', 'steel', extrude2d([(0.50, -0.03), (0.58, -0.026), (0.68, 0.0), (0.58, 0.026), (0.50, 0.03)], 0, 0.014), smooth=False)
    # thrust glint: R-channel edge on the blade, shown on the lunge frames of 'attack' only
    part('glint', 'spear', 'spear', 'glow', extrude2d([(0.52, -0.012), (0.60, -0.010), (0.70, 0.0), (0.60, 0.010), (0.52, 0.012)], -0.012, 0.006), smooth=False, group='glint')
    return rp


BASE = dict(dz=-0.02, lean=5, spine=2, chest=3, head=4, hipR=14, knR=18, hipL=-12, knL=14,
            shR=42, elR=48, wp=28, shL=-6, elL=26)
FEET = (0.13, -0.12)  # planted ankle x (R front, L back) for the standing poses


def P(**kw):
    d = dict(BASE)
    d.update(kw)
    return d


def stand(dx=0.0, dz=-0.02, lean=5.0, fR=FEET[0], fL=FEET[1], zR=0.0, zL=0.0, **kw):
    """Pose with the feet planted (IK legs): the body moves, the feet stay put."""
    p = P(dx=dx, dz=dz, lean=lean, **kw)
    p.update(kit.plant(kit.HUMANP, dx, dz, fR, fL, lean, zR, zL))
    return p


def idle(i, n):
    # 8-frame breath (in over 0..3, out over 4..7) with a slow weight shift onto the front foot and back
    b = S(TAU * i / n)
    w = S(TAU * i / n - 0.9)
    return stand(dx=0.018 * w, dz=-0.025 + 0.012 * b, lean=5 + 1.5 * w, chest=3 + 3 * b, spine=2 + 1 * b,
                 head=4 - 2 * b, shR=42 + 3 * b, elR=48 - 2 * b, wp=28 + 4 * b - 2 * w, shL=-6 + 4 * b + 3 * w, elL=26 + 4 * b)


# Trot: 10 frames, contact (0/5), down (1/6), passing (2-3/7-8), up/flight (4/9). The fps is solved from
# gruntSpeed 3.5 so the stance foot sweeps back at exactly the run speed (no foot sliding).
WALK_N, SPEED, HALF_STRIDE, DUTY = 10, 3.5, 0.27, 0.38
WALK_FPS, WALK_D = kit.gait_fps(WALK_N, SPEED, HALF_STRIDE, DUTY)


def walk(i, n):
    g = kit.gait(i, n, kit.HUMANP, SPEED, WALK_FPS, duty=DUTY, lift=0.16, bob=0.035, lean=12)
    u = g.pop('u')
    sw = C(TAU * u)                      # +1 at right contact: left arm forward
    bob2 = C(2 * TAU * (u - 0.08))       # +1 at each down pose
    p = P(lean=12, chest=4 - 2 * bob2, spine=2, head=8 + 2 * bob2, pelvis=-3 * sw,
          shL=-2 + 38 * sw, elL=40 - 14 * sw,
          shR=38 - 7 * sw + 3 * bob2, elR=52 + 4 * bob2, wp=22 - 9 * bob2 - 4 * sw)  # spear dips after each footfall
    p.update(g)
    return p


# Thrust (presentation only: grunts deal contact damage). Anticipation (pull back, coil) -> lunge with the front
# foot stepping in -> full extension with the tip glinting -> overshoot -> recover.

def ATTACK_POSES():
    return [
    stand(dx=-0.04, dz=-0.05, lean=0, chest=-2, head=2, shR=14, elR=84, wp=16, shL=20, elL=60),
    stand(dx=-0.08, dz=-0.07, lean=-6, chest=-5, head=0, shR=-6, elR=100, wp=10, shL=34, elL=56),
    stand(dx=0.08, dz=-0.08, lean=16, chest=6, head=10, fR=0.30, zR=0.05, shR=66, elR=24, wp=3, shL=-10, elL=30, show=['glint']),
    stand(dx=0.17, dz=-0.11, lean=24, chest=8, head=14, fR=0.40, shR=88, elR=4, wp=-1, shL=-32, elL=16, show=['glint']),
    stand(dx=0.18, dz=-0.12, lean=26, chest=9, head=15, fR=0.40, shR=92, elR=2, wp=-6, shL=-36, elL=14, show=['glint']),
    stand(dx=0.11, dz=-0.08, lean=15, chest=6, head=10, fR=0.34, shR=64, elR=30, wp=14, shL=-16, elL=26),
    stand(dx=0.03, dz=-0.04, lean=8, chest=4, head=6, fR=0.20, shR=48, elR=44, wp=24, shL=-8, elL=26),
]


def HURT_POSES():
    return [stand(dx=-0.10, dz=-0.05, lean=-20, spine=-6, chest=-10, head=-26, shR=66, elR=64, wp=66, shL=56, elL=44),
        stand(dx=-0.11, dz=-0.06, lean=-22, spine=-6, chest=-11, head=-22, shR=62, elR=60, wp=58, shL=50, elL=42),
        stand(dx=-0.06, dz=-0.05, lean=-10, spine=-3, chest=-5, head=-10, shR=54, elR=54, wp=44, shL=24, elL=36),
        stand(dx=-0.02, dz=-0.03, lean=1, chest=1, head=0, shR=46, elR=50, wp=33, shL=2, elL=30)]


def DEATH_POSES():
    return [HURT_POSES()[0],
         stand(dx=-0.13, dz=-0.09, lean=-24, chest=-10, head=-24, fL=-0.30, zL=0.0, shR=74, elR=44, wp=96, shL=82, elL=30),
         P(dx=-0.12, dz=-0.24, lean=-26, chest=-8, head=-15, hipR=60, knR=100, anR=10, hipL=50, knL=105, anL=10, shR=92, elR=30, wp=130, shL=96, elL=20),
         P(dx=-0.18, dz=-0.40, lean=-58, chest=-8, head=-34, hipR=85, knR=70, hipL=75, knL=80, shR=124, elR=20, wp=160, shL=132, elL=20),
         P(dx=-0.23, dz=-0.52, lean=-88, spine=-2, chest=-2, head=-94, hipR=104, knR=34, anR=60, hipL=98, knL=44, anL=60,
           shR=176, elR=15, wp=178, shL=172, elL=10),
         P(dx=-0.23, dz=-0.48, lean=-84, spine=-2, chest=-2, head=-84, hipR=92, knR=26, anR=70, hipL=88, knL=36, anL=66,
           shR=170, elR=14, wp=174, shL=166, elL=10),
         P(dx=-0.23, dz=-0.51, lean=-86, spine=-1, chest=-1, head=-90, hipR=96, knR=20, anR=75, hipL=92, knL=30, anL=70,
           shR=178, elR=10, wp=180, shL=172, elL=8)]

ANIMS = [
    dict(name='idle', n=8, fps=8, loop=True, pose=idle),
    dict(name='walk', n=WALK_N, fps=WALK_FPS, loop=True, pose=walk),
    dict(name='attack', n=7, fps=16, loop=False, pose=lambda i, n: ATTACK_POSES()[i]),
    dict(name='hurt', n=4, fps=16, loop=False, pose=lambda i, n: HURT_POSES()[i]),
    dict(name='death', n=7, fps=12, loop=False, pose=lambda i, n: DEATH_POSES()[i]),
]

META = dict(cell_w=3.0, cell_h=2.0, pad_bottom=0.25,
            pivot_note='feet centre = bottom-centre of the 1.0 x 1.3 collision body; sprites face right',
            emissive_encoding='R = attack glow: spear-blade glint (attack frames 2-4), G = jingasa eye slit, B = amber light strips (do, hat band, spear collar); A = 255 where any mask is set')

if __name__ == '__main__':
    a = kit.args()
    kit.init()
    build()
    kit.animate(ANIMS, META, a['out'])
