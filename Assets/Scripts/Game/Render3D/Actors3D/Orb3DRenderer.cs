using System;
using System.Collections.Generic;
using Redline.Core;
using UnityEngine;

namespace Redline.Game.Render3D.Actors3D
{
    /// <summary>
    /// 3D-scene version of <see cref="OrbRenderer"/> with the same public API: per orb an HDR pixel-art talisman
    /// billboard (the same three silhouettes and colours: Health crimson diamond with a cross, Speed gold capsule
    /// with chevrons, Hook cyan ring with a hook), an additive glow behind it, the same bob and expiry blink, a brightness pulse (whole-texel token, no scale pulse),
    /// and a small coloured point light on the first <see cref="MaxLights"/> visible orbs.
    /// </summary>
    /// <example>
    /// <code>
    /// var orbs = new Orb3DRenderer(worldRoot, config.orbMaxCount);
    /// orbs.Sync(sim, game.ToDraw, Time.time);   // once per frame
    /// </code>
    /// </example>
    /// <remarks>Main-thread only. One pooled token + glow per orb slot; nothing allocated per frame.</remarks>
    public sealed class Orb3DRenderer
    {
        /// <summary>Seconds of <see cref="Orb.TimeLeft"/> below which the orb blinks (same as the 2D renderer).</summary>
        public const float BlinkSeconds = OrbRenderer.BlinkSeconds;
        /// <summary>Orb point lights (cap; Forward+ handles more, but each lit pixel pays per light).</summary>
        public const int MaxLights = 4;
        /// <summary>Token and glow HDR multipliers, light intensity and range.</summary>
        public const float TokenHdr = 1.8f, GlowHdr = 1.6f, LightIntensity = 2.5f, LightRange = 2.6f;
        private const float BobHeight = 0.12f, BobRate = 4f, GlowSize = 1.9f, PulseAmount = 0.06f;
        private const int Px = 13;
        /// <summary>Game texels per token pixel: an integer, so the 13-px token covers exactly 39 texels at 48 texels per
        /// unit (<see cref="Pixel3DCamera.TexelsPerUnit"/>) and never resamples (the old 16 ppu = 3.0, now explicit).</summary>
        private const int TexelsPerPx = 3;
        private const float Grid = Pixel3DCamera.TexelsPerUnit;
        private const float TokenSize = Px * TexelsPerPx / Grid;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ParamsId = Shader.PropertyToID("_Params");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        private static readonly string[] HealthPattern =
        {
            "......O......", ".....OLO.....", "....OLddO....", "...OLdWddO...", "..OddDWdddO..", ".OddddWddddO.",
            "OddWWWWWWWddO", ".OddddWddddO.", "..OdddWdddO..", "...OddWddO...", "....OdddO....", ".....OdO.....", "......O......"
        };
        private static readonly string[] SpeedPattern =
        {
            "....OOOOO....", "...OLddddO...", "..OLddddddO..", "..OdddWdddO..", "..OddWWWddO..", "..OdWWdWWdO..",
            "..OWWdddWWO..", "..OdddWdddO..", "..OddWWWddO..", "..OdWWdWWdO..", "..OWWdddWWO..", "...OdddddO...", "....OOOOO...."
        };
        private static readonly string[] HookPattern =
        {
            "....OOOOO....", "..OOLddddOO..", ".OLdddWWdddO.", ".OLddddWdddO.", "OddddddWddddO", "OddddddWddddO",
            "OdWddddWddddO", "OdWWdddWddddO", "OddWWWWWddddO", ".OddWWWddddO.", ".OdddddddddO.", "..OOdddddOO..", "....OOOOO...."
        };

        private static Texture2D[] _tokens;

        private readonly MeshRenderer[] _token;
        private readonly MeshRenderer[] _glow;
        private readonly Light[] _lights = new Light[MaxLights];
        private readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _tokens = null;

        /// <summary>Creates <paramref name="capacity"/> pooled orb views under a parent transform.</summary>
        public Orb3DRenderer(Transform parent, int capacity)
        {
            var root = new GameObject("Orbs3D").transform;
            root.SetParent(parent, false);
            capacity = Mathf.Max(1, capacity);
            _token = new MeshRenderer[capacity];
            _glow = new MeshRenderer[capacity];
            for (int i = 0; i < capacity; i++)
            {
                _glow[i] = Actors3DAssets.CreateQuad("OrbGlow", root, Actors3DAssets.FxAdditive, false, false);
                _glow[i].transform.localScale = new Vector3(GlowSize, GlowSize, 1f);
                _token[i] = Actors3DAssets.CreateQuad("Orb", root, Actors3DAssets.FxAlpha, false, true);
            }
            for (int i = 0; i < MaxLights; i++)
            {
                var l = new GameObject("OrbLight").AddComponent<Light>();
                l.transform.SetParent(root, false);
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.range = LightRange;
                l.enabled = false;
                _lights[i] = l;
            }
        }

        /// <summary>The neon colour of an orb kind (shared with pickup effects and the HUD).</summary>
        public static Color KindColor(OrbKind kind) => OrbRenderer.KindColor(kind);

        /// <summary>Expiry blink "off" beats (same rule as <see cref="OrbRenderer.Blinking"/>).</summary>
        public static bool Blinking(float timeLeft) => OrbRenderer.Blinking(timeLeft);

        /// <summary>Positions one view per orb and hides the rest (same signature as <see cref="OrbRenderer.Sync"/>).</summary>
        public void Sync(RedlineSim sim, Func<Vector2, Vector2> toDraw, float time) => SyncRaw(sim.Orbs, toDraw, time);

        /// <summary>The same update from an explicit orb list (capture staging).</summary>
        internal void SyncRaw(IReadOnlyList<Orb> orbs, Func<Vector2, Vector2> toDraw, float time)
        {
            int shown = Mathf.Min(orbs.Count, _token.Length);
            int lights = 0;
            for (int i = 0; i < shown; i++)
            {
                if (Place(i, orbs[i], toDraw, time) && lights < MaxLights)
                {
                    var l = _lights[lights++];
                    l.enabled = true;
                    var c = KindColor(orbs[i].Kind);
                    l.color = c;
                    l.intensity = LightIntensity;
                    var p = _token[i].transform.localPosition;
                    l.transform.localPosition = new Vector3(p.x, p.y, -0.6f);
                }
            }
            for (int i = shown; i < _token.Length; i++) _token[i].enabled = _glow[i].enabled = false;
            for (int i = lights; i < MaxLights; i++) _lights[i].enabled = false;
        }

        private bool Place(int i, Orb orb, Func<Vector2, Vector2> toDraw, float time)
        {
            bool visible = !Blinking(orb.TimeLeft);
            var token = _token[i];
            var glow = _glow[i];
            token.enabled = glow.enabled = visible;
            if (!visible) return false;
            var c = orb.Center;
            var at = toDraw != null ? toDraw(new Vector2(c.X, c.Y)) : new Vector2(c.X, c.Y);
            at.y += BobHeight * (0.5f + 0.5f * Mathf.Sin(orb.Age * BobRate));
            // The token is an odd number of texels wide: snap its centre to a texel centre so its edges sit on the grid.
            float tx = (Mathf.Round(at.x * Grid - 0.5f) + 0.5f) / Grid, ty = (Mathf.Round(at.y * Grid - 0.5f) + 0.5f) / Grid;
            token.transform.localPosition = new Vector3(tx, ty, -0.05f);
            token.transform.localScale = new Vector3(TokenSize, TokenSize, 1f);
            glow.transform.localPosition = new Vector3(at.x, at.y, 0.05f);
            // The pulse is a brightness pulse: a scale pulse would resample the pixel art every frame (shimmer).
            float pulse = TokenHdr * (1f + PulseAmount * 2f * Mathf.Sin(time * 4.5f + orb.Age * 1.7f));

            var kc = KindColor(orb.Kind);
            _mpb.Clear();
            _mpb.SetColor(ColorId, new Color(pulse, pulse, pulse, 1f));
            _mpb.SetVector(ParamsId, new Vector4(3f, 0.3f, 0f, 0f));
            _mpb.SetTexture(MainTexId, Token(orb.Kind));
            token.SetPropertyBlock(_mpb);
            _mpb.Clear();
            _mpb.SetColor(ColorId, new Color(kc.r * GlowHdr, kc.g * GlowHdr, kc.b * GlowHdr, 0.45f + 0.15f * Mathf.Sin(time * 5f + orb.Age)));
            _mpb.SetVector(ParamsId, new Vector4(1f, 0.3f, 0f, 0f));
            glow.SetPropertyBlock(_mpb);
            return true;
        }

        private static Texture2D Token(OrbKind kind)
        {
            if (_tokens == null)
            {
                _tokens = new Texture2D[3];
                _tokens[(int)OrbKind.Health] = Build("Orb3DHealth", HealthPattern, KindColor(OrbKind.Health));
                _tokens[(int)OrbKind.Speed] = Build("Orb3DSpeed", SpeedPattern, KindColor(OrbKind.Speed));
                _tokens[(int)OrbKind.Hook] = Build("Orb3DHook", HookPattern, KindColor(OrbKind.Hook));
            }
            return _tokens[(int)kind];
        }

        private static Texture2D Build(string name, IReadOnlyList<string> rows, Color color)
        {
            Color32 rim = color;
            Color32 fill = new Color(color.r * 0.28f, color.g * 0.28f, color.b * 0.28f, 1f);
            Color32 glyph = Color.Lerp(color, Color.white, 0.8f);
            Color32 light = Color.Lerp(color, Color.white, 0.45f);
            var pixels = new Color32[Px * Px];
            for (int y = 0; y < Px; y++)
            {
                string row = rows[Px - 1 - y];
                for (int x = 0; x < Px; x++)
                {
                    char ch = x < row.Length ? row[x] : '.';
                    pixels[y * Px + x] = ch == 'O' ? rim : ch == 'L' ? light : ch == 'd' || ch == 'D' ? fill : ch == 'W' ? glyph : new Color32(0, 0, 0, 0);
                }
            }
            var tex = new Texture2D(Px, Px, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name };
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
