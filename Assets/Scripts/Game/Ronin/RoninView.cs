using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Redline.Game.Ronin
{
    /// <summary>Simple per-frame inputs for <see cref="RoninView"/>; fill it from the sim each frame.</summary>
    public struct RoninInput
    {
        /// <summary>Horizontal speed, world units per second (absolute).</summary>
        public float Speed;
        /// <summary>Standing on ground.</summary>
        public bool Grounded;
        /// <summary>Vertical velocity; above <see cref="RoninView.JumpRiseThreshold"/> plays jump, else fall.</summary>
        public float VelocityY;
        /// <summary>Slide attack in progress.</summary>
        public bool Sliding;
        /// <summary>Hanging from the hook.</summary>
        public bool Hooked;
        /// <summary>A strike is active. A rising edge or a new <see cref="StrikeId"/> restarts the swing.</summary>
        public bool Attacking;
        /// <summary>Strike id: 0..2 slashes, 3 spin attack (wraps). Airborne strikes play the jump attack.</summary>
        public int StrikeId;
        /// <summary>Hurt this frame (rising edge plays the hurt clip once).</summary>
        public bool Hurt;
        /// <summary>Dead (holds the death clip).</summary>
        public bool Dead;
        /// <summary>+1 faces +X (right), -1 faces -X (left).</summary>
        public int Facing;
    }

    /// <summary>
    /// The Tech Ronin presenter: spawns the rigged model, drives it with a code-built PlayableGraph (a crossfading
    /// mixer of the 13 clips, on the model's Animator), runs the coat and sash spring bones, tints the hat rim and
    /// blade emissive with the tier colour, and turns the model to face left or right in a side view with a slight
    /// 3/4 turn toward the camera. Independent of the game sim: feed it a <see cref="RoninInput"/>.
    /// All renderers get rendering-layer bit 1 ("Characters") in addition to their default bit.
    /// </summary>
    /// <remarks>
    /// Per-frame work is allocation-free. In Play Mode it ticks itself in LateUpdate (<see cref="AutoTick"/>);
    /// tools can tick it manually with <see cref="Tick"/>. The camera is assumed to look along +Z (side view, the
    /// character moves along X); <see cref="CameraTurn"/> rotates the model toward -Z.
    /// </remarks>
    /// <example>
    /// <code>
    /// var ronin = RoninView.Create(RoninAssets.Load(), playerRoot);   // once
    /// ronin.SetTierColor(new Color(0.4f, 2.5f, 3f));                  // HDR, on tier change
    /// // every frame:
    /// ronin.Input = new RoninInput { Speed = Mathf.Abs(vx), Grounded = onGround, VelocityY = vy,
    ///     Sliding = sliding, Hooked = hooked, Attacking = striking, StrikeId = strike, Facing = vx &gt;= 0 ? 1 : -1 };
    /// ronin.transform.position = simPosition;                          // feet at the pivot
    /// </code>
    /// </example>
    public sealed class RoninView : MonoBehaviour
    {
        /// <summary>Vertical speed above which the airborne state is "jump" rather than "fall".</summary>
        public const float JumpRiseThreshold = 0.5f;
        private const uint CharactersLayerBit = 1u << 1;
        private const int ClipCount = 17;

        /// <summary>Current inputs; set every frame.</summary>
        public RoninInput Input;
        /// <summary>Tick in LateUpdate during Play Mode. Disable to drive <see cref="Tick"/> yourself.</summary>
        public bool AutoTick = true;
        /// <summary>Crossfade duration, seconds.</summary>
        public float FadeTime = 0.12f;
        /// <summary>Degrees the side-facing model turns toward the camera (3/4 read).</summary>
        public float CameraTurn = 22f;
        /// <summary>Turn speed when flipping facing, degrees per second.</summary>
        public float TurnSpeed = 1500f;
        /// <summary>Run speed at which the run clip plays at 1x.</summary>
        public float RunReferenceSpeed = 6f;
        /// <summary>Playback speed of the slash and jump-attack clips (the source swings are slow two-handed ones).</summary>
        public float AttackSpeed = 1f;
        /// <summary>Fraction of a slash clip skipped at the start (trims the wind-up so the blade lands fast).</summary>
        public float AttackStart = 0.3f;
        /// <summary>
        /// Seconds from the press to the blade's impact. Slashes with a measured impact time start at
        /// impact − ImpactLead × AttackSpeed; others use <see cref="AttackStart"/>.
        /// </summary>
        public float ImpactLead = 2f / 30f;
        /// <summary>Seconds one press holds the slash before blending back to locomotion.</summary>
        public float AttackDuration = 0.3f;
        /// <summary>Fade time of the upper-body slash layer used while running, seconds.</summary>
        public float UpperFade = 0.06f;

        /// <summary>
        /// HDR multiplier of the body atlas emission mask (eyes, cyber lines). Applied to the runtime atlas instance,
        /// which also forces the _EMISSION keyword on, so the glow does not depend on the keyword surviving in the
        /// saved material asset.
        /// </summary>
        public const float AtlasEmissionScale = 14f;

        /// <summary>Resources path of the body-atlas shader (URP Lit plus a faint fresnel silhouette rim).</summary>
        public const string RimShaderPath = "Ronin/RoninLitRim";
        /// <summary>Linear HDR colour of the body's view-dependent silhouette rim (cool blue, low: a thin backlit edge,
        /// not a sheen; same hue as Enemy3DRenderer.RimColor so both read with one rim language).
        /// A shader global, so it applies to every ronin instance.</summary>
        public static Color SilhouetteRimColor = new Color(0f, 0f, 0f); // rim off: it flickered in motion (user feedback 2026-11-04)
        /// <summary>Fresnel exponent of the silhouette rim; higher keeps it to a thinner edge.</summary>
        public static float SilhouetteRimPower = 5f;
        /// <summary>Unlit fill floor (albedo × this), so the ronin's colour blocks read in the murk like the sprite
        /// enemies' fill overlay does. 0 = plain Lit.</summary>
        public static float SilhouetteFill = 0.8f;

        private static readonly int RimColorId = Shader.PropertyToID("_RoninRimColor");
        private static readonly int RimPowerId = Shader.PropertyToID("_RoninRimPower");
        private static readonly int FillFloorId = Shader.PropertyToID("_RoninFillFloor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private RoninAssets _assets;
        private bool _initialized;
        private Transform _model;
        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private AnimationClipPlayable[] _playables;
        private float[] _weights;
        private float[] _lengths;
        private bool[] _loops;
        private int _current = -1;
        private bool _prevAttacking;
        private int _prevStrike = -1;
        private bool _prevHurt;
        private float _hurtLeft;
        private float _yaw;
        private SpringBone[] _springs;
        private SpringCapsule[] _capsules;
        private Material _rimMat;
        private Material _bladeMat;
        private Material _atlasMat;
        private Renderer[] _renderers;
        private float[] _windDrag;
        private float[] _windLift;
        private Vector3 _lastPos;
        private Vector3 _vel;
        private bool _hasLastPos;
        private float _time;
        private AnimationLayerMixerPlayable _layers;
        private AnimationMixerPlayable _upper;
        private AnimationClipPlayable[] _upperClips;
        private AvatarMask _upperMask;
        private float _upperWeight;
        private float _attackLeft;
        private int _attackState = -1;
        private int _combo;
        private bool _upperMode;
        private Color _tier = Color.white;

        // Extra turn toward the camera while hanging (the hang clip is authored in profile; symmetric for both facings).
        private const float HangExtraTurn = 28f;
        // A frame step longer than this (world units) is treated as a teleport: no wind impulse.
        private const float TeleportDistance = 3f;

        /// <summary>The instantiated model root (child of this transform).</summary>
        public Transform Model => _model;
        /// <summary>The katana mesh transform (child of the right-hand bone); move it to adjust the grip.</summary>
        public Transform Katana { get; private set; }
        /// <summary>The swing ribbon, once <see cref="EnableTrail"/> has been called (else null).</summary>
        public BladeTrail Trail { get; private set; }
        /// <summary>The state currently playing.</summary>
        public RoninClip Current => (RoninClip)Mathf.Max(_current, 0);

        /// <summary>Creates a GameObject under <paramref name="parent"/> with a ready RoninView.</summary>
        public static RoninView Create(RoninAssets assets, Transform parent)
        {
            var go = new GameObject("Ronin");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<RoninView>();
            view.Initialize(assets);
            return view;
        }

        /// <summary>Spawns the model and builds the graph, materials and springs. Allocates; call once.</summary>
        public void Initialize(RoninAssets assets)
        {
            _assets = assets;
            _model = Instantiate(assets.Model, transform, false).transform;
            _model.name = "RoninModel";
            var animator = _model.GetComponent<Animator>();
            if (animator == null) animator = _model.gameObject.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.runtimeAnimatorController = null;

            SetupMaterials();
            BuildGraph(animator);
            Katana = Find(_model, "Katana");
            _yaw = TargetYaw(1);
            _model.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            _graph.Evaluate(0f);
            SetupSprings();
            SetTierColor(assets.DefaultTier);
            _initialized = true;
        }

        /// <summary>
        /// Adds the katana swing ribbon (shown during slashes and slides) drawn with a copy of
        /// <paramref name="additive"/> (an Actors3D/Fx additive material). Call once after <see cref="Initialize"/>.
        /// </summary>
        public void EnableTrail(Material additive)
        {
            if (Trail != null || Katana == null) return;
            Trail = BladeTrail.Create(transform, Katana, additive);
            if (Trail != null) Trail.SetColor(_tier * TrailHdr);
        }

        /// <summary>Multiplier of the tier colour on the swing ribbon.</summary>
        public float TrailHdr = 1.6f;

        /// <summary>Sets the tier colour (HDR) of the hat's emissive rim, the blade and the swing ribbon.</summary>
        public void SetTierColor(Color hdr)
        {
            _tier = hdr;
            if (Trail != null) Trail.SetColor(hdr * TrailHdr);
            if (_rimMat != null) _rimMat.SetColor(EmissionId, hdr * 1.6f);
            if (_bladeMat != null)
            {
                _bladeMat.SetColor(EmissionId, hdr);
                Color hue = hdr.maxColorComponent > 0f ? hdr / hdr.maxColorComponent : hdr;
                _bladeMat.SetColor(BaseColorId, Color.Lerp(new Color(0.75f, 0.8f, 0.85f), hue, 0.35f));
            }
        }

        /// <summary>Snaps the springs to rest (call after teleporting the character).</summary>
        public void ResetSprings()
        {
            for (int i = 0; i < _springs.Length; i++) _springs[i].ResetState();
            if (Trail != null) Trail.Clear();
            _hasLastPos = false;
            _vel = Vector3.zero;
        }

        /// <summary>Snaps facing to <paramref name="facing"/> without turning.</summary>
        public void SnapFacing(int facing)
        {
            _yaw = TargetYaw(facing);
            _model.localRotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        /// <summary>Advances animation, facing and springs by <paramref name="dt"/> seconds. No allocations.</summary>
        public void Tick(float dt)
        {
            if (!_initialized) return;
            int target = Resolve(dt, out bool restart);
            if (target != _current || restart) Enter(target, target == _current);

            // Crossfade weights (normalised so the humanoid pose never blends toward the default pose).
            float rate = FadeTime > 0f ? dt / FadeTime : 1f;
            float sum = 0f;
            for (int i = 0; i < ClipCount; i++)
            {
                _weights[i] = Mathf.MoveTowards(_weights[i], i == _current ? 1f : 0f, rate);
                sum += _weights[i];
            }
            for (int i = 0; i < ClipCount; i++) _mixer.SetInputWeight(i, sum > 0f ? _weights[i] / sum : (i == _current ? 1f : 0f));

            // Playback: the graph advances every clip by dt * speed; one-shots clamp at their end.
            for (int i = 0; i < ClipCount; i++)
            {
                if (!_playables[i].IsValid()) continue;
                float speed = i == (int)RoninClip.Run || i == (int)RoninClip.Sprint ? Mathf.Max(Input.Speed / RunReferenceSpeed, 0.5f)
                    : IsAttack(i) ? AttackSpeed : 1f;
                _playables[i].SetSpeed(_weights[i] > 0f ? speed : 0f);
            }
            // Upper-body slash layer (running slashes).
            if (_upperClips != null)
            {
                float wantUpper = _upperMode && _attackLeft > 0f ? 1f : 0f;
                _upperWeight = Mathf.MoveTowards(_upperWeight, wantUpper, UpperFade > 0f ? dt / UpperFade : 1f);
                _layers.SetInputWeight(1, _upperWeight);
                for (int k = 0; k < _upperClips.Length; k++)
                    if (_upperClips[k].IsValid()) _upperClips[k].SetSpeed(_upperWeight > 0f ? AttackSpeed : 0f);
            }
            _graph.Evaluate(dt);
            for (int i = 0; i < ClipCount; i++)
                if (_playables[i].IsValid() && !_loops[i] && _playables[i].GetTime() > _lengths[i]) _playables[i].SetTime(_lengths[i]);

            _yaw = Mathf.MoveTowardsAngle(_yaw, TargetYaw(Input.Facing), TurnSpeed * dt);
            _model.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            if (Trail != null)
                Trail.Tick(dt, !Input.Dead && (_attackLeft > 0f || Input.Sliding || (IsAttack(_current) && _weights[_current] > 0.5f)));
            SimulateCloth(dt);
        }

        /// <summary>
        /// Tool path (the sprite baker): poses the model with <paramref name="clip"/> alone at exactly
        /// <paramref name="time"/> seconds (no mixer blend, no crossfade, no upper-body layer, foot IK off) and faces
        /// right. Call <see cref="SimulateCloth"/> afterwards to advance the spring bones. Allocation-free.
        /// </summary>
        /// <example><code>view.SamplePose(RoninClip.Run, t); view.SimulateCloth(1f / 120f);</code></example>
        public void SamplePose(RoninClip clip, float time)
        {
            if (!_initialized) return;
            int c = (int)clip;
            if (!_playables[c].IsValid()) c = (int)RoninClip.Idle;
            for (int i = 0; i < ClipCount; i++)
            {
                _weights[i] = i == c ? 1f : 0f;
                _mixer.SetInputWeight(i, _weights[i]);
                if (_playables[i].IsValid()) _playables[i].SetSpeed(0f);
            }
            _layers.SetInputWeight(1, 0f);
            _upperWeight = 0f;
            _current = c;
            _playables[c].SetTime(time);
            _graph.Evaluate(0f);
            SnapFacing(1);
        }

        /// <summary>The source clip's length in seconds (0 if missing).</summary>
        public float ClipLength(RoninClip clip) => _playables[(int)clip].IsValid() ? _lengths[(int)clip] : 0f;

        /// <summary>
        /// Advances the coat, sash and scarf spring bones by <paramref name="dt"/> against the character's world motion
        /// (called by <see cref="Tick"/>; tools that pose with <see cref="SamplePose"/> call it themselves).
        /// </summary>
        public void SimulateCloth(float dt)
        {
            // Air drag on the cloth: the springs already lag world motion; this adds a per-chain push against the
            // character's world velocity, a lift that grows with speed and a small flutter, so the tails and
            // the scarf stream visibly behind a running figure even at game scale.
            Vector3 pos = transform.position;
            if (_hasLastPos && dt > 0f)
            {
                Vector3 step = pos - _lastPos;
                Vector3 v = step.sqrMagnitude < TeleportDistance * TeleportDistance ? step / dt : Vector3.zero;
                _vel = Vector3.Lerp(_vel, v, 1f - Mathf.Exp(-dt * 12f));
            }
            _lastPos = pos;
            _hasLastPos = true;
            _time += dt;
            float airSpeed = _vel.magnitude;
            for (int i = 0; i < _springs.Length; i++)
            {
                float flutter = Mathf.Sin(_time * 11f + i * 1.7f) * 0.35f + Mathf.Sin(_time * 17.3f + i * 0.9f) * 0.2f;
                _springs[i].ExternalAcceleration = -_vel * _windDrag[i]
                    + Vector3.up * (_windLift[i] * airSpeed * (1f + flutter))
                    + new Vector3(0f, 0f, flutter * airSpeed * 0.6f);
                _springs[i].Simulate(dt);
            }
        }

        private void LateUpdate()
        {
            if (AutoTick && Application.isPlaying) Tick(Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
            if (_rimMat != null) Destroy(_rimMat);
            if (_bladeMat != null) Destroy(_bladeMat);
            if (_atlasMat != null) Destroy(_atlasMat);
            if (_upperMask != null) Destroy(_upperMask);
        }

        private float TargetYaw(int facing)
        {
            float turn = CameraTurn + (_current == (int)RoninClip.Hang ? HangExtraTurn : 0f);
            return facing >= 0 ? 90f + turn : -(90f + turn);
        }

        private int Resolve(float dt, out bool restart)
        {
            restart = false;
            ref RoninInput s = ref Input;
            bool hurtEdge = s.Hurt && !_prevHurt;
            _prevHurt = s.Hurt;
            bool attackEdge = s.Attacking && (!_prevAttacking || s.StrikeId != _prevStrike);
            _prevAttacking = s.Attacking;
            _prevStrike = s.StrikeId;
            if (s.Dead) return (int)RoninClip.Death;
            if (hurtEdge) { _hurtLeft = _lengths[(int)RoninClip.Hurt]; restart = true; }
            if (_hurtLeft > 0f) { _hurtLeft -= dt; return (int)RoninClip.Hurt; }
            if (attackEdge)
            {
                // One press = one short slash; successive presses alternate Attack1/Attack2 (combo). Running on the
                // ground, the slash plays on the upper body only so the legs keep running.
                _combo ^= 1;
                _attackLeft = AttackDuration;
                bool air = !s.Grounded && !s.Sliding;
                _attackState = air ? (int)RoninClip.JumpAttack : _combo == 0 ? (int)RoninClip.Attack1 : (int)RoninClip.Attack2;
                _upperMode = !air && !s.Sliding && s.Speed > 0.1f && _upperClips != null && _upperClips[_combo].IsValid();
                if (_upperMode)
                {
                    for (int k = 0; k < 2; k++) _upper.SetInputWeight(k, k == _combo ? 1f : 0f);
                    var up = _upperClips[_combo];
                    up.SetTime(AttackStartTime(_attackState));
                }
                else restart = true;
            }
            if (_attackLeft > 0f)
            {
                _attackLeft -= dt;
                if (!_upperMode && !s.Sliding) return _attackState;
            }
            if (s.Sliding) return (int)RoninClip.SlideAttack;
            if (s.Hooked) return (int)RoninClip.Hang;
            if (!s.Grounded) return s.VelocityY > JumpRiseThreshold ? (int)RoninClip.Jump : (int)RoninClip.Fall;
            if (s.Speed <= 0.1f) return (int)RoninClip.Idle;
            return s.Speed >= 13f && _playables[(int)RoninClip.Sprint].IsValid() ? (int)RoninClip.Sprint : (int)RoninClip.Run;
        }

        // Impact (peak right-hand speed relative to the hips, sampled at 60 Hz by Game3DCapture.ReportClipImpacts),
        // clip seconds: Attack1 0.517 of 1.267, Attack2 0.983 of 1.833, JumpAttack 0.45 (first swing) of 2.167.
        // v3 procedural slashes (ronin_anims.py): impact at frame 2 of 10 at 30 fps for all three.
        private const float Attack1Impact = 2f / 30f, Attack2Impact = 2f / 30f, JumpAttackImpact = 2f / 30f;

        private double AttackStartTime(int state)
        {
            float impact = state == (int)RoninClip.Attack1 ? Attack1Impact : state == (int)RoninClip.Attack2 ? Attack2Impact
                : state == (int)RoninClip.JumpAttack ? JumpAttackImpact : -1f;
            if (impact < 0f) return AttackStart * _lengths[state];
            return Mathf.Clamp(impact - ImpactLead * AttackSpeed, 0f, _lengths[state]);
        }

        private static bool IsAttack(int state) =>
            state == (int)RoninClip.Attack1 || state == (int)RoninClip.Attack2 || state == (int)RoninClip.Attack3 ||
            state == (int)RoninClip.SpinAttack || state == (int)RoninClip.JumpAttack;

        private void Enter(int state, bool sameState)
        {
            if (!_playables[state].IsValid()) state = (int)RoninClip.Idle;
            _playables[state].SetTime(IsAttack(state) ? AttackStartTime(state) : 0.0);
            if (sameState) _weights[state] = 1f;
            _current = state;
        }

        private void BuildGraph(Animator animator)
        {
            _graph = PlayableGraph.Create("Ronin");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _mixer = AnimationMixerPlayable.Create(_graph, ClipCount);
            _playables = new AnimationClipPlayable[ClipCount];
            _weights = new float[ClipCount];
            _lengths = new float[ClipCount];
            _loops = new bool[ClipCount];
            for (int i = 0; i < ClipCount; i++)
            {
                AnimationClip clip = _assets.Clips != null && i < _assets.Clips.Length ? _assets.Clips[i] : null;
                if (clip == null) continue;
                var p = AnimationClipPlayable.Create(_graph, clip);
                p.SetApplyFootIK(false);
                _graph.Connect(p, 0, _mixer, i);
                _playables[i] = p;
                _lengths[i] = clip.length;
                _loops[i] = clip.isLooping;
            }
            // Layer 0: the full-body state mixer. Layer 1: Attack1/Attack2 masked to the upper body.
            _layers = AnimationLayerMixerPlayable.Create(_graph, 2);
            _graph.Connect(_mixer, 0, _layers, 0);
            _layers.SetInputWeight(0, 1f);
            _upper = AnimationMixerPlayable.Create(_graph, 2);
            _upperClips = new AnimationClipPlayable[2];
            for (int k = 0; k < 2; k++)
            {
                int ci = (int)RoninClip.Attack1 + k;
                AnimationClip clip = _assets.Clips != null && ci < _assets.Clips.Length ? _assets.Clips[ci] : null;
                if (clip == null) continue;
                var p = AnimationClipPlayable.Create(_graph, clip);
                p.SetApplyFootIK(false);
                p.SetSpeed(0f);
                _graph.Connect(p, 0, _upper, k);
                _upperClips[k] = p;
            }
            _graph.Connect(_upper, 0, _layers, 1);
            _layers.SetInputWeight(1, 0f);
            _upperMask = new AvatarMask();
            for (int b = 0; b < (int)AvatarMaskBodyPart.LastBodyPart; b++)
            {
                var part = (AvatarMaskBodyPart)b;
                bool lower = part == AvatarMaskBodyPart.Root || part == AvatarMaskBodyPart.LeftLeg || part == AvatarMaskBodyPart.RightLeg
                    || part == AvatarMaskBodyPart.LeftFootIK || part == AvatarMaskBodyPart.RightFootIK;
                _upperMask.SetHumanoidBodyPartActive(part, !lower);
            }
            _layers.SetLayerMaskFromAvatarMask(1, _upperMask);
            var output = AnimationPlayableOutput.Create(_graph, "Ronin", animator);
            output.SetSourcePlayable(_layers);
            _current = (int)RoninClip.Idle;
            _weights[_current] = 1f;
            _mixer.SetInputWeight(_current, 1f);
        }

        private void SetupMaterials()
        {
            _renderers = _model.GetComponentsInChildren<Renderer>(true);
            foreach (var r in _renderers)
            {
                r.renderingLayerMask |= CharactersLayerBit;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var replacement = FindMaterial(mats[i].name);
                    if (replacement == null) continue;
                    if (replacement.name == "Ronin_Rim") replacement = _rimMat ??= new Material(replacement);
                    else if (replacement.name == "Ronin_Blade") replacement = _bladeMat ??= new Material(replacement);
                    else if (replacement.name == "Ronin_Atlas") replacement = _atlasMat ??= CreateAtlasInstance(replacement);
                    mats[i] = replacement;
                }
                r.sharedMaterials = mats;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }
        }

        /// <summary>
        /// The body atlas instance with emission forced on: keyword, HDR colour and the palette emission mask (taken
        /// from the blade material, which shares the palette, if the atlas asset lost its reference).
        /// </summary>
        private Material CreateAtlasInstance(Material source)
        {
            var m = new Material(source) { name = source.name };
            // Body only (atlas): swap to the rim variant of Lit. Same properties, so every value carries over; the
            // keywords are re-applied because a shader swap can drop ones the new shader declares differently.
            var rimShader = Resources.Load<Shader>(RimShaderPath);
            if (rimShader != null && rimShader.isSupported)
            {
                var keywords = source.shaderKeywords;
                m.shader = rimShader;
                foreach (var k in keywords) m.EnableKeyword(k);
            }
            else Debug.LogWarning("Redline: Resources/" + RimShaderPath + " is missing or unsupported; the ronin has no silhouette rim.");
            ApplySilhouetteRim();
            Texture mask = source.GetTexture(EmissionMapId);
            if (mask == null)
            {
                var blade = FindMaterial("Ronin_Blade");
                if (blade != null && blade.HasProperty(EmissionMapId)) mask = blade.GetTexture(EmissionMapId);
            }
            if (mask != null) m.SetTexture(EmissionMapId, mask);
            m.SetColor(EmissionId, new Color(AtlasEmissionScale, AtlasEmissionScale, AtlasEmissionScale, 1f));
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.EnableKeyword("_EMISSION");
            return m;
        }

        /// <summary>Pushes <see cref="SilhouetteRimColor"/> and <see cref="SilhouetteRimPower"/> to the shader globals.</summary>
        public static void ApplySilhouetteRim()
        {
            Shader.SetGlobalColor(RimColorId, SilhouetteRimColor);
            Shader.SetGlobalFloat(RimPowerId, SilhouetteRimPower);
            Shader.SetGlobalFloat(FillFloorId, SilhouetteFill);
        }

        private Material FindMaterial(string name)
        {
            if (_assets.Materials == null) return null;
            foreach (var m in _assets.Materials)
                if (m != null && (name == m.name || name.StartsWith(m.name + " "))) return m;
            return null;
        }

        private void SetupSprings()
        {
            Transform lUp = Find(_model, "mixamorig:LeftUpLeg"), lLeg = Find(_model, "mixamorig:LeftLeg"), lFoot = Find(_model, "mixamorig:LeftFoot");
            Transform rUp = Find(_model, "mixamorig:RightUpLeg"), rLeg = Find(_model, "mixamorig:RightLeg"), rFoot = Find(_model, "mixamorig:RightFoot");
            // World-unit scale from the thigh (X Bot thigh = 0.444 m).
            float unit = lUp != null && lLeg != null ? Vector3.Distance(lUp.position, lLeg.position) / 0.444f : 1f;
            _capsules = new[]
            {
                new SpringCapsule { A = lUp, B = lLeg, Radius = 0.085f * unit },
                new SpringCapsule { A = lLeg, B = lFoot, Radius = 0.065f * unit },
                new SpringCapsule { A = rUp, B = rLeg, Radius = 0.085f * unit },
                new SpringCapsule { A = rLeg, B = rFoot, Radius = 0.065f * unit },
            };
            Transform hips = Find(_model, "mixamorig:Hips"), neck = Find(_model, "mixamorig:Neck");
            var torso = new[] { new SpringCapsule { A = hips, B = neck, Radius = 0.14f * unit } };
            // Per chain: stiffness (1/s^2), drag (per 120 Hz step), gravity scale, wind drag (1/s), wind lift (1/s),
            // max swing from the animated rest (degrees). v3: stiffer, heavier and angle-limited, so the coat hangs,
            // sways and trails in separate panels instead of flagging out horizontally as one slab; the side tails
            // differ slightly so the panels part.
            (string root, float stiff, float drag, float grav, float wind, float lift, bool legs, float maxAngle)[] cfg =
            {
                ("Coat_BL_1", 72f, 0.12f, 1.6f, 1.6f, 0.05f, true, 42f),
                ("Coat_BC_1", 60f, 0.11f, 1.5f, 1.9f, 0.07f, true, 50f),
                ("Coat_BR_1", 80f, 0.12f, 1.6f, 1.4f, 0.04f, true, 38f),
                ("Coat_FL_1", 150f, 0.13f, 1.3f, 1.0f, 0.02f, true, 30f),
                ("Coat_FR_1", 150f, 0.13f, 1.3f, 1.0f, 0.02f, true, 30f),
                ("Sash_1", 45f, 0.10f, 1.4f, 2.4f, 0.10f, false, 55f),
                ("Scarf_1", 26f, 0.09f, 1.2f, 3.2f, 0.20f, false, 65f),
            };
            var list = new SpringBone[cfg.Length];
            _windDrag = new float[cfg.Length];
            _windLift = new float[cfg.Length];
            int n = 0;
            for (int i = 0; i < cfg.Length; i++)
            {
                var t = Find(_model, cfg[i].root);
                if (t == null) continue;
                var sb = t.gameObject.AddComponent<SpringBone>();
                sb.Configure(stiffness: cfg[i].stiff, drag: cfg[i].drag, gravityScale: cfg[i].grav, jointRadius: 0.03f * unit, maxAngle: cfg[i].maxAngle);
                sb.Initialize(cfg[i].legs ? _capsules : cfg[i].root.StartsWith("Scarf") ? torso : null);
                _windDrag[n] = cfg[i].wind;
                _windLift[n] = cfg[i].lift;
                list[n++] = sb;
            }
            System.Array.Resize(ref list, n);
            _springs = list;
        }

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var f = Find(root.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }
    }
}
