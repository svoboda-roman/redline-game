using System;
using System.Globalization;
using System.IO;
using System.Text;
using Redline.Game.Render3D;
using UnityEngine;

namespace Redline.Game
{
    /// <summary>
    /// Dev-only camera-follow trace. Inactive unless the environment variable <c>REDLINE_CAMTRACE</c> names an output
    /// directory. Every frame (LateUpdate, after the game placed the camera and the actors) it records the player's bounding
    /// box as fractions of the visible 3D picture, the exact camera position (texel snap plus remainder), its velocity and
    /// acceleration, and the camera-minus-player horizontal offset (the look-ahead). Writes <c>camtrace-&lt;level&gt;.csv</c>
    /// and logs one <c>[CamTrace]</c> summary per level: % of frames with the player within <see cref="EdgeFraction"/> of a
    /// picture edge, mean |acceleration|, and look-ahead side flips per minute. With <c>REDLINE_CAMTRACE_SHOTS</c> set it
    /// also saves every <see cref="ShotEvery"/>th low-res frame of the first level as PNG (evidence strips).
    /// </summary>
    /// <remarks>Used with <see cref="AutoPlay"/>; never runs in normal play. Frames where the camera jumps more than
    /// <see cref="TeleportDistance"/> (level loads, wraps) are left out of the motion statistics.</remarks>
    /// <example>REDLINE_CAMTRACE=/abs/dir REDLINE_AUTOPLAY=Loop01,Loop02,Loop03 ./Redline</example>
    public sealed class CameraTrace : MonoBehaviour
    {
        private const string EnvVar = "REDLINE_CAMTRACE";
        /// <summary>The safe-frame margin measured (fraction of the picture per edge).</summary>
        public const float EdgeFraction = 0.12f;
        /// <summary>A per-frame camera move larger than this (units) is a cut, not motion.</summary>
        public const float TeleportDistance = 3f;
        /// <summary>The look-ahead side counts only beyond this offset (units), so hovering around zero is not a flip.</summary>
        public const float FlipDeadband = 0.8f;
        /// <summary>Frames between evidence shots.</summary>
        public const int ShotEvery = 6;

        private static CameraTrace _instance;
        private RedlineGame _game;
        private string _dir;
        private string _level;
        private StringBuilder _csv;
        private int _frames, _edgeFrames, _accFrames, _flips, _side, _shotIndex;
        private double _accSum, _seconds;
        private Vector2 _prevPos, _prevVel;
        private int _have;
        private bool _shots;
        private Texture2D _tmp;

        /// <summary>The running trace, or null.</summary>
        public static CameraTrace Instance => _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string dir = Environment.GetEnvironmentVariable(EnvVar);
            if (string.IsNullOrEmpty(dir)) return;
            var go = new GameObject("CameraTrace");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<CameraTrace>();
            _instance._dir = dir;
            _instance._shots = Environment.GetEnvironmentVariable("REDLINE_CAMTRACE_SHOTS") != null;
            Directory.CreateDirectory(dir);
        }

        private void LateUpdate()
        {
            if (_game == null) _game = FindAnyObjectByType<RedlineGame>();
            if (_game == null || _game.Sim == null) return;
            if (AutoPlay.ActiveLevel == null) return;   // only while the autoplay drives a level
            if (_level != AutoPlay.ActiveLevel) { Flush(); Begin(AutoPlay.ActiveLevel); }
            var rig = _game.Render3DActive ? FindAnyObjectByType<Pixel3DCamera>() : null;
            var cam = _game.Camera;
            if (cam == null) return;
            var p = _game.Sim.Player;
            var feet = _game.ToDraw(new Vector2(p.X, p.Y));
            // Picture fractions of the bounding box (3D: the world viewport includes a one-texel guard on each side).
            float x0 = 1f, x1 = 0f, y0 = 1f, y1 = 0f;
            for (int i = 0; i < 4; i++)
            {
                var w = new Vector3(feet.x + ((i & 1) == 0 ? -0.5f : 0.5f) * p.Width, feet.y + ((i & 2) == 0 ? 0f : p.Height), 0f);
                var v = cam.WorldToViewportPoint(w);
                if (rig != null)
                {
                    v.x = (v.x * (rig.Width + 2f) - 1f) / rig.Width;
                    v.y = (v.y * (rig.Height + 2f) - 1f) / rig.Height;
                }
                x0 = Mathf.Min(x0, v.x); x1 = Mathf.Max(x1, v.x); y0 = Mathf.Min(y0, v.y); y1 = Mathf.Max(y1, v.y);
            }
            // Exact (unsnapped) camera point on the play plane.
            Vector2 pos;
            if (rig != null)
            {
                float texel = rig.FramingHeight / rig.Height;
                float texelY = texel / Mathf.Max(0.5f, Mathf.Cos(rig.Pitch * Mathf.Deg2Rad));
                var c = rig.CameraPosition + rig.WorldCamera.transform.forward * rig.PlaneDistance;
                pos = new Vector2(c.x + rig.Remainder.x * texel, c.y + rig.Remainder.y * texelY);
            }
            else pos = cam.transform.position;
            float dt = Mathf.Max(1e-4f, Time.deltaTime);
            bool cut = _have > 0 && (pos - _prevPos).magnitude > TeleportDistance;
            Vector2 vel = _have > 0 && !cut ? (pos - _prevPos) / dt : Vector2.zero;
            Vector2 acc = _have > 1 && !cut ? (vel - _prevVel) / dt : Vector2.zero;
            if (cut) _have = 0;
            bool edge = x0 < EdgeFraction || x1 > 1f - EdgeFraction || y0 < EdgeFraction || y1 > 1f - EdgeFraction;
            _frames++;
            if (edge) _edgeFrames++;
            if (_have > 1) { _accSum += acc.magnitude; _accFrames++; }
            float lead = pos.x - feet.x;
            // The look-ahead side: CameraFollow's own when it drives the view, else the camera-minus-player sign.
            var follow = _game.Follow3D;
            int side = follow != null ? follow.LookSide : lead > FlipDeadband ? 1 : lead < -FlipDeadband ? -1 : 0;
            if (side != 0 && _side != 0 && side != _side) _flips++;
            if (side != 0) _side = side;
            _seconds += dt;
            _csv.Append(string.Format(CultureInfo.InvariantCulture,
                "{0:F3},{1:F3},{2:F3},{3:F3},{4:F3},{5:F3},{6:F3},{7:F2},{8:F2},{9:F1},{10:F1},{11:F3},{12},{13},{14:F2},{15:F2}\n",
                _seconds, x0, x1, y0, y1, pos.x, pos.y, vel.x, vel.y, acc.x, acc.y, lead, p.Grounded ? 1 : 0, p.Hooked ? 1 : 0, p.Vx, p.Vy));
            _prevVel = vel; _prevPos = pos; _have = Mathf.Min(2, _have + 1);
            if (_shots && rig != null && _level == "Loop01" && _frames % ShotEvery == 0) Shot(rig, p);
        }

        private void Shot(Pixel3DCamera rig, Redline.Core.Player p)
        {
            var rt = rig.Target;
            if (_tmp == null || _tmp.width != rt.width) _tmp = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            rig.WorldCamera.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            _tmp.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            _tmp.Apply();
            RenderTexture.active = prev;
            string state = p.Hooked ? "hook" : !p.Grounded ? "air" : Mathf.Abs(p.Vx) > 1f ? "run" : "idle";
            File.WriteAllBytes(Path.Combine(_dir, string.Format(CultureInfo.InvariantCulture, "shot-{0:D4}-{1}.png", _shotIndex++, state)), _tmp.EncodeToPNG());
        }

        private void Begin(string level)
        {
            _level = level;
            _csv = new StringBuilder("t,x0,x1,y0,y1,camX,camY,velX,velY,accX,accY,lead,grounded,hooked,pvx,pvy\n");
            _frames = _edgeFrames = _accFrames = _flips = _side = _have = 0;
            _accSum = _seconds = 0;
        }

        /// <summary>Writes the current level's CSV and logs its summary (called on level change and before quitting).</summary>
        public void Flush()
        {
            if (_level == null || _csv == null || _frames == 0) return;
            File.WriteAllText(Path.Combine(_dir, "camtrace-" + _level + ".csv"), _csv.ToString());
            double minutes = Math.Max(1e-6, _seconds / 60.0);
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[CamTrace] level={0} frames={1} seconds={2:F1} edgePct={3:F2} meanAcc={4:F2} flipsPerMin={5:F2}",
                _level, _frames, _seconds, 100.0 * _edgeFrames / _frames, _accSum / Math.Max(1, _accFrames), _flips / minutes));
            _level = null;
        }

        private void OnDestroy()
        {
            Flush();
            if (_tmp != null) Destroy(_tmp);
            if (_instance == this) _instance = null;
        }
    }
}
