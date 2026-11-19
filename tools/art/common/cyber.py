"""Cyberpunk half-human / half-machine parts for the enemy kit (Blender side, used by tools/art/enemies/*.py).

Conventions as kit.py: the character faces +X, up is +Z, the camera looks along +Y, so -Y is the near
(visible) side. A "half face" is human skin with a chrome skull plate over its near, upper half and a red
cyber eye (material 'eye' = emissive G); the far half stays human.
"""
import math
import kit
from kit import part, cat, box, tube, ellipsoid, shell, ecyl


def half_face(H, r=0.085, c=(0.01, 0.0, 0.04), mohawk=True, jaw=True, mohawk_mat='strip'):
    """Skin skull + chrome plate over the near upper half + red eye + mohawk ridge + cables at the nape."""
    cx, cy, cz = c
    part('skull', H, 'head', 'skin', ellipsoid(c, (r, r * 0.94, r * 1.05)))
    # chrome plate: shell around the vertical axis over the near side (angles toward -Y), top half of the head
    part('plate', H, 'head', 'chrome', shell((cx, cy), -175, -5,
                                             [(cz - r * 0.05, r * 1.04, r * 0.99), (cz + r * 0.55, r * 0.95, r * 0.9),
                                              (cz + r * 0.95, r * 0.45, r * 0.42)], 14))
    part('plate_rim', H, 'head', 'cable', shell((cx, cy), -175, -5,
                                                   [(cz - r * 0.12, r * 1.06, r * 1.01), (cz - r * 0.02, r * 1.06, r * 1.01)], 14))
    part('eye', H, 'head', 'eye', ellipsoid((cx + r * 0.88, cy - r * 0.4, cz + r * 0.3), (r * 0.26, r * 0.36, r * 0.3)))
    part('eye_human', H, 'head', 'cable', box((cx + r * 0.93, cy + r * 0.38, cz + r * 0.28), (r * 0.1, r * 0.22, r * 0.1)))
    if jaw:
        part('jaw', H, 'head', 'chrome', box((cx + r * 0.62, cy - r * 0.2, cz - r * 0.62), (r * 0.6, r * 0.75, r * 0.32)), bevel=0.006)
    if mohawk:
        part('mohawk', H, 'head', mohawk_mat, box((cx - r * 0.15, cy + r * 0.05, cz + r * 1.08), (r * 1.5, r * 0.22, r * 0.36)), bevel=0.004)
    cables = [tube((cx - r * 0.8, cy + dy, cz + r * 0.1), (cx - r * 1.4, cy + dy, cz - r * 1.6), 0.011, 0.011, 6)
              for dy in (-r * 0.25, r * 0.05)]
    part('nape_cables', H, 'head', 'cable', cat(*cables))


def robo_arm(side, r=(0.038, 0.03, 0.032, 0.026), hand=(0.06, 0.05, 0.07)):
    """A machine arm on `side`: chrome bones, gunmetal joints, a piston along the forearm, cable to the shoulder."""
    rp = kit.HUMANP
    s = side
    part('upper_' + s, 'sh_' + s, 'sh_' + s, 'chrome', cat(ellipsoid((0, 0, 0), (r[0] + 0.012,) * 3),
                                                           tube((0, 0, 0), (0, 0, -rp['upper']), r[0], r[1], 10)))
    part('fore_' + s, 'el_' + s, 'el_' + s, 'chrome', cat(ellipsoid((0, 0, 0), (r[2] + 0.012,) * 3),
                                                          tube((0, 0, 0), (0, 0, -rp['fore']), r[2], r[3], 10)))
    part('elbow_' + s, 'el_' + s, 'el_' + s, 'gunmetal', ecyl((0, 0), -0.025, 0.025, r[2] + 0.018, r[2] + 0.018, 12))
    part('piston_' + s, 'el_' + s, 'el_' + s, 'cyber', cat(tube((r[2] + 0.01, 0, -0.02), (r[2] + 0.01, 0, -rp['fore'] * 0.85), 0.012, 0.012, 6),
                                                           tube((r[2] + 0.01, 0, -0.02), (r[2] + 0.01, 0, -rp['fore'] * 0.45), 0.018, 0.018, 6)))
    part('arm_cable_' + s, 'sh_' + s, 'sh_' + s, 'cable', tube((-r[0], 0, 0.02), (-r[0] - 0.01, 0, -rp['upper'] * 0.9), 0.01, 0.01, 6))
    part('hand_' + s, 'wr_' + s, 'wr_' + s, 'gunmetal', cat(box((0.01, 0, -hand[2] / 2), hand),
                                                           box((0.03, 0, -hand[2] - 0.02), (hand[0] * 0.5, hand[1] * 0.9, 0.04))), bevel=0.008)


def human_arm(side, sleeve='suit', r=(0.05, 0.042, 0.045, 0.038), hand=(0.065, 0.055, 0.07)):
    """A human arm: sleeve on the upper arm, bare skin forearm and hand."""
    kit.arms({side: (sleeve, 'skin')}, r=r, hand=hand, hand_mat='skin', sides=side)


def spine_cables(frame='chest_geo', jnt='chest', x=-0.13, z0=-0.05, z1=0.22, n=3, spread=0.035):
    """A bundle of cables running up the back into the skull."""
    part('spine_cables', frame, jnt, 'cable',
         cat(*[tube((x, (k - (n - 1) / 2) * spread, z0), (x - 0.02, (k - (n - 1) / 2) * spread * 0.6, z1), 0.012, 0.01, 6)
               for k in range(n)]))


def chest_port(frame='chest_geo', jnt='chest', at=(0.13, -0.05, 0.05)):
    """A small glowing implant port on the chest (light strip, emissive B)."""
    part('port', frame, jnt, 'gunmetal', ecyl((at[0], at[1]), at[2] - 0.03, at[2] + 0.03, 0.035, 0.035, 12))
    part('port_light', frame, jnt, 'strip', box((at[0] + 0.03, at[1], at[2]), (0.01, 0.04, 0.02)))
