using Redline.Core;
using UnityEngine;

namespace Redline.Game
{
    /// <summary>
    /// The Replaced-style terminal palette and font of the HUD and menus (art bible sections 4 and 7):
    /// monochrome orange, bone for the active value, crimson only for low HP, tier colours on the
    /// speed meter. Departure Mono (SIL OFL 1.1, Resources/Fonts) with Source Code Pro as fallback.
    /// </summary>
    /// <remarks>Main-thread only.</remarks>
    public static class UiTheme
    {
        /// <summary>Default UI colour #F08A3C (7.9:1 on the ground black).</summary>
        public static readonly Color Orange = new Color32(0xF0, 0x8A, 0x3C, 255);
        /// <summary>Dim orange for secondary text, 5:1 or better on the ground black.</summary>
        public static readonly Color Dim = new Color32(0xC3, 0x72, 0x30, 255);
        /// <summary>Disabled / lost: 40 % orange (exempt from contrast, always paired with text or shape).</summary>
        public static readonly Color Faint = new Color(0.941f, 0.541f, 0.235f, 0.4f);
        /// <summary>Active value #E8E2D4.</summary>
        public static readonly Color Bone = new Color32(0xE8, 0xE2, 0xD4, 255);
        /// <summary>Ground black #0B0A0C.</summary>
        public static readonly Color Ground = new Color32(0x0B, 0x0A, 0x0C, 255);
        /// <summary>
        /// Low-HP crimson. The art bible value #E5232E measures 4.34:1 on the ground black (below 4.5:1),
        /// so the HUD uses the lighter #F0353F (4.97:1).
        /// </summary>
        public static readonly Color Crimson = new Color32(0xF0, 0x35, 0x3F, 255);
        /// <summary>Tier colours: cold blue, amber, white-hot (luminance rises with tier).</summary>
        public static readonly Color[] Tier =
        {
            new Color32(0x9C, 0xC8, 0xFF, 255), new Color32(0xFF, 0xB3, 0x47, 255), new Color32(0xFF, 0xF4, 0xE8, 255),
        };

        private static Font _font;

        /// <summary>Reduced Motion: transitions, blinks and flashes become steady or instant.</summary>
        public static bool ReducedMotion => GameSettings.Current.ReducedMotion;

        /// <summary>The HUD text scale in percent (100, 125 or 150).</summary>
        public static int HudTextScale => GameSettings.Current.HudTextScale;

        /// <summary>The terminal font: Departure Mono, else Source Code Pro Bold, else the built-in font.</summary>
        public static Font Font
        {
            get
            {
                if (_font != null) return _font;
                _font = Resources.Load<Font>("Fonts/DepartureMono-Regular");
                if (_font == null) _font = Resources.Load<Font>("Fonts/SourceCodePro-Bold");
                if (_font == null)
                {
                    Debug.LogWarning("Redline: UI fonts missing from Resources/Fonts; using the built-in font.");
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
                return _font;
            }
        }

        /// <summary>The colour with another alpha.</summary>
        public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}
