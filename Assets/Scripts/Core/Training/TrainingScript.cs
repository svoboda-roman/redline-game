using System;

namespace Redline.Core
{
    /// <summary>Where a training step is. Transitions: Active -> Success (goal met or skipped) -> Active (next step) ... -> Complete.</summary>
    public enum TrainingPhase
    {
        /// <summary>The step's card is up and the script waits for the player to do the thing.</summary>
        Active,
        /// <summary>The success flash; the next step starts after successTime.</summary>
        Success,
        /// <summary>Every step is done. The sim keeps running so the player can still move.</summary>
        Complete
    }

    /// <summary>
    /// Drives the training level: runs the sim, shows one step at a time (the game reads
    /// <see cref="Current"/>), checks the step's goal over the sim's state and events, counts failed
    /// attempts (a hit, a fall out of the station, enemies or orbs gone without the goal, a jump or
    /// fling that falls short), shows the tip after enough of them or after a long time, respawns
    /// the player at the station checkpoint when they leave it, and skips a step while {RESTART} is
    /// held for skipHoldTime. Deterministic; allocation-free per step except when enemies spawn.
    /// </summary>
    /// <example>
    /// <code>
    /// var sim = new RedlineSim(cfg, TileMap.Parse(trainingTxt));
    /// var script = new TrainingScript(sim, TrainingData.Parse(trainingJson));
    /// script.Step(input, RedlineSim.FixedDt); // instead of sim.Step
    /// if (script.Phase == TrainingPhase.Complete) { /* back to the menu */ }
    /// </code>
    /// </example>
    /// <remarks>Single-thread only. The sim must be a training level (<see cref="RedlineSim.Training"/>).</remarks>
    public sealed class TrainingScript
    {
        private readonly RedlineSim _sim;
        private readonly Enemy[] _spawned;
        private int _spawnedCount;

        // Per-step progress, reset by StartStep.
        private float _startX;
        private int _slides, _wallJumps, _kills, _reflects;
        private int _orbMask, _pickedMask;
        private bool _jumpInAir, _jumpReached, _flinging;
        private float _hookTimeSeen;
        private float _skipHeld;

        /// <summary>The sim the script runs.</summary>
        public RedlineSim Sim => _sim;
        /// <summary>The steps and settings.</summary>
        public TrainingData Data { get; }
        /// <summary>0-based index of the current step (StepCount once complete).</summary>
        public int StepIndex { get; private set; }
        /// <summary>Number of steps.</summary>
        public int StepCount => Data.Steps.Count;
        /// <summary>The current step, or null once complete.</summary>
        public TrainingStep Current => StepIndex < StepCount ? Data.Steps[StepIndex] : null;
        /// <summary>Where the current step is.</summary>
        public TrainingPhase Phase { get; private set; }
        /// <summary>Seconds in the current phase.</summary>
        public float PhaseTime { get; private set; }
        /// <summary>Seconds the current step has been active.</summary>
        public float StepTime { get; private set; }
        /// <summary>Failed attempts on the current step.</summary>
        public int Attempts { get; private set; }
        /// <summary>True when the current step's tip line should show.</summary>
        public bool TipVisible { get; private set; }
        /// <summary>True when the step that just finished was skipped rather than done.</summary>
        public bool Skipped { get; private set; }
        /// <summary>How far the skip hold is (0-1).</summary>
        public float SkipProgress => Math.Min(1f, _skipHeld / Data.Settings.skipHoldTime);
        /// <summary>Times the player was put back at a checkpoint (for views and tests).</summary>
        public int Respawns { get; private set; }
        /// <summary>World x of the current step's marker line, or NaN when it has none.</summary>
        public float MarkerX => Current != null && Current.MarkerX >= 0 ? Current.MarkerX + 0.5f : float.NaN;
        /// <summary>Increments whenever the step, the phase or the tip changes (views redraw on a change).</summary>
        public int Revision { get; private set; }

        /// <summary>Starts the first step. Throws ArgumentException when the sim is not a training level.</summary>
        public TrainingScript(RedlineSim sim, TrainingData data)
        {
            _sim = sim ?? throw new ArgumentNullException(nameof(sim));
            Data = data ?? throw new ArgumentNullException(nameof(data));
            if (!sim.Training) throw new ArgumentException("TrainingScript needs a ';training' level", nameof(sim));
            int most = 1;
            foreach (var s in data.Steps) most = Math.Max(most, s.Enemies.Length);
            _spawned = new Enemy[most];
            StartStep(0);
        }

        /// <summary>
        /// Advances the sim by dt (RestartPressed never reaches it: holding Restart skips the step)
        /// and then the script.
        /// </summary>
        public void Step(InputFrame input, float dt)
        {
            input.RestartPressed = false;
            _sim.Step(input, dt);
            switch (Phase)
            {
                case TrainingPhase.Complete:
                    return;
                case TrainingPhase.Success:
                    PhaseTime += dt;
                    if (PhaseTime >= Data.Settings.successTime) Advance();
                    return;
            }
            PhaseTime += dt;
            StepTime += dt;
            if (input.RestartHeld)
            {
                _skipHeld += dt;
                if (_skipHeld >= Data.Settings.skipHoldTime)
                {
                    SkipStep();
                    return;
                }
            }
            else
            {
                _skipHeld = 0f;
            }

            bool hurt = false, exploded = false, released = false;
            ScanEvents(ref hurt, ref exploded, ref released);
            if (hurt) Fail();
            var step = Current;
            var p = _sim.Player;
            if (!step.Station.Contains(p.Center))
            {
                Respawn();
                Fail();
            }
            if (GoalMet(step, p, hurt, exploded, released))
            {
                Succeed(false);
                return;
            }
            RefillActors(step);
            bool tip = Attempts >= Data.Settings.tipAfterAttempts || StepTime >= Data.Settings.tipAfterSeconds;
            if (tip != TipVisible)
            {
                TipVisible = tip;
                Revision++;
            }
        }

        /// <summary>Ends the current step as skipped (the hold on {RESTART} calls this).</summary>
        public void SkipStep()
        {
            if (Phase != TrainingPhase.Active) return;
            Succeed(true);
        }

        private void ScanEvents(ref bool hurt, ref bool exploded, ref bool released)
        {
            var step = Current;
            var events = _sim.Events;
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                switch (e.Type)
                {
                    case SimEventType.Jump: _jumpInAir = true; break;
                    case SimEventType.Slide: _slides++; break;
                    case SimEventType.WallJump: _wallJumps++; break;
                    case SimEventType.Kill:
                        if (e.Detail == (int)step.Target) _kills++;
                        break;
                    case SimEventType.ProjectileReflect: _reflects++; break;
                    case SimEventType.OrbPickup: _pickedMask |= 1 << e.Detail; break;
                    case SimEventType.PlayerHurt: hurt = true; break;
                    case SimEventType.Explode: exploded = true; break;
                    case SimEventType.HookRelease: released = true; break;
                }
            }
        }

        private bool GoalMet(TrainingStep step, Player p, bool hurt, bool exploded, bool released)
        {
            var cfg = _sim.Config;
            float baseY = step.Station.CheckpointY;
            switch (step.Goal)
            {
                case TrainingGoal.Move:
                    return MathF.Abs(_sim.Map.Dx(_startX, p.X)) >= step.Amount;
                case TrainingGoal.Jump:
                    if (_jumpInAir && p.Y >= baseY + step.Amount) _jumpReached = true;
                    if (_jumpInAir && p.Grounded && p.Vy <= 0f)
                    {
                        if (_jumpReached) return true;
                        _jumpInAir = false;
                        Fail(); // landed from a jump that was not high enough
                    }
                    return false;
                case TrainingGoal.Slide:
                    return _slides >= step.Amount;
                case TrainingGoal.Kill:
                    return _kills >= step.Amount;
                case TrainingGoal.Chain:
                    return p.Chain >= step.Amount;
                case TrainingGoal.Climb:
                    return _wallJumps > 0 && p.Y >= baseY + step.Amount;
                case TrainingGoal.Hook:
                    if (p.Hooked) _hookTimeSeen = p.HookTime;
                    if (p.Hooked && p.HookTime >= step.Amount) return true;
                    if (released && !p.Hooked)
                    {
                        if (_hookTimeSeen > 0f) Fail(); // let go too early
                        _hookTimeSeen = 0f;
                    }
                    return false;
                case TrainingGoal.Fling:
                    if (p.Hooked)
                    {
                        _hookTimeSeen = p.HookTime;
                        _flinging = false;
                        return false;
                    }
                    if (released && !hurt && !p.Grounded && _hookTimeSeen >= cfg.hookReleaseMinTime) _flinging = true;
                    _hookTimeSeen = 0f;
                    if (!_flinging) return false;
                    if (!p.Grounded && MathF.Abs(p.Vx) > cfg.maxSpeed) return true;
                    if (!p.Grounded) return false;
                    _flinging = false;
                    int dir = step.MarkerX + 0.5f >= step.Station.Checkpoint.X ? 1 : -1;
                    if ((p.X - (step.MarkerX + 0.5f)) * dir >= 0f) return true;
                    Fail(); // landed short of the marker
                    return false;
                case TrainingGoal.Tier:
                    return _sim.PlayerTier >= step.Amount;
                case TrainingGoal.Reflect:
                    return _reflects >= step.Amount;
                case TrainingGoal.Survive:
                    return _kills > 0 || (exploded && !hurt);
                case TrainingGoal.Orb:
                    return _orbMask != 0 && (_pickedMask & _orbMask) == _orbMask;
                default:
                    return false;
            }
        }

        /// <summary>Enemies all gone (or orbs all faded) without the goal: count an attempt and place them again.</summary>
        private void RefillActors(TrainingStep step)
        {
            if (_spawnedCount > 0)
            {
                bool anyAlive = false;
                for (int i = 0; i < _spawnedCount; i++)
                    if (_spawned[i].Alive) anyAlive = true;
                if (!anyAlive)
                {
                    Fail();
                    _sim.ClearActors();
                    SpawnEnemies(step);
                    SpawnOrbs(step, _pickedMask);
                    return;
                }
            }
            if (step.Orbs.Length > 0 && _sim.Orbs.Count == 0)
            {
                Fail();
                SpawnOrbs(step, _pickedMask);
            }
        }

        private void StartStep(int index)
        {
            StepIndex = index;
            Phase = TrainingPhase.Active;
            PhaseTime = 0f;
            StepTime = 0f;
            Attempts = 0;
            TipVisible = false;
            _skipHeld = 0f;
            _slides = _wallJumps = _kills = _reflects = 0;
            _pickedMask = 0;
            _jumpInAir = _jumpReached = _flinging = false;
            _hookTimeSeen = 0f;
            _sim.ClearActors();
            var step = Current;
            var p = _sim.Player;
            if (!step.Station.Contains(p.Center)) Respawn();
            else p.Hp = _sim.Config.playerMaxHp;
            _startX = p.X;
            SpawnEnemies(step);
            _orbMask = 0;
            foreach (var o in step.Orbs) _orbMask |= 1 << o.Kind;
            SpawnOrbs(step, 0);
            Revision++;
        }

        private void SpawnEnemies(TrainingStep step)
        {
            _spawnedCount = 0;
            var cp = step.Station.Checkpoint;
            foreach (var s in step.Enemies)
            {
                var e = _sim.SpawnEnemy((EnemyKind)s.Kind, s.Position);
                e.StunTimer = Data.Settings.spawnGrace; // still while the card is read
                e.Charges = e.Kind == EnemyKind.Brute;
                e.Facing = cp.X >= e.X ? 1 : -1;
                e.ShieldDir = e.Facing;
                _spawned[_spawnedCount++] = e;
            }
        }

        private void SpawnOrbs(TrainingStep step, int skipMask)
        {
            foreach (var o in step.Orbs)
                if ((skipMask & (1 << o.Kind)) == 0) _sim.SpawnOrb((OrbKind)o.Kind, o.Position);
        }

        private void Respawn()
        {
            _sim.RespawnPlayer(Current.Station.Checkpoint);
            _jumpInAir = _flinging = false;
            _hookTimeSeen = 0f;
            Respawns++;
        }

        private void Fail() => Attempts++;

        private void Succeed(bool skipped)
        {
            Skipped = skipped;
            Phase = TrainingPhase.Success;
            PhaseTime = 0f;
            _skipHeld = 0f;
            _sim.ClearActors();
            _spawnedCount = 0;
            Revision++;
        }

        private void Advance()
        {
            if (StepIndex + 1 >= StepCount)
            {
                StepIndex = StepCount;
                Phase = TrainingPhase.Complete;
                PhaseTime = 0f;
                Revision++;
                return;
            }
            StartStep(StepIndex + 1);
        }
    }
}
