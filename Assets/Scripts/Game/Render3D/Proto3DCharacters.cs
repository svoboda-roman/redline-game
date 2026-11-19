using System;
using System.Collections.Generic;
using UnityEngine;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// A character shown in the 2.5D scene, placed by its feet on the play plane (z = 0). The sprite quad below
    /// is one implementation; a rigged 3D model (e.g. a Mixamo-animated player) implements the same interface and
    /// drops in without touching the scene builder.
    /// </summary>
    public interface IProto3DActor
    {
        /// <summary>Root transform (feet at its origin).</summary>
        Transform Root { get; }

        /// <summary>Places the feet at <paramref name="feet"/> (world x, y; z = 0) facing +x (1) or -x (-1).</summary>
        void Place(Vector2 feet, int facing);
    }

    /// <summary>
    /// Sprite-sheet actor: one Lit alpha-clipped quad (colour + normal sheet, lit by the 3D lamps, casts shadows)
    /// plus an additive emissive-mask quad. Frame rects come from the sheet json (bottom-left origin, pivot at feet).
    /// </summary>
    /// <remarks>Prototype-only. Reads the sheet PNGs from disk because the normal sheet is imported as sRGB.</remarks>
    public sealed class Proto3DSpriteActor : IProto3DActor
    {
        [Serializable] private class R { public float x, y, w, h; }
        [Serializable] private class P { public float x, y; }
        [Serializable] private class Frame { public string name; public R rect; public P pivot; }
        [Serializable] private class Size { public float w, h; }
        [Serializable] private class Sheet { public float ppu; public Size atlas; public List<Frame> frames; }

        private static readonly Dictionary<string, (Material lit, Material em, Sheet sheet)> Cache =
            new Dictionary<string, (Material, Material, Sheet)>();

        private readonly Transform _flip;

        /// <inheritdoc/>
        public Transform Root { get; }

        /// <summary>Builds the actor for sheet <paramref name="kind"/> (folder name, e.g. "Samurai") at <paramref name="frame"/>.</summary>
        public Proto3DSpriteActor(Transform parent, string artDir, string kind, string frame, Color tintR, Color tintG, Color tintB)
        {
            string lower = kind.ToLowerInvariant();
            if (!Cache.TryGetValue(kind, out var c))
            {
                string dir = System.IO.Path.Combine(artDir, kind);
                var sheet = JsonUtility.FromJson<Sheet>(System.IO.File.ReadAllText(System.IO.Path.Combine(dir, lower + ".json")));
                var col = Proto3DTextures.LoadPng(System.IO.Path.Combine(dir, lower + "_color.png"), false);
                var nrm = Proto3DTextures.LoadPng(System.IO.Path.Combine(dir, lower + "_normal.png"), true);
                Proto3DTextures.OpaqueNormals(nrm);
                var em = Proto3DTextures.LoadPng(System.IO.Path.Combine(dir, lower + "_emissive.png"), false);
                c = (Proto3DMaterials.Sprite(kind, col, nrm), Proto3DMaterials.EmissiveMask(kind + "_em", em, tintR, tintG, tintB), sheet);
                Cache[kind] = c;
            }
            var f = c.sheet.frames.Find(x => x.name == frame) ?? c.sheet.frames[0];
            float ppu = c.sheet.ppu > 0 ? c.sheet.ppu : 40f;
            float w = f.rect.w / ppu, h = f.rect.h / ppu;
            var st = new Vector4(f.rect.w / c.sheet.atlas.w, f.rect.h / c.sheet.atlas.h, f.rect.x / c.sheet.atlas.w, f.rect.y / c.sheet.atlas.h);

            Root = new GameObject(kind).transform;
            Root.SetParent(parent, false);
            _flip = new GameObject("flip").transform;
            _flip.SetParent(Root, false);
            var mesh = Proto3DMeshes.Quad(w, h);
            var offset = new Vector3((0.5f - f.pivot.x) * w, (0.5f - f.pivot.y) * h, 0f);

            var lit = new Material(c.lit);
            lit.SetVector("_BaseMap_ST", st);
            Quad("body", mesh, lit, offset, true);
            var emm = new Material(c.em);
            emm.SetVector("_MainTex_ST", st);
            Quad("emissive", mesh, emm, offset + new Vector3(0, 0, -0.01f), false);
        }

        private void Quad(string name, Mesh mesh, Material mat, Vector3 pos, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_flip, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            r.renderingLayerMask = Arena3DRenderer.CharacterRenderingLayerMask; // receives the character rim lights
        }

        /// <inheritdoc/>
        public void Place(Vector2 feet, int facing)
        {
            Root.position = new Vector3(feet.x, feet.y, 0f);
            _flip.localScale = new Vector3(facing >= 0 ? 1f : -1f, 1f, 1f);
        }
    }
}
