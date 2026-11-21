using System.Collections.Generic;
using Redline.Core;
using Redline.Game.Art;
using Redline.Game.Atmosphere;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Redline.Game
{
    /// <summary>
    /// Draws the moving actors: the player as the animated cyberpunk samurai (sheet
    /// <c>Resources/Art/Characters/Samurai</c> via <see cref="CharacterSheet"/>; lit body with a normal
    /// map, unlit emissive blade/visor/arm overlays; facing, lean, squash and stretch). The speed tier
    /// reads from the blade (tier colour, glow, slash ghosts) and a tier-tinted key/rim light that
    /// follows the player. Also the seven enemy kinds from their own sheets
    /// (<c>Resources/Art/Characters/&lt;Kind&gt;</c>, see <see cref="EnemyLook"/>: state-driven animations,
    /// kind-coloured emissive strips, hit pop and flash, brute armor state, the shield bearer's shield
    /// side and turn, shooter, kamikaze and summoner telegraphs, and a death animation that holds and
    /// fades where the enemy fell), shooter projectiles and the grappling rope. Positions arrive in
    /// draw space (the ring already unrolled around the camera).
    /// </summary>
    /// <example>
    /// <code>
    /// var actors = new ActorRenderer(worldRoot, effects);
    /// actors.Sync(sim, interpolatedFeet, enemyFeet, Time.deltaTime);
    /// actors.Squash(0.75f);   // on landing
    /// </code>
    /// </example>
    /// <remarks>Main-thread only. Enemy views are pooled and grow only when a wave is larger than any before.</remarks>
    public sealed class ActorRenderer
    {
        private static readonly Color RopeCore = new Color(0.9f, 0.97f, 1f);
        private static readonly Color ShotColor = new Color(1f, 0.4f, 0.15f);

        private readonly Transform _root;
        private readonly SpriteRenderer _player;
        private readonly SpriteRenderer _playerGlow;
        private readonly Light2D _playerLight;
        // Samurai sheet (design: cyberpunk-samurai player art): body is lit with its normal map;
        // blade (tier colour), visor (red) and arm strips (cyan) are unlit emissive overlays above it.
        private readonly SpriteRenderer _blade;
        private readonly SpriteRenderer _visor;
        private readonly SpriteRenderer _arm;
        private readonly SpriteAnimator _anim = new SpriteAnimator();
        private readonly SheetAnimation _idle, _run, _slide, _jump, _fall, _wallSlide, _hookSwing,
            _attack, _airAttack, _hurt, _death;
        private readonly EffectsRenderer _effects;
        private int _lastHp = int.MinValue;
        private float _hurtTimer;
        private int _lastStrike = -1;
        private readonly SpriteRenderer _rope;
        private readonly SpriteRenderer _ropeGlow;
        private readonly SpriteRenderer _anchor;
        // Enemy sheets (Resources/Art/Characters/<Kind>) by (int)EnemyKind; live views per sim slot; corpse pool.
        private readonly EnemyAnimations[] _enemyAnims = new EnemyAnimations[7];
        private readonly List<EnemyView> _enemyViews = new List<EnemyView>(32);
        private readonly List<EnemyView> _corpses = new List<EnemyView>(16);
        private RedlineSim _lastSim;
        private readonly List<SpriteRenderer> _shots = new List<SpriteRenderer>(24);
        private readonly List<SpriteRenderer> _shotGlows = new List<SpriteRenderer>(24);
        private float _stretch = 1f;
        private float _time;
        private float _fuseTime;

        /// <summary>The player's drawn sprite, position, scale and flip (for afterimages).</summary>
        public SpriteRenderer PlayerView => _player;

        /// <summary>Creates the actor views under a parent transform.</summary>
        public ActorRenderer(Transform parent, EffectsRenderer effects)
        {
            _effects = effects;
            _root = new GameObject("Actors").transform;
            _root.SetParent(parent, false);
            _playerGlow = SpriteFactory.Create("PlayerGlow", _root, PixelArt.Glow, Color.clear, 6);
            _ropeGlow = SpriteFactory.Create("RopeGlow", _root, SpriteFactory.Square, Color.clear, 6);
            _rope = SpriteFactory.Create("Rope", _root, SpriteFactory.Square, RopeCore, 7);
            _anchor = SpriteFactory.Create("Anchor", _root, PixelArt.Glow, RopeCore, 7);
            var sheet = CharacterSheet.Load(SamuraiLook.SheetPath);
            _idle = sheet.Animation(SamuraiLook.Idle);
            _run = sheet.Animation(SamuraiLook.Run);
            _slide = sheet.Animation(SamuraiLook.Slide);
            _jump = sheet.Animation(SamuraiLook.Jump);
            _fall = sheet.Animation(SamuraiLook.Fall);
            _wallSlide = sheet.Animation(SamuraiLook.WallSlide);
            _hookSwing = sheet.Animation(SamuraiLook.HookSwing);
            _attack = sheet.Animation(SamuraiLook.Attack);
            _airAttack = sheet.Animation(SamuraiLook.AirAttack);
            _hurt = sheet.Animation(SamuraiLook.Hurt);
            _death = sheet.Animation(SamuraiLook.Death);
            _anim.Play(_idle);
            for (int k = 0; k < _enemyAnims.Length; k++) _enemyAnims[k] = EnemyAnimations.Load((EnemyKind)k);
            _player = SpriteFactory.CreateLit("Player", _root, _idle.Frame(0), SamuraiLook.BodyTint, SamuraiLook.BodyOrder);
            // Overlays ride on the body transform (same position, squash, tilt); flip is copied per frame.
            _blade = SpriteFactory.Create("Blade", _player.transform, null, Color.white, SamuraiLook.BladeOrder);
            _visor = SpriteFactory.Create("Visor", _player.transform, null, SamuraiLook.Visor, SamuraiLook.VisorOrder);
            _arm = SpriteFactory.Create("ArmStrips", _player.transform, null, SamuraiLook.Arm, SamuraiLook.ArmOrder);
            _playerLight = Lights2D.Point(_root, "PlayerRimLight", Vector3.zero, Color.white,
                AtmosphereConfig.PlayerLightIntensity, AtmosphereConfig.PlayerLightRadius,
                AtmosphereConfig.PlayerLightInner, 360f, 360f, AtmosphereConfig.PlayerLightFalloff);
        }

        /// <summary>Squashes (below 1) or stretches (above 1) the player for a moment.</summary>
        public void Squash(float amount)
        {
            _stretch = amount;
        }

        /// <summary>Updates every actor view from the simulation.</summary>
        public void Sync(RedlineSim sim, Vector2 playerFeet, IReadOnlyList<Vector2> enemyFeet, float dt)
        {
            _time += dt;
            _fuseTime = sim.Config.kamikazeFuseTime;
            _stretch = Mathf.Lerp(_stretch, 1f, Mathf.Min(1f, dt * 12f));
            int tier = sim.PlayerTier;
            SyncPlayer(sim, playerFeet, tier, dt);
            SyncRope(sim, playerFeet, tier);
            SyncEnemies(sim, playerFeet, enemyFeet, tier, dt);
            SyncProjectiles(sim, playerFeet, tier);
        }

        /// <summary>
        /// Projectiles, placed relative to the drawn player (the ring is unrolled around them):
        /// hot orange while hostile, the player's tier colour once reflected. Views are pooled.
        /// </summary>
        private void SyncProjectiles(RedlineSim sim, Vector2 playerFeet, int tier)
        {
            var shots = sim.Projectiles;
            while (_shots.Count < shots.Count)
            {
                _shotGlows.Add(SpriteFactory.Create("ShotGlow", _root, PixelArt.Glow, Color.clear, 8));
                _shots.Add(SpriteFactory.Create("Shot", _root, PixelArt.Projectile, ShotColor, 9));
            }
            var p = sim.Player;
            for (int i = 0; i < _shots.Count; i++)
            {
                bool on = i < shots.Count;
                _shots[i].enabled = on;
                _shotGlows[i].enabled = on;
                if (!on) continue;
                var shot = shots[i];
                var c = shot.Reflected ? RedlineGame.TierColors[tier - 1] : ShotColor;
                var pos = new Vector3(playerFeet.x + sim.Map.Dx(p.X, shot.X), shot.Y + shot.Height * 0.5f, 0f);
                float pulse = 1f + Mathf.Sin(_time * 30f + i) * 0.12f;
                var t = _shots[i].transform;
                t.localPosition = pos;
                t.localScale = new Vector3(shot.Width * 1.5f * pulse, shot.Width * 1.5f * pulse, 1f);
                t.localRotation = Quaternion.Euler(0f, 0f, _time * 360f);
                _shots[i].color = Color.Lerp(c, Color.white, 0.35f);
                var gc = c;
                gc.a = 0.45f;
                _shotGlows[i].color = gc;
                _shotGlows[i].transform.localPosition = pos;
                float gs = shot.Width * 4.5f * pulse;
                _shotGlows[i].transform.localScale = new Vector3(gs, gs, 1f);
            }
        }

        /// <summary>
        /// The samurai: picks the animation from sim state, draws the lit body frame and the three
        /// emissive overlays (blade in the tier colour, visor, arm strips) with the same flip,
        /// squash/stretch and tilt, a tier glow on the blade, a slash ghost while striking, and the
        /// tier rim light. Pivot is the feet, 1/ppu world units per texel (no extra scale).
        /// </summary>
        private void SyncPlayer(RedlineSim sim, Vector2 feet, int tier, float dt)
        {
            var p = sim.Player;
            bool dead = sim.State == SimState.GameOver;
            if (_lastHp != int.MinValue && p.Hp < _lastHp && !dead) _hurtTimer = _hurt.Duration;
            else _hurtTimer = Mathf.Max(0f, _hurtTimer - dt);
            _lastHp = p.Hp;

            var anim = PickAnimation(p, dead, out bool restart);
            _anim.Play(anim, restart);
            _anim.Tick(dt, AnimRate(anim, p, sim.Config));
            int frame = _anim.Frame;
            // wall_slide is drawn facing the wall; everything else faces the way the player faces.
            bool flip = anim == _wallSlide ? p.WallDir < 0 : p.Facing < 0;

            float alpha = !dead && p.InvulnTimer > 0f && Mathf.Repeat(Time.time * SamuraiLook.BlinkRate, 2f) < 1f
                ? SamuraiLook.BlinkAlpha
                : 1f;
            var body = SamuraiLook.BodyTint;
            body.a *= alpha;
            _player.sprite = _anim.Current.Frame(frame);
            _player.flipX = flip;
            _player.color = body;

            var tierColor = RedlineGame.TierColors[tier - 1];
            Overlay(_blade, EmissiveChannel.R, frame, flip, Color.Lerp(tierColor, Color.white, SamuraiLook.BladeWhiteMix), alpha);
            Overlay(_visor, EmissiveChannel.G, frame, flip, SamuraiLook.Visor, alpha);
            Overlay(_arm, EmissiveChannel.B, frame, flip, SamuraiLook.Arm, alpha);

            // Squash/stretch about the feet; tilt about the body centre.
            // A slide's height follows the tier it started at: tier 1 a taller half crouch, tier 3 pressed low.
            float s = dead ? 1f : p.Sliding ? (p.SlideTier == 1 ? 1.3f : p.SlideTier == 3 ? 0.65f : 1f) : _stretch;
            float tilt = dead ? 0f : PlayerTilt(p, sim.Map);
            var rot = Quaternion.Euler(0f, 0f, tilt);
            var half = new Vector3(0f, p.Height * 0.5f, 0f);
            var t = _player.transform;
            t.localScale = new Vector3(1f / s, s, 1f);
            t.localRotation = rot;
            t.localPosition = new Vector3(feet.x, feet.y, 0f) + half - rot * half;

            // Tier glow centred on the blade's lit texels in this frame.
            bool glowOn = _blade.enabled && !dead;
            _playerGlow.enabled = glowOn;
            if (glowOn)
            {
                var c = _anim.Current.EmissiveCentroid(EmissiveChannel.R, frame);
                if (flip) c.x = -c.x;
                var glow = tierColor;
                glow.a = (SamuraiLook.BladeGlowAlpha + SamuraiLook.BladeGlowAlphaPerTier * tier
                          + Mathf.Sin(_time * 10f) * 0.03f * tier) * alpha;
                _playerGlow.color = glow;
                _playerGlow.transform.localPosition = t.localPosition + rot * new Vector3(c.x / s, c.y * s, 0f);
                float gs = SamuraiLook.BladeGlowSize + SamuraiLook.BladeGlowSizePerTier * (tier - 1);
                _playerGlow.transform.localScale = new Vector3(gs, gs, 1f);
            }

            if (p.Attacking && _blade.enabled)
            {
                var ghost = tierColor;
                ghost.a = SamuraiLook.SlashGhostAlpha;
                _effects.Ghost(_blade.sprite, t.localPosition, t.localRotation, t.localScale, flip, ghost, SamuraiLook.SlashGhostLife);
            }

            // Key/rim light: tier-tinted, behind and above the body centre so it rims the back edge.
            _playerLight.enabled = true;
            float back = p.Facing >= 0 ? -AtmosphereConfig.PlayerLightBack : AtmosphereConfig.PlayerLightBack;
            _playerLight.transform.localPosition = new Vector3(feet.x + back, feet.y + p.Height * 0.5f + AtmosphereConfig.PlayerLightUp, 0f);
            _playerLight.color = tierColor;
            _playerLight.intensity = AtmosphereConfig.PlayerLightIntensity * SamuraiLook.RimLightBoost;
        }

        /// <summary>
        /// State to animation, highest priority first: death, hurt (just took damage), hook swing,
        /// slide, strike (attack on the ground, air_attack in the air; a new strike restarts it and it
        /// follows through to its last frame), wall slide, jump (rising) / fall, run, idle.
        /// </summary>
        private SheetAnimation PickAnimation(Player p, bool dead, out bool restart)
        {
            restart = false;
            if (dead) return _death;
            if (_hurtTimer > 0f) return _hurt;
            if (p.Hooked) return _hookSwing;
            if (p.Sliding) return _slide;
            bool swinging = _anim.Current == _attack || _anim.Current == _airAttack;
            if (p.Attacking)
            {
                bool fresh = p.StrikeId != _lastStrike;
                _lastStrike = p.StrikeId;
                if (fresh || !swinging)
                {
                    restart = true;
                    return p.Grounded ? _attack : _airAttack;
                }
                return _anim.Current;
            }
            if (swinging && !_anim.Finished) return _anim.Current;
            if (!p.Grounded)
            {
                if (p.WallDir != 0 && p.Vy <= 0f) return _wallSlide;
                return p.Vy > 0f ? _jump : _fall;
            }
            return Mathf.Abs(p.Vx) >= SamuraiLook.RunMinSpeed ? _run : _idle;
        }

        /// <summary>Speed scale on the authored fps: run follows |Vx|; strikes play at SamuraiLook's fps.</summary>
        private float AnimRate(SheetAnimation anim, Player p, RedlineConfig cfg)
        {
            if (anim == _run) return RunRate(p, cfg);
            if (anim == _attack) return SamuraiLook.AttackFps / _attack.Fps;
            if (anim == _airAttack) return SamuraiLook.AirAttackFps / _airAttack.Fps;
            return 1f;
        }

        /// <summary>Run cycle rate: the authored fps at run speed, scaled with |Vx|.</summary>
        private static float RunRate(Player p, RedlineConfig cfg) =>
            Mathf.Clamp(Mathf.Abs(p.Vx) / Mathf.Max(0.01f, cfg.runSpeed), SamuraiLook.RunRateMin, SamuraiLook.RunRateMax);

        /// <summary>Shows one emissive channel of the current frame, or hides it when the frame has none.</summary>
        private void Overlay(SpriteRenderer r, EmissiveChannel channel, int frame, bool flip, Color color, float alpha)
        {
            var sprite = _anim.Current.Emissive(channel, frame);
            r.enabled = sprite != null;
            if (sprite == null) return;
            r.sprite = sprite;
            r.flipX = flip;
            color.a *= alpha;
            r.color = color;
        }

        /// <summary>Leans slightly into a run (the drawn run already leans), and hangs along the rope while swinging.</summary>
        private static float PlayerTilt(Player p, TileMap map)
        {
            if (p.Hooked)
            {
                var d = map.Near(p.Center, p.HookAnchor) - p.Center;
                return Mathf.Atan2(d.Y, d.X) * Mathf.Rad2Deg - 90f;
            }
            if (p.Sliding || !p.Grounded) return 0f;
            return Mathf.Clamp(-p.Vx * SamuraiLook.LeanPerSpeed, -SamuraiLook.MaxLean, SamuraiLook.MaxLean);
        }

        private void SyncRope(RedlineSim sim, Vector2 feet, int tier)
        {
            var p = sim.Player;
            bool show = p.Hooked && sim.State != SimState.GameOver;
            _rope.enabled = _ropeGlow.enabled = _anchor.enabled = show;
            if (!show) return;
            var from = new Vector2(feet.x, feet.y + p.Height * 0.5f);
            var to = new Vector2(feet.x + sim.Map.Dx(p.X, p.HookAnchor.X), p.HookAnchor.Y);
            var d = to - from;
            var mid = (from + to) * 0.5f;
            var rot = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            _rope.transform.localPosition = mid;
            _rope.transform.localRotation = rot;
            _rope.transform.localScale = new Vector3(d.magnitude, 0.06f, 1f);
            var glow = RedlineGame.TierColors[tier - 1];
            glow.a = 0.35f;
            _ropeGlow.color = glow;
            _ropeGlow.transform.localPosition = mid;
            _ropeGlow.transform.localRotation = rot;
            _ropeGlow.transform.localScale = new Vector3(d.magnitude, 0.22f, 1f);
            _anchor.transform.localPosition = to;
            _anchor.transform.localScale = new Vector3(0.6f, 0.6f, 1f);
        }

        // ------------------------------------------------------------------ enemies

        /// <summary>
        /// One drawn enemy (a live slot or a pooled corpse): the lit body with its normal map, an unlit
        /// fill/flash copy of the colour frame, the three unlit emissive overlays (R attack glow,
        /// G eyes, B kind-coloured strips) riding on the body transform, and (live views only) a halo.
        /// </summary>
        private sealed class EnemyView
        {
            public readonly SpriteRenderer Body, Fill, AttackGlow, Eyes, Strips, Halo;
            public readonly SpriteAnimator Anim = new SpriteAnimator();
            /// <summary>Live views: the enemy bound to this slot (a new reference re-binds; no corpse).</summary>
            public Enemy Enemy;
            public EnemyKind Kind;
            public bool WasAlive, Flip;
            public EnemyAct LastAct;
            public float LastSinceHit = 99f, FireTimer, BlockTimer;
            /// <summary>Corpses: in use, seconds held after the death animation ended, sim feet.</summary>
            public bool Active;
            public float Held, X, Y, Height;

            public EnemyView(Transform root, bool corpse)
            {
                int body = corpse ? EnemyLook.CorpseBodyOrder : EnemyLook.BodyOrder;
                int over = corpse ? EnemyLook.CorpseOverlayOrder : EnemyLook.OverlayOrder;
                if (!corpse) Halo = SpriteFactory.Create("EnemyHalo", root, PixelArt.Glow, Color.clear, EnemyLook.HaloOrder);
                Body = SpriteFactory.CreateLit(corpse ? "Corpse" : "Enemy", root, null, Color.white, body);
                Fill = SpriteFactory.Create("Fill", Body.transform, null, Color.white, over);
                AttackGlow = SpriteFactory.Create("AttackGlow", Body.transform, null, Color.white, over);
                Eyes = SpriteFactory.Create("Eyes", Body.transform, null, Color.white, over);
                Strips = SpriteFactory.Create("Strips", Body.transform, null, Color.white, over);
                // Same sorting order: the emissive masks sit nearer the (orthographic) camera than the fill.
                var front = new Vector3(0f, 0f, -0.01f);
                AttackGlow.transform.localPosition = front;
                Eyes.transform.localPosition = front;
                Strips.transform.localPosition = front;
                Hide();
            }

            public void Hide()
            {
                Body.enabled = Fill.enabled = AttackGlow.enabled = Eyes.enabled = Strips.enabled = false;
                if (Halo != null) Halo.enabled = false;
            }

            /// <summary>Draws the animator's current frame with the given tints, all faded by <paramref name="alpha"/>.</summary>
            public void Draw(Color attack, Color eyes, Color strips, Color fill, float alpha)
            {
                var a = Anim.Current;
                int frame = Anim.Frame;
                var sprite = a.Frame(frame);
                Body.enabled = true;
                Body.sprite = sprite;
                Body.flipX = Flip;
                Body.color = new Color(1f, 1f, 1f, alpha);
                fill.a *= alpha;
                Fill.enabled = fill.a > 0.001f;
                Fill.sprite = sprite;
                Fill.flipX = Flip;
                Fill.color = fill;
                Channel(AttackGlow, a, EmissiveChannel.R, frame, attack, alpha);
                Channel(Eyes, a, EmissiveChannel.G, frame, eyes, alpha);
                Channel(Strips, a, EmissiveChannel.B, frame, strips, alpha);
            }

            private void Channel(SpriteRenderer r, SheetAnimation a, EmissiveChannel channel, int frame, Color color, float alpha)
            {
                var sprite = a.Emissive(channel, frame);
                color.a *= alpha;
                r.enabled = sprite != null && color.a > 0.001f;
                if (!r.enabled) return;
                r.sprite = sprite;
                r.flipX = Flip;
                r.color = color;
            }
        }

        /// <summary>
        /// Live enemies, one pooled view per slot of <see cref="RedlineSim.Enemies"/> (dead enemies stay
        /// in the list until the next wave). An enemy seen alive and then dead leaves a corpse that plays
        /// its death animation; a kamikaze that blew up (its fuse was burning and the blast reset it to
        /// Idle, which a kill does not) leaves none. A different sim (new level) clears the corpses.
        /// </summary>
        private void SyncEnemies(RedlineSim sim, Vector2 playerFeet, IReadOnlyList<Vector2> feet, int tier, float dt)
        {
            if (sim != _lastSim)
            {
                _lastSim = sim;
                for (int i = 0; i < _corpses.Count; i++)
                {
                    _corpses[i].Active = false;
                    _corpses[i].Hide();
                }
            }
            var enemies = sim.Enemies;
            while (_enemyViews.Count < enemies.Count) _enemyViews.Add(new EnemyView(_root, false));
            for (int i = 0; i < _enemyViews.Count; i++)
            {
                var v = _enemyViews[i];
                if (i >= enemies.Count)
                {
                    v.Enemy = null;
                    v.Hide();
                    continue;
                }
                var e = enemies[i];
                if (v.Enemy != e) Bind(v, e);
                if (!e.Alive)
                {
                    bool exploded = e.Kind == EnemyKind.Kamikaze && v.LastAct == EnemyAct.Fuse && e.Act == EnemyAct.Idle;
                    if (v.WasAlive && !exploded) SpawnCorpse(v, e);
                    v.WasAlive = false;
                    v.Hide();
                    continue;
                }
                SyncEnemy(sim, v, e, i < feet.Count ? feet[i] : new Vector2(e.X, e.Y), tier, i, dt);
                v.LastAct = e.Act;
                v.LastSinceHit = e.SinceHit;
            }
            SyncCorpses(sim, playerFeet, dt);
        }

        private void Bind(EnemyView v, Enemy e)
        {
            v.Enemy = e;
            v.Kind = e.Kind;
            v.WasAlive = e.Alive;
            v.LastAct = e.Act;
            v.LastSinceHit = e.SinceHit;
            v.FireTimer = v.BlockTimer = 0f;
            v.Flip = (e.Kind == EnemyKind.Shield ? e.ShieldDir : e.Facing) < 0;
            v.Anim.Play(_enemyAnims[(int)e.Kind].Idle, true);
        }

        private void SpawnCorpse(EnemyView from, Enemy e)
        {
            EnemyView c = null;
            for (int i = 0; i < _corpses.Count; i++)
            {
                if (_corpses[i].Active) continue;
                c = _corpses[i];
                break;
            }
            if (c == null)
            {
                c = new EnemyView(_root, true);
                _corpses.Add(c);
            }
            c.Active = true;
            c.Kind = e.Kind;
            c.X = e.X;
            c.Y = e.Y;
            c.Height = e.Height;
            c.Flip = from.Flip;
            c.Held = 0f;
            c.Anim.Play(_enemyAnims[(int)e.Kind].Death, true);
        }

        /// <summary>Death plays once where the enemy fell, holds its last frame, then fades (EnemyLook.CorpseHold/Fade).</summary>
        private void SyncCorpses(RedlineSim sim, Vector2 playerFeet, float dt)
        {
            var p = sim.Player;
            for (int i = 0; i < _corpses.Count; i++)
            {
                var c = _corpses[i];
                if (!c.Active) continue;
                c.Anim.Tick(dt);
                if (c.Anim.Finished) c.Held += dt;
                if (c.Held >= EnemyLook.CorpseHold + EnemyLook.CorpseFade)
                {
                    c.Active = false;
                    c.Hide();
                    continue;
                }
                float alpha = 1f - Mathf.Clamp01((c.Held - EnemyLook.CorpseHold) / EnemyLook.CorpseFade);
                float y = c.Y + (EnemyLook.CentrePivot(c.Kind) ? c.Height * 0.5f : 0f);
                var t = c.Body.transform;
                t.localPosition = new Vector3(playerFeet.x + sim.Map.Dx(p.X, c.X), y, 0f);
                t.localScale = Vector3.one;
                t.localRotation = Quaternion.identity;
                var fill = Color.white;
                fill.a = EnemyLook.FillAlpha;
                c.Draw(EnemyLook.AttackColor(c.Kind), EnemyLook.Eyes, StripColor(EnemyLook.KindColor(c.Kind)), fill, alpha);
            }
        }

        /// <summary>
        /// One live enemy: animation from sim state, flip, the tints (kind strips, brute armor state,
        /// telegraph flashes on the fill copy), hit pop, fuse swell, shake/sway offsets and the halo
        /// (which grows to the blast radius while a kamikaze's fuse burns).
        /// </summary>
        private void SyncEnemy(RedlineSim sim, EnemyView v, Enemy e, Vector2 feet, int tier, int index, float dt)
        {
            var a = _enemyAnims[(int)e.Kind];
            var cfg = sim.Config;
            bool newHit = e.SinceHit < v.LastSinceHit;
            v.FireTimer = Mathf.Max(0f, v.FireTimer - dt);
            v.BlockTimer = Mathf.Max(0f, v.BlockTimer - dt);
            // Shooter: a wind-up that ended without a hit is a shot.
            bool fired = e.Kind == EnemyKind.Shooter && v.LastAct == EnemyAct.Windup && e.Act == EnemyAct.Idle
                         && e.SinceHit > EnemyLook.HitPopTime;
            if (fired) v.FireTimer = a.Attack.Duration;
            bool blocked = e.Kind == EnemyKind.Shield && v.BlockTimer <= 0f && ShieldBlocked(sim, e);
            if (blocked) v.BlockTimer = a.Special.Duration;

            var anim = PickEnemyAnimation(v, e, a, cfg, out float rate);
            bool restart = fired || blocked || (anim == a.Hurt && newHit);
            v.Anim.Play(anim, restart);
            v.Anim.Tick(dt, rate);
            v.Flip = EnemyFlip(sim, e);

            bool brute = e.Kind == EnemyKind.Brute;
            bool breakable = brute && CombatRules.CanDamage(tier, e.ArmorTier);
            var kindColor = brute ? (breakable ? EnemyLook.BruteBreakable : EnemyLook.BruteArmored) : EnemyLook.KindColor(e.Kind);
            var strips = StripColor(kindColor);
            if (breakable) strips.a = 0.75f + 0.25f * Mathf.Sin(_time * 14f);
            bool windup = (brute && e.Mode == BruteMode.Windup)
                          || (e.Kind == EnemyKind.Drone && e.DroneState == DroneMode.Windup)
                          || (e.Kind == EnemyKind.Shooter && e.Act == EnemyAct.Windup)
                          || (e.Kind == EnemyKind.Summoner && e.Act == EnemyAct.Cast);
            var attack = EnemyLook.AttackColor(e.Kind);
            if (windup || FuseProgress(e) > 0f) attack = Color.Lerp(attack, Color.white, 0.3f + 0.3f * Mathf.Sin(_time * 24f));
            v.Draw(attack, EnemyLook.Eyes, strips, FillColor(e), 1f);

            float pop = (e.SinceHit < EnemyLook.HitPopTime ? EnemyLook.HitPop : 1f) * FuseSwell(e);
            var offset = EnemyOffset(e, index);
            float lift = EnemyLook.CentrePivot(e.Kind) ? e.Height * 0.5f : 0f;
            var t = v.Body.transform;
            t.localScale = new Vector3(pop, pop, 1f);
            t.localPosition = new Vector3(feet.x + offset.x, feet.y + lift + offset.y, 0f);
            // A crashed brute wobbles, dazed (about its feet).
            t.localRotation = brute && e.Mode == BruteMode.Crashed
                ? Quaternion.Euler(0f, 0f, Mathf.Sin(_time * 9f) * 6f)
                : Quaternion.identity;

            var halo = v.Halo;
            halo.enabled = true;
            var hc = kindColor;
            hc.a = breakable ? 0.3f + 0.15f * Mathf.Sin(_time * 14f) : brute ? EnemyLook.HaloAlphaArmored : EnemyLook.HaloAlpha;
            float hs = e.Height * (breakable ? 2.4f : EnemyLook.HaloSize);
            if (Telegraphing(e) || windup)
            {
                // Telegraphs swell the halo; a lit fuse shows the blast radius.
                bool fuse = e.Kind == EnemyKind.Kamikaze;
                hc = fuse ? EnemyLook.KamikazeColor : EnemyLook.WindupColor;
                hc.a = 0.3f + 0.15f * Mathf.Sin(_time * (fuse ? 40f : 20f));
                hs = fuse ? cfg.kamikazeBlastRadius * 2f * 1.4f : e.Height * 2.6f;
            }
            halo.color = hc;
            halo.transform.localPosition = new Vector3(feet.x + offset.x, feet.y + offset.y + e.Height * 0.5f * pop, 0f);
            halo.transform.localScale = new Vector3(hs, hs, 1f);
        }

        /// <summary>
        /// State to animation (and a playback rate), highest priority first: the kind's attack and
        /// telegraph states (non-looping telegraphs are time-scaled to end with their configured
        /// duration), hurt right after a hit, move when |Vx| is above EnemyLook.WalkMinSpeed, idle.
        /// </summary>
        private static SheetAnimation PickEnemyAnimation(EnemyView v, Enemy e, EnemyAnimations a, RedlineConfig cfg, out float rate)
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
                    // ModeTimer > 0 while rushing = mid-lunge (see RedlineSim's kamikaze steering).
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
            }
            if (a.Hurt != null && e.SinceHit < a.Hurt.Duration) return a.Hurt;
            float speed = Mathf.Abs(e.Vx);
            if (speed >= EnemyLook.WalkMinSpeed && (e.Grounded || e.Kind == EnemyKind.Drone))
            {
                rate = Mathf.Clamp(speed / Mathf.Max(0.01f, e.Speed), EnemyLook.WalkRateMin, EnemyLook.WalkRateMax);
                return a.Move;
            }
            return a.Idle;
        }

        private static float TelegraphRate(SheetAnimation a, float seconds) =>
            Mathf.Clamp(a.Duration / Mathf.Max(0.05f, seconds), EnemyLook.TelegraphRateMin, EnemyLook.TelegraphRateMax);

        /// <summary>
        /// Sheets face right. The shield bearer is drawn by where its shield is, not where it walks
        /// (a lagging shield, its back open, reads at a glance); drones face their dive, their drift
        /// or the player.
        /// </summary>
        private static bool EnemyFlip(RedlineSim sim, Enemy e)
        {
            if (e.Kind == EnemyKind.Shield) return e.ShieldDir < 0;
            if (e.Kind != EnemyKind.Drone) return e.Facing < 0;
            if (e.DroneState == DroneMode.Dive) return e.DiveDir.X < 0f;
            if (Mathf.Abs(e.Vx) > 0.2f) return e.Vx < 0f;
            return sim.Map.Dx(e.X, sim.Player.X) < 0f;
        }

        /// <summary>A ShieldBlock event this step near the bearer's centre.</summary>
        private static bool ShieldBlocked(RedlineSim sim, Enemy e)
        {
            var events = sim.Events;
            var c = e.Center;
            float r2 = EnemyLook.BlockMatchRadius * EnemyLook.BlockMatchRadius;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].Type != SimEventType.ShieldBlock) continue;
                float dx = sim.Map.Dx(c.X, events[i].Position.X);
                float dy = events[i].Position.Y - c.Y;
                if (dx * dx + dy * dy <= r2) return true;
            }
            return false;
        }

        private static Color StripColor(Color kind) => Color.Lerp(kind, Color.white, EnemyLook.StripWhiteMix);

        /// <summary>
        /// The unlit fill copy: a readability floor (EnemyLook.FillAlpha), raised to a flash for hits
        /// (white) and telegraphs (blinking; the kamikaze fuse flashes faster as it burns down).
        /// </summary>
        private Color FillColor(Enemy e)
        {
            if (e.SinceHit < EnemyLook.HitFlashTime) return Color.white;
            bool blink = Mathf.Repeat(_time * 18f, 2f) < 1f;
            var flash = new Color(1f, 1f, 1f, 0.85f);
            switch (e.Kind)
            {
                case EnemyKind.Brute:
                    if (e.Mode == BruteMode.Windup && blink) return new Color(EnemyLook.WindupColor.r, EnemyLook.WindupColor.g, EnemyLook.WindupColor.b, 0.85f);
                    break;
                case EnemyKind.Drone:
                    if (e.DroneState == DroneMode.Windup && blink) return flash;
                    break;
                case EnemyKind.Shooter:
                    if (e.Act == EnemyAct.Windup && blink) return new Color(EnemyLook.WindupColor.r, EnemyLook.WindupColor.g, EnemyLook.WindupColor.b, 0.85f);
                    break;
                case EnemyKind.Summoner:
                    if (e.Act == EnemyAct.Cast && blink) return flash;
                    break;
                case EnemyKind.Kamikaze:
                    if (e.Act == EnemyAct.Fuse && Mathf.Repeat(_time * (14f + 40f * FuseProgress(e)), 2f) < 1f) return Color.white;
                    break;
            }
            return new Color(1f, 1f, 1f, EnemyLook.FillAlpha);
        }

        /// <summary>Shake while a brute telegraphs a charge; gentle hover sway for drones.</summary>
        private Vector2 EnemyOffset(Enemy e, int index)
        {
            if (e.Kind == EnemyKind.Brute && e.Mode == BruteMode.Windup)
                return new Vector2(Mathf.Sin(_time * 70f) * 0.07f, 0f);
            if (e.Kind == EnemyKind.Drone && e.DroneState == DroneMode.Hover)
                return new Vector2(0f, Mathf.Sin(_time * 4f + index) * 0.12f);
            // Shooter recoils back while winding up; a fusing kamikaze trembles; a turning shield wobbles.
            if (e.Kind == EnemyKind.Shooter && e.Act == EnemyAct.Windup)
                return new Vector2(-e.Facing * 0.06f, 0f);
            if (e.Kind == EnemyKind.Kamikaze && e.Act == EnemyAct.Fuse)
                return new Vector2(Mathf.Sin(_time * 80f) * 0.06f, 0f);
            if (e.Kind == EnemyKind.Shield && e.TurnTimer > 0f)
                return new Vector2(Mathf.Sin(_time * 50f) * 0.05f, 0f);
            if (e.Kind == EnemyKind.Summoner && e.Act == EnemyAct.Cast)
                return new Vector2(0f, Mathf.Abs(Mathf.Sin(_time * 10f)) * 0.1f);
            return Vector2.zero;
        }

        /// <summary>Shooter wind-up, kamikaze fuse or summoner cast in progress.</summary>
        private static bool Telegraphing(Enemy e) =>
            (e.Kind == EnemyKind.Shooter && e.Act == EnemyAct.Windup)
            || (e.Kind == EnemyKind.Kamikaze && e.Act == EnemyAct.Fuse)
            || (e.Kind == EnemyKind.Summoner && e.Act == EnemyAct.Cast);

        /// <summary>0 when a kamikaze's fuse is lit, 1 at the blast (0 when not fusing).</summary>
        private float FuseProgress(Enemy e) =>
            e.Kind == EnemyKind.Kamikaze && e.Act == EnemyAct.Fuse && _fuseTime > 0f
                ? Mathf.Clamp01(1f - e.ModeTimer / _fuseTime)
                : 0f;

        /// <summary>A fusing kamikaze swells toward the blast.</summary>
        private float FuseSwell(Enemy e) => 1f + 0.3f * FuseProgress(e);
    }
}
