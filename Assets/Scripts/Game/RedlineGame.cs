using System;
using System.Collections.Generic;
using Redline.Core;
using UnityEngine;

namespace Redline.Game
{
    /// <summary>
    /// Owns the run: loads config and arena from Resources, steps <see cref="RedlineSim"/> at a
    /// fixed 60 Hz, interpolates between steps for drawing, and routes simulation events to the
    /// renderers (<see cref="ArenaRenderer"/>, <see cref="ActorRenderer"/>,
    /// <see cref="EffectsRenderer"/>) and <see cref="PostFx"/>. All rules live in Redline.Core.
    /// </summary>
    /// <remarks>Created by <see cref="RedlineBootstrap"/>. Main-thread only.</remarks>
    public sealed class RedlineGame : MonoBehaviour
    {
        /// <summary>The level the game plays (a Resources text file).</summary>
        private const string LevelName = "Loop01";
        /// <summary>Half the view height in tiles: a close camera that follows the player.</summary>
        private const float CameraSize = 8f;
        /// <summary>Seconds of the player's velocity the camera looks ahead by.</summary>
        private const float Lookahead = 0.35f;
        private static readonly Color Background = new Color(0.01f, 0.01f, 0.04f);
        private static readonly Color GruntDebris = new Color(1f, 0.55f, 0.15f);
        private static readonly Color BruteDebris = new Color(1f, 0.2f, 0.28f);
        private static readonly Color DroneDebris = new Color(0.78f, 0.45f, 1f);
        private static readonly Color WindupColor = new Color(1f, 0.85f, 0.4f);
        private static readonly Color HurtColor = new Color(1f, 0.25f, 0.4f);
        private static readonly Color ClangColor = new Color(0.7f, 0.8f, 1f);

        /// <summary>Colour of each speed tier (index = tier - 1). Shared with the HUD and renderers.</summary>
        public static readonly Color[] TierColors =
        {
            new Color(0.25f, 0.85f, 1f),
            new Color(1f, 0.3f, 0.9f),
            new Color(1f, 0.95f, 0.45f)
        };

        private readonly RedlineInput _input = new RedlineInput();
        // Positions before the latest fixed step, for drawing between steps (render interpolation).
        private readonly List<Vector2> _prevEnemyFeet = new List<Vector2>(32);
        private readonly List<Vector2> _drawEnemyFeet = new List<Vector2>(32);
        private Vector2 _prevPlayerFeet;
        private int _prevRunId = -1;

        private Camera _camera;
        private ArenaRenderer _arena;
        private ActorRenderer _actors;
        private EffectsRenderer _effects;
        private PostFx _postFx;

        private float _accumulator;
        private float _hitStop;
        private float _shake;
        private float _camX;
        private float _camY;
        private float _lookahead;
        private float _afterimageTimer;
        private float _speedLineTimer;

        /// <summary>The running simulation (read by the HUD).</summary>
        public RedlineSim Sim { get; private set; }

        private void Awake()
        {
            Application.targetFrameRate = 120;
            Sim = new RedlineSim(LoadConfig(), LoadMap());

            var world = new GameObject("World").transform;
            world.SetParent(transform, false);
            SetupCamera();
            _arena = new ArenaRenderer(world, Sim.Map);
            _effects = new EffectsRenderer(world, _camera);
            _actors = new ActorRenderer(world, _effects);
            _postFx = gameObject.AddComponent<PostFx>();
            _postFx.Init(_camera);

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
                bool wasGrounded = Sim.Player.Grounded;
                float fallSpeed = -Sim.Player.Vy;
                Sim.Step(_input.Consume(), RedlineSim.FixedDt);
                if (!wasGrounded && Sim.Player.Grounded && fallSpeed > 6f) OnLanded(fallSpeed);
                HandleEvents();
                _accumulator -= RedlineSim.FixedDt;
            }

            UpdateCamera(dt);
            var feet = InterpolatedFeet();
            _actors.Sync(Sim, feet, _drawEnemyFeet, dt);
            _arena.Update(dt, _camera.transform.position);
            SpawnSpeedTrail(dt);
            _effects.Update(dt);
        }

        // ------------------------------------------------------------------ interpolation

        private void CapturePrevious()
        {
            var p = Sim.Player;
            _prevPlayerFeet = new Vector2(p.X, p.Y);
            _prevRunId = Sim.RunId;
            _prevEnemyFeet.Clear();
            var enemies = Sim.Enemies;
            for (int i = 0; i < enemies.Count; i++) _prevEnemyFeet.Add(new Vector2(enemies[i].X, enemies[i].Y));
        }

        /// <summary>Blends the last two fixed steps; fills the enemy list and returns the player's feet.</summary>
        private Vector2 InterpolatedFeet()
        {
            float alpha = Mathf.Clamp01(_accumulator / RedlineSim.FixedDt);
            bool sameRun = _prevRunId == Sim.RunId;
            var p = Sim.Player;
            var enemies = Sim.Enemies;
            bool enemiesMatch = sameRun && _prevEnemyFeet.Count == enemies.Count;
            _drawEnemyFeet.Clear();
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = new Vector2(enemies[i].X, enemies[i].Y);
                _drawEnemyFeet.Add(ToDraw(enemiesMatch ? Blend(_prevEnemyFeet[i], e, alpha) : e));
            }
            var now = new Vector2(p.X, p.Y);
            return ToDraw(sameRun ? Blend(_prevPlayerFeet, now, alpha) : now);
        }

        /// <summary>Blends two step positions; on a ring it goes the short way across the seam.</summary>
        private Vector2 Blend(Vector2 prev, Vector2 now, float alpha)
        {
            float dx = Sim.Map.Dx(prev.x, now.x);
            return new Vector2(now.x - (1f - alpha) * dx, Mathf.Lerp(prev.y, now.y, alpha));
        }

        /// <summary>
        /// Simulation position -> drawing position. On a ring the copy nearest the camera is drawn,
        /// so the level has no visible seam.
        /// </summary>
        public Vector2 ToDraw(Vector2 world) =>
            Sim.Map.WrapsX ? new Vector2(_camX + Sim.Map.Dx(_camX, world.x), world.y) : world;

        /// <summary>The game camera (for HUD tooling).</summary>
        public Camera Camera => _camera;

        private Vector2 ToDraw(Vec2 world) => ToDraw(new Vector2(world.X, world.Y));

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
            var asset = Resources.Load<TextAsset>(LevelName);
            if (asset == null) throw new InvalidOperationException("Redline: Resources/" + LevelName + ".txt is missing.");
            return TileMap.Parse(asset.text);
        }

        // ------------------------------------------------------------------ camera

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
            _camX = Sim.Player.X;
            _camY = Sim.Player.Y + 2.5f;
        }

        /// <summary>
        /// Follows the player closely, looking ahead in the running direction; vertically it keeps
        /// the player in the lower half and stays inside the level. Screen shake rides on top.
        /// </summary>
        private void UpdateCamera(float dt)
        {
            var p = Sim.Player;
            var map = Sim.Map;
            _camera.orthographicSize = CameraSize;
            _lookahead = Mathf.Lerp(_lookahead, Mathf.Clamp(p.Vx * Lookahead, -5f, 5f), 1f - Mathf.Exp(-dt * 3f));
            float targetX = _camX + map.Dx(_camX, p.X) + _lookahead;
            float targetY = p.Y + 2.5f;
            _camX = Mathf.Lerp(_camX, targetX, 1f - Mathf.Exp(-dt * 7f));
            _camY = Mathf.Lerp(_camY, targetY, 1f - Mathf.Exp(-dt * 5f));
            float halfW = CameraSize * Mathf.Max(0.1f, _camera.aspect);
            if (!map.WrapsX) _camX = ClampView(_camX, halfW, map.Width);
            _camY = ClampView(_camY, CameraSize, map.Height);

            _shake = Mathf.Max(0f, _shake - dt * 1.5f);
            var offset = _shake > 0f
                ? new Vector3(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f), 0f) * _shake
                : Vector3.zero;
            _camera.transform.position = new Vector3(_camX, _camY, -10f) + offset;
        }

        /// <summary>Keeps a view of half-size <paramref name="half"/> inside [-1, size+1], centring when it is larger.</summary>
        private static float ClampView(float centre, float half, float size)
        {
            float min = half - 1f;
            float max = size + 1f - half;
            return min > max ? size * 0.5f : Mathf.Clamp(centre, min, max);
        }

        // ------------------------------------------------------------------ feedback

        private void HandleEvents()
        {
            var events = Sim.Events;
            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                var at = ToDraw(ev.Position);
                var color = TierColors[Mathf.Clamp(ev.Tier, 1, 3) - 1];
                switch (ev.Type)
                {
                    case SimEventType.Hit: OnHit(at, color, ev.Tier); break;
                    case SimEventType.Kill: OnKill(at, color, ev.Tier, KilledKindAt(new Vector2(ev.Position.X, ev.Position.Y))); break;
                    case SimEventType.Clang: OnClang(at); break;
                    case SimEventType.PlayerHurt: OnHurt(at); break;
                    case SimEventType.GameOver: OnGameOver(at); break;
                    case SimEventType.Jump: OnJump(); break;
                    case SimEventType.WallJump: OnWallJump(color); break;
                    case SimEventType.Slide: OnSlide(color); break;
                    case SimEventType.HookAttach: _effects.Sparks(at, color, 8, 5f); break;
                    case SimEventType.WaveStart: _effects.Flash(0.08f); break;
                    case SimEventType.Victory: _effects.Flash(0.25f); break;
                    case SimEventType.BruteWindup: _effects.Sparks(at, WindupColor, 6, 3f); break;
                    case SimEventType.BruteCrash: OnBruteCrash(at); break;
                }
            }
        }

        private void OnHit(Vector2 at, Color color, int tier)
        {
            _effects.Sparks(at, color, 6 + 3 * tier, 6f + 3f * tier);
            _shake = Mathf.Max(_shake, 0.05f * tier);
        }

        private void OnKill(Vector2 at, Color color, int tier, EnemyKind kind)
        {
            bool brute = kind == EnemyKind.Brute;
            var debris = brute ? BruteDebris : kind == EnemyKind.Drone ? DroneDebris : GruntDebris;
            _effects.Sparks(at, color, 10 + 6 * tier, 8f + 4f * tier);
            _effects.Debris(at, debris, brute ? 16 : 9, 7f + 2f * tier);
            _shake = Mathf.Max(_shake, 0.08f * tier + (brute ? 0.2f : 0f));
            _hitStop = Mathf.Max(_hitStop, 0.02f * tier + (brute ? 0.05f : 0f));
            if (tier == 3) _effects.Flash(brute ? 0.22f : 0.1f);
        }

        private void OnBruteCrash(Vector2 at)
        {
            _effects.Sparks(at, WindupColor, 18, 9f);
            _effects.Dust(new Vector2(at.x, at.y - 1f), 8, 0f);
            _shake = Mathf.Max(_shake, 0.35f);
            _hitStop = Mathf.Max(_hitStop, 0.06f);
        }

        private void OnClang(Vector2 at)
        {
            _effects.Sparks(at, ClangColor, 14, 8f);
            _shake = Mathf.Max(_shake, 0.15f);
            _hitStop = Mathf.Max(_hitStop, 0.04f);
        }

        private void OnHurt(Vector2 at)
        {
            _effects.Sparks(at, HurtColor, 14, 8f);
            _postFx.PulseDamage();
            _shake = Mathf.Max(_shake, 0.3f);
            _hitStop = Mathf.Max(_hitStop, 0.05f);
        }

        private void OnGameOver(Vector2 at)
        {
            _effects.Debris(at, TierColors[0], 24, 10f);
            _effects.Sparks(at, HurtColor, 20, 10f);
            _effects.Flash(0.3f);
            _shake = Mathf.Max(_shake, 0.45f);
        }

        private void OnJump()
        {
            var p = Sim.Player;
            _effects.Dust(ToDraw(new Vector2(p.X, p.Y)), 5, p.Vx);
            _actors.Squash(1.25f);
        }

        private void OnWallJump(Color color)
        {
            var p = Sim.Player;
            _effects.Dust(ToDraw(new Vector2(p.X - Mathf.Sign(p.Vx) * p.Width * 0.5f, p.Y + p.Height * 0.5f)), 6, p.Vx);
            _effects.Sparks(ToDraw(new Vector2(p.X, p.Y + p.Height * 0.5f)), color, 5, 4f);
            _actors.Squash(1.2f);
        }

        private void OnSlide(Color color)
        {
            var p = Sim.Player;
            _effects.Dust(ToDraw(new Vector2(p.X, p.Y)), 8, p.Vx);
            _effects.Sparks(ToDraw(new Vector2(p.X, p.Y + 0.1f)), color, 5, 4f);
        }

        private void OnLanded(float fallSpeed)
        {
            var p = Sim.Player;
            _effects.Dust(ToDraw(new Vector2(p.X, p.Y)), fallSpeed > 15f ? 8 : 4, 0f);
            _actors.Squash(Mathf.Lerp(0.85f, 0.65f, Mathf.InverseLerp(6f, 24f, fallSpeed)));
        }

        /// <summary>Dev-only: plays the tier-3 kill feedback at a point (used by the screenshot tour).</summary>
        internal void PreviewKillEffects(Vector2 at, bool brute)
        {
            _effects.Sparks(at, TierColors[2], 28, 20f);
            _effects.Debris(at, brute ? BruteDebris : GruntDebris, brute ? 16 : 9, 13f);
            _effects.Flash(0.12f);
        }

        /// <summary>Kind of the enemy that died at a point this step (events carry no enemy reference).</summary>
        private EnemyKind KilledKindAt(Vector2 at)
        {
            var enemies = Sim.Enemies;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (e.Alive || e.SinceHit > 0f) continue;
                var c = e.Center;
                if (Mathf.Abs(c.X - at.x) < 0.01f && Mathf.Abs(c.Y - at.y) < 0.01f) return e.Kind;
            }
            return EnemyKind.Grunt;
        }

        /// <summary>Afterimages at tier 2+, speed lines at tier 3.</summary>
        private void SpawnSpeedTrail(float dt)
        {
            if (Sim.State == SimState.GameOver) return;
            var p = Sim.Player;
            int tier = Sim.PlayerTier;
            var view = _actors.PlayerView;
            var color = TierColors[tier - 1];

            _afterimageTimer -= dt;
            if (tier >= 2 && _afterimageTimer <= 0f)
            {
                _afterimageTimer = tier == 3 ? 0.025f : 0.045f;
                var c = color;
                c.a = 0.4f;
                _effects.Afterimage(view.sprite, view.transform.localPosition, view.transform.localScale, view.flipX, c);
            }

            _speedLineTimer -= dt;
            if (tier == 3 && _speedLineTimer <= 0f)
            {
                _speedLineTimer = 0.02f;
                var pos = view.transform.localPosition;
                _effects.SpeedLine(new Vector2(pos.x, pos.y), Mathf.Sign(p.Vx), color);
            }
        }
    }
}
