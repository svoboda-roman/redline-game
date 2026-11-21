using Redline.Core;
using Redline.Game.Render3D.Integration;
using UnityEngine;

namespace Redline.Game
{
    /// <summary>
    /// STORY MODE: its own map (Resources/Story01.txt, converted from the user's drawing by tools/story) with its own
    /// rules (RedlineSim.Story.cs: checkpoints, hook from the first checkpoint, levers and gates, fire traps, a crumbling
    /// bridge, a lift, placed enemies, no waves, no game over) and its boss, Big Tentacle Bill (RedlineSim.Boss.cs). It
    /// shares nothing with ENDLESS: no upgrades, perks, points or records. The hero is the Ronin until Bill is beaten;
    /// that unlocks the Reaper for the story (GameSettings StoryReaper) and returns to the title menu.
    /// </summary>
    public sealed partial class RedlineGame
    {
        private const string StoryLevel = "Story01";

        /// <summary>True while a story run is on.</summary>
        private bool _story;

        /// <summary>The character picked for ENDLESS, put back after a story run (-1: nothing to restore).</summary>
        private int _storySavedCharacter = -1;

        private StoryProps3D _storyProps;
        private BossView3D _bossView;
        private float _storyEndTimer;

        /// <summary>STORY MODE from the title menu: a fresh story run from the start of the map.</summary>
        private void StartStory()
        {
            var asset = Resources.Load<TextAsset>(StoryLevel);
            if (asset == null)
            {
                _mainMenu.SetNotice("STORY MAP IS MISSING");
                return;
            }
            _mainMenu.Hide();
            _screen.Hide();
            _audio.SetPaused(false);
            _story = true;
            _storyEndTimer = 0f;
            // The story's hero is the Ronin until Big Tentacle Bill is beaten; then the picked character (the Reaper too).
            var hero = GameSettings.Current.StoryReaper == 1 ? SelectedCharacter : PlayerCharacter.Ronin;
            bool swap = GameSettings.Current.Character != (int)hero;
            if (swap)
            {
                _storySavedCharacter = GameSettings.Current.Character;
                GameSettings.Current.Character = (int)hero;
            }
            // Story rules: the base tuning with the hero's weapon - no upgrades, no random waves.
            _runConfig = Upgrades.Apply(_config, _upgradeCfg, null);
            Characters.Apply(_runConfig, hero);
            _runConfig.randomWaves = 0;
            _run = default;
            _runEnded = true;   // nothing to award: story runs never reach the points / upgrade screen
            ExtrasNewRun();
            Sim = new RedlineSim(_runConfig, TileMap.Parse(asset.text));
            if (swap) BuildViews();   // also builds the arena
            OnLevelChanged();
            _tutorial.Hide();
            SetFlow(Flow.Playing);
            _hud.ShowCallout("STORY", new Color(0.95f, 0.9f, 0.8f));
        }

        /// <summary>Leaves the story (pause menu, or the end of the story so far) for the title menu.</summary>
        private void ExitStory(string notice)
        {
            _story = false;
            _storyProps?.Dispose();
            _storyProps = null;
            _bossView?.Dispose();
            _bossView = null;
            _hud.SetBossBar(null, 0f, 0, 0);
            bool restore = _storySavedCharacter >= 0;
            if (restore)
            {
                GameSettings.Current.Character = _storySavedCharacter;
                _storySavedCharacter = -1;
            }
            ShowMainMenu(notice);
            if (restore) BuildViews();
        }

        /// <summary>Story feedback for one sim event (called from HandleEvents).</summary>
        private void OnStoryEvent(SimEvent ev, Vector2 at)
        {
            var warm = new Color(1f, 0.82f, 0.45f);
            switch (ev.Type)
            {
                case SimEventType.StoryCheckpoint:
                    _hud.ShowCallout("CHECKPOINT", warm);
                    _effects.Sparks(at + new Vector2(0f, 1f), warm, 18, 5f);
                    break;
                case SimEventType.HookUnlocked:
                    _hud.ShowCallout("HOOK UNLOCKED  -  HOLD K / X", warm);
                    break;
                case SimEventType.LeverPulled:
                    _hud.ShowCallout(ev.Detail > 0 ? "THE WAY UP IS OPEN" : "THE GATE IS OPEN", warm);
                    _shake = Mathf.Max(_shake, 0.25f);
                    break;
                case SimEventType.GateOpen:
                    _shake = Mathf.Max(_shake, 0.3f);
                    break;
                case SimEventType.BridgeCrumble:
                    _effects.Debris(at, new Color(0.35f, 0.25f, 0.15f), 5, 4f);
                    break;
                case SimEventType.StoryRespawn:
                    _hud.ShowCallout("BACK TO THE CHECKPOINT", new Color(0.9f, 0.3f, 0.3f));
                    _camX = Sim.Player.X;
                    _camY = Sim.Player.Y + 2.5f;
                    _prevRunId = -1;
                    break;
                case SimEventType.BossWake:
                    _hud.ShowCallout(StoryBoss.Name, new Color(0.9f, 0.25f, 0.3f));
                    _prevRunId = -1;   // the player was put in the middle of the room: no interpolation streak
                    _shake = Mathf.Max(_shake, 0.6f);
                    break;
                case SimEventType.BossSlam:
                    _shake = Mathf.Max(_shake, 0.7f);
                    _effects.Debris(at, new Color(0.3f, 0.2f, 0.1f), 14, 7f);
                    break;
                case SimEventType.BossTentacleHit:
                    BossIchor(at, 0.6f);
                    _shake = Mathf.Max(_shake, 0.15f);
                    break;
                case SimEventType.BossTentacleCut:
                {
                    BossIchor(at, 1.6f);
                    _shake = Mathf.Max(_shake, 0.45f);
                    int left = Sim.Boss != null ? Sim.Boss.TentaclesLeft : 0;
                    if (left > 0) _hud.ShowCallout(left + (left == 1 ? " TENTACLE LEFT" : " TENTACLES LEFT"), new Color(0.9f, 0.6f, 0.5f));
                    break;
                }
                case SimEventType.BossExposed:
                    _hud.ShowCallout("STRIKE THE BODY!", new Color(1f, 0.4f, 0.35f));
                    break;
                case SimEventType.BossHurt:
                    BossIchor(at, 0.8f);
                    break;
                case SimEventType.BossDefeated:
                    _hud.ShowCallout(StoryBoss.Name + " DEFEATED", warm);
                    _shake = Mathf.Max(_shake, 1f);
                    BossIchor(at, 3f);
                    break;
                case SimEventType.StoryComplete:
                    if (GameSettings.Current.StoryReaper != 1)
                    {
                        GameSettings.Current.StoryReaper = 1;
                        GameSettings.Save();
                        _hud.ShowCallout("REAPER UNLOCKED", new Color(0.85f, 0.75f, 1f));
                    }
                    break;
            }
        }

        /// <summary>Per frame while a story run plays: props and boss follow the sim, the boss bar shows during the fight,
        /// and beating the boss returns to the menu.</summary>
        private void StoryTick(float dt)
        {
            _storyProps?.Sync(Sim, Time.time);
            _bossView?.Sync(Sim, Time.time);
            var b = Sim.Boss;
            if (b != null && b.Move != BossMove.Dormant)
                _hud.SetBossBar(StoryBoss.Name, b.Hp / Mathf.Max(1f, b.MaxHp), b.TentaclesLeft, StoryBoss.TentacleCount);
            else _hud.SetBossBar(null, 0f, 0, 0);
            if (Sim.State != SimState.Victory) return;
            _storyEndTimer += dt;
            if (_storyEndTimer >= 3.5f) ExitStory("BIG TENTACLE BILL IS DEAD  -  THE REAPER CAN NOW PLAY THE STORY");
        }

        /// <summary>Builds the story props and the boss for a story map's arena.</summary>
        private void BuildStoryProps()
        {
            _storyProps?.Dispose();
            _storyProps = null;
            _bossView?.Dispose();
            _bossView = null;
            if (!_render3D || Sim?.Map.Story == null || _arenaHost == null) return;
            _storyProps = new StoryProps3D(_arenaHost.transform, Sim.Map);
            if (Sim.Boss != null) _bossView = new BossView3D(_arenaHost.transform, Sim.Boss);
        }

        private float _baseFraming = -1f;

        /// <summary>
        /// While Big Tentacle Bill fights (or dies), the camera pulls back (framing height x2.3) and aims between the
        /// player and the boss so both stay in view; afterwards it eases back to the normal framing.
        /// </summary>
        private void StoryBossFraming(ref Vector2 focus, float dt)
        {
            if (_baseFraming < 0f) _baseFraming = _pixelCam.FramingHeight;
            var b = _story ? Sim.Boss : null;
            bool fight = b != null && b.Move != BossMove.Dormant;
            float target = fight ? _baseFraming * 2.3f : _baseFraming;
            _pixelCam.FramingHeight = Mathf.MoveTowards(_pixelCam.FramingHeight, target, dt * _baseFraming * 1.5f);
            if (!fight) return;
            focus = new Vector2(Mathf.Lerp(focus.x, b.Home.X, 0.58f), Mathf.Lerp(focus.y, b.FloorY + 3f, 0.6f));
        }

        /// <summary>Black ichor bursting from Big Tentacle Bill, pooling on its room's floor.</summary>
        private void BossIchor(Vector2 at, float amount)
        {
            var b = Sim.Boss;
            float floor = b != null ? ToDraw(new Vec2(at.x, b.FloorY)).y : at.y - 2f;
            _effects.Blood(at, Sim.Player.X < at.x ? 1f : -1f, floor, amount, true);
        }
    }
}
