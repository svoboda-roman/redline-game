using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Redline.Game.Audio
{
    /// <summary>
    /// The CREDITS screen of the main menu: the game title, the CC BY 4.0 music attribution (exact
    /// lines from Assets/Resources/Audio/CREDITS.md, loaded at runtime as a TextAsset so it works in
    /// builds) and the CC0 SFX packs with their authors. Scrollable; Esc / B / BACK closes it.
    /// </summary>
    /// <example>
    /// <code>
    /// var credits = gameObject.AddComponent&lt;CreditsScreen&gt;();
    /// credits.Closed += menu.Show;
    /// credits.Show();
    /// </code>
    /// </example>
    /// <remarks>Main-thread only. Built lazily on the first <see cref="Show"/>.</remarks>
    public sealed class CreditsScreen : MonoBehaviour
    {
        /// <summary>Resources path of the credits text (Assets/Resources/Audio/CREDITS.md).</summary>
        public const string CreditsResource = "Audio/CREDITS";

        private PanelSettings _settings;
        private VisualElement _root;
        private ScrollView _scroll;
        private Button _back;
        private MenuNav _nav;
        private int _openedFrame = -1;

        /// <summary>True while any credits screen is showing (read by the audio for the menu music).</summary>
        public static bool AnyVisible { get; private set; }

        /// <summary>Whether this screen is showing.</summary>
        public bool Visible { get; private set; }

        /// <summary>Invoked after the screen closes.</summary>
        public event Action Closed;

        /// <summary>Shows the screen at the top.</summary>
        public void Show()
        {
            Build();
            Visible = AnyVisible = true;
            _openedFrame = Time.frameCount;
            _root.style.display = DisplayStyle.Flex;
            _scroll.scrollOffset = Vector2.zero;
        }

        /// <summary>Hides the screen and raises <see cref="Closed"/>.</summary>
        public void Hide()
        {
            if (!Visible) return;
            Visible = AnyVisible = false;
            _root.style.display = DisplayStyle.None;
            Closed?.Invoke();
        }

        private void Update()
        {
            if (!Visible || Time.frameCount == _openedFrame) return;
            _nav.Read();
            if (_nav.Cancel || _nav.Submit) { Hide(); return; }
            if (_nav.Down) _scroll.scrollOffset += new Vector2(0f, 80f);
            if (_nav.Up) _scroll.scrollOffset -= new Vector2(0f, 80f);
        }

        private void Build()
        {
            if (_root != null) return;
            _settings = MenuKit.CreatePanel("CreditsPanelSettings", 95f);
            _root = MenuKit.CreateRoot(transform, _settings, "Credits", MenuKit.OpaqueOverlay);

            var heading = MenuKit.Text("CREDITS", MenuKit.Black, MenuKit.Heading, UiTheme.Orange);
            heading.style.marginBottom = 4f * MenuKit.T;
            _root.Add(heading);

            var panel = MenuKit.Panel(190f * MenuKit.T, MenuKit.Steel);
            panel.style.height = 8f * 12f * MenuKit.T;
            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _scroll.style.flexGrow = 1;
            panel.Add(_scroll);
            _root.Add(panel);

            var data = Parse(LoadText());
            AddLine("REDLINE", MenuKit.Black, MenuKit.Entry, UiTheme.Bone, 0);
            AddLine("FONT", MenuKit.Black, MenuKit.Label, UiTheme.Dim, 12);
            AddLine("DEPARTURE MONO - HELENA ZHANG - SIL OFL 1.1", MenuKit.Bold, MenuKit.Label, MenuKit.Steel, 2);
            AddLine("MUSIC", MenuKit.Black, MenuKit.Label, UiTheme.Dim, 12);
            for (int i = 0; i < data.MusicLines.Count; i++) AddLine(data.MusicLines[i], MenuKit.Bold, MenuKit.Label, MenuKit.Steel, 2);
            AddLine("SOUND EFFECTS: CC0 1.0 (public domain), credited with thanks", MenuKit.Black, MenuKit.Label, UiTheme.Dim, 12);
            for (int i = 0; i < data.Packs.Count; i++) AddLine(data.Packs[i], MenuKit.Bold, MenuKit.Label, MenuKit.Steel, 2);

            _back = MenuKit.MakeButton(Hide, () => Time.frameCount != _openedFrame, 90f * MenuKit.T);
            _back.style.marginTop = 2f * MenuKit.T;
            MenuKit.StyleButton(_back, "BACK", MenuKit.Steel, true, true);
            _root.Add(_back);
            var hint = MenuKit.Text("W/S - D-PAD  SCROLL      ESC - B  BACK", MenuKit.Bold, MenuKit.Label, MenuKit.Dim);
            hint.style.marginTop = 2f * MenuKit.T;
            _root.Add(hint);
            _root.style.display = DisplayStyle.None;
        }

        private void AddLine(string text, Font font, int size, Color color, float marginTop)
        {
            var label = MenuKit.Text(text, font, size, color);
            label.style.marginTop = marginTop;
            label.style.fontSize = size;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.unityTextAlign = TextAnchor.UpperLeft;
            _scroll.Add(label);
        }

        private static string LoadText()
        {
            var asset = Resources.Load<TextAsset>(CreditsResource);
            if (asset != null) return asset.text;
            Debug.LogWarning("Redline: Resources/" + CreditsResource + " missing; credits show the fixed attribution only.");
            return "";
        }

        /// <summary>Lines shown on the screen.</summary>
        public sealed class CreditsData
        {
            /// <summary>The CC BY attribution block, line by line, verbatim.</summary>
            public readonly List<string> MusicLines = new List<string>();
            /// <summary>"Pack name - Author" per CC0 pack.</summary>
            public readonly List<string> Packs = new List<string>();
        }

        /// <summary>
        /// Extracts the required attribution block (between "## Required attribution" and "Source:") and
        /// the "### pack" + "- Author:" pairs of CREDITS.md. Falls back to the licence line when empty.
        /// </summary>
        public static CreditsData Parse(string markdown)
        {
            var data = new CreditsData();
            data.MusicLines.Add("Music by Kevin MacLeod (incompetech.com), licensed under Creative Commons: By Attribution 4.0 License, http://creativecommons.org/licenses/by/4.0/");
            data.MusicLines.Add("");
            var lines = (markdown ?? "").Replace("\r", "").Split('\n');
            bool inAttribution = false;
            string pack = null;
            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i].TrimEnd();
                if (raw.StartsWith("## Required attribution", StringComparison.Ordinal)) { inAttribution = true; continue; }
                if (inAttribution)
                {
                    if (raw.StartsWith("Source:", StringComparison.Ordinal) || raw.StartsWith("## ", StringComparison.Ordinal)) inAttribution = false;
                    else if (raw.Trim().Length > 0) data.MusicLines.Add(raw.Trim());
                    continue;
                }
                if (raw.StartsWith("### ", StringComparison.Ordinal)) { pack = raw.Substring(4).Trim(); continue; }
                if (pack != null && raw.StartsWith("- Author:", StringComparison.Ordinal))
                {
                    data.Packs.Add(new StringBuilder(pack).Append("  -  ").Append(raw.Substring(9).Trim()).ToString());
                    pack = null;
                }
            }
            if (data.MusicLines.Count <= 2)
            {
                data.MusicLines.Add("(Track list unavailable: Resources/Audio/CREDITS.md missing.)");
            }
            return data;
        }

        private void OnDestroy()
        {
            if (Visible) AnyVisible = false;
            if (_settings != null) Destroy(_settings);
        }
    }
}
