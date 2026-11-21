using UnityEngine;

namespace Redline.Game.Render3D.Actors3D
{
    /// <summary>
    /// 3D-scene version of <see cref="EffectsRenderer"/> with the same public API: pooled HDR effect quads on (or
    /// lying on) the play plane so they bloom, drawn by the GPU-instanced <c>Actors3D/Fx</c> shader. Sparks and
    /// their star, the kill cut-flash line with debris, rainwater spray plus splash rings lying flat on the deck,
    /// the kamikaze blast (core flash with a short-lived point light, fogged smoke behind the actors, embers),
    /// pops, speed lines, sprite afterimages/ghosts and a full-view flash (an additive quad in front of the camera,
    /// so it lands in the low-res render target like everything else).
    /// </summary>
    /// <example>
    /// <code>
    /// var fx = new Effects3DRenderer(worldRoot, camera3D);
    /// fx.Sparks(hitPoint, tierColor, 12, 9f);
    /// fx.Update(Time.deltaTime);   // once per frame
    /// </code>
    /// </example>
    /// <remarks>
    /// Main-thread only. Budgets: <see cref="PoolSize"/> quads recycled round-robin (no allocation after
    /// construction; same-material quads instance), <see cref="MaxLights"/> blast point lights (no shadows).
    /// </remarks>
    public sealed class Effects3DRenderer
    {
        /// <summary>Effect quads in the pool (same as the 2D renderer).</summary>
        public const int PoolSize = 320;
        /// <summary>Concurrent blast point lights (oldest is reused).</summary>
        public const int MaxLights = 4;
        /// <summary>HDR multipliers: hot slivers/stars, the cut line, the blast core, embers.</summary>
        public const float SparkHdr = 3f, CutHdr = 5f, BlastHdr = 3f, EmberHdr = 3.5f, GlowHdr = 2f;

        private static readonly Color WaterColor = new Color(0.62f, 0.74f, 0.9f, 0.6f);
        private static readonly Color SmokeColor = new Color(0.07f, 0.075f, 0.09f, 0.6f);
        private static readonly Color EmberColor = new Color(1f, 0.55f, 0.18f);
        private static readonly Color FlashColor = new Color(0.93f, 0.96f, 1f);
        private static readonly Color BlastLightColor = new Color(1f, 0.55f, 0.25f);
        private const float ExplosionSize = 2.5f;
        private const float PixelGrid = Pixel3DCamera.TexelsPerUnit;

        private const int Solid = 0, Glow = 1, Ring = 2, Textured = 3;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ParamsId = Shader.PropertyToID("_Params");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");

        private readonly MeshRenderer[] _quad = new MeshRenderer[PoolSize];
        private readonly Transform[] _tr = new Transform[PoolSize];
        private readonly Vector2[] _velocity = new Vector2[PoolSize];
        private readonly Vector2[] _baseScale = new Vector2[PoolSize];
        private readonly Color[] _color = new Color[PoolSize];
        private readonly float[] _age = new float[PoolSize];
        private readonly float[] _life = new float[PoolSize];
        private readonly float[] _gravity = new float[PoolSize];
        private readonly float[] _drag = new float[PoolSize];
        private readonly float[] _spin = new float[PoolSize];
        private readonly float[] _growth = new float[PoolSize];
        private readonly float[] _shape = new float[PoolSize];
        private readonly float[] _fog = new float[PoolSize];
        private readonly bool[] _stretch = new bool[PoolSize];
        private readonly bool[] _flat = new bool[PoolSize];
        private readonly float[] _z = new float[PoolSize];
        private int _next;

        private readonly Light[] _lights = new Light[MaxLights];
        private readonly float[] _lightAge = new float[MaxLights];
        private readonly float[] _lightLife = new float[MaxLights];
        private readonly float[] _lightPeak = new float[MaxLights];
        private int _nextLight;

        private readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        private readonly Camera _camera;
        private readonly MeshRenderer _flash;
        private float _flashAlpha;

        /// <summary>Creates the pool under <paramref name="parent"/>. <paramref name="camera"/> is the 3D camera (flash).</summary>
        public Effects3DRenderer(Transform parent, Camera camera)
        {
            _camera = camera;
            var root = new GameObject("Effects3D").transform;
            root.SetParent(parent, false);
            for (int i = 0; i < PoolSize; i++)
            {
                _quad[i] = Actors3DAssets.CreateQuad("fx", root, Actors3DAssets.FxAdditive, false, false);
                _tr[i] = _quad[i].transform;
            }
            for (int i = 0; i < MaxLights; i++)
            {
                var l = new GameObject("fxLight").AddComponent<Light>();
                l.transform.SetParent(root, false);
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.color = BlastLightColor;
                l.enabled = false;
                _lights[i] = l;
            }
            _flash = Actors3DAssets.CreateQuad("flash", camera != null ? camera.transform : root, Actors3DAssets.FxAdditiveTop, false, false);
        }

        /// <summary>Hot slivers stretched along their velocity, loose falling pixels and (count >= 8) a spark star.</summary>
        public void Sparks(Vector2 at, Color color, int count, float speed)
        {
            var hot = Color.Lerp(color, Color.white, 0.4f) * SparkHdr;
            var c = color * SparkHdr * 0.8f;
            hot.a = c.a = 1f;
            int pixels = Mathf.Min(4, count / 3);
            for (int i = 0; i < count - pixels; i++)
            {
                var v = RandomDirection() * speed * Random.Range(0.45f, 1f);
                Spawn(Solid, null, at, -0.1f, v, i % 3 == 0 ? c : hot, new Vector2(0.06f, 0.035f), Random.Range(0.2f, 0.38f), 18f, 3f, 0f, 0f, true, true, 0.3f);
            }
            for (int i = 0; i < pixels; i++)
            {
                var v = RandomDirection() * speed * Random.Range(0.25f, 0.6f) + new Vector2(0f, speed * 0.2f);
                Spawn(Solid, null, at, -0.1f, v, c, new Vector2(0.07f, 0.07f), Random.Range(0.35f, 0.6f), 22f, 1f, 0f, 0f, false, true, 0.3f);
            }
            if (count >= 8) Star(at, Color.Lerp(color, Color.white, 0.6f), 0.5f + 0.03f * count);
        }

        private void Star(Vector2 at, Color color, float size)
        {
            var c = color * SparkHdr;
            c.a = 1f;
            int a = Spawn(Solid, null, at, -0.12f, Vector2.zero, c, new Vector2(size, 0.05f), 0.1f, 0f, 0f, 0f, 0.8f, false, true, 0.2f);
            _tr[a].localRotation = Quaternion.Euler(0f, 0f, 45f);
            int b = Spawn(Solid, null, at, -0.12f, Vector2.zero, c, new Vector2(size, 0.05f), 0.1f, 0f, 0f, 0f, 0.8f, false, true, 0.2f);
            _tr[b].localRotation = Quaternion.Euler(0f, 0f, -45f);
            var g = color * GlowHdr;
            g.a = 0.6f;
            Spawn(Glow, null, at, -0.11f, Vector2.zero, g, new Vector2(size * 0.9f, size * 0.9f), 0.1f, 0f, 0f, 0f, 1f, false, true, 0.2f);
        }

        /// <summary>A kill: a brief white-hot cut-flash line through the point, its coloured edge, tumbling debris.</summary>
        public void Debris(Vector2 at, Color color, int count, float speed)
        {
            float angle = Random.Range(-28f, 28f);
            int cut = Spawn(Solid, null, at, -0.15f, Vector2.zero, new Color(CutHdr, CutHdr * 0.98f, CutHdr * 0.95f, 0.95f),
                new Vector2(2.4f + 0.04f * count, 0.06f), 0.13f, 0f, 0f, 0f, 0.5f, false, true, 0.1f);
            _tr[cut].localRotation = Quaternion.Euler(0f, 0f, angle);
            var edgeC = color * 2.2f;
            edgeC.a = 0.7f;
            int edge = Spawn(Solid, null, at, -0.14f, Vector2.zero, edgeC, new Vector2(3f + 0.05f * count, 0.16f), 0.1f, 0f, 0f, 0f, 0.3f, false, true, 0.1f);
            _tr[edge].localRotation = Quaternion.Euler(0f, 0f, angle);
            var dc = color * 1.4f;
            dc.a = 1f;
            for (int i = 0; i < count; i++)
            {
                var v = RandomDirection() * speed * Random.Range(0.3f, 1f) + new Vector2(0f, speed * 0.4f);
                float size = Random.Range(0.08f, 0.2f);
                Spawn(Solid, null, at, Random.Range(-0.3f, 0.3f), v, dc, new Vector2(size, size), Random.Range(0.6f, 1f), 30f, 0.5f,
                    Random.Range(-720f, 720f), 0f, false, true, 0.5f);
            }
        }

        /// <summary>Ink-world blood (alpha-blended): dark crimson, a brighter fresh red, and the floor pool.</summary>
        private static readonly Color BloodDark = new Color(0.42f, 0.01f, 0.04f, 1f);
        private static readonly Color BloodBright = new Color(0.78f, 0.04f, 0.08f, 1f);
        private static readonly Color BloodPool = new Color(0.3f, 0.01f, 0.03f, 0.92f);
        /// <summary>Drone "blood": black machine oil with a faint sheen.</summary>
        private static readonly Color OilDark = new Color(0.035f, 0.035f, 0.04f, 1f);
        private static readonly Color OilBright = new Color(0.17f, 0.16f, 0.19f, 1f);
        private static readonly Color OilPool = new Color(0.03f, 0.03f, 0.035f, 0.92f);

        /// <summary>
        /// A kill's neon blood splash (additive, glowing, small): a short burst of hot red droplets thrown away from the
        /// killer along <paramref name="dirX"/> in an arc, a red glow puff at the wound, and a brief glowing red
        /// splat on the floor at <paramref name="floorY"/>. Goes with the white cut-flash lines (the impact frame).
        /// <paramref name="amount"/> 1 = a normal kill, more = a heavy one (brute, tier 3).
        /// </summary>
        public void Blood(Vector2 at, float dirX, float floorY, float amount, bool oil = false)
        {
            var dark = oil ? OilDark : BloodDark; var bright = oil ? OilBright : BloodBright; var pool = oil ? OilPool : BloodPool;
            // Ink world: real blood, alpha-blended dark crimson (additive red vanishes on the white paper).
            float side = dirX >= 0f ? 1f : -1f;
            int drops = Mathf.RoundToInt(24 * amount);
            for (int i = 0; i < drops; i++)
            {
                float spd = Random.Range(3.5f, 10f);
                var dir = new Vector2(side * Random.Range(0.15f, 1f), Random.Range(-0.05f, 1.1f)).normalized;
                var c = i % 3 == 0 ? bright : dark;
                float s = Random.Range(0.05f, 0.12f);
                Spawn(Solid, null, at + Random.insideUnitCircle * 0.15f, Random.Range(-0.3f, 0.2f), dir * spd, c, new Vector2(s, s * 0.85f),
                    Random.Range(0.5f, 0.95f), 22f, 0.6f, 0f, 0f, true, false, 0.2f);
            }
            // A gush: a few big blobs that burst out and fall.
            for (int i = 0; i < 4 + Mathf.RoundToInt(2 * amount); i++)
            {
                var v = new Vector2(side * Random.Range(1.5f, 4.5f), Random.Range(2f, 5f));
                float s = Random.Range(0.14f, 0.24f) * amount;
                Spawn(Solid, null, at, -0.25f, v, dark, new Vector2(s, s), Random.Range(0.5f, 0.8f), 18f, 0.8f, Random.Range(-200f, 200f), -0.4f, false, false, 0.2f);
            }
            // Pools and splatter on the floor, strung out away from the killer; they stay a while.
            for (int i = 0; i < 3 + Mathf.RoundToInt(2 * amount); i++)
            {
                float dx = side * Random.Range(0.05f, 1.6f) * amount;
                float w = Random.Range(0.35f, 0.8f), d = w * Random.Range(0.3f, 0.5f);
                Spawn(Solid, null, new Vector2(at.x + dx, floorY + 0.012f + i * 0.001f), Random.Range(-0.6f, 0.6f), Vector2.zero, pool,
                    new Vector2(w, d), Random.Range(2.5f, 3.5f), 0f, 0f, 0f, 0.1f, false, false, 0.3f, true);
            }
        }

        /// <summary>
        /// A blood streak along a strike: a thin dark-red crescent swept in front of the striker (<paramref name="facing"/>
        /// ±1), from above the head down past the waist at <paramref name="radius"/>, tapering at both ends, with a few
        /// drops flung from its tip. Fades in ~0.3 s.
        /// </summary>
        public void BloodSlash(Vector2 centre, float facing, float radius, float thickness, bool oil = false)
        {
            var dark = oil ? OilDark : BloodDark; var bright = oil ? OilBright : BloodBright;
            const int Segs = 14;
            float a0 = 75f, a1 = -55f;   // degrees above/below the horizontal, in front of the striker
            Vector2 Arc(float deg) => centre + new Vector2(facing * Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad)) * radius;
            for (int i = 0; i < Segs; i++)
            {
                float t0 = i / (float)Segs, t1 = (i + 1) / (float)Segs;
                var p0 = Arc(Mathf.Lerp(a0, a1, t0)); var p1 = Arc(Mathf.Lerp(a0, a1, t1));
                var mid = (p0 + p1) * 0.5f;
                float tm = (t0 + t1) * 0.5f;
                float w = thickness * Mathf.Sin(tm * Mathf.PI) * (0.6f + 0.4f * tm);    // tapered, heavier toward the end
                var d = p1 - p0;
                int k = Spawn(Solid, null, mid, -0.3f, Vector2.zero, i % 3 == 0 ? bright : dark,
                    new Vector2(d.magnitude * 1.15f, Mathf.Max(0.03f, w)), 0.26f + 0.06f * tm, 0f, 0f, 0f, 0f, false, false, 0.1f);
                _tr[k].localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            }
            var tip = Arc(a1);
            for (int i = 0; i < 6; i++)
            {
                var v = new Vector2(facing * Random.Range(1f, 3.5f), Random.Range(-1f, 1.5f));
                float s = Random.Range(0.04f, 0.08f);
                Spawn(Solid, null, Vector2.Lerp(Arc(0f), tip, Random.value), -0.3f, v, dark, new Vector2(s, s), Random.Range(0.35f, 0.6f), 18f, 0.5f, 0f, 0f, true, false, 0.1f);
            }
        }

        /// <summary>
        /// Rainwater at the feet: droplets kicked up (thrown back against <paramref name="pushX"/>), a mist puff, and
        /// when <paramref name="pushX"/> is 0 (landing, brute crash) a splash ring lying flat on the deck.
        /// </summary>
        public void Dust(Vector2 feet, int count, float pushX)
        {
            var c = WaterColor;
            for (int i = 0; i < count + 2; i++)
            {
                var v = new Vector2(Random.Range(-2.6f, 2.6f) - pushX * 0.3f, Random.Range(2.5f, 5.5f));
                Spawn(Solid, null, feet + new Vector2(Random.Range(-0.3f, 0.3f), 0.05f), Random.Range(-0.35f, 0.35f), v, c,
                    new Vector2(0.05f, 0.035f), Random.Range(0.25f, 0.42f), 22f, 1f, 0f, 0f, true, false, 0.6f);
            }
            var mist = new Color(c.r, c.g, c.b, 0.18f);
            Spawn(Glow, null, feet + new Vector2(-pushX * 0.04f, 0.15f), -0.05f, new Vector2(-pushX * 0.2f, 0.4f), mist,
                new Vector2(0.9f, 0.45f), 0.35f, 0f, 3f, 0f, 1.4f, false, false, 1f);
            if (Mathf.Abs(pushX) < 0.01f)
                Spawn(Ring, null, feet + new Vector2(0f, 0.02f), 0f, Vector2.zero, new Color(c.r * 1.3f, c.g * 1.3f, c.b * 1.3f, 0.85f),
                    new Vector2(0.55f, 0.55f), 0.3f, 0f, 0f, 0f, 3.2f, false, true, 0.6f, true);
        }

        /// <summary>Speed line thickness in world units: 1.5 texels at 48 texels per unit (never above 2).</summary>
        private const float SpeedLineThickness = 1.5f / Render3D.Pixel3DCamera.TexelsPerUnit;

        /// <summary>
        /// A thin, short streak behind a sprinting player (<paramref name="around"/> = body centre): 12 to 24 texels
        /// long, 1.5 thick, starting just behind the back edge within the torso and legs band, a little behind the sprite
        /// in depth, drifting backward and fading out over 0.15 s. The caller gates it on sprint speed.
        /// </summary>
        public void SpeedLine(Vector2 around, float travelDirX, Color color)
        {
            var at = around + new Vector2(Random.Range(0.45f, 0.85f) * -travelDirX, Random.Range(-0.55f, 0.45f));
            var c = color * 1.2f;
            c.a = 0.4f;
            Spawn(Solid, null, at, 0.05f, new Vector2(-travelDirX * 4f, 0f), c, new Vector2(Random.Range(0.25f, 0.5f), SpeedLineThickness),
                0.15f, 0f, 0f, 0f, 0f, false, true, 0.3f);
        }

        /// <summary>A soft glow that swells (~3.5x) and fades. Diameters >= 2.5 are explosions: core, light, smoke, embers.</summary>
        public void Pop(Vector2 at, Color color, float size, float life)
        {
            bool blast = size >= ExplosionSize;
            var c = color * (blast ? 1.2f : GlowHdr);
            c.a = color.a * (blast ? 0.45f : 1f);
            Spawn(Glow, null, at, -0.1f, Vector2.zero, c, new Vector2(size, size), life, 0f, 0f, 0f, 2.5f, false, true, 0.3f);
            if (size >= ExplosionSize) Explosion(at, size, life);
        }

        private void Explosion(Vector2 at, float size, float life)
        {
            Spawn(Glow, null, at, -0.2f, Vector2.zero, new Color(BlastHdr, BlastHdr * 0.9f, BlastHdr * 0.75f, 1f),
                new Vector2(size * 0.4f, size * 0.4f), life * 0.45f, 0f, 0f, 0f, 1.2f, false, true, 0.1f);
            BlastLight(new Vector3(at.x, at.y + 0.3f, -1.2f), size, Mathf.Max(0.25f, life));
            for (int i = 0; i < 6; i++)
            {
                // Behind the actors (z > 0) so the smoke never hides a telegraph; fogged so it sits in the haze.
                var off = RandomDirection() * size * 0.15f;
                Spawn(Glow, null, at + off, Random.Range(0.4f, 0.9f), new Vector2(off.x * 1.5f, Random.Range(0.8f, 1.6f)), SmokeColor,
                    new Vector2(size * 0.45f, size * 0.45f), Random.Range(0.8f, 1.2f), 0f, 1.5f, 0f, 1.6f, false, false, 1f);
            }
            var ember = EmberColor * EmberHdr;
            ember.a = 1f;
            for (int i = 0; i < 12; i++)
            {
                var v = RandomDirection() * Random.Range(3f, 8f) + new Vector2(0f, 3f);
                Spawn(Solid, null, at, Random.Range(-0.6f, 0.6f), v, ember, new Vector2(0.06f, 0.05f), Random.Range(0.5f, 0.9f),
                    6f, 1.2f, 0f, 0f, true, true, 0.4f);
            }
        }

        private void BlastLight(Vector3 pos, float size, float life)
        {
            int k = _nextLight;
            _nextLight = (_nextLight + 1) % MaxLights;
            var l = _lights[k];
            l.transform.localPosition = pos;
            l.range = size * 2.2f;
            _lightPeak[k] = 30f;
            l.intensity = _lightPeak[k];
            _lightAge[k] = 0f;
            _lightLife[k] = life;
            l.enabled = true;
        }

        /// <summary>A fading copy of a sprite pose (sprite-based actors; the 3D player model brings its own trail).</summary>
        public void Afterimage(Sprite sprite, Vector3 position, Vector3 scale, bool flipX, Color color)
        {
            SpriteCopy(sprite, position, Quaternion.identity, scale, flipX, color, 0.22f, 0.05f);
        }

        /// <summary>A fading copy of a rotated sprite (a swing trail).</summary>
        public void Ghost(Sprite sprite, Vector3 position, Quaternion rotation, Vector3 scale, bool flipX, Color color, float life)
        {
            SpriteCopy(sprite, position, rotation, scale, flipX, color, life, -0.05f);
        }

        private void SpriteCopy(Sprite sprite, Vector3 position, Quaternion rotation, Vector3 scale, bool flipX, Color color, float life, float z)
        {
            if (sprite == null) return;
            // Unrotated copies land on the texel grid like the bodies they copy (48 ppu sheets = 1 sprite px per texel).
            if (rotation == Quaternion.identity)
            {
                position.x = Mathf.Round(position.x * PixelGrid) / PixelGrid;
                position.y = Mathf.Round(position.y * PixelGrid) / PixelGrid;
            }
            var r = sprite.rect;
            float ppu = sprite.pixelsPerUnit;
            var size = new Vector2(r.width / ppu * scale.x, r.height / ppu * scale.y);
            var piv = sprite.pivot;
            var off = new Vector2((r.width * 0.5f - piv.x) / ppu * scale.x * (flipX ? -1f : 1f), (r.height * 0.5f - piv.y) / ppu * scale.y);
            off = rotation * off;
            int k = Spawn(Textured, sprite.texture, (Vector2)position + off, z, Vector2.zero, color, size, life, 0f, 0f, 0f, 0f, false, true, 0.3f);
            if (flipX) _baseScale[k].x = -size.x;
            _tr[k].localRotation = rotation;
            var tex = sprite.texture;
            _mpb.SetVector(MainTexStId, new Vector4(r.width / tex.width, r.height / tex.height, r.x / tex.width, r.y / tex.height));
            _quad[k].SetPropertyBlock(_mpb);
        }

        /// <summary>Flashes the whole view (alpha 0-1), fading out quickly.</summary>
        public void Flash(float alpha)
        {
            _flashAlpha = Mathf.Max(_flashAlpha, alpha);
        }

        /// <summary>Advances every live effect, light and the flash.</summary>
        public void Update(float dt)
        {
            for (int i = 0; i < PoolSize; i++)
            {
                var q = _quad[i];
                if (!q.enabled) continue;
                _age[i] += dt;
                float t = _age[i] / _life[i];
                if (t >= 1f) { q.enabled = false; continue; }
                Advance(i, dt, t);
            }
            for (int i = 0; i < MaxLights; i++)
            {
                var l = _lights[i];
                if (!l.enabled) continue;
                _lightAge[i] += dt;
                float t = _lightAge[i] / _lightLife[i];
                if (t >= 1f) { l.enabled = false; continue; }
                l.intensity = _lightPeak[i] * (1f - t) * (1f - t);
            }
            UpdateFlash(dt);
        }

        private void Advance(int i, float dt, float t)
        {
            _velocity[i].y -= _gravity[i] * dt;
            _velocity[i] *= Mathf.Max(0f, 1f - _drag[i] * dt);
            var tr = _tr[i];
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
            _quad[i].GetPropertyBlock(_mpb);
            _mpb.SetColor(ColorId, c);
            _quad[i].SetPropertyBlock(_mpb);
        }

        private void UpdateFlash(float dt)
        {
            if (_flashAlpha <= 0f || _camera == null) { _flash.enabled = false; return; }
            _flash.enabled = true;
            float d = _camera.nearClipPlane + 0.05f;
            float h = 2f * d * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.1f;
            var tr = _flash.transform;
            tr.localPosition = new Vector3(0f, 0f, d);
            tr.localRotation = Quaternion.identity;
            tr.localScale = new Vector3(h * _camera.aspect, h, 1f);
            _mpb.Clear();
            _mpb.SetColor(ColorId, new Color(FlashColor.r, FlashColor.g, FlashColor.b, _flashAlpha));
            _mpb.SetVector(ParamsId, new Vector4(Solid, 0f, 0f, 0f));
            _flash.SetPropertyBlock(_mpb);
            _flashAlpha = Mathf.Max(0f, _flashAlpha - dt * 4f);
        }

        /// <summary>
        /// Starts pool slot _next. <paramref name="additive"/> picks the material; <paramref name="fog"/> 0..1;
        /// <paramref name="flat"/> lays the quad on the deck (rotated 90° about x).
        /// </summary>
        private int Spawn(int shape, Texture tex, Vector2 at, float z, Vector2 velocity, Color color, Vector2 scale, float life,
            float gravity, float drag, float spin, float growth, bool stretch, bool additive, float fog, bool flat = false)
        {
            int k = _next;
            _next = (_next + 1) % PoolSize;
            var q = _quad[k];
            q.sharedMaterial = additive ? Actors3DAssets.FxAdditive : Actors3DAssets.FxAlpha;
            q.enabled = true;
            var tr = _tr[k];
            tr.localPosition = new Vector3(at.x, at.y, z);
            tr.localRotation = flat ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
            tr.localScale = new Vector3(scale.x, scale.y, 1f);
            _velocity[k] = flat ? Vector2.zero : velocity;
            _baseScale[k] = scale;
            _color[k] = color;
            _age[k] = 0f;
            _life[k] = Mathf.Max(0.01f, life);
            _gravity[k] = gravity;
            _drag[k] = drag;
            _spin[k] = flat ? 0f : spin;
            _growth[k] = growth;
            _stretch[k] = stretch;
            _shape[k] = shape;
            _fog[k] = fog;
            _flat[k] = flat;
            _z[k] = z;
            _mpb.Clear();
            _mpb.SetColor(ColorId, color);
            _mpb.SetVector(ParamsId, new Vector4(shape, fog, 0f, 0f));
            if (tex != null) _mpb.SetTexture(MainTexId, tex);
            q.SetPropertyBlock(_mpb);
            return k;
        }

        private static Vector2 RandomDirection()
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }
    }
}
