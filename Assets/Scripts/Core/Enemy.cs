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
        Drone,
        /// <summary>Holds its post and fires telegraphed projectiles; a sword strike reflects them.</summary>
        Shooter,
        /// <summary>Walker with a shield on its <see cref="Enemy.ShieldDir"/> side; open from behind and above.</summary>
        Shield,
        /// <summary>Rushes the player, stops on a short fuse, then explodes (hurts the player and enemies).</summary>
        Kamikaze,
        /// <summary>Flees the player and summons drones (capped) until killed.</summary>
        Summoner
    }

    /// <summary>
    /// What a shooter, kamikaze or summoner is doing (brutes use <see cref="BruteMode"/>, drones
    /// <see cref="DroneMode"/>). Transitions: shooter Idle -> Windup -> Idle (shot fired);
    /// kamikaze Idle -> Rush -> Fuse -> explode; summoner Idle -> Cast -> Idle (drone summoned).
    /// A hit during a shooter's Windup or a summoner's Cast cancels it back to Idle.
    /// </summary>
    public enum EnemyAct
    {
        Idle,
        /// <summary>Shooter: telegraph before a shot (stands still).</summary>
        Windup,
        /// <summary>Kamikaze: running at the player.</summary>
        Rush,
        /// <summary>Kamikaze: stopped next to the player, about to explode.</summary>
        Fuse,
        /// <summary>Summoner: telegraph before a drone appears (stands still).</summary>
        Cast
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
        public float MaxHp { get; private set; }
        /// <summary>A wave boss (every bossEveryWaves-th wave in endless levels): far more HP, drawn bigger.</summary>
        public bool IsBoss { get; private set; }

        /// <summary>Adds <paramref name="extra"/> to the max HP and refills (random-wave toughness scaling).</summary>
        public void AddMaxHp(float extra)
        {
            if (extra <= 0f) return;
            MaxHp += extra;
            Hp = MaxHp;
        }

        /// <summary>Turns this enemy into a boss: <paramref name="hpMultiplier"/> times the HP, <paramref name="contactBonus"/> more contact damage.</summary>
        public void MakeBoss(float hpMultiplier, int contactBonus = 1)
        {
            IsBoss = true;
            MaxHp *= hpMultiplier;
            Hp = MaxHp;
            ContactDamage += contactBonus;
        }
        /// <summary>Lowest player tier that can damage this enemy when its armor is up.</summary>
        public int MinTier { get; }
        public int ContactDamage { get; private set; }
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
        /// <summary>
        /// Walkers above the player whose near ledge is beyond the leash: committed to walking to the
        /// far ledge (in <see cref="Facing"/>) and dropping off it, until it leaves its post level.
        /// </summary>
        public bool Detour;

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

        /// <summary>Shooter, kamikaze and summoner: current action (timed by <see cref="ModeTimer"/>).</summary>
        public EnemyAct Act;
        /// <summary>Shield bearers: the side (+1 right, -1 left) the shield covers. Follows <see cref="Facing"/> after shieldTurnTime.</summary>
        public int ShieldDir = 1;
        /// <summary>Shield bearers: how long <see cref="Facing"/> has differed from <see cref="ShieldDir"/>.</summary>
        public float TurnTimer;
        /// <summary>Drones summoned by a summoner: that summoner (null otherwise).</summary>
        public Enemy Owner;

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
                case EnemyKind.Shooter:
                    Width = cfg.shooterWidth;
                    Height = cfg.shooterHeight;
                    MaxHp = cfg.shooterHp;
                    ContactDamage = cfg.shooterContactDamage;
                    Speed = cfg.shooterSpeed;
                    JumpSpeed = cfg.shooterJumpSpeed;
                    break;
                case EnemyKind.Shield:
                    Width = cfg.shieldWidth;
                    Height = cfg.shieldHeight;
                    MaxHp = cfg.shieldHp;
                    ContactDamage = cfg.shieldContactDamage;
                    Speed = cfg.shieldSpeed;
                    JumpSpeed = cfg.shieldJumpSpeed;
                    break;
                case EnemyKind.Kamikaze:
                    Width = cfg.kamikazeWidth;
                    Height = cfg.kamikazeHeight;
                    MaxHp = cfg.kamikazeHp;
                    ContactDamage = cfg.kamikazeContactDamage;
                    Speed = cfg.kamikazeSpeed;
                    JumpSpeed = cfg.kamikazeJumpSpeed;
                    break;
                case EnemyKind.Summoner:
                    Width = cfg.summonerWidth;
                    Height = cfg.summonerHeight;
                    MaxHp = cfg.summonerHp;
                    ContactDamage = cfg.summonerContactDamage;
                    Speed = cfg.summonerSpeed;
                    JumpSpeed = cfg.summonerJumpSpeed;
                    ModeTimer = cfg.summonerCastInterval;
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
