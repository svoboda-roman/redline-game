using System;
using Redline.Core;
using Redline.Game.Render3D.Actors3D;
using UnityEngine;

namespace Redline.Game
{
    /// <summary>
    /// Run extras on top of the core loop: the combo multiplier on kill points, slow-motion and callouts (juice), the
    /// perk pick every few waves, run challenges, wave bosses, the shock floor view, cosmetic accents and the level
    /// unlocks (a level opens once the one before it reached <see cref="UpgradeConfig.unlockWave"/>).
    /// </summary>
    public sealed partial class RedlineGame
    {
        /// <summary>Waves between perk picks.</summary>
        public const int PerkEveryWaves = 5;

        private readonly System.Random _extrasRng = new System.Random();
        private readonly ChallengeTracker _challenges = new ChallengeTracker();
        private MenuScreen _perkMenu;
        private MenuScreen _cosmeticMenu;
        private PerkKind[] _perkOffer = new PerkKind[3];
        private readonly System.Collections.Generic.List<PerkKind> _perksTaken = new System.Collections.Generic.List<PerkKind>();
        private int _perkOfferedAtWave;
        private int _comboMult = 1;
        private float _slowMo;
        private bool _hurtThisWave;
        private ShockFloorView _shockView;

        // ------------------------------------------------------------------ run lifecycle

        private void ExtrasNewRun()
        {
            _challenges.NewRun(_extrasRng);
            _perksTaken.Clear();
            _perkOfferedAtWave = 0;
            _comboMult = 1;
            _slowMo = 0f;
            _hurtThisWave = false;
            RefreshObjectives();
        }

        private void ExtrasLevelChanged()
        {
            _perkOfferedAtWave = 0;
            _shockView?.Dispose();
            _shockView = _render3D && _arenaHost != null ? new ShockFloorView(_arenaHost.transform, Sim.Map) : null;
        }

        /// <summary>Sim step multiplier: slow-motion right after the wave's last kill or a boss falls.</summary>
        private float ExtrasTimeScale => _slowMo > 0f && !UiTheme.ReducedMotion ? 0.3f : 1f;

        private void ExtrasUpdate(float dt)
        {
            if (_slowMo > 0f) _slowMo -= dt;
            _shockView?.Sync(Sim, Time.time);
            if (Sim.Player.Chain == 0) _comboMult = 1;
            if (_flow == Flow.Playing && _training == null && Sim.State == SimState.WaveBreak && Sim.Map.Endless
                && Sim.WaveIndex > 0 && Sim.WaveIndex % PerkEveryWaves == 0 && _perkOfferedAtWave != Sim.WaveIndex)
                OfferPerks();
        }

        // ------------------------------------------------------------------ 1 combo + 4 juice + 7 challenges + 9 bosses

        /// <summary>Points multiplier for a chain: x2 from 3 kills, x3 from 6, x4 from 9, x5 from 12.</summary>
        public static int ComboMultiplier(int chain) => Combo.Multiplier(chain);

        private void ExtrasOnKill(int tier, EnemyKind kind)
        {
            var p = Sim.Player;
            int mult = ComboMultiplier(p.Chain);
            _run.BonusPoints += _upgradeCfg.pointsPerKill * (mult - 1);
            if (mult > _comboMult)
            {
                _hud.ShowCallout("x" + mult + " POINTS", TierColors[Mathf.Clamp(mult - 1, 1, 3) - 1]);
                _effects.Flash(0.06f);
                _hitStop = Mathf.Max(_hitStop, 0.05f);
            }
            _comboMult = mult;
            _challenges.OnKill(tier, !p.Grounded, p.Hooked, kind);
            _challenges.OnChain(p.Chain);
            // the wave's last kill: a beat of slow motion
            bool anyLeft = false;
            foreach (var e in Sim.Enemies) if (e.Alive) { anyLeft = true; break; }
            if (!anyLeft && Sim.State == SimState.Fighting) _slowMo = 0.6f;
            TakeChallenges();
        }

        private void ExtrasOnWaveCleared()
        {
            _challenges.OnWaveCleared(_hurtThisWave);
            _hurtThisWave = false;
            TakeChallenges();
        }

        private void ExtrasOnBossSpawn()
        {
            _hud.ShowCallout("BOSS INCOMING", new Color(1f, 0.25f, 0.3f));
            _shake = Mathf.Max(_shake, 0.5f);
            _effects.Flash(0.2f);
        }

        private void ExtrasOnBossDown()
        {
            _run.BonusPoints += 50;
            _hud.ShowCallout("BOSS DOWN  +50", new Color(1f, 0.85f, 0.3f));
            _slowMo = 1.0f;
            _shake = Mathf.Max(_shake, 0.6f);
            _effects.Flash(0.35f);
        }

        private void TakeChallenges()
        {
            foreach (var c in _challenges.TakeCompleted())
            {
                _run.BonusPoints += c.Reward;
                _hud.ShowCallout("CHALLENGE DONE  +" + c.Reward, new Color(0.4f, 1f, 0.55f));
            }
            RefreshObjectives();
        }

        private void RefreshObjectives()
        {
            if (_hud == null) return;
            var sb = new System.Text.StringBuilder();
            foreach (var c in _challenges.Active)
                sb.Append(c.Done ? "[X] " : "[ ] ").Append(c.Text).Append(c.Done ? "" : "  " + c.Progress + "/" + c.Target).Append('\n');
            if (_perksTaken.Count > 0)
            {
                sb.Append("PERKS: ");
                for (int i = 0; i < _perksTaken.Count; i++) sb.Append(i > 0 ? ", " : "").Append(Perks.Title(_perksTaken[i]));
            }
            _hud.SetObjectives(sb.ToString().TrimEnd());
        }

        // ------------------------------------------------------------------ 6 perks

        private void OfferPerks()
        {
            _perkOfferedAtWave = Sim.WaveIndex;
            _perkOffer = Perks.Roll(_extrasRng);
            for (int i = 0; i < _perkOffer.Length; i++)
                _perkMenu.SetItemLabel(i, Perks.Title(_perkOffer[i]) + "  -  " + Perks.Description(_perkOffer[i]));
            _audio.SetPaused(true);
            _perkMenu.Show();
            SetFlow(Flow.Paused);
        }

        private void PickPerk(int i)
        {
            if (i < 0 || i >= _perkOffer.Length) return;
            var perk = _perkOffer[i];
            Perks.Apply(perk, _runConfig, Sim.Player);
            _perksTaken.Add(perk);
            _perkMenu.Hide();
            _audio.SetPaused(false);
            _accumulator = 0f;
            SetFlow(Flow.Playing);
            _hud.ShowCallout(Perks.Title(perk), new Color(0.4f, 0.85f, 1f));
            RefreshObjectives();
        }

        private bool PerkMenuOpen => _perkMenu != null && _perkMenu.Visible;

        // ------------------------------------------------------------------ 8 cosmetics + 11 unlocks (menus)

        private static readonly string[] CosmeticNames = { "CLASSIC", "CYAN", "VIOLET", "TOXIC", "GOLD", "WHITE" };
        // the classic palette (same values as TierColors' initial ones; not read from it: partial-class static init order)
        private static readonly Color[] BaseTierColors =
            { new Color(0.612f, 0.784f, 1f), new Color(1f, 0.702f, 0.278f), new Color(1f, 0.957f, 0.91f) };
        private static readonly Color[] Accents =
        {
            Color.clear, new Color(0.3f, 0.92f, 1f), new Color(0.72f, 0.42f, 1f), new Color(0.5f, 1f, 0.32f),
            new Color(1f, 0.78f, 0.22f), new Color(0.92f, 0.95f, 1f)
        };

        /// <summary>Recolours the speed-tier palette (player glow, rope, trails, sparks) with the equipped accent.</summary>
        public static void ApplyCosmetic(CosmeticKind kind)
        {
            var c = CosmeticTierColors(kind);
            for (int t = 0; t < 3; t++) TierColors[t] = c[t];
        }

        /// <summary>The three speed-tier colours a cosmetic gives (classic = the art bible palette).</summary>
        public static Color[] CosmeticTierColors(CosmeticKind kind)
        {
            int i = (int)kind;
            if (i <= 0 || i >= Accents.Length) return new[] { BaseTierColors[0], BaseTierColors[1], BaseTierColors[2] };
            var a = Accents[i];
            return new[] { Color.Lerp(a, new Color(0.6f, 0.65f, 0.75f), 0.45f), a, Color.Lerp(a, Color.white, 0.6f) };
        }

        // ------------------------------------------------------------------ cosmetic preview

        private UnityEngine.UIElements.VisualElement _cosGlow, _cosRope, _cosRopeTip;
        private readonly UnityEngine.UIElements.VisualElement[] _cosTrail = new UnityEngine.UIElements.VisualElement[4];
        private readonly UnityEngine.UIElements.VisualElement[] _cosSparks = new UnityEngine.UIElements.VisualElement[6];
        private readonly UnityEngine.UIElements.VisualElement[] _cosSwatch = new UnityEngine.UIElements.VisualElement[3];
        private UnityEngine.UIElements.VisualElement _cosBox;

        /// <summary>COSMETICS: a sprinting ronin with its glow, speed trail, hook rope, sparks and the tier swatches in the highlighted colour.</summary>
        private void BuildCosmeticPreview()
        {
            UnityEngine.UIElements.VisualElement Abs(UnityEngine.UIElements.VisualElement parent, float l, float t, float w, float h)
            {
                var e = new UnityEngine.UIElements.VisualElement { pickingMode = UnityEngine.UIElements.PickingMode.Ignore };
                e.style.position = UnityEngine.UIElements.Position.Absolute;
                e.style.left = l; e.style.top = t; e.style.width = w; e.style.height = h;
                parent.Add(e);
                return e;
            }
            var box = new UnityEngine.UIElements.VisualElement { pickingMode = UnityEngine.UIElements.PickingMode.Ignore };
            box.style.position = UnityEngine.UIElements.Position.Absolute;
            box.style.right = new UnityEngine.UIElements.Length(6f, UnityEngine.UIElements.LengthUnit.Percent);
            box.style.top = new UnityEngine.UIElements.Length(26f, UnityEngine.UIElements.LengthUnit.Percent);
            box.style.width = 640f; box.style.height = 420f;
            box.style.backgroundColor = new Color(0.05f, 0.03f, 0.08f, 0.92f);
            box.style.overflow = UnityEngine.UIElements.Overflow.Hidden;
            MenuKit.Border(box, 3f, UiTheme.Faint);
            _cosBox = box;
            // floor line
            var floor = Abs(box, 0f, 330f, 640f, 4f);
            floor.style.backgroundColor = new Color(0.25f, 0.27f, 0.33f);
            // speed trail streaks behind the runner
            for (int i = 0; i < _cosTrail.Length; i++) _cosTrail[i] = Abs(box, 40f + i * 18f, 190f + i * 26f, 240f - i * 40f, 10f);
            // glow behind the body
            _cosGlow = Abs(box, 230f, 110f, 220f, 220f);
            _cosGlow.style.borderTopLeftRadius = _cosGlow.style.borderTopRightRadius = 110f;
            _cosGlow.style.borderBottomLeftRadius = _cosGlow.style.borderBottomRightRadius = 110f;
            // hook rope up to the right and its anchor
            _cosRope = Abs(box, 380f, 60f, 6f, 210f);
            _cosRope.style.rotate = new UnityEngine.UIElements.Rotate(new UnityEngine.UIElements.Angle(38f, UnityEngine.UIElements.AngleUnit.Degree));
            _cosRopeTip = Abs(box, 440f, 52f, 18f, 18f);
            _cosRopeTip.style.rotate = new UnityEngine.UIElements.Rotate(new UnityEngine.UIElements.Angle(45f, UnityEngine.UIElements.AngleUnit.Degree));
            // the ronin, sprinting
            try
            {
                var sheet = Redline.Game.Art.CharacterSheet.Load(Redline.Game.Ronin.BakedRoninView.SheetPath);
                if (sheet.TryGetAnimation("sprint", out var anim) || sheet.TryGetAnimation("run", out anim))
                {
                    var img = new UnityEngine.UIElements.Image { sprite = anim.Frame(Mathf.Min(2, anim.FrameCount - 1)), scaleMode = ScaleMode.ScaleToFit, pickingMode = UnityEngine.UIElements.PickingMode.Ignore };
                    img.style.position = UnityEngine.UIElements.Position.Absolute;
                    img.style.left = 190f; img.style.top = 70f; img.style.width = 300f; img.style.height = 264f;
                    box.Add(img);
                }
            }
            catch (Exception ex) { Debug.LogWarning("Redline: cosmetic preview has no ronin sprite: " + ex.Message); }
            // sparks at the blade
            float[,] sp = { { 470, 170 }, { 500, 200 }, { 488, 140 }, { 520, 180 }, { 455, 215 }, { 510, 230 } };
            for (int i = 0; i < _cosSparks.Length; i++) _cosSparks[i] = Abs(box, sp[i, 0], sp[i, 1], 10f, 10f);
            // tier swatches
            for (int i = 0; i < 3; i++)
            {
                _cosSwatch[i] = Abs(box, 40f + i * 200f, 350f, 150f, 26f);
                var lbl = MenuKit.Text("SPEED " + (i + 1), null, MenuKit.Label, UiTheme.Bone);
                lbl.style.position = UnityEngine.UIElements.Position.Absolute;
                lbl.style.left = 40f + i * 200f; lbl.style.top = 378f;
                box.Add(lbl);
            }
            _cosmeticMenu.Root.Add(box);
            _cosmeticMenu.SelectionChanged = i =>
            {
                bool colour = i >= 0 && i < CosmeticNames.Length;
                box.style.display = colour ? UnityEngine.UIElements.DisplayStyle.Flex : UnityEngine.UIElements.DisplayStyle.None;
                if (colour) PaintCosmeticPreview((CosmeticKind)i);
            };
        }

        private void PaintCosmeticPreview(CosmeticKind kind)
        {
            var c = CosmeticTierColors(kind);
            Color A(Color col, float a) => new Color(col.r, col.g, col.b, a);
            for (int i = 0; i < _cosTrail.Length; i++) _cosTrail[i].style.backgroundColor = A(c[i == 0 ? 2 : 1], 0.85f - i * 0.18f);
            _cosGlow.style.backgroundColor = A(c[1], 0.28f);
            _cosRope.style.backgroundColor = A(Color.Lerp(c[2], Color.white, 0.3f), 1f);
            _cosRopeTip.style.backgroundColor = A(c[2], 1f);
            for (int i = 0; i < _cosSparks.Length; i++) _cosSparks[i].style.backgroundColor = A(c[i % 3], 1f);
            for (int i = 0; i < 3; i++) _cosSwatch[i].style.backgroundColor = A(c[i], 1f);
            MenuKit.Border(_cosBox, 3f, A(c[1], 0.9f));
        }

        private void BuildExtraMenus()
        {
            _perkMenu = gameObject.AddComponent<MenuScreen>();
            _perkMenu.Init("PerkMenu", 112f, "CHOOSE A PERK", false);
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                _perkMenu.AddItem("PERK", () => PickPerk(k));
            }
            _perkMenu.SetHint("W/S - D-PAD  SELECT      ENTER - A  TAKE IT");

            _cosmeticMenu = gameObject.AddComponent<MenuScreen>();
            _cosmeticMenu.Init("CosmeticMenu", 90f, "NEON COLOUR", true);
            for (int i = 0; i < CosmeticNames.Length; i++)
            {
                int k = i;
                _cosmeticMenu.AddItem(CosmeticNames[i], () => PickCosmetic((CosmeticKind)k));
            }
            _cosmeticMenu.AddItem("BACK", CloseCosmetics);
            _cosmeticMenu.Cancelled = CloseCosmetics;
            BuildCosmeticPreview();
            ApplyCosmetic(_store.State.Equipped);
        }

        private void OpenCosmetics()
        {
            _mainMenu.Hide();
            RefreshCosmeticLabels();
            _cosmeticMenu.SetNotice("POINTS  " + _store.State.Points);
            _cosmeticMenu.Show();
            SetFlow(_flow);
        }

        private void CloseCosmetics()
        {
            _cosmeticMenu.Hide();
            _mainMenu.Show();
            SetFlow(_flow);
        }

        private void RefreshCosmeticLabels()
        {
            var s = _store.State;
            for (int i = 0; i < CosmeticNames.Length; i++)
            {
                var k = (CosmeticKind)i;
                int cost = _upgradeCfg.cosmeticCosts != null && i < _upgradeCfg.cosmeticCosts.Length ? _upgradeCfg.cosmeticCosts[i] : 0;
                string tail = s.Equipped == k ? "EQUIPPED" : s.Owns(k) ? "OWNED" : cost + " PTS";
                _cosmeticMenu.SetItemLabel(i, CosmeticNames[i].PadRight(10) + tail);
            }
        }

        private void PickCosmetic(CosmeticKind kind)
        {
            var s = _store.State;
            if (s.TryEquipOrBuy(kind, _upgradeCfg) != BuyResult.Ok)
            {
                _cosmeticMenu.SetNotice("NOT ENOUGH POINTS  -  " + s.Points);
                return;
            }
            _store.Save();
            ApplyCosmetic(kind);
            RefreshCosmeticLabels();
            _cosmeticMenu.SetNotice("POINTS  " + s.Points);
        }

        /// <summary>Level i is open when it is the first, or the level before it reached the unlock wave.</summary>
        private bool LevelUnlocked(int i) =>
            i <= 0 || _store.State.BestWave(Levels[i - 1]) >= _upgradeCfg.unlockWave;

        private string LevelTitle(int i) => i < LevelTitles.Length ? LevelTitles[i] : Levels[i];

        private void RefreshLevelLabels()
        {
            for (int i = 0; i < Levels.Length; i++)
                _levelMenu.SetItemLabel(i, LevelTitle(i) + (LevelUnlocked(i) ? "" : "  - LOCKED"));
            _levelMenu.SetNotice("");
        }

        private void PickLevel(int level)
        {
            if (!LevelUnlocked(level))
            {
                _levelMenu.SetNotice("REACH WAVE " + _upgradeCfg.unlockWave + " ON " + LevelTitle(level - 1).Substring(3).Trim() + " TO UNLOCK");
                return;
            }
            _levelMenu.Hide();
            _startLevel = level;
            StartRun();
        }

        /// <summary>Records the wave reached on the current level (records and unlocks) when a run ends.</summary>
        private void RecordBestWave()
        {
            if (_training != null || Sim == null) return;
            int reached = Sim.State == SimState.Victory ? Sim.WaveCount : Sim.WaveIndex + (Sim.State == SimState.WaveBreak ? 0 : 1);
            if (_store.State.RecordWave(LevelName, reached)) Debug.Log("Redline: new best wave " + reached + " on " + LevelName);
        }
    }
}
