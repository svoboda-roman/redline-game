using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Redline.Game.Ronin;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Redline.RoninEditor
{
    /// <summary>
    /// Diagnostic for the ronin clips: samples each clip three ways and logs leg metrics plus stick-figure strips
    /// (side view, model facing +Z drawn left to right): (A) the live <see cref="RoninView"/> state machine via Tick,
    /// (B) <see cref="RoninView.SamplePose"/> (direct humanoid sample), (C) a temporary Generic import of the clip FBX
    /// sampled onto the raw transforms (no humanoid retarget). Writes PNG strips to <c>REDLINE_RONIN_DIAG</c>.
    /// </summary>
    /// <remarks>Editor only. Batch: <c>-executeMethod Redline.RoninEditor.RoninAnimDiag.RunFromCommandLine</c>.</remarks>
    public static class RoninAnimDiag
    {
        private static readonly RoninClip[] Clips = { RoninClip.Idle, RoninClip.Run, RoninClip.Jump, RoninClip.Fall, RoninClip.Hurt, RoninClip.Death, RoninClip.Attack1 };
        private static readonly string[] Files = { "idle", "run", "jump", "fall", "hurt", "death", "attack_1" };
        private const string TmpDir = "Assets/RoninDiagTmp";
        private const int Cell = 72, Samples = 10;

        /// <summary>Batch entry point.</summary>
        public static void RunFromCommandLine()
        {
            try { Run(Environment.GetEnvironmentVariable("REDLINE_RONIN_DIAG") ?? "Temp/ronin-diag"); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogError("RONIN_DIAG_FAIL " + e); EditorApplication.Exit(1); }
        }

        /// <summary>Runs the diagnostic into <paramref name="outDir"/>.</summary>
        public static void Run(string outDir)
        {
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var assets = RoninAssets.Load();
            var root = new GameObject("DiagRoot");
            var rows = new List<(string, List<Vector3[]>)>();
            try
            {
                var view = RoninView.Create(assets, root.transform);
                view.AutoTick = false;
                view.CameraTurn = 0f;
                var anim = view.Model.GetComponent<Animator>();
                Debug.Log($"RONIN_DIAG avatar human={anim.isHuman} valid={anim.avatar != null && anim.avatar.isValid} hasTransformHierarchy={anim.hasTransformHierarchy}");
                LogHierarchy(view.Model, 0, 3);
                for (int c = 0; c < Clips.Length; c++)
                {
                    var clip = assets.Clips[(int)Clips[c]];
                    if (clip == null) { Debug.Log("RONIN_DIAG missing " + Clips[c]); continue; }
                    Debug.Log($"RONIN_DIAG clip={clip.name} len={clip.length:F3} fr={clip.frameRate} loop={clip.isLooping} human={clip.humanMotion} " +
                              $"curves={AnimationUtility.GetCurveBindings(clip).Length} avgVel={clip.averageSpeed} apparentSpeed={clip.apparentSpeed}");
                    foreach (var b in AnimationUtility.GetCurveBindings(clip).Take(400).Where(b => b.propertyName.Contains("Leg") || b.propertyName.Contains("Root") || b.propertyName.Contains("Motion")).Take(14))
                    {
                        var curve = AnimationUtility.GetEditorCurve(clip, b);
                        float mn = curve.keys.Min(k => k.value), mx = curve.keys.Max(k => k.value);
                        Debug.Log($"RONIN_DIAG   {clip.name} curve '{b.path}' {b.propertyName} keys={curve.length} range=[{mn:F3},{mx:F3}]");
                    }
                    // A: state-machine path (as the old baker).
                    rows.Add(($"A_{Files[c]}", SampleTick(view, Clips[c], clip.length, anim)));
                    // B: direct humanoid sample.
                    var b2 = new List<Vector3[]>();
                    for (int i = 0; i < Samples; i++)
                    {
                        float t = clip.length * i / (Samples - 1);
                        view.SamplePose(Clips[c], t);
                        b2.Add(Bones(anim, view.transform));
                    }
                    rows.Add(($"B_{Files[c]}", b2));
                    Metrics("B", Files[c], b2);
                }
                // C: Generic copies.
                if (!AssetDatabase.IsValidFolder(TmpDir)) AssetDatabase.CreateFolder("Assets", "RoninDiagTmp");
                var raw = UnityEngine.Object.Instantiate(assets.Model, root.transform);
                var rawAnim = raw.GetComponent<Animator>();
                for (int c = 0; c < Files.Length; c++)
                {
                    string src = RoninImporter.Root + "Animations/" + Files[c] + ".fbx", dst = TmpDir + "/" + Files[c] + ".fbx";
                    AssetDatabase.CopyAsset(src, dst);
                    var mi = (ModelImporter)AssetImporter.GetAtPath(dst);
                    mi.animationType = ModelImporterAnimationType.Generic;
                    mi.importAnimation = true;
                    mi.SaveAndReimport();
                    var gclip = AssetDatabase.LoadAllAssetsAtPath(dst).OfType<AnimationClip>().FirstOrDefault(x => !x.name.StartsWith("__preview__"));
                    if (gclip == null) { Debug.Log("RONIN_DIAG no generic clip " + dst); continue; }
                    var paths = AnimationUtility.GetCurveBindings(gclip).Select(b => b.path).Distinct().Take(4).ToArray();
                    Debug.Log($"RONIN_DIAG generic {gclip.name} len={gclip.length:F3} paths: {string.Join(" | ", paths)}");
                    // Sample on the GameObject whose children match the clip's top path segment.
                    string top = paths.Length > 0 ? paths[0].Split('/')[0] : "";
                    var host = FindParentOf(raw.transform, top) ?? raw.transform;
                    Debug.Log($"RONIN_DIAG generic host={host.name} for top='{top}'");
                    var list = new List<Vector3[]>();
                    for (int i = 0; i < Samples; i++)
                    {
                        float t = gclip.length * i / (Samples - 1);
                        gclip.SampleAnimation(host.gameObject, t);
                        list.Add(Bones(rawAnim, raw.transform));
                    }
                    rows.Add(($"C_{Files[c]}", list));
                    Metrics("C", Files[c], list);
                }
                Draw(rows, Path.Combine(outDir, "diag.png"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                if (AssetDatabase.IsValidFolder(TmpDir)) AssetDatabase.DeleteAsset(TmpDir);
            }
        }

        /// <summary>Batch entry point: skinning check (bones array vs avatar, CPU skin vs bones, a GPU render).</summary>
        public static void SkinCheckFromCommandLine()
        {
            try { SkinCheck(Environment.GetEnvironmentVariable("REDLINE_RONIN_DIAG") ?? "Temp/ronin-diag"); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogError("RONIN_DIAG_FAIL " + e); EditorApplication.Exit(1); }
        }

        /// <summary>Checks that the mesh follows the leg bones at a wide-stride run pose.</summary>
        public static void SkinCheck(string outDir)
        {
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var assets = RoninAssets.Load();
            var root = new GameObject("DiagRoot");
            try
            {
                var view = RoninView.Create(assets, root.transform);
                view.AutoTick = false;
                var anim = view.Model.GetComponent<Animator>();
                var feet = new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftLowerLeg, HumanBodyBones.Hips, HumanBodyBones.RightHand };
                foreach (float t in new[] { 0.13f, 0.4f })
                {
                    view.SamplePose(RoninClip.Run, t);
                    foreach (var smr in view.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        var bones = smr.bones;
                        var mesh = smr.sharedMesh;
                        Debug.Log($"RONIN_SKIN t={t} smr={smr.name} enabled={smr.enabled} bones={bones.Length} root={(smr.rootBone ? smr.rootBone.name : "null")} mesh={mesh.name} v={mesh.vertexCount} bind={mesh.bindposeCount} quality={smr.quality} skinnedMotion={smr.skinnedMotionVectors} gpu={SystemInfo.supportsComputeShaders}");
                        var baked = new Mesh();
                        smr.BakeMesh(baked, true);
                        var verts = baked.vertices;
                        var bw = mesh.boneWeights;
                        foreach (var hb in feet)
                        {
                            var bt = anim.GetBoneTransform(hb);
                            int idx = Array.IndexOf(bones, bt);
                            int dupes = bones.Count(b => b != null && b.name == bt.name);
                            Vector3 sum = Vector3.zero; int n = 0;
                            for (int v = 0; v < bw.Length && idx >= 0; v++)
                                if (bw[v].boneIndex0 == idx && bw[v].weight0 > 0.6f) { sum += smr.transform.TransformPoint(verts[v]) - smr.transform.TransformVector(Vector3.zero); n++; }
                            // BakeMesh(useScale) returns vertices in the renderer's local space without its scale: place with position+rotation only.
                            Vector3 cpu = Vector3.zero;
                            if (n > 0)
                            {
                                Vector3 s2 = Vector3.zero;
                                for (int v = 0; v < bw.Length; v++)
                                    if (bw[v].boneIndex0 == idx && bw[v].weight0 > 0.6f) s2 += smr.transform.position + smr.transform.rotation * verts[v];
                                cpu = s2 / n;
                            }
                            Debug.Log($"RONIN_SKIN   {hb} bone={bt.name} idxInSmr={idx} sameNameInSmr={dupes} boneRootIsView={bt.root == root.transform} bonePos={bt.position:F3} cpuSkinMean={cpu:F3} verts={n}" +
                                      (idx >= 0 ? $" smrBoneIsSame={ReferenceEquals(bones[idx], bt)} smrBonePath={BonePath(bones[idx])}" : ""));
                        }
                        UnityEngine.Object.DestroyImmediate(baked);
                    }
                    // GPU render of the pose with the in-game materials.
                    var camGo = new GameObject("cam");
                    var cam = camGo.AddComponent<Camera>();
                    cam.orthographic = true; cam.orthographicSize = 1.2f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.gray;
                    cam.transform.position = new Vector3(0f, 1f, -10f);
                    var light = new GameObject("l").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(30f, 20f, 0f);
                    var rt = new RenderTexture(256, 256, 24);
                    cam.targetTexture = rt; cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false); tex.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
                    RenderTexture.active = null;
                    File.WriteAllBytes(System.IO.Path.Combine(outDir, $"gpu_run_{t:F2}.png"), tex.EncodeToPNG());
                    cam.targetTexture = null;
                    UnityEngine.Object.DestroyImmediate(tex); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(camGo); UnityEngine.Object.DestroyImmediate(light.gameObject);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static string BonePath(Transform t) => t == null ? "null" : t.parent == null ? t.name : BonePath(t.parent) + "/" + t.name;

        private static List<Vector3[]> SampleTick(RoninView view, RoninClip clip, float len, Animator anim)
        {
            var idle = new RoninInput { Grounded = true, Facing = 1 };
            view.FadeTime = 0.05f;
            for (int i = 0; i < 72; i++) { view.Input = idle; view.Tick(1f / 120f); }
            var inp = idle;
            switch (clip)
            {
                case RoninClip.Run: inp.Speed = 6f; break;
                case RoninClip.Jump: inp.Grounded = false; inp.VelocityY = 6f; break;
                case RoninClip.Fall: inp.Grounded = false; inp.VelocityY = -4f; break;
                case RoninClip.Death: inp.Dead = true; break;
            }
            var list = new List<Vector3[]>();
            float span = Mathf.Min(len, 1.5f);
            int k = 0;
            for (float t = 0f; k < Samples; t += 1f / 120f)
            {
                var x = inp;
                if (clip == RoninClip.Hurt) x.Hurt = t < 0.1f;
                if (clip == RoninClip.Attack1) { x.Attacking = t < 0.1f; x.StrikeId = 99; }
                view.Input = x;
                view.Tick(1f / 120f);
                if (t >= span * k / (Samples - 1) - 1e-4f) { list.Add(Bones(anim, view.transform)); k++; }
            }
            Metrics("A", clip.ToString(), list);
            return list;
        }

        private static readonly HumanBodyBones[] B =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
        };
        private static readonly int[,] Links = { { 0, 1 }, { 1, 2 }, { 2, 3 }, { 0, 4 }, { 4, 5 }, { 5, 6 }, { 6, 7 }, { 0, 8 }, { 8, 9 }, { 9, 10 }, { 10, 11 }, { 2, 12 }, { 12, 13 }, { 13, 14 }, { 2, 15 }, { 15, 16 }, { 16, 17 } };

        /// <summary>Bone positions in the root's frame but along the model's own facing: x = forward, y = up, z = right.</summary>
        private static Vector3[] Bones(Animator a, Transform root)
        {
            var model = a.transform;
            var p = new Vector3[B.Length];
            for (int i = 0; i < B.Length; i++)
            {
                var t = a.GetBoneTransform(B[i]);
                if (t == null) continue;
                var d = t.position - root.position;
                // Facing direction of the model (its local +Z) and right.
                var fwd = model.forward; var right = model.right;
                p[i] = new Vector3(Vector3.Dot(d, fwd), d.y, Vector3.Dot(d, right));
            }
            return p;
        }

        private static void Metrics(string tag, string clip, List<Vector3[]> poses)
        {
            float sepMin = 9, sepMax = -9, hipMin = 9, hipMax = -9, footMax = -9;
            foreach (var p in poses)
            {
                float sep = p[6].x - p[10].x;
                sepMin = Mathf.Min(sepMin, sep); sepMax = Mathf.Max(sepMax, sep);
                hipMin = Mathf.Min(hipMin, p[0].y); hipMax = Mathf.Max(hipMax, p[0].y);
                footMax = Mathf.Max(footMax, Mathf.Min(p[6].y, p[10].y));
            }
            Debug.Log($"RONIN_DIAG metrics {tag} {clip}: footFwdSep=[{sepMin:F2},{sepMax:F2}] hipY=[{hipMin:F2},{hipMax:F2}] lowerFootMaxY={footMax:F2}");
        }

        private static Transform FindParentOf(Transform t, string childName)
        {
            for (int i = 0; i < t.childCount; i++) if (t.GetChild(i).name == childName) return t;
            for (int i = 0; i < t.childCount; i++) { var r = FindParentOf(t.GetChild(i), childName); if (r != null) return r; }
            return null;
        }

        private static void LogHierarchy(Transform t, int depth, int max)
        {
            Debug.Log("RONIN_DIAG node " + new string(' ', depth * 2) + t.name + " rot=" + t.localEulerAngles + " s=" + t.localScale);
            if (depth >= max) return;
            for (int i = 0; i < t.childCount && i < 6; i++) LogHierarchy(t.GetChild(i), depth + 1, max);
        }

        private static void Draw(List<(string name, List<Vector3[]> poses)> rows, string path)
        {
            int w = Cell * Samples, h = Cell * rows.Count;
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(24, 24, 30, 255);
            // Scale: model height ~1.8 (raw) or ~1.6 x scale; normalise per row by the idle-ish max height.
            for (int r = 0; r < rows.Count; r++)
            {
                var poses = rows[r].poses;
                float hmax = 0.01f;
                foreach (var p in poses) foreach (var v in p) hmax = Mathf.Max(hmax, v.y);
                float s = (Cell - 8) / Mathf.Max(hmax, 1.2f * Mathf.Abs(poses[0][3].y));
                int y0 = (rows.Count - 1 - r) * Cell + 3;
                Color32 tag = rows[r].name[0] == 'A' ? new Color32(255, 120, 120, 255) : rows[r].name[0] == 'B' ? new Color32(120, 255, 140, 255) : new Color32(120, 170, 255, 255);
                for (int x = 0; x < 6; x++) for (int y = 0; y < 3; y++) px[(y0 + Cell - 6 + y) * w + x + 2] = tag;
                for (int i = 0; i < poses.Count; i++)
                {
                    var p = poses[i];
                    int cx = i * Cell + Cell / 2;
                    // ground line
                    for (int x = i * Cell + 4; x < (i + 1) * Cell - 4; x++) px[y0 * w + x] = new Color32(60, 60, 70, 255);
                    for (int l = 0; l < Links.GetLength(0); l++)
                    {
                        int a = Links[l, 0], b = Links[l, 1];
                        bool left = (a >= 4 && a <= 7) || (b >= 4 && b <= 7) || (b >= 12 && b <= 14);
                        bool right = (b >= 8 && b <= 11) || (b >= 15);
                        var col = left ? new Color32(255, 200, 80, 255) : right ? new Color32(80, 200, 255, 255) : new Color32(230, 230, 230, 255);
                        Line(px, w, h, cx + p[a].x * s, y0 + p[a].y * s, cx + p[b].x * s, y0 + p[b].y * s, col);
                    }
                }
                Debug.Log("RONIN_DIAG row " + r + " = " + rows[r].name);
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            Debug.Log("RONIN_DIAG wrote " + path);
        }

        private static void Line(Color32[] px, int w, int h, float x0, float y0, float x1, float y1, Color32 c)
        {
            int n = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0))) + 1;
            for (int i = 0; i <= n; i++)
            {
                int x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, i / (float)n)), y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, i / (float)n));
                if (x >= 0 && y >= 0 && x < w && y < h) px[y * w + x] = c;
            }
        }
    }
}
