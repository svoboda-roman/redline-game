using System;

namespace Redline.Core.Presentation
{
    /// <summary>
    /// Texel-grid snapping shared by the pixel-art sprite views and the camera rig (Pixel3DCamera).
    /// <para>
    /// A sprite snapped to the world grid is drawn up to half a texel away from where it really is (its residual). The
    /// rig shows the camera's own sub-texel remainder by sliding the whole picture, so on screen a snapped sprite lands at
    /// <c>snapped(p) − c</c>: when the player and the camera move together at a speed that is not a whole number of
    /// texels per frame, that residual changes every frame and the sprite steps back and forth by one texel against
    /// the camera. <see cref="LockCamera"/> moves the camera by the subject's residual too, so the snapped subject
    /// keeps exactly the offset its exact position has (<c>p − c</c>) and holds still on screen while it is followed.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// float spriteX = PixelSnap.Snap(feet.x, 48f);
    /// float camX = PixelSnap.LockCamera(cameraX, feet.x, 48f, 1f / 48f, out float remX);   // remX slides the picture
    /// </code>
    /// </example>
    /// <remarks>Pure, allocation-free, engine-free; unit tested in PixelSnapTests.</remarks>
    public static class PixelSnap
    {
        /// <summary>
        /// <paramref name="value"/> rounded to the nearest multiple of 1/<paramref name="texelsPerUnit"/> (ties to even,
        /// identical to <c>Mathf.Round(v * g) / g</c>). Every sprite quad uses this so the camera can reproduce it.
        /// </summary>
        public static float Snap(float value, float texelsPerUnit) =>
            (float)Math.Round(value * texelsPerUnit) / texelsPerUnit;

        /// <summary>
        /// Plain camera snap: <paramref name="camera"/> floored to a multiple of <paramref name="step"/> world units;
        /// <paramref name="remainder"/> is the cut-off part in steps (0..1), shown by sliding the picture.
        /// </summary>
        public static float SnapCamera(float camera, float step, out float remainder)
        {
            float t = camera / step;
            float whole = (float)Math.Floor(t);
            remainder = t - whole;
            return whole * step;
        }

        /// <summary>
        /// Camera snap locked to a followed subject drawn at <see cref="Snap"/>(<paramref name="subject"/>,
        /// <paramref name="texelsPerUnit"/>): the camera is moved by the subject's snap residual before it is floored to
        /// <paramref name="step"/>, so the snapped subject stays <c>subject − camera</c> from the exact (slid) camera and
        /// its texel offset from the snapped camera depends only on <c>camera − subject</c>. While the camera follows the
        /// subject at a steady offset, both are constant: no back-and-forth step. The result stays on the
        /// <paramref name="step"/> grid, so static scenery does not shimmer; when step is the subject's grid it is measured
        /// from the snapped subject, so float error in large absolute positions cannot flip the floor.
        /// </summary>
        /// <param name="camera">Exact camera position (world units, shake included).</param>
        /// <param name="subject">Exact drawing position of the followed sprite (world units, before snapping).</param>
        /// <param name="texelsPerUnit">The sprite's snap grid (texels per world unit).</param>
        /// <param name="step">The camera's snap step (world units per picture texel on this axis).</param>
        /// <param name="remainder">The sub-step part, 0..1 steps, for the picture slide.</param>
        /// <returns>The snapped camera position.</returns>
        public static float LockCamera(float camera, float subject, float texelsPerUnit, float step, out float remainder)
        {
            float snapped = Snap(subject, texelsPerUnit);
            bool sameGrid = Math.Abs(step * texelsPerUnit - 1f) < 1e-5f;
            float anchor = sameGrid ? snapped : 0f;
            float t = sameGrid ? (camera - subject) / step : (camera - (subject - snapped)) / step;
            float whole = (float)Math.Floor(t);
            remainder = t - whole;
            return anchor + whole * step;
        }
    }
}
