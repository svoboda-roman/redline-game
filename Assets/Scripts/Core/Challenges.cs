using System;
using System.Collections.Generic;

namespace Redline.Core
{
    /// <summary>Run challenges: three random goals per run, each paying bonus points once when done.</summary>
    public enum ChallengeKind
    {
        Kills,        // kill N enemies
        Combo,        // reach a chain of N
        FastKills,    // N kills at speed tier 3
        HookKills,    // N kills while hanging on the hook
        AirKills,     // N kills in the air
        Waves,        // clear N waves
        Clean,        // clear N waves in a row without taking damage
        Brutes,       // kill N brutes
    }

    /// <summary>Points multiplier on kills for the current chain.</summary>
    public static class Combo
    {
        /// <summary>x2 from a 3-kill chain, x3 from 6, x4 from 9, x5 from 12.</summary>
        public static int Multiplier(int chain) => 1 + Math.Min(4, Math.Max(0, chain) / 3);
    }

    /// <summary>One goal of the run and its progress.</summary>
    public sealed class Challenge
    {
        public ChallengeKind Kind;
        public int Target;
        public int Reward;
        public int Progress;
        public bool Done => Progress >= Target;

        public string Text
        {
            get
            {
                switch (Kind)
                {
                    case ChallengeKind.Kills: return "KILL " + Target + " ENEMIES";
                    case ChallengeKind.Combo: return "REACH A x" + Target + " CHAIN";
                    case ChallengeKind.FastKills: return Target + " KILLS AT SPEED 3";
                    case ChallengeKind.HookKills: return Target + " KILLS ON THE HOOK";
                    case ChallengeKind.AirKills: return Target + " KILLS IN THE AIR";
                    case ChallengeKind.Waves: return "CLEAR " + Target + " WAVES";
                    case ChallengeKind.Clean: return Target + " WAVES WITHOUT A HIT";
                    case ChallengeKind.Brutes: return "KILL " + Target + " BRUTES";
                    default: return Kind.ToString();
                }
            }
        }
    }

    /// <summary>Tracks the run's challenges; feed it the run's moments, read <see cref="Completed"/> for toasts.</summary>
    public sealed class ChallengeTracker
    {
        private static readonly (ChallengeKind kind, int target, int reward)[] Pool =
        {
            (ChallengeKind.Kills, 30, 40), (ChallengeKind.Combo, 6, 40), (ChallengeKind.FastKills, 8, 50),
            (ChallengeKind.HookKills, 4, 50), (ChallengeKind.AirKills, 10, 40), (ChallengeKind.Waves, 8, 50),
            (ChallengeKind.Clean, 3, 60), (ChallengeKind.Brutes, 2, 40),
        };

        public readonly List<Challenge> Active = new List<Challenge>(3);
        /// <summary>Challenges finished since the last <see cref="TakeCompleted"/> (for the HUD callout).</summary>
        private readonly List<Challenge> _completed = new List<Challenge>(3);
        private int _cleanStreak;

        /// <summary>Picks three different challenges for a new run.</summary>
        public void NewRun(Random rng)
        {
            Active.Clear();
            _completed.Clear();
            _cleanStreak = 0;
            var pool = new List<(ChallengeKind, int, int)>(Pool);
            for (int i = 0; i < 3 && pool.Count > 0; i++)
            {
                int j = rng.Next(pool.Count);
                var (kind, target, reward) = pool[j];
                pool.RemoveAt(j);
                Active.Add(new Challenge { Kind = kind, Target = target, Reward = reward });
            }
        }

        public void OnKill(int tier, bool airborne, bool hooked, EnemyKind kind)
        {
            Add(ChallengeKind.Kills, 1);
            if (tier >= 3) Add(ChallengeKind.FastKills, 1);
            if (hooked) Add(ChallengeKind.HookKills, 1);
            if (airborne) Add(ChallengeKind.AirKills, 1);
            if (kind == EnemyKind.Brute) Add(ChallengeKind.Brutes, 1);
        }

        public void OnChain(int chain) => Max(ChallengeKind.Combo, chain);

        public void OnWaveCleared(bool tookDamage)
        {
            Add(ChallengeKind.Waves, 1);
            _cleanStreak = tookDamage ? 0 : _cleanStreak + 1;
            Max(ChallengeKind.Clean, _cleanStreak);
        }

        /// <summary>Returns and forgets the challenges completed since the last call.</summary>
        public List<Challenge> TakeCompleted()
        {
            var list = new List<Challenge>(_completed);
            _completed.Clear();
            return list;
        }

        private void Add(ChallengeKind kind, int n)
        {
            foreach (var c in Active)
                if (c.Kind == kind && !c.Done) { c.Progress = Math.Min(c.Target, c.Progress + n); if (c.Done) _completed.Add(c); }
        }

        private void Max(ChallengeKind kind, int value)
        {
            foreach (var c in Active)
                if (c.Kind == kind && !c.Done && value > c.Progress) { c.Progress = Math.Min(c.Target, value); if (c.Done) _completed.Add(c); }
        }
    }
}
