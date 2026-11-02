using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Redline.Core;
using static Redline.Tests.TestSupport;

namespace Redline.Tests
{
    /// <summary>Story 005 — arena data, and the shipped config file (integration: reads Assets/Resources).</summary>
    public class ArenaTests
    {
        private TileMap _arena;

        [SetUp]
        public void SetUp()
        {
            _arena = TileMap.Parse(ReadResource("Arena01.txt"));
        }

        [Test]
        public void Arena01_HasStartAndSpawns()
        {
            Assert.GreaterOrEqual(_arena.SpawnPoints.Count, 2);
            Assert.AreEqual(Tile.Empty, _arena.Get((int)_arena.PlayerStart.X, (int)_arena.PlayerStart.Y));
        }

        [Test]
        public void Arena01_IsEnclosed()
        {
            for (int x = 0; x < _arena.Width; x++)
            {
                Assert.IsTrue(_arena.IsSolid(x, 0), $"floor at x={x}");
                Assert.IsTrue(_arena.IsSolid(x, _arena.Height - 1), $"ceiling at x={x}");
            }
            for (int y = 0; y < _arena.Height; y++)
            {
                Assert.IsTrue(_arena.IsSolid(0, y), $"left wall at y={y}");
                Assert.IsTrue(_arena.IsSolid(_arena.Width - 1, y), $"right wall at y={y}");
            }
        }

        [Test]
        public void Arena01_HookFromStartReachesTerrain()
        {
            var sim = new RedlineSim(NoWaves(), _arena);
            sim.Step(new InputFrame { HookPressed = true, HookHeld = true }, Dt);

            Assert.IsTrue(sim.Player.Hooked);
            Assert.Greater(sim.Player.HookAnchor.Y, sim.Player.MaxY, "anchored to terrain overhead");
        }

        [Test]
        public void Arena01_SpikesHurtAndBounce()
        {
            var cfg = NoWaves();
            var sim = new RedlineSim(cfg, _arena);
            int spikeX = -1, spikeY = -1;
            for (int y = 0; y < _arena.Height && spikeX < 0; y++)
                for (int x = 0; x < _arena.Width && spikeX < 0; x++)
                    if (_arena.Get(x, y) == Tile.Spike)
                    {
                        spikeX = x;
                        spikeY = y;
                    }
            Assert.GreaterOrEqual(spikeX, 0, "arena has spikes");

            Teleport(sim, spikeX + 0.5f, spikeY);
            sim.Step(Hold(0f), Dt);

            Assert.AreEqual(cfg.playerMaxHp - cfg.spikeDamage, sim.Player.Hp);
            Assert.Greater(sim.Player.Vy, 0f);
        }

        [Test]
        public void Arena01_SpawnPointsAreNotInsideTerrain()
        {
            var cfg = new RedlineConfig();
            foreach (var pt in _arena.SpawnPoints)
            {
                Assert.IsFalse(_arena.OverlapsSolid(pt.X - cfg.bruteWidth / 2, pt.Y, pt.X + cfg.bruteWidth / 2, pt.Y + cfg.bruteHeight),
                    $"spawn {pt} fits a brute");
            }
        }

        [Test]
        public void ConfigJson_MatchesConfigFieldsAndDefaults()
        {
            string json = ReadResource("RedlineConfig.json");
            var values = new Dictionary<string, string>();
            foreach (Match m in Regex.Matches(json, "\"(\\w+)\"\\s*:\\s*(\\[[^\\]]*\\]|[-0-9.eE]+)"))
                values[m.Groups[1].Value] = m.Groups[2].Value;

            var defaults = new RedlineConfig();
            var fields = typeof(RedlineConfig).GetFields(BindingFlags.Public | BindingFlags.Instance);
            Assert.AreEqual(fields.Length, values.Count, "every config field appears once in the JSON, and nothing else");
            foreach (var f in fields)
            {
                Assert.IsTrue(values.ContainsKey(f.Name), $"JSON is missing {f.Name}");
                string raw = values[f.Name];
                object def = f.GetValue(defaults);
                if (def is float fl)
                    Assert.AreEqual(fl, float.Parse(raw, CultureInfo.InvariantCulture), 1e-5f, f.Name);
                else if (def is int i)
                    Assert.AreEqual(i, int.Parse(raw, CultureInfo.InvariantCulture), f.Name);
                else if (def is float[] fa)
                    CollectionAssert.AreEqual(fa, ParseArray(raw, s => float.Parse(s, CultureInfo.InvariantCulture)), f.Name);
                else if (def is int[] ia)
                    CollectionAssert.AreEqual(ia, ParseArray(raw, s => int.Parse(s, CultureInfo.InvariantCulture)), f.Name);
                else
                    Assert.Fail($"unhandled config field type {f.FieldType} for {f.Name}");
            }
            Assert.IsEmpty(defaults.Validate());
        }

        private static List<T> ParseArray<T>(string raw, System.Func<string, T> parse)
        {
            var list = new List<T>();
            foreach (var part in raw.Trim('[', ']').Split(','))
                if (part.Trim().Length > 0) list.Add(parse(part.Trim()));
            return list;
        }
    }
}
