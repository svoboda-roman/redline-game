using Redline.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Redline.Game
{
    /// <summary>
    /// UI Toolkit HUD built from code: speed meter with tier markers, HP pips, wave and enemy
    /// counters, the wave / GAME OVER / VICTORY banner, and the controls hint.
    /// Labels are only rewritten when their value changes, so a steady frame allocates nothing.
    /// </summary>
    /// <remarks>Added by <see cref="RedlineGame"/>. Main-thread only.</remarks>
    public sealed class RedlineHud : MonoBehaviour
    {
        private const string ControlsText =
            "A/D move · Space jump · J attack · hold K hook · S slide · R restart\n" +
            "Pad: stick · A jump · X attack · hold RB hook · B slide · Start restart";

        private RedlineGame _game;
        private PanelSettings _settings;
        private int _shownBannerWave = -1;
        private VisualElement _speedFill;
        private Label _tierLabel;
        private Label _waveLabel;
        private Label _enemiesLabel;
        private Label _banner;
        private Label _subBanner;
        private Label _controls;
        private VisualElement[] _hpPips;

        private int _shownTier = -1;
        private int _shownHp = -1;
        private int _shownWave = -1;
        private int _shownEnemies = -1;
        private SimState _shownState = (SimState)(-1);

        /// <summary>The root element, exposed for screenshot tooling.</summary>
        public VisualElement Root { get; private set; }

        /// <summary>Builds the HUD for a game.</summary>
        public void Init(RedlineGame game)
        {
            if (_game != null) return; // already built
            _game = game;
            var cfg = game.Sim.Config;

            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings = settings;
            settings.name = "RedlinePanelSettings";
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("RedlineTheme");
            if (settings.themeStyleSheet == null)
                Debug.LogWarning("Redline: Resources/RedlineTheme.tss not found; the HUD will use unstyled defaults.");
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;

            var host = new GameObject("HUD");
            host.transform.SetParent(transform, false);
            host.SetActive(false);
            var doc = host.AddComponent<UIDocument>();
            doc.panelSettings = settings;
            host.SetActive(true);

            Root = doc.rootVisualElement;
            Root.pickingMode = PickingMode.Ignore;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) Root.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(font));
            Root.style.color = Color.white;

            // Speed meter (top left)
            var meterBox = Absolute(new VisualElement(), left: 28, top: 20);
            Backing(meterBox);
            Root.Add(meterBox);
            var meterTitle = MakeLabel("SPEED", 26, new Color(0.8f, 0.85f, 1f));
            meterTitle.style.marginBottom = 8;
            meterBox.Add(meterTitle);
            var meter = new VisualElement();
            meter.style.width = 520;
            meter.style.height = 28;
            meter.style.backgroundColor = new Color(0.08f, 0.09f, 0.16f, 0.9f);
            Border(meter, 2, new Color(0.3f, 0.35f, 0.55f));
            meterBox.Add(meter);
            _speedFill = new VisualElement();
            _speedFill.style.height = Length.Percent(100);
            _speedFill.style.width = Length.Percent(0);
            meter.Add(_speedFill);
            AddTierMark(meter, cfg.tier2Speed / cfg.maxSpeed, "T2");
            AddTierMark(meter, cfg.tier3Speed / cfg.maxSpeed, "T3");
            _tierLabel = MakeLabel("TIER 1", 34, RedlineGame.TierColors[0]);
            _tierLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            // Tier label and HP pips share one row, so the panel stays above the arena.
            var statusRow = new VisualElement();
            statusRow.style.flexDirection = FlexDirection.Row;
            statusRow.style.alignItems = Align.Center;
            statusRow.style.marginTop = 6;
            meterBox.Add(statusRow);
            statusRow.Add(_tierLabel);

            var hpRow = new VisualElement();
            hpRow.style.flexDirection = FlexDirection.Row;
            hpRow.style.marginLeft = 24;
            statusRow.Add(hpRow);
            _hpPips = new VisualElement[cfg.playerMaxHp];
            for (int i = 0; i < _hpPips.Length; i++)
            {
                var pip = new VisualElement();
                pip.style.width = 26;
                pip.style.height = 26;
                pip.style.marginRight = 6;
                Border(pip, 2, new Color(1f, 0.3f, 0.45f));
                hpRow.Add(pip);
                _hpPips[i] = pip;
            }

            // Wave and enemies (top right)
            var right = Absolute(new VisualElement(), top: 20);
            right.style.right = 28;
            right.style.alignItems = Align.FlexEnd;
            Backing(right);
            Root.Add(right);
            _waveLabel = MakeLabel("WAVE 1/5", 40, Color.white);
            _waveLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            right.Add(_waveLabel);
            _enemiesLabel = MakeLabel("ENEMIES 0", 30, new Color(1f, 0.72f, 0.45f));
            right.Add(_enemiesLabel);

            // Centre banner
            var centre = Absolute(new VisualElement(), left: 0, top: 0);
            centre.style.right = 0;
            centre.style.bottom = 0;
            // Sit in the open band above the arena's platforms, not over the centre platform.
            centre.style.justifyContent = Justify.FlexStart;
            centre.style.paddingTop = 175;
            centre.style.alignItems = Align.Center;
            centre.pickingMode = PickingMode.Ignore;
            Root.Add(centre);
            _banner = MakeLabel("", 110, Color.white);
            _banner.style.unityFontStyleAndWeight = FontStyle.Bold;
            _banner.style.unityTextAlign = TextAnchor.MiddleCenter;
            centre.Add(_banner);
            _subBanner = MakeLabel("", 36, new Color(0.9f, 0.92f, 1f));
            _subBanner.style.unityTextAlign = TextAnchor.MiddleCenter;
            centre.Add(_subBanner);

            // Controls hint (top centre, between the two panels, so it never covers the player)
            var hintBar = Absolute(new VisualElement(), left: 0, top: 20);
            hintBar.style.right = 0;
            hintBar.style.alignItems = Align.Center;
            Root.Add(hintBar);
            _controls = MakeLabel(ControlsText, 24, new Color(0.85f, 0.88f, 1f));
            _controls.style.unityTextAlign = TextAnchor.MiddleCenter;
            _controls.style.backgroundColor = new Color(0f, 0f, 0f, 0.45f);
            _controls.style.paddingLeft = 16;
            _controls.style.paddingRight = 16;
            _controls.style.paddingTop = 8;
            _controls.style.paddingBottom = 8;
            hintBar.Add(_controls);
        }

        private void LateUpdate()
        {
            if (_game == null || _speedFill == null) return;
            var sim = _game.Sim;
            var p = sim.Player;
            int tier = sim.PlayerTier;

            _speedFill.style.width = Length.Percent(CombatRules.SpeedFraction(p.Vx, sim.Config) * 100f);
            if (tier != _shownTier)
            {
                _shownTier = tier;
                var c = RedlineGame.TierColors[tier - 1];
                _speedFill.style.backgroundColor = c;
                _tierLabel.text = tier == 3 ? "TIER 3  ·  BREAKS ARMOR" : "TIER " + tier;
                _tierLabel.style.color = c;
            }

            if (p.Hp != _shownHp)
            {
                _shownHp = p.Hp;
                for (int i = 0; i < _hpPips.Length; i++)
                    _hpPips[i].style.backgroundColor = i < p.Hp ? new Color(1f, 0.3f, 0.45f) : new Color(0f, 0f, 0f, 0f);
            }

            if (sim.CurrentWave != _shownWave)
            {
                _shownWave = sim.CurrentWave;
                _waveLabel.text = "WAVE " + sim.CurrentWave + "/" + sim.WaveCount;
            }

            if (sim.EnemiesAlive != _shownEnemies)
            {
                _shownEnemies = sim.EnemiesAlive;
                _enemiesLabel.text = "ENEMIES " + sim.EnemiesAlive;
            }

            bool bannerStale = sim.State == SimState.WaveBreak && sim.CurrentWave != _shownBannerWave;
            if (sim.State != _shownState || bannerStale)
            {
                bool changed = sim.State != _shownState || bannerStale;
                _shownState = sim.State;
                switch (sim.State)
                {
                    case SimState.WaveBreak:
                        if (changed)
                        {
                            _shownBannerWave = sim.CurrentWave;
                            _banner.text = "WAVE " + sim.CurrentWave;
                            _banner.style.color = Color.white;
                            _subBanner.text = sim.CurrentWave == sim.WaveCount ? "final wave" : "get ready";
                        }
                        break;
                    case SimState.Fighting:
                        _banner.text = "";
                        _subBanner.text = "";
                        break;
                    case SimState.GameOver:
                        _banner.text = "GAME OVER";
                        _banner.style.color = new Color(1f, 0.25f, 0.4f);
                        _subBanner.text = "Reached wave " + sim.CurrentWave + " · " + sim.Kills + " kills\nPress R / Start to retry";
                        break;
                    case SimState.Victory:
                        _banner.text = "VICTORY";
                        _banner.style.color = RedlineGame.TierColors[2];
                        _subBanner.text = "All " + sim.WaveCount + " waves cleared · " + sim.Kills + " kills\nPress R / Start to play again";
                        break;
                }
            }

            bool showControls = sim.CurrentWave == 1 && sim.State != SimState.GameOver && sim.State != SimState.Victory;
            _controls.style.display = showControls ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnDestroy()
        {
            if (_settings != null) Destroy(_settings);
        }

        /// <summary>Translucent panel behind HUD text so it stays readable over the arena.</summary>
        private static void Backing(VisualElement e)
        {
            e.style.backgroundColor = new Color(0.02f, 0.02f, 0.06f, 0.72f);
            e.style.paddingLeft = 14;
            e.style.paddingRight = 14;
            e.style.paddingTop = 8;
            e.style.paddingBottom = 10;
            e.style.borderTopLeftRadius = 6;
            e.style.borderTopRightRadius = 6;
            e.style.borderBottomLeftRadius = 6;
            e.style.borderBottomRightRadius = 6;
        }

        private static Label MakeLabel(string text, int size, Color color)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.color = color;
            label.pickingMode = PickingMode.Ignore;
            return label;
        }

        private static VisualElement Absolute(VisualElement e, float left = float.NaN, float top = float.NaN)
        {
            e.style.position = Position.Absolute;
            if (!float.IsNaN(left)) e.style.left = left;
            if (!float.IsNaN(top)) e.style.top = top;
            e.pickingMode = PickingMode.Ignore;
            return e;
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

        private static void AddTierMark(VisualElement meter, float fraction, string text)
        {
            var mark = new VisualElement();
            mark.style.position = Position.Absolute;
            mark.style.left = Length.Percent(Mathf.Clamp01(fraction) * 100f);
            mark.style.top = 0;
            mark.style.bottom = 0;
            mark.style.width = 3;
            mark.style.backgroundColor = Color.white;
            meter.Add(mark);
            var label = MakeLabel(text, 24, Color.white);
            label.style.position = Position.Absolute;
            label.style.left = Length.Percent(Mathf.Clamp01(fraction) * 100f);
            label.style.top = -30;
            meter.Add(label);
        }
    }
}
