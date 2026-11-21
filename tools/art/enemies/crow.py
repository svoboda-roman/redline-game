"""Crow: the flier re-dressed as a big black crow (replaces the drone's look; same sheet name 'drone', same
animations and cell, so Enemy3DRenderer loads it unchanged). Body 1.0 x 1.0, pivot = body centre, faces +X.

Black feathers, a heavy grey beak, one red eye (emissive G, flares in windup/dive), wings on two shoulder joints
that flap in the side-view plane (rot > 0 = wing up), a fanned tail, tucked claws.
States: hover (flapping loop), windup (rears back, wings high, beak open, eye flares), dive (wings swept back,
claws forward), hurt (knocked back, wings flailing, loose feathers), death (tumbles, falls on its back).
Visibility groups: beakA/beakB (closed/open), flare (eye glow), feat0/feat1 (loose feathers).
Run:  blender -b --factory-startup -P crow.py -- --out crow.blend   (then render_char.py + bake_char.py)
"""
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'common'))
import kit  # noqa: E402
from kit import part, cat, box, tube, ellipsoid, extrude2d, S, C, TAU  # noqa: E402

# Wing silhouette in its shoulder joint's XZ plane, pointing back along -X (rot 0 = spread straight back);
# the primaries end in separated "fingers".
WING = [(0.05, 0.02), (-0.04, 0.06), (-0.18, 0.075), (-0.33, 0.06), (-0.46, 0.02), (-0.55, -0.02),
        (-0.47, -0.035), (-0.53, -0.07), (-0.44, -0.07), (-0.48, -0.11), (-0.39, -0.10), (-0.41, -0.14),
        (-0.32, -0.12), (-0.31, -0.15), (-0.23, -0.12), (-0.14, -0.11), (-0.06, -0.08), (0.03, -0.04)]
COVERT = [(0.05, 0.02), (-0.04, 0.05), (-0.17, 0.06), (-0.27, 0.035), (-0.2, -0.03), (-0.08, -0.04), (0.03, -0.03)]


def build():
    kit.joint('root', None, (0, 0, 0))
    kit.joint('body', 'root', (0, 0, 0))
    B = 'body'
    part('torso', B, B, 'feather', ellipsoid((-0.02, 0, 0.0), (0.25, 0.12, 0.13), (0, -8, 0)))
    part('breast', B, B, 'feather', ellipsoid((0.1, 0, -0.03), (0.15, 0.115, 0.12)))
    part('back', B, B, 'plume', ellipsoid((-0.06, 0, 0.06), (0.18, 0.09, 0.06), (0, -6, 0)))
    # Head + beak + eye.
    kit.joint('head', B, (0.22, 0, 0.07))
    H = 'head'
    part('skull', H, H, 'feather', ellipsoid((0.04, 0, 0.02), (0.1, 0.085, 0.085)))
    part('ruff', H, H, 'plume', ellipsoid((-0.02, 0, -0.03), (0.08, 0.09, 0.07)))
    part('beak_top', H, H, 'beak', tube((0.11, 0, 0.03), (0.3, 0, -0.005), 0.042, 0.006, 12), group='beakA')
    part('beak_low', H, H, 'beak', tube((0.11, 0, 0.0), (0.27, 0, -0.02), 0.03, 0.005, 12), group='beakA')
    part('beak_top_o', H, H, 'beak', tube((0.11, 0, 0.035), (0.3, 0, 0.03), 0.042, 0.006, 12), group='beakB')
    part('beak_low_o', H, H, 'beak', tube((0.11, 0, -0.005), (0.25, 0, -0.08), 0.03, 0.005, 12), group='beakB')
    for y in (-0.072, 0.072):
        part('eye%d' % (y > 0), H, H, 'red', ellipsoid((0.085, y, 0.04), (0.024, 0.014, 0.022)))
    part('eyeflare', H, H, 'glow', ellipsoid((0.09, -0.08, 0.04), (0.032, 0.01, 0.028)), group='flare')
    # Tail: a fan of long feathers.
    kit.joint('tail', B, (-0.22, 0, 0.0))
    T = 'tail'
    part("tail", T, T, "feather", extrude2d([(0.03, 0.035), (-0.18, 0.05), (-0.33, 0.07), (-0.37, 0.03), (-0.35, -0.01), (-0.38, -0.05), (-0.3, -0.06), (-0.16, -0.035), (0.03, -0.035)], 0.0, 0.07), smooth=False)
    part("tail_mid", T, T, "plume", extrude2d([(0.0, 0.015), (-0.3, 0.03), (-0.33, 0.0), (0.0, -0.01)], -0.04, 0.01), smooth=False)
    # Legs and claws, tucked under the breast.
    kit.joint('legs', B, (0.04, 0, -0.1))
    L = 'legs'
    for y in (-0.04, 0.04):
        part('shank%d' % (y > 0), L, L, 'beak', tube((0, y, 0), (-0.02, y, -0.12), 0.016, 0.012, 8))
        part('toes%d' % (y > 0), L, L, 'beak', cat(tube((-0.02, y, -0.12), (0.06, y, -0.15), 0.011, 0.005, 6),
                                                    tube((-0.02, y, -0.12), (-0.08, y, -0.14), 0.01, 0.005, 6)))
    # Wings: the far one a touch higher and behind (depth), the near one toward the camera (-Y).
    for nm, y in (('F', 0.11), ('N', -0.12)):
        kit.joint('wing_' + nm, B, (0.05, y, 0.06))
        part('wing_' + nm, 'wing_' + nm, 'wing_' + nm, 'feather', extrude2d(WING, 0.0, 0.03), smooth=False)
        part('covert_' + nm, 'wing_' + nm, 'wing_' + nm, 'plume', extrude2d(COVERT, -0.02 if nm == 'N' else 0.02, 0.012), smooth=False)
    # Loose feathers (hurt, death).
    fl = [[((0.18, -0.2, 0.2), 30), ((-0.2, -0.2, 0.12), -50), ((0.05, -0.2, -0.22), 70)],
          [((-0.1, -0.2, 0.26), -20), ((0.26, -0.2, -0.05), 55), ((-0.28, -0.2, -0.12), 15)]]
    for k, st in enumerate(fl):
        part('feat%d' % k, B, B, 'plume', cat(*[box(c, (0.1, 0.012, 0.03), (0, -a, 0)) for c, a in st]),
             smooth=False, group='feat%d' % k)


def wings(up, lag=10.0, extra=None):
    """rot dict: near wing at <up> degrees (up > 0 = raised), far wing lagging a little."""
    r = {'wing_N': up, 'wing_F': up + lag}
    if extra:
        r.update(extra)
    return r


def hover(i, n):
    # 8 frames, one full wing beat: downstroke lifts the body, tail and head counter-sway.
    ph = TAU * i / n
    up = 22 + 58 * C(ph)
    return dict(dz=-0.05 * C(ph), lean=4 * S(ph), rot=wings(up, 12, {'tail': -6 * S(ph), 'head': 3 * S(ph)}), show=['beakA'])


def windup(i, n):
    # rears back, wings flung high, beak opens (caw), eye flares; last frame tips forward into the dive.
    k = [0.4, 0.8, 1.0, 1.0, 0.85][i]
    tip = [0, 0, 0, 0, 12][i]
    return dict(dz=0.08 * k, dx=-0.07 * k, lean=-22 * k + tip,
                rot=wings(40 + 50 * k, 8, {'head': -10 * k, 'tail': 12 * k, 'legs': -20 * k}),
                show=(['beakB'] if i >= 1 else ['beakA']) + (['flare'] if i >= 1 else []))


def dive(i, n):
    # stoop: wings swept back along the body, claws thrust forward, beak open.
    return dict(dz=-0.03 * i, dx=0.04 * i, lean=40 + 4 * i,
                rot=wings(-8 + 4 * i, 5, {'legs': -55, 'head': -12, 'tail': 6}), show=['beakB', 'flare'])


def hurt(i, n):
    k = [1.0, 1.1, 0.5, 0.15][i]
    return dict(dx=-0.10 * k, dz=0.03 * k, lean=-26 * k,
                rot=wings([75, -30, 50, 30][i], -25, {'head': 18 * k, 'tail': -15 * k}),
                show=['beakB' if i < 2 else 'beakA'] + (['feat%d' % (i % 2)] if i < 3 else []))


# Death: feathers burst, the crow tumbles and drops onto its back (dz -0.32 puts it on the floor of its 1.0 body
# when the renderer lands the corpse), bounces once and holds with the wings splayed limp.
DEATH = [dict(dx=-0.06, lean=-25, rot=wings(70, -30, {'head': 20}), show=['beakB', 'feat0']),
         dict(dx=-0.08, dz=-0.06, lean=-60, rot=wings(-20, 40, {'head': 30}), show=['beakB', 'feat1']),
         dict(dx=-0.10, dz=-0.16, lean=-120, rot=wings(40, -20, {'head': 25}), show=['beakB', 'feat0']),
         dict(dx=-0.12, dz=-0.26, lean=-170, rot=wings(10, 20, {'head': 20}), show=['beakB', 'feat1']),
         dict(dx=-0.14, dz=-0.32, lean=-195, rot=wings(-15, 25, {'head': 15, 'legs': 30}), show=['beakB']),
         dict(dx=-0.15, dz=-0.27, lean=-188, rot=wings(-5, 20, {'head': 12, 'legs': 30}), show=['beakB', 'feat0']),
         dict(dx=-0.15, dz=-0.32, lean=-192, rot=wings(-18, 25, {'head': 15, 'legs': 35}), show=['beakB'])]

ANIMS = [
    dict(name='hover', n=8, fps=12, loop=True, pose=hover),
    dict(name='windup', n=5, fps=10, loop=False, pose=windup),
    dict(name='dive', n=3, fps=12, loop=True, pose=dive),
    dict(name='hurt', n=4, fps=16, loop=False, pose=hurt),
    dict(name='death', n=7, fps=12, loop=False, pose=lambda i, n: DEATH[i]),
]

META = dict(cell_w=2.0, cell_h=2.0, pivot='centre', pad_bottom=1.0,
            pivot_note='flier: pivot = body centre = centre of the 1.0 x 1.0 collision body (cell centre); sprites face right',
            emissive_encoding='R = attack glow: eye flare in windup/dive, G = red eyes, B = unused; A = 255 where any mask is set')

if __name__ == '__main__':
    a = kit.args()
    kit.init()
    build()
    kit.animate(ANIMS, META, a['out'])
