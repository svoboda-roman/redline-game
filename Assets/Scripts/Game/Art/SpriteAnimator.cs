namespace Redline.Game.Art
{
    /// <summary>
    /// Plays one <see cref="SheetAnimation"/> at a time: advances the frame from elapsed time,
    /// wraps looping animations, holds the last frame of one-shots (and reports
    /// <see cref="Finished"/>), and restarts from frame 0 when a different animation is played.
    /// </summary>
    /// <example>
    /// <code>
    /// var animator = new SpriteAnimator();
    /// animator.Play(sheet.Animation(grounded ? "run" : "fall"));   // no-op if already playing
    /// animator.Tick(dt, speedScale: 1.4f);                          // run faster
    /// renderer.sprite = animator.Current.Frame(animator.Frame);
    /// </code>
    /// </example>
    /// <remarks>Main-thread only. Allocation-free after construction.</remarks>
    public sealed class SpriteAnimator
    {
        private float _time;

        /// <summary>The animation playing (null before the first <see cref="Play"/>).</summary>
        public SheetAnimation Current { get; private set; }

        /// <summary>Index of the frame to draw.</summary>
        public int Frame { get; private set; }

        /// <summary>A non-looping animation has reached and is holding its last frame.</summary>
        public bool Finished { get; private set; }

        /// <summary>Seconds since the current animation (re)started, at the authored rate.</summary>
        public float Elapsed => Current == null ? 0f : _time / Current.Fps;

        /// <summary>
        /// Switches to <paramref name="animation"/> from frame 0. Playing the animation that is already
        /// current does nothing unless <paramref name="restart"/> is true.
        /// </summary>
        public void Play(SheetAnimation animation, bool restart = false)
        {
            if (animation == Current && !restart) return;
            Current = animation;
            _time = 0f;
            Frame = 0;
            Finished = false;
        }

        /// <summary>Advances by <paramref name="dt"/> seconds; <paramref name="speedScale"/> multiplies the authored fps.</summary>
        public void Tick(float dt, float speedScale = 1f)
        {
            if (Current == null) return;
            int count = Current.FrameCount;
            if (count <= 0) return;
            _time += dt * Current.Fps * (speedScale > 0f ? speedScale : 0f);
            if (Current.Loop)
            {
                if (_time >= count) _time %= count;
                Frame = (int)_time;
                if (Frame >= count) Frame = count - 1;
            }
            else if (_time >= count)
            {
                _time = count;
                Frame = count - 1;
                Finished = true;
            }
            else
            {
                Frame = (int)_time;
            }
        }
    }
}
