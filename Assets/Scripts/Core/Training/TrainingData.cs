using System;
using System.Collections.Generic;

namespace Redline.Core
{
    /// <summary>
    /// What a training step asks the player to do. Each goal is checked by <see cref="TrainingScript"/>
    /// over the sim's state and events; <see cref="TrainingStep.Amount"/> is its threshold.
    /// </summary>
    public enum TrainingGoal
    {
        /// <summary>Travel Amount tiles from where the step started.</summary>
        Move,
        /// <summary>Jump and get the feet Amount tiles above the checkpoint (a held, full jump).</summary>
        Jump,
        /// <summary>Slide Amount times.</summary>
        Slide,
        /// <summary>Kill Amount enemies of the step's Target kind.</summary>
        Kill,
        /// <summary>Reach a kill chain of Amount.</summary>
        Chain,
        /// <summary>Wall-jump at least once and get the feet Amount tiles above the checkpoint.</summary>
        Climb,
        /// <summary>Stay on the rope for Amount seconds in one go.</summary>
        Hook,
        /// <summary>
        /// Let go of a swing in the air and land past the step's marker column, or fly faster than
        /// maxSpeed after the release.
        /// </summary>
        Fling,
        /// <summary>Reach speed tier Amount.</summary>
        Tier,
        /// <summary>Reflect Amount projectiles with a strike.</summary>
        Reflect,
        /// <summary>Kill the Target before it explodes, or be clear of the blast when it does.</summary>
        Survive,
        /// <summary>Pick up every orb the step drops.</summary>
        Orb
    }

    /// <summary>A training station: a room of the training level with a checkpoint to respawn at.</summary>
    public sealed class TrainingStation
    {
        /// <summary>Station id, referenced by steps.</summary>
        public string Id;
        /// <summary>Inclusive cell bounds of the room; a player whose centre leaves them is respawned.</summary>
        public int X0, Y0, X1, Y1;
        /// <summary>Checkpoint cell; the player respawns standing at its bottom-centre.</summary>
        public int CheckpointX, CheckpointY;

        /// <summary>World bottom-centre of the checkpoint cell.</summary>
        public Vec2 Checkpoint => new Vec2(CheckpointX + 0.5f, CheckpointY);

        /// <summary>True when a world point lies inside the room's cells.</summary>
        public bool Contains(Vec2 p) => p.X >= X0 && p.X < X1 + 1 && p.Y >= Y0 && p.Y < Y1 + 1;
    }

    /// <summary>An enemy or orb a step places when it starts (cell coordinates, bottom-centre).</summary>
    public readonly struct TrainingSpawn
    {
        /// <summary>(int)EnemyKind for enemies, (int)OrbKind for orbs.</summary>
        public readonly int Kind;
        /// <summary>Cell the spawn stands in.</summary>
        public readonly int X, Y;

        /// <summary>Creates a spawn.</summary>
        public TrainingSpawn(int kind, int x, int y)
        {
            Kind = kind;
            X = x;
            Y = y;
        }

        /// <summary>World bottom-centre of the cell.</summary>
        public Vec2 Position => new Vec2(X + 0.5f, Y);
    }

    /// <summary>One step of the training: its card text, station, goal and what it spawns.</summary>
    public sealed class TrainingStep
    {
        /// <summary>Step id (a-z, 0-9, '.', '_', '-').</summary>
        public string Id;
        /// <summary>The station the step runs in.</summary>
        public TrainingStation Station;
        /// <summary>What the player must do.</summary>
        public TrainingGoal Goal;
        /// <summary>The goal's threshold (tiles, seconds, a count or a tier; see <see cref="TrainingGoal"/>).</summary>
        public float Amount;
        /// <summary>Enemy kind the Kill and Survive goals count.</summary>
        public EnemyKind Target;
        /// <summary>Marker column for the Fling goal (-1 when the step has none).</summary>
        public int MarkerX = -1;
        /// <summary>Card title; may contain key tokens such as {HOOK}.</summary>
        public string Title;
        /// <summary>Card body (1-2 short lines); may contain key tokens.</summary>
        public string Body;
        /// <summary>Extra line shown after repeated failures or a long time; may contain key tokens.</summary>
        public string Tip;
        /// <summary>Enemies placed when the step starts (and again when they are all gone and the goal is not met).</summary>
        public TrainingSpawn[] Enemies = Array.Empty<TrainingSpawn>();
        /// <summary>Orbs dropped when the step starts (missing ones drop again if they fade).</summary>
        public TrainingSpawn[] Orbs = Array.Empty<TrainingSpawn>();
    }

    /// <summary>Training timing values (TrainingSteps.json "settings").</summary>
    public sealed class TrainingSettings
    {
        /// <summary>Seconds the success flash shows before the next step starts.</summary>
        public float successTime = 1.2f;
        /// <summary>Seconds a step's enemies stay stunned after they appear, so the card can be read first.</summary>
        public float spawnGrace = 1.5f;
        /// <summary>Seconds {RESTART} must be held to skip a step.</summary>
        public float skipHoldTime = 1.5f;
        /// <summary>Seconds on a step before its tip line appears.</summary>
        public float tipAfterSeconds = 25f;
        /// <summary>Failed attempts on a step before its tip line appears.</summary>
        public int tipAfterAttempts = 3;
        /// <summary>Seconds the "TRAINING COMPLETE" card shows before the game returns to the main menu.</summary>
        public float completeTime = 3f;
    }

    /// <summary>
    /// The training script's data, parsed from Resources/TrainingSteps.json:
    /// <c>{ "settings": {...}, "stations": [ { "id": "runway", "box": [x0, y0, x1, y1], "checkpoint": [x, y] } ],
    /// "steps": [ { "id": "move", "station": "runway", "goal": "move", "amount": 6, "title": "...", "body": "...",
    /// "tip": "...", "target": "grunt", "marker": 120, "enemies": [ { "kind": "grunt", "at": [x, y] } ],
    /// "orbs": [ { "kind": "speed", "at": [x, y] } ] } ] }</c>. Coordinates are cells of Training.txt
    /// (y counted from the bottom row, 0). "settings" may be partial; "target", "marker", "enemies",
    /// "orbs" and "amount" (default 1) are optional.
    /// </summary>
    /// <example>
    /// <code>
    /// var data = TrainingData.Parse(jsonText);
    /// var errors = data.Validate(map); // empty when the steps fit the level
    /// </code>
    /// </example>
    public sealed class TrainingData
    {
        /// <summary>Timing values.</summary>
        public TrainingSettings Settings { get; }
        /// <summary>Stations in file order.</summary>
        public IReadOnlyList<TrainingStation> Stations { get; }
        /// <summary>Steps in play order.</summary>
        public IReadOnlyList<TrainingStep> Steps { get; }

        private TrainingData(TrainingSettings settings, List<TrainingStation> stations, List<TrainingStep> steps)
        {
            Settings = settings;
            Stations = stations;
            Steps = steps;
        }

        /// <summary>Parses TrainingSteps.json. Throws FormatException naming the bad entry.</summary>
        public static TrainingData Parse(string json)
        {
            if (!(MiniJson.Parse(json) is Dictionary<string, object> root)) throw new FormatException("TrainingSteps.json must be a JSON object");
            var settings = new TrainingSettings();
            if (root.TryGetValue("settings", out var so))
            {
                if (!(so is Dictionary<string, object> sd)) throw new FormatException("'settings' must be an object");
                foreach (var kv in sd) ApplySetting(settings, kv.Key, kv.Value);
            }

            var stations = new List<TrainingStation>();
            foreach (var o in Objects(root, "stations"))
            {
                var box = Ints(o, "box", 4, "station");
                var cp = Ints(o, "checkpoint", 2, "station");
                var st = new TrainingStation
                {
                    Id = Text(o, "id", true, "station"),
                    X0 = box[0], Y0 = box[1], X1 = box[2], Y1 = box[3],
                    CheckpointX = cp[0], CheckpointY = cp[1]
                };
                if (st.X1 < st.X0 || st.Y1 < st.Y0) throw new FormatException($"station '{st.Id}': box must be [x0, y0, x1, y1] with x0 <= x1 and y0 <= y1");
                foreach (var other in stations)
                    if (other.Id == st.Id) throw new FormatException($"station '{st.Id}' is defined twice");
                stations.Add(st);
            }

            var steps = new List<TrainingStep>();
            foreach (var o in Objects(root, "steps"))
            {
                string id = Text(o, "id", true, "step");
                if (!HintZone.IsValidId(id)) throw new FormatException($"step '{id}': id must use only a-z, 0-9, '.', '_' or '-'");
                foreach (var other in steps)
                    if (other.Id == id) throw new FormatException($"step '{id}' is defined twice");
                string where = "step '" + id + "'";
                var step = new TrainingStep
                {
                    Id = id,
                    Title = Text(o, "title", true, where),
                    Body = Text(o, "body", true, where),
                    Tip = Text(o, "tip", true, where)
                };
                string stationId = Text(o, "station", true, where);
                foreach (var st in stations)
                    if (st.Id == stationId) step.Station = st;
                if (step.Station == null) throw new FormatException($"{where}: unknown station '{stationId}'");
                if (!Enum.TryParse(Text(o, "goal", true, where), true, out step.Goal) || !Enum.IsDefined(typeof(TrainingGoal), step.Goal))
                    throw new FormatException($"{where}: unknown goal '{o["goal"]}'");
                step.Amount = 1f;
                if (o.TryGetValue("amount", out var a))
                {
                    if (!(a is double d) || !(d > 0)) throw new FormatException($"{where}: 'amount' must be a positive number");
                    step.Amount = (float)d;
                }
                string target = Text(o, "target", false, where);
                if (target != null && !TryKind(target, out step.Target)) throw new FormatException($"{where}: unknown target '{target}'");
                if (o.TryGetValue("marker", out var m))
                {
                    if (!(m is double md) || md != Math.Floor(md) || md < 0) throw new FormatException($"{where}: 'marker' must be a cell column");
                    step.MarkerX = (int)md;
                }
                step.Enemies = Spawns(o, "enemies", where, true);
                step.Orbs = Spawns(o, "orbs", where, false);
                steps.Add(step);
            }
            if (steps.Count == 0) throw new FormatException("TrainingSteps.json has no steps");
            return new TrainingData(settings, stations, steps);
        }

        /// <summary>
        /// Problems with the data against the training level: stations outside the map, checkpoints
        /// that are not empty cells over a floor, spawns in walls, and goals missing what they need
        /// (a Kill target among the spawns, a shooter to reflect, orbs, a marker). Empty when it fits.
        /// </summary>
        public List<string> Validate(TileMap map)
        {
            var errors = new List<string>();
            if (!map.Training) errors.Add("the level is not marked ';training'");
            foreach (var st in Stations)
            {
                if (st.X0 < 0 || st.Y0 < 0 || st.X1 >= map.Width || st.Y1 >= map.Height) errors.Add($"station '{st.Id}' lies outside the map");
                if (!st.Contains(st.Checkpoint + new Vec2(0f, 0.5f))) errors.Add($"station '{st.Id}': checkpoint is outside its box");
                if (map.Get(st.CheckpointX, st.CheckpointY) != Tile.Empty || map.Get(st.CheckpointX, st.CheckpointY + 1) != Tile.Empty)
                    errors.Add($"station '{st.Id}': checkpoint has no head room");
                if (!map.IsSolid(st.CheckpointX, st.CheckpointY - 1)) errors.Add($"station '{st.Id}': checkpoint is not on a floor");
            }
            foreach (var step in Steps)
            {
                string where = "step '" + step.Id + "'";
                foreach (var s in step.Enemies) CheckSpawn(map, step, s, where + " enemy", errors);
                foreach (var s in step.Orbs) CheckSpawn(map, step, s, where + " orb", errors);
                switch (step.Goal)
                {
                    case TrainingGoal.Kill:
                    case TrainingGoal.Survive:
                        if (!HasEnemy(step, step.Target)) errors.Add($"{where}: no {step.Target} to {(step.Goal == TrainingGoal.Kill ? "kill" : "survive")}");
                        break;
                    case TrainingGoal.Chain:
                        if (step.Enemies.Length < step.Amount) errors.Add($"{where}: fewer enemies than the chain asks for");
                        break;
                    case TrainingGoal.Reflect:
                        if (!HasEnemy(step, EnemyKind.Shooter)) errors.Add($"{where}: no shooter to reflect");
                        break;
                    case TrainingGoal.Orb:
                        if (step.Orbs.Length == 0) errors.Add($"{where}: no orbs to pick up");
                        break;
                    case TrainingGoal.Fling:
                        if (step.MarkerX < step.Station.X0 || step.MarkerX > step.Station.X1) errors.Add($"{where}: needs a marker inside its station");
                        break;
                }
            }
            return errors;
        }

        private static void CheckSpawn(TileMap map, TrainingStep step, TrainingSpawn s, string where, List<string> errors)
        {
            if (!step.Station.Contains(s.Position + new Vec2(0f, 0.5f))) errors.Add($"{where} at ({s.X}, {s.Y}) is outside the station");
            if (map.Get(s.X, s.Y) != Tile.Empty) errors.Add($"{where} at ({s.X}, {s.Y}) is inside a wall");
        }

        private static bool HasEnemy(TrainingStep step, EnemyKind kind)
        {
            foreach (var e in step.Enemies)
                if (e.Kind == (int)kind) return true;
            return false;
        }

        private static bool TryKind(string name, out EnemyKind kind) =>
            Enum.TryParse(name, true, out kind) && Enum.IsDefined(typeof(EnemyKind), kind);

        private static TrainingSpawn[] Spawns(Dictionary<string, object> o, string key, string where, bool enemies)
        {
            if (!o.TryGetValue(key, out var v)) return Array.Empty<TrainingSpawn>();
            if (!(v is List<object> list)) throw new FormatException($"{where}: '{key}' must be an array");
            var result = new TrainingSpawn[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                if (!(list[i] is Dictionary<string, object> so)) throw new FormatException($"{where}: {key}[{i}] must be an object");
                string kindName = Text(so, "kind", true, where);
                int kind;
                if (enemies)
                {
                    if (!TryKind(kindName, out var ek)) throw new FormatException($"{where}: unknown enemy kind '{kindName}'");
                    kind = (int)ek;
                }
                else
                {
                    if (!Enum.TryParse(kindName, true, out OrbKind ok) || !Enum.IsDefined(typeof(OrbKind), ok))
                        throw new FormatException($"{where}: unknown orb kind '{kindName}'");
                    kind = (int)ok;
                }
                var at = Ints(so, "at", 2, where);
                result[i] = new TrainingSpawn(kind, at[0], at[1]);
            }
            return result;
        }

        private static IEnumerable<Dictionary<string, object>> Objects(Dictionary<string, object> root, string key)
        {
            if (!root.TryGetValue(key, out var v) || !(v is List<object> list)) throw new FormatException($"TrainingSteps.json needs a '{key}' array");
            for (int i = 0; i < list.Count; i++)
            {
                if (!(list[i] is Dictionary<string, object> o)) throw new FormatException($"{key}[{i}] must be an object");
                yield return o;
            }
        }

        private static string Text(Dictionary<string, object> o, string key, bool required, string where)
        {
            if (!o.TryGetValue(key, out var v) || v == null)
            {
                if (required) throw new FormatException($"{where} has no '{key}'");
                return null;
            }
            if (!(v is string s) || s.Trim().Length == 0) throw new FormatException($"{where}: '{key}' must be a non-empty string");
            return s;
        }

        private static int[] Ints(Dictionary<string, object> o, string key, int count, string where)
        {
            if (!o.TryGetValue(key, out var v) || !(v is List<object> list) || list.Count != count)
                throw new FormatException($"{where}: '{key}' must be an array of {count} whole numbers");
            var r = new int[count];
            for (int i = 0; i < count; i++)
            {
                if (!(list[i] is double d) || d != Math.Floor(d)) throw new FormatException($"{where}: '{key}' must be an array of {count} whole numbers");
                r[i] = (int)d;
            }
            return r;
        }

        private static void ApplySetting(TrainingSettings t, string key, object value)
        {
            if (!(value is double d) || !(d > 0)) throw new FormatException($"setting '{key}' must be a positive number");
            float f = (float)d;
            switch (key)
            {
                case "successTime": t.successTime = f; break;
                case "spawnGrace": t.spawnGrace = f; break;
                case "skipHoldTime": t.skipHoldTime = f; break;
                case "tipAfterSeconds": t.tipAfterSeconds = f; break;
                case "completeTime": t.completeTime = f; break;
                case "tipAfterAttempts":
                    if (d != Math.Floor(d)) throw new FormatException("setting 'tipAfterAttempts' must be a whole number");
                    t.tipAfterAttempts = (int)d;
                    break;
                default: throw new FormatException($"unknown setting '{key}' (known: " + string.Join(", ", "successTime", "spawnGrace", "skipHoldTime", "tipAfterSeconds", "tipAfterAttempts", "completeTime") + ")");
            }
        }
    }
}
