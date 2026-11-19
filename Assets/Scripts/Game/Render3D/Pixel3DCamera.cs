using Redline.Core.Presentation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// The 3D-pixelated camera rig. Owns:
    /// <list type="bullet">
    /// <item>the perspective world camera, aimed at a point on the play plane (z = 0), pitched down slightly, at
    /// the distance where <see cref="FramingHeight"/> play-plane units fill the screen height;</item>
    /// <item>a low-resolution point-filtered RenderTexture (default 640×320 (2.0:1 letterboxed on a 16:9 screen) = exactly <see cref="TexelsPerUnit"/> (48) texels per play-plane unit at FramingHeight 6.67, i.e. 13.3 × 6.7 units of play plane, matching the 48 PPU character sheets 1:1 so pixels never crawl; ×3 to 1080p; plus a one-texel guard border);</item>
    /// <item>the point upscale to the screen: an orthographic presenter camera that sees one quad on layer
    /// <see cref="PresentLayer"/> showing the RenderTexture. UI Toolkit overlay panels draw after it at native
    /// resolution;</item>
    /// <item>its own post-processing Volume, isolated by volume layer mask so other global volumes (the 2D
    /// game's PostFx) do not stack on top.</item>
    /// </list>
    /// The camera is snapped to the texel grid of the play plane, and the sub-texel remainder is shown by
    /// sliding the presenter quad by whole screen pixels, so static pixels do not shimmer while scrolling.
    /// The upscale is an integer number of screen pixels per texel (<see cref="PixelUpscale"/>): 1920x1080 is x3,
    /// 2560x1440 x4, 3840x2160 x6, 1366x768 x2 with bars; the remainder is covered by four bar quads.
    /// The rig only places the view where it is told: following the player is <see cref="CameraFollow"/>'s job
    /// (RedlineGame.UpdateCamera3D), so there is exactly one follow system.
    /// </summary>
    /// <example>
    /// <code>
    /// var rig = cameraHost.AddComponent&lt;Pixel3DCamera&gt;();
    /// rig.ApplyLook(Arena3DLook.For("Loop01"));
    /// // each frame, after CameraFollow placed the picture centre:
    /// rig.SetTarget(new Vector2(follow.X, follow.Y));
    /// arena3D.Update(dt, rig.CameraPosition);
    /// // on hits:
    /// rig.Shake(0.4f);
    /// </code>
    /// </example>
    /// <remarks>Main thread only. Does not allocate per frame. Destroying the component releases the RT, the
    /// cameras and the volume profile.</remarks>
    public sealed class Pixel3DCamera : MonoBehaviour
    {
        /// <summary>GameObject layer used only by the presenter quad and the rig's Volume ("Pixel3DPresent").
        /// The world camera excludes it; the presenter camera sees only it. Unnamed in TagManager on purpose.</summary>
        public const int PresentLayer = 30;

        /// <summary>
        /// Texels per play-plane unit at the default framing (Height 320 / FramingHeight 6.67). Every character sheet is
        /// baked at this many pixels per unit and every sprite quad snaps to its 1/48 grid, so one sprite pixel is one
        /// texel (an integer 1:1 mapping; any other ratio shimmers while moving).
        /// </summary>
        public const float TexelsPerUnit = 48f;

        /// <summary>Vertical field of view of the world camera (degrees, for the visible area without the guard border).</summary>
        public float FieldOfView = 30f;
        /// <summary>Downward pitch of the world camera (degrees). Low, so the view reads side-on while the deck
        /// still visibly recedes; the camera sits PlaneDistance × sin(Pitch) above the aim point.</summary>
        public float Pitch = 3f;
        /// <summary>Play-plane units visible vertically (Height / <see cref="TexelsPerUnit"/> = 320 / 48). Smaller = closer =
        /// bigger characters; change it only together with <see cref="Height"/> or the sprite bake PPU.</summary>
        public float FramingHeight = 320f / TexelsPerUnit;
        /// <summary>The most recently built rig (null once destroyed); lets the actor view lock the texel snap to the ronin.</summary>
        public static Pixel3DCamera Latest { get; private set; }
        /// <summary>Shake decay per second (amount units).</summary>
        public float ShakeDecay = 4f;
        /// <summary>DOF focus distance behind the play plane (focus band is Z 0..+12; this is its optimum).</summary>
        public float FocusAhead = 5f;
        /// <summary>DOF lens: focal length (mm) and aperture (f-stop). Larger aperture number = sharper.</summary>
        public float DofFocalLength = 100f, DofAperture = 2.8f;
        /// <summary>Texel rows each letterbox bar grows by in cinematic mode (2.39:1 from 2.0:1 at 640x320 = 26; even, the bars move in 2-row steps).</summary>
        public int CinematicExtraTexels = 26;
        /// <summary>Seconds the bars take to move between 2.0:1 and 2.4:1.</summary>
        public float CinematicSeconds = 0.4f;
        /// <summary>World units of offset per unit of shake amount.</summary>
        public float ShakeScale = 0.35f;

        /// <summary>The perspective camera that renders the world into <see cref="Target"/>.</summary>
        public Camera WorldCamera { get; private set; }
        /// <summary>The orthographic camera that presents the upscaled image to the screen.</summary>
        public Camera PresentCamera { get; private set; }
        /// <summary>The low-res RenderTexture (Width+2 × Height+2, point filtered).</summary>
        public RenderTexture Target { get; private set; }
        /// <summary>Visible render width in texels.</summary>
        public int Width { get; private set; } = 640;
        /// <summary>Visible render height in texels.</summary>
        public int Height { get; private set; } = 320;
        /// <summary>Height of the top letterbox bar in pixels of the present camera's current output (screen or capture
        /// texture). Prefer <see cref="PictureFraction"/>, which also covers the side bars.</summary>
        public float LetterboxPixels { get; private set; }
        /// <summary>The visible picture as a fraction of the present output (origin top-left, y down): between all four
        /// bars, cropped to the output. Resolution-independent, so a UI panel of any size can scale it.</summary>
        public Rect PictureFraction { get; private set; } = new Rect(0f, 0f, 1f, 1f);
        /// <summary>The world camera's full viewport (the guarded render target incl. the slide) as a fraction of the
        /// present output (origin top-left, y down): maps <c>WorldToViewportPoint</c> results onto the screen.</summary>
        public Rect ViewportFraction { get; private set; } = new Rect(0f, 0f, 1f, 1f);
        /// <summary>Screen pixels per texel of the current layout (an integer).</summary>
        public int PixelsPerTexel { get; private set; } = 1;
        /// <summary>Largest fraction of the picture an integer upscale one step larger may crop on each axis (0 = never crop).</summary>
        public float MaxOverscan = 0.06f;
        /// <summary>True while the cinematic (2.4:1) bars are requested.</summary>
        public bool Cinematic => _cinematic;
        /// <summary>World camera position after the last <see cref="SetTarget"/> (snapped, shake included).</summary>
        public Vector3 CameraPosition { get; private set; }
        /// <summary>The rig's Volume profile (bloom, DOF, grade). Modify parameters, not components.</summary>
        public VolumeProfile Profile { get; private set; }
        /// <summary>Distance from the camera to the play plane along the view axis.</summary>
        public float PlaneDistance => FramingHeight * 0.5f / Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);

        private const int Guard = 1;
        private Transform _quad;
        private Material _quadMat;
        private Mesh _quadMesh;
        private Volume _volume;
        private DepthOfField _dof;
        private Bloom _bloom;
        private ColorAdjustments _color;
        private ShadowsMidtonesHighlights _smh;
        private Vignette _vignette;
        private FilmGrain _grain;
        private Vector2 _target;
        private float _shake;
        private float _shakeTime;
        private bool _built;
        private Transform _barTop, _barBottom, _barLeft, _barRight;
        private int _lastOutW, _lastOutH;
        private Material _barMat;
        private bool _cinematic;
        private float _cineT, _remX, _remY, _lastAspect;
        private int _tier = 1;
        private bool _calm;
        private float _calmT, _tierF = 1f;
        private float _keyBase = 2f, _vigBase = 0.25f, _satBase = -20f, _lastKey = -1f;

        private void Awake() { Latest = this; Build(); }

        /// <summary>Changes the low-res resolution (visible texels). Recreates the RenderTexture.</summary>
        public void SetResolution(int width, int height)
        {
            Width = Mathf.Max(16, width);
            Height = Mathf.Max(9, height);
            if (_built) CreateTarget();
        }

        /// <summary>
        /// Aims the camera at a point on the play plane (z = 0): the exact centre of the visible picture. Applies texel
        /// snapping and the rig's own shake (<see cref="Shake"/>). No smoothing or lead: the caller's follow decides.
        /// </summary>
        public void SetTarget(Vector2 playPlaneCentre)
        {
            _hasSubject = false;   // re-locked by LockSubject after the actors have moved this frame
            _target = playPlaneCentre;
            Place();
        }

        /// <summary>The last point passed to <see cref="SetTarget"/> (picture centre on the play plane, before snapping).</summary>
        public Vector2 Aim => _target;

        private Vector2 _subject;
        private bool _hasSubject;

        /// <summary>
        /// Locks the texel snap to the followed sprite for this frame: <paramref name="drawFeet"/> is the exact drawing
        /// position the sprite snaps with <see cref="PixelSnap.Snap"/> at <see cref="TexelsPerUnit"/>. Call after
        /// <see cref="SetTarget"/> and after the sprite moved (the next SetTarget releases it). Re-places the camera.
        /// </summary>
        /// <example><code>baked.Tick(input, feet, dt); rig.LockSubject(feet);</code></example>
        public void LockSubject(Vector2 drawFeet)
        {
            _subject = drawFeet;
            _hasSubject = true;
            Place();
        }

        /// <summary>The camera's sub-texel remainder (0..1 texels per axis) after the last placement.</summary>
        public Vector2 Remainder => new Vector2(_remX, _remY);

        /// <summary>The presenter slide of the last layout, whole output pixels per axis (0..PixelsPerTexel).</summary>
        public Vector2Int SlidePixels { get; private set; }

        /// <summary>Animates the letterbox bars to 2.4:1 (true) or back to gameplay 2.0:1 (false) over ~0.4 s in texel steps.</summary>
        public void SetCinematic(bool on) => _cinematic = on;

        /// <summary>Combat tier 1..3: key light +15 % / +30 % and vignette 25 % -> 40 % at tier 3 (bible section 2).</summary>
        public void SetTier(int tier) => _tier = Mathf.Clamp(tier, 1, 3);

        /// <summary>Between-waves exhale: key light -40 %, saturation halved.</summary>
        public void SetCalm(bool calm) => _calm = calm;

        /// <summary>Adds screen shake (amount in rough "hit" units: 0.2 light, 1 heavy). Decays over time.</summary>
        public void Shake(float amount) => _shake = Mathf.Max(_shake, amount);

        /// <summary>Applies a look's bloom and grade to the rig's Volume.</summary>
        public void ApplyLook(Arena3DLook look)
        {
            Build();
            _bloom.threshold.Override(look.BloomThreshold);
            _bloom.intensity.Override(look.BloomIntensity);
            _bloom.scatter.Override(look.BloomScatter);
            _bloom.tint.Override(look.BloomTint);
            _color.postExposure.Override(look.Exposure);
            _color.contrast.Override(look.Contrast);
            _color.saturation.Override(look.Saturation);
            _smh.shadows.Override(look.GradeShadows);
            _smh.highlights.Override(look.GradeHighlights);
            _vignette.intensity.Override(look.Vignette);
            _grain.intensity.Override(look.FilmGrain);
            _keyBase = look.MoonIntensity; _vigBase = look.Vignette; _satBase = look.Saturation; _lastKey = -1f;
            WorldCamera.backgroundColor = look.FogColor;
        }

        private void Build()
        {
            if (_built) return;
            _built = true;
            var worldGo = new GameObject("Pixel3D World Camera");
            worldGo.transform.SetParent(transform, false);
            WorldCamera = worldGo.AddComponent<Camera>();
            WorldCamera.nearClipPlane = 0.3f;
            WorldCamera.farClipPlane = 700f;
            WorldCamera.clearFlags = CameraClearFlags.SolidColor;
            WorldCamera.backgroundColor = Color.black;
            WorldCamera.cullingMask = ~(1 << PresentLayer);
            WorldCamera.depth = -10f;
            var wd = WorldCamera.GetUniversalAdditionalCameraData();
            wd.renderPostProcessing = true;
            wd.antialiasing = AntialiasingMode.None;
            wd.renderShadows = true;
            wd.volumeLayerMask = 1 << PresentLayer;

            var presentGo = new GameObject("Pixel3D Present Camera");
            presentGo.transform.SetParent(transform, false);
            presentGo.transform.localPosition = new Vector3(0f, 0f, -10000f);
            PresentCamera = presentGo.AddComponent<Camera>();
            PresentCamera.orthographic = true;
            PresentCamera.orthographicSize = 0.5f;
            PresentCamera.nearClipPlane = 0.1f;
            PresentCamera.farClipPlane = 10f;
            PresentCamera.clearFlags = CameraClearFlags.SolidColor;
            PresentCamera.backgroundColor = Color.black;
            PresentCamera.cullingMask = 1 << PresentLayer;
            PresentCamera.allowHDR = false;
            PresentCamera.allowMSAA = false;
            PresentCamera.depth = 10f;
            var pd = PresentCamera.GetUniversalAdditionalCameraData();
            pd.renderPostProcessing = false;
            pd.antialiasing = AntialiasingMode.None;
            pd.renderShadows = false;
            pd.volumeLayerMask = 0;

            _quadMesh = Proto3DMeshes.Quad(1f, 1f);
            _quadMat = Proto3DMaterials.FxMat("Pixel3DPresent", null, Color.white, 2, 0f);
            var q = new GameObject("Pixel3D Present Quad") { layer = PresentLayer };
            q.transform.SetParent(presentGo.transform, false);
            q.transform.localPosition = new Vector3(0f, 0f, 1f);
            q.AddComponent<MeshFilter>().sharedMesh = _quadMesh;
            var r = q.AddComponent<MeshRenderer>();
            r.sharedMaterial = _quadMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            _quad = q.transform;
            _barMat = Proto3DMaterials.FxMat("Pixel3DBar", null, Color.black, 2, 0f);
            _barTop = MakeBar(presentGo.transform, "Pixel3D Bar Top");
            _barBottom = MakeBar(presentGo.transform, "Pixel3D Bar Bottom");
            _barLeft = MakeBar(presentGo.transform, "Pixel3D Bar Left");
            _barRight = MakeBar(presentGo.transform, "Pixel3D Bar Right");
            // Captures retarget the present camera and call Render() directly: re-lay out for any new output size.
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;

            BuildVolume();
            CreateTarget();
            Place();
        }

        private Transform MakeBar(Transform parent, string name)
        {
            var b = new GameObject(name) { layer = PresentLayer };
            b.transform.SetParent(parent, false);
            b.AddComponent<MeshFilter>().sharedMesh = _quadMesh;
            var r = b.AddComponent<MeshRenderer>();
            r.sharedMaterial = _barMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return b.transform;
        }

        private void BuildVolume()
        {
            var go = new GameObject("Pixel3D Volume") { layer = PresentLayer };
            go.transform.SetParent(transform, false);
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 100f;
            Profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _bloom = Profile.Add<Bloom>(true);
            _bloom.threshold.Override(0.8f); _bloom.intensity.Override(1.6f); _bloom.scatter.Override(0.75f);
            _dof = Profile.Add<DepthOfField>(true);
            _dof.mode.Override(DepthOfFieldMode.Bokeh);
            _dof.focalLength.Override(DofFocalLength); _dof.aperture.Override(DofAperture);
            var tm = Profile.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
            _color = Profile.Add<ColorAdjustments>(true);
            _smh = Profile.Add<ShadowsMidtonesHighlights>(true);
            _vignette = Profile.Add<Vignette>(true); _vignette.smoothness.Override(0.45f);
            _grain = Profile.Add<FilmGrain>(true); _grain.type.Override(FilmGrainLookup.Thin1); _grain.intensity.Override(0.04f);
            _volume.sharedProfile = Profile;
            ApplyLook(new Arena3DLook());
        }

        private void CreateTarget()
        {
            if (Target != null)
            {
                WorldCamera.targetTexture = null;
                Target.Release();
                Destroy(Target);
            }
            Target = new RenderTexture(Width + 2 * Guard, Height + 2 * Guard, 24, RenderTextureFormat.ARGB32)
            {
                name = "Pixel3D Target",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
            };
            Target.Create();
            WorldCamera.targetTexture = Target;
            WorldCamera.aspect = (float)Target.width / Target.height;
            // FOV over the guarded height, keeping the play-plane framing of the visible Height rows.
            float halfTan = Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad) * (Height + 2f * Guard) / Height;
            WorldCamera.fieldOfView = 2f * Mathf.Atan(halfTan) * Mathf.Rad2Deg;
            _quadMat.SetTexture("_MainTex", Target);
            Layout();
        }

        /// <summary>Recomputes the presenter layout for the present camera's current output (call after changing its target or aspect).</summary>
        public void RefreshLayout() => Layout();

        private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam != PresentCamera || PresentCamera == null) return;
            if (cam.pixelWidth != _lastOutW || cam.pixelHeight != _lastOutH || !Mathf.Approximately(cam.aspect, _lastAspect)) Layout();
        }

        /// <summary>
        /// Presenter layout in whole output pixels (<see cref="PixelUpscale.Compute"/>): integer pixels per texel, the
        /// sub-texel slide rounded to whole pixels, the remainder covered by four bars; cinematic mode covers more rows.
        /// The ortho camera is 1 unit tall and <c>aspect</c> units wide.
        /// </summary>
        private void Layout()
        {
            if (_quad == null || PresentCamera == null) return;
            float a = PresentCamera.aspect;
            int outW = Mathf.Max(1, PresentCamera.pixelWidth), outH = Mathf.Max(1, PresentCamera.pixelHeight);
            _lastAspect = a; _lastOutW = outW; _lastOutH = outH;
            int extra = Mathf.RoundToInt(Mathf.Clamp01(_cineT) * CinematicExtraTexels / 2f) * 2;
            var l = PixelUpscale.Compute(outW, outH, Width, Height, Guard, _remX, _remY, extra, MaxOverscan);
            PixelsPerTexel = l.Scale;
            SlidePixels = new Vector2Int(l.SlideX, l.SlideY);
            PlacePx(_quad, l.QuadX, l.QuadY, l.QuadW, l.QuadH, outW, outH, a, 1f);
            // Bars run past the output edges (by a full output) so no edge pixel is ever left uncovered.
            SetBar(_barTop, -outW, l.VisibleY1, 3 * outW, 2 * outH, outW, outH, a);
            SetBar(_barBottom, -outW, -outH, 3 * outW, outH + l.VisibleY0, outW, outH, a);
            SetBar(_barLeft, -outW, l.VisibleY0, outW + l.VisibleX0, l.VisibleY1 - l.VisibleY0, outW, outH, a);
            SetBar(_barRight, l.VisibleX1, l.VisibleY0, 2 * outW - l.VisibleX1, l.VisibleY1 - l.VisibleY0, outW, outH, a);
            LetterboxPixels = outH - l.VisibleY1;
            PictureFraction = new Rect((float)l.VisibleX0 / outW, (float)(outH - l.VisibleY1) / outH,
                (float)(l.VisibleX1 - l.VisibleX0) / outW, (float)(l.VisibleY1 - l.VisibleY0) / outH);
            ViewportFraction = new Rect((float)l.QuadX / outW, (float)(outH - (l.QuadY + l.QuadH)) / outH,
                (float)l.QuadW / outW, (float)l.QuadH / outH);
        }

        private void SetBar(Transform bar, int x, int y, int w, int h, int outW, int outH, float aspect)
        {
            bool on = w > 0 && h > 0 && x < outW && y < outH && x + w > 0 && y + h > 0;
            if (bar.gameObject.activeSelf != on) bar.gameObject.SetActive(on);
            if (on) PlacePx(bar, x, y, w, h, outW, outH, aspect, 0.5f);
        }

        /// <summary>Places a centred unit quad over the output pixel rect (x, y, w, h), origin bottom-left.</summary>
        private static void PlacePx(Transform t, int x, int y, int w, int h, int outW, int outH, float aspect, float z)
        {
            float ux = aspect / outW, uy = 1f / outH;
            t.localScale = new Vector3(w * ux, h * uy, 1f);
            t.localPosition = new Vector3((x + w * 0.5f - outW * 0.5f) * ux, (y + h * 0.5f - outH * 0.5f) * uy, z);
        }

        private void Update()
        {
            if (!_built) return;
            float udt = Time.unscaledDeltaTime;
            float wantCine = _cinematic ? 1f : 0f;
            if (!Mathf.Approximately(_cineT, wantCine) || !Mathf.Approximately(_lastAspect, PresentCamera.aspect)
                || PresentCamera.pixelWidth != _lastOutW || PresentCamera.pixelHeight != _lastOutH)
            {
                _cineT = Mathf.MoveTowards(_cineT, wantCine, udt / Mathf.Max(0.01f, CinematicSeconds));
                Layout();
            }
            // Tier / calm response: key intensity, vignette, saturation (never the hue).
            _calmT = Mathf.MoveTowards(_calmT, _calm ? 1f : 0f, udt / 0.8f);
            float tierKey = _tier >= 3 ? 1.30f : _tier == 2 ? 1.15f : 1f;
            _tierF = Mathf.MoveTowards(_tierF, tierKey, udt * 0.5f);
            float key = _keyBase * _tierF * Mathf.Lerp(1f, 0.6f, _calmT);
            var sun = RenderSettings.sun;
            if (sun != null && !Mathf.Approximately(key, _lastKey)) { sun.intensity = key; _lastKey = key; }
            float vig = _tier >= 3 ? Mathf.Max(_vigBase, 0.40f) : _vigBase;
            _vignette.intensity.Override(vig);
            float sat = (1f + _satBase / 100f) * Mathf.Lerp(1f, 0.5f, _calmT) * 100f - 100f;
            _color.saturation.Override(sat);
            if (_shake <= 0f) return;
            _shakeTime += udt;
            _shake = Mathf.Max(0f, _shake - ShakeDecay * udt * Mathf.Max(0.25f, _shake));
            Place();
        }

        private void Place()
        {
            if (!_built) return;
            Vector2 t = _target;
            if (_shake > 0f)
            {
                float a = _shake * _shake * ShakeScale;
                t.x += a * (Mathf.PerlinNoise(_shakeTime * 23f, 0.1f) * 2f - 1f);
                t.y += a * (Mathf.PerlinNoise(0.7f, _shakeTime * 23f) * 2f - 1f);
            }
            // Snap to the play-plane texel grid; the remainder slides the presenter quad by a sub-texel amount.
            // The camera is pitched, so a world-Y move of dy shows on the image as dy·cos(Pitch): one image texel
            // vertically is texel / cos(Pitch) world units. Snapping Y to that step keeps static pixels from drifting.
            // With a locked subject (the grid-snapped player sprite) the camera also takes the sprite's snap residual,
            // so the sprite keeps a constant texel offset and remainder while followed (PixelSnap.LockCamera); without
            // it a run at a non-whole number of texels per frame stepped the sprite ±1 texel against the camera.
            float texel = FramingHeight / Height;
            float texelY = texel / Mathf.Max(0.5f, Mathf.Cos(Pitch * Mathf.Deg2Rad));
            float sx, sy, rx, ry;
            if (_hasSubject)
            {
                sx = PixelSnap.LockCamera(t.x, _subject.x, TexelsPerUnit, texel, out rx);
                sy = PixelSnap.LockCamera(t.y, _subject.y, TexelsPerUnit, texelY, out ry);
            }
            else
            {
                sx = PixelSnap.SnapCamera(t.x, texel, out rx);
                sy = PixelSnap.SnapCamera(t.y, texelY, out ry);
            }
            var rot = Quaternion.Euler(Pitch, 0f, 0f);
            var pos = new Vector3(sx, sy, 0f) - rot * Vector3.forward * PlaneDistance;
            WorldCamera.transform.SetPositionAndRotation(pos, rot);
            CameraPosition = pos;
            _remX = rx; _remY = ry;
            Layout();
            _dof.focusDistance.Override(PlaneDistance + FocusAhead);
            _dof.focalLength.Override(DofFocalLength); _dof.aperture.Override(DofAperture);
        }

        private void OnDestroy()
        {
            if (Latest == this) Latest = null;
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            if (WorldCamera != null) WorldCamera.targetTexture = null;
            if (Target != null) { Target.Release(); Destroy(Target); }
            if (_quadMat != null) Destroy(_quadMat);
            if (_barMat != null) Destroy(_barMat);
            if (_quadMesh != null) Destroy(_quadMesh);
            if (Profile != null) Destroy(Profile);
        }
    }
}
