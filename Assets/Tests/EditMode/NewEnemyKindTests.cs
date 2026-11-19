using System;
using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>
    /// The four wave-2+ enemy kinds: shooter (telegraphed shots, sword reflects them), shield
    /// bearer (front shield, open from behind and above), kamikaze (rush, fuse, blast) and
    /// summoner (flees, summons drones up to a cap).
    /// </summary>
    public class NewEnemyKindTests
    {
        private RedlineConfig _cfg;
        private RedlineSim _sim;

        [SetUp]
        public void SetUp()
        {
            _cfg = NoWaves();
            _sim = new RedlineSim(_cfg, Box(60, 20));
            Teleport(_sim, 20f, 1f);
        }

        private static bool Saw(RedlineSim sim, SimEventType type) => HasEvent(sim, type);

        // ------------------------------------------------------------------ shooter

        [Test]
        public void Shooter_TelegraphsThenFires_AndTheProjectileHurts()
        {
            _sim.SpawnEnemy(EnemyKind.Shooter, new Vec2(28f, 1f));
            float windupAt = -1f, shotAt = -1f, t = 0f;
            int hp = _sim.Player.Hp;
            for (int i = 0; i < 300 && _sim.Player.Hp == hp; i++)
            {
                _sim.Step(Hold(0f), Dt);
                t += Dt;
                if (windupAt < 0f && Saw(_sim, SimEventType.ShooterWindup)) windupAt = t;
                if (shotAt < 0f && Saw(_sim, SimEventType.Shoot))
                {
                    shotAt = t;
                    Assert.AreEqual(1, _sim.Projectiles.Count, "one projectile in flight");
                }
            }

            Assert.Greater(windupAt, 0f, "telegraphs");
            Assert.Greater(shotAt, windupAt, "fires after the telegraph");
            Assert.GreaterOrEqual(shotAt - windupAt, _cfg.shooterWindupTime - Dt * 1.5f);
            Assert.AreEqual(hp - _cfg.projectileDamage, _sim.Player.Hp, "the projectile hurts");
            Assert.AreEqual(0, _sim.Projectiles.Count, "the projectile is spent on the hit");
        }

        [Test]
        public void Shooter_OutOfRange_HoldsFire()
        {
            _sim.SpawnEnemy(EnemyKind.Shooter, new Vec2(20f + _cfg.shooterRange + 3f, 1f));

            bool windup = false;
            for (int i = 0; i < 240; i++)
            {
                _sim.Step(Hold(0f), Dt);
                windup |= Saw(_sim, SimEventType.ShooterWindup);
            }

            Assert.IsFalse(windup);
            Assert.AreEqual(0, _sim.Projectiles.Count);
        }

        [Test]
        public void StrikingAProjectile_ReflectsIt_AndTheReflectionKillsTheShooter()
        {
            var shooter = _sim.SpawnEnemy(EnemyKind.Shooter, new Vec2(30f, 1f));
            bool reflected = false;
            int hp = _sim.Player.Hp;
            for (int i = 0; i < 400 && shooter.Alive; i++)
            {
                bool swing = _sim.Projectiles.Count > 0 && !_sim.Projectiles[0].Reflected
                    && _sim.Projectiles[0].X - _sim.Player.MaxX < _cfg.attackRange * 0.7f;
                _sim.Step(new InputFrame { AttackPressed = swing }, Dt);
                reflected |= Saw(_sim, SimEventType.ProjectileReflect);
            }

            Assert.IsTrue(reflected, "the strike reflected the shot");
            Assert.IsFalse(shooter.Alive, "the reflected shot killed the shooter");
            Assert.AreEqual(hp, _sim.Player.Hp, "a reflected shot never hurts the player");
            Assert.AreEqual(1, _sim.Kills);
        }

        // ------------------------------------------------------------------ shield

        private Enemy ShieldAt(float x, int shieldDir)
        {
            var e = _sim.SpawnEnemy(EnemyKind.Shield, new Vec2(x, 1f));
            e.Facing = shieldDir;
            e.ShieldDir = shieldDir;
            e.StunTimer = 5f; // hold it still; a stunned bearer still holds its shield up
            _sim.Player.InvulnTimer = 99f;
            return e;
        }

        private void StrikeOnce()
        {
            _sim.Step(new InputFrame { AttackPressed = true }, Dt);
        }

        [Test]
        public void Shield_StrikeFromTheFront_Clangs_NoDamage()
        {
            var e = ShieldAt(21.6f, -1); // shield faces the player on its left

            _sim.Step(new InputFrame { AttackPressed = true }, Dt);

            Assert.AreEqual(e.MaxHp, e.Hp);
            Assert.IsTrue(Saw(_sim, SimEventType.ShieldBlock));
            Assert.IsTrue(Saw(_sim, SimEventType.Clang));
            Assert.Less(_sim.Player.Vx, 0f, "bounced back");
        }

        [Test]
        public void Shield_StrikeFromBehind_Damages()
        {
            var e = ShieldAt(21.6f, 1); // shield faces away from the player

            StrikeOnce();

            Assert.Less(e.Hp, e.MaxHp);
            Assert.IsFalse(Saw(_sim, SimEventType.ShieldBlock));
        }

        [Test]
        public void Shield_StrikeFromAbove_WhileFalling_Damages()
        {
            var e = ShieldAt(21.4f, -1);
            Teleport(_sim, 20.6f, e.Y + e.Height * (_cfg.shieldAboveFraction + 0.1f));
            _sim.Player.Vy = -2f;

            StrikeOnce();

            Assert.Less(e.Hp, e.MaxHp);
            Assert.IsFalse(Saw(_sim, SimEventType.ShieldBlock));
        }

        [Test]
        public void Shield_TurnsToThePlayer_OnlyAfterTheTurnDelay()
        {
            var e = _sim.SpawnEnemy(EnemyKind.Shield, new Vec2(26f, 1f));
            e.Facing = e.ShieldDir = 1; // back to the player
            _sim.Player.InvulnTimer = 99f;

            Run(_sim, Hold(0f), _cfg.shieldTurnTime * 0.5f);
            Assert.AreEqual(1, e.ShieldDir, "still open from behind just after turning");

            Run(_sim, Hold(0f), _cfg.shieldTurnTime);
            Assert.AreEqual(-1, e.ShieldDir, "shield now faces the player");
        }

        // ------------------------------------------------------------------ kamikaze

        [Test]
        public void Kamikaze_RushesFusesThenExplodes_HurtingThePlayerAndEnemiesInRadius()
        {
            var bomb = _sim.SpawnEnemy(EnemyKind.Kamikaze, new Vec2(28f, 1f));
            var near = _sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(21.5f, 1f));
            var far = _sim.SpawnEnemy(EnemyKind.Grunt, new Vec2(40f, 1f));
            near.StunTimer = far.StunTimer = 99f; // dazed: they neither move nor touch the player
            int hp = _sim.Player.Hp;
            float fuseAt = -1f, boomAt = -1f, t = 0f;
            for (int i = 0; i < 400 && boomAt < 0f; i++)
            {
                _sim.Step(Hold(0f), Dt);
                t += Dt;
                if (fuseAt < 0f && Saw(_sim, SimEventType.KamikazeFuse)) fuseAt = t;
                if (Saw(_sim, SimEventType.Explode)) boomAt = t;
            }

            Assert.Greater(fuseAt, 0f, "telegraphs with a fuse");
            Assert.GreaterOrEqual(boomAt - fuseAt, _cfg.kamikazeFuseTime - Dt * 1.5f);
            Assert.IsFalse(bomb.Alive);
            Assert.AreEqual(hp - _cfg.kamikazeBlastDamage, _sim.Player.Hp);
            Assert.IsFalse(near.Alive, "enemy in the blast radius dies");
            Assert.IsTrue(far.Alive, "enemy outside the blast radius is untouched");
        }

        [Test]
        public void Kamikaze_KilledBeforeTheBlast_DoesNotExplode()
        {
            var bomb = _sim.SpawnEnemy(EnemyKind.Kamikaze, new Vec2(28f, 1f));
            _sim.Player.Vx = _cfg.runSpeed; // a tier 2 strike: a tier 1 hit would only light the fuse (kamikazeHp 2)
            int hp = _sim.Player.Hp;
            bool boom = false;
            for (int i = 0; i < 240; i++)
            {
                bool swing = bomb.Alive && bomb.X - _sim.Player.X < 2f;
                _sim.Step(new InputFrame { MoveX = 1f, AttackPressed = swing }, Dt);
                boom |= Saw(_sim, SimEventType.Explode);
            }

            Assert.IsFalse(bomb.Alive);
            Assert.IsFalse(boom, "a kill defuses it");
            Assert.AreEqual(hp, _sim.Player.Hp);
            Assert.AreEqual(1, _sim.Kills);
        }

        // ------------------------------------------------------------------ summoner

        [Test]
        public void Summoner_FleesFromThePlayer_SlowerThanTheirRun()
        {
            var s = _sim.SpawnEnemy(EnemyKind.Summoner, new Vec2(24f, 1f));
            s.ModeTimer = 99f; // no casting in this test

            Run(_sim, Hold(0f), 1f);

            Assert.Greater(s.X, 26f, "ran away from the player");
            Assert.Less(s.Speed, _cfg.runSpeed);
        }

        [Test]
        public void SummonerFasterThanThePlayersRun_IsAnInvalidConfig()
        {
            var cfg = OneHitHp(new RedlineConfig());
            cfg.summonerSpeed = cfg.runSpeed;

            Assert.IsNotEmpty(cfg.Validate());
        }

        [Test]
        public void Summoner_CastsThenSummonsDrones_UpToTheCap_AndStopsWhenDead()
        {
            _cfg.summonerCastInterval = 0.5f;
            _cfg.summonerMaxDrones = 2;
            var s = _sim.SpawnEnemy(EnemyKind.Summoner, new Vec2(30f, 1f));
            _sim.Player.InvulnTimer = 999f;
            int summons = 0;
            bool castFirst = false, cast = false;
            for (int i = 0; i < 480; i++)
            {
                _sim.Step(Hold(0f), Dt);
                cast |= Saw(_sim, SimEventType.SummonerCast);
                if (Saw(_sim, SimEventType.Summon))
                {
                    if (summons == 0) castFirst = cast;
                    summons++;
                }
            }

            Assert.IsTrue(castFirst, "telegraphs a cast before the first summon");
            Assert.AreEqual(2, summons, "summons up to the cap and no more");
            Assert.AreEqual(2, SummonedAlive(s));

            s.Hp = 0f;
            foreach (var e in _sim.Enemies) if (e.Owner == s) e.Hp = 0f;
            Run(_sim, Hold(0f), 4f);

            Assert.AreEqual(0, SummonedAlive(s), "a dead summoner summons nothing");
        }

        private int SummonedAlive(Enemy summoner)
        {
            int n = 0;
            foreach (var e in _sim.Enemies)
                if (e.Alive && e.Owner == summoner && e.Kind == EnemyKind.Drone) n++;
            return n;
        }

        // ------------------------------------------------------------------ levels

        [TestCase("Loop02.txt")]
        [TestCase("Loop03.txt")]
        public void Level_BuildsAValidSim_AndWaveOneSpawnsItsKinds(string file)
        {
            var cfg = new RedlineConfig { firstWaveDelay = 0.05f };
            RedlineSim sim = null;
            Assert.DoesNotThrow(() => sim = new RedlineSim(cfg, TileMap.Parse(ReadResource(file))));
            sim.Player.InvulnTimer = 999f;

            Run(sim, new InputFrame(), 0.1f);

            Assert.AreEqual(SimState.Fighting, sim.State);
            var wave = sim.Waves[0];
            for (int w = 0; w < WaveSpec.KindCount; w++)
            {
                Assert.IsTrue(RedlineSim.TryGetEnemyKind((WaveEnemy)w, out var kind));
                int n = 0;
                foreach (var e in sim.Enemies) if (e.Kind == kind) n++;
                Assert.AreEqual(wave[(WaveEnemy)w], n, $"{file}: {WaveSpec.KeyOf((WaveEnemy)w)}");
            }
        }
    }
}
