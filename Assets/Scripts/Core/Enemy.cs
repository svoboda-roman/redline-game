namespace Redline.Core
{
    /// <summary>Enemy archetypes.</summary>
    public enum EnemyKind
    {
        /// <summary>Fast chaser, dies to any hit.</summary>
        Grunt,
        /// <summary>Slow, heavy, armored below a minimum player tier; may charge.</summary>
        Brute,
        /// <summary>Flying; hovers out of ground reach and dives at the player.</summary>
        Drone
    }

    /// <summary>What a charging brute is doing.</summary>
    public enum BruteMode
    {
        Walk,
        /// <summary>Telegraph before a charge: stands still.</summary>
        Windup,
        Charge,
        /// <summary>After a charge that hit nothing.</summary>
        Recover,
        /// <summary>Dazed after charging into a wall: armor down, no contact damage.</summary>
        Crashed
    }

    /// <summary>What a drone is doing.</summary>
    public enum DroneMode
    {
        Hover,
        /// <summary>Telegraph before a dive: holds still.</summary>
        Windup,
        Dive,
        Climb
    }

    /// <summary>A live enemy in the arena.</summary>
    public sealed class Enemy : Body
    {
        public EnemyKind Kind { get; }
        public float Hp;
        public float MaxHp { get; }
        /// <summary>Lowest player tier that can damage this enemy when its armor is up.</summary>
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

        /// <summary>Brutes only: whether this brute charges (set per wave).</summary>
        public bool Charges;
        /// <summary>Brutes only: current charge state.</summary>
        public BruteMode Mode;
        /// <summary>Drones only: current flight state.</summary>
        public DroneMode DroneState;
        /// <summary>Time left in the current brute or drone state (drone hover: time before it may dive).</summary>
        public float ModeTimer;
        /// <summary>Brutes only: time before the next charge may start.</summary>
        public float ChargeCooldown;
        /// <summary>Brutes only: direction of the current charge.</summary>
        public int ChargeDir;
        /// <summary>Drones only: unit direction of the current dive.</summary>
        public Vec2 DiveDir;

        /// <summary>Where this enemy stands guard; it returns here when the player leaves.</summary>
        public Vec2 Post;

        public bool Alive => Hp > 0f;

        /// <summary>The tier a strike needs to damage this enemy right now (a crashed brute's armor is down).</summary>
        public int ArmorTier => Mode == BruteMode.Crashed ? 1 : MinTier;

        /// <summary>Knocked back or crashed: not steering and harmless to touch.</summary>
        public bool Dazed => StunTimer > 0f || Mode == BruteMode.Crashed;

        public Enemy(EnemyKind kind, Vec2 spawn, RedlineConfig cfg)
        {
            Kind = kind;
            X = spawn.X;
            Y = spawn.Y;
            Post = spawn;
            MinTier = 1;
            switch (kind)
            {
                case EnemyKind.Grunt:
                    Width = cfg.gruntWidth;
                    Height = cfg.gruntHeight;
                    MaxHp = cfg.gruntHp;
                    ContactDamage = cfg.gruntContactDamage;
                    Speed = cfg.gruntSpeed;
                    JumpSpeed = cfg.gruntJumpSpeed;
                    break;
                case EnemyKind.Brute:
                    Width = cfg.bruteWidth;
                    Height = cfg.bruteHeight;
                    MaxHp = cfg.bruteHp;
                    MinTier = cfg.bruteMinTier;
                    ContactDamage = cfg.bruteContactDamage;
                    Speed = cfg.bruteSpeed;
                    JumpSpeed = cfg.bruteJumpSpeed;
                    break;
                default:
                    Width = cfg.droneSize;
                    Height = cfg.droneSize;
                    MaxHp = cfg.droneHp;
                    ContactDamage = cfg.droneContactDamage;
                    Speed = cfg.droneSpeed;
                    ModeTimer = cfg.droneDiveCooldown;
                    break;
            }
            Hp = MaxHp;
        }
    }
}
