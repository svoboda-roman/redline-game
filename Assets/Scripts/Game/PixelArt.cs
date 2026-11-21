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
        private static Sprite[] _playerRun;
        private static Sprite _playerJump;
        private static Sprite _playerFall;
        private static Sprite _playerSwing;
        private static Sprite _grunt;
        private static Sprite _brute;
        private static Sprite _drone;
        private static Sprite _shooter;
        private static Sprite _shieldBearer;
        private static Sprite _kamikaze;
        private static Sprite _summoner;
        private static Sprite _projectile;
        private static Sprite _tile;
        private static Sprite _sword;
        private static Sprite _glow;
        private static Sprite _gradient;

        /// <summary>Clears cached sprites before each Play (Play Mode runs without a domain reload).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _playerRun = null;
            _player = _playerSlide = _playerJump = _playerFall = _playerSwing = _grunt = _brute = _drone = _tile = _sword = _glow = _gradient = null;
            _shooter = _shieldBearer = _kamikaze = _summoner = _projectile = null;
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

        /// <summary>Number of frames in the run cycle (see <see cref="PlayerRunFrame"/>).</summary>
        public const int PlayerRunFrameCount = 4;

        /// <summary>
        /// One frame of the run cycle, facing right: 0 stride, 1 passing, 2 the other stride,
        /// 3 the other passing. The near limbs are white, the far limbs mid grey for depth. Every
        /// frame is 16 rows tall like <see cref="Player"/>, so pixels stay the same size.
        /// </summary>
        /// <example>
        /// <code>
        /// renderer.sprite = PixelArt.PlayerRunFrame((int)phase % PixelArt.PlayerRunFrameCount);
        /// </code>
        /// </example>
        public static Sprite PlayerRunFrame(int index)
        {
            if (_playerRun == null)
            {
                _playerRun = new[]
                {
                    FromPattern("RedlinePlayerRun0", RunRows(
                        "..MWWWWWWL..",
                        ".M.WWWWWW.L.",
                        ".D.WWWWWW.W.",
                        "....MMWW....",
                        "...MM..WW...",
                        "..MM....WW..",
                        ".DD.....LL..",
                        "........MMM.")),
                    FromPattern("RedlinePlayerRun1", RunRows(
                        "...WWWWWWL..",
                        "..MWWWWWWL..",
                        "..DWWWWWWW..",
                        ".....WWMMM..",
                        ".....WW..M..",
                        ".....WW.MM..",
                        ".....LL.....",
                        ".....MMM....")),
                    FromPattern("RedlinePlayerRun2", RunRows(
                        "..LWWWWWWM..",
                        ".L.WWWWWW.M.",
                        ".W.WWWWWW.D.",
                        "....WWMM....",
                        "...WW..MM...",
                        "..WW....MM..",
                        ".LL.....MM..",
                        "........DDD.")),
                    FromPattern("RedlinePlayerRun3", RunRows(
                        "..LWWWWWW...",
                        "..LWWWWWWM..",
                        "..WWWWWWWD..",
                        ".....MMWWW..",
                        ".....MM..W..",
                        ".....MM.WW..",
                        ".....DD.....",
                        ".....DDD....")),
                };
            }
            return _playerRun[((index % PlayerRunFrameCount) + PlayerRunFrameCount) % PlayerRunFrameCount];
        }

        /// <summary>Rising jump, facing right: near fist punched up, near knee tucked, far leg trailing.</summary>
        public static Sprite PlayerJump => _playerJump != null ? _playerJump : (_playerJump = FromPattern("RedlinePlayerJump", new[]
        {
            "....WWWW....",
            "...WWWWWW...",
            "...WWWKKK...",
            "...WWWWWW.W.",
            "....WWWW..L.",
            "...WWWWWWL..",
            "..MWWWWWW...",
            ".M.WWWWWW...",
            ".D.WWWWWW...",
            "...WWWWWW...",
            "...WWMMWW...",
            "....MMWWWW..",
            "...MM...WW..",
            "...MM...LL..",
            "..DD....MM..",
            "............"
        }));

        /// <summary>Falling, facing right: both arms thrown up, legs spread and reaching for the ground.</summary>
        public static Sprite PlayerFall => _playerFall != null ? _playerFall : (_playerFall = FromPattern("RedlinePlayerFall", new[]
        {
            "....WWWW....",
            "...WWWWWW...",
            ".M.WWWKKK.W.",
            ".M.WWWWWW.L.",
            "..M.WWWW.L..",
            "..MWWWWWWL..",
            "...WWWWWW...",
            "...WWWWWW...",
            "...WWWWWW...",
            "...WWWWWW...",
            "...WWMMWW...",
            "...MM.WW....",
            "..MM...WW...",
            "..MM...WW...",
            ".MM.....WW..",
            ".DD.....LL.."
        }));

        /// <summary>
        /// Hanging on the rope, facing right: both hands grip at the top centre (where the rope
        /// meets the sprite's centre line once it is rotated toward the anchor), head between the
        /// arms, legs trailing behind.
        /// </summary>
        public static Sprite PlayerSwing => _playerSwing != null ? _playerSwing : (_playerSwing = FromPattern("RedlinePlayerSwing", new[]
        {
            ".....WW.....",
            "....M..L....",
            "...MWWWWL...",
            "...MWWKKL...",
            "...MWWWWL...",
            "..MWWWWWWL..",
            "...WWWWWW...",
            "...WWWWWW...",
            "...WWWWWW...",
            "...WWMMWW...",
            "....WWMM....",
            "...WW.MM....",
            "...WW..MM...",
            "..WW...MM...",
            "..LL..MM....",
            ".MM..DD....."
        }));

        /// <summary>Head and hips shared by the run frames, around eight rows of arms and legs.</summary>
        private static string[] RunRows(string arms6, string arms7, string arms8,
            string leg11, string leg12, string leg13, string leg14, string leg15)
        {
            return new[]
            {
                "....WWWW....",
                "...WWWWWW...",
                "...WWWKKK...",
                "...WWWWWW...",
                "....WWWW....",
                "...WWWWWW...",
                arms6, arms7, arms8,
                "...WWWWWW...",
                "...WWMMWW...",
                leg11, leg12, leg13, leg14, leg15
            };
        }

        /// <summary>Spiky grunt with two eyes and a mouth.</summary>
        public static Sprite Grunt => _grunt != null ? _grunt : (_grunt = FromPattern("RedlineGrunt", new[]
        {
            "..W..........W..",
            "..WW........WW..",
            "...WWWWWWWWWW...",
            "..WWWWWWWWWWWW..",
            ".WWKKKWWWWKKKWW.",
            ".WWKWKWWWWKWKWW.",
            ".WWWWWWWWWWWWWW.",
            "..WWKKKKKKKKWW..",
            "..WWWKWKWKWWWW..",
            "...WWWWWWWWWW...",
            "MWW.WWWWWWWW.WWM",
            "MW..LWWWWWWL..WM",
            "....WWW..WWW....",
            "....WW....WW....",
            "...MWW....WWM...",
            "...MM......MM..."
        }));

        /// <summary>Armored brute with a visor slit and plated shoulders.</summary>
        public static Sprite Brute => _brute != null ? _brute : (_brute = FromPattern("RedlineBrute", new[]
        {
            "......MMMMMM......",
            ".....MWWWWWWM.....",
            ".....MWKKKKWM.....",
            ".....MWWWWWWM.....",
            "...MMMMMMMMMMMM...",
            "..MWWWWWWWWWWWWM..",
            ".MWWLLWWWWWWLLWWM.",
            "MWWWWWWWWWWWWWWWWM",
            "MWLLWWWWLLWWWWLLWM",
            "MWWWWWWWWWWWWWWWWM",
            "MWLLWWWWLLWWWWLLWM",
            ".MWWWWWWWWWWWWWWM.",
            ".MWWMMMMMMMMMMWWM.",
            ".MWWWWWWWWWWWWWWM.",
            "..MWWWWWWWWWWWWM..",
            "..MWWWWMMMMWWWWM..",
            "..MWWWM....MWWWM..",
            "..MWWWM....MWWWM..",
            "..MWWWM....MWWWM..",
            "..MWWWM....MWWWM..",
            ".MMWWWMM..MMWWWMM.",
            ".MMMMMMM..MMMMMMM."
        }));

        /// <summary>Flying drone: wings, a single dark eye band, two dangling claws.</summary>
        public static Sprite Drone => _drone != null ? _drone : (_drone = FromPattern("RedlineDrone", new[]
        {
            "WW..........WW",
            ".WW........WW.",
            "..WW.MMMM.WW..",
            "...WWWWWWWW...",
            "..WWKKKKKKWW..",
            ".WWWKWWWWKWWW.",
            "WWWWWKKKKWWWWW",
            "..WWWWWWWWWW..",
            "...W.W..W.W...",
            "...M.M..M.M..."
        }));

        /// <summary>Shooter, facing right: hooded head with a visor slit, a gun arm pointing forward.</summary>
        public static Sprite Shooter => _shooter != null ? _shooter : (_shooter = FromPattern("RedlineShooter", new[]
        {
            "....MMMMMM....",
            "...MWWWWWWM...",
            "..MWWWWWWWWM..",
            "..MWWWKKKKWM..",
            "..MWWWWWWWWM..",
            "...MWWWWWWM...",
            "..LWWWWWWWWWWW",
            ".LWWWWWWWWLKKW",
            ".LWWWWWWWWWLLL",
            ".MWWWWWWWWM...",
            "..WWWWWWWW....",
            "..WWWMMWWW....",
            "..WWW..WWW....",
            "..WWW..WWW....",
            "..LLL..LLL....",
            "..MMM..MMM...."
        }));

        /// <summary>Shield bearer, shield on the right (flip the sprite to put it on the left).</summary>
        public static Sprite ShieldBearer => _shieldBearer != null ? _shieldBearer : (_shieldBearer = FromPattern("RedlineShieldBearer", new[]
        {
            "....MMMMM.......",
            "...MWWWWWM..MM..",
            "...MWKKKWM.MLLM.",
            "...MWWWWWM.MLWLM",
            "....MWWWM..MLWLM",
            "..MWWWWWWWMMLWLM",
            ".MWWWWWWWWWMLWLM",
            ".MWWWWWWWWWMLWLM",
            ".MWWWWWWWWMMLWLM",
            "..MWWWWWWM.MLWLM",
            "..MWWMMWWM.MLWLM",
            "..MWW..WWM.MLWLM",
            "..MWW..WWM..MLM.",
            "..MWW..WWM...M..",
            "..LLL..LLL......",
            "..MMM..MMM......"
        }));

        /// <summary>Kamikaze: a round bomb with a lit fuse on top, angry eyes and stubby legs.</summary>
        public static Sprite Kamikaze => _kamikaze != null ? _kamikaze : (_kamikaze = FromPattern("RedlineKamikaze", new[]
        {
            "......W.W...",
            ".....D.W....",
            ".....D......",
            "...MMDMM....",
            "..MWWWWWM...",
            ".MWWWWWWWM..",
            "MWKKWWWKKWM.",
            "MWWKKWKKWWM.",
            "MWWWWWWWWWM.",
            "MWWKKKKKWWM.",
            ".MWWWWWWWM..",
            "..MWM.MWM...",
            "..MM...MM..."
        }));

        /// <summary>Summoner: a robed caster holding a staff topped with a glowing orb (staff on the right).</summary>
        public static Sprite Summoner => _summoner != null ? _summoner : (_summoner = FromPattern("RedlineSummoner", new[]
        {
            ".....MMMM...W.",
            "....MWWWWM.WLW",
            "...MWWWWWWM.W.",
            "...MWKWWKWM.M.",
            "...MWWWWWWM.M.",
            "....MWWWWM..M.",
            "...MWWWWWWMWM.",
            "..MWWWWWWWWWM.",
            "..MWWWWWWWM.M.",
            "..MWWLWWLWM.M.",
            "..MWWWWWWWM.M.",
            ".MWWWWWWWWWMM.",
            ".MWWLWWWLWWMM.",
            ".MWWWWWWWWWMM.",
            "MWWWWWWWWWWWM.",
            "MWWWWWWWWWWWM.",
            "MMMMMMMMMMMMM."
        }));

        /// <summary>Shooter projectile: a small bright orb.</summary>
        public static Sprite Projectile => _projectile != null ? _projectile : (_projectile = FromPattern("RedlineProjectile", new[]
        {
            "..LL..",
            ".LWWL.",
            "LWWWWL",
            "LWWWWL",
            ".LWWL.",
            "..LL.."
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

        /// <summary>Sword pointing up, pivot at the grip (rotate it to swing).</summary>
        public static Sprite Sword => _sword != null ? _sword : (_sword = FromPattern("RedlineSword", new[]
        {
            "...W...",
            "..WWW..",
            "..WLW..",
            "..WLW..",
            "..WLW..",
            "..WLW..",
            "..WLW..",
            "..WLW..",
            "..WLW..",
            "..WLW..",
            "..WLW..",
            "..WLW..",
            "..WLW..",
            "MMMMMMM",
            ".MDDDM.",
            "...D...",
            "...D...",
            "..MMM.."
        }, new Vector2(0.5f, 3.5f / 18f)));

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
                _glow = Make("RedlineGlow", size, size, pixels, FilterMode.Bilinear, new Vector2(0.5f, 0.5f));
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
                _gradient = Make("RedlineGradient", 1, height, pixels, FilterMode.Bilinear, new Vector2(0.5f, 0.5f));
                return _gradient;
            }
        }

        private static Sprite FromPattern(string name, string[] rows) => FromPattern(name, rows, new Vector2(0.5f, 0.5f));

        private static Sprite FromPattern(string name, string[] rows, Vector2 pivot)
        {
            int h = rows.Length;
            int w = rows[0].Length;
            var pixels = new Color32[w * h];
            for (int row = 0; row < h; row++)
            {
                int y = h - 1 - row;
                for (int x = 0; x < w; x++) pixels[y * w + x] = Shade(x < rows[row].Length ? rows[row][x] : '.');
            }
            return Make(name, w, h, pixels, FilterMode.Point, pivot);
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
        private static Sprite Make(string name, int w, int h, Color32[] pixels, FilterMode filter, Vector2 pivot)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = filter,
                wrapMode = TextureWrapMode.Clamp,
                name = name
            };
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), pivot, Mathf.Max(w, h));
        }

        /// <summary>World size of a sprite made by this class at scale 1 (longer side = 1).</summary>
        public static Vector2 UnitSize(Sprite sprite) => sprite.bounds.size;
    }
}
