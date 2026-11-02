using System;
using System.IO;
using System.Text;
using Redline.Core;

namespace Redline.Tests
{
    /// <summary>Factories for test maps, configs and simulation runs.</summary>
    public static class TestSupport
    {
        public const float Dt = RedlineSim.FixedDt;

        /// <summary>Config with waves pushed far into the future so movement tests run without enemies.</summary>
        public static RedlineConfig NoWaves()
        {
            return new RedlineConfig { firstWaveDelay = 1e6f };
        }

        /// <summary>
        /// Walled box with a floor. Player start at cell (2, 1), enemy spawn at (width-3, 1).
        /// Optional solid row at ceilingRow (y, counted from the bottom) spanning the interior.
        /// </summary>
        public static TileMap Box(int width, int height, int ceilingRow = -1)
        {
            var sb = new StringBuilder();
            for (int y = height - 1; y >= 0; y--)
            {
                for (int x = 0; x < width; x++)
                {
                    bool border = x == 0 || x == width - 1 || y == 0 || y == height - 1;
                    char c = border || y == ceilingRow ? '#' : '.';
                    if (y == 1 && x == 2) c = 'P';
                    if (y == 1 && x == width - 3) c = 'S';
                    sb.Append(c);
                }
                sb.Append('\n');
            }
            return TileMap.Parse(sb.ToString());
        }

        /// <summary>
        /// 60×10 room with a 1-tile-high tunnel from x=20 to x=44 (floor top y=1, ceiling tile at y=2):
        /// tall enough for a sliding player (0.8), too low to stand (1.6).
        /// </summary>
        public static TileMap Tunnel()
        {
            var sb = new StringBuilder();
            const int width = 60, height = 10;
            for (int y = height - 1; y >= 0; y--)
            {
                for (int x = 0; x < width; x++)
                {
                    bool border = x == 0 || x == width - 1 || y == 0 || y == height - 1;
                    bool ceiling = y == 2 && x >= 20 && x <= 44;
                    char c = border || ceiling ? '#' : '.';
                    if (y == 1 && x == 2) c = 'P';
                    if (y == 1 && x == width - 3) c = 'S';
                    sb.Append(c);
                }
                sb.Append('\n');
            }
            return TileMap.Parse(sb.ToString());
        }

        /// <summary>40×12 room with a ledge (solid row y=4, x=1..12) the player can walk off.</summary>
        public static TileMap Ledge()
        {
            var sb = new StringBuilder();
            const int width = 40, height = 12;
            for (int y = height - 1; y >= 0; y--)
            {
                for (int x = 0; x < width; x++)
                {
                    bool border = x == 0 || x == width - 1 || y == 0 || y == height - 1;
                    bool ledge = y == 4 && x <= 12;
                    char c = border || ledge ? '#' : '.';
                    if (y == 1 && x == 20) c = 'P';
                    if (y == 1 && x == width - 3) c = 'S';
                    sb.Append(c);
                }
                sb.Append('\n');
            }
            return TileMap.Parse(sb.ToString());
        }

        /// <summary>True when the last step emitted an event of this type.</summary>
        public static bool HasEvent(RedlineSim sim, SimEventType type)
        {
            foreach (var e in sim.Events)
                if (e.Type == type) return true;
            return false;
        }

        /// <summary>Steps the sim for the given seconds with a constant input (edge flags only on the first step).</summary>
        public static void Run(RedlineSim sim, InputFrame input, float seconds)
        {
            int steps = (int)Math.Round(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                sim.Step(input, Dt);
                input.JumpPressed = false;
                input.AttackPressed = false;
                input.SlidePressed = false;
                input.HookPressed = false;
                input.RestartPressed = false;
            }
        }

        public static InputFrame Hold(float moveX) => new InputFrame { MoveX = moveX };

        /// <summary>Moves the player and refreshes its ground/wall contacts, as a real move would.</summary>
        public static void Teleport(RedlineSim sim, float x, float y)
        {
            sim.Player.X = x;
            sim.Player.Y = y;
            sim.Player.RefreshContacts(sim.Map);
        }

        /// <summary>Repository root (the folder that contains Assets/), found from the working or binary directory.</summary>
        public static string ProjectRoot()
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "Assets", "Resources", "Arena01.txt"))) return dir.FullName;
                    dir = dir.Parent;
                }
            }
            throw new DirectoryNotFoundException("Could not find the project root containing Assets/Resources/Arena01.txt");
        }

        public static string ReadResource(string fileName) =>
            File.ReadAllText(Path.Combine(ProjectRoot(), "Assets", "Resources", fileName));
    }
}
