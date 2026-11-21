using System;
using System.IO;
using Redline.Core;
using UnityEngine;

namespace Redline.Game
{
    /// <summary>
    /// Loads and saves meta-progression (<see cref="UpgradeState"/>) as JSON in
    /// <c>Application.persistentDataPath/upgrades.json</c>, and loads <see cref="UpgradeConfig"/> from
    /// Resources/UpgradeConfig.json. Writes are atomic (temp file, then replace), so a crash mid-write
    /// leaves the previous save intact. A corrupt or unreadable save is copied to
    /// <c>upgrades.json.bad</c> before defaults are used, so nothing is silently lost.
    /// </summary>
    /// <example>
    /// <code>
    /// var store = new UpgradeStore(UpgradeStore.LoadConfig());
    /// store.State.AwardRun(run, store.Config);
    /// store.Save();
    /// </code>
    /// </example>
    /// <remarks>Main-thread only (Application.persistentDataPath and Resources).</remarks>
    public sealed class UpgradeStore
    {
        /// <summary>Save file name inside Application.persistentDataPath.</summary>
        public const string FileName = "upgrades.json";

        private readonly string _path;

        /// <summary>Upgrade tuning in use.</summary>
        public UpgradeConfig Config { get; }

        /// <summary>The loaded progress; mutate it, then call <see cref="Save"/>.</summary>
        public UpgradeState State { get; private set; }

        /// <summary>How the save was read at startup (for logging / UI).</summary>
        public SaveLoadStatus LoadStatus { get; private set; }

        /// <summary>Creates the store and loads the save. <paramref name="path"/> defaults to persistentDataPath/upgrades.json.</summary>
        public UpgradeStore(UpgradeConfig config, string path = null)
        {
            Config = config ?? new UpgradeConfig();
            _path = path ?? Path.Combine(Application.persistentDataPath, FileName);
            Load();
        }

        /// <summary>(Re)reads the save file. Never throws; falls back to a fresh state.</summary>
        public void Load()
        {
            string text = null;
            try
            {
                if (File.Exists(_path)) text = File.ReadAllText(_path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Debug.LogError("Redline: cannot read " + _path + ", starting fresh progress: " + ex.Message);
            }

            State = UpgradeSave.Deserialize(text, Config, out var status);
            LoadStatus = status;
            switch (status)
            {
                case SaveLoadStatus.Corrupt:
                case SaveLoadStatus.UnsupportedVersion:
                    Debug.LogWarning("Redline: upgrade save is " + status + "; using defaults (old file kept as " + FileName + ".bad).");
                    BackUpBadFile();
                    break;
                case SaveLoadStatus.Repaired:
                    Debug.LogWarning("Redline: upgrade save had invalid fields; they were reset.");
                    break;
            }
        }

        /// <summary>Writes the current state atomically. Returns false (and logs) when the write fails.</summary>
        public bool Save()
        {
            string tmp = _path + ".tmp";
            try
            {
                string dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(tmp, UpgradeSave.Serialize(State));
                if (File.Exists(_path))
                {
                    try
                    {
                        File.Replace(tmp, _path, null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        // Some platforms lack File.Replace: fall back to delete + move.
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
                Debug.LogError("Redline: cannot save upgrades to " + _path + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>Wipes all progress (points and levels) and saves.</summary>
        public bool ResetProgress()
        {
            State.Reset();
            return Save();
        }

        /// <summary>
        /// Loads Resources/UpgradeConfig.json over the built-in defaults; a missing, malformed or
        /// invalid file gives the defaults (logged). Pass the game config to also check that
        /// maxed-out upgrades keep it valid.
        /// </summary>
        public static UpgradeConfig LoadConfig(RedlineConfig gameConfig = null)
        {
            var config = new UpgradeConfig();
            var asset = Resources.Load<TextAsset>("UpgradeConfig");
            if (asset == null)
            {
                Debug.LogWarning("Redline: Resources/UpgradeConfig.json not found, using built-in upgrade defaults.");
                return config;
            }
            try
            {
                JsonUtility.FromJsonOverwrite(asset.text, config);
            }
            catch (ArgumentException ex)
            {
                Debug.LogError("Redline: UpgradeConfig.json is not valid JSON, using built-in defaults: " + ex.Message);
                return new UpgradeConfig();
            }
            var errors = gameConfig != null ? config.ValidateAgainst(gameConfig) : config.Validate();
            if (errors.Count == 0) return config;
            Debug.LogError("Redline: UpgradeConfig.json is invalid, using built-in defaults: " + string.Join("; ", errors));
            return new UpgradeConfig();
        }

        private void BackUpBadFile()
        {
            try
            {
                if (File.Exists(_path)) File.Copy(_path, _path + ".bad", true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Debug.LogWarning("Redline: could not back up the bad upgrade save: " + ex.Message);
            }
        }
    }
}
