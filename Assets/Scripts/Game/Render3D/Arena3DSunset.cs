using UnityEngine;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// The neon sunset backdrop of <see cref="Arena3DRenderer"/> (<see cref="Arena3DLook.Sunset"/>): a big sun with
    /// retro horizontal cut stripes setting behind the city (yellow at the top, pink / red at the bottom), its wide soft
    /// halo and a warm horizon band, and neon signs (vertical blades, horizontal boards) on the nearer city rows.
    /// Everything follows the camera like the sky and the city, so the sun stays put while the level scrolls.
    /// </summary>
    public sealed partial class Arena3DRenderer
    {
        /// <summary>Depth of the sun plane: behind every city row (farthest row is z 185 + 8) and in front of the sky (z 430).</summary>
        private const float SunDepth = 380f;

        private Material _signMat;

        private void SunsetBackdrop()
        {
            if (!_look.Sunset) return;
            // The sun: additive, unfogged, drawn before the haze veils so the near haze still tints its lower edge.
            var sunTex = Own(SunTexture(_look.SunTop, _look.SunBottom));
            var sunMat = Own(Proto3DMaterials.FxMat("sun", sunTex, Color.white, 0, 0f, 0f, -60));
            var sun = MeshGo("sun", _follow, Own(Proto3DMeshes.Quad(_look.SunSize, _look.SunSize)), sunMat);
            sun.transform.localPosition = new Vector3(_look.SunOffset.x, _look.SunOffset.y, SunDepth);
            NoShadow(Hidden(sun));

            // Halo around the sun and a warm band along the horizon (unfogged soft blobs).
            var haloMat = Own(Proto3DMaterials.FxMat("sunHalo", _blob, Color.white, 0, 0f, 0f, -61));
            var halo = new QuadBatch();
            var c = new Vector3(_look.SunOffset.x, _look.SunOffset.y, SunDepth + 5f);
            halo.Billboard(c, _look.SunSize * 2.8f, _look.SunSize * 2.8f, Color.Lerp(_look.SunBottom, _look.SunTop, 0.4f) * 0.32f);
            halo.Billboard(c, _look.SunSize * 1.5f, _look.SunSize * 1.5f, _look.SunTop * 0.22f);
            halo.Billboard(new Vector3(0f, FloorTop + 8f, SunDepth - 20f), 1400f, 140f, _look.SunBottom * 0.22f);
            NoShadow(Hidden(MeshGo("sunHalo", _follow, Own(halo.Build("sunHalo")), haloMat)));
        }

        /// <summary>Lightly fogged additive material for the city's neon signs (they must read through the pink fog).</summary>
        private Material SignMaterial()
        {
            if (_signMat == null) _signMat = Own(Proto3DMaterials.FxMat("citySigns", null, Color.white, 0, 0.3f));
            return _signMat;
        }

        /// <summary>
        /// One neon sign on a city building front (x..x+w wide, roof at <paramref name="top"/>, front face at
        /// <paramref name="front"/>): a vertical blade or a horizontal board, a darker frame, a bright core and a soft glow.
        /// </summary>
        private void CitySign(QuadBatch signs, QuadBatch glow, float x, float w, float top, float front, float rowZ)
        {
            var col = _look.SignColors[_rnd.Next(_look.SignColors.Length)];
            float scale = 1f + rowZ * 0.012f;   // farther rows: bigger signs so they still read
            bool blade = _rnd.NextDouble() < 0.5;
            float sw = blade ? (0.9f + (float)_rnd.NextDouble() * 0.7f) * scale : Mathf.Min(w * 0.8f, (3f + (float)_rnd.NextDouble() * 3f) * scale);
            float sh = blade ? (4f + (float)_rnd.NextDouble() * 5f) * scale : (0.9f + (float)_rnd.NextDouble() * 0.6f) * scale;
            float cx = x + w * (0.25f + (float)_rnd.NextDouble() * 0.5f);
            float cy = Mathf.Min(top - sh * 0.6f, -2f + (float)_rnd.NextDouble() * (top + 2f));
            if (cy - sh * 0.5f < -6f) cy = -6f + sh * 0.5f;
            var p = new Vector3(cx, cy, front - 0.3f);
            signs.Billboard(p, sw + 0.35f * scale, sh + 0.35f * scale, col * 0.35f);       // frame
            signs.Billboard(p + Vector3.back * 0.02f, sw, sh, col * 1.6f);                  // lit panel
            // Lit "letters": short dark gaps across the panel so it reads as a sign, not a plain bar.
            int n = blade ? Mathf.Max(2, Mathf.RoundToInt(sh / (1.1f * scale))) : Mathf.Max(2, Mathf.RoundToInt(sw / (1.1f * scale)));
            for (int i = 0; i < n; i++)
            {
                float t = (i + 0.5f) / n - 0.5f;
                var lp = blade ? p + new Vector3(0f, t * sh * 0.9f, -0.04f) : p + new Vector3(t * sw * 0.9f, 0f, -0.04f);
                float lw = blade ? sw * 0.55f : sw / n * 0.6f, lh = blade ? sh / n * 0.6f : sh * 0.55f;
                signs.Billboard(lp, lw, lh, Color.Lerp(col, Color.white, 0.55f) * 1.4f);
            }
            glow.Billboard(p + Vector3.back * 0.5f, (sw + 3f) * 2.2f, (sh + 3f) * 1.8f, col * 0.35f);
        }

        /// <summary>
        /// Sun disc texture: vertical gradient from <paramref name="top"/> to <paramref name="bottom"/>, a soft edge, and
        /// horizontal cut stripes in the lower half that get thicker toward the bottom (retro sunset). RGB premultiplied.
        /// </summary>
        private static Texture2D SunTexture(Color top, Color bottom)
        {
            const int N = 256;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false, false)
            {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "sun",
            };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
            {
                float v = (y + 0.5f) / N;                 // 0 bottom .. 1 top
                float dy = v * 2f - 1f;
                var grad = Color.Lerp(bottom, top, Mathf.SmoothStep(0.05f, 0.95f, v));
                // Stripes: below the middle, bands whose gap grows toward the bottom.
                float cut = 1f;
                if (v < 0.5f)
                {
                    float k = (0.5f - v) / 0.5f;          // 0 at the middle .. 1 at the bottom
                    float period = 0.085f;
                    float phase = Mathf.Repeat((0.5f - v) / period, 1f);
                    float gap = Mathf.Lerp(0.08f, 0.55f, k);
                    cut = Mathf.SmoothStep(gap - 0.03f, gap + 0.03f, phase);
                }
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float disc = 1f - Mathf.SmoothStep(0.94f, 1f, r);
                    float a = disc * cut;
                    var c = grad * a;
                    c.a = a;
                    px[y * N + x] = c;
                }
            }
            t.SetPixels(px);
            t.Apply(false);
            return t;
        }
    }
}
