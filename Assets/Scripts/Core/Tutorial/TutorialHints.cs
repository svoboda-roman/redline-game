using System;
using System.Collections.Generic;

namespace Redline.Core
{
    /// <summary>
    /// Decides which tutorial hint is showing. Engine-free; the Unity overlay only draws
    /// <see cref="Active"/>. Call <see cref="Step"/> once after every sim step (sim time, so a paused
    /// game freezes the hint) and <see cref="BeginRun"/> when a new run starts.
    /// <para>Triggers:</para>
    /// <list type="bullet">
    /// <item>Zone hints: the player's feet are over a ';hint' zone of the current level (first zone in file order wins).</item>
    /// <item>Enemy cards: an alive enemy of a kind not yet shown is within sightRangeX/Y (horizontal distance around the ring)
    /// and, with requireLineOfSight, no solid cell lies between the player's centre and the enemy's.</item>
    /// <item>Orb cards: the first <see cref="SimEventType.OrbPickup"/> of each orb kind.</item>
    /// </list>
    /// <para>Policy:</para>
    /// <list type="bullet">
    /// <item>One hint at a time. A hint counts as seen the moment it starts showing (scope Save: in
    /// <see cref="Progress"/>; scope Run: until the next <see cref="BeginRun"/>). Ids missing from the catalog never show.</item>
    /// <item>Every hint stays up at least minDisplayTime. Cards end after cardTime; zone hints end once the
    /// player has left the zone, or after zoneMaxTime.</item>
    /// <item>Enemy and orb cards wait in a FIFO queue of queueCapacity behind the active hint, and take over
    /// as soon as it has shown for minDisplayTime. A request that finds the queue full is dropped and not
    /// marked seen, so the next sighting or pickup asks again.</item>
    /// <item>Zone hints are never queued: one shows only when nothing else is up or waiting and the player
    /// is still in its zone; missed, it can show on a later pass.</item>
    /// <item>Disabled (<c>enabled = false</c>), or after victory / game over, nothing new triggers; disabling
    /// also clears the active hint and the queue without marking anything seen.</item>
    /// </list>
    /// Step allocates nothing once the sets have grown.
    /// </summary>
    public sealed class TutorialHints
    {
        private readonly HintCatalog _catalog;
        private readonly TutorialSettings _cfg;
        private readonly HashSet<string> _runSeen = new HashSet<string>();
        private readonly List<HintEntry> _queue;

        /// <summary>The once-per-save seen-set (persist it with the settings save).</summary>
        public TutorialProgress Progress { get; private set; }

        /// <summary>The hint showing now, or null.</summary>
        public HintEntry Active { get; private set; }

        /// <summary>True when <see cref="Active"/> came from a level zone rather than a card.</summary>
        public bool ActiveIsZone { get; private set; }

        /// <summary>Seconds (sim time) the active hint has been up.</summary>
        public float ActiveTime { get; private set; }

        /// <summary>Increments every time <see cref="Active"/> changes (presentation caches text per version).</summary>
        public int Version { get; private set; }

        /// <summary>Cards waiting behind the active hint.</summary>
        public int QueuedCount => _queue.Count;

        /// <summary>Creates the director. A null progress starts an empty seen-set.</summary>
        public TutorialHints(HintCatalog catalog, TutorialProgress progress = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _cfg = catalog.Settings;
            _queue = new List<HintEntry>(Math.Max(1, _cfg.queueCapacity));
            Progress = progress ?? new TutorialProgress();
        }

        /// <summary>Swaps in a loaded seen-set (e.g. after the settings save is read).</summary>
        public void SetProgress(TutorialProgress progress) => Progress = progress ?? new TutorialProgress();

        /// <summary>Starts a new run: run-scope hints may show again; the active hint and queue are cleared.</summary>
        public void BeginRun()
        {
            _runSeen.Clear();
            Clear();
        }

        /// <summary>Removes the active hint and the queue (nothing is un-seen).</summary>
        public void Clear()
        {
            _queue.Clear();
            SetActive(null, false);
        }

        /// <summary>True when the hint has already shown (per save or, for run-scope hints, this run).</summary>
        public bool WasShown(HintEntry entry) =>
            entry.Scope == HintScope.Run ? _runSeen.Contains(entry.Id) : Progress.Contains(entry.Id);

        /// <summary>Advances timers by dt, reads this step's sim events and state, and picks the active hint.</summary>
        public void Step(RedlineSim sim, float dt, bool enabled)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            if (!enabled)
            {
                if (Active != null || _queue.Count > 0) Clear();
                return;
            }
            bool live = sim.State == SimState.Fighting || sim.State == SimState.WaveBreak;
            HintEntry zone = live ? ZoneHint(sim) : null;

            if (Active != null)
            {
                ActiveTime += dt;
                bool pastMin = ActiveTime >= _cfg.minDisplayTime;
                bool done = ActiveIsZone
                    ? pastMin && (zone != Active || ActiveTime >= _cfg.zoneMaxTime)
                    : ActiveTime >= _cfg.cardTime;
                if (done || (pastMin && _queue.Count > 0)) SetActive(null, false);
            }
            if (Active == null && _queue.Count > 0)
            {
                var next = _queue[0];
                _queue.RemoveAt(0);
                Show(next, false);
            }

            if (live)
            {
                var events = sim.Events;
                for (int i = 0; i < events.Count; i++)
                    if (events[i].Type == SimEventType.OrbPickup) Request(HintIds.Orb((OrbKind)events[i].Detail));
                ScanEnemies(sim);
            }
            if (Active == null && zone != null && !WasShown(zone)) Show(zone, true);
        }

        private void ScanEnemies(RedlineSim sim)
        {
            var enemies = sim.Enemies;
            if (enemies.Count == 0) return;
            var eye = sim.Player.Center;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (!e.Alive || !_catalog.TryGet(HintIds.Enemy(e.Kind), out var entry) || IsPending(entry)) continue;
                var c = e.Center;
                float dx = sim.Map.Dx(eye.X, c.X);
                float dy = c.Y - eye.Y;
                if (MathF.Abs(dx) > _cfg.sightRangeX || MathF.Abs(dy) > _cfg.sightRangeY) continue;
                if (_cfg.requireLineOfSight)
                {
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist > 1e-3f && sim.Map.Raycast(eye, new Vec2(dx / dist, dy / dist), dist, out _)) continue;
                }
                Enqueue(entry);
            }
        }

        private HintEntry ZoneHint(RedlineSim sim)
        {
            var zones = sim.Map.Hints;
            if (zones.Count == 0) return null;
            float x = sim.Map.WrapX(sim.Player.X);
            for (int i = 0; i < zones.Count; i++)
                if (zones[i].Contains(x) && _catalog.TryGet(zones[i].Id, out var entry)) return entry;
            return null;
        }

        private void Request(string id)
        {
            if (_catalog.TryGet(id, out var entry) && !IsPending(entry)) Enqueue(entry);
        }

        /// <summary>Shown already, showing now, or waiting.</summary>
        private bool IsPending(HintEntry entry) => entry == Active || WasShown(entry) || _queue.Contains(entry);

        private void Enqueue(HintEntry entry)
        {
            if (Active == null) Show(entry, false);
            else if (_queue.Count < _cfg.queueCapacity) _queue.Add(entry);
        }

        private void Show(HintEntry entry, bool zone)
        {
            if (entry.Scope == HintScope.Run) _runSeen.Add(entry.Id);
            else Progress.Add(entry.Id);
            SetActive(entry, zone);
        }

        private void SetActive(HintEntry entry, bool zone)
        {
            if (entry == null && Active == null) return;
            Active = entry;
            ActiveIsZone = entry != null && zone;
            ActiveTime = 0f;
            Version++;
        }
    }
}
