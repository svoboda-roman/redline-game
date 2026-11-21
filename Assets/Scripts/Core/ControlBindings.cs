using System;

namespace Redline.Core
{
    /// <summary>A player action that has rebindable keyboard keys.</summary>
    public enum GameAction
    {
        /// <summary>Run left.</summary>
        MoveLeft,
        /// <summary>Run right.</summary>
        MoveRight,
        /// <summary>Jump (held for a higher jump).</summary>
        Jump,
        /// <summary>Sword attack.</summary>
        Attack,
        /// <summary>Slide.</summary>
        Slide,
        /// <summary>Grappling hook (held).</summary>
        Hook,
        /// <summary>End the run (retry, play again, or abandon) and open the upgrades.</summary>
        Restart,
        /// <summary>Open the pause menu.</summary>
        Pause
    }

    /// <summary>Outcome of <see cref="ControlBindings.TryBind"/> or <see cref="ControlBindings.TryClear"/>.</summary>
    public enum BindResult
    {
        /// <summary>The key was bound to a free slot (or the slot was cleared).</summary>
        Ok,
        /// <summary>The key was already in that slot; nothing changed.</summary>
        Unchanged,
        /// <summary>The key belonged to another action, which got this slot's previous key in exchange.</summary>
        Swapped,
        /// <summary>The key moved from another action (which still has other keys) to this slot.</summary>
        Moved,
        /// <summary>Refused: it would leave an action with no key at all.</summary>
        RefusedLastKey,
        /// <summary>Refused: no key name, or the slot index is out of range.</summary>
        Invalid
    }

    /// <summary>
    /// The keyboard binding table: up to <see cref="SlotsPerAction"/> keys per <see cref="GameAction"/>.
    /// Keys are Unity Input System <c>Key</c> enum names ("A", "Space", "LeftArrow"), kept as strings
    /// so this class stays engine-free; the game layer validates and resolves them. A key is bound to
    /// at most one action, and every action always keeps at least one key.
    /// </summary>
    /// <example>
    /// <code>
    /// var b = ControlBindings.CreateDefaults();
    /// var result = b.TryBind(GameAction.Hook, 0, "L", out var other);   // Moved / Swapped / ...
    /// string label = KeyLabels.Label(b.Primary(GameAction.Hook));       // "L"
    /// </code>
    /// </example>
    public sealed class ControlBindings
    {
        /// <summary>Key slots per action.</summary>
        public const int SlotsPerAction = 3;

        /// <summary>All actions, in display order.</summary>
        public static readonly GameAction[] Actions =
        {
            GameAction.MoveLeft, GameAction.MoveRight, GameAction.Jump, GameAction.Attack,
            GameAction.Slide, GameAction.Hook, GameAction.Restart, GameAction.Pause
        };

        // The keyboard layout the game shipped with before rebinding (RedlineInput's former keys).
        private static readonly string[][] DefaultKeys =
        {
            new[] { "A", "LeftArrow" },
            new[] { "D", "RightArrow" },
            new[] { "Space", "W", "UpArrow" },
            new[] { "J", "Z" },
            new[] { "S", "DownArrow", "LeftShift" },
            new[] { "K", "X" },
            new[] { "R", "Enter" },
            new[] { "Escape" }
        };

        private readonly string[][] _keys;

        /// <summary>Increases on every change, so readers can cache what they derive from the table.</summary>
        public int Version { get; private set; }

        /// <summary>An empty table (every slot unbound). Use <see cref="CreateDefaults"/> for a playable one.</summary>
        public ControlBindings()
        {
            _keys = new string[Actions.Length][];
            for (int i = 0; i < _keys.Length; i++) _keys[i] = new string[SlotsPerAction];
        }

        /// <summary>A table holding the default keys.</summary>
        public static ControlBindings CreateDefaults()
        {
            var b = new ControlBindings();
            b.ResetToDefaults();
            return b;
        }

        /// <summary>The default keys of an action (a copy).</summary>
        public static string[] DefaultsOf(GameAction action) => (string[])DefaultKeys[(int)action].Clone();

        /// <summary>Restores every action's default keys.</summary>
        public void ResetToDefaults()
        {
            for (int a = 0; a < _keys.Length; a++)
            {
                Array.Clear(_keys[a], 0, SlotsPerAction);
                var d = DefaultKeys[a];
                for (int s = 0; s < d.Length && s < SlotsPerAction; s++) _keys[a][s] = d[s];
            }
            Version++;
        }

        /// <summary>Copies another table into this one.</summary>
        public void CopyFrom(ControlBindings other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            for (int a = 0; a < _keys.Length; a++) Array.Copy(other._keys[a], _keys[a], SlotsPerAction);
            Version++;
        }

        /// <summary>The key in a slot, or null when the slot is empty.</summary>
        public string Get(GameAction action, int slot) =>
            slot >= 0 && slot < SlotsPerAction ? _keys[(int)action][slot] : null;

        /// <summary>The first bound key of an action, or null if it has none.</summary>
        public string Primary(GameAction action)
        {
            var keys = _keys[(int)action];
            for (int s = 0; s < SlotsPerAction; s++)
                if (keys[s] != null) return keys[s];
            return null;
        }

        /// <summary>Number of keys bound to an action.</summary>
        public int Count(GameAction action)
        {
            int n = 0;
            var keys = _keys[(int)action];
            for (int s = 0; s < SlotsPerAction; s++)
                if (keys[s] != null) n++;
            return n;
        }

        /// <summary>The action holding a key, or null when the key is free.</summary>
        public GameAction? ActionOf(string key) => Find(key, out var action, out _) ? action : (GameAction?)null;

        /// <summary>
        /// Binds <paramref name="key"/> to a slot. Conflicts swap: if another action holds the key, it
        /// receives this slot's previous key. Refused when that would leave the other action keyless.
        /// </summary>
        /// <param name="action">Action to bind.</param>
        /// <param name="slot">Slot index, 0 to <see cref="SlotsPerAction"/> - 1.</param>
        /// <param name="key">Input System key name.</param>
        /// <param name="other">The other action involved in a swap, move or refusal (else the same action).</param>
        public BindResult TryBind(GameAction action, int slot, string key, out GameAction other)
        {
            other = action;
            if (string.IsNullOrEmpty(key) || slot < 0 || slot >= SlotsPerAction) return BindResult.Invalid;
            var target = _keys[(int)action];
            string previous = target[slot];
            if (previous == key) return BindResult.Unchanged;

            if (!Find(key, out var holder, out int holderSlot))
            {
                target[slot] = key; // replacing the only key still leaves one key

                Version++;
                return BindResult.Ok;
            }

            other = holder;
            if (holder == action)
            {
                // Same action, another slot: exchange the two slots.
                target[holderSlot] = previous;
                target[slot] = key;
                Version++;
                return previous != null ? BindResult.Swapped : BindResult.Moved;
            }
            if (previous == null && Count(holder) == 1) return BindResult.RefusedLastKey;
            _keys[(int)holder][holderSlot] = previous;
            target[slot] = key;
            Version++;
            return previous != null ? BindResult.Swapped : BindResult.Moved;
        }

        /// <summary>Empties a slot. Refused for an action's last key.</summary>
        public BindResult TryClear(GameAction action, int slot)
        {
            if (slot < 0 || slot >= SlotsPerAction) return BindResult.Invalid;
            var keys = _keys[(int)action];
            if (keys[slot] == null) return BindResult.Unchanged;
            if (Count(action) == 1) return BindResult.RefusedLastKey;
            keys[slot] = null;
            Version++;
            return BindResult.Ok;
        }

        /// <summary>
        /// Sets an action's keys without conflict checks (for loading). Extra keys beyond the slots
        /// are dropped. Call <see cref="IsValid"/> afterwards.
        /// </summary>
        public void SetRaw(GameAction action, string[] keys)
        {
            var slots = _keys[(int)action];
            Array.Clear(slots, 0, SlotsPerAction);
            if (keys != null)
                for (int s = 0; s < keys.Length && s < SlotsPerAction; s++)
                    slots[s] = string.IsNullOrEmpty(keys[s]) ? null : keys[s];
            Version++;
        }

        /// <summary>True when every action has a key and no key is bound twice.</summary>
        public bool IsValid()
        {
            for (int a = 0; a < _keys.Length; a++)
            {
                if (Count((GameAction)a) == 0) return false;
                for (int s = 0; s < SlotsPerAction; s++)
                {
                    string k = _keys[a][s];
                    if (k == null) continue;
                    for (int a2 = 0; a2 < _keys.Length; a2++)
                        for (int s2 = 0; s2 < SlotsPerAction; s2++)
                            if ((a2 != a || s2 != s) && _keys[a2][s2] == k) return false;
                }
            }
            return true;
        }

        private bool Find(string key, out GameAction action, out int slot)
        {
            for (int a = 0; a < _keys.Length; a++)
                for (int s = 0; s < SlotsPerAction; s++)
                    if (_keys[a][s] == key)
                    {
                        action = (GameAction)a;
                        slot = s;
                        return true;
                    }
            action = default;
            slot = -1;
            return false;
        }
    }

    /// <summary>Short upper-case display names for keys and actions ("SPACE", "LEFT", "HOOK").</summary>
    public static class KeyLabels
    {
        /// <summary>
        /// Display label of an Input System key name: "Space" -> "SPACE", "LeftArrow" -> "LEFT",
        /// "Digit1" -> "1", "LeftShift" -> "L-SHIFT". Null or empty gives "—".
        /// </summary>
        public static string Label(string key)
        {
            if (string.IsNullOrEmpty(key)) return "—";
            switch (key)
            {
                case "LeftArrow": return "LEFT";
                case "RightArrow": return "RIGHT";
                case "UpArrow": return "UP";
                case "DownArrow": return "DOWN";
                case "Escape": return "ESC";
                case "Enter": return "ENTER";
                case "NumpadEnter": return "NUM ENTER";
                case "Backquote": return "`";
                case "Quote": return "'";
                case "Semicolon": return ";";
                case "Comma": return ",";
                case "Period": return ".";
                case "Slash": return "/";
                case "Backslash": return "\\";
                case "LeftBracket": return "[";
                case "RightBracket": return "]";
                case "Minus": return "-";
                case "Equals": return "=";
            }
            if (key.Length == 6 && key.StartsWith("Digit", StringComparison.Ordinal)) return key.Substring(5);
            if (key.StartsWith("Numpad", StringComparison.Ordinal) && key.Length > 6) return "NUM " + key.Substring(6).ToUpperInvariant();
            if (key.StartsWith("Left", StringComparison.Ordinal) && key.Length > 4) return "L-" + key.Substring(4).ToUpperInvariant();
            if (key.StartsWith("Right", StringComparison.Ordinal) && key.Length > 5) return "R-" + key.Substring(5).ToUpperInvariant();
            return key.ToUpperInvariant();
        }

        /// <summary>Display name of an action ("MOVE LEFT", "HOOK").</summary>
        public static string ActionName(GameAction action)
        {
            switch (action)
            {
                case GameAction.MoveLeft: return "MOVE LEFT";
                case GameAction.MoveRight: return "MOVE RIGHT";
                case GameAction.Jump: return "JUMP";
                case GameAction.Attack: return "ATTACK";
                case GameAction.Slide: return "SLIDE";
                case GameAction.Hook: return "HOOK";
                case GameAction.Restart: return "END RUN";
                case GameAction.Pause: return "PAUSE";
                default: return action.ToString().ToUpperInvariant();
            }
        }

        /// <summary>The fixed gamepad (and mouse) controls of an action, for read-only display.</summary>
        public static string GamepadLabel(GameAction action)
        {
            switch (action)
            {
                case GameAction.MoveLeft: return "STICK / D-PAD";
                case GameAction.MoveRight: return "STICK / D-PAD";
                case GameAction.Jump: return "A / CROSS";
                case GameAction.Attack: return "X / SQUARE · LMB";
                case GameAction.Slide: return "B / CIRCLE · LT";
                case GameAction.Hook: return "RB / RT · RMB";
                case GameAction.Restart: return "SELECT / VIEW";
                case GameAction.Pause: return "START / OPTIONS";
                default: return "";
            }
        }
    }
}
