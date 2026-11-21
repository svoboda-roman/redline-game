using System;
using System.Collections.Generic;
using Redline.Core;
using UnityEngine;

namespace Redline.Game
{
    /// <summary>
    /// Draws the orbs lying in the level (<see cref="RedlineSim.Orbs"/>): an unlit pixel-art
    /// talisman per kind, distinct in colour AND silhouette (Health: crimson diamond with a cross,
    /// Speed: tier-gold capsule with up-chevrons, Hook: cyan ring with a hook) over a soft pulsing
    /// glow, bobbing gently and blinking during its last
    /// <see cref="BlinkSeconds"/>. One pooled pair of sprites per orb slot (orbMaxCount), so a
    /// frame allocates nothing. On a ring level each orb is drawn at the copy nearest the camera
    /// (<see cref="RedlineGame.ToDraw(Vector2)"/>).
    /// </summary>
    /// <example>
    /// <code>
    /// var orbs = new OrbRenderer(worldRoot, config.orbMaxCount);
    /// orbs.Sync(sim, game.ToDraw, Time.deltaTime);   // once per frame
    /// </code>
    /// </example>
    /// <remarks>Main-thread only.</remarks>
    public sealed class OrbRenderer
    {
        /// <summary>Seconds of <see cref="Orb.TimeLeft"/> below which the orb blinks.</summary>
        public const float BlinkSeconds = 2f;
        private const int GlowOrder = 6;
        private const int TokenOrder = 8;
        private const float BobHeight = 0.12f;
        private const float BobRate = 4f;
        private const float GlowSize = 1.9f;
        private const int Px = 13;
        /// <summary>Pixels per world unit: the 13 px token is ~0.8 tiles, a bit larger than the 0.6 hitbox.</summary>
        private const float PixelsPerUnit = 16f;
        /// <summary>Token "breathing" scale amplitude (the soft pulse).</summary>
        private const float PulseAmount = 0.06f;

        private static readonly Color HealthColor = new Color(1f, 0.22f, 0.3f);
        private static readonly Color SpeedColor = new Color(1f, 0.84f, 0.34f);
        private static readonly Color HookColor = new Color(0.36f, 0.88f, 1f);

        // Each kind has its own silhouette as well as its own colour (colourblind safety):
        // health = crimson diamond with a cross, speed = gold capsule with up-chevrons,
        // hook = cyan ring with a hook. 'O' rim (kind colour), 'L' rim highlight, 'd' dark
        // fill, 'W' glyph (near white), '.' transparent.
        private static readonly string[] HealthPattern =
        {
            "......O......",
            ".....OLO.....",
            "....OLddO....",
            "...OLdWddO...",
            "..OddDWdddO..",
            ".OddddWddddO.",
            "OddWWWWWWWddO",
            ".OddddWddddO.",
            "..OdddWdddO..",
            "...OddWddO...",
            "....OdddO....",
            ".....OdO.....",
            "......O......"
        };
        private static readonly string[] SpeedPattern =
        {
            "....OOOOO....",
            "...OLddddO...",
            "..OLddddddO..",
            "..OdddWdddO..",
            "..OddWWWddO..",
            "..OdWWdWWdO..",
            "..OWWdddWWO..",
            "..OdddWdddO..",
            "..OddWWWddO..",
            "..OdWWdWWdO..",
            "..OWWdddWWO..",
            "...OdddddO...",
            "....OOOOO...."
        };
        private static readonly string[] HookPattern =
        {
            "....OOOOO....",
            "..OOLddddOO..",
            ".OLdddWWdddO.",
            ".OLddddWdddO.",
            "OddddddWddddO",
            "OddddddWddddO",
            "OdWddddWddddO",
            "OdWWdddWddddO",
            "OddWWWWWddddO",
            ".OddWWWddddO.",
            ".OdddddddddO.",
            "..OOdddddOO..",
            "....OOOOO...."
        };

        private static Sprite[] _tokens;

        private readonly SpriteRenderer[] _glow;
        private readonly SpriteRenderer[] _token;

        /// <summary>
        /// Clears the cached sprites before each Play (the project enters Play Mode without a
        /// domain reload, so statics outlive the objects they point to).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _tokens = null;

        /// <summary>Creates <paramref name="capacity"/> pooled orb views under a parent transform.</summary>
        public OrbRenderer(Transform parent, int capacity)
        {
            var root = new GameObject("Orbs").transform;
            root.SetParent(parent, false);
            capacity = Mathf.Max(1, capacity);
            _glow = new SpriteRenderer[capacity];
            _token = new SpriteRenderer[capacity];
            for (int i = 0; i < capacity; i++)
            {
                _glow[i] = SpriteFactory.Create("OrbGlow", root, PixelArt.Glow, Color.clear, GlowOrder);
                _glow[i].transform.localScale = new Vector3(GlowSize, GlowSize, 1f);
                _glow[i].enabled = false;
                _token[i] = SpriteFactory.Create("Orb", root, Token(OrbKind.Health), Color.white, TokenOrder);
                _token[i].enabled = false;
            }
        }

        /// <summary>The neon colour of an orb kind (shared with pickup effects and the HUD).</summary>
        public static Color KindColor(OrbKind kind) =>
            kind == OrbKind.Health ? HealthColor : kind == OrbKind.Speed ? SpeedColor : HookColor;

        /// <summary>
        /// Positions one view per orb in <see cref="RedlineSim.Orbs"/> and hides the rest.
        /// <paramref name="toDraw"/> maps a sim position to its drawing position (ring seam);
        /// pass a cached delegate so no closure is allocated per frame.
        /// </summary>
        public void Sync(RedlineSim sim, Func<Vector2, Vector2> toDraw, float time)
        {
            var orbs = sim.Orbs;
            int shown = Mathf.Min(orbs.Count, _token.Length);
            for (int i = 0; i < shown; i++) Place(i, orbs[i], toDraw, time);
            for (int i = shown; i < _token.Length; i++)
            {
                _token[i].enabled = false;
                _glow[i].enabled = false;
            }
        }

        private void Place(int i, Orb orb, Func<Vector2, Vector2> toDraw, float time)
        {
            bool visible = !Blinking(orb.TimeLeft);
            var token = _token[i];
            var glow = _glow[i];
            token.enabled = visible;
            glow.enabled = visible;
            if (!visible) return;

            var c = orb.Center;
            var at = toDraw(new Vector2(c.X, c.Y));
            at.y += BobHeight * (0.5f + 0.5f * Mathf.Sin(orb.Age * BobRate));
            var pos = new Vector3(at.x, at.y, 0f);
            token.transform.localPosition = pos;
            glow.transform.localPosition = pos;
            token.sprite = Token(orb.Kind);
            float pulse = 1f + PulseAmount * Mathf.Sin(time * 4.5f + orb.Age * 1.7f);
            token.transform.localScale = new Vector3(pulse, pulse, 1f);

            var g = KindColor(orb.Kind);
            g.a = 0.45f + 0.15f * Mathf.Sin(time * 5f + orb.Age);
            glow.color = g;
        }

        /// <summary>
        /// True on the "off" beats of the expiry blink: none above <see cref="BlinkSeconds"/>, then
        /// a 4 Hz blink that doubles to 8 Hz in the final second.
        /// </summary>
        public static bool Blinking(float timeLeft)
        {
            if (timeLeft >= BlinkSeconds) return false;
            float rate = timeLeft < 1f ? 8f : 4f;
            float phase = timeLeft * rate;
            return phase - Mathf.Floor(phase) < 0.35f;
        }

        private static Sprite Token(OrbKind kind)
        {
            if (_tokens == null)
            {
                _tokens = new Sprite[3];
                _tokens[(int)OrbKind.Health] = Build("RedlineOrbHealth", HealthPattern, HealthColor);
                _tokens[(int)OrbKind.Speed] = Build("RedlineOrbSpeed", SpeedPattern, SpeedColor);
                _tokens[(int)OrbKind.Hook] = Build("RedlineOrbHook", HookPattern, HookColor);
            }
            return _tokens[(int)kind];
        }

        private static Sprite Build(string name, IReadOnlyList<string> rows, Color color)
        {
            Color32 rim = color;
            Color32 fill = new Color(color.r * 0.28f, color.g * 0.28f, color.b * 0.28f, 1f);
            Color32 glyph = Color.Lerp(color, Color.white, 0.8f);
            Color32 light = Color.Lerp(color, Color.white, 0.45f);
            var pixels = new Color32[Px * Px];
            for (int y = 0; y < Px; y++)
            {
                string row = rows[Px - 1 - y]; // texture rows go bottom-up
                for (int x = 0; x < Px; x++)
                {
                    char ch = x < row.Length ? row[x] : '.';
                    pixels[y * Px + x] = ch == 'O' ? rim : ch == 'L' ? light : ch == 'd' || ch == 'D' ? fill : ch == 'W' ? glyph : new Color32(0, 0, 0, 0);
                }
            }
            var tex = new Texture2D(Px, Px, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = name
            };
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, Px, Px), new Vector2(0.5f, 0.5f), PixelsPerUnit);
        }
    }
}
