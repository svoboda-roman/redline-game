using System;
using System.Collections.Generic;
using UnityEngine;

namespace Redline.Game.Art
{
    /// <summary>One of the three emissive masks packed in a sheet's <c>_emissive</c> atlas.</summary>
    public enum EmissiveChannel
    {
        /// <summary>Red channel. Samurai: katana blade and slash smear (tinted by speed tier).</summary>
        R = 0,
        /// <summary>Green channel. Samurai: visor slit.</summary>
        G = 1,
        /// <summary>Blue channel. Samurai: cyber-arm light strips.</summary>
        B = 2
    }

    /// <summary>
    /// One named animation of a <see cref="CharacterSheet"/>: frame sprites (lit, with the normal
    /// map attached), per-channel emissive sprites, playback rate and loop flag. Immutable.
    /// </summary>
    public sealed class SheetAnimation
    {
        private readonly Sprite[] _frames;
        private readonly Sprite[][] _emissive;
        private readonly Vector2[][] _centroids;

        internal SheetAnimation(string name, float fps, bool loop, Sprite[] frames, Sprite[][] emissive, Vector2[][] centroids)
        {
            Name = name;
            Fps = fps > 0f ? fps : 1f;
            Loop = loop;
            _frames = frames;
            _emissive = emissive;
            _centroids = centroids;
        }

        /// <summary>Animation name as written in the sheet JSON (e.g. <c>"run"</c>).</summary>
        public string Name { get; }

        /// <summary>Authored playback rate, frames per second.</summary>
        public float Fps { get; }

        /// <summary>True: wraps around. False: holds the last frame when it ends.</summary>
        public bool Loop { get; }

        /// <summary>Number of frames.</summary>
        public int FrameCount => _frames.Length;

        /// <summary>Length of one pass at the authored rate, in seconds.</summary>
        public float Duration => _frames.Length / Fps;

        /// <summary>The lit colour sprite of frame <paramref name="index"/> (normal map attached as <c>_NormalMap</c>).</summary>
        public Sprite Frame(int index) => _frames[index];

        /// <summary>
        /// The emissive mask of frame <paramref name="index"/> for one channel: a white sprite whose
        /// alpha is the mask, meant for an unlit renderer tinted with the channel's colour. Null when
        /// the channel is empty in that frame (disable the overlay renderer).
        /// </summary>
        public Sprite Emissive(EmissiveChannel channel, int index) => _emissive[(int)channel][index];

        /// <summary>
        /// Centre of the channel's lit pixels in frame <paramref name="index"/>, in world units from the
        /// pivot, for the unflipped (facing-right) sprite. Zero when the channel is empty.
        /// </summary>
        public Vector2 EmissiveCentroid(EmissiveChannel channel, int index) => _centroids[(int)channel][index];
    }

    /// <summary>
    /// Loads a character sprite sheet from <c>Resources</c>: <c>&lt;path&gt;_color</c>,
    /// <c>&lt;path&gt;_normal</c>, <c>&lt;path&gt;_emissive</c> (same layout) and <c>&lt;path&gt;.json</c>
    /// (ppu, per-frame rect with a bottom-left origin and normalised pivot, per-animation fps, loop flag
    /// and frame names). Every frame's sprite is built once, with the normal map as a secondary
    /// texture; the emissive atlas is split once into three white alpha-mask atlases (one per
    /// channel) so each glow can be tinted independently, then the source is unloaded. Sheets are
    /// cached by path: loading the same sheet twice returns the same instance.
    /// </summary>
    /// <example>
    /// <code>
    /// var sheet = CharacterSheet.Load("Art/Characters/Samurai/samurai");
    /// var run = sheet.Animation("run");               // frames, fps, loop
    /// var animator = new SpriteAnimator();
    /// animator.Play(run);
    /// // per frame:
    /// animator.Tick(Time.deltaTime);
    /// body.sprite = animator.Current.Frame(animator.Frame);
    /// blade.sprite = animator.Current.Emissive(EmissiveChannel.R, animator.Frame);
    /// blade.enabled = blade.sprite != null;
    /// </code>
    /// </example>
    /// <remarks>
    /// Main-thread only. Import rules live in <c>Assets/Editor/ArtSheetImporter.cs</c> (the emissive
    /// atlas must be Read/Write). Loading allocates; lookups and frame access do not.
    /// GPU cost per sheet: colour + normal (RGBA32) plus three mask atlases (RGBA4444 where supported).
    /// </remarks>
    public sealed class CharacterSheet
    {
        private const string NormalMapProperty = "_NormalMap";
        private const int Channels = 3;

        private static readonly Dictionary<string, CharacterSheet> Cache = new Dictionary<string, CharacterSheet>(StringComparer.Ordinal);

        private readonly Dictionary<string, SheetAnimation> _animations = new Dictionary<string, SheetAnimation>(StringComparer.Ordinal);
        private readonly Texture2D _color;

        /// <summary>Sheet name from the JSON (e.g. <c>"samurai"</c>).</summary>
        public string Name { get; private set; }

        /// <summary>Pixels per world unit the sprites were built with.</summary>
        public float PixelsPerUnit { get; private set; }

        /// <summary>Every animation name in the sheet.</summary>
        public IEnumerable<string> AnimationNames => _animations.Keys;

        private CharacterSheet(Texture2D color)
        {
            _color = color;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => Cache.Clear();

        /// <summary>
        /// Loads (or returns the cached) sheet at a Resources path without suffix, e.g.
        /// <c>"Art/Characters/Samurai/samurai"</c>.
        /// </summary>
        /// <exception cref="InvalidOperationException">A texture or the JSON is missing or malformed.</exception>
        public static CharacterSheet Load(string resourcePath)
        {
            if (Cache.TryGetValue(resourcePath, out var cached) && cached._color != null) return cached;
            var sheet = Build(resourcePath);
            Cache[resourcePath] = sheet;
            return sheet;
        }

        /// <summary>The named animation.</summary>
        /// <exception cref="KeyNotFoundException">No animation of that name in the sheet.</exception>
        public SheetAnimation Animation(string name)
        {
            if (_animations.TryGetValue(name, out var a)) return a;
            throw new KeyNotFoundException("CharacterSheet '" + Name + "' has no animation '" + name + "'");
        }

        /// <summary>The named animation, if the sheet has it.</summary>
        public bool TryGetAnimation(string name, out SheetAnimation animation) => _animations.TryGetValue(name, out animation);

        private static CharacterSheet Build(string path)
        {
            var json = Resources.Load<TextAsset>(path);
            if (json == null) throw new InvalidOperationException("CharacterSheet: missing Resources/" + path + ".json");
            var data = JsonUtility.FromJson<SheetJson>(json.text);
            if (data == null || data.frames == null || data.animations == null || data.ppu <= 0f)
                throw new InvalidOperationException("CharacterSheet: malformed " + path + ".json");

            var color = LoadTexture(path + "_color");
            var normal = LoadTexture(path + "_normal");
            var emissive = LoadTexture(path + "_emissive");
            if (!emissive.isReadable)
                throw new InvalidOperationException("CharacterSheet: " + path + "_emissive must be Read/Write (see ArtSheetImporter)");

            var sheet = new CharacterSheet(color) { Name = data.name, PixelsPerUnit = data.ppu };
            var masks = SplitEmissive(emissive, out var src);
            int w = emissive.width;
            Resources.UnloadAsset(emissive);

            var secondary = normal != null
                ? new[] { new SecondarySpriteTexture { name = NormalMapProperty, texture = normal } }
                : Array.Empty<SecondarySpriteTexture>();

            var frames = new Dictionary<string, FrameJson>(data.frames.Length, StringComparer.Ordinal);
            foreach (var f in data.frames) frames[f.name] = f;

            foreach (var a in data.animations)
            {
                int n = a.frames?.Length ?? 0;
                var sprites = new Sprite[n];
                var glow = new Sprite[Channels][];
                var centroids = new Vector2[Channels][];
                for (int c = 0; c < Channels; c++)
                {
                    glow[c] = new Sprite[n];
                    centroids[c] = new Vector2[n];
                }
                for (int i = 0; i < n; i++)
                {
                    if (!frames.TryGetValue(a.frames[i], out var f))
                        throw new InvalidOperationException("CharacterSheet: " + path + " animation '" + a.name + "' names unknown frame '" + a.frames[i] + "'");
                    var rect = new Rect(f.rect.x, f.rect.y, f.rect.w, f.rect.h);
                    var pivot = new Vector2(f.pivot.x, f.pivot.y);
                    sprites[i] = Sprite.Create(color, rect, pivot, data.ppu, 0, SpriteMeshType.FullRect, Vector4.zero, false, secondary);
                    sprites[i].name = f.name;
                    for (int c = 0; c < Channels; c++)
                    {
                        if (!MaskStats(src, w, f.rect, c, out var centre)) continue;
                        glow[c][i] = Sprite.Create(masks[c], rect, pivot, data.ppu, 0, SpriteMeshType.FullRect);
                        glow[c][i].name = f.name + "_e" + c;
                        centroids[c][i] = (centre - new Vector2(pivot.x * f.rect.w, pivot.y * f.rect.h)) / data.ppu;
                    }
                }
                sheet._animations[a.name] = new SheetAnimation(a.name, a.fps, a.loop, sprites, glow, centroids);
            }
            return sheet;
        }

        private static Texture2D LoadTexture(string path)
        {
            var t = Resources.Load<Texture2D>(path);
            if (t == null) throw new InvalidOperationException("CharacterSheet: missing Resources/" + path + ".png");
            return t;
        }

        /// <summary>Three white atlases whose alpha is the R, G or B mask of the emissive atlas.</summary>
        private static Texture2D[] SplitEmissive(Texture2D emissive, out Color32[] src)
        {
            src = emissive.GetPixels32();
            int w = emissive.width, h = emissive.height;
            var format = SystemInfo.SupportsTextureFormat(TextureFormat.RGBA4444) ? TextureFormat.RGBA4444 : TextureFormat.RGBA32;
            var result = new Texture2D[Channels];
            var px = new Color32[src.Length];
            for (int c = 0; c < Channels; c++)
            {
                for (int i = 0; i < src.Length; i++)
                {
                    var s = src[i];
                    byte v = c == 0 ? s.r : c == 1 ? s.g : s.b;
                    px[i] = new Color32(255, 255, 255, v);
                }
                var t = new Texture2D(w, h, format, false) { name = emissive.name + "_mask" + c, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                t.SetPixels32(px);
                t.Apply(false, true);
                result[c] = t;
            }
            return result;
        }

        /// <summary>Whether a channel has any lit texel in a frame rect, and the centre of those texels (rect pixels).</summary>
        private static bool MaskStats(Color32[] src, int width, RectJson r, int channel, out Vector2 centre)
        {
            double sx = 0, sy = 0;
            int count = 0;
            for (int y = 0; y < r.h; y++)
            {
                int row = (r.y + y) * width + r.x;
                for (int x = 0; x < r.w; x++)
                {
                    var s = src[row + x];
                    byte v = channel == 0 ? s.r : channel == 1 ? s.g : s.b;
                    if (v == 0) continue;
                    sx += x + 0.5;
                    sy += y + 0.5;
                    count++;
                }
            }
            centre = count > 0 ? new Vector2((float)(sx / count), (float)(sy / count)) : Vector2.zero;
            return count > 0;
        }

        // JsonUtility DTOs: field names match the exporter's JSON exactly.
#pragma warning disable 0649
        [Serializable] private sealed class SheetJson { public string name; public float ppu; public AnimJson[] animations; public FrameJson[] frames; }
        [Serializable] private sealed class AnimJson { public string name; public float fps; public bool loop; public string[] frames; }
        [Serializable] private sealed class FrameJson { public string name; public RectJson rect; public PivotJson pivot; }
        [Serializable] private sealed class RectJson { public int x; public int y; public int w; public int h; }
        [Serializable] private sealed class PivotJson { public float x; public float y; }
#pragma warning restore 0649
    }
}
