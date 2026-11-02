using System.Collections.Generic;
using Redline.Core;
using UnityEngine;

namespace Redline.Game
{
    /// <summary>
    /// Draws the moving actors: the player (pose: idle, speed-timed run cycle, jump, fall, slide,
    /// rope swing; facing, lean, squash and stretch, tier glow),
    /// the sword (held, swung in an arc with a ghost trail), enemies (drawn larger than their
    /// hitboxes; bob, hit pop, brute armor state, the shield bearer's shield side, shooter, kamikaze
    /// and summoner telegraphs), shooter projectiles and the grappling rope. Positions arrive in
    /// draw space (the ring already unrolled around the camera).
    /// </summary>
    /// <example>
    /// <code>
    /// var actors = new ActorRenderer(worldRoot, effects);
    /// actors.Sync(sim, interpolatedFeet, enemyFeet, Time.deltaTime);
    /// actors.Squash(0.75f);   // on landing
    /// </code>
    /// </example>
    /// <remarks>Main-thread only. Enemy views are pooled and grow only when a wave is larger than any before.</remarks>
    public sealed class ActorRenderer
    {
        private static readonly Color GruntColor = new Color(1f, 0.55f, 0.15f);
        private static readonly Color BruteArmored = new Color(0.55f, 0.65f, 0.82f);
        private static readonly Color BruteBreakable = new Color(1f, 0.2f, 0.28f);
        private static readonly Color RopeCore = new Color(0.9f, 0.97f, 1f);
        private static readonly Color DroneColor = new Color(0.78f, 0.45f, 1f);
        private static readonly Color WindupColor = new Color(1f, 0.85f, 0.4f);
        private static readonly Color ShooterColor = new Color(0.5f, 1f, 0.4f);
        private static readonly Color ShieldColor = new Color(0.95f, 0.6f, 0.35f);
        private static readonly Color KamikazeColor = new Color(1f, 0.22f, 0.2f);
        private static readonly Color SummonerColor = new Color(0.55f, 0.4f, 1f);
        private static readonly Color ShotColor = new Color(1f, 0.4f, 0.15f);

        private readonly Transform _root;
        private readonly SpriteRenderer _player;
        private readonly SpriteRenderer _playerGlow;
        private readonly SpriteRenderer _sword;
        private readonly EffectsRenderer _effects;
        private float _swingHold;
        private float _swingAngle = RestAngle;
        private const float RestAngle = 28f;
        // While hanging on the rope the sword trails low behind, clear of the rope and the raised arms.
        private const float HookedRestAngle = 150f;
        private const float SwingFrom = 115f;
        private const float SwingTo = -120f;
        private const float SwordLength = 1.7f;
        private readonly SpriteRenderer _rope;
        private readonly SpriteRenderer _ropeGlow;
        private readonly SpriteRenderer _anchor;
        private readonly List<SpriteRenderer> _enemies = new List<SpriteRenderer>(32);
        private readonly List<SpriteRenderer> _enemyGlows = new List<SpriteRenderer>(32);
        private readonly List<SpriteRenderer> _shots = new List<SpriteRenderer>(24);
        private readonly List<SpriteRenderer> _shotGlows = new List<SpriteRenderer>(24);
        private float _stretch = 1f;
        private float _time;
        private float _fuseTime;
        private float _runPhase;
        // Run cycle: frames advanced per tile travelled, so the legs speed up with |Vx|
        // (about 11 frames/s at run speed 9, 26 at 22).
        private const float RunFramesPerTile = 1.2f;
        private const float RunMinSpeed = 0.6f;
        // |Vy| below this reads as the top of the arc and keeps the rising pose.
        private const float FallPoseSpeed = 1.5f;

        /// <summary>The player's drawn sprite, position, scale and flip (for afterimages).</summary>
        public SpriteRenderer PlayerView => _player;

        /// <summary>Creates the actor views under a parent transform.</summary>
        public ActorRenderer(Transform parent, EffectsRenderer effects)
        {
            _effects = effects;
            _root = new GameObject("Actors").transform;
            _root.SetParent(parent, false);
            _playerGlow = SpriteFactory.Create("PlayerGlow", _root, PixelArt.Glow, Color.clear, 6);
            _ropeGlow = SpriteFactory.Create("RopeGlow", _root, SpriteFactory.Square, Color.clear, 6);
            _rope = SpriteFactory.Create("Rope", _root, SpriteFactory.Square, RopeCore, 7);
            _anchor = SpriteFactory.Create("Anchor", _root, PixelArt.Glow, RopeCore, 7);
            _sword = SpriteFactory.Create("Sword", _root, PixelArt.Sword, Color.white, 11);
            _player = SpriteFactory.Create("Player", _root, PixelArt.Player, Color.white, 10);
        }

        /// <summary>Squashes (below 1) or stretches (above 1) the player for a moment.</summary>
        public void Squash(float amount)
        {
            _stretch = amount;
        }

        /// <summary>Updates every actor view from the simulation.</summary>
        public void Sync(RedlineSim sim, Vector2 playerFeet, IReadOnlyList<Vector2> enemyFeet, float dt)
        {
            _time += dt;
            _fuseTime = sim.Config.kamikazeFuseTime;
            _stretch = Mathf.Lerp(_stretch, 1f, Mathf.Min(1f, dt * 12f));
            int tier = sim.PlayerTier;
            SyncPlayer(sim, playerFeet, tier, dt);
            SyncSword(sim, playerFeet, tier, dt);
            SyncRope(sim, playerFeet, tier);
            SyncEnemies(sim, enemyFeet, tier);
            SyncProjectiles(sim, playerFeet, tier);
        }

        /// <summary>
        /// Projectiles, placed relative to the drawn player (the ring is unrolled around them):
        /// hot orange while hostile, the player's tier colour once reflected. Views are pooled.
        /// </summary>
        private void SyncProjectiles(RedlineSim sim, Vector2 playerFeet, int tier)
        {
            var shots = sim.Projectiles;
            while (_shots.Count < shots.Count)
            {
                _shotGlows.Add(SpriteFactory.Create("ShotGlow", _root, PixelArt.Glow, Color.clear, 8));
                _shots.Add(SpriteFactory.Create("Shot", _root, PixelArt.Projectile, ShotColor, 9));
            }
            var p = sim.Player;
            for (int i = 0; i < _shots.Count; i++)
            {
                bool on = i < shots.Count;
                _shots[i].enabled = on;
                _shotGlows[i].enabled = on;
                if (!on) continue;
                var shot = shots[i];
                var c = shot.Reflected ? RedlineGame.TierColors[tier - 1] : ShotColor;
                var pos = new Vector3(playerFeet.x + sim.Map.Dx(p.X, shot.X), shot.Y + shot.Height * 0.5f, 0f);
                float pulse = 1f + Mathf.Sin(_time * 30f + i) * 0.12f;
                var t = _shots[i].transform;
                t.localPosition = pos;
                t.localScale = new Vector3(shot.Width * 1.5f * pulse, shot.Width * 1.5f * pulse, 1f);
                t.localRotation = Quaternion.Euler(0f, 0f, _time * 360f);
                _shots[i].color = Color.Lerp(c, Color.white, 0.35f);
                var gc = c;
                gc.a = 0.45f;
                _shotGlows[i].color = gc;
                _shotGlows[i].transform.localPosition = pos;
                float gs = shot.Width * 4.5f * pulse;
                _shotGlows[i].transform.localScale = new Vector3(gs, gs, 1f);
            }
        }

        private void SyncPlayer(RedlineSim sim, Vector2 feet, int tier, float dt)
        {
            var p = sim.Player;
            bool dead = sim.State == SimState.GameOver;
            _player.enabled = !dead;
            _playerGlow.enabled = !dead;
            if (dead) return;

            var color = RedlineGame.TierColors[tier - 1];
            if (p.InvulnTimer > 0f && Mathf.Repeat(Time.time * 16f, 2f) < 1f) color.a = 0.3f;
            _player.color = color;
            _player.sprite = PlayerPose(p, dt);
            _player.flipX = p.Facing < 0;

            float height = p.Height * (p.Sliding ? 1f : _stretch);
            float k = p.Height / _player.sprite.bounds.size.y;
            var t = _player.transform;
            t.localScale = new Vector3(k / (p.Sliding ? 1f : _stretch), k * (p.Sliding ? 1f : _stretch), 1f);
            t.localPosition = new Vector3(feet.x, feet.y + height * 0.5f, 0f);
            t.localRotation = Quaternion.Euler(0f, 0f, PlayerTilt(p, sim.Map));

            var glow = RedlineGame.TierColors[tier - 1];
            glow.a = 0.14f + 0.12f * tier;
            _playerGlow.color = glow;
            float gs = 2.2f + 0.7f * (tier - 1) + Mathf.Sin(_time * 10f) * 0.08f * tier;
            _playerGlow.transform.localPosition = t.localPosition;
            _playerGlow.transform.localScale = new Vector3(gs, gs, 1f);
        }

        /// <summary>
        /// Picks the player's sprite: swing pose on the rope, slide, jump or fall in the air, a run
        /// cycle timed by distance travelled, or the standing pose. Only swaps cached sprites.
        /// </summary>
        private Sprite PlayerPose(Player p, float dt)
        {
            if (p.Hooked) return PixelArt.PlayerSwing;
            if (p.Sliding) return PixelArt.PlayerSlide;
            if (!p.Grounded)
            {
                _runPhase = 0f;
                return p.Vy < -FallPoseSpeed ? PixelArt.PlayerFall : PixelArt.PlayerJump;
            }
            float speed = Mathf.Abs(p.Vx);
            if (speed < RunMinSpeed)
            {
                _runPhase = 0f;
                return PixelArt.Player;
            }
            _runPhase = Mathf.Repeat(_runPhase + speed * dt * RunFramesPerTile, PixelArt.PlayerRunFrameCount);
            return PixelArt.PlayerRunFrame((int)_runPhase);
        }

        /// <summary>Leans into a run, and hangs along the rope while swinging.</summary>
        private static float PlayerTilt(Player p, TileMap map)
        {
            if (p.Hooked)
            {
                var d = map.Near(p.Center, p.HookAnchor) - p.Center;
                return Mathf.Atan2(d.Y, d.X) * Mathf.Rad2Deg - 90f;
            }
            if (p.Sliding) return 0f;
            return Mathf.Clamp(-p.Vx * 0.7f, -12f, 12f);
        }

        /// <summary>
        /// The sword rests over the shoulder; a strike swings it from behind the head down in front
        /// (matching the strike box), leaving a fading trail, then it eases back to rest.
        /// </summary>
        private void SyncSword(RedlineSim sim, Vector2 feet, int tier, float dt)
        {
            var p = sim.Player;
            bool show = sim.State != SimState.GameOver;
            _sword.enabled = show;
            if (!show) return;

            float target = p.Hooked ? HookedRestAngle : p.Sliding ? -70f : RestAngle;
            if (p.Attacking)
            {
                float t = 1f - Mathf.Clamp01(p.AttackTimer / sim.Config.attackActiveTime);
                float ease = 1f - (1f - t) * (1f - t) * (1f - t);
                _swingAngle = Mathf.Lerp(SwingFrom, SwingTo, ease);
                _swingHold = 0.1f;
            }
            else if (_swingHold > 0f)
            {
                _swingHold -= dt;
            }
            else
            {
                _swingAngle = Mathf.Lerp(_swingAngle, target, Mathf.Min(1f, dt * 14f));
            }

            float side = p.Facing >= 0 ? 1f : -1f;
            var t2 = _sword.transform;
            t2.localPosition = new Vector3(feet.x + side * 0.28f, feet.y + p.Height * 0.5f, 0f);
            t2.localRotation = Quaternion.Euler(0f, 0f, _swingAngle * side);
            float k = SwordLength / _sword.sprite.bounds.size.y;
            t2.localScale = new Vector3(k, k, 1f);
            _sword.flipX = side < 0f;
            var c = Color.Lerp(Color.white, RedlineGame.TierColors[tier - 1], 0.35f);
            _sword.color = c;

            if (p.Attacking)
            {
                var ghost = RedlineGame.TierColors[tier - 1];
                ghost.a = 0.5f;
                _effects.Ghost(_sword.sprite, t2.localPosition, t2.localRotation, t2.localScale, _sword.flipX, ghost, 0.12f);
            }
        }

        private void SyncRope(RedlineSim sim, Vector2 feet, int tier)
        {
            var p = sim.Player;
            bool show = p.Hooked && sim.State != SimState.GameOver;
            _rope.enabled = _ropeGlow.enabled = _anchor.enabled = show;
            if (!show) return;
            var from = new Vector2(feet.x, feet.y + p.Height * 0.5f);
            var to = new Vector2(feet.x + sim.Map.Dx(p.X, p.HookAnchor.X), p.HookAnchor.Y);
            var d = to - from;
            var mid = (from + to) * 0.5f;
            var rot = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            _rope.transform.localPosition = mid;
            _rope.transform.localRotation = rot;
            _rope.transform.localScale = new Vector3(d.magnitude, 0.06f, 1f);
            var glow = RedlineGame.TierColors[tier - 1];
            glow.a = 0.35f;
            _ropeGlow.color = glow;
            _ropeGlow.transform.localPosition = mid;
            _ropeGlow.transform.localRotation = rot;
            _ropeGlow.transform.localScale = new Vector3(d.magnitude, 0.22f, 1f);
            _anchor.transform.localPosition = to;
            _anchor.transform.localScale = new Vector3(0.6f, 0.6f, 1f);
        }

        private void SyncEnemies(RedlineSim sim, IReadOnlyList<Vector2> feet, int tier)
        {
            var enemies = sim.Enemies;
            while (_enemies.Count < enemies.Count)
            {
                _enemyGlows.Add(SpriteFactory.Create("EnemyGlow", _root, PixelArt.Glow, Color.clear, 7));
                _enemies.Add(SpriteFactory.Create("Enemy", _root, PixelArt.Grunt, GruntColor, 8));
            }
            for (int i = 0; i < _enemies.Count; i++)
            {
                bool alive = i < enemies.Count && enemies[i].Alive;
                _enemies[i].enabled = alive;
                _enemyGlows[i].enabled = alive;
                if (alive) SyncEnemy(sim, enemies[i], i < feet.Count ? feet[i] : new Vector2(enemies[i].X, enemies[i].Y), tier, i);
            }
        }

        private void SyncEnemy(RedlineSim sim, Enemy e, Vector2 feet, int tier, int index)
        {
            var view = _enemies[index];
            bool brute = e.Kind == EnemyKind.Brute;
            bool breakable = brute && CombatRules.CanDamage(tier, e.ArmorTier);
            view.sprite = EnemySprite(e.Kind);
            // The shield bearer is drawn by where its shield is, not where it walks: a lagging
            // shield (its back open) reads at a glance.
            view.flipX = (e.Kind == EnemyKind.Shield ? e.ShieldDir : e.Facing) < 0;

            var c = EnemyColor(e, breakable);
            view.color = c;

            float bob = e.Kind == EnemyKind.Grunt && e.Grounded ? Mathf.Abs(Mathf.Sin(_time * 12f + index)) * 0.08f : 0f;
            float pop = (e.SinceHit < 0.12f ? 1.15f : 1f) * (brute ? 1.2f : 1.15f) * FuseSwell(e);
            float k = e.Height / view.sprite.bounds.size.y * pop;
            var t = view.transform;
            t.localScale = new Vector3(k * (1f + bob), k * (1f - bob), 1f);
            var offset = EnemyOffset(e, index);
            t.localPosition = new Vector3(feet.x + offset.x, feet.y + e.Height * pop * (1f - bob) * 0.5f + offset.y, 0f);
            // A crashed brute wobbles, dazed.
            t.localRotation = brute && e.Mode == BruteMode.Crashed
                ? Quaternion.Euler(0f, 0f, Mathf.Sin(_time * 9f) * 10f)
                : Quaternion.identity;

            var glow = _enemyGlows[index];
            var gc = c;
            gc.a = breakable ? 0.3f + 0.15f * Mathf.Sin(_time * 14f) : brute ? 0.1f : 0.14f;
            float gs = e.Height * (breakable ? 2.4f : 1.8f);
            if (Telegraphing(e))
            {
                // Shooter wind-up and summoner cast: the glow swells; a lit fuse shows the blast radius.
                bool fuse = e.Kind == EnemyKind.Kamikaze;
                gc = fuse ? KamikazeColor : WindupColor;
                gc.a = 0.3f + 0.15f * Mathf.Sin(_time * (fuse ? 40f : 20f));
                gs = fuse ? sim.Config.kamikazeBlastRadius * 2f * 1.4f : e.Height * 2.6f;
            }
            glow.color = gc;
            glow.transform.localPosition = t.localPosition;
            glow.transform.localScale = new Vector3(gs, gs, 1f);
        }

        /// <summary>Base colour per kind and state, with flashes for hits and telegraphs.</summary>
        private Color EnemyColor(Enemy e, bool breakable)
        {
            if (e.SinceHit < 0.08f) return Color.white;
            bool blink = Mathf.Repeat(_time * 18f, 2f) < 1f;
            switch (e.Kind)
            {
                case EnemyKind.Grunt:
                    return GruntColor;
                case EnemyKind.Drone:
                    return e.DroneState == DroneMode.Windup && blink ? Color.white : DroneColor;
                case EnemyKind.Shooter:
                    return e.Act == EnemyAct.Windup && blink ? WindupColor : ShooterColor;
                case EnemyKind.Shield:
                    return ShieldColor;
                case EnemyKind.Kamikaze:
                    // The fuse flashes faster as it burns down.
                    if (e.Act != EnemyAct.Fuse) return KamikazeColor;
                    return Mathf.Repeat(_time * (14f + 40f * FuseProgress(e)), 2f) < 1f ? Color.white : KamikazeColor;
                case EnemyKind.Summoner:
                    return e.Act == EnemyAct.Cast && blink ? Color.white : SummonerColor;
                default:
                    if (e.Mode == BruteMode.Windup) return blink ? WindupColor : BruteArmored;
                    return breakable ? BruteBreakable : BruteArmored;
            }
        }

        /// <summary>Shake while a brute telegraphs a charge; gentle hover sway for drones.</summary>
        private Vector2 EnemyOffset(Enemy e, int index)
        {
            if (e.Kind == EnemyKind.Brute && e.Mode == BruteMode.Windup)
                return new Vector2(Mathf.Sin(_time * 70f) * 0.07f, 0f);
            if (e.Kind == EnemyKind.Drone && e.DroneState == DroneMode.Hover)
                return new Vector2(0f, Mathf.Sin(_time * 4f + index) * 0.12f);
            // Shooter recoils back while winding up; a fusing kamikaze trembles; a turning shield wobbles.
            if (e.Kind == EnemyKind.Shooter && e.Act == EnemyAct.Windup)
                return new Vector2(-e.Facing * 0.06f, 0f);
            if (e.Kind == EnemyKind.Kamikaze && e.Act == EnemyAct.Fuse)
                return new Vector2(Mathf.Sin(_time * 80f) * 0.06f, 0f);
            if (e.Kind == EnemyKind.Shield && e.TurnTimer > 0f)
                return new Vector2(Mathf.Sin(_time * 50f) * 0.05f, 0f);
            if (e.Kind == EnemyKind.Summoner && e.Act == EnemyAct.Cast)
                return new Vector2(0f, Mathf.Abs(Mathf.Sin(_time * 10f)) * 0.1f);
            return Vector2.zero;
        }

        private static Sprite EnemySprite(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Grunt: return PixelArt.Grunt;
                case EnemyKind.Brute: return PixelArt.Brute;
                case EnemyKind.Shooter: return PixelArt.Shooter;
                case EnemyKind.Shield: return PixelArt.ShieldBearer;
                case EnemyKind.Kamikaze: return PixelArt.Kamikaze;
                case EnemyKind.Summoner: return PixelArt.Summoner;
                default: return PixelArt.Drone;
            }
        }

        /// <summary>Shooter wind-up, kamikaze fuse or summoner cast in progress.</summary>
        private static bool Telegraphing(Enemy e) =>
            (e.Kind == EnemyKind.Shooter && e.Act == EnemyAct.Windup)
            || (e.Kind == EnemyKind.Kamikaze && e.Act == EnemyAct.Fuse)
            || (e.Kind == EnemyKind.Summoner && e.Act == EnemyAct.Cast);

        /// <summary>0 when a kamikaze's fuse is lit, 1 at the blast (0 when not fusing).</summary>
        private float FuseProgress(Enemy e) =>
            e.Kind == EnemyKind.Kamikaze && e.Act == EnemyAct.Fuse && _fuseTime > 0f
                ? Mathf.Clamp01(1f - e.ModeTimer / _fuseTime)
                : 0f;

        /// <summary>A fusing kamikaze swells toward the blast.</summary>
        private float FuseSwell(Enemy e) => 1f + 0.3f * FuseProgress(e);
    }
}
