"""Shooter: corporate rifleman (body 0.9 x 1.5) with a long-barrelled rail rifle. Tall narrow silhouette,
olive long coat, gunmetal helmet with a single amber monocle, rifle longer than the body is wide.

States: idle, walk, aim (windup: barrel rails glow), fire (muzzle flash + recoil), hurt, death.
Visibility groups: charge (barrel glow), flash.
Run:  blender -b --factory-startup -P shooter.py -- --out shooter.blend   (see tools/art/common/make_char.sh)
"""
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'common'))
import kit  # noqa: E402
from kit import part, cat, box, tube, ellipsoid, ecyl, shell, lathe, extrude2d, S, C, TAU  # noqa: E402


def build():
    rp = kit.humanoid(thigh=0.34, shin=0.33, ankle=0.08, hip_w=0.08, spine=0.11, chest=0.17, neck=0.18, head=0.06,
                      sh_w=0.16, sh_z=0.13, upper=0.24, fore=0.22)
    kit.set_rig(rp)
    kit.weapon('rifle', 'R', 'wp')
    kit.legs(thigh_mat='olive', shin_mat='cloth', r=(0.068, 0.052, 0.048, 0.038), boot=(0.17, 0.08, 0.07), shin_guard='gunmetal')
    kit.arms({'R': ('olive', 'olive'), 'L': ('olive', 'olive')}, r=(0.044, 0.038, 0.04, 0.034), hand=(0.06, 0.05, 0.07))
    part('hips', 'pelvis_geo', 'pelvis', 'olive', ellipsoid((0, 0, -0.02), (0.11, 0.12, 0.09)))
    part('coat_skirt', 'pelvis_geo', 'pelvis', 'olive', shell((0, 0), -150, 150, [(0.05, 0.12, 0.13), (-0.30, 0.17, 0.18)], 14))
    part('belly', 'spine_geo', 'spine', 'cloth', ellipsoid((0.0, 0, 0.06), (0.10, 0.12, 0.10)))
    part('vest', 'chest_geo', 'chest', 'gunmetal', ellipsoid((0.02, 0, 0.04), (0.13, 0.155, 0.15)))
    part('pack', 'chest_geo', 'chest', 'helmet', box((-0.15, 0, 0.04), (0.08, 0.2, 0.2)), bevel=0.015)
    part('pack_strip', 'chest_geo', 'chest', 'strip', box((-0.19, -0.06, 0.04), (0.012, 0.03, 0.12)))
    H = 'head_geo'
    part('neck_m', H, 'head', 'cloth', tube((0, 0, -0.10), (0, 0, 0.0), 0.04, 0.04))
    part('helm', H, 'head', 'gunmetal', ellipsoid((0.0, 0, 0.05), (0.09, 0.085, 0.09)))
    part('helm_brim', H, 'head', 'helmet', box((0.06, 0, 0.09), (0.09, 0.17, 0.02), (0, -8, 0)))
    part('face', H, 'head', 'helmet', box((0.06, 0, 0.02), (0.07, 0.13, 0.08)), bevel=0.01)
    part('monocle', H, 'head', 'eye', tube((0.08, -0.03, 0.045), (0.12, -0.03, 0.045), 0.024, 0.024, 10))
    # rail rifle along +X of the rifle joint (held at the grip)
    part('stock', 'rifle', 'rifle', 'helmet', box((-0.16, 0, -0.02), (0.18, 0.04, 0.07)), bevel=0.01)
    part('receiver', 'rifle', 'rifle', 'gunmetal', box((0.06, 0, 0.01), (0.30, 0.05, 0.08)), bevel=0.01)
    part('grip', 'rifle', 'rifle', 'helmet', box((0.0, 0, -0.05), (0.04, 0.035, 0.08), (0, -15, 0)))
    part('scope', 'rifle', 'rifle', 'cyber', tube((0.0, 0, 0.07), (0.13, 0, 0.07), 0.018, 0.018, 8))
    part('rails', 'rifle', 'rifle', 'cyber', cat(box((0.45, 0, 0.025), (0.50, 0.03, 0.018)),
                                                box((0.45, 0, -0.025), (0.50, 0.03, 0.018))))
    part('rail_strip', 'rifle', 'rifle', 'strip', box((0.30, -0.02, 0.0), (0.16, 0.01, 0.012)))
    part('charge', 'rifle', 'rifle', 'glow', box((0.46, 0, 0.0), (0.48, 0.035, 0.035)), group='charge')
    part('flash', 'rifle', 'rifle', 'glow', extrude2d([(0.70, 0.0), (0.78, 0.05), (0.92, 0.0), (0.78, -0.05)], 0, 0.02), group='flash')
    part('flash_core', 'rifle', 'rifle', 'spark', extrude2d([(0.70, 0.0), (0.76, 0.022), (0.84, 0.0), (0.76, -0.022)], -0.02, 0.01), group='flash')
    return rp


BASE = dict(dz=-0.02, lean=3, spine=2, chest=2, head=4, hipR=12, knR=14, hipL=-10, knL=12,
            shR=20, elR=70, wp=60, shL=30, elL=60)


def P(**kw):
    d = dict(BASE)
    d.update(kw)
    return d


def st(**kw):
    return kit.stand(BASE, **kw)


def idle(i, n):
    b = S(TAU * i / n)
    w = S(TAU * i / n - 0.9)
    return st(dx=0.015 * w, dz=-0.02 + 0.012 * b, lean=3 + 1.2 * w, chest=2 + 3 * b, head=4 - 2 * b,
              shR=20 + 3 * b, wp=60 + 3 * b - 2 * w, shL=30 + 3 * b, elL=60 - 2 * b)


# Patrol walk with the rifle at port arms: 10 frames, fps solved from shooterSpeed 2.0 (planted feet);
# the rifle bobs after each footfall.
WALK_N, SPEED, HALF_STRIDE, DUTY = 10, 2.0, 0.28, 0.5
WALK_FPS, WALK_D = kit.gait_fps(WALK_N, SPEED, HALF_STRIDE, DUTY)


def walk(i, n):
    g = kit.gait(i, n, kit.HUMANP, SPEED, WALK_FPS, duty=DUTY, lift=0.13, bob=0.03, lean=6)
    u = g.pop('u')
    sw = C(TAU * u)
    bob2 = C(2 * TAU * (u - 0.08))
    p = P(lean=6, chest=2 - 2 * bob2, head=5 + 2 * bob2, pelvis=-3 * sw, shR=18 + 4 * bob2 - 3 * sw,
          shL=28 + 4 * bob2 + 3 * sw, wp=58 - 6 * bob2)
    p.update(g)
    return p


AIM = dict(lean=-2, chest=-2, head=0, shR=55, elR=40, wp=0, shL=82, elL=12)


def AIM_POSES():
    # anticipation dip (rifle drops, knees give), then the snap up into the aim and the charge glow
    seq = [(0.0, dict(dz=-0.05, lean=6, chest=4, head=8, shR=14, elR=80, wp=70, shL=24, elL=66)),
           (0.55, {}), (1.0, {}), (1.0, dict(show=['charge'])), (1.0, dict(show=['charge']))]
    out = []
    for k, extra in seq:
        d = {key: BASE.get(key, 0) + (v - BASE.get(key, 0)) * k for key, v in AIM.items()}
        d.update(extra)
        dz = d.pop('dz', -0.03 - 0.02 * k)
        out.append(st(dz=dz, fR=0.20, **d))
    return out


def FIRE_POSES():
    # kick (flash), recoil peak, follow-through settle, back on target
    rec = [(0.8, ['flash', 'charge']), (1.0, []), (0.45, []), (0.1, [])]
    return [st(dx=-0.05 * r, dz=-0.05, fR=0.20, **dict(AIM, chest=-2 - 10 * r, head=-6 * r, wp=10 * r, shR=55 + 8 * r, elR=40 + 6 * r),
               show=sh) for r, sh in rec]


def HURT_POSES():
    return [st(dx=-0.09, dz=-0.05, lean=-18, chest=-10, head=-22, shR=44, elR=72, wp=86, shL=56, elL=50),
            st(dx=-0.10, dz=-0.06, lean=-19, chest=-10, head=-19, shR=42, elR=71, wp=82, shL=52, elL=50),
            st(dx=-0.05, dz=-0.04, lean=-6, chest=-4, head=-6, shR=30, elR=70, wp=70, shL=40, elL=56),
            st(dx=-0.01, dz=-0.02, lean=2, chest=1, head=2, shR=22, elR=70, wp=62, shL=32, elL=60)]


def DEATH_POSES():
    return [HURT_POSES()[0],
            P(dx=-0.04, dz=-0.10, lean=-4, head=10, hipR=30, knR=50, hipL=-4, knL=55, shR=20, elR=40, wp=30, shL=20, elL=30),
            P(dz=-0.30, lean=4, head=20, hipR=8, knR=95, anR=70, hipL=0, knL=95, anL=70, shR=10, elR=20, wp=-20, shL=10, elL=20),
            P(dx=0.06, dz=-0.38, lean=40, head=50, hipR=30, knR=100, hipL=20, knL=100, shR=50, elR=30, wp=-10, shL=50, elL=20),
            P(dx=0.08, dz=-0.53, lean=87, spine=3, chest=3, head=90, hipR=-80, knR=8, anR=10, hipL=-78, knL=10, anL=10,
              shR=97, elR=20, wp=-6, shL=92, elL=20),
            P(dx=0.08, dz=-0.49, lean=82, spine=3, chest=3, head=86, hipR=-77, knR=10, anR=10, hipL=-75, knL=12, anL=10,
              shR=92, elR=20, wp=0, shL=88, elL=20),
            P(dx=0.08, dz=-0.53, lean=86, spine=2, chest=2, head=90, hipR=-82, knR=6, anR=10, hipL=-80, knL=8, anL=10,
              shR=96, elR=20, wp=-4, shL=92, elL=20)]


ANIMS = [
    dict(name='idle', n=8, fps=8, loop=True, pose=idle),
    dict(name='walk', n=WALK_N, fps=WALK_FPS, loop=True, pose=walk),
    dict(name='aim', n=5, fps=10, loop=False, pose=lambda i, n: AIM_POSES()[i]),
    dict(name='fire', n=4, fps=16, loop=False, pose=lambda i, n: FIRE_POSES()[i]),
    dict(name='hurt', n=4, fps=16, loop=False, pose=lambda i, n: HURT_POSES()[i]),
    dict(name='death', n=7, fps=12, loop=False, pose=lambda i, n: DEATH_POSES()[i]),
]

META = dict(cell_w=4.2, cell_h=2.4, pad_bottom=0.25,  # 4.2: the death fall (frames 4-5) reaches ~1.9 units forward
            pivot_note='feet centre = bottom-centre of the 0.9 x 1.5 collision body; sprites face right; muzzle at about +0.8 world units from the pivot in aim/fire',
            emissive_encoding='R = attack glow: barrel charge (aim frames 3-4, fire 0) + muzzle flash (fire 0), G = monocle, B = amber light strips (rail, backpack); A = 255 where any mask is set')

if __name__ == '__main__':
    a = kit.args()
    kit.init()
    build()
    kit.animate(ANIMS, META, a['out'])
