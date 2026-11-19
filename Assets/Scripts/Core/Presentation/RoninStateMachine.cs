namespace Redline.Core.Presentation
{
    /// <summary>Animation names of the baked Tech Ronin sheet (<c>Resources/Art/Characters/Ronin/ronin</c>).</summary>
    public enum BakedAnim
    {
        Idle, Run, Jump, Fall, Hang, Slide, Attack1, Attack2, JumpAttack, RunAttack1, RunAttack2, Hurt, Death, Sprint, Land
    }

    /// <summary>Per-frame player state the ronin presentation reads (filled from the sim).</summary>
    public struct RoninStateInput
    {
        /// <summary>Horizontal speed, world units per second (absolute).</summary>
        public float Speed;
        /// <summary>Standing on ground.</summary>
        public bool Grounded;
        /// <summary>Vertical velocity.</summary>
        public float VelocityY;
        /// <summary>Slide attack in progress.</summary>
        public bool Sliding;
        /// <summary>Hanging from the hook.</summary>
        public bool Hooked;
        /// <summary>A strike is active; a rising edge or a new <see cref="StrikeId"/> is a new press.</summary>
        public bool Attacking;
        /// <summary>Strike id from the sim.</summary>
        public int StrikeId;
        /// <summary>Hurt this frame (rising edge plays the hurt animation once).</summary>
        public bool Hurt;
        /// <summary>Dead (holds the death animation).</summary>
        public bool Dead;
    }

    /// <summary>
    /// Engine-free animation selection for the baked ronin, with the live RoninView's priorities and attack timing:
    /// death, hurt (held <see cref="HurtDuration"/>), slash press (alternating Attack1/Attack2, held
    /// <see cref="AttackDuration"/>; airborne = JumpAttack, on the ground while moving = RunAttack variant), slide, hang,
    /// jump/fall, land squash (after a fast fall, held <see cref="LandDuration"/>), sprint (with hysteresis), run, idle.
    /// </summary>
    /// <remarks>No allocation. Not thread-safe (one instance per presented ronin).</remarks>
    public sealed class RoninStateMachine
    {
        /// <summary>Seconds one press holds the slash.</summary>
        public float AttackDuration = 0.3f;
        /// <summary>Seconds the hurt animation holds (the presenter sets it from the sheet).</summary>
        public float HurtDuration = 0.4f;
        /// <summary>Speed above which the ronin is "running".</summary>
        public float RunThreshold = 0.1f;
        /// <summary>Vertical speed above which the airborne animation is jump rather than fall.</summary>
        public float JumpRiseThreshold = 0.5f;
        /// <summary>Ground speed at or above which Run becomes Sprint.</summary>
        public float SprintThreshold = 13f;
        /// <summary>Ground speed below which Sprint drops back to Run (hysteresis, so speed jitter cannot flicker the cycle).</summary>
        public float SprintExitThreshold = 12f;
        /// <summary>Downward speed (positive) at touchdown from which the land squash plays.</summary>
        public float LandSpeed = 12f;
        /// <summary>Seconds the land squash holds (the presenter sets it from the sheet).</summary>
        public float LandDuration = 0.125f;
        /// <summary>Above this ground speed a touchdown keeps running instead of squashing (the squash has no stride).</summary>
        public float LandMaxRunSpeed = 9f;

        /// <summary>
        /// Playback rate of a locomotion cycle so its feet do not skate: the clip carries the body <paramref name="stride"/>
        /// world units per <paramref name="cycleSeconds"/> at 1x, so the rate is speed x cycle / stride, with no upper cap.
        /// Floored at <paramref name="minRate"/> (near-standstill would freeze the cycle); 1 without stride data.
        /// </summary>
        /// <example><code>float rate = RoninStateMachine.LocomotionRate(Mathf.Abs(vx), anim.Stride, anim.Duration, 0.35f);</code></example>
        public static float LocomotionRate(float speed, float stride, float cycleSeconds, float minRate)
        {
            if (stride <= 0f || cycleSeconds <= 0f) return 1f;
            float r = speed * cycleSeconds / stride;
            return r < minRate ? minRate : r;
        }

        private bool _prevAttacking, _prevHurt, _wasGrounded = true, _sprinting;
        private int _prevStrike = -1, _combo;
        private float _attackLeft, _hurtLeft, _landLeft, _airVy;
        private BakedAnim _attackAnim;

        /// <summary>True for the step in which the returned animation must restart from its first frame.</summary>
        public bool Restart { get; private set; }

        /// <summary>The animation chosen by the last <see cref="Step"/>.</summary>
        public BakedAnim Current { get; private set; }

        /// <summary>Advances the timers by <paramref name="dt"/> seconds and returns the animation to show.</summary>
        public BakedAnim Step(in RoninStateInput s, float dt)
        {
            Restart = false;
            bool hurtEdge = s.Hurt && !_prevHurt;
            _prevHurt = s.Hurt;
            bool attackEdge = s.Attacking && (!_prevAttacking || s.StrikeId != _prevStrike);
            _prevAttacking = s.Attacking;
            _prevStrike = s.StrikeId;
            // Touchdown: remember the last airborne vertical speed; a fast enough fall arms the land squash.
            bool touchdown = s.Grounded && !_wasGrounded;
            if (!s.Grounded) { _airVy = s.VelocityY; _landLeft = 0f; }
            _wasGrounded = s.Grounded;
            if (touchdown && -_airVy >= LandSpeed && !s.Sliding && s.Speed <= LandMaxRunSpeed) { _landLeft = LandDuration; Restart = true; }
            if (touchdown) _airVy = 0f;
            _sprinting = s.Speed >= SprintThreshold || (_sprinting && s.Speed >= SprintExitThreshold);
            Current = Pick(s, dt, hurtEdge, attackEdge);
            if (Current != BakedAnim.Land) _landLeft = 0f;
            return Current;
        }

        private BakedAnim Pick(in RoninStateInput s, float dt, bool hurtEdge, bool attackEdge)
        {
            if (s.Dead) { _hurtLeft = 0f; _attackLeft = 0f; _landLeft = 0f; return BakedAnim.Death; }
            if (hurtEdge) { _hurtLeft = HurtDuration; Restart = true; }
            if (_hurtLeft > 0f) { _hurtLeft -= dt; return BakedAnim.Hurt; }
            if (attackEdge)
            {
                _combo ^= 1;
                _attackLeft = AttackDuration;
                bool air = !s.Grounded && !s.Sliding;
                bool running = !air && !s.Sliding && s.Speed > RunThreshold;
                bool first = _combo == 1;   // presses alternate Attack1, Attack2, Attack1, ...
                _attackAnim = air ? BakedAnim.JumpAttack
                    : running ? (first ? BakedAnim.RunAttack1 : BakedAnim.RunAttack2)
                    : (first ? BakedAnim.Attack1 : BakedAnim.Attack2);
                Restart = true;
            }
            if (_attackLeft > 0f)
            {
                _attackLeft -= dt;
                if (!s.Sliding) return _attackAnim;
            }
            if (s.Sliding) return BakedAnim.Slide;
            if (s.Hooked) return BakedAnim.Hang;
            if (!s.Grounded) return s.VelocityY > JumpRiseThreshold ? BakedAnim.Jump : BakedAnim.Fall;
            if (_landLeft > 0f) { _landLeft -= dt; return BakedAnim.Land; }
            if (s.Speed <= RunThreshold) return BakedAnim.Idle;
            return _sprinting ? BakedAnim.Sprint : BakedAnim.Run;
        }

        /// <summary>Sheet animation name of <paramref name="a"/> (lower-case, e.g. <c>"runattack1"</c>).</summary>
        public static string Name(BakedAnim a) => a.ToString().ToLowerInvariant();
    }
}
