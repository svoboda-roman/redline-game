using System.Collections.Generic;
using Redline.Core;
using Redline.Game.Render3D.Actors3D;
using Redline.Game.Ronin;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Redline.Game.Render3D.Integration
{
    /// <summary>
    /// The 3D actor view for <see cref="RedlineGame"/>: the Tech Ronin (<see cref="RoninView"/>) driven from the sim's
    /// player, the enemies (<see cref="Enemy3DRenderer"/>), shooter projectiles as HDR glow billboards on the play
    /// plane, and the hook rope as a thin emissive strip from the ronin's left hand to the anchor. Mirrors
    /// <see cref="ActorRenderer"/>'s rules: shots are hot orange while hostile and tier-coloured once reflected; the
    /// rope shows while hooked and the run is not over. Positions arrive as drawing positions (ToDraw).
    /// </summary>
    /// <remarks>Main thread only. Pools grow to the projectile count and never shrink; no per-frame allocation.</remarks>
    public sealed class Actors3DView : IGameActors
    {
        /// <summary>Ronin height (feet to hat top) as a multiple of the sim's player height. 0.96 × 1.6 = 1.54 units,
        /// about 1.08× the drawn grunt/shooter height, so he is the largest humanoid on screen besides the brute.</summary>
        public static float RoninHeightRatio = 0.9375f; // 1.5 units = 72 texels at 48 per unit
        /// <summary>True: the player is the pre-rendered sprite (<see cref="BakedRoninView"/>); false: the live skinned 3D model (fallback). Can be flipped at run time.</summary>
        public static bool UseBakedRonin = true;
        /// <summary>Draw a soft dark contact ellipse under grounded characters (the sprite quads face the camera, so their
        /// cast shadow from the key light is thin or falls off the deck; this guarantees the feet sit on the floor).</summary>
        public static bool ContactShadows = true;
        /// <summary>Opacity of each of the two stacked bands of the contact ellipse (centre gets both: 1-(1-a)² = 0.75
        /// at 0.5). Raised from 0.3 so the feet read as planted on the bright decks.</summary>
        public static float ContactShadowAlpha = 0.5f;
        /// <summary>Contact ellipse width as a multiple of the body width.</summary>
        public static float ContactShadowWidth = 1.6f;
        /// <summary>Contact ellipse height in world units (3 texels at 48 texels per unit).</summary>
        public const float ContactShadowHeight = 3f / Grid;
        private const float Grid = Pixel3DCamera.TexelsPerUnit;
        /// <summary>The newest live view (dev capture access); null once its GameObjects were destroyed or it was disposed.</summary>
        public static Actors3DView Instance => _instance != null && _instance._root != null ? _instance : null;
        private static Actors3DView _instance;
        /// <summary>HDR multiplier of the tier colour on the hat rim and blade.</summary>
        public static float TierHdr = 2.6f;
        /// <summary>Degrees the ronin turns toward the camera (3/4 read from the pitched-down rig).</summary>
        public static float RoninCameraTurn = 30f;
        /// <summary>Cool back light that follows the ronin (rendering layer 1 only): colour and intensity.</summary>
        public static Color RoninRimColor = new Color(0.45f, 0.65f, 1f);
        /// <summary>Intensity of the ronin's follow rim light.</summary>
        public static float RoninRimIntensity = 36f;
        /// <summary>Soft front fill that follows the ronin (rendering layer 1 only): lifts the blue-black coat off
        /// the dark decks without flattening the key. Colour and intensity.</summary>
        public static Color RoninFillColor = new Color(0.62f, 0.7f, 0.9f);
        /// <summary>Intensity of the ronin's front fill light.</summary>
        public static float RoninFillIntensity = 7f;
        /// <summary>Lock the camera's texel snap to the baked ronin (<see cref="Pixel3DCamera.LockSubject"/>). False only
        /// for the flicker burst's before/after comparison.</summary>
        public static bool LockCameraToRonin = true;
        /// <summary>A frame-to-frame jump of the feet beyond this (units) snaps the springs (teleport, wrap, respawn).</summary>
        public static float TeleportDistance = 2f;
        /// <summary>Seconds the hurt input stays raised after an HP loss.</summary>
        public static float HurtHold = 0.12f;
        /// <summary>HDR multipliers: shot core, shot glow, rope core.</summary>
        public static float ShotHdr = 3f, ShotGlowHdr = 1.4f, RopeHdr = 2.2f;

        private static readonly Color ShotColor = new Color(1f, 0.4f, 0.15f);
        private static readonly Color RopeCore = new Color(0.9f, 0.97f, 1f);
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ParamsId = Shader.PropertyToID("_Params");
        private const float Solid = 0f, Glow = 1f;

        private readonly Transform _root;
        private readonly Enemy3DRenderer _enemies;
        private readonly RoninView _ronin;
        private readonly Transform _roninRoot;
        private readonly Transform _leftHand;
        private readonly Light _roninRim;
        private readonly BakedRoninView _baked;
        private bool _bakedShown;
        private readonly float _roninScale = 1f;
        private readonly List<MeshRenderer> _shots = new List<MeshRenderer>(8);
        private readonly List<MeshRenderer> _shotGlows = new List<MeshRenderer>(8);
        private readonly MeshRenderer _rope, _ropeGlow, _anchor, _anchorCore;
        private readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        private readonly MaterialPropertyBlock _texMpb = new MaterialPropertyBlock();
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");
        /// <summary>Hemp rope width and grapnel size (world units).</summary>
        public static float RopeWidth = 0.14f, HookSize = 0.8f;
        private int _tier = -1;
        /// <summary>The tier colour last applied (a cosmetic accent can change it without a tier change).</summary>
        private Color _tierColor = Color.clear;
        private int _lastHp = int.MinValue;
        private float _hurt;
        private float _stretch = 1f;
        private float _time;
        private Vector2 _lastFeet;
        private bool _hasLast;
        private readonly Pixel3DCamera _rig;

        /// <summary>The shared 3D effects renderer (capture tooling triggers effects through it).</summary>
        public Effects3DRenderer Effects { get; }

        /// <summary>Builds the actor views under <paramref name="parent"/>; enemies share <paramref name="effects"/>. The ronin is scaled to <paramref name="playerHeight"/> × RoninHeightRatio.</summary>
        public Actors3DView(Transform parent, Effects3DRenderer effects, float playerHeight)
        {
            _root = new GameObject("Actors3D").transform;
            _root.SetParent(parent, false);
            _enemies = new Enemy3DRenderer(_root, effects);
            Effects = effects;
            _instance = this;
            // The rig is built just before the actors (RedlineGame.BuildViews); the baked ronin locks its texel snap.
            _rig = Pixel3DCamera.Latest;
            try { _baked = new BakedRoninView(_root); }
            catch (System.Exception e) { Debug.LogWarning("Redline: baked ronin unavailable, using the live 3D model. " + e.Message); }
            if (_baked != null) AddBakedLights(_baked.Root, playerHeight * RoninHeightRatio);

            var assets = RoninAssets.Load();
            if (assets == null) Debug.LogError("Redline: Resources/" + RoninAssets.ResourcePath + " is missing; the 3D player is not drawn.");
            else
            {
                _roninRoot = new GameObject("PlayerRoot").transform;
                _roninRoot.SetParent(_root, false);
                _ronin = RoninView.Create(assets, _roninRoot);
                _ronin.CameraTurn = RoninCameraTurn;
                _roninScale = MeasureScale(_ronin.transform, playerHeight * RoninHeightRatio);
                _ronin.transform.localScale = Vector3.one * _roninScale;
                var animator = _ronin.Model != null ? _ronin.Model.GetComponent<Animator>() : null;
                if (animator != null && animator.isHuman) _leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                // Cool rim from behind and above (away from the camera) so the blue-black coat separates from the
                // warm sodium key; characters only.
                float h = playerHeight * RoninHeightRatio;
                var rimGo = new GameObject("RoninRim");
                rimGo.transform.SetParent(_roninRoot, false);
                rimGo.transform.localPosition = new Vector3(0f, h * 0.9f, h * 0.55f);
                _roninRim = rimGo.AddComponent<Light>();
                _roninRim.type = LightType.Point;
                _roninRim.color = RoninRimColor;
                _roninRim.intensity = RoninRimIntensity;
                _roninRim.range = h * 2.2f;
                _roninRim.shadows = LightShadows.None;
                _roninRim.GetUniversalAdditionalLightData().renderingLayers = (RenderingLayerMask)(1u << Arena3DRenderer.CharacterRenderingLayer);
                // Front fill from the camera side, above the head.
                var fillGo = new GameObject("RoninFill");
                fillGo.transform.SetParent(_roninRoot, false);
                fillGo.transform.localPosition = new Vector3(0f, h * 0.85f, -h * 1.3f);
                var fill = fillGo.AddComponent<Light>();
                fill.type = LightType.Point;
                fill.color = RoninFillColor;
                fill.intensity = RoninFillIntensity;
                fill.range = h * 2.6f;
                fill.shadows = LightShadows.None;
                fill.GetUniversalAdditionalLightData().renderingLayers = (RenderingLayerMask)(1u << Arena3DRenderer.CharacterRenderingLayer);
                _ronin.EnableTrail(Actors3DAssets.FxAdditive);
            }

            _ropeGlow = Actors3DAssets.CreateQuad("RopeGlow", _root, Actors3DAssets.FxAdditive, false, false);
            _rope = Actors3DAssets.CreateQuad("Rope", _root, Actors3DAssets.FxSolid, false, false);   // opaque: in the depth texture, so DOF keeps it sharp
            _anchorCore = Actors3DAssets.CreateQuad("AnchorCore", _root, Actors3DAssets.FxSolid, false, false);
            _anchor = Actors3DAssets.CreateQuad("Anchor", _root, Actors3DAssets.FxAdditive, false, false);
            _playerBlob = Actors3DAssets.CreateQuad("PlayerContactShadow", _root, Actors3DAssets.FxAlpha, false, false);
            _playerBlobCore = Actors3DAssets.CreateQuad("PlayerContactShadowCore", _root, Actors3DAssets.FxAlpha, false, false);
        }

        /// <summary>Neutral-warm front light on the baked ronin sprite (characters only): keeps the samurai in his own colours
        /// against the cool neon level so he reads apart from the background.</summary>
        public static Color BakedFillColor = new Color(1f, 0.93f, 0.85f);
        /// <summary>Intensity of <see cref="BakedFillColor"/>.</summary>
        public static float BakedFillIntensity = 4.5f;
        /// <summary>Warm back rim on the baked ronin sprite (characters only): a warm edge against the cool background.</summary>
        public static Color BakedRimColor = new Color(1f, 0.7f, 0.42f);
        /// <summary>Intensity of <see cref="BakedRimColor"/>.</summary>
        public static float BakedRimIntensity = 24f;

        private static void AddBakedLights(Transform root, float h)
        {
            var mask = (RenderingLayerMask)(1u << Arena3DRenderer.CharacterRenderingLayer);
            var fillGo = new GameObject("BakedRoninFill");
            fillGo.transform.SetParent(root, false);
            fillGo.transform.localPosition = new Vector3(0f, h * 0.8f, -h * 1.4f);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.color = BakedFillColor;
            fill.intensity = BakedFillIntensity;
            fill.range = h * 2.8f;
            fill.shadows = LightShadows.None;
            fill.GetUniversalAdditionalLightData().renderingLayers = mask;

            var rimGo = new GameObject("BakedRoninRim");
            rimGo.transform.SetParent(root, false);
            rimGo.transform.localPosition = new Vector3(0f, h * 0.95f, h * 0.5f);
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Point;
            rim.color = BakedRimColor;
            rim.intensity = BakedRimIntensity;
            rim.range = h * 2.2f;
            rim.shadows = LightShadows.None;
            rim.GetUniversalAdditionalLightData().renderingLayers = mask;
        }

        private readonly MeshRenderer _playerBlob, _playerBlobCore;
        private readonly List<MeshRenderer> _enemyBlobs = new List<MeshRenderer>(16);
        private bool _playerHidden;

        private void SyncContactShadows(RedlineSim sim, Vector2 playerFeet, IReadOnlyList<Vector2> enemyFeet)
        {
            var p = sim.Player;
            bool showPlayer = ContactShadows && !_playerHidden && p.Grounded && sim.State != SimState.GameOver;
            _playerBlob.enabled = _playerBlobCore.enabled = showPlayer;
            if (showPlayer) PlaceBlob(_playerBlob, _playerBlobCore, playerFeet, p.Width);
            var enemies = sim.Enemies;
            while (_enemyBlobs.Count < enemies.Count * 2)
                _enemyBlobs.Add(Actors3DAssets.CreateQuad("EnemyContactShadow", _root, Actors3DAssets.FxAlpha, false, false));
            for (int i = 0; i * 2 < _enemyBlobs.Count; i++)
            {
                bool on = ContactShadows && i < enemies.Count && enemies[i].Alive && enemies[i].Grounded && enemies[i].Kind != EnemyKind.Drone;
                _enemyBlobs[2 * i].enabled = _enemyBlobs[2 * i + 1].enabled = on;
                if (!on) continue;
                var e = enemies[i];
                var f = enemyFeet != null && i < enemyFeet.Count ? enemyFeet[i] : new Vector2(e.X, e.Y);
                PlaceBlob(_enemyBlobs[2 * i], _enemyBlobs[2 * i + 1], f, e.Width);
            }
        }

        /// <summary>Destroys the actor GameObjects (and with them the baked ronin's materials) and clears <see cref="Instance"/>.
        /// Dropping the view by destroying its parent has the same effect except for the static reference, which
        /// <see cref="Instance"/> then reports as null.</summary>
        public void Dispose()
        {
            _baked?.Dispose();
            if (_root != null) Object.Destroy(_root.gameObject);
            if (_instance == this) _instance = null;
        }

        // Pixel-art contact ellipse: two stacked solid black bands on the 1/48 grid, 3 texels tall, just above the floor
        // line and behind the sprite (z > 0): the wide band at ContactShadowAlpha, the core (60% width) doubles it. A radial
        // glow cannot be used here: across a 2-texel height its falloff leaves under 0.2 opacity.
        private void PlaceBlob(MeshRenderer wide, MeshRenderer core, Vector2 feet, float width)
        {
            float w = Mathf.Max(0.3f, width * ContactShadowWidth);
            float x = Mathf.Round(feet.x * Grid) / Grid, y = Mathf.Round(feet.y * Grid) / Grid;
            Band(wide, x, y, Mathf.Round(w * Grid) / Grid);
            Band(core, x, y, Mathf.Round(w * 0.6f * Grid) / Grid);
        }

        private void Band(MeshRenderer r, float x, float y, float w)
        {
            var t = r.transform;
            t.localPosition = new Vector3(x, y + ContactShadowHeight * 0.5f, 0.06f);
            t.localRotation = Quaternion.identity;
            t.localScale = new Vector3(w, ContactShadowHeight, 1f);
            _mpb.SetColor(ColorId, new Color(0f, 0f, 0f, ContactShadowAlpha));
            _mpb.SetVector(ParamsId, new Vector4(Solid, 0.2f, 0f, 0f));
            r.SetPropertyBlock(_mpb);
        }

        /// <inheritdoc/>
        public SpriteRenderer PlayerView => null;

        /// <summary>The baked ronin sprite view (null when the sheet is missing).</summary>
        public BakedRoninView Baked => _baked;

        /// <summary>Feet-pivot transform of whichever player representation is shown.</summary>
        public Transform PlayerTransform => UseBakedRonin && _baked != null ? _baked.Root : _roninRoot;

        /// <summary>Shows or hides the player (dev capture: mask render).</summary>
        public void SetPlayerVisible(bool v)
        {
            bool baked = UseBakedRonin && _baked != null;
            _playerHidden = !v;
            if (_playerBlob != null && !v) _playerBlob.enabled = _playerBlobCore.enabled = false;
            if (_baked != null) _baked.SetVisible(v && baked);
            if (_roninRoot == null) return;
            // The live model is hidden through its renderers, not SetActive: re-enabling an Animator rebinds the graph.
            _roninRoot.gameObject.SetActive(!baked);
            _liveRenderers ??= _roninRoot.GetComponentsInChildren<Renderer>(true);
            _liveEnabled ??= new bool[_liveRenderers.Length];
            if (!v && !_liveHidden)
                for (int i = 0; i < _liveRenderers.Length; i++) { _liveEnabled[i] = _liveRenderers[i].enabled; _liveRenderers[i].enabled = false; }
            else if (v && _liveHidden)
                for (int i = 0; i < _liveRenderers.Length; i++) _liveRenderers[i].enabled = _liveEnabled[i];
            _liveHidden = !v;
        }

        private Renderer[] _liveRenderers;
        private bool[] _liveEnabled;
        private bool _liveHidden;

        /// <summary>The ronin presenter (null when its assets are missing).</summary>
        public RoninView Ronin => _ronin;

        /// <summary>Drawing position of the player's feet in the last <see cref="Sync"/>.</summary>
        public Vector2 PlayerFeet => _lastFeet;

        /// <inheritdoc/>
        public void Squash(float amount) => _stretch = amount;

        /// <inheritdoc/>
        public void Sync(RedlineSim sim, Vector2 playerFeet, IReadOnlyList<Vector2> enemyFeet, float dt)
        {
            _time += dt;
            _stretch = Mathf.Lerp(_stretch, 1f, Mathf.Min(1f, dt * 12f));
            int tier = Mathf.Clamp(sim.PlayerTier, 1, 3);
            _enemies.Sync(sim, playerFeet, enemyFeet, dt);
            SyncRonin(sim, playerFeet, tier, dt);
            SyncRope(sim, playerFeet, tier);
            SyncShots(sim, playerFeet, tier);
            SyncContactShadows(sim, playerFeet, enemyFeet);
        }

        private void SyncRonin(RedlineSim sim, Vector2 feet, int tier, float dt)
        {
            bool bakedMode = UseBakedRonin && _baked != null;
            // Mode flip: the newly shown representation has never received the tier colour, so force a re-send.
            if (bakedMode != _bakedShown) { _bakedShown = bakedMode; _tier = -1; SetPlayerVisible(true); }
            if (_ronin == null && _baked == null) return;
            var p = sim.Player;
            bool dead = sim.State == SimState.GameOver;
            if (_lastHp != int.MinValue && p.Hp < _lastHp && !dead) _hurt = HurtHold;
            else _hurt = Mathf.Max(0f, _hurt - dt);
            _lastHp = p.Hp;

            var input = new RoninInput
            {
                Speed = Mathf.Abs(p.Vx),
                Grounded = p.Grounded,
                VelocityY = p.Vy,
                Sliding = p.Sliding,
                SlideTier = p.SlideTier,
                Hooked = p.Hooked,
                Attacking = p.Attacking,
                StrikeId = p.StrikeId,
                Hurt = _hurt > 0f,
                Dead = dead,
                Facing = p.Facing >= 0 ? 1 : -1,
            };
            if (bakedMode)
            {
                _lastFeet = feet;
                _hasLast = true;
                if (tier != _tier || TierColor(tier) != _tierColor) { _tier = tier; _tierColor = TierColor(tier); _baked.SetTierColor(_tierColor); }   // RedlineGame.TierColors, follows Sim.PlayerTier
                _baked.Tick(input, feet, dt);
                // The camera moved before the actors this frame (RedlineGame.UpdateCamera3D); re-place it on the sprite's
                // snap so the followed sprite keeps a constant texel offset (no ±1 back-and-forth while running).
                if (_rig != null && LockCameraToRonin) _rig.LockSubject(feet);
                return;
            }
            if (_ronin == null) return;
            _ronin.Input = input;
            float s = p.Sliding || dead ? 1f : _stretch;
            _roninRoot.localPosition = new Vector3(feet.x, feet.y, 0f);
            _roninRoot.localScale = new Vector3(1f / Mathf.Sqrt(s), s, 1f);
            bool teleported = _hasLast && (feet - _lastFeet).sqrMagnitude > TeleportDistance * TeleportDistance;
            _lastFeet = feet;
            if (!_hasLast || teleported)
            {
                _hasLast = true;
                _ronin.SnapFacing(_ronin.Input.Facing);
                _ronin.ResetSprings();
            }
            if (tier != _tier || TierColor(tier) != _tierColor)
            {
                _tier = tier;
                _tierColor = TierColor(tier);
                _ronin.SetTierColor(TierColor(tier) * TierHdr);
            }
        }

        private void SyncRope(RedlineSim sim, Vector2 feet, int tier)
        {
            var p = sim.Player;
            bool show = p.Hooked && sim.State != SimState.GameOver;
            _rope.enabled = _ropeGlow.enabled = _anchor.enabled = _anchorCore.enabled = show;
            if (!show) return;
            Vector3 from3 = UseBakedRonin && _baked != null
                ? new Vector3(feet.x + _baked.HandOffset.x, feet.y + _baked.HandOffset.y, 0f)
                : _leftHand != null ? _leftHand.position : new Vector3(feet.x, feet.y + p.Height * 0.75f, 0f);
            var from = new Vector2(from3.x, from3.y);
            var to = new Vector2(feet.x + sim.Map.Dx(p.X, p.HookAnchor.X), p.HookAnchor.Y);
            var d = to - from;
            var dir = d.sqrMagnitude > 1e-6f ? d.normalized : Vector2.up;
            float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            var rot = Quaternion.Euler(0f, 0f, ang);
            float z = from3.z * 0.5f;
            // Grapnel: its prongs bite at the anchor, the ring (rope end) trails back toward the player.
            var hookCentre = to - dir * (HookSize * 0.3f);
            var ringEnd = to - dir * (HookSize * 0.82f);
            var rd = ringEnd - from;
            float len = Mathf.Max(0.01f, rd.magnitude);
            var mid = (from + ringEnd) * 0.5f;
            // Twisted hemp rope: the texture repeats every RopeAspect widths, so the plies keep their size at any length.
            PlaceTex(_rope, new Vector3(mid.x, mid.y, z), rot, new Vector2(len, RopeWidth), RopeArt.Rope,
                new Vector4(len / (RopeWidth * RopeArt.RopeAspect), 1f, 0f, 0f));
            PlaceTex(_anchorCore, new Vector3(hookCentre.x, hookCentre.y, z * 0.5f), rot, new Vector2(HookSize, HookSize), RopeArt.Hook,
                new Vector4(1f, 1f, 0f, 0f));
            // A faint tier-coloured spark where the hook bites (the old neon line and glow are gone).
            Place(_anchor, new Vector3(to.x, to.y, 0f), Quaternion.identity, new Vector2(0.35f, 0.35f), TierColor(tier) * 0.8f, Glow);
            _ropeGlow.enabled = false;
        }

        /// <summary>Textured opaque quad (Fx shape 3): point-sampled, alpha-cut texture tinted white.</summary>
        private void PlaceTex(MeshRenderer r, Vector3 pos, Quaternion rot, Vector2 size, Texture tex, Vector4 st)
        {
            var t = r.transform;
            t.localPosition = pos;
            t.localRotation = rot;
            t.localScale = new Vector3(size.x, size.y, 1f);
            _texMpb.SetColor(ColorId, Color.white);
            _texMpb.SetVector(ParamsId, new Vector4(3f, 0.2f, 0f, 0f));
            _texMpb.SetTexture(MainTexId, tex);
            _texMpb.SetVector(MainTexStId, st);
            r.SetPropertyBlock(_texMpb);
        }

        private void SyncShots(RedlineSim sim, Vector2 playerFeet, int tier)
        {
            var shots = sim.Projectiles;
            while (_shots.Count < shots.Count)
            {
                _shotGlows.Add(Actors3DAssets.CreateQuad("ShotGlow", _root, Actors3DAssets.FxAdditive, false, false));
                _shots.Add(Actors3DAssets.CreateQuad("Shot", _root, Actors3DAssets.FxAdditive, false, false));
            }
            var p = sim.Player;
            for (int i = 0; i < _shots.Count; i++)
            {
                bool on = i < shots.Count && !shots[i].Mud;   // the boss's mud clumps are drawn by BossView3D
                _shots[i].enabled = _shotGlows[i].enabled = on;
                if (!on) continue;
                var shot = shots[i];
                var c = shot.Reflected ? TierColor(tier) : ShotColor;
                var pos = new Vector3(playerFeet.x + sim.Map.Dx(p.X, shot.X), shot.Y + shot.Height * 0.5f, 0f);
                float pulse = 1f + Mathf.Sin(_time * 30f + i) * 0.12f;
                float core = shot.Width * 1.3f * pulse;
                Place(_shots[i], pos, Quaternion.Euler(0f, 0f, _time * 360f), new Vector2(core, core),
                    Color.Lerp(c, Color.white, 0.35f) * ShotHdr, Solid);
                float gs = shot.Width * 4.5f * pulse;
                Place(_shotGlows[i], pos, Quaternion.identity, new Vector2(gs, gs), c * ShotGlowHdr, Glow);
            }
        }

        private void Place(MeshRenderer r, Vector3 pos, Quaternion rot, Vector2 size, Color hdr, float shape)
        {
            var t = r.transform;
            t.localPosition = pos;
            t.localRotation = rot;
            t.localScale = new Vector3(size.x, size.y, 1f);
            hdr.a = 1f;
            _mpb.SetColor(ColorId, hdr);
            _mpb.SetVector(ParamsId, new Vector4(shape, 0.2f, 0f, 0f));
            r.SetPropertyBlock(_mpb);
        }

        private static Color TierColor(int tier) => RedlineGame.TierColors[Mathf.Clamp(tier, 1, 3) - 1];

        /// <summary>Scale that makes the model's bind-pose height RoninHeightRatio × the config player height.</summary>
        private static float MeasureScale(Transform model, float target)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 1f;
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            float h = b.max.y - model.position.y;
            return h > 0.01f ? target / h : 1f;
        }
    }
}
