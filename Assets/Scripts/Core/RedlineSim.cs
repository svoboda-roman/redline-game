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

        public RedlineConfig Config => _cfg;
        public TileMap Map => _map;
        public Player Player { get; private set; }
        public IReadOnlyList<Enemy> Enemies => _enemies;

        /// <summary>Events emitted by the last Step (cleared at the start of each Step).</summary>
        public IReadOnlyList<SimEvent> Events => _events;

        public SimState State { get; private set; }

        /// <summary>0-based index of the wave that is running, or that comes next during a break.</summary>
        public int WaveIndex { get; private set; }

        /// <summary>1-based wave number for display.</summary>
        public int CurrentWave => Math.Min(WaveIndex + 1, WaveCount);

        public int WaveCount => _cfg.WaveCount;

        /// <summary>Seconds left before the next wave spawns (WaveBreak only).</summary>
        public float BreakTimer { get; private set; }

        public int EnemiesAlive { get; private set; }

        public int Kills { get; private set; }

        /// <summary>Increments on every <see cref="Restart"/>, so views can tell a fresh run apart.</summary>
        public int RunId { get; private set; }

        /// <summary>The player's current speed tier (1-3), from horizontal speed.</summary>
        public int PlayerTier => CombatRules.TierFor(Player.Vx, _cfg);

        /// <summary>Creates a run. Throws ArgumentException when the config is invalid.</summary>
        public RedlineSim(RedlineConfig config, TileMap map)
        {
            _cfg = config ?? throw new ArgumentNullException(nameof(config));
            _map = map ?? throw new ArgumentNullException(nameof(map));
            var errors = _cfg.Validate();
            if (errors.Count > 0) throw new ArgumentException("Invalid RedlineConfig: " + string.Join("; ", errors));
            Restart();
        }

        /// <summary>Starts again from wave 1 with a fresh player.</summary>
        public void Restart()
        {
            _enemies.Clear();
            _events.Clear();
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
            var e = new Enemy(kind, position, _cfg);
            e.RefreshContacts(_map);
            _enemies.Add(e);
            EnemiesAlive++;
            return e;
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
            ResolveContacts();

            if (Player.Hp <= 0)
            {
                Player.Hp = 0;
                ReleaseHook();
                State = SimState.GameOver;
                Emit(SimEventType.GameOver, Player.Center);
                return;
            }
            UpdateWaveProgress();
        }

        /// <summary>The player's strike hitbox for the current facing (valid whether or not striking).</summary>
        public void GetStrikeBox(out float minX, out float minY, out float maxX, out float maxY)
        {
            var p = Player;
            float cy = p.Y + p.Height * 0.5f;
            minY = cy - _cfg.attackHeight * 0.5f;
            maxY = cy + _cfg.attackHeight * 0.5f;
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
            int grunts = _cfg.waveGrunts[WaveIndex];
            int brutes = _cfg.waveBrutes[WaveIndex];
            int k = 0;
            for (int i = 0; i < brutes; i++) SpawnAtPoint(EnemyKind.Brute, k++);
            for (int i = 0; i < grunts; i++) SpawnAtPoint(EnemyKind.Grunt, k++);
            State = SimState.Fighting;
            Emit(SimEventType.WaveStart, Player.Center);
        }

        private void SpawnAtPoint(EnemyKind kind, int k)
        {
            var points = _map.SpawnPoints;
            var pt = points[k % points.Count];
            int ring = k / points.Count;
            float offset = (ring % 2 == 0 ? 1f : -1f) * ((ring + 1) / 2) * _cfg.spawnSpacing;
            var e = SpawnEnemy(kind, new Vec2(pt.X + offset, pt.Y));
            if (_map.OverlapsSolid(e.MinX, e.Y, e.MaxX, e.MaxY))
            {
                e.X = pt.X;
                e.RefreshContacts(_map);
            }
        }

        // ------------------------------------------------------------------ player

        private void UpdatePlayer(InputFrame input, float dt)
        {
            var p = Player;
            TickPlayerTimers(p, input, dt);
            float move = Math.Clamp(input.MoveX, -1f, 1f);
            int dir = move > MoveDeadZone ? 1 : move < -MoveDeadZone ? -1 : 0;

            UpdateFacing(p, dir);
            if (p.Hooked && !input.HookHeld) ReleaseHook();
            if (p.HookBufferTimer > 0f && input.HookHeld && !p.Hooked && p.HookCooldownTimer <= 0f) TryAttachHook();
            TryStartAttack(p);
            TryStartSlide(p);

            HorizontalControl(p, dir, dt);
            HandleJump(p, input);
            ApplyGravity(p, dir, dt);
            ApplyRope(p, dt);

            p.Vx = Math.Clamp(p.Vx, -_cfg.maxSpeed, _cfg.maxSpeed);
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
            p.AttackCooldownTimer = Dec(p.AttackCooldownTimer, dt);
            p.AttackTimer = Dec(p.AttackTimer, dt);
            p.JumpBufferTimer = Dec(p.JumpBufferTimer, dt);
            p.AttackBufferTimer = Dec(p.AttackBufferTimer, dt);
            p.SlideBufferTimer = Dec(p.SlideBufferTimer, dt);
            p.HookBufferTimer = Dec(p.HookBufferTimer, dt);
            p.CoyoteTimer = Dec(p.CoyoteTimer, dt);

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
            p.Vx = Sign(p.Vx) * MathF.Min(MathF.Abs(p.Vx) + c.slideBoost, c.maxSpeed);
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
            bool keepingOverspeed = Sign(p.Vx) == dir && MathF.Abs(p.Vx) >= _cfg.runSpeed;
            if (!keepingOverspeed) p.Vx = MoveToward(p.Vx, dir * _cfg.runSpeed, _cfg.airAccel * dt);
        }

        private void GroundControl(Player p, int dir, float dt)
        {
            var c = _cfg;
            if (dir == 0)
            {
                p.Vx = MoveToward(p.Vx, 0f, c.groundFriction * dt);
            }
            else if (Sign(p.Vx) == dir && MathF.Abs(p.Vx) > c.runSpeed)
            {
                p.Vx = dir * MathF.Max(c.runSpeed, MathF.Abs(p.Vx) - c.overspeedDecay * dt);
            }
            else
            {
                p.Vx = MoveToward(p.Vx, dir * c.runSpeed, c.groundAccel * dt);
            }
        }

        private void HandleJump(Player p, InputFrame input)
        {
            var c = _cfg;
            if (p.JumpBufferTimer > 0f)
            {
                if (p.Hooked)
                {
                    ReleaseHook();
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
            p.Vx = -p.WallDir * MathF.Min(speed, c.maxSpeed);
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
        private static void ApplyRope(Player p, float dt)
        {
            if (!p.Hooked) return;
            var centre = p.Center;
            var predicted = new Vec2(centre.X + p.Vx * dt, centre.Y + p.Vy * dt);
            var d = predicted - p.HookAnchor;
            float len = d.Length;
            if (len <= p.HookLength || len <= 1e-4f) return;
            var onRope = p.HookAnchor + d * (p.HookLength / len);
            p.Vx = (onRope.X - centre.X) / dt;
            p.Vy = (onRope.Y - centre.Y) / dt;
        }

        private void PumpSwing(Player p, int dir, float dt)
        {
            var d = p.Center - p.HookAnchor;
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
            p.HookCooldownTimer = c.hookCooldown;
            float a = c.hookAngleDeg * (MathF.PI / 180f);
            var dir = new Vec2(MathF.Cos(a) * p.Facing, MathF.Sin(a));
            if (!_map.Raycast(p.Center, dir, c.hookRange, out var hit)) return;
            p.Hooked = true;
            p.HookAnchor = hit;
            p.HookLength = MathF.Max(c.hookMinLength, (p.Center - hit).Length);
            Emit(SimEventType.HookAttach, hit);
        }

        private void ReleaseHook()
        {
            var p = Player;
            if (!p.Hooked) return;
            p.Hooked = false;
            p.HookCooldownTimer = _cfg.hookCooldown;
            Emit(SimEventType.HookRelease, p.Center);
        }

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
                if (e.StunTimer > 0f)
                {
                    e.StunTimer -= dt;
                    if (e.Grounded) e.Vx = MoveToward(e.Vx, 0f, c.enemyFriction * dt);
                }
                else
                {
                    SteerEnemy(e);
                }
                e.Vy -= c.gravity * dt;
                if (e.Vy < -c.maxFallSpeed) e.Vy = -c.maxFallSpeed;
                e.Move(_map, dt);
            }
        }

        private void SteerEnemy(Enemy e)
        {
            var p = Player;
            float dx = p.X - e.X;
            int dir = dx > 0.3f ? 1 : dx < -0.3f ? -1 : 0;
            // Roughly above the player: commit to one direction so the enemy walks off its
            // ledge instead of turning back and forth over the player's head.
            if (p.Y < e.Y - 1f && MathF.Abs(dx) < 1.5f) dir = e.Facing;
            if (dir != 0) e.Facing = dir;
            e.Vx = dir * e.Speed;
            if (e.Grounded && dir != 0 && e.WallDir == dir) e.Vy = e.JumpSpeed;
            else if (e.Kind == EnemyKind.Grunt && e.Grounded && p.Y - e.Y > 2f && MathF.Abs(dx) < 3f)
                e.Vy = e.JumpSpeed;
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
            Enemy clanged = null;
            for (int i = 0; i < _enemies.Count; i++)
            {
                var e = _enemies[i];
                if (!e.Alive || e.LastStrikeId == p.StrikeId) continue;
                if (!e.Overlaps(minX, minY, maxX, maxY)) continue;
                e.LastStrikeId = p.StrikeId;
                e.SinceHit = 0f;
                if (CombatRules.CanDamage(tier, e.MinTier)) StrikeEnemy(p, e, tier);
                else if (clanged == null) clanged = e;
            }
            if (clanged == null) return;
            p.Vx = -p.Facing * _cfg.clangBounceSpeed;
            p.AttackTimer = 0f;
            Emit(SimEventType.Clang, clanged.Center, tier);
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
            Kills++;
            RefundMomentum(p);
            Emit(SimEventType.Kill, e.Center, tier);
        }

        /// <summary>
        /// A kill pushes the player on in the direction they struck. Moving that way already, they
        /// speed up (capped); moving the other way, the refund only brakes them — it never flips them.
        /// </summary>
        private void RefundMomentum(Player p)
        {
            var c = _cfg;
            if (p.Vx == 0f || Sign(p.Vx) == p.Facing)
                p.Vx = p.Facing * MathF.Min(MathF.Abs(p.Vx) + c.killMomentumRefund, c.maxSpeed);
            else
                p.Vx += p.Facing * MathF.Min(c.killMomentumRefund, MathF.Abs(p.Vx));
        }

        private void ResolveContacts()
        {
            var p = Player;
            if (p.InvulnTimer > 0f) return;
            for (int i = 0; i < _enemies.Count; i++)
            {
                var e = _enemies[i];
                if (!e.Alive || e.StunTimer > 0f || !p.Overlaps(e)) continue;
                int away = p.X > e.X ? 1 : p.X < e.X ? -1 : -p.Facing;
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

        private static float Dec(float timer, float dt) => timer > 0f ? timer - dt : 0f;

        private static int Sign(float v) => v > 0f ? 1 : v < 0f ? -1 : 0;

        private static float MoveToward(float value, float target, float maxDelta)
        {
            if (MathF.Abs(target - value) <= maxDelta) return target;
            return value + MathF.Sign(target - value) * maxDelta;
        }
    }
}
