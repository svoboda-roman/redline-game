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
            public int Dir;
            public float Speed;
            public float ReleaseX;
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

        /// <summary>Hook launches the arena offers that reach tier 3 (measured at construction).</summary>
        public int LaunchCount => _launches.Count;

        /// <summary>Current plan step, for diagnostics.</summary>
        public string PlanName => _plan.ToString();

        /// <summary>Hook launches this player has completed (released at speed toward a brute).</summary>
        public int HookLaunches { get; private set; }

        public ScriptedPlayer(RedlineConfig cfg, TileMap map, bool skilled)
        {
            _skilled = skilled;
            if (skilled) MeasureLaunches(cfg, map);
        }

        /// <summary>The input for the next step.</summary>
        public InputFrame Next(RedlineSim sim)
        {
            var input = Decide(sim);
            if (_skilled && _plan == Plan.None) AvoidArmor(sim, ref input);
            HopWalls(sim, ref input);
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
            bool spikes = false;
            for (float look = 0.6f; look <= 1.8f; look += 0.4f)
                if (sim.Map.Get((int)MathF.Floor(p.X + dir * look), (int)MathF.Floor(p.Y + 0.01f)) == Tile.Spike) spikes = true;
            if (p.WallDir != dir && !spikes) return;
            input.JumpPressed = true;
            input.JumpHeld = true;
            _jumpHold = 20;
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
            Enemy drone = Nearest(sim, EnemyKind.Drone, sameLevelOnly: false);
            Enemy brute = Nearest(sim, EnemyKind.Brute, sameLevelOnly: false);

            if (grunt != null) return Approach(sim, input, grunt.X);
            if (brute != null && brute.Mode == BruteMode.Crashed) return Approach(sim, input, brute.X);
            // Enemies guarding posts above: walk close enough to wake them, so they come down.
            var anyGrunt = Nearest(sim, EnemyKind.Grunt, sameLevelOnly: false);
            if (anyGrunt != null) return Wait(sim, input, anyGrunt);
            if (brute != null && brute.Y - p.Y > 1f) return Wait(sim, input, brute);
            if (brute != null && Grounded(brute) && TryStartLaunch(sim, brute)) return ContinuePlan(sim, input);
            if (brute != null && brute.Charges && !sim.Map.WrapsX) return LureToWall(sim, input, brute);
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
                if (MathF.Sign(map.Dx(l.X, brute.X)) != l.Dir) continue;
                float ahead = map.Dx(l.ReleaseX, brute.X) * l.Dir;
                if (ahead < 2.5f || ahead > 12f) continue;
                float runupX = l.X - l.Dir * Runup;
                if (!map.WrapsX && (runupX < 1.5f || runupX > map.Width - 1.5f)) continue;
                float cost = MathF.Abs(map.Dx(p.X, runupX));
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

        private InputFrame ContinuePlan(RedlineSim sim, InputFrame input)
        {
            var p = sim.Player;
            _planTime += RedlineSim.FixedDt;
            int dir = _launch.Dir;
            if (_planTarget == null || !_planTarget.Alive || _planTime > 8f) return Abort(sim, input);

            switch (_plan)
            {
                case Plan.ToRunup:
                    float toRunup = sim.Map.Dx(p.X, _launch.X - dir * Runup);
                    input.MoveX = MathF.Sign(toRunup);
                    if (MathF.Abs(toRunup) < 0.5f) _plan = Plan.RunIn;
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
                    if (AnyKillableInReach(sim))
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
                    if (AnyKillableInReach(sim)) input.AttackPressed = true;
                    return input;
            }
        }

        private InputFrame Abort(RedlineSim sim, InputFrame input)
        {
            _plan = Plan.None;
            return Opportunist(sim, input);
        }

        // ------------------------------------------------------------------ helpers

        private static bool Grounded(Enemy e) => e.Grounded;

        private static Enemy Nearest(RedlineSim sim, EnemyKind kind, bool sameLevelOnly)
        {
            var p = sim.Player;
            Enemy best = null;
            float bestD = float.MaxValue;
            foreach (var e in sim.Enemies)
            {
                if (!e.Alive || e.Kind != kind) continue;
                if (sameLevelOnly && MathF.Abs(e.Y - p.Y) > 1.5f) continue;
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
                    var l = Probe(probe, map, x0, dir);
                    if (l.Speed >= cfg.tier3Speed + 0.5f) _launches.Add(l);
                }
            }
        }

        private static Launch Probe(RedlineConfig cfg, TileMap map, float x0, int dir)
        {
            var sim = new RedlineSim(cfg, map);
            var p = sim.Player;
            p.X = x0;
            p.Y = 1f;
            p.Vx = dir * cfg.runSpeed;
            p.Facing = dir;
            p.RefreshContacts(map);
            var result = new Launch { X = x0, Dir = dir };
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
            return result;
        }
    }
}
