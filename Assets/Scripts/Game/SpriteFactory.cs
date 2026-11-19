using UnityEngine;

namespace Redline.Game
{
    /// <summary>
    /// Generates the few flat shapes the game draws (no art assets needed) and the two sprite
    /// materials: unlit (glows, effects, rain, sky: full brightness regardless of lights) and lit
    /// (URP 2D <c>Sprite-Lit-Default</c>: terrain, props and actors react to 2D lights and, when the
    /// sprite carries a <c>_NormalMap</c> secondary texture, to normal-mapped lights).
    /// </summary>
    /// <example>
    /// <code>
    /// var glow = SpriteFactory.Create("Glow", parent, PixelArt.Glow, Color.white, 0);       // unlit
    /// var tile = SpriteFactory.CreateLit("Tile", parent, AtmoArt.Tile(1, 0), Color.white, 0); // lit
    /// </code>
    /// </example>
    /// <remarks>Main-thread only (creates Unity objects).</remarks>
    public static class SpriteFactory
    {
        private const int Size = 16;
        private static Sprite _square;
        private static Sprite _triangle;
        private static Material _material;
        private static Material _litMaterial;

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
            _litMaterial = null;
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

        /// <summary>Creates a child object with a SpriteRenderer using the lit 2D sprite material.</summary>
        public static SpriteRenderer CreateLit(string name, Transform parent, Sprite sprite, Color color, int sortingOrder)
        {
            var sr = Create(name, parent, sprite, color, sortingOrder);
            var material = LitMaterial();
            if (material != null) sr.sharedMaterial = material;
            return sr;
        }

        /// <summary>
        /// The URP 2D renderer's lit sprite shader (the shader behind the 2D Renderer's default lit
        /// material, Sprite-Lit-Default). Included in builds because Renderer2D.asset references it.
        /// </summary>
        private static Material LitMaterial()
        {
            if (_litMaterial != null) return _litMaterial;
            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
            if (shader != null) _litMaterial = new Material(shader) { name = "RedlineLitSprite" };
            return _litMaterial;
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
