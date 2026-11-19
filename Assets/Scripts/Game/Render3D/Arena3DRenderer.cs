using System;
using System.Collections.Generic;
using Redline.Core;
using Redline.Game.Atmosphere;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// The 3D level renderer for the 3D-pixelated (2.5D) mode. Replaces <c>ArenaRenderer</c> when
    /// <see cref="Render3DMode"/> is on. Builds, for any <see cref="TileMap"/>:
    /// <list type="bullet">
    /// <item>level geometry on the play plane (z = 0, depth +z): solid tile runs merged into rectangles and
    /// extruded into chamfered boxes with pixel-textured fronts and tops, spikes, and support structure under floating
        /// slabs (Arena3DStructure.cs);</item>
    /// <item>the receding rooftop deck (the floor row continues toward and away from the camera) with its
    /// parapet, ground clutter (puddles, cables, debris, low vents) and props behind the play plane;</item>
    /// <item>lamps placed by the 2D planner (<see cref="LampField.Plan"/>: platform edges and overhangs, spaced),
    /// with heads in front of z = 0 so camera-facing sprite quads are lit; red signals that blink;</item>
    /// <item>wet-deck reflections: rippled, flickering vertical streaks at each lamp's and neon light's X;</item>
    /// <item>blurred dark foreground framers at negative z;</item>
    /// <item>a fogged 3D city in rows, glowing mid-ground haze, sky, rain, and two cool back rim lights that
    /// only affect rendering layer <see cref="CharacterRenderingLayer"/>.</item>
    /// </list>
    /// Ring levels (<see cref="TileMap.WrapsX"/>) get three copies of everything level-bound (left, centre,
    /// right) that jump by whole level widths to stay under the camera; the city rows are periodic strips that
    /// jump by whole periods, so neither shows a seam. Only the lamps nearest the view (up to
    /// <see cref="Arena3DLook.MaxShadowedLamps"/>) cast real-time shadows; the set is updated each frame.
    /// </summary>
    /// <example>
    /// <code>
    /// Render3DMode.Enable();
    /// var rig = cameraHost.AddComponent&lt;Pixel3DCamera&gt;();
    /// var look = Arena3DLook.For("Loop01");
    /// rig.ApplyLook(look);
    /// var arena = new Arena3DRenderer(levelHost, sim.Map, look);
    /// // every frame:
    /// rig.SetTarget(new Vector2(camX, camY));
    /// arena.Update(Time.deltaTime, rig.CameraPosition);
    /// // level change:
    /// arena.Dispose();
    /// </code>
    /// </example>
    /// <remarks>
    /// Main thread only. All meshes, materials and textures are made in the constructor (allocates); Update does
    /// not allocate. Dispose destroys everything it made and restores the RenderSettings it changed.
    /// </remarks>
    public sealed partial class Arena3DRenderer : IDisposable
    {
        /// <summary>Rendering layer index for characters (receives the rim lights). Set it on character renderers.</summary>
        public const int CharacterRenderingLayer = 1;

        /// <summary>Renderer mask for characters: the default layer plus <see cref="CharacterRenderingLayer"/>.</summary>
        public const uint CharacterRenderingLayerMask = 1u | (1u << CharacterRenderingLayer);

        private const float FloorTop = 1f;
        private const float ShadowRange = 15f;
        private const float PlaneDistance = 20.5f;

        private static readonly float[] CityRows = { 26f, 42f, 64f, 92f, 132f, 185f };

        private readonly TileMap _map;
        private readonly Arena3DLook _look;
        private readonly Transform _root, _level, _follow;
        private readonly List<Object> _owned = new List<Object>();
        private readonly Props3D.Palette _palette;
        private readonly System.Random _rnd;
        private readonly SavedEnvironment _savedEnv;

        private Material _wallMat, _deckMat, _darkMat, _postMat, _headMat, _spikeMat, _glowMat, _solidAddMat, _wetMat,
            _shaftMat, _puddleMat, _fgMat, _hazeMat, _roofMat, _beaconMat, _signalMat;
        private readonly List<Material> _facades = new List<Material>();
        private Texture2D _blob, _haze;
        private Mesh _levelMesh, _deckMesh, _spikeMesh, _cone, _postMesh, _headMesh;
        private List<LampField.Spot> _spots;

        // Lamps across all copies (fixed arrays, no per-frame allocation).
        private Light[] _lampLights;
        private float[] _lampX;          // x relative to the level root (copy offset included)
        private bool[] _lampShadowed;
        private float[] _lampScore;
        private int _lampCount;
        private Light[] _signals;
        private float[] _signalPhase;
        private float[] _signalBase;
        private int _signalCount;
        private Transform[] _cityRows;
        private float[] _cityPeriod;
        private float _time;
        private bool _disposed;

        /// <summary>Root of everything this renderer built.</summary>
        public Transform Root => _root;

        /// <summary>Lamps (with a spot light) across all copies.</summary>
        public int LampCount => _lampCount;

        /// <summary>Lamps currently casting shadows.</summary>
        public int ShadowedLampCount { get; private set; }

        /// <summary>Real-time lights created (lamps, fills, signals, moon, rims).</summary>
        public int LightCount { get; private set; }

        /// <summary>Foreground framer roots (one per copy). Exposed for coverage measurement in captures.</summary>
        internal readonly List<Transform> Framers = new List<Transform>(3);

        /// <summary>Builds the 3D level for <paramref name="map"/> under <paramref name="parent"/> with <paramref name="look"/>.</summary>
        public Arena3DRenderer(Transform parent, TileMap map, Arena3DLook look)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _look = look ?? new Arena3DLook();
            _rnd = new System.Random(_look.Seed);
            _palette = new Props3D.Palette(_look);
            _savedEnv = SavedEnvironment.Capture();

            _root = new GameObject("Arena3D").transform;
            _root.SetParent(parent, false);
            _level = new GameObject("Level").transform;
            _level.SetParent(_root, false);
            _follow = new GameObject("FollowCamera").transform;
            _follow.SetParent(_root, false);

            Materials();
            ReflectionSetup(); // planar floor reflection (Arena3DReflections.cs)
            Environment();
            FrontFill();
            _spots = LampField.Plan(map);
            BuildLevelMeshes();

            var offsets = map.WrapsX ? new[] { -map.Width, 0, map.Width } : new[] { 0 };
            int maxLamps = _spots.Count * offsets.Length;
            _lampLights = new Light[maxLamps];
            _lampX = new float[maxLamps];
            _lampShadowed = new bool[maxLamps];
            _lampScore = new float[maxLamps];
            _signals = new Light[maxLamps];
            _signalPhase = new float[maxLamps];
            _signalBase = new float[maxLamps];
            foreach (int off in offsets) BuildCopy(off);

            City();
            FloodWater();
            Haze();
            Rain();
            Probe();
        }

        // ------------------------------------------------------------------------------------------------
        // Per frame
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Keeps the ring copies, city strips, haze and rain under the camera, picks the shadowed lamps, blinks the
        /// signals. <paramref name="cameraPos"/> is the world camera position (unwrapped X, as the game's camera).
        /// </summary>
        public void Update(float dt, Vector3 cameraPos)
        {
            if (_disposed) return;
            _time += dt;
            float camX = cameraPos.x;
            if (_map.WrapsX) _level.localPosition = new Vector3(_map.Width * Mathf.Floor(camX / _map.Width), 0f, 0f);
            _follow.localPosition = new Vector3(camX, 0f, 0f);
            for (int i = 0; i < _cityRows.Length; i++)
            {
                float p = _cityPeriod[i];
                _cityRows[i].localPosition = new Vector3(p * Mathf.Floor(camX / p) - camX, 0f, 0f);
            }
            UpdateShadows(camX - _level.localPosition.x);
            for (int i = 0; i < _signalCount; i++)
            {
                float s = Mathf.Repeat(_time * 0.9f + _signalPhase[i], 1f);
                _signals[i].intensity = s < 0.35f ? _signalBase[i] : _signalBase[i] * 0.08f;
            }
        }

        private void UpdateShadows(float camLocalX)
        {
            int candidates = 0;
            for (int i = 0; i < _lampCount; i++)
            {
                float d = Mathf.Abs(_lampX[i] - camLocalX);
                _lampScore[i] = d < ShadowRange ? d : float.MaxValue;
                if (d < ShadowRange) candidates++;
            }
            int want = Mathf.Min(_look.MaxShadowedLamps, candidates);
            // Mark the `want` nearest by repeated minimum (n ~ 100, want ~ 4).
            for (int k = 0; k < want; k++)
            {
                int best = -1; float bd = float.MaxValue;
                for (int i = 0; i < _lampCount; i++) if (_lampScore[i] >= 0f && _lampScore[i] < bd) { bd = _lampScore[i]; best = i; }
                if (best < 0) break;
                _lampScore[best] = -1f; // chosen
            }
            int on = 0;
            for (int i = 0; i < _lampCount; i++)
            {
                bool want1 = _lampScore[i] < 0f;
                if (want1) on++;
                if (want1 == _lampShadowed[i]) continue;
                _lampShadowed[i] = want1;
                _lampLights[i].shadows = want1 ? LightShadows.Soft : LightShadows.None;
            }
            ShadowedLampCount = on;
        }

        // ------------------------------------------------------------------------------------------------
        // Build: materials and environment
        // ------------------------------------------------------------------------------------------------

        private T Own<T>(T o) where T : Object { _owned.Add(o); return o; }

        private void Materials()
        {
            _darkMat = Own(Proto3DMaterials.Lit3D("dark", new Color(0.05f, 0.055f, 0.065f), 0.45f, 0.6f));
            _postMat = Own(Proto3DMaterials.Lit3D("lamppost", new Color(0.07f, 0.075f, 0.085f), 0.6f, 0.9f));
            _headMat = Own(Proto3DMaterials.Lit3D("lamphead", Color.black, 0f, 0f, emissionColor: _look.Sodium * 9f));
            _signalMat = Own(Proto3DMaterials.Lit3D("signal", Color.black, 0f, 0f, emissionColor: _look.Signal * 6f));
            _spikeMat = Own(Proto3DMaterials.Lit3D("spike", new Color(0.12f, 0.12f, 0.13f), 0.8f, 1f, emissionColor: _look.Signal * 0.25f));
            _fgMat = Own(Proto3DMaterials.Lit3D("framer", new Color(0.05f, 0.055f, 0.07f), 0.5f, 0.4f, emissionColor: _look.FogColor * 0.35f));
            _roofMat = Own(Proto3DMaterials.Lit3D("roof", new Color(0.06f, 0.065f, 0.08f), 0.5f, 0.3f));
            _beaconMat = Own(Proto3DMaterials.Lit3D("beacon", Color.black, 0f, 0f, emissionColor: _look.Signal * 6f));
            for (int i = 0; i < 4; i++)
            {
                var (al, em) = Proto3DTextures.Windows(100 + i + _look.Seed, _look.WindowsLit + i * 0.04f);
                Own(al); Own(em);
                _facades.Add(Own(Proto3DMaterials.Lit3D("facade" + i, Color.white, 0.35f, 0.2f, al, null, 1f, null, em, Color.white * _look.WindowGlow)));
            }
            _blob = Own(Proto3DTextures.Blob());
            _haze = Own(Proto3DTextures.HazeBand(5));
            var shaft = Own(Proto3DTextures.Shaft());
            _glowMat = Own(Proto3DMaterials.FxMat("glowAdd", _blob, Color.white, 0, 0.8f));
            _solidAddMat = Own(Proto3DMaterials.FxMat("solidAdd", null, Color.white, 0, 1f));
            _shaftMat = Own(Proto3DMaterials.FxMat("shaft", shaft, _look.Sodium * 0.24f, 0, 0.8f, 1.6f));
            _hazeMat = Own(Proto3DMaterials.FxMat("haze", _haze, Color.white, 1, 0.3f, 0f, -20));
            // Streaks stay as the lamp glints; the mirror render now carries the reflected scene, so they are softer.
            _wetMat = Own(Proto3DMaterials.FxMat("wetReflection", null, Color.white * (_look.ReflectionIntensity * 0.6f), 0, 1f, 0f, -30));
            _wetMat.SetFloat("_Wet", 1f);
            _wetMat.SetFloat("_Ripple", _look.ReflectionRipple);
            // Puddle: near-black mirror, alpha-clipped soft oval; picks up lamp speculars and the reflection probe.
            _puddleMat = Own(Proto3DMaterials.Sprite("puddle", _blob, null, 0.97f));
            _puddleMat.SetColor("_BaseColor", new Color(0.02f, 0.022f, 0.03f, 1f));
            _puddleMat.SetFloat("_Cutoff", 0.25f);
            _puddleMat.SetFloat("_Metallic", 0.2f);
            SurfaceMaterials(); // deck, wall, fascia, slab top, structure, clutter (Arena3DStructure.cs)
        }

        private void Environment()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = _look.FogDensity;
            RenderSettings.fogColor = _look.FogColor;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = _look.Ambient;
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(_look.Ambient);
            RenderSettings.ambientProbe = sh;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;

            // The key light: one strong directional per level (bible section 2), the only shadowed directional.
            var moon = NewLight("key", _root, LightType.Directional, _look.Moon, _look.MoonIntensity);
            moon.transform.rotation = Quaternion.LookRotation(_look.KeyDir.normalized);
            moon.shadows = LightShadows.Soft;
            moon.shadowStrength = 1f;
            moon.shadowBias = 0.02f; moon.shadowNormalBias = 0.4f;
            RenderSettings.sun = moon;

            var sky = Own(Proto3DTextures.Sky(_look.SkyHorizon, _look.SkyZenith, _look.SkyGlow));
            var skyMat = Own(Proto3DMaterials.FxMat("sky", sky, Color.white, 2, 0f, 0f, 400));
            var q = MeshGo("sky", _follow, Own(Proto3DMeshes.Quad(1600f, 520f)), skyMat);
            q.transform.localPosition = new Vector3(0f, 170f, 430f);
        }

        // ------------------------------------------------------------------------------------------------
        // Build: level geometry (shared meshes, map-local space)
        // ------------------------------------------------------------------------------------------------

        private void BuildLevelMeshes()
        {
            int w = _map.Width, h = _map.Height;
            var boxes = new List<Mesh>();
            var used = new bool[w, h];
            for (int y = 1; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (used[x, y] || !_map.IsSolid(x, y)) continue;
                int xe = x;
                while (xe + 1 < w && _map.IsSolid(xe + 1, y) && !used[xe + 1, y]) xe++;
                int ye = y;
                bool grow = true;
                while (grow && ye + 1 < h)
                {
                    for (int k = x; k <= xe; k++) if (!_map.IsSolid(k, ye + 1) || used[k, ye + 1]) { grow = false; break; }
                    if (grow) ye++;
                }
                for (int yy = y; yy <= ye; yy++) for (int k = x; k <= xe; k++) used[k, yy] = true;
                // Depth keyed by the ring start of the top row's run, so a run cut by the seam keeps one depth.
                float depth = 5f + Hash01(RingRunStart(x, ye), ye, 11) * 3f;
                RecordBox(x, xe + 1, y, ye + 1, depth);
                boxes.Add(Proto3DMeshes.ChamferBox(new Vector3(x, y, -0.5f), new Vector3(xe + 1, ye + 1, depth), 0.18f, 4f));
            }
            // Deck: per run of the floor row, extended past the ends of non-ring maps.
            var deck = new List<Mesh>();
            ForEachRun(0, (x0, x1) =>
            {
                float a = x0, b = x1;
                if (!_map.WrapsX) { if (x0 == 0) a -= 40f; if (x1 == w) b += 40f; }
                deck.Add(Proto3DMeshes.ChamferBox(new Vector3(a, -40f, _look.DeckNear), new Vector3(b, FloorTop, _look.DeckFar), 0.25f, _streetUnits));
                deck.Add(Proto3DMeshes.ChamferBox(new Vector3(a, FloorTop, _look.DeckFar - 0.6f), new Vector3(b, FloorTop + 0.7f, _look.DeckFar), 0.12f, 4f));
            });
            _levelMesh = Own(boxes.Count > 0 ? Proto3DMeshes.MergeTwoSub(boxes, "level") : new Mesh());
            SplitShadedSides(_levelMesh); // sub-mesh 2: side faces turned away from the key (Arena3DReflections.cs)
            _deckMesh = Own(deck.Count > 0 ? Proto3DMeshes.MergeTwoSub(deck, "deck") : new Mesh());

            var spikes = new List<CombineInstance>();
            for (int y = 1; y < h; y++)
            for (int x = 0; x < w; x++)
                if (_map.Get(x, y) == Tile.Spike)
                    spikes.Add(new CombineInstance { mesh = Proto3DMeshes.Spikes(x, y, -1.5f, 2.5f, 0.55f), transform = Matrix4x4.identity });
            _spikeMesh = Own(new Mesh { name = "spikes", indexFormat = IndexFormat.UInt32 });
            if (spikes.Count > 0) _spikeMesh.CombineMeshes(spikes.ToArray(), true, false);
            foreach (var c in spikes) Object.Destroy(c.mesh);

            _cone = Own(Proto3DMeshes.Cone(2.1f, 5f));
            BuildLampMeshes();
        }

        private void BuildLampMeshes()
        {
            var posts = new List<Mesh>();
            var heads = new List<Mesh>();
            foreach (var s in _spots)
            {
                var head = LampHead(s);
                float x = head.x;
                switch (s.Kind)
                {
                    case LampField.Kind.Post:
                    {
                        float baseY = s.Y + 1f, z = 1.4f, top = head.y + 0.35f;
                        posts.Add(Proto3DMeshes.ChamferBox(new Vector3(x - 0.09f, baseY, z - 0.09f), new Vector3(x + 0.09f, top, z + 0.09f), 0f, 4f));
                        posts.Add(Proto3DMeshes.ChamferBox(new Vector3(x - 0.08f, top - 0.12f, head.z), new Vector3(x + 0.08f, top, z), 0f, 4f));
                        heads.Add(Proto3DMeshes.ChamferBox(new Vector3(x - 0.35f, head.y + 0.05f, head.z - 0.25f), new Vector3(x + 0.35f, head.y + 0.22f, head.z + 0.25f), 0.04f, 4f));
                        break;
                    }
                    case LampField.Kind.Ceiling:
                    {
                        float y0 = s.Y;
                        posts.Add(Proto3DMeshes.ChamferBox(new Vector3(x - 0.06f, y0 - 0.12f, head.z), new Vector3(x + 0.06f, y0, -0.5f), 0f, 4f));
                        posts.Add(Proto3DMeshes.ChamferBox(new Vector3(x - 0.05f, head.y + 0.2f, head.z - 0.05f), new Vector3(x + 0.05f, y0 - 0.12f, head.z + 0.05f), 0f, 4f));
                        heads.Add(Proto3DMeshes.ChamferBox(new Vector3(x - 0.4f, head.y + 0.05f, head.z - 0.2f), new Vector3(x + 0.4f, head.y + 0.2f, head.z + 0.2f), 0.03f, 4f));
                        break;
                    }
                    default:
                        posts.Add(Proto3DMeshes.ChamferBox(new Vector3(x - 0.2f, head.y - 0.1f, head.z - 0.05f), new Vector3(x + 0.2f, s.Y, -0.5f), 0f, 4f));
                        break;
                }
            }
            _postMesh = Own(posts.Count > 0 ? Proto3DMeshes.MergeTwoSub(posts, "posts") : new Mesh());
            _headMesh = Own(heads.Count > 0 ? Proto3DMeshes.MergeTwoSub(heads, "heads") : new Mesh());
        }

        /// <summary>Lamp light position in map-local space; heads always in front of z = 0.</summary>
        private static Vector3 LampHead(LampField.Spot s)
        {
            switch (s.Kind)
            {
                case LampField.Kind.Post: return new Vector3(s.X + 0.5f, s.Y + 1f + 2.9f, -0.7f);
                case LampField.Kind.Ceiling: return new Vector3(s.X + 0.5f, s.Y - 0.75f, -1.1f);
                default: return new Vector3(s.X + 0.5f, s.Y - 0.35f, -0.75f);
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Build: one copy of the level (ring copies at -W, 0, +W)
        // ------------------------------------------------------------------------------------------------

        private void BuildCopy(int offsetX)
        {
            var copy = new GameObject("Copy " + offsetX).transform;
            copy.SetParent(_level, false);
            copy.localPosition = new Vector3(offsetX, 0f, 0f);
            Filled(MeshGo("level", copy, _levelMesh, _fasciaMat, _slabTopMat, _fasciaSideMat));
            Filled(Hidden(MeshGo("deck", copy, _deckMesh, _wallMat, _deckMat)));
            if (_spikeMesh.vertexCount > 0) Filled(MeshGo("spikes", copy, _spikeMesh, _spikeMat));
            DeckReflection(copy);
            if (_postMesh.vertexCount > 0) MeshGo("posts", copy, _postMesh, _postMat, _postMat);
            if (_headMesh.vertexCount > 0) MeshGo("heads", copy, _headMesh, _headMat, _headMat);

            // Same seed per copy: every copy is dressed identically.
            var rnd = new System.Random(_look.Seed * 31 + 7);
            var glows = new QuadBatch();
            var solids = new QuadBatch();
            var wet = new QuadBatch();
            var puddles = new QuadBatch();

            // (No neon trim strips under slabs: platforms read as built structure, see Arena3DStructure.cs.)

            // Lamps.
            foreach (var s in _spots)
            {
                var head = LampHead(s);
                if (s.Kind == LampField.Kind.Signal)
                {
                    solids.Billboard(head + new Vector3(0f, 0f, -0.12f), 0.22f, 0.22f, _look.Signal * 4f);
                    glows.Billboard(head + new Vector3(0f, 0f, -0.2f), 1.4f, 1.4f, _look.Signal * 0.9f);
                    var sig = NewLight("signal", copy, LightType.Point, _look.Signal, 6f);
                    sig.transform.localPosition = head + new Vector3(0f, 0f, -0.3f);
                    sig.range = 4.5f;
                    _signals[_signalCount] = sig;
                    _signalBase[_signalCount] = 6f;
                    _signalPhase[_signalCount] = Hash01(s.X, s.Y, 3);
                    _signalCount++;
                    continue;
                }
                glows.Billboard(head + new Vector3(0f, -0.05f, -0.3f), 2.4f, 2.4f, _look.Sodium * 1.4f);
                var shaft = MeshGo("shaft", copy, _cone, _shaftMat);
                shaft.transform.localPosition = head;
                var spot = NewLight("lampSpot", copy, LightType.Spot, _look.Sodium, _look.LampSpotIntensity);
                spot.transform.localPosition = head;
                spot.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                spot.spotAngle = 110f; spot.innerSpotAngle = 40f; spot.range = 15f;
                spot.shadowStrength = 0.95f;
                _lampLights[_lampCount] = spot;
                _lampX[_lampCount] = head.x + copy.localPosition.x;
                _lampCount++;
                // Wet reflection on the deck under the lamp, weaker the higher the lamp hangs.
                if (HasDeck(s.X))
                {
                    float k = 1f / (1f + 0.12f * (head.y - FloorTop - 3f));
                    wet.FloorStrip(new Vector3(head.x, FloorTop + 0.02f, -0.3f), 1.5f, 11f, _look.Sodium * Mathf.Clamp(k, 0.25f, 1.2f), (float)rnd.NextDouble());
                    glows.Floor(new Vector3(head.x, FloorTop + 0.015f, head.z - 0.2f), 5f, 3.6f, _look.Sodium * 0.22f);
                }
            }

            Props(copy, rnd, glows, wet);
            BuildDressing(copy);
            BuildStructure(copy);
            BuildRuins(copy); // Arena3DRuins.cs
            Clutter(copy, rnd, puddles);
            Framers.Add(BuildFramers(copy, rnd));

            if (glows.Count > 0) MeshGo("glows", copy, Own(glows.Build("glows")), _glowMat);
            if (solids.Count > 0) MeshGo("neonStrips", copy, Own(solids.Build("neonStrips")), _solidAddMat);
            if (wet.Count > 0) Hidden(MeshGo("wetReflections", copy, Own(wet.Build("wet")), _wetMat));
            if (puddles.Count > 0)
            {
                var pm = Own(puddles.Build("puddles"));
                if (_puddleReflMat != null) Hidden(MeshGo("puddles", copy, pm, _puddleMat, _puddleReflMat));
                else Hidden(MeshGo("puddles", copy, pm, _puddleMat));
            }
        }

        private void Props(Transform copy, System.Random rnd, QuadBatch glows, QuadBatch wet)
        {
            string[] back = { "water_tank", "ac_unit", "container", "stair_hut", "fan_tower", "vent_stack", "antenna_mast", "neon_sign", "exhaust_elbow", "sat_dish", "crates", "barrels", "cable_posts" }; // no "billboard": its pale panel is off-palette
            string[] onSlab = { "ac_unit", "vent_stack", "crates", "barrels", "sat_dish", "exhaust_elbow", "antenna_mast" };
            int k = (int)(rnd.NextDouble() * back.Length);
            // Behind the play plane on the deck.
            for (float x = 1f; x < _map.Width; x += 4.5f + (float)rnd.NextDouble() * 3.5f)
            {
                int ix = Mathf.FloorToInt(x);
                if (!HasDeck(ix) || _map.IsSolid(ix, 1) || _map.IsSolid(ix - 1, 1) || _map.IsSolid(ix + 1, 1)) continue;
                string name = back[k++ % back.Length];
                float z = 6f + (float)rnd.NextDouble() * 6.5f;
                var go = Props3D.Spawn(name, copy, _palette.For);
                if (go == null) continue;
                go.transform.localPosition = new Vector3(x, FloorTop, z);
                go.transform.localRotation = Quaternion.Euler(0f, -15f + (float)rnd.NextDouble() * 30f, 0f);
                go.transform.localScale = Vector3.one * 1.3f;
                if (name == "neon_sign" || name == "billboard")
                    wet.FloorStrip(new Vector3(x, FloorTop + 0.02f, -0.3f), 2.6f, 8f, _look.Neon * 0.35f, (float)rnd.NextDouble());
            }
            // On top of platforms, behind the play plane.
            for (int y = 2; y < _map.Height - 2; y++)
            {
                int yy = y;
                ForEachRun(y, (x0, x1) =>
                {
                    if (x1 - x0 < 3) return;
                    for (float x = x0 + 1f + (float)rnd.NextDouble() * 2f; x < x1 - 1f; x += 6f + (float)rnd.NextDouble() * 4f)
                    {
                        var go = Props3D.Spawn(onSlab[rnd.Next(onSlab.Length)], copy, _palette.For);
                        if (go == null) continue;
                        go.transform.localPosition = new Vector3(x, yy + 1f, 2.4f + (float)rnd.NextDouble() * 1.4f);
                        go.transform.localRotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 40f - 20f, 0f);
                        go.transform.localScale = Vector3.one * 0.9f;
                    }
                }, onlyTopEdge: true);
            }
            // Fence and railing along the parapet.
            ForEachRun(0, (x0, x1) =>
            {
                for (float x = x0 + 0.5f; x < x1; x += 3.2f)
                {
                    var go = Props3D.Spawn(((int)x / 3) % 3 == 0 ? "fence" : "railing", copy, _palette.For);
                    if (go == null) continue;
                    go.transform.localPosition = new Vector3(x, FloorTop + 0.7f, _look.DeckFar - 0.3f);
                    go.transform.localScale = Vector3.one * 1.3f;
                }
            });
        }

        /// <summary>
        /// Ground clutter in front of the play plane, kept low so it never hides feet (z -1.6..-3.2, height ≤ 0.22).
        /// Not deeper: the camera is only ≈ 9.3 units out, and a vent box at z -7 filled the bottom of the frame.
        /// </summary>
        private void Clutter(Transform copy, System.Random rnd, QuadBatch puddles)
        {
            var boxes = new List<Mesh>();
            var vents = new List<Mesh>();
            for (float x = 0.5f; x < _map.Width; x += 2.5f + (float)rnd.NextDouble() * 4f)
            {
                if (!HasDeck(Mathf.FloorToInt(x))) continue;
                double r = rnd.NextDouble();
                float z = -1.6f - (float)rnd.NextDouble() * 1.6f;
                if (r < 0.3)
                {
                    puddles.Floor(new Vector3(x, FloorTop + 0.006f, z), 1.2f + (float)rnd.NextDouble() * 2.6f, 0.5f + (float)rnd.NextDouble() * 1.2f, Color.white);
                }
                else if (r < 0.65)
                {
                    // Cable: a meandering chain of thin segments lying on the deck.
                    float len = 3f + (float)rnd.NextDouble() * 6f, ph = (float)rnd.NextDouble() * 6f;
                    for (float s = 0f; s < len; s += 0.35f)
                    {
                        float zz = z + Mathf.Sin((s + ph) * 0.9f) * 0.5f;
                        boxes.Add(Proto3DMeshes.ChamferBox(new Vector3(x + s, FloorTop, zz - 0.035f), new Vector3(x + s + 0.36f, FloorTop + 0.06f, zz + 0.035f), 0f, 4f));
                    }
                }
                else if (r < 0.8)
                {
                    // Low vent box with a grille top.
                    float w = 0.6f + (float)rnd.NextDouble() * 0.4f, d = 0.4f + (float)rnd.NextDouble() * 0.3f;
                    vents.Add(Proto3DMeshes.ChamferBox(new Vector3(x, FloorTop, z - d / 2), new Vector3(x + w, FloorTop + 0.22f, z + d / 2), 0.04f, 1f));
                }
                else
                {
                    var go = Props3D.Spawn("debris", copy, _palette.For);
                    if (go == null) continue;
                    go.transform.localPosition = new Vector3(x, FloorTop, Mathf.Clamp(z, -3.2f, -2.5f));
                    go.transform.localRotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                    go.transform.localScale = new Vector3(0.7f, 0.45f, 0.7f);
                }
            }
            if (boxes.Count > 0) MeshGo("cables", copy, Own(Proto3DMeshes.MergeTwoSub(boxes, "cables")), _darkMat, _darkMat).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            if (vents.Count > 0) Filled(MeshGo("vents", copy, Own(Proto3DMeshes.MergeTwoSub(vents, "vents")), _clutterMat, _clutterMat));
        }

        /// <summary>
        /// Sparse foreground framing on the deck between camera and play plane (blurred by DOF): short kerb stubs
        /// (0.16 tall) and the odd bollard (0.45 tall) at z -4.2..-4.8. The camera sits about 9.3 units in front of
        /// the play plane and about 1.5 above the feet, so anything at that depth lower than about 0.6 projects below
        /// the feet sight-line: the stubs edge the bottom of the frame and can never cover a character. One stub per
        /// about 14 units, 3-4 % of the frame when one is in view. Textured and lit like the deck (wall material, fills).
        /// </summary>
        /// <remarks>The previous barrier (z -11) and overhead pipe (z -9.5) sat behind the camera and never rendered.</remarks>
        private Transform BuildFramers(Transform copy, System.Random rnd)
        {
            var root = new GameObject("Framers").transform;
            root.SetParent(copy, false);
            var parts = new List<Mesh>();
            int n = Mathf.Max(1, Mathf.RoundToInt(_map.Width / 14f));
            float step = _map.Width / (float)n;
            for (int i = 0; i < n; i++)
            {
                float x = i * step + (float)rnd.NextDouble() * step * 0.5f;
                if (!HasDeck(Mathf.FloorToInt(x))) continue;
                float z = -4.2f - (float)rnd.NextDouble() * 0.6f, len = 1.6f + (float)rnd.NextDouble();
                parts.Add(Proto3DMeshes.ChamferBox(new Vector3(x, FloorTop, z - 0.35f), new Vector3(x + len, FloorTop + 0.16f, z), 0.04f, 4f));
                if (i % 3 == 1)
                    parts.Add(Proto3DMeshes.ChamferBox(new Vector3(x + len + 0.5f, FloorTop, z - 0.2f), new Vector3(x + len + 0.72f, FloorTop + 0.45f, z), 0.03f, 4f));
            }
            if (parts.Count == 0) return root;
            var go = Filled(Hidden(MeshGo("framers", root, Own(Proto3DMeshes.MergeTwoSub(parts, "framers")), _wallMat, _wallMat)));
            go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            return root;
        }

        // ------------------------------------------------------------------------------------------------
        // Build: backdrop (follows the camera)
        // ------------------------------------------------------------------------------------------------

        private void City()
        {
            _cityRows = new Transform[CityRows.Length];
            _cityPeriod = new float[CityRows.Length];
            var beaconMesh = Own(Proto3DMeshes.ChamferBox(new Vector3(-0.3f, -0.3f, -0.3f), new Vector3(0.3f, 0.3f, 0.3f), 0f, 1f));
            for (int r = 0; r < CityRows.Length; r++)
            {
                float z = CityRows[r];
                // Period wider than the view at this depth (≈ 0.95 × distance for 30° / 16:9), multiple of 8.
                float period = Mathf.Ceil((PlaneDistance + z) * 1.15f / 8f) * 8f;
                _cityPeriod[r] = period;
                var bld = new List<Mesh>();
                var glow = new QuadBatch();
                var beacons = new List<CombineInstance>();
                float x = 0f;
                while (x < period - 4f)
                {
                    float w = Mathf.Min(5f + (float)_rnd.NextDouble() * (6f + z * 0.06f), period - x);
                    float d = 6f + (float)_rnd.NextDouble() * 10f;
                    float top = -4f + (float)_rnd.NextDouble() * (12f + z * 0.18f);
                    bool tower = _rnd.NextDouble() < 0.14;
                    if (tower) top += 18f + (float)_rnd.NextDouble() * 22f;
                    float zz = z + (float)_rnd.NextDouble() * 8f;
                    bld.Add(Proto3DMeshes.ChamferBox(new Vector3(x, -80f, zz), new Vector3(x + w, top, zz + d), 0.3f, 10f));
                    int nClutter = _rnd.Next(0, 3);
                    for (int k = 0; k < nClutter; k++)
                    {
                        float bw = 0.8f + (float)_rnd.NextDouble() * 2.5f, bh = 0.6f + (float)_rnd.NextDouble() * 2f;
                        float bx = x + (float)_rnd.NextDouble() * Mathf.Max(0.1f, w - bw);
                        bld.Add(Proto3DMeshes.ChamferBox(new Vector3(bx, top, zz + 1f), new Vector3(bx + bw, top + bh, zz + 1f + bw), 0.05f, 4f));
                    }
                    // Industrial skyline silhouettes in the three nearer rows: crane, chimney stack.
                    if (r < 3)
                    {
                        double pick = _rnd.NextDouble();
                        float cx = x + w * 0.5f, cz = zz + 1f;
                        if (pick < 0.1)
                        {
                            float mh = 12f + (float)_rnd.NextDouble() * 8f, jl = 10f + (float)_rnd.NextDouble() * 6f;
                            bld.Add(Proto3DMeshes.ChamferBox(new Vector3(cx - 0.4f, top, cz), new Vector3(cx + 0.4f, top + mh, cz + 0.8f), 0f, 4f));
                            bld.Add(Proto3DMeshes.ChamferBox(new Vector3(cx - jl * 0.35f, top + mh, cz + 0.2f), new Vector3(cx + jl, top + mh + 0.6f, cz + 0.6f), 0f, 4f));
                            bld.Add(Proto3DMeshes.ChamferBox(new Vector3(cx - jl * 0.35f, top + mh - 1.2f, cz), new Vector3(cx - jl * 0.2f, top + mh, cz + 0.8f), 0f, 4f));
                            bld.Add(Proto3DMeshes.ChamferBox(new Vector3(cx + jl * 0.7f, top + mh - 5f, cz + 0.35f), new Vector3(cx + jl * 0.7f + 0.15f, top + mh, cz + 0.5f), 0f, 4f));
                        }
                        else if (pick < 0.28)
                        {
                            float ch = 6f + (float)_rnd.NextDouble() * 9f, cw = 1f + (float)_rnd.NextDouble() * 0.8f;
                            bld.Add(Proto3DMeshes.ChamferBox(new Vector3(cx - cw * 0.5f, top, cz), new Vector3(cx + cw * 0.5f, top + ch, cz + cw), 0.05f, 4f));
                        }
                    }
                    if (tower || _rnd.NextDouble() < 0.15)
                    {
                        float mh = 3f + (float)_rnd.NextDouble() * 6f, mx = x + w * 0.5f;
                        bld.Add(Proto3DMeshes.ChamferBox(new Vector3(mx - 0.12f, top, zz + d * 0.5f), new Vector3(mx + 0.12f, top + mh, zz + d * 0.5f + 0.24f), 0f, 4f));
                        var bp = new Vector3(mx, top + mh, zz + d * 0.5f);
                        float bs = 1f + z * 0.008f;
                        beacons.Add(new CombineInstance { mesh = beaconMesh, transform = Matrix4x4.TRS(bp, Quaternion.identity, Vector3.one * bs) });
                        glow.Billboard(bp + Vector3.back * 0.5f, 4f + z * 0.03f, 4f + z * 0.03f, _look.Signal * 0.7f);
                    }
                    // Street glow below (sodium bleeding up into the fog), only in the nearer rows.
                    if (z < 140f && _rnd.NextDouble() < 0.35)
                    {
                        float s = 14f + z * 0.35f;
                        glow.Billboard(new Vector3(x + w * 0.5f, -6f + (float)_rnd.NextDouble() * 6f, zz - 1f), s * 1.8f, s, _look.CityGlow * _look.CityGlowStrength);
                    }
                    x += w + (float)_rnd.NextDouble() * 3f;
                }
                var rowMesh = Own(Proto3DMeshes.MergeTwoSub(bld, "cityRow" + r));
                var glowMesh = glow.Count > 0 ? Own(glow.Build("cityGlow" + r)) : null;
                Mesh beaconRow = null;
                if (beacons.Count > 0)
                {
                    beaconRow = Own(new Mesh { name = "beacons" + r, indexFormat = IndexFormat.UInt32 });
                    beaconRow.CombineMeshes(beacons.ToArray(), true, true);
                }
                var row = new GameObject("CityRow " + z).transform;
                row.SetParent(_follow, false);
                _cityRows[r] = row;
                var facade = _facades[r % _facades.Count];
                for (int c = -1; c <= 1; c++)
                {
                    var inst = new GameObject("Strip " + c).transform;
                    inst.SetParent(row, false);
                    inst.localPosition = new Vector3(c * period, 0f, 0f);
                    // The city is not mirrored (cost): the floor reflects the near scene and the haze glow.
                    NoShadow(Hidden(MeshGo("bldg", inst, rowMesh, facade, _roofMat)));
                    if (glowMesh != null) Hidden(MeshGo("glow", inst, glowMesh, _glowMat));
                    if (beaconRow != null) NoShadow(Hidden(MeshGo("beacons", inst, beaconRow, _beaconMat)));
                }
            }
        }

        private void Haze()
        {
            // Veil cards: each veils everything behind it a little more. Tall enough for any camera height.
            (float z, float y0, float y1, float w, float a)[] veils =
            {
                (7.5f, -2f, 26f, 200f, 0.16f), (14.2f, -1f, 24f, 220f, 0.55f),
                (30f, -6f, 50f, 230f, 0.34f), (55f, -8f, 70f, 300f, 0.42f),
                (100f, -12f, 100f, 420f, 0.55f), (160f, -16f, 140f, 560f, 0.6f),
            };
            for (int i = 0; i < veils.Length; i++)
            {
                var v = veils[i];
                // Near veils carry the fog colour; far veils lift toward the bright backlit far layer (FarGlow).
                // The z 14 veil sits just in front of the mid-ground buildings: it backlights them so the girders
                // and pillars between it and the play plane read as dark silhouettes (Replaced value structure).
                var col = i == 0 ? _look.FogColor * 1.3f : _look.FarGlow * 1.15f;
                col.a = Mathf.Clamp01(v.a * _look.HazeStrength);
                var mat = Own(new Material(_hazeMat) { renderQueue = _hazeMat.renderQueue + i });
                mat.SetColor("_Color", col);
                var go = MeshGo("haze", _follow, Own(Proto3DMeshes.Quad(v.w, v.y1 - v.y0)), mat);
                go.transform.localPosition = new Vector3(0f, (v.y0 + v.y1) * 0.5f, v.z);
            }
            // Glowing mid-ground haze: additive self-lit cards (the city light trapped in the fog).
            var glow = new QuadBatch();
            foreach (var z in new[] { 14f, 24f, 38f, 60f })
            {
                float w = 80f + z * 3f;
                glow.Billboard(new Vector3(0f, FloorTop + 2f + z * 0.08f, z), w, 10f + z * 0.35f, _look.HazeTint * _look.HazeGlow);
            }
            // Backlight: a wide additive glow low behind the city, so the far layer is the brightest large area.
            glow.Billboard(new Vector3(0f, FloorTop + 10f, 120f), 520f, 70f, _look.FarGlow * 0.55f);
            glow.Billboard(new Vector3(0f, FloorTop + 5f, 70f), 320f, 30f, _look.FarGlow * 0.3f);
            MeshGo("hazeGlow", _follow, Own(glow.Build("hazeGlow")), _glowMat);
            // Far glow band, god rays along the key, drifting ground mist, near shade and dust motes (Atmosphere3D;
            // per-level strengths in Arena3DLook: FarLift, ShaftStrength, MistStrength, NearShade, DustAmount).
            Atmosphere3D.Build(_follow, _look, FloorTop, _map.Height, _owned);
        }

        private void Rain()
        {
            if (_look.Rain <= 0.01f) return;
            var streak = Own(Proto3DTextures.Streak());
            var far = Own(Proto3DMaterials.FxMat("rainFar", streak, new Color(0.45f, 0.55f, 0.7f, 1f) * 0.2f, 0, 1f));
            var near = Own(Proto3DMaterials.FxMat("rainNear", streak, new Color(0.5f, 0.6f, 0.75f, 1f) * 0.1f, 0, 1f));
            var splash = Own(Proto3DMaterials.FxMat("splash", _blob, new Color(0.6f, 0.7f, 0.85f) * 0.5f, 0, 1f));
            float a = _look.Rain;
            RainSystem("rainFar", new Vector3(0f, 22f, 14f), new Vector3(70f, 1f, 30f), (int)(2400 * a), 0.03f, far, 0.05f, 1.6f);
            RainSystem("rainNear", new Vector3(0f, 22f, -8f), new Vector3(40f, 1f, 12f), (int)(160 * a), 0.09f, near, 0.035f, 1.6f);
            var go = new GameObject("splashes") { layer = FloorReflection3D.HiddenLayer };
            go.transform.SetParent(_follow, false);
            go.transform.localPosition = new Vector3(0f, FloorTop + 0.05f, -2f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.loop = true; m.prewarm = true; m.startLifetime = 0.18f; m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f); m.maxParticles = 1500;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 800f * a;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(40f, 0.01f, 16f);
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.4f, 1f, 1.3f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = splash;
            r.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();
        }

        private void RainSystem(string name, Vector3 pos, Vector3 box, int rate, float width, Material mat, float velScale, float life)
        {
            var go = new GameObject(name) { layer = FloorReflection3D.HiddenLayer }; // rain is not mirrored (cost)
            go.transform.SetParent(_follow, false);
            go.transform.localPosition = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.loop = true; m.prewarm = true; m.startLifetime = life; m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(width * 0.7f, width * 1.3f);
            m.maxParticles = Mathf.Max(16, (int)(rate * life * 1.2f));
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(-2.5f); v.y = new ParticleSystem.MinMaxCurve(-24f); v.z = new ParticleSystem.MinMaxCurve(0f);
            var em = ps.emission; em.rateOverTime = rate;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = box;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = velScale; r.lengthScale = 1f;
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();
        }

        // No per-character lights (bible 8, Lighting): characters are lit by the key and the lamps.

        private void Probe()
        {
            var go = new GameObject("probe");
            go.transform.SetParent(_follow, false);
            go.transform.localPosition = new Vector3(0f, 6f, 0f);
            var p = go.AddComponent<ReflectionProbe>();
            p.mode = ReflectionProbeMode.Realtime;
            p.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            p.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            p.size = new Vector3(600f, 160f, 160f);
            p.resolution = 128;
            p.hdr = true;
            p.farClipPlane = 600f;
            p.RenderProbe();
        }

        // ------------------------------------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------------------------------------

        private Light NewLight(string name, Transform parent, LightType type, Color color, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var l = go.AddComponent<Light>();
            l.type = type; l.color = color; l.intensity = intensity;
            l.shadows = LightShadows.None;
            LightCount++;
            return l;
        }

        private static GameObject MeshGo(string name, Transform parent, Mesh mesh, params Material[] mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            if (mats.Length > 0 && mats[0].renderQueue >= (int)RenderQueue.Transparent) r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        private static void NoShadow(GameObject go) => go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

        private bool HasDeck(int x) => _map.IsSolid(Wrap(x), 0);

        private int Wrap(int x) => _map.WrapsX ? ((x % _map.Width) + _map.Width) % _map.Width : Mathf.Clamp(x, 0, _map.Width - 1);

        private int RingRunStart(int x, int y)
        {
            if (!_map.WrapsX) { while (x > 0 && _map.IsSolid(x - 1, y)) x--; return x; }
            for (int i = 0; i < _map.Width && _map.IsSolid(Wrap(x - 1), y); i++) x--;
            return Wrap(x);
        }

        /// <summary>
        /// Calls <paramref name="run"/>(x0, x1) for each horizontal run of solid tiles in row <paramref name="y"/>
        /// (map-local, x1 exclusive). Optional filters: only runs whose tiles have open space above / below.
        /// </summary>
        private void ForEachRun(int y, Action<int, int> run, bool onlyTopEdge = false, bool onlyBottomEdge = false)
        {
            int w = _map.Width;
            int x = 0;
            while (x < w)
            {
                if (!Accept(x, y, onlyTopEdge, onlyBottomEdge)) { x++; continue; }
                int x0 = x;
                while (x < w && Accept(x, y, onlyTopEdge, onlyBottomEdge)) x++;
                run(x0, x);
            }
        }

        private bool Accept(int x, int y, bool top, bool bottom)
        {
            if (!_map.IsSolid(x, y)) return false;
            if (top && (y + 1 >= _map.Height || _map.Get(x, y + 1) == Tile.Solid)) return false;
            if (bottom && (y - 1 < 0 || _map.Get(x, y - 1) == Tile.Solid)) return false;
            return true;
        }

        private static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
            }
        }

        /// <summary>Destroys everything built and restores the RenderSettings changed at construction.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_root != null) Object.Destroy(_root.gameObject);
            foreach (var o in _owned) if (o != null) Object.Destroy(o);
            _owned.Clear();
            _palette.Dispose();
            _savedEnv.Restore();
        }

        /// <summary>Accumulates coloured quads into one mesh (billboards facing -z and flat floor quads).</summary>
        private sealed class QuadBatch
        {
            private readonly List<Vector3> _v = new List<Vector3>();
            private readonly List<Vector3> _n = new List<Vector3>();
            private readonly List<Vector2> _u = new List<Vector2>();
            private readonly List<Color> _c = new List<Color>();
            private readonly List<int> _t = new List<int>();

            public int Count => _v.Count / 4;

            public void Billboard(Vector3 c, float w, float h, Color col)
            {
                Add(c + new Vector3(-w / 2, -h / 2, 0), c + new Vector3(-w / 2, h / 2, 0), c + new Vector3(w / 2, h / 2, 0), c + new Vector3(w / 2, -h / 2, 0),
                    Vector3.back, new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), col);
            }

            /// <summary>Flat quad on the floor centred at <paramref name="c"/>, w (x) × d (z), UV 0..1.</summary>
            public void Floor(Vector3 c, float w, float d, Color col)
            {
                Add(c + new Vector3(-w / 2, 0, -d / 2), c + new Vector3(-w / 2, 0, d / 2), c + new Vector3(w / 2, 0, d / 2), c + new Vector3(w / 2, 0, -d / 2),
                    Vector3.up, new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), col);
            }

            /// <summary>Reflection strip from <paramref name="origin"/> toward the camera (−z); UV.y 0 at origin, 1 at the near end; alpha = seed.</summary>
            public void FloorStrip(Vector3 origin, float w, float len, Color col, float seed)
            {
                col.a = seed;
                Add(origin + new Vector3(-w / 2, 0, 0), origin + new Vector3(-w / 2, 0, -len), origin + new Vector3(w / 2, 0, -len), origin + new Vector3(w / 2, 0, 0),
                    Vector3.up, new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), col);
            }

            private void Add(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Color col)
            {
                int i = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
                _u.Add(ua); _u.Add(ub); _u.Add(uc); _u.Add(ud);
                for (int k = 0; k < 4; k++) { _n.Add(n); _c.Add(col); }
                _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
                _t.Add(i); _t.Add(i + 2); _t.Add(i + 3);
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                m.SetVertices(_v); m.SetNormals(_n); m.SetUVs(0, _u); m.SetColors(_c);
                m.SetTriangles(_t, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        /// <summary>The RenderSettings this renderer overrides, captured so Dispose can put them back.</summary>
        private readonly struct SavedEnvironment
        {
            private readonly bool _fog;
            private readonly FogMode _fogMode;
            private readonly float _fogDensity;
            private readonly Color _fogColor;
            private readonly Material _skybox;
            private readonly AmbientMode _ambientMode;
            private readonly Color _ambient;
            private readonly SphericalHarmonicsL2 _probe;
            private readonly DefaultReflectionMode _reflection;
            private readonly Light _sun;

            private SavedEnvironment(bool f)
            {
                _fog = RenderSettings.fog; _fogMode = RenderSettings.fogMode; _fogDensity = RenderSettings.fogDensity;
                _fogColor = RenderSettings.fogColor; _skybox = RenderSettings.skybox; _ambientMode = RenderSettings.ambientMode;
                _ambient = RenderSettings.ambientLight; _probe = RenderSettings.ambientProbe;
                _reflection = RenderSettings.defaultReflectionMode; _sun = RenderSettings.sun;
            }

            public static SavedEnvironment Capture() => new SavedEnvironment(true);

            public void Restore()
            {
                RenderSettings.fog = _fog; RenderSettings.fogMode = _fogMode; RenderSettings.fogDensity = _fogDensity;
                RenderSettings.fogColor = _fogColor; RenderSettings.skybox = _skybox; RenderSettings.ambientMode = _ambientMode;
                RenderSettings.ambientLight = _ambient; RenderSettings.ambientProbe = _probe;
                RenderSettings.defaultReflectionMode = _reflection; RenderSettings.sun = _sun;
            }
        }
    }
}
