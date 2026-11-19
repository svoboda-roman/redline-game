using System;
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
    /// Settings screen (UI Toolkit, terminal style), opened from the main menu or the pause menu.
    /// One scrolling list: master / music / effects block-bar sliders, fullscreen, tutorial hints,
    /// Reduced Motion and HUD Text Scale choices, then keyboard rebinding, one row per <see cref="GameAction"/> with three key slots and
    /// the fixed gamepad control shown read-only. Edits <see cref="GameSettings.Current"/> in place,
    /// raises <see cref="GameSettings.NotifyChanged"/> on every change and saves on close.
    /// </summary>
    /// <example>
    /// <code>
    /// var settings = gameObject.AddComponent&lt;SettingsScreen&gt;();
    /// settings.Init(audio.PlayPreview);
    /// settings.Show(() => pauseMenu.Show());   // the callback runs when the screen closes
    /// </code>
    /// </example>
    /// <remarks>
    /// Keyboard: W/S or arrows select, A/D or left/right change a value or pick a key slot,
    /// Enter/Space toggle or start rebinding, Backspace/Delete clear a key slot, Esc back (or cancel
    /// a rebinding). Gamepad: d-pad/stick, South, North clears, East back. Mouse: hover, click, drag sliders.
    /// Rebinding: the next key pressed is bound; a key owned by another action is swapped with this
    /// slot's previous key; leaving an action without any key is refused. Esc cannot be bound here
    /// (it cancels); RESET CONTROLS restores the defaults. Main-thread only.
    /// </remarks>
    public sealed class SettingsScreen : MonoBehaviour
    {
        private const int SliderCount = 3;
        private const int FullscreenIndex = 3;
        private const int HintsIndex = 4;
        private const int MotionIndex = 5;
        private const int ScaleIndex = 6;
        private const int FirstBindIndex = 7;
        private static readonly int BindCount = ControlBindings.Actions.Length;
        private static readonly int ResetIndex = FirstBindIndex + BindCount;
        private static readonly int BackIndex = ResetIndex + 1;
        private static readonly int ItemCount = BackIndex + 1;
        private const int VolumeStep = 5;
        private static readonly string[] SliderNames = { "MASTER", "MUSIC", "EFFECTS" };
        private static readonly int[] ScaleSteps = { 100, 125, 150 };

        /// <summary>Status line shown when the settings file could not be written.</summary>
        public const string SaveFailed = "SAVE FAILED - SETTINGS NOT KEPT";

        private static readonly string[] Bars = BuildBars();

        private Action _preview;
        private Action _onClose;
        private PanelSettings _panel;
        private MenuNav _nav;
        private int _selected;
        private int _slot;
        private int _openedFrame = -1;
        private bool _capturing;
        private int _captureFrame = -1;

        private sealed class RowView
        {
            public VisualElement Root;
            public Label Name;
            public Label Value;   // value, or key slot 1
            public Label Value2;  // key slot 2 (bind rows)
            public Label[] Slots; // every key slot of a bind row (ControlBindings.SlotsPerAction)
            public Label Extra;   // read-only gamepad label (bind rows)
        }

        private readonly RowView[] _views = new RowView[64];
        private ScrollView _scroll;
        private Label _status;
        private bool _saveFailedShown;
        private bool _blinkOn = true;
        private int _scrolledTo = -1;

        /// <summary>The root element, exposed for screenshot tooling.</summary>
        public VisualElement Root { get; private set; }

        /// <summary>Whether the screen is showing.</summary>
        public bool Visible { get; private set; }

        /// <summary>Builds the (hidden) screen. Call once.</summary>
        /// <param name="previewEffect">Plays a short effect when the effects or master volume changes; may be null.</param>
        public void Init(Action previewEffect)
        {
            if (Root != null) return;
            _preview = previewEffect;
            _panel = MenuKit.CreatePanel("RedlineSettingsPanelSettings", 120f);
            Root = MenuKit.CreateRoot(transform, _panel, "SettingsScreen", MenuKit.OpaqueOverlay);
            Build();
            Hide();
        }

        /// <summary>Shows the screen; <paramref name="onClose"/> runs after it closes (settings saved).</summary>
        public void Show(Action onClose)
        {
            _onClose = onClose;
            Visible = true;
            _capturing = false;
            _openedFrame = Time.frameCount;
            _selected = 0;
            _slot = 0;
            _saveFailedShown = false;
            _scrolledTo = -1;
            SetStatus("", MenuKit.Dim);
            Root.style.display = DisplayStyle.Flex;
            Refresh();
        }

        /// <summary>Hides the screen without saving or running the close callback.</summary>
        public void Hide()
        {
            Visible = false;
            _capturing = false;
            Root.style.display = DisplayStyle.None;
        }

        private void Close()
        {
            if (!GameSettings.Save() && !_saveFailedShown)
            {
                // Stay open once so the player sees it; a second BACK leaves anyway.
                _saveFailedShown = true;
                SetStatus(SaveFailed, MenuKit.Danger);
                return;
            }
            Hide();
            var cb = _onClose;
            _onClose = null;
            cb?.Invoke();
        }

        // ------------------------------------------------------------------ input

        private void Update()
        {
            if (!Visible || Time.frameCount == _openedFrame) return;
            if (_capturing)
            {
                bool on = UiTheme.ReducedMotion || ((int)Time.unscaledTime & 1) == 0; // 1 Hz blink
                if (on != _blinkOn) { _blinkOn = on; Refresh(); }
                if (Time.frameCount != _captureFrame) Capture();
                return;
            }
            _nav.Read();
            if (_nav.Up) Select(PrevItem(_selected));
            if (_nav.Down) Select(NextItem(_selected));
            if (_nav.Left) Side(-1);
            if (_nav.Right) Side(1);
            if (_nav.Clear && IsBindRow(_selected)) ClearSlot(_selected - FirstBindIndex, _slot);
            if (_nav.Submit) Activate(_selected);
            else if (_nav.Cancel) Close();
        }

        private static int NextItem(int i) => (i + 1) % ItemCount;
        private static int PrevItem(int i) => (i + ItemCount - 1) % ItemCount;
        private static bool IsBindRow(int i) => i >= FirstBindIndex && i < ResetIndex;

        private void Side(int dir)
        {
            if (_selected < SliderCount) AddVolume(_selected, dir * VolumeStep);
            else if (_selected == ScaleIndex) CycleScale(dir);
            else if (_selected == FullscreenIndex || _selected == HintsIndex || _selected == MotionIndex) Activate(_selected);
            else if (IsBindRow(_selected)) { _slot = Mathf.Clamp(_slot + dir, 0, ControlBindings.SlotsPerAction - 1); Refresh(); }
            else Select(_selected == ResetIndex ? BackIndex : ResetIndex);
        }

        private void Activate(int index)
        {
            var s = GameSettings.Current;
            if (index == FullscreenIndex) { s.Fullscreen = !s.Fullscreen; Changed(); }
            else if (index == ScaleIndex) CycleScale(1);
            else if (index == MotionIndex) { s.ReducedMotion = !s.ReducedMotion; Changed(); }
            else if (index == HintsIndex) { s.HintsEnabled = !s.HintsEnabled; Changed(); }
            else if (IsBindRow(index)) BeginCapture();
            else if (index == ResetIndex)
            {
                s.Bindings.ResetToDefaults();
                SetStatus("CONTROLS RESET TO DEFAULTS", MenuKit.Gold);
                Changed();
            }
            else if (index == BackIndex) Close();
        }

        private void CycleScale(int dir)
        {
            var s = GameSettings.Current;
            int i = Array.IndexOf(ScaleSteps, s.HudTextScale);
            if (i < 0) i = 0;
            s.HudTextScale = ScaleSteps[(i + dir + ScaleSteps.Length) % ScaleSteps.Length];
            Changed();
        }

        private void AddVolume(int slider, int delta) => SetVolume(slider, GetVolume(slider) + delta);

        private static int GetVolume(int slider)
        {
            var s = GameSettings.Current;
            return slider == 0 ? s.MasterVolume : slider == 1 ? s.MusicVolume : s.SfxVolume;
        }

        private void SetVolume(int slider, int value)
        {
            value = Mathf.Clamp(value, 0, 100);
            if (value == GetVolume(slider)) return;
            var s = GameSettings.Current;
            if (slider == 0) s.MasterVolume = value;
            else if (slider == 1) s.MusicVolume = value;
            else s.SfxVolume = value;
            Changed();
            if (slider != 1) _preview?.Invoke();
        }

        private void Changed()
        {
            GameSettings.NotifyChanged();
            Refresh();
        }

        // ------------------------------------------------------------------ rebinding

        private void BeginCapture()
        {
            _capturing = true;
            _captureFrame = Time.frameCount;
            var action = ControlBindings.Actions[_selected - FirstBindIndex];
            SetStatus("PRESS A KEY FOR " + KeyLabels.ActionName(action) + " - ESC CANCELS", MenuKit.Gold);
            Refresh();
        }

        private void Capture()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame))
            {
                _capturing = false;
                SetStatus("REBINDING CANCELLED", MenuKit.Dim);
                Refresh();
                return;
            }
            if (kb == null || !kb.anyKey.wasPressedThisFrame) return;
            var keys = kb.allKeys;
            for (int i = 0; i < keys.Count; i++)
            {
                KeyControl k = keys[i];
                if (k == null || !k.wasPressedThisFrame || k.keyCode == Key.None) continue;
                Bind(k.keyCode.ToString());
                return;
            }
#else
            _capturing = false;
#endif
        }

        private void Bind(string key)
        {
            _capturing = false;
            var action = ControlBindings.Actions[_selected - FirstBindIndex];
            var b = GameSettings.Current.Bindings;
            string previous = b.Get(action, _slot);
            var result = b.TryBind(action, _slot, key, out var other);
            string label = KeyLabels.Label(key);
            switch (result)
            {
                case BindResult.Ok:
                    SetStatus(label + " BOUND TO " + KeyLabels.ActionName(action), MenuKit.Gold);
                    break;
                case BindResult.Swapped:
                    SetStatus("SWAPPED: " + label + " NOW " + KeyLabels.ActionName(action) + ", "
                              + KeyLabels.Label(previous) + " NOW " + KeyLabels.ActionName(other), MenuKit.Gold);
                    break;
                case BindResult.Moved:
                    SetStatus(label + " MOVED FROM " + KeyLabels.ActionName(other) + " TO " + KeyLabels.ActionName(action), MenuKit.Gold);
                    break;
                case BindResult.RefusedLastKey:
                    SetStatus("REFUSED: " + label + " IS THE ONLY KEY FOR " + KeyLabels.ActionName(other)
                              + ". GIVE IT ANOTHER KEY FIRST", MenuKit.Danger);
                    break;
                case BindResult.Unchanged:
                    SetStatus(label + " IS ALREADY BOUND HERE", MenuKit.Dim);
                    break;
                default:
                    SetStatus("THAT KEY CANNOT BE BOUND", MenuKit.Danger);
                    break;
            }
            Changed();
        }

        private void ClearSlot(int row, int slot)
        {
            var action = ControlBindings.Actions[row];
            var result = GameSettings.Current.Bindings.TryClear(action, slot);
            if (result == BindResult.RefusedLastKey)
                SetStatus("REFUSED: " + KeyLabels.ActionName(action) + " NEEDS AT LEAST ONE KEY", MenuKit.Danger);
            else if (result == BindResult.Ok)
                SetStatus("KEY CLEARED FROM " + KeyLabels.ActionName(action), MenuKit.Dim);
            Changed();
        }

        private void SetStatus(string text, Color color)
        {
            _status.text = text;
            _status.style.color = color;
        }

        // ------------------------------------------------------------------ view

        private void Select(int index)
        {
            if (_capturing) return;
            _selected = index;
            Refresh();
        }

        private void Refresh()
        {
            var s = GameSettings.Current;
            for (int i = 0; i < SliderCount; i++) _views[i].Value.text = Bars[GetVolume(i)];
            _views[FullscreenIndex].Value.text = Choice(s.Fullscreen ? "ON" : "OFF");
            _views[HintsIndex].Value.text = Choice(s.HintsEnabled ? "ON" : "OFF");
            _views[MotionIndex].Value.text = Choice(s.ReducedMotion ? "ON" : "OFF");
            _views[ScaleIndex].Value.text = Choice(s.HudTextScale + "%");

            var b = s.Bindings;
            for (int r = 0; r < BindCount; r++)
            {
                var action = ControlBindings.Actions[r];
                bool rowSelected = _selected == FirstBindIndex + r;
                var v = _views[FirstBindIndex + r];
                for (int k = 0; k < ControlBindings.SlotsPerAction; k++)
                {
                    bool here = rowSelected && _slot == k;
                    string key = KeyLabels.Label(b.Get(action, k));
                    string text = here && _capturing ? (_blinkOn ? "PRESS A KEY" : "") : here ? "<" + key + ">" : key;
                    v.Slots[k].text = text;
                }
            }
            for (int i = 0; i < ItemCount; i++)
            {
                var v = _views[i];
                bool sel = _selected == i;
                v.Root.style.backgroundColor = sel ? UiTheme.Orange : Color.clear;
                var nameColor = sel ? UiTheme.Ground : UiTheme.Orange;
                var valColor = sel ? UiTheme.Ground : UiTheme.Bone;
                v.Name.style.color = (i == ResetIndex || i == BackIndex) ? nameColor : nameColor;
                if (v.Value != null) v.Value.style.color = valColor;
                if (v.Value2 != null) v.Value2.style.color = valColor;
                if (v.Slots != null) foreach (var sl in v.Slots) sl.style.color = valColor;
                if (v.Extra != null) v.Extra.style.color = sel ? UiTheme.Ground : UiTheme.Dim;
            }
            if (_scrolledTo != _selected)
            {
                _scrolledTo = _selected;
                var root = _views[_selected].Root;
                _scroll.schedule.Execute(() => _scroll.ScrollTo(root)).StartingIn(0);
            }
        }

        private static string Choice(string v) => "< " + v + " >";

        // ------------------------------------------------------------------ building

        private bool ClickAllowed() => Time.frameCount != _openedFrame && !_capturing;

        private void Build()
        {
            Root.Add(MenuKit.Text("SETTINGS", MenuKit.Black, MenuKit.Heading, UiTheme.Orange));
            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _scroll.style.width = 440f * MenuKit.T;   // name 116 + three key slots 72 + gamepad label
            _scroll.style.height = 8f * 18f * MenuKit.T; // about 9 rows of 16 T plus headers
            _scroll.style.marginTop = 2f * MenuKit.T;
            _scroll.style.flexShrink = 1;
            Root.Add(_scroll);

            _scroll.Add(Section("AUDIO"));
            for (int i = 0; i < SliderCount; i++) AddSlider(i);
            _scroll.Add(Section("DISPLAY"));
            AddChoice(FullscreenIndex, "FULLSCREEN");
            _scroll.Add(Section("GAMEPLAY"));
            AddChoice(HintsIndex, "TUTORIAL HINTS");
            AddChoice(MotionIndex, "REDUCED MOTION");
            AddChoice(ScaleIndex, "HUD TEXT SCALE");
            _scroll.Add(Section("CONTROLS - KEYBOARD - GAMEPAD IS FIXED"));
            for (int r = 0; r < BindCount; r++) AddBind(r);
            AddPlain(ResetIndex, "RESET CONTROLS");
            AddPlain(BackIndex, "BACK");

            _status = MenuKit.Text("", MenuKit.Bold, MenuKit.Label, MenuKit.Gold);
            _status.style.marginTop = 2f * MenuKit.T;
            _status.style.height = 8f * MenuKit.T;
            Root.Add(_status);
            var hint = MenuKit.Text("ENTER REBIND - BACKSPACE CLEAR - A/D PICK SLOT OR CHANGE VALUE - ESC BACK", MenuKit.Bold, MenuKit.Label, MenuKit.Dim);
            Root.Add(hint);
        }

        private static Label Section(string text)
        {
            var c = MenuKit.Caption(text);
            c.style.marginTop = 3f * MenuKit.T;
            c.style.marginBottom = MenuKit.T;
            return c;
        }

        private RowView NewRow(int index, string name)
        {
            var row = MenuKit.Row(Justify.FlexStart);
            row.style.height = MenuKit.RowHeight;
            row.style.paddingLeft = 4f * MenuKit.T;
            row.pickingMode = PickingMode.Position;
            row.RegisterCallback<PointerEnterEvent>(_ => Select(index));
            var v = new RowView { Root = row };
            v.Name = MenuKit.Text(name, null, MenuKit.Entry, UiTheme.Orange);
            v.Name.style.width = 116f * MenuKit.T;   // fits the longest name (REDUCED MOTION, HUD TEXT SCALE) before the value
            v.Name.style.whiteSpace = WhiteSpace.NoWrap;
            row.Add(v.Name);
            _views[index] = v;
            _scroll.Add(row);
            return v;
        }

        private void AddPlain(int index, string name)
        {
            var v = NewRow(index, name);
            v.Root.RegisterCallback<ClickEvent>(_ => { if (ClickAllowed()) { Select(index); Activate(index); } });
        }

        private void AddChoice(int index, string name)
        {
            var v = NewRow(index, name);
            v.Value = MenuKit.Text("", null, MenuKit.Entry, UiTheme.Bone);
            v.Root.Add(v.Value);
            v.Root.RegisterCallback<ClickEvent>(_ => { if (ClickAllowed()) { Select(index); Activate(index); } });
        }

        private void AddSlider(int i)
        {
            var v = NewRow(i, SliderNames[i]);
            v.Value = MenuKit.Text("", null, MenuKit.Entry, UiTheme.Bone);
            v.Value.pickingMode = PickingMode.Position;
            v.Value.style.whiteSpace = WhiteSpace.NoWrap;
            v.Root.Add(v.Value);
            // Click or drag along the block bar sets the value (bar cells span the label after "[").
            v.Value.RegisterCallback<PointerDownEvent>(e => { Select(i); SetFromPointer(i, v.Value, e.localPosition.x); });
            v.Value.RegisterCallback<PointerMoveEvent>(e =>
            {
                if ((e.pressedButtons & 1) != 0) SetFromPointer(i, v.Value, e.localPosition.x);
            });
        }

        private void AddBind(int r)
        {
            int index = FirstBindIndex + r;
            var action = ControlBindings.Actions[r];
            var v = NewRow(index, KeyLabels.ActionName(action));
            for (int k = 0; k < ControlBindings.SlotsPerAction; k++)
            {
                int slot = k;
                var l = MenuKit.Text("", null, MenuKit.Entry, UiTheme.Bone);
                l.style.width = 72f * MenuKit.T;   // fits a selected long key: <L-SHIFT>
                l.style.whiteSpace = WhiteSpace.NoWrap;
                l.pickingMode = PickingMode.Position;
                l.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    if (_capturing) return;
                    _selected = index; _slot = slot; Refresh();
                });
                l.RegisterCallback<ClickEvent>(_ =>
                {
                    if (!ClickAllowed()) return;
                    _selected = index; _slot = slot; BeginCapture();
                });
                if (v.Slots == null) v.Slots = new Label[ControlBindings.SlotsPerAction];
                v.Slots[k] = l;
                if (k == 0) v.Value = l; else if (k == 1) v.Value2 = l;
                v.Root.Add(l);
            }
            v.Extra = MenuKit.Text(KeyLabels.GamepadLabel(action), null, MenuKit.Label, UiTheme.Dim);
            v.Extra.style.whiteSpace = WhiteSpace.NoWrap;
            v.Root.Add(v.Extra);
        }

        private void SetFromPointer(int slider, Label bar, float x)
        {
            if (_capturing || Time.frameCount == _openedFrame) return;
            float w = bar.resolvedStyle.width;
            if (!(w > 1f)) return;
            float cell = w / Bars[0].Length;
            int v = Mathf.RoundToInt(Mathf.Clamp01((x - cell) / (cell * 10f)) * 10f) * 10;
            SetVolume(slider, v);
        }

        /// <summary>"[||||||||..]  80": ten block cells (one per 10 %) plus the number.</summary>
        private static string[] BuildBars()
        {
            var p = new string[101];
            for (int i = 0; i <= 100; i++)
            {
                int n = Mathf.RoundToInt(i / 10f);
                p[i] = "[" + new string('|', n) + new string('.', 10 - n) + "] " + i.ToString().PadLeft(3);
            }
            return p;
        }

        private void OnDestroy()
        {
            if (_panel != null) Destroy(_panel);
        }
    }
}
