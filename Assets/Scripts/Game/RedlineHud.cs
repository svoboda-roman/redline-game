using Redline.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Redline.Game
{
    /// <summary>
    /// UI Toolkit HUD built from code, styled to match the neon arena: a speed panel (tier-coloured
    /// border, zoned meter, live speed, tier badge, HP pips), a chain badge with a draining timer,
    /// a wave panel (number + progress dots + enemies left), an animated centre banner and the
    /// wave-1 controls hint. Text is only rewritten when its value changes, and the speed readout
    /// uses pre-built strings, so a steady frame allocates nothing.
    /// </summary>
    /// <remarks>Added by <see cref="RedlineGame"/>. Main-thread only. Font: Source Code Pro (SIL OFL, see Resources/Fonts).</remarks>
    public sealed class RedlineHud : MonoBehaviour
    {
        private const string ControlsText =
            "<color=#7FE3FF>A/D</color> move · <color=#7FE3FF>Space</color> jump · <color=#7FE3FF>J</color> attack · " +
            "hold <color=#7FE3FF>K</color> hook · <color=#7FE3FF>S</color> slide · <color=#7FE3FF>R</color> restart\n" +
            "Pad: stick · <color=#7FE3FF>A</color> jump · <color=#7FE3FF>X</color> attack · hold <color=#7FE3FF>RB</color> hook · " +
            "<color=#7FE3FF>B</color> slide\n" +
            "<color=#FFF27A>Tier 3 breaks armor:</color> hook swing · wall-jump after a slide · kill chain";

        private static readonly Color PanelColor = new Color(0.03f, 0.03f, 0.09f, 0.82f);
        private static readonly Color Dim = new Color(0.62f, 0.68f, 0.9f);
        private static readonly Color Cyan = new Color(0.3f, 0.85f, 1f);
        private static readonly Color HpColor = new Color(1f, 0.3f, 0.45f);
        private static readonly Color EnemyColor = new Color(1f, 0.62f, 0.3f);
        private static readonly Color Gold = new Color(1f, 0.92f, 0.45f);
        private static readonly Color Danger = new Color(1f, 0.25f, 0.4f);

        private RedlineGame _game;
        private PanelSettings _settings;
        private Font _black;
        private Font _bold;

        private VisualElement _speedPanel;
        private VisualElement _speedFill;
        private Label _speedValue;
        private Label _tierBadge;
        private Label _armorTag;
        private VisualElement[] _hpPips;
        private VisualElement _chainBox;
        private Label _chainLabel;
        private VisualElement _chainTimer;
        private Label _waveNumber;
        private Label _waveTotal;
        private VisualElement[] _waveDots;
        private Label _enemiesLabel;
        private VisualElement _bannerBox;
        private Label _banner;
        private VisualElement _bannerLine;
        private Label _subBanner;
        private VisualElement _controls;

        private string[] _speedTexts;
        private int _shownSpeed = -1;
        private int _shownTier = -1;
        private int _shownHp = -1;
        private int _shownChain = -1;
        private int _shownWave = -1;
        private int _shownEnemies = -1;
        private int _shownCountdown = -1;
        private int _shownBannerWave = -1;
        private SimState _shownState = (SimState)(-1);
        private float _hpFlash;
        private int _hpFlashIndex = -1;
        private float _chainPop;
        private float _bannerAge = 99f;

        /// <summary>The root element, exposed for screenshot tooling.</summary>
        public VisualElement Root { get; private set; }

        /// <summary>Builds the HUD for a game.</summary>
        public void Init(RedlineGame game)
        {
            if (_game != null) return; // already built
            _game = game;
            var cfg = game.Sim.Config;
            LoadFonts();
            BuildDocument();
            BuildSpeedPanel(cfg);
            BuildChainBadge();
            BuildWavePanel(cfg);
            BuildBanner();
            BuildControls();

            _speedTexts = new string[Mathf.CeilToInt(cfg.maxSpeed * 2f) + 1];
            for (int i = 0; i < _speedTexts.Length; i++) _speedTexts[i] = (i * 0.5f).ToString("0.0");
        }

        // ------------------------------------------------------------------ building

        private void LoadFonts()
        {
            var fallback = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _black = Resources.Load<Font>("Fonts/SourceCodePro-Black");
            _bold = Resources.Load<Font>("Fonts/SourceCodePro-Bold");
            if (_black == null || _bold == null)
            {
                Debug.LogWarning("Redline: HUD fonts missing from Resources/Fonts; using the built-in font.");
                _black = _black != null ? _black : fallback;
                _bold = _bold != null ? _bold : fallback;
            }
        }

        private void BuildDocument()
        {
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.name = "RedlinePanelSettings";
            _settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("RedlineTheme");
            if (_settings.themeStyleSheet == null)
                Debug.LogWarning("Redline: Resources/RedlineTheme.tss not found; the HUD will use unstyled defaults.");
            _settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            _settings.referenceResolution = new Vector2Int(1920, 1080);
            _settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            _settings.match = 0.5f;

            var host = new GameObject("HUD");
            host.transform.SetParent(transform, false);
            host.SetActive(false);
            var doc = host.AddComponent<UIDocument>();
            doc.panelSettings = _settings;
            host.SetActive(true);

            Root = doc.rootVisualElement;
            Root.pickingMode = PickingMode.Ignore;
            Root.style.color = Color.white;
            var body = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (body != null) Root.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(body));
        }

        private void BuildSpeedPanel(RedlineConfig cfg)
        {
            _speedPanel = Panel(left: 24f, top: 16f, Cyan);
            _speedPanel.style.width = 560;
            Root.Add(_speedPanel);

            var header = Row(Justify.SpaceBetween);
            header.Add(Caption("SPEED"));
            _speedValue = Text("0.0", _black, 30, Cyan);
            header.Add(_speedValue);
            _speedPanel.Add(header);

            _speedPanel.Add(BuildMeter(cfg));

            var status = Row(Justify.FlexStart);
            status.style.marginTop = 10;
            _tierBadge = Text("TIER 1", _black, 26, Color.black);
            Pill(_tierBadge, Cyan);
            status.Add(_tierBadge);
            _armorTag = Text("BREAKS ARMOR", _bold, 20, Gold);
            _armorTag.style.marginLeft = 12;
            status.Add(_armorTag);
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            status.Add(spacer);
            status.Add(BuildHpPips(cfg));
            _speedPanel.Add(status);
        }

        private VisualElement BuildMeter(RedlineConfig cfg)
        {
            float t2 = cfg.tier2Speed / cfg.maxSpeed * 100f;
            float t3 = cfg.tier3Speed / cfg.maxSpeed * 100f;
            var track = new VisualElement();
            track.style.height = 22;
            track.style.marginTop = 6;
            track.style.backgroundColor = new Color(0.06f, 0.07f, 0.14f, 1f);
            Round(track, 5);
            track.style.overflow = Overflow.Hidden;
            track.Add(Zone(0f, t2, RedlineGame.TierColors[0]));
            track.Add(Zone(t2, t3, RedlineGame.TierColors[1]));
            track.Add(Zone(t3, 100f, RedlineGame.TierColors[2]));
            _speedFill = new VisualElement();
            _speedFill.style.position = Position.Absolute;
            _speedFill.style.left = 0;
            _speedFill.style.top = 0;
            _speedFill.style.bottom = 0;
            _speedFill.style.width = Length.Percent(0);
            track.Add(_speedFill);
            track.Add(Mark(t2));
            track.Add(Mark(t3));

            return track;
        }

        private VisualElement BuildHpPips(RedlineConfig cfg)
        {
            var row = Row(Justify.FlexEnd);
            _hpPips = new VisualElement[cfg.playerMaxHp];
            for (int i = 0; i < _hpPips.Length; i++)
            {
                var pip = new VisualElement();
                pip.style.width = 24;
                pip.style.height = 24;
                pip.style.marginLeft = 6;
                Round(pip, 6);
                Border(pip, 2f, HpColor);
                row.Add(pip);
                _hpPips[i] = pip;
            }
            return row;
        }

        private void BuildChainBadge()
        {
            _chainBox = new VisualElement();
            // Top centre, in the strip between the HUD panels and the arena (the wave-1 hint sits above it).
            _chainBox.style.position = Position.Absolute;
            _chainBox.style.left = 0;
            _chainBox.style.right = 0;
            _chainBox.style.top = 142;
            _chainBox.style.alignItems = Align.Center;
            _chainBox.pickingMode = PickingMode.Ignore;
            Root.Add(_chainBox);
            _chainLabel = Text("", _black, 26, Color.black);
            Pill(_chainLabel, Gold);
            _chainBox.Add(_chainLabel);
            var bar = new VisualElement();
            bar.style.height = 5;
            bar.style.width = 150;
            bar.style.marginTop = 5;
            bar.style.backgroundColor = new Color(1f, 1f, 1f, 0.15f);
            Round(bar, 2);
            _chainTimer = new VisualElement();
            _chainTimer.style.height = Length.Percent(100);
            _chainTimer.style.backgroundColor = Gold;
            Round(_chainTimer, 2);
            bar.Add(_chainTimer);
            _chainBox.Add(bar);
            _chainBox.style.display = DisplayStyle.None;
        }

        private void BuildWavePanel(RedlineConfig cfg)
        {
            var panel = Panel(left: float.NaN, top: 16f, Cyan);
            panel.style.right = 24;
            panel.style.width = 330;
            Root.Add(panel);

            var top = Row(Justify.SpaceBetween);
            top.style.alignItems = Align.FlexEnd;
            var caption = Caption("WAVE");
            caption.style.marginBottom = 10;
            top.Add(caption);
            var number = Row(Justify.FlexEnd);
            number.style.alignItems = Align.FlexEnd;
            _waveNumber = Text("1", _black, 54, Color.white);
            number.Add(_waveNumber);
            _waveTotal = Text("/" + cfg.WaveCount, _bold, 28, Dim);
            _waveTotal.style.marginBottom = 8;
            number.Add(_waveTotal);
            top.Add(number);
            panel.Add(top);

            var bottom = Row(Justify.SpaceBetween);
            bottom.style.marginTop = 4;
            var dots = Row(Justify.FlexStart);
            _waveDots = new VisualElement[cfg.WaveCount];
            for (int i = 0; i < _waveDots.Length; i++)
            {
                var dot = new VisualElement();
                dot.style.width = 16;
                dot.style.height = 16;
                dot.style.marginRight = 6;
                Round(dot, 8);
                Border(dot, 2f, Cyan);
                dots.Add(dot);
                _waveDots[i] = dot;
            }
            bottom.Add(dots);
            var enemies = Row(Justify.FlexEnd);
            enemies.Add(Caption("FOES"));
            _enemiesLabel = Text("0", _black, 30, EnemyColor);
            _enemiesLabel.style.marginLeft = 10;
            enemies.Add(_enemiesLabel);
            bottom.Add(enemies);
            panel.Add(bottom);
        }

        private void BuildBanner()
        {
            _bannerBox = new VisualElement();
            _bannerBox.style.position = Position.Absolute;
            _bannerBox.style.left = 0;
            _bannerBox.style.right = 0;
            _bannerBox.style.top = 210;
            _bannerBox.style.alignItems = Align.Center;
            _bannerBox.pickingMode = PickingMode.Ignore;
            Root.Add(_bannerBox);
            _banner = Text("", _black, 124, Color.white);
            _banner.style.letterSpacing = 12;
            _banner.style.unityTextAlign = TextAnchor.MiddleCenter;
            _bannerBox.Add(_banner);
            _bannerLine = new VisualElement();
            _bannerLine.style.height = 6;
            _bannerLine.style.width = 0;
            _bannerLine.style.marginTop = 2;
            Round(_bannerLine, 3);
            _bannerBox.Add(_bannerLine);
            _subBanner = Text("", _bold, 34, new Color(0.9f, 0.92f, 1f));
            _subBanner.style.unityTextAlign = TextAnchor.MiddleCenter;
            _subBanner.style.marginTop = 14;
            _bannerBox.Add(_subBanner);
        }

        private void BuildControls()
        {
            var bar = new VisualElement();
            bar.style.position = Position.Absolute;
            bar.style.left = 0;
            bar.style.right = 0;
            bar.style.top = 16;
            bar.style.alignItems = Align.Center;
            bar.pickingMode = PickingMode.Ignore;
            Root.Add(bar);
            _controls = Panel(float.NaN, float.NaN, new Color(0.3f, 0.85f, 1f, 0.35f));
            _controls.style.position = Position.Relative;
            var label = new Label(ControlsText) { enableRichText = true, pickingMode = PickingMode.Ignore };
            label.style.fontSize = 21;
            label.style.color = new Color(0.85f, 0.88f, 1f);
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            _controls.Add(label);
            bar.Add(_controls);
        }

        // ------------------------------------------------------------------ per frame

        private void LateUpdate()
        {
            if (_game == null || _speedFill == null) return;
            var sim = _game.Sim;
            float dt = Time.unscaledDeltaTime;
            UpdateSpeed(sim);
            UpdateHp(sim, dt);
            UpdateChain(sim, dt);
            UpdateWave(sim);
            UpdateBanner(sim, dt);
            bool showControls = sim.CurrentWave == 1 && sim.State != SimState.GameOver && sim.State != SimState.Victory;
            _controls.style.display = showControls ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateSpeed(RedlineSim sim)
        {
            var p = sim.Player;
            int tier = sim.PlayerTier;
            _speedFill.style.width = Length.Percent(CombatRules.SpeedFraction(p.Vx, sim.Config) * 100f);
            int half = Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(p.Vx) * 2f), 0, _speedTexts.Length - 1);
            if (half != _shownSpeed)
            {
                _shownSpeed = half;
                _speedValue.text = _speedTexts[half];
            }
            bool armor = tier == 3;
            _armorTag.style.opacity = armor ? 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 10f) : 0f;
            if (tier == _shownTier) return;
            _shownTier = tier;
            var c = RedlineGame.TierColors[tier - 1];
            _speedFill.style.backgroundColor = c;
            _speedValue.style.color = c;
            _tierBadge.text = "TIER " + tier;
            _tierBadge.style.backgroundColor = c;
            Border(_speedPanel, 2f, c);
        }

        private void UpdateHp(RedlineSim sim, float dt)
        {
            int hp = sim.Player.Hp;
            if (hp != _shownHp)
            {
                if (hp < _shownHp && _shownHp > 0)
                {
                    _hpFlash = 0.35f;
                    _hpFlashIndex = hp;
                }
                _shownHp = hp;
                for (int i = 0; i < _hpPips.Length; i++)
                {
                    _hpPips[i].style.backgroundColor = i < hp ? HpColor : new Color(0f, 0f, 0f, 0f);
                    _hpPips[i].style.scale = new StyleScale(new Scale(Vector2.one));
                }
            }
            if (_hpFlash > 0f && _hpFlashIndex >= 0 && _hpFlashIndex < _hpPips.Length)
            {
                _hpFlash -= dt;
                _hpPips[_hpFlashIndex].style.backgroundColor = _hpFlash > 0f ? Color.white : new Color(0f, 0f, 0f, 0f);
            }
            // Low HP: the remaining pips pulse.
            float pulse = hp <= 2 && hp > 0 ? 1f + 0.18f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 8f)) : 1f;
            for (int i = 0; i < hp && i < _hpPips.Length; i++)
                _hpPips[i].style.scale = new StyleScale(new Scale(new Vector2(pulse, pulse)));
        }

        private void UpdateChain(RedlineSim sim, float dt)
        {
            var p = sim.Player;
            int chain = p.Chain >= 2 ? p.Chain : 0;
            if (chain != _shownChain)
            {
                if (chain > _shownChain && chain >= 2) _chainPop = 0.25f;
                _shownChain = chain;
                _chainBox.style.display = chain >= 2 ? DisplayStyle.Flex : DisplayStyle.None;
                if (chain >= 2) _chainLabel.text = "CHAIN ×" + chain;
            }
            if (chain < 2) return;
            _chainPop = Mathf.Max(0f, _chainPop - dt);
            float s = 1f + _chainPop * 1.2f;
            _chainLabel.style.scale = new StyleScale(new Scale(new Vector2(s, s)));
            _chainTimer.style.width = Length.Percent(Mathf.Clamp01(p.ChainTimer / sim.Config.chainWindow) * 100f);
        }

        private void UpdateWave(RedlineSim sim)
        {
            if (sim.CurrentWave != _shownWave || sim.State == SimState.Victory)
            {
                _shownWave = sim.CurrentWave;
                _waveNumber.text = sim.CurrentWave.ToString();
                int cleared = sim.State == SimState.Victory ? sim.WaveCount : sim.CurrentWave - 1;
                for (int i = 0; i < _waveDots.Length; i++)
                {
                    _waveDots[i].style.backgroundColor = i < cleared ? Cyan : new Color(0f, 0f, 0f, 0f);
                    _waveDots[i].style.opacity = 1f;
                }
            }
            if (sim.EnemiesAlive != _shownEnemies)
            {
                _shownEnemies = sim.EnemiesAlive;
                _enemiesLabel.text = sim.EnemiesAlive.ToString();
            }
            // The current wave's dot breathes.
            int current = sim.CurrentWave - 1;
            if (sim.State != SimState.Victory && current >= 0 && current < _waveDots.Length)
                _waveDots[current].style.opacity = 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f));
        }

        private void UpdateBanner(RedlineSim sim, float dt)
        {
            bool stale = sim.State == SimState.WaveBreak && sim.CurrentWave != _shownBannerWave;
            if (sim.State != _shownState || stale) ShowBanner(sim);
            if (sim.State == SimState.WaveBreak)
            {
                int countdown = Mathf.Max(1, Mathf.CeilToInt(sim.BreakTimer));
                if (countdown != _shownCountdown)
                {
                    _shownCountdown = countdown;
                    _subBanner.text = (sim.CurrentWave == sim.WaveCount ? "FINAL WAVE" : "GET READY") + "  ·  " + countdown;
                }
            }
            _bannerAge += dt;
            float t = Mathf.Clamp01(_bannerAge / 0.25f);
            float ease = 1f - (1f - t) * (1f - t);
            float scale = 1f + 0.3f * (1f - ease);
            _banner.style.scale = new StyleScale(new Scale(new Vector2(scale, scale)));
            _bannerBox.style.opacity = Mathf.Clamp01(_bannerAge / 0.15f);
            _bannerLine.style.width = 420f * ease;
        }

        private void ShowBanner(RedlineSim sim)
        {
            _shownState = sim.State;
            _bannerAge = 0f;
            _shownCountdown = -1;
            switch (sim.State)
            {
                case SimState.WaveBreak:
                    _shownBannerWave = sim.CurrentWave;
                    SetBanner("WAVE " + sim.CurrentWave, Color.white, Cyan, "");
                    break;
                case SimState.Fighting:
                    SetBanner("", Color.white, Color.clear, "");
                    break;
                case SimState.GameOver:
                    SetBanner("GAME OVER", Danger, Danger,
                        "REACHED WAVE " + sim.CurrentWave + "  ·  " + sim.Kills + " KILLS\nPRESS R / START TO RETRY");
                    break;
                case SimState.Victory:
                    SetBanner("VICTORY", Gold, Gold,
                        "ALL " + sim.WaveCount + " WAVES CLEARED  ·  " + sim.Kills + " KILLS\nPRESS R / START TO PLAY AGAIN");
                    break;
            }
        }

        private void SetBanner(string title, Color color, Color line, string sub)
        {
            _banner.text = title;
            _banner.style.color = color;
            _bannerLine.style.backgroundColor = line;
            _subBanner.text = sub;
            _bannerBox.style.display = title.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnDestroy()
        {
            if (_settings != null) Destroy(_settings);
        }

        // ------------------------------------------------------------------ element helpers

        private VisualElement Panel(float left, float top, Color border)
        {
            var e = new VisualElement();
            e.style.position = Position.Absolute;
            if (!float.IsNaN(left)) e.style.left = left;
            if (!float.IsNaN(top)) e.style.top = top;
            e.style.backgroundColor = PanelColor;
            e.style.paddingLeft = 18;
            e.style.paddingRight = 18;
            e.style.paddingTop = 12;
            e.style.paddingBottom = 12;
            Round(e, 10);
            Border(e, 2f, border);
            e.pickingMode = PickingMode.Ignore;
            return e;
        }

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

        private static void Pill(Label label, Color background)
        {
            label.style.backgroundColor = background;
            label.style.paddingLeft = 12;
            label.style.paddingRight = 12;
            label.style.paddingTop = 2;
            label.style.paddingBottom = 2;
            Round(label, 6);
        }

        private static VisualElement Zone(float fromPercent, float toPercent, Color color)
        {
            var zone = new VisualElement();
            zone.style.position = Position.Absolute;
            zone.style.left = Length.Percent(fromPercent);
            zone.style.width = Length.Percent(toPercent - fromPercent);
            zone.style.top = 0;
            zone.style.bottom = 0;
            color.a = 0.14f;
            zone.style.backgroundColor = color;
            return zone;
        }

        private static VisualElement Mark(float percent)
        {
            var mark = new VisualElement();
            mark.style.position = Position.Absolute;
            mark.style.left = Length.Percent(percent);
            mark.style.top = 0;
            mark.style.bottom = 0;
            mark.style.width = 3;
            mark.style.backgroundColor = new Color(1f, 1f, 1f, 0.85f);
            return mark;
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
