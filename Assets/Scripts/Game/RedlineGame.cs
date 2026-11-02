using System;
using System.Collections.Generic;
using Redline.Core;
using UnityEngine;

namespace Redline.Game
{
    /// <summary>
    /// Owns the run: loads config and arena from Resources, steps <see cref="RedlineSim"/> at a
    /// fixed 60 Hz, and draws it with generated neon sprites plus feedback (afterimages, sparks,
    /// screen shake, hit-stop). All rules live in Redline.Core; this class only presents them.
    /// </summary>
    /// <remarks>Created by <see cref="RedlineBootstrap"/>. Main-thread only.</remarks>
    public sealed class RedlineGame : MonoBehaviour
    {
        private const int AfterimageCount = 24;
        private const int SparkCount = 96;
        private const float AfterimageLife = 0.25f;
        private const float SparkLife = 0.45f;
        /// <summary>World tiles kept free above the arena for the HUD panels.</summary>
        private const float HudHeadroom = 4.8f;

        private static readonly Color Background = new Color(0.03f, 0.03f, 0.07f);
        private static readonly Color TileColor = new Color(0.10f, 0.12f, 0.24f);
        private static readonly Color TileEdge = new Color(0.20f, 0.55f, 0.95f);
        private static readonly Color SpikeColor = new Color(1f, 0.22f, 0.42f);
        private static readonly Color GruntColor = new Color(1f, 0.55f, 0.15f);
        private static readonly Color BruteArmored = new Color(0.45f, 0.55f, 0.72f);
        private static readonly Color BruteBreakable = new Color(1f, 0.18f, 0.25f);
        private static readonly Color RopeColor = new Color(0.85f, 0.95f, 1f);

        /// <summary>Colour of each speed tier (index = tier - 1). Shared with the HUD.</summary>
        public static readonly Color[] TierColors =
        {
            new Color(0.25f, 0.85f, 1f),
            new Color(1f, 0.3f, 0.9f),
            new Color(1f, 0.95f, 0.45f)
        };

        private readonly RedlineInput _input = new RedlineInput();
        private readonly List<SpriteRenderer> _enemyViews = new List<SpriteRenderer>(32);

        private Camera _camera;
        private Transform _world;
        private SpriteRenderer _player;
        private SpriteRenderer _playerEye;
        private SpriteRenderer _strike;
        private SpriteRenderer _rope;
        private SpriteRenderer[] _afterimages;
        private float[] _afterimageAge;
        private int _nextAfterimage;
        private float _afterimageTimer;
        private SpriteRenderer[] _sparks;
        private Vector2[] _sparkVelocity;
        private float[] _sparkAge;
        private int _nextSpark;

        // Positions before the latest fixed step, for drawing between steps (render interpolation).
        private readonly List<Vector2> _prevEnemyFeet = new List<Vector2>(32);
        private Vector2 _prevPlayerFeet;
        private int _prevRunId = -1;
        private Vector2 _drawOffset;

        private float _accumulator;
        private float _hitStop;
        private float _shake;
        private Vector3 _cameraHome;

        /// <summary>The running simulation (read by the HUD).</summary>
        public RedlineSim Sim { get; private set; }

        private void Awake()
        {
            Application.targetFrameRate = 120;
            var config = LoadConfig();
            var map = LoadMap();
            Sim = new RedlineSim(config, map);

            _world = new GameObject("World").transform;
            _world.SetParent(transform, false);
            SetupCamera();
            BuildArena(map);
            BuildActors();

            gameObject.AddComponent<RedlineHud>().Init(this);
            if (RedlineCapture.Requested) gameObject.AddComponent<RedlineCapture>().Init(this);
        }

        private void Update()
        {
            _input.Poll();
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (_hitStop > 0f) _hitStop -= dt;
            else _accumulator += dt;

            while (_accumulator >= RedlineSim.FixedDt)
            {
                CapturePrevious();
                Sim.Step(_input.Consume(), RedlineSim.FixedDt);
                HandleEvents();
                _accumulator -= RedlineSim.FixedDt;
            }

            SyncActors();
            UpdateEffects(dt);
            UpdateCamera(dt);
        }

        private void CapturePrevious()
        {
            var p = Sim.Player;
            _prevPlayerFeet = new Vector2(p.X, p.Y);
            _prevRunId = Sim.RunId;
            _prevEnemyFeet.Clear();
            var enemies = Sim.Enemies;
            for (int i = 0; i < enemies.Count; i++) _prevEnemyFeet.Add(new Vector2(enemies[i].X, enemies[i].Y));
        }

        /// <summary>Fraction of the way from the previous fixed step to the current one.</summary>
        private float InterpolationAlpha => Mathf.Clamp01(_accumulator / RedlineSim.FixedDt);

        // ------------------------------------------------------------------ loading

        private static RedlineConfig LoadConfig()
        {
            var config = new RedlineConfig();
            var asset = Resources.Load<TextAsset>("RedlineConfig");
            if (asset == null)
            {
                Debug.LogWarning("Redline: Resources/RedlineConfig.json not found, using built-in defaults.");
                return config;
            }
            try
            {
                JsonUtility.FromJsonOverwrite(asset.text, config);
            }
            catch (ArgumentException ex)
            {
                Debug.LogError("Redline: RedlineConfig.json is not valid JSON, using built-in defaults: " + ex.Message);
                return new RedlineConfig();
            }
            var errors = config.Validate();
            if (errors.Count == 0) return config;
            Debug.LogError("Redline: RedlineConfig.json is invalid, using built-in defaults: " + string.Join("; ", errors));
            return new RedlineConfig();
        }

        private static TileMap LoadMap()
        {
            var asset = Resources.Load<TextAsset>("Arena01");
            if (asset == null) throw new InvalidOperationException("Redline: Resources/Arena01.txt is missing.");
            return TileMap.Parse(asset.text);
        }

        // ------------------------------------------------------------------ building

        private void SetupCamera()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                _camera = go.AddComponent<Camera>();
            }
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Background;
            FitCamera();
        }

        private void FitCamera()
        {
            var map = Sim.Map;
            float aspect = Mathf.Max(0.1f, _camera.aspect);
            // Fit the whole arena, shifted down so the HUD panels sit above it (HudHeadroom tiles).
            _camera.orthographicSize = Mathf.Max(map.Height * 0.5f + HudHeadroom * 0.5f + 0.5f, map.Width * 0.5f / aspect + 0.5f);
            float aboveArena = _camera.orthographicSize - map.Height * 0.5f;
            float shift = Mathf.Max(0f, HudHeadroom - aboveArena);
            _cameraHome = new Vector3(map.Width * 0.5f, map.Height * 0.5f + shift, -10f);
            _camera.transform.position = _cameraHome;
        }

        private void BuildArena(TileMap map)
        {
            var arena = new GameObject("Arena").transform;
            arena.SetParent(_world, false);
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    var tile = map.Get(x, y);
                    if (tile == Tile.Solid)
                    {
                        var sr = SpriteFactory.Create("Tile", arena, SpriteFactory.Square, TileColor, 0);
                        sr.transform.localPosition = new Vector3(x + 0.5f, y + 0.5f, 0f);
                        bool openAbove = y + 1 < map.Height && !map.IsSolid(x, y + 1);
                        if (openAbove)
                        {
                            var edge = SpriteFactory.Create("Edge", arena, SpriteFactory.Square, TileEdge, 1);
                            edge.transform.localPosition = new Vector3(x + 0.5f, y + 0.94f, 0f);
                            edge.transform.localScale = new Vector3(1f, 0.12f, 1f);
                        }
                    }
                    else if (tile == Tile.Spike)
                    {
                        var sr = SpriteFactory.Create("Spike", arena, SpriteFactory.Triangle, SpikeColor, 1);
                        sr.transform.localPosition = new Vector3(x + 0.5f, y + 0.3f, 0f);
                        sr.transform.localScale = new Vector3(0.9f, 0.6f, 1f);
                    }
                }
            }
        }

        private void BuildActors()
        {
            _afterimages = new SpriteRenderer[AfterimageCount];
            _afterimageAge = new float[AfterimageCount];
            for (int i = 0; i < AfterimageCount; i++)
            {
                _afterimages[i] = SpriteFactory.Create("Afterimage", _world, SpriteFactory.Square, Color.clear, 5);
                _afterimages[i].enabled = false;
                _afterimageAge[i] = AfterimageLife;
            }

            _rope = SpriteFactory.Create("Rope", _world, SpriteFactory.Square, RopeColor, 6);
            _strike = SpriteFactory.Create("Strike", _world, SpriteFactory.Square, Color.clear, 7);
            _player = SpriteFactory.Create("Player", _world, SpriteFactory.Square, TierColors[0], 10);
            _playerEye = SpriteFactory.Create("Eye", _world, SpriteFactory.Square, Background, 11);

            _sparks = new SpriteRenderer[SparkCount];
            _sparkVelocity = new Vector2[SparkCount];
            _sparkAge = new float[SparkCount];
            for (int i = 0; i < SparkCount; i++)
            {
                _sparks[i] = SpriteFactory.Create("Spark", _world, SpriteFactory.Square, Color.clear, 12);
                _sparks[i].transform.localScale = new Vector3(0.16f, 0.16f, 1f);
                _sparks[i].enabled = false;
                _sparkAge[i] = SparkLife;
            }
        }

        // ------------------------------------------------------------------ feedback

        private void HandleEvents()
        {
            var events = Sim.Events;
            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                var color = TierColors[Mathf.Clamp(ev.Tier, 1, 3) - 1];
                switch (ev.Type)
                {
                    case SimEventType.Hit:
                        Burst(ev.Position, color, 6 + 3 * ev.Tier, 6f + 3f * ev.Tier);
                        _shake = Mathf.Max(_shake, 0.05f * ev.Tier);
                        break;
                    case SimEventType.Kill:
                        Burst(ev.Position, color, 10 + 6 * ev.Tier, 8f + 4f * ev.Tier);
                        _shake = Mathf.Max(_shake, 0.08f * ev.Tier);
                        _hitStop = Mathf.Max(_hitStop, 0.02f * ev.Tier);
                        break;
                    case SimEventType.Clang:
                        Burst(ev.Position, BruteArmored, 12, 7f);
                        _shake = Mathf.Max(_shake, 0.15f);
                        _hitStop = Mathf.Max(_hitStop, 0.04f);
                        break;
                    case SimEventType.PlayerHurt:
                        Burst(ev.Position, SpikeColor, 14, 8f);
                        _shake = Mathf.Max(_shake, 0.3f);
                        _hitStop = Mathf.Max(_hitStop, 0.05f);
                        break;
                    case SimEventType.HookAttach:
                        Burst(ev.Position, RopeColor, 5, 4f);
                        break;
                    case SimEventType.WallJump:
                    case SimEventType.Slide:
                        Burst(ev.Position, color, 5, 4f);
                        break;
                }
            }
        }

        private void Burst(Vec2 at, Color color, int count, float speed)
        {
            for (int i = 0; i < count; i++)
            {
                int k = _nextSpark;
                _nextSpark = (_nextSpark + 1) % SparkCount;
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float s = speed * UnityEngine.Random.Range(0.4f, 1f);
                _sparkVelocity[k] = new Vector2(Mathf.Cos(angle) * s, Mathf.Sin(angle) * s);
                _sparkAge[k] = 0f;
                _sparks[k].transform.localPosition = new Vector3(at.X, at.Y, 0f);
                _sparks[k].color = color;
                _sparks[k].enabled = true;
            }
        }

        private void UpdateEffects(float dt)
        {
            for (int i = 0; i < SparkCount; i++)
            {
                if (!_sparks[i].enabled) continue;
                _sparkAge[i] += dt;
                if (_sparkAge[i] >= SparkLife)
                {
                    _sparks[i].enabled = false;
                    continue;
                }
                _sparkVelocity[i].y -= 25f * dt;
                var t = _sparks[i].transform;
                t.localPosition += (Vector3)(_sparkVelocity[i] * dt);
                var c = _sparks[i].color;
                c.a = 1f - _sparkAge[i] / SparkLife;
                _sparks[i].color = c;
            }

            for (int i = 0; i < AfterimageCount; i++)
            {
                if (!_afterimages[i].enabled) continue;
                _afterimageAge[i] += dt;
                if (_afterimageAge[i] >= AfterimageLife)
                {
                    _afterimages[i].enabled = false;
                    continue;
                }
                var c = _afterimages[i].color;
                c.a = 0.45f * (1f - _afterimageAge[i] / AfterimageLife);
                _afterimages[i].color = c;
            }

            var p = Sim.Player;
            int tier = Sim.PlayerTier;
            _afterimageTimer -= dt;
            if (tier >= 2 && Sim.State != SimState.GameOver && _afterimageTimer <= 0f)
            {
                _afterimageTimer = tier == 3 ? 0.025f : 0.045f;
                int k = _nextAfterimage;
                _nextAfterimage = (_nextAfterimage + 1) % AfterimageCount;
                var a = _afterimages[k];
                a.transform.localPosition = _player.transform.localPosition;
                a.transform.localScale = new Vector3(p.Width, p.Height, 1f);
                var c = TierColors[tier - 1];
                c.a = 0.45f;
                a.color = c;
                a.enabled = true;
                _afterimageAge[k] = 0f;
            }
        }

        // ------------------------------------------------------------------ drawing

        private void SyncActors()
        {
            var p = Sim.Player;
            int tier = Sim.PlayerTier;
            float alpha = InterpolationAlpha;
            bool sameRun = _prevRunId == Sim.RunId;
            var feet = sameRun ? Vector2.Lerp(_prevPlayerFeet, new Vector2(p.X, p.Y), alpha) : new Vector2(p.X, p.Y);
            _drawOffset = feet - new Vector2(p.X, p.Y);
            var color = TierColors[tier - 1];
            if (p.InvulnTimer > 0f && Mathf.Repeat(Time.time * 16f, 2f) < 1f) color.a = 0.3f;
            if (Sim.State == SimState.GameOver) color = new Color(0.4f, 0.4f, 0.45f);
            _player.color = color;
            _player.transform.localPosition = new Vector3(feet.x, feet.y + p.Height * 0.5f, 0f);
            _player.transform.localScale = new Vector3(p.Width, p.Height, 1f);

            _playerEye.transform.localPosition = new Vector3(feet.x + p.Facing * p.Width * 0.22f, feet.y + p.Height * 0.72f, 0f);
            _playerEye.transform.localScale = new Vector3(0.18f, Mathf.Min(0.22f, p.Height * 0.25f), 1f);

            if (p.Attacking)
            {
                Sim.GetStrikeBox(out float minX, out float minY, out float maxX, out float maxY);
                _strike.enabled = true;
                _strike.transform.localPosition = new Vector3((minX + maxX) * 0.5f + _drawOffset.x, (minY + maxY) * 0.5f + _drawOffset.y, 0f);
                _strike.transform.localScale = new Vector3(maxX - minX, maxY - minY, 1f);
                var sc = TierColors[tier - 1];
                sc.a = 0.55f;
                _strike.color = sc;
            }
            else
            {
                _strike.enabled = false;
            }

            if (p.Hooked)
            {
                var from = new Vector2(p.Center.X, p.Center.Y) + _drawOffset;
                var to = new Vector2(p.HookAnchor.X, p.HookAnchor.Y);
                var d = to - from;
                _rope.enabled = true;
                _rope.transform.localPosition = (from + to) * 0.5f;
                _rope.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                _rope.transform.localScale = new Vector3(d.magnitude, 0.08f, 1f);
            }
            else
            {
                _rope.enabled = false;
            }

            var enemies = Sim.Enemies;
            while (_enemyViews.Count < enemies.Count)
                _enemyViews.Add(SpriteFactory.Create("Enemy", _world, SpriteFactory.Square, GruntColor, 8));
            for (int i = 0; i < _enemyViews.Count; i++)
            {
                var view = _enemyViews[i];
                if (i >= enemies.Count || !enemies[i].Alive)
                {
                    view.enabled = false;
                    continue;
                }
                var e = enemies[i];
                view.enabled = true;
                var efeet = new Vector2(e.X, e.Y);
                if (sameRun && _prevEnemyFeet.Count == enemies.Count) efeet = Vector2.Lerp(_prevEnemyFeet[i], efeet, alpha);
                view.transform.localPosition = new Vector3(efeet.x, efeet.y + e.Height * 0.5f, 0f);
                view.transform.localScale = new Vector3(e.Width, e.Height, 1f);
                Color ec;
                if (e.SinceHit < 0.08f) ec = Color.white;
                else if (e.Kind == EnemyKind.Grunt) ec = GruntColor;
                else ec = CombatRules.CanDamage(tier, e.MinTier) ? BruteBreakable : BruteArmored;
                view.color = ec;
            }
        }

        private void UpdateCamera(float dt)
        {
            if (!Mathf.Approximately(_camera.aspect, _lastAspect))
            {
                _lastAspect = _camera.aspect;
                FitCamera();
            }
            _shake = Mathf.Max(0f, _shake - dt * 1.5f);
            var offset = _shake > 0f
                ? new Vector3(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f), 0f) * _shake
                : Vector3.zero;
            _camera.transform.position = _cameraHome + offset;
        }

        private float _lastAspect;
    }
}
