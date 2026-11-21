using NUnit.Framework;
using Redline.Core.Audio;

namespace Redline.Tests
{
    /// <summary>Audio overhaul (production/audio/music-sfx-overhaul): tier mix, ducks, variants, tier hold.</summary>
    public class AudioRulesTests
    {
        [Test]
        public void ForTier_MatchesDirectionTable()
        {
            var t1 = AudioRules.ForTier(1);
            var t2 = AudioRules.ForTier(2);
            var t3 = AudioRules.ForTier(3);
            Assert.AreEqual(1500f, t1.IntenseCutoffHz);
            Assert.AreEqual(0f, AudioRules.DbToLinear(t1.IntenseDb));
            Assert.AreEqual(6000f, t2.IntenseCutoffHz);
            Assert.AreEqual(-3f, t2.CalmDb);
            Assert.AreEqual(AudioRules.OpenCutoffHz, t3.IntenseCutoffHz);
            Assert.AreEqual(-9f, t3.CalmDb);
            Assert.AreEqual(1f, t3.LiftDb);
            Assert.AreEqual(3f, AudioRules.ForTier(7).CalmDb + 12f); // clamped to tier 3
        }

        [Test]
        public void At_WholePositions_EqualTiers_AndMidpointIsBetween()
        {
            Assert.AreEqual(AudioRules.ForTier(2).CalmDb, AudioRules.At(2f).CalmDb, 0.01f);
            var mid = AudioRules.At(1.5f);
            Assert.That(mid.IntenseCutoffHz, Is.GreaterThan(1500f).And.LessThan(6000f));
            Assert.That(mid.IntenseDb, Is.GreaterThan(AudioRules.SilentDb));
        }

        [Test]
        public void StepPosition_UpTakesUpSeconds_DownTakesDownSeconds()
        {
            float up = AudioRules.StepPosition(1f, 2, 0.75f, 1.5f, 3f);
            Assert.AreEqual(1.5f, up, 1e-4f);
            float down = AudioRules.StepPosition(2f, 1, 1.5f, 1.5f, 3f);
            Assert.AreEqual(1.5f, down, 1e-4f);
            Assert.AreEqual(2f, AudioRules.StepPosition(1.9f, 2, 5f, 1.5f, 3f));
        }

        [Test]
        public void Ducks_DeepestWins_NotStacked()
        {
            var d = new DuckResolver(8, -9f, -8f);
            d.Add(DuckTarget.Music, -2f, 0f, 1f, 0f, 0f);
            d.Add(DuckTarget.Music, -5f, 0f, 1f, 0f, 0f);
            Assert.AreEqual(-5f, d.Evaluate(DuckTarget.Music, 0.5f), 1e-4f);
        }

        [Test]
        public void Ducks_ClampToFloors()
        {
            var d = new DuckResolver(8, -9f, -8f);
            d.Add(DuckTarget.Music, -20f, 0f, 1f, 0f, 0f);
            d.Add(DuckTarget.Ambience, -20f, 0f, 1f, 0f, 0f);
            Assert.AreEqual(-9f, d.Evaluate(DuckTarget.Music, 0.5f), 1e-4f);
            Assert.AreEqual(-8f, d.Evaluate(DuckTarget.Ambience, 0.5f), 1e-4f);
        }

        [Test]
        public void Ducks_AttackHoldRelease()
        {
            Assert.AreEqual(-2f, DuckResolver.Envelope(-4f, 0.2f, 0.1f, 0.4f, 0.1f), 1e-4f);
            Assert.AreEqual(-4f, DuckResolver.Envelope(-4f, 0.2f, 0.1f, 0.4f, 0.25f), 1e-4f);
            Assert.AreEqual(-2f, DuckResolver.Envelope(-4f, 0.2f, 0.1f, 0.4f, 0.5f), 1e-4f);
            Assert.AreEqual(0f, DuckResolver.Envelope(-4f, 0.2f, 0.1f, 0.4f, 0.8f));
            var d = new DuckResolver();
            d.Add(DuckTarget.Music, -4f, 0f, 0.1f, 0.1f, 0f);
            Assert.AreEqual(0f, d.Evaluate(DuckTarget.Music, 1f));
        }

        [Test]
        public void PickVariant_NeverRepeatsLast()
        {
            for (int i = 0; i < 100; i++)
            {
                float r = i / 100f;
                Assert.AreNotEqual(2, AudioRules.PickVariant(3, 2, -1, r));
                int v = AudioRules.PickVariant(5, 1, 3, r);
                Assert.AreNotEqual(1, v);
                Assert.AreNotEqual(3, v);
                Assert.That(v, Is.InRange(0, 4));
            }
            Assert.AreEqual(0, AudioRules.PickVariant(1, 0, 0, 0.9f));
            Assert.AreEqual(1, AudioRules.PickVariant(2, 0, -1, 0.99f));
        }

        [Test]
        public void TierTracker_RisesAtOnce_FallsAfterHold_StingGated()
        {
            var t = new MusicTierTracker(2.5f, 4f);
            Assert.AreEqual(3, t.Update(3, 0.1f));
            Assert.AreEqual(3, t.Tier);
            t.Update(1, 2.0f);
            Assert.AreEqual(3, t.Tier);
            t.Update(1, 0.6f);
            Assert.AreEqual(2, t.Tier);
            Assert.AreEqual(0, t.Update(3, 0.1f)); // within 4 s of the last sting
            Assert.AreEqual(3, t.Tier);
        }
    }
}
