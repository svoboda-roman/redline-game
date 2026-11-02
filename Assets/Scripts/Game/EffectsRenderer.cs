using UnityEngine;

namespace Redline.Game
{
    /// <summary>
    /// Pooled visual effects: spark streaks, debris, dust puffs, speed lines, afterimages and a
    /// full-screen flash. One fixed pool of sprites is recycled round-robin, so nothing is
    /// allocated after construction.
    /// </summary>
    /// <example>
    /// <code>
    /// var fx = new EffectsRenderer(worldRoot, camera);
    /// fx.Sparks(hitPoint, tierColor, 12, 9f);
    /// fx.Update(Time.deltaTime);   // once per frame
    /// </code>
    /// </example>
    /// <remarks>Main-thread only.</remarks>
    public sealed class EffectsRenderer
    {
        private const int PoolSize = 320;
        private const int SortingOrder = 12;

        private readonly SpriteRenderer[] _sprite = new SpriteRenderer[PoolSize];
        private readonly Vector2[] _velocity = new Vector2[PoolSize];
        private readonly Vector2[] _baseScale = new Vector2[PoolSize];
        private readonly Color[] _color = new Color[PoolSize];
        private readonly float[] _age = new float[PoolSize];
        private readonly float[] _life = new float[PoolSize];
        private readonly float[] _gravity = new float[PoolSize];
        private readonly float[] _drag = new float[PoolSize];
        private readonly float[] _spin = new float[PoolSize];
        private readonly float[] _growth = new float[PoolSize];
        private readonly bool[] _stretch = new bool[PoolSize];
        private int _next;

        private readonly Camera _camera;
        private readonly SpriteRenderer _flash;
        private float _flashAlpha;

        /// <summary>Creates the pool under a parent transform.</summary>
        public EffectsRenderer(Transform parent, Camera camera)
        {
            _camera = camera;
            var root = new GameObject("Effects").transform;
            root.SetParent(parent, false);
            for (int i = 0; i < PoolSize; i++)
            {
                _sprite[i] = SpriteFactory.Create("Fx", root, SpriteFactory.Square, Color.clear, SortingOrder);
                _sprite[i].enabled = false;
            }
            _flash = SpriteFactory.Create("Flash", root, SpriteFactory.Square, Color.clear, 200);
            _flash.enabled = false;
        }

        /// <summary>Bright streaks that fly out and stretch along their velocity.</summary>
        public void Sparks(Vector2 at, Color color, int count, float speed)
        {
            for (int i = 0; i < count; i++)
            {
                var v = RandomDirection() * speed * Random.Range(0.4f, 1f);
                Spawn(SpriteFactory.Square, at, v, color, new Vector2(0.09f, 0.09f), Random.Range(0.25f, 0.45f),
                    18f, 2f, 0f, 0f, true);
            }
        }

        /// <summary>Chunks of a destroyed body that tumble and fall.</summary>
        public void Debris(Vector2 at, Color color, int count, float speed)
        {
            for (int i = 0; i < count; i++)
            {
                var v = RandomDirection() * speed * Random.Range(0.3f, 1f) + new Vector2(0f, speed * 0.4f);
                float size = Random.Range(0.12f, 0.28f);
                Spawn(SpriteFactory.Square, at, v, color, new Vector2(size, size), Random.Range(0.6f, 1f),
                    30f, 0.5f, Random.Range(-720f, 720f), 0f, false);
            }
        }

        /// <summary>Soft grey puffs at the feet (jump, landing, slide).</summary>
        public void Dust(Vector2 feet, int count, float pushX)
        {
            var c = new Color(0.75f, 0.8f, 0.95f, 0.45f);
            for (int i = 0; i < count; i++)
            {
                var v = new Vector2(Random.Range(-1.5f, 1.5f) - pushX * 0.15f, Random.Range(0.3f, 1.4f));
                float size = Random.Range(0.12f, 0.22f);
                Spawn(PixelArt.Glow, feet + new Vector2(Random.Range(-0.3f, 0.3f), 0.05f), v, c,
                    new Vector2(size, size) * 2.5f, Random.Range(0.3f, 0.5f), 0f, 3f, 0f, 1.6f, false);
            }
        }

        /// <summary>A thin streak trailing behind a fast player.</summary>
        public void SpeedLine(Vector2 around, float travelDirX, Color color)
        {
            var at = around + new Vector2(Random.Range(-0.4f, 0.8f) * -travelDirX, Random.Range(-0.9f, 0.9f));
            var c = color;
            c.a = 0.55f;
            Spawn(SpriteFactory.Square, at, new Vector2(-travelDirX * 6f, 0f), c,
                new Vector2(Random.Range(0.6f, 1.4f), 0.04f), 0.18f, 0f, 0f, 0f, 0f, false);
        }

        /// <summary>A fading copy of the player's current pose.</summary>
        public void Afterimage(Sprite sprite, Vector3 position, Vector3 scale, bool flipX, Color color)
        {
            int k = Spawn(sprite, position, Vector2.zero, color, scale, 0.22f, 0f, 0f, 0f, 0f, false);
            _sprite[k].flipX = flipX;
            _sprite[k].sortingOrder = 5;
        }

        /// <summary>A fading copy of a rotated sprite (the sword's swing trail).</summary>
        public void Ghost(Sprite sprite, Vector3 position, Quaternion rotation, Vector3 scale, bool flipX, Color color, float life)
        {
            int k = Spawn(sprite, position, Vector2.zero, color, scale, life, 0f, 0f, 0f, 0f, false);
            _sprite[k].flipX = flipX;
            _sprite[k].sortingOrder = 9;
            _sprite[k].transform.localRotation = rotation;
        }

        /// <summary>Flashes the whole view (alpha 0-1), fading out quickly.</summary>
        public void Flash(float alpha)
        {
            _flashAlpha = Mathf.Max(_flashAlpha, alpha);
        }

        /// <summary>Advances every live effect.</summary>
        public void Update(float dt)
        {
            for (int i = 0; i < PoolSize; i++)
            {
                var s = _sprite[i];
                if (!s.enabled) continue;
                _age[i] += dt;
                float t = _age[i] / _life[i];
                if (t >= 1f)
                {
                    s.enabled = false;
                    continue;
                }
                Advance(i, s, dt, t);
            }
            UpdateFlash(dt);
        }

        private void Advance(int i, SpriteRenderer s, float dt, float t)
        {
            _velocity[i].y -= _gravity[i] * dt;
            _velocity[i] *= Mathf.Max(0f, 1f - _drag[i] * dt);
            var tr = s.transform;
            tr.localPosition += (Vector3)(_velocity[i] * dt);
            float grow = 1f + _growth[i] * t;
            if (_stretch[i])
            {
                float speed = _velocity[i].magnitude;
                tr.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(_velocity[i].y, _velocity[i].x) * Mathf.Rad2Deg);
                tr.localScale = new Vector3(_baseScale[i].x + speed * 0.035f, _baseScale[i].y, 1f);
            }
            else
            {
                if (_spin[i] != 0f) tr.localRotation *= Quaternion.Euler(0f, 0f, _spin[i] * dt);
                tr.localScale = new Vector3(_baseScale[i].x * grow, _baseScale[i].y * grow, 1f);
            }
            var c = _color[i];
            c.a *= 1f - t;
            s.color = c;
        }

        private void UpdateFlash(float dt)
        {
            if (_flashAlpha <= 0f)
            {
                _flash.enabled = false;
                return;
            }
            _flash.enabled = true;
            var cam = _camera.transform.position;
            _flash.transform.position = new Vector3(cam.x, cam.y, 0f);
            float h = _camera.orthographicSize * 2f;
            _flash.transform.localScale = new Vector3(h * _camera.aspect + 2f, h + 2f, 1f);
            _flash.color = new Color(1f, 1f, 1f, _flashAlpha);
            _flashAlpha = Mathf.Max(0f, _flashAlpha - dt * 4f);
        }

        private int Spawn(Sprite sprite, Vector2 at, Vector2 velocity, Color color, Vector2 scale, float life,
            float gravity, float drag, float spin, float growth, bool stretch)
        {
            int k = _next;
            _next = (_next + 1) % PoolSize;
            var s = _sprite[k];
            s.sprite = sprite;
            s.flipX = false;
            s.sortingOrder = SortingOrder;
            s.color = color;
            s.enabled = true;
            var tr = s.transform;
            tr.localPosition = new Vector3(at.x, at.y, 0f);
            tr.localRotation = Quaternion.identity;
            tr.localScale = new Vector3(scale.x, scale.y, 1f);
            _velocity[k] = velocity;
            _baseScale[k] = scale;
            _color[k] = color;
            _age[k] = 0f;
            _life[k] = life;
            _gravity[k] = gravity;
            _drag[k] = drag;
            _spin[k] = spin;
            _growth[k] = growth;
            _stretch[k] = stretch;
            return k;
        }

        private static Vector2 RandomDirection()
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }
    }
}
