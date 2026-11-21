using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>Character select: the Ronin keeps the katana tuning, the Reaper's scythe reaches further, swings slower,
    /// hits harder and heals per connecting strike; the choice round-trips through the settings file.</summary>
    public class CharacterTests
    {
        [Test]
        public void Ronin_LeavesTheConfigUnchanged()
        {
            var c = new RedlineConfig();
            var before = new RedlineConfig();
            Characters.Apply(c, PlayerCharacter.Ronin);
            Assert.AreEqual(before.attackRange, c.attackRange);
            Assert.AreEqual(before.attackCooldown, c.attackCooldown);
            Assert.AreEqual(before.baseDamage, c.baseDamage);
            Assert.AreEqual(0, c.healPerHit);
        }

        [Test]
        public void Reaper_ReachesFurther_SwingsSlower_HitsHarder_Heals()
        {
            var c = new RedlineConfig();
            var katana = new RedlineConfig();
            Characters.Apply(c, PlayerCharacter.Reaper);
            Assert.Greater(c.attackRange, katana.attackRange, "longer reach");
            Assert.Greater(c.attackCooldown, katana.attackCooldown, "slower swings");
            Assert.Greater(c.baseDamage, katana.baseDamage, "a bit more damage");
            Assert.AreEqual(c.reaperHealPerHit, c.healPerHit);
            Assert.AreEqual(katana.baseDamage * katana.reaperDamageMultiplier, c.baseDamage, 1e-5f);
        }

        [Test]
        public void ReaperStrike_ThatConnects_HealsOneHp()
        {
            var cfg = NoWaves();
            Characters.Apply(cfg, PlayerCharacter.Reaper);
            var sim = new RedlineSim(cfg, Box(80, 14));
            var p = sim.Player;
            p.X = 10f;
            p.Hp = 30;   // below the Reaper's 50 max
            p.InvulnTimer = 1f;   // isolate the heal from any contact damage
            var grunt = sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(p.X + 1.9f, p.Y));
            float hpBefore = grunt.Hp;
            sim.Step(new InputFrame { AttackPressed = true }, Dt);
            Assert.Less(grunt.Hp, hpBefore, "the strike connected (1.9 tiles: beyond the katana, inside the scythe)");
            Assert.AreEqual(30 + cfg.reaperHealPerHit, p.Hp);
        }

        [Test]
        public void ReaperHeal_NeverExceedsMaxHp()
        {
            var cfg = NoWaves();
            Characters.Apply(cfg, PlayerCharacter.Reaper);
            var sim = new RedlineSim(cfg, Box(80, 14));
            var p = sim.Player;
            p.X = 10f;
            p.InvulnTimer = 1f;
            sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(p.X + 1.9f, p.Y));
            sim.Step(new InputFrame { AttackPressed = true }, Dt);
            Assert.AreEqual(cfg.playerMaxHp, p.Hp);
        }

        [Test]
        public void KatanaStrike_DoesNotHeal()
        {
            var cfg = NoWaves();
            var sim = new RedlineSim(cfg, Box(80, 14));
            var p = sim.Player;
            p.X = 10f;
            p.Hp = 50;
            p.InvulnTimer = 1f;
            var grunt = sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(p.X + 1.2f, p.Y));
            float hpBefore = grunt.Hp;
            sim.Step(new InputFrame { AttackPressed = true }, Dt);
            Assert.Less(grunt.Hp, hpBefore, "the strike connected");
            Assert.AreEqual(50, p.Hp);
        }

        [Test]
        public void Reaper_HasHalfTheBaseHp()
        {
            var c = new RedlineConfig();
            int baseHp = c.playerMaxHp;
            Characters.Apply(c, PlayerCharacter.Reaper);
            Assert.AreEqual(baseHp + c.reaperMaxHpDelta, c.playerMaxHp);
            Assert.AreEqual(50, c.playerMaxHp, "100-HP base: the Reaper has 50");
        }

        [Test]
        public void ReaperOnTheHook_CannotStrike()
        {
            var cfg = NoWaves();
            Characters.Apply(cfg, PlayerCharacter.Reaper);
            var sim = new RedlineSim(cfg, Box(80, 14));
            var p = sim.Player;
            p.X = 10f;
            p.Hooked = true;
            p.HookAnchor = new Vec2(p.X, p.Y + 5f);
            sim.Step(new InputFrame { AttackPressed = true, HookHeld = true }, Dt);
            Assert.AreEqual(0f, p.AttackTimer, "no strike while hanging on the rope");
        }

        [Test]
        public void RoninOnTheHook_StillStrikes()
        {
            var cfg = NoWaves();
            var sim = new RedlineSim(cfg, Box(80, 14));
            var p = sim.Player;
            p.X = 10f;
            p.Hooked = true;
            p.HookAnchor = new Vec2(p.X, p.Y + 5f);
            sim.Step(new InputFrame { AttackPressed = true, HookHeld = true }, Dt);
            Assert.Greater(p.AttackTimer, 0f, "the katana strikes from the rope");
        }

        [TestCase(0, PlayerCharacter.Ronin)]
        [TestCase(1, PlayerCharacter.Reaper)]
        [TestCase(7, PlayerCharacter.Ronin)]
        [TestCase(-1, PlayerCharacter.Ronin)]
        public void FromIndex_ClampsUnknownToRonin(int index, PlayerCharacter expected)
        {
            Assert.AreEqual(expected, Characters.FromIndex(index));
        }

        [Test]
        public void Settings_RoundTripTheCharacter()
        {
            var s = new SettingsData { Character = 1 };
            string json = SettingsSave.Serialize(s);
            var back = SettingsSave.Deserialize(json, _ => true, out _);
            Assert.AreEqual(1, back.Character);
        }
    }
}
