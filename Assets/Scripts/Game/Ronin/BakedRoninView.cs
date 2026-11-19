using System;
using System.Collections.Generic;
using Redline.Core.Presentation;
using Redline.Game.Art;
using Redline.Game.Render3D.Actors3D;
using UnityEngine;

namespace Redline.Game.Ronin
{
    /// <summary>
    /// The Tech Ronin as a lit sprite quad (pre-rendered from the 3D model by <c>RoninSpriteBaker</c>): the same machinery
    /// as the enemies (URP Lit alpha-clipped body with the baked normal map, one unlit overlay for the emissive masks),
    /// snapped to the 1/48 world grid (<see cref="Render3D.Pixel3DCamera.TexelsPerUnit"/>), facing by flip. Frames are fixed pixels, so nothing can shimmer between frames.
    /// Emissive channels: R katana blade (tier colour), G the two eye dots (dim cool white). B is unused: the hat brim is a
    /// dark non-emissive trim baked into the albedo (art bible section 5: tier shows on the katana only).
    /// </summary>
    /// <remarks>Main thread only. No per-frame allocation after construction. The two materials it creates are
    /// destroyed with its root GameObject (a <see cref="MaterialOwner"/> on the root), or by <see cref="Dispose"/>.</remarks>
    public sealed class BakedRoninView : IDisposable
    {
        /// <summary>Resources path of the baked sheet.</summary>
        public const string SheetPath = "Art/Characters/Ronin/ronin";
        /// <summary>Texels per world unit (matches the 640x320 / 6.67 render target and the sheet's 48 ppu).</summary>
        public const float PixelGrid = Render3D.Pixel3DCamera.TexelsPerUnit;
        /// <summary>HDR multiplier of the tier colour on the blade mask.</summary>
        public static float TierHdr = 2.2f;
        /// <summary>HDR multiplier of the eye / cyber-line mask.</summary>
        public static float EyesHdr = 1.5f;
        /// <summary>Eye and cyber line colour.</summary>
        public static Color EyeColor = new Color(0.8f, 0.9f, 1f);

        private static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int FrameSt = Shader.PropertyToID("_FrameST");
        private static readonly int TintR = Shader.PropertyToID("_TintR");
        private static readonly int TintG = Shader.PropertyToID("_TintG");
        private static readonly int TintB = Shader.PropertyToID("_TintB");
        private static readonly int FillId = Shader.PropertyToID("_Fill");
        private static readonly int FlashId = Shader.PropertyToID("_Flash");
        private static readonly int RimId = Shader.PropertyToID("_Rim");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");

        private readonly CharacterSheet _sheet;
        private readonly SheetAnimation[] _anims;
        private readonly Dictionary<string, Vector2> _hands = new Dictionary<string, Vector2>(256, StringComparer.Ordinal);
        private readonly float[] _strides;
        /// <summary>Lowest locomotion playback rate (near standstill the cycle would otherwise freeze).</summary>
        public static float MinLocomotionRate = 0.35f;
        private readonly SpriteAnimator _anim = new SpriteAnimator();
        private readonly RoninStateMachine _machine = new RoninStateMachine();
        private readonly Transform _root, _quad;
        private readonly MeshRenderer _body, _overlay;
        private readonly Material _lit, _ov;
        private readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        private Color _tier = Color.white;
        private bool _flip;
        private Vector2 _hand, _handRaw = new Vector2(0f, 0.9f);
        private Sprite _handSprite;

        /// <summary>The state machine (attack duration etc. can be tuned).</summary>
        public RoninStateMachine Machine => _machine;
        /// <summary>The animation currently shown.</summary>
        public BakedAnim Current => _machine.Current;
        /// <summary>Frame index currently shown.</summary>
        public int Frame => _anim.Frame;
        /// <summary>Root transform (feet pivot).</summary>
        public Transform Root => _root;
        /// <summary>World offset of the left hand from the feet in the current frame (flip applied).</summary>
        public Vector2 HandOffset => _hand;

        /// <summary>Loads the sheet and builds the quads under <paramref name="parent"/>. Throws if the sheet is missing.</summary>
        public BakedRoninView(Transform parent)
        {
            _sheet = CharacterSheet.Load(SheetPath);
            var names = (BakedAnim[])Enum.GetValues(typeof(BakedAnim));
            _anims = new SheetAnimation[names.Length];
            foreach (var n in names) _sheet.TryGetAnimation(RoninStateMachine.Name(n), out _anims[(int)n]);
            if (_anims[(int)BakedAnim.Idle] == null) throw new InvalidOperationException("Ronin sheet has no idle animation");
            if (_anims[(int)BakedAnim.Hurt] != null) _machine.HurtDuration = _anims[(int)BakedAnim.Hurt].Duration;
            if (_anims[(int)BakedAnim.Attack1] != null) _machine.AttackDuration = _anims[(int)BakedAnim.Attack1].Duration;
            if (_anims[(int)BakedAnim.Land] != null) _machine.LandDuration = _anims[(int)BakedAnim.Land].Duration;
            else _machine.LandSpeed = float.MaxValue;   // an older sheet without the land squash
            if (_anims[(int)BakedAnim.Sprint] == null) _machine.SprintThreshold = _machine.SprintExitThreshold = float.MaxValue;
            _strides = new float[names.Length];
            LoadHands();

            var color = _anims[(int)BakedAnim.Idle].Frame(0).texture;
            var normal = Resources.Load<Texture2D>(SheetPath + "_normal");
            _lit = Actors3DAssets.SpriteLit("RoninBakedLit", color, normal, false);
            _ov = new Material(Actors3DAssets.Overlay) { name = "RoninBakedOverlay" };
            _ov.SetTexture("_ColorTex", color);
            if (normal != null) _ov.SetTexture("_BumpTex", normal);
            for (int c = 0; c < 3; c++)
            {
                var mask = FindMask((EmissiveChannel)c);
                _ov.SetTexture(c == 0 ? "_MaskR" : c == 1 ? "_MaskG" : "_MaskB", mask != null ? mask : Texture2D.blackTexture);
            }
            _ov.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 5;

            _root = new GameObject("RoninBaked").transform;
            _root.SetParent(parent, false);
            _quad = new GameObject("quad").transform;
            _quad.SetParent(_root, false);
            _body = Actors3DAssets.CreateQuad("body", _quad, _lit, true, true);
            _overlay = Actors3DAssets.CreateQuad("overlay", _quad, _ov, false, true);
            _overlay.transform.localPosition = new Vector3(0f, 0f, -0.02f);
            _root.gameObject.AddComponent<MaterialOwner>().Materials = new[] { _lit, _ov };
            _anim.Play(_anims[(int)BakedAnim.Idle], true);
        }

        /// <summary>Destroys the sprite's GameObjects and its two materials. Safe to call twice.</summary>
        public void Dispose()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);   // MaterialOwner.OnDestroy frees the materials
        }

        /// <summary>Frees runtime-created materials when its GameObject is destroyed (the views are dropped by
        /// destroying their host, e.g. RedlineGame.BuildViews, so the materials must follow the hierarchy).</summary>
        public sealed class MaterialOwner : MonoBehaviour
        {
            /// <summary>Materials destroyed with this GameObject.</summary>
            public Material[] Materials;

            private void OnDestroy()
            {
                if (Materials == null) return;
                foreach (var m in Materials) if (m != null) Destroy(m);
                Materials = null;
            }
        }

        private Texture FindMask(EmissiveChannel ch)
        {
            foreach (var a in _anims)
            {
                if (a == null) continue;
                for (int i = 0; i < a.FrameCount; i++)
                {
                    var s = a.Emissive(ch, i);
                    if (s != null) return s.texture;
                }
            }
            return null;
        }

        private void LoadHands()
        {
            var json = Resources.Load<TextAsset>(SheetPath);
            if (json == null) return;
            var data = JsonUtility.FromJson<HandJson>(json.text);
            if (data?.frames == null) return;
            foreach (var f in data.frames) if (f.hand != null) _hands[f.name] = new Vector2(f.hand.x, f.hand.y);
            if (data.animations != null)
                foreach (var an in data.animations)
                    foreach (BakedAnim b in Enum.GetValues(typeof(BakedAnim)))
                        if (RoninStateMachine.Name(b) == an.name) _strides[(int)b] = an.stride;
        }

        /// <summary>Tier colour (HDR base; <see cref="TierHdr"/> is applied here).</summary>
        public void SetTierColor(Color c) => _tier = c;

        /// <summary>Whether the body quad casts a real key-light shadow (the contact ellipse is separate).</summary>
        public bool CastShadows
        {
            get => _body.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off;
            set => _body.shadowCastingMode = value ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Shows or hides the sprite.</summary>
        public void SetVisible(bool v)
        {
            _root.gameObject.SetActive(v);
        }

        /// <summary>Advances the state machine and animation and positions the quads. <paramref name="feet"/> is the drawing position.</summary>
        public void Tick(in RoninInput input, Vector2 feet, float dt)
        {
            var state = new RoninStateInput
            {
                Speed = input.Speed, Grounded = input.Grounded, VelocityY = input.VelocityY, Sliding = input.Sliding,
                Hooked = input.Hooked, Attacking = input.Attacking, StrikeId = input.StrikeId, Hurt = input.Hurt, Dead = input.Dead,
            };
            var a = _machine.Step(state, dt);
            var sheetAnim = _anims[(int)a] ?? _anims[(int)BakedAnim.Idle];
            _anim.Play(sheetAnim, _machine.Restart);
            // Run and sprint: playback matched to the stride the baker measured (no skating, no upper cap).
            float rate = a == BakedAnim.Run || a == BakedAnim.Sprint
                ? RoninStateMachine.LocomotionRate(input.Speed, _strides[(int)a], sheetAnim.Duration, MinLocomotionRate) : 1f;
            _anim.Tick(dt, rate);
            _flip = input.Facing < 0;

            var pos = new Vector3(PixelSnap.Snap(feet.x, PixelGrid), PixelSnap.Snap(feet.y, PixelGrid), 0f);   // Pixel3DCamera.LockSubject reproduces this
            _root.localPosition = pos;
            _root.localScale = new Vector3(_flip ? -1f : 1f, 1f, 1f);

            var sprite = _anim.Current.Frame(_anim.Frame);
            var tex = sprite.texture;
            var r = sprite.rect;
            float ppu = sprite.pixelsPerUnit;
            var piv = sprite.pivot;
            _quad.localPosition = new Vector3((r.width * 0.5f - piv.x) / ppu, (r.height * 0.5f - piv.y) / ppu, 0f);
            _quad.localScale = new Vector3(r.width / ppu, r.height / ppu, 1f);
            var st = new Vector4(r.width / tex.width, r.height / tex.height, r.x / tex.width, r.y / tex.height);
            _body.enabled = _overlay.enabled = true;
            _mpb.Clear();
            _mpb.SetVector(BaseMapSt, st);
            _mpb.SetColor(BaseColor, Color.white);
            _body.SetPropertyBlock(_mpb);
            _mpb.Clear();
            _mpb.SetVector(FrameSt, st);
            var tier = _tier * TierHdr; tier.a = 1f;
            _mpb.SetColor(TintR, tier);
            _mpb.SetColor(TintG, EyeColor * EyesHdr);
            _mpb.SetColor(TintB, Color.clear);
            _mpb.SetColor(FillId, new Color(1f, 1f, 1f, Enemy3DRenderer.Fill3D));
            _mpb.SetColor(FlashId, Color.clear);
            _mpb.SetColor(RimId, new Color(0f, 0f, 0f, 0f));
            _mpb.SetFloat(AlphaId, 1f);
            _overlay.SetPropertyBlock(_mpb);

            if (sprite != _handSprite)   // Object.name allocates, so look the hand up only when the frame changes
            {
                _handSprite = sprite;
                _handRaw = _hands.TryGetValue(sprite.name, out var h) ? h : new Vector2(0f, 0.9f);
            }
            _hand = new Vector2(_flip ? -_handRaw.x : _handRaw.x, _handRaw.y);
        }

#pragma warning disable 0649
        [Serializable] private sealed class HandJson { public FrameHand[] frames; public AnimStride[] animations; }
        [Serializable] private sealed class AnimStride { public string name; public float stride; }
        [Serializable] private sealed class FrameHand { public string name; public HandPoint hand; }
        [Serializable] private sealed class HandPoint { public float x; public float y; }
#pragma warning restore 0649
    }
}
