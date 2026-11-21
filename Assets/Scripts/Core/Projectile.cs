namespace Redline.Core
{
    /// <summary>
    /// A shooter's shot. Flies straight (no gravity), ends on a solid tile, a target or after its
    /// lifetime. Unreflected shots hurt the player; a sword strike reflects one back along its path
    /// (faster), and a reflected shot damages the first enemy it touches. The sim owns a fixed pool
    /// (projectileMaxCount), so do not keep a reference after it leaves <see cref="RedlineSim.Projectiles"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// foreach (var shot in sim.Projectiles)
    ///     Draw(shot.Center, shot.Vx, shot.Vy, reflected: shot.Reflected);
    /// </code>
    /// </example>
    public sealed class Projectile : Body
    {
        /// <summary>True once a sword strike sent it back: it now hurts enemies, never the player.</summary>
        public bool Reflected;
        /// <summary>A clump of mud spat by the story boss (drawn as mud; otherwise a normal shot).</summary>
        public bool Mud;
        /// <summary>Seconds before the shot fizzles out.</summary>
        public float TimeLeft;
    }
}
