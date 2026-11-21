using System;
using System.Collections;
using System.Reflection;
using Redline.Core;
using UnityEngine;

namespace Redline.Game.Audio
{
    /// <summary>
    /// Dev-only Play Mode check of the music and SFX routing. Active only when the environment
    /// variable REDLINE_AUDIO_PROBE is set: drives <see cref="RedlineGame"/> through the main menu,
    /// Loop01 at tier 1 and tier 3 (player invulnerable) and a game over, logging the music state
    /// ("PROBE ...") while <see cref="RedlineAudio.LogEnabled"/> logs every effect, then exits.
    /// </summary>
    /// <example>
    /// REDLINE_AUDIO_PROBE=1 REDLINE_AUDIO_LOG=1 Unity -batchmode -projectPath . -executeMethod Redline.Game.RedlineCapture.RunFromCommandLine
    /// </example>
    /// <remarks>Never runs in normal play.</remarks>
    public sealed class AudioProbe : MonoBehaviour
    {
        private RedlineGame _game;
        private RedlineAudio _audio;
        private float _vx;
        private bool _drive;
        private int _dir = 1;
        private float _lastX, _stuck;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REDLINE_AUDIO_PROBE"))) return;
            var go = new GameObject("AudioProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<AudioProbe>();
        }

        private IEnumerator Start()
        {
            for (int i = 0; i < 3; i++) yield return null;
            _game = FindAnyObjectByType<RedlineGame>();
            _audio = _game != null ? _game.GetComponent<RedlineAudio>() : null;
            if (_audio == null) { Debug.LogError("PROBE no RedlineGame/RedlineAudio"); Exit(1); yield break; }
            Debug.Log("PROBE audio output rate=" + AudioSettings.outputSampleRate + " driver=" + AudioSettings.driverCapabilities);
            Call("ShowMainMenu", new object[] { null });
            yield return Wait(2.5f);
            Debug.Log("PROBE menu | " + _audio.DescribeMusic());
            Call("StartRun");
            float guard = 15f;
            while (_game.Sim.State != SimState.Fighting && guard > 0f) { guard -= Time.unscaledDeltaTime; yield return null; }
            _vx = 5f;
            _drive = true;
            yield return Wait(5f);
            Debug.Log("PROBE " + _game.LevelName + " playerTier=" + _game.Sim.PlayerTier + " | " + _audio.DescribeMusic());
            _vx = _game.Sim.Config.tier3Speed + 3f;
            yield return Wait(6f);
            Debug.Log("PROBE " + _game.LevelName + " playerTier=" + _game.Sim.PlayerTier + " | " + _audio.DescribeMusic());
            _drive = false;
            _game.Sim.Player.InvulnTimer = 0f;
            _game.Sim.Player.Hp = 0;
            yield return Wait(2f);
            Debug.Log("PROBE gameover state=" + _game.Sim.State + " | " + _audio.DescribeMusic());
            Exit(0);
        }

        private void Update() => Drive();
        private void LateUpdate() => Drive();

        private void Drive()
        {
            if (!_drive || _game == null || _game.Sim == null) return;
            var p = _game.Sim.Player;
            p.InvulnTimer = 1f;
            _stuck = Mathf.Abs(p.X - _lastX) < 0.01f ? _stuck + Time.deltaTime : 0f;
            _lastX = p.X;
            if (_stuck > 0.15f) { _dir = -_dir; _stuck = 0f; }
            p.Vx = _dir * _vx;
            p.Facing = _dir;
        }

        private void Call(string method, object[] args = null)
        {
            var m = typeof(RedlineGame).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (m == null) { Debug.LogError("PROBE missing method " + method); return; }
            m.Invoke(_game, args);
        }

        private static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
        }

        private static void Exit(int code)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit(code);
#endif
        }
    }
}
