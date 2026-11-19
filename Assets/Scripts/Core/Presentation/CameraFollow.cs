using System;

namespace Redline.Core.Presentation
{
    /// <summary>
    /// Tuning of <see cref="CameraFollow"/> (Resources/CameraFollow.json; field names match the JSON keys). Distances in
    /// play-plane units, fractions of the visible picture, times in seconds. Defaults are the shipped values.
    /// </summary>
    [Serializable]
    public sealed class CameraFollowSettings
    {
        /// <summary>Half-width of the horizontal dead zone around the framing point: smaller moves leave the view still.</summary>
        public float deadZone = 0.6f;
        /// <summary>Largest look-ahead (camera minus player, at <see cref="leadFullSpeed"/>) as a fraction of the view width.</summary>
        public float leadFraction = 0.18f;
        /// <summary>|Vx| at which the look-ahead is at its largest; it scales linearly below.</summary>
        public float leadFullSpeed = 18f;
        /// <summary>|Vx| below which the player counts as not moving horizontally (no direction change).</summary>
        public float leadMinSpeed = 3f;
        /// <summary>Time constant of the look-ahead's own easing.</summary>
        public float leadSmoothTime = 0.35f;
        /// <summary>The look-ahead side flips only after moving the other way this long…</summary>
        public float flipTime = 0.25f;
        /// <summary>…or this far.</summary>
        public float flipDistance = 1.5f;
        /// <summary>Critically damped smoothing time, horizontal (SmoothDamp-style: about the time to cover most of a step).</summary>
        public float smoothTimeX = 0.25f;
        /// <summary>Critically damped smoothing time, vertical.</summary>
        public float smoothTimeY = 0.3f;
        /// <summary>Time constant of the low-pass on the followed velocity used as feed-forward (no lag at a steady speed).</summary>
        public float velocityFilterTime = 0.12f;
        /// <summary>Camera speed cap (units per second); only the safe frame may exceed it.</summary>
        public float maxSpeed = 45f;
        /// <summary>Grounded: the feet sit this fraction of the picture height above the bottom edge.</summary>
        public float groundFeetFraction = 0.30f;
        /// <summary>Airborne: the view holds still while the feet stay above this fraction and the head below 1 minus it.</summary>
        public float bandFraction = 0.25f;
        /// <summary>Falling faster than this (units per second) eases the view down so the landing shows.</summary>
        public float fallSpeed = 12f;
        /// <summary>Extra fall speed over which the feet target rises from the band edge to <see cref="fallFeetFraction"/>.</summary>
        public float fallRamp = 8f;
        /// <summary>Feet fraction (from the bottom) targeted at full fall speed.</summary>
        public float fallFeetFraction = 0.5f;
        /// <summary>Hard rule: the player's box stays at least this fraction of the picture inside every edge.</summary>
        public float safeFraction = 0.12f;
        /// <summary>A subject move larger than this in one update (units) is a teleport: the view cuts instead of easing.</summary>
        public float teleportDistance = 4f;
        /// <summary>Shake noise frequency (Hz).</summary>
        public float shakeFrequency = 9f;
    }

    /// <summary>What the follow camera tracks this frame: the player's feet (drawing position), velocity and box.</summary>
    public struct FollowSubject
    {
        /// <summary>Feet position (centre bottom of the box), drawing space.</summary>
        public float X, Y;
        /// <summary>Velocity (units per second).</summary>
        public float Vx, Vy;
        /// <summary>Box size.</summary>
        public float Width, Height;
        /// <summary>Standing on ground this frame.</summary>
        public bool Grounded;
    }

    /// <summary>
    /// The 3D view's follow camera (the one follow system for Render3D; the 2D view keeps RedlineGame's own). Produces
    /// the picture centre on the play plane each frame:
    /// <list type="bullet">
    /// <item>horizontal: a dead zone of ±<see cref="CameraFollowSettings.deadZone"/>, a speed-scaled look-ahead up to
    /// <see cref="CameraFollowSettings.leadFraction"/> of the width whose side flips only after a sustained reversal
    /// (time or distance hysteresis), so turns and hook swings do not whip the view;</item>
    /// <item>vertical, platformer style: grounded, the feet ease to <see cref="CameraFollowSettings.groundFeetFraction"/>;
    /// airborne, the view holds unless the box leaves the middle band; a fast fall eases the view down;</item>
    /// <item>critically damped smoothing (exact solution, frame-rate independent) with velocity feed-forward and a speed cap;</item>
    /// <item>level bounds (unless the level wraps horizontally), then the safe frame, which always wins;</item>
    /// <item>teleports and the first update cut without easing.</item>
    /// </list>
    /// Shake is separate (<see cref="ShakeOffset"/>) so the caller can drop it for Reduced Motion.
    /// </summary>
    /// <example>
    /// <code>
    /// var follow = new CameraFollow(settings);
    /// follow.SetView(640f / 48f, 320f / 48f);
    /// follow.SetBounds(-1f, map.Width + 1f, -1f, map.Height + 1f, map.WrapsX);
    /// follow.Update(new FollowSubject { X = feet.x, Y = feet.y, Vx = p.Vx, Vy = p.Vy, Width = p.Width, Height = p.Height, Grounded = p.Grounded }, dt);
    /// var (sx, sy) = reducedMotion ? (0f, 0f) : CameraFollow.ShakeOffset(shake, time, settings.shakeFrequency);
    /// rig.SetTarget(new Vector2(follow.X + sx, follow.Y + sy));
    /// </code>
    /// </example>
    /// <remarks>Pure, allocation-free, engine-free; unit tested in CameraFollowTests.</remarks>
    public sealed class CameraFollow
    {
        private readonly CameraFollowSettings _s;
        private float _viewW = 640f / 48f, _viewH = 320f / 48f;
        private float _minX = float.NegativeInfinity, _maxX = float.PositiveInfinity, _minY = float.NegativeInfinity, _maxY = float.PositiveInfinity;
        private bool _wrapX = true;
        private bool _started;
        private float _x, _y, _vx, _vy;
        private float _anchorX, _goalY;
        private float _lead, _leadTarget;
        private int _side;
        private float _revTime, _revDist;
        private float _ffx, _ffy;
        private float _prevSubX, _prevSubY;

        /// <summary>Creates a follow camera; the first <see cref="Update"/> places it without easing.</summary>
        public CameraFollow(CameraFollowSettings settings) { _s = settings ?? new CameraFollowSettings(); }

        /// <summary>Picture centre, play-plane units (no shake).</summary>
        public float X => _x;
        /// <summary>Picture centre, play-plane units (no shake).</summary>
        public float Y => _y;
        /// <summary>Camera velocity (units per second).</summary>
        public float VelocityX => _vx;
        /// <summary>Camera velocity (units per second).</summary>
        public float VelocityY => _vy;
        /// <summary>Current look-ahead side: +1 right, -1 left, 0 not chosen yet.</summary>
        public int LookSide => _side;
        /// <summary>Current look-ahead offset (units, signed).</summary>
        public float LookAhead => _lead;
        /// <summary>The settings in use.</summary>
        public CameraFollowSettings Settings => _s;

        /// <summary>Visible play-plane size (units). Call when the framing changes.</summary>
        public void SetView(float width, float height)
        {
            _viewW = Math.Max(0.1f, width);
            _viewH = Math.Max(0.1f, height);
        }

        /// <summary>Level extents the view stays inside; <paramref name="wrapX"/> leaves the horizontal axis free (ring levels).</summary>
        public void SetBounds(float minX, float maxX, float minY, float maxY, bool wrapX)
        {
            _minX = minX; _maxX = maxX; _minY = minY; _maxY = maxY; _wrapX = wrapX;
        }

        /// <summary>Forgets the motion state: the next <see cref="Update"/> cuts to the subject.</summary>
        public void Reset() => _started = false;

        /// <summary>Advances the camera by <paramref name="dt"/> seconds toward framing <paramref name="s"/>.</summary>
        public void Update(FollowSubject s, float dt)
        {
            float jump = MathF.Abs(s.X - _prevSubX) + MathF.Abs(s.Y - _prevSubY);
            _prevSubX = s.X; _prevSubY = s.Y;
            if (!_started || jump > _s.teleportDistance || dt <= 0f) { Snap(s); return; }

            // --- horizontal: look-ahead side with hysteresis, eased lead, dead zone.
            int dir = MathF.Abs(s.Vx) > _s.leadMinSpeed ? Math.Sign(s.Vx) : 0;
            if (_side == 0 && dir != 0) _side = dir;
            if (dir != 0 && dir != _side)
            {
                _revTime += dt;
                _revDist += MathF.Abs(s.Vx) * dt;
                if (_revTime >= _s.flipTime || _revDist >= _s.flipDistance) { _side = dir; _revTime = _revDist = 0f; }
            }
            else if (dir == _side && dir != 0) { _revTime = _revDist = 0f; }
            if (dir == 0 || dir == _side) _leadTarget = LeadFor(s.Vx);   // during a pending reversal the target holds
            _lead += (_leadTarget - _lead) * (1f - MathF.Exp(-dt / Math.Max(1e-3f, _s.leadSmoothTime)));
            float px = s.X;
            bool trackX = false;
            if (px - _anchorX > _s.deadZone) { _anchorX = px - _s.deadZone; trackX = true; }
            else if (_anchorX - px > _s.deadZone) { _anchorX = px + _s.deadZone; trackX = true; }
            float goalX = _anchorX + _lead;

            // --- vertical: ground framing, air band, fast-fall ease.
            bool trackY = false;
            if (s.Grounded)
            {
                _goalY = s.Y + (0.5f - _s.groundFeetFraction) * _viewH;
            }
            else
            {
                float lo = _goalY - 0.5f * _viewH + _s.bandFraction * _viewH;
                float hi = _goalY + 0.5f * _viewH - _s.bandFraction * _viewH;
                if (s.Y < lo) { _goalY = s.Y + (0.5f - _s.bandFraction) * _viewH; trackY = true; }
                else if (s.Y + s.Height > hi) { _goalY = s.Y + s.Height - (0.5f - _s.bandFraction) * _viewH; trackY = true; }
                float fall = -s.Vy;
                if (fall > _s.fallSpeed)
                {
                    float k = Clamp01((fall - _s.fallSpeed) / Math.Max(1e-3f, _s.fallRamp));
                    float feet = _s.bandFraction + (_s.fallFeetFraction - _s.bandFraction) * k;
                    float want = s.Y + (0.5f - feet) * _viewH;
                    if (want < _goalY) { _goalY = want; trackY = true; }
                }
            }
            float goalY = _goalY;
            ClampToBounds(ref goalX, ref goalY);

            // --- feed-forward velocity (the followed point's velocity while it is being tracked), low-passed.
            float a = 1f - MathF.Exp(-dt / Math.Max(1e-3f, _s.velocityFilterTime));
            _ffx += ((trackX ? s.Vx : 0f) - _ffx) * a;
            _ffy += ((trackY ? s.Vy : 0f) - _ffy) * a;
            if (goalX <= LoX() || goalX >= HiX()) _ffx = 0f;
            if (goalY <= LoY() || goalY >= HiY()) _ffy = 0f;

            float ox = _x, oy = _y;
            Spring(ref _x, ref _vx, goalX, _ffx, _s.smoothTimeX, dt);
            Spring(ref _y, ref _vy, goalY, _ffy, _s.smoothTimeY, dt);
            Cap(ref _x, ref _vx, ox, dt);
            Cap(ref _y, ref _vy, oy, dt);

            float bx = _x, by = _y;
            ClampToBounds(ref _x, ref _y);
            if (_x != bx) _vx = 0f;
            if (_y != by) _vy = 0f;
            SafeFrame(s, ox, oy, dt);
        }

        /// <summary>
        /// Smooth shake offset: two-axis value noise at <paramref name="frequency"/> Hz scaled by
        /// <paramref name="amplitude"/> (units, the caller decays it). Continuous in time, so it wobbles instead of
        /// jumping every frame; zero amplitude is exactly zero.
        /// </summary>
        public static (float x, float y) ShakeOffset(float amplitude, float time, float frequency)
        {
            if (amplitude <= 0f) return (0f, 0f);
            float t = time * frequency;
            return (amplitude * Noise(t, 17), amplitude * Noise(t, 91));
        }

        private float LeadFor(float vx)
        {
            if (_side == 0) return 0f;
            float k = Clamp01(MathF.Abs(vx) / Math.Max(1e-3f, _s.leadFullSpeed));
            // The dead zone eats deadZone of the offset while running, so the target includes it: at full speed the
            // camera sits leadFraction of the width ahead of the player.
            return _side * k * (_s.leadFraction * _viewW + _s.deadZone);
        }

        private void Snap(FollowSubject s)
        {
            _started = true;
            int dir = MathF.Abs(s.Vx) > _s.leadMinSpeed ? Math.Sign(s.Vx) : 0;
            if (dir != 0) _side = dir;
            _revTime = _revDist = 0f;
            _leadTarget = _lead = LeadFor(s.Vx);
            float visible = _lead - Math.Sign(_lead) * Math.Min(MathF.Abs(_lead), _s.deadZone);
            _anchorX = s.X + visible - _lead;
            _goalY = s.Y + (0.5f - _s.groundFeetFraction) * _viewH;
            _x = _anchorX + _lead;
            _y = _goalY;
            _vx = _vy = _ffx = _ffy = 0f;
            ClampToBounds(ref _x, ref _y);
            SafeFrame(s, _x, _y, 0f);
            _vx = _vy = 0f;
        }

        private void SafeFrame(FollowSubject s, float ox, float oy, float dt)
        {
            float mx = (0.5f - _s.safeFraction) * _viewW, my = (0.5f - _s.safeFraction) * _viewH;
            float minX = s.X + s.Width * 0.5f - mx, maxX = s.X - s.Width * 0.5f + mx;
            float minY = s.Y + s.Height - my, maxY = s.Y + my;
            float x = minX > maxX ? s.X : Math.Clamp(_x, minX, maxX);
            float y = minY > maxY ? s.Y + s.Height * 0.5f : Math.Clamp(_y, minY, maxY);
            if (x != _x) { _x = x; if (dt > 0f) _vx = (x - ox) / dt; }
            if (y != _y) { _y = y; if (dt > 0f) _vy = (y - oy) / dt; }
        }

        private float LoX() => _wrapX ? float.NegativeInfinity : _minX + _viewW * 0.5f;
        private float HiX() => _wrapX ? float.PositiveInfinity : _maxX - _viewW * 0.5f;
        private float LoY() => _minY + _viewH * 0.5f;
        private float HiY() => _maxY - _viewH * 0.5f;

        private void ClampToBounds(ref float x, ref float y)
        {
            if (!_wrapX)
            {
                float lo = LoX(), hi = HiX();
                x = lo > hi ? (_minX + _maxX) * 0.5f : Math.Clamp(x, lo, hi);
            }
            float ly = LoY(), hy = HiY();
            y = ly > hy ? (_minY + _maxY) * 0.5f : Math.Clamp(y, ly, hy);
        }

        /// <summary>
        /// Exact step of a critically damped spring toward a goal moving at <paramref name="goalVel"/>
        /// (ω = 2 / smoothTime, as SmoothDamp): no overshoot for a still goal, no steady lag for a steadily moving one.
        /// </summary>
        private static void Spring(ref float x, ref float v, float goal, float goalVel, float smoothTime, float dt)
        {
            float w = 2f / Math.Max(1e-3f, smoothTime);
            // goal is where the moving goal is at the end of the step; the error is measured from where it was at the start.
            float e = x - (goal - goalVel * dt), ev = v - goalVel;
            float c = ev + w * e;
            float ex = MathF.Exp(-w * dt);
            float e1 = (e + c * dt) * ex;
            float ev1 = (ev - c * w * dt) * ex;
            x = goal + e1;
            v = goalVel + ev1;
        }

        private void Cap(ref float x, ref float v, float old, float dt)
        {
            if (MathF.Abs(v) <= _s.maxSpeed) return;
            v = Math.Sign(v) * _s.maxSpeed;
            float step = x - old, lim = _s.maxSpeed * dt;
            if (MathF.Abs(step) > lim) x = old + Math.Sign(step) * lim;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        /// <summary>1D value noise in [-1, 1]: hashed lattice values, smoothstep between them.</summary>
        private static float Noise(float t, int seed)
        {
            float f = MathF.Floor(t);
            int i = (int)f;
            float u = t - f;
            u = u * u * (3f - 2f * u);
            float a = Hash(i, seed), b = Hash(i + 1, seed);
            return a + (b - a) * u;
        }

        private static float Hash(int i, int seed)
        {
            unchecked
            {
                uint h = (uint)i * 374761393u + (uint)seed * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0xFFFFFF * 2f - 1f;
            }
        }
    }
}
