using System;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Redline.Game
{
    /// <summary>
    /// Dev-only frame-time probe. Inactive unless the environment variable REDLINE_PERF is set.
    /// When set, it turns vsync off and uncaps the frame rate (so the numbers show headroom, not the
    /// 60 Hz cap), then logs wall-clock frame time every <see cref="WindowSeconds"/> seconds:
    /// <code>[Perf] scene=... frames=312 avg=5.21ms p95=7.80ms max=12.4ms fps=192</code>
    /// Uses a Stopwatch rather than Time.unscaledDeltaTime, which capture modes can fix to 1/60 s.
    /// </summary>
    /// <example>REDLINE_PERF=1 ./Redline -logFile perf.log   then   grep "\[Perf\]" perf.log</example>
    /// <remarks>Main-thread only. Allocates only when it logs (once per window), never per frame.</remarks>
    public sealed class PerfProbe : MonoBehaviour
    {
        /// <summary>Length of one reporting window in seconds.</summary>
        public const float WindowSeconds = 5f;

        private const int MaxSamples = 4096;
        private readonly float[] _ms = new float[MaxSamples];
        private readonly float[] _sorted = new float[MaxSamples];
        private readonly Stopwatch _clock = new Stopwatch();
        private int _count;
        private double _windowStart, _last;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REDLINE_PERF"))) return;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            var go = new GameObject("PerfProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<PerfProbe>();
            Debug.Log("[Perf] probe active (vsync off, uncapped)");
        }

        private void Awake()
        {
            _clock.Start();
            _last = _windowStart = _clock.Elapsed.TotalSeconds;
        }

        private void Update()
        {
            double now = _clock.Elapsed.TotalSeconds;
            if (_count < MaxSamples) _ms[_count++] = (float)((now - _last) * 1000.0);
            _last = now;
            if (now - _windowStart < WindowSeconds || _count == 0) return;

            float sum = 0f, max = 0f;
            for (int i = 0; i < _count; i++) { sum += _ms[i]; if (_ms[i] > max) max = _ms[i]; }
            Array.Copy(_ms, _sorted, _count);
            Array.Sort(_sorted, 0, _count);
            float p95 = _sorted[Mathf.Min(_count - 1, (int)(_count * 0.95f))];
            float avg = sum / _count;
            Debug.Log($"[Perf] scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} frames={_count} " +
                      $"avg={avg:F2}ms p95={p95:F2}ms max={max:F1}ms fps={1000f / avg:F0}");
            _count = 0;
            _windowStart = now;
        }
    }
}
