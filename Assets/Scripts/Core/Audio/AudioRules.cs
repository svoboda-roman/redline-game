using System;

namespace Redline.Core.Audio
{
    /// <summary>
    /// Music levels of one intensity position (production/audio/music-sfx-overhaul/direction.md 2.2):
    /// CALM and INTENSE gains, the INTENSE low-pass cutoff and the master music lift.
    /// </summary>
    public readonly struct MusicMix
    {
        /// <summary>CALM track gain in dB (0 = reference).</summary>
        public readonly float CalmDb;
        /// <summary>INTENSE track gain in dB (<see cref="AudioRules.SilentDb"/> = muted).</summary>
        public readonly float IntenseDb;
        /// <summary>Low-pass cutoff on the INTENSE track in Hz.</summary>
        public readonly float IntenseCutoffHz;
        /// <summary>Lift applied to both tracks in dB.</summary>
        public readonly float LiftDb;

        public MusicMix(float calmDb, float intenseDb, float intenseCutoffHz, float liftDb)
        {
            CalmDb = calmDb;
            IntenseDb = intenseDb;
            IntenseCutoffHz = intenseCutoffHz;
            LiftDb = liftDb;
        }
    }

    /// <summary>
    /// Pure audio rules behind Redline.Game.Audio (no UnityEngine): the tier-to-music-mix table, dB
    /// helpers, the tier crossfade position and round-robin variant picking. Lives in Core only so the
    /// engine-free test project (tools/core-tests) and the EditMode tests can reach it; it has no
    /// effect on the simulation.
    /// </summary>
    public static class AudioRules
    {
        /// <summary>Gain treated as silence (direction.md: "-inf (mute, actually -60 dB)").</summary>
        public const float SilentDb = -60f;
        /// <summary>A cutoff at or above this is "open" (the low-pass is effectively off).</summary>
        public const float OpenCutoffHz = 20000f;

        /// <summary>Default tier table, index = tier - 1 (direction.md 2.2).</summary>
        public static readonly MusicMix[] DefaultTierMix =
        {
            new MusicMix(0f, SilentDb, 1500f, 0f),
            new MusicMix(-3f, -4f, 6000f, 0f),
            new MusicMix(-9f, 0f, OpenCutoffHz, 1f),
        };

        /// <summary>Linear gain of a dB value; <see cref="SilentDb"/> or below is exactly 0.</summary>
        public static float DbToLinear(float db) => db <= SilentDb ? 0f : MathF.Pow(10f, db / 20f);

        /// <summary>dB of a linear gain, floored at <see cref="SilentDb"/>.</summary>
        public static float LinearToDb(float linear) =>
            linear <= 0f ? SilentDb : MathF.Max(SilentDb, 20f * MathF.Log10(linear));

        /// <summary>Tier clamped to 1..3.</summary>
        public static int ClampTier(int tier) => tier < 1 ? 1 : tier > 3 ? 3 : tier;

        /// <summary>The mix of a whole tier (1-3, clamped) from <paramref name="table"/> (default table if null).</summary>
        public static MusicMix ForTier(int tier, MusicMix[] table = null) => (table ?? DefaultTierMix)[ClampTier(tier) - 1];

        /// <summary>
        /// The mix at a continuous intensity position (1.0 = tier 1, 3.0 = tier 3) while a tier crossfade
        /// runs. Gains are interpolated as equal-power amplitude (a muted INTENSE fades in as sin, the
        /// fading side as cos), the cutoff logarithmically, the lift linearly.
        /// </summary>
        public static MusicMix At(float position, MusicMix[] table = null)
        {
            table ??= DefaultTierMix;
            if (float.IsNaN(position)) position = 1f;
            position = MathF.Min(3f, MathF.Max(1f, position));
            int lo = (int)MathF.Floor(position);
            if (lo >= 3) return table[2];
            float t = position - lo;
            var a = table[lo - 1];
            var b = table[lo];
            float fadeIn = MathF.Sin(t * MathF.PI * 0.5f);
            float fadeOut = MathF.Cos(t * MathF.PI * 0.5f);
            return new MusicMix(
                LinearToDb(DbToLinear(a.CalmDb) * fadeOut + DbToLinear(b.CalmDb) * fadeIn),
                LinearToDb(DbToLinear(a.IntenseDb) * fadeOut + DbToLinear(b.IntenseDb) * fadeIn),
                MathF.Exp(MathF.Log(a.IntenseCutoffHz) + (MathF.Log(b.IntenseCutoffHz) - MathF.Log(a.IntenseCutoffHz)) * t),
                a.LiftDb + (b.LiftDb - a.LiftDb) * t);
        }

        /// <summary>
        /// Moves the crossfade position toward <paramref name="targetTier"/>: one tier per
        /// <paramref name="upSeconds"/> going up, one tier per <paramref name="downSeconds"/> going down.
        /// </summary>
        public static float StepPosition(float position, int targetTier, float dt, float upSeconds, float downSeconds)
        {
            float target = ClampTier(targetTier);
            if (dt <= 0f) return position;
            if (position < target) return MathF.Min(target, position + dt / MathF.Max(0.001f, upSeconds));
            if (position > target) return MathF.Max(target, position - dt / MathF.Max(0.001f, downSeconds));
            return position;
        }

        /// <summary>
        /// Round-robin variant pick (sfx-spec.md 0): never the previous variant, and not the one before it
        /// either when there are 4 or more. <paramref name="random01"/> is a uniform value in [0,1).
        /// </summary>
        /// <param name="count">Number of variants.</param>
        /// <param name="last">Index played last (-1 = none).</param>
        /// <param name="beforeLast">Index played before that (-1 = none).</param>
        /// <param name="random01">Random value in [0,1).</param>
        public static int PickVariant(int count, int last, int beforeLast, float random01)
        {
            if (count <= 1) return 0;
            bool skipTwo = count >= 4 && beforeLast >= 0 && beforeLast < count && beforeLast != last;
            bool skipLast = last >= 0 && last < count;
            int allowed = count - (skipLast ? 1 : 0) - (skipTwo ? 1 : 0);
            if (random01 < 0f || float.IsNaN(random01)) random01 = 0f;
            int k = Math.Min(allowed - 1, (int)(random01 * allowed));
            for (int i = 0; i < count; i++)
            {
                if (skipLast && i == last) continue;
                if (skipTwo && i == beforeLast) continue;
                if (k-- == 0) return i;
            }
            return 0;
        }
    }

    /// <summary>What a duck lowers (direction.md 3.2).</summary>
    public enum DuckTarget
    {
        Music,
        Ambience
    }

    /// <summary>
    /// Program-driven ducking (direction.md 3.2): every trigger is an attack/hold/release envelope; the
    /// deepest active envelope per target wins (no arithmetic stacking), clamped to a per-target floor
    /// (music -9 dB, rain -8 dB). Fixed capacity, no allocation after construction.
    /// </summary>
    public sealed class DuckResolver
    {
        private struct Duck
        {
            public DuckTarget Target;
            public float DepthDb;
            public float Attack;
            public float Hold;
            public float Release;
            public float Start;
            public bool Used;
        }

        private readonly Duck[] _ducks;
        private readonly float _musicFloorDb;
        private readonly float _ambienceFloorDb;

        /// <param name="capacity">Most envelopes alive at once; the one closest to its end is replaced beyond it.</param>
        /// <param name="musicFloorDb">Deepest total music duck (default -9 dB).</param>
        /// <param name="ambienceFloorDb">Deepest total ambience duck (default -8 dB).</param>
        public DuckResolver(int capacity = 16, float musicFloorDb = -9f, float ambienceFloorDb = -8f)
        {
            _ducks = new Duck[Math.Max(1, capacity)];
            _musicFloorDb = musicFloorDb;
            _ambienceFloorDb = ambienceFloorDb;
        }

        /// <summary>Starts a duck at time <paramref name="now"/> (seconds).</summary>
        /// <param name="depthDb">Reduction, e.g. -4 (positive values are treated as negative).</param>
        public void Add(DuckTarget target, float depthDb, float attack, float hold, float release, float now)
        {
            int slot = -1;
            float soonestEnd = float.PositiveInfinity;
            for (int i = 0; i < _ducks.Length; i++)
            {
                var d = _ducks[i];
                float end = d.Start + d.Attack + d.Hold + d.Release;
                if (!d.Used || end <= now) { slot = i; break; }
                if (end < soonestEnd) { soonestEnd = end; slot = i; }
            }
            _ducks[slot] = new Duck
            {
                Target = target,
                DepthDb = -MathF.Abs(depthDb),
                Attack = MathF.Max(0f, attack),
                Hold = MathF.Max(0f, hold),
                Release = MathF.Max(0f, release),
                Start = now,
                Used = true
            };
        }

        /// <summary>Drops every duck.</summary>
        public void Clear()
        {
            for (int i = 0; i < _ducks.Length; i++) _ducks[i].Used = false;
        }

        /// <summary>The current reduction of <paramref name="target"/> in dB (0 = none, never below its floor).</summary>
        public float Evaluate(DuckTarget target, float now)
        {
            float deepest = 0f;
            for (int i = 0; i < _ducks.Length; i++)
            {
                var d = _ducks[i];
                if (!d.Used || d.Target != target) continue;
                float v = Envelope(d.DepthDb, d.Attack, d.Hold, d.Release, now - d.Start);
                if (v < deepest) deepest = v;
            }
            float floor = target == DuckTarget.Music ? _musicFloorDb : _ambienceFloorDb;
            return MathF.Max(floor, deepest);
        }

        /// <summary>One attack/hold/release envelope at <paramref name="t"/> seconds after its start.</summary>
        public static float Envelope(float depthDb, float attack, float hold, float release, float t)
        {
            if (t < 0f) return 0f;
            if (t < attack) return depthDb * (t / attack);
            t -= attack;
            if (t < hold) return depthDb;
            t -= hold;
            if (t < release) return depthDb * (1f - t / release);
            return 0f;
        }
    }

    /// <summary>
    /// The tier the music follows (direction.md 2.2): rises at once, falls one tier only after the
    /// player has stayed below it for the hold time; the tier-up sting is gated to once per
    /// <c>stingInterval</c> seconds against flapping.
    /// </summary>
    public sealed class MusicTierTracker
    {
        private readonly float _holdSeconds;
        private readonly float _stingInterval;
        private float _hold;
        private float _sinceSting;

        /// <summary>The tier the music is heading to (1-3).</summary>
        public int Tier { get; private set; } = 1;

        public MusicTierTracker(float holdSeconds, float stingInterval)
        {
            _holdSeconds = holdSeconds;
            _stingInterval = stingInterval;
            _sinceSting = stingInterval;
        }

        /// <summary>Back to tier 1, sting ready.</summary>
        public void Reset()
        {
            Tier = 1;
            _hold = 0f;
            _sinceSting = _stingInterval;
        }

        /// <summary>Advances by <paramref name="dt"/> with the player's current tier.</summary>
        /// <returns>The new tier when the music rose and a sting should play, else 0.</returns>
        public int Update(int playerTier, float dt)
        {
            playerTier = AudioRules.ClampTier(playerTier);
            if (dt < 0f) dt = 0f;
            _sinceSting += dt;
            if (playerTier > Tier)
            {
                Tier = playerTier;
                _hold = _holdSeconds;
                if (_sinceSting >= _stingInterval)
                {
                    _sinceSting = 0f;
                    return Tier;
                }
                return 0;
            }
            if (playerTier == Tier)
            {
                _hold = _holdSeconds;
                return 0;
            }
            _hold -= dt;
            if (_hold <= 0f)
            {
                Tier--;
                _hold = _holdSeconds;
            }
            return 0;
        }
    }
}
