using System.Collections.Generic;
using Redline.Core;
using UnityEngine;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// Spike tiles drawn as electrified puddles (<see cref="Arena3DLook.MetalPlatforms"/> levels): a dark mirror-smooth
    /// puddle over each run of spike tiles, a leaning broken utility pole behind it whose downed power cable hangs into
    /// the water with a frayed copper end, crackling arcs and sparks at the end, a cyan glow on the water, and a
    /// flickering cyan point light. The hazard itself (damage on contact) is unchanged: this is presentation only.
    /// </summary>
    public sealed partial class Arena3DRenderer
    {
        private Material _hazardWaterMat, _hazardCableMat, _hazardCopperMat, _hazardPoleMat, _hazardGlowMat, _hazardArcMatA, _hazardArcMatB;
        private Mesh _unitCube;
        private readonly List<Light> _hazardLights = new List<Light>(8);
        private readonly List<float> _hazardPhase = new List<float>(8);

        private static readonly Color ArcColor = new Color(0.55f, 0.9f, 1.6f);

        private void HazardMaterials()
        {
            if (_hazardWaterMat != null) return;
            _hazardWaterMat = Own(Proto3DMaterials.Lit3D("hazardWater", new Color(0.012f, 0.02f, 0.035f), 0.97f, 0.05f));
            _hazardCableMat = Own(Proto3DMaterials.Lit3D("hazardCable", new Color(0.03f, 0.03f, 0.035f), 0.55f, 0f));
            _hazardCopperMat = Own(Proto3DMaterials.Lit3D("hazardCopper", new Color(0.75f, 0.38f, 0.16f), 0.75f, 1f, emissionColor: new Color(0.25f, 0.1f, 0.03f)));
            _hazardPoleMat = Own(Proto3DMaterials.Lit3D("hazardPole", new Color(0.09f, 0.08f, 0.09f), 0.5f, 0.6f));
            _hazardGlowMat = Own(Proto3DMaterials.FxMat("hazardGlow", _blob, Color.white, 0, 0.5f));
            _hazardArcMatA = Own(Proto3DMaterials.FxMat("hazardArcA", null, ArcColor, 0, 0.3f));
            _hazardArcMatB = Own(Proto3DMaterials.FxMat("hazardArcB", null, ArcColor, 0, 0.3f));
            _unitCube = Own(Proto3DMeshes.ChamferBox(new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f), 0f, 1f));
        }

        /// <summary>Puddle, pole, cable, arcs and light for every run of spike tiles in one ring copy.</summary>
        private void ElectricPuddles(Transform copy)
        {
            HazardMaterials();
            var rnd = new System.Random(_look.Seed * 23 + 7);
            for (int y = 1; y < _map.Height; y++)
            {
                int x = 0;
                while (x < _map.Width)
                {
                    if (_map.Get(x, y) != Tile.Spike) { x++; continue; }
                    int x0 = x;
                    while (x < _map.Width && _map.Get(x, y) == Tile.Spike) x++;
                    Puddle(copy, rnd, x0, x, y);
                }
            }
        }

        private void Puddle(Transform copy, System.Random rnd, int x0, int x1, int y)
        {
            float R() => (float)rnd.NextDouble();
            float xm = (x0 + x1) * 0.5f, w = x1 - x0;
            // Water: a thin dark mirror slab, slightly wider than the hazard so its edge reads.
            var water = Proto3DMeshes.ChamferBox(new Vector3(x0 - 0.15f, y - 0.02f, -1.7f), new Vector3(x1 + 0.15f, y + 0.045f, 2.7f), 0.03f, 1f);
            Filled(MeshGo("hazardWater", copy, Own(water), _hazardWaterMat));

            // Broken utility pole behind the puddle (leaning), crossarm at the top.
            bool right = x1 < _map.Width && !_map.IsSolid(x1, y);
            float px = right ? x1 + 0.35f : x0 - 0.35f, pz = 2.4f, lean = (right ? 1f : -1f) * (8f + R() * 6f);
            var parts = new List<CombineInstance>();
            void Beam(Vector3 a, Vector3 b, float thick)
            {
                var d = b - a;
                if (d.sqrMagnitude < 1e-6f) return;
                parts.Add(new CombineInstance { mesh = _unitCube, transform = Matrix4x4.TRS((a + b) * 0.5f, Quaternion.LookRotation(d.normalized), new Vector3(thick, thick, d.magnitude)) });
            }
            var poleRot = Quaternion.Euler(0f, 0f, -lean);
            var foot = new Vector3(px, y, pz);
            var poleTop = foot + poleRot * new Vector3(0f, 4.6f, 0f);
            Beam(foot, poleTop, 0.16f);
            var armL = poleTop + poleRot * new Vector3(-0.6f, -0.2f, 0f);
            var armR = poleTop + poleRot * new Vector3(0.6f, -0.2f, 0f);
            Beam(armL, armR, 0.09f);
            var poleMesh = Own(new Mesh { name = "hazardPole", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 });
            poleMesh.CombineMeshes(parts.ToArray(), true, true);
            MeshGo("hazardPole", copy, poleMesh, _hazardPoleMat);

            // Downed cable: from the crossarm end, sagging, down into the water near the middle of the puddle.
            parts.Clear();
            var start = right ? armL : armR;
            var end = new Vector3(xm + (R() - 0.5f) * w * 0.4f, y + 0.04f, 0.2f + R() * 0.6f);
            var ctrl = new Vector3((start.x + end.x) * 0.5f, end.y + 0.3f, (start.z + end.z) * 0.5f);
            Vector3 prev = start;
            const int N = 14;
            for (int i = 1; i <= N; i++)
            {
                float t = i / (float)N;
                var p = (1 - t) * (1 - t) * start + 2 * (1 - t) * t * ctrl + t * t * end;
                Beam(prev, p, 0.055f);
                prev = p;
            }
            var cableMesh = Own(new Mesh { name = "hazardCable", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 });
            cableMesh.CombineMeshes(parts.ToArray(), true, true);
            MeshGo("hazardCable", copy, cableMesh, _hazardCableMat);
            // Frayed copper strands at the end.
            parts.Clear();
            for (int k = 0; k < 5; k++)
            {
                var d = new Vector3((R() - 0.5f) * 0.5f, 0.02f + R() * 0.12f, (R() - 0.5f) * 0.4f);
                Beam(end, end + d, 0.018f);
            }
            var copperMesh = Own(new Mesh { name = "hazardCopper", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 });
            copperMesh.CombineMeshes(parts.ToArray(), true, true);
            MeshGo("hazardCopper", copy, copperMesh, _hazardCopperMat);

            // Arcs (two flicker sets) and sparks at the cable end, a cyan glow pool on the water.
            var arcsA = new UvQuads();
            var arcsB = new UvQuads();
            for (int k = 0; k < 7; k++)
            {
                var set = k % 2 == 0 ? arcsA : arcsB;
                Vector3 a = end + new Vector3(0f, 0.02f, -0.15f);
                float dir = R() * Mathf.PI * 2f, len = 0.35f + R() * 0.7f;
                bool up = k < 2;
                for (int s = 0; s < 5; s++)
                {
                    var b = up
                        ? a + new Vector3((R() - 0.5f) * 0.18f, len / 5f, 0f)
                        : a + new Vector3(Mathf.Cos(dir) * len / 5f + (R() - 0.5f) * 0.12f, (R() - 0.3f) * 0.06f, 0f);
                    set.Line(a, b, 0.035f, WhiteUv, Color.white);
                    a = b;
                }
            }
            for (int k = 0; k < 10; k++)
            {
                var sp = end + new Vector3((R() - 0.5f) * 0.9f, 0.05f + R() * 0.5f, -0.2f);
                (k % 2 == 0 ? arcsA : arcsB).Quad(sp, 0.05f, 0.05f, WhiteUv, new Color(1.4f, 1.3f, 1f));
            }
            MeshGo("hazardArcsA", copy, Own(arcsA.Build("hazardArcsA")), _hazardArcMatA);
            MeshGo("hazardArcsB", copy, Own(arcsB.Build("hazardArcsB")), _hazardArcMatB);
            var glow = new QuadBatch();
            glow.Floor(new Vector3(xm, y + 0.06f, 0.5f), w + 1.6f, 4.2f, new Color(0.12f, 0.35f, 0.55f));
            glow.Billboard(end + new Vector3(0f, 0.3f, -0.4f), 1.6f, 1.2f, new Color(0.3f, 0.6f, 1f));
            MeshGo("hazardGlow", copy, Own(glow.Build("hazardGlow")), _hazardGlowMat);

            var light = NewLight("hazardLight", copy, LightType.Point, new Color(0.45f, 0.8f, 1f), 6f);
            light.transform.localPosition = end + new Vector3(0f, 0.5f, -0.5f);
            light.range = 5f;
            light.shadows = LightShadows.None;
            _hazardLights.Add(light);
            _hazardPhase.Add(R() * 10f);
        }

        /// <summary>Electric flicker: the two arc sets crackle out of phase, the lights follow.</summary>
        private void HazardUpdate(float time)
        {
            if (_hazardArcMatA == null) return;
            float a = Crackle(time, 0f), b = Crackle(time, 3.7f);
            _hazardArcMatA.SetColor("_Color", ArcColor * a);
            _hazardArcMatB.SetColor("_Color", ArcColor * b);
            for (int i = 0; i < _hazardLights.Count; i++)
                if (_hazardLights[i] != null) _hazardLights[i].intensity = 1.5f + 9f * Mathf.Max(Crackle(time, _hazardPhase[i]), 0.1f);
        }

        /// <summary>Mostly-on electric flicker with hard drop-outs and bright bursts.</summary>
        private static float Crackle(float t, float phase)
        {
            float n = Mathf.PerlinNoise(t * 17f + phase, phase * 1.3f);
            float burst = Mathf.PerlinNoise(t * 41f, phase + 5f);
            if (n < 0.32f) return 0.05f;
            return burst > 0.7f ? 1.8f : 0.7f + n * 0.5f;
        }
    }
}
