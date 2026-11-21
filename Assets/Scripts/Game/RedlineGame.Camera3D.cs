using Redline.Core;
using Redline.Core.Presentation;
using UnityEngine;

namespace Redline.Game
{
    /// <summary>The 3D view's camera follow (CameraFollow, tuned in Resources/CameraFollow.json). The 2D view keeps UpdateCamera.</summary>
    public sealed partial class RedlineGame
    {
        private CameraFollow _follow3D;
        private Vector2 _follow3DAim;

        /// <summary>The 3D follow camera (null until the first 3D frame); read by the dev CameraTrace.</summary>
        internal CameraFollow Follow3D => _follow3D;

        private static CameraFollowSettings LoadFollowSettings()
        {
            var s = new CameraFollowSettings();
            var asset = Resources.Load<TextAsset>("CameraFollow");
            if (asset == null) Debug.LogWarning("Redline: Resources/CameraFollow.json not found, using built-in camera defaults.");
            else JsonUtility.FromJsonOverwrite(asset.text, s);
            return s;
        }

        /// <summary>
        /// One follow system for the 3D view: CameraFollow frames the interpolated player (dead zone, look-ahead with
        /// hysteresis, platformer vertical, 12 % safe frame, level bounds by the 3D view size); shake is smooth noise,
        /// off under Reduced Motion. The rig only snaps and presents.
        /// </summary>
        private void UpdateCamera3D(float dt)
        {
            var p = Sim.Player;
            var map = Sim.Map;
            if (_follow3D == null) _follow3D = new CameraFollow(LoadFollowSettings());
            // Someone else (menu backdrop, capture) aimed the rig since our last frame: cut instead of easing from there.
            if (_pixelCam.Aim != _follow3DAim) _follow3D.Reset();
            _follow3D.SetView(_pixelCam.FramingHeight * _pixelCam.Width / _pixelCam.Height, _pixelCam.FramingHeight);
            _follow3D.SetBounds(-1f, map.Width + 1f, -1f, map.Height + 1f, map.WrapsX);
            // The drawn (interpolated) feet, in drawing space near the camera on ring levels.
            var now = new Vector2(p.X, p.Y);
            var feet = _prevRunId == Sim.RunId ? Blend(_prevPlayerFeet, now, Mathf.Clamp01(_accumulator / RedlineSim.FixedDt)) : now;
            feet.x = _camX + map.Dx(_camX, feet.x);
            StoryBossFraming(ref feet, dt);   // boss fight: wider view between the player and Big Tentacle Bill
            _follow3D.Update(new FollowSubject
            {
                X = feet.x, Y = feet.y, Vx = p.Vx, Vy = p.Vy, Width = p.Width, Height = p.Height, Grounded = p.Grounded
            }, dt);
            _camX = _follow3D.X;
            _camY = _follow3D.Y;
            _shake = Mathf.Max(0f, _shake - dt * 1.5f);
            var (sx, sy) = UiTheme.ReducedMotion ? (0f, 0f) : CameraFollow.ShakeOffset(_shake, Time.time, _follow3D.Settings.shakeFrequency);
            _camera.transform.position = new Vector3(_camX + sx, _camY + sy, -10f);   // the audio listener rides the 2D camera
            _follow3DAim = new Vector2(_camX + sx, _camY + sy);
            _pixelCam.SetTarget(_follow3DAim);
        }
    }
}
