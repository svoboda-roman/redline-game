using System;
using System.Collections.Generic;
using System.Reflection;
using Redline.Core;

namespace Redline.Tests
{
    /// <summary>
    /// A scripted player used to prove the arena and waves are winnable (and that sloppy play is not).
    /// Skilled: kills grunts on its level, counter-hits diving drones, lures charging brutes into walls,
    /// and reaches tier 3 for armored brutes with a measured jump-and-hook launch.
    /// Sloppy: walks at the nearest enemy and swings — no jumps, hook or slide.
    /// </summary>
    public sealed class ScriptedPlayer
    {
        private enum Plan { None, ToRunup, RunIn, Rising, Swinging, Flying }

        private struct Launch
        {
            public float X;
            /// <summary>Floor height the launch was measured from: it only works from that level.</summary>
            public float Y;
            public int Dir;
            public float Speed;
            public float ReleaseX;
            /// <summary>
            /// Along the flight while the player is at tier 3: the strike box (minX, minY, maxX, maxY)
            /// then the player's body (minX, minY, maxX, maxY), one entry per step.
            /// </summary>
            public List<float[]> Reach;
        }

        private const float Runup = 2.5f;
        private readonly bool _skilled;
        private readonly List<Launch> _launches = new List<Launch>();
        private Plan _plan;
        private Launch _launch;
        private Enemy _planTarget;
        private float _planTime;
        private float _lastSpeed;
        private int _jumpHold;
        private float _groundY = 1f;
        private Enemy _chase;
        private int _crossDir;
        private bool _crossHooked;
        private TileMap _map;
        private RedlineConfig _cfg;

        /// <summary>Hook launches the arena offers that reach tier 3 (measured at construction).</summary>
        public int LaunchCount => _launches.Count;

        /// <summary>Current plan step, for diagnostics.</summary>
        public string PlanName => _plan.ToString();

        /// <summary>Hook launches this player has completed (released at speed toward a brute).</summary>
        public int HookLaunches { get; private set; }

        public ScriptedPlayer(RedlineConfig cfg, TileMap map, bool skilled)
        {
            _skilled = skilled;
            _map = map;
            _cfg = cfg;
            if (skilled) MeasureLaunches(cfg, map);
        }

        /// <summary>The input for the next step.</summary>
        public InputFrame Next(RedlineSim sim)
        {
            if (sim.Player.Grounded) _groundY = sim.Player.Y;
            var input = Decide(sim);
            if (_skilled && _plan == Plan.None) AvoidArmor(sim, ref input);
            HopWalls(sim, ref input);
            if (_crossDir != 0) CrossTrench(sim, ref input);
            else _crossHooked = false;
            if (_skilled && _plan == Plan.None) SteerClearInAir(sim, ref input);
            if (_skilled && ChargeIncoming(sim) && sim.Player.Grounded)
            {
                input.JumpPressed = true; // reflex: hop over a charge, whatever the plan
                input.JumpHeld = true;
                _jumpHold = 30;
            }
            return input;
        }

        /// <summary>Basic movement for both players: hop over a low wall that blocks the way.</summary>
        private void HopWalls(RedlineSim sim, ref InputFrame input)
        {
            var p = sim.Player;
            if (!p.Grounded || input.MoveX == 0f) return;
            int dir = MathF.Sign(input.MoveX);
            int floorY = (int)MathF.Floor(p.Y + 0.01f);
            // A run-speed jump clears a spike or two from about a tile out.
            bool spikes = sim.Map.Get((int)MathF.Floor(p.X + dir * 1f), floorY) == Tile.Spike;
            if (p.WallDir != dir && !spikes) return;
            input.JumpPressed = true;
            input.JumpHeld = true;
            _jumpHold = 20;
            // A trench (Loop01's x 20-24, under the hook gate) is wider than any run-speed jump:
            // a skilled player swings across it on the hook.
            if (_skilled && spikes && SpikeRun(sim.Map, p.X + dir * 1f, floorY, dir) >= 3) _crossDir = dir;
        }

        /// <summary>How many spike tiles in a row from x on, in one direction.</summary>
        private static int SpikeRun(TileMap map, float x, int y, int dir)
        {
            int n = 0;
            while (n < 12 && map.Get((int)MathF.Floor(x + dir * n), y) == Tile.Spike) n++;
            return n;
        }

        /// <summary>Hook swing across a spike trench: hook once falling, hold until clear of the spikes.</summary>
        private void CrossTrench(RedlineSim sim, ref InputFrame input)
        {
            var p = sim.Player;
            int dir = _crossDir;
            input.MoveX = dir;
            if (p.Grounded && !input.JumpPressed)
            {
                _crossDir = 0;
                return;
            }
            int floorY = (int)MathF.Floor(_groundY + 0.01f);
            bool overSpikes = false;
            for (float k = -0.6f; k <= 1.2f; k += 0.3f)
                if (sim.Map.Get((int)MathF.Floor(p.X + dir * k), floorY) == Tile.Spike) overSpikes = true;
            if (!p.Hooked)
            {
                if (overSpikes && !p.Grounded && p.Vy < 0f && !_crossHooked) // at the top of the hop: a low hook swings into the spikes
                {
                    input.HookPressed = true;
                    input.HookHeld = true;
                    _crossHooked = true;
                }
                return;
            }
            input.HookHeld = overSpikes;
            if (!overSpikes)
            {
                _crossDir = 0;
                _crossHooked = false;
            }
        }

        /// <summary>
        /// Reflex in the air (after a fling's kill, say): steer away from a brute below that this
        /// strike cannot hurt, so the player does not land on it.
        /// </summary>
        private static void SteerClearInAir(RedlineSim sim, ref InputFrame input)
        {
            var p = sim.Player;
            if (p.Grounded || p.Vy > 0f) return; // only on the way down: a hop over the pit is meant to pass it
            foreach (var e in sim.Enemies)
            {
                if (!e.Alive || e.Kind != EnemyKind.Brute || CombatRules.CanDamage(sim.PlayerTier, e.ArmorTier)) continue;
                float dx = sim.Map.Dx(p.X, e.X);
                if (MathF.Abs(dx) > 3f || p.Y < e.Y || p.Y - e.MaxY > 4f) continue;
                input.MoveX = dx == 0f ? -p.Facing : -MathF.Sign(dx);
                return;
            }
        }

        /// <summary>Reflex: never walk into a brute whose armor this strike cannot break.</summary>
        private static void AvoidArmor(RedlineSim sim, ref InputFrame input)
        {
            var p = sim.Player;
            if (input.MoveX == 0f) return;
            foreach (var e in sim.Enemies)
            {
                if (!e.Alive || e.Kind != EnemyKind.Brute || CombatRules.CanDamage(sim.PlayerTier, e.ArmorTier)) continue;
                float dx = sim.Map.Dx(p.X, e.X);
                float gap = MathF.Abs(dx) - (p.Width + e.Width) * 0.5f;
                if (MathF.Sign(dx) == MathF.Sign(input.MoveX) && gap < 1.2f && MathF.Abs(e.Y - p.Y) < 1.5f)
                {
                    input.MoveX = -input.MoveX;
                    return;
                }
            }
        }

        private static bool ChargeIncoming(RedlineSim sim)
        {
            var p = sim.Player;
            foreach (var e in sim.Enemies)
            {
                if (!e.Alive || e.Kind != EnemyKind.Brute || e.Mode != BruteMode.Charge) continue;
                float dx = sim.Map.Dx(e.X, p.X);
                if (MathF.Sign(dx) != e.ChargeDir || MathF.Abs(e.Y - p.Y) > 1f) continue;
                float gap = MathF.Abs(dx) - (p.Width + e.Width) * 0.5f;
                float closing = MathF.Abs(e.Vx - p.Vx);
                float arrival = closing > 0.1f ? gap / closing : 99f;
                if (arrival > 0.15f && arrival < 0.24f) return true; // feet clear the brute from ~0.19 s to ~0.5 s
            }
            return false;
        }

        private InputFrame Decide(RedlineSim sim)
        {
            var input = new InputFrame();
            if (_jumpHold > 0)
            {
                input.JumpHeld = true;
                _jumpHold--;
            }
            if (!_skilled) return Sloppy(sim, input);

            if (_plan != Plan.None) return ContinuePlan(sim, input);

            var p = sim.Player;
            Enemy grunt = Nearest(sim, EnemyKind.Grunt, sameLevelOnly: true);
            // Keep after a grunt the route leads to: on the way the route climbs off its level.
            if (grunt == null && _chase != null && _chase.Alive) grunt = _chase;
            _chase = grunt;
            Enemy drone = Nearest(sim, EnemyKind.Drone, sameLevelOnly: false);
            Enemy brute = Nearest(sim, EnemyKind.Brute, sameLevelOnly: false);

            // A brute just above (on the stairs): bring it down first, or the route to anything
            // else runs under or into it.
            var perched = BruteAbove(sim);
            if (perched != null) return LureDown(sim, input, perched);
            if (grunt != null) return Approach(sim, input, grunt.X, grunt.Y);
            if (brute != null && brute.Mode == BruteMode.Crashed) return Approach(sim, input, brute.X);
            // A brute above the player (Loop01 spawns it on the stairs, the route up): bring it down
            // before anything else, or the bot walks up the stairs into it.
            if (brute != null && brute.Y - Level(p) > 1f) return LureDown(sim, input, brute);
            if (brute != null && Grounded(brute) && TryStartLaunch(sim, brute)) return ContinuePlan(sim, input);
            if (brute != null && Level(p) - brute.Y > 1f) return LureDown(sim, input, brute);
            // Enemies guarding posts above: walk close enough to wake them, so they come down.
            var anyGrunt = Nearest(sim, EnemyKind.Grunt, sameLevelOnly: false);
            if (anyGrunt != null) return Wait(sim, input, anyGrunt);
            if (brute != null && brute.Charges && !sim.Map.WrapsX) return LureToWall(sim, input, brute);
            if (drone != null && drone.Y < Level(p))
            {
                // A drone below the player's floor hovers under it, out of reach: go down to its level.
                int cx = Cell(drone.X);
                for (int y = (int)MathF.Floor(drone.Y); y >= 1; y--)
                    if (StandCell(cx, y))
                    {
                        Navigate(sim, ref input, cx + 0.5f, y);
                        return Opportunist(sim, input);
                    }
            }
            if (drone != null) return Approach(sim, input, drone.X, standUnder: true);
            if (brute != null) return Approach(sim, input, sim.Map.Width * 0.5f); // reposition for a launch
            return Opportunist(sim, input);
        }

        /// <summary>Reports what happened in the last step (hurt cancels a plan).</summary>
        public void Observe(RedlineSim sim)
        {
            foreach (var ev in sim.Events)
                if (ev.Type == SimEventType.PlayerHurt) _plan = Plan.None;
        }

        // ------------------------------------------------------------------ basic behaviours

        private InputFrame Approach(RedlineSim sim, InputFrame input, float targetX, bool standUnder = false)
        {
            var p = sim.Player;
            float dx = sim.Map.Dx(p.X, targetX);
            float stop = standUnder ? 0.3f : 0.9f;
            input.MoveX = MathF.Abs(dx) > stop ? MathF.Sign(dx) : 0f;
            return Opportunist(sim, input);
        }

        /// <summary>Approach on a level: far off, follow the route (the straight way can be walled off).</summary>
        private InputFrame Approach(RedlineSim sim, InputFrame input, float targetX, float targetY)
        {
            if (_skilled && MathF.Abs(sim.Map.Dx(sim.Player.X, targetX)) > 2.5f)
            {
                Navigate(sim, ref input, targetX, (int)MathF.Round(targetY));
                return Opportunist(sim, input);
            }
            return Approach(sim, input, targetX);
        }

        /// <summary>
        /// Draws an enemy above down: close enough to wake it from its post, but out from under
        /// where it will land.
        /// </summary>
        private InputFrame Wait(RedlineSim sim, InputFrame input, Enemy above)
        {
            float dx = sim.Map.Dx(sim.Player.X, above.X);
            if (MathF.Abs(dx) < 3f) input.MoveX = -MathF.Sign(dx == 0f ? 1f : dx);
            else if (MathF.Abs(dx) > 5f) input.MoveX = MathF.Sign(dx);
            return Opportunist(sim, input);
        }

        /// <summary>
        /// Brute on another level. Player above it: walk away from it until off the ledge (down to its
        /// level or below). Player below it: stand off to the side so it walks down to the player.
        /// </summary>
        private InputFrame LureDown(RedlineSim sim, InputFrame input, Enemy brute)
        {
            var p = sim.Player;
            if (Level(p) > brute.Y + 1f)
            {
                float dx = sim.Map.Dx(p.X, brute.X);
                input.MoveX = -MathF.Sign(dx == 0f ? 1f : dx);
                return Opportunist(sim, input);
            }
            // Below it: stand off the side it can walk off (a ledge, not a wall it cannot climb),
            // so following the player brings it down.
            int level = (int)MathF.Round(Level(p));
            if (!Descent(brute, level, out float landX, out int side)) return Wait(sim, input, brute);
            float spot = landX + side * 3f;
            for (int r = 0; r <= 6; r++)
            {
                // The nearest place to stand on the player's own level (the spot can be over a gap).
                int c = Cell(spot + side * r);
                if (StandCell(c, level))
                {
                    spot = c + 0.5f;
                    break;
                }
            }
            float toSpot = sim.Map.Dx(p.X, spot);
            if (MathF.Abs(toSpot) < 0.5f && p.Grounded) input.MoveX = 0f;
            else Navigate(sim, ref input, spot, level);
            return Opportunist(sim, input);
        }

        /// <summary>
        /// Where a walker that cannot jump ends up following the player down: off the one open side of
        /// each ledge in turn, until it reaches the given level. False when the way down is not one-sided.
        /// </summary>
        private bool Descent(Enemy e, int level, out float landX, out int side)
        {
            float x = e.X;
            int y = (int)MathF.Round(e.Y);
            landX = x;
            side = 0;
            for (int step = 0; step < 12 && y > level; step++)
            {
                float right = EdgeDistance(x, y, 1), left = EdgeDistance(x, y, -1);
                if ((right >= 0f) == (left >= 0f)) return false;
                side = right >= 0f ? 1 : -1;
                x = Cell(x + side * ((side > 0 ? right : left) + 1f)) + 0.5f;
                int cx = Cell(x);
                while (y > 0 && !_map.IsSolid(cx, y - 1)) y--;
            }
            landX = x;
            return side != 0 && y <= level;
        }

        /// <summary>Tiles from x to the end of the floor at level y on one side, or -1 when a wall comes first.</summary>
        private float EdgeDistance(float x, int y, int dir)
        {
            for (int d = 0; d <= 12; d++)
            {
                int cx = Cell(x + dir * d);
                if (_map.IsSolid(cx, y) || _map.IsSolid(cx, y + 1)) return -1f;
                if (!_map.IsSolid(cx, y - 1)) return d;
            }
            return -1f;
        }

        /// <summary>Charging brute: stand at the nearer wall, jump the charge, then hit it while dazed.</summary>
        private InputFrame LureToWall(RedlineSim sim, InputFrame input, Enemy brute)
        {
            var p = sim.Player;
            float leftX = 1.6f;
            float rightX = sim.Map.Width - 1.6f;
            // The wall nearest the brute: a guarding brute only charges within reach of its post.
            float wallX = MathF.Abs(brute.X - leftX) < MathF.Abs(brute.X - rightX) ? leftX : rightX;
            float dx = wallX - p.X;
            if (MathF.Abs(dx) > 0.4f && brute.Mode != BruteMode.Charge) input.MoveX = MathF.Sign(dx);

            float gap = MathF.Abs(sim.Map.Dx(p.X, brute.X));
            if (brute.Mode == BruteMode.Charge && gap < 3.4f && p.Grounded)
            {
                input.JumpPressed = true;
                input.JumpHeld = true;
                _jumpHold = 30;
            }
            return Opportunist(sim, input);
        }

        private InputFrame Sloppy(RedlineSim sim, InputFrame input)
        {
            Enemy target = null;
            float best = float.MaxValue;
            foreach (var e in sim.Enemies)
            {
                if (!e.Alive) continue;
                float d = MathF.Abs(e.X - sim.Player.X) + MathF.Abs(e.Y - sim.Player.Y);
                if (d < best)
                {
                    best = d;
                    target = e;
                }
            }
            if (target != null)
            {
                float dx = target.X - sim.Player.X;
                input.MoveX = MathF.Abs(dx) > 0.9f ? MathF.Sign(dx) : 0f;
            }
            input.AttackPressed = AnyKillableInReach(sim);
            return input;
        }

        /// <summary>Swings at anything the current strike can damage; turns toward an enemy just behind.</summary>
        private InputFrame Opportunist(RedlineSim sim, InputFrame input)
        {
            if (AnyKillableInReach(sim))
            {
                input.AttackPressed = true;
                return input;
            }
            var p = sim.Player;
            foreach (var e in sim.Enemies)
            {
                if (!e.Alive || !CombatRules.CanDamage(sim.PlayerTier, e.ArmorTier)) continue;
                float dx = sim.Map.Dx(p.X, e.X);
                bool behind = MathF.Sign(dx) == -p.Facing && MathF.Abs(dx) < 1.6f && MathF.Abs(e.Center.Y - p.Center.Y) < 1.4f;
                if (!behind) continue;
                input.MoveX = MathF.Sign(dx);
                input.AttackPressed = true;
                break;
            }
            return input;
        }

        private bool TargetInReach(RedlineSim sim)
        {
            sim.GetStrikeBox(out float minX, out float minY, out float maxX, out float maxY);
            var e = _planTarget;
            return e != null && e.Alive && CombatRules.CanDamage(sim.PlayerTier, e.ArmorTier) && e.Overlaps(minX, minY, maxX, maxY);
        }

        private static bool AnyKillableInReach(RedlineSim sim)
        {
            sim.GetStrikeBox(out float minX, out float minY, out float maxX, out float maxY);
            int tier = sim.PlayerTier;
            foreach (var e in sim.Enemies)
                if (e.Alive && CombatRules.CanDamage(tier, e.ArmorTier) && e.Overlaps(minX, minY, maxX, maxY))
                    return true;
            return false;
        }

        // ------------------------------------------------------------------ hook launch plan

        private bool TryStartLaunch(RedlineSim sim, Enemy brute)
        {
            var p = sim.Player;
            float bestCost = float.MaxValue;
            bool found = false;
            foreach (var l in _launches)
            {
                var map = sim.Map;
                if (!Reaches(map, l, brute)) continue;
                float runupX = l.X - l.Dir * Runup;
                if (!map.WrapsX && (runupX < 1.5f || runupX > map.Width - 1.5f)) continue;
                float cost = MathF.Abs(map.Dx(p.X, runupX)) + MathF.Abs(Level(p) - l.Y);
                if (cost >= bestCost) continue;
                bestCost = cost;
                _launch = l;
                found = true;
            }
            if (!found) return false;
            _plan = Plan.ToRunup;
            _planTarget = brute;
            _planTime = 0f;
            return true;
        }

        /// <summary>
        /// True when the launch's tier-3 flight puts the brute, where it stands now, inside a strike
        /// before the player's body touches it (a touch first is contact damage, not a kill).
        /// </summary>
        private static bool Reaches(TileMap map, Launch l, Enemy brute)
        {
            foreach (var s in l.Reach)
            {
                if (Overlap(map, s[4], s[5], s[6], s[7], brute, -Margin)) return false;
                if (Overlap(map, s[0], s[1], s[2], s[3], brute, Margin)) return true;
            }
            return false;
        }

        /// <summary>
        /// Slack for the measured flight: the bot's own run-in differs by a few hundredths from the
        /// probe's, so a strike must overlap by this much, and a body that comes this close counts as a touch.
        /// </summary>
        private const float Margin = 0.3f;

        /// <summary>Box against an enemy; a positive margin demands that much overlap, a negative one allows that gap.</summary>
        private static bool Overlap(TileMap map, float minX, float minY, float maxX, float maxY, Enemy e, float margin)
        {
            float dx = map.Dx((minX + maxX) * 0.5f, e.X);
            // Vertically a third of the slack: a dive moves ~0.4 tiles a step, so the first overlap is shallow.
            float my = margin / 3f;
            return MathF.Abs(dx) < (maxX - minX + e.Width) * 0.5f - margin && maxY - my > e.Y && minY + my < e.MaxY;
        }

        private InputFrame ContinuePlan(RedlineSim sim, InputFrame input)
        {
            var p = sim.Player;
            _planTime += RedlineSim.FixedDt;
            int dir = _launch.Dir;
            if (_planTarget == null || !_planTarget.Alive || _planTime > 8f) return Abort(sim, input);

            if (_plan == Plan.ToRunup || _plan == Plan.RunIn)
            {
                // A grunt on the way comes first: fight it, then plan the launch again.
                var g = Nearest(sim, EnemyKind.Grunt, sameLevelOnly: true);
                if (g != null && MathF.Abs(sim.Map.Dx(p.X, g.X)) < 5f) return Abort(sim, input);
                // The brute moved off the measured flight: plan again from where it is now.
                if (!Reaches(sim.Map, _launch, _planTarget)) return Abort(sim, input);
            }
            switch (_plan)
            {
                case Plan.ToRunup:
                    float runupX = _launch.X - dir * Runup;
                    float toRunup = sim.Map.Dx(p.X, runupX);
                    if (MathF.Abs(_groundY - _launch.Y) < 0.1f) // on the launch's level (or hopping on it)
                    {
                        input.MoveX = MathF.Sign(toRunup);
                        if (MathF.Abs(toRunup) < 0.5f && p.Grounded) _plan = Plan.RunIn;
                    }
                    else
                    {
                        Navigate(sim, ref input, runupX, (int)MathF.Round(_launch.Y));
                    }
                    return input;
                case Plan.RunIn:
                    input.MoveX = dir;
                    float along = sim.Map.Dx(_launch.X, p.X) * dir;
                    if (along > 1f) return Abort(sim, input);
                    if (along >= -0.25f && p.Grounded && p.Vx * dir >= 8f)
                    {
                        input.JumpPressed = true;
                        input.JumpHeld = true;
                        _jumpHold = 40;
                        _plan = Plan.Rising;
                    }
                    return input;
                case Plan.Rising:
                    input.MoveX = dir;
                    if (p.Vy <= 0f)
                    {
                        input.HookPressed = true;
                        input.HookHeld = true;
                        _plan = Plan.Swinging;
                        _lastSpeed = 0f;
                    }
                    return input;
                case Plan.Swinging:
                    if (!p.Hooked && _lastSpeed > 0f) return Abort(sim, input);
                    float speed = MathF.Abs(p.Vx);
                    bool pastPeak = speed < _lastSpeed - 0.05f && _lastSpeed >= sim.Config.tier3Speed;
                    input.MoveX = dir;
                    input.HookHeld = !pastPeak && _planTime < 6f;
                    // Only the target cuts the swing short: an early release for anything else
                    // flies a different arc from the measured one.
                    if (TargetInReach(sim))
                    {
                        input.AttackPressed = true;
                        input.HookHeld = false;
                        pastPeak = true;
                    }
                    if (pastPeak)
                    {
                        _plan = Plan.Flying;
                        HookLaunches++;
                    }
                    _lastSpeed = MathF.Max(_lastSpeed, speed);
                    return input;
                default: // Flying
                    input.MoveX = dir;
                    if (sim.PlayerTier < 3) return Abort(sim, input);
                    if (TargetInReach(sim)) input.AttackPressed = true; // a kill on the way changes the arc
                    return input;
            }
        }

        private InputFrame Abort(RedlineSim sim, InputFrame input)
        {
            _plan = Plan.None;
            return Opportunist(sim, input);
        }

        // ------------------------------------------------------------------ navigation

        private int _navDir;

        /// <summary>
        /// Walks toward a spot on another level along the shortest route of standable cells (walk, drop,
        /// jump up to two tiles or across a gap of up to two), jumping where the route needs it.
        /// </summary>
        private void Navigate(RedlineSim sim, ref InputFrame input, float targetX, int targetY)
        {
            var p = sim.Player;
            if (!p.Grounded)
            {
                input.MoveX = _navDir;
                return;
            }
            int cx = Cell(p.X);
            int cy = (int)MathF.Round(p.Y);
            var next = NextOnRoute(cx, cy, Cell(targetX), targetY);
            if (next.x < 0)
            {
                input.MoveX = MathF.Sign(sim.Map.Dx(p.X, targetX));
                _navDir = (int)input.MoveX;
                return;
            }
            float dx = sim.Map.Dx(p.X, next.x + 0.5f);
            _navDir = MathF.Sign(dx) == 0 ? _navDir : MathF.Sign(dx);
            input.MoveX = _navDir;
            int span = Math.Abs(WrapDelta(cx, next.x));
            bool up = next.y > cy;
            bool gap = next.y == cy && span > 1;
            // Take off at the edge for a gap; for a climb, as soon as the step is in reach.
            float footAhead = p.X + _navDir * (p.Width * 0.5f + 0.15f);
            bool edge = !sim.Map.IsSolid(Cell(footAhead), cy - 1);
            if ((gap && edge) || (up && MathF.Abs(dx) < 2.2f))
            {
                input.JumpPressed = true;
                input.JumpHeld = true;
                _jumpHold = 30;
            }
        }

        private int Cell(float x)
        {
            int c = (int)MathF.Floor(x);
            return _map.WrapsX ? ((c % _map.Width) + _map.Width) % _map.Width : c;
        }

        private int WrapDelta(int from, int to)
        {
            int d = to - from;
            if (!_map.WrapsX) return d;
            if (d > _map.Width / 2) d -= _map.Width;
            if (d < -_map.Width / 2) d += _map.Width;
            return d;
        }

        private bool StandCell(int cx, int y)
        {
            if (!_map.WrapsX && (cx < 0 || cx >= _map.Width)) return false;
            int c = Cell(cx + 0.5f);
            return _map.Get(c, y) != Tile.Spike && Standable(_map, c + 0.5f, y, _cfg.playerWidth, _cfg.playerHeight);
        }

        /// <summary>Room for a jump: above the start, and over every column it crosses, up to the arc's top.</summary>
        private bool ClearArc(int x, int y, int dir, int d, int h)
        {
            int top = y + h + 2;
            for (int k = 0; k <= d; k++)
            {
                int cx = Cell(x + dir * k + 0.5f);
                int from = k == 0 ? y + 1 : y + h;
                for (int cy = from; cy <= top; cy++)
                    if (_map.IsSolid(cx, cy)) return false;
            }
            return true;
        }

        /// <summary>First cell after the start on a shortest route (breadth first), or x = -1 when none.</summary>
        private readonly Dictionary<(int, int, int, int), (int, int)> _routes = new Dictionary<(int, int, int, int), (int, int)>();

        private (int x, int y) NextOnRoute(int sx, int sy, int tx, int ty)
        {
            if (_routes.TryGetValue((sx, sy, tx, ty), out var cached)) return cached;
            var next = FindRoute(sx, sy, tx, ty);
            _routes[(sx, sy, tx, ty)] = next;
            return next;
        }

        private (int x, int y) FindRoute(int sx, int sy, int tx, int ty)
        {
            var start = (sx, sy);
            var prev = new Dictionary<(int, int), (int, int)> { [start] = start };
            var queue = new Queue<(int x, int y)>();
            queue.Enqueue(start);
            bool found = false;
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                if (c.x == tx && c.y == ty)
                {
                    found = true;
                    break;
                }
                foreach (var n in Neighbours(c.x, c.y))
                {
                    if (prev.ContainsKey(n)) continue;
                    prev[n] = c;
                    queue.Enqueue(n);
                }
            }
            if (!found) return (-1, 0);
            var at = (tx, ty);
            while (prev[at] != start) at = prev[at];
            return at;
        }

        private IEnumerable<(int x, int y)> Neighbours(int x, int y)
        {
            foreach (int dir in new[] { 1, -1 })
            {
                int nx = Cell(x + dir + 0.5f);
                bool walk = StandCell(nx, y);
                if (walk) yield return (nx, y);
                else if (!_map.IsSolid(nx, y) && !_map.IsSolid(nx, y + 1))
                {
                    for (int fy = y - 1; fy >= 1; fy--) // drop off the edge
                    {
                        if (StandCell(nx, fy))
                        {
                            yield return (nx, fy);
                            break;
                        }
                        if (_map.IsSolid(nx, fy)) break;
                    }
                }
                for (int h = walk ? 1 : 0; h <= 2; h++) // a flat jump only across a gap
                    for (int d = h == 0 ? 2 : 1; d <= (h == 0 ? 6 : 3); d++)
                    {
                        int jx = Cell(x + dir * d + 0.5f);
                        if (StandCell(jx, y + h) && ClearArc(x, y, dir, d, h)) yield return (jx, y + h);
                    }
            }
        }

        // ------------------------------------------------------------------ helpers

        private static bool Grounded(Enemy e) => e.Grounded;

        /// <summary>The nearest brute standing above the player within 8 tiles (on Loop01, on the stairs up).</summary>
        private Enemy BruteAbove(RedlineSim sim)
        {
            var p = sim.Player;
            Enemy best = null;
            float bestD = 8f;
            foreach (var e in sim.Enemies)
            {
                if (!e.Alive || e.Kind != EnemyKind.Brute || e.Y - Level(p) <= 1f) continue;
                float d = MathF.Abs(sim.Map.Dx(p.X, e.X));
                if (d >= bestD) continue;
                bestD = d;
                best = e;
            }
            return best;
        }

        /// <summary>The level the player stands on; mid-jump, the one it jumped from (so decisions do not flip in the air).</summary>
        private float Level(Player p) => p.Grounded ? p.Y : _groundY;

        private Enemy Nearest(RedlineSim sim, EnemyKind kind, bool sameLevelOnly)
        {
            var p = sim.Player;
            // Mid-jump the player is still on the level it jumped from (or the decision flips in the air).
            float level = Level(p);
            Enemy best = null;
            float bestD = float.MaxValue;
            foreach (var e in sim.Enemies)
            {
                if (!e.Alive || e.Kind != kind) continue;
                if (sameLevelOnly && MathF.Abs(e.Y - level) > 1.5f) continue;
                float d = MathF.Abs(sim.Map.Dx(p.X, e.X));
                if (d >= bestD) continue;
                bestD = d;
                best = e;
            }
            return best;
        }

        /// <summary>Simulates a jump-and-hook from every floor spot, both directions; keeps those reaching tier 3.</summary>
        private void MeasureLaunches(RedlineConfig cfg, TileMap map)
        {
            var probe = new RedlineConfig();
            foreach (var f in typeof(RedlineConfig).GetFields(BindingFlags.Public | BindingFlags.Instance))
                f.SetValue(probe, f.GetValue(cfg));
            probe.firstWaveDelay = 1e6f;

            foreach (int dir in new[] { 1, -1 })
            {
                for (float x0 = 2f; x0 <= map.Width - 2f; x0 += 0.5f)
                {
                    for (int y = 1; y < map.Height - 1; y++)
                    {
                        if (!Runway(map, x0, y, dir, cfg)) continue;
                        var l = Probe(probe, map, x0, y, dir);
                        if (l.Speed >= cfg.tier3Speed + 0.5f && l.Reach.Count > 0) _launches.Add(l);
                    }
                }
            }
        }

        /// <summary>True when a player can stand at (x, y): solid floor under both feet' span, room above.</summary>
        private static bool Standable(TileMap map, float x, int y, float width, float height)
        {
            float half = width * 0.5f;
            return map.OverlapsSolid(x - half, y - 0.5f, x + half, y - 0.01f) &&
                   !map.OverlapsSolid(x - half, y + 0.01f, x + half, y + height);
        }

        /// <summary>True when the run-up to x0 (Runup tiles behind it, plus a step) is floor at level y.</summary>
        private static bool Runway(TileMap map, float x0, int y, int dir, RedlineConfig cfg)
        {
            // Floor from the run-up to the take-off, and no spike within a hop's look-ahead of it.
            for (float back = -2f; back <= Runup + 0.5f; back += 0.25f)
            {
                float x = map.WrapsX ? Wrap(map, x0 - dir * back) : x0 - dir * back;
                if (back >= -0.5f && !Standable(map, x, y, cfg.playerWidth, cfg.playerHeight)) return false;
                if (map.OverlapsSpike(x - cfg.playerWidth * 0.5f, y, x + cfg.playerWidth * 0.5f, y + cfg.playerHeight)) return false;
            }
            return true;
        }

        private static float Wrap(TileMap map, float x) => ((x % map.Width) + map.Width) % map.Width;

        private static Launch Probe(RedlineConfig cfg, TileMap map, float x0, float y0, int dir)
        {
            var sim = new RedlineSim(cfg, map);
            var p = sim.Player;
            p.X = x0;
            p.Y = y0;
            p.Vx = dir * cfg.runSpeed;
            p.Facing = dir;
            p.RefreshContacts(map);
            var result = new Launch { X = x0, Y = y0, Dir = dir, Reach = new List<float[]>() };
            if (!p.Grounded) return result;

            var input = new InputFrame { MoveX = dir, JumpPressed = true, JumpHeld = true };
            bool fired = false;
            float last = 0f;
            for (int i = 0; i < 150; i++)
            {
                if (!fired && !p.Grounded && p.Vy <= 0f)
                {
                    input.HookPressed = true;
                    fired = true;
                }
                input.HookHeld = fired;
                sim.Step(input, RedlineSim.FixedDt);
                input.JumpPressed = false;
                input.HookPressed = false;
                if (!fired) continue;
                if (!p.Hooked) break;
                float speed = MathF.Abs(p.Vx);
                if (speed < last - 0.05f && last >= cfg.tier3Speed) break;
                if (speed > result.Speed)
                {
                    result.Speed = speed;
                    result.ReleaseX = p.X;
                }
                last = MathF.Max(last, speed);
            }
            if (!fired || result.Speed < cfg.tier3Speed) return result;

            // The flight after release, as the Flying plan flies it: strike boxes while still at tier 3.
            var fly = new InputFrame { MoveX = dir };
            for (int i = 0; i < 120 && sim.PlayerTier >= 3; i++)
            {
                sim.GetStrikeBox(out float minX, out float minY, out float maxX, out float maxY);
                result.Reach.Add(new[] { minX, minY, maxX, maxY, p.MinX, p.Y, p.MaxX, p.MaxY });
                sim.Step(fly, RedlineSim.FixedDt);
            }
            return result;
        }
    }
}
