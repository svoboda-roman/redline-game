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
    /// Between-runs upgrade screen: no shop, one RNG button. The four upgrades stand side by side, each a
    /// vertical stack of level bars over a name box; the RNG button pays a rising price
    /// (<see cref="UpgradeConfig.SpinCost"/>) and spins a vertical reel of upgrade names like a case opening:
    /// it races, slows, stops on the won upgrade, and that column gains a bar. Every spin and reset is saved
    /// through the <see cref="UpgradeStore"/> at once.
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
    /// Keyboard: W/S/A/D or arrows select, Enter/Space activate, Esc cancels the reset prompt. Gamepad: d-pad/stick
    /// select, South activate, East cancel. Mouse: hover and click. The press that opens the screen is ignored, and
    /// nothing reacts while the reel spins. Main-thread only.
    /// </remarks>
    public sealed class UpgradeScreen : MonoBehaviour
    {
        private static readonly Color Dim = MenuKit.Dim;
        private static readonly Color Bone = UiTheme.Bone;
        private static readonly Color Danger = MenuKit.Danger;
        private static readonly Color Neon = new Color(0.36f, 0.8f, 1f);
        private static readonly Color NeonOff = new Color(0.36f, 0.8f, 1f, 0.35f);

        private const int SpinIndex = 0;
        private const int ResetIndex = 1;
        private const int ContinueIndex = 2;
        private const int MenuIndex = 3;
        private const int ItemCount = 4;

        /// <summary>Reel: seconds of one spin, names it scrolls past, line height (px).</summary>
        public static float SpinSeconds = 2.8f;
        private const int ReelLength = 34;
        private const float LineH = 50f;

        private UpgradeStore _store;
        private PanelSettings _settings;

        private Label _points;
        private Label _earned;
        private Label _status;
        private Label _result;
        private readonly VisualElement[][] _bars = new VisualElement[UpgradeState.Kinds.Length][];
        private readonly Label[] _colLevel = new Label[UpgradeState.Kinds.Length];
        private readonly VisualElement[] _colBox = new VisualElement[UpgradeState.Kinds.Length];
        private readonly Label[] _reel = new Label[3];
        private VisualElement _reelStrip;
        private Button _spin;
        private Button _reset;
        private Button _continue;
        private Button _menu;
        private bool _menuShown;
        private VisualElement _confirm;
        private Button _confirmYes;
        private Button _confirmNo;

        private int _selected = SpinIndex;
        private bool _confirming;
        private bool _confirmOnYes;
        private int _openedFrame = -1;
        private int _stickDir;

        private readonly System.Random _rng = new System.Random();
        private readonly UpgradeKind[] _reelKinds = new UpgradeKind[ReelLength];
        private bool _spinning;
        private float _spinTime;
        private UpgradeKind _won;
        private float _flash;

        /// <summary>Raised when the player picks continue (the screen hides itself first).</summary>
        public event Action Continued;

        /// <summary>Raised when the player picks MENU (shown after a run only; the screen hides itself first).</summary>
        public event Action MenuRequested;

        /// <summary>Raised after a spin or reset changed the saved state.</summary>
        public event Action Changed;

        /// <summary>The root element, exposed for screenshot tooling.</summary>
        public VisualElement Root { get; private set; }

        /// <summary>Whether the screen is showing (the game should not step while true).</summary>
        public bool Visible { get; private set; }

        /// <summary>Status line shown when the progress could not be written.</summary>
        public const string SaveFailed = "SAVE FAILED - SETTINGS NOT KEPT";

        /// <summary>Builds the (hidden) screen for a store. Call once.</summary>
        public void Init(UpgradeStore store)
        {
            if (_store != null) return;
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _settings = MenuKit.CreatePanel("RedlineUpgradePanelSettings", 100f); // above the HUD
            Root = MenuKit.CreateRoot(transform, _settings, "UpgradeScreen", MenuKit.OpaqueOverlay);
            Root.style.paddingTop = 56f;
            Root.style.paddingBottom = 40f;
            BuildContent();
            BuildConfirm();
            Hide();
        }

        /// <summary>Shows the screen with the result of the run just finished.</summary>
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
            if (_spinning) FinishSpin();
            Root.style.display = DisplayStyle.None;
        }

        private void Open()
        {
            Visible = true;
            _confirming = false;
            _confirm.style.display = DisplayStyle.None;
            _openedFrame = Time.frameCount;
            SetStatus("");
            _result.text = "";
            _flash = 0f;
            for (int k = 0; k < 3; k++) _reel[k].text = k == 1 ? "?" : "";
            _reelStrip.style.top = 0f;
            _selected = _store.State.CanSpin(_store.Config) == BuyResult.Ok ? SpinIndex : ContinueIndex;
            _menu.style.display = _menuShown ? DisplayStyle.Flex : DisplayStyle.None;
            Root.style.display = DisplayStyle.Flex;
            Refresh();
        }

        // ------------------------------------------------------------------ actions

        private void Activate(int index)
        {
            if (!Visible || _spinning) return;
            if (index == SpinIndex) Spin();
            else if (index == ResetIndex) AskReset();
            else if (index == ContinueIndex) { Hide(); Continued?.Invoke(); }
            else if (index == MenuIndex && _menuShown) { Hide(); MenuRequested?.Invoke(); }
        }

        /// <summary>Pays and rolls now (saved at once), then plays the reel that lands on the result.</summary>
        private void Spin()
        {
            var state = _store.State;
            if (state.TrySpin(_store.Config, _rng.NextDouble(), out _won) != BuyResult.Ok) return;
            bool saved = _store.Save();
            if (!saved) SetStatus(SaveFailed);
            var kinds = UpgradeState.Kinds;
            for (int i = 0; i < ReelLength; i++) _reelKinds[i] = kinds[_rng.Next(kinds.Length)];
            _reelKinds[ReelLength - 2] = _won;                      // the stop line
            _spinning = true;
            _spinTime = 0f;
            _result.text = "";
            Refresh();
            Changed?.Invoke();
        }

        private void FinishSpin()
        {
            _spinning = false;
            ShowReel(ReelLength - 2, 0f);
            _result.text = "UNLOCKED  " + Title(_won) + "  LV " + _store.State.LevelOf(_won);
            _flash = 1.4f;
            Refresh();
        }

        private void ShowReel(int centre, float frac)
        {
            for (int k = 0; k < 3; k++)
            {
                int i = Mathf.Clamp(centre - 1 + k, 0, ReelLength - 1);
                _reel[k].text = Title(_reelKinds[i]);
            }
            _reelStrip.style.top = -frac * LineH;
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
                _result.text = "";
                Refresh();
                if (!saved) SetStatus(SaveFailed);
                return;
            }
            Refresh();
        }

        private void SetStatus(string text)
        {
            _status.text = text;
            _status.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // ------------------------------------------------------------------ frame

        private void Update()
        {
            if (!Visible) return;
            if (_spinning)
            {
                _spinTime += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(_spinTime / SpinSeconds);
                float e = 1f - Mathf.Pow(1f - t, 3.2f);              // fast start, long slow-down
                float pos = e * (ReelLength - 2);
                int centre = Mathf.FloorToInt(pos);
                ShowReel(centre, pos - centre);
                if (t >= 1f) FinishSpin();
                return;
            }
            if (_flash > 0f)
            {
                _flash -= Time.unscaledDeltaTime;
                RefreshBars();
            }
            if (Time.frameCount == _openedFrame) return;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            int stick = 0;
            if (pad != null)
            {
                float y = pad.leftStick.ReadValue().y, x = pad.leftStick.ReadValue().x;
                stick = y > 0.5f ? -1 : y < -0.5f ? 1 : x < -0.5f ? -1 : x > 0.5f ? 1 : 0;
            }
            bool stickEdge = stick != 0 && stick != _stickDir;
            _stickDir = stick;
            bool prev = Pressed(kb?.wKey) || Pressed(kb?.upArrowKey) || Pressed(kb?.aKey) || Pressed(kb?.leftArrowKey)
                        || Pressed(pad?.dpad.up) || Pressed(pad?.dpad.left) || (stickEdge && stick == -1);
            bool next = Pressed(kb?.sKey) || Pressed(kb?.downArrowKey) || Pressed(kb?.dKey) || Pressed(kb?.rightArrowKey)
                        || Pressed(pad?.dpad.down) || Pressed(pad?.dpad.right) || (stickEdge && stick == 1);
            bool submit = Pressed(kb?.enterKey) || Pressed(kb?.numpadEnterKey) || Pressed(kb?.spaceKey) || Pressed(pad?.buttonSouth);
            bool cancel = Pressed(kb?.escapeKey) || Pressed(pad?.buttonEast);
            if (_confirming)
            {
                if (prev || next) { _confirmOnYes = !_confirmOnYes; Refresh(); }
                if (cancel) AnswerReset(false);
                else if (submit) AnswerReset(_confirmOnYes);
                return;
            }
            if (prev) Select(Step(_selected, -1));
            if (next) Select(Step(_selected, 1));
            if (submit) Activate(_selected);
#endif
        }

        private int Step(int from, int dir)
        {
            int i = (from + dir + ItemCount) % ItemCount;
            if (i == MenuIndex && !_menuShown) i = (i + dir + ItemCount) % ItemCount;
            return i;
        }

#if ENABLE_INPUT_SYSTEM
        private static bool Pressed(ButtonControl b) => b != null && b.wasPressedThisFrame;
#endif

        private void Select(int index)
        {
            if (_confirming || _spinning) return;
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
                int level = state.LevelOf(kind), max = cfg.MaxLevel(kind);
                bool shownLevel = !(_spinning && kind == _won);   // the won level appears when the reel stops
                _colLevel[i].text = (level >= max && shownLevel ? "MAX" : "LV " + (shownLevel ? level : level - 1) + "/" + max) + "\n" + EffectText(kind);
            }
            RefreshBars();

            var spin = state.CanSpin(cfg);
            int cost = cfg.SpinCost(state.Spins);
            string tail = spin == BuyResult.MaxLevel ? "ALL MAXED" : spin == BuyResult.Ok ? "SPIN - " + cost : "NEED " + Mathf.Max(1, cost - state.Points);
            MenuKit.StyleButton(_spin, _spinning ? "SPINNING..." : tail, spin == BuyResult.Ok || _spinning ? MenuKit.Steel : MenuKit.Locked, false,
                !_confirming && _selected == SpinIndex);
            MenuKit.StyleButton(_reset, "RESET PROGRESS", MenuKit.Steel, false, !_confirming && _selected == ResetIndex);
            MenuKit.StyleButton(_continue, "CONTINUE", MenuKit.Steel, false, !_confirming && _selected == ContinueIndex);
            MenuKit.StyleButton(_menu, "MENU", MenuKit.Steel, false, !_confirming && _selected == MenuIndex);
            MenuKit.StyleButton(_confirmNo, "NO", MenuKit.Steel, false, _confirming && !_confirmOnYes);
            MenuKit.StyleButton(_confirmYes, "YES", MenuKit.Steel, false, _confirming && _confirmOnYes);
        }

        private void RefreshBars()
        {
            var cfg = _store.Config;
            var state = _store.State;
            bool pulse = _flash > 0f && Mathf.Repeat(_flash * 6f, 1f) > 0.4f;
            for (int i = 0; i < UpgradeState.Kinds.Length; i++)
            {
                var kind = UpgradeState.Kinds[i];
                int level = state.LevelOf(kind);
                if (_spinning && kind == _won) level--;
                bool wonCol = _flash > 0f && kind == _won;
                var bars = _bars[i];
                for (int b = 0; b < bars.Length; b++)
                {
                    bool on = b < level;
                    bool fresh = wonCol && b == level - 1;
                    // owned = solid neon bar, not owned = an empty outline (reads at a glance)
                    var col = fresh ? (pulse ? Color.white : Neon) : Neon;
                    bars[b].style.backgroundColor = on ? col : Color.clear;
                    MenuKit.Border(bars[b], 2f, on ? col : NeonOff);
                }
                MenuKit.Border(_colBox[i], 3f, wonCol ? (pulse ? Color.white : Neon) : UiTheme.Faint);
            }
        }

        private string EffectText(UpgradeKind kind)
        {
            var c = _store.Config;
            switch (kind)
            {
                case UpgradeKind.MaxHp: return "+" + c.maxHpPerLevel + " HP / " + Mathf.Max(1, c.maxHpLevelsPerHp) + " LV";
                case UpgradeKind.Speed: return "+" + (c.speedMultiplierPerLevel * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "% SPEED / LV";
                case UpgradeKind.Hook: return "+" + Num(c.hookRangePerLevel) + " HOOK / LV";
                case UpgradeKind.SwordAndOrbs: return "+" + Num(c.attackRangePerLevel) + " REACH / LV";
                default: return "";
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

        private void BuildContent()
        {
            var page = new VisualElement { pickingMode = PickingMode.Ignore };
            page.style.flexGrow = 1;
            page.style.paddingRight = MenuKit.LeftMargin;
            Root.Add(page);

            var header = MenuKit.Row(Justify.SpaceBetween);
            header.Add(MenuKit.Text("UPGRADES", null, MenuKit.Heading, UiTheme.Orange));
            var pointsBox = MenuKit.Row(Justify.FlexEnd);
            pointsBox.Add(MenuKit.Text("POINTS  ", null, MenuKit.Label, UiTheme.Orange));
            _points = MenuKit.Text("0", null, MenuKit.Entry, Bone);
            pointsBox.Add(_points);
            header.Add(pointsBox);
            header.style.flexShrink = 0;
            page.Add(header);

            _earned = MenuKit.Text("", null, MenuKit.Label, Bone);
            _earned.style.marginTop = 2f * MenuKit.T;
            page.Add(_earned);

            // four columns: a vertical stack of level bars over a name box (as drawn)
            var cols = MenuKit.Row(Justify.SpaceAround);
            cols.style.alignItems = Align.FlexEnd;
            cols.style.marginTop = 4f * MenuKit.T;
            cols.style.flexShrink = 0;
            page.Add(cols);
            for (int i = 0; i < UpgradeState.Kinds.Length; i++)
            {
                var kind = UpgradeState.Kinds[i];
                int max = _store.Config.MaxLevel(kind);
                var col = new VisualElement { pickingMode = PickingMode.Ignore };
                col.style.alignItems = Align.Center;
                col.style.width = 400f;
                col.style.flexShrink = 0;
                var stack = new VisualElement { pickingMode = PickingMode.Ignore };
                stack.style.flexDirection = FlexDirection.ColumnReverse;   // level 1 at the bottom
                stack.style.alignItems = Align.Center;
                _bars[i] = new VisualElement[max];
                for (int b = 0; b < max; b++)
                {
                    var bar = new VisualElement { pickingMode = PickingMode.Ignore };
                    bar.style.height = 12f;
                    bar.style.width = 200f;                                  // every bar the same: clean
                    bar.style.marginTop = 7f;
                    bar.style.flexShrink = 0;
                    MenuKit.Round(bar, 0);
                    stack.Add(bar);
                    _bars[i][b] = bar;
                }
                col.Add(stack);
                var box = new VisualElement { pickingMode = PickingMode.Ignore };
                box.style.marginTop = 18f;
                box.style.width = 380f;
                box.style.height = 178f;
                box.style.flexShrink = 0;
                box.style.paddingLeft = box.style.paddingRight = 3f * MenuKit.T;
                box.style.paddingTop = box.style.paddingBottom = 2f * MenuKit.T;
                MenuKit.Border(box, 3f, UiTheme.Faint);
                var title = MenuKit.Text(Title(kind), null, MenuKit.Entry, UiTheme.Orange);
                title.style.whiteSpace = WhiteSpace.NoWrap;
                box.Add(title);
                _colLevel[i] = MenuKit.Text("", null, MenuKit.Label, Bone);
                box.Add(_colLevel[i]);
                _colBox[i] = box;
                col.Add(box);
                cols.Add(col);
            }

            // the RNG box: a vertical reel window over the spin button
            var rngRow = MenuKit.Row(Justify.Center);
            rngRow.style.marginTop = 6f * MenuKit.T;
            rngRow.style.flexShrink = 0;
            page.Add(rngRow);
            var rng = new VisualElement { pickingMode = PickingMode.Ignore };
            rng.style.width = 560f;
            rng.style.alignItems = Align.Stretch;
            rng.style.paddingLeft = rng.style.paddingRight = 3f * MenuKit.T;
            rng.style.paddingTop = rng.style.paddingBottom = 2f * MenuKit.T;
            MenuKit.Border(rng, 4f, UiTheme.Orange);
            rng.Add(MenuKit.Text("RNG", null, MenuKit.Label, UiTheme.Orange));
            var window = new VisualElement { pickingMode = PickingMode.Ignore };
            window.style.height = LineH * 3f;
            window.style.overflow = Overflow.Hidden;
            window.style.marginTop = MenuKit.T;
            window.style.marginBottom = 2f * MenuKit.T;
            window.style.backgroundColor = UiTheme.WithAlpha(Color.black, 0.45f);
            var marker = new VisualElement { pickingMode = PickingMode.Ignore };
            marker.style.position = Position.Absolute;
            marker.style.left = 0; marker.style.right = 0;
            marker.style.top = LineH;
            marker.style.height = LineH;
            MenuKit.Border(marker, 3f, Neon);
            _reelStrip = new VisualElement { pickingMode = PickingMode.Ignore };
            _reelStrip.style.position = Position.Absolute;
            _reelStrip.style.left = 0; _reelStrip.style.right = 0;
            for (int k = 0; k < 3; k++)
            {
                _reel[k] = MenuKit.Text("", null, MenuKit.Entry, k == 1 ? Bone : Dim);
                _reel[k].style.height = LineH;
                _reel[k].style.unityTextAlign = TextAnchor.MiddleCenter;
                _reelStrip.Add(_reel[k]);
            }
            window.Add(_reelStrip);
            window.Add(marker);
            rng.Add(window);
            _spin = MakeButton(() => { Select(SpinIndex); Activate(SpinIndex); }, SpinIndex, 520f);
            _spin.style.unityTextAlign = TextAnchor.MiddleCenter;
            rng.Add(_spin);
            rngRow.Add(rng);

            _result = MenuKit.Text("", null, MenuKit.Entry, Neon);
            _result.style.unityTextAlign = TextAnchor.MiddleCenter;
            _result.style.height = 14f * MenuKit.T;
            _result.style.flexShrink = 0;
            _result.style.marginTop = 2f * MenuKit.T;
            page.Add(_result);

            var footer = MenuKit.Row(Justify.Center);
            _reset = MakeButton(() => { Select(ResetIndex); Activate(ResetIndex); }, ResetIndex, 420f);
            _continue = MakeButton(() => { Select(ContinueIndex); Activate(ContinueIndex); }, ContinueIndex, 300f);
            _menu = MakeButton(() => { Select(MenuIndex); Activate(MenuIndex); }, MenuIndex, 200f);
            foreach (var b in new[] { _reset, _continue, _menu }) { b.style.width = StyleKeyword.Auto; b.style.whiteSpace = WhiteSpace.NoWrap; b.style.flexShrink = 0; b.style.paddingLeft = b.style.paddingRight = 4f * MenuKit.T; }
            _reset.style.marginRight = _continue.style.marginRight = 8f * MenuKit.T;
            footer.style.flexShrink = 0;
            footer.Add(_reset);
            footer.Add(_continue);
            footer.Add(_menu);
            page.Add(footer);

            _status = MenuKit.Text("", null, MenuKit.Label, Danger);
            _status.style.display = DisplayStyle.None;
            _status.style.unityTextAlign = TextAnchor.MiddleCenter;
            page.Add(_status);
            var hint = MenuKit.Text("ARROWS / D-PAD  SELECT      ENTER - A  SPIN / CONFIRM", null, MenuKit.Label, Dim);
            hint.style.unityTextAlign = TextAnchor.MiddleCenter;
            hint.style.marginTop = 3f * MenuKit.T;
            page.Add(hint);
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
            _confirm.pickingMode = PickingMode.Position; // blocks clicks to the screen behind
            Root.Add(_confirm);

            _confirm.Add(MenuKit.Text("RESET ALL PROGRESS?", null, MenuKit.Heading, Danger));
            var sub = MenuKit.Text("ALL POINTS, SPINS AND UPGRADES WILL BE LOST", null, MenuKit.Label, UiTheme.Orange);
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

        /// <summary>A click-only button (not focusable; keyboard and gamepad go through <see cref="Update"/>).</summary>
        private Button MakeButton(Action onClick, int selectIndex, float width)
        {
            var b = MenuKit.MakeButton(onClick, () => Time.frameCount != _openedFrame && !_spinning, width);
            if (selectIndex >= 0) b.RegisterCallback<PointerEnterEvent>(_ => Select(selectIndex));
            return b;
        }

        private void OnDestroy()
        {
            if (_settings != null) Destroy(_settings);
        }
    }
}
