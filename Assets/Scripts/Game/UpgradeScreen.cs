using System;
using System.Globalization;
using Redline.Core;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace Redline.Game
{
    /// <summary>
    /// Between-runs upgrade shop, UI Toolkit built from code in the HUD's neon style (see
    /// <see cref="RedlineHud"/>): unspent points and what the last run earned, one row per upgrade
    /// (name, per-level effect, level pips, buy button with cost), a reset button behind a
    /// confirmation, and continue. Every purchase and reset is saved through the
    /// <see cref="UpgradeStore"/> at once.
    /// </summary>
    /// <example>
    /// <code>
    /// var screen = gameObject.AddComponent&lt;UpgradeScreen&gt;();
    /// screen.Init(store);
    /// screen.Continued += StartNextRun;
    /// screen.Show(run, earned);   // at run end; the game pauses while screen.Visible
    /// </code>
    /// </example>
    /// <remarks>
    /// Keyboard: W/S or arrows select, Enter/Space activate, Esc cancels the reset prompt.
    /// Gamepad: d-pad/stick select, South activate, East cancel. Mouse: hover and click.
    /// The press that opens the screen is ignored, so the key that ended a run cannot buy.
    /// Main-thread only. In <see cref="RedlineGame"/>: after a run CONTINUE starts a new run and
    /// MENU goes to the title menu; opened from the title menu, CONTINUE returns there.
    /// </remarks>
    public sealed class UpgradeScreen : MonoBehaviour
    {
        private static readonly Color Overlay = new Color(0f, 0f, 0.02f, 0.78f);
        private static readonly Color PanelColor = new Color(0.03f, 0.03f, 0.09f, 0.94f);
        private static readonly Color RowColor = new Color(0.06f, 0.07f, 0.14f, 1f);
        private static readonly Color Dim = new Color(0.62f, 0.68f, 0.9f);
        private static readonly Color Cyan = new Color(0.3f, 0.85f, 1f);
        private static readonly Color Gold = new Color(1f, 0.92f, 0.45f);
        private static readonly Color Danger = new Color(1f, 0.25f, 0.4f);
        private static readonly Color Locked = new Color(0.32f, 0.35f, 0.5f);
        private static readonly Color PipOff = new Color(0.16f, 0.18f, 0.3f);

        private const int ResetIndex = 4;
        private const int ContinueIndex = 5;
        private const int MenuIndex = 6;
        private const int ItemCount = 7;

        private UpgradeStore _store;
        private PanelSettings _settings;
        private Font _black;
        private Font _bold;

        private Label _points;
        private Label _earned;
        private readonly VisualElement[] _rows = new VisualElement[UpgradeState.Kinds.Length];
        private readonly Label[] _levels = new Label[UpgradeState.Kinds.Length];
        private readonly VisualElement[][] _pips = new VisualElement[UpgradeState.Kinds.Length][];
        private readonly Button[] _buy = new Button[UpgradeState.Kinds.Length];
        private Button _reset;
        private Button _continue;
        private Button _menu;
        private bool _menuShown;
        private VisualElement _confirm;
        private Button _confirmYes;
        private Button _confirmNo;

        private int _selected = ContinueIndex;
        private bool _confirming;
        private bool _confirmOnYes;
        private int _openedFrame = -1;
        private int _stickDir;

        /// <summary>Raised when the player picks continue (the screen hides itself first).</summary>
        public event Action Continued;

        /// <summary>Raised when the player picks MENU (shown after a run only; the screen hides itself first).</summary>
        public event Action MenuRequested;

        /// <summary>Raised after a purchase or reset changed the saved state.</summary>
        public event Action Changed;

        /// <summary>The root element, exposed for screenshot tooling.</summary>
        public VisualElement Root { get; private set; }

        /// <summary>Whether the screen is showing (the game should not step while true).</summary>
        public bool Visible { get; private set; }

        /// <summary>Builds the (hidden) screen for a store. Call once.</summary>
        public void Init(UpgradeStore store)
        {
            if (_store != null) return; // already built
            _store = store ?? throw new ArgumentNullException(nameof(store));
            LoadFonts();
            BuildDocument();
            BuildContent();
            Hide();
        }

        /// <summary>Shows the screen with the result of the run just finished.</summary>
        /// <param name="run">The run just finished.</param>
        /// <param name="pointsEarned">Points it earned.</param>
        /// <param name="offerMenu">Also show a MENU button (back to the title menu instead of a new run).</param>
        public void Show(RunResult run, int pointsEarned, bool offerMenu = false)
        {
            _menuShown = offerMenu;
            _earned.text = pointsEarned > 0
                ? "+" + pointsEarned + " THIS RUN  ·  " + run.Kills + " KILLS  ·  " + run.WavesCleared + " WAVES  ·  "
                  + run.LevelsCleared + " LEVELS  ·  TIER " + Mathf.Max(1, run.HighestTier)
                : "NO POINTS THIS RUN";
            _earned.style.display = DisplayStyle.Flex;
            Open();
        }

        /// <summary>Shows the screen without a run summary (e.g. from a title menu).</summary>
        public void Show()
        {
            _menuShown = false;
            _earned.style.display = DisplayStyle.None;
            Open();
        }

        /// <summary>Hides the screen without raising <see cref="Continued"/>.</summary>
        public void Hide()
        {
            Visible = false;
            _confirming = false;
            Root.style.display = DisplayStyle.None;
        }

        private void Open()
        {
            Visible = true;
            _confirming = false;
            _confirm.style.display = DisplayStyle.None;
            _openedFrame = Time.frameCount;
            _selected = FirstAffordable();
            _menu.style.display = _menuShown ? DisplayStyle.Flex : DisplayStyle.None;
            Root.style.display = DisplayStyle.Flex;
            Refresh();
        }

        // ------------------------------------------------------------------ actions

        private void Activate(int index)
        {
            if (!Visible) return;
            if (index < UpgradeState.Kinds.Length) Buy(UpgradeState.Kinds[index]);
            else if (index == ResetIndex) AskReset();
            else if (index == ContinueIndex) Continue();
            else if (index == MenuIndex && _menuShown)
            {
                Hide();
                MenuRequested?.Invoke();
            }
        }

        private void Buy(UpgradeKind kind)
        {
            if (_store.State.TryBuy(kind, _store.Config) != BuyResult.Ok) return;
            _store.Save();
            Refresh();
            Changed?.Invoke();
        }

        private void AskReset()
        {
            _confirming = true;
            _confirmOnYes = false; // default to the safe answer
            _confirm.style.display = DisplayStyle.Flex;
            Refresh();
        }

        private void AnswerReset(bool yes)
        {
            _confirming = false;
            _confirm.style.display = DisplayStyle.None;
            if (yes)
            {
                _store.ResetProgress();
                Changed?.Invoke();
            }
            Refresh();
        }

        private void Continue()
        {
            Hide();
            Continued?.Invoke();
        }

        private int FirstAffordable()
        {
            for (int i = 0; i < UpgradeState.Kinds.Length; i++)
                if (_store.State.CanBuy(UpgradeState.Kinds[i], _store.Config) == BuyResult.Ok) return i;
            return ContinueIndex;
        }

        // ------------------------------------------------------------------ input

        private void Update()
        {
            if (!Visible || Time.frameCount == _openedFrame) return;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            var pad = Gamepad.current;

            int stick = 0;
            if (pad != null)
            {
                float y = pad.leftStick.ReadValue().y;
                float x = pad.leftStick.ReadValue().x;
                stick = y > 0.5f ? -1 : y < -0.5f ? 1 : x < -0.5f ? -2 : x > 0.5f ? 2 : 0;
            }
            bool stickEdge = stick != 0 && stick != _stickDir;
            _stickDir = stick;

            bool up = Pressed(kb?.wKey) || Pressed(kb?.upArrowKey) || Pressed(pad?.dpad.up) || (stickEdge && stick == -1);
            bool down = Pressed(kb?.sKey) || Pressed(kb?.downArrowKey) || Pressed(pad?.dpad.down) || (stickEdge && stick == 1);
            bool side = Pressed(kb?.aKey) || Pressed(kb?.dKey) || Pressed(kb?.leftArrowKey) || Pressed(kb?.rightArrowKey)
                        || Pressed(pad?.dpad.left) || Pressed(pad?.dpad.right) || (stickEdge && (stick == -2 || stick == 2));
            bool submit = Pressed(kb?.enterKey) || Pressed(kb?.numpadEnterKey) || Pressed(kb?.spaceKey) || Pressed(pad?.buttonSouth);
            bool cancel = Pressed(kb?.escapeKey) || Pressed(pad?.buttonEast);

            if (_confirming)
            {
                if (side || up || down) { _confirmOnYes = !_confirmOnYes; Refresh(); }
                if (cancel) AnswerReset(false);
                else if (submit) AnswerReset(_confirmOnYes);
                return;
            }
            if (up) Select(Step(_selected, -1));
            if (down) Select(Step(_selected, 1));
            bool left = Pressed(kb?.aKey) || Pressed(kb?.leftArrowKey) || Pressed(pad?.dpad.left) || (stickEdge && stick == -2);
            if (side && _selected >= ResetIndex) Select(Footer(_selected, left ? -1 : 1));
            if (submit) Activate(_selected);
#endif
        }

        /// <summary>Next item up or down, skipping the hidden MENU button.</summary>
        private int Step(int from, int dir)
        {
            int i = (from + dir + ItemCount) % ItemCount;
            if (i == MenuIndex && !_menuShown) i = (i + dir + ItemCount) % ItemCount;
            return i;
        }

        private static readonly int[] FooterWithMenu = { ResetIndex, MenuIndex, ContinueIndex };
        private static readonly int[] FooterNoMenu = { ResetIndex, ContinueIndex };

        /// <summary>Moves along the footer, laid out RESET, MENU, CONTINUE.</summary>
        private int Footer(int from, int dir)
        {
            int[] order = _menuShown ? FooterWithMenu : FooterNoMenu;
            int pos = Array.IndexOf(order, from);
            if (pos < 0) pos = 0;
            return order[(pos + dir + order.Length) % order.Length];
        }

#if ENABLE_INPUT_SYSTEM
        private static bool Pressed(ButtonControl b) => b != null && b.wasPressedThisFrame;
#endif

        private void Select(int index)
        {
            if (_confirming) return;
            _selected = index;
            Refresh();
        }

        // ------------------------------------------------------------------ view

        private void Refresh()
        {
            var cfg = _store.Config;
            var state = _store.State;
            _points.text = state.Points.ToString(CultureInfo.InvariantCulture);

            for (int i = 0; i < UpgradeState.Kinds.Length; i++)
            {
                var kind = UpgradeState.Kinds[i];
                int level = state.LevelOf(kind);
                int max = cfg.MaxLevel(kind);
                _levels[i].text = "LV " + level + "/" + max;
                for (int p = 0; p < _pips[i].Length; p++)
                    _pips[i][p].style.backgroundColor = p < level ? Cyan : PipOff;

                var result = state.CanBuy(kind, cfg);
                var button = _buy[i];
                bool selected = !_confirming && _selected == i;
                if (result == BuyResult.MaxLevel)
                    StyleButton(button, "MAX", Gold, false, selected);
                else
                    StyleButton(button, "BUY  " + cfg.CostOf(kind, level), result == BuyResult.Ok ? Cyan : Locked,
                        result == BuyResult.Ok, selected);
                Border(_rows[i], 2f, selected ? Gold : new Color(Cyan.r, Cyan.g, Cyan.b, 0.25f));
            }
            StyleButton(_reset, "RESET PROGRESS", Danger, false, !_confirming && _selected == ResetIndex);
            StyleButton(_continue, "CONTINUE", Cyan, true, !_confirming && _selected == ContinueIndex);
            StyleButton(_menu, "MENU", Cyan, false, !_confirming && _selected == MenuIndex);
            StyleButton(_confirmYes, "YES, RESET", Danger, _confirmOnYes, _confirming && _confirmOnYes);
            StyleButton(_confirmNo, "NO", Cyan, !_confirmOnYes, _confirming && !_confirmOnYes);
        }

        /// <summary>Filled buttons are the primary action; outlined ones are secondary or unavailable.</summary>
        private static void StyleButton(Button b, string text, Color color, bool filled, bool selected)
        {
            b.text = text;
            b.style.backgroundColor = filled ? color : new Color(color.r, color.g, color.b, 0.08f);
            b.style.color = filled ? Color.black : color;
            Border(b, selected ? 4f : 2f, selected ? Gold : color);
        }

        private string EffectText(UpgradeKind kind)
        {
            var c = _store.Config;
            switch (kind)
            {
                case UpgradeKind.MaxHp:
                    return "+" + c.maxHpPerLevel + " MAX HP PER LEVEL";
                case UpgradeKind.Speed:
                    return "+" + Pct(c.speedMultiplierPerLevel) + " RUN & TOP SPEED PER LEVEL";
                case UpgradeKind.Hook:
                    return "+" + Num(c.hookRangePerLevel) + " HOOK RANGE  ·  +" + Num(c.hookReleaseBoostPerLevel) + " FLING PER LEVEL";
                case UpgradeKind.SwordAndOrbs:
                    return "+" + Num(c.attackRangePerLevel) + " REACH  ·  +" + Pct(c.orbChancePerLevel) + " ORB DROP  ·  +"
                           + Pct(c.orbDurationPerLevel) + " ORB TIME";
                default:
                    return "";
            }
        }

        private static string Title(UpgradeKind kind)
        {
            switch (kind)
            {
                case UpgradeKind.MaxHp: return "VITALITY";
                case UpgradeKind.Speed: return "VELOCITY";
                case UpgradeKind.Hook: return "GRAPPLE";
                case UpgradeKind.SwordAndOrbs: return "BLADE & ORBS";
                default: return kind.ToString().ToUpperInvariant();
            }
        }

        private static string Pct(float fraction) => Mathf.RoundToInt(fraction * 100f).ToString(CultureInfo.InvariantCulture) + "%";
        private static string Num(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ building

        private void LoadFonts()
        {
            var fallback = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _black = Resources.Load<Font>("Fonts/SourceCodePro-Black");
            _bold = Resources.Load<Font>("Fonts/SourceCodePro-Bold");
            if (_black == null || _bold == null)
            {
                Debug.LogWarning("Redline: upgrade screen fonts missing from Resources/Fonts; using the built-in font.");
                _black = _black != null ? _black : fallback;
                _bold = _bold != null ? _bold : fallback;
            }
        }

        private void BuildDocument()
        {
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.name = "RedlineUpgradePanelSettings";
            _settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("RedlineTheme");
            if (_settings.themeStyleSheet == null)
                Debug.LogWarning("Redline: Resources/RedlineTheme.tss not found; the upgrade screen will use unstyled defaults.");
            _settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            _settings.referenceResolution = new Vector2Int(1920, 1080);
            _settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            _settings.match = 0.5f;
            _settings.sortingOrder = 100f; // above the HUD

            var host = new GameObject("UpgradeScreen");
            host.transform.SetParent(transform, false);
            host.SetActive(false);
            var doc = host.AddComponent<UIDocument>();
            doc.panelSettings = _settings;
            host.SetActive(true);

            Root = doc.rootVisualElement;
            Root.style.position = Position.Absolute;
            Root.style.left = 0;
            Root.style.right = 0;
            Root.style.top = 0;
            Root.style.bottom = 0;
            Root.style.backgroundColor = Overlay;
            Root.style.alignItems = Align.Center;
            Root.style.justifyContent = Justify.Center;
            Root.style.color = Color.white;
        }

        private void BuildContent()
        {
            var panel = new VisualElement();
            panel.style.width = 1080;
            panel.style.backgroundColor = PanelColor;
            panel.style.paddingLeft = 36;
            panel.style.paddingRight = 36;
            panel.style.paddingTop = 28;
            panel.style.paddingBottom = 28;
            Round(panel, 12);
            Border(panel, 2f, Cyan);
            Root.Add(panel);

            var header = Row(Justify.SpaceBetween);
            header.Add(Text("UPGRADES", _black, 54, Cyan));
            var pointsBox = Row(Justify.FlexEnd);
            pointsBox.Add(Caption("POINTS"));
            _points = Text("0", _black, 46, Gold);
            _points.style.marginLeft = 16;
            pointsBox.Add(_points);
            header.Add(pointsBox);
            panel.Add(header);

            var line = new VisualElement();
            line.style.height = 3;
            line.style.marginTop = 6;
            line.style.backgroundColor = Cyan;
            panel.Add(line);

            _earned = Text("", _bold, 20, Gold);
            _earned.style.marginTop = 10;
            panel.Add(_earned);

            for (int i = 0; i < UpgradeState.Kinds.Length; i++) panel.Add(BuildRow(i));

            var footer = Row(Justify.SpaceBetween);
            footer.style.marginTop = 26;
            _reset = MakeButton(() => { Select(ResetIndex); Activate(ResetIndex); }, ResetIndex, 300);
            _continue = MakeButton(() => { Select(ContinueIndex); Activate(ContinueIndex); }, ContinueIndex, 300);
            _menu = MakeButton(() => { Select(MenuIndex); Activate(MenuIndex); }, MenuIndex, 220);
            footer.Add(_reset);
            footer.Add(_menu);
            footer.Add(_continue);
            panel.Add(footer);

            BuildConfirm();
        }

        private VisualElement BuildRow(int i)
        {
            var kind = UpgradeState.Kinds[i];
            var row = Row(Justify.SpaceBetween);
            row.style.marginTop = 14;
            row.style.paddingLeft = 18;
            row.style.paddingRight = 14;
            row.style.paddingTop = 12;
            row.style.paddingBottom = 12;
            row.style.backgroundColor = RowColor;
            Round(row, 8);
            row.pickingMode = PickingMode.Position;
            row.RegisterCallback<PointerEnterEvent>(_ => Select(i));
            _rows[i] = row;

            var info = new VisualElement();
            info.style.flexGrow = 1;
            info.pickingMode = PickingMode.Ignore;
            var titleRow = Row(Justify.FlexStart);
            titleRow.Add(Text(Title(kind), _black, 30, Color.white));
            _levels[i] = Text("LV 0/0", _bold, 20, Dim);
            _levels[i].style.marginLeft = 16;
            titleRow.Add(_levels[i]);
            info.Add(titleRow);
            var effect = Text(EffectText(kind), _bold, 18, Dim);
            effect.style.marginTop = 4;
            info.Add(effect);
            row.Add(info);

            var pips = Row(Justify.FlexEnd);
            pips.style.marginRight = 20;
            int max = _store.Config.MaxLevel(kind);
            _pips[i] = new VisualElement[max];
            for (int p = 0; p < max; p++)
            {
                var pip = new VisualElement();
                pip.style.width = 22;
                pip.style.height = 22;
                pip.style.marginLeft = 6;
                pip.style.rotate = new StyleRotate(new Rotate(45f));
                pip.pickingMode = PickingMode.Ignore;
                Round(pip, 3);
                _pips[i][p] = pip;
                pips.Add(pip);
            }
            row.Add(pips);

            _buy[i] = MakeButton(() => { Select(i); Activate(i); }, i, 200);
            row.Add(_buy[i]);
            return row;
        }

        private void BuildConfirm()
        {
            _confirm = new VisualElement();
            _confirm.style.position = Position.Absolute;
            _confirm.style.left = 0;
            _confirm.style.right = 0;
            _confirm.style.top = 0;
            _confirm.style.bottom = 0;
            _confirm.style.backgroundColor = Overlay;
            _confirm.style.alignItems = Align.Center;
            _confirm.style.justifyContent = Justify.Center;
            _confirm.pickingMode = PickingMode.Position; // blocks clicks to the shop behind
            Root.Add(_confirm);

            var box = new VisualElement();
            box.style.backgroundColor = PanelColor;
            box.style.paddingLeft = 40;
            box.style.paddingRight = 40;
            box.style.paddingTop = 28;
            box.style.paddingBottom = 28;
            box.style.alignItems = Align.Center;
            Round(box, 12);
            Border(box, 2f, Danger);
            _confirm.Add(box);

            box.Add(Text("RESET ALL PROGRESS?", _black, 40, Danger));
            var sub = Text("ALL POINTS AND UPGRADES WILL BE LOST", _bold, 20, Dim);
            sub.style.marginTop = 8;
            box.Add(sub);

            var buttons = Row(Justify.Center);
            buttons.style.marginTop = 24;
            _confirmNo = MakeButton(() => AnswerReset(false), -1, 220);
            _confirmYes = MakeButton(() => AnswerReset(true), -1, 220);
            _confirmYes.style.marginLeft = 24;
            _confirmNo.RegisterCallback<PointerEnterEvent>(_ => { _confirmOnYes = false; Refresh(); });
            _confirmYes.RegisterCallback<PointerEnterEvent>(_ => { _confirmOnYes = true; Refresh(); });
            buttons.Add(_confirmNo);
            buttons.Add(_confirmYes);
            box.Add(buttons);
        }

        /// <summary>
        /// A click-only button: not focusable, so UI Toolkit's own navigation cannot also submit it
        /// (keyboard and gamepad go through <see cref="Update"/>).
        /// </summary>
        private Button MakeButton(Action onClick, int selectIndex, float width)
        {
            var b = new Button(() =>
            {
                if (Time.frameCount != _openedFrame) onClick();
            });
            b.focusable = false;
            b.style.width = width;
            b.style.height = 52;
            b.style.fontSize = 24;
            b.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(_black));
            b.style.marginLeft = 0;
            b.style.marginRight = 0;
            Round(b, 8);
            if (selectIndex >= 0) b.RegisterCallback<PointerEnterEvent>(_ => Select(selectIndex));
            return b;
        }

        private void OnDestroy()
        {
            if (_settings != null) Destroy(_settings);
        }

        // ------------------------------------------------------------------ element helpers (as in RedlineHud)

        private static VisualElement Row(Justify justify)
        {
            var e = new VisualElement();
            e.style.flexDirection = FlexDirection.Row;
            e.style.justifyContent = justify;
            e.style.alignItems = Align.Center;
            e.pickingMode = PickingMode.Ignore;
            return e;
        }

        private Label Caption(string text)
        {
            var label = Text(text, _bold, 20, Dim);
            label.style.letterSpacing = 5;
            return label;
        }

        private static Label Text(string text, Font font, int size, Color color)
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

        private static void Round(VisualElement e, float radius)
        {
            e.style.borderTopLeftRadius = radius;
            e.style.borderTopRightRadius = radius;
            e.style.borderBottomLeftRadius = radius;
            e.style.borderBottomRightRadius = radius;
        }

        private static void Border(VisualElement e, float width, Color color)
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
}
