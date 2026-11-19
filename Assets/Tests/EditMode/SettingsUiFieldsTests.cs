using NUnit.Framework;
using Redline.Core;

namespace Redline.Tests
{
    /// <summary>Round-trip and back-compat of the optional UI settings (Reduced Motion, HUD Text Scale).</summary>
    public class SettingsUiFieldsTests
    {
        [Test]
        public void Serialize_ThenDeserialize_KeepsReducedMotionAndTextScale()
        {
            var s = new SettingsData { ReducedMotion = true, HudTextScale = 150 };
            var loaded = SettingsSave.Deserialize(SettingsSave.Serialize(s), null, out var status);
            Assert.AreEqual(SaveLoadStatus.Ok, status);
            Assert.IsTrue(loaded.ReducedMotion);
            Assert.AreEqual(150, loaded.HudTextScale);
        }

        [Test]
        public void Deserialize_OldFileWithoutUiFields_UsesDefaultsWithoutRepair()
        {
            var json = SettingsSave.Serialize(new SettingsData())
                .Replace(",\"reducedMotion\":false", "")
                .Replace(",\"hudTextScale\":100", "");
            Assert.IsFalse(json.Contains("reducedMotion"));
            var loaded = SettingsSave.Deserialize(json, null, out var status);
            Assert.AreEqual(SaveLoadStatus.Ok, status);
            Assert.IsFalse(loaded.ReducedMotion);
            Assert.AreEqual(100, loaded.HudTextScale);
        }

        [Test]
        public void Deserialize_UnsupportedTextScale_RepairsTo100()
        {
            var json = SettingsSave.Serialize(new SettingsData()).Replace("\"hudTextScale\":100", "\"hudTextScale\":175");
            var loaded = SettingsSave.Deserialize(json, null, out var status);
            Assert.AreEqual(SaveLoadStatus.Repaired, status);
            Assert.AreEqual(100, loaded.HudTextScale);
        }

        [Test]
        public void CopyFrom_CopiesUiFields()
        {
            var a = new SettingsData();
            a.CopyFrom(new SettingsData { ReducedMotion = true, HudTextScale = 125 });
            Assert.IsTrue(a.ReducedMotion);
            Assert.AreEqual(125, a.HudTextScale);
        }
    }
}
