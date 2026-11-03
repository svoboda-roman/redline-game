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
    /// Settings screen (UI Toolkit, neon style), opened from the main menu or the pause menu.
    /// Left column: master / music / effects volume sliders, fullscreen and tutorial hints toggles.
    /// Right column: keyboard rebinding, one row per <see cref="GameAction"/> with three key slots and
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
        private const int FirstBindIndex = 5;
        private static readonly int BindCount = ControlBindings.Actions.Length;
        private static readonly int ResetIndex = FirstBindIndex + BindCount;
        private static readonly int BackIndex = ResetIndex + 1;
        private static readonly int ItemCount = BackIndex + 1;
        private const int VolumeStep = 5;
        private static readonly string[] SliderNames = { "MASTER", "MUSIC", "EFFECTS" };

        private static readonly string[] Percent = BuildPercent();

        private Action _preview;
        private Action _onClose;
        private PanelSettings _panel;
        private MenuNav _nav;
        private int _selected;
        private int _slot;
        private int _openedFrame = -1;
        private bool _capturing;
        private int _captureFrame = -1;

        private readonly VisualElement[] _rows = new VisualElement[16];
        private readonly VisualElement[] _sliderFill = new VisualElement[SliderCount];
        private readonly Label[] _sliderValue = new Label[SliderCount];
        private Button _fullscreen;
        private Button _hints;
        private readonly Button[][] _slots = new Button[ControlBindings.Actions.Length][];
        private Button _reset;
        private Button _back;
        private Label _status;

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
            GameSettings.Save();
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
            else if (_selected == FullscreenIndex || _selected == HintsIndex) Activate(_selected);
            else if (IsBindRow(_selected)) { _slot = Mathf.Clamp(_slot + dir, 0, ControlBindings.SlotsPerAction - 1); Refresh(); }
            else Select(_selected == ResetIndex ? BackIndex : ResetIndex);
        }

        private void Activate(int index)
        {
            var s = GameSettings.Current;
            if (index == FullscreenIndex) { s.Fullscreen = !s.Fullscreen; Changed(); }
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
            SetStatus("PRESS A KEY FOR " + KeyLabels.ActionName(action) + "  ·  ESC CANCELS", MenuKit.Gold);
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
            for (int i = 0; i < SliderCount; i++)
            {
                int v = GetVolume(i);
                _sliderFill[i].style.width = Length.Percent(v);
                _sliderValue[i].text = Percent[v];
            }
            MenuKit.StyleButton(_fullscreen, s.Fullscreen ? "ON" : "OFF", MenuKit.Cyan, s.Fullscreen, _selected == FullscreenIndex);
            MenuKit.StyleButton(_hints, s.HintsEnabled ? "ON" : "OFF", MenuKit.Cyan, s.HintsEnabled, _selected == HintsIndex);

            var b = s.Bindings;
            for (int r = 0; r < BindCount; r++)
            {
                var action = ControlBindings.Actions[r];
                bool rowSelected = _selected == FirstBindIndex + r;
                for (int k = 0; k < ControlBindings.SlotsPerAction; k++)
                {
                    bool here = rowSelected && _slot == k;
                    string text = here && _capturing ? "..." : KeyLabels.Label(b.Get(action, k));
                    MenuKit.StyleButton(_slots[r][k], text, b.Get(action, k) == null ? MenuKit.Locked : MenuKit.Cyan, here && _capturing, here);
                }
            }
            for (int i = 0; i < ResetIndex; i++)
                if (_rows[i] != null) MenuKit.Border(_rows[i], 2f, _selected == i ? MenuKit.Gold : new Color(0.3f, 0.85f, 1f, 0.2f));
            MenuKit.StyleButton(_reset, "RESET CONTROLS", MenuKit.Danger, false, _selected == ResetIndex);
            MenuKit.StyleButton(_back, "BACK", MenuKit.Cyan, true, _selected == BackIndex);
        }

        // ------------------------------------------------------------------ building

        private bool ClickAllowed() => Time.frameCount != _openedFrame && !_capturing;

        private void Build()
        {
            var panel = MenuKit.Panel(1680f, MenuKit.Cyan);
            Root.Add(panel);
            panel.Add(MenuKit.Text("SETTINGS", MenuKit.Black, 54, MenuKit.Cyan));
            var line = new VisualElement();
            line.style.height = 3;
            line.style.marginTop = 6;
            line.style.marginBottom = 12;
            line.style.backgroundColor = MenuKit.Cyan;
            panel.Add(line);

            var columns = MenuKit.Row(Justify.SpaceBetween);
            columns.style.alignItems = Align.FlexStart;
            panel.Add(columns);

            var left = new VisualElement();
            left.style.width = 560;
            left.pickingMode = PickingMode.Ignore;
            columns.Add(left);
            left.Add(Section("AUDIO"));
            for (int i = 0; i < SliderCount; i++) left.Add(SliderRow(i));
            left.Add(Section("DISPLAY"));
            _fullscreen = MenuKit.MakeButton(() => { Select(FullscreenIndex); Activate(FullscreenIndex); }, ClickAllowed, 130, 44, 22);
            left.Add(ToggleRow(FullscreenIndex, "FULLSCREEN", _fullscreen));
            left.Add(Section("GAMEPLAY"));
            _hints = MenuKit.MakeButton(() => { Select(HintsIndex); Activate(HintsIndex); }, ClickAllowed, 130, 44, 22);
            left.Add(ToggleRow(HintsIndex, "TUTORIAL HINTS", _hints));

            var right = new VisualElement();
            right.style.width = 1000;
            right.pickingMode = PickingMode.Ignore;
            columns.Add(right);
            right.Add(Section("KEYBOARD  ·  GAMEPAD IS FIXED"));
            for (int r = 0; r < BindCount; r++) right.Add(BindRow(r));

            _status = MenuKit.Text("", MenuKit.Bold, 20, MenuKit.Dim);
            _status.style.marginTop = 16;
            _status.style.height = 28;
            panel.Add(_status);

            var footer = MenuKit.Row(Justify.SpaceBetween);
            footer.style.marginTop = 10;
            _reset = MenuKit.MakeButton(() => { Select(ResetIndex); Activate(ResetIndex); }, ClickAllowed, 300);
            _back = MenuKit.MakeButton(() => { Select(BackIndex); Activate(BackIndex); }, ClickAllowed, 300);
            _reset.RegisterCallback<PointerEnterEvent>(_ => Select(ResetIndex));
            _back.RegisterCallback<PointerEnterEvent>(_ => Select(BackIndex));
            footer.Add(_reset);
            footer.Add(_back);
            panel.Add(footer);
            panel.Add(MenuKit.Text("ENTER REBIND  ·  BACKSPACE CLEAR  ·  A/D PICK SLOT OR CHANGE VALUE  ·  ESC BACK", MenuKit.Bold, 16, MenuKit.Dim));
        }

        private static Label Section(string text)
        {
            var c = MenuKit.Caption(text);
            c.style.marginTop = 14;
            c.style.marginBottom = 4;
            return c;
        }

        private VisualElement ItemRow(int index)
        {
            var row = MenuKit.Row(Justify.SpaceBetween);
            row.style.marginTop = 6;
            row.style.paddingLeft = 16;
            row.style.paddingRight = 10;
            row.style.height = 56;
            row.style.backgroundColor = MenuKit.RowColor;
            MenuKit.Round(row, 8);
            row.pickingMode = PickingMode.Position;
            row.RegisterCallback<PointerEnterEvent>(_ => Select(index));
            _rows[index] = row;
            return row;
        }

        private VisualElement SliderRow(int i)
        {
            var row = ItemRow(i);
            var name = MenuKit.Text(SliderNames[i], MenuKit.Black, 24, Color.white);
            name.style.width = 150;
            row.Add(name);

            var track = new VisualElement();
            track.style.flexGrow = 1;
            track.style.height = 16;
            track.style.marginLeft = 10;
            track.style.marginRight = 16;
            track.style.backgroundColor = MenuKit.PipOff;
            MenuKit.Round(track, 4);
            track.pickingMode = PickingMode.Position;
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.style.height = Length.Percent(100);
            fill.style.backgroundColor = MenuKit.Cyan;
            MenuKit.Round(fill, 4);
            track.Add(fill);
            _sliderFill[i] = fill;
            track.RegisterCallback<PointerDownEvent>(e => { Select(i); SetFromPointer(i, track, e.localPosition.x); });
            track.RegisterCallback<PointerMoveEvent>(e =>
            {
                if ((e.pressedButtons & 1) != 0) SetFromPointer(i, track, e.localPosition.x);
            });
            row.Add(track);

            _sliderValue[i] = MenuKit.Text("100%", MenuKit.Bold, 22, MenuKit.Gold);
            _sliderValue[i].style.width = 76;
            _sliderValue[i].style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(_sliderValue[i]);
            return row;
        }

        private void SetFromPointer(int slider, VisualElement track, float x)
        {
            if (_capturing || Time.frameCount == _openedFrame) return;
            float w = track.resolvedStyle.width;
            if (!(w > 1f)) return;
            int v = Mathf.RoundToInt(Mathf.Clamp01(x / w) * 100f);
            SetVolume(slider, v);
        }

        private VisualElement ToggleRow(int index, string label, Button toggle)
        {
            var row = ItemRow(index);
            row.Add(MenuKit.Text(label, MenuKit.Black, 24, Color.white));
            row.Add(toggle);
            return row;
        }

        private VisualElement BindRow(int r)
        {
            int index = FirstBindIndex + r;
            var action = ControlBindings.Actions[r];
            var row = ItemRow(index);
            row.style.height = 50;
            row.style.marginTop = 5;
            var name = MenuKit.Text(KeyLabels.ActionName(action), MenuKit.Black, 22, Color.white);
            name.style.width = 190;
            row.Add(name);

            var slots = MenuKit.Row(Justify.FlexStart);
            _slots[r] = new Button[ControlBindings.SlotsPerAction];
            for (int k = 0; k < ControlBindings.SlotsPerAction; k++)
            {
                int slot = k;
                var b = MenuKit.MakeButton(() =>
                {
                    _selected = index;
                    _slot = slot;
                    BeginCapture();
                }, ClickAllowed, 170, 38, 18);
                b.style.marginLeft = 8;
                b.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    if (_capturing) return;
                    _selected = index;
                    _slot = slot;
                    Refresh();
                });
                _slots[r][k] = b;
                slots.Add(b);
            }
            row.Add(slots);

            var pad = MenuKit.Text(KeyLabels.GamepadLabel(action), MenuKit.Bold, 16, MenuKit.Dim);
            pad.style.width = 230;
            pad.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(pad);
            return row;
        }

        private static string[] BuildPercent()
        {
            var p = new string[101];
            for (int i = 0; i <= 100; i++) p[i] = i + "%";
            return p;
        }

        private void OnDestroy()
        {
            if (_panel != null) Destroy(_panel);
        }
    }
}
