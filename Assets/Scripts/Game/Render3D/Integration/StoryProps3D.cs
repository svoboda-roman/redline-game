using System;
using System.Collections.Generic;
using Redline.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Redline.Game.Render3D.Integration
{
    /// <summary>
    /// The moving and switching pieces of a story map (<see cref="StoryLayout"/>), drawn in 3D next to the static arena
    /// (which leaves gate and bridge cells out): iron portcullis gates that slide up when opened, wooden bridge planks that
    /// shake and drop away, the lift platform on its chains, the lever, stone-lantern checkpoints that light up when
    /// reached, and fire traps that burst from the walls while live. <see cref="Sync"/> follows the sim each frame.
    /// </summary>
    /// <example><code>
    /// var props = new StoryProps3D(arenaHost, sim.Map);   // after the arena is built
    /// props.Sync(sim, Time.time);                          // every frame
    /// props.Dispose();                                     // with the arena
    /// </code></example>
    public sealed class StoryProps3D : IDisposable
    {
        private readonly Transform _root;
        private readonly List<Object> _owned = new List<Object>();
        private readonly StoryLayout _s;
        private readonly Transform[] _gates;
        private readonly float[] _gateLift;
        private readonly Transform[] _planks;
        private readonly Vector3[] _plankHome;
        private readonly Transform[] _lifts;
        private readonly Transform[] _leverHandles;
        private readonly Renderer[] _cpHeads;
        private readonly Light[] _cpLights;
        private readonly Transform[] _flames;
        private readonly MeshRenderer[] _flameRenderers;
        private readonly MaterialPropertyBlock _flameBlock = new MaterialPropertyBlock();
        private static readonly int MainTexSt = Shader.PropertyToID("_MainTex_ST");
        private readonly Light[] _fireLights;
        private readonly Material _cpLit, _cpDark;

        public StoryProps3D(Transform parent, TileMap map)
        {
            _s = map.Story ?? throw new ArgumentException("not a story map", nameof(map));
            _root = new GameObject("StoryProps").transform;
            _root.SetParent(parent, false);

            var iron = Own(Proto3DMaterials.Lit3D("storyIron", new Color(0.12f, 0.12f, 0.13f), 0.35f, 0.7f));
            var wood = Own(Proto3DMaterials.Lit3D("storyWood", new Color(0.32f, 0.22f, 0.13f), 0.15f, 0f));
            var stone = Own(Proto3DMaterials.Lit3D("storyStone", new Color(0.42f, 0.41f, 0.39f), 0.1f, 0f));
            _cpLit = Own(Proto3DMaterials.Lit3D("storyLanternLit", Color.black, 0f, 0f, emissionColor: new Color(4f, 2.2f, 0.7f)));
            _cpDark = Own(Proto3DMaterials.Lit3D("storyLanternDark", new Color(0.2f, 0.19f, 0.18f), 0.1f, 0f));

            // Gates: a portcullis per gate (vertical bars + cross bars over its cells).
            _gates = new Transform[_s.Gates.Count];
            _gateLift = new float[_s.Gates.Count];
            for (int g = 0; g < _s.Gates.Count; g++)
            {
                var parts = new List<Mesh>();
                foreach (var c in _s.Gates[g])
                {
                    for (int k = 0; k < 3; k++)
                    {
                        float bx = c.x + 0.17f + k * 0.33f;
                        parts.Add(Proto3DMeshes.ChamferBox(new Vector3(bx - 0.06f, c.y, -0.5f), new Vector3(bx + 0.06f, c.y + 1f, 0.5f), 0.02f, 1f));
                    }
                    parts.Add(Proto3DMeshes.ChamferBox(new Vector3(c.x, c.y + 0.42f, -0.55f), new Vector3(c.x + 1f, c.y + 0.58f, 0.55f), 0.02f, 1f));
                }
                _gates[g] = Go("gate" + g, Own(Proto3DMeshes.MergeTwoSub(parts, "gate" + g)), iron).transform;
            }

            // Bridge: one plank per cell (they drop away one by one).
            _planks = new Transform[_s.BridgeCells.Count];
            _plankHome = new Vector3[_s.BridgeCells.Count];
            var plank = Own(Proto3DMeshes.ChamferBox(new Vector3(-0.48f, -0.22f, -0.7f), new Vector3(0.48f, 0f, 0.7f), 0.03f, 1f));
            for (int i = 0; i < _s.BridgeCells.Count; i++)
            {
                var c = _s.BridgeCells[i];
                var go = Go("plank" + i, plank, wood);
                _plankHome[i] = new Vector3(c.x + 0.5f, c.y + 1f, 0f);
                go.transform.localPosition = _plankHome[i];
                _planks[i] = go.transform;
            }

            // Lifts: a heavy wooden platform with chains running up to the top of its track.
            _lifts = new Transform[_s.Lifts.Count];
            for (int i = 0; i < _s.Lifts.Count; i++)
            {
                var t = _s.Lifts[i];
                float w = t.MaxX - t.MinX;
                var parts = new List<Mesh>
                {
                    Proto3DMeshes.ChamferBox(new Vector3(0f, -0.4f, -0.9f), new Vector3(w, 0f, 0.9f), 0.04f, 1f),
                };
                var slab = Go("lift" + i, Own(Proto3DMeshes.MergeTwoSub(parts, "lift" + i)), wood);
                _lifts[i] = slab.transform;
                var chains = new List<Mesh>();
                foreach (float cx in new[] { t.MinX + 0.25f, t.MaxX - 0.25f })
                    for (float y = t.Bottom; y < t.Top + 3f; y += 0.5f)
                        chains.Add(Proto3DMeshes.ChamferBox(new Vector3(cx - 0.05f, y, -0.05f), new Vector3(cx + 0.05f, y + 0.35f, 0.05f), 0.01f, 1f));
                Go("liftChains" + i, Own(Proto3DMeshes.MergeTwoSub(chains, "liftChains" + i)), iron);
            }

            // Levers: a stone base and an iron handle that swings over when pulled.
            _leverHandles = new Transform[_s.Levers.Count];
            var leverBase = Own(Proto3DMeshes.ChamferBox(new Vector3(-0.35f, 0f, -0.35f), new Vector3(0.35f, 0.3f, 0.35f), 0.04f, 1f));
            var leverStick = Own(Proto3DMeshes.ChamferBox(new Vector3(-0.05f, 0f, -0.05f), new Vector3(0.05f, 1f, 0.05f), 0.01f, 1f));
            for (int i = 0; i < _s.Levers.Count; i++)
            {
                var l = _s.Levers[i];
                var b = Go("leverBase" + i, leverBase, stone);
                b.transform.localPosition = new Vector3(l.X, l.Y, 0.3f);
                var h = Go("leverHandle" + i, leverStick, iron);
                h.transform.localPosition = new Vector3(l.X, l.Y + 0.25f, 0.3f);
                h.transform.localRotation = Quaternion.Euler(0f, 0f, 40f);
                _leverHandles[i] = h.transform;
            }

            // Checkpoints: stone lanterns (toro) that light up once reached.
            _cpHeads = new Renderer[_s.Checkpoints.Count];
            _cpLights = new Light[_s.Checkpoints.Count];
            for (int i = 0; i < _s.Checkpoints.Count; i++)
            {
                var p = _s.Checkpoints[i];
                var body = new List<Mesh>
                {
                    Proto3DMeshes.ChamferBox(new Vector3(-0.4f, 0f, -0.4f), new Vector3(0.4f, 0.2f, 0.4f), 0.03f, 1f),
                    Proto3DMeshes.ChamferBox(new Vector3(-0.12f, 0.2f, -0.12f), new Vector3(0.12f, 0.9f, 0.12f), 0.02f, 1f),
                    Proto3DMeshes.ChamferBox(new Vector3(-0.5f, 1.3f, -0.5f), new Vector3(0.5f, 1.45f, 0.5f), 0.03f, 1f),
                    Proto3DMeshes.ChamferBox(new Vector3(-0.1f, 1.45f, -0.1f), new Vector3(0.1f, 1.65f, 0.1f), 0.02f, 1f),
                };
                var go = Go("checkpoint" + i, Own(Proto3DMeshes.MergeTwoSub(body, "checkpoint" + i)), stone);
                go.transform.localPosition = new Vector3(p.X, p.Y, 0.6f);
                var head = Go("checkpointLight" + i, Own(Proto3DMeshes.ChamferBox(new Vector3(-0.28f, 0.9f, -0.28f), new Vector3(0.28f, 1.3f, 0.28f), 0.02f, 1f)), _cpDark);
                head.transform.SetParent(go.transform, false);
                _cpHeads[i] = head.GetComponent<Renderer>();
                var light = new GameObject("checkpointGlow" + i).AddComponent<Light>();
                light.transform.SetParent(go.transform, false);
                light.transform.localPosition = new Vector3(0f, 1.1f, -0.8f);
                light.type = LightType.Point;
                light.color = new Color(1f, 0.7f, 0.35f);
                light.range = 6f;
                light.intensity = 0f;
                _cpLights[i] = light;
            }

            // Fire traps: flame tongues on every fire cell, a light per trap run.
            // Alpha-blended painted flame tongues (an emissive glow alone vanishes on the white ink-paper world), each
            // licking out of its wall cell into the open air beside it, pivoted at the wall.
            _flames = new Transform[_s.FireCells.Count];
            _flameRenderers = new MeshRenderer[_s.FireCells.Count];
            var fireMat = Own(Proto3DMaterials.FxMat("storyFire", Own(FlameTexture()), Color.white, 1, 0f, 0f, 5));
            var tongue = Own(FlameQuad(0.75f, 1.35f));
            for (int i = 0; i < _s.FireCells.Count; i++)
            {
                var c = _s.FireCells[i];
                var go = new GameObject("flame" + i);
                go.transform.SetParent(_root, false);
                go.AddComponent<MeshFilter>().sharedMesh = tongue;
                var fr = go.AddComponent<MeshRenderer>();
                fr.sharedMaterial = fireMat;
                _flameRenderers[i] = fr;
                int side = !map.IsSolid(c.x + 1, c.y) ? 1 : !map.IsSolid(c.x - 1, c.y) ? -1 : 0;
                int up = side == 0 && !map.IsSolid(c.x, c.y - 1) ? -1 : 1;
                // Base on the wall face, tip pointing into the open cell (sideways for wall traps, else up/down).
                go.transform.localPosition = side != 0
                    ? new Vector3(c.x + 0.5f + side * 0.5f, c.y + 0.5f, -0.3f)
                    : new Vector3(c.x + 0.5f, c.y + 0.5f + up * 0.5f, -0.3f);
                go.transform.localRotation = Quaternion.Euler(0f, 0f, side != 0 ? -90f * side : up < 0 ? 180f : 0f);
                _flames[i] = go.transform;
            }
            var lights = new List<Light>();
            for (int i = 0; i < _s.FireCells.Count; i += 4)
            {
                var c = _s.FireCells[i];
                var l = new GameObject("fireLight" + i).AddComponent<Light>();
                l.transform.SetParent(_root, false);
                int ls = !map.IsSolid(c.x + 1, c.y) ? 1 : !map.IsSolid(c.x - 1, c.y) ? -1 : 0;
                l.transform.localPosition = new Vector3(c.x + 0.5f + ls * 1.2f, c.y + 0.5f, -1.2f);
                l.type = LightType.Point;
                l.color = new Color(1f, 0.55f, 0.2f);
                l.range = 5f;
                lights.Add(l);
            }
            _fireLights = lights.ToArray();
        }

        /// <summary>Follows the sim: gates open, planks shake and fall, lifts move, lever, checkpoint lights, fire.</summary>
        public void Sync(RedlineSim sim, float time)
        {
            if (sim == null) return;
            float dt = Time.deltaTime;
            for (int g = 0; g < _gates.Length; g++)
            {
                float target = sim.GateOpen(g) ? 1f : 0f;
                _gateLift[g] = Mathf.MoveTowards(_gateLift[g], target, dt * 1.5f);
                _gates[g].localPosition = new Vector3(0f, _gateLift[g] * 2.2f, 0f);
                bool show = _gateLift[g] < 0.999f;
                if (_gates[g].gameObject.activeSelf != show) _gates[g].gameObject.SetActive(show);
            }
            for (int i = 0; i < _planks.Length; i++)
            {
                bool gone = sim.BridgeGone(i);
                if (_planks[i].gameObject.activeSelf == gone) _planks[i].gameObject.SetActive(!gone);
                if (gone) continue;
                float shake = sim.BridgeShake(i);
                var off = shake > 0f ? new Vector3(Mathf.Sin(time * 70f + i) * 0.05f, -shake * 0.12f, 0f) : Vector3.zero;
                _planks[i].localPosition = _plankHome[i] + off;
                _planks[i].localRotation = Quaternion.Euler(0f, 0f, shake * 9f * Mathf.Sin(time * 40f + i * 1.3f));
            }
            for (int i = 0; i < _lifts.Length; i++)
                _lifts[i].localPosition = new Vector3(_s.Lifts[i].MinX, sim.LiftTop(i), 0f);
            for (int i = 0; i < _leverHandles.Length; i++)
                _leverHandles[i].localRotation = Quaternion.RotateTowards(_leverHandles[i].localRotation,
                    Quaternion.Euler(0f, 0f, sim.LeverPulledAt(i) ? -40f : 40f), dt * 240f);
            for (int i = 0; i < _cpHeads.Length; i++)
            {
                bool lit = i <= sim.StoryCheckpoint;
                var m = lit ? _cpLit : _cpDark;
                if (_cpHeads[i].sharedMaterial != m) _cpHeads[i].sharedMaterials = new[] { m, m };
                _cpLights[i].intensity = lit ? 3f + 0.4f * Mathf.Sin(time * 9f + i) : 0f;
            }
            bool live = sim.FireLive;
            for (int i = 0; i < _flames.Length; i++)
            {
                if (_flames[i].gameObject.activeSelf != live) _flames[i].gameObject.SetActive(live);
                if (!live) continue;
                // Flip-book at 10 fps, each tongue out of step with its neighbours; a small stretch on top.
                int frame = (int)(time * 10f + i * 1.37f) % FlameFrames;
                _flameBlock.SetVector(MainTexSt, new Vector4(1f / FlameFrames, 1f, frame / (float)FlameFrames, 0f));
                _flameRenderers[i].SetPropertyBlock(_flameBlock);
                _flames[i].localScale = new Vector3(1f, 0.9f + 0.15f * Mathf.Abs(Mathf.Sin(time * 6f + i * 1.7f)), 1f);
            }
            for (int i = 0; i < _fireLights.Length; i++)
                _fireLights[i].intensity = live ? 4f + 1.5f * Mathf.Sin(time * 17f + i * 2f) : 0f;
        }

        /// <summary>A flame tongue quad <paramref name="w"/> wide, its base at the pivot, reaching <paramref name="h"/> up (+Y).</summary>
        private static Mesh FlameQuad(float w, float h)
        {
            var m = new Mesh { name = "flameQuad" };
            m.SetVertices(new List<Vector3> { new Vector3(-w / 2, 0f, 0f), new Vector3(-w / 2, h, 0f), new Vector3(w / 2, h, 0f), new Vector3(w / 2, 0f, 0f) });
            m.SetNormals(new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) });
            m.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Frames in the fire flip-book (side by side in <see cref="FlameTexture"/>).</summary>
        private const int FlameFrames = 4;

        /// <summary>
        /// Retro PSX-style pixel-art fire: a 4-frame flip-book of 16 x 32 frames, hard pixels (point filtering), a
        /// five-colour palette (pale yellow core, yellow, orange, red, dark crimson rim) with a 4 x 4 ordered dither
        /// between the bands, and a few loose embers above the tip. Each frame's tongue sways and licks differently.
        /// </summary>
        private static Texture2D FlameTexture()
        {
            const int FW = 16, FH = 32;
            int W = FW * FlameFrames;
            var t = new Texture2D(W, FH, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, name = "storyFlamePixel" };
            var px = new Color32[W * FH];
            Color32[] pal =
            {
                new Color32(255, 244, 176, 255),   // core
                new Color32(252, 200, 64, 255),    // yellow
                new Color32(236, 112, 28, 255),    // orange
                new Color32(176, 34, 16, 255),     // red
                new Color32(92, 14, 12, 255),      // rim
            };
            int[] bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            for (int f = 0; f < FlameFrames; f++)
            {
                float ph = f * 1.57f;
                for (int y = 0; y < FH; y++)
                for (int x = 0; x < FW; x++)
                {
                    float v = (y + 0.5f) / FH, u = (x + 0.5f) / FW * 2f - 1f;
                    // Tongue: wide at the base, swaying and splitting into licks toward the tip (different per frame).
                    float half = 0.92f * Mathf.Pow(1f - v, 0.7f) * (0.8f + 0.2f * Mathf.Sin(v * 11f + ph * 1.3f));
                    float off = 0.22f * Mathf.Sin(v * 6f + ph) * v;
                    float d = Mathf.Abs(u - off) / Mathf.Max(1e-3f, half);
                    float lick = v > 0.55f ? 0.25f * Mathf.Sin(u * 9f + ph * 2f) : 0f;   // ragged top
                    d += lick;
                    int i = y * W + f * FW + x;
                    if (d > 1f)
                    {
                        // Loose embers above the flame.
                        bool ember = v > 0.6f && ((x * 7 + y * 13 + f * 5) % 37 == 0);
                        px[i] = ember ? pal[1] : new Color32(0, 0, 0, 0);
                        continue;
                    }
                    // Heat: hottest low in the middle; quantised to the palette with an ordered dither.
                    float heat = Mathf.Clamp01((1f - d) * 1.1f - v * 0.55f + 0.25f);
                    float level = (1f - heat) * (pal.Length - 1);
                    float dither = (bayer[(y & 3) * 4 + (x & 3)] + 0.5f) / 16f;
                    int band = Mathf.Clamp(Mathf.FloorToInt(level + dither - 0.5f + 0.5f), 0, pal.Length - 1);
                    px[i] = d > 0.86f ? pal[4] : pal[band];   // dark outline at the edge, like the game's pixel sprites
                }
            }
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        public void Dispose()
        {
            if (_root != null) Object.Destroy(_root.gameObject);
            foreach (var o in _owned) if (o != null) Object.Destroy(o);
            _owned.Clear();
        }

        private T Own<T>(T o) where T : Object { _owned.Add(o); return o; }

        private GameObject Go(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = new[] { mat, mat };   // ChamferBox meshes: sides + tops submeshes
            return go;
        }
    }
}
