using Redline.Core;
using UnityEngine;
using C = Redline.Game.Atmosphere.AtmosphereConfig;

namespace Redline.Game.Atmosphere
{
    /// <summary>
    /// Screen-covering slanted rain in three depth layers (far: behind the terrain, short and dim;
    /// near: in front of the actors, long, bright and fast) plus small splash flickers on exposed
    /// tile tops near the camera. Streaks live in camera space and wrap around the view, so the
    /// rain follows the camera; each layer counter-scrolls a little against camera motion for depth.
    /// </summary>
    /// <example>
    /// <code>
    /// var rain = new RainField(root, map);
    /// rain.Update(camera.transform.position, Time.deltaTime);   // every frame
    /// </code>
    /// </example>
    /// <remarks>
    /// Main-thread only. Fixed pools (220 streaks + 28 splashes) built in the constructor; Update
    /// does not allocate. Unlit, alpha-blended thin quads: overdraw is a few percent of the screen.
    /// </remarks>
    public sealed class RainField
    {
        private const float WrapMargin = 1.5f;
        private const int SplashTries = 6;

        private readonly SpriteRenderer[] _streaks;
        private readonly Transform[] _streakT;
        private readonly Vector2[] _rel;
        private readonly int[] _layerOf;
        private readonly Vector2[] _velocity = new Vector2[3];

        private readonly SpriteRenderer[] _splash = new SpriteRenderer[C.SplashPool];
        private readonly float[] _splashAge = new float[C.SplashPool];
        private int _nextSplash;
        private float _splashDebt;

        // Exposed tile tops per column: _tops[_columnStart[x] .. _columnStart[x+1]) are y values.
        private readonly int[] _columnStart;
        private readonly int[] _tops;
        private readonly int _width;
        private readonly bool _wraps;

        // Lamp cone apexes (map space): streaks inside a cone turn warm and bright.
        private readonly Vector2[] _cones;
        private readonly bool[] _inCone;
        private readonly Color[] _baseColor = new Color[3];
        private readonly float _coneSlope = Mathf.Tan(C.LampConeHalfAngle * Mathf.Deg2Rad);

        private Vector3 _lastCamera;
        private bool _hasCamera;

        /// <summary>Builds the pools and indexes the map's exposed tile tops for splashes.</summary>
        public RainField(Transform root, TileMap map, Vector2[] coneApexes)
        {
            _cones = coneApexes ?? new Vector2[0];
            int total = 0;
            for (int l = 0; l < 3; l++) total += C.RainCount[l];
            _streaks = new SpriteRenderer[total];
            _streakT = new Transform[total];
            _rel = new Vector2[total];
            _layerOf = new int[total];
            _inCone = new bool[total];

            float slant = C.RainSlant * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Sin(slant), -Mathf.Cos(slant));
            var rng = new AtmoArt.Rng(911);
            int k = 0;
            for (int l = 0; l < 3; l++)
            {
                _velocity[l] = dir * C.RainSpeed[l];
                var color = C.RainColor;
                color.a = C.RainAlpha[l];
                _baseColor[l] = color;
                for (int i = 0; i < C.RainCount[l]; i++, k++)
                {
                    var sr = SpriteFactory.Create("Rain", root, AtmoArt.Rain, color, C.RainOrder[l]);
                    var t = sr.transform;
                    t.localRotation = Quaternion.Euler(0f, 0f, C.RainSlant);
                    t.localScale = new Vector3(C.RainWidth[l] / AtmoArt.Rain.bounds.size.x * rng.Range(0.8f, 1.2f),
                        C.RainLength[l] / AtmoArt.Rain.bounds.size.y * rng.Range(0.7f, 1.3f), 1f);
                    _streaks[k] = sr;
                    _streakT[k] = t;
                    _layerOf[k] = l;
                    _rel[k] = new Vector2(rng.Range(-HalfW, HalfW), rng.Range(-HalfH, HalfH));
                }
            }

            var splashColor = C.RainColor;
            splashColor.a = 0f;
            for (int i = 0; i < C.SplashPool; i++)
            {
                _splash[i] = SpriteFactory.Create("Splash", root, AtmoArt.Splash, splashColor, C.RainOrder[1]);
                _splash[i].enabled = false;
                _splashAge[i] = 1f;
            }

            _width = map.Width;
            _wraps = map.WrapsX;
            _columnStart = new int[map.Width + 1];
            int count = 0;
            for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < map.Height - 1; y++)
                    if (map.Get(x, y) == Tile.Solid && map.Get(x, y + 1) == Tile.Empty) count++;
            _tops = new int[count];
            int n = 0;
            for (int x = 0; x < map.Width; x++)
            {
                _columnStart[x] = n;
                for (int y = 0; y < map.Height - 1; y++)
                    if (map.Get(x, y) == Tile.Solid && map.Get(x, y + 1) == Tile.Empty) _tops[n++] = y + 1;
            }
            _columnStart[map.Width] = n;
        }

        private static float HalfW => C.ViewHalfWidth + WrapMargin;
        private static float HalfH => C.ViewHalfHeight + WrapMargin;

        /// <summary>Advances the streaks and splashes around the camera.</summary>
        public void Update(Vector3 camera, float dt)
        {
            var camDelta = _hasCamera ? (Vector2)(camera - _lastCamera) : Vector2.zero;
            // A level change or a teleport snaps the camera: don't smear the rain across the jump.
            if (camDelta.sqrMagnitude > 25f) camDelta = Vector2.zero;
            _lastCamera = camera;
            _hasCamera = true;
            float w = HalfW * 2f, h = HalfH * 2f;
            for (int i = 0; i < _streaks.Length; i++)
            {
                int l = _layerOf[i];
                var p = _rel[i] + _velocity[l] * dt - camDelta * C.RainParallax[l];
                if (p.y < -HalfH) p.y += h;
                else if (p.y > HalfH) p.y -= h;
                if (p.x < -HalfW) p.x += w;
                else if (p.x > HalfW) p.x -= w;
                _rel[i] = p;
                float wx = camera.x + p.x, wy = camera.y + p.y;
                _streakT[i].localPosition = new Vector3(wx, wy, 0f);
                // Far rain sits behind the terrain: only the mid and near layers glint in the cones.
                bool lit = l > 0 && InCone(wx, wy);
                if (lit != _inCone[i])
                {
                    _inCone[i] = lit;
                    _streaks[i].color = lit ? C.RainInCone : _baseColor[l];
                }
            }
            UpdateSplashes(camera, dt);
        }

        private bool InCone(float wx, float wy)
        {
            for (int c = 0; c < _cones.Length; c++)
            {
                float dy = _cones[c].y - wy;
                if (dy <= 0f || dy > C.LampConeLength) continue;
                float dx = wx - _cones[c].x;
                if (_wraps) dx -= _width * Mathf.Round(dx / _width);
                if (Mathf.Abs(dx) < dy * _coneSlope) return true;
            }
            return false;
        }

        private void UpdateSplashes(Vector3 camera, float dt)
        {
            for (int i = 0; i < C.SplashPool; i++)
            {
                if (!_splash[i].enabled) continue;
                _splashAge[i] += dt / C.SplashLife;
                if (_splashAge[i] >= 1f)
                {
                    _splash[i].enabled = false;
                    continue;
                }
                var c = C.RainColor;
                c.a = C.SplashAlpha * (1f - _splashAge[i]);
                _splash[i].color = c;
                float s = 0.18f + 0.14f * _splashAge[i];
                _splash[i].transform.localScale = new Vector3(s, s, 1f);
            }

            if (_tops.Length == 0) return;
            _splashDebt += C.SplashRate * dt;
            while (_splashDebt >= 1f)
            {
                _splashDebt -= 1f;
                TrySpawnSplash(camera);
            }
        }

        private void TrySpawnSplash(Vector3 camera)
        {
            for (int attempt = 0; attempt < SplashTries; attempt++)
            {
                float wx = camera.x + Random.Range(-C.ViewHalfWidth, C.ViewHalfWidth);
                int col = Mathf.FloorToInt(wx);
                int mx = _wraps ? ((col % _width) + _width) % _width : col;
                if (mx < 0 || mx >= _width) continue;
                int from = _columnStart[mx], to = _columnStart[mx + 1];
                if (to <= from) continue;
                int y = _tops[Random.Range(from, to)];
                if (Mathf.Abs(y - camera.y) > C.ViewHalfHeight) continue;
                int k = _nextSplash;
                _nextSplash = (_nextSplash + 1) % C.SplashPool;
                _splash[k].enabled = true;
                _splash[k].transform.localPosition = new Vector3(col + Random.Range(0.1f, 0.9f), y, 0f);
                _splashAge[k] = 0f;
                return;
            }
        }
    }
}
