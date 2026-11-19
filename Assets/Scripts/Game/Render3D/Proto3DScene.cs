using System.Collections.Generic;
using Redline.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// Builds the 2.5D (3D-pixelated) prototype scene for one level in code: tile runs extruded into chamfered wet
    /// metal boxes on the play plane (z = 0, depth +z), a rooftop deck receding toward and away from the camera,
    /// a fogged 3D city (z 20..220), haze cards and light shafts, sodium lamps with shadows, rain, FBX props and
    /// big foreground occluders at negative z. Characters are placed through <see cref="IProto3DActor"/>.
    /// </summary>
    /// <remarks>Prototype-only, capture use. Tunables are the public fields of <see cref="Look"/>.</remarks>
    public sealed class Proto3DScene
    {
        /// <summary>Look tunables (fog, light, post). Values are prototype placeholders pending art-director sign-off.</summary>
        public sealed class Look
        {
            public Color FogColor = new Color(0.10f, 0.15f, 0.24f);
            public float FogDensity = 0.02f;
            public Color Sodium = new Color(1f, 0.58f, 0.24f);
            public float LampSpotIntensity = 170f;
            public float LampPointIntensity = 14f;
            /// <summary>Only lamps with x in this range cast real-time shadows (the ones in view).</summary>
            public float ShadowXMin = 20f, ShadowXMax = 48f;
            /// <summary>Strength of the fake wet-floor reflection streak under each lamp.</summary>
            public float StreakIntensity = 0.55f;
            public float LampSpacing = 9f;
            public Color Moon = new Color(0.45f, 0.58f, 0.85f);
            public float MoonIntensity = 0.35f;
            public Color Ambient = new Color(0.05f, 0.07f, 0.11f);
            public float FloorTop = 1f;
            public float DeckNear = -16f, DeckFar = 14f;
        }

        public readonly Transform Root;
        public readonly Look L;
        public readonly List<Vector3> Lamps = new List<Vector3>();
        private readonly TileMap _map;
        private readonly System.Random _rnd = new System.Random(1234);
        private readonly float _x0, _x1;
        private Material _wallMat, _deckMat, _darkMat;
        private readonly Dictionary<string, Material> _propMats = new Dictionary<string, Material>();
        private Texture2D _blob, _haze, _shaft, _streak;

        /// <summary>Builds the static scene for <paramref name="map"/> over world x range [x0, x1).</summary>
        public Proto3DScene(TileMap map, float x0, float x1, Look look)
        {
            _map = map; _x0 = x0; _x1 = x1; L = look;
            Root = new GameObject("Proto3DScene").transform;
            Textures();
            Environment();
            Level();
            City();
            LampsAndLights();
            Haze();
            Props();
            Rain();
        }

        private void Textures()
        {
            var deck = Proto3DTextures.Panels(3, new Color(0.24f, 0.26f, 0.29f), true, 2f, 1f);
            var wall = Proto3DTextures.Panels(7, new Color(0.13f, 0.14f, 0.17f), false, 1f, 0.5f);
            _deckMat = Proto3DMaterials.Lit3D("deck", Color.white, 1f, 1f, deck.Albedo, deck.Normal, 0.6f, deck.MetalGloss);
            _wallMat = Proto3DMaterials.Lit3D("wall", Color.white, 0.8f, 1f, wall.Albedo, wall.Normal, 1f, wall.MetalGloss);
            _darkMat = Proto3DMaterials.Lit3D("dark", new Color(0.05f, 0.055f, 0.065f), 0.45f, 0.6f);
            _blob = Proto3DTextures.Blob();
            _haze = Proto3DTextures.HazeBand(5);
            _shaft = Proto3DTextures.Shaft();
            _streak = Proto3DTextures.Streak();
        }

        private GameObject Mesh(string name, Mesh mesh, params Material[] mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            return go;
        }

        private void Environment()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = L.FogDensity;
            RenderSettings.fogColor = L.FogColor;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = L.Ambient;
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(L.Ambient);
            RenderSettings.ambientProbe = sh;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            // Sky backdrop: far opaque gradient, fog-exempt, glow at the horizon.
            var sky = Proto3DTextures.Sky(L.FogColor * 1.25f, new Color(0.02f, 0.03f, 0.06f), new Color(0.12f, 0.1f, 0.12f));
            var q = Mesh("sky", Proto3DMeshes.Quad(1400f, 500f), Proto3DMaterials.FxMat("sky", sky, Color.white, 2, 0f, 0f, 400));
            q.transform.position = new Vector3((_x0 + _x1) / 2f, 160f, 420f);
            q.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        private void Level()
        {
            float ft = L.FloorTop;
            // Deck: the ground row continues toward the camera and away to the roof edge.
            var deck = Mesh("deck", Proto3DMeshes.ChamferBox(new Vector3(_x0 - 30, -40, L.DeckNear), new Vector3(_x1 + 30, ft, L.DeckFar), 0.25f, 4f), _wallMat, _deckMat);
            // Parapet at the roof edge.
            Mesh("parapet", Proto3DMeshes.ChamferBox(new Vector3(_x0 - 30, ft, L.DeckFar - 0.6f), new Vector3(_x1 + 30, ft + 0.7f, L.DeckFar), 0.12f, 4f), _wallMat, _deckMat);
            // Solid tiles above the ground row: horizontal runs merged into rectangles, extruded 6 units in +z.
            int h = _map.Height;
            var used = new bool[(int)(_x1 - _x0) + 2, h];
            for (int y = 1; y < h; y++)
            for (int x = (int)_x0; x < (int)_x1; x++)
            {
                int ix = x - (int)_x0;
                if (used[ix, y] || !_map.IsSolid(x, y)) continue;
                int xe = x;
                while (xe + 1 < (int)_x1 && _map.IsSolid(xe + 1, y) && !used[xe + 1 - (int)_x0, y]) xe++;
                int ye = y;
                bool grow = true;
                while (grow && ye + 1 < h)
                {
                    for (int k = x; k <= xe; k++) if (!_map.IsSolid(k, ye + 1) || used[k - (int)_x0, ye + 1]) { grow = false; break; }
                    if (grow) ye++;
                }
                for (int yy = y; yy <= ye; yy++) for (int k = x; k <= xe; k++) used[k - (int)_x0, yy] = true;
                float depth = 5f + (float)_rnd.NextDouble() * 3f;
                var go = Mesh("tiles", Proto3DMeshes.ChamferBox(new Vector3(x, y, -0.5f), new Vector3(xe + 1, ye + 1, depth), 0.18f, 4f), _wallMat, _deckMat);
                // Underside trim light strip (cool) on floating slabs reads the edge in the dark.
                if (y > 1)
                {
                    var strip = Mesh("strip", Proto3DMeshes.Quad(xe + 1 - x - 0.2f, 0.05f),
                        Proto3DMaterials.FxMat("strip", null, new Color(0.25f, 0.9f, 1.2f), 0, 1f));
                    strip.transform.position = new Vector3((x + xe + 1) / 2f, y + 0.12f, -0.52f);
                }
            }
            // Spikes.
            var spikeMat = Proto3DMaterials.Lit3D("spike", new Color(0.12f, 0.12f, 0.13f), 0.8f, 1f, emissionColor: new Color(0.25f, 0.01f, 0.02f));
            for (int y = 1; y < h; y++)
            for (int x = (int)_x0; x < (int)_x1; x++)
                if (_map.Get(x, y) == Tile.Spike)
                    Mesh("spikes", Proto3DMeshes.Spikes(x, y, -1.5f, 2.5f, 0.55f), spikeMat);
        }

        private void City()
        {
            var facades = new List<Material>();
            for (int i = 0; i < 4; i++)
            {
                var (al, em) = Proto3DTextures.Windows(100 + i, 0.18f + i * 0.04f);
                facades.Add(Proto3DMaterials.Lit3D("facade" + i, Color.white, 0.35f, 0.2f, al, null, 1f, null, em, Color.white * 2.2f));
            }
            var roofMat = Proto3DMaterials.Lit3D("roof", new Color(0.06f, 0.065f, 0.08f), 0.5f, 0.3f);
            var beaconMat = Proto3DMaterials.Lit3D("beacon", Color.black, 0f, 0f, emissionColor: new Color(6f, 0.25f, 0.2f));
            float cx = (_x0 + _x1) / 2f;
            // Rows of buildings, wider spread with depth so they fill the perspective frustum.
            float[] rows = { 24f, 40f, 62f, 90f, 130f, 180f };
            foreach (var z in rows)
            {
                float half = 30f + z * 0.75f;
                float x = cx - half;
                while (x < cx + half)
                {
                    float w = 5f + (float)_rnd.NextDouble() * (6f + z * 0.06f);
                    float d = 6f + (float)_rnd.NextDouble() * 10f;
                    float top = -6f + (float)_rnd.NextDouble() * (10f + z * 0.18f);
                    bool tower = _rnd.NextDouble() < 0.12;
                    if (tower) top += 18f + (float)_rnd.NextDouble() * 20f;
                    float zz = z + (float)_rnd.NextDouble() * 8f;
                    var go = Mesh("bldg", Proto3DMeshes.ChamferBox(new Vector3(x, -80f, zz), new Vector3(x + w, top, zz + d), 0.3f, 10f),
                        facades[_rnd.Next(facades.Count)], roofMat);
                    go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                    // Rooftop clutter.
                    int n = _rnd.Next(0, 4);
                    for (int k = 0; k < n; k++)
                    {
                        float bw = 0.8f + (float)_rnd.NextDouble() * 2.5f, bh = 0.6f + (float)_rnd.NextDouble() * 2f;
                        float bx = x + (float)_rnd.NextDouble() * (w - bw);
                        var c = Mesh("clutter", Proto3DMeshes.ChamferBox(new Vector3(bx, top, zz + 1f), new Vector3(bx + bw, top + bh, zz + 1f + bw), 0.05f, 4f), roofMat, roofMat);
                        c.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                    }
                    if (tower || _rnd.NextDouble() < 0.15)
                    {
                        float mh = 3f + (float)_rnd.NextDouble() * 6f;
                        float mx = x + w * 0.5f;
                        Mesh("mast", Proto3DMeshes.ChamferBox(new Vector3(mx - 0.12f, top, zz + d * 0.5f), new Vector3(mx + 0.12f, top + mh, zz + d * 0.5f + 0.24f), 0f, 4f), roofMat, roofMat);
                        var b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        Object.Destroy(b.GetComponent<Collider>());
                        b.transform.SetParent(Root, false);
                        b.transform.position = new Vector3(mx, top + mh, zz + d * 0.5f);
                        b.transform.localScale = Vector3.one * (0.5f + z * 0.004f);
                        b.GetComponent<Renderer>().sharedMaterial = beaconMat;
                        var glow = Mesh("beaconGlow", Proto3DMeshes.Quad(4f + z * 0.03f, 4f + z * 0.03f), Proto3DMaterials.FxMat("bglow", _blob, new Color(1.2f, 0.08f, 0.06f), 0, 0.6f));
                        glow.transform.position = b.transform.position + Vector3.back * 0.5f;
                        if (z < 70f) Point(b.transform.position + Vector3.back, new Color(1f, 0.1f, 0.08f), 25f, 30f, false);
                    }
                    x += w + (float)_rnd.NextDouble() * 3f;
                }
            }
        }

        private Light Point(Vector3 pos, Color c, float intensity, float range, bool shadows)
        {
            var go = new GameObject("point");
            go.transform.SetParent(Root, false);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c; l.intensity = intensity; l.range = range;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            return l;
        }

        private void LampsAndLights()
        {
            var moon = new GameObject("moon").AddComponent<Light>();
            moon.transform.SetParent(Root, false);
            moon.type = LightType.Directional;
            moon.color = L.Moon; moon.intensity = L.MoonIntensity;
            moon.transform.rotation = Quaternion.Euler(52f, -30f, 0f);
            moon.shadows = LightShadows.Soft;
            moon.shadowStrength = 0.8f;

            var postMat = Proto3DMaterials.Lit3D("lamppost", new Color(0.07f, 0.075f, 0.085f), 0.6f, 0.9f);
            var headMat = Proto3DMaterials.Lit3D("lamphead", Color.black, 0f, 0f, emissionColor: L.Sodium * 9f);
            var glowMat = Proto3DMaterials.FxMat("lampglow", _blob, L.Sodium * 1.4f, 0, 0.8f);
            var shaftMat = Proto3DMaterials.FxMat("shaft", _shaft, L.Sodium * 0.22f, 0, 0.8f, 1.6f);
            var cone = Proto3DMeshes.Cone(2.3f, 5.6f);
            var streakMat = Proto3DMaterials.FxMat("wetStreak", _blob, L.Sodium * L.StreakIntensity, 0, 1f, 0f, -30);
            var poolMat = Proto3DMaterials.FxMat("wetPool", _blob, L.Sodium * 0.2f, 0, 1f, 0f, -31);
            float ft = L.FloorTop;
            for (float x = _x0 + 2f; x < _x1; x += L.LampSpacing)
            {
                // Skip posts that would stand inside a solid run at floor height.
                if (_map.IsSolid(Mathf.FloorToInt(x), 1) || _map.Get(Mathf.FloorToInt(x), 1) == Tile.Spike) x += 2f;
                float z = 1.6f, top = ft + 5.4f;
                Mesh("post", Proto3DMeshes.ChamferBox(new Vector3(x - 0.09f, ft, z - 0.09f), new Vector3(x + 0.09f, top, z + 0.09f), 0f, 4f), postMat, postMat);
                Mesh("arm", Proto3DMeshes.ChamferBox(new Vector3(x - 0.09f, top - 0.12f, z - 2.2f), new Vector3(x + 0.09f, top, z), 0f, 4f), postMat, postMat);
                var head = Mesh("head", Proto3DMeshes.ChamferBox(new Vector3(x - 0.35f, top - 0.28f, z - 2.45f), new Vector3(x + 0.35f, top - 0.12f, z - 1.95f), 0.04f, 4f), postMat, headMat);
                var lp = new Vector3(x, top - 0.35f, z - 2.2f); // in front of the sprite plane so camera-facing quads are lit
                Lamps.Add(lp);
                var glow = Mesh("glow", Proto3DMeshes.Quad(2.2f, 2.2f), glowMat);
                glow.transform.position = lp + Vector3.back * 0.3f;
                var shaft = Mesh("shaft", cone, shaftMat);
                shaft.transform.position = lp;
                var sp = new GameObject("lampSpot").AddComponent<Light>();
                sp.transform.SetParent(Root, false);
                sp.transform.position = lp;
                sp.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                sp.type = LightType.Spot;
                sp.spotAngle = 95f; sp.innerSpotAngle = 35f;
                sp.range = 14f; sp.color = L.Sodium; sp.intensity = L.LampSpotIntensity;
                bool shadowed = x >= L.ShadowXMin && x <= L.ShadowXMax;
                sp.shadows = shadowed ? LightShadows.Soft : LightShadows.None; sp.shadowStrength = 0.95f;
                // Fake wet reflection: a long soft additive streak lying on the deck, from under the lamp toward
                // the camera (the real mirror highlight falls off-screen from this high camera).
                var streak = Mesh("wetStreak", Proto3DMeshes.Quad(1.1f, 9f), streakMat);
                streak.transform.position = new Vector3(x, ft + 0.02f, lp.z - 4.2f);
                streak.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                var pool = Mesh("wetPool", Proto3DMeshes.Quad(5f, 4f), poolMat);
                pool.transform.position = new Vector3(x, ft + 0.015f, lp.z - 0.6f);
                pool.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                Point(lp + new Vector3(0, -0.4f, -0.4f), L.Sodium, L.LampPointIntensity, 10f, false);
            }
        }

        private void Haze()
        {
            float cx = (_x0 + _x1) / 2f;
            // Big haze cards at increasing depth: each one veils everything behind it a little more.
            (float z, float y, float w, float h, float a, Color tint)[] cards =
            {
                (16f, -2f, 160f, 14f, 0.35f, new Color(0.55f, 0.7f, 0.95f)),
                (30f, -4f, 220f, 26f, 0.45f, new Color(0.5f, 0.65f, 0.95f)),
                (55f, -6f, 300f, 40f, 0.5f, new Color(0.55f, 0.6f, 0.85f)),
                (100f, -10f, 420f, 70f, 0.55f, new Color(0.6f, 0.6f, 0.8f)),
                (160f, -14f, 560f, 110f, 0.6f, new Color(0.6f, 0.58f, 0.75f)),
            };
            int i = 0;
            foreach (var c in cards)
            {
                var col = L.FogColor * 2.2f * c.tint;
                col.a = c.a;
                var go = Mesh("haze", Proto3DMeshes.Quad(c.w, c.h), Proto3DMaterials.FxMat("haze", _haze, col, 1, 0.3f, 0f, -20 + i++));
                go.transform.position = new Vector3(cx, c.y + c.h / 2f, c.z);
            }
            // Warm city glow puffs low in the distance (sodium-lit streets below).
            for (int k = 0; k < 14; k++)
            {
                float z = 40f + (float)_rnd.NextDouble() * 140f;
                float s = 30f + z * 0.4f;
                var go = Mesh("cityglow", Proto3DMeshes.Quad(s * 1.8f, s), Proto3DMaterials.FxMat("cityglow", _blob, new Color(0.5f, 0.28f, 0.14f) * 0.35f, 0, 0.5f));
                go.transform.position = new Vector3(cx + ((float)_rnd.NextDouble() - 0.5f) * z * 1.6f, -8f + (float)_rnd.NextDouble() * 6f, z);
            }
            // Ground mist hugging the deck behind the play plane.
            var mist = new Color(0.45f, 0.55f, 0.75f, 0.18f);
            foreach (var z in new[] { 3f, 9f })
            {
                var go = Mesh("mist", Proto3DMeshes.Quad(_x1 - _x0 + 60f, 4f), Proto3DMaterials.FxMat("mist", _haze, mist, 1, 0.6f, 0f, -5));
                go.transform.position = new Vector3(cx, L.FloorTop + 1.6f, z);
            }
        }

        private Material PropMat(string name)
        {
            name = name.Replace(" (Instance)", "").Trim();
            int dot = name.IndexOf('.');
            if (dot > 0) name = name.Substring(0, dot);
            if (_propMats.TryGetValue(name, out var m)) return m;
            Color c; float sm = 0.55f, me = 0.6f; Color em = Color.black;
            switch (name)
            {
                case "metal": c = new Color(0.2f, 0.23f, 0.27f); break;
                case "gunmetal": c = new Color(0.14f, 0.15f, 0.17f); break;
                case "steel": c = new Color(0.32f, 0.34f, 0.36f); sm = 0.7f; break;
                case "dark": c = new Color(0.05f, 0.055f, 0.065f); break;
                case "rust": c = new Color(0.25f, 0.11f, 0.06f); sm = 0.3f; me = 0.3f; break;
                case "wood": c = new Color(0.17f, 0.11f, 0.07f); sm = 0.3f; me = 0f; break;
                case "concrete": c = new Color(0.2f, 0.2f, 0.21f); sm = 0.4f; me = 0f; break;
                case "teal": c = new Color(0.06f, 0.2f, 0.22f); break;
                case "hazard": c = new Color(0.45f, 0.33f, 0.05f); sm = 0.45f; me = 0.2f; break;
                case "paintred": c = new Color(0.35f, 0.05f, 0.05f); me = 0.2f; break;
                case "wire": case "cable": c = new Color(0.04f, 0.04f, 0.045f); sm = 0.4f; break;
                case "red": c = Color.black; em = new Color(5f, 0.2f, 0.15f); break;
                case "warm": c = Color.black; em = L.Sodium * 5f; break;
                case "neon": c = Color.black; em = new Color(0.5f, 2.5f, 4f); break;
                case "panel": c = Color.black; em = new Color(0.4f, 0.8f, 1.6f); break;
                default: c = new Color(0.12f, 0.13f, 0.15f); break;
            }
            m = Proto3DMaterials.Lit3D("prop_" + name, c, sm, me, emissionColor: em);
            _propMats[name] = m;
            return m;
        }

        /// <summary>Instantiates an FBX prop (Resources/Props3D) at a world position.</summary>
        public GameObject Prop(string name, Vector3 pos, float yaw = 0f, float scale = 1f, Material overrideMat = null)
        {
            var go = Props3D.Spawn(name, Root, PropMat, overrideMat);
            if (go == null) return null;
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;
            return go;
        }

        private void Props()
        {
            float ft = L.FloorTop;
            string[] back = { "water_tank", "ac_unit", "container", "stair_hut", "fan_tower", "vent_stack", "antenna_mast", "exhaust_elbow", "sat_dish", "billboard", "crates", "barrels", "neon_sign", "cable_posts" };
            int k = 0;
            for (float x = _x0 + 1f; x < _x1; x += 4.5f + (float)_rnd.NextDouble() * 3f)
            {
                float z = 5.5f + (float)_rnd.NextDouble() * 6.5f;
                Prop(back[k++ % back.Length], new Vector3(x, ft, z), -15f + (float)_rnd.NextDouble() * 30f, 1.3f);
            }
            // Fence and railing along the parapet.
            for (float x = _x0 - 10f; x < _x1 + 10f; x += 3.2f)
                Prop(k++ % 3 == 0 ? "fence" : "railing", new Vector3(x, ft + 0.7f, L.DeckFar - 0.3f), 0f, 1.3f);
            // Props close in front of the play plane on the deck (soft, mid-near).
            for (float x = _x0 + 3f; x < _x1; x += 9f + (float)_rnd.NextDouble() * 5f)
                Prop(_rnd.NextDouble() < 0.5 ? "debris" : "barrels", new Vector3(x, ft, -3.5f - (float)_rnd.NextDouble() * 2f), (float)_rnd.NextDouble() * 360f, 1.2f);
        }

        /// <summary>
        /// Big dark foreground occluders between the camera and the play plane (blurred by DOF): a concrete pillar,
        /// a railing and an overhead pipe, placed around <paramref name="x"/> at depth <paramref name="z"/>.
        /// </summary>
        public void Foreground(float x, float z, float pipeY)
        {
            float ft = L.FloorTop;
            var fgMat = Proto3DMaterials.Lit3D("fg", new Color(0.035f, 0.04f, 0.05f), 0.5f, 0.4f);
            Mesh("fgPillar", Proto3DMeshes.ChamferBox(new Vector3(x, -10f, z - 1.2f), new Vector3(x + 1.6f, 40f, z), 0.15f, 4f), fgMat, fgMat);
            var pipe = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(pipe.GetComponent<Collider>());
            pipe.transform.SetParent(Root, false);
            pipe.transform.position = new Vector3(x + 15f, pipeY, z + 0.5f);
            pipe.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            pipe.transform.localScale = new Vector3(0.7f, 40f, 0.7f);
            pipe.GetComponent<Renderer>().sharedMaterial = fgMat;
            Prop("railing", new Vector3(x + 6f, ft, z + 1.5f), 0f, 1.6f, fgMat);
            Prop("railing", new Vector3(x + 10.2f, ft, z + 1.5f), 0f, 1.6f, fgMat);
        }

        private void Rain()
        {
            float cx = (_x0 + _x1) / 2f;
            var far = Proto3DMaterials.FxMat("rainFar", _streak, new Color(0.45f, 0.55f, 0.7f, 1f) * 0.2f, 0, 1f);
            var near = Proto3DMaterials.FxMat("rainNear", _streak, new Color(0.5f, 0.6f, 0.75f, 1f) * 0.1f, 0, 1f);
            var splash = Proto3DMaterials.FxMat("splash", _blob, new Color(0.6f, 0.7f, 0.85f) * 0.5f, 0, 1f);
            RainSystem("rainFar", new Vector3(cx, 14f, 14f), new Vector3(90f, 1f, 34f), 3200, 0.03f, far, 0.05f);
            RainSystem("rainNear", new Vector3(cx, 14f, -8f), new Vector3(70f, 1f, 14f), 220, 0.09f, near, 0.035f);
            // Splashes: short-lived flat-ish puffs on the deck.
            var go = new GameObject("splashes");
            go.transform.SetParent(Root, false);
            go.transform.position = new Vector3(cx, L.FloorTop + 0.05f, -2f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.loop = true; m.prewarm = true; m.startLifetime = 0.18f; m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f); m.maxParticles = 3000;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 1200f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(70f, 0.01f, 22f);
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.4f, 1f, 1.3f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = splash;
            r.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();
        }

        private void RainSystem(string name, Vector3 pos, Vector3 box, int rate, float width, Material mat, float velScale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root, false);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.loop = true; m.prewarm = true; m.startLifetime = 1.1f; m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(width * 0.7f, width * 1.3f);
            m.maxParticles = rate * 2;
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

        /// <summary>Realtime reflection probe over the play area, rendered once (wet-floor reflections of lamps/city).</summary>
        public void ReflectionProbe(Vector3 center)
        {
            var go = new GameObject("probe");
            go.transform.SetParent(Root, false);
            go.transform.position = center;
            var p = go.AddComponent<UnityEngine.ReflectionProbe>();
            p.mode = ReflectionProbeMode.Realtime;
            p.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            p.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            p.size = new Vector3(400f, 120f, 120f);
            p.resolution = 128;
            p.hdr = true;
            p.farClipPlane = 600f;
            p.RenderProbe();
        }
    }
}
