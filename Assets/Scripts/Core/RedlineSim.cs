using System;
using System.Collections.Generic;

namespace Redline.Core
{
    /// <summary>
    /// The whole game rules, engine-free and deterministic. Call <see cref="Step"/> at a fixed
    /// rate (<see cref="FixedDt"/>); read <see cref="Player"/>, <see cref="Enemies"/>,
    /// <see cref="State"/> and <see cref="Events"/> to present it.
    /// </summary>
    /// <example>
    /// <code>
    /// var sim = new RedlineSim(new RedlineConfig(), TileMap.Parse(text));
    /// sim.Step(new InputFrame { MoveX = 1f }, RedlineSim.FixedDt);
    /// </code>
    /// </example>
    /// <remarks>Single-thread only. Step does not allocate except when a wave spawns.</remarks>
    public sealed class RedlineSim
    {
        /// <summary>The fixed step the rules are tuned for (60 Hz).</summary>
        public const float FixedDt = 1f / 60f;

        /// <summary>Stick deflection below which horizontal input counts as neutral.</summary>
        private const float MoveDeadZone = 0.2f;

        private static readonly Predicate<Enemy> IsDead = e => !e.Alive;

        private readonly RedlineConfig _cfg;
        private readonly TileMap _map;
        private readonly List<Enemy> _enemies = new List<Enemy>(32);
        private readonly List<SimEvent> _events = new List<SimEvent>(32);
        private readonly List<Orb> _orbs;
        private readonly IReadOnlyList<WaveSpec> _waves;
        private readonly List<Orb> _freeOrbs;
        private readonly List<Projectile> _projectiles;
        private readonly List<Projectile> _freeProjectiles;
        /// <summary>xorshift32 state of the sim's own RNG (orb drops); reset from rngSeed on Restart.</summary>
        private uint _rng;

        public RedlineConfig Config => _cfg;
        public TileMap Map => _map;
        public Player Player { get; private set; }
        public IReadOnlyList<Enemy> Enemies => _enemies;

        /// <summary>
        /// Orbs lying in the level (kind, position, <see cref="Orb.TimeLeft"/>). Pooled: an entry is
        /// reused once it is picked up or expires.
        /// </summary>
        public IReadOnlyList<Orb> Orbs => _orbs;

        /// <summary>
        /// Shooter projectiles in flight (position, velocity, <see cref="Projectile.Reflected"/>).
        /// Pooled like <see cref="Orbs"/>: an entry is reused once the shot ends.
        /// </summary>
        public IReadOnlyList<Projectile> Projectiles => _projectiles;

        /// <summary>Events emitted by the last Step (cleared at the start of each Step).</summary>
        public IReadOnlyList<SimEvent> Events => _events;

        public SimState State { get; private set; }

        /// <summary>0-based index of the wave that is running, or that comes next during a break.</summary>
        public int WaveIndex { get; private set; }

        /// <summary>1-based wave number for display.</summary>
        public int CurrentWave => Math.Min(WaveIndex + 1, WaveCount);

        /// <summary>Waves in this level: the map's ';wave' lines, or the config's waves when it has none.</summary>
        public int WaveCount => _waves.Count;

        /// <summary>The waves this run plays, in order.</summary>
        public IReadOnlyList<WaveSpec> Waves => _waves;

        /// <summary>Seconds left before the next wave spawns (WaveBreak only).</summary>
        public float BreakTimer { get; private set; }

        public int EnemiesAlive { get; private set; }

        public int Kills { get; private set; }

        /// <summary>Increments on every <see cref="Restart"/>, so views can tell a fresh run apart.</summary>
        public int RunId { get; private set; }

        /// <summary>The player's current speed tier (1-3), from horizontal speed.</summary>
        public int PlayerTier => CombatRules.TierFor(Player.Vx, _cfg);

        /// <summary>
        /// Creates a run. Throws ArgumentException when the config is invalid or the level's waves
        /// ask for an enemy kind the sim cannot spawn yet.
        /// </summary>
        public RedlineSim(RedlineConfig config, TileMap map)
        {
            _cfg = config ?? throw new ArgumentNullException(nameof(config));
            _map = map ?? throw new ArgumentNullException(nameof(map));
            var errors = _cfg.Validate();
            if (errors.Count > 0) throw new ArgumentException("Invalid RedlineConfig: " + string.Join("; ", errors));
            _waves = _map.Waves.Count > 0 ? _map.Waves : WaveSpec.FromConfig(_cfg);
            var waveErrors = ValidateWaves(_waves);
            if (waveErrors.Count > 0) throw new ArgumentException("Invalid level waves: " + string.Join("; ", waveErrors));
            _orbs = new List<Orb>(_cfg.orbMaxCount);
            _freeOrbs = new List<Orb>(_cfg.orbMaxCount);
            for (int i = 0; i < _cfg.orbMaxCount; i++) _freeOrbs.Add(new Orb());
            _projectiles = new List<Projectile>(_cfg.projectileMaxCount);
            _freeProjectiles = new List<Projectile>(_cfg.projectileMaxCount);
            for (int i = 0; i < _cfg.projectileMaxCount; i++) _freeProjectiles.Add(new Projectile());
            Restart();
        }

        /// <summary>Starts again from wave 1 with a fresh player.</summary>
        public void Restart()
        {
            _enemies.Clear();
            _events.Clear();
            for (int i = 0; i < _orbs.Count; i++) _freeOrbs.Add(_orbs[i]);
            _orbs.Clear();
            for (int i = 0; i < _projectiles.Count; i++) _freeProjectiles.Add(_projectiles[i]);
            _projectiles.Clear();
            _rng = SeedState(_cfg.rngSeed);
            var p = new Player
            {
                X = _map.PlayerStart.X,
                Y = _map.PlayerStart.Y,
                Width = _cfg.playerWidth,
                Height = _cfg.playerHeight,
                Hp = _cfg.playerMaxHp,
                Facing = 1
            };
            p.RefreshContacts(_map);
            Player = p;
            State = SimState.WaveBreak;
            WaveIndex = 0;
            BreakTimer = _cfg.firstWaveDelay;
            EnemiesAlive = 0;
            Kills = 0;
            RunId++;
        }

        /// <summary>Spawns an enemy directly (used by tests and tools; waves use the same path).</summary>
        public Enemy SpawnEnemy(EnemyKind kind, Vec2 position)
        {
            var e = new Enemy(kind, new Vec2(_map.WrapX(position.X), position.Y), _cfg);
            e.RefreshContacts(_map);
            _enemies.Add(e);
            EnemiesAlive++;
            return e;
        }

        /// <summary>
        /// Drops an orb at a point (kills use the same path). Returns null when the pool
        /// (orbMaxCount) is full. Emits <see cref="SimEventType.OrbSpawn"/>.
        /// </summary>
        public Orb SpawnOrb(OrbKind kind, Vec2 position)
        {
            if (_freeOrbs.Count == 0) return null;
            var o = _freeOrbs[_freeOrbs.Count - 1];
            _freeOrbs.RemoveAt(_freeOrbs.Count - 1);
            o.Kind = kind;
            o.Width = _cfg.orbSize;
            o.Height = _cfg.orbSize;
            o.X = _map.WrapX(position.X);
            o.Y = position.Y;
            o.Vx = 0f;
            o.Vy = _cfg.orbPopSpeed;
            o.TimeLeft = _cfg.orbLifetime;
            o.Age = 0f;
            o.RefreshContacts(_map);
            _orbs.Add(o);
            Emit(SimEventType.OrbSpawn, o.Center, PlayerTier, (int)kind);
            return o;
        }

        /// <summary>Advances the run by dt seconds.</summary>
        public void Step(InputFrame input, float dt)
        {
            _events.Clear();
            if (input.RestartPressed)
            {
                Restart();
                return;
            }
            if (State == SimState.Victory || State == SimState.GameOver) return;

            if (State == SimState.WaveBreak)
            {
                BreakTimer -= dt;
                if (BreakTimer <= 0f) StartWave();
            }

            UpdatePlayer(input, dt);
            UpdateEnemies(dt);
            ResolveStrike();
            UpdateProjectiles(dt);
            ResolveContacts();

            if (Player.Hp <= 0)
            {
                Player.Hp = 0;
                ReleaseHook();
                State = SimState.GameOver;
                Emit(SimEventType.GameOver, Player.Center);
                return;
            }
            UpdateOrbs(dt);
            UpdateWaveProgress();
        }

        /// <summary>
        /// The player's strike hitbox for the current facing (valid whether or not striking): the
        /// sword's arc, from the feet to just above the head, reaching forward by attackRange.
        /// </summary>
        public void GetStrikeBox(out float minX, out float minY, out float maxX, out float maxY)
        {
            var p = Player;
            minY = p.Y + 0.1f;
            maxY = p.MaxY + _cfg.attackReachUp;
            if (p.Facing > 0)
            {
                minX = p.X - _cfg.strikeBackReach;
                maxX = p.MaxX + _cfg.attackRange;
            }
            else
            {
                minX = p.MinX - _cfg.attackRange;
                maxX = p.X + _cfg.strikeBackReach;
            }
        }

        // ------------------------------------------------------------------ waves

        private void UpdateWaveProgress()
        {
            int alive = 0;
            for (int i = 0; i < _enemies.Count; i++)
                if (_enemies[i].Alive) alive++;
            EnemiesAlive = alive;
            if (State != SimState.Fighting || alive > 0) return;

            WaveIndex++;
            if (WaveIndex >= WaveCount)
            {
                WaveIndex = WaveCount - 1;
                State = SimState.Victory;
                Emit(SimEventType.Victory, Player.Center);
                return;
            }
            State = SimState.WaveBreak;
            BreakTimer = _cfg.waveBreakTime;
            Player.Hp = Math.Min(_cfg.playerMaxHp, Player.Hp + _cfg.waveClearHeal);
        }

        private void StartWave()
        {
            _enemies.RemoveAll(IsDead);
            var wave = _waves[WaveIndex];
            bool charging = WaveIndex + 1 >= _cfg.bruteChargeFromWave;
            int k = 0;
            // Spawn order (and so spawn-point assignment): brutes, grunts, drones, then the rest in
            // WaveEnemy order.
            for (int i = 0; i < wave[WaveEnemy.Brute]; i++) SpawnAtPoint(EnemyKind.Brute, k++).Charges = charging;
            for (int i = 0; i < wave[WaveEnemy.Grunt]; i++) SpawnAtPoint(EnemyKind.Grunt, k++);
            for (int i = 0; i < wave[WaveEnemy.Drone]; i++) SpawnAtPoint(EnemyKind.Drone, k++);
            for (int w = (int)WaveEnemy.Drone + 1; w < WaveSpec.KindCount; w++)
            {
                if (!TryGetEnemyKind((WaveEnemy)w, out var kind)) continue; // rejected by ValidateWaves
                for (int i = 0; i < wave[(WaveEnemy)w]; i++) SpawnAtPoint(kind, k++);
            }
            State = SimState.Fighting;
            Emit(SimEventType.WaveStart, Player.Center);
        }

        /// <summary>
        /// The enemy kind a wave entry spawns; false for kinds not implemented yet. Adding a kind:
        /// add its EnemyKind, return it here, and give it an orb chance in OrbChance.
        /// </summary>
        public static bool TryGetEnemyKind(WaveEnemy wave, out EnemyKind kind)
        {
            switch (wave)
            {
                case WaveEnemy.Grunt: kind = EnemyKind.Grunt; return true;
                case WaveEnemy.Brute: kind = EnemyKind.Brute; return true;
                case WaveEnemy.Drone: kind = EnemyKind.Drone; return true;
                case WaveEnemy.Shooter: kind = EnemyKind.Shooter; return true;
                case WaveEnemy.Shield: kind = EnemyKind.Shield; return true;
                case WaveEnemy.Kamikaze: kind = EnemyKind.Kamikaze; return true;
                case WaveEnemy.Summoner: kind = EnemyKind.Summoner; return true;
                default: kind = default; return false;
            }
        }

        /// <summary>Problems with a wave list: an enemy kind the sim cannot spawn yet. Empty when playable.</summary>
        public static List<string> ValidateWaves(IReadOnlyList<WaveSpec> waves)
        {
            var errors = new List<string>();
            if (waves.Count == 0) errors.Add("no waves");
            for (int i = 0; i < waves.Count; i++)
                for (int w = 0; w < WaveSpec.KindCount; w++)
                    if (waves[i][(WaveEnemy)w] > 0 && !TryGetEnemyKind((WaveEnemy)w, out _))
                        errors.Add($"wave {i + 1} asks for {waves[i][(WaveEnemy)w]} {WaveSpec.KeyOf((WaveEnemy)w)}, which is not implemented yet");
            return errors;
        }

        private Enemy SpawnAtPoint(EnemyKind kind, int k)
        {
            var points = _map.SpawnPoints;
            var pt = points[k % points.Count];
            int ring = k / points.Count;
            float offset = (ring % 2 == 0 ? 1f : -1f) * ((ring + 1) / 2) * _cfg.spawnSpacing;
            // Drones appear in the air above the spawn point.
            float lift = kind == EnemyKind.Drone ? 2.5f : 0f;
            var e = SpawnEnemy(kind, new Vec2(pt.X + offset, pt.Y + lift));
            if (_map.OverlapsSolid(e.MinX, e.Y, e.MaxX, e.MaxY))
            {
                e.X = pt.X;
                e.Y = pt.Y;
                e.RefreshContacts(_map);
            }
            return e;
        }

        // ------------------------------------------------------------------ player

        private void UpdatePlayer(InputFrame input, float dt)
        {
            var p = Player;
            TickPlayerTimers(p, input, dt);
            float move = Math.Clamp(input.MoveX, -1f, 1f);
            int dir = move > MoveDeadZone ? 1 : move < -MoveDeadZone ? -1 : 0;

            UpdateFacing(p, dir);
            if (p.Hooked && !input.HookHeld) ReleaseHook(voluntary: true);
            if (p.HookBufferTimer > 0f && input.HookHeld && !p.Hooked && p.HookCooldownTimer <= 0f) TryAttachHook();
            TryStartAttack(p);
            TryStartSlide(p);

            HorizontalControl(p, dir, dt);
            HandleJump(p, input);
            ApplyGravity(p, dir, dt);
            ApplyRope(p, dt);

            LimitSpeed(p, dt);
            float impact = p.Move(_map, dt);
            if (impact > 0f)
            {
                p.WallImpactSpeed = MathF.Max(p.WallImpactSpeed, impact);
                p.WallImpactTimer = _cfg.wallImpactMemory;
            }
        }

        private void TickPlayerTimers(Player p, InputFrame input, float dt)
        {
            var c = _cfg;
            p.InvulnTimer = Dec(p.InvulnTimer, dt);
            p.WallJumpLockTimer = Dec(p.WallJumpLockTimer, dt);
            p.WallImpactTimer = Dec(p.WallImpactTimer, dt);
            p.SlideCooldownTimer = Dec(p.SlideCooldownTimer, dt);
            p.HookCooldownTimer = Dec(p.HookCooldownTimer, dt);
            if (p.Hooked) p.HookTime += dt;
            p.AttackCooldownTimer = Dec(p.AttackCooldownTimer, dt);
            p.AttackTimer = Dec(p.AttackTimer, dt);
            p.JumpBufferTimer = Dec(p.JumpBufferTimer, dt);
            p.AttackBufferTimer = Dec(p.AttackBufferTimer, dt);
            p.SlideBufferTimer = Dec(p.SlideBufferTimer, dt);
            p.HookBufferTimer = Dec(p.HookBufferTimer, dt);
            p.CoyoteTimer = Dec(p.CoyoteTimer, dt);
            p.SpeedBuffTimer = MathF.Max(0f, p.SpeedBuffTimer - dt);
            p.HookBuffTimer = MathF.Max(0f, p.HookBuffTimer - dt);
            p.ChainTimer = Dec(p.ChainTimer, dt);
            if (p.ChainTimer <= 0f) p.Chain = 0;

            if (input.JumpPressed) p.JumpBufferTimer = c.jumpBufferTime;
            if (input.AttackPressed) p.AttackBufferTimer = c.inputBufferTime;
            if (input.SlidePressed) p.SlideBufferTimer = c.inputBufferTime;
            if (input.HookPressed) p.HookBufferTimer = c.inputBufferTime;
            if (p.Grounded) p.CoyoteTimer = c.coyoteTime;
            if (p.WallImpactTimer <= 0f) p.WallImpactSpeed = 0f;
        }

        /// <summary>
        /// Facing follows the stick. Only while swinging does it follow velocity, so a clang or
        /// a knockback never turns the player away from the enemy they were fighting.
        /// </summary>
        private static void UpdateFacing(Player p, int dir)
        {
            if (p.Hooked)
            {
                if (MathF.Abs(p.Vx) > 0.5f) p.Facing = Sign(p.Vx);
            }
            else if (dir != 0 && p.WallJumpLockTimer <= 0f)
            {
                p.Facing = dir;
            }
        }

        private void TryStartAttack(Player p)
        {
            if (p.AttackBufferTimer <= 0f || p.AttackCooldownTimer > 0f) return;
            p.AttackBufferTimer = 0f;
            p.AttackTimer = _cfg.attackActiveTime;
            p.AttackCooldownTimer = _cfg.attackCooldown;
            p.StrikeId++;
            Emit(SimEventType.Attack, p.Center);
        }

        private void TryStartSlide(Player p)
        {
            var c = _cfg;
            if (p.SlideBufferTimer <= 0f || !p.Grounded || p.Sliding || p.Hooked) return;
            if (p.SlideCooldownTimer > 0f || MathF.Abs(p.Vx) < c.slideMinSpeed) return;
            p.SlideBufferTimer = 0f;
            p.Sliding = true;
            p.SlideTimer = c.slideDuration;
            // Never slows the player: air overspeed carried into a slide is kept.
            float speed = MathF.Abs(p.Vx);
            p.Vx = Sign(p.Vx) * MathF.Max(speed, MathF.Min(speed + c.slideBoost, MaxSpeed(p)));
            p.Height = c.slideHeight;
            Emit(SimEventType.Slide, p.Center);
        }

        private void HorizontalControl(Player p, int dir, float dt)
        {
            if (p.Hooked)
            {
                if (dir != 0) PumpSwing(p, dir, dt);
                return;
            }
            if (p.Sliding)
            {
                p.SlideTimer -= dt;
                bool slideOver = p.SlideTimer <= 0f;
                if (slideOver || !p.Grounded) TryEndSlide();
                // Still low after the slide (no head room): crawl with normal ground rules.
                if (p.Sliding && slideOver && p.Grounded) GroundControl(p, dir, dt);
                return;
            }
            if (p.Grounded)
            {
                GroundControl(p, dir, dt);
                return;
            }
            if (dir == 0 || p.WallJumpLockTimer > 0f) return;
            float run = RunSpeed(p);
            bool keepingOverspeed = Sign(p.Vx) == dir && MathF.Abs(p.Vx) >= run;
            if (!keepingOverspeed) p.Vx = MoveToward(p.Vx, dir * run, _cfg.airAccel * dt);
        }

        private void GroundControl(Player p, int dir, float dt)
        {
            var c = _cfg;
            float run = RunSpeed(p);
            if (dir == 0)
            {
                p.Vx = MoveToward(p.Vx, 0f, c.groundFriction * dt);
            }
            else if (Sign(p.Vx) == dir && MathF.Abs(p.Vx) > run)
            {
                p.Vx = dir * MathF.Max(run, MathF.Abs(p.Vx) - c.overspeedDecay * dt);
            }
            else
            {
                p.Vx = MoveToward(p.Vx, dir * run, c.groundAccel * dt);
            }
        }

        private void HandleJump(Player p, InputFrame input)
        {
            var c = _cfg;
            if (p.JumpBufferTimer > 0f)
            {
                if (p.Hooked)
                {
                    ReleaseHook(voluntary: true);
                    if (p.Vy < c.hookJumpSpeed) p.Vy = c.hookJumpSpeed;
                    ConsumeJump(p);
                    Emit(SimEventType.Jump, p.Center);
                }
                else if (p.Grounded || p.CoyoteTimer > 0f)
                {
                    if (p.Sliding) TryEndSlide();
                    p.Vy = c.jumpSpeed;
                    p.JumpCutAvailable = true;
                    ConsumeJump(p);
                    Emit(SimEventType.Jump, p.Center);
                }
                else if (p.WallDir != 0)
                {
                    WallJump(p);
                }
            }
            if (!input.JumpHeld && p.JumpCutAvailable && p.Vy > 0f)
            {
                p.Vy *= c.jumpCutMultiplier;
                p.JumpCutAvailable = false;
            }
            if (p.Vy <= 0f) p.JumpCutAvailable = false;
        }

        private void WallJump(Player p)
        {
            var c = _cfg;
            float speed = MathF.Max(c.wallJumpSpeedX, p.WallImpactSpeed) + c.wallJumpKick;
            p.Vx = -p.WallDir * MathF.Min(speed, MaxSpeed(p));
            p.Vy = c.wallJumpSpeedY;
            p.Facing = -p.WallDir;
            p.WallJumpLockTimer = c.wallJumpLockTime;
            p.WallImpactSpeed = 0f;
            p.JumpCutAvailable = false;
            ConsumeJump(p);
            Emit(SimEventType.WallJump, p.Center);
        }

        private void ApplyGravity(Player p, int dir, float dt)
        {
            var c = _cfg;
            p.Vy -= c.gravity * dt;
            bool wallSliding = !p.Grounded && !p.Hooked && p.WallDir != 0 && dir == p.WallDir;
            if (wallSliding && p.Vy < -c.wallSlideMaxFall) p.Vy = -c.wallSlideMaxFall;
            if (p.Vy < -c.maxFallSpeed) p.Vy = -c.maxFallSpeed;
        }

        /// <summary>Rope constraint: keep the predicted centre within the rope length.</summary>
        private void ApplyRope(Player p, float dt)
        {
            if (!p.Hooked) return;
            var centre = p.Center;
            var anchor = _map.Near(centre, p.HookAnchor);
            // The rope reels in while the player moves toward the anchor, so it is always taut and
            // the swing starts the moment they would move away (no slack "delay").
            p.HookLength = MathF.Max(_cfg.hookMinLength, MathF.Min(p.HookLength, (centre - anchor).Length));
            var predicted = new Vec2(centre.X + p.Vx * dt, centre.Y + p.Vy * dt);
            var d = predicted - anchor;
            float len = d.Length;
            if (len <= p.HookLength || len <= 1e-4f) return;
            var onRope = anchor + d * (p.HookLength / len);
            p.Vx = (onRope.X - centre.X) / dt;
            p.Vy = (onRope.Y - centre.Y) / dt;
        }

        private void PumpSwing(Player p, int dir, float dt)
        {
            var d = p.Center - _map.Near(p.Center, p.HookAnchor);
            float len = d.Length;
            if (len < 1e-3f || d.Y >= 0f) return; // only pump while hanging below the anchor
            float tx = -d.Y / len;
            float ty = d.X / len;
            if (tx * dir < 0f)
            {
                tx = -tx;
                ty = -ty;
            }
            p.Vx += tx * _cfg.hookPumpAccel * dt;
            p.Vy += ty * _cfg.hookPumpAccel * dt;
        }

        private void TryAttachHook()
        {
            var p = Player;
            var c = _cfg;
            // No head room to stand up from a slide: keep the press buffered, spend nothing.
            if (p.Sliding && !TryEndSlide()) return;
            p.HookBufferTimer = 0f;
            p.HookCooldownTimer = HookCooldown(p);
            float a = c.hookAngleDeg * (MathF.PI / 180f);
            var dir = new Vec2(MathF.Cos(a) * p.Facing, MathF.Sin(a));
            float range = c.hookRange + (p.HookBuffTimer > 0f ? c.orbHookRangeBonus : 0f);
            if (!_map.Raycast(p.Center, dir, range, out var hit)) return;
            p.Hooked = true;
            p.HookTime = 0f;
            p.HookAnchor = new Vec2(_map.WrapX(hit.X), hit.Y);
            var toAnchor = hit - p.Center;
            float distance = toAnchor.Length;
            p.HookLength = MathF.Max(c.hookMinLength, distance);
            // From the ground, yank toward the anchor at once so the hook acts the moment it lands
            // (in the air the self-tightening rope already engages within a step or two). It mostly
            // lifts: the sideways part is small, so tier 3 still has to come from the swing itself.
            if (distance > 1e-3f && p.Grounded)
            {
                p.Vx += toAnchor.X / distance * c.hookAttachPull * 0.3f;
                p.Vy = MathF.Max(p.Vy, 0f) + toAnchor.Y / distance * c.hookAttachPull;
            }
            Emit(SimEventType.HookAttach, hit);
        }

        /// <summary>
        /// Lets go of the rope. A voluntary release in the air after a real swing (on the rope at
        /// least hookReleaseMinTime) flings the player on along their velocity: the boost ramps
        /// from 0 at hookReleaseBoostMinSpeed to hookReleaseBoost at maxSpeed, so tapping the hook
        /// is never a free dash. Hurt, spike and game-over releases give nothing.
        /// </summary>
        private void ReleaseHook(bool voluntary = false)
        {
            var p = Player;
            if (!p.Hooked) return;
            p.Hooked = false;
            p.HookCooldownTimer = HookCooldown(p);
            if (voluntary && !p.Grounded && p.HookTime >= _cfg.hookReleaseMinTime) Fling(p);
            Emit(SimEventType.HookRelease, p.Center);
        }

        private void Fling(Player p)
        {
            var c = _cfg;
            float speed = MathF.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy);
            float max = MaxSpeed(p);
            if (speed < 1e-3f || max <= c.hookReleaseBoostMinSpeed) return;
            float ramp = Math.Clamp((speed - c.hookReleaseBoostMinSpeed) / (max - c.hookReleaseBoostMinSpeed), 0f, 1f);
            float boost = c.hookReleaseBoost * ramp / speed;
            p.Vx += p.Vx * boost;
            p.Vy += p.Vy * boost;
            float air = AirMaxSpeed(p);
            p.Vx = Math.Clamp(p.Vx, -air, air);
        }

        /// <summary>
        /// Caps horizontal speed. In the air (swinging or not) speed above maxSpeed is allowed up
        /// to airMaxSpeed and decays toward maxSpeed at airOverspeedDecay per second; on the ground
        /// the ground rules (overspeedDecay, friction) bring it down.
        /// </summary>
        private void LimitSpeed(Player p, float dt)
        {
            float speed = MathF.Abs(p.Vx);
            float max = MaxSpeed(p);
            if (speed > max && !p.Grounded) speed = MathF.Max(max, speed - _cfg.airOverspeedDecay * dt);
            speed = MathF.Min(speed, AirMaxSpeed(p));
            p.Vx = Sign(p.Vx) * speed;
        }

        /// <summary>Hook cooldown; none while the hook orb buff runs.</summary>
        private float HookCooldown(Player p) => p.HookBuffTimer > 0f ? 0f : _cfg.hookCooldown;

        /// <summary>Run speed with the speed buff applied.</summary>
        private float RunSpeed(Player p) => _cfg.runSpeed * SpeedScale(p);

        /// <summary>Normal speed cap with the speed buff applied.</summary>
        private float MaxSpeed(Player p) => _cfg.maxSpeed * SpeedScale(p);

        /// <summary>Air overspeed cap with the speed buff applied.</summary>
        private float AirMaxSpeed(Player p) => _cfg.airMaxSpeed * SpeedScale(p);

        private float SpeedScale(Player p) => p.SpeedBuffTimer > 0f ? _cfg.orbSpeedMultiplier : 1f;

        /// <summary>Stands up from a slide if there is head room. Returns true when no longer sliding.</summary>
        private bool TryEndSlide()
        {
            var p = Player;
            if (!p.Sliding) return true;
            if (_map.OverlapsSolid(p.MinX, p.Y, p.MaxX, p.Y + _cfg.playerHeight)) return false;
            p.Sliding = false;
            p.Height = _cfg.playerHeight;
            p.SlideCooldownTimer = _cfg.slideCooldown;
            return true;
        }

        private static void ConsumeJump(Player p)
        {
            p.JumpBufferTimer = 0f;
            p.CoyoteTimer = 0f;
        }

        // ------------------------------------------------------------------ enemies and combat

        private void UpdateEnemies(float dt)
        {
            var c = _cfg;
            for (int i = 0; i < _enemies.Count; i++)
            {
                var e = _enemies[i];
                if (!e.Alive) continue;
                e.SinceHit += dt;
                if (e.Kind == EnemyKind.Drone)
                {
                    UpdateDrone(e, dt);
                    continue;
                }
                // A lit fuse burns down even while the kamikaze is knocked about.
                if (e.Kind == EnemyKind.Kamikaze && e.Act == EnemyAct.Fuse)
                {
                    e.ModeTimer -= dt;
                    if (e.ModeTimer <= 0f)
                    {
                        Explode(e);
                        continue;
                    }
                }
                if (e.StunTimer > 0f)
                {
                    e.StunTimer -= dt;
                    if (e.Grounded) e.Vx = MoveToward(e.Vx, 0f, c.enemyFriction * dt);
                    CancelTelegraph(e);
                }
                else
                {
                    switch (e.Kind)
                    {
                        case EnemyKind.Shooter: SteerShooter(e, dt); break;
                        case EnemyKind.Kamikaze: SteerKamikaze(e); break;
                        case EnemyKind.Summoner: SteerSummoner(e, dt); break;
                        default:
                            if (!(e.Charges && UpdateCharge(e, dt))) SteerEnemy(e);
                            break;
                    }
                }
                if (e.Kind == EnemyKind.Shield) TurnShield(e, dt);
                e.Vy -= c.gravity * dt;
                if (e.Vy < -c.maxFallSpeed) e.Vy = -c.maxFallSpeed;
                e.Move(_map, dt);
                if (e.Mode == BruteMode.Charge && e.WallDir == e.ChargeDir) CrashBrute(e);
            }
        }

        /// <summary>
        /// Charging brute state machine. Returns true when it set the brute's motion this step,
        /// false when the brute should simply walk.
        /// </summary>
        private bool UpdateCharge(Enemy e, float dt)
        {
            var c = _cfg;
            var p = Player;
            e.ModeTimer -= dt;
            switch (e.Mode)
            {
                case BruteMode.Walk:
                    e.ChargeCooldown -= dt;
                    float dx = _map.Dx(e.X, p.X);
                    bool sameLevel = MathF.Abs(p.Y - e.Y) < 1f && p.Grounded;
                    if (e.ChargeCooldown > 0f || !e.Grounded || !sameLevel || MathF.Abs(dx) > c.bruteChargeRange) return false;
                    e.Mode = BruteMode.Windup;
                    e.ModeTimer = c.bruteWindupTime;
                    e.ChargeDir = dx >= 0f ? 1 : -1;
                    e.Facing = e.ChargeDir;
                    e.Vx = 0f;
                    Emit(SimEventType.BruteWindup, e.Center);
                    return true;
                case BruteMode.Windup:
                    e.Vx = 0f;
                    if (e.ModeTimer <= 0f) SetBruteMode(e, BruteMode.Charge, c.bruteChargeTime);
                    return true;
                case BruteMode.Charge:
                    e.Vx = e.ChargeDir * c.bruteChargeSpeed;
                    if (e.ModeTimer <= 0f) SetBruteMode(e, BruteMode.Recover, c.bruteRecoverTime);
                    return true;
                default: // Recover, Crashed
                    e.Vx = MoveToward(e.Vx, 0f, c.enemyFriction * dt);
                    if (e.ModeTimer <= 0f)
                    {
                        e.Mode = BruteMode.Walk;
                        e.ChargeCooldown = c.bruteChargeCooldown;
                    }
                    return true;
            }
        }

        private static void SetBruteMode(Enemy e, BruteMode mode, float time)
        {
            e.Mode = mode;
            e.ModeTimer = time;
        }

        private void CrashBrute(Enemy e)
        {
            SetBruteMode(e, BruteMode.Crashed, _cfg.bruteCrashTime);
            e.Vx = 0f;
            Emit(SimEventType.BruteCrash, e.Center);
        }

        /// <summary>
        /// Drone flight: hover above the player out of ground reach, telegraph when overhead,
        /// dive at where the player was, then climb back. Ignores gravity; collides with terrain.
        /// </summary>
        private void UpdateDrone(Enemy e, float dt)
        {
            var c = _cfg;
            var p = Player;
            e.ModeTimer -= dt;
            if (e.StunTimer > 0f)
            {
                e.StunTimer -= dt;
                e.Vx = MoveToward(e.Vx, 0f, c.enemyFriction * dt);
                e.Vy = MoveToward(e.Vy, 0f, c.enemyFriction * dt);
            }
            else
            {
                SteerDrone(e, p);
            }
            e.Move(_map, dt);
            // A dive slides along walls; it ends on landing, when fully stopped, or against a ceiling.
            bool blocked = e.Grounded || (e.Vx == 0f && e.Vy == 0f) || (e.DiveDir.Y > 0f && e.Vy == 0f);
            if (e.DroneState == DroneMode.Dive && (e.ModeTimer <= 0f || blocked))
            {
                e.DroneState = DroneMode.Climb;
            }
        }

        private void SteerDrone(Enemy e, Player p)
        {
            var c = _cfg;
            if (e.DroneState == DroneMode.Hover && !Aggro(e, c.droneAggroRange))
            {
                FlyToward(e, e.Post.X, e.Post.Y, c.droneSpeed);
                return;
            }
            ChooseHoverPoint(e, p, out float hoverX, out float hoverY);
            switch (e.DroneState)
            {
                case DroneMode.Hover:
                    FlyToward(e, hoverX, hoverY, c.droneSpeed);
                    bool inPlace = MathF.Abs(_map.Dx(hoverX, e.X)) < c.droneDiveTriggerX && MathF.Abs(e.Y - hoverY) < 1f;
                    if (e.ModeTimer <= 0f && inPlace && HasLineOfSight(e.Center, _map.Near(e.Center, p.Center)))
                    {
                        e.DroneState = DroneMode.Windup;
                        e.ModeTimer = c.droneWindupTime;
                        Emit(SimEventType.DroneWindup, e.Center);
                    }
                    break;
                case DroneMode.Windup:
                    e.Vx = 0f;
                    e.Vy = 0f;
                    if (e.ModeTimer > 0f) break;
                    var d = _map.Near(e.Center, p.Center) - e.Center;
                    float len = MathF.Max(1e-3f, d.Length);
                    e.DiveDir = new Vec2(d.X / len, d.Y / len);
                    e.DroneState = DroneMode.Dive;
                    e.ModeTimer = c.droneDiveTime;
                    e.Vx = e.DiveDir.X * c.droneDiveSpeed;
                    e.Vy = e.DiveDir.Y * c.droneDiveSpeed;
                    break;
                case DroneMode.Dive:
                    e.Vx = e.DiveDir.X * c.droneDiveSpeed;
                    e.Vy = e.DiveDir.Y * c.droneDiveSpeed;
                    break;
                default: // Climb
                    FlyToward(e, hoverX, hoverY, c.droneSpeed * 1.5f);
                    if (MathF.Abs(e.Y - hoverY) < 0.3f)
                    {
                        e.DroneState = DroneMode.Hover;
                        e.ModeTimer = c.droneDiveCooldown;
                    }
                    break;
            }
        }

        /// <summary>Horizontal offsets tried for a drone's hover point, nearest first (times the drone's side).</summary>
        private static readonly float[] HoverOffsets = { 0f, 2f, -2f, 3f, -3f, 4f, -4f, 5f, -5f, 6f, -6f };

        /// <summary>
        /// Picks where a drone hovers: at hover height above the player, shifted sideways when terrain
        /// is in the way, so it always has a clear line to dive along (never parked on a platform).
        /// </summary>
        private void ChooseHoverPoint(Enemy e, Player p, out float x, out float y)
        {
            y = p.Y + _cfg.droneHoverHeight;
            float side = e.X >= p.X ? 1f : -1f;
            var target = p.Center;
            for (int i = 0; i < HoverOffsets.Length; i++)
            {
                x = p.X + HoverOffsets[i] * side;
                float half = e.Width * 0.5f;
                if (_map.OverlapsSolid(x - half, y, x + half, y + e.Height)) continue;
                if (HasClearDive(new Vec2(x, y + e.Height * 0.5f), half, target)) return;
            }
            x = p.X;
        }

        /// <summary>True when a body of the given half-width could fly straight from one point to another.</summary>
        private bool HasClearDive(Vec2 from, float half, Vec2 to)
        {
            var side = new Vec2(half * 0.9f, 0f);
            return HasLineOfSight(from, to) && HasLineOfSight(from - side, to - side) && HasLineOfSight(from + side, to + side);
        }

        /// <summary>True when no solid tile lies on the straight line between two points.</summary>
        private bool HasLineOfSight(Vec2 from, Vec2 to)
        {
            var d = to - from;
            float len = d.Length;
            if (len < 1e-3f) return true;
            return !_map.Raycast(from, d * (1f / len), len, out _);
        }

        /// <summary>Velocity toward a point (around the ring), easing in near it, capped at a speed per axis.</summary>
        private void FlyToward(Enemy e, float x, float y, float speed)
        {
            e.Vx = Math.Clamp(_map.Dx(e.X, x) * 3f, -speed, speed);
            e.Vy = Math.Clamp((y - e.Y) * 3f, -speed, speed);
        }

        /// <summary>True when the player is close enough for an enemy to leave its post and attack.</summary>
        private bool Aggro(Enemy e, float range)
        {
            var p = Player;
            return MathF.Abs(_map.Dx(e.X, p.X)) < range && MathF.Abs(p.Y - e.Y) < _cfg.enemyAggroHeight;
        }

        /// <summary>
        /// Walkers guard a post: they chase a nearby player (never further than the leash from the
        /// post) and walk back to the post when the player leaves.
        /// </summary>
        private void SteerEnemy(Enemy e)
        {
            var p = Player;
            float dx = _map.Dx(e.X, p.X);
            bool onPostLevel = MathF.Abs(e.Y - e.Post.Y) < 1f;
            // A detour lasts while the player is below: it outlives horizontal aggro (the far ledge
            // can be further from the player than that) and a drop onto a lower step (stairs down
            // from the ledge), so the enemy does not hop straight back up toward the player.
            // A wall ends it too: hopping a wall on the way down leads back up.
            if (e.Detour && (!(p.Y < e.Y - 1f && e.Y - p.Y < _cfg.enemyAggroHeight) || e.WallDir == e.Facing))
                e.Detour = false;
            if (!e.Detour && !Aggro(e, _cfg.enemyAggroRange))
            {
                ReturnToPost(e, dx);
                return;
            }
            int dir = dx > 0.3f ? 1 : dx < -0.3f ? -1 : 0;
            if (e.Detour)
            {
                // Walking to the far ledge: no leash and no turning back toward the player, or
                // it paces at the edge of the "above the player" band forever.
                dir = e.Facing;
            }
            else
            {
                // Roughly above the player: commit to one direction so the enemy walks off its
                // ledge instead of turning back and forth over the player's head.
                bool above = p.Y < e.Y - 1f && MathF.Abs(dx) < 4f;
                if (above) dir = e.Facing;
                float fromPost = _map.Dx(e.Post.X, e.X);
                bool leashed = MathF.Abs(fromPost) > _cfg.enemyLeash && Sign(fromPost) == dir && onPostLevel;
                if (leashed && above)
                {
                    // Above the player but leashed this way: commit to the other ledge.
                    e.Facing = -e.Facing;
                    e.Detour = true;
                    dir = e.Facing;
                }
                else if (leashed)
                {
                    dir = 0;
                }
            }
            if (dir != 0) e.Facing = dir;
            else if (dx != 0f && !(p.Y < e.Y - 1f && MathF.Abs(dx) < 4f)) e.Facing = Sign(dx);
            e.Vx = dir * e.Speed;
            if (e.Grounded && dir != 0 && e.WallDir == dir) e.Vy = e.JumpSpeed;
            else if (e.Kind == EnemyKind.Grunt && e.Grounded && p.Y - e.Y > 2f && MathF.Abs(dx) < 3f)
                e.Vy = e.JumpSpeed;
        }

        private void ReturnToPost(Enemy e, float dxToPlayer)
        {
            // Fell off its post (or was knocked off): guard where it landed.
            if (e.Grounded && MathF.Abs(e.Y - e.Post.Y) > 1.5f) e.Post = new Vec2(e.X, e.Y);
            float toPost = _map.Dx(e.X, e.Post.X);
            int dir = toPost > 0.5f ? 1 : toPost < -0.5f ? -1 : 0;
            e.Vx = dir * e.Speed * 0.6f;
            e.Facing = dir != 0 ? dir : (dxToPlayer >= 0f ? 1 : -1);
            if (e.Grounded && dir != 0 && e.WallDir == dir) e.Vy = e.JumpSpeed;
        }

        /// <summary>
        /// Applies the active strike to every enemy in the strike box once. Armored enemies the
        /// player is too slow for "clang": they take no damage and bounce the player back, but
        /// the rest of the strike still lands on everything else in the box.
        /// </summary>
        private void ResolveStrike()
        {
            var p = Player;
            if (!p.Attacking) return;
            int tier = PlayerTier;
            GetStrikeBox(out float minX, out float minY, out float maxX, out float maxY);
            ReflectProjectiles(p, tier, minX, minY, maxX, maxY);
            Enemy clanged = null;
            for (int i = 0; i < _enemies.Count; i++)
            {
                var e = _enemies[i];
                if (!e.Alive || e.LastStrikeId == p.StrikeId) continue;
                if (!InStrikeBox(e, minX, minY, maxX, maxY)) continue;
                e.LastStrikeId = p.StrikeId;
                e.SinceHit = 0f;
                bool shielded = e.Kind == EnemyKind.Shield && ShieldBlocks(e);
                if (!shielded && CombatRules.CanDamage(tier, e.ArmorTier)) StrikeEnemy(p, e, tier);
                else if (clanged == null) clanged = e;
            }
            if (clanged == null) return;
            p.Vx = -p.Facing * _cfg.clangBounceSpeed;
            p.AttackTimer = 0f;
            Emit(SimEventType.Clang, clanged.Center, tier);
            if (clanged.Kind == EnemyKind.Shield) Emit(SimEventType.ShieldBlock, clanged.Center, tier);
        }

        /// <summary>True when a body (an enemy or a projectile; its copy nearest the player, on a ring) overlaps the strike box.</summary>
        public bool InStrikeBox(Body e, float minX, float minY, float maxX, float maxY)
        {
            float ex = Player.X + _map.Dx(Player.X, e.X);
            float half = e.Width * 0.5f;
            return ex + half > minX && ex - half < maxX && e.MaxY > minY && e.Y < maxY;
        }

        private void StrikeEnemy(Player p, Enemy e, int tier)
        {
            var c = _cfg;
            e.Hp -= CombatRules.Damage(tier, c);
            float kb = CombatRules.Knockback(tier, c) * (e.Kind == EnemyKind.Brute ? c.bruteKnockbackScale : 1f);
            e.Vx = p.Facing * kb;
            e.Vy = MathF.Max(e.Vy, kb * c.knockbackLift);
            e.StunTimer = c.enemyStunTime;
            if (e.Alive)
            {
                Emit(SimEventType.Hit, e.Center, tier);
                return;
            }
            p.Chain = p.ChainTimer > 0f ? p.Chain + 1 : 1;
            p.ChainTimer = c.chainWindow;
            RefundMomentum(p);
            KillEnemy(e, tier);
        }

        /// <summary>Counts a kill, reports it (Detail = (int)EnemyKind) and rolls its orb drop.</summary>
        private void KillEnemy(Enemy e, int tier)
        {
            Kills++;
            Emit(SimEventType.Kill, e.Center, tier, (int)e.Kind);
            RollOrbDrop(e);
        }

        /// <summary>
        /// Damage from something other than a sword strike (a reflected projectile, a blast):
        /// ignores armor, stuns and pushes the enemy along dir. A kill counts, rolls an orb, and
        /// extends the player's chain when <paramref name="chain"/> is set (no momentum refund).
        /// </summary>
        private void DamageEnemy(Enemy e, float damage, int dir, bool chain)
        {
            var c = _cfg;
            e.Hp -= damage;
            e.SinceHit = 0f;
            e.StunTimer = c.enemyStunTime;
            e.Vx = dir * c.tierKnockback[0] * (e.Kind == EnemyKind.Brute ? c.bruteKnockbackScale : 1f);
            int tier = PlayerTier;
            if (e.Alive)
            {
                Emit(SimEventType.Hit, e.Center, tier);
                return;
            }
            if (chain)
            {
                var p = Player;
                p.Chain = p.ChainTimer > 0f ? p.Chain + 1 : 1;
                p.ChainTimer = c.chainWindow;
            }
            KillEnemy(e, tier);
        }

        // ------------------------------------------------------------------ shooter, shield, kamikaze, summoner

        /// <summary>A hit interrupts a shooter's telegraph or a summoner's cast (back to Idle, full cooldown).</summary>
        private void CancelTelegraph(Enemy e)
        {
            if (e.Kind == EnemyKind.Shooter && e.Act == EnemyAct.Windup)
            {
                e.Act = EnemyAct.Idle;
                e.ModeTimer = _cfg.shooterCooldown;
            }
            else if (e.Kind == EnemyKind.Summoner && e.Act == EnemyAct.Cast)
            {
                e.Act = EnemyAct.Idle;
                e.ModeTimer = _cfg.summonerCastInterval;
            }
        }

        /// <summary>
        /// Shooter: holds its post, faces a player in range, and with line of sight telegraphs
        /// (shooterWindupTime, standing still) and then fires at where the player is.
        /// </summary>
        private void SteerShooter(Enemy e, float dt)
        {
            var c = _cfg;
            var p = Player;
            float dx = _map.Dx(e.X, p.X);
            ReturnToPost(e, dx);
            bool inRange = MathF.Abs(dx) < c.shooterRange && MathF.Abs(p.Y - e.Y) < c.enemyAggroHeight;
            if (inRange) e.Facing = dx >= 0f ? 1 : -1;
            e.ModeTimer -= dt;
            if (e.Act == EnemyAct.Windup)
            {
                e.Vx = 0f;
                if (e.ModeTimer > 0f) return;
                FireAtPlayer(e);
                e.Act = EnemyAct.Idle;
                e.ModeTimer = c.shooterCooldown;
                return;
            }
            if (e.ModeTimer > 0f || !inRange || !e.Grounded) return;
            if (!HasLineOfSight(e.Center, _map.Near(e.Center, p.Center))) return;
            e.Act = EnemyAct.Windup;
            e.ModeTimer = c.shooterWindupTime;
            e.Vx = 0f;
            Emit(SimEventType.ShooterWindup, e.Center);
        }

        private void FireAtPlayer(Enemy e)
        {
            var from = e.Center;
            var d = _map.Near(from, Player.Center) - from;
            float len = MathF.Max(1e-3f, d.Length);
            var dir = d * (1f / len);
            SpawnProjectile(from + dir * (e.Width * 0.5f), dir * _cfg.projectileSpeed);
        }

        /// <summary>
        /// Fires a projectile from a centre point (shooters use the same path). Returns null when
        /// the pool (projectileMaxCount) is full. Emits <see cref="SimEventType.Shoot"/>.
        /// </summary>
        public Projectile SpawnProjectile(Vec2 centre, Vec2 velocity)
        {
            if (_freeProjectiles.Count == 0) return null;
            var pr = _freeProjectiles[_freeProjectiles.Count - 1];
            _freeProjectiles.RemoveAt(_freeProjectiles.Count - 1);
            float size = _cfg.projectileSize;
            pr.Width = size;
            pr.Height = size;
            pr.X = _map.WrapX(centre.X);
            pr.Y = centre.Y - size * 0.5f;
            pr.Vx = velocity.X;
            pr.Vy = velocity.Y;
            pr.Reflected = false;
            pr.TimeLeft = _cfg.projectileLifetime;
            _projectiles.Add(pr);
            Emit(SimEventType.Shoot, pr.Center);
            return pr;
        }

        /// <summary>
        /// The active strike sends every unreflected projectile in the box back the way it came,
        /// reflectSpeedMultiplier faster and always away from the player (in their facing).
        /// </summary>
        private void ReflectProjectiles(Player p, int tier, float minX, float minY, float maxX, float maxY)
        {
            var c = _cfg;
            for (int i = 0; i < _projectiles.Count; i++)
            {
                var pr = _projectiles[i];
                if (pr.Reflected || !InStrikeBox(pr, minX, minY, maxX, maxY)) continue;
                pr.Reflected = true;
                pr.Vx = p.Facing * MathF.Abs(pr.Vx) * c.reflectSpeedMultiplier;
                pr.Vy = -pr.Vy * c.reflectSpeedMultiplier;
                pr.TimeLeft = c.projectileLifetime;
                Emit(SimEventType.ProjectileReflect, pr.Center, tier);
            }
        }

        /// <summary>
        /// Projectiles fly straight. They end on a solid tile or when their time runs out; an
        /// unreflected one ends on the player (hurting them unless invulnerable, else it passes);
        /// a reflected one ends on the first enemy it touches (a shield facing it blocks it).
        /// </summary>
        private void UpdateProjectiles(float dt)
        {
            var c = _cfg;
            var p = Player;
            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                var pr = _projectiles[i];
                pr.TimeLeft -= dt;
                pr.X += pr.Vx * dt;
                pr.Y += pr.Vy * dt;
                if (_map.WrapsX) pr.X = _map.WrapX(pr.X);
                if (pr.TimeLeft <= 0f || _map.OverlapsSolid(pr.MinX, pr.Y, pr.MaxX, pr.MaxY))
                {
                    EndProjectileAt(i);
                    continue;
                }
                int dir = pr.Vx >= 0f ? 1 : -1;
                if (!pr.Reflected)
                {
                    if (p.InvulnTimer > 0f || !p.Overlaps(pr, _map)) continue;
                    EndProjectileAt(i);
                    Hurt(c.projectileDamage, dir, false);
                    continue;
                }
                for (int k = 0; k < _enemies.Count; k++)
                {
                    var e = _enemies[k];
                    if (!e.Alive || !e.Overlaps(pr, _map)) continue;
                    if (e.Kind == EnemyKind.Shield && dir == -e.ShieldDir) Emit(SimEventType.ShieldBlock, pr.Center);
                    else DamageEnemy(e, c.reflectedProjectileDamage, dir, chain: true);
                    EndProjectileAt(i);
                    break;
                }
            }
        }

        /// <summary>Order-preserving removal; emits ProjectileHit and returns the projectile to the pool.</summary>
        private void EndProjectileAt(int i)
        {
            var pr = _projectiles[i];
            Emit(SimEventType.ProjectileHit, pr.Center);
            _freeProjectiles.Add(pr);
            _projectiles.RemoveAt(i);
        }

        /// <summary>
        /// True when a shield bearer's shield stops a strike: the player is on its shield side
        /// (or level with it), unless they are falling onto it from above (feet over
        /// shieldAboveFraction of its height).
        /// </summary>
        private bool ShieldBlocks(Enemy e)
        {
            var p = Player;
            bool fromAbove = !p.Grounded && p.Vy <= 0f && p.Y >= e.Y + e.Height * _cfg.shieldAboveFraction;
            if (fromAbove) return false;
            float side = _map.Dx(e.X, p.X);
            int s = side > 0f ? 1 : side < 0f ? -1 : e.ShieldDir;
            return s == e.ShieldDir;
        }

        /// <summary>The shield follows the bearer's facing only after shieldTurnTime: jump over it and its back is open.</summary>
        private void TurnShield(Enemy e, float dt)
        {
            if (e.Facing == e.ShieldDir)
            {
                e.TurnTimer = 0f;
                return;
            }
            e.TurnTimer += dt;
            if (e.TurnTimer < _cfg.shieldTurnTime) return;
            e.ShieldDir = e.Facing;
            e.TurnTimer = 0f;
        }

        /// <summary>
        /// Kamikaze: guards its post until the player is in kamikazeAggroRange with line of sight,
        /// then rushes; within kamikazeFuseRange it stops and lights its fuse (the fuse burns in
        /// UpdateEnemies). A kill at any point before the blast defuses it.
        /// </summary>
        private void SteerKamikaze(Enemy e)
        {
            var c = _cfg;
            var p = Player;
            var to = _map.Near(e.Center, p.Center) - e.Center;
            switch (e.Act)
            {
                case EnemyAct.Fuse:
                    e.Vx = 0f;
                    return;
                case EnemyAct.Rush:
                    if (to.Length < c.kamikazeFuseRange)
                    {
                        e.Act = EnemyAct.Fuse;
                        e.ModeTimer = c.kamikazeFuseTime;
                        e.Vx = 0f;
                        Emit(SimEventType.KamikazeFuse, e.Center);
                        return;
                    }
                    int dir = to.X >= 0f ? 1 : -1;
                    e.Facing = dir;
                    e.Vx = dir * c.kamikazeRushSpeed;
                    if (e.Grounded && (e.WallDir == dir || (to.Y > 2f && MathF.Abs(to.X) < 3f))) e.Vy = e.JumpSpeed;
                    return;
                default:
                    if (Aggro(e, c.kamikazeAggroRange) && HasLineOfSight(e.Center, e.Center + to))
                        e.Act = EnemyAct.Rush;
                    else
                        ReturnToPost(e, to.X);
                    return;
            }
        }

        /// <summary>
        /// The blast: the kamikaze dies (no kill credit, no orb), the player takes
        /// kamikazeBlastDamage within kamikazeBlastRadius, and every other enemy in the radius takes
        /// kamikazeBlastEnemyDamage (kills count, without extending the player's chain).
        /// </summary>
        private void Explode(Enemy e)
        {
            var c = _cfg;
            var p = Player;
            var at = e.Center;
            e.Hp = 0f;
            e.Act = EnemyAct.Idle;
            Emit(SimEventType.Explode, at);
            var toPlayer = _map.Near(at, p.Center) - at;
            if (p.InvulnTimer <= 0f && toPlayer.Length <= c.kamikazeBlastRadius)
                Hurt(c.kamikazeBlastDamage, toPlayer.X >= 0f ? 1 : -1, false);
            for (int i = 0; i < _enemies.Count; i++)
            {
                var o = _enemies[i];
                if (o == e || !o.Alive) continue;
                var d = _map.Near(at, o.Center) - at;
                if (d.Length <= c.kamikazeBlastRadius) DamageEnemy(o, c.kamikazeBlastEnemyDamage, d.X >= 0f ? 1 : -1, chain: false);
            }
        }

        /// <summary>
        /// Summoner: runs away (at summonerSpeed, below the player's run) while the player is within
        /// summonerFleeRange, otherwise stands facing them. Every summonerCastInterval, grounded and
        /// with the player within summonerCastRange and fewer than summonerMaxDrones of its drones
        /// alive, it stops to cast (the telegraph) and then summons a drone.
        /// </summary>
        private void SteerSummoner(Enemy e, float dt)
        {
            var c = _cfg;
            float dx = _map.Dx(e.X, Player.X);
            e.ModeTimer -= dt;
            if (e.Act == EnemyAct.Cast)
            {
                e.Vx = 0f;
                if (e.ModeTimer > 0f) return;
                Summon(e);
                e.Act = EnemyAct.Idle;
                e.ModeTimer = c.summonerCastInterval;
                return;
            }
            if (Aggro(e, c.summonerFleeRange))
            {
                int dir = dx >= 0f ? -1 : 1;
                e.Facing = dir;
                e.Vx = dir * e.Speed;
                if (e.Grounded && e.WallDir == dir) e.Vy = e.JumpSpeed;
            }
            else
            {
                e.Vx = MoveToward(e.Vx, 0f, c.enemyFriction * dt);
                e.Facing = dx >= 0f ? 1 : -1;
            }
            if (e.ModeTimer > 0f || !e.Grounded || !Aggro(e, c.summonerCastRange)) return;
            if (LiveSummons(e) >= c.summonerMaxDrones) return;
            e.Act = EnemyAct.Cast;
            e.ModeTimer = c.summonerCastTime;
            e.Vx = 0f;
            Emit(SimEventType.SummonerCast, e.Center);
        }

        private int LiveSummons(Enemy summoner)
        {
            int n = 0;
            for (int i = 0; i < _enemies.Count; i++)
                if (_enemies[i].Alive && _enemies[i].Owner == summoner) n++;
            return n;
        }

        /// <summary>A drone appears above the summoner (allocates one Enemy, like a wave spawn).</summary>
        private void Summon(Enemy e)
        {
            var d = SpawnEnemy(EnemyKind.Drone, new Vec2(e.X, e.MaxY + _cfg.summonerSummonHeight));
            if (_map.OverlapsSolid(d.MinX, d.Y, d.MaxX, d.MaxY))
            {
                d.X = e.X;
                d.Y = e.Y;
                d.Post = new Vec2(d.X, d.Y);
                d.RefreshContacts(_map);
            }
            d.Owner = e;
            Emit(SimEventType.Summon, d.Center);
        }

        // ------------------------------------------------------------------ orbs

        /// <summary>
        /// One roll per kill against the kind's drop chance; on a drop a second roll picks the orb
        /// kind by weight. The RNG is the sim's own (seeded by rngSeed), so runs are reproducible.
        /// </summary>
        private void RollOrbDrop(Enemy e)
        {
            var c = _cfg;
            if (NextRandom() >= OrbChance(e.Kind)) return;
            float pick = NextRandom() * (c.orbWeightHealth + c.orbWeightSpeed + c.orbWeightHook);
            var kind = pick < c.orbWeightHealth ? OrbKind.Health
                : pick < c.orbWeightHealth + c.orbWeightSpeed ? OrbKind.Speed
                : OrbKind.Hook;
            var centre = e.Center;
            SpawnOrb(kind, new Vec2(centre.X, centre.Y - c.orbSize * 0.5f));
        }

        /// <summary>Drop chance for an enemy kind. New kinds: add a config field and a case here.</summary>
        private float OrbChance(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Grunt: return _cfg.gruntOrbChance;
                case EnemyKind.Brute: return _cfg.bruteOrbChance;
                case EnemyKind.Drone: return _cfg.droneOrbChance;
                case EnemyKind.Shooter: return _cfg.shooterOrbChance;
                case EnemyKind.Shield: return _cfg.shieldOrbChance;
                case EnemyKind.Kamikaze: return _cfg.kamikazeOrbChance;
                case EnemyKind.Summoner: return _cfg.summonerOrbChance;
                default: return 0f;
            }
        }

        /// <summary>
        /// Scrambles a seed into a non-zero xorshift32 state (a raw small seed would give tiny
        /// first values, i.e. a burst of drops).
        /// </summary>
        private static uint SeedState(int seed)
        {
            uint x = unchecked((uint)seed * 0x9E3779B9u + 0x6D2B79F5u);
            x ^= x >> 16;
            x = unchecked(x * 0x85EBCA6Bu);
            x ^= x >> 13;
            x = unchecked(x * 0xC2B2AE35u);
            x ^= x >> 16;
            return x == 0u ? 0x9E3779B9u : x;
        }

        /// <summary>Uniform float in [0, 1) from the sim's xorshift32 state.</summary>
        private float NextRandom()
        {
            uint x = _rng;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _rng = x;
            return (x >> 8) * (1f / 16777216f);
        }

        /// <summary>Orbs fall and rest, expire, and are picked up when the player overlaps them.</summary>
        private void UpdateOrbs(float dt)
        {
            var c = _cfg;
            var p = Player;
            for (int i = _orbs.Count - 1; i >= 0; i--)
            {
                var o = _orbs[i];
                o.Age += dt;
                o.TimeLeft -= dt;
                o.Vy -= c.gravity * dt;
                if (o.Vy < -c.maxFallSpeed) o.Vy = -c.maxFallSpeed;
                o.Move(_map, dt);
                if (o.Age >= c.orbPickupDelay && p.Overlaps(o, _map))
                {
                    ApplyOrb(p, o.Kind);
                    Emit(SimEventType.OrbPickup, o.Center, PlayerTier, (int)o.Kind);
                    RemoveOrbAt(i);
                }
                else if (o.TimeLeft <= 0f)
                {
                    o.TimeLeft = 0f;
                    Emit(SimEventType.OrbExpire, o.Center, PlayerTier, (int)o.Kind);
                    RemoveOrbAt(i);
                }
            }
        }

        private void ApplyOrb(Player p, OrbKind kind)
        {
            var c = _cfg;
            switch (kind)
            {
                case OrbKind.Health:
                    p.Hp = Math.Min(c.playerMaxHp, p.Hp + c.orbHealAmount);
                    break;
                case OrbKind.Speed:
                    p.SpeedBuffTimer = c.orbSpeedDuration;
                    break;
                default:
                    p.HookBuffTimer = c.orbHookDuration;
                    p.HookCooldownTimer = 0f;
                    break;
            }
        }

        /// <summary>Order-preserving removal (views may index orbs); returns the orb to the pool.</summary>
        private void RemoveOrbAt(int i)
        {
            _freeOrbs.Add(_orbs[i]);
            _orbs.RemoveAt(i);
        }

        /// <summary>
        /// A kill pushes the player on in the direction they struck, more for each kill in the
        /// current chain. Moving that way already, they speed up (capped at maxSpeed); moving the
        /// other way, the refund only brakes them — it never flips them.
        /// </summary>
        private void RefundMomentum(Player p)
        {
            var c = _cfg;
            float refund = c.killMomentumRefund + MathF.Min((p.Chain - 1) * c.chainBonusPerKill, c.chainMaxBonus);
            float speed = MathF.Abs(p.Vx);
            // Moving that way: up to maxSpeed, and never slower (air overspeed is kept).
            if (p.Vx == 0f || Sign(p.Vx) == p.Facing)
                p.Vx = p.Facing * MathF.Max(speed, MathF.Min(speed + refund, MaxSpeed(p)));
            else
                p.Vx += p.Facing * MathF.Min(refund, MathF.Abs(p.Vx));
        }

        private void ResolveContacts()
        {
            var p = Player;
            if (p.InvulnTimer > 0f) return;
            for (int i = 0; i < _enemies.Count; i++)
            {
                var e = _enemies[i];
                if (!e.Alive || e.Dazed || e.ContactDamage <= 0 || !p.Overlaps(e, _map)) continue;
                float side = _map.Dx(e.X, p.X);
                int away = side > 0f ? 1 : side < 0f ? -1 : -p.Facing;
                Hurt(e.ContactDamage, away, false);
                return;
            }
            if (_map.OverlapsSpike(p.MinX, p.Y, p.MaxX, p.MaxY)) Hurt(_cfg.spikeDamage, 0, true);
        }

        private void Hurt(int damage, int awayDir, bool spike)
        {
            var p = Player;
            var c = _cfg;
            p.Hp -= damage;
            p.InvulnTimer = c.invulnTime;
            p.Chain = 0;
            p.ChainTimer = 0f;
            p.AttackTimer = 0f;
            ReleaseHook();
            if (p.Sliding) TryEndSlide();
            if (spike)
            {
                p.Vx *= c.spikeSpeedKeep;
                p.Vy = c.spikeBounceSpeed;
            }
            else
            {
                p.Vx = awayDir * c.hurtKnockbackX;
                p.Vy = c.hurtKnockbackY;
            }
            Emit(SimEventType.PlayerHurt, p.Center);
        }

        // ------------------------------------------------------------------ helpers

        private void Emit(SimEventType type, Vec2 position) => Emit(type, position, PlayerTier);

        private void Emit(SimEventType type, Vec2 position, int tier) =>
            _events.Add(new SimEvent(type, position, tier));

        private void Emit(SimEventType type, Vec2 position, int tier, int detail) =>
            _events.Add(new SimEvent(type, position, tier, detail));

        private static float Dec(float timer, float dt) => timer > 0f ? timer - dt : 0f;

        private static int Sign(float v) => v > 0f ? 1 : v < 0f ? -1 : 0;

        private static float MoveToward(float value, float target, float maxDelta)
        {
            if (MathF.Abs(target - value) <= maxDelta) return target;
            return value + MathF.Sign(target - value) * maxDelta;
        }
    }
}
