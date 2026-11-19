using System.Collections.Generic;
using Redline.Core;
using Redline.Game.Art;
using UnityEngine;

namespace Redline.Game.Render3D.Actors3D
{
    /// <summary>
    /// 3D-scene version of the enemy half of <see cref="ActorRenderer"/>: each of the seven enemy kinds is a
    /// camera-facing quad on the play plane (z = 0) drawn with URP Lit (alpha clip, the sheet's normal map, casts
    /// shadows), so the scene lamps and the world's back rim light (rendering layer bit 1) light it, plus one unlit
    /// overlay quad (HDR emissive masks that bloom, readability fill, a cool silhouette rim, hit and telegraph
    /// flashes) and an additive halo. Same state-to-animation mapping, telegraphs, hit pop, brute armour state,
    /// shield side/turn/block and corpses (death plays, holds <see cref="EnemyLook.CorpseHold"/> s, fades; kamikaze
    /// blasts leave none) as the 2D renderer.
    /// </summary>
    /// <example>
    /// <code>
    /// var enemies = new Enemy3DRenderer(worldRoot, effects3D);
    /// enemies.Sync(sim, interpolatedPlayerFeet, enemyFeet, Time.deltaTime);   // once per frame
    /// </code>
    /// </example>
    /// <remarks>
    /// Main-thread only. Frames are selected by UV rect through a MaterialPropertyBlock (no per-frame materials and
    /// no allocations after the pools are warm). Live views are pooled per sim slot; corpses in a growing pool.
    /// Sheets load through <see cref="CharacterSheet"/> (Resources), so it works in builds.
    /// </remarks>
    public sealed class Enemy3DRenderer
    {
        // Overlay tuning (HDR: values above ~1 pass the bloom threshold of the 3D volume).
        /// <summary>Emissive multipliers: R attack glow, G eyes, B strips.</summary>
        public const float AttackHdr = 2.6f, EyesHdr = 6.4f, StripHdr = 1.1f;
        /// <summary>
        /// The shield bearer's B mask is a closed 1-px loop around the whole plate (authored "shield rim strip").
        /// At strip HDR it out-blooms the plate and reads as a hollow rectangle; kept below the bloom threshold.
        /// </summary>
        public const float ShieldStripHdr = 0.55f;
        /// <summary>Unlit colour floor: much lower than the 2D FillAlpha because the 3D lamps light the body.</summary>
        public const float Fill3D = 0.55f;
        /// <summary>Telegraph and hit flash brightness (HDR so the blinks bloom).</summary>
        public const float FlashHdr = 1.6f;
        /// <summary>
        /// Thin cool backlight on the sprite's OUTER silhouette only (1 texel, found from alpha in the shader; interior
        /// normal-map bevels no longer rim). Kept near the bloom threshold so enemies never out-glow the lit scene;
        /// matches the ronin's fresnel edge (RoninView.SilhouetteRimColor) in hue.
        /// </summary>
        public static readonly Color RimColor = new Color(0f, 0f, 0f); // rim off: a 1-texel edge flickers in motion (user feedback 2026-11-04)
        /// <summary>Rim on side edges relative to top edges (1); bottom edges get none. The scene rims sit behind and above.</summary>
        public const float RimSideWeight = 0.3f;
        /// <summary>Additive halo brightness relative to the 2D halo alpha.</summary>
        public const float HaloHdr = 1.3f;

        private static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
        private static readonly int FrameSt = Shader.PropertyToID("_FrameST");
        private static readonly int TintR = Shader.PropertyToID("_TintR");
        private static readonly int TintG = Shader.PropertyToID("_TintG");
        private static readonly int TintB = Shader.PropertyToID("_TintB");
        private static readonly int FillId = Shader.PropertyToID("_Fill");
        private static readonly int FlashId = Shader.PropertyToID("_Flash");
        private static readonly int RimId = Shader.PropertyToID("_Rim");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ParamsId = Shader.PropertyToID("_Params");

        /// <summary>Per-kind sheet data and materials.</summary>
        private sealed class KindData
        {
            public EnemyAnimations Anims;
            public Material Lit, CorpseLit, Overlay;
        }

        private readonly Transform _root;
        private readonly KindData[] _kinds = new KindData[7];
        private readonly List<View> _views = new List<View>(32);
        private readonly List<View> _corpses = new List<View>(16);
        private readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        private object _lastSim;
        private float _time, _fuseTime;

        private readonly Effects3DRenderer _effects;

        /// <summary>Creates the enemy views under <paramref name="parent"/>. <paramref name="effects"/> gets the corpse landing dust (optional).</summary>
        public Enemy3DRenderer(Transform parent, Effects3DRenderer effects = null)
        {
            _effects = effects;
            _root = new GameObject("Enemies3D").transform;
            _root.SetParent(parent, false);
            for (int k = 0; k < _kinds.Length; k++) _kinds[k] = LoadKind((EnemyKind)k);
        }

        private static KindData LoadKind(EnemyKind kind)
        {
            var anims = EnemyAnimations.Load(kind);
            var color = anims.Idle.Frame(0).texture;
            var normal = Resources.Load<Texture2D>(EnemyLook.SheetPath(kind) + "_normal");
            string name = kind.ToString();
            var overlay = new Material(Actors3DAssets.Overlay) { name = name + "Overlay3D" };
            overlay.SetTexture("_ColorTex", color);
            if (normal != null) overlay.SetTexture("_BumpTex", normal);
            for (int c = 0; c < 3; c++)
            {
                var mask = FindMask(anims, (EmissiveChannel)c);
                overlay.SetTexture(c == 0 ? "_MaskR" : c == 1 ? "_MaskG" : "_MaskB", mask != null ? mask : Texture2D.blackTexture);
            }
            overlay.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 5;
            return new KindData
            {
                Anims = anims,
                Lit = Actors3DAssets.SpriteLit(name + "Lit3D", color, normal, false),
                CorpseLit = Actors3DAssets.SpriteLit(name + "Corpse3D", color, normal, true),
                Overlay = overlay
            };
        }

        /// <summary>The per-channel mask atlas CharacterSheet split from the emissive sheet (any frame that has one).</summary>
        private static Texture FindMask(EnemyAnimations a, EmissiveChannel ch)
        {
            var list = new[] { a.Idle, a.Move, a.Hurt, a.Death, a.Telegraph, a.Attack, a.Special };
            foreach (var anim in list)
            {
                if (anim == null) continue;
                for (int i = 0; i < anim.FrameCount; i++)
                {
                    var s = anim.Emissive(ch, i);
                    if (s != null) return s.texture;
                }
            }
            return null;
        }

        /// <summary>One drawn enemy (live slot or pooled corpse).</summary>
        private sealed class View
        {
            public readonly Transform Root, Quad;
            public readonly MeshRenderer Body, Overlay, Halo;
            /// <summary>Health bar over a damaged enemy: dark back and neon fill (null on corpses).</summary>
            public readonly MeshRenderer HpBack, HpFill;
            public readonly SpriteAnimator Anim = new SpriteAnimator();
            public Enemy Enemy;
            public EnemyKind Kind;
            public bool WasAlive, Flip;
            public EnemyAct LastAct;
            public float LastSinceHit = 99f, FireTimer, BlockTimer, AttackTimer, AttackGap;
            public bool Active;
            public float Held, X, Y, Height, Vy;
            public bool Landed;
            /// <summary>Corpse flight: horizontal speed, tilt (degrees) and its rate, bounces left, dust pending.</summary>
            public float Vx, Tilt, TiltVel;
            public int Bounces;
            public bool Thumped;

            public View(Transform parent, bool corpse)
            {
                Root = new GameObject(corpse ? "Corpse3D" : "Enemy3D").transform;
                Root.SetParent(parent, false);
                Quad = new GameObject("quad").transform;
                Quad.SetParent(Root, false);
                Body = Actors3DAssets.CreateQuad("body", Quad, null, !corpse, true);
                Overlay = Actors3DAssets.CreateQuad("overlay", Quad, null, false, true);
                // In front of the body (camera at -z) so the additive layer is not depth-rejected by it.
                Overlay.transform.localPosition = new Vector3(0f, 0f, -0.02f);
                if (!corpse) Halo = Actors3DAssets.CreateQuad("halo", parent, Actors3DAssets.FxAdditive, false, false);
                if (!corpse)
                {
                    HpBack = Actors3DAssets.CreateQuad("hpBack", parent, Actors3DAssets.FxSolid, false, false);
                    HpFill = Actors3DAssets.CreateQuad("hpFill", parent, Actors3DAssets.FxSolid, false, false);
                }
            }

            public void Hide()
            {
                Body.enabled = Overlay.enabled = false;
                if (Halo != null) Halo.enabled = false;
                if (HpBack != null) HpBack.enabled = HpFill.enabled = false;
            }
        }

        /// <summary>Updates every enemy view from the simulation (same signature as <see cref="ActorRenderer.Sync"/>).</summary>
        public void Sync(RedlineSim sim, Vector2 playerFeet, IReadOnlyList<Vector2> enemyFeet, float dt)
        {
            _playerGrounded = sim.Player.Grounded;
            SyncRaw(sim, sim.Enemies, sim.Events, sim.Config, sim.Map, sim.Player.X, playerFeet, sim.PlayerTier, enemyFeet, dt);
        }

        /// <summary>
        /// The same update from explicit inputs (used by <see cref="Sync"/> and by the capture, which stages
        /// enemies without a running sim). <paramref name="key"/> identifies the level: a new key clears corpses.
        /// </summary>
        internal void SyncRaw(object key, IReadOnlyList<Enemy> enemies, IReadOnlyList<SimEvent> events, RedlineConfig cfg,
            TileMap map, float playerX, Vector2 playerFeet, int tier, IReadOnlyList<Vector2> feet, float dt)
        {
            _time += dt;
            _fuseTime = cfg.kamikazeFuseTime;
            if (key != _lastSim)
            {
                _lastSim = key;
                for (int i = 0; i < _corpses.Count; i++) { _corpses[i].Active = false; _corpses[i].Hide(); }
            }
            while (_views.Count < enemies.Count) _views.Add(new View(_root, false));
            for (int i = 0; i < _views.Count; i++)
            {
                var v = _views[i];
                if (i >= enemies.Count) { v.Enemy = null; v.Hide(); continue; }
                var e = enemies[i];
                if (v.Enemy != e) Bind(v, e);
                if (!e.Alive)
                {
                    bool exploded = e.Kind == EnemyKind.Kamikaze && v.LastAct == EnemyAct.Fuse && e.Act == EnemyAct.Idle;
                    if (v.WasAlive && !exploded) SpawnCorpse(v, e, map, playerX);
                    v.WasAlive = false;
                    v.Hide();
                    continue;
                }
                var f = feet != null && i < feet.Count ? feet[i] : new Vector2(e.X, e.Y);
                SyncEnemy(v, e, events, cfg, map, playerX, playerFeet, f, tier, i, dt);
                v.LastAct = e.Act;
                v.LastSinceHit = e.SinceHit;
            }
            SyncCorpses(map, playerX, playerFeet, dt);
        }

        private void Bind(View v, Enemy e)
        {
            v.Enemy = e;
            v.Kind = e.Kind;
            v.WasAlive = e.Alive;
            v.LastAct = e.Act;
            v.LastSinceHit = e.SinceHit;
            v.FireTimer = v.BlockTimer = v.AttackTimer = v.AttackGap = 0f;
            v.Flip = (e.Kind == EnemyKind.Shield ? e.ShieldDir : e.Facing) < 0;
            v.Anim.Play(_kinds[(int)e.Kind].Anims.Idle, true);
        }

        // Corpse physics (presentation only): the killing blow throws the body away from the player in an arc; it
        // tumbles a little, the death clip plays slowed while airborne, it lands with a thump of dust, bounces once
        // if it came down hard, skids to a stop, lies there, then dithers out.
        /// <summary>Corpse launch: horizontal speed range (u/s) away from the player, upward speed (u/s).</summary>
        public static float CorpseThrowMin = 3.5f, CorpseThrowMax = 9f, CorpseLift = 6.5f;
        /// <summary>Corpse gravity (u/s^2), ground friction while skidding (u/s^2), tilt rate in flight (deg/s).</summary>
        public static float CorpseGravity = 34f, CorpseFriction = 16f, CorpseTiltRate = 140f;
        /// <summary>Seconds a landed corpse lies before fading, and the fade time.</summary>
        public static float CorpseLie = 2.2f, CorpseFadeTime = 0.9f;
        /// <summary>Death clip playback rate while the body is still in the air (lets the fall read).</summary>
        public static float CorpseAirAnimRate = 0.6f;

        private void SpawnCorpse(View from, Enemy e, TileMap map, float playerX)
        {
            View c = null;
            for (int i = 0; i < _corpses.Count; i++) if (!_corpses[i].Active) { c = _corpses[i]; break; }
            if (c == null) { c = new View(_root, true); _corpses.Add(c); }
            c.Active = true;
            c.Kind = e.Kind;
            c.X = e.X;
            c.Y = e.Y;
            c.Height = e.Height;
            c.Held = 0f;
            // Thrown away from the player, harder the faster the body was already moving (knockback from the hit).
            float away = map.Dx(playerX, e.X);
            int dir = away > 0.01f ? 1 : away < -0.01f ? -1 : (e.Vx >= 0f ? 1 : -1);
            float speed = Mathf.Clamp(Mathf.Abs(e.Vx) * 0.8f + CorpseThrowMin, CorpseThrowMin, CorpseThrowMax);
            c.Vx = dir * speed;
            c.Vy = Mathf.Max(e.Vy, 0f) * 0.5f + CorpseLift * (e.Kind == EnemyKind.Brute ? 0.6f : 1f);
            // Falls backwards: faces the player (the killer) while flying away from him.
            c.Flip = dir > 0;
            c.Tilt = 0f;
            c.TiltVel = dir * CorpseTiltRate * (e.Kind == EnemyKind.Brute ? 0.5f : 1f);
            c.Bounces = 1;
            c.Thumped = false;
            c.Landed = false;
            c.Anim.Play(_kinds[(int)e.Kind].Anims.Death, true);
        }

        private void SyncCorpses(TileMap map, float playerX, Vector2 playerFeet, float dt)
        {
            for (int i = 0; i < _corpses.Count; i++)
            {
                var c = _corpses[i];
                if (!c.Active) continue;
                float drawX = playerFeet.x + map.Dx(playerX, c.X);
                MoveCorpse(c, map, dt, drawX);
                c.Anim.Tick(dt, c.Landed ? 1f : CorpseAirAnimRate);
                if (c.Anim.Finished && c.Landed && Mathf.Abs(c.Vx) < 0.05f) c.Held += dt;
                if (c.Held >= CorpseLie + CorpseFadeTime) { c.Active = false; c.Hide(); continue; }
                float alpha = 1f - Mathf.Clamp01((c.Held - CorpseLie) / CorpseFadeTime);
                float y = c.Y + (EnemyLook.CentrePivot(c.Kind) ? c.Height * 0.5f : 0f);
                Place(c, new Vector3(drawX, y, 0f), 1f, Quaternion.Euler(0f, 0f, -c.Tilt));
                var k = _kinds[(int)c.Kind];
                Draw(c, k.CorpseLit, k.Overlay, EnemyLook.AttackColor(c.Kind) * AttackHdr, EnemyLook.Eyes * EyesHdr,
                    StripColor(c.Kind, EnemyLook.KindColor(c.Kind)), new Color(1f, 1f, 1f, EnemyLook.FillAlpha), alpha);
            }
        }

        /// <summary>
        /// Flight, landing and skid of a corpse (body box bottom at c.Y, centre c.X): gravity, wall stops, one bounce
        /// when it lands hard, then ground friction; the in-flight tilt settles back to level on the ground.
        /// </summary>
        private void MoveCorpse(View c, TileMap map, float dt, float drawX)
        {
            const float halfW = 0.3f, h = 0.6f;
            // Horizontal: stop against walls.
            if (Mathf.Abs(c.Vx) > 0.001f)
            {
                float nx = c.X + c.Vx * dt;
                if (map.OverlapsSolid(nx - halfW, c.Y + 0.05f, nx + halfW, c.Y + h)) c.Vx = 0f;
                else c.X = nx;
            }
            if (c.Landed)
            {
                c.Vx = Mathf.MoveTowards(c.Vx, 0f, CorpseFriction * dt);
                c.Tilt = Mathf.MoveTowards(c.Tilt, 0f, 220f * dt);
                // Walked off a ledge while skidding: fall again.
                if (!map.OverlapsSolid(c.X - halfW, c.Y - 0.08f, c.X + halfW, c.Y - 0.01f) && c.Y > 0.5f) c.Landed = false;
                return;
            }
            c.Vy -= CorpseGravity * dt;
            c.Tilt = Mathf.Clamp(c.Tilt + c.TiltVel * dt, -35f, 35f);
            float y = c.Y + c.Vy * dt;
            if (c.Vy < 0f && map.OverlapsSolid(c.X - halfW, y, c.X + halfW, c.Y))
            {
                c.Y = Mathf.Floor(c.Y + 1e-3f); // tile tops are whole units
                float impact = -c.Vy;
                if (!c.Thumped && _effects != null)
                {
                    _effects.Dust(new Vector2(drawX, c.Y), impact > 8f ? 7 : 4, c.Vx);
                    c.Thumped = true;
                }
                if (c.Bounces > 0 && impact > 7f)
                {
                    c.Bounces--;
                    c.Vy = impact * 0.28f;
                    c.Vx *= 0.55f;
                    c.TiltVel *= -0.5f;
                    return;
                }
                c.Vy = 0f;
                c.Vx *= 0.7f;
                c.Landed = true;
                return;
            }
            c.Y = y;
            if (c.Y < -2f) { c.Landed = true; c.Vx = 0f; } // fell out of the map: stop simulating
        }

        /// <summary>Player grounded in the last <see cref="Sync"/> (true for <see cref="SyncRaw"/>-only captures).</summary>
        private bool _playerGrounded = true;

        /// <summary>
        /// Grunt thrust trigger (presentation only): grounded, facing the player, within strike range, not
        /// reeling from a hit, and the player on the grunt's level: feet within <see cref="EnemyLook.GruntStrikeFeetBand"/>
        /// of the grunt's, and grounded or above the same floor (a player passing under the grunt's platform does not
        /// trigger it). One thrust plays to its end, then a short gap before the next.
        /// </summary>
        private static void UpdateGruntStrike(View v, Enemy e, EnemyAnimations a, TileMap map, float playerX,
            Vector2 playerFeet, Vector2 feet, bool playerGrounded, float dt, out bool started)
        {
            started = false;
            if (a.Attack == null) return;
            v.AttackGap = Mathf.Max(0f, v.AttackGap - dt);
            if (v.AttackTimer > 0f)
            {
                v.AttackTimer -= dt;
                if (v.AttackTimer <= 0f) { v.AttackTimer = 0f; v.AttackGap = EnemyLook.GruntStrikeGap; }
                return;
            }
            if (v.AttackGap > 0f || !e.Grounded || e.SinceHit < a.Hurt?.Duration) return;
            float dx = map.Dx(e.X, playerX);
            bool inRange = Mathf.Abs(dx) <= EnemyLook.GruntStrikeRange
                           && Mathf.Abs(playerFeet.y - feet.y) <= EnemyLook.GruntStrikeFeetBand
                           && (dx == 0f || (dx > 0f ? 1 : -1) == e.Facing);
            if (!inRange) return;
            // Airborne: only over the grunt's own floor (solid just below the grunt's feet height under the player).
            float px = map.WrapX(playerX);
            if (!playerGrounded && !map.OverlapsSolid(px - 0.1f, e.Y - 0.2f, px + 0.1f, e.Y - 0.02f)) return;
            v.AttackTimer = a.Attack.Duration;
            started = true;
        }

        private void SyncEnemy(View v, Enemy e, IReadOnlyList<SimEvent> events, RedlineConfig cfg, TileMap map,
            float playerX, Vector2 playerFeet, Vector2 feet, int tier, int index, float dt)
        {
            var kd = _kinds[(int)e.Kind];
            var a = kd.Anims;
            bool newHit = e.SinceHit < v.LastSinceHit;
            v.FireTimer = Mathf.Max(0f, v.FireTimer - dt);
            v.BlockTimer = Mathf.Max(0f, v.BlockTimer - dt);
            bool fired = e.Kind == EnemyKind.Shooter && v.LastAct == EnemyAct.Windup && e.Act == EnemyAct.Idle
                         && e.SinceHit > EnemyLook.HitPopTime;
            if (fired) v.FireTimer = a.Attack.Duration;
            bool blocked = e.Kind == EnemyKind.Shield && v.BlockTimer <= 0f && ShieldBlocked(map, events, e);
            if (blocked) v.BlockTimer = a.Special.Duration;
            bool thrust = false;
            if (e.Kind == EnemyKind.Grunt) UpdateGruntStrike(v, e, a, map, playerX, playerFeet, feet, _playerGrounded, dt, out thrust);

            var anim = PickAnimation(v, e, a, cfg, out float rate);
            bool restart = fired || blocked || thrust || (anim == a.Hurt && newHit);
            v.Anim.Play(anim, restart);
            v.Anim.Tick(dt, rate);
            v.Flip = Flip(map, playerX, e);

            bool brute = e.Kind == EnemyKind.Brute;
            bool breakable = brute && CombatRules.CanDamage(tier, e.ArmorTier);
            var kindColor = brute ? (breakable ? EnemyLook.BruteBreakable : EnemyLook.BruteArmored) : EnemyLook.KindColor(e.Kind);
            var strips = StripColor(e.Kind, kindColor);
            if (breakable) strips *= 0.75f + 0.25f * Mathf.Sin(_time * 14f);
            bool windup = (brute && e.Mode == BruteMode.Windup)
                          || (e.Kind == EnemyKind.Drone && e.DroneState == DroneMode.Windup)
                          || (e.Kind == EnemyKind.Shooter && e.Act == EnemyAct.Windup)
                          || (e.Kind == EnemyKind.Summoner && e.Act == EnemyAct.Cast);
            var attack = EnemyLook.AttackColor(e.Kind);
            float attackHdr = AttackHdr;
            if (windup || FuseProgress(e) > 0f)
            {
                attack = Color.Lerp(attack, Color.white, 0.3f + 0.3f * Mathf.Sin(_time * 24f));
                attackHdr *= 1.6f;
            }

            float pop = (e.SinceHit < EnemyLook.HitPopTime ? EnemyLook.HitPop : 1f) * FuseSwell(e);
            var offset = Offset(e, index);
            float lift = EnemyLook.CentrePivot(e.Kind) ? e.Height * 0.5f : 0f;
            var rot = brute && e.Mode == BruteMode.Crashed ? Quaternion.Euler(0f, 0f, Mathf.Sin(_time * 9f) * 6f) : Quaternion.identity;
            Place(v, new Vector3(feet.x + offset.x, feet.y + lift + offset.y, 0f), pop, rot);
            Draw(v, kd.Lit, kd.Overlay, attack * attackHdr, EnemyLook.Eyes * EyesHdr, strips, FillColor(e), 1f);

            // Halo behind the body: kind glow; telegraphs swell it, a lit fuse shows the blast radius.
            var hc = kindColor;
            float ha = breakable ? 0.3f + 0.15f * Mathf.Sin(_time * 14f) : brute ? EnemyLook.HaloAlphaArmored : EnemyLook.HaloAlpha;
            float hs = e.Height * (breakable ? 2.4f : EnemyLook.HaloSize);
            if (Telegraphing(e) || windup)
            {
                bool fuse = e.Kind == EnemyKind.Kamikaze;
                hc = fuse ? EnemyLook.KamikazeColor : EnemyLook.WindupColor;
                ha = 0.3f + 0.15f * Mathf.Sin(_time * (fuse ? 40f : 20f));
                hs = fuse ? cfg.kamikazeBlastRadius * 2f * 1.4f : e.Height * 2.6f;
            }
            var halo = v.Halo;
            halo.enabled = true;
            var ht = halo.transform;
            ht.localPosition = new Vector3(feet.x + offset.x, feet.y + offset.y + e.Height * 0.5f * pop, 0.25f);
            ht.localScale = new Vector3(hs, hs, 1f);
            _mpb.Clear();
            _mpb.SetColor(ColorId, new Color(hc.r * HaloHdr, hc.g * HaloHdr, hc.b * HaloHdr, ha));
            _mpb.SetVector(ParamsId, new Vector4(1f, 0.6f, 0f, 0f));
            halo.SetPropertyBlock(_mpb);

            SyncHealthBar(v, e, new Vector2(feet.x + offset.x, feet.y + offset.y));
        }

        /// <summary>Health bar size (world units), gap over the head, and colours. Shown only once the enemy is hurt.</summary>
        public static float HpBarWidth = 0.9f, HpBarHeight = 0.09f, HpBarGap = 0.3f;
        public static Color HpBackColor = new Color(0.05f, 0.03f, 0.06f), HpFillColor = new Color(1f, 0.12f, 0.22f) * 2.2f;

        private void SyncHealthBar(View v, Enemy e, Vector2 feet)
        {
            bool show = e.Hp < e.MaxHp - 0.001f && e.Hp > 0f;
            v.HpBack.enabled = v.HpFill.enabled = show;
            if (!show) return;
            float frac = Mathf.Clamp01(e.Hp / e.MaxHp);
            float y = feet.y + e.Height + HpBarGap;
            float x0 = feet.x - HpBarWidth * 0.5f;
            Bar(v.HpBack, new Vector3(feet.x, y, -0.05f), new Vector2(HpBarWidth + 0.06f, HpBarHeight + 0.06f), HpBackColor);
            float w = HpBarWidth * frac;
            Bar(v.HpFill, new Vector3(x0 + w * 0.5f, y, -0.07f), new Vector2(w, HpBarHeight), HpFillColor);
        }

        private void Bar(MeshRenderer r, Vector3 pos, Vector2 size, Color c)
        {
            var t = r.transform;
            t.localPosition = pos;
            t.localRotation = Quaternion.identity;
            t.localScale = new Vector3(size.x, size.y, 1f);
            _mpb.Clear();
            _mpb.SetColor(ColorId, new Color(c.r, c.g, c.b, 1f));
            _mpb.SetVector(ParamsId, new Vector4(0f, 0f, 0f, 0f));
            r.SetPropertyBlock(_mpb);
        }

        /// <summary>Sprite texels per world unit; quads snap to this grid so they line up 1:1 with the
        /// 480×240 / FramingHeight 5.0 render target (48 texels per unit) and never crawl while moving.</summary>
        private const float PixelGrid = Pixel3DCamera.TexelsPerUnit;

        private static void Place(View v, Vector3 pos, float pop, Quaternion rot)
        {
            pos.x = global::Redline.Core.Presentation.PixelSnap.Snap(pos.x, PixelGrid);   // same grid and rounding as the ronin
            pos.y = global::Redline.Core.Presentation.PixelSnap.Snap(pos.y, PixelGrid);
            v.Root.localPosition = pos;
            v.Root.localRotation = rot;
            v.Root.localScale = new Vector3(v.Flip ? -pop : pop, pop, 1f);
        }

        /// <summary>Frame by UV rect; body (Lit) and overlay (emissive/fill/rim/flash) via one shared property block.</summary>
        private void Draw(View v, Material lit, Material overlay, Color attack, Color eyes, Color strips, Color fill, float alpha)
        {
            var anim = v.Anim.Current;
            var sprite = anim.Frame(v.Anim.Frame);
            var tex = sprite.texture;
            var r = sprite.rect;
            float ppu = sprite.pixelsPerUnit;
            float w = r.width / ppu, h = r.height / ppu;
            var piv = sprite.pivot; // pixels from the rect's bottom-left
            v.Quad.localPosition = new Vector3((r.width * 0.5f - piv.x) / ppu, (r.height * 0.5f - piv.y) / ppu, 0f);
            v.Quad.localScale = new Vector3(w, h, 1f);
            var st = new Vector4(r.width / tex.width, r.height / tex.height, r.x / tex.width, r.y / tex.height);

            v.Body.sharedMaterial = lit;
            v.Body.enabled = true;
            _mpb.Clear();
            _mpb.SetVector(BaseMapSt, st);
            _mpb.SetColor(BaseColor, new Color(1f, 1f, 1f, alpha));
            // Lit clips albedo.a * _BaseColor.a against _Cutoff; scaling the cutoff keeps the 0.5 texture-alpha edge while
            // the corpse material (Actors3D/SpriteLitDither) dithers the body out by _BaseColor.a.
            _mpb.SetFloat(CutoffId, Actors3DAssets.DitherCutoff * alpha);
            v.Body.SetPropertyBlock(_mpb);

            // 2D fill above the readability floor is a flash (hit white, telegraph blink): it covers the body.
            bool flash = fill.a > EnemyLook.FillAlpha + 0.01f;
            v.Overlay.sharedMaterial = overlay;
            v.Overlay.enabled = true;
            _mpb.Clear();
            _mpb.SetVector(FrameSt, st);
            _mpb.SetColor(TintR, attack);
            _mpb.SetColor(TintG, eyes);
            _mpb.SetColor(TintB, strips);
            _mpb.SetColor(FillId, new Color(1f, 1f, 1f, Fill3D));
            _mpb.SetColor(FlashId, flash ? new Color(fill.r * FlashHdr, fill.g * FlashHdr, fill.b * FlashHdr, fill.a * 0.9f) : Color.clear);
            _mpb.SetColor(RimId, new Color(RimColor.r, RimColor.g, RimColor.b, RimSideWeight));
            _mpb.SetFloat(AlphaId, alpha);
            v.Overlay.SetPropertyBlock(_mpb);
        }

        private static Color StripColor(EnemyKind kind, Color kindColor) =>
            Color.Lerp(kindColor, Color.white, EnemyLook.StripWhiteMix) * (kind == EnemyKind.Shield ? ShieldStripHdr : StripHdr);

        private static SheetAnimation PickAnimation(View v, Enemy e, EnemyAnimations a, RedlineConfig cfg, out float rate)
        {
            rate = 1f;
            switch (e.Kind)
            {
                case EnemyKind.Brute:
                    if (e.Mode == BruteMode.Windup) { rate = TelegraphRate(a.Telegraph, cfg.bruteWindupTime); return a.Telegraph; }
                    if (e.Mode == BruteMode.Charge) return a.Attack;
                    if (e.Mode == BruteMode.Crashed) return a.Special;
                    break;
                case EnemyKind.Drone:
                    if (e.DroneState == DroneMode.Windup) { rate = TelegraphRate(a.Telegraph, cfg.droneWindupTime); return a.Telegraph; }
                    if (e.DroneState == DroneMode.Dive) return a.Attack;
                    break;
                case EnemyKind.Kamikaze:
                    if (e.Act == EnemyAct.Fuse) return a.Telegraph;
                    if (e.Act == EnemyAct.Rush && e.ModeTimer > 0f && !e.Grounded) return a.Attack;
                    break;
                case EnemyKind.Shield:
                    if (v.BlockTimer > 0f) return a.Special;
                    if (e.TurnTimer > 0f) { rate = TelegraphRate(a.Telegraph, cfg.shieldTurnTime); return a.Telegraph; }
                    break;
                case EnemyKind.Shooter:
                    if (e.Act == EnemyAct.Windup) { rate = TelegraphRate(a.Telegraph, cfg.shooterWindupTime); return a.Telegraph; }
                    if (v.FireTimer > 0f) return a.Attack;
                    break;
                case EnemyKind.Summoner:
                    if (e.Act == EnemyAct.Cast) { rate = TelegraphRate(a.Telegraph, cfg.summonerCastTime); return a.Telegraph; }
                    break;
                case EnemyKind.Grunt:
                    if (v.AttackTimer > 0f && a.Attack != null) return a.Attack;
                    break;
            }
            if (a.Hurt != null && e.SinceHit < a.Hurt.Duration) return a.Hurt;
            float speed = Mathf.Abs(e.Vx);
            if (speed >= EnemyLook.WalkMinSpeed && (e.Grounded || e.Kind == EnemyKind.Drone))
            {
                // The kamikaze's move loop is its 12 u/s rush sprint: scale it by the rush speed, not the walk speed.
                float nominal = e.Kind == EnemyKind.Kamikaze ? cfg.kamikazeRushSpeed : e.Speed;
                rate = Mathf.Clamp(speed / Mathf.Max(0.01f, nominal), EnemyLook.WalkRateMin, EnemyLook.WalkRateMax);
                return a.Move;
            }
            return a.Idle;
        }

        private static float TelegraphRate(SheetAnimation a, float seconds) =>
            Mathf.Clamp(a.Duration / Mathf.Max(0.05f, seconds), EnemyLook.TelegraphRateMin, EnemyLook.TelegraphRateMax);

        private static bool Flip(TileMap map, float playerX, Enemy e)
        {
            if (e.Kind == EnemyKind.Shield) return e.ShieldDir < 0;
            if (e.Kind != EnemyKind.Drone) return e.Facing < 0;
            if (e.DroneState == DroneMode.Dive) return e.DiveDir.X < 0f;
            if (Mathf.Abs(e.Vx) > 0.2f) return e.Vx < 0f;
            return map.Dx(e.X, playerX) < 0f;
        }

        private static bool ShieldBlocked(TileMap map, IReadOnlyList<SimEvent> events, Enemy e)
        {
            if (events == null) return false;
            var c = e.Center;
            float r2 = EnemyLook.BlockMatchRadius * EnemyLook.BlockMatchRadius;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].Type != SimEventType.ShieldBlock) continue;
                float dx = map.Dx(c.X, events[i].Position.X);
                float dy = events[i].Position.Y - c.Y;
                if (dx * dx + dy * dy <= r2) return true;
            }
            return false;
        }

        private Color FillColor(Enemy e)
        {
            if (e.SinceHit < EnemyLook.HitFlashTime) return Color.white;
            bool blink = Mathf.Repeat(_time * 18f, 2f) < 1f;
            var flash = new Color(1f, 1f, 1f, 0.85f);
            var windup = new Color(EnemyLook.WindupColor.r, EnemyLook.WindupColor.g, EnemyLook.WindupColor.b, 0.85f);
            switch (e.Kind)
            {
                case EnemyKind.Brute: if (e.Mode == BruteMode.Windup && blink) return windup; break;
                case EnemyKind.Drone: if (e.DroneState == DroneMode.Windup && blink) return flash; break;
                case EnemyKind.Shooter: if (e.Act == EnemyAct.Windup && blink) return windup; break;
                case EnemyKind.Summoner: if (e.Act == EnemyAct.Cast && blink) return flash; break;
                case EnemyKind.Kamikaze:
                    if (e.Act == EnemyAct.Fuse && Mathf.Repeat(_time * (14f + 40f * FuseProgress(e)), 2f) < 1f) return Color.white;
                    break;
            }
            return new Color(1f, 1f, 1f, EnemyLook.FillAlpha);
        }

        private Vector2 Offset(Enemy e, int index)
        {
            if (e.Kind == EnemyKind.Brute && e.Mode == BruteMode.Windup) return new Vector2(Mathf.Sin(_time * 70f) * 0.07f, 0f);
            if (e.Kind == EnemyKind.Drone && e.DroneState == DroneMode.Hover) return new Vector2(0f, Mathf.Sin(_time * 4f + index) * 0.12f);
            if (e.Kind == EnemyKind.Shooter && e.Act == EnemyAct.Windup) return new Vector2(-e.Facing * 0.06f, 0f);
            if (e.Kind == EnemyKind.Kamikaze && e.Act == EnemyAct.Fuse) return new Vector2(Mathf.Sin(_time * 80f) * 0.06f, 0f);
            if (e.Kind == EnemyKind.Shield && e.TurnTimer > 0f) return new Vector2(Mathf.Sin(_time * 50f) * 0.05f, 0f);
            if (e.Kind == EnemyKind.Summoner && e.Act == EnemyAct.Cast) return new Vector2(0f, Mathf.Abs(Mathf.Sin(_time * 10f)) * 0.1f);
            return Vector2.zero;
        }

        private static bool Telegraphing(Enemy e) =>
            (e.Kind == EnemyKind.Shooter && e.Act == EnemyAct.Windup)
            || (e.Kind == EnemyKind.Kamikaze && e.Act == EnemyAct.Fuse)
            || (e.Kind == EnemyKind.Summoner && e.Act == EnemyAct.Cast);

        private float FuseProgress(Enemy e) =>
            e.Kind == EnemyKind.Kamikaze && e.Act == EnemyAct.Fuse && _fuseTime > 0f ? Mathf.Clamp01(1f - e.ModeTimer / _fuseTime) : 0f;

        private float FuseSwell(Enemy e) => 1f + 0.3f * FuseProgress(e);
    }
}
