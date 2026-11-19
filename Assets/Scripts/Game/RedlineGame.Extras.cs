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
            int i = (int)kind;
            if (i <= 0 || i >= Accents.Length)
            {
                for (int t = 0; t < 3; t++) TierColors[t] = BaseTierColors[t];
                return;
            }
            var a = Accents[i];
            TierColors[0] = Color.Lerp(a, new Color(0.6f, 0.65f, 0.75f), 0.45f);
            TierColors[1] = a;
            TierColors[2] = Color.Lerp(a, Color.white, 0.6f);
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
