using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// Keeps the painted ink backdrop cheap to (re)build. Painting every layer costs ~2 s of CPU, and the textures are the
    /// same on every map, so: <see cref="WarmInkBackdrop"/> paints all of them on a worker thread as soon as the game
    /// boots (while the main menu is up), and <see cref="InkTex"/> turns each one into a texture once and keeps it for
    /// the rest of the session. Arena rebuilds (PLAY, CONTINUE, a new map) then only create materials and meshes.
    /// </summary>
    /// <remarks>Cached textures are not <c>Own</c>ed: an arena's Dispose must not destroy them.</remarks>
    public sealed partial class Arena3DRenderer
    {
        /// <summary>A finished layer as plain pixels (made off the main thread, turned into a texture on it).</summary>
        private sealed class InkImage
        {
            public string Name;
            public int W, H;
            public Color32[] Px;
            public bool Repeat;

            public Texture2D ToTexture()
            {
                var t = new Texture2D(W, H, TextureFormat.RGBA32, false, false)
                {
                    wrapMode = Repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = Name,
                    hideFlags = HideFlags.DontUnloadUnusedAsset,
                };
                t.SetPixels32(Px);
                bool dump = !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("REDLINE_INK_DUMP"));
                t.Apply(false, !dump);   // no CPU copy kept (the pixels live on the GPU) unless dumping to PNG
                if (dump) DumpInk(Name, t);
                return t;
            }
        }

        /// <summary>The four rock ranges, far to near. Value: 0 = ink black, 1 = paper (distance fades the ink).</summary>
        private static readonly InkRange[] InkRanges =
        {
            new InkRange { Z = 320f, Period = 980f, Height = 175f, Base = -40f, Value = 0.55f, Alpha = 0.95f, Peaks = 1.0f, Seed = 11, Birds = true },
            new InkRange { Z = 230f, Period = 700f, Height = 128f, Base = -26f, Value = 0.4f, Alpha = 1f, Peaks = 1.1f, Seed = 12 },
            new InkRange { Z = 160f, Period = 440f, Height = 86f, Base = -16f, Value = 0.26f, Alpha = 1f, Peaks = 1.2f, Seed = 13, Pines = true, Waterfall = true, PineScale = 0.42f },
            new InkRange { Z = 110f, Period = 270f, Height = 50f, Base = -9f, Value = 0.16f, Alpha = 1f, Peaks = 1.0f, Seed = 14, Pagodas = true, Pines = true, PineScale = 0.32f },
        };

        private static readonly Dictionary<string, Texture2D> InkTexCache = new Dictionary<string, Texture2D>();
        private static Task<Dictionary<string, InkImage>> _inkPaint;

        /// <summary>Every cached layer: its key and its painter.</summary>
        private static IEnumerable<KeyValuePair<string, Func<InkImage>>> InkJobs()
        {
            KeyValuePair<string, Func<InkImage>> J(string k, Func<InkImage> f) => new KeyValuePair<string, Func<InkImage>>(k, f);
            yield return J("stain", SkyStainTexture);
            yield return J("moon", InkMoonTexture);
            yield return J("cloud41", () => CloudTexture(41, true));
            yield return J("cloud51", () => CloudTexture(51, false));
            yield return J("cloud52", () => CloudTexture(52, false));
            yield return J("cloud53", () => CloudTexture(53, false));
            for (int i = 0; i < InkRanges.Length; i++)
            {
                var r = InkRanges[i];
                yield return J("rock" + i, () => RockRangeTexture(r));
            }
            yield return J("pillars", () => PillarsTexture(31));
            yield return J("tree", () => ForeTreeTexture(61));
        }

        /// <summary>Starts painting the whole ink backdrop on worker threads (call once at boot; later calls do nothing).</summary>
        public static void WarmInkBackdrop()
        {
            if (_inkPaint != null) return;
            _inkPaint = Task.Run(() =>
            {
                var jobs = new List<KeyValuePair<string, Func<InkImage>>>(InkJobs());
                var done = new InkImage[jobs.Count];
                Parallel.For(0, jobs.Count, k => done[k] = jobs[k].Value());
                var map = new Dictionary<string, InkImage>();
                for (int k = 0; k < jobs.Count; k++) map[jobs[k].Key] = done[k];
                return map;
            });
        }

        /// <summary>The cached texture for <paramref name="key"/>: made from the warm-up's pixels (waiting for it if it
        /// is still running), or painted right here when there was no warm-up.</summary>
        private static Texture2D InkTex(string key, Func<InkImage> paint)
        {
            if (InkTexCache.TryGetValue(key, out var cached) && cached != null) return cached;
            InkImage img = null;
            if (_inkPaint != null)
            {
                try
                {
                    if (_inkPaint.Result.TryGetValue(key, out var pre)) img = pre;   // Result waits if still painting
                }
                catch (Exception e) { Debug.LogWarning("Redline: ink backdrop warm-up failed, painting inline. " + e.Message); }
            }
            if (img == null) img = paint();
            var t = img.ToTexture();
            img.Px = null;   // the pixels are on the GPU now
            InkTexCache[key] = t;
            return t;
        }
    }
}
