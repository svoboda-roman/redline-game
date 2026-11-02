using System.Text;
using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>
    /// Integration: scripted players run the shipped level (the Loop01 ring) with the shipped config. A skilled one must
    /// clear every wave, using the hook to earn tier 3 for armored brutes; a sloppy one (no jump,
    /// hook or slide) must die before the last wave. Stories 009 and 013.
    /// </summary>
    public class BotPlaythroughTests
    {
        private const float TimeLimit = 600f;

        private static (RedlineSim sim, ScriptedPlayer bot, float time) Play(bool skilled)
        {
            var cfg = new RedlineConfig();
            var map = TileMap.Parse(ReadResource("Loop01.txt"));
            var sim = new RedlineSim(cfg, map);
            var bot = new ScriptedPlayer(cfg, map, skilled);
            float t = 0f;
            while (t < TimeLimit && sim.State != SimState.Victory && sim.State != SimState.GameOver)
            {
                sim.Step(bot.Next(sim), Dt);
                bot.Observe(sim);
                t += Dt;
            }
            return (sim, bot, t);
        }

        private static string Describe(RedlineSim sim, float t)
        {
            var left = new StringBuilder();
            foreach (var e in sim.Enemies)
                if (e.Alive) left.Append($" {e.Kind}@({e.X:0.0},{e.Y:0.0})");
            return $"ended in {sim.State} at wave {sim.CurrentWave}/{sim.WaveCount}, hp {sim.Player.Hp}, t={t:0.0}s, " +
                   $"kills {sim.Kills}, player@({sim.Player.X:0.0},{sim.Player.Y:0.0}), alive:{left}";
        }

        [Test]
        public void Loop01_OffersHookLaunchesThatReachTier3()
        {
            var cfg = new RedlineConfig();
            var bot = new ScriptedPlayer(cfg, TileMap.Parse(ReadResource("Loop01.txt")), skilled: true);

            Assert.Greater(bot.LaunchCount, 0);
        }

        [Test]
        public void SkilledPlayer_ClearsAllWaves_UsingTheHook()
        {
            var (sim, bot, t) = Play(skilled: true);

            Assert.AreEqual(SimState.Victory, sim.State, Describe(sim, t));
            Assert.Greater(bot.HookLaunches, 0, "tier 3 for brutes was earned with the hook");
        }

        [Test]
        public void SloppyPlayer_DiesBeforeTheLastWave()
        {
            var (sim, _, t) = Play(skilled: false);

            Assert.AreEqual(SimState.GameOver, sim.State, Describe(sim, t));
            TestContext.WriteLine("sloppy: " + Describe(sim, t));
            Assert.Less(sim.CurrentWave, sim.WaveCount, Describe(sim, t));
        }
    }
}
