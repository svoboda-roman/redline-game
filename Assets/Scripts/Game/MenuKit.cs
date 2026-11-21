using System;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace Redline.Game
{
    /// <summary>
    /// Shared building blocks of the full-screen menus in the Replaced terminal style (design/ux/menus.md,
    /// art bible sections 4 and 7): near-black ground, orange pixel-font text, bone for the active
    /// value, no panels, brackets, glows or shadows. The single permitted fill is the inverse selection
    /// bar (orange bar, near-black text). Text units: T = 4 px in the 1920x1080 panel space (headings
    /// 16 T = 64 px, list entries 12 T = 48 px, labels and hints 8 T = 32 px).
    /// </summary>
    /// <example>
    /// <code>
    /// var settings = MenuKit.CreatePanel("Demo", 90f);
    /// var root = MenuKit.CreateRoot(transform, settings, "Demo", MenuKit.OpaqueOverlay);
    /// root.Add(MenuKit.Text("TITLE", MenuKit.Black, MenuKit.Heading, MenuKit.Steel));
    /// </code>
    /// </example>
    /// <remarks>Main-thread only. The colour names of the old neon kit are kept as aliases of the new palette.</remarks>
    internal static class MenuKit
    {
        /// <summary>One texel in panel pixels (1080p reference).</summary>
        public const float T = 4f;
        /// <summary>Heading size, 16 T.</summary>
        public const int Heading = 64;
        /// <summary>List entry / value size, 12 T.</summary>
        public const int Entry = 48;
        /// <summary>Label / hint size, 8 T.</summary>
        public const int Label = 32;
        /// <summary>Row pitch, 16 T.</summary>
        public const float RowHeight = 64f;
        /// <summary>Left margin of the menu column, 24 T.</summary>
        public const float LeftMargin = 96f;

        /// <summary>Pause overlay: the scene dimmed to 25 %.</summary>
        public static readonly Color Overlay = UiTheme.WithAlpha(UiTheme.Ground, 0.75f);
        /// <summary>Full-screen screens: ground black; CreateRoot turns it into the backdrop scrim (<see cref="ApplyScrim"/>).</summary>
        public static readonly Color OpaqueOverlay = UiTheme.Ground;
        public static readonly Color PanelColor = Color.clear;
        public static readonly Color RowColor = Color.clear;
        public static readonly Color Dim = UiTheme.Dim;
        /// <summary>Default menu text (orange).</summary>
        public static readonly Color Steel = UiTheme.Orange;
        public static readonly Color Cyan = UiTheme.Orange;
        public static readonly Color Crimson = UiTheme.Orange;
        public static readonly Color Amber = UiTheme.Orange;
        /// <summary>Active value (bone).</summary>
        public static readonly Color Gold = UiTheme.Bone;
        /// <summary>Errors and refusals (the contrast-safe crimson).</summary>
        public static readonly Color Danger = UiTheme.Crimson;
        public static readonly Color Locked = UiTheme.Faint;
        public static readonly Color PipOff = UiTheme.Faint;
        public const float SelectBar = 0f;

        /// <summary>The terminal font.</summary>
        public static Font Black => UiTheme.Font;

        /// <summary>The terminal font (single weight).</summary>
        public static Font Bold => UiTheme.Font;

        /// <summary>Panel settings matching the other screens (1920x1080 reference, theme from Resources).</summary>
        public static PanelSettings CreatePanel(string name, float sortingOrder)
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = name;
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("RedlineTheme");
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            settings.sortingOrder = sortingOrder;
            return settings;
        }

        /// <summary>
        /// A full-screen root on its own UIDocument under <paramref name="parent"/>. Content is
        /// left-aligned at 24 T and kept inside the 2.4:1 picture (about 140 px bars at 1080p).
        /// </summary>
        public static VisualElement CreateRoot(Transform parent, PanelSettings settings, string name, Color overlay)
        {
            var host = new GameObject(name);
            host.transform.SetParent(parent, false);
            host.SetActive(false);
            var doc = host.AddComponent<UIDocument>();
            doc.panelSettings = settings;
            host.SetActive(true);

            var root = doc.rootVisualElement;
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.right = 0; root.style.top = 0; root.style.bottom = 0;
            root.style.backgroundColor = overlay;
            // Full-screen screens keep the live level behind them (Replaced): a strong left-to-right scrim
            // instead of an opaque ground. The main menu lightens it further on the right (MenuScreen).
            if (overlay == OpaqueOverlay) ApplyScrim(root, 0.96f, 0.84f, 0.45f, 1f);
            root.style.alignItems = Align.FlexStart;
            root.style.justifyContent = Justify.Center;
            root.style.paddingLeft = LeftMargin;
            root.style.paddingRight = LeftMargin;
            root.style.paddingTop = 140;
            root.style.paddingBottom = 140;
            root.style.color = UiTheme.Orange;
            root.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(UiTheme.Font));
            return root;
        }

        /// <summary>
        /// Replaces the root's flat ground with a horizontal ground-black gradient: <paramref name="leftAlpha"/>
        /// up to <paramref name="fadeStart"/> of the width, easing to <paramref name="rightAlpha"/> at
        /// <paramref name="fadeEnd"/>. The text column sits in the dense part (WCAG 4.5:1 for orange and dim
        /// text needs at least ~0.95 over the brightest backdrop).
        /// </summary>
        public static void ApplyScrim(VisualElement root, float leftAlpha, float rightAlpha, float fadeStart, float fadeEnd)
        {
            const int W = 256;
            var tex = new Texture2D(W, 1, TextureFormat.RGBA32, false)
            {
                name = "MenuScrim", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave,
            };
            var g = UiTheme.Ground;
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fadeStart, Mathf.Max(fadeStart + 0.001f, fadeEnd), u));
                tex.SetPixel(x, 0, new Color(g.r, g.g, g.b, Mathf.Lerp(leftAlpha, rightAlpha, k)));
            }
            tex.Apply(false, true);
            root.style.backgroundColor = Color.clear;
            root.style.backgroundImage = new StyleBackground(tex);
            root.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
        }

        /// <summary>A plain container of the given width: the terminal style has no panels.</summary>
        public static VisualElement Panel(float width, Color border)
        {
            var panel = new VisualElement { pickingMode = PickingMode.Ignore };
            panel.style.width = width;
            return panel;
        }

        /// <summary>No-op kept for compatibility: the terminal style has no brackets. Returns an empty array.</summary>
        public static VisualElement[] Brackets(VisualElement host, Color color, float size = 12f, float width = 2f, float inset = -4f) =>
            Array.Empty<VisualElement>();

        /// <summary>No-op (no brackets).</summary>
        public static void TintBrackets(VisualElement[] corners, Color color) { }

        /// <summary>No-op (no brackets).</summary>
        public static void ShowBrackets(VisualElement[] corners, bool on) { }

        /// <summary>Marks a row element selected: the inverse orange selection bar (caller recolours its text).</summary>
        public static void SelectRow(VisualElement row, bool selected, Color idle)
        {
            row.style.backgroundColor = selected ? UiTheme.Orange : Color.clear;
        }

        /// <summary><paramref name="c"/> with alpha <paramref name="a"/>.</summary>
        public static Color Fade(Color c, float a) => new Color(c.r, c.g, c.b, a);

        /// <summary>Kept for compatibility; the terminal style has no decorative glyphs.</summary>
        public static VisualElement Glyph(Color color) => new VisualElement { pickingMode = PickingMode.Ignore };

        /// <summary>
        /// A click-only list row button (not focusable; keyboard and gamepad are read by each screen).
        /// <paramref name="allowClick"/> can veto clicks, e.g. on the frame the screen opened.
        /// </summary>
        public static Button MakeButton(Action onClick, Func<bool> allowClick, float width, float height = RowHeight, int fontSize = Entry)
        {
            var b = new Button(() =>
            {
                if (allowClick == null || allowClick()) onClick();
            });
            b.focusable = false;
            b.style.width = width;
            b.style.height = RowHeight;
            b.style.fontSize = Entry;
            b.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(UiTheme.Font));
            b.style.unityTextAlign = TextAnchor.MiddleLeft;
            b.style.paddingLeft = 4f * T;
            b.style.paddingRight = 4f * T;
            b.style.marginLeft = 0; b.style.marginRight = 0; b.style.marginTop = 0; b.style.marginBottom = 0;
            b.style.letterSpacing = 0;
            b.style.overflow = Overflow.Hidden;
            b.style.backgroundImage = StyleKeyword.None;
            Round(b, 0);
            Border(b, 0f, Color.clear);
            return b;
        }

        /// <summary>
        /// Styles a row button: selected = orange bar with near-black text (the inverse selection bar),
        /// else orange text on nothing; <see cref="Locked"/> is the 40 % disabled look.
        /// </summary>
        public static void StyleButton(Button b, string text, Color color, bool filled, bool selected)
        {
            if (b.text != text) b.text = text;
            b.style.backgroundColor = selected ? UiTheme.Orange : Color.clear;
            b.style.color = selected ? UiTheme.Ground : color;
        }

        public static VisualElement Row(Justify justify)
        {
            var e = new VisualElement();
            e.style.flexDirection = FlexDirection.Row;
            e.style.justifyContent = justify;
            e.style.alignItems = Align.Center;
            e.pickingMode = PickingMode.Ignore;
            return e;
        }

        /// <summary>A section header: 8 T dim text.</summary>
        public static Label Caption(string text) => Text(text, UiTheme.Font, Label, Dim);

        /// <summary>A label in the terminal font (the font argument is ignored; one face).</summary>
        public static Label Text(string text, Font font, int size, Color color)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size;
            label.style.color = color;
            label.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(UiTheme.Font));
            label.style.paddingLeft = 0; label.style.paddingRight = 0;
            label.style.paddingTop = 0; label.style.paddingBottom = 0;
            label.style.marginLeft = 0; label.style.marginRight = 0;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        /// <summary>Square corners only (the radius is ignored).</summary>
        public static void Round(VisualElement e, float radius)
        {
            e.style.borderTopLeftRadius = 0; e.style.borderTopRightRadius = 0;
            e.style.borderBottomLeftRadius = 0; e.style.borderBottomRightRadius = 0;
        }

        public static void Border(VisualElement e, float width, Color color)
        {
            e.style.borderTopWidth = width; e.style.borderBottomWidth = width;
            e.style.borderLeftWidth = width; e.style.borderRightWidth = width;
            e.style.borderTopColor = color; e.style.borderBottomColor = color;
            e.style.borderLeftColor = color; e.style.borderRightColor = color;
        }
    }

    /// <summary>
    /// Fixed menu navigation read once per frame: W/S/A/D or arrows, Enter/Space confirm,
    /// Esc back, Backspace/Delete clear; gamepad d-pad or stick (edge-triggered), South confirm, East back.
    /// </summary>
    internal struct MenuNav
    {
        private int _stickDir;

        public bool Up, Down, Left, Right, Submit, Cancel, Clear;

        public void Read()
        {
            Up = Down = Left = Right = Submit = Cancel = Clear = false;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            int stick = 0;
            if (pad != null)
            {
                var v = pad.leftStick.ReadValue();
                stick = v.y > 0.5f ? 1 : v.y < -0.5f ? 2 : v.x < -0.5f ? 3 : v.x > 0.5f ? 4 : 0;
            }
            bool edge = stick != 0 && stick != _stickDir;
            _stickDir = stick;

            Up = P(kb?.wKey) || P(kb?.upArrowKey) || P(pad?.dpad.up) || (edge && stick == 1);
            Down = P(kb?.sKey) || P(kb?.downArrowKey) || P(pad?.dpad.down) || (edge && stick == 2);
            Left = P(kb?.aKey) || P(kb?.leftArrowKey) || P(pad?.dpad.left) || (edge && stick == 3);
            Right = P(kb?.dKey) || P(kb?.rightArrowKey) || P(pad?.dpad.right) || (edge && stick == 4);
            Submit = P(kb?.enterKey) || P(kb?.numpadEnterKey) || P(kb?.spaceKey) || P(pad?.buttonSouth);
            Cancel = P(kb?.escapeKey) || P(pad?.buttonEast);
            Clear = P(kb?.backspaceKey) || P(kb?.deleteKey) || P(pad?.buttonNorth);
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private static bool P(ButtonControl b) => b != null && b.wasPressedThisFrame;
#endif
    }
}
