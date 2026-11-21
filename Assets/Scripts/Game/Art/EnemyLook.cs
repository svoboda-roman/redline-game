using Redline.Core;
using UnityEngine;

namespace Redline.Game.Art
{
    /// <summary>
    /// Tuning for how the seven enemy sheets (<c>Resources/Art/Characters/&lt;Kind&gt;</c>) are drawn by
    /// <see cref="ActorRenderer"/>: sheet paths, animation names, emissive tints, telegraph flashes,
    /// readability fill, hurt window and the corpse hold/fade after a death.
    /// Emissive channels: R = weapon/attack glow, G = eyes, B = tech strips (tinted by kind so each
    /// archetype keeps the colour code the old code-drawn sprites had).
    /// </summary>
    /// <example>
    /// <code>
    /// var anims = EnemyAnimations.Load(EnemyKind.Brute);
    /// strips.color = EnemyLook.StripColor(EnemyKind.Brute);
    /// </code>
    /// </example>
    public static class EnemyLook
    {
        /// <summary>Resources path of a kind's sheet, without suffix.</summary>
        public static string SheetPath(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Grunt: return "Art/Characters/Grunt/grunt";
                case EnemyKind.Brute: return "Art/Characters/Brute/brute";
                case EnemyKind.Shooter: return "Art/Characters/Shooter/shooter";
                case EnemyKind.Shield: return "Art/Characters/Shield/shield";
                case EnemyKind.Kamikaze: return "Art/Characters/Kamikaze/kamikaze";
                case EnemyKind.Summoner: return "Art/Characters/Summoner/summoner";
                default: return "Art/Characters/Drone/drone";
            }
        }

        /// <summary>The drone sheet is pivoted on the body centre; every other sheet on the feet.</summary>
        public static bool CentrePivot(EnemyKind kind) => kind == EnemyKind.Drone;

        /// <summary>Animation names in the sheet JSONs.</summary>
        public const string Idle = "idle", Walk = "walk", Hurt = "hurt", Death = "death",
            Windup = "windup", Charge = "charge", Crashed = "crashed",
            Hover = "hover", Dive = "dive",
            Rush = "rush", Lunge = "lunge", Fuse = "fuse",
            Turn = "turn", Block = "block",
            Aim = "aim", Fire = "fire",
            Flee = "flee", Cast = "cast", Attack = "attack";

        /// <summary>Kind colours (strips, halo glow): the same hues the old code-drawn enemies used.</summary>
        public static readonly Color GruntColor = new Color(1f, 0.55f, 0.15f);
        public static readonly Color BruteArmored = new Color(0.55f, 0.65f, 0.82f);
        public static readonly Color BruteBreakable = new Color(1f, 0.2f, 0.28f);
        public static readonly Color DroneColor = new Color(0.78f, 0.45f, 1f);
        public static readonly Color ShooterColor = new Color(0.5f, 1f, 0.4f);
        public static readonly Color ShieldColor = new Color(0.95f, 0.6f, 0.35f);
        public static readonly Color KamikazeColor = new Color(1f, 0.22f, 0.2f);
        public static readonly Color SummonerColor = new Color(0.55f, 0.4f, 1f);
        /// <summary>Telegraph colour (brute/shooter wind-up flash and glow).</summary>
        public static readonly Color WindupColor = new Color(1f, 0.85f, 0.4f);

        /// <summary>R channel default: hostile amber weapon/attack glow.</summary>
        public static readonly Color HostileAmber = new Color(1f, 0.62f, 0.18f);
        /// <summary>G channel: eyes / visor (hostile red).</summary>
        public static readonly Color Eyes = new Color(1f, 0.22f, 0.25f);
        /// <summary>Strip colour = kind colour mixed this far toward white.</summary>
        public const float StripWhiteMix = 0.2f;

        /// <summary>Kind colour (strips and halo) for the live state.</summary>
        public static Color KindColor(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Grunt: return GruntColor;
                case EnemyKind.Brute: return BruteArmored;
                case EnemyKind.Shooter: return ShooterColor;
                case EnemyKind.Shield: return ShieldColor;
                case EnemyKind.Kamikaze: return KamikazeColor;
                case EnemyKind.Summoner: return SummonerColor;
                default: return DroneColor;
            }
        }

        /// <summary>R tint per kind: hostile amber, except the kamikaze's red charges.</summary>
        public static Color AttackColor(EnemyKind kind) => kind == EnemyKind.Kamikaze ? KamikazeColor : HostileAmber;

        /// <summary>
        /// Unlit copy of the colour frame drawn over the lit body at this alpha: a floor of
        /// brightness so the dark sheets read against the night scene away from lamps.
        /// The same renderer carries hit and telegraph flashes (alpha up to 1).
        /// </summary>
        public const float FillAlpha = 0.42f;

        /// <summary>Halo glow behind an enemy: alpha (normal, brute armored), size in body heights.</summary>
        public const float HaloAlpha = 0.2f, HaloAlphaArmored = 0.15f, HaloSize = 1.8f;

        /// <summary>Seconds after a hit that the white hit flash and the hit pop show.</summary>
        public const float HitFlashTime = 0.08f, HitPopTime = 0.12f;
        /// <summary>Hit pop scale.</summary>
        public const float HitPop = 1.12f;

        /// <summary>|Vx| below this is standing (idle); walk cycle rate = |Vx| / Speed clamped to this range.</summary>
        public const float WalkMinSpeed = 0.3f, WalkRateMin = 0.6f, WalkRateMax = 1.6f;

        /// <summary>Non-looping telegraphs are time-scaled to their configured duration, within this rate range.</summary>
        public const float TelegraphRateMin = 0.4f, TelegraphRateMax = 3f;

        /// <summary>
        /// Grunt thrust (presentation only - grunts deal contact damage): plays when a grounded grunt faces the
        /// player within this horizontal / vertical distance (world units, feet to feet), then waits
        /// <see cref="GruntStrikeGap"/> seconds before it may thrust again.
        /// </summary>
        public const float GruntStrikeRange = 1.7f, GruntStrikeHeight = 1.0f, GruntStrikeGap = 0.3f;
        /// <summary>Largest feet-height difference (world units) between the player and a thrusting grunt: the player
        /// must be on the grunt's level, not passing just below its platform.</summary>
        public const float GruntStrikeFeetBand = 0.5f;

        /// <summary>Drone corpses (killed in the air) fall at this gravity (units/s^2) until they land on a solid tile.</summary>
        public const float DroneCorpseGravity = 30f;

        /// <summary>After a death animation ends: hold the last frame, then fade out (seconds).</summary>
        public const float CorpseHold = 1f, CorpseFade = 0.5f;

        /// <summary>A ShieldBlock event within this distance of a shield bearer's centre plays its block.</summary>
        public const float BlockMatchRadius = 1.4f;

        /// <summary>Sorting orders: halo, body, emissive/fill overlays; corpses draw beneath live enemies.</summary>
        public const int HaloOrder = 7, BodyOrder = 8, OverlayOrder = 9, CorpseBodyOrder = 6, CorpseOverlayOrder = 7;
    }

    /// <summary>
    /// One enemy kind's animations, resolved once from its sheet into role slots the renderer
    /// maps sim state onto. Slots a kind does not have are null.
    /// </summary>
    /// <example>
    /// <code>
    /// var a = EnemyAnimations.Load(EnemyKind.Shooter);
    /// animator.Play(e.Act == EnemyAct.Windup ? a.Telegraph : a.Idle);
    /// </code>
    /// </example>
    public sealed class EnemyAnimations
    {
        /// <summary>Standing / hovering loop.</summary>
        public SheetAnimation Idle;
        /// <summary>Moving loop (walk, rush, flee; drone hover).</summary>
        public SheetAnimation Move;
        /// <summary>Hit reaction (null for the kamikaze).</summary>
        public SheetAnimation Hurt;
        /// <summary>One-shot death.</summary>
        public SheetAnimation Death;
        /// <summary>Telegraph: brute/drone windup, shooter aim, summoner cast, kamikaze fuse, shield turn.</summary>
        public SheetAnimation Telegraph;
        /// <summary>Attack: brute charge, drone dive, shooter fire, kamikaze lunge, grunt thrust (null if the sheet has none).</summary>
        public SheetAnimation Attack;
        /// <summary>Brute crashed; shield block.</summary>
        public SheetAnimation Special;

        /// <summary>Loads (cached) the kind's sheet and fills the role slots.</summary>
        public static EnemyAnimations Load(EnemyKind kind)
        {
            var s = CharacterSheet.Load(EnemyLook.SheetPath(kind));
            var a = new EnemyAnimations { Death = s.Animation(EnemyLook.Death) };
            s.TryGetAnimation(EnemyLook.Hurt, out a.Hurt);
            switch (kind)
            {
                case EnemyKind.Brute:
                    a.Idle = s.Animation(EnemyLook.Idle);
                    a.Move = s.Animation(EnemyLook.Walk);
                    a.Telegraph = s.Animation(EnemyLook.Windup);
                    a.Attack = s.Animation(EnemyLook.Charge);
                    a.Special = s.Animation(EnemyLook.Crashed);
                    break;
                case EnemyKind.Drone:
                    a.Idle = a.Move = s.Animation(EnemyLook.Hover);
                    a.Telegraph = s.Animation(EnemyLook.Windup);
                    a.Attack = s.Animation(EnemyLook.Dive);
                    break;
                case EnemyKind.Kamikaze:
                    a.Idle = s.Animation(EnemyLook.Idle);
                    a.Move = s.Animation(EnemyLook.Rush);
                    a.Telegraph = s.Animation(EnemyLook.Fuse);
                    a.Attack = s.Animation(EnemyLook.Lunge);
                    break;
                case EnemyKind.Shield:
                    a.Idle = s.Animation(EnemyLook.Idle);
                    a.Move = s.Animation(EnemyLook.Walk);
                    a.Telegraph = s.Animation(EnemyLook.Turn);
                    a.Special = s.Animation(EnemyLook.Block);
                    break;
                case EnemyKind.Shooter:
                    a.Idle = s.Animation(EnemyLook.Idle);
                    a.Move = s.Animation(EnemyLook.Walk);
                    a.Telegraph = s.Animation(EnemyLook.Aim);
                    a.Attack = s.Animation(EnemyLook.Fire);
                    break;
                case EnemyKind.Summoner:
                    a.Idle = s.Animation(EnemyLook.Idle);
                    a.Move = s.Animation(EnemyLook.Flee);
                    a.Telegraph = s.Animation(EnemyLook.Cast);
                    break;
                default:
                    a.Idle = s.Animation(EnemyLook.Idle);
                    a.Move = s.Animation(EnemyLook.Walk);
                    s.TryGetAnimation(EnemyLook.Attack, out a.Attack); // grunt spear thrust (optional)
                    break;
            }
            return a;
        }
    }
}
