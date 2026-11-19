using System;
using System.Collections.Generic;
using Redline.Game.Audio;
using UnityEngine;
using UnityEngine.UIElements;

namespace Redline.Game
{
    /// <summary>
    /// A full-screen vertical selection-bar menu in the Replaced terminal style: used for the main menu (big "REDLINE"
    /// title) and the pause menu. Items are added once with <see cref="AddItem"/>; a notice line
    /// under the title can carry a short message (e.g. points banked by the last run).
    /// </summary>
    /// <example>
    /// <code>
    /// var menu = gameObject.AddComponent&lt;MenuScreen&gt;();
    /// menu.Init("MainMenu", 90f, "REDLINE", true);
    /// menu.AddItem("PLAY", StartRun);
    /// menu.Cancelled = null;   // Esc does nothing on the main menu
    /// menu.Show();
    /// </code>
    /// </example>
    /// <remarks>
    /// Keyboard: W/S or arrows select, Enter/Space activate, Esc invokes <see cref="Cancelled"/>.
    /// Gamepad: d-pad/stick select, South activate, East cancel. Mouse: hover and click.
    /// The press that opens the menu is ignored. Main-thread only.
    /// </remarks>
    public sealed class MenuScreen : MonoBehaviour
    {
        private readonly List<Button> _buttons = new List<Button>(6);
        private readonly List<string> _labels = new List<string>(6);
        private readonly List<Action> _actions = new List<Action>(6);
        private PanelSettings _settings;
        private VisualElement _list;
        private Label _notice;
        private Label _hint;
        private MenuNav _nav;
        private int _selected;
        private int _openedFrame = -1;
        private CreditsScreen _credits;
        private bool _creditsAdded;

        /// <summary>The screen name given to <see cref="Init"/> ("MainMenu" for the main menu).</summary>
        public string ScreenName { get; private set; }

        /// <summary>Invoked on Esc / gamepad East; null means back does nothing.</summary>
        public Action Cancelled;

        /// <summary>The root element, exposed for screenshot tooling.</summary>
        public VisualElement Root { get; private set; }

        /// <summary>Whether the menu is showing.</summary>
        public bool Visible { get; private set; }

        /// <summary>Builds the (hidden) menu. Call once.</summary>
        /// <param name="screenName">GameObject and panel name.</param>
        /// <param name="sortingOrder">UI layer (HUD is below 100, the upgrade screen at 100).</param>
        /// <param name="title">Heading text.</param>
        /// <param name="neonTitle">True for the main menu (left scrim over the live level; the pause menu dims the scene instead).</param>
        public void Init(string screenName, float sortingOrder, string title, bool neonTitle)
        {
            if (Root != null) return;
            ScreenName = screenName;
            _settings = MenuKit.CreatePanel(screenName + "PanelSettings", sortingOrder);
            Root = MenuKit.CreateRoot(transform, _settings, screenName, neonTitle ? MenuKit.OpaqueOverlay : MenuKit.Overlay);
            // Title menu: dense ground under the left text column, the live level clear on the right.
            if (neonTitle) MenuKit.ApplyScrim(Root, 0.97f, 0.06f, 0.56f, 0.9f); // dense past the hint line's end (~51 %)

            var column = new VisualElement();
            column.style.alignItems = Align.FlexStart;
            column.pickingMode = PickingMode.Ignore;
            Root.Add(column);

            // Heading 16 T, left; the main menu title is the same size (no logo art, no glow).
            var heading = MenuKit.Text(title, MenuKit.Black, MenuKit.Heading, UiTheme.Orange);
            heading.style.marginBottom = 4f * MenuKit.T * 3f;
            column.Add(heading);

            _notice = MenuKit.Text("", MenuKit.Bold, MenuKit.Label, UiTheme.Bone);
            _notice.style.marginBottom = 4f * MenuKit.T;
            _notice.style.display = DisplayStyle.None;
            column.Add(_notice);

            _list = new VisualElement();
            _list.style.alignItems = Align.FlexStart;
            _list.pickingMode = PickingMode.Ignore;
            column.Add(_list);

            _hint = MenuKit.Text("W/S - D-PAD  SELECT      ENTER - A  CONFIRM", MenuKit.Bold, MenuKit.Label, MenuKit.Dim);
            _hint.style.marginTop = 8f * MenuKit.T;
            column.Add(_hint);
            Hide();
        }

        /// <summary>Adds a button. Items keep their order; the first is selected on open.</summary>
        public void AddItem(string label, Action onActivate)
        {
            // The main menu gets CREDITS after SETTINGS, right before QUIT (the audio attribution).
            if (!_creditsAdded && ScreenName == "MainMenu" && label == "QUIT")
            {
                _creditsAdded = true;
                AddItem("CREDITS", OpenCredits);
            }
            int index = _buttons.Count;
            var b = MenuKit.MakeButton(() => { Select(index); Activate(index); }, () => Time.frameCount != _openedFrame, 90f * MenuKit.T, MenuKit.RowHeight);
            b.RegisterCallback<PointerEnterEvent>(_ => Select(index));
            _buttons.Add(b);
            _labels.Add(label);
            _actions.Add(onActivate);
            _list.Add(b);
            Refresh();
        }

        /// <summary>Replaces the 8 T hint line under the list.</summary>
        public void SetHint(string text) => _hint.text = text;

        /// <summary>Sets (or clears, with null/empty) the notice line under the title.</summary>
        public void SetNotice(string text)
        {
            bool show = !string.IsNullOrEmpty(text);
            _notice.text = show ? text : "";
            _notice.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Shows the menu with the first item selected.</summary>
        public void Show()
        {
            Visible = true;
            _openedFrame = Time.frameCount;
            _selected = 0;
            Root.style.display = DisplayStyle.Flex;
            Refresh();
        }

        /// <summary>Hides the menu.</summary>
        public void Hide()
        {
            Visible = false;
            Root.style.display = DisplayStyle.None;
        }

        private void Update()
        {
            if (!Visible || Time.frameCount == _openedFrame || _buttons.Count == 0) return;
            _nav.Read();
            if (_nav.Up) Select((_selected + _buttons.Count - 1) % _buttons.Count);
            if (_nav.Down) Select((_selected + 1) % _buttons.Count);
            if (_nav.Submit) Activate(_selected);
            else if (_nav.Cancel) Cancelled?.Invoke();
        }

        private void OpenCredits()
        {
            if (_credits == null)
            {
                _credits = gameObject.AddComponent<CreditsScreen>();
                _credits.Closed += Show;
            }
            Hide();
            _credits.Show();
        }

        private void Activate(int index)
        {
            if (!Visible || index < 0 || index >= _actions.Count) return;
            _actions[index]?.Invoke();
        }

        private void Select(int index)
        {
            if (index == _selected) return;
            _selected = index;
            Refresh();
        }

        private void Refresh()
        {
            for (int i = 0; i < _buttons.Count; i++)
                MenuKit.StyleButton(_buttons[i], _labels[i], MenuKit.Steel, i == _selected, i == _selected);
        }

        private void OnDestroy()
        {
            if (_settings != null) Destroy(_settings);
        }
    }
}
