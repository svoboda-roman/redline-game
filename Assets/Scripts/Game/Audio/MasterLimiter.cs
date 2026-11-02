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
}
