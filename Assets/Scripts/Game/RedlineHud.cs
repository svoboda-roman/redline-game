using System.Collections.Generic;
using Redline.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Redline.Game
{
    /// <summary>
    /// The Replaced-style instrument-cluster HUD (design/ux/hud.md): thin orange line glyphs and
    /// pixel-terminal text sitting directly on the picture. No panels, brackets, fills or shadows.
    /// Everything is anchored to <see cref="LetterboxInfo.PictureRect"/> in HUD units (T = picture
    /// width / 480, LetterboxInfo.Texel): HP diamonds top-left, level/wave/foes top-right, chain + speed meter + tier +
    /// buffs bottom-left, banners centred, pickup toasts following the player. Text is rewritten only
    /// when its value changes (cached string tables) and styles only when they change, so a steady
    /// frame allocates nothing. Honours Reduced Motion and HUD Text Scale from <see cref="GameSettings"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// _hud = gameObject.AddComponent&lt;RedlineHud&gt;();
    /// _hud.Init(this);
    /// _hud.ShowPickup(OrbKind.Health);
    /// _hud.SetPaused(true);   // dim to 40 %
    /// </code>
    /// </example>
    /// <remarks>Added by <see cref="RedlineGame"/>. Main-thread only. Font: Departure Mono (SIL OFL 1.1, see Resources/Fonts).</remarks>
    public sealed class RedlineHud : MonoBehaviour
    {
        private const float M = 8f;          // safe margin in T
        private const float LevelTagSeconds = 3f;
        private const float ToastLife = 1.0f;
        private const float BuffBlinkBelow = 2f;
        private const int MarkerCount = 12;

        private RedlineGame _game;
        private PanelSettings _settings;
        private RedlineSim _shownSim;

        // layout state
        private VisualElement _pic;
        private float _texel;
        private int _scalePct;
        private Rect _picShown;
        private float _kShown;
        private readonly List<Label> _labels = new List<Label>(32);
        private readonly List<int> _labelT = new List<int>(32);
        private readonly List<System.Action> _relayout = new List<System.Action>(16);

        // zones
        private VisualElement _tl, _tr, _bl;

        // HP
        private VisualElement _hpRow;
        private VisualElement[] _hpCells, _hpDots;
        private Label _hpLow;
        private int _shownHp = -1;
        private int _hpFlashIndex = -1;
        private int _hpFlashUntilFrame;
        private bool _hpBlinkOn = true;

        // right column
        private Label _levelTag, _waveValue, _foesValue, _waveCaption, _foesCaption;
        private VisualElement _waveRow, _foesRow;
        private float _levelAge = 99f;
        private int _shownLevelStep = -1;
        private int _shownWave = -1, _shownEnemies = -1;
        private int _foesFlashUntilFrame;

        // speed block
        private VisualElement _meterBox, _meterLine, _meterFill, _tick2, _tick3, _overRow, _overLine, _blade;
        private Label _tierNumeral, _speedValue, _overTag;
        private VisualElement[] _pips;
        private string[] _speedTexts;
        private float _overTop;
        private float _speedTextClock;
        private int _shownSpeed = -1, _shownTier = -1, _shownFillPx = -1, _shownOverPx = -1;
        private bool _shownOver;
        private int _tierFlashUntilFrame;
        private float _t2Frac, _t3Frac;

        // chain
        private VisualElement _chainBox, _chainDecay;
        private Label _chainLabel;
        private int _shownChain = -1, _shownDecayPx = -1;
        private int _chainFlashUntilFrame;
        private static readonly string[] ChainTexts = BuildNumbers("x", "", 100);

        // buffs
        private BuffChip _speedBuff, _hookBuff;
        private VisualElement _buffRow;

        // banner
        private VisualElement _bannerBox;
        private Label _banner, _subBanner, _prompt;
        private SimState _shownState = (SimState)(-1);
        private int _shownCountdown = -1, _shownBannerWave = -1;
        private bool _shownTraining;

        // toasts
        private Label[] _toasts;
        private float[] _toastAge;
        private string[] _toastTexts;
        private VisualElement[] _markers;

        /// <summary>The root element, exposed for screenshot tooling.</summary>
        public VisualElement Root { get; private set; }

        /// <summary>Builds the HUD for a game.</summary>
        public void Init(RedlineGame game)
        {
            if (_game != null) return;
            _game = game;
            var cfg = game.Sim.Config;
            BuildDocument();
            BuildZones();
            BuildHp(cfg);
            BuildRightColumn();
            BuildBottomLeft(cfg);
            BuildBanner();
            BuildToasts(cfg);
            BuildMarkers();

            _overTop = Mathf.Max(cfg.airMaxSpeed, cfg.maxSpeed) * cfg.orbSpeedMultiplier;
            _speedTexts = new string[Mathf.CeilToInt(_overTop * 1.25f * 2f) + 1];
            for (int i = 0; i < _speedTexts.Length; i++) _speedTexts[i] = (i * 0.5f).ToString("0.0");
        }

        private bool _paused;

        /// <summary>Dims the whole HUD to 40 % while the game is paused (hud.md section 5); toast and level-tag ages freeze.</summary>
        public void SetPaused(bool paused)
        {
            _paused = paused;
            if (Root != null) Root.style.opacity = paused ? 0.4f : 1f;
        }

        // ------------------------------------------------------------------ building

        private void BuildDocument()
        {
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.name = "RedlinePanelSettings";
            _settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("RedlineTheme");
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
            Root.style.color = UiTheme.Orange;
            Root.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(UiTheme.Font));
        }

        private void BuildZones()
        {
            _pic = Abs(Root);
            _tl = Abs(_pic);
            _tr = Abs(_pic);
            _tr.style.alignItems = Align.FlexEnd;
            _bl = Abs(_pic);
            _bl.style.alignItems = Align.FlexStart;
            _relayout.Add(() =>
            {
                float m = M * _texel;
                _tl.style.left = m; _tl.style.top = m;
                _tr.style.right = m; _tr.style.top = m;
                _bl.style.left = m; _bl.style.bottom = m;
            });
        }

        private void BuildHp(RedlineConfig cfg)
        {
            _hpRow = new VisualElement { pickingMode = PickingMode.Ignore };
            _hpRow.style.flexDirection = FlexDirection.Row;
            _hpRow.style.flexWrap = Wrap.Wrap;
            _hpRow.style.alignItems = Align.Center;
            _tl.Add(_hpRow);
            _hpLow = Lbl("1", 12, UiTheme.Crimson);
            _hpLow.style.display = DisplayStyle.None;
            _hpRow.Add(_hpLow);
            FillHp(cfg.playerMaxHp);
            _relayout.Add(LayoutHp);
        }

        /// <summary>(Re)creates the diamonds; max HP changes between runs with upgrades.</summary>
        private void FillHp(int maxHp)
        {
            if (_hpCells != null) for (int i = 0; i < _hpCells.Length; i++) _hpCells[i].RemoveFromHierarchy();
            _hpCells = new VisualElement[maxHp];
            _hpDots = new VisualElement[maxHp];
            for (int i = 0; i < maxHp; i++)
            {
                var cell = new VisualElement { pickingMode = PickingMode.Ignore };
                var dia = Abs(cell);
                dia.style.rotate = new StyleRotate(new Rotate(45f));
                var dot = Abs(cell);
                cell.userData = dia;
                _hpRow.Insert(i, cell);
                _hpCells[i] = cell;
                _hpDots[i] = dot;
            }
            _hpLow.BringToFront();
            _shownHp = -1;
            if (_texel > 0f) LayoutHp();
        }

        private void LayoutHp()
        {
            float t = _texel;
            float cellSize = 8f * t, side = Mathf.Round(5.6f * t), dot = 2f * t;
            _hpRow.style.width = 10f * (cellSize + 3f * t) + 14f * t; // wraps after 10 diamonds
            for (int i = 0; i < _hpCells.Length; i++)
            {
                var c = _hpCells[i];
                c.style.width = cellSize; c.style.height = cellSize;
                c.style.marginRight = 3f * t; c.style.marginBottom = 3f * t;
                var dia = (VisualElement)c.userData;
                dia.style.left = Mathf.Round((cellSize - side) * 0.5f); dia.style.top = dia.style.left;
                dia.style.width = side; dia.style.height = side;
                Border(dia, t);
                var d = _hpDots[i];
                d.style.width = dot; d.style.height = dot;
                d.style.left = Mathf.Round((cellSize - dot) * 0.5f); d.style.top = d.style.left;
            }
            _hpLow.style.marginLeft = 2f * t;
            _shownHp = -1; // restyle
        }

        private void BuildRightColumn()
        {
            _levelTag = Lbl("", 8, UiTheme.Dim);
            _tr.Add(_levelTag);
            _waveRow = RowOf(_tr, Justify.FlexEnd);
            _waveCaption = Lbl("WAVE", 8, UiTheme.Orange);
            _waveRow.Add(_waveCaption);
            _waveValue = Lbl("1/1", 12, UiTheme.Bone);
            _waveRow.Add(_waveValue);
            _foesRow = RowOf(_tr, Justify.FlexEnd);
            _foesCaption = Lbl("FOES", 8, UiTheme.Orange);
            _foesRow.Add(_foesCaption);
            _relayout.Add(() =>
            {
                _waveCaption.style.marginRight = 3f * _texel;
                _foesCaption.style.marginRight = 3f * _texel;
                _foesRow.style.marginTop = _texel;
            });
            _foesValue = Lbl("0", 12, UiTheme.Bone);
            _foesRow.Add(_foesValue);
        }

        private void BindLevel(RedlineSim sim)
        {
            _shownSim = sim;
            if (_hpCells == null || _hpCells.Length != sim.Config.playerMaxHp) FillHp(sim.Config.playerMaxHp);
            _shownWave = -1; _shownEnemies = -1; _shownBannerWave = -1;
            _shownState = (SimState)(-1);
            _shownHp = -1;
            _levelAge = 0f; _shownLevelStep = -1;
            _levelTag.text = "LEVEL " + _game.LevelNumber + "/" + _game.LevelCount + " " + _game.LevelName.ToUpperInvariant();
            _t2Frac = Mathf.Clamp01(sim.Config.tier2Speed / sim.Config.maxSpeed);
            _t3Frac = Mathf.Clamp01(sim.Config.tier3Speed / sim.Config.maxSpeed);
            _tick2.style.left = Length.Percent(_t2Frac * 100f);
            _tick3.style.left = Length.Percent(_t3Frac * 100f);
        }

        private void BuildBottomLeft(RedlineConfig cfg)
        {
            // chain on top
            _chainBox = new VisualElement { pickingMode = PickingMode.Ignore };
            _chainLabel = Lbl("", 12, UiTheme.Bone);
            _chainBox.Add(_chainLabel);
            _chainDecay = new VisualElement { pickingMode = PickingMode.Ignore };
            _chainDecay.style.backgroundColor = UiTheme.Orange;
            _chainBox.Add(_chainDecay);
            _chainBox.style.display = DisplayStyle.None;
            _bl.Add(_chainBox);

            // meter row: tier numeral, meter, pips
            var meterRow = RowOf(_bl, Justify.FlexStart);
            _tierNumeral = Lbl("1", 12, UiTheme.Tier[0]);
            meterRow.Add(_tierNumeral);
            _blade = new VisualElement { pickingMode = PickingMode.Ignore };
            _blade.style.backgroundColor = UiTheme.Tier[2];
            _blade.style.rotate = new StyleRotate(new Rotate(45f));
            _blade.style.display = DisplayStyle.None;
            meterRow.Add(_blade);
            _meterBox = new VisualElement { pickingMode = PickingMode.Ignore };
            meterRow.Add(_meterBox);
            _meterLine = Abs(_meterBox);
            _meterLine.style.backgroundColor = UiTheme.WithAlpha(UiTheme.Orange, 0.3f);
            _meterFill = Abs(_meterBox);
            _meterFill.style.backgroundColor = UiTheme.Tier[0];
            _tick2 = Abs(_meterBox); _tick2.style.backgroundColor = UiTheme.Orange;
            _tick3 = Abs(_meterBox); _tick3.style.backgroundColor = UiTheme.Orange;
            var pipRow = RowOf(meterRow, Justify.FlexStart);
            _pips = new VisualElement[3];
            for (int i = 0; i < 3; i++)
            {
                var p = new VisualElement { pickingMode = PickingMode.Ignore };
                pipRow.Add(p);
                _pips[i] = p;
            }
            _pipRow = pipRow;

            // overspeed strip under the meter
            _overRow = RowOf(_bl, Justify.FlexStart);
            _overLine = new VisualElement { pickingMode = PickingMode.Ignore };
            _overLine.style.backgroundColor = UiTheme.Orange;
            _overRow.Add(_overLine);
            _overTag = Lbl("OVER", 8, UiTheme.Orange);
            _overRow.Add(_overTag);
            _overRow.style.display = DisplayStyle.None;

            // value + buffs
            _buffRow = RowOf(_bl, Justify.FlexStart);
            _speedValue = Lbl("0.0", 12, UiTheme.Bone);
            _buffRow.Add(_speedValue);
            _speedBuff = new BuffChip(this, BuffGlyph.Chevron, cfg.orbSpeedDuration);
            _hookBuff = new BuffChip(this, BuffGlyph.Ring, cfg.orbHookDuration);
            _buffRow.Add(_speedBuff.Root);
            _buffRow.Add(_hookBuff.Root);

            _relayout.Add(LayoutBottomLeft);
        }

        private VisualElement _pipRow;

        private void LayoutBottomLeft()
        {
            float t = _texel;
            _chainBox.style.marginBottom = 2f * t;
            _chainDecay.style.height = t;
            _chainDecay.style.width = 24f * t;
            _tierNumeral.style.marginRight = 3f * t;
            _tierNumeral.style.minWidth = 8f * t;
            _meterBox.style.width = 80f * t;
            _meterBox.style.height = 6f * t;
            _meterLine.style.left = 0; _meterLine.style.right = 0;
            _meterLine.style.top = 2f * t; _meterLine.style.height = 2f * t;
            _meterFill.style.left = 0; _meterFill.style.top = 2f * t; _meterFill.style.height = 2f * t;
            _shownFillPx = -1;
            _tick2.style.top = t; _tick2.style.height = 4f * t; _tick2.style.width = t;
            _tick3.style.top = t; _tick3.style.height = 4f * t; _tick3.style.width = t;
            _blade.style.width = t; _blade.style.height = 6f * t;
            _blade.style.marginRight = 3f * t; _blade.style.marginLeft = 2f * t;
            _pipRow.style.marginLeft = 3f * t;
            for (int i = 0; i < 3; i++)
            {
                _pips[i].style.width = 4f * t; _pips[i].style.height = 4f * t;
                _pips[i].style.marginRight = 2f * t;
                Border(_pips[i], t);
            }
            _shownTier = -1;
            _overRow.style.marginTop = t;
            _overLine.style.height = t;
            _overTag.style.marginLeft = 2f * t;
            _shownOverPx = -1;
            _buffRow.style.marginTop = 2f * t;
            _speedBuff.Layout(t);
            _hookBuff.Layout(t);
        }

        private void BuildBanner()
        {
            _bannerBox = new VisualElement { pickingMode = PickingMode.Ignore };
            _bannerBox.style.position = Position.Absolute;
            _bannerBox.style.left = 0; _bannerBox.style.right = 0;
            _bannerBox.style.alignItems = Align.Center;
            _pic.Add(_bannerBox);
            _banner = Lbl("", 16, UiTheme.Bone);
            _banner.style.unityTextAlign = TextAnchor.MiddleCenter;
            _bannerBox.Add(_banner);
            _subBanner = Lbl("", 8, UiTheme.Orange);
            _subBanner.style.unityTextAlign = TextAnchor.MiddleCenter;
            _bannerBox.Add(_subBanner);
            _prompt = Lbl("", 8, UiTheme.Bone);
            _prompt.style.unityTextAlign = TextAnchor.MiddleCenter;
            _bannerBox.Add(_prompt);
            _bannerBox.style.display = DisplayStyle.None;
            _relayout.Add(() =>
            {
                _bannerBox.style.top = Length.Percent(36f);
                _subBanner.style.marginTop = 4f * _texel;
                _prompt.style.marginTop = 4f * _texel;
            });
        }

        private void BuildToasts(RedlineConfig cfg)
        {
            _toastTexts = new string[3];
            _toastTexts[(int)OrbKind.Health] = "+" + cfg.orbHealAmount + " HP";
            _toastTexts[(int)OrbKind.Speed] = "> SPEED";
            _toastTexts[(int)OrbKind.Hook] = "O HOOK";
            _toasts = new Label[3];
            _toastAge = new[] { 99f, 99f, 99f };
            for (int i = 0; i < 3; i++)
            {
                var l = Lbl(_toastTexts[i], 8, UiTheme.Orange);
                l.style.position = Position.Absolute;
                l.style.unityTextAlign = TextAnchor.MiddleCenter;
                l.style.display = DisplayStyle.None;
                Root.Add(l);
                _toasts[i] = l;
            }
        }

        /// <summary>Shows the pickup toast (glyph + word) above the player; the same kind within 0.5 s merges into one.</summary>
        public void ShowPickup(OrbKind kind)
        {
            if (_toasts == null) return;
            int i = (int)kind;
            _toasts[i].style.display = DisplayStyle.Flex;
            if (_toastAge[i] > 0.5f || _toastAge[i] >= ToastLife) _toastAge[i] = 0f;
        }

        private void UpdateToasts(RedlineSim sim, float dt, float k)
        {
            var cam = _game.Camera;
            float w = Root.resolvedStyle.width, h = Root.resolvedStyle.height;
            bool rm = UiTheme.ReducedMotion;
            for (int i = 0; i < 3; i++)
            {
                if (_toastAge[i] >= ToastLife) continue;
                _toastAge[i] += dt;
                if (_toastAge[i] >= ToastLife) { _toasts[i].style.display = DisplayStyle.None; continue; }
                if (cam == null || float.IsNaN(w) || w <= 0f) continue;
                var p = sim.Player;
                var vp = cam.WorldToViewportPoint(_game.ToDraw(new Vector2(p.X, p.MaxY + 0.6f)));
                int step = rm ? 0 : Mathf.Min(3, (int)(_toastAge[i] / ToastLife * 4f)); // 3-step rise of 4 T
                _toasts[i].style.left = vp.x * w - 20f * _texel;
                _toasts[i].style.width = 40f * _texel;
                _toasts[i].style.top = (1f - vp.y) * h - 6f * _texel - step * (4f * _texel / 3f) - i * 10f * _texel;
                // vanish in the last 2 frames' worth of time
                _toasts[i].style.opacity = _toastAge[i] > ToastLife - 0.033f ? 0f : 1f;
            }
        }

        private void BuildMarkers()
        {
            _markers = new VisualElement[MarkerCount];
            for (int i = 0; i < MarkerCount; i++)
            {
                var m = new VisualElement { pickingMode = PickingMode.Ignore };
                m.style.position = Position.Absolute;
                m.style.rotate = new StyleRotate(new Rotate(45f));
                m.style.display = DisplayStyle.None;
                Root.Add(m);
                _markers[i] = m;
            }
            _relayout.Add(() =>
            {
                for (int i = 0; i < MarkerCount; i++)
                {
                    _markers[i].style.width = 4f * _texel; _markers[i].style.height = 4f * _texel;
                    Border(_markers[i], _texel);
                    SetBorderColor(_markers[i], UiTheme.Bone);
                }
            });
        }

        private void UpdateMarkers(RedlineSim sim, float k)
        {
            var cam = _game.Camera;
            float w = Root.resolvedStyle.width, h = Root.resolvedStyle.height;
            int used = 0;
            if (cam != null && !float.IsNaN(w) && w > 0f && (sim.State == SimState.Fighting || sim.State == SimState.Training))
            {
                float m = M * _texel;
                float top = _picShown.y + m, bottom = _picShown.yMax - m;
                var view = LetterboxInfo.ViewportRectIn(w, h);   // the world camera's viewport (integer upscale, bars)
                var enemies = sim.Enemies;                        // by index: foreach over IReadOnlyList boxes the enumerator
                for (int i = 0; i < enemies.Count; i++)
                {
                    var e = enemies[i];
                    if (used >= MarkerCount) break;
                    if (!e.Alive) continue;
                    var vp = cam.WorldToViewportPoint(_game.ToDraw(new Vector2(e.Center.X, e.Center.Y)));
                    if (vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f) continue;
                    var mk = _markers[used++];
                    mk.style.display = DisplayStyle.Flex;
                    mk.style.left = Mathf.Clamp(view.x + vp.x * view.width, _picShown.x + m, _picShown.xMax - m) - 2f * _texel;
                    mk.style.top = Mathf.Clamp(view.y + (1f - vp.y) * view.height, top, bottom) - 2f * _texel;
                }
            }
            for (int i = used; i < MarkerCount; i++)
                if (_markers[i].resolvedStyle.display != DisplayStyle.None) _markers[i].style.display = DisplayStyle.None;
        }

        // ------------------------------------------------------------------ per frame

        private void LateUpdate()
        {
            if (_game == null || _meterFill == null) return;
            float pw = Root.resolvedStyle.width;
            if (float.IsNaN(pw) || pw <= 0f || Screen.width <= 0) return;
            float k = pw / Screen.width;
            UpdateLayout(k);
            var sim = _game.Sim;
            // Toast and level-tag ages (and the 10 Hz speed readout) stand still while paused or behind the menu.
            bool frozen = _paused || Root.style.display.value == DisplayStyle.None;
            float dt = frozen ? 0f : Time.unscaledDeltaTime;
            if (sim != _shownSim) BindLevel(sim);

            var state = sim.State;
            bool training = state == SimState.Training;
            bool ended = state == SimState.GameOver || state == SimState.Victory;
            bool breakState = state == SimState.WaveBreak;
            ShowZones(training, ended, breakState);

            if (!ended)
            {
                UpdateSpeed(sim, dt);
                UpdateBuffs(sim);
                UpdateHp(sim);
                UpdateChain(sim);
                if (!training) UpdateRight(sim, dt);
            }
            UpdateToasts(sim, dt, k);
            if (!training) UpdateBanner(sim); else if (!_shownTraining || _bannerBox.resolvedStyle.display != DisplayStyle.None) HideBanner();
            _shownTraining = training;
            UpdateMarkers(sim, k);
        }

        private bool _zonesInit;
        private bool _zTraining, _zEnded, _zBreak;

        private void ShowZones(bool training, bool ended, bool breakState)
        {
            if (_zonesInit && training == _zTraining && ended == _zEnded && breakState == _zBreak) return;
            _zonesInit = true; _zTraining = training; _zEnded = ended; _zBreak = breakState;
            _tl.style.display = ended ? DisplayStyle.None : DisplayStyle.Flex;
            _bl.style.display = ended ? DisplayStyle.None : DisplayStyle.Flex;
            _tr.style.display = ended || training ? DisplayStyle.None : DisplayStyle.Flex;
            _tr.style.opacity = breakState ? 0.5f : 1f;
            if (breakState) { _chainBox.style.display = DisplayStyle.None; _shownChain = -1; }
        }

        private void UpdateLayout(float k)
        {
            // Picture rect in panel units (proportional to the screen, so a capture render texture of a
            // different aspect still lines up).
            var rect = LetterboxInfo.PictureRectIn(Root.resolvedStyle.width, Root.resolvedStyle.height);
            int scale = UiTheme.HudTextScale;
            float texel = LetterboxInfo.Texel(rect.width);
            if (texel == _texel && scale == _scalePct && rect == _picShown) return;
            _texel = texel; _scalePct = scale; _picShown = rect; _kShown = k;
            _pic.style.left = rect.x; _pic.style.top = rect.y;
            _pic.style.width = rect.width; _pic.style.height = rect.height;
            for (int i = 0; i < _labels.Count; i++)
                _labels[i].style.fontSize = LetterboxInfo.FontPx(_labelT[i], texel, scale);
            for (int i = 0; i < _relayout.Count; i++) _relayout[i]();
        }

        private void UpdateSpeed(RedlineSim sim, float dt)
        {
            var p = sim.Player;
            int tier = Mathf.Clamp(sim.PlayerTier, 1, 3);
            float speed = Mathf.Abs(p.Vx);
            // The fill tracks speed directly (no lerp); rewrite only when the pixel width changes.
            int fillPx = Mathf.RoundToInt(CombatRules.SpeedFraction(p.Vx, sim.Config) * 80f * _texel);
            if (fillPx != _shownFillPx)
            {
                _shownFillPx = fillPx;
                _meterFill.style.width = fillPx;
            }
            // Speed value at 10 Hz.
            _speedTextClock += dt;
            if (_speedTextClock >= 0.1f)
            {
                _speedTextClock = 0f;
                int half = Mathf.Clamp(Mathf.RoundToInt(speed * 2f), 0, _speedTexts.Length - 1);
                if (half != _shownSpeed) { _shownSpeed = half; _speedValue.text = _speedTexts[half]; }
            }
            // Overspeed: 1 T line beyond the max mark + OVER.
            float cap = sim.Config.maxSpeed;
            bool over = speed > cap + 0.05f;
            if (over != _shownOver)
            {
                _shownOver = over;
                _overRow.style.display = over ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (over)
            {
                int px = Mathf.RoundToInt(Mathf.Clamp01((speed - cap) / Mathf.Max(0.01f, _overTop - cap)) * 20f * _texel);
                if (px != _shownOverPx) { _shownOverPx = px; _overLine.style.width = px + 1f; }
            }
            if (tier != _shownTier)
            {
                bool first = _shownTier < 0;
                _shownTier = tier;
                var c = UiTheme.Tier[tier - 1];
                _meterFill.style.backgroundColor = c;
                _tierNumeral.text = TierTexts[tier - 1];
                for (int i = 0; i < 3; i++)
                {
                    bool lit = i < tier;
                    _pips[i].style.backgroundColor = lit ? c : Color.clear;
                    SetBorderColor(_pips[i], lit ? c : UiTheme.Faint);
                }
                _blade.style.display = tier == 3 ? DisplayStyle.Flex : DisplayStyle.None;
                if (!first) _tierFlashUntilFrame = Time.frameCount + (UiTheme.ReducedMotion ? 1 : 3);
            }
            bool flash = Time.frameCount < _tierFlashUntilFrame;
            var nc = flash ? UiTheme.Bone : UiTheme.Tier[tier - 1];
            if (flash && UiTheme.ReducedMotion) nc = Color.Lerp(UiTheme.Tier[tier - 1], UiTheme.Bone, 0.5f);
            if (_tierNumeral.resolvedStyle.color != nc) _tierNumeral.style.color = nc;
        }

        private static readonly string[] TierTexts = { "1", "2", "3" };

        private void UpdateBuffs(RedlineSim sim)
        {
            _speedBuff.Update(sim.Player.SpeedBuffTimer);
            _hookBuff.Update(sim.Player.HookBuffTimer);
        }

        private void UpdateHp(RedlineSim sim)
        {
            int hp = sim.Player.Hp;
            bool rm = UiTheme.ReducedMotion;
            bool low = hp == 1;
            bool blinkOn = rm || !low || ((int)(Time.unscaledTime * 4f) & 1) == 0; // 2 Hz, stepped
            bool flashing = Time.frameCount < _hpFlashUntilFrame;
            if (hp == _shownHp && blinkOn == _hpBlinkOn && !flashing && _hpFlashIndex < 0) return;
            if (hp != _shownHp)
            {
                if (hp < _shownHp && _shownHp > 0)
                {
                    _hpFlashIndex = hp;
                    _hpFlashUntilFrame = Time.frameCount + (rm ? 1 : 3);
                    flashing = true;
                }
                _hpLow.style.display = low ? DisplayStyle.Flex : DisplayStyle.None;
            }
            _shownHp = hp;
            _hpBlinkOn = blinkOn;
            for (int i = 0; i < _hpCells.Length; i++)
            {
                var dia = (VisualElement)_hpCells[i].userData;
                bool full = i < hp;
                Color border = full ? UiTheme.Orange : UiTheme.Faint;
                Color dot = full ? UiTheme.Orange : Color.clear;
                if (full && low) { border = UiTheme.Crimson; dot = UiTheme.Crimson; if (!blinkOn) { border = Color.clear; dot = Color.clear; } }
                if (flashing && i == _hpFlashIndex) { border = rm ? Color.Lerp(UiTheme.Faint, UiTheme.Bone, 0.5f) : UiTheme.Bone; }
                SetBorderColor(dia, border);
                _hpDots[i].style.backgroundColor = dot;
            }
            if (!flashing) _hpFlashIndex = -1;
        }

        private void UpdateChain(RedlineSim sim)
        {
            var p = sim.Player;
            int chain = p.Chain >= 2 ? p.Chain : 0;
            if (chain != _shownChain)
            {
                if (chain > _shownChain && chain >= 2) _chainFlashUntilFrame = Time.frameCount + (UiTheme.ReducedMotion ? 1 : 2);
                _shownChain = chain;
                _chainBox.style.display = chain >= 2 ? DisplayStyle.Flex : DisplayStyle.None;
                if (chain >= 2) _chainLabel.text = chain < ChainTexts.Length ? ChainTexts[chain] : "x" + chain;
            }
            if (chain < 2) return;
            var c = Time.frameCount < _chainFlashUntilFrame ? Color.white : UiTheme.Bone;
            if (_chainLabel.resolvedStyle.color != c) _chainLabel.style.color = c;
            int px = Mathf.RoundToInt(Mathf.Clamp01(p.ChainTimer / sim.Config.chainWindow) * 24f * _texel);
            if (px != _shownDecayPx) { _shownDecayPx = px; _chainDecay.style.width = px; }
        }

        private void UpdateRight(RedlineSim sim, float dt)
        {
            // Level tag: 3 s, then a 3-step fade (hard cut under Reduced Motion).
            if (_levelAge < LevelTagSeconds + 0.6f)
            {
                _levelAge += dt;
                int step = _levelAge < LevelTagSeconds ? 0 : UiTheme.ReducedMotion ? 4 : Mathf.Min(4, 1 + (int)((_levelAge - LevelTagSeconds) / 0.2f));
                if (step != _shownLevelStep)
                {
                    _shownLevelStep = step;
                    _levelTag.style.opacity = step == 0 ? 1f : step >= 4 ? 0f : 1f - step * 0.25f;
                    _levelTag.style.display = step >= 4 ? DisplayStyle.None : DisplayStyle.Flex;
                }
            }
            if (sim.CurrentWave != _shownWave || sim.State == SimState.Victory)
            {
                _shownWave = sim.CurrentWave;
                _waveValue.text = sim.Endless ? sim.CurrentWave.ToString() : sim.CurrentWave + "/" + sim.WaveCount;
            }
            if (sim.EnemiesAlive != _shownEnemies)
            {
                if (_shownEnemies >= 0) _foesFlashUntilFrame = Time.frameCount + (UiTheme.ReducedMotion ? 1 : 2);
                _shownEnemies = sim.EnemiesAlive;
                _foesValue.text = EnemyTexts[Mathf.Clamp(sim.EnemiesAlive, 0, EnemyTexts.Length - 1)];
            }
            var c = Time.frameCount < _foesFlashUntilFrame ? Color.white : UiTheme.Bone;
            if (_foesValue.resolvedStyle.color != c) _foesValue.style.color = c;
        }

        private static readonly string[] EnemyTexts = BuildNumbers("", "", 100);

        private void UpdateBanner(RedlineSim sim)
        {
            bool stale = sim.State == SimState.WaveBreak && sim.CurrentWave != _shownBannerWave;
            if (sim.State != _shownState || stale) ShowBanner(sim);
            if (sim.State == SimState.WaveBreak)
            {
                int countdown = Mathf.Max(1, Mathf.CeilToInt(sim.BreakTimer));
                if (countdown != _shownCountdown)
                {
                    _shownCountdown = countdown;
                    _banner.text = (!sim.Endless && sim.CurrentWave == sim.WaveCount ? "FINAL WAVE - " : "GET READY - ") + countdown;
                }
            }
            if (_prompt.text.Length > 0)
            {
                // Stepped 1 Hz blink; steady under Reduced Motion.
                bool on = UiTheme.ReducedMotion || ((int)Time.unscaledTime & 1) == 0;
                float o = on ? 1f : 0f;
                if (_prompt.resolvedStyle.opacity != o) _prompt.style.opacity = o;
            }
        }

        private void ShowBanner(RedlineSim sim)
        {
            _shownState = sim.State;
            _shownCountdown = -1;
            switch (sim.State)
            {
                case SimState.WaveBreak:
                    _shownBannerWave = sim.CurrentWave;
                    SetBanner("", sim.Endless ? "WAVE " + sim.CurrentWave : "WAVE " + sim.CurrentWave + "/" + sim.WaveCount, "");
                    break;
                case SimState.GameOver:
                    SetBanner("RUN ENDED", "WAVE " + sim.CurrentWave + " - " + sim.Kills + " KILLS",
                        "[" + ControlLabels.Get(GameAction.Restart) + "] / [SELECT] UPGRADES");
                    break;
                case SimState.Victory:
                    string next = _game.NextLevelName;
                    if (next != null)
                        SetBanner("LEVEL CLEAR", "NEXT: " + next.ToUpperInvariant() + " - " + sim.Kills + " KILLS", "");
                    else
                        SetBanner("ALL " + _game.LevelCount + " LEVELS CLEARED", sim.Kills + " KILLS",
                            "[" + ControlLabels.Get(GameAction.Restart) + "] / [SELECT] UPGRADES");
                    break;
                default:
                    HideBanner();
                    _shownState = sim.State;   // cache the hidden state so the banner isn't rebuilt every frame
                    break;
            }
        }

        private void SetBanner(string title, string sub, string prompt)
        {
            _banner.text = title;
            _subBanner.text = sub;
            _prompt.text = prompt;
            _prompt.style.opacity = 1f;
            _bannerBox.style.display = DisplayStyle.Flex;
        }

        private void HideBanner()
        {
            _banner.text = ""; _subBanner.text = ""; _prompt.text = "";
            _bannerBox.style.display = DisplayStyle.None;
            _shownState = (SimState)(-1);
        }

        private void OnDestroy()
        {
            if (_settings != null) Destroy(_settings);
        }

        // ------------------------------------------------------------------ buff chip

        private enum BuffGlyph { Chevron, Ring }

        /// <summary>
        /// A timed-buff chip: glyph (chevron = speed, ring = hook; shape, not colour, tells them apart)
        /// plus the remaining seconds. Hidden while the buff is off; under 2 s the number blinks bone
        /// (stepped 2 Hz; steady under Reduced Motion).
        /// </summary>
        private sealed class BuffChip
        {
            private static readonly string[] Seconds = BuildNumbers("", "S", 100);
            private readonly RedlineHud _hud;
            private readonly BuffGlyph _kind;
            private readonly VisualElement _glyph;
            private readonly Label _secs;
            private int _shownSecs = -1;
            private bool _shown = true;
            private bool _blinkBone;

            /// <summary>The chip's element.</summary>
            public readonly VisualElement Root;

            public BuffChip(RedlineHud hud, BuffGlyph kind, float duration)
            {
                _hud = hud;
                _kind = kind;
                Root = new VisualElement { pickingMode = PickingMode.Ignore };
                Root.style.flexDirection = FlexDirection.Row;
                Root.style.alignItems = Align.Center;
                _glyph = new VisualElement { pickingMode = PickingMode.Ignore };
                Root.Add(_glyph);
                _secs = hud.Lbl("", 8, UiTheme.Orange);
                Root.Add(_secs);
                Root.style.display = DisplayStyle.None;
                _shown = false;
            }

            public void Layout(float t)
            {
                Root.style.marginLeft = 4f * t;
                _glyph.style.width = 4f * t; _glyph.style.height = 4f * t;
                _glyph.style.marginRight = 2f * t; _glyph.style.marginLeft = 2f * t;
                _glyph.style.borderTopWidth = 0; _glyph.style.borderLeftWidth = 0;
                if (_kind == BuffGlyph.Chevron)
                {
                    _glyph.style.borderRightWidth = t; _glyph.style.borderBottomWidth = t;
                    _glyph.style.rotate = new StyleRotate(new Rotate(-45f));
                    SetSideColor(_glyph, UiTheme.Orange);
                }
                else
                {
                    _glyph.style.borderTopWidth = t; _glyph.style.borderLeftWidth = t;
                    _glyph.style.borderRightWidth = t; _glyph.style.borderBottomWidth = t;
                    float r = 3f * t;
                    _glyph.style.borderTopLeftRadius = r; _glyph.style.borderTopRightRadius = r;
                    _glyph.style.borderBottomLeftRadius = r; _glyph.style.borderBottomRightRadius = r;
                    SetBorderColor(_glyph, UiTheme.Orange);
                }
            }

            private static void SetSideColor(VisualElement e, Color c)
            {
                e.style.borderRightColor = c; e.style.borderBottomColor = c;
            }

            public void Update(float timeLeft)
            {
                bool on = timeLeft > 0f;
                if (on != _shown)
                {
                    _shown = on;
                    Root.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                }
                if (!on) return;
                int secs = Mathf.Clamp(Mathf.CeilToInt(timeLeft), 0, Seconds.Length - 1);
                if (secs != _shownSecs) { _shownSecs = secs; _secs.text = Seconds[secs]; }
                bool bone = timeLeft < BuffBlinkBelow && !UiTheme.ReducedMotion && (((int)(Time.unscaledTime * 4f)) & 1) == 0;
                if (bone != _blinkBone)
                {
                    _blinkBone = bone;
                    _secs.style.color = bone ? UiTheme.Bone : UiTheme.Orange;
                }
            }
        }

        // ------------------------------------------------------------------ element helpers

        private static string[] BuildNumbers(string prefix, string suffix, int count)
        {
            var s = new string[count];
            for (int i = 0; i < count; i++) s[i] = prefix + i + suffix;
            return s;
        }

        private static VisualElement Abs(VisualElement parent)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.position = Position.Absolute;
            parent.Add(e);
            return e;
        }

        private static VisualElement RowOf(VisualElement parent, Justify justify)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.flexDirection = FlexDirection.Row;
            e.style.justifyContent = justify;
            e.style.alignItems = Align.Center;
            parent.Add(e);
            return e;
        }

        /// <summary>A label in the terminal font, sized tUnits * T (rescaled by the layout pass).</summary>
        private Label Lbl(string text, int tUnits, Color color)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.color = color;
            label.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(UiTheme.Font));
            label.style.paddingLeft = 0; label.style.paddingRight = 0;
            label.style.paddingTop = 0; label.style.paddingBottom = 0;
            label.style.marginLeft = 0; label.style.marginRight = 0;
            label.style.marginTop = 0; label.style.marginBottom = 0;
            label.style.fontSize = LetterboxInfo.FontPx(tUnits, Mathf.Max(_texel, 4f), 100);
            label.style.whiteSpace = WhiteSpace.NoWrap;
            _labels.Add(label);
            _labelT.Add(tUnits);
            return label;
        }

        private static void Border(VisualElement e, float width)
        {
            e.style.borderTopWidth = width; e.style.borderBottomWidth = width;
            e.style.borderLeftWidth = width; e.style.borderRightWidth = width;
        }

        private static void SetBorderColor(VisualElement e, Color c)
        {
            e.style.borderTopColor = c; e.style.borderBottomColor = c;
            e.style.borderLeftColor = c; e.style.borderRightColor = c;
        }
    }
}
