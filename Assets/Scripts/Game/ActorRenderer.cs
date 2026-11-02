using System.Collections.Generic;
using Redline.Core;
using UnityEngine;

namespace Redline.Game
{
    /// <summary>
    /// Draws the moving actors: the player (pose, facing, lean, squash and stretch, tier glow),
    /// enemies (bob, hit pop, brute armor state), the strike slash and the grappling rope.
    /// </summary>
    /// <example>
    /// <code>
    /// var actors = new ActorRenderer(worldRoot);
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

        private readonly Transform _root;
        private readonly SpriteRenderer _player;
        private readonly SpriteRenderer _playerGlow;
        private readonly SpriteRenderer _slash;
        private readonly SpriteRenderer _rope;
        private readonly SpriteRenderer _ropeGlow;
        private readonly SpriteRenderer _anchor;
        private readonly List<SpriteRenderer> _enemies = new List<SpriteRenderer>(32);
        private readonly List<SpriteRenderer> _enemyGlows = new List<SpriteRenderer>(32);
        private float _stretch = 1f;
        private float _time;

        /// <summary>The player's drawn sprite, position, scale and flip (for afterimages).</summary>
        public SpriteRenderer PlayerView => _player;

        /// <summary>Creates the actor views under a parent transform.</summary>
        public ActorRenderer(Transform parent)
        {
            _root = new GameObject("Actors").transform;
            _root.SetParent(parent, false);
            _playerGlow = SpriteFactory.Create("PlayerGlow", _root, PixelArt.Glow, Color.clear, 6);
            _ropeGlow = SpriteFactory.Create("RopeGlow", _root, SpriteFactory.Square, Color.clear, 6);
            _rope = SpriteFactory.Create("Rope", _root, SpriteFactory.Square, RopeCore, 7);
            _anchor = SpriteFactory.Create("Anchor", _root, PixelArt.Glow, RopeCore, 7);
            _slash = SpriteFactory.Create("Slash", _root, PixelArt.Slash, Color.clear, 11);
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
            _stretch = Mathf.Lerp(_stretch, 1f, Mathf.Min(1f, dt * 12f));
            int tier = sim.PlayerTier;
            SyncPlayer(sim, playerFeet, tier);
            SyncStrike(sim, playerFeet, tier);
            SyncRope(sim, playerFeet, tier);
            SyncEnemies(sim, enemyFeet, tier);
        }

        private void SyncPlayer(RedlineSim sim, Vector2 feet, int tier)
        {
            var p = sim.Player;
            bool dead = sim.State == SimState.GameOver;
            _player.enabled = !dead;
            _playerGlow.enabled = !dead;
            if (dead) return;

            var color = RedlineGame.TierColors[tier - 1];
            if (p.InvulnTimer > 0f && Mathf.Repeat(Time.time * 16f, 2f) < 1f) color.a = 0.3f;
            _player.color = color;
            _player.sprite = p.Sliding ? PixelArt.PlayerSlide : PixelArt.Player;
            _player.flipX = p.Facing < 0;

            float height = p.Height * (p.Sliding ? 1f : _stretch);
            float k = p.Height / _player.sprite.bounds.size.y;
            var t = _player.transform;
            t.localScale = new Vector3(k / (p.Sliding ? 1f : _stretch), k * (p.Sliding ? 1f : _stretch), 1f);
            t.localPosition = new Vector3(feet.x, feet.y + height * 0.5f, 0f);
            t.localRotation = Quaternion.Euler(0f, 0f, PlayerTilt(p));

            var glow = RedlineGame.TierColors[tier - 1];
            glow.a = 0.14f + 0.12f * tier;
            _playerGlow.color = glow;
            float gs = 2.2f + 0.7f * (tier - 1) + Mathf.Sin(_time * 10f) * 0.08f * tier;
            _playerGlow.transform.localPosition = t.localPosition;
            _playerGlow.transform.localScale = new Vector3(gs, gs, 1f);
        }

        /// <summary>Leans into a run, and hangs along the rope while swinging.</summary>
        private static float PlayerTilt(Player p)
        {
            if (p.Hooked)
            {
                var d = p.HookAnchor - p.Center;
                return Mathf.Atan2(d.Y, d.X) * Mathf.Rad2Deg - 90f;
            }
            if (p.Sliding) return 0f;
            return Mathf.Clamp(-p.Vx * 0.7f, -12f, 12f);
        }

        private void SyncStrike(RedlineSim sim, Vector2 feet, int tier)
        {
            var p = sim.Player;
            if (!p.Attacking || sim.State == SimState.GameOver)
            {
                _slash.enabled = false;
                return;
            }
            sim.GetStrikeBox(out float minX, out float minY, out float maxX, out float maxY);
            var offset = feet - new Vector2(p.X, p.Y);
            _slash.enabled = true;
            _slash.flipX = p.Facing < 0;
            var c = RedlineGame.TierColors[tier - 1];
            c.a = Mathf.Clamp01(p.AttackTimer / sim.Config.attackActiveTime) * 0.95f;
            _slash.color = c;
            var size = _slash.sprite.bounds.size;
            _slash.transform.localPosition = new Vector3((minX + maxX) * 0.5f + offset.x, (minY + maxY) * 0.5f + offset.y, 0f);
            _slash.transform.localScale = new Vector3((maxX - minX) / size.x, (maxY - minY) / size.y, 1f);
        }

        private void SyncRope(RedlineSim sim, Vector2 feet, int tier)
        {
            var p = sim.Player;
            bool show = p.Hooked && sim.State != SimState.GameOver;
            _rope.enabled = _ropeGlow.enabled = _anchor.enabled = show;
            if (!show) return;
            var from = new Vector2(feet.x, feet.y + p.Height * 0.5f);
            var to = new Vector2(p.HookAnchor.X, p.HookAnchor.Y);
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
                if (alive) SyncEnemy(enemies[i], i < feet.Count ? feet[i] : new Vector2(enemies[i].X, enemies[i].Y), tier, i);
            }
        }

        private void SyncEnemy(Enemy e, Vector2 feet, int tier, int index)
        {
            var view = _enemies[index];
            bool brute = e.Kind == EnemyKind.Brute;
            bool breakable = brute && CombatRules.CanDamage(tier, e.ArmorTier);
            view.sprite = e.Kind == EnemyKind.Grunt ? PixelArt.Grunt : brute ? PixelArt.Brute : PixelArt.Drone;
            view.flipX = e.Facing < 0;

            var c = EnemyColor(e, breakable);
            view.color = c;

            float bob = e.Kind == EnemyKind.Grunt && e.Grounded ? Mathf.Abs(Mathf.Sin(_time * 12f + index)) * 0.08f : 0f;
            float pop = e.SinceHit < 0.12f ? 1.15f : 1f;
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
            glow.color = gc;
            float gs = e.Height * (breakable ? 2.4f : 1.8f);
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
            return Vector2.zero;
        }
    }
}
