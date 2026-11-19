using UnityEngine;

namespace Redline.Game.Ronin
{
    /// <summary>A capsule (segment A-B plus radius) that spring-bone joints are pushed out of, e.g. a thigh.</summary>
    [System.Serializable]
    public struct SpringCapsule
    {
        /// <summary>Segment start (usually a bone, e.g. LeftUpLeg).</summary>
        public Transform A;
        /// <summary>Segment end (usually the child bone, e.g. LeftLeg).</summary>
        public Transform B;
        /// <summary>Radius in world units.</summary>
        public float Radius;
    }

    /// <summary>
    /// Allocation-free Verlet spring chain for secondary motion (coat tails, sash end). Put it on the first bone
    /// of a chain; the chain follows the first child of each bone down to <see cref="maxJoints"/>. Each joint's
    /// tail is pulled toward its animated rest direction (<see cref="stiffness"/>), keeps momentum (damped by
    /// <see cref="drag"/>), falls with gravity and is pushed out of capsules. It reacts to world-space motion of
    /// the character, so moving the character makes the coat stream behind it.
    /// </summary>
    /// <remarks>
    /// Not self-updating: the owner calls <see cref="Simulate"/> once per frame after the animation is evaluated
    /// (<see cref="RoninView"/> does). Fixed 120 Hz sub-steps keep it frame-rate independent.
    /// Call <see cref="ResetState"/> after a teleport.
    /// </remarks>
    /// <example>
    /// <code>
    /// var tail = coatRootBone.gameObject.AddComponent&lt;SpringBone&gt;();
    /// tail.Configure(stiffness: 70f, drag: 0.06f, gravityScale: 0.8f, jointRadius: 0.03f);
    /// tail.Initialize(legCapsules);           // once, with the model in its rest or first animated pose
    /// // every frame, after the Animator/PlayableGraph evaluated:
    /// tail.Simulate(Time.deltaTime);
    /// </code>
    /// </example>
    public sealed class SpringBone : MonoBehaviour
    {
        private const float Step = 1f / 120f;
        private const int MaxSteps = 8;

        [SerializeField, Tooltip("Pull toward the rest direction, 1/s^2.")] private float stiffness = 70f;
        [SerializeField, Range(0f, 1f), Tooltip("Velocity lost per 120 Hz step.")] private float drag = 0.06f;
        [SerializeField, Tooltip("Multiplier on Physics.gravity.")] private float gravityScale = 0.8f;
        [SerializeField, Tooltip("Joint radius for collisions, world units.")] private float jointRadius = 0.03f;
        [SerializeField] private int maxJoints = 6;
        [SerializeField, Range(1f, 180f), Tooltip("Max degrees a joint may swing away from its animated rest direction.")] private float maxAngle = 180f;

        /// <summary>Extra world-space acceleration (wind, dash drag), applied to every joint.</summary>
        public Vector3 ExternalAcceleration { get; set; }

        private Transform[] _bones;
        private Quaternion[] _restLocal;
        private Vector3[] _localTail;
        private Vector3[] _curr;
        private Vector3[] _prev;
        private float[] _length;
        private int _count;
        private SpringCapsule[] _colliders;
        private float _accum;

        /// <summary>Sets the tuning values (call before <see cref="Initialize"/> or any time after).</summary>
        public void Configure(float stiffness, float drag, float gravityScale, float jointRadius, float maxAngle = 180f)
        {
            this.maxAngle = maxAngle;
            this.stiffness = stiffness;
            this.drag = drag;
            this.gravityScale = gravityScale;
            this.jointRadius = jointRadius;
        }

        /// <summary>Collects the chain and its rest pose. Allocates once; <paramref name="colliders"/> is kept by reference.</summary>
        public void Initialize(SpringCapsule[] colliders)
        {
            _colliders = colliders;
            _bones = new Transform[maxJoints];
            _restLocal = new Quaternion[maxJoints];
            _localTail = new Vector3[maxJoints];
            _curr = new Vector3[maxJoints];
            _prev = new Vector3[maxJoints];
            _length = new float[maxJoints];
            _count = 0;
            for (var t = transform; t != null && _count < maxJoints; t = t.childCount > 0 ? t.GetChild(0) : null)
            {
                _bones[_count] = t;
                _restLocal[_count] = t.localRotation;
                Vector3 tailWorld = t.childCount > 0
                    ? t.GetChild(0).position
                    : t.position + (t.position - t.parent.position) * 0.6f;   // virtual tail for the last bone
                _localTail[_count] = t.InverseTransformPoint(tailWorld);
                _length[_count] = Vector3.Distance(t.position, tailWorld);
                _count++;
            }
            ResetState();
        }

        /// <summary>Snaps every joint to its rest direction with zero velocity (use after teleports).</summary>
        public void ResetState()
        {
            for (int i = 0; i < _count; i++)
            {
                _bones[i].localRotation = _restLocal[i];
                _curr[i] = _prev[i] = _bones[i].TransformPoint(_localTail[i]);
            }
            _accum = 0f;
        }

        /// <summary>Advances the simulation by <paramref name="dt"/> seconds and poses the bones. No allocations.</summary>
        public void Simulate(float dt)
        {
            if (_count == 0) return;
            _accum = Mathf.Min(_accum + Mathf.Max(dt, 0f), Step * MaxSteps);
            bool stepped = false;
            while (_accum >= Step)
            {
                _accum -= Step;
                Pass(true);
                stepped = true;
            }
            if (!stepped) Pass(false);
        }

        private void Pass(bool integrate)
        {
            Vector3 accel = Physics.gravity * gravityScale + ExternalAcceleration;
            float keep = 1f - drag;
            for (int i = 0; i < _count; i++)
            {
                Transform bone = _bones[i];
                bone.localRotation = _restLocal[i];
                Vector3 head = bone.position;
                Vector3 restTail = bone.TransformPoint(_localTail[i]);
                Vector3 next = _curr[i];
                if (integrate)
                {
                    Vector3 a = (restTail - _curr[i]) * stiffness + accel;
                    next = _curr[i] + (_curr[i] - _prev[i]) * keep + a * (Step * Step);
                    next = head + (next - head).normalized * _length[i];
                    if (maxAngle < 180f)
                    {
                        // Angle limit: a panel can sway and trail but never flag out flat.
                        Vector3 rd = restTail - head, nd = next - head;
                        float ang = Vector3.Angle(rd, nd);
                        // the root joint gets the full limit, the joints below a third each (the limit is relative
                        // to the parent, so it accumulates down the chain)
                        float lim = i == 0 ? maxAngle : maxAngle * 0.33f;
                        if (ang > lim) next = head + Vector3.Slerp(rd.normalized, nd.normalized, lim / ang) * _length[i];
                    }
                    if (_colliders != null) next = Collide(next, head, _length[i]);
                    _prev[i] = _curr[i];
                    _curr[i] = next;
                }
                Vector3 from = restTail - head;
                Vector3 to = next - head;
                if (from.sqrMagnitude > 1e-10f && to.sqrMagnitude > 1e-10f)
                    bone.rotation = Quaternion.FromToRotation(from, to) * bone.rotation;
            }
        }

        private Vector3 Collide(Vector3 p, Vector3 head, float length)
        {
            for (int c = 0; c < _colliders.Length; c++)
            {
                ref SpringCapsule cap = ref _colliders[c];
                if (cap.A == null || cap.B == null) continue;
                Vector3 a = cap.A.position;
                Vector3 ab = cap.B.position - a;
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
                Vector3 closest = a + ab * t;
                Vector3 d = p - closest;
                float r = cap.Radius + jointRadius;
                float sq = d.sqrMagnitude;
                if (sq < r * r && sq > 1e-12f)
                {
                    p = closest + d * (r / Mathf.Sqrt(sq));
                    p = head + (p - head).normalized * length;
                }
            }
            return p;
        }
    }
}
