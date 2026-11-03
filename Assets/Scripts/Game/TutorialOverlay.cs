using System;
using Redline.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Redline.Game
{
    /// <summary>
    /// Draws the active tutorial hint (<see cref="TutorialHints"/>) as a small neon card at the top
    /// centre of the screen that fades and slides in, never takes input, and never blocks the action.
    /// Own UIDocument, same theme and fonts as <see cref="RedlineHud"/>.
    /// </summary>
    /// <remarks>
    /// Call <see cref="Step"/> after every sim step (hint timers run on sim time, so pausing the sim
    /// freezes them) and <see cref="Tick"/> once per frame for the animation. Text is resolved and
    /// assigned only when the hint changes; Tick allocates nothing. Main-thread only.
    /// Design: contextual hints task (hint ids and text in Resources/Hints.json).
    /// </remarks>
    public sealed class TutorialOverlay : MonoBehaviour
    {
        /// <summary>Seconds for the card to fade fully in or out (presentation only).</summary>
        private const float FadeTime = 0.22f;
        /// <summary>Pixels (reference 1920×1080) the card slides down while it fades in.</summary>
        private const float SlideDistance = 18f;
        /// <summary>Card top edge, below the HUD's top row.</summary>
        private const float CardTop = 118f;
        private const float CardWidth = 760f;

        private static readonly Color PanelColor = new Color(0.03f, 0.03f, 0.09f, 0.86f);
        private static readonly Color Cyan = new Color(0.3f, 0.85f, 1f);
        private static readonly Color EnemyColor = new Color(1f, 0.62f, 0.3f);
        private static readonly Color BodyColor = new Color(0.85f, 0.88f, 1f);

        private TutorialHints _hints;
        private Func<string, string> _labels;
        private PanelSettings _settings;
        private VisualElement _card;
        private Label _title;
        private Label _body;
        private int _shownVersion = -1;
        private float _alpha;
        private float _drawnAlpha = -1f;

        /// <summary>The hint director (null until <see cref="Init"/> succeeds).</summary>
        public TutorialHints Hints => _hints;

        /// <summary>The once-per-save seen-set to persist with the settings save (null before Init).</summary>
        public TutorialProgress Progress => _hints?.Progress;

        /// <summary>
        /// Builds the panel. <paramref name="catalog"/> null (Hints.json missing or invalid) leaves the
        /// overlay inert. <paramref name="labels"/> turns tokens like "HOOK" into key labels.
        /// </summary>
        public void Init(HintCatalog catalog, TutorialProgress progress, Func<string, string> labels)
        {
            _labels = labels;
            if (catalog == null) return;
            _hints = new TutorialHints(catalog, progress);
            Build();
        }

        /// <summary>Loads Resources/Hints.json; null (with a logged warning) when it is missing or invalid.</summary>
        public static HintCatalog LoadCatalog()
        {
            var asset = Resources.Load<TextAsset>("Hints");
            if (asset == null)
            {
                Debug.LogWarning("Redline: Resources/Hints.json not found; tutorial hints are off.");
                return null;
            }
            try
            {
                return HintCatalog.Parse(asset.text);
            }
            catch (FormatException ex)
            {
                Debug.LogWarning("Redline: Resources/Hints.json is invalid; tutorial hints are off: " + ex.Message);
                return null;
            }
        }

        /// <summary>Feeds one sim step to the hint director. Call right after <c>sim.Step</c>.</summary>
        public void Step(RedlineSim sim, bool enabled)
        {
            if (_hints != null && sim != null) _hints.Step(sim, RedlineSim.FixedDt, enabled);
        }

        /// <summary>Starts a new run: once-per-run hints (Loop01's zones) may show again.</summary>
        public void BeginRun() => _hints?.BeginRun();

        /// <summary>
        /// Animates the card. <paramref name="paused"/> (pause menu, upgrade screen) fades it out without
        /// ending the hint; it comes back on resume. Pass unscaled frame time.
        /// </summary>
        public void Tick(float dt, bool paused)
        {
            if (_hints == null || _card == null) return;
            if (_hints.Version != _shownVersion)
            {
                _shownVersion = _hints.Version;
                if (_hints.Active != null)
                {
                    ApplyText(_hints.Active);
                    _alpha = 0f;
                }
            }
            float target = _hints.Active != null && !paused ? 1f : 0f;
            _alpha = Mathf.MoveTowards(_alpha, target, dt / FadeTime);
            Draw();
        }

        /// <summary>Ends the current hint and queue at once and hides the card (nothing is un-seen).</summary>
        public void Hide()
        {
            _hints?.Clear();
            if (_hints != null) _shownVersion = _hints.Version;
            _alpha = 0f;
            Draw();
        }

        /// <summary>Re-resolves the visible text, e.g. after the player rebinds a key.</summary>
        public void SetLabels(Func<string, string> labels)
        {
            _labels = labels;
            if (_hints?.Active != null) ApplyText(_hints.Active);
        }

        private void ApplyText(HintEntry hint)
        {
            _title.text = TutorialText.Resolve(hint.Title, _labels);
            _body.text = TutorialText.Resolve(hint.Body, _labels);
            var accent = Accent(hint.Id);
            _title.style.color = accent;
            accent.a = 0.55f;
            _card.style.borderTopColor = accent;
            _card.style.borderBottomColor = accent;
            _card.style.borderLeftColor = accent;
            _card.style.borderRightColor = accent;
        }

        private static Color Accent(string id)
        {
            if (id.StartsWith("enemy.", StringComparison.Ordinal)) return EnemyColor;
            if (id == HintIds.Orb(OrbKind.Health)) return OrbRenderer.KindColor(OrbKind.Health);
            if (id == HintIds.Orb(OrbKind.Speed)) return OrbRenderer.KindColor(OrbKind.Speed);
            if (id == HintIds.Orb(OrbKind.Hook)) return OrbRenderer.KindColor(OrbKind.Hook);
            return Cyan;
        }

        private void Draw()
        {
            if (Mathf.Approximately(_alpha, _drawnAlpha)) return;
            _drawnAlpha = _alpha;
            _card.style.display = _alpha > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            _card.style.opacity = _alpha;
            float eased = 1f - (1f - _alpha) * (1f - _alpha);
            _card.style.top = CardTop - (1f - eased) * SlideDistance;
        }

        private void Build()
        {
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.name = "RedlineTutorialPanelSettings";
            _settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("RedlineTheme");
            _settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            _settings.referenceResolution = new Vector2Int(1920, 1080);
            _settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            _settings.match = 0.5f;
            _settings.sortingOrder = 1;

            var host = new GameObject("TutorialOverlay");
            host.transform.SetParent(transform, false);
            host.SetActive(false);
            var doc = host.AddComponent<UIDocument>();
            doc.panelSettings = _settings;
            host.SetActive(true);

            var root = doc.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;

            var fallback = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var black = Resources.Load<Font>("Fonts/SourceCodePro-Black") ?? fallback;
            var bold = Resources.Load<Font>("Fonts/SourceCodePro-Bold") ?? fallback;

            // A full-width strip centres the card horizontally without measuring it.
            var strip = new VisualElement { pickingMode = PickingMode.Ignore };
            strip.style.position = Position.Absolute;
            strip.style.left = 0;
            strip.style.right = 0;
            strip.style.top = 0;
            strip.style.bottom = 0;
            strip.style.alignItems = Align.Center;
            root.Add(strip);

            _card = new VisualElement { pickingMode = PickingMode.Ignore };
            _card.style.position = Position.Absolute;
            _card.style.top = CardTop;
            _card.style.maxWidth = CardWidth;
            _card.style.backgroundColor = PanelColor;
            _card.style.paddingLeft = 22;
            _card.style.paddingRight = 22;
            _card.style.paddingTop = 12;
            _card.style.paddingBottom = 14;
            _card.style.alignItems = Align.Center;
            _card.style.borderTopWidth = 2;
            _card.style.borderBottomWidth = 2;
            _card.style.borderLeftWidth = 2;
            _card.style.borderRightWidth = 2;
            _card.style.borderTopLeftRadius = 8;
            _card.style.borderTopRightRadius = 8;
            _card.style.borderBottomLeftRadius = 8;
            _card.style.borderBottomRightRadius = 8;
            _card.style.display = DisplayStyle.None;
            _card.style.opacity = 0f;
            strip.Add(_card);

            _title = MakeLabel(black, 26, Cyan);
            _title.style.letterSpacing = 4;
            _title.style.marginBottom = 4;
            _card.Add(_title);
            _body = MakeLabel(bold, 21, BodyColor);
            _body.style.whiteSpace = WhiteSpace.Normal;
            _body.style.unityTextAlign = TextAnchor.MiddleCenter;
            _card.Add(_body);
        }

        private static Label MakeLabel(Font font, int size, Color color)
        {
            var label = new Label(string.Empty) { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size;
            label.style.color = color;
            if (font != null) label.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(font));
            label.style.marginLeft = 0;
            label.style.marginRight = 0;
            return label;
        }

        private void OnDestroy()
        {
            if (_settings != null) Destroy(_settings);
        }
    }
}
