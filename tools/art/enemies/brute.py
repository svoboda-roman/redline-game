"""Brute: tentacled mutant juggernaut (body 1.5 x 2.1). Bloated flesh held together by a chrome harness plate and leg
braces, a small sunken head with three red eyes and a toothed maw, bio-luminescent nodules on the hump and four
tentacles off the back and near shoulder that sway per animation (rise and light up in windup/charge, trail
in the charge, go limp when crashed and on death).

States: idle, walk, windup, charge, crashed (harness torn open on a glowing heart, sparks), hurt, death.
Visibility groups: intact (closed plate), flare (eyes + tentacle tips), crack, spark0..2.
Run:  blender -b --factory-startup -P brute.py -- --out brute.blend   (see tools/art/common/make_char.ps1)
"""
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'common'))
import kit  # noqa: E402
import cyber  # noqa: E402
from kit import part, cat, box, tube, ellipsoid, ecyl, shell, lathe, extrude2d, S, C, TAU  # noqa: E402
import math  # noqa: E402

# Tentacles: chain of n joints off the chest at `at` (chest frame), segment length seg, root radius r,
# rest angle `base` (screen-plane degrees, 0 = straight up, + = toward the front), wave phase.
TENTS = [dict(name='tA', at=(-0.24, -0.20, 0.16), base=-62, n=6, seg=0.16, r=0.095, phase=0.0),
         dict(name='tB', at=(-0.30, 0.16, 0.12), base=-100, n=6, seg=0.15, r=0.09, phase=1.7),
         dict(name='tC', at=(-0.18, -0.04, 0.27), base=-28, n=7, seg=0.13, r=0.085, phase=3.1),
         dict(name='tD', at=(0.02, -0.34, 0.10), base=70, n=5, seg=0.14, r=0.08, phase=4.4)]


def build():
    rp = kit.humanoid(thigh=0.45, shin=0.41, ankle=0.11, hip_w=0.18, spine=0.20, chest=0.30, neck=0.17, head=0.04,
                      sh_w=0.36, sh_z=0.06, upper=0.32, fore=0.30)
    kit.set_rig(rp)
    kit.legs(thigh_mat='flesh', shin_mat='flesh_dk', boot_mat='flesh_dk', r=(0.15, 0.12, 0.11, 0.095), boot=(0.30, 0.17, 0.11),
             knee_mat='chrome', shin_guard=None)
    for s in 'RL':  # chrome leg braces bolted into the meat
        part('brace_' + s, 'kn_' + s, 'kn_' + s, 'chrome', cat(tube((0.11, 0, -0.03), (0.11, 0, -0.36), 0.02, 0.02, 6),
                                                              ecyl((0, 0), -0.2, -0.16, 0.125, 0.125, 14)))
    # arms: swollen flesh, chrome bracers, hooked claws
    kit.arms({'R': ('flesh', 'flesh'), 'L': ('flesh', 'flesh_dk')}, r=(0.11, 0.10, 0.12, 0.11),
             hand=(0.15, 0.13, 0.13), hand_mat='flesh_dk')
    for s in 'RL':
        part('bracer_' + s, 'el_' + s, 'el_' + s, 'chrome', ecyl((0, 0), -0.24, -0.12, 0.125, 0.125, 16))
        part('bracer_light_' + s, 'el_' + s, 'el_' + s, 'strip', box((0.125, -0.03, -0.18), (0.012, 0.03, 0.08)))
        part('claws_' + s, 'wr_' + s, 'wr_' + s, 'steel', cat(*[tube((0.06, y, -0.12), (0.13, y, -0.24), 0.022, 0.004, 6)
                                                               for y in (-0.045, 0.0, 0.045)]))
    # mass: bloated belly and chest, bio-luminescent nodules, chrome spine rods
    part('hips', 'pelvis_geo', 'pelvis', 'flesh_dk', ellipsoid((0.02, 0, 0.0), (0.32, 0.30, 0.19)))
    part('belt', 'pelvis_geo', 'pelvis', 'gunmetal', ecyl((0.02, 0), -0.04, 0.05, 0.33, 0.32, 22))
    part('belly', 'spine_geo', 'spine', 'flesh', ellipsoid((0.10, 0, 0.10), (0.42, 0.36, 0.30)))
    part('belly_veins', 'spine_geo', 'spine', 'flesh_dk', cat(box((0.42, -0.1, 0.08), (0.03, 0.02, 0.3), (0, 0, 0)),
                                                            box((0.40, 0.08, 0.14), (0.03, 0.02, 0.22), (0, 20, 0))))
    part('chest_m', 'chest_geo', 'chest', 'flesh', ellipsoid((0.04, 0, 0.0), (0.38, 0.38, 0.27)))
    part('hump', 'chest_geo', 'chest', 'flesh_dk', ellipsoid((-0.2, 0, 0.12), (0.26, 0.32, 0.24)))
    part('nodules', 'chest_geo', 'chest', 'strip', cat(*[ellipsoid(c, (0.035, 0.035, 0.035)) for c in
                                                       ((-0.36, -0.12, 0.16), (-0.4, 0.05, 0.02), (-0.3, -0.2, -0.05), (-0.42, -0.02, 0.24))]))
    part('spine_rods', 'chest_geo', 'chest', 'chrome', cat(*[tube((-0.38, y, -0.1), (-0.30, y, 0.32), 0.016, 0.016, 6) for y in (-0.08, 0.08)]))
    # chest harness: intact plate vs torn open on the glowing heart (crashed / death)
    part('plate', 'chest_geo', 'chest', 'gunmetal', box((0.36, 0, -0.06), (0.10, 0.40, 0.34), (0, -8, 0)), bevel=0.03, group='intact')
    part('plate_rivets', 'chest_geo', 'chest', 'chrome', cat(box((0.42, 0, 0.06), (0.02, 0.38, 0.03), (0, -8, 0)),
                                                            box((0.42, 0, -0.14), (0.02, 0.38, 0.03), (0, -8, 0))), group='intact')
    part('core_strip', 'chest_geo', 'chest', 'strip', box((0.42, -0.12, -0.04), (0.02, 0.06, 0.12), (0, -8, 0)), group='intact')
    part('plate_top', 'chest_geo', 'chest', 'gunmetal', box((0.40, 0, 0.07), (0.10, 0.40, 0.18), (0, -26, 0)), bevel=0.025, group='crack')
    part('plate_bot', 'chest_geo', 'chest', 'gunmetal', box((0.41, 0, -0.22), (0.10, 0.40, 0.14), (0, 22, 0)), bevel=0.025, group='crack')
    part('core', 'chest_geo', 'chest', 'glow', ellipsoid((0.40, 0, -0.07), (0.08, 0.17, 0.10)), group='crack')
    # head: a small mutated head sunk in the meat, three red eyes and a toothed maw
    H = 'head_geo'
    part('head_m', H, 'head', 'flesh', ellipsoid((0.06, 0, 0.06), (0.16, 0.15, 0.13)))
    part('head_lump', H, 'head', 'flesh_dk', ellipsoid((-0.02, 0.05, 0.13), (0.11, 0.1, 0.08)))
    part('eyes', H, 'head', 'eye', cat(ellipsoid((0.2, -0.06, 0.1), (0.025, 0.03, 0.026)), ellipsoid((0.19, 0.03, 0.12), (0.022, 0.026, 0.022)),
                                       ellipsoid((0.16, -0.1, 0.16), (0.018, 0.02, 0.018))))
    part('eyes_flare', H, 'head', 'eye', cat(ellipsoid((0.21, -0.06, 0.1), (0.04, 0.045, 0.04)), ellipsoid((0.2, 0.03, 0.12), (0.035, 0.04, 0.035)),
                                             ellipsoid((0.17, -0.1, 0.16), (0.03, 0.032, 0.03))), group='flare')
    part('maw', H, 'head', 'flesh_dk', box((0.19, -0.02, -0.01), (0.07, 0.18, 0.06)))
    part('teeth', H, 'head', 'steel', cat(*[box((0.215, y, 0.015), (0.02, 0.012, 0.03)) for y in (-0.08, -0.04, 0.0, 0.04)],
                                         *[box((0.215, y, -0.04), (0.02, 0.012, 0.03)) for y in (-0.06, -0.02, 0.02)]))
    # sparks (crashed): three alternating sets of short bright streaks
    sp = [[((0.50, -0.1, 0.02), 30), ((0.52, -0.05, -0.15), -20), ((0.25, -0.3, 0.2), 60)],
          [((0.55, -0.1, -0.05), -40), ((0.38, -0.2, 0.18), 15), ((0.56, -0.05, 0.09), 70)],
          [((0.52, -0.15, 0.07), 10), ((0.53, -0.1, -0.23), -55), ((0.30, -0.3, -0.05), 35)]]
    for k, st in enumerate(sp):
        part('spark%d' % k, 'chest_geo', 'chest', 'spark',
             cat(*[box(c, (0.09, 0.02, 0.018), (0, -a, 0)) for c, a in st]), smooth=False, group='spark%d' % k)
    # tentacles: joint chains off the chest (rest = straight up), swung per frame by tentacles() below
    for T in TENTS:
        prev = 'chest'
        loc = T['at']
        for k in range(T['n']):
            jn = '%s%d' % (T['name'], k)
            kit.joint(jn, prev, loc)
            f = 1.0 - k / T['n']
            r0, r1 = T['r'] * (0.25 + 0.75 * f), T['r'] * (0.25 + 0.75 * (f - 1.0 / T['n']))
            last = k == T['n'] - 1
            part(jn, jn, jn, 'flesh', cat(ellipsoid((0, 0, 0), (r0 * 1.05,) * 3),
                                          tube((0, 0, 0), (0, 0, T['seg']), r0, max(r1, 0.012), 10)))
            if k % 2 == 1:  # suckers along the underside
                part(jn + '_suck', jn, jn, 'flesh_dk', ellipsoid((r0 * 0.85, 0, T['seg'] * 0.5), (r0 * 0.35, r0 * 0.5, r0 * 0.45)))
            if last:
                tip = (0, 0, T['seg'])
                part(jn + '_tip', jn, jn, 'flesh_dk', ellipsoid(tip, (0.025, 0.025, 0.035)))
                part(jn + '_glow', jn, jn, 'glow', ellipsoid(tip, (0.045, 0.045, 0.055)), group='flare')
            prev = jn
            loc = (0, 0, T['seg'])
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


# Tentacle motion per animation, t = frame / count in [0, 1): (root offset, wave amplitude, per-segment curl,
# wave cycles over the animation). Looping anims use whole cycles so they loop seamlessly.
TMODE = {
    'idle':    lambda t: (0.0, 12.0, 17.0, 1),
    'walk':    lambda t: (-8.0, 14.0, 15.0, 1),
    'windup':  lambda t: (25.0 * t + 10.0, 8.0, 10.0, 1),          # rear up, tips light up (flare group)
    'charge':  lambda t: (-45.0, 10.0, -6.0, 2),                    # streamed back by the sprint
    'crashed': lambda t: (-95.0, 4.0, 14.0, 1),                    # hanging limp
    'hurt':    lambda t: (-30.0 * (1 - t), 16.0 * (1 - t), 6.0, 1),
    'death':   lambda t: (-40.0 - 90.0 * t, 10.0 * (1 - t), 10.0 * (1 - t), 1),
}


def tentacles(mode, fn):
    """Wraps a pose function: adds the tentacle joint swings (pose 'rot' keys) for this animation."""
    def pose(i, n):
        p = fn(i, n)
        off, amp, curl, cyc = TMODE[mode](i / max(1, n))
        rot = dict(p.get('rot', {}))
        for T in TENTS:
            for k in range(T['n']):
                wave = amp * math.sin(TAU * cyc * i / max(1, n) + T['phase'] - 0.8 * k)
                rot['%s%d' % (T['name'], k)] = (T['base'] + off if k == 0 else curl) + wave
        p['rot'] = rot
        return p
    return pose


ANIMS = [
    dict(name='idle', n=8, fps=7, loop=True, pose=tentacles('idle', idle)),
    dict(name='walk', n=WALK_N, fps=WALK_FPS, loop=True, pose=tentacles('walk', walk)),
    dict(name='windup', n=5, fps=10, loop=False, pose=tentacles('windup', lambda i, n: WINDUP_POSES()[i])),
    dict(name='charge', n=CH_N, fps=CH_FPS, loop=True, pose=tentacles('charge', charge)),
    dict(name='crashed', n=6, fps=10, loop=True, pose=tentacles('crashed', crashed)),
    dict(name='hurt', n=4, fps=16, loop=False, pose=tentacles('hurt', lambda i, n: HURT_POSES()[i])),
    dict(name='death', n=7, fps=10, loop=False, pose=tentacles('death', lambda i, n: DEATH_POSES()[i])),
]

META = dict(cell_w=4.4, cell_h=3.2, pad_bottom=0.25,
            pivot_note='feet centre = bottom-centre of the 1.5 x 2.1 collision body; sprites face right',
            emissive_encoding='R = attack glow: tentacle tips (windup/charge) + exposed heart (crashed, death) + sparks (crashed), G = three red eyes (wider flare in windup/charge), B = bio-luminescent nodules, bracer and harness lights; A = 255 where any mask is set')

if __name__ == '__main__':
    a = kit.args()
    kit.init()
    build()
    kit.animate(ANIMS, META, a['out'])
