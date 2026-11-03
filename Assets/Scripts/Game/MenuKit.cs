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
    /// Shared building blocks of the full-screen menus (main menu, pause, settings), in the neon
    /// style of <see cref="UpgradeScreen"/> and <see cref="RedlineHud"/>: palette, fonts, a UIDocument
    /// per screen, and element helpers.
    /// </summary>
    /// <remarks>Main-thread only.</remarks>
    internal static class MenuKit
    {
        public static readonly Color Overlay = new Color(0f, 0f, 0.02f, 0.78f);
        public static readonly Color OpaqueOverlay = new Color(0.005f, 0.005f, 0.03f, 0.93f);
        public static readonly Color PanelColor = new Color(0.03f, 0.03f, 0.09f, 0.94f);
        public static readonly Color RowColor = new Color(0.06f, 0.07f, 0.14f, 1f);
        public static readonly Color Dim = new Color(0.62f, 0.68f, 0.9f);
        public static readonly Color Cyan = new Color(0.3f, 0.85f, 1f);
        public static readonly Color Magenta = new Color(1f, 0.3f, 0.9f);
        public static readonly Color Gold = new Color(1f, 0.92f, 0.45f);
        public static readonly Color Danger = new Color(1f, 0.25f, 0.4f);
        public static readonly Color Locked = new Color(0.32f, 0.35f, 0.5f);
        public static readonly Color PipOff = new Color(0.16f, 0.18f, 0.3f);

        private static Font _black;
        private static Font _bold;

        /// <summary>The heavy display font.</summary>
        public static Font Black { get { LoadFonts(); return _black; } }

        /// <summary>The body font.</summary>
        public static Font Bold { get { LoadFonts(); return _bold; } }

        private static void LoadFonts()
        {
            if (_black != null && _bold != null) return;
            var fallback = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _black = Resources.Load<Font>("Fonts/SourceCodePro-Black");
            _bold = Resources.Load<Font>("Fonts/SourceCodePro-Bold");
            if (_black == null || _bold == null)
            {
                Debug.LogWarning("Redline: menu fonts missing from Resources/Fonts; using the built-in font.");
                if (_black == null) _black = fallback;
                if (_bold == null) _bold = fallback;
            }
        }

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

        /// <summary>A full-screen, centred root on its own UIDocument under <paramref name="parent"/>.</summary>
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
            root.style.left = 0;
            root.style.right = 0;
            root.style.top = 0;
            root.style.bottom = 0;
            root.style.backgroundColor = overlay;
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            root.style.color = Color.white;
            return root;
        }

        /// <summary>A bordered dark panel.</summary>
        public static VisualElement Panel(float width, Color border)
        {
            var panel = new VisualElement();
            panel.style.width = width;
            panel.style.backgroundColor = PanelColor;
            panel.style.paddingLeft = 36;
            panel.style.paddingRight = 36;
            panel.style.paddingTop = 28;
            panel.style.paddingBottom = 28;
            Round(panel, 12);
            Border(panel, 2f, border);
            return panel;
        }

        /// <summary>
        /// A click-only button (not focusable, so UI Toolkit navigation cannot also submit it;
        /// keyboard and gamepad are read by each screen). <paramref name="allowClick"/> can veto clicks,
        /// e.g. on the frame the screen opened.
        /// </summary>
        public static Button MakeButton(Action onClick, Func<bool> allowClick, float width, float height = 52f, int fontSize = 24)
        {
            var b = new Button(() =>
            {
                if (allowClick == null || allowClick()) onClick();
            });
            b.focusable = false;
            b.style.width = width;
            b.style.height = height;
            b.style.fontSize = fontSize;
            b.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(Black));
            b.style.marginLeft = 0;
            b.style.marginRight = 0;
            Round(b, 8);
            return b;
        }

        /// <summary>Filled buttons are the primary action; outlined ones are secondary. Selected ones get a gold border.</summary>
        public static void StyleButton(Button b, string text, Color color, bool filled, bool selected)
        {
            if (b.text != text) b.text = text;
            b.style.backgroundColor = filled ? color : new Color(color.r, color.g, color.b, 0.08f);
            b.style.color = filled ? Color.black : color;
            Border(b, selected ? 4f : 2f, selected ? Gold : color);
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

        public static Label Caption(string text)
        {
            var label = Text(text, Bold, 20, Dim);
            label.style.letterSpacing = 5;
            return label;
        }

        public static Label Text(string text, Font font, int size, Color color)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size;
            label.style.color = color;
            label.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(font));
            label.style.paddingLeft = 0;
            label.style.paddingRight = 0;
            label.style.marginLeft = 0;
            label.style.marginRight = 0;
            return label;
        }

        public static void Round(VisualElement e, float radius)
        {
            e.style.borderTopLeftRadius = radius;
            e.style.borderTopRightRadius = radius;
            e.style.borderBottomLeftRadius = radius;
            e.style.borderBottomRightRadius = radius;
        }

        public static void Border(VisualElement e, float width, Color color)
        {
            e.style.borderTopWidth = width;
            e.style.borderBottomWidth = width;
            e.style.borderLeftWidth = width;
            e.style.borderRightWidth = width;
            e.style.borderTopColor = color;
            e.style.borderBottomColor = color;
            e.style.borderLeftColor = color;
            e.style.borderRightColor = color;
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
