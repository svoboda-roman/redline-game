using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>
    /// Contextual tutorial hints: ';hint' level zones, first-sight enemy cards, first-pickup orb
    /// cards, the once-only policy, the one-at-a-time queue and Hints.json.
    /// </summary>
    public class TutorialHintTests
    {
        private const int RingWidth = 40;
        private const float MinTime = 1f;
        private const float CardTime = 3f;
        private const float ZoneMax = 5f;

        /// <summary>A 40-wide ring: open floor, player at x2, one post; extra directives go first.</summary>
        private static TileMap Ring(params string[] directives)
        {
            string open = new string('.', RingWidth) + "\n";
            string start = "..P" + new string('.', 11) + "S" + new string('.', RingWidth - 15) + "\n";
            string floor = new string('#', RingWidth) + "\n";
            return TileMap.Parse(";wrap\n" + string.Join("\n", directives) + "\n" + open + open + open + open + start + floor);
        }

        /// <summary>Catalog with short test timings: one zone hint per scope plus every card.</summary>
        private static HintCatalog Catalog(int queueCapacity = 2, bool lineOfSight = true)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{ \"settings\": { \"minDisplayTime\": ").Append(MinTime).Append(", \"cardTime\": ").Append(CardTime)
              .Append(", \"zoneMaxTime\": ").Append(ZoneMax).Append(", \"sightRangeX\": 10, \"sightRangeY\": 6, \"queueCapacity\": ")
              .Append(queueCapacity).Append(", \"requireLineOfSight\": ").Append(lineOfSight ? "true" : "false").Append(" },\n \"hints\": [\n");
            sb.Append("{ \"id\": \"edge\", \"title\": \"EDGE\", \"body\": \"Hold {HOOK}\" },\n");
            sb.Append("{ \"id\": \"start\", \"scope\": \"run\", \"title\": \"START\", \"body\": \"Run\" }");
            foreach (var id in HintIds.AllCards()) sb.Append(",\n{ \"id\": \"").Append(id).Append("\", \"title\": \"T\", \"body\": \"B\" }");
            sb.Append("\n] }");
            return HintCatalog.Parse(sb.ToString());
        }

        private static RedlineSim Sim(TileMap map) => new RedlineSim(NoWaves(), map);

        /// <summary>Steps the sim and then the hints, like the game loop does.</summary>
        private static void Play(RedlineSim sim, TutorialHints hints, InputFrame input, float seconds, bool enabled = true)
        {
            int steps = (int)Math.Round(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                sim.Step(input, Dt);
                hints.Step(sim, Dt, enabled);
            }
        }

        private static Enemy Frozen(RedlineSim sim, EnemyKind kind, float x)
        {
            var e = sim.SpawnEnemy(kind, new Vec2(x, 1f));
            e.StunTimer = 1e6f;
            return e;
        }

        // ------------------------------------------------------------------ parsing

        [Test]
        public void HintLines_AreParsedInOrder()
        {
            var map = Ring(";hint 0 11 l1.runway", ";hint 36 3 seam-zone");

            Assert.AreEqual(2, map.Hints.Count);
            Assert.AreEqual(0, map.Hints[0].X0);
            Assert.AreEqual(11, map.Hints[0].X1);
            Assert.AreEqual("l1.runway", map.Hints[0].Id);
            Assert.AreEqual("seam-zone", map.Hints[1].Id);
            Assert.IsTrue(map.Hints[1].Contains(38.5f));
            Assert.IsTrue(map.Hints[1].Contains(1.2f));
            Assert.IsFalse(map.Hints[1].Contains(20f));
        }

        [Test]
        public void NoHintLines_GiveAnEmptyList_AndLookalikeCommentsAreIgnored()
        {
            var map = Ring(";hints are explained in Hints.json");
            Assert.AreEqual(0, map.Hints.Count);
        }

        [TestCase(";hint")]
        [TestCase(";hint 1 2")]
        [TestCase(";hint a 2 id")]
        [TestCase(";hint -1 2 id")]
        [TestCase(";hint 1 40 id")]
        [TestCase(";hint 1 2 Bad.Id")]
        [TestCase(";hint 1 2 id extra")]
        public void MalformedHint_IsAParseError_WithTheLineNumber(string line)
        {
            var ex = Assert.Throws<FormatException>(() => Ring(";wave grunt=1", line));
            StringAssert.Contains("line 3", ex.Message);
        }

        [Test]
        public void SeamZone_OnANonRingLevel_AndDuplicateIds_AreParseErrors()
        {
            const string room = "#####\n#P.S#\n#####\n";
            Assert.Throws<FormatException>(() => TileMap.Parse(";hint 3 1 a\n" + room));
            var ex = Assert.Throws<FormatException>(() => Ring(";hint 1 2 a", ";hint 5 6 a"));
            StringAssert.Contains("twice", ex.Message);
        }

        [Test]
        public void MalformedCatalog_IsAFormatError()
        {
            Assert.Throws<FormatException>(() => HintCatalog.Parse("{ \"hints\": [ { \"id\": \"a\", \"title\": \"T\" } ] }"));
            Assert.Throws<FormatException>(() => HintCatalog.Parse("{ \"hints\": [ { \"id\": \"a\", \"title\": \"T\", \"body\": \"B\", \"scope\": \"lap\" } ] }"));
            Assert.Throws<FormatException>(() => HintCatalog.Parse("{ \"settings\": { \"speed\": 1 }, \"hints\": [] }"));
            Assert.Throws<FormatException>(() => HintCatalog.Parse("{ \"hints\": [ "));
        }

        // ------------------------------------------------------------------ zones and once-only

        [Test]
        public void ZoneHint_Fires_WhenThePlayerWalksAcrossTheSeamIntoIt()
        {
            var sim = Sim(Ring(";hint 34 38 edge"));
            var hints = new TutorialHints(Catalog());
            Teleport(sim, 1.5f, 1f);

            Play(sim, hints, Hold(0f), 0.2f);
            Assert.IsNull(hints.Active, "not in the zone yet");

            for (int i = 0; i < 600 && hints.Active == null; i++) Play(sim, hints, Hold(-1f), Dt);

            Assert.IsNotNull(hints.Active, "walking left across x=0 reaches the zone at x34-38");
            Assert.AreEqual("edge", hints.Active.Id);
            Assert.IsTrue(hints.ActiveIsZone);
            float x = sim.Map.WrapX(sim.Player.X);
            Assert.That(x, Is.InRange(34f, 39f));
        }

        [Test]
        public void ZoneHint_StaysForTheMinimumTime_ThenClosesOutsideTheZone_AndNeverReturns()
        {
            var sim = Sim(Ring(";hint 0 5 edge"));
            var hints = new TutorialHints(Catalog());
            Play(sim, hints, Hold(0f), Dt);
            Assert.AreEqual("edge", hints.Active?.Id);

            Teleport(sim, 20.5f, 1f);
            Play(sim, hints, Hold(0f), MinTime * 0.5f);
            Assert.AreEqual("edge", hints.Active?.Id, "minimum display time holds it after leaving");
            Play(sim, hints, Hold(0f), MinTime);
            Assert.IsNull(hints.Active);

            Teleport(sim, 2.5f, 1f);
            Play(sim, hints, Hold(0f), 0.5f);
            Assert.IsNull(hints.Active, "a save-scope hint shows once");
            Assert.IsTrue(hints.Progress.Contains("edge"));
        }

        [Test]
        public void ZoneHint_ClosesAfterTheMaximumTime_WhileThePlayerStays()
        {
            var sim = Sim(Ring(";hint 0 5 edge"));
            var hints = new TutorialHints(Catalog());
            Play(sim, hints, Hold(0f), ZoneMax - 0.5f);
            Assert.AreEqual("edge", hints.Active?.Id);
            Play(sim, hints, Hold(0f), 1f);
            Assert.IsNull(hints.Active);
        }

        [Test]
        public void RunScopeHints_ShowAgainNextRun_SaveScopeHintsDoNot()
        {
            var sim = Sim(Ring(";hint 0 5 start", ";hint 10 12 edge"));
            var hints = new TutorialHints(Catalog());
            Play(sim, hints, Hold(0f), Dt);
            Assert.AreEqual("start", hints.Active?.Id);
            Teleport(sim, 11.5f, 1f);
            Play(sim, hints, Hold(0f), MinTime + Dt);
            Assert.AreEqual("edge", hints.Active?.Id);
            Assert.IsFalse(hints.Progress.Contains("start"), "run-scope ids are not saved");

            hints.BeginRun();
            Assert.IsNull(hints.Active);
            Play(sim, hints, Hold(0f), Dt);
            Assert.IsNull(hints.Active, "edge was seen in this save");
            Teleport(sim, 2.5f, 1f);
            Play(sim, hints, Hold(0f), Dt);
            Assert.AreEqual("start", hints.Active?.Id, "start shows once per run");
        }

        [Test]
        public void Disabled_ShowsNothing_AndMarksNothingSeen()
        {
            var sim = Sim(Ring(";hint 0 5 edge"));
            var hints = new TutorialHints(Catalog());
            Frozen(sim, EnemyKind.Grunt, 6.5f);
            Play(sim, hints, Hold(0f), 1f, enabled: false);

            Assert.IsNull(hints.Active);
            Assert.AreEqual(0, hints.Progress.seen.Count);
            Play(sim, hints, Hold(0f), Dt);
            Assert.IsNotNull(hints.Active, "re-enabled, the hints fire as normal");
        }

        // ------------------------------------------------------------------ first sight and orbs

        [Test]
        public void FirstSight_ShowsOneCardPerKind_InRangeOnly()
        {
            var sim = Sim(Ring());
            var hints = new TutorialHints(Catalog());
            Frozen(sim, EnemyKind.Brute, 2.5f + 15f);
            Play(sim, hints, Hold(0f), 0.5f);
            Assert.IsNull(hints.Active, "a brute 15 tiles away is out of sight range");

            Frozen(sim, EnemyKind.Grunt, 6.5f);
            Play(sim, hints, Hold(0f), Dt);
            Assert.AreEqual(HintIds.Enemy(EnemyKind.Grunt), hints.Active?.Id);
            Play(sim, hints, Hold(0f), CardTime + 0.1f);
            Assert.IsNull(hints.Active, "a card ends after cardTime");

            Frozen(sim, EnemyKind.Grunt, 5.5f);
            Play(sim, hints, Hold(0f), 0.5f);
            Assert.IsNull(hints.Active, "a second grunt does not repeat the card");

            Teleport(sim, 12.5f, 1f);
            Play(sim, hints, Hold(0f), Dt);
            Assert.AreEqual(HintIds.Enemy(EnemyKind.Brute), hints.Active?.Id);
        }

        [Test]
        public void FirstSight_NeedsAClearLine()
        {
            const string walled =
                ";wrap\n" +
                "........#...........\n" +
                "........#...........\n" +
                "........#...........\n" +
                "..P.....#.....S.....\n" +
                "####################\n";
            var sim = Sim(TileMap.Parse(walled));
            var hints = new TutorialHints(Catalog());
            Frozen(sim, EnemyKind.Grunt, 12.5f);
            Play(sim, hints, Hold(0f), 0.5f);
            Assert.IsNull(hints.Active, "the wall at x8 (and the ceiling over the ring) hide it");
            Assert.IsFalse(hints.Progress.Contains(HintIds.Enemy(EnemyKind.Grunt)));
        }

        [Test]
        public void FirstOrbPickup_ShowsTheOrbCard_OncePerKind()
        {
            var sim = Sim(Ring());
            var hints = new TutorialHints(Catalog());
            sim.SpawnOrb(OrbKind.Speed, sim.Player.Center);
            for (int i = 0; i < 300 && hints.Active == null; i++) Play(sim, hints, Hold(0f), Dt);
            Assert.AreEqual(HintIds.Orb(OrbKind.Speed), hints.Active?.Id);

            Play(sim, hints, Hold(0f), CardTime + 0.1f);
            sim.SpawnOrb(OrbKind.Speed, sim.Player.Center);
            Play(sim, hints, Hold(0f), 2f);
            Assert.IsNull(hints.Active);
        }

        // ------------------------------------------------------------------ one at a time

        [Test]
        public void Cards_QueueBehindTheActiveOne_AndOverflowIsDroppedUnseen()
        {
            var sim = Sim(Ring());
            var hints = new TutorialHints(Catalog(queueCapacity: 2));
            Frozen(sim, EnemyKind.Grunt, 5.5f);
            Frozen(sim, EnemyKind.Brute, 7.5f);
            Frozen(sim, EnemyKind.Shooter, 8.5f);
            Frozen(sim, EnemyKind.Shield, 9.5f);
            Play(sim, hints, Hold(0f), Dt);

            Assert.AreEqual(HintIds.Enemy(EnemyKind.Grunt), hints.Active?.Id);
            Assert.AreEqual(2, hints.QueuedCount);
            Assert.IsFalse(hints.Progress.Contains(HintIds.Enemy(EnemyKind.Shield)), "dropped, so not seen");

            Play(sim, hints, Hold(0f), MinTime - 0.1f);
            Assert.AreEqual(HintIds.Enemy(EnemyKind.Grunt), hints.Active?.Id, "held for the minimum time");
            Play(sim, hints, Hold(0f), 0.2f);
            Assert.AreEqual(HintIds.Enemy(EnemyKind.Brute), hints.Active?.Id, "the next card takes over");

            var shown = new List<string>();
            int version = hints.Version;
            for (int i = 0; i < 1200; i++)
            {
                Play(sim, hints, Hold(0f), Dt);
                if (hints.Active != null && hints.Version != version) shown.Add(hints.Active.Id);
                version = hints.Version;
            }
            CollectionAssert.AreEqual(new[] { HintIds.Enemy(EnemyKind.Shooter), HintIds.Enemy(EnemyKind.Shield) }, shown,
                "the dropped shield card is asked again and shows last");
        }

        [Test]
        public void Card_TakesOverAZoneHint_AfterTheMinimumTime()
        {
            var sim = Sim(Ring(";hint 0 5 edge"));
            var hints = new TutorialHints(Catalog());
            Play(sim, hints, Hold(0f), Dt);
            Assert.AreEqual("edge", hints.Active?.Id);

            Frozen(sim, EnemyKind.Drone, 8.5f).Y = 3f;
            Play(sim, hints, Hold(0f), MinTime * 0.5f);
            Assert.AreEqual("edge", hints.Active?.Id);
            Play(sim, hints, Hold(0f), MinTime);
            Assert.AreEqual(HintIds.Enemy(EnemyKind.Drone), hints.Active?.Id);
        }

        // ------------------------------------------------------------------ text and persistence

        [Test]
        public void Tokens_AreResolved_UnknownOrFailedOnesFallBackToTheKey()
        {
            Func<string, string> labels = k => k == "HOOK" ? "Shift" : k == "SLIDE" ? throw new InvalidOperationException() : null;

            Assert.AreEqual("Hold Shift, tap JUMP, SLIDE", TutorialText.Resolve("Hold {HOOK}, tap {JUMP}, {SLIDE}", labels));
            Assert.AreEqual("{lower} { {", TutorialText.Resolve("{lower} { {", labels));
            Assert.AreEqual("Press ATTACK", TutorialText.Resolve("Press {ATTACK}", null));
            Assert.AreEqual("", TutorialText.Resolve(null, labels));
        }

        [Test]
        public void Progress_RoundTripsThroughItsString_AndSkipsJunk()
        {
            var p = new TutorialProgress();
            p.Add("enemy.brute");
            p.Add("l2.rail");
            p.Add("enemy.brute");
            p.Add("Not Valid");

            var back = TutorialProgress.Deserialize(p.Serialize());
            CollectionAssert.AreEqual(new[] { "enemy.brute", "l2.rail" }, back.seen);
            Assert.AreEqual(0, TutorialProgress.Deserialize(null).seen.Count);
            Assert.AreEqual(1, TutorialProgress.Deserialize(",, orb.hook ,X Y").seen.Count);

            var sim = Sim(Ring(";hint 0 5 edge"));
            var hints = new TutorialHints(Catalog(), TutorialProgress.Deserialize("edge"));
            Play(sim, hints, Hold(0f), 0.5f);
            Assert.IsNull(hints.Active, "a loaded seen-set suppresses the hint");
        }

        [Test]
        public void HintsJson_DefinesEveryLevelHint_AndEveryCard_WithKnownTokensOnly()
        {
            var catalog = HintCatalog.Parse(ReadResource("Hints.json"));
            foreach (var id in HintIds.AllCards())
                Assert.IsTrue(catalog.TryGet(id, out _), "missing card " + id);
            int zones = 0;
            foreach (var level in new[] { "Loop01.txt", "Loop02.txt", "Loop03.txt" })
                foreach (var zone in TileMap.Parse(ReadResource(level)).Hints)
                {
                    zones++;
                    Assert.IsTrue(catalog.TryGet(zone.Id, out _), level + " uses undefined hint " + zone.Id);
                }
            Assert.GreaterOrEqual(zones, 6);

            var known = new HashSet<string> { "HOOK", "JUMP", "ATTACK", "SLIDE" };
            foreach (var id in catalog.Ids)
            {
                catalog.TryGet(id, out var e);
                foreach (Match m in Regex.Matches(e.Title + " " + e.Body, @"\{([A-Z0-9_]+)\}"))
                    Assert.IsTrue(known.Contains(m.Groups[1].Value), id + " uses unknown token " + m.Value);
            }
        }
    }
}
