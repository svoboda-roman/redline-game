using System;
using System.Collections.Generic;
using Redline.Core;
using Redline.Game.Render3D.Actors3D;
using UnityEngine;

namespace Redline.Game.Render3D.Integration
{
    /// <summary>
    /// The effect calls <see cref="RedlineGame"/> makes, shared by the 2D <see cref="EffectsRenderer"/> and the 3D
    /// <see cref="Effects3DRenderer"/> (wrapped by <see cref="Effects2D"/> / <see cref="Effects3D"/>), so the game's
    /// event handling stays one code path for both render modes.
    /// </summary>
    public interface IGameEffects
    {
        /// <summary>Hot sparks bursting from a point.</summary>
        void Sparks(Vector2 at, Color color, int count, float speed);
        /// <summary>Heavier falling debris.</summary>
        void Debris(Vector2 at, Color color, int count, float speed);
        /// <summary>Dust puffs at the feet, pushed along X.</summary>
        void Dust(Vector2 feet, int count, float pushX);
        /// <summary>Blood from a kill: a spray thrown along <paramref name="dirX"/>, a mist puff, splatters on the floor at <paramref name="floorY"/>.</summary>
        void Blood(Vector2 at, float dirX, float floorY, float amount);
        /// <summary>A speed line around a point, travelling along X.</summary>
        void SpeedLine(Vector2 around, float travelDirX, Color color);
        /// <summary>A short expanding glow.</summary>
        void Pop(Vector2 at, Color color, float size, float life);
        /// <summary>A fading sprite copy (2D only; the 3D view ignores it).</summary>
        void Afterimage(Sprite sprite, Vector3 position, Vector3 scale, bool flipX, Color color);
        /// <summary>Flashes the whole view.</summary>
        void Flash(float alpha);
        /// <summary>Advances every live effect.</summary>
        void Update(float dt);
    }

    /// <summary>Orb drawing shared by <see cref="OrbRenderer"/> and <see cref="Orb3DRenderer"/>.</summary>
    public interface IGameOrbs
    {
        /// <summary>Updates every orb view from the simulation.</summary>
        void Sync(RedlineSim sim, Func<Vector2, Vector2> toDraw, float time);
    }

    /// <summary>Actor drawing (player, enemies, projectiles, rope) shared by the 2D and 3D views.</summary>
    public interface IGameActors
    {
        /// <summary>Updates every actor view from the simulation; positions are drawing positions.</summary>
        void Sync(RedlineSim sim, Vector2 playerFeet, IReadOnlyList<Vector2> enemyFeet, float dt);
        /// <summary>Squashes (below 1) or stretches (above 1) the player for a moment.</summary>
        void Squash(float amount);
        /// <summary>The 2D player sprite (afterimages); null in the 3D view.</summary>
        SpriteRenderer PlayerView { get; }
    }

    /// <summary>Adapter: the 2D <see cref="EffectsRenderer"/> as <see cref="IGameEffects"/>.</summary>
    public sealed class Effects2D : IGameEffects
    {
        /// <summary>The wrapped renderer (ActorRenderer takes it directly).</summary>
        public readonly EffectsRenderer Renderer;
        /// <summary>Wraps <paramref name="renderer"/>.</summary>
        public Effects2D(EffectsRenderer renderer) { Renderer = renderer; }
        /// <inheritdoc/>
        public void Sparks(Vector2 at, Color color, int count, float speed) => Renderer.Sparks(at, color, count, speed);
        /// <inheritdoc/>
        public void Debris(Vector2 at, Color color, int count, float speed) => Renderer.Debris(at, color, count, speed);
        /// <inheritdoc/>
        public void Dust(Vector2 feet, int count, float pushX) => Renderer.Dust(feet, count, pushX);
        /// <summary>2D view: a dark red debris spray (the classic renderer has no floor decals).</summary>
        public void Blood(Vector2 at, float dirX, float floorY, float amount) => Renderer.Debris(at, new Color(0.45f, 0.02f, 0.03f), Mathf.RoundToInt(12 * amount), 7f);
        /// <inheritdoc/>
        public void SpeedLine(Vector2 around, float travelDirX, Color color) => Renderer.SpeedLine(around, travelDirX, color);
        /// <inheritdoc/>
        public void Pop(Vector2 at, Color color, float size, float life) => Renderer.Pop(at, color, size, life);
        /// <inheritdoc/>
        public void Afterimage(Sprite sprite, Vector3 position, Vector3 scale, bool flipX, Color color) => Renderer.Afterimage(sprite, position, scale, flipX, color);
        /// <inheritdoc/>
        public void Flash(float alpha) => Renderer.Flash(alpha);
        /// <inheritdoc/>
        public void Update(float dt) => Renderer.Update(dt);
    }

    /// <summary>Adapter: the 3D <see cref="Effects3DRenderer"/> as <see cref="IGameEffects"/>.</summary>
    public sealed class Effects3D : IGameEffects
    {
        /// <summary>The wrapped renderer (Enemy3DRenderer takes it directly).</summary>
        public readonly Effects3DRenderer Renderer;
        /// <summary>Wraps <paramref name="renderer"/>.</summary>
        public Effects3D(Effects3DRenderer renderer) { Renderer = renderer; }
        /// <inheritdoc/>
        public void Sparks(Vector2 at, Color color, int count, float speed) => Renderer.Sparks(at, color, count, speed);
        /// <inheritdoc/>
        public void Debris(Vector2 at, Color color, int count, float speed) => Renderer.Debris(at, color, count, speed);
        /// <inheritdoc/>
        public void Dust(Vector2 feet, int count, float pushX) => Renderer.Dust(feet, count, pushX);
        /// <inheritdoc/>
        public void Blood(Vector2 at, float dirX, float floorY, float amount) => Renderer.Blood(at, dirX, floorY, amount);
        /// <inheritdoc/>
        public void SpeedLine(Vector2 around, float travelDirX, Color color) => Renderer.SpeedLine(around, travelDirX, color);
        /// <inheritdoc/>
        public void Pop(Vector2 at, Color color, float size, float life) => Renderer.Pop(at, color, size, life);
        /// <summary>No-op: the 3D ronin has no sprite pose to copy.</summary>
        public void Afterimage(Sprite sprite, Vector3 position, Vector3 scale, bool flipX, Color color) { }
        /// <inheritdoc/>
        public void Flash(float alpha) => Renderer.Flash(alpha);
        /// <inheritdoc/>
        public void Update(float dt) => Renderer.Update(dt);
    }

    /// <summary>Adapter: <see cref="OrbRenderer"/> as <see cref="IGameOrbs"/>.</summary>
    public sealed class Orbs2D : IGameOrbs
    {
        private readonly OrbRenderer _r;
        /// <summary>Wraps <paramref name="renderer"/>.</summary>
        public Orbs2D(OrbRenderer renderer) { _r = renderer; }
        /// <inheritdoc/>
        public void Sync(RedlineSim sim, Func<Vector2, Vector2> toDraw, float time) => _r.Sync(sim, toDraw, time);
    }

    /// <summary>Adapter: <see cref="Orb3DRenderer"/> as <see cref="IGameOrbs"/>.</summary>
    public sealed class Orbs3D : IGameOrbs
    {
        private readonly Orb3DRenderer _r;
        /// <summary>Wraps <paramref name="renderer"/>.</summary>
        public Orbs3D(Orb3DRenderer renderer) { _r = renderer; }
        /// <inheritdoc/>
        public void Sync(RedlineSim sim, Func<Vector2, Vector2> toDraw, float time) => _r.Sync(sim, toDraw, time);
    }

    /// <summary>Adapter: the 2D <see cref="ActorRenderer"/> as <see cref="IGameActors"/>.</summary>
    public sealed class Actors2D : IGameActors
    {
        private readonly ActorRenderer _r;
        /// <summary>Wraps <paramref name="renderer"/>.</summary>
        public Actors2D(ActorRenderer renderer) { _r = renderer; }
        /// <inheritdoc/>
        public void Sync(RedlineSim sim, Vector2 playerFeet, IReadOnlyList<Vector2> enemyFeet, float dt) => _r.Sync(sim, playerFeet, enemyFeet, dt);
        /// <inheritdoc/>
        public void Squash(float amount) => _r.Squash(amount);
        /// <inheritdoc/>
        public SpriteRenderer PlayerView => _r.PlayerView;
    }

    /// <summary>
    /// Chooses the render mode: the settings value (Graphics: Cinematic 3D / Classic 2D), overridden for the
    /// session by the command-line flags <c>-render3d</c> / <c>-render2d</c> until the player changes the setting.
    /// </summary>
    public static class RenderModeSwitch
    {
        private static bool _parsed;
        private static bool? _commandLine;
        private static bool _haveStart;
        private static bool _settingAtStart;

        /// <summary>The command-line choice: true = 3D, false = 2D, null = none.</summary>
        public static bool? CommandLine
        {
            get
            {
                if (_parsed) return _commandLine;
                _parsed = true;
                foreach (var a in Environment.GetCommandLineArgs())
                {
                    if (string.Equals(a, "-render2d", StringComparison.OrdinalIgnoreCase)) _commandLine = false;
                    else if (string.Equals(a, "-render3d", StringComparison.OrdinalIgnoreCase)) _commandLine = true;
                }
                return _commandLine;
            }
        }

        /// <summary>Whether the game should draw in 3D, given the setting (true = Cinematic 3D).</summary>
        public static bool Want3D(bool setting3D)
        {
            if (!_haveStart)
            {
                _haveStart = true;
                _settingAtStart = setting3D;
            }
            var cmd = CommandLine;
            return cmd.HasValue && setting3D == _settingAtStart ? cmd.Value : setting3D;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _parsed = false;
            _commandLine = null;
            _haveStart = false;
        }
    }
}
