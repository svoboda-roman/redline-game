"""Shield: riot cyborg (body 1.1 x 1.6). Two heavy machine arms, chrome shins, a half chrome skull with a red eye and
neon mohawk; the tower shield rides on the near forearm with a red sensor slit.

Run:  blender -b --factory-startup -P shield.py -- --out shield.blend   (see tools/art/common/make_char.ps1)
"""
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'common'))
import kit  # noqa: E402
import cyber  # noqa: E402
from kit import part, cat, box, tube, ellipsoid, ecyl, shell, lathe, extrude2d, S, C, TAU  # noqa: E402


def build():
    rp = kit.humanoid(thigh=0.35, shin=0.34, ankle=0.08, hip_w=0.11, spine=0.12, chest=0.19, neck=0.17, head=0.06,
                      sh_w=0.21, sh_z=0.13, upper=0.25, fore=0.23)
    kit.set_rig(rp)
    kit.legs(thigh_mat='suit', shin_mat='chrome', r=(0.085, 0.065, 0.065, 0.052), boot=(0.20, 0.10, 0.08),
             knee_mat='gunmetal', shin_guard='gunmetal')
    # both arms are heavy machine arms (riot cyborg)
    cyber.robo_arm('R', r=(0.05, 0.044, 0.048, 0.042), hand=(0.075, 0.065, 0.08))
    cyber.robo_arm('L', r=(0.05, 0.044, 0.048, 0.042), hand=(0.075, 0.065, 0.08))
    part('hips', 'pelvis_geo', 'pelvis', 'suit', ellipsoid((0, 0, -0.02), (0.15, 0.17, 0.11)))
    part('tassets', 'pelvis_geo', 'pelvis', 'gunmetal', shell((0, 0), -120, 120, [(0.02, 0.17, 0.19), (-0.16, 0.20, 0.22)], 12))
    part('belly', 'spine_geo', 'spine', 'suit', ellipsoid((0.0, 0, 0.07), (0.14, 0.16, 0.13)))
    part('cuirass', 'chest_geo', 'chest', 'gunmetal', ellipsoid((0.03, 0, 0.05), (0.19, 0.22, 0.19)))
    part('cuirass_lights', 'chest_geo', 'chest', 'strip', cat(box((0.2, -0.07, 0.08), (0.012, 0.025, 0.1)), box((0.2, 0.07, 0.08), (0.012, 0.025, 0.1))))
    for s, sy in (('R', -1), ('L', 1)):
        part('pauldron_' + s, 'sh_' + s, 'sh_' + s, 'chrome', ellipsoid((0.0, sy * 0.04, 0.02), (0.12, 0.10, 0.09)))
    cyber.spine_cables(x=-0.18, z0=0.0, z1=0.28, n=3, spread=0.05)
    H = 'head_geo'
    part('neck_m', H, 'head', 'cable', tube((0, 0, -0.10), (0, 0, 0.0), 0.05, 0.05, 10))
    cyber.half_face(H, r=0.1, c=(0.01, 0.0, 0.05), mohawk=True, jaw=True)
    # the shield rides on the near forearm, standing in FRONT (+X) of the body
    kit.joint('shield', 'wr_R', (0.0, 0, 0.0))
    kit.WEAPONS.append(('shield', 'R', 'sa'))
    W, Hh, T = 0.50, 1.05, 0.07
    from mathutils import Matrix
    YAW = Matrix.Rotation(kit.D(-55), 3, 'Z')  # 3/4 yaw baked about the grip so the face shows

    def spart(name, f, j, mat, geo, **kw):
        v, fc, cl = geo
        return kit.part(name, f, j, mat, ([YAW @ x for x in v], fc, cl), **kw)
    X, Z = 0.10, -0.12  # slab centre: forward of the grip, hanging so it covers knees to brow
    F = X + T / 2 + 0.008  # front face
    spart('shield_slab', 'shield', 'shield', 'gunmetal', box((X, 0, Z), (T, W, Hh)), bevel=0.02)
    spart('shield_ribs', 'shield', 'shield', 'helmet', cat(box((F, 0, Z + 0.33), (0.02, W - 0.06, 0.05)),
                                                         box((F, 0, Z - 0.30), (0.02, W - 0.06, 0.05))))
    spart('shield_slit', 'shield', 'shield', 'cable', box((F, 0, Z + 0.20), (0.02, W * 0.6, 0.05)))
    spart('shield_eye', 'shield', 'shield', 'eye', box((F + 0.004, 0, Z + 0.20), (0.02, W * 0.18, 0.03)))
    rim = [box((F, -W / 2 + 0.012, Z), (0.016, 0.024, Hh)), box((F, W / 2 - 0.012, Z), (0.016, 0.024, Hh)),
           box((F, 0, Z + Hh / 2 - 0.012), (0.016, W, 0.024)), box((F, 0, Z - Hh / 2 + 0.012), (0.016, W, 0.024))]
    spart('shield_rim', 'shield', 'shield', 'strip', cat(*rim))
    spart('shield_flare', 'shield', 'shield', 'glow', box((F + 0.012, 0, Z), (0.01, W - 0.05, Hh - 0.05)), group='flare')
    return rp


# sa: shield tilt = absolute angle of the shield's face normal; 0 = upright facing forward, + = top leans back
BASE = dict(dz=-0.03, lean=6, spine=2, chest=3, head=4, hipR=16, knR=22, hipL=-14, knL=16,
            shR=40, elR=50, sa=0, shL=20, elL=60)


def P(**kw):
    d = dict(BASE)
    d.update(kw)
    return d


def st(**kw):
    return kit.stand(BASE, **kw)


def idle(i, n):
    b = S(TAU * i / n)
    w = S(TAU * i / n - 0.9)
    return st(dx=0.015 * w, dz=-0.03 + 0.012 * b, lean=6 + 1.2 * w, chest=3 + 3 * b, head=4 - 2 * b,
              shR=40 + 1.5 * b, shL=20 + 4 * b + 2 * w, elL=60 + 4 * b)


# March behind the shield: 10 frames, fps solved from shieldSpeed 2.6 (planted feet). The shield arm stays
# braced (it is the block surface); the free arm and the shield bob with the steps.
WALK_N, SPEED, HALF_STRIDE, DUTY = 10, 2.6, 0.30, 0.45
WALK_FPS, WALK_D = kit.gait_fps(WALK_N, SPEED, HALF_STRIDE, DUTY)


def walk(i, n):
    g = kit.gait(i, n, kit.HUMANP, SPEED, WALK_FPS, duty=DUTY, lift=0.14, bob=0.035, lean=9)
    u = g.pop('u')
    sw = C(TAU * u)
    bob2 = C(2 * TAU * (u - 0.08))
    p = P(lean=9, chest=3 - 2 * bob2, head=5 + 2 * bob2, pelvis=-3 * sw, shL=14 + 30 * sw, elL=58 - 10 * sw,
          shR=40 + 2 * bob2, sa=-2 * bob2)
    p.update(g)
    return p


def turn(i, n):
    return P(yaw=-45 * i, dz=-0.04, knR=28, knL=24, shR=38, elR=55)


# Block: brace (shoulder in, knees bend) -> impact (shield shoved forward, flare) -> follow-through push -> settle.
def BLOCK_POSES():
    return [st(dx=-0.02, dz=-0.07, lean=12, head=10, shR=46, elR=44, show=[]),
            st(dx=-0.07, dz=-0.11, lean=16, head=14, shR=56, elR=26, sa=4, show=['flare']),
            st(dx=0.03, dz=-0.10, lean=18, head=12, shR=54, elR=22, sa=-2, show=['flare']),
            st(dx=0.0, dz=-0.07, lean=12, head=8, shR=48, elR=36, show=[])]


def HURT_POSES():
    return [st(dx=-0.09, dz=-0.05, lean=-12, chest=-8, head=-18, shR=52, elR=38, sa=14, shL=44, elL=48),
            st(dx=-0.10, dz=-0.06, lean=-13, chest=-8, head=-15, shR=51, elR=39, sa=12, shL=42, elL=48),
            st(dx=-0.05, dz=-0.04, lean=-3, chest=-3, head=-5, shR=46, elR=44, sa=6, shL=30, elL=54),
            st(dx=-0.01, dz=-0.03, lean=4, chest=2, head=2, shR=41, elR=49, sa=1, shL=22, elL=59)]


def DEATH_POSES():
    return [HURT_POSES()[0],
            P(dx=-0.04, dz=-0.10, lean=0, head=10, hipR=30, knR=50, hipL=-4, knL=55, shR=30, elR=40, sa=-20, shL=20, elL=30),
            P(dz=-0.30, lean=6, head=20, hipR=8, knR=95, anR=70, hipL=0, knL=95, anL=70, shR=40, elR=30, sa=-50, shL=10, elL=20),
            P(dx=0.06, dz=-0.40, lean=40, head=50, hipR=30, knR=100, hipL=20, knL=100, shR=70, elR=20, sa=-80, shL=50, elL=20),
            P(dx=0.08, dz=-0.56, lean=86, spine=3, chest=3, head=90, hipR=-80, knR=8, anR=10, hipL=-78, knL=10, anL=10,
              shR=97, elR=10, sa=-92, shL=92, elL=20),
            P(dx=0.08, dz=-0.52, lean=82, spine=3, chest=3, head=86, hipR=-77, knR=10, anR=10, hipL=-75, knL=12, anL=10,
              shR=92, elR=12, sa=-87, shL=88, elL=20),
            P(dx=0.08, dz=-0.56, lean=84, spine=3, chest=3, head=88, hipR=-80, knR=8, anR=10, hipL=-78, knL=10, anL=10,
              shR=95, elR=10, sa=-90, shL=90, elL=20)]


ANIMS = [
    dict(name='idle', n=8, fps=8, loop=True, pose=idle),
    dict(name='walk', n=WALK_N, fps=WALK_FPS, loop=True, pose=walk),
    dict(name='turn', n=4, fps=7, loop=False, pose=turn),
    dict(name='block', n=4, fps=14, loop=False, pose=lambda i, n: BLOCK_POSES()[i]),
    dict(name='hurt', n=4, fps=16, loop=False, pose=lambda i, n: HURT_POSES()[i]),
    dict(name='death', n=7, fps=12, loop=False, pose=lambda i, n: DEATH_POSES()[i]),
]

META = dict(cell_w=3.4, cell_h=2.4, pad_bottom=0.25,
            pivot_note='feet centre = bottom-centre of the 1.1 x 1.6 collision body; sprites face right, shield on the front (+X) side; turn yaws toward the camera over 4 frames (0..-135 deg), flip the sprite after its last frame',
            emissive_encoding='R = attack glow: shield face flare (block frames 1-2 of 4), G = visor, B = amber shield rim strip; A = 255 where any mask is set')

if __name__ == '__main__':
    a = kit.args()
    kit.init()
    build()
    kit.animate(ANIMS, META, a['out'])
