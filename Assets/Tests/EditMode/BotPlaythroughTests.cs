using System;
using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>
    /// Integration: a simple scripted player must be able to clear every wave of Arena01 with
    /// the shipped config. Proves the arena, the waves and "only top tier breaks a brute" are
    /// winnable together. The bot is deliberately naive — a human has more tools (hook, wall-jump).
    /// </summary>
    public class BotPlaythroughTests
    {
        private sealed class Bot
        {
            private bool _retreat;
            private int _jumpHold;
            private Enemy _target;

            public InputFrame Next(RedlineSim sim)
            {
                var p = sim.Player;
                var input = new InputFrame();
                // Stick with one target until it dies; otherwise prefer enemies on the bot's own
                // level (enemies above come down to the player).
                Enemy target = _target != null && _target.Alive && _target.Grounded
                               && MathF.Abs(_target.Y - p.Y) < 1.5f ? _target : null;
                float best = target != null ? -1f : float.MaxValue;
                foreach (var e in sim.Enemies)
                {
                    if (!e.Alive) continue;
                    bool level = MathF.Abs(e.Y - p.Y) < 1.5f && e.Grounded;
                    float d = MathF.Abs(e.X - p.X) + (level ? 0f : 1000f);
                    if (d < best)
                    {
                        best = d;
                        target = e;
                    }
                }
                if (target == null)
                {
                    _retreat = false;
                    return input;
                }
                _target = target;

                float dx = target.X - p.X;
                int toward = dx >= 0f ? 1 : -1;
                float gap = MathF.Abs(dx) - (p.Width + target.Width) * 0.5f;
                bool sameLevel = MathF.Abs(target.Y - p.Y) < 1.5f;
                int dir = toward;

                if (target.Kind == EnemyKind.Brute)
                {
                    if (_retreat)
                    {
                        dir = -toward;
                        // Back off just far enough for a run-up (top speed takes ~0.15 s, then slide).
                        if (gap > 4f || p.WallDir == dir || BruteAhead(sim, target, dir)) _retreat = false;
                    }
                    else if (p.Grounded && sameLevel && gap < 5f && !p.Sliding
                             && MathF.Abs(p.Vx) + sim.Config.slideBoost >= sim.Config.tier3Speed && Math.Sign(p.Vx) == dir)
                    {
                        input.SlidePressed = true;
                    }
                    if (!_retreat && sameLevel && gap <= sim.Config.attackRange && p.Facing == dir && sim.PlayerTier >= 3)
                        input.AttackPressed = true;
                    if (!_retreat && sameLevel && gap < 0.4f && sim.PlayerTier < 3) _retreat = true;
                }
                else if (sameLevel && gap <= sim.Config.attackRange && p.Facing == dir)
                {
                    input.AttackPressed = true;
                }

                // Hop over floor spikes in the way, and up toward enemies standing above.
                int footY = (int)MathF.Floor(p.Y + 0.01f);
                bool spikesAhead = false;
                for (float look = 0.5f; look <= 1.5f; look += 0.5f)
                    if (sim.Map.Get((int)MathF.Floor(p.X + dir * look), footY) == Tile.Spike) spikesAhead = true;
                // Wait for an enemy above to come down, out from under where it will land.
                if (!sameLevel && !spikesAhead) dir = MathF.Abs(dx) < 3f ? -toward : 0;
                bool bruteBlocking = dir != 0 && BruteAhead(sim, target, dir);
                if (p.Grounded && dir != 0 && (spikesAhead || bruteBlocking))
                {
                    input.JumpPressed = true;
                    _jumpHold = 20;
                }
                if (_jumpHold > 0)
                {
                    input.JumpHeld = true;
                    _jumpHold--;
                }

                input.MoveX = dir;
                return input;
            }

            private static bool BruteAhead(RedlineSim sim, Enemy except, int dir)
            {
                var p = sim.Player;
                foreach (var e in sim.Enemies)
                    if (e.Alive && e.Kind == EnemyKind.Brute && e != except
                        && Math.Sign(e.X - p.X) == dir && MathF.Abs(e.X - p.X) < 3f && MathF.Abs(e.Y - p.Y) < 1.5f)
                        return true;
                return false;
            }

            public void Observe(RedlineSim sim)
            {
                foreach (var ev in sim.Events)
                    if (ev.Type == SimEventType.Clang || ev.Type == SimEventType.PlayerHurt)
                        _retreat = true;
            }
        }

        [Test]
        public void ScriptedBot_ClearsAllWavesOfArena01()
        {
            // Arrange
            var cfg = new RedlineConfig();
            var sim = new RedlineSim(cfg, TileMap.Parse(ReadResource("Arena01.txt")));
            var bot = new Bot();
            int brutesKilled = 0;
            const float timeLimit = 600f;

            // Act
            float t = 0f;
            while (t < timeLimit && sim.State != SimState.Victory && sim.State != SimState.GameOver)
            {
                sim.Step(bot.Next(sim), Dt);
                bot.Observe(sim);
                foreach (var ev in sim.Events)
                    if (ev.Type == SimEventType.Kill && ev.Tier == 3) brutesKilled++;
                t += Dt;
            }

            // Assert
            var left = new System.Text.StringBuilder();
            foreach (var e in sim.Enemies)
                if (e.Alive) left.Append($" {e.Kind}@({e.X:0.0},{e.Y:0.0})");
            Assert.AreEqual(SimState.Victory, sim.State,
                $"bot ended in {sim.State} at wave {sim.CurrentWave}/{sim.WaveCount}, hp {sim.Player.Hp}, t={t:0.0}s, " +
                $"kills {sim.Kills}, player@({sim.Player.X:0.0},{sim.Player.Y:0.0}), alive:{left}");
            int totalBrutes = 0;
            foreach (int b in cfg.waveBrutes) totalBrutes += b;
            Assert.GreaterOrEqual(brutesKilled, totalBrutes, "every brute fell to a tier-3 strike");
        }
    }
}
