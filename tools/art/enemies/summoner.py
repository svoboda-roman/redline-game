"""Summoner: techno-priest cyborg (body 0.9 x 1.7). Robe with a neon hem, a half chrome skull crowned with data cables
ending in glowing plugs, chrome machine fingers; holo talismans orbit the back and a glyph ring flares on the cast.

Run:  blender -b --factory-startup -P summoner.py -- --out summoner.blend   (see tools/art/common/make_char.ps1)
"""
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'common'))
import kit  # noqa: E402
import cyber  # noqa: E402
from kit import part, cat, box, tube, ellipsoid, ecyl, shell, lathe, extrude2d, S, C, TAU, D  # noqa: E402
import math  # noqa: E402

TALIS = [(-0.34, 0.30), (-0.42, 0.05), (-0.30, -0.20)]  # (x, z) rest offset from the chest, behind the back


def build():
    rp = kit.humanoid(thigh=0.36, shin=0.36, ankle=0.08, hip_w=0.07, spine=0.12, chest=0.19, neck=0.18, head=0.06,
                      sh_w=0.16, sh_z=0.13, upper=0.25, fore=0.23)
    kit.set_rig(rp)
    kit.legs(thigh_mat='cloth', shin_mat='cloth', r=(0.06, 0.05, 0.045, 0.036), boot=(0.15, 0.07, 0.06))
    for s, sy in (('R', -1), ('L', 1)):
        part('upper_' + s, 'sh_' + s, 'sh_' + s, 'suit', cat(ellipsoid((0, 0, 0), (0.05,) * 3),
                                                             tube((0, 0, 0), (0, 0, -0.25), 0.05, 0.06)))
        part('sleeve_' + s, 'el_' + s, 'el_' + s, 'suit', tube((0, 0, 0.02), (0, 0, -0.22), 0.06, 0.10, 14))
        part('cuff_' + s, 'el_' + s, 'el_' + s, 'strip', tube((0, 0, -0.205), (0, 0, -0.225), 0.102, 0.102, 14))
        # long chrome machine fingers
        part('hand_' + s, 'wr_' + s, 'wr_' + s, 'chrome', cat(box((0.01, 0, -0.04), (0.05, 0.045, 0.07)),
                                                             box((0.02, 0, -0.10), (0.02, 0.04, 0.07))), bevel=0.008)
    # robe: rigid skirt on the pelvis reaching the shins, open at the front, neon hem
    part('robe', 'pelvis_geo', 'pelvis', 'suit', shell((0, 0), -150, 150, [(0.06, 0.13, 0.14), (-0.30, 0.20, 0.21), (-0.62, 0.27, 0.28)], 16))
    part('robe_hem', 'pelvis_geo', 'pelvis', 'strip', shell((0, 0), -150, 150, [(-0.58, 0.268, 0.278), (-0.64, 0.275, 0.285)], 16))
    part('hips', 'pelvis_geo', 'pelvis', 'cloth', ellipsoid((0, 0, -0.02), (0.11, 0.12, 0.09)))
    part('belly', 'spine_geo', 'spine', 'suit', ellipsoid((0.0, 0, 0.06), (0.11, 0.12, 0.10)))
    part('sash', 'spine_geo', 'spine', 'gunmetal', ecyl((0, 0), -0.02, 0.05, 0.115, 0.125, 18))
    part('torso', 'chest_geo', 'chest', 'suit', ellipsoid((0.02, 0, 0.04), (0.13, 0.15, 0.15)))
    part('stole', 'chest_geo', 'chest', 'chrome', shell((0, 0), 30, 330, [(0.08, 0.15, 0.16), (0.20, 0.10, 0.11)], 14))
    part('spine_strip', 'chest_geo', 'chest', 'strip', box((-0.14, 0, 0.0), (0.012, 0.025, 0.2)))
    cyber.spine_cables(x=-0.15, z0=0.05, z1=0.3, n=3, spread=0.04)
    H = 'head_geo'
    part('neck_m', H, 'head', 'cable', tube((0, 0, -0.10), (0, 0, 0.0), 0.04, 0.04, 10))
    cyber.half_face(H, r=0.085, c=(0.01, 0.0, 0.04), mohawk=False, jaw=True)
    # crown of data cables arching back from the skull, glowing plugs at the ends
    crown = []
    tips = []
    for k, (a, l) in enumerate(((-20, 0.24), (5, 0.30), (30, 0.26), (55, 0.2))):
        ang = math.radians(a)
        p0 = (-0.02, -0.03 + 0.02 * k, 0.11)
        p1 = (p0[0] - l * math.sin(ang + 0.6), p0[1], p0[2] + l * math.cos(ang + 0.6))
        crown.append(tube(p0, p1, 0.013, 0.009, 6))
        tips.append(ellipsoid(p1, (0.016, 0.016, 0.016)))
    part('crown', H, 'head', 'cable', cat(*crown))
    part('crown_tips', H, 'head', 'strip', cat(*tips))
    # floating talismans (holo cards): each on its own chest-child joint so it can bob (loc) independently
    for k, (x, z) in enumerate(TALIS):
        kit.joint('talis%d' % k, 'chest', (x, 0.05, z))
        part('talis%d' % k, 'talis%d' % k, 'talis%d' % k, 'talisman', box((0, 0, 0), (0.07, 0.01, 0.15), (0, 8 - 8 * k, 0)), smooth=False)
        part('talis_glyph%d' % k, 'talis%d' % k, 'talis%d' % k, 'glow', box((0, -0.01, 0.0), (0.03, 0.01, 0.08), (0, 8 - 8 * k, 0)),
             smooth=False, group='flare')
    # cast glyph ring in front of the raised hands
    kit.joint('glyph', 'chest', (0.45, -0.05, 0.42))
    seg = []
    for j in range(10):
        a0 = TAU * j / 10
        pts = []
        for t in (0, 1):
            a = a0 + t * TAU / 10 * 0.7
            pts.append((0.18 * math.cos(a), 0.18 * math.sin(a)))
        for t in (1, 0):
            a = a0 + t * TAU / 10 * 0.7
            pts.append((0.14 * math.cos(a), 0.14 * math.sin(a)))
        seg.append(extrude2d(pts, 0, 0.01))
    part('glyph_ring', 'glyph', 'glyph', 'glow', cat(*seg), smooth=False, group='glyph')
    part('glyph_core', 'glyph', 'glyph', 'talisman', extrude2d([(0, 0.08), (0.06, 0), (0, -0.08), (-0.06, 0)], 0, 0.01),
         smooth=False, group='glyph')
    return rp


BASE = dict(dz=-0.02, lean=2, spine=2, chest=2, head=6, hipR=8, knR=10, hipL=-6, knL=8,
            shR=12, elR=60, shL=8, elL=62)


def bob(i, n, amp=0.03):
    return {'talis%d' % k: (0.0, amp * S(TAU * i / n + k * 2.1)) for k in range(3)}


def P(**kw):
    d = dict(BASE)
    d.update(kw)
    return d


def st(**kw):
    return kit.stand(BASE, **kw)


def idle(i, n):
    b = S(TAU * i / n)
    w = S(TAU * i / n - 0.9)
    return st(dx=0.012 * w, dz=-0.02 + 0.01 * b, lean=2 + w, chest=2 + 3 * b, head=6 - 2 * b,
              shR=12 + 3 * b, shL=8 + 3 * b, loc=bob(i, n))


# Flee sprint: 10 frames, fps solved from summonerSpeed 6.0 (planted feet, long flight), robes trailing.
WALK_N, SPEED, HALF_STRIDE, DUTY = 10, 6.0, 0.32, 0.35
WALK_FPS, WALK_D = kit.gait_fps(WALK_N, SPEED, HALF_STRIDE, DUTY)


def flee(i, n):
    g = kit.gait(i, n, kit.HUMANP, SPEED, WALK_FPS, duty=DUTY, lift=0.2, bob=0.04, lean=16)
    u = g.pop('u')
    ph = TAU * u
    sw = C(ph)
    p = P(lean=16, chest=4, head=0, shL=-20 + 34 * sw, elL=70, shR=-20 - 34 * sw, elR=70, pelvis=-4 * sw,
          loc={'talis%d' % k: (-0.10, 0.05 * S(ph * 2 + k)) for k in range(3)})
    p.update(g)
    return p


def CAST_POSES():
    # anticipation crouch (arms gather low) -> sweep up -> hold with glyph -> release follow-through
    out = []
    for i, (k, crouch, fx) in enumerate([(-0.25, 1.0, []), (0.4, 0.4, ['flare']), (1.0, 0.0, ['flare', 'glyph']),
                                         (1.1, 0.0, ['flare', 'glyph']), (1.0, 0.0, ['flare', 'glyph']), (0.9, 0.1, ['flare', 'glyph'])]):
        kk = max(0.0, k)
        up = {'talis%d' % j: (0.10 * kk + 0.05 * j * kk, 0.25 * k + 0.02 * S(TAU * i / 6 + j)) for j in range(3)}
        out.append(st(dz=-0.02 - 0.07 * crouch, lean=-4 * k + 8 * crouch, chest=-6 * k + 4 * crouch, head=-10 * k + 6 * crouch,
                      shR=12 + 140 * k, elR=60 - 45 * kk, shL=8 + 150 * k, elL=62 - 50 * kk, loc=up, show=fx))
    return out


def HURT_POSES():
    return [st(dx=-0.09, dz=-0.04, lean=-16, chest=-10, head=-20, shR=44, elR=60, shL=54, elL=50, loc=bob(1, 4, 0.08)),
            st(dx=-0.10, dz=-0.05, lean=-17, chest=-10, head=-17, shR=42, elR=60, shL=52, elL=50, loc=bob(1, 4, 0.06)),
            st(dx=-0.05, dz=-0.03, lean=-6, chest=-4, head=-6, shR=28, elR=60, shL=28, elL=56, loc=bob(2, 4)),
            st(dx=-0.01, dz=-0.02, lean=1, chest=1, head=4, shR=15, elR=60, shL=11, elL=60, loc=bob(3, 4))]


def DEATH_POSES():
    return [HURT_POSES()[0],
            P(dx=-0.04, dz=-0.10, lean=0, head=14, hipR=30, knR=50, hipL=-4, knL=55, shR=20, elR=40, shL=20, elL=30,
              loc={'talis%d' % k: (0.0, -0.15) for k in range(3)}),
            P(dz=-0.34, lean=6, head=24, hipR=8, knR=95, anR=70, hipL=0, knL=95, anL=70, shR=20, elR=30, shL=10, elL=20,
              loc={'talis%d' % k: (0.05, -0.40) for k in range(3)}),
            P(dx=0.06, dz=-0.42, lean=40, head=50, hipR=30, knR=100, hipL=20, knL=100, shR=50, elR=30, shL=50, elL=20,
              loc={'talis%d' % k: (0.2, -0.65) for k in range(3)}),
            P(dx=0.06, dz=-0.57, lean=87, spine=3, chest=3, head=90, hipR=-80, knR=8, anR=10, hipL=-78, knL=10, anL=10,
              shR=97, elR=20, shL=92, elL=20, loc={'talis%d' % k: (0.2, -0.72) for k in range(3)}),
            P(dx=0.06, dz=-0.53, lean=82, spine=3, chest=3, head=86, hipR=-77, knR=10, anR=10, hipL=-75, knL=12, anL=10,
              shR=92, elR=20, shL=88, elL=20, loc={'talis%d' % k: (0.2, -0.76) for k in range(3)}),
            P(dx=0.06, dz=-0.56, lean=84, spine=3, chest=3, head=88, hipR=-80, knR=8, anR=10, hipL=-78, knL=10, anL=10,
              shR=95, elR=20, shL=90, elL=20, loc={'talis%d' % k: (0.2, -0.75) for k in range(3)})]


ANIMS = [
    dict(name='idle', n=8, fps=8, loop=True, pose=idle),
    dict(name='flee', n=WALK_N, fps=WALK_FPS, loop=True, pose=flee),
    dict(name='cast', n=6, fps=10, loop=False, pose=lambda i, n: CAST_POSES()[i]),
    dict(name='hurt', n=4, fps=16, loop=False, pose=lambda i, n: HURT_POSES()[i]),
    dict(name='death', n=7, fps=12, loop=False, pose=lambda i, n: DEATH_POSES()[i]),
]

META = dict(cell_w=3.0, cell_h=2.8, pad_bottom=0.45,  # 0.45: death_03 slumps below the feet line
            pivot_note='feet centre = bottom-centre of the 0.9 x 1.7 collision body; sprites face right',
            emissive_encoding='R = attack glow: talisman glyphs (cast frames 1-5) + glyph ring (cast frames 2-5), G = visor, B = amber light strips (cuffs, spine) + holo-talisman paper; A = 255 where any mask is set')

if __name__ == '__main__':
    a = kit.args()
    kit.init()
    build()
    kit.animate(ANIMS, META, a['out'])
