# Procedural, keyposed animation for the Tech Ronin (Blender 5.2). Imported by build_ronin.py.
#
# The Mixamo Great Sword pack moves like a heavy two-hander (blade held out like a pointer, hunched run, slow
# swings). The ronin is a fast one-handed katana fighter (REPLACED / Katana ZERO / Dead Cells), so the locomotion,
# the airborne states, the landing and the slashes are authored here as poses on the X Bot armature:
#   * a pose is a dict of numbers (hips offset and rotation, torso lean and twist, ankle targets, arm angles,
#     blade direction), solved onto the bones: legs by two-bone IK on the ankle target (knee pole forward),
#     arms by direction, the right hand rolled so the katana points along the requested blade direction with
#     the wrist as straight as possible, the fingers taken from a gripping clip frame;
#   * loops (idle, run, sprint, fall) are functions of the cycle phase; one-shots interpolate keyposes with a
#     per-segment ease (the slashes: 1 anticipation frame, a 1-frame smear, impact at frame 2, follow-through).
# Coordinates: armature space, centimetres, +Y up, +Z character front, +X character left (the right, sword side
# is -X and faces the game camera). Angles in degrees.
import math
from mathutils import Vector, Matrix, Quaternion

FPS = 30
# Gait timing (per foot, fraction of the cycle on the ground). Short contact = long flight: at the sim's speeds
# (9 to 22 u/s for a 1.5 u tall figure) a human contact share would spin the legs far too fast once the playback
# rate is matched to the stride, so the runner bounds.
RUN_FRAMES, RUN_CONTACT = 20, 0.18
SPRINT_FRAMES, SPRINT_CONTACT = 16, 0.14
IDLE_FRAMES, FALL_FRAMES = 40, 30
SLASH_FRAMES = 10            # baked 1:1 at 30 fps: impact at frame 2 (0.067 s; the sim's strike is live 0 to 0.15 s)
LAND_FRAMES = 6
FOREARM_BIAS_DEG = 8.0       # below this forearm-to-blade angle the forearm plane is biased (see Rig.pose)


def _lerp(a, b, t): return a + (b - a) * t


def _ease(kind, x):
    if kind == "lin": return x
    if kind == "out": return 1 - (1 - x) ** 2          # fast start, settles (follow-through)
    if kind == "in": return x * x                      # slow start, accelerates (into the strike)
    if kind == "snap": return 1 - (1 - x) ** 3
    return x * x * (3 - 2 * x)                        # smooth


def _spline(keys, u):
    """Periodic Catmull-Rom through (u, value) keys on [0, 1); keys sorted, first u = 0."""
    n = len(keys)
    u %= 1.0
    i = max(k for k in range(n) if keys[k][0] <= u)
    u0 = keys[i][0]
    u1 = keys[(i + 1) % n][0] + (1.0 if i + 1 == n else 0.0)
    t = (u - u0) / max(u1 - u0, 1e-6)
    p0, p1, p2, p3 = (keys[(i + d) % n][1] for d in (-1, 0, 1, 2))
    return 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3)


def _spline_open(keys, w):
    """Catmull-Rom through (w, value) keys on [0, 1] (clamped ends)."""
    w = min(max(w, 0.0), 1.0)
    n = len(keys)
    i = max(k for k in range(n - 1) if keys[k][0] <= w) if w < keys[-1][0] else n - 2
    t = (w - keys[i][0]) / max(keys[i + 1][0] - keys[i][0], 1e-6)
    p1, p2 = keys[i][1], keys[i + 1][1]
    p0 = keys[i - 1][1] if i > 0 else p1
    p3 = keys[i + 2][1] if i + 2 < n else p2
    return 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3)


def sag(a, x=0.0):
    """Direction of a down-hanging limb swung a degrees forward (negative = back), x = lateral (+ = char left)."""
    r = math.radians(a)
    return Vector((x, -math.cos(r), math.sin(r))).normalized()


def blade_dir(beta, x=0.0):
    """Blade direction: beta 0 = forward, 90 = straight down, 180 = backward, 270 / -90 = straight up
    (so 90..180 = back and down, the trailing carry)."""
    r = math.radians(beta)
    return Vector((x, -math.sin(r), math.cos(r))).normalized()


# Neutral pose (a relaxed ready stance). Ankle z/y: armature z, height above the standing ankle height.
BASE = dict(
    hy=0.0, hz=0.0, hpitch=0.0, hyaw=0.0, hroll=0.0,
    sp=0.0, side=0.0, twist=0.0, neck=0.0, head=0.0,
    Lz=0.0, Ly=0.0, Lx=9.0, Lp=0.0,
    Rz=0.0, Ry=0.0, Rx=-9.0, Rp=0.0,
    LAu=10.0, LAux=0.2, LAf=20.0, LAfx=0.1,
    RAu=10.0, RAux=-0.2, RAf=20.0, RAfx=-0.1,
    blade=120.0, bladex=-0.1,
)


class Rig:
    def __init__(self, bpy, arm, B, rest, katana, fist_act):
        self.bpy, self.arm, self.B = bpy, arm, B
        self.PB = arm.pose.bones
        for pb in self.PB: pb.rotation_mode = 'QUATERNION'
        self.REST = rest
        H = lambda n: arm.data.bones[B(n)].head_local.copy()
        self.hips_rest = H("Hips")
        self.thigh = (H("LeftLeg") - H("LeftUpLeg")).length
        self.shin = (H("LeftFoot") - H("LeftLeg")).length
        self.ankle_h = H("LeftFoot").y
        self.hipjoint_y = H("LeftUpLeg").y
        # Katana axes in the right hand's local frame (from the mesh at rest: ring 0 = blade base, ring 12 = tip).
        kv = [v.co.copy() for v in katana.data.vertices]
        base = sum(kv[0:5], Vector()) / 5
        tip = sum(kv[60:65], Vector()) / 5
        rinv = rest[B("RightHand")].to_3x3().inverted()
        self.bl = (rinv @ (tip - base)).normalized()
        e = rinv @ (kv[0] - base)
        self.edge = (e - self.bl * e.dot(self.bl)).normalized()
        # Gripping fingers: local rotations of every finger bone from a frame of the source idle (both fists closed).
        arm.animation_data.action = fist_act
        if len(fist_act.slots): arm.animation_data.action_slot = fist_act.slots[0]
        bpy.context.scene.frame_set(int(fist_act.frame_range[0]) + 10)
        self.fingers = {pb.name: pb.rotation_quaternion.copy() for pb in self.PB
                        if any(k in pb.name for k in ("Thumb", "Index", "Middle", "Ring", "Pinky"))}
        arm.animation_data.action = None
        self.log = []

    def upd(self): self.bpy.context.view_layer.update()

    def aim(self, n, d):
        pb = self.PB[self.B(n)]
        cur = (pb.tail - pb.head).normalized()
        q = cur.rotation_difference(d.normalized())
        pb.matrix = Matrix.Translation(pb.head) @ q.to_matrix().to_4x4() @ Matrix.Translation(-pb.head) @ pb.matrix
        self.upd()

    def roll(self, n, deg):
        pb = self.PB[self.B(n)]
        ax = (pb.tail - pb.head).normalized()
        pb.matrix = Matrix.Translation(pb.head) @ Matrix.Rotation(math.radians(deg), 4, ax) @ Matrix.Translation(-pb.head) @ pb.matrix
        self.upd()

    def leg(self, side, z, y, x, pitch):
        """Two-bone IK: ankle to (x, ankle_h + y, z), knee pole forward; foot pitched (+ = toes down)."""
        up = self.PB[self.B(side + "UpLeg")]
        P = up.head.copy()
        A = Vector((x, self.ankle_h + y, z))
        a, b = self.thigh, self.shin
        d = A - P
        dist = d.length
        u = d.normalized()
        if dist >= (a + b) * 0.999:
            self.log.append("reach")
            thigh_dir, knee_dir = u, u
        else:
            dist = max(dist, abs(a - b) + 1e-3)
            ca = (a * a + dist * dist - b * b) / (2 * a * dist)
            sa = math.sqrt(max(0.0, 1 - ca * ca))
            pole = Vector((0, 0, 1))
            n = (pole - u * pole.dot(u))
            n = n.normalized() if n.length > 1e-4 else Vector((0, 0, 1))
            thigh_dir = u * ca + n * sa
            K = P + thigh_dir * a
            knee_dir = (A - K).normalized()
        self.aim(side + "UpLeg", thigh_dir)
        self.aim(side + "Leg", knee_dir)
        fb = self.arm.data.bones[self.B(side + "Foot")]
        fdir = (fb.tail_local - fb.head_local).normalized()
        self.aim(side + "Foot", Matrix.Rotation(math.radians(pitch), 3, 'X') @ fdir)
        tb = self.arm.data.bones.get(self.B(side + "ToeBase"))
        if tb:
            tdir = (tb.tail_local - tb.head_local).normalized()
            # toes stay flat while the ball is on the ground, else follow the foot
            self.aim(side + "ToeBase", tdir if y < 8 and pitch > 0 else Matrix.Rotation(math.radians(pitch * 0.6), 3, 'X') @ tdir)

    def hand_blade(self, D):
        """Rotates the right hand so the katana points along D; of all rolls about D the one whose hand axis follows
        the forearm best (straightest wrist) wins."""
        pb = self.PB[self.B("RightHand")]
        fa = self.PB[self.B("RightForeArm")]
        fdir = (fa.tail - fa.head).normalized()
        D = D.normalized()
        bl, e = self.bl, self.edge
        F_loc = Matrix((bl, e, bl.cross(e))).transposed()
        ref = Vector((0, 1, 0)) if abs(D.y) < 0.9 else Vector((0, 0, 1))
        best, bestM = -9, None
        for k in range(48):
            th = 2 * math.pi * k / 48
            e0 = (ref - D * ref.dot(D)).normalized()
            E = Matrix.Rotation(th, 3, D) @ e0
            M = Matrix((D, E, D.cross(E))).transposed() @ F_loc.transposed()
            yax = (M @ Vector((0, 1, 0))).normalized()
            s = yax.dot(fdir)
            if s > best: best, bestM = s, M
        pb.matrix = Matrix.Translation(pb.head) @ bestM.to_4x4()
        self.upd()

    def pose(self, P):
        B, PB = self.B, self.PB
        for pb in PB:
            pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0)
        for n, q in self.fingers.items(): PB[n].rotation_quaternion = q
        self.upd()
        hips = PB[B("Hips")]
        rot = (Matrix.Rotation(math.radians(P["hyaw"]), 4, 'Y') @ Matrix.Rotation(math.radians(P["hpitch"]), 4, 'X')
               @ Matrix.Rotation(math.radians(P["hroll"]), 4, 'Z'))
        m = rot @ self.REST[B("Hips")].to_3x3().to_4x4()
        m.translation = self.hips_rest + Vector((0, P["hy"], P["hz"]))
        hips.matrix = m
        self.upd()
        sp, side = P["sp"], P["side"]
        for n, f in (("Spine", 0.55), ("Spine1", 0.8), ("Spine2", 1.0)):
            r = math.radians(sp * f)
            self.aim(n, Vector((math.radians(side * f), math.cos(r), math.sin(r))))
        self.roll("Spine1", P["twist"] * 0.5)
        self.roll("Spine2", P["twist"] * 0.5)
        r = math.radians(sp * 0.5 + P["neck"])
        self.aim("Neck", Vector((0, math.cos(r), math.sin(r))))
        r = math.radians(P["head"])
        self.aim("Head", Vector((0, math.cos(r), math.sin(r))))
        self.aim("LeftArm", sag(P["LAu"], P["LAux"]))
        self.aim("LeftForeArm", sag(P["LAf"], P["LAfx"]))
        self.aim("LeftHand", sag(P["LAf"] + 10, P["LAfx"]))
        self.aim("RightArm", sag(P["RAu"], P["RAux"]))
        # Forearm from the blade: in the fist grip the katana sits GRIP_ANGLE off the hand axis, so the forearm is
        # put that far from the blade, toward the authored forearm direction. The wrist then stays nearly straight,
        # which Unity's humanoid retarget needs (it clamps wrist muscles; a bent wrist turned the carry vertical).
        D = blade_dir(P["blade"], P["bladex"])
        Pf = sag(P["RAf"], P["RAfx"])
        n = Pf - D * Pf.dot(D)
        # Forearm nearly parallel to the blade (the sprint's trailing carry at the back of the arm swing): the plane
        # above is noise there, the forearm swung to an arbitrary side and Unity's wrist clamp stood the blade up.
        # Bias toward the blade's "down/forward" side (beta - 90), weight 1 when parallel, 0 from FOREARM_BIAS_DEG.
        ang = math.degrees(math.acos(max(-1.0, min(1.0, Pf.normalized().dot(D)))))
        w = max(0.0, 1.0 - ang / FOREARM_BIAS_DEG)
        if w > 0.0:
            S = blade_dir(P["blade"] - 90.0, P["bladex"])
            n = n + (S - D * S.dot(D)).normalized() * w
        if n.length < 1e-3: n = Vector((0, 1, 0)) - D * D.y
        n.normalize()
        ga = math.acos(max(-1.0, min(1.0, self.bl.dot(Vector((0, 1, 0))))))
        self.aim("RightForeArm", D * math.cos(ga) + n * math.sin(ga))
        self.hand_blade(D)
        self.leg("Left", P["Lz"], P["Ly"], P["Lx"], P["Lp"])
        self.leg("Right", P["Rz"], P["Ry"], P["Rx"], P["Rp"])
        return {pb.name: (pb.location.copy(), pb.rotation_quaternion.copy()) for pb in PB}


def P(**kw):
    d = dict(BASE)
    d.update(kw)
    return d


def mix(a, b, t):
    return {k: _lerp(a[k], b[k], t) for k in a}


# ---------------------------------------------------------------- loops

def idle_pose(phase):
    """Light ready stance: far (left) foot forward, near (right) foot back, knees soft, sword low in front with the
    tip toward the ground, free hand loose. Breathing: 1 cm hips, arms follow."""
    b = math.sin(2 * math.pi * phase)
    return P(hy=-6 + 0.9 * b, hpitch=4, hyaw=-10, sp=6 - 1.0 * b, twist=-8, neck=-4, head=2,
             Lz=13, Ly=0, Lx=12, Lp=0, Rz=-15, Ry=0, Rx=-12, Rp=0,
             LAu=8 + 2 * b, LAux=0.28, LAf=28 + 3 * b, LAfx=0.15,
             RAu=12 + 1.5 * b, RAux=-0.32, RAf=58 + 2 * b, RAfx=-0.05,
             blade=58 - 1.5 * b, bladex=-0.12)


def gait(phase, contact, zf, zb, swing_z, swing_y, pitch_keys):
    """Ankle (z, y, pitch) of one foot at its own phase (0 = touchdown)."""
    u = phase % 1.0
    if u < contact:
        w = u / contact
        z = _lerp(zf, zb, w)
        y = max(0.0, (w - 0.55) / 0.45) ** 1.5 * 7.0          # heel peels off late in the contact
        return z, y, _lerp(pitch_keys[0], pitch_keys[1], max(0.0, (w - 0.4) / 0.6))
    w = (u - contact) / (1 - contact)
    return _spline_open(swing_z, w), _spline_open(swing_y, w), _spline_open(pitch_keys[2], w)


def run_pose(phase, sprint=False):
    """Athletic run (sprint = longer stride, more lean, arms further back). Right leg touches down at phase 0, the
    left at 0.5. Hips are lowest at mid-stance and highest mid-flight."""
    if sprint:
        c, zf, zb, lean, hy0, bob, pump = SPRINT_CONTACT, 33.0, -37.0, 32.0, -11.0, 4.0, 62.0
        sz = [(0, zb), (0.15, zb - 14), (0.38, -22), (0.62, 22), (0.82, zf + 12), (1.0, zf)]
        sy = [(0, 7), (0.15, 26), (0.38, 46), (0.62, 34), (0.82, 14), (1.0, 0)]
    else:
        c, zf, zb, lean, hy0, bob, pump = RUN_CONTACT, 28.0, -32.0, 21.0, -7.0, 3.0, 48.0
        sz = [(0, zb), (0.15, zb - 10), (0.38, -18), (0.62, 16), (0.82, zf + 8), (1.0, zf)]
        sy = [(0, 7), (0.15, 20), (0.38, 36), (0.62, 26), (0.82, 11), (1.0, 0)]
    pk = (0.0, 38.0, [(0, 40), (0.25, 48), (0.5, 20), (0.75, -12), (1.0, 0)])
    Rz, Ry, Rp = gait(phase, c, zf, zb, sz, sy, pk)
    Lz, Ly, Lp = gait(phase + 0.5, c, zf, zb, sz, sy, pk)
    mid = c * 0.5
    b = -math.cos(2 * math.pi * 2 * (phase - mid))     # -1 mid-stance, +1 mid-flight
    yaw = 8 * math.cos(2 * math.pi * (phase - 0.05))   # pelvis turns with the forward leg
    sw = math.cos(2 * math.pi * (phase - 0.9))        # free arm forward while the right leg reaches
    arm_back = -44 if sprint else -32
    return P(hy=hy0 + bob * b, hz=0, hpitch=lean * 0.55, hyaw=yaw, sp=lean, twist=-1.6 * yaw,
             neck=-lean * 0.55, head=-lean * 0.15 + 4,
             Lz=Lz, Ly=Ly, Lx=7, Lp=Lp, Rz=Rz, Ry=Ry, Rx=-7, Rp=Rp,
             LAu=pump * sw + lean * 0.3, LAux=0.18, LAf=pump * sw + 85 + lean * 0.3, LAfx=0.12,
             RAu=arm_back + 7 * sw, RAux=-0.3, RAf=arm_back - 12 + 9 * sw, RAfx=-0.25,
             blade=(158 if sprint else 152) + 4 * sw, bladex=-0.2)


def fall_pose(phase):
    s = math.sin(2 * math.pi * phase)
    c = math.cos(2 * math.pi * phase)
    return P(hy=0, hpitch=-4, sp=2, twist=-10, neck=-2, head=-8,
             Lz=16 + 3 * s, Ly=26 + 3 * c, Lx=11, Lp=-10, Rz=-12 - 3 * s, Ry=12 - 2 * c, Rx=-10, Rp=30,
             LAu=125 + 8 * s, LAux=0.55, LAf=150 + 8 * s, LAfx=0.35,
             RAu=-38 - 5 * s, RAux=-0.45, RAf=-20 - 5 * s, RAfx=-0.35,
             blade=150 + 5 * c, bladex=-0.25)


# ---------------------------------------------------------------- keyposes

JUMP_KEYS = [  # (frame from 0, pose, ease into this key)
    (0, P(hy=-4, hpitch=6, sp=10, twist=-6, neck=-4, head=-2,
          Lz=16, Ly=22, Lx=10, Lp=-10, Rz=-14, Ry=0, Rx=-9, Rp=48,
          LAu=150, LAux=0.3, LAf=165, LAfx=0.2, RAu=-50, RAux=-0.35, RAf=-35, RAfx=-0.3, blade=155, bladex=-0.2), None),
    (4, P(hy=0, hpitch=0, sp=4, twist=-8, neck=-2, head=-4,
          Lz=20, Ly=36, Lx=10, Lp=-12, Rz=-6, Ry=18, Rx=-9, Rp=30,
          LAu=135, LAux=0.4, LAf=150, LAfx=0.3, RAu=-42, RAux=-0.4, RAf=-28, RAfx=-0.3, blade=152, bladex=-0.25), "out"),
    (12, P(hy=0, hpitch=-2, sp=2, twist=-8, neck=-2, head=-6,
           Lz=18, Ly=40, Lx=10, Lp=-12, Rz=2, Ry=34, Rx=-9, Rp=10,
           LAu=120, LAux=0.45, LAf=140, LAfx=0.35, RAu=-36, RAux=-0.4, RAf=-22, RAfx=-0.3, blade=150, bladex=-0.25), "smooth"),
]

_ready = idle_pose(0.0)
LAND_KEYS = [
    (0, P(hy=-24, hz=2, hpitch=20, sp=32, twist=-8, neck=-14, head=4,
          Lz=18, Ly=0, Lx=13, Lp=0, Rz=-14, Ry=0, Rx=-13, Rp=10,
          LAu=55, LAux=0.45, LAf=70, LAfx=0.3, RAu=-55, RAux=-0.4, RAf=-40, RAfx=-0.3, blade=158, bladex=-0.2), None),
    (1, P(hy=-28, hz=2, hpitch=22, sp=36, twist=-8, neck=-16, head=6,
          Lz=18, Ly=0, Lx=13, Lp=0, Rz=-14, Ry=0, Rx=-13, Rp=10,
          LAu=48, LAux=0.45, LAf=62, LAfx=0.3, RAu=-58, RAux=-0.4, RAf=-44, RAfx=-0.3, blade=160, bladex=-0.2), "out"),
    (3, P(hy=-14, hz=1, hpitch=10, sp=18, twist=-8, neck=-8, head=3,
          Lz=15, Ly=0, Lx=12, Lp=0, Rz=-15, Ry=0, Rx=-12, Rp=4,
          LAu=25, LAux=0.35, LAf=45, LAfx=0.2, RAu=-10, RAux=-0.35, RAf=20, RAfx=-0.2, blade=110, bladex=-0.15), "smooth"),
    (LAND_FRAMES - 1, _ready, "smooth"),
]

# Slash keyposes. Frame 0 = anticipation (coiled), 2 = impact (blade fully through the strike line), 4 = follow-
# through, 9 = back to the ready stance. 0 -> 2 is linear so the in-between frame 1 is a true mid-swing (the baker
# smears it); 2 -> 4 eases out.
_stance = dict(Lz=20, Ly=0, Lx=12, Lp=0, Rz=-18, Ry=0, Rx=-12, Rp=8)
ATTACK1_KEYS = [  # overhead diagonal cut, high back to low front
    (0, P(hy=-7, hz=-3, hpitch=0, hyaw=-14, sp=-4, twist=-26, neck=2, head=4, **_stance,
          LAu=70, LAux=0.3, LAf=95, LAfx=0.2, RAu=200, RAux=-0.25, RAf=160, RAfx=-0.2, blade=195, bladex=-0.2), None),
    (2, P(hy=-14, hz=9, hpitch=16, hyaw=8, sp=26, twist=22, neck=-12, head=2,
          Lz=34, Ly=0, Lx=12, Lp=0, Rz=-26, Ry=0, Rx=-12, Rp=30,
          LAu=-40, LAux=0.3, LAf=-15, LAfx=0.2, RAu=82, RAux=-0.12, RAf=78, RAfx=-0.05, blade=388, bladex=0.0), "lin"),
    (4, P(hy=-15, hz=9, hpitch=18, hyaw=10, sp=30, twist=30, neck=-14, head=2,
          Lz=34, Ly=0, Lx=12, Lp=0, Rz=-26, Ry=0, Rx=-12, Rp=30,
          LAu=-45, LAux=0.3, LAf=-20, LAfx=0.2, RAu=35, RAux=0.25, RAf=15, RAfx=0.3, blade=472, bladex=0.45), "out"),
    (SLASH_FRAMES - 1, dict(_ready, blade=_ready["blade"] + 360), "smooth"),
]
ATTACK2_KEYS = [  # rising cut, low across the body to high front, finishing overhead
    (0, P(hy=-13, hz=2, hpitch=14, hyaw=6, sp=22, twist=26, neck=-10, head=2, **_stance,
          LAu=-30, LAux=0.3, LAf=-5, LAfx=0.2, RAu=22, RAux=0.28, RAf=5, RAfx=0.3, blade=125, bladex=0.35), None),
    (2, P(hy=-4, hz=8, hpitch=4, hyaw=-6, sp=6, twist=-24, neck=-2, head=6,
          Lz=30, Ly=0, Lx=12, Lp=0, Rz=-22, Ry=0, Rx=-12, Rp=25,
          LAu=-50, LAux=0.35, LAf=-30, LAfx=0.25, RAu=148, RAux=-0.2, RAf=160, RAfx=-0.15, blade=-48, bladex=-0.05), "lin"),
    (4, P(hy=-3, hz=7, hpitch=2, hyaw=-8, sp=2, twist=-30, neck=0, head=8,
          Lz=30, Ly=0, Lx=12, Lp=0, Rz=-22, Ry=0, Rx=-12, Rp=25,
          LAu=-45, LAux=0.35, LAf=-25, LAfx=0.25, RAu=172, RAux=-0.25, RAf=188, RAfx=-0.2, blade=-112, bladex=-0.1), "out"),
    (SLASH_FRAMES - 1, dict(_ready, blade=_ready["blade"] - 360), "smooth"),   # lowers behind the back
]
_tuck = dict(Lz=20, Ly=38, Lx=10, Lp=-12, Rz=-2, Ry=30, Rx=-9, Rp=12)
JUMPATTACK_KEYS = [  # wide horizontal cut in the air, legs tucked
    (0, P(hy=0, hpitch=-4, hyaw=-10, sp=0, twist=-32, neck=0, head=-4, **_tuck,
          LAu=95, LAux=0.4, LAf=110, LAfx=0.3, RAu=195, RAux=-0.3, RAf=165, RAfx=-0.25, blade=200, bladex=-0.2), None),
    (2, P(hy=0, hpitch=8, hyaw=10, sp=14, twist=26, neck=-8, head=-2, **_tuck,
          LAu=-35, LAux=0.4, LAf=-15, LAfx=0.3, RAu=88, RAux=-0.1, RAf=84, RAfx=0.0, blade=382, bladex=0.05), "lin"),
    (4, P(hy=0, hpitch=10, hyaw=12, sp=18, twist=34, neck=-10, head=-2, **_tuck,
          LAu=-40, LAux=0.4, LAf=-20, LAfx=0.3, RAu=40, RAux=0.3, RAf=20, RAfx=0.35, blade=455, bladex=0.45), "out"),
    (SLASH_FRAMES - 1, dict(fall_pose(0.0), blade=fall_pose(0.0)["blade"] + 360), "smooth"),
]

UPPER = ("hpitch", "sp", "side", "twist", "neck", "head", "LAu", "LAux", "LAf", "LAfx", "RAu", "RAux", "RAf", "RAfx", "blade", "bladex")


def sample_keys(keys, f):
    for i in range(len(keys) - 1):
        f0, a, _ = keys[i]
        f1, b, ease = keys[i + 1]
        if f <= f1:
            return mix(a, b, _ease(ease, (f - f0) / max(f1 - f0, 1e-6)))
    return dict(keys[-1][1])


def build(bpy, arm, B, rest, katana, fist_act, make_action, log):
    """Builds every procedural action; returns {name: action}. make_action(name, [(frame, pose)])."""
    rig = Rig(bpy, arm, B, rest, katana, fist_act)
    out = {}

    def loop(name, frames, fn):
        keys = [(1 + f, rig.pose(fn(f / frames))) for f in range(frames + 1)]
        out[name] = make_action(name, keys)

    def oneshot(name, frames, fn):
        out[name] = make_action(name, [(1 + f, rig.pose(fn(f))) for f in range(frames)])

    loop("idle", IDLE_FRAMES, idle_pose)
    loop("run", RUN_FRAMES, lambda p: run_pose(p, False))
    loop("sprint", SPRINT_FRAMES, lambda p: run_pose(p, True))
    loop("fall", FALL_FRAMES, fall_pose)
    oneshot("jump", 13, lambda f: sample_keys(JUMP_KEYS, f))
    oneshot("land", LAND_FRAMES, lambda f: sample_keys(LAND_KEYS, f))
    oneshot("attack_1", SLASH_FRAMES, lambda f: sample_keys(ATTACK1_KEYS, f))
    oneshot("attack_2", SLASH_FRAMES, lambda f: sample_keys(ATTACK2_KEYS, f))
    oneshot("jump_attack", SLASH_FRAMES, lambda f: sample_keys(JUMPATTACK_KEYS, f))

    def run_slash(keys):
        def fn(f):
            legs = run_pose(f / RUN_FRAMES, False)
            up = sample_keys(keys, f)
            # the follow-through settles into the run carry, not the standing stance
            if f > 4:
                tgt = dict(legs)
                b4 = sample_keys(keys, 4)["blade"]
                tgt["blade"] += 360.0 * round((b4 - tgt["blade"]) / 360.0)   # settle along the short way round
                up = mix(up, tgt, _ease("smooth", (f - 4) / (SLASH_FRAMES - 5)))
            d = dict(legs)
            for k in UPPER: d[k] = up[k]
            d["hpitch"] = max(d["hpitch"], legs["hpitch"])
            return d
        return fn

    oneshot("run_attack_1", SLASH_FRAMES, run_slash(ATTACK1_KEYS))
    oneshot("run_attack_2", SLASH_FRAMES, run_slash(ATTACK2_KEYS))
    reach = sum(1 for m in rig.log if m == "reach")
    log("procedural clips", sorted(out), "ik reach clamps", reach, "thigh", round(rig.thigh, 1), "shin", round(rig.shin, 1),
        "ankle_h", round(rig.ankle_h, 1), "blade local", tuple(round(v, 2) for v in rig.bl))
    return out
