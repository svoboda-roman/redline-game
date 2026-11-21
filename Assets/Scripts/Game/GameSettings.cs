using System;
using System.IO;
using Redline.Core;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Redline.Game
{
    /// <summary>
    /// The player's settings for this session (<see cref="SettingsData"/>), loaded from and saved to
    /// <c>Application.persistentDataPath/settings.json</c> (format: <see cref="SettingsSave"/>).
    /// Writes are atomic (temp file, then replace); a corrupt file is copied to
    /// <c>settings.json.bad</c> and defaults are used. A static access point on purpose: input,
    /// audio, the settings screen and the tutorial hints all read the same values.
    /// </summary>
    /// <example>
    /// <code>
    /// GameSettings.Load();                       // once at startup
    /// if (GameSettings.HintsEnabled) ShowHint();
    /// GameSettings.Current.SfxVolume = 50;
    /// GameSettings.NotifyChanged();              // applies and raises Changed
    /// GameSettings.Save();
    /// </code>
    /// </example>
    /// <remarks>Main-thread only.</remarks>
    public static class GameSettings
    {
        /// <summary>Settings file name inside Application.persistentDataPath.</summary>
        public const string FileName = "settings.json";

        private static string _path;

        /// <summary>The live settings. Mutate, then call <see cref="NotifyChanged"/> (and <see cref="Save"/>).</summary>
        public static SettingsData Current { get; private set; } = new SettingsData();

        /// <summary>Whether tutorial hints should be shown.</summary>
        public static bool HintsEnabled => Current.HintsEnabled;

        /// <summary>Graphics setting: true = Cinematic 3D (default), false = Classic 2D.</summary>
        public static bool Cinematic3D => !Current.Classic2D;

        /// <summary>How the file was read by the last <see cref="Load"/>.</summary>
        public static SaveLoadStatus LoadStatus { get; private set; } = SaveLoadStatus.Missing;

        /// <summary>Raised after <see cref="NotifyChanged"/> or <see cref="Load"/> (volumes, toggles or bindings changed).</summary>
        public static event Action Changed;

        /// <summary>(Re)reads the settings file. Never throws; falls back to defaults.</summary>
        /// <param name="path">File path; defaults to persistentDataPath/settings.json.</param>
        public static void Load(string path = null)
        {
            _path = path ?? Path.Combine(Application.persistentDataPath, FileName);
            string text = null;
            try
            {
                if (File.Exists(_path)) text = File.ReadAllText(_path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Debug.LogError("Redline: cannot read " + _path + ", using default settings: " + ex.Message);
            }
            var loaded = SettingsSave.Deserialize(text, IsKnownKey, out var status);
            LoadStatus = status;
            switch (status)
            {
                case SaveLoadStatus.Corrupt:
                case SaveLoadStatus.UnsupportedVersion:
                    Debug.LogWarning("Redline: settings file is " + status + "; using defaults (old file kept as " + FileName + ".bad).");
                    BackUpBadFile();
                    break;
                case SaveLoadStatus.Repaired:
                    Debug.LogWarning("Redline: settings file had invalid fields; they were reset.");
                    break;
            }
            Current.CopyFrom(loaded); // keep the same instance: input holds its Bindings
            Changed?.Invoke();
        }

        /// <summary>Writes the settings atomically. Returns false (and logs) when the write fails.</summary>
        public static bool Save()
        {
            if (_path == null) _path = Path.Combine(Application.persistentDataPath, FileName);
            string tmp = _path + ".tmp";
            try
            {
                string dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(tmp, SettingsSave.Serialize(Current));
                if (File.Exists(_path))
                {
                    try
                    {
                        File.Replace(tmp, _path, null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Delete(_path);
                        File.Move(tmp, _path);
                    }
                }
                else
                {
                    File.Move(tmp, _path);
                }
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Debug.LogError("Redline: cannot save settings to " + _path + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>Raises <see cref="Changed"/> after <see cref="Current"/> was edited.</summary>
        public static void NotifyChanged() => Changed?.Invoke();

        /// <summary>True for an Input System key name the keyboard has ("A", "Space", "LeftArrow").</summary>
        public static bool IsKnownKey(string name)
        {
            if (string.IsNullOrEmpty(name) || !char.IsLetter(name[0])) return false;
#if ENABLE_INPUT_SYSTEM
            return Enum.TryParse(name, false, out Key key) && key != Key.None && Enum.IsDefined(typeof(Key), key);
#else
            return true;
#endif
        }

        private static void BackUpBadFile()
        {
            try
            {
                if (File.Exists(_path)) File.Copy(_path, _path + ".bad", true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Debug.LogWarning("Redline: could not back up the bad settings file: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Display labels of the current keyboard bindings, for UI and tutorial hint tokens
    /// (e.g. "{HOOK}" -> <c>ControlLabels.Get(GameAction.Hook)</c> -> "K"). Labels are cached and
    /// rebuilt only when the bindings change, so calling every frame does not allocate.
    /// </summary>
    /// <example>
    /// <code>
    /// string hook = ControlLabels.Get(GameAction.Hook);      // "K"
    /// string jump = ControlLabels.GetAll(GameAction.Jump);   // "SPACE / W / UP"
    /// string pad = ControlLabels.Gamepad(GameAction.Pause);  // "START / OPTIONS"
    /// </code>
    /// </example>
    /// <remarks>Main-thread only.</remarks>
    public static class ControlLabels
    {
        private static readonly string[] Primary = new string[ControlBindings.Actions.Length];
        private static readonly string[] All = new string[ControlBindings.Actions.Length];
        private static readonly string[] Pad = new string[ControlBindings.Actions.Length];
        private static int _version = -1;
        private static ControlBindings _cachedFor;

        /// <summary>Label of the action's first key ("K", "SPACE"); "—" if it has none.</summary>
        public static string Get(GameAction action)
        {
            Refresh();
            return Primary[(int)action];
        }

        /// <summary>Labels of all the action's keys joined with " / " ("SPACE / W / UP").</summary>
        public static string GetAll(GameAction action)
        {
            Refresh();
            return All[(int)action];
        }

        /// <summary>The fixed gamepad control of the action ("A / CROSS").</summary>
        public static string Gamepad(GameAction action)
        {
            Refresh();
            return Pad[(int)action];
        }

        private static void Refresh()
        {
            var b = GameSettings.Current.Bindings;
            if (b == _cachedFor && b.Version == _version) return;
            _cachedFor = b;
            _version = b.Version;
            var actions = ControlBindings.Actions;
            for (int i = 0; i < actions.Length; i++)
            {
                var a = actions[i];
                Primary[i] = KeyLabels.Label(b.Primary(a));
                string all = null;
                for (int s = 0; s < ControlBindings.SlotsPerAction; s++)
                {
                    string k = b.Get(a, s);
                    if (k == null) continue;
                    all = all == null ? KeyLabels.Label(k) : all + " / " + KeyLabels.Label(k);
                }
                All[i] = all ?? KeyLabels.Label(null);
                Pad[i] = KeyLabels.GamepadLabel(a);
            }
        }
    }
}
