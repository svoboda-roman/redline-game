using System;
using Redline.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Redline.Game
{
    /// <summary>
    /// Draws the training (<see cref="TrainingScript"/>): the current step's card (step counter,
    /// title, body, the tip line once it is due, and a "hold {RESTART} to skip" line with its hold
    /// bar), the success flash ("NICE" / "SKIPPED"), a marker line for the fling step, and the
    /// "TRAINING COMPLETE" card. Same look as <see cref="TutorialOverlay"/>; never takes input.
    /// </summary>
    /// <remarks>
    /// Call <see cref="Tick"/> once per frame. Text is resolved only when the script's
    /// <see cref="TrainingScript.Revision"/> changes (or keys are rebound); a steady frame
    /// allocates nothing. Step text: Resources/Training.json. Main-thread only.
    /// </remarks>
    public sealed class TrainingOverlay : MonoBehaviour
    {
        private const float FadeTime = 0.2f;
        private const float CardTop = 118f;
        private const float CardWidth = 820f;
        private const float SkipBarWidth = 160f;
        private const string CompleteTitle = "TRAINING COMPLETE";
        private const string CompleteBody = "You know the moves. Now chain them: speed is power.";
        private const string SkipText = "HOLD {RESTART} TO SKIP THIS STEP";

        private static readonly Color PanelColor = new Color(0.03f, 0.03f, 0.09f, 0.86f);
        private static readonly Color Cyan = new Color(0.3f, 0.85f, 1f);
        private static readonly Color Gold = new Color(1f, 0.92f, 0.45f);
        private static readonly Color Good = new Color(0.45f, 1f, 0.6f);
        private static readonly Color BodyColor = new Color(0.85f, 0.88f, 1f);
        private static readonly Color Dim = new Color(0.62f, 0.68f, 0.9f);

        private RedlineGame _game;
        private TrainingScript _script;
        private Func<string, string> _labels;
        private PanelSettings _settings;
        private VisualElement _card, _skipRow, _skipFill, _marker;
        private Label _counter, _title, _body, _tip, _skip, _flash;
        private string[] _counterTexts = Array.Empty<string>();
        private int _shownRevision = -1;
        private bool _shownTip;
        private float _alpha;

        /// <summary>Builds the panel for a game (the camera and world-to-draw mapping come from it).</summary>
        public void Init(RedlineGame game, Func<string, string> labels)
        {
            _game = game;
            _labels = labels;
            Build();
        }

        /// <summary>Loads Resources/Training.json; null (with a logged error) when it is missing or invalid.</summary>
        public static TrainingData LoadData()
        {
            var asset = Resources.Load<TextAsset>("Training");
            if (asset == null)
            {
                Debug.LogError("Redline: Resources/Training.json not found; the training is unavailable.");
                return null;
            }
            try
            {
                return TrainingData.Parse(asset.text);
            }
            catch (FormatException ex)
            {
                Debug.LogError("Redline: Resources/Training.json is invalid; the training is unavailable: " + ex.Message);
                return null;
            }
        }

        /// <summary>Shows a script (null hides the overlay). Allocates the counter texts once per script.</summary>
        public void Bind(TrainingScript script)
        {
            _script = script;
            _shownRevision = -1;
            _alpha = 0f;
            if (script != null && _counterTexts.Length != script.StepCount)
            {
                _counterTexts = new string[script.StepCount];
                for (int i = 0; i < _counterTexts.Length; i++) _counterTexts[i] = "STEP " + (i + 1) + "/" + script.StepCount;
            }
            if (_card == null) return;
            if (script == null)
            {
                _card.style.display = DisplayStyle.None;
                _flash.style.display = DisplayStyle.None;
                _marker.style.display = DisplayStyle.None;
            }
        }

        /// <summary>Re-resolves the visible text, e.g. after the player rebinds a key.</summary>
        public void SetLabels(Func<string, string> labels)
        {
            _labels = labels;
            _shownRevision = -1;
        }

        /// <summary>Animates the overlay. <paramref name="paused"/> fades the card out (pause menu). Pass unscaled frame time.</summary>
        public void Tick(float dt, bool paused)
        {
            if (_card == null) return;
            var s = _script;
            if (s == null) return;
            if (s.Revision != _shownRevision || s.TipVisible != _shownTip) ApplyText(s);

            _alpha = Mathf.MoveTowards(_alpha, paused ? 0f : 1f, dt / FadeTime);
            _card.style.opacity = _alpha;
            _card.style.display = _alpha > 0f ? DisplayStyle.Flex : DisplayStyle.None;

            // Success flash: pops in and fades over the success time.
            bool flashing = s.Phase == TrainingPhase.Success && !paused;
            _flash.style.display = flashing ? DisplayStyle.Flex : DisplayStyle.None;
            if (flashing)
            {
                float t = Mathf.Clamp01(s.PhaseTime / Mathf.Max(0.01f, s.Data.Settings.successTime));
                float scale = 1f + 0.35f * (1f - Mathf.Clamp01(t * 5f));
                _flash.style.scale = new StyleScale(new Scale(new Vector2(scale, scale)));
                _flash.style.opacity = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f;
            }

            _skipFill.style.width = SkipBarWidth * s.SkipProgress;
            UpdateMarker(s, paused);
        }

        private void ApplyText(TrainingScript s)
        {
            _shownRevision = s.Revision;
            _shownTip = s.TipVisible;
            var step = s.Current;
            if (step == null)
            {
                _counter.text = "";
                _title.text = CompleteTitle;
                _title.style.color = Gold;
                _body.text = CompleteBody;
                _tip.style.display = DisplayStyle.None;
                _skipRow.style.display = DisplayStyle.None;
                SetBorder(Gold);
                return;
            }
            _counter.text = _counterTexts[Mathf.Clamp(s.StepIndex, 0, _counterTexts.Length - 1)];
            _title.text = TutorialText.Resolve(step.Title, _labels);
            _title.style.color = Cyan;
            _body.text = TutorialText.Resolve(step.Body, _labels);
            _tip.text = TutorialText.Resolve(step.Tip, _labels);
            _tip.style.display = s.TipVisible ? DisplayStyle.Flex : DisplayStyle.None;
            _skip.text = TutorialText.Resolve(SkipText, _labels);
            _skipRow.style.display = DisplayStyle.Flex;
            _flash.text = s.Skipped ? "SKIPPED" : "NICE";
            _flash.style.color = s.Skipped ? Dim : Good;
            SetBorder(Cyan);
        }

        private void SetBorder(Color c)
        {
            c.a = 0.55f;
            _card.style.borderTopColor = c;
            _card.style.borderBottomColor = c;
            _card.style.borderLeftColor = c;
            _card.style.borderRightColor = c;
        }

        /// <summary>A vertical line at the fling marker column, placed through the game camera.</summary>
        private void UpdateMarker(TrainingScript s, bool paused)
        {
            float x = s.MarkerX;
            var cam = _game != null ? _game.Camera : null;
            var root = _marker.parent;
            float w = root.resolvedStyle.width;
            float h = root.resolvedStyle.height;
            bool show = !paused && !float.IsNaN(x) && s.Phase == TrainingPhase.Active && cam != null && !float.IsNaN(w) && w > 0f;
            if (show)
            {
                var floorY = s.Current.Station.CheckpointY;
                var bottom = cam.WorldToViewportPoint(_game.ToDraw(new Vector2(x, floorY)));
                var top = cam.WorldToViewportPoint(_game.ToDraw(new Vector2(x, floorY + 4f)));
                show = bottom.x >= 0f && bottom.x <= 1f;
                if (show)
                {
                    _marker.style.left = bottom.x * w - 3f;
                    _marker.style.top = (1f - top.y) * h;
                    _marker.style.height = Mathf.Max(4f, (top.y - bottom.y) * h);
                }
            }
            _marker.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void Build()
        {
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.name = "RedlineTrainingPanelSettings";
            _settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("RedlineTheme");
            _settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            _settings.referenceResolution = new Vector2Int(1920, 1080);
            _settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            _settings.match = 0.5f;
            _settings.sortingOrder = 1;

            var host = new GameObject("TrainingOverlay");
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

            _marker = new VisualElement { pickingMode = PickingMode.Ignore };
            _marker.style.position = Position.Absolute;
            _marker.style.width = 6;
            _marker.style.backgroundColor = new Color(Gold.r, Gold.g, Gold.b, 0.75f);
            _marker.style.display = DisplayStyle.None;
            root.Add(_marker);

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
            _card.style.paddingLeft = 24;
            _card.style.paddingRight = 24;
            _card.style.paddingTop = 10;
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
            strip.Add(_card);

            _counter = MakeLabel(bold, 16, Dim);
            _counter.style.letterSpacing = 3;
            _card.Add(_counter);
            _title = MakeLabel(black, 30, Cyan);
            _title.style.letterSpacing = 4;
            _title.style.marginBottom = 4;
            _card.Add(_title);
            _body = MakeLabel(bold, 22, BodyColor);
            _body.style.whiteSpace = WhiteSpace.Normal;
            _body.style.unityTextAlign = TextAnchor.MiddleCenter;
            _card.Add(_body);
            _tip = MakeLabel(bold, 20, Gold);
            _tip.style.whiteSpace = WhiteSpace.Normal;
            _tip.style.unityTextAlign = TextAnchor.MiddleCenter;
            _tip.style.marginTop = 6;
            _card.Add(_tip);

            _skipRow = new VisualElement { pickingMode = PickingMode.Ignore };
            _skipRow.style.flexDirection = FlexDirection.Row;
            _skipRow.style.alignItems = Align.Center;
            _skipRow.style.marginTop = 8;
            _skip = MakeLabel(bold, 14, Dim);
            _skip.style.marginRight = 10;
            _skipRow.Add(_skip);
            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.style.width = SkipBarWidth;
            bar.style.height = 5;
            bar.style.backgroundColor = new Color(1f, 1f, 1f, 0.15f);
            _skipFill = new VisualElement { pickingMode = PickingMode.Ignore };
            _skipFill.style.height = Length.Percent(100);
            _skipFill.style.width = 0;
            _skipFill.style.backgroundColor = Dim;
            bar.Add(_skipFill);
            _skipRow.Add(bar);
            _card.Add(_skipRow);

            _flash = MakeLabel(black, 96, Good);
            _flash.style.position = Position.Absolute;
            _flash.style.top = 380;
            _flash.style.letterSpacing = 10;
            _flash.style.display = DisplayStyle.None;
            strip.Add(_flash);
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
