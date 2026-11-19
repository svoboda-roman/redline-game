using UnityEngine;

namespace Redline.Game
{
    /// <summary>
    /// Pooled visual effects in the rainy night-city style: hot spark slivers with a few loose
    /// pixels and a small spark star, kill cut-flash lines with pixel debris, rainwater spray (slide,
    /// jumps) and splash rings (landing), explosion flash + smoke + embers, speed lines,
    /// afterimages and a full-screen flash. One fixed pool of sprites is recycled round-robin, so nothing is
    /// allocated after construction. Effects are unlit and keep their saturated colours: they are
    /// gameplay signals and must read through the murky grading.
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
        /// <summary>Rainwater spray and splash: pale blue-grey (gameplay effects stay saturated).</summary>
        private static readonly Color WaterColor = new Color(0.7f, 0.8f, 0.92f, 0.65f);
        private static readonly Color SmokeColor = new Color(0.16f, 0.17f, 0.2f, 0.55f);
        private static readonly Color EmberColor = new Color(1f, 0.55f, 0.18f);
        private static readonly Color FlashColor = new Color(0.93f, 0.96f, 1f);
        /// <summary>Pop diameters at or above this are explosions (kamikaze blast), below are glows.</summary>
        private const float ExplosionSize = 2.5f;
        private const int RingPx = 24;
        private readonly Sprite _ring;

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
            _ring = BuildRing();
        }

        /// <summary>A 1 px pixel-art ring (splash), white so it can be tinted.</summary>
        private static Sprite BuildRing()
        {
            var pixels = new Color32[RingPx * RingPx];
            float c = (RingPx - 1) * 0.5f;
            float r = c - 0.5f;
            for (int y = 0; y < RingPx; y++)
                for (int x = 0; x < RingPx; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    pixels[y * RingPx + x] = Mathf.Abs(d - r) < 0.75f ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                }
            var tex = new Texture2D(RingPx, RingPx, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "RedlineFxRing"
            };
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, RingPx, RingPx), new Vector2(0.5f, 0.5f), RingPx);
        }

        /// <summary>
        /// Short bright slivers that fly out and stretch along their velocity (hot core: the colour
        /// pushed toward white), a few loose falling pixels, and for bigger impacts a brief spark
        /// star at the contact point (blue-white for a clang, since that is its colour).
        /// </summary>
        public void Sparks(Vector2 at, Color color, int count, float speed)
        {
            var hot = Color.Lerp(color, Color.white, 0.4f);
            int pixels = Mathf.Min(4, count / 3);
            for (int i = 0; i < count - pixels; i++)
            {
                var v = RandomDirection() * speed * Random.Range(0.45f, 1f);
                Spawn(SpriteFactory.Square, at, v, i % 3 == 0 ? color : hot, new Vector2(0.06f, 0.035f),
                    Random.Range(0.2f, 0.38f), 18f, 3f, 0f, 0f, true);
            }
            for (int i = 0; i < pixels; i++)
            {
                var v = RandomDirection() * speed * Random.Range(0.25f, 0.6f) + new Vector2(0f, speed * 0.2f);
                Spawn(SpriteFactory.Square, at, v, color, new Vector2(0.07f, 0.07f), Random.Range(0.35f, 0.6f),
                    22f, 1f, 0f, 0f, false);
            }
            if (count >= 8) Star(at, Color.Lerp(color, Color.white, 0.6f), 0.5f + 0.03f * count);
        }

        /// <summary>A four-point spark star: two crossed hairlines that flare and fade.</summary>
        private void Star(Vector2 at, Color color, float size)
        {
            int a = Spawn(SpriteFactory.Square, at, Vector2.zero, color, new Vector2(size, 0.05f), 0.1f, 0f, 0f, 0f, 0.8f, false);
            _sprite[a].transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            int b = Spawn(SpriteFactory.Square, at, Vector2.zero, color, new Vector2(size, 0.05f), 0.1f, 0f, 0f, 0f, 0.8f, false);
            _sprite[b].transform.localRotation = Quaternion.Euler(0f, 0f, -45f);
            Spawn(PixelArt.Glow, at, Vector2.zero, new Color(color.r, color.g, color.b, 0.6f), new Vector2(size * 0.6f, size * 0.6f),
                0.08f, 0f, 0f, 0f, 1f, false);
        }

        /// <summary>
        /// A destroyed body: a brief white cut-flash line through the point (a kill), then pixel
        /// debris that tumbles and falls.
        /// </summary>
        public void Debris(Vector2 at, Color color, int count, float speed)
        {
            // The slash direction is not passed in; a near-horizontal diagonal reads as a cut.
            float angle = Random.Range(-28f, 28f);
            int cut = Spawn(SpriteFactory.Square, at, Vector2.zero, new Color(1f, 0.98f, 0.95f, 0.95f),
                new Vector2(2.4f + 0.04f * count, 0.06f), 0.13f, 0f, 0f, 0f, 0.5f, false);
            _sprite[cut].transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            int edge = Spawn(SpriteFactory.Square, at, Vector2.zero, new Color(color.r, color.g, color.b, 0.7f),
                new Vector2(3f + 0.05f * count, 0.16f), 0.1f, 0f, 0f, 0f, 0.3f, false);
            _sprite[edge].transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            for (int i = 0; i < count; i++)
            {
                var v = RandomDirection() * speed * Random.Range(0.3f, 1f) + new Vector2(0f, speed * 0.4f);
                float size = Random.Range(0.08f, 0.2f);
                Spawn(SpriteFactory.Square, at, v, color, new Vector2(size, size), Random.Range(0.6f, 1f),
                    30f, 0.5f, Random.Range(-720f, 720f), 0f, false);
            }
        }

        /// <summary>
        /// Rainwater at the feet: droplet slivers kicked up (thrown back against
        /// <paramref name="pushX"/> on a slide or run-up) and, when standing still on impact
        /// (landing, a brute crash, <paramref name="pushX"/> = 0), a flat splash ring.
        /// </summary>
        public void Dust(Vector2 feet, int count, float pushX)
        {
            var c = WaterColor;
            for (int i = 0; i < count + 2; i++)
            {
                var v = new Vector2(Random.Range(-2.6f, 2.6f) - pushX * 0.3f, Random.Range(2.5f, 5.5f));
                Spawn(SpriteFactory.Square, feet + new Vector2(Random.Range(-0.3f, 0.3f), 0.05f), v, c,
                    new Vector2(0.05f, 0.035f), Random.Range(0.25f, 0.42f), 22f, 1f, 0f, 0f, true);
            }
            var mist = new Color(c.r, c.g, c.b, 0.22f);
            Spawn(PixelArt.Glow, feet + new Vector2(-pushX * 0.04f, 0.1f), new Vector2(-pushX * 0.2f, 0.4f), mist,
                new Vector2(0.7f, 0.35f), 0.35f, 0f, 3f, 0f, 1.4f, false);
            if (Mathf.Abs(pushX) < 0.01f)
                Spawn(_ring, feet + new Vector2(0f, 0.04f), Vector2.zero, new Color(c.r, c.g, c.b, 0.8f),
                    new Vector2(0.5f, 0.14f), 0.3f, 0f, 0f, 0f, 3.2f, false);
        }

        /// <summary>Ground speed (u/s) from which speed lines show: the ronin's sprint threshold.</summary>
        public static readonly float SpeedLineMinSpeed = new Redline.Core.Presentation.RoninStateMachine().SprintThreshold;
        /// <summary>Seconds between speed lines while sprinting (about three on screen at once).</summary>
        public const float SpeedLineInterval = 0.06f;
        /// <summary>Speed line thickness in world units: 1.5 texels at 48 texels per unit (never above 2).</summary>
        private const float SpeedLineThickness = 1.5f / 48f;

        /// <summary>
        /// A thin, short streak behind a sprinting player (<paramref name="around"/> = body centre): starts just behind
        /// the back edge within the torso and legs band, drifts backward and fades out over 0.15 s.
        /// </summary>
        public void SpeedLine(Vector2 around, float travelDirX, Color color)
        {
            var at = around + new Vector2(Random.Range(0.45f, 0.85f) * -travelDirX, Random.Range(-0.55f, 0.45f));
            var c = color;
            c.a = 0.4f;
            Spawn(SpriteFactory.Square, at, new Vector2(-travelDirX * 4f, 0f), c,
                new Vector2(Random.Range(0.25f, 0.5f), SpeedLineThickness), 0.15f, 0f, 0f, 0f, 0f, false);
        }

        /// <summary>
        /// A soft glow that swells and fades in place (orb pickup / expiry). <paramref name="size"/>
        /// is the starting diameter in world units; it grows to about 3.5x over <paramref name="life"/>.
        /// </summary>
        public void Pop(Vector2 at, Color color, float size, float life)
        {
            Spawn(PixelArt.Glow, at, Vector2.zero, color, new Vector2(size, size), life, 0f, 0f, 0f, 2.5f, false);
            if (size >= ExplosionSize) Explosion(at, size, life);
        }

        /// <summary>The extra layers of a blast: an orange-white core flash, rising smoke, embers.</summary>
        private void Explosion(Vector2 at, float size, float life)
        {
            Spawn(PixelArt.Glow, at, Vector2.zero, new Color(1f, 0.93f, 0.8f, 1f), new Vector2(size * 0.55f, size * 0.55f),
                life * 0.4f, 0f, 0f, 0f, 1.2f, false);
            for (int i = 0; i < 5; i++)
            {
                var off = RandomDirection() * size * 0.15f;
                int k = Spawn(PixelArt.Glow, at + off, new Vector2(off.x * 1.5f, Random.Range(0.8f, 1.6f)), SmokeColor,
                    new Vector2(size * 0.35f, size * 0.35f), Random.Range(0.7f, 1f), 0f, 1.5f, 0f, 1.6f, false);
                _sprite[k].sortingOrder = 4; // behind actors, so the smoke never hides a telegraph
            }
            for (int i = 0; i < 10; i++)
            {
                var v = RandomDirection() * Random.Range(3f, 8f) + new Vector2(0f, 3f);
                Spawn(SpriteFactory.Square, at, v, EmberColor, new Vector2(0.06f, 0.05f), Random.Range(0.5f, 0.9f),
                    6f, 1.2f, 0f, 0f, true);
            }
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
            _flash.color = new Color(FlashColor.r, FlashColor.g, FlashColor.b, _flashAlpha);
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
