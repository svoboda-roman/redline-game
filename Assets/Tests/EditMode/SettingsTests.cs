using NUnit.Framework;
using Redline.Core;

namespace Redline.Tests
{
    /// <summary>Settings: keyboard binding table (conflicts, defaults), key labels, and the settings file format.</summary>
    public class SettingsTests
    {
        [Test]
        public void Defaults_MatchTheShippedKeyboardLayout()
        {
            var b = ControlBindings.CreateDefaults();
            Assert.That(b.Get(GameAction.MoveLeft, 0), Is.EqualTo("A"));
            Assert.That(b.Get(GameAction.MoveLeft, 1), Is.EqualTo("LeftArrow"));
            Assert.That(b.Get(GameAction.Jump, 2), Is.EqualTo("UpArrow"));
            Assert.That(b.Get(GameAction.Slide, 2), Is.EqualTo("LeftShift"));
            Assert.That(b.Primary(GameAction.Hook), Is.EqualTo("K"));
            Assert.That(b.Get(GameAction.Restart, 1), Is.EqualTo("Enter"));
            Assert.That(b.Primary(GameAction.Pause), Is.EqualTo("Escape"));
            Assert.That(b.IsValid(), Is.True);
        }

        [Test]
        public void TutorialSeen_RoundTrips_AndIsOptionalInOlderFiles()
        {
            // Arrange
            var s = new SettingsData { TutorialSeen = "enemy.grunt,orb.health" };

            // Act
            var loaded = SettingsSave.Deserialize(SettingsSave.Serialize(s), null, out var status);
            var old = SettingsSave.Deserialize(
                SettingsSave.Serialize(new SettingsData()).Replace(",\"tutorialSeen\":\"\"", ""), null, out var oldStatus);

            // Assert
            Assert.That(loaded.TutorialSeen, Is.EqualTo("enemy.grunt,orb.health"));
            Assert.That(status, Is.EqualTo(SaveLoadStatus.Ok));
            Assert.That(old.TutorialSeen, Is.EqualTo(""));
            Assert.That(oldStatus, Is.EqualTo(SaveLoadStatus.Ok), "a file without the field is not a repair");
        }

        [Test]
        public void TryBind_FreeKey_BindsIt()
        {
            var b = ControlBindings.CreateDefaults();
            Assert.That(b.TryBind(GameAction.Hook, 2, "L", out _), Is.EqualTo(BindResult.Ok));
            Assert.That(b.Get(GameAction.Hook, 2), Is.EqualTo("L"));
            Assert.That(b.IsValid(), Is.True);
        }

        [Test]
        public void TryBind_KeyOfAnotherAction_SwapsKeys()
        {
            var b = ControlBindings.CreateDefaults();
            var result = b.TryBind(GameAction.Hook, 0, "J", out var other);
            Assert.That(result, Is.EqualTo(BindResult.Swapped));
            Assert.That(other, Is.EqualTo(GameAction.Attack));
            Assert.That(b.Get(GameAction.Hook, 0), Is.EqualTo("J"));
            Assert.That(b.Get(GameAction.Attack, 0), Is.EqualTo("K"));
            Assert.That(b.IsValid(), Is.True);
        }

        [Test]
        public void TryBind_IntoEmptySlot_MovesKeyWhenTheOtherActionKeepsOne()
        {
            var b = ControlBindings.CreateDefaults();
            Assert.That(b.TryBind(GameAction.Hook, 2, "Z", out var other), Is.EqualTo(BindResult.Moved));
            Assert.That(other, Is.EqualTo(GameAction.Attack));
            Assert.That(b.Count(GameAction.Attack), Is.EqualTo(1));
            Assert.That(b.IsValid(), Is.True);
        }

        [Test]
        public void TryBind_TakingAnActionsLastKey_IsRefused()
        {
            var b = ControlBindings.CreateDefaults();
            Assert.That(b.TryBind(GameAction.Hook, 2, "Escape", out var other), Is.EqualTo(BindResult.RefusedLastKey));
            Assert.That(other, Is.EqualTo(GameAction.Pause));
            Assert.That(b.Primary(GameAction.Pause), Is.EqualTo("Escape"));
            Assert.That(b.Get(GameAction.Hook, 2), Is.Null);
        }

        [Test]
        public void TryClear_LastKey_IsRefused_OtherwiseClears()
        {
            var b = ControlBindings.CreateDefaults();
            Assert.That(b.TryClear(GameAction.Pause, 0), Is.EqualTo(BindResult.RefusedLastKey));
            Assert.That(b.TryClear(GameAction.Jump, 1), Is.EqualTo(BindResult.Ok));
            Assert.That(b.Count(GameAction.Jump), Is.EqualTo(2));
        }

        [Test]
        public void ResetToDefaults_RestoresTheLayout_AndBumpsVersion()
        {
            var b = ControlBindings.CreateDefaults();
            b.TryBind(GameAction.Hook, 0, "J", out _);
            int v = b.Version;
            b.ResetToDefaults();
            Assert.That(b.Primary(GameAction.Hook), Is.EqualTo("K"));
            Assert.That(b.Version, Is.GreaterThan(v));
        }

        [TestCase("Space", "SPACE")]
        [TestCase("LeftArrow", "LEFT")]
        [TestCase("K", "K")]
        [TestCase("Digit1", "1")]
        [TestCase("LeftShift", "L-SHIFT")]
        [TestCase("Escape", "ESC")]
        [TestCase(null, "—")]
        public void KeyLabel_IsShortAndUpperCase(string key, string expected)
        {
            Assert.That(KeyLabels.Label(key), Is.EqualTo(expected));
        }

        [Test]
        public void Save_RoundTrips()
        {
            var s = new SettingsData { MasterVolume = 70, MusicVolume = 0, SfxVolume = 35, Fullscreen = false, HintsEnabled = false };
            s.Bindings.TryBind(GameAction.Hook, 0, "L", out _);
            var loaded = SettingsSave.Deserialize(SettingsSave.Serialize(s), null, out var status);
            Assert.That(status, Is.EqualTo(SaveLoadStatus.Ok));
            Assert.That(loaded.MasterVolume, Is.EqualTo(70));
            Assert.That(loaded.MusicVolume, Is.EqualTo(0));
            Assert.That(loaded.SfxVolume, Is.EqualTo(35));
            Assert.That(loaded.Fullscreen, Is.False);
            Assert.That(loaded.HintsEnabled, Is.False);
            Assert.That(loaded.Bindings.Primary(GameAction.Hook), Is.EqualTo("L"));
            Assert.That(loaded.Bindings.Get(GameAction.Hook, 1), Is.EqualTo("X"));
        }

        [TestCase(null, SaveLoadStatus.Missing)]
        [TestCase("   ", SaveLoadStatus.Missing)]
        [TestCase("{not json", SaveLoadStatus.Corrupt)]
        [TestCase("[1,2]", SaveLoadStatus.Corrupt)]
        [TestCase("{\"version\":99}", SaveLoadStatus.UnsupportedVersion)]
        public void Load_BadInput_GivesDefaults(string json, SaveLoadStatus expected)
        {
            var s = SettingsSave.Deserialize(json, null, out var status);
            Assert.That(status, Is.EqualTo(expected));
            Assert.That(s.MasterVolume, Is.EqualTo(100));
            Assert.That(s.Bindings.Primary(GameAction.Pause), Is.EqualTo("Escape"));
        }

        [Test]
        public void Load_OutOfRangeVolume_IsClampedAndRepaired()
        {
            var json = SettingsSave.Serialize(new SettingsData()).Replace("\"master\":100", "\"master\":250");
            var s = SettingsSave.Deserialize(json, null, out var status);
            Assert.That(status, Is.EqualTo(SaveLoadStatus.Repaired));
            Assert.That(s.MasterVolume, Is.EqualTo(100));
        }

        [Test]
        public void Load_DuplicateOrUnknownKey_ResetsBindingsOnly()
        {
            var good = new SettingsData { MusicVolume = 40 };
            string dup = SettingsSave.Serialize(good).Replace("\"Pause\":[\"Escape\"]", "\"Pause\":[\"A\"]");
            var s = SettingsSave.Deserialize(dup, null, out var status);
            Assert.That(status, Is.EqualTo(SaveLoadStatus.Repaired));
            Assert.That(s.MusicVolume, Is.EqualTo(40));
            Assert.That(s.Bindings.Primary(GameAction.Pause), Is.EqualTo("Escape"));

            var unknown = SettingsSave.Deserialize(SettingsSave.Serialize(good), k => k != "K", out status);
            Assert.That(status, Is.EqualTo(SaveLoadStatus.Repaired));
            Assert.That(unknown.Bindings.Primary(GameAction.Hook), Is.EqualTo("K"));
        }
    }
}
