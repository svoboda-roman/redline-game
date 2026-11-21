namespace Redline.Core
{
    /// <summary>What Big Tentacle Bill is doing (see <see cref="StoryBoss"/>).</summary>
    public enum BossMove
    {
        /// <summary>Asleep until the player walks into its room.</summary>
        Dormant,
        /// <summary>Tentacles sway; the next attack is being chosen.</summary>
        Idle,
        /// <summary>One tentacle rears up (telegraph), then sweeps down across the floor toward the player.</summary>
        Swipe,
        /// <summary>One tentacle draws back (telegraph), then stabs straight out at where the player stood.</summary>
        Lunge,
        /// <summary>The body heaves up and slams down: a shockwave runs both ways along the floor (jump it).</summary>
        Slam,
        /// <summary>Spits clumps of mud at the player (a sword strike sends them back into it).</summary>
        Spit,
        /// <summary>Crawls after the player on its tentacles (or rolls, once they are gone).</summary>
        Chase,
        /// <summary>Whirls every tentacle round itself (telegraph: they curl in first) - anything in reach is hit.</summary>
        Spin,
        /// <summary>Whirls its tentacles, then leaps at the player in a high, slow arc and crashes down with a shockwave.</summary>
        Leap,
        /// <summary>Hurls mud high: clumps rain down around the player (no tentacles left only).</summary>
        MudRain,
        /// <summary>Beaten: sinks and dies.</summary>
        Dying,
    }

    /// <summary>One of Big Tentacle Bill's seven tentacles: a chain of points from its root on the body to its tip.</summary>
    public sealed class BossTentacle
    {
        /// <summary>Points along the tentacle, root first (world tiles); <see cref="PointCount"/> of them.</summary>
        public readonly Vec2[] Points = new Vec2[PointCount];
        public const int PointCount = 12;

        /// <summary>Where it grows from, relative to the body centre.</summary>
        public Vec2 RootOffset;
        /// <summary>Resting direction (degrees, 0 = +X, 90 = up).</summary>
        public float BaseAngle;
        /// <summary>Resting length (tiles).</summary>
        public float Length;
        /// <summary>Hits taken (it falls off at the config's storyBossTentacleHits).</summary>
        public int Hits;
        /// <summary>Cut off: gone from the fight.</summary>
        public bool Severed;
        /// <summary>0..1 flash after a hit (the view tints it).</summary>
        public float Flash;
        /// <summary>The strike that last hit it (one hit per swing).</summary>
        public int LastStrikeId = -1;
        /// <summary>Thickness at the root (tips taper to a third of it).</summary>
        public float Thickness = 0.55f;
    }

    /// <summary>
    /// Big Tentacle Bill, the story boss: a huge body in the boss room with seven tentacles. Each tentacle falls off after
    /// three hits; while any tentacle is left the body shrugs off strikes. With all seven gone the body can be hurt, and
    /// it dies when its HP runs out. Attacks: tentacle swipes and lunges, a body slam with a floor shockwave, and spat
    /// mud clumps that a strike can send back. The sim (RedlineSim.Boss.cs) drives it; views read it.
    /// </summary>
    /// <example><code>
    /// var bill = sim.Boss;   // null on maps without a boss room
    /// if (bill != null &amp;&amp; bill.Move != BossMove.Dormant) DrawBossBar(bill.Hp / bill.MaxHp, bill.TentaclesLeft);
    /// </code></example>
    public sealed class StoryBoss
    {
        public const string Name = "BIG TENTACLE BILL";
        public const int TentacleCount = 7;

        /// <summary>Body centre at rest (world tiles) and its radius.</summary>
        public Vec2 Home;
        public float Radius = 3.2f;
        /// <summary>Current body centre (rises and falls in a slam).</summary>
        public Vec2 Body;
        public float Hp, MaxHp;
        public BossMove Move = BossMove.Dormant;
        /// <summary>Seconds into the current move.</summary>
        public float MoveTime;
        /// <summary>Tentacle used by a swipe or lunge.</summary>
        public int MoveTentacle = -1;
        /// <summary>Where a lunge aims / which side a swipe sweeps to.</summary>
        public Vec2 MoveTarget;
        /// <summary>Shots left in a spit volley (or clumps left in a mud rain).</summary>
        public int ShotsLeft;
        /// <summary>Clumps in the current volley.</summary>
        public int ShotsTotal;
        /// <summary>Attacks to make where it stands before it chases the player again.</summary>
        public int StandLeft;
        /// <summary>How long the current chase lasts (s).</summary>
        public float ChaseTime;
        /// <summary>Spin angle added to every tentacle (degrees) while spinning or leaping.</summary>
        public float SpinAngle;
        /// <summary>Leap start and landing x.</summary>
        public float LeapFrom, LeapTo;
        /// <summary>-1: faces left (tentacles fan out to the left), +1: faces right. Turns toward the player between attacks.</summary>
        public int Facing = -1;
        /// <summary>Height of the body centre when resting on the floor.</summary>
        public float GroundY;
        /// <summary>Seconds until the next move is chosen.</summary>
        public float Cooldown;
        /// <summary>0..1 flash after the body is hit (or shrugs a strike off).</summary>
        public float Flash;
        public int LastStrikeId = -1;
        /// <summary>Floor height of the room (shockwaves run along it) and the room's x extent.</summary>
        public float FloorY, RoomMinX, RoomMaxX;
        /// <summary>Live shockwave fronts (x positions) after a slam; NaN when none. Two fronts run out from the impact.</summary>
        public float WaveLeft = float.NaN, WaveRight = float.NaN;
        public readonly BossTentacle[] Tentacles = new BossTentacle[TentacleCount];

        public StoryBoss()
        {
            for (int i = 0; i < TentacleCount; i++) Tentacles[i] = new BossTentacle();
        }

        /// <summary>Tentacles still attached.</summary>
        public int TentaclesLeft
        {
            get
            {
                int n = 0;
                foreach (var t in Tentacles) if (!t.Severed) n++;
                return n;
            }
        }

        /// <summary>True once every tentacle is off: the body can be hurt.</summary>
        public bool Vulnerable => TentaclesLeft == 0;

        /// <summary>True while the fight is on (woken, not yet dead).</summary>
        public bool Fighting => Move != BossMove.Dormant && Move != BossMove.Dying;
    }
}
