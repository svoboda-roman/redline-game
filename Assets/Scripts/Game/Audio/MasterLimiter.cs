using System;
using UnityEngine;

namespace Redline.Game.Audio
{
    /// <summary>
    /// Peak limiter on the final mix. Added by <see cref="RedlineAudio"/> to the GameObject that
    /// carries the AudioListener, so <see cref="OnAudioFilterRead"/> sees everything that plays and
    /// the output never exceeds <see cref="AudioMix.LimiterThreshold"/>.
    /// </summary>
    public sealed class MasterLimiter : MonoBehaviour
    {
        private PeakLimiter _limiter;
        private volatile float _release = 0.0003f;
        private int _rate;

        private void Awake() => UpdateRate();

        private void Update() => UpdateRate();

        private void UpdateRate()
        {
            int rate = AudioSettings.outputSampleRate;
            if (rate == _rate || rate <= 0) return;
            _rate = rate;
            _release = 1f - MathF.Exp(-1f / (AudioMix.LimiterReleaseSeconds * rate));
        }

        // Audio thread: no Unity API calls here.
        private void OnAudioFilterRead(float[] data, int channels) =>
            _limiter.Process(data, channels, AudioMix.LimiterThreshold, _release);
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
