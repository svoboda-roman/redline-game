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
    /// Between-runs upgrade shop, UI Toolkit built from code in the terminal style (see
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
        private static readonly Color Dim = MenuKit.Dim;
        private static readonly Color Bone = UiTheme.Bone;
        private static readonly Color Danger = MenuKit.Danger;

        private const int ResetIndex = 4;
        private const int ContinueIndex = 5;
        private const int MenuIndex = 6;
        private const int ItemCount = 7;

        private UpgradeStore _store;
        private PanelSettings _settings;

        private Label _points;
        private Label _earned;
        private Label _detail;
        private Label _status;
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
                ? "+" + pointsEarned + " THIS RUN - " + run.Kills + " KILLS - " + run.WavesCleared + " WAVES - "
                  + run.LevelsCleared + " LEVELS - TIER " + Mathf.Max(1, run.HighestTier)
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
            SetStatus("");
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
            bool saved = _store.Save();
            Refresh();
            if (!saved) SetStatus(SaveFailed);
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
                bool saved = _store.ResetProgress();
                Changed?.Invoke();
                Refresh();
                if (!saved) SetStatus(SaveFailed);
                return;
            }
            Refresh();
        }

        /// <summary>Status line shown when the progress could not be written.</summary>
        public const string SaveFailed = "SAVE FAILED - SETTINGS NOT KEPT";

        private void SetStatus(string text)
        {
            _status.text = text;
            _status.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
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
                var result = state.CanBuy(kind, cfg);
                bool selected = !_confirming && _selected == i;
                string tail;
                if (result == BuyResult.MaxLevel) tail = "MAX";
                else if (result == BuyResult.Ok) tail = "COST " + cfg.CostOf(kind, level);
                else tail = "NEED " + Mathf.Max(1, cfg.CostOf(kind, level) - state.Points);
                // Monospace columns: name, LV n/max, level squares (# / .), cost or NEED or MAX.
                string text = Title(kind).PadRight(11) + ("LV " + level + "/" + max).PadRight(7) + Squares(level, max) + "  " + tail;
                bool dim = result != BuyResult.Ok && result != BuyResult.MaxLevel;
                StyleButton(_buy[i], text, dim ? Locked : MenuKit.Steel, false, selected);
                if (selected) _detail.text = EffectText(kind);
            }
            if (_selected >= UpgradeState.Kinds.Length || _confirming) _detail.text = "";
            StyleButton(_reset, "RESET PROGRESS", MenuKit.Steel, false, !_confirming && _selected == ResetIndex);
            StyleButton(_continue, "CONTINUE", MenuKit.Steel, false, !_confirming && _selected == ContinueIndex);
            StyleButton(_menu, "MENU", MenuKit.Steel, false, !_confirming && _selected == MenuIndex);
            StyleButton(_confirmNo, "NO", MenuKit.Steel, false, _confirming && !_confirmOnYes);
            StyleButton(_confirmYes, "YES", MenuKit.Steel, false, _confirming && _confirmOnYes);
        }

        private static readonly Color Locked = MenuKit.Locked;

        private static string Squares(int level, int max)
        {
            var c = new char[max];
            for (int i = 0; i < max; i++) c[i] = i < level ? '#' : '.';
            return new string(c);
        }

        private static void StyleButton(Button b, string text, Color color, bool filled, bool selected) =>
            MenuKit.StyleButton(b, text, color, filled, selected);

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
                    return "+" + Num(c.hookRangePerLevel) + " HOOK RANGE - +" + Num(c.hookReleaseBoostPerLevel) + " FLING PER LEVEL";
                case UpgradeKind.SwordAndOrbs:
                    return "+" + Num(c.attackRangePerLevel) + " REACH - +" + Pct(c.orbChancePerLevel) + " ORB DROP  ·  +"
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

        private void BuildDocument()
        {
            _settings = MenuKit.CreatePanel("RedlineUpgradePanelSettings", 100f); // above the HUD
            Root = MenuKit.CreateRoot(transform, _settings, "UpgradeScreen", MenuKit.OpaqueOverlay);
        }

        private void BuildContent()
        {
            var column = new VisualElement { pickingMode = PickingMode.Ignore };
            column.style.alignItems = Align.Stretch;
            column.style.width = 150f * MenuKit.T;
            Root.Add(column);

            var header = MenuKit.Row(Justify.SpaceBetween);
            header.Add(MenuKit.Text("UPGRADES", null, MenuKit.Heading, UiTheme.Orange));
            var pointsBox = MenuKit.Row(Justify.FlexEnd);
            pointsBox.Add(MenuKit.Text("POINTS ", null, MenuKit.Label, UiTheme.Orange));
            _points = MenuKit.Text("0", null, MenuKit.Entry, Bone);
            pointsBox.Add(_points);
            header.Add(pointsBox);
            column.Add(header);

            _earned = MenuKit.Text("", null, MenuKit.Label, Bone);
            _earned.style.marginTop = 2f * MenuKit.T;
            _earned.style.marginBottom = 4f * MenuKit.T;
            column.Add(_earned);

            for (int i = 0; i < UpgradeState.Kinds.Length; i++)
            {
                int idx = i;
                _buy[i] = MakeButton(() => { Select(idx); Activate(idx); }, idx, 150f * MenuKit.T);
                column.Add(_buy[i]);
            }
            _detail = MenuKit.Text("", null, MenuKit.Label, Dim);
            _detail.style.height = 8f * MenuKit.T;
            _detail.style.marginTop = 2f * MenuKit.T;
            _detail.style.marginLeft = 4f * MenuKit.T;
            column.Add(_detail);

            _reset = MakeButton(() => { Select(ResetIndex); Activate(ResetIndex); }, ResetIndex, 150f * MenuKit.T);
            _continue = MakeButton(() => { Select(ContinueIndex); Activate(ContinueIndex); }, ContinueIndex, 150f * MenuKit.T);
            _menu = MakeButton(() => { Select(MenuIndex); Activate(MenuIndex); }, MenuIndex, 150f * MenuKit.T);
            column.Add(_reset);
            column.Add(_continue);
            column.Add(_menu);

            _status = MenuKit.Text("", null, MenuKit.Label, Danger);
            _status.style.display = DisplayStyle.None;
            _status.style.marginTop = 2f * MenuKit.T;
            column.Add(_status);
            var hint = MenuKit.Text("W/S - D-PAD  SELECT      ENTER - A  BUY / CONFIRM", null, MenuKit.Label, Dim);
            hint.style.marginTop = 4f * MenuKit.T;
            column.Add(hint);

            BuildConfirm();
        }

        private void BuildConfirm()
        {
            _confirm = new VisualElement();
            _confirm.style.position = Position.Absolute;
            _confirm.style.left = 0; _confirm.style.right = 0; _confirm.style.top = 0; _confirm.style.bottom = 0;
            _confirm.style.backgroundColor = UiTheme.WithAlpha(UiTheme.Ground, 0.92f);
            _confirm.style.alignItems = Align.FlexStart;
            _confirm.style.justifyContent = Justify.Center;
            _confirm.style.paddingLeft = MenuKit.LeftMargin;
            _confirm.pickingMode = PickingMode.Position; // blocks clicks to the shop behind
            Root.Add(_confirm);

            _confirm.Add(MenuKit.Text("RESET ALL PROGRESS?", null, MenuKit.Heading, Danger));
            var sub = MenuKit.Text("ALL POINTS AND UPGRADES WILL BE LOST", null, MenuKit.Label, UiTheme.Orange);
            sub.style.marginTop = 2f * MenuKit.T;
            sub.style.marginBottom = 4f * MenuKit.T;
            _confirm.Add(sub);

            _confirmNo = MakeButton(() => AnswerReset(false), -1, 60f * MenuKit.T);
            _confirmYes = MakeButton(() => AnswerReset(true), -1, 60f * MenuKit.T);
            _confirmNo.RegisterCallback<PointerEnterEvent>(_ => { _confirmOnYes = false; Refresh(); });
            _confirmYes.RegisterCallback<PointerEnterEvent>(_ => { _confirmOnYes = true; Refresh(); });
            _confirm.Add(_confirmNo);
            _confirm.Add(_confirmYes);
        }

        /// <summary>A click-only row (not focusable; keyboard and gamepad go through <see cref="Update"/>).</summary>
        private Button MakeButton(Action onClick, int selectIndex, float width)
        {
            var b = MenuKit.MakeButton(onClick, () => Time.frameCount != _openedFrame, width);
            if (selectIndex >= 0) b.RegisterCallback<PointerEnterEvent>(_ => Select(selectIndex));
            return b;
        }

        private void OnDestroy()
        {
            if (_settings != null) Destroy(_settings);
        }
    }
}
