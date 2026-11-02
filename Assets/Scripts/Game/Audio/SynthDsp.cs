using System;

namespace Redline.Game.Audio
{
    /// <summary>
    /// Allocation-free DSP helpers shared by <see cref="SynthClips"/> and <see cref="MusicSequencer"/>.
    /// Pure C# with no Unity calls, so it is safe on the audio thread.
    /// </summary>
    public static class Dsp
    {
        /// <summary>2 pi.</summary>
        public const float TwoPi = 6.2831853f;

        /// <summary>Frequency in Hz of a MIDI note number (69 = A4 = 440 Hz).</summary>
        public static float MidiToHz(float note) => 440f * MathF.Pow(2f, (note - 69f) / 12f);

        /// <summary>PolyBLEP residual that removes most aliasing from a hard edge at phase 0.</summary>
        public static float PolyBlep(float t, float dt)
        {
            if (t < dt)
            {
                t /= dt;
                return t + t - t * t - 1f;
            }
            if (t > 1f - dt)
            {
                t = (t - 1f) / dt;
                return t * t + t + t + 1f;
            }
            return 0f;
        }

        /// <summary>Smooth saturation: about linear near 0, reaches +/-1 at +/-3.</summary>
        public static float SoftClip(float x)
        {
            if (x >= 3f) return 1f;
            if (x <= -3f) return -1f;
            float x2 = x * x;
            return x * (27f + x2) / (27f + 9f * x2);
        }

        /// <summary>Exponential decay with time constant <paramref name="tau"/> seconds.</summary>
        public static float Decay(float t, float tau) => MathF.Exp(-t / tau);

        /// <summary>Linear attack to 1 over <paramref name="attack"/> seconds, then exponential decay.</summary>
        public static float AttackDecay(float t, float attack, float tau) =>
            t < attack ? t / attack : MathF.Exp(-(t - attack) / tau);

        /// <summary>A value gliding exponentially from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static float Glide(float from, float to, float t, float tau) => to + (from - to) * MathF.Exp(-t / tau);

        /// <summary>Hermite smoothstep of a 0-1 value, for perceptually smooth fades.</summary>
        public static float Smooth(float x) => x * x * (3f - 2f * x);
    }

    /// <summary>Deterministic xorshift noise source.</summary>
    public struct Rng
    {
        private uint _s;

        /// <summary>Creates a generator; seed 0 is replaced by a fixed non-zero seed.</summary>
        public Rng(uint seed)
        {
            _s = seed == 0 ? 0x9E3779B9u : seed;
        }

        /// <summary>Next value in [-1, 1).</summary>
        public float Next()
        {
            if (_s == 0) _s = 0x9E3779B9u;
            _s ^= _s << 13;
            _s ^= _s >> 17;
            _s ^= _s << 5;
            return (_s >> 8) * (2f / 16777216f) - 1f;
        }
    }

    /// <summary>Phase-accumulating oscillator with band-limited saw and pulse.</summary>
    public struct Osc
    {
        /// <summary>Current phase in [0, 1).</summary>
        public float Phase;

        /// <summary>Next sample of a band-limited sawtooth.</summary>
        public float Saw(float hz, float rate)
        {
            float dt = hz / rate;
            float v = 2f * Phase - 1f - Dsp.PolyBlep(Phase, dt);
            Advance(dt);
            return v;
        }

        /// <summary>Next sample of a band-limited pulse with duty cycle <paramref name="width"/>.</summary>
        public float Square(float hz, float rate, float width)
        {
            float dt = hz / rate;
            float v = Phase < width ? 1f : -1f;
            v += Dsp.PolyBlep(Phase, dt);
            float t2 = Phase - width;
            if (t2 < 0f) t2 += 1f;
            v -= Dsp.PolyBlep(t2, dt);
            Advance(dt);
            return v;
        }

        /// <summary>Next sample of a sine.</summary>
        public float Sine(float hz, float rate)
        {
            float v = MathF.Sin(Dsp.TwoPi * Phase);
            Advance(hz / rate);
            return v;
        }

        /// <summary>Next sample of a (naive) triangle.</summary>
        public float Triangle(float hz, float rate)
        {
            float v = 4f * MathF.Abs(Phase - 0.5f) - 1f;
            Advance(hz / rate);
            return v;
        }

        private void Advance(float dt)
        {
            Phase += dt;
            if (Phase >= 1f) Phase -= MathF.Floor(Phase);
        }
    }

    /// <summary>
    /// Chamberlin state-variable filter, run twice per sample for stability up to about a quarter
    /// of the sample rate. Read <see cref="Low"/>, <see cref="Band"/> or <see cref="High"/> after
    /// <see cref="Process"/>.
    /// </summary>
    public struct Svf
    {
        /// <summary>Low-pass output.</summary>
        public float Low;
        /// <summary>Band-pass output.</summary>
        public float Band;
        /// <summary>High-pass output.</summary>
        public float High;

        /// <summary>Filters one sample. <paramref name="damping"/>: 1.4 = soft, 0.1 = very resonant. Returns <see cref="Low"/>.</summary>
        public float Process(float x, float cutoff, float damping, float rate)
        {
            float fc = MathF.Max(20f, MathF.Min(cutoff, rate * 0.23f));
            float f = 2f * MathF.Sin(MathF.PI * fc / (2f * rate));
            float q = MathF.Max(0.08f, MathF.Min(damping, 1.6f));
            for (int k = 0; k < 2; k++)
            {
                Low += f * Band;
                High = x - Low - q * Band;
                Band += f * High;
            }
            return Low;
        }
    }

    /// <summary>
    /// Zero-latency peak limiter for the final mix: instant attack, exponential release, then a hard
    /// clamp to [-1, 1]. Mutated only by the thread that calls <see cref="Process"/>.
    /// </summary>
    public struct PeakLimiter
    {
        private float _gain;
        private bool _started;

        /// <summary>Lowest gain applied so far (1 = never limited). For diagnostics.</summary>
        public float MinGain;

        /// <summary>Limits an interleaved buffer in place.</summary>
        /// <param name="data">Interleaved samples.</param>
        /// <param name="channels">Channel count.</param>
        /// <param name="threshold">Output peak ceiling (linear).</param>
        /// <param name="release">Per-sample release coefficient, 1 - exp(-1 / (seconds * rate)).</param>
        public void Process(float[] data, int channels, float threshold, float release)
        {
            if (!_started)
            {
                _gain = 1f;
                MinGain = 1f;
                _started = true;
            }
            if (channels < 1) channels = 1;
            for (int i = 0; i + channels <= data.Length; i += channels)
            {
                float peak = 0f;
                for (int c = 0; c < channels; c++)
                {
                    float x = data[i + c];
                    if (float.IsNaN(x) || float.IsInfinity(x))
                    {
                        x = 0f;
                        data[i + c] = 0f;
                    }
                    float a = MathF.Abs(x);
                    if (a > peak) peak = a;
                }
                float wanted = peak > threshold ? threshold / peak : 1f;
                if (wanted < _gain) _gain = wanted;
                else _gain += (wanted - _gain) * release;
                if (_gain < MinGain) MinGain = _gain;
                for (int c = 0; c < channels; c++)
                {
                    float y = data[i + c] * _gain;
                    data[i + c] = y > 1f ? 1f : y < -1f ? -1f : y;
                }
            }
        }
    }
}
