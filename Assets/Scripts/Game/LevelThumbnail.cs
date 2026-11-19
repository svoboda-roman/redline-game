using Redline.Core;
using UnityEngine;

namespace Redline.Game
{
    /// <summary>
    /// Draws a level's layout into a small texture for LEVEL SELECT: night-sky gradient, scaffold-steel
    /// platforms with a lit top edge, neon-red spikes, a green marker at the player start. Built from the
    /// level file itself, so the picture always matches the map.
    /// </summary>
    public static class LevelThumbnail
    {
        private static readonly Color SkyTop = new Color(0.06f, 0.04f, 0.10f), SkyBottom = new Color(0.20f, 0.06f, 0.14f);
        private static readonly Color Steel = new Color(0.30f, 0.33f, 0.42f), SteelEdge = new Color(0.78f, 0.82f, 0.92f);
        private static readonly Color Spike = new Color(1f, 0.16f, 0.24f), Start = new Color(0.3f, 1f, 0.55f);
        private static readonly Color Frame = new Color(1f, 0.55f, 0.2f);

        /// <summary>A point-filtered texture of <paramref name="map"/>, <paramref name="px"/> pixels per tile.</summary>
        public static Texture2D Build(TileMap map, int px = 6)
        {
            int w = map.Width * px, h = map.Height * px;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var pix = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                Color32 sky = Color.Lerp(SkyBottom, SkyTop, (float)y / h);
                for (int x = 0; x < w; x++) pix[y * w + x] = sky;
            }
            for (int ty = 0; ty < map.Height; ty++)
            for (int tx = 0; tx < map.Width; tx++)
            {
                var t = map.Get(tx, ty);
                if (t == Tile.Solid)
                {
                    bool top = !map.IsSolid(tx, ty + 1);
                    Rect(pix, w, tx * px, ty * px, px, px, Steel);
                    if (top) Rect(pix, w, tx * px, ty * px + px - 1, px, 1, SteelEdge);
                }
                else if (t == Tile.Spike)
                {
                    // a little triangle pointing away from what it sits on (up by default)
                    bool hang = !map.IsSolid(tx, ty - 1) && map.IsSolid(tx, ty + 1);
                    for (int k = 0; k < px; k++)
                    {
                        int half = (px - k) / 2;
                        int row = hang ? ty * px + px - 1 - k : ty * px + k;
                        Rect(pix, w, tx * px + px / 2 - half, row, half * 2 + 1, 1, Spike);
                    }
                }
            }
            int sx = Mathf.RoundToInt(map.PlayerStart.X * px), sy = Mathf.RoundToInt(map.PlayerStart.Y * px);
            Rect(pix, w, sx - px / 2, sy, px, px * 2, Start);
            Rect(pix, w, 0, 0, w, 2, Frame); Rect(pix, w, 0, h - 2, w, 2, Frame);
            Rect(pix, w, 0, 0, 2, h, Frame); Rect(pix, w, w - 2, 0, 2, h, Frame);
            tex.SetPixels32(pix);
            tex.Apply(false, true);
            return tex;
        }

        private static void Rect(Color32[] pix, int w, int x0, int y0, int rw, int rh, Color c)
        {
            int h = pix.Length / w;
            Color32 c32 = c;
            for (int y = Mathf.Max(0, y0); y < Mathf.Min(h, y0 + rh); y++)
            for (int x = Mathf.Max(0, x0); x < Mathf.Min(w, x0 + rw); x++)
                pix[y * w + x] = c32;
        }
    }
}
