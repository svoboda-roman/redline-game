namespace Redline.Core
{
    /// <summary>Enemy archetypes.</summary>
    public enum EnemyKind
    {
        /// <summary>Fast chaser, dies to any hit.</summary>
        Grunt,
        /// <summary>Slow, heavy, armored below a minimum player tier.</summary>
        Brute
    }

    /// <summary>A live enemy in the arena.</summary>
    public sealed class Enemy : Body
    {
        public EnemyKind Kind { get; }
        public float Hp;
        public float MaxHp { get; }
        /// <summary>Lowest player tier that can damage this enemy.</summary>
        public int MinTier { get; }
        public int ContactDamage { get; }
        public float Speed { get; }
        public float JumpSpeed { get; }
        /// <summary>While above 0 the enemy does not steer and deals no contact damage.</summary>
        public float StunTimer;
        /// <summary>Seconds since the enemy was last struck (for hit flashes).</summary>
        public float SinceHit = 99f;
        /// <summary>Strike id of the last player strike that hit this enemy.</summary>
        public int LastStrikeId = -1;
        /// <summary>Last walking direction (+1 right, -1 left); used to walk off ledges above the player.</summary>
        public int Facing = 1;

        public bool Alive => Hp > 0f;

        public Enemy(EnemyKind kind, Vec2 spawn, RedlineConfig cfg)
        {
            Kind = kind;
            X = spawn.X;
            Y = spawn.Y;
            if (kind == EnemyKind.Grunt)
            {
                Width = cfg.gruntWidth;
                Height = cfg.gruntHeight;
                MaxHp = cfg.gruntHp;
                MinTier = 1;
                ContactDamage = cfg.gruntContactDamage;
                Speed = cfg.gruntSpeed;
                JumpSpeed = cfg.gruntJumpSpeed;
            }
            else
            {
                Width = cfg.bruteWidth;
                Height = cfg.bruteHeight;
                MaxHp = cfg.bruteHp;
                MinTier = cfg.bruteMinTier;
                ContactDamage = cfg.bruteContactDamage;
                Speed = cfg.bruteSpeed;
                JumpSpeed = cfg.bruteJumpSpeed;
            }
            Hp = MaxHp;
        }
    }
}
