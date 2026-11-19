using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// Real floor reflections and the front fill for <see cref="Arena3DRenderer"/>:
    /// <list type="bullet">
    /// <item>a <see cref="FloorReflection3D"/> mirror render of the scene about the deck top (half resolution);</item>
    /// <item>a reflection overlay on the deck (wetness from the street gloss map: damp smear plus mirror puddles), on
    /// the puddle decals (their mask) and, where <see cref="Arena3DLook.FloodWater"/> is set, on a flood plane
    /// behind the deck at floor height;</item>
    /// <item>an unshadowed front fill directional on rendering layer <see cref="FillRenderingLayer"/> that lights
    /// only the play-plane level (platform fronts, deck), not the mid-ground or the characters.</item>
    /// </list>
    /// </summary>
    public sealed partial class Arena3DRenderer
    {
        /// <summary>Rendering layer index lit by the front fill (play-plane level geometry only).</summary>
        public const int FillRenderingLayer = 2;

        /// <summary>Renderer mask for play-plane level geometry: default layer plus <see cref="FillRenderingLayer"/>.</summary>
        public const uint LevelRenderingLayerMask = 1u | (1u << FillRenderingLayer);

        private Material _deckReflMat, _puddleReflMat, _waterReflMat;
        private FloorReflection3D _reflection;
        private Shader _reflShader;

        /// <summary>The floor reflection driver (null when the shader is missing).</summary>
        public FloorReflection3D Reflection => _reflection;

        private Material ReflMat(string name, float mode, Texture mask, float maskUnits, float damp, float puddle, float cover)
        {
            var m = Own(new Material(_reflShader) { name = name });
            if (mask != null) m.SetTexture("_MaskTex", mask);
            m.SetFloat("_MaskFromUv", mode);
            m.SetFloat("_MaskUnits", maskUnits);
            m.SetFloat("_Damp", damp);
            m.SetFloat("_Puddle", puddle);
            m.SetFloat("_Cover", cover);
            _reflection.Register(m);
            return m;
        }

        /// <summary>Creates the mirror driver and the overlay materials (after SurfaceMaterials).</summary>
        private void ReflectionSetup()
        {
            _reflShader = Shader.Find("Proto3D/FloorReflection");
            if (_reflShader == null) { Debug.LogWarning("[Reflection] Proto3D/FloorReflection shader missing; floor reflections off"); return; }
            _reflection = _root.gameObject.AddComponent<FloorReflection3D>();
            _reflection.PlaneY = FloorTop;
            _reflection.Background = _look.FogColor;
            _deckReflMat = ReflMat("deckReflection", 0f, _streetGloss, _streetUnits, _look.FloorReflect, _look.PuddleReflect, 0.55f);
            _puddleReflMat = ReflMat("puddleReflection", 1f, _puddleMat.GetTexture("_BaseMap"), 1f, 0f, _look.PuddleReflect, 0.7f);
            _puddleReflMat.renderQueue += 1;
            if (_look.FloodWater) _waterReflMat = ReflMat("waterReflection", 2f, null, 1f, 0f, _look.PuddleReflect, 0.85f);
        }

        /// <summary>Deck overlay for one ring copy: one quad per floor run, a hair above the deck top.</summary>
        private void DeckReflection(Transform copy)
        {
            if (_deckReflMat == null) return;
            var v = new List<Vector3>(); var t = new List<int>(); var n = new List<Vector3>(); var uv = new List<Vector2>();
            ForEachRun(0, (x0, x1) =>
            {
                float a = x0, b = x1;
                if (!_map.WrapsX) { if (x0 == 0) a -= 40f; if (x1 == _map.Width) b += 40f; }
                float y = FloorTop + 0.004f, z0 = _look.DeckNear + 0.3f, z1 = _look.DeckFar - 0.62f;
                int i = v.Count;
                v.Add(new Vector3(a, y, z0)); v.Add(new Vector3(a, y, z1)); v.Add(new Vector3(b, y, z1)); v.Add(new Vector3(b, y, z0));
                for (int k = 0; k < 4; k++) { n.Add(Vector3.up); uv.Add(Vector2.zero); }
                t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
            });
            if (v.Count == 0) return;
            var mesh = Own(new Mesh { name = "deckReflection" });
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            Hidden(MeshGo("deckReflection", copy, mesh, _deckReflMat));
        }

        /// <summary>Flood plane behind the deck at floor height (dark water base + mirror overlay), follows the camera.</summary>
        private void FloodWater()
        {
            if (!_look.FloodWater || _waterReflMat == null) return;
            var baseCol = _look.FogColor * 0.12f; baseCol.a = 1f;
            var water = Own(Proto3DMaterials.Lit3D("floodWater", baseCol, 0.92f, 0f));
            var mesh = Own(new Mesh { name = "floodWater" });
            // Starts on the deck behind the play plane (z 4), a hair above it: from the camera's height the 0.7 parapet
            // at DeckFar hid a plane at floor height out to z ≈ 33, so the flood never showed. Now the back half of the
            // deck is under water (ruins, rubble and columns stand in it) and the characters' lane stays dry.
            float z0 = 4f, z1 = 140f, w = 700f, y = FloorTop + 0.03f;
            mesh.SetVertices(new List<Vector3> { new Vector3(-w, y, z0), new Vector3(-w, y, z1), new Vector3(w, y, z1), new Vector3(w, y, z0) });
            mesh.SetNormals(new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            mesh.SetUVs(0, new List<Vector2> { Vector2.zero, Vector2.up, Vector2.one, Vector2.right });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateBounds();
            var go = Hidden(MeshGo("floodWater", _follow, mesh, water, _waterReflMat));
            go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>Front fill: unshadowed directional from the camera side, rendering layer <see cref="FillRenderingLayer"/> only.</summary>
        private void FrontFill()
        {
            if (_look.FrontFillIntensity <= 0.001f) return;
            var fill = NewLight("frontFill", _root, LightType.Directional, _look.FrontFill, _look.FrontFillIntensity);
            fill.transform.rotation = Quaternion.LookRotation(new Vector3(0.12f, -0.35f, 1f).normalized);
            fill.GetUniversalAdditionalLightData().renderingLayers = (RenderingLayerMask)(1u << FillRenderingLayer);
        }

        private Material _fasciaSideMat;

        /// <summary>
        /// Moves the level boxes' side faces (normal ±X) that face away from the key out of sub-mesh 0 into a new
        /// sub-mesh 2, drawn with <see cref="_fasciaSideMat"/>: the fascia plus a self-lit fill term (the fascia albedo
        /// map × base colour × the level's front-fill colour, as emission). The boxes run 5-8 units deep and the camera
        /// is only ≈ 9.3 units out, so a box at the frame edge shows a big side face; turned away from the key it got
        /// ambient only and rendered near black (Loop02, right edge). A rendering-layer "sideFill" directional did not
        /// reach it (measured: face at ambient level with N·L 0.91 to that light, layers matching), so the fill is
        /// carried by the material instead: no light, no per-light cost, independent of the visible-light limit.
        /// </summary>
        private void SplitShadedSides(Mesh mesh)
        {
            _fasciaSideMat = Own(new Material(_fasciaMat) { name = "fasciaSide" });
            var b = _fasciaMat.GetColor("_BaseColor");
            _fasciaSideMat.SetTexture("_EmissionMap", _fasciaMat.GetTexture("_BaseMap"));
            _fasciaSideMat.SetColor("_EmissionColor", new Color(b.r * _look.FrontFill.r, b.g * _look.FrontFill.g, b.b * _look.FrontFill.b) * _look.FasciaSideFill);
            _fasciaSideMat.EnableKeyword("_EMISSION");
            _fasciaSideMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            if (mesh.subMeshCount < 2 || mesh.vertexCount == 0) { mesh.subMeshCount = 3; return; }
            var normals = mesh.normals;
            var tris = mesh.GetTriangles(0);
            var keep = new List<int>(tris.Length);
            var side = new List<int>();
            Vector3 toKey = -_look.KeyDir.normalized;
            for (int i = 0; i < tris.Length; i += 3)
            {
                var n = normals[tris[i]];
                bool shadedSide = Mathf.Abs(n.x) > 0.5f && Vector3.Dot(n, toKey) < 0.1f;
                var dst = shadedSide ? side : keep;
                dst.Add(tris[i]); dst.Add(tris[i + 1]); dst.Add(tris[i + 2]);
            }
            var tops = mesh.GetTriangles(1);
            mesh.subMeshCount = 3;
            mesh.SetTriangles(keep, 0, false);
            mesh.SetTriangles(tops, 1, false);
            mesh.SetTriangles(side, 2, true);
        }

        /// <summary>Puts <paramref name="go"/> on the layer the mirror camera skips.</summary>
        private static GameObject Hidden(GameObject go)
        {
            go.layer = FloorReflection3D.HiddenLayer;
            return go;
        }

        /// <summary>Marks a play-plane level renderer as lit by the front fill.</summary>
        private static GameObject Filled(GameObject go)
        {
            go.GetComponent<Renderer>().renderingLayerMask = LevelRenderingLayerMask;
            return go;
        }
    }
}
