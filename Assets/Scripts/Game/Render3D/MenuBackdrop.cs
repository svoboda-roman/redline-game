using Redline.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// The live level behind the title menu (Replaced keeps its world behind every menu): a slow camera drift
    /// over the current level, and a helper to hide the HUD while the menus are up.
    /// </summary>
    /// <example>
    /// <code>
    /// if (inMenu) rig.SetTarget(MenuBackdrop.Drift(Time.time, sim.Map, anchorX, anchorY));
    /// MenuBackdrop.SetHidden(hud.Root, inMenu);
    /// </code>
    /// </example>
    /// <remarks>Pure function plus a style toggle; main thread only for <see cref="SetHidden"/>.</remarks>
    public static class MenuBackdrop
    {
        /// <summary>Pan speed on ring levels (world units per second, about 8 texels per second).</summary>
        public const float PanSpeed = 0.4f;
        /// <summary>Picture centre relative to the anchor. The menu's first anchor is the 2D camera point (feet + 2.5);
        /// -0.3 keeps the framing the rig gave before its own -1.5 offset moved into CameraFollow (+1.2 - 1.5).</summary>
        public const float Lift = -0.3f;

        /// <summary>
        /// Camera target for <paramref name="time"/> seconds: ring levels pan steadily to the right; bounded
        /// levels sway across their width (half-period ~ width / PanSpeed); a slow ±0.25 vertical bob.
        /// </summary>
        public static Vector2 Drift(float time, TileMap map, float anchorX, float anchorY)
        {
            float y = anchorY + Lift + 0.25f * Mathf.Sin(time * 0.13f);
            if (map == null || map.WrapsX) return new Vector2(anchorX + time * PanSpeed, y);
            float half = Mathf.Max(0f, map.Width * 0.5f - 8f);
            float w = half > 0.01f ? PanSpeed / half : 0f;
            return new Vector2(map.Width * 0.5f + half * Mathf.Sin(time * w), y);
        }

        /// <summary>Hides (or shows) a UI root, touching the style only when it changes.</summary>
        public static void SetHidden(VisualElement root, bool hidden)
        {
            if (root == null) return;
            var want = hidden ? DisplayStyle.None : DisplayStyle.Flex;
            if (root.style.display.value != want || root.style.display.keyword != StyleKeyword.Undefined) root.style.display = want;
        }
    }
}
