"""Key every sprite frame of every animation onto the samurai rig, add smear arcs, save samurai.blend.

Run:  blender -b model.blend -P animate.py -- --out samurai.blend
Poses are authored in screen-plane degrees (see apply_pose):
  lean/spine/chest/neck : forward lean (+ = top tips forward)
  head                  : ABSOLUTE head pitch (0 = level, + = looking down)
  shR/shL, hipR/hipL    : ABSOLUTE limb angle from straight down (+ = forward, 90 = horizontal forward, 180 = up)
  elR/elL               : elbow bend (+ = forearm forward);  knR/knL: knee bend (+ = shin back)
  anR/anL               : ABSOLUTE foot pitch (+ = toes down)
  sw                    : ABSOLUTE blade direction, math angle (0 = forward, 90 = up, 180 = back)
  dx/dz                 : pelvis offset in world units
Coat tails are secondary motion: per-frame world drag angle (+ = streaming back), propagated down
each chain with a frame lag plus a travelling flutter wave, so loops stay seamless.
"""
import bpy, math, json, os, sys
from mathutils import Vector

D = math.radians
TAU = 2 * math.pi
S, C = math.sin, math.cos


def args():
    a = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    return {a[i].lstrip('-'): a[i + 1] for i in range(0, len(a) - 1, 2)}


BASE = dict(dz=-0.03, lean=4, spine=3, chest=4, head=6,
            hipR=16, knR=20, anR=0, hipL=-12, knL=16, anL=0,
            shR=28, elR=35, sw=-28, shL=-6, elL=30)


def P(base=None, **kw):
    d = dict(BASE if base is None else base)
    d.update(kw)
    return d


# ------------------------------------------------------------------ animations
def idle(i, n):
    ph = TAU * i / n
    b = S(ph)  # inhale at b = 1: chest lifts, shoulders rise, head settles; crest lags the head
    return P(chest=4 + 2.5 * b, spine=3 + b, dz=-0.03 + 0.01 * b, shy=0.012 * max(0.0, b) + 0.004 * b,
             shR=28 + 2.5 * b, shL=-6 + 3 * b, elL=30 - 3 * b, sw=-28 + 2 * b, head=6 - 1.5 * b, crest=-3 * S(ph - 1.1))


def run(i, n):
    ph = TAU * i / n
    # ph = 0 / pi: passing (high), pi/2 / 3pi/2: contact-reach (low); push-off between
    p = dict(lean=22 + 2 * C(2 * ph), dz=-0.045 + 0.04 * C(2 * ph), spine=5, chest=6 + 3 * S(2 * ph), neck=-4,
             head=4 - 2 * C(2 * ph), shL=12 + 62 * S(ph), elL=72 + 18 * S(ph), shR=-45 - 9 * S(ph), elR=25,
             sw=197 - 3 * S(ph), shy=0.006 * C(2 * ph), crest=-8 + 4 * S(2 * ph - 1.0))
    for s, q in (('R', ph), ('L', ph + math.pi)):
        p['hip' + s] = 8 + 56 * S(q)
        p['kn' + s] = 12 + 108 * max(0.0, C(q - 0.5)) ** 1.3
        p['an' + s] = 8 - 36 * S(q)
    return p


SLIDE_HOLD = dict(dx=0.08, dz=-0.46, lean=38, spine=6, chest=6, head=8,
                  hipR=92, knR=118, anR=0, hipL=-48, knL=42, anL=80,
                  shR=62, elR=25, sw=2, shL=-50, elL=20)


def slide(i, n):
    if i == 0:
        return dict(dx=0.05, dz=-0.30, lean=28, spine=5, chest=6, head=8, hipR=62, knR=72, hipL=-36, knL=52, anL=45,
                    shR=60, elR=30, sw=5, shL=-45, elL=20)
    b = S(TAU * i / 3)
    return P(SLIDE_HOLD, dz=-0.46 + 0.006 * b, sw=2 + 2 * b)


JUMP = [dict(dz=0.0, lean=8, spine=3, chest=4, head=0, hipR=35, knR=50, anR=10, hipL=-25, knL=25, anL=45,
             shR=-40, elR=25, sw=205, shL=70, elL=50),
        dict(dz=0.05, lean=4, spine=3, chest=3, head=0, hipR=75, knR=105, anR=10, hipL=15, knL=95, anL=30,
             shR=-30, elR=30, sw=200, shL=50, elL=70),
        dict(dz=0.04, lean=2, spine=2, chest=2, head=2, hipR=60, knR=85, anR=10, hipL=5, knL=80, anL=25,
             shR=-25, elR=35, sw=190, shL=40, elL=60)]


def jump(i, n):
    return JUMP[i]


def fall(i, n):
    b = S(TAU * i / n)
    return dict(dz=0.0, lean=0, spine=2, chest=2, head=-4, hipR=30 + 3 * b, knR=40, anR=25, hipL=-8 - 3 * b, knL=50, anL=35,
                shR=-20 + 4 * b, elR=40, sw=150 + 4 * b, shL=110 - 5 * b, elL=40)


def wall_slide(i, n):
    b = S(TAU * i / n)
    return dict(dx=0.10, dz=-0.04 + 0.008 * b, lean=-6, spine=0, chest=-4, head=10, shL=100 + 2 * b, elL=15,
                shR=-20, elR=30, sw=-115, hipR=55, knR=75, anR=-60, hipL=25, knL=70, anL=-50)


def hook_swing(i, n):
    b = S(TAU * i / n)
    return dict(dz=0.10, lean=10 * b, spine=2, chest=-2, head=4, shL=172, elL=8, shR=-50 + 8 * b, elR=15, sw=195,
                hipR=20 + 12 * b, knR=35 - 8 * b, anR=30, hipL=12 * b, knL=50, anL=40)


ATK_LEGS = [dict(dz=-0.07, dx=-0.02, lean=2, hipR=22, knR=35, hipL=-18, knL=25),
            dict(dz=-0.08, dx=-0.03, lean=-4, hipR=22, knR=38, hipL=-18, knL=28),
            dict(dz=-0.12, dx=0.12, lean=16, hipR=50, knR=60, anR=0, hipL=-32, knL=20, anL=35),
            dict(dz=-0.12, dx=0.12, lean=16, hipR=50, knR=60, anR=0, hipL=-32, knL=20, anL=35),
            dict(dz=-0.12, dx=0.12, lean=16, hipR=50, knR=60, anR=0, hipL=-32, knL=20, anL=35),
            dict(dz=-0.10, dx=0.10, lean=14, hipR=45, knR=55, hipL=-30, knL=20, anL=30),
            dict(dz=-0.05, dx=0.05, lean=8, hipR=25, knR=28, hipL=-16, knL=18),
            dict(dz=-0.04, dx=0.02, lean=5, hipR=18, knR=22, hipL=-13, knL=16)]
ATK_ARMS = [dict(shR=165, elR=65, sw=148, shL=45, elL=70, spine=0, chest=-4, head=2),
            dict(shR=185, elR=75, sw=160, shL=60, elL=80, spine=-2, chest=-8, head=0),
            dict(shR=130, elR=25, sw=78, shL=20, elL=50, spine=3, chest=6, head=6),
            dict(shR=88, elR=4, sw=2, shL=-40, elL=20, spine=4, chest=12, head=8),
            dict(shR=55, elR=6, sw=-42, shL=-55, elL=25, spine=4, chest=12, head=8),
            dict(shR=15, elR=22, sw=-105, shL=-40, elL=30, spine=4, chest=10, head=8),
            dict(shR=20, elR=32, sw=-70, shL=-20, elL=30, spine=3, chest=8, head=6),
            dict(shR=28, elR=35, sw=-30, shL=-6, elL=30, spine=3, chest=5, head=6)]


def attack(i, n):
    d = dict(ATK_LEGS[i])
    d.update(ATK_ARMS[i])
    return d


AIR_LEGS = dict(hipR=65, knR=95, anR=15, hipL=25, knL=85, anL=30, dz=0.05, lean=6)
AIR_ARMS = [dict(shR=165, elR=55, sw=125, shL=60, elL=60, chest=-6),
            dict(shR=110, elR=12, sw=30, shL=0, elL=40, chest=8),
            dict(shR=50, elR=5, sw=-55, shL=-45, elL=25, chest=14, lean=12),
            dict(shR=15, elR=20, sw=-110, shL=-30, elL=30, chest=12),
            dict(shR=30, elR=35, sw=-60, shL=-10, elL=30, chest=6)]


def air_attack(i, n):
    d = dict(AIR_LEGS)
    d.update(AIR_ARMS[i])
    return d


HURT = [dict(dx=-0.06, dz=-0.02, lean=-16, spine=-6, chest=-8, head=-18, hipR=25, knR=25, hipL=-10, knL=30, anL=20,
             shR=50, elR=60, sw=40, shL=70, elL=40),
        dict(dx=-0.04, dz=-0.03, lean=-10, spine=-4, chest=-5, head=-10, hipR=22, knR=24, hipL=-10, knL=28, anL=15,
             shR=40, elR=50, sw=10, shL=50, elL=40)]


def hurt(i, n):
    return HURT[i]


KNEEL = dict(hipR=5, knR=92, anR=70, hipL=0, knL=92, anL=75)
DEATH = [HURT[0],
         dict(dx=-0.05, dz=-0.10, lean=-8, chest=-4, head=5, hipR=30, knR=45, hipL=-5, knL=45, shR=20, elR=30, sw=-60, shL=30, elL=30),
         dict(dz=-0.24, lean=2, head=15, hipR=25, knR=70, hipL=10, knL=70, anL=30, shR=35, elR=20, sw=-48, shL=15, elL=20),
         dict(KNEEL, dz=-0.38, lean=4, chest=6, head=25, shR=68, elR=10, sw=-46, shL=5, elL=15),
         dict(KNEEL, dz=-0.40, lean=10, spine=10, chest=18, head=40, shR=72, elR=15, sw=-52, shL=0, elL=10),
         dict(dz=-0.34, lean=45, spine=8, chest=10, head=55, hipR=25, knR=100, anR=5, hipL=20, knL=95, anL=5,
              shR=50, elR=40, sw=-20, shL=50, elL=20),
         dict(dz=-0.61, lean=86, spine=2, chest=2, head=90, hipR=-84, knR=6, anR=10, hipL=-80, knL=10, anL=10,
              shR=60, elR=60, sw=196, shL=80, elL=30),
         dict(dz=-0.63, lean=88, spine=1, chest=1, head=92, hipR=-86, knR=4, anR=10, hipL=-84, knL=8, anL=10,
              shR=60, elR=60, sw=197, shL=82, elL=28)]


def death(i, n):
    return DEATH[i]


# name, frames, fps, loop, pose fn, coat(drag per frame or fn, flutter, cycles, lag, curl), smears [(frame, prev_sw, sw, strength)]
ANIMS = [
    dict(name='idle', n=8, fps=8, loop=True, pose=idle,
         coat=dict(drag=lambda i, n: 6 + 2 * S(TAU * i / n), flutter=4, cycles=1, lag=0.8, curl=2)),
    dict(name='run', n=12, fps=21, loop=True, pose=run,
         coat=dict(drag=lambda i, n: 72 + 4 * S(2 * TAU * i / n), flutter=9, cycles=2, lag=1.0, curl=7)),
    dict(name='slide', n=4, fps=12, loop=False, pose=slide,
         coat=dict(drag=[50, 82, 84, 82], flutter=9, cycles=2, lag=1.0, curl=5)),
    dict(name='jump', n=3, fps=10, loop=False, pose=jump,
         coat=dict(drag=[4, 10, 25], flutter=5, cycles=1, lag=1.0, curl=2)),
    dict(name='fall', n=3, fps=10, loop=True, pose=fall,
         coat=dict(drag=lambda i, n: 128, flutter=12, cycles=1, lag=0.7, curl=4)),
    dict(name='wall_slide', n=3, fps=10, loop=True, pose=wall_slide,
         coat=dict(drag=lambda i, n: 30, flutter=8, cycles=1, lag=1.0, curl=3)),
    dict(name='hook_swing', n=4, fps=8, loop=True, pose=hook_swing,
         coat=dict(drag=lambda i, n: 20 + 22 * S(TAU * i / n - 1.2), flutter=5, cycles=1, lag=1.0, curl=3)),
    dict(name='attack', n=8, fps=18, loop=False, pose=attack,
         coat=dict(drag=[5, -4, 25, 55, 60, 45, 25, 10], flutter=6, cycles=2, lag=1.0, curl=3),
         smears=[(2, 160, 78, 1.0), (3, 78, 2, 1.0), (4, 2, -42, 0.45)]),
    dict(name='air_attack', n=5, fps=18, loop=False, pose=air_attack,
         coat=dict(drag=[40, 50, 70, 65, 55], flutter=8, cycles=1, lag=1.0, curl=4),
         smears=[(1, 125, 30, 1.0), (2, 30, -55, 0.8)]),
    dict(name='hurt', n=2, fps=10, loop=False, pose=hurt,
         coat=dict(drag=[-20, -8], flutter=4, cycles=1, lag=0.5, curl=0)),
    dict(name='death', n=8, fps=10, loop=False, pose=death,
         coat=dict(drag=[-20, -5, 8, 22, 38, 72, 90, 92], flutter=3, cycles=1, lag=0.3, curl=1)),
]

TAILS = [('B', 1.0, 0.0), ('N', 0.92, 1.7), ('F', 0.85, 3.1)]
SEGS = 4


def coat_angles(spec, i, n, loop):
    drag = spec['drag']
    f = (lambda t: drag(t, n)) if callable(drag) else None

    def at(t):
        if f:
            return f(t % n if loop else max(0.0, t))
        if loop:
            t %= n
        else:
            t = min(max(t, 0.0), n - 1)
        a = int(math.floor(t))
        b = (a + 1) % n if loop else min(a + 1, n - 1)
        return drag[a] + (drag[b] - drag[a]) * (t - a)

    out = {}
    for name, w, phase in TAILS:
        out[name] = [w * at(i - k * spec['lag']) + spec['curl'] * k +
                     spec['flutter'] * S(TAU * spec['cycles'] * i / n - k * 0.9 + phase) for k in range(SEGS)]
    return out


def apply_pose(p, coat, frame):
    g = lambda k, d=0.0: float(p.get(k, d))
    Rr = {'root': g('lean'), 'pelvis': g('pelvis'), 'spine': g('spine'), 'chest': g('chest'), 'neck': g('neck')}
    torso = Rr['root'] + Rr['pelvis'] + Rr['spine'] + Rr['chest']
    Rr['head'] = g('head') - (torso + Rr['neck'])
    hipb = Rr['root'] + Rr['pelvis']
    for s in 'RL':
        Rr['sh_' + s] = -g('sh' + s) - torso
        Rr['el_' + s] = -g('el' + s)
        Rr['wr_' + s] = -g('wr' + s)
        Rr['hip_' + s] = -g('hip' + s) - hipb
        Rr['kn_' + s] = g('kn' + s)
        Rr['an_' + s] = g('an' + s) - (hipb + Rr['hip_' + s] + Rr['kn_' + s])
    Rr['sword'] = -g('sw') - (torso + Rr['sh_R'] + Rr['el_R'] + Rr['wr_R'])
    for t, angs in coat.items():
        prev = hipb
        for k, a in enumerate(angs):
            Rr['tail_%s_%d' % (t, k)] = a - prev
            prev = a
    Rr['crest_j'] = g('crest')
    for name, deg in Rr.items():
        e = bpy.data.objects[name]
        e.rotation_euler = (0.0, D(deg), e['rest_z'])
        e.keyframe_insert('rotation_euler', frame=frame)
    for s in 'RL':
        e = bpy.data.objects['sh_' + s]
        rl = e['rest_loc']
        e.location = (rl[0], rl[1], rl[2] + g('shy'))
        e.keyframe_insert('location', frame=frame)
    root = bpy.data.objects['root']
    rl = root['rest_loc']
    root.location = (rl[0] + g('dx'), rl[1], rl[2] + g('dz'))
    root.keyframe_insert('location', frame=frame)


def sword_state(frame):
    bpy.context.scene.frame_set(frame)
    m = bpy.data.objects['sword'].matrix_world
    return m.translation.copy()


def make_smear(name, h0, h1, a0, a1, strength, frame, last_frame):
    """Crescent ribbon swept by the blade between the previous and current pose, tapering to the old end."""
    n = 16
    r_out = 0.86
    width = 0.22 * strength
    V, F = [], []
    for i in range(n + 1):
        s = i / n
        a = D(a0 + (a1 - a0) * s)
        h = h0.lerp(h1, s)
        d = Vector((math.cos(a), 0, math.sin(a)))
        w = max(width * (s ** 1.4), 0.01)
        V.append(h + d * r_out)                                   # outer edge (blade tip path)
        V.append(h + d * max(0.08, r_out - 0.42 * w))             # core / edge boundary
        V.append(h + d * max(0.08, r_out - w))                    # inner edge
    mi = []
    for i in range(n):
        a, b = 3 * i, 3 * i + 3
        F.append((a, b, b + 1, a + 1)); mi.append(0)              # bright core
        F.append((a + 1, b + 1, b + 2, a + 2)); mi.append(1)      # tier-mask edge
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in V], [], F)
    me.materials.append(bpy.data.materials['smear'])
    me.materials.append(bpy.data.materials['smear_edge'])
    me.polygons.foreach_set('material_index', mi)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    for f, hide in ((1, True), (frame, False), (frame + 1, True)):
        if f <= last_frame + 1:
            ob.hide_render = hide
            ob.keyframe_insert('hide_render', frame=f)


def main():
    a = args()
    sc = bpy.context.scene
    frame = 1
    manifest = []
    poses = {}
    for an in ANIMS:
        start = frame
        for i in range(an['n']):
            p = an['pose'](i, an['n'])
            poses[(an['name'], i)] = p
            apply_pose(p, coat_angles(an['coat'], i, an['n'], an['loop']), frame)
            frame += 1
        manifest.append(dict(name=an['name'], start=start, count=an['n'], fps=an['fps'], loop=an['loop']))
    last = frame - 1
    for an, m in zip(ANIMS, manifest):
        for (fi, a0, a1, strength) in an.get('smears', []):
            f = m['start'] + fi
            h0 = sword_state(f - 1)
            h1 = sword_state(f)
            make_smear('smear_%s_%d' % (an['name'], fi), h0, h1, a0, a1, strength, f, last)
    sc.frame_start, sc.frame_end = 1, last
    sc['redline_anims'] = json.dumps(manifest)
    out = a.get('out')
    if out:
        bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(out), compress=True)
        print('SAVED', out, 'frames', last)


main()
