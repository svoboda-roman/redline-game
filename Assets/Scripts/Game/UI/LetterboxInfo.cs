using UnityEngine;

namespace Redline.Game
{
    /// <summary>
    /// Where the picture sits between the letterbox and pillar bars, so the HUD can anchor to the picture
    /// and never draw on a bar. In 3D the game publishes the camera's integer-upscale layout every frame with
    /// <see cref="SetPicture"/> (fractions of the output, so any panel size can use them); otherwise a 60 px bar
    /// at 1080p (scaled with the screen height) and 2:1 pillar bars are assumed.
    /// </summary>
    /// <example>
    /// <code>
    /// LetterboxInfo.SetPicture(rig.PictureFraction, rig.ViewportFraction);   // each frame (3D)
    /// Rect picture = LetterboxInfo.PictureRectIn(panelWidth, panelHeight);   // panel units, y down from the top
    /// </code>
    /// </example>
    /// <remarks>Main-thread only. The pure maths (<see cref="Compute"/>, <see cref="Texel"/>, <see cref="FontPx"/>) has no engine state and is unit tested.</remarks>
    public static class LetterboxInfo
    {
        /// <summary>Bar height at the 1080p reference, in pixels.</summary>
        public const float DefaultBar1080 = 60f;

        private static float _bar = -1f;
        private static bool _hasPicture;
        private static Rect _picture, _viewport;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _bar = -1f;
            _hasPicture = false;
            _picture = default;
            _viewport = default;
        }

        /// <summary>
        /// Height of each black bar in screen pixels (fallback model, used until <see cref="SetPicture"/>). Setting it
        /// clears a published picture; a negative value restores the default (60 px at 1080p, scaled with
        /// <see cref="Screen.height"/>).
        /// </summary>
        public static float BarPixels
        {
            get => _bar >= 0f ? _bar : DefaultBarFor(Screen.height);
            set { _bar = value; _hasPicture = false; }
        }

        /// <summary>True while a camera layout is published (<see cref="SetPicture"/>).</summary>
        public static bool HasPicture => _hasPicture;

        /// <summary>
        /// Publishes the actual picture and world viewport as fractions of the output (origin top-left, y down),
        /// e.g. <c>Pixel3DCamera.PictureFraction</c> / <c>ViewportFraction</c>. Overrides <see cref="BarPixels"/>.
        /// </summary>
        public static void SetPicture(Rect pictureFraction, Rect viewportFraction)
        {
            _picture = pictureFraction;
            _viewport = viewportFraction;
            _hasPicture = true;
        }

        /// <summary>The picture between the bars in screen pixels (origin top-left, y down).</summary>
        public static Rect PictureRect => PictureRectIn(Screen.width, Screen.height);

        /// <summary>The default bar height for a screen height.</summary>
        public static float DefaultBarFor(float screenHeight) => DefaultBar1080 * screenHeight / 1080f;

        /// <summary>Gameplay picture aspect (2.0:1). Wider windows get pillar bars; the cinematic 2.4:1 state only grows the top/bottom bars.</summary>
        public const float PictureAspect = 2f;

        /// <summary>
        /// The picture rect for a screen size and bar height (bars clamped so the picture keeps at least
        /// 1 px). Screens wider than <see cref="PictureAspect"/> get side bars of (width - 2 x height) / 2.
        /// </summary>
        public static Rect Compute(float screenWidth, float screenHeight, float barPixels)
        {
            float bar = Mathf.Clamp(barPixels, 0f, Mathf.Max(0f, (screenHeight - 1f) * 0.5f));
            float side = Mathf.Max(0f, (screenWidth - PictureAspect * screenHeight) * 0.5f);
            return new Rect(side, bar, screenWidth - 2f * side, screenHeight - 2f * bar);
        }

        /// <summary>
        /// The picture rect mapped into another space of size <paramref name="width"/> x <paramref name="height"/>
        /// (e.g. a UI Toolkit panel), proportionally to the screen. Use this rather than multiplying by a
        /// single scale: the panel and the screen need not share an aspect (render-texture captures).
        /// </summary>
        public static Rect PictureRectIn(float width, float height)
        {
            if (_hasPicture) return Scale(_picture, width, height);
            float sw = Mathf.Max(1f, Screen.width), sh = Mathf.Max(1f, Screen.height);
            var r = Compute(sw, sh, BarPixels);
            return new Rect(r.x / sw * width, r.y / sh * height, r.width / sw * width, r.height / sh * height);
        }

        /// <summary>
        /// The world camera's viewport (0..1 of <c>WorldToViewportPoint</c>) in a space of size
        /// <paramref name="width"/> x <paramref name="height"/>; the whole space when no layout is published.
        /// </summary>
        public static Rect ViewportRectIn(float width, float height) =>
            _hasPicture ? Scale(_viewport, width, height) : new Rect(0f, 0f, width, height);

        private static Rect Scale(Rect f, float width, float height) =>
            new Rect(f.x * width, f.y * height, f.width * width, f.height * height);

        /// <summary>
        /// The HUD unit T in the units of the given picture width (width / 480, at least 2): 4 px at 1080p. It was one game
        /// texel when the 3D view was 480 texels wide; the view is now 640 (3 px at 1080p) and the HUD keeps its size.
        /// </summary>
        public static float Texel(float pictureWidth) => Mathf.Max(2f, Mathf.Round(pictureWidth / 480f));

        /// <summary>An integer font size for a size in T units at a HUD text scale in percent (100/125/150).</summary>
        public static int FontPx(int tUnits, float texel, int scalePercent) =>
            Mathf.Max(1, Mathf.RoundToInt(tUnits * texel * scalePercent / 100f));
    }
}
