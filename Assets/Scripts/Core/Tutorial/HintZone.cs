using System;

namespace Redline.Core
{
    /// <summary>
    /// A tutorial hint zone from a level's ';hint x0 x1 id' line: while the player's feet are over
    /// the cell columns x0..x1 (inclusive) the hint <see cref="Id"/> may show. On a ring level
    /// x0 &gt; x1 is allowed and means the zone runs across the seam (x0..Width-1, then 0..x1).
    /// Ids use lowercase letters, digits, '.', '_' and '-'; their text lives in Resources/Hints.json.
    /// </summary>
    public readonly struct HintZone
    {
        /// <summary>First cell column of the zone.</summary>
        public readonly int X0;
        /// <summary>Last cell column of the zone (inclusive).</summary>
        public readonly int X1;
        /// <summary>Hint id, a key in Hints.json.</summary>
        public readonly string Id;

        /// <summary>Creates a zone; use <see cref="Parse"/> for level lines.</summary>
        public HintZone(int x0, int x1, string id)
        {
            X0 = x0;
            X1 = x1;
            Id = id;
        }

        /// <summary>True when a (wrapped) x position lies over the zone's columns.</summary>
        public bool Contains(float wrappedX)
        {
            int cell = (int)MathF.Floor(wrappedX);
            return X0 <= X1 ? cell >= X0 && cell <= X1 : cell >= X0 || cell <= X1;
        }

        /// <summary>
        /// Parses ';hint x0 x1 id' for a map of the given width. Throws FormatException (without a
        /// line number; <see cref="TileMap.Parse"/> adds it) when the line is malformed.
        /// </summary>
        public static HintZone Parse(string line, int mapWidth, bool wraps)
        {
            if (line == null) throw new ArgumentNullException(nameof(line));
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4 || parts[0] != ";hint")
                throw new FormatException($"expected ';hint x0 x1 id', got '{line}'");
            int x0 = Column(parts[1], mapWidth);
            int x1 = Column(parts[2], mapWidth);
            if (x0 > x1 && !wraps)
                throw new FormatException($"hint x0 {x0} is after x1 {x1}; only a ';wrap' level may run a zone across the seam");
            string id = parts[3];
            if (!IsValidId(id)) throw new FormatException($"hint id '{id}' must use only a-z, 0-9, '.', '_' or '-'");
            return new HintZone(x0, x1, id);
        }

        /// <summary>True for a non-empty id of lowercase letters, digits, '.', '_' and '-'.</summary>
        public static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            foreach (char c in id)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-')) return false;
            return true;
        }

        private static int Column(string text, int mapWidth)
        {
            if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int x))
                throw new FormatException($"hint column '{text}' is not a whole number");
            if (x >= mapWidth) throw new FormatException($"hint column {x} is outside the map (width {mapWidth})");
            return x;
        }
    }
}
