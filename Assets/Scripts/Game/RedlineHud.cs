using Redline.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Redline.Game
{
    /// <summary>
    /// UI Toolkit HUD built from code, styled to match the neon arena: a speed panel (tier-coloured
    /// border, zoned meter, live speed, tier badge, HP pips), a chain badge with a draining timer,
    /// a wave panel (number + progress dots + enemies left) and an animated centre banner (the
    /// controls are taught by the training level, see TrainingOverlay). Text is only rewritten when its value changes, and the speed readout
    /// uses pre-built strings, so a steady frame allocates nothing.
    /// </summary>
    /// <remarks>Added by <see cref="RedlineGame"/>. Main-thread only. Font: Source Code Pro (SIL OFL, see Resources/Fonts).</remarks>
    public sealed class RedlineHud : MonoBehaviour
    {
        private static readonly Color PanelColor = new Color(0.03f, 0.03f, 0.09f, 0.82f);
        private static readonly Color Dim = new Color(0.62f, 0.68f, 0.9f);
        private static readonly Color Cyan = new Color(0.3f, 0.85f, 1f);
        private static readonly Color HpColor = new Color(1f, 0.3f, 0.45f);
        private static readonly Color EnemyColor = new Color(1f, 0.62f, 0.3f);
        private static readonly Color Gold = new Color(1f, 0.92f, 0.45f);
        private static readonly Color Danger = new Color(1f, 0.25f, 0.4f);
        /// <summary>Speed readout colour above maxSpeed (hook flings, air speed, speed buff).</summary>
        private static readonly Color Overspeed = new Color(1f, 0.45f, 0.18f);
        private const float ToastLife = 0.9f;
        private const float ToastRise = 60f;

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
        private VisualElement _hpRow;
        private VisualElement _overFill;
        private Label _overTag;
        private BuffChip _speedBuff;
        private BuffChip _hookBuff;
        private Label _levelLabel;
        private VisualElement _dotsRow;
        private Label _toast;
        private float _toastAge = 99f;
        private string[] _toastTexts;
        private float _overTop;
        private VisualElement _chainBox;
        private Label _chainLabel;
        private VisualElement _chainTimer;
        private Label _waveNumber;
        private Label _waveTotal;
        private VisualElement[] _waveDots;
        private Label _enemiesLabel;
        private VisualElement _bannerBox;
        /// <summary>The wave panel (level label, wave number and dots, foes); hidden in training.</summary>
        private VisualElement _wavePanel;
        private bool _shownTraining;
        private Label _banner;
        private VisualElement _bannerLine;
        private Label _subBanner;
        private VisualElement[] _markers;
        private const int MarkerCount = 12;
        private static readonly Color DroneMarker = new Color(0.78f, 0.45f, 1f);
        private static readonly Color BruteMarker = new Color(1f, 0.3f, 0.35f);

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
        private RedlineSim _shownSim;
        private bool _shownOver;
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
            BuildMarkers();
            BuildToast(cfg);

            // Readout range: up to the buffed air cap with headroom for hook flings (clamped beyond).
            _overTop = Mathf.Max(cfg.airMaxSpeed, cfg.maxSpeed) * cfg.orbSpeedMultiplier;
            _speedTexts = new string[Mathf.CeilToInt(_overTop * 1.25f * 2f) + 1];
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
            _speedPanel.Add(BuildOverspeedStrip());

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

            var buffs = Row(Justify.FlexStart);
            buffs.style.marginTop = 8;
            _speedBuff = new BuffChip(this, "SPEED", OrbRenderer.KindColor(OrbKind.Speed), cfg.orbSpeedDuration);
            _hookBuff = new BuffChip(this, "HOOK", OrbRenderer.KindColor(OrbKind.Hook), cfg.orbHookDuration);
            buffs.Add(_speedBuff.Root);
            buffs.Add(_hookBuff.Root);
            _speedPanel.Add(buffs);
        }

        /// <summary>A thin strip under the meter that fills from maxSpeed up to the buffed air cap.</summary>
        private VisualElement BuildOverspeedStrip()
        {
            var row = Row(Justify.FlexStart);
            row.style.marginTop = 4;
            var track = new VisualElement();
            track.style.height = 6;
            track.style.flexGrow = 1;
            track.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f);
            Round(track, 3);
            track.style.overflow = Overflow.Hidden;
            _overFill = new VisualElement();
            _overFill.style.height = Length.Percent(100);
            _overFill.style.width = Length.Percent(0);
            _overFill.style.backgroundColor = Overspeed;
            Round(_overFill, 3);
            track.Add(_overFill);
            row.Add(track);
            _overTag = Text("OVERSPEED", _bold, 16, Overspeed);
            _overTag.style.marginLeft = 10;
            _overTag.style.letterSpacing = 3;
            _overTag.style.opacity = 0f;
            row.Add(_overTag);
            return row;
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
            _hpRow = Row(Justify.FlexEnd);
            FillHpPips(cfg.playerMaxHp);
            return _hpRow;
        }

        /// <summary>(Re)creates one pip per max HP (max HP changes between runs with upgrades).</summary>
        private void FillHpPips(int maxHp)
        {
            var row = _hpRow;
            row.Clear();
            _hpPips = new VisualElement[maxHp];
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
        }

        private void BuildChainBadge()
        {
            _chainBox = new VisualElement();
            // Top centre, in the strip between the HUD panels and the arena.
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
            _wavePanel = panel;

            _levelLabel = Text("", _bold, 18, Dim);
            _levelLabel.style.letterSpacing = 3;
            panel.Add(_levelLabel);

            var top = Row(Justify.SpaceBetween);
            top.style.alignItems = Align.FlexEnd;
            var caption = Caption("WAVE");
            caption.style.marginBottom = 10;
            top.Add(caption);
            var number = Row(Justify.FlexEnd);
            number.style.alignItems = Align.FlexEnd;
            _waveNumber = Text("1", _black, 54, Color.white);
            number.Add(_waveNumber);
            _waveTotal = Text("", _bold, 28, Dim);
            _waveTotal.style.marginBottom = 8;
            number.Add(_waveTotal);
            top.Add(number);
            panel.Add(top);

            var bottom = Row(Justify.SpaceBetween);
            bottom.style.marginTop = 4;
            _dotsRow = Row(Justify.FlexStart);
            _waveDots = new VisualElement[0];
            bottom.Add(_dotsRow);
            var enemies = Row(Justify.FlexEnd);
            enemies.Add(Caption("FOES"));
            _enemiesLabel = Text("0", _black, 30, EnemyColor);
            _enemiesLabel.style.marginLeft = 10;
            enemies.Add(_enemiesLabel);
            bottom.Add(enemies);
            panel.Add(bottom);
        }

        /// <summary>
        /// Rebuilds the per-level parts (wave dots, wave total, level label) when a new level's
        /// sim replaces the old one. Allocates, but only on a level change.
        /// </summary>
        private void BindLevel(RedlineSim sim)
        {
            _shownSim = sim;
            if (_hpRow != null && (_hpPips == null || _hpPips.Length != sim.Config.playerMaxHp)) FillHpPips(sim.Config.playerMaxHp);
            _shownWave = -1;
            _shownEnemies = -1;
            _shownBannerWave = -1;
            _shownState = (SimState)(-1);
            _waveTotal.text = "/" + sim.WaveCount;
            _levelLabel.text = "LEVEL " + _game.LevelNumber + "/" + _game.LevelCount + "  ·  " + _game.LevelName.ToUpperInvariant();
            _dotsRow.Clear();
            _waveDots = new VisualElement[sim.WaveCount];
            for (int i = 0; i < _waveDots.Length; i++)
            {
                var dot = new VisualElement();
                dot.style.width = 16;
                dot.style.height = 16;
                dot.style.marginRight = 6;
                Round(dot, 8);
                Border(dot, 2f, Cyan);
                _dotsRow.Add(dot);
                _waveDots[i] = dot;
            }
        }

        /// <summary>A short label that rises from the player and fades after an orb pickup.</summary>
        private void BuildToast(RedlineConfig cfg)
        {
            _toastTexts = new string[3];
            _toastTexts[(int)OrbKind.Health] = "+" + cfg.orbHealAmount + " HP";
            _toastTexts[(int)OrbKind.Speed] = "SPEED";
            _toastTexts[(int)OrbKind.Hook] = "HOOK";
            _toast = Text("", _black, 34, Color.white);
            _toast.style.position = Position.Absolute;
            _toast.style.width = 300;
            _toast.style.unityTextAlign = TextAnchor.MiddleCenter;
            _toast.style.letterSpacing = 4;
            _toast.style.display = DisplayStyle.None;
            Root.Add(_toast);
        }

        /// <summary>Shows the pickup text for an orb kind above the player ("+1 HP", "SPEED", "HOOK").</summary>
        public void ShowPickup(OrbKind kind)
        {
            if (_toast == null) return;
            _toast.text = _toastTexts[(int)kind];
            _toast.style.color = OrbRenderer.KindColor(kind);
            _toast.style.display = DisplayStyle.Flex;
            _toastAge = 0f;
        }

        private void UpdateToast(RedlineSim sim, float dt)
        {
            if (_toastAge >= ToastLife) return;
            _toastAge += dt;
            if (_toastAge >= ToastLife)
            {
                _toast.style.display = DisplayStyle.None;
                return;
            }
            var cam = _game.Camera;
            float w = Root.resolvedStyle.width;
            float h = Root.resolvedStyle.height;
            if (cam == null || float.IsNaN(w) || w <= 0f) return;
            var p = sim.Player;
            var vp = cam.WorldToViewportPoint(_game.ToDraw(new Vector2(p.X, p.MaxY + 0.6f)));
            float t = _toastAge / ToastLife;
            _toast.style.left = vp.x * w - 150f;
            _toast.style.top = (1f - vp.y) * h - 24f - ToastRise * (1f - (1f - t) * (1f - t));
            _toast.style.opacity = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
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

        /// <summary>Edge markers pointing at enemies outside the view (the camera follows the player).</summary>
        private void BuildMarkers()
        {
            _markers = new VisualElement[MarkerCount];
            for (int i = 0; i < MarkerCount; i++)
            {
                var m = new VisualElement { pickingMode = PickingMode.Ignore };
                m.style.position = Position.Absolute;
                m.style.width = 20;
                m.style.height = 20;
                m.style.rotate = new StyleRotate(new Rotate(45f));
                Border(m, 2f, Color.white);
                m.style.display = DisplayStyle.None;
                Root.Add(m);
                _markers[i] = m;
            }
        }

        private void UpdateMarkers(RedlineSim sim)
        {
            var cam = _game.Camera;
            float w = Root.resolvedStyle.width;
            float h = Root.resolvedStyle.height;
            int used = 0;
            if (cam != null && !float.IsNaN(w) && w > 0f && (sim.State == SimState.Fighting || sim.State == SimState.Training))
            {
                foreach (var e in sim.Enemies)
                {
                    if (used >= MarkerCount) break;
                    if (!e.Alive) continue;
                    var vp = cam.WorldToViewportPoint(_game.ToDraw(new Vector2(e.Center.X, e.Center.Y)));
                    if (vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f) continue;
                    var m = _markers[used++];
                    m.style.display = DisplayStyle.Flex;
                    m.style.left = Mathf.Clamp(vp.x, 0.02f, 0.97f) * w - 10f;
                    m.style.top = (1f - Mathf.Clamp(vp.y, 0.06f, 0.78f)) * h - 10f;
                    var c = e.Kind == EnemyKind.Brute ? BruteMarker : e.Kind == EnemyKind.Drone ? DroneMarker : EnemyColor;
                    m.style.backgroundColor = c;
                }
            }
            for (int i = used; i < MarkerCount; i++) _markers[i].style.display = DisplayStyle.None;
        }

        // ------------------------------------------------------------------ per frame

        private void LateUpdate()
        {
            if (_game == null || _speedFill == null) return;
            var sim = _game.Sim;
            float dt = Time.unscaledDeltaTime;
            if (sim != _shownSim) BindLevel(sim);
            UpdateSpeed(sim);
            _speedBuff.Update(sim.Player.SpeedBuffTimer);
            _hookBuff.Update(sim.Player.HookBuffTimer);
            UpdateToast(sim, dt);
            UpdateHp(sim, dt);
            UpdateChain(sim, dt);
            // Training has no waves: no wave panel and no wave banners (its own overlay draws the steps).
            bool training = sim.State == SimState.Training;
            if (training != _shownTraining)
            {
                _shownTraining = training;
                _wavePanel.style.display = training ? DisplayStyle.None : DisplayStyle.Flex;
                if (training) SetBanner("", Color.white, Color.clear, "");
            }
            if (!training)
            {
                UpdateWave(sim);
                UpdateBanner(sim, dt);
            }
            UpdateMarkers(sim);
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

            float speed = Mathf.Abs(p.Vx);
            float cap = sim.Config.maxSpeed;
            bool over = speed > cap + 0.05f;
            _overFill.style.width = Length.Percent(over ? Mathf.Clamp01((speed - cap) / (_overTop - cap)) * 100f : 0f);
            _overTag.style.opacity = over ? 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 14f) : 0f;
            if (over != _shownOver)
            {
                _shownOver = over;
                var vc = over ? Overspeed : RedlineGame.TierColors[tier - 1];
                _speedValue.style.color = vc;
                _speedFill.style.backgroundColor = vc;
            }

            if (tier == _shownTier) return;
            _shownTier = tier;
            var c = RedlineGame.TierColors[tier - 1];
            _speedFill.style.backgroundColor = over ? Overspeed : c;
            _speedValue.style.color = over ? Overspeed : c;
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
                        "REACHED WAVE " + sim.CurrentWave + "  ·  " + sim.Kills + " KILLS\nPRESS " + ControlLabels.Get(GameAction.Restart) + " / SELECT FOR UPGRADES");
                    break;
                case SimState.Victory:
                    string next = _game.NextLevelName;
                    if (next != null)
                        SetBanner("LEVEL CLEAR", Gold, Gold,
                            "NEXT: " + next.ToUpperInvariant() + "  ·  " + sim.Kills + " KILLS");
                    else
                        SetBanner("VICTORY", Gold, Gold,
                            "ALL " + _game.LevelCount + " LEVELS CLEARED  ·  " + sim.Kills + " KILLS\nPRESS " + ControlLabels.Get(GameAction.Restart) + " / SELECT FOR UPGRADES");
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

        // ------------------------------------------------------------------ buff chip

        /// <summary>
        /// A small "NAME [bar] 6s" chip for a timed orb buff, hidden while the buff is off. The
        /// seconds text comes from a pre-built table, so updating it allocates nothing.
        /// </summary>
        private sealed class BuffChip
        {
            private static readonly string[] Seconds = BuildSeconds();
            private readonly VisualElement _fill;
            private readonly Label _secs;
            private readonly float _duration;
            private int _shownSecs = -1;
            private bool _shown = true;

            /// <summary>The chip's element (add it to a row).</summary>
            public readonly VisualElement Root;

            /// <summary>Builds a chip for a buff lasting <paramref name="duration"/> seconds.</summary>
            public BuffChip(RedlineHud hud, string name, Color color, float duration)
            {
                _duration = Mathf.Max(0.01f, duration);
                Root = Row(Justify.FlexStart);
                Root.style.marginRight = 18;
                var tag = Text(name, hud._black, 18, Color.black);
                Pill(tag, color);
                Root.Add(tag);
                var bar = new VisualElement();
                bar.style.width = 110;
                bar.style.height = 8;
                bar.style.marginLeft = 8;
                bar.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
                Round(bar, 4);
                _fill = new VisualElement();
                _fill.style.height = Length.Percent(100);
                _fill.style.backgroundColor = color;
                Round(_fill, 4);
                bar.Add(_fill);
                Root.Add(bar);
                _secs = Text("", hud._bold, 18, color);
                _secs.style.marginLeft = 8;
                _secs.style.width = 34;
                Root.Add(_secs);
                Update(0f);
            }

            /// <summary>Shows the remaining time (hidden at 0); the last 1.5 s blink.</summary>
            public void Update(float timeLeft)
            {
                bool on = timeLeft > 0f;
                if (on != _shown)
                {
                    _shown = on;
                    Root.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                }
                if (!on) return;
                _fill.style.width = Length.Percent(Mathf.Clamp01(timeLeft / _duration) * 100f);
                int secs = Mathf.Clamp(Mathf.CeilToInt(timeLeft), 0, Seconds.Length - 1);
                if (secs != _shownSecs)
                {
                    _shownSecs = secs;
                    _secs.text = Seconds[secs];
                }
                Root.style.opacity = timeLeft < 1.5f ? 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 9f)) : 1f;
            }

            private static string[] BuildSeconds()
            {
                var s = new string[100];
                for (int i = 0; i < s.Length; i++) s[i] = i + "s";
                return s;
            }
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
