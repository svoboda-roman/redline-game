using UnityEngine;

namespace Redline.Game
{
    /// <summary>
    /// Procedural pixel art. Every sprite is a greyscale text pattern tinted at draw time by
    /// SpriteRenderer.color, so one sprite serves every tier colour and the brute's armor states.
    /// Pattern rows are top-down; '.' is transparent, 'W' full white, 'L' light, 'M' mid, 'D' dark,
    /// 'K' near-black.
    /// </summary>
    /// <example>
    /// <code>
    /// renderer.sprite = PixelArt.Player;
    /// renderer.color = tierColor;   // tints the white pixels, keeps the dark visor dark
    /// </code>
    /// </example>
    /// <remarks>Main-thread only. Sprites are created once and cached for the session.</remarks>
    public static class PixelArt
    {
        private static Sprite _player;
        private static Sprite _playerSlide;
        private static Sprite _grunt;
        private static Sprite _brute;
        private static Sprite _drone;
        private static Sprite _tile;
        private static Sprite _slash;
        private static Sprite _glow;
        private static Sprite _gradient;

        /// <summary>Clears cached sprites before each Play (Play Mode runs without a domain reload).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _player = _playerSlide = _grunt = _brute = _drone = _tile = _slash = _glow = _gradient = null;
        }

        /// <summary>Standing runner, facing right (visor on the right).</summary>
        public static Sprite Player => _player != null ? _player : (_player = FromPattern("RedlinePlayer", new[]
        {
            "...WWWW...",
            "..WWWWWW..",
            "..WWWKKK..",
            "..WWWWWW..",
            "...WWWW...",
            "..LWWWWL..",
            ".LWWWWWWL.",
            ".LWWWWWWL.",
            ".MWWWWWWM.",
            "..WWWWWW..",
            "..WWMMWW..",
            "..WW..WW..",
            "..WW..WW..",
            "..WW..WW..",
            "..LL..LL..",
            "..MM..MM.."
        }));

        /// <summary>Sliding runner, low and stretched, facing right.</summary>
        public static Sprite PlayerSlide => _playerSlide != null ? _playerSlide : (_playerSlide = FromPattern("RedlinePlayerSlide", new[]
        {
            "...........WWWW.",
            "..........WWWKKK",
            ".....LWWWWWWWWW.",
            "...LWWWWWWWWWWL.",
            ".MWWWWWWWWWWWW..",
            "MWWWWMMWWWWWW...",
            ".MM.....MM......",
            "................"
        }));

        /// <summary>Spiky grunt with two eyes and a mouth.</summary>
        public static Sprite Grunt => _grunt != null ? _grunt : (_grunt = FromPattern("RedlineGrunt", new[]
        {
            ".W......W.",
            ".WW....WW.",
            "..WWWWWW..",
            ".WWWWWWWW.",
            ".WKKWWKKW.",
            ".WKKWWKKW.",
            ".WWWWWWWW.",
            ".WWKKKKWW.",
            "..WWWWWW..",
            "..WW..WW..",
            ".WW....WW.",
            ".MM....MM."
        }));

        /// <summary>Armored brute with a visor slit and plated shoulders.</summary>
        public static Sprite Brute => _brute != null ? _brute : (_brute = FromPattern("RedlineBrute", new[]
        {
            "....MMMMM....",
            "...MWWWWWM...",
            "...MWKKKWM...",
            "...MWWWWWM...",
            ".MMMMMMMMMMM.",
            "MWWWWWWWWWWWM",
            "MWLLWWWWWLLWM",
            "MWWWWWWWWWWWM",
            "MWLLWWWWWLLWM",
            "MWWWWWWWWWWWM",
            ".MWWWWWWWWWM.",
            ".MWWWWWWWWWM.",
            ".MWWMMMMMWWM.",
            "..MWWWWWWWM..",
            "..MWW...WWM..",
            "..MWW...WWM..",
            "..MWW...WWM..",
            ".MMWW...WWMM.",
            ".MMMM...MMMM.",
            "............."
        }));

        /// <summary>Flying drone: wings, a single dark eye band, two dangling claws.</summary>
        public static Sprite Drone => _drone != null ? _drone : (_drone = FromPattern("RedlineDrone", new[]
        {
            "W........W",
            ".WW....WW.",
            "..WWWWWW..",
            ".WWKKKKWW.",
            "WWWKWWKWWW",
            ".WWWWWWWW.",
            "..W....W..",
            "..M....M.."
        }));

        /// <summary>Bevelled terrain block: bright top-left edge, dark bottom-right edge.</summary>
        public static Sprite Tile => _tile != null ? _tile : (_tile = FromPattern("RedlineTile", new[]
        {
            "WWWWWWWW",
            "WLLLLLLM",
            "WLMMMMLD",
            "WLMMMMLD",
            "WLMMMMLD",
            "WLMMMMLD",
            "WLLLLLLD",
            "MDDDDDDD"
        }));

        /// <summary>Crescent slash for a strike, opening to the right.</summary>
        public static Sprite Slash => _slash != null ? _slash : (_slash = FromPattern("RedlineSlash", new[]
        {
            "......WWWW......",
            "........WWWW....",
            "..........WWW...",
            "...........WWW..",
            "............WWW.",
            "............LWW.",
            ".............LW.",
            ".............LW.",
            ".............LW.",
            ".............LW.",
            "............LWW.",
            "............WWW.",
            "...........WWW..",
            "..........WWW...",
            "........WWWW....",
            "......WWWW......"
        }));

        /// <summary>Soft round glow (alpha falls off from the centre).</summary>
        public static Sprite Glow
        {
            get
            {
                if (_glow != null) return _glow;
                const int size = 32;
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x + 0.5f) / size - 0.5f;
                        float dy = (y + 0.5f) / size - 0.5f;
                        float t = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) * 2f);
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(t * t * 255f));
                    }
                _glow = Make("RedlineGlow", size, size, pixels, FilterMode.Bilinear);
                return _glow;
            }
        }

        /// <summary>Vertical gradient, white at the top fading to black at the bottom (tint it).</summary>
        public static Sprite Gradient
        {
            get
            {
                if (_gradient != null) return _gradient;
                const int height = 64;
                var pixels = new Color32[height];
                for (int y = 0; y < height; y++)
                {
                    byte v = (byte)(255f * y / (height - 1));
                    pixels[y] = new Color32(v, v, v, 255);
                }
                _gradient = Make("RedlineGradient", 1, height, pixels, FilterMode.Bilinear);
                return _gradient;
            }
        }

        private static Sprite FromPattern(string name, string[] rows)
        {
            int h = rows.Length;
            int w = rows[0].Length;
            var pixels = new Color32[w * h];
            for (int row = 0; row < h; row++)
            {
                int y = h - 1 - row;
                for (int x = 0; x < w; x++) pixels[y * w + x] = Shade(x < rows[row].Length ? rows[row][x] : '.');
            }
            return Make(name, w, h, pixels, FilterMode.Point);
        }

        private static Color32 Shade(char c)
        {
            switch (c)
            {
                case 'W': return new Color32(255, 255, 255, 255);
                case 'L': return new Color32(205, 205, 205, 255);
                case 'M': return new Color32(150, 150, 150, 255);
                case 'D': return new Color32(95, 95, 95, 255);
                case 'K': return new Color32(18, 18, 26, 255);
                default: return new Color32(0, 0, 0, 0);
            }
        }

        /// <summary>Builds a sprite whose full texture spans one world unit on its longer side.</summary>
        private static Sprite Make(string name, int w, int h, Color32[] pixels, FilterMode filter)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = filter,
                wrapMode = TextureWrapMode.Clamp,
                name = name
            };
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), Mathf.Max(w, h));
        }

        /// <summary>World size of a sprite made by this class at scale 1 (longer side = 1).</summary>
        public static Vector2 UnitSize(Sprite sprite) => sprite.bounds.size;
    }
}
