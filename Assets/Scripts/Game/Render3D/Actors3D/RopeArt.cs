using UnityEngine;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// Procedural pixel textures for the grappling hook: a twisted three-ply hemp rope (tiles along its length) and a
    /// kaginawa grapnel - an iron ring, a short shank and four prongs curving back (two in profile, two foreshortened
    /// behind them), dark rusty iron with a 1-texel outline. Both are point-sampled and alpha-cut (texel alpha 0 or 1),
    /// so they read as solid objects on the opaque Fx path (shape 3) and stay sharp under depth of field.
    /// </summary>
    /// <example>
    /// <code>
    /// mpb.SetTexture("_MainTex", RopeArt.Rope);
    /// mpb.SetVector("_MainTex_ST", new Vector4(length / (width * RopeArt.RopeAspect), 1f, 0f, 0f));
    /// </code>
    /// </example>
    public static class RopeArt
    {
        /// <summary>Texture width / height of one rope repeat (one twist period spans this many rope widths).</summary>
        public const float RopeAspect = 2f;

        private static Texture2D _rope, _hook;

        /// <summary>Rope tile: U runs along the rope (wraps), V across it.</summary>
        public static Texture2D Rope => _rope != null ? _rope : (_rope = BuildRope());

        /// <summary>Grapnel: shank along +U, ring at the left (rope end), prongs at the right (the end that bites).</summary>
        public static Texture2D Hook => _hook != null ? _hook : (_hook = BuildHook());

        private static Texture2D NewTex(int w, int h, string name, TextureWrapMode wrapU) =>
            new Texture2D(w, h, TextureFormat.RGBA32, false, false)
            {
                name = name, filterMode = FilterMode.Point, wrapModeU = wrapU, wrapModeV = TextureWrapMode.Clamp,
            };

        private static Texture2D BuildRope()
        {
            const int W = 16, H = 8;   // one twist: three plies cross the rope diagonally
            var t = NewTex(W, H, "HookRope", TextureWrapMode.Repeat);
            var light = new Color(0.84f, 0.76f, 0.58f, 1f);
            var mid = new Color(0.66f, 0.57f, 0.41f, 1f);
            var groove = new Color(0.32f, 0.26f, 0.19f, 1f);
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (y == 0 || y == H - 1) { px[y * W + x] = Color.clear; continue; }   // round-ish silhouette
                // Diagonal plies: phase along (x + y), three strands per period.
                float ph = Mathf.Repeat((x + y * 1.2f) / (W / 3f), 1f);
                Color c = ph < 0.18f ? groove : ph < 0.55f ? light : mid;
                if (y == 1 || y == H - 2) c = Color.Lerp(c, groove, 0.45f);   // shaded edges = a round cord
                px[y * W + x] = c;
            }
            t.SetPixels(px);
            t.Apply(false);
            return t;
        }

        private static Texture2D BuildHook()
        {
            const int N = 32;
            var t = NewTex(N, N, "HookGrapnel", TextureWrapMode.Clamp);
            var a = new int[N * N];          // 0 empty, 1 iron, 2 iron highlight, 3 back prong (darker)
            void Dot(float cx, float cy, float r, int v)
            {
                for (int y = Mathf.Max(0, (int)(cy - r - 1)); y <= Mathf.Min(N - 1, (int)(cy + r + 1)); y++)
                for (int x = Mathf.Max(0, (int)(cx - r - 1)); x <= Mathf.Min(N - 1, (int)(cx + r + 1)); x++)
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r && (a[y * N + x] == 0 || v != 3)) a[y * N + x] = v;
            }
            void Line(Vector2 p0, Vector2 p1, float r0, float r1, int v)
            {
                int n = Mathf.CeilToInt((p1 - p0).magnitude * 3f) + 1;
                for (int i = 0; i <= n; i++)
                {
                    float k = i / (float)n;
                    var p = Vector2.Lerp(p0, p1, k);
                    Dot(p.x, p.y, Mathf.Lerp(r0, r1, k), v);
                }
            }
            void Prong(float sign, int v, float spread)
            {
                // From the crown, out and up/down, then curling back toward the ring: a hooked claw.
                var crown = new Vector2(23f, 16f);
                var p1 = new Vector2(26.5f, 16f + sign * 4.5f * spread);
                var p2 = new Vector2(27f, 16f + sign * 10.5f * spread);
                var p3 = new Vector2(23.5f, 16f + sign * 13.5f * spread);
                var tip = new Vector2(19.5f, 16f + sign * 12.2f * spread);
                Line(crown, p1, 1.5f, 1.4f, v);
                Line(p1, p2, 1.4f, 1.2f, v);
                Line(p2, p3, 1.2f, 0.9f, v);
                Line(p3, tip, 0.9f, 0.4f, v);
            }
            Prong(1f, 3, 0.62f);              // back pair, foreshortened
            Prong(-1f, 3, 0.62f);
            Prong(1f, 1, 1f);                 // front pair in profile
            Prong(-1f, 1, 1f);
            Line(new Vector2(7f, 16f), new Vector2(24f, 16f), 1.6f, 1.9f, 1);   // shank
            Line(new Vector2(9f, 17.2f), new Vector2(22f, 17.2f), 0.4f, 0.4f, 2);
            for (int k = 0; k < 24; k++)      // iron ring at the rope end
            {
                float ang = k / 24f * Mathf.PI * 2f;
                Dot(4.5f + Mathf.Cos(ang) * 3.2f, 16f + Mathf.Sin(ang) * 3.2f, 0.9f, 1);
            }
            var iron = new Color(0.45f, 0.33f, 0.24f, 1f);
            var hi = new Color(0.66f, 0.52f, 0.38f, 1f);
            var back = new Color(0.3f, 0.22f, 0.17f, 1f);
            var outline = new Color(0.1f, 0.08f, 0.07f, 1f);
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int v = a[y * N + x];
                if (v == 0)
                {
                    bool edge = false;
                    for (int dy = -1; dy <= 1 && !edge; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx >= 0 && yy >= 0 && xx < N && yy < N && a[yy * N + xx] != 0) { edge = true; break; }
                    }
                    px[y * N + x] = edge ? outline : Color.clear;
                    continue;
                }
                px[y * N + x] = v == 2 ? hi : v == 3 ? back : iron;
            }
            t.SetPixels(px);
            t.Apply(false);
            return t;
        }
    }
}
