using System;

namespace Redline.Core
{
    /// <summary>
    /// Story mode (a ';story' level, <see cref="TileMap.Story"/>): no waves and no game over. Placed enemies spawn when
    /// the player comes near and stay dead until the player dies (then they all come back); dying puts the player back at the last checkpoint (full HP, the
    /// bridge restored). The first checkpoint unlocks the hook; the lever opens gate 0; riding a lift to its top opens
    /// the other gates; fire traps flare on and off and burn everything around them; bridge cells give way under the
    /// player and come back later (gates no lever opens open when a lift reaches its top); reaching the boss room ends the story so far (<see cref="SimEventType.StoryComplete"/>).
    /// The player always has the double jump here (the story map's platforms are ~5 tiles apart).
    /// </summary>
    public sealed partial class RedlineSim
    {
        /// <summary>True on a story level.</summary>
        public bool StoryMode => _map.Story != null;

        /// <summary>False on a story level until the first checkpoint; always true elsewhere.</summary>
        public bool HookUnlocked { get; private set; } = true;

        /// <summary>Index of the last checkpoint reached (-1: none yet, the run restarts at the level's start).</summary>
        public int StoryCheckpoint { get; private set; } = -1;

        /// <summary>True once the first lever is pulled (see <see cref="LeverPulledAt"/> for the others).</summary>
        public bool LeverPulled { get; private set; }

        /// <summary>True while the story fire traps burn.</summary>
        public bool FireLive => _cfg.storyFireOn > 0f && Elapsed % (_cfg.storyFireOn + MathF.Max(0f, _cfg.storyFireOff)) < _cfg.storyFireOn;

        private bool[] _gateOpen = Array.Empty<bool>();
        private bool[] _leverPulled = Array.Empty<bool>();
        private float[] _bridgeTimer = Array.Empty<float>();   // > 0: counting down to crumble (stepped on) or to come back (gone)
        private bool[] _bridgeGone = Array.Empty<bool>();
        private float[] _liftTop = Array.Empty<float>();
        private int[] _liftDir = Array.Empty<int>();
        private float[] _liftPause = Array.Empty<float>();
        private bool[] _spawned = Array.Empty<bool>();
        private int _riding = -1;

        /// <summary>True when gate <paramref name="i"/> is open.</summary>
        public bool GateOpen(int i) => i >= 0 && i < _gateOpen.Length && _gateOpen[i];

        /// <summary>True when bridge cell <paramref name="i"/> (index into StoryLayout.BridgeCells) has crumbled away.</summary>
        public bool BridgeGone(int i) => i >= 0 && i < _bridgeGone.Length && _bridgeGone[i];

        /// <summary>0..1 how close a stepped-on bridge cell is to giving way (0 when idle or gone): the view shakes it.</summary>
        public float BridgeShake(int i) =>
            i < 0 || i >= _bridgeGone.Length || _bridgeGone[i] || _bridgeTimer[i] <= 0f ? 0f
            : 1f - _bridgeTimer[i] / MathF.Max(1e-3f, _cfg.storyBridgeDelay);

        /// <summary>Height of lift <paramref name="i"/>'s platform top.</summary>
        public float LiftTop(int i) => i >= 0 && i < _liftTop.Length ? _liftTop[i] : 0f;

        /// <summary>Resets the story pieces for a fresh run (called from Restart).</summary>
        private void StoryReset()
        {
            var s = _map.Story;
            HookUnlocked = false;
            StoryCheckpoint = -1;
            LeverPulled = false;
            _leverPulled = new bool[s.Levers.Count];
            _gateOpen = new bool[s.Gates.Count];
            foreach (var g in s.Gates) foreach (var c in g) _map.Set(c.x, c.y, Tile.Solid);
            _bridgeTimer = new float[s.BridgeCells.Count];
            _bridgeGone = new bool[s.BridgeCells.Count];
            foreach (var c in s.BridgeCells) _map.Set(c.x, c.y, Tile.Solid);
            _liftTop = new float[s.Lifts.Count];
            _liftDir = new int[s.Lifts.Count];
            _liftPause = new float[s.Lifts.Count];
            for (int i = 0; i < s.Lifts.Count; i++) { _liftTop[i] = s.Lifts[i].Bottom; _liftDir[i] = 1; _liftPause[i] = _cfg.storyLiftPause; }
            _spawned = new bool[s.Spawns.Count];
            _riding = -1;
            BossReset();
        }

        /// <summary>After the player moved: lifts move and carry a rider.</summary>
        private void StoryLifts(float dt)
        {
            var s = _map.Story;
            var p = Player;
            _riding = -1;
            for (int i = 0; i < s.Lifts.Count; i++)
            {
                var t = s.Lifts[i];
                float before = _liftTop[i];
                if (_liftPause[i] > 0f) _liftPause[i] -= dt;
                else
                {
                    _liftTop[i] += _liftDir[i] * _cfg.storyLiftSpeed * dt;
                    if (_liftTop[i] >= t.Top) { _liftTop[i] = t.Top; _liftDir[i] = -1; _liftPause[i] = _cfg.storyLiftPause; }
                    else if (_liftTop[i] <= t.Bottom) { _liftTop[i] = t.Bottom; _liftDir[i] = 1; _liftPause[i] = _cfg.storyLiftPause; }
                }
                float top = _liftTop[i], dy = top - before;
                bool over = p.MaxX > t.MinX && p.MinX < t.MaxX;
                // Enemies stand on it too (fliers excepted).
                for (int k = 0; k < _enemies.Count; k++)
                {
                    var e = _enemies[k];
                    if (!e.Alive || e.Kind == EnemyKind.Drone || !(e.MaxX > t.MinX && e.MinX < t.MaxX)) continue;
                    if (LandsOnLift(e, before, top, dy, dt))
                    {
                        e.Y = top;
                        if (e.Vy < 0f) e.Vy = 0f;
                        e.SetGrounded();
                    }
                }
                // Stand on it (also on the rope: the hook does not let the player drop through): the feet crossed or
                // sit at the top this step while not rising faster than it.
                if (over && LandsOnLift(p, before, top, dy, dt))
                {
                    p.Y = top;
                    if (p.Vy < 0f) p.Vy = 0f;
                    p.SetGrounded();
                    p.AirJumpUsed = false;
                    _riding = i;
                    // Riding it to the top opens the far gates (the way up to the boss).
                    if (top >= t.Top - 0.01f) OpenLiftGates();
                }
            }
        }

        /// <summary>
        /// True when a body lands on (or rides) a lift top this step: its feet were at or above the top before the step
        /// (swept with its velocity, so a fast fall cannot tunnel through) and are at or just below it now, and it is not
        /// rising away faster than the lift.
        /// </summary>
        private static bool LandsOnLift(Body b, float topBefore, float top, float dy, float dt)
        {
            float prevY = b.Y - b.Vy * dt;
            float liftVy = dy / MathF.Max(dt, 1e-4f);
            return b.Y <= top + 0.12f && prevY >= topBefore - 0.12f && b.Vy <= MathF.Max(0f, liftVy) + 0.5f;
        }

        /// <summary>After contacts: fire, bridges, checkpoints, lever, enemy activation, the boss room.</summary>
        private void StoryUpdate(float dt)
        {
            var s = _map.Story;
            var p = Player;
            var c = _cfg;

            // Fire traps burn everything around them while live.
            if (FireLive && p.InvulnTimer <= 0f && p.Hp > 0)
            {
                var pc = p.Center;
                float r = c.storyFireRadius;
                for (int i = 0; i < s.FireCells.Count; i++)
                {
                    var f = s.FireCells[i];
                    float dx = pc.X - (f.x + 0.5f), dy = pc.Y - (f.y + 0.5f);
                    if (MathF.Abs(dx) < r + p.Width * 0.5f && MathF.Abs(dy) < r + p.Height * 0.5f)
                    {
                        Hurt(c.storyFireDamage, dx >= 0f ? 1 : -1, false);
                        break;
                    }
                }
            }

            // Bridge cells: the ones under the player start to give way; gone ones come back after a while.
            for (int i = 0; i < s.BridgeCells.Count; i++)
            {
                var b = s.BridgeCells[i];
                if (_bridgeGone[i])
                {
                    _bridgeTimer[i] -= dt;
                    if (_bridgeTimer[i] <= 0f && !p.Overlaps(b.x, b.y, b.x + 1, b.y + 1))
                    {
                        _bridgeGone[i] = false;
                        _bridgeTimer[i] = 0f;
                        _map.Set(b.x, b.y, Tile.Solid);
                    }
                    continue;
                }
                if (_bridgeTimer[i] > 0f)
                {
                    _bridgeTimer[i] -= dt;
                    if (_bridgeTimer[i] <= 0f)
                    {
                        _bridgeGone[i] = true;
                        _bridgeTimer[i] = c.storyBridgeRespawn;
                        _map.Set(b.x, b.y, Tile.Empty);
                        Emit(SimEventType.BridgeCrumble, new Vec2(b.x + 0.5f, b.y + 0.5f));
                    }
                }
                else if (p.Grounded && MathF.Abs(p.Y - (b.y + 1)) < 0.05f && p.MaxX > b.x - 1f && p.MinX < b.x + 2f)
                    _bridgeTimer[i] = c.storyBridgeDelay;
            }

            // Checkpoints (in x order): reaching a later one saves it; the first one gives the hook.
            for (int i = StoryCheckpoint + 1; i < s.Checkpoints.Count; i++)
            {
                var cp = s.Checkpoints[i];
                if (MathF.Abs(p.X - cp.X) < 1.6f && p.Y > cp.Y - 2.5f && p.Y < cp.Y + 3f)
                {
                    StoryCheckpoint = i;
                    p.Hp = c.playerMaxHp;
                    Emit(SimEventType.StoryCheckpoint, cp, PlayerTier, i);
                    if (!HookUnlocked)
                    {
                        HookUnlocked = true;
                        Emit(SimEventType.HookUnlocked, cp);
                    }
                }
            }

            // Levers: touch one to open its gate (StoryLayout.LeverGate).
            for (int i = 0; i < s.Levers.Count; i++)
            {
                if (_leverPulled[i]) continue;
                var l = s.Levers[i];
                if (MathF.Abs(p.X - l.X) < 1.2f && p.Y > l.Y - 1.5f && p.Y < l.Y + 2f)
                {
                    _leverPulled[i] = true;
                    if (i == 0) LeverPulled = true;
                    Emit(SimEventType.LeverPulled, l, PlayerTier, i);
                    OpenGate(s.LeverGate[i]);
                }
            }

            // Placed enemies wake up when the player comes near.
            float range = c.storyActivateRange;
            for (int i = 0; i < s.Spawns.Count; i++)
            {
                if (_spawned[i]) continue;
                var sp = s.Spawns[i];
                if (MathF.Abs(sp.Position.X - p.X) > range || MathF.Abs(sp.Position.Y - p.Y) > range * 0.5f) continue;
                _spawned[i] = true;
                SpawnEnemy(sp.Kind, sp.Kind == EnemyKind.Drone ? new Vec2(sp.Position.X, sp.Position.Y + 1.5f) : sp.Position);
            }

            // The boss room: Big Tentacle Bill (RedlineSim.Boss.cs); beating it completes the story so far.
            BossUpdate(dt);
        }

        /// <summary>The player died on a story level: back to the last checkpoint with full HP, the bridge restored.</summary>
        private void StoryRespawn()
        {
            var s = _map.Story;
            var at = StoryCheckpoint >= 0 ? s.Checkpoints[StoryCheckpoint] : _map.PlayerStart;
            ReleaseHook();
            RespawnPlayer(at);
            Player.InvulnTimer = _cfg.invulnTime;
            // Dying brings every enemy back (killed ones too): they wake again at their posts as the player comes near.
            ClearActors();
            Array.Clear(_spawned, 0, _spawned.Length);
            BossReset();   // the boss fight starts over too
            for (int i = 0; i < s.BridgeCells.Count; i++)
            {
                var b = s.BridgeCells[i];
                _bridgeGone[i] = false;
                _bridgeTimer[i] = 0f;
                _map.Set(b.x, b.y, Tile.Solid);
            }
            for (int i = 0; i < _projectiles.Count; i++) _freeProjectiles.Add(_projectiles[i]);
            _projectiles.Clear();
            Emit(SimEventType.StoryRespawn, at);
        }

        private void OpenGate(int i)
        {
            var s = _map.Story;
            if (i < 0 || i >= s.Gates.Count || _gateOpen[i]) return;
            _gateOpen[i] = true;
            float sx = 0f, sy = 0f;
            foreach (var c in s.Gates[i]) { _map.Set(c.x, c.y, Tile.Empty); sx += c.x + 0.5f; sy += c.y + 0.5f; }
            int n = Math.Max(1, s.Gates[i].Count);
            Emit(SimEventType.GateOpen, new Vec2(sx / n, sy / n), PlayerTier, i);
        }

        /// <summary>A lift reached its top: opens every gate that no lever opens.</summary>
        private void OpenLiftGates()
        {
            for (int i = 0; i < _gateOpen.Length; i++)
                if (!_map.Story.GateHasLever(i)) OpenGate(i);
        }

        /// <summary>True when lever <paramref name="i"/> has been pulled.</summary>
        public bool LeverPulledAt(int i) => i >= 0 && i < _leverPulled.Length && _leverPulled[i];

        private bool AllGatesOpen()
        {
            for (int i = 0; i < _gateOpen.Length; i++) if (!_gateOpen[i]) return false;
            return true;
        }
    }
}
