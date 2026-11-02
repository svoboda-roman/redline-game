using UnityEngine;

namespace Redline.Game
{
    /// <summary>
    /// Generates the few flat shapes the game draws (no art assets needed) and one unlit
    /// sprite material so the neon colours show at full brightness without 2D lights.
    /// </summary>
    /// <example>
    /// <code>
    /// var sr = SpriteFactory.Create("Tile", parent, SpriteFactory.Square, Color.white, 0);
    /// </code>
    /// </example>
    /// <remarks>Main-thread only (creates Unity objects).</remarks>
    public static class SpriteFactory
    {
        private const int Size = 16;
        private static Sprite _square;
        private static Sprite _triangle;
        private static Material _material;

        /// <summary>
        /// Clears the cached objects before each Play. The project enters Play Mode without a
        /// domain reload, so statics survive while the objects they point to are destroyed.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _square = null;
            _triangle = null;
            _material = null;
        }

        /// <summary>A 1×1 world-unit white square, pivot in the centre.</summary>
        public static Sprite Square => _square != null ? _square : (_square = Build(false));

        /// <summary>A 1×1 world-unit white upward triangle, pivot in the centre.</summary>
        public static Sprite Triangle => _triangle != null ? _triangle : (_triangle = Build(true));

        /// <summary>Creates a child object with a SpriteRenderer.</summary>
        public static SpriteRenderer Create(string name, Transform parent, Sprite sprite, Color color, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = sortingOrder;
            var material = UnlitMaterial();
            if (material != null) sr.sharedMaterial = material;
            return sr;
        }

        private static Material UnlitMaterial()
        {
            if (_material != null) return _material;
            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader != null) _material = new Material(shader) { name = "RedlineUnlitSprite" };
            return _material;
        }

        private static Sprite Build(bool triangle)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = triangle ? "RedlineTriangle" : "RedlineSquare"
            };
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    bool inside = true;
                    if (triangle)
                    {
                        // Apex at the top centre, base along the bottom row.
                        float half = (Size - y) * 0.5f;
                        inside = Mathf.Abs(x + 0.5f - Size * 0.5f) <= half;
                    }
                    pixels[y * Size + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), Size);
        }
    }
}
