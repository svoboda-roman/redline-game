using UnityEngine;

namespace Redline.Game.Atmosphere
{
    /// <summary>
    /// A horizontally repeating strip (skyline layer or fog band) drawn as two side-by-side copies
    /// of one sprite. Its content origin moves with <c>camera.x × followX + drift</c>; the left copy
    /// snaps by whole periods so the pair always covers the view. Because the position is a
    /// continuous function of the camera, there is no seam or pop — including on ring levels, where
    /// the camera X keeps growing while the terrain copies jump by whole level widths.
    /// </summary>
    /// <example>
    /// <code>
    /// var strip = new ParallaxStrip(root, "Far", sprite, period: 64f, followX: 0.94f, followY: 0.96f,
    ///     baseY: -5.5f, levelMidY: levelMid, order: -40, color: Color.white);
    /// strip.Update(camera.transform.position, Time.deltaTime);
    /// </code>
    /// </example>
    /// <remarks>Main-thread only. Update does not allocate.</remarks>
    public sealed class ParallaxStrip
    {
        private readonly Transform _a, _b;
        private readonly float _period, _followX, _followY, _baseY, _levelMidY, _driftSpeed;
        private float _drift;
        private float _left;

        /// <summary>World X where the left copy currently starts (for things placed in strip space).</summary>
        public float Start { get; private set; }

        /// <summary>World Y of the strip's bottom edge.</summary>
        public float Bottom { get; private set; }

        /// <summary>Repeat period in world units.</summary>
        public float Period => _period;

        /// <summary>Content X origin (strip space 0) in world X this frame.</summary>
        public float Origin { get; private set; }

        /// <summary>Builds the two copies. <paramref name="baseY"/> is the strip bottom relative to the level middle when the camera is there. The sprite's pivot must be bottom-left and its width = <paramref name="period"/> at <paramref name="scale"/>.</summary>
        public ParallaxStrip(Transform parent, string name, Sprite sprite, float period, float followX, float followY,
            float baseY, float levelMidY, int order, Color color, Vector3 scale = default, float driftSpeed = 0f)
        {
            _period = period;
            _followX = followX;
            _followY = followY;
            _baseY = baseY;
            _levelMidY = levelMidY;
            _driftSpeed = driftSpeed;
            if (scale == default) scale = Vector3.one;
            _a = SpriteFactory.Create(name, parent, sprite, color, order).transform;
            _b = SpriteFactory.Create(name, parent, sprite, color, order).transform;
            _a.localScale = scale;
            _b.localScale = scale;
        }

        /// <summary>Repositions both copies for the camera; drifts the content when a drift speed is set.</summary>
        public void Update(Vector3 camera, float dt)
        {
            _drift = Mathf.Repeat(_drift + _driftSpeed * dt, _period);
            float origin = camera.x * _followX + _drift;
            float left = camera.x - AtmosphereConfig.ViewHalfWidth;
            float start = origin + _period * Mathf.Floor((left - origin) / _period);
            float y = _levelMidY + (camera.y - _levelMidY) * _followY + _baseY;
            Origin = origin;
            _left = left;
            Start = start;
            Bottom = y;
            _a.localPosition = new Vector3(start, y, 0f);
            _b.localPosition = new Vector3(start + _period, y, 0f);
        }

        /// <summary>
        /// Maps a strip-space X (0..Period) to the world X of the copy that is on screen, or NaN
        /// when neither copy shows it.
        /// </summary>
        public float ToWorldX(float contentX)
        {
            float x = _left + Mathf.Repeat(Origin + contentX - _left, _period);
            return x <= _left + AtmosphereConfig.ViewHalfWidth * 2f ? x : float.NaN;
        }
    }
}
