using System;
using System.Collections.Generic;
using System.IO;
using Redline.Game.Render3D.Integration;
using Redline.Game.Ronin;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Redline.RoninEditor
{
    /// <summary>
    /// Pre-renders the Tech Ronin into the lit-sprite sheet format the enemies use
    /// (<c>Assets/Resources/Art/Characters/Ronin/ronin_{color,normal,emissive}.png</c> + <c>ronin.json</c>).
    /// Idle, run, jump, fall, hurt and death sample their clip directly (<see cref="RoninView.SamplePose"/>, exact clip
    /// times, no crossfade, full length); hang, slide and the slashes go through the live <see cref="RoninView"/> state
    /// machine (the running slashes need its upper-body layer). Spring bones run at 120 Hz with the in-game run velocity.
    /// Skinned meshes are rendered via CPU-skinned proxies (BakeMesh per snapshot). An orthographic camera at the
    /// in-game yaw/pitch renders at exactly 48 px per world unit (<c>Pixel3DCamera.TexelsPerUnit</c>, 1 sprite px = 1 game texel) with 4x4 coverage
    /// down-sampling, in three unlit passes (albedo, view-space normal, emissive channels R blade / G eyes+cyber / B hat band).
    /// A clean-up pass enforces the readability rules: no isolated pixels, emissive blobs of at least 2x2, lines of at least 2 px.
    /// Per-frame left-hand offsets (world units from the feet pivot) go into the JSON for the hook rope.
    /// </summary>
    /// <remarks>Editor only. Batch: <c>-executeMethod Redline.RoninEditor.RoninSpriteBaker.BakeFromCommandLine</c> (needs the GPU, no -nographics).</remarks>
    public static class RoninSpriteBaker
    {
        private const int Ppu = (int)Redline.Game.Render3D.Pixel3DCamera.TexelsPerUnit, Ss = 4, CW = 192, CH = 136, PadBottom = 10, AtlasWidth = 1024;
        private const float Step = 1f / 120f, Pitch = 3f;
        private const string OutDir = "Assets/Resources/Art/Characters/Ronin/";
        private const string EvidenceDir = "production/qa/evidence/";

        private sealed class Fr
        {
            public bool[] Op = new bool[CW * CH];
            public Color32[] Col = new Color32[CW * CH];
            public Color32[] Nrm = new Color32[CW * CH];
            public byte[] Em = new byte[CW * CH];
            /// <summary>Blade smear intensity per texel (0..1, emissive R only, written after the clean-up); null = none.</summary>
            public float[] Smear;
            public Vector2 Hand;

            public Fr Clone(string name) => new Fr
            {
                Op = (bool[])Op.Clone(), Col = (Color32[])Col.Clone(), Nrm = (Color32[])Nrm.Clone(), Em = (byte[])Em.Clone(),
                Smear = Smear != null ? (float[])Smear.Clone() : null, Hand = Hand, Name = name,
            };
            public string Name;
        }

        private sealed class AnimData { public string Name; public float Fps; public bool Loop; public float Stride; public List<Fr> Frames = new List<Fr>(); }

        private static RoninView _view;
        private static Camera _cam;
        private static RenderTexture _rt;
        private static Material[][][] _mats;
        private static Renderer[] _rends;
        private static SkinnedMeshRenderer[] _skinned = new SkinnedMeshRenderer[0];
        private static Mesh[] _proxyMeshes = new Mesh[0];
        private static int _presses;

        /// <summary>Batch entry point.</summary>
        public static void BakeFromCommandLine()
        {
            try { Bake(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogError("RONIN_BAKE_FAIL " + e); EditorApplication.Exit(1); }
        }

        /// <summary>Batch entry point: bake, then refresh the shader-variant keeper materials (one editor run for both).</summary>
        public static void BakeAndKeepVariantsFromCommandLine()
        {
            try { Bake(); ShaderVariantKeeper.Create(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogError("RONIN_BAKE_FAIL " + e); EditorApplication.Exit(1); }
        }

        /// <summary>Bakes the sheet and writes the assets and the evidence preview.</summary>
        [MenuItem("Redline/Bake Ronin Sprites")]
        public static void Bake()
        {
            RoninSetup.EnsureAssets();   // registers newly exported clips (sprint, land, running slashes)
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var assets = RoninAssets.Load();
            if (assets == null) throw new InvalidOperationException("RoninAssets missing");
            GameObject root = null;
            try
            {
                SetupCamera();
                root = new GameObject("BakeRoot");
                BakeInto(assets, root);
            }
            finally
            {
                // The camera, its render texture and the 3 x renderers x slots bake materials are HideAndDontSave or
                // scene objects that outlive the bake in the editor: free them even when a capture throws.
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                Cleanup();
            }
        }

        private static void Cleanup()
        {
            _katana = null;
            _uprightDeg = float.NaN;
            if (_cam != null)
            {
                _cam.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(_cam.gameObject);
                _cam = null;
            }
            if (_rt != null) { _rt.Release(); UnityEngine.Object.DestroyImmediate(_rt); _rt = null; }
            if (_mats != null)
                foreach (var pass in _mats)
                    if (pass != null)
                        foreach (var slots in pass)
                            if (slots != null)
                                foreach (var m in slots) if (m != null) UnityEngine.Object.DestroyImmediate(m);
            _mats = null;
            _rends = null;
            foreach (var m in _proxyMeshes) if (m != null) UnityEngine.Object.DestroyImmediate(m);
            _proxyMeshes = new Mesh[0];
            _skinned = new SkinnedMeshRenderer[0];
            _view = null;
        }

        private static void BakeInto(RoninAssets assets, GameObject root)
        {
            _view = RoninView.Create(assets, root.transform);
            _view.AutoTick = false;
            _view.CameraTurn = Actors3DView.RoninCameraTurn;
            _view.FadeTime = 0.05f;
            _view.SnapFacing(1);
            float scale = Measure(_view.transform, 1.6f * Actors3DView.RoninHeightRatio);
            _view.transform.localScale = Vector3.one * scale;
            BuildPassMaterials();
            var hand = _view.Model.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.LeftHand);

            float L(RoninClip c) => assets.Clips[(int)c] != null ? assets.Clips[(int)c].length : 1f;
            var idle = new RoninInput { Grounded = true, Facing = 1 };
            var list = new List<AnimData>();
            // Locomotion, airborne, hurt and death: the clip alone, sampled at exact clip times (no state machine, no
            // crossfade from idle, full length). Loops cover exactly one cycle so frame N-1 -> 0 is a clean seam.
            float idleHips = HipsHeight(RoninClip.Idle, 0f);
            // v3 (ronin_anims.py): procedural katana clips authored at 30 fps. Loops cover exactly one cycle.
            // Idle: 1.33 s breathing cycle, 16 frames at 12 fps.
            // Sword held upright in a normal grip (blade up, leaning forward) in every non-attack pose; the slashes,
            // the slide attack and death keep their own blade (see _uprightDeg / FixKatana).
            _uprightDeg = 18f;
            list.Add(SampleClip("idle", RoninClip.Idle, 16f / L(RoninClip.Idle), true, Uniform(16, 0f, L(RoninClip.Idle), true), 0f, false, idleHips, hand));
            // Run and sprint: one stride cycle each (10 and 8 frames). The presenter plays them at
            // speed x duration / stride (the stride is measured here from the planted foot), so the cloth is simulated
            // in the real time one cycle takes at the speed each represents (run 9 u/s = the sim's run speed, sprint 18).
            _uprightDeg = 35f;
            list.Add(Loco("run", RoninClip.Run, 10, 9f, idleHips, hand));
            _uprightDeg = 55f;
            list.Add(Loco("sprint", RoninClip.Sprint, 8, 18f, idleHips, hand));
            _uprightDeg = 20f;
            // Jump: take-off -> knee drive -> tuck in 0.4 s; the sim supplies the height, so the hips are pinned.
            list.Add(SampleClip("jump", RoninClip.Jump, 20, false, Uniform(8, 0f, Mathf.Min(0.4f, L(RoninClip.Jump)), false), 0f, true, idleHips, hand));
            list.Add(SampleClip("fall", RoninClip.Fall, 12, true, Uniform(12, 0f, L(RoninClip.Fall), true), 0f, true, idleHips, hand));
            // Land: squash -> deepest -> recover (the clip's keys 0, 1 and 3 at 30 fps, never in between), then idle's
            // frame 0 itself, so the hand-off to idle is pixel-identical. 4 frames at 24 fps (the presenter holds it).
            var land = SampleClip("land", RoninClip.Land, 24, false, new[] { 0f, 1f / 30f, 3f / 30f }, 0f, false, idleHips, hand);
            land.Frames.Add(list[0].Frames[0].Clone("land_03"));
            list.Add(land);
            list.Add(Capture("hang", 12, true, Mathf.RoundToInt(L(RoninClip.Hang) * 12), 0f, 0.3f, 0f, t => { var i = idle; i.Grounded = false; i.Hooked = true; return i; }, hand));
            _uprightDeg = float.NaN;
            list.Add(Capture("slide", 20, false, Mathf.Min(16, Mathf.RoundToInt(L(RoninClip.SlideAttack) * 20)), 0f, 0f, 0f, t => { var i = idle; i.Sliding = true; i.Speed = 8f; return i; }, hand));
            // Slashes: 10 frames at 30 fps sampled 1:1 (frame 0 anticipation, 1 mid-swing, 2 impact = 0.067 s after the
            // press, inside the sim's 0.15 s live strike; 3-4 follow-through; 5-9 recover). The 1-2 fastest frames carry
            // a blade smear: a filled crescent swept by the outer blade since the previous frame (see AddSmear).
            var slashTimes = Uniform(10, 0f, 9f / 30f, false);
            const bool smear = true;
            list.Add(SampleClip("attack1", RoninClip.Attack1, 30, false, slashTimes, 0f, false, idleHips, hand, 1f, smear));
            list.Add(SampleClip("attack2", RoninClip.Attack2, 30, false, slashTimes, 0f, false, idleHips, hand, 1f, smear));
            list.Add(SampleClip("jumpattack", RoninClip.JumpAttack, 30, false, slashTimes, 0f, true, idleHips, hand, 1f, smear));
            list.Add(SampleClip("runattack1", RoninClip.RunAttack1, 30, false, slashTimes, 9f, false, idleHips, hand, 1f, smear));
            list.Add(SampleClip("runattack2", RoninClip.RunAttack2, 30, false, slashTimes, 9f, false, idleHips, hand, 1f, smear));
            // Hurt: time-warped so the recoil snaps (clip 0.12 -> 0.45 s, its deepest lean, in 2 frames) then recovers to
            // the clip's end pose over 6 frames; 8 frames at 20 fps = 0.4 s (the presenter takes HurtDuration from this).
            _uprightDeg = 30f;
            list.Add(SampleClip("hurt", RoninClip.Hurt, 20, false, new[] { 0.12f, 0.3f, 0.45f, 0.6f, 0.75f, 0.9f, 1.05f, L(RoninClip.Hurt) }, 0f, false, idleHips, hand));
            // Death: the whole 2.4 s clip (stagger, collapse, lying on the ground) in 22 frames at 14 fps (~1.5x speed);
            // the last frame is the clip's final lying pose and is held by the one-shot playback.
            _uprightDeg = float.NaN;
            list.Add(SampleClip("death", RoninClip.Death, 14, false, Uniform(22, 0f, L(RoninClip.Death), false), 0f, false, idleHips, hand));

            int total = 0, fixedTotal = 0, bladePx = 0, eyePx = 0;
            foreach (var a in list) foreach (var f in a.Frames) { fixedTotal += Clean(f); total++; }
            foreach (var a in list) foreach (var f in a.Frames) foreach (var e in f.Em) { if ((e & 1) != 0) bladePx++; if ((e & 2) != 0) eyePx++; }
            // The blade glow is the tier read: a bake without it is broken, not just different.
            if (bladePx == 0) throw new InvalidOperationException("no blade emissive pixels baked (Ronin_Blade emission)");
            Write(list, scale);
            Debug.Log($"RONIN_BAKE_OK animations={list.Count} frames={total} cleanedPixels={fixedTotal} bladePx={bladePx} eyePx={eyePx} scale={scale:F4}");
        }

        // ---------------------------------------------------------------- capture

        private static void SetupCamera()
        {
            var go = new GameObject("BakeCam");
            _cam = go.AddComponent<Camera>();
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.renderShadows = false;
            _cam.orthographic = true;
            _cam.orthographicSize = CH / (2f * Ppu);
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 60f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _cam.allowHDR = false;
            _cam.allowMSAA = false;
            _cam.enabled = false;
            _rt = new RenderTexture(CW * Ss, CH * Ss, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { antiAliasing = 1, filterMode = FilterMode.Point };
            _cam.targetTexture = _rt;
        }

        private static float Measure(Transform model, float target)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 1f;
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            float h = b.max.y - model.position.y;
            return h > 0.01f ? target / h : 1f;
        }

        private static void BuildPassMaterials()
        {
            var shader = Shader.Find("Hidden/Ronin/Bake");
            if (shader == null) throw new InvalidOperationException("Hidden/Ronin/Bake shader not found");
            // Skinned meshes are rendered through CPU-skinned proxies (BakeMesh every snapshot): a synchronous batch bake
            // renders many poses inside one editor frame, and the GPU-skinned buffers of the SkinnedMeshRenderer do not
            // reliably follow every pose there (v1 sheets: hips and legs stuck while the skeleton ran, fell and jumped).
            var src0 = _view.GetComponentsInChildren<Renderer>(true);
            var rl = new List<Renderer>();
            var sk = new List<SkinnedMeshRenderer>();
            var pm = new List<Mesh>();
            foreach (var r in src0)
            {
                if (r is SkinnedMeshRenderer smr)
                {
                    var go = new GameObject(smr.name + "_cpuSkin");
                    go.transform.SetParent(smr.transform, false);
                    var mesh = new Mesh { name = smr.name + "_cpuSkin", indexFormat = smr.sharedMesh.indexFormat, hideFlags = HideFlags.HideAndDontSave };
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = smr.sharedMaterials;
                    smr.forceRenderingOff = true;
                    sk.Add(smr); pm.Add(mesh); rl.Add(mr);
                }
                else rl.Add(r);
            }
            _skinned = sk.ToArray();
            _proxyMeshes = pm.ToArray();
            _rends = rl.ToArray();
            _mats = new Material[3][][];
            for (int pass = 0; pass < 3; pass++)
            {
                _mats[pass] = new Material[_rends.Length][];
                for (int r = 0; r < _rends.Length; r++)
                {
                    var src = _rends[r].sharedMaterials;
                    var dst = new Material[src.Length];
                    for (int i = 0; i < src.Length; i++) dst[i] = MakeBake(shader, src[i], pass);
                    _mats[pass][r] = dst;
                }
            }
        }

        private static Material MakeBake(Shader shader, Material m, int pass)
        {
            var b = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            b.SetFloat("_Mode", pass);
            if (m == null) return b;
            if (m.HasProperty("_BaseMap"))
            {
                b.SetTexture("_BaseMap", m.GetTexture("_BaseMap"));
                b.SetTextureScale("_BaseMap", m.GetTextureScale("_BaseMap"));
                b.SetTextureOffset("_BaseMap", m.GetTextureOffset("_BaseMap"));
            }
            if (m.HasProperty("_BaseColor")) b.SetColor("_BaseColor", m.GetColor("_BaseColor"));
            bool hasMap = m.HasProperty("_EmissionMap") && m.GetTexture("_EmissionMap") != null;
            if (hasMap) b.SetTexture("_EmissionMap", m.GetTexture("_EmissionMap"));
            string n = m.name;
            if (n.Contains("Blade"))
            {
                b.SetVector("_Chan", new Vector4(1, 0, 0, 0));
                // The shader's map default is black now; a blade without a map glows whole (its in-game emission is a
                // flat HDR colour) instead of silently losing the tier glow.
                if (!hasMap) { b.SetFloat("_EmitFull", 1f); Debug.Log("RONIN_BAKE blade " + n + " has no _EmissionMap: whole surface emissive"); }
            }
            else if (n.Contains("Atlas")) b.SetVector("_Chan", new Vector4(0, 1, 0, 0));
            else if (n.Contains("Rim")) { b.SetVector("_Chan", new Vector4(0, 0, 1, 0)); b.SetFloat("_EmitFull", 1f); }
            return b;
        }

        /// <summary>Puts the bake camera on the character (feet pivot at the cell's PadBottom row, centred).</summary>
        private static void PlaceCamera()
        {
            Vector3 p = _view.transform.position;
            float a = (CH * 0.5f - PadBottom) / Ppu;
            float pitch = Pitch * Mathf.Deg2Rad;
            _cam.transform.rotation = Quaternion.Euler(Pitch, 0f, 0f);
            _cam.transform.position = new Vector3(p.x, p.y + (a + 20f * Mathf.Sin(pitch)) / Mathf.Cos(pitch), -20f);
        }

        private static Color32[] Render(int pass)
        {
            for (int r = 0; r < _rends.Length; r++) _rends[r].sharedMaterials = _mats[pass][r];
            PlaceCamera();
            _cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            var tex = new Texture2D(_rt.width, _rt.height, TextureFormat.RGBA32, false, true);
            tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
            RenderTexture.active = prev;
            var px = tex.GetPixels32();
            UnityEngine.Object.DestroyImmediate(tex);
            return px;
        }

        private static Fr Snapshot(string name, Transform hand)
        {
            FixKatana();
            for (int i = 0; i < _skinned.Length; i++) _skinned[i].BakeMesh(_proxyMeshes[i], true);
            var alb = Render(0);
            var nrm = Render(1);
            var emi = Render(2);
            var f = new Fr { Name = name };
            const int W = CW * Ss;
            for (int y = 0; y < CH; y++)
            for (int x = 0; x < CW; x++)
            {
                int n = 0, nr = 0, ng = 0, nb = 0;
                float cr = 0, cg = 0, cb = 0, vx = 0, vy = 0, vz = 0;
                for (int sy = 0; sy < Ss; sy++)
                for (int sx = 0; sx < Ss; sx++)
                {
                    int i = (y * Ss + sy) * W + x * Ss + sx;
                    var a = alb[i];
                    if (a.a < 128) continue;
                    n++;
                    cr += a.r / 255f; cg += a.g / 255f; cb += a.b / 255f;
                    var v = nrm[i];
                    vx += v.r / 127.5f - 1f; vy += v.g / 127.5f - 1f; vz += v.b / 127.5f - 1f;
                    var e = emi[i];
                    if (e.r > 128) nr++;
                    if (e.g > 128) ng++;
                    if (e.b > 128) nb++;
                }
                int k = y * CW + x;
                byte em = (byte)((nr >= 2 ? 1 : 0) | (ng >= 3 ? 2 : 0) | (nb >= 3 ? 4 : 0));
                bool op = n >= 6 || (em != 0 && n >= 2);
                if (!op) { f.Em[k] = 0; continue; }
                f.Op[k] = true;
                f.Em[k] = em;
                f.Col[k] = new Color32(Enc(cr / n), Enc(cg / n), Enc(cb / n), 255);
                var nv = new Vector3(vx, vy, vz);
                nv = nv.sqrMagnitude > 1e-6f ? nv.normalized : new Vector3(0, 0, 1);
                f.Nrm[k] = new Color32((byte)Mathf.RoundToInt((nv.x * 0.5f + 0.5f) * 255f), (byte)Mathf.RoundToInt((nv.y * 0.5f + 0.5f) * 255f),
                    (byte)Mathf.RoundToInt((nv.z * 0.5f + 0.5f) * 255f), 255);
            }
            if (hand != null)
            {
                var m = _cam.worldToCameraMatrix;
                var d = m.MultiplyPoint3x4(hand.position) - m.MultiplyPoint3x4(_view.transform.position);
                f.Hand = new Vector2(d.x, d.y);
            }
            return f;
        }

        private static byte Enc(float linear) => (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.LinearToGammaSpace(Mathf.Clamp01(linear)) * 255f), 0, 255);

        private static void Settle(float seconds)
        {
            var idle = new RoninInput { Grounded = true, Facing = 1 };
            for (float t = 0; t < seconds; t += Step) { _view.Input = idle; _view.Tick(Step); }
        }

        private static AnimData Capture(string name, float fps, bool loop, int frames, float unused, float warm, float speed,
            Func<float, RoninInput> input, Transform hand, RoninClip? expect = null)
        {
            frames = Mathf.Max(frames, 2);
            _view.transform.position = Vector3.zero;
            _view.ResetSprings();
            _view.SnapFacing(1);
            Settle(0.6f);
            var data = new AnimData { Name = name, Fps = fps, Loop = loop };
            int k = 0;
            for (float t = -warm; k < frames; t += Step)
            {
                var inp = input(t);
                if (inp.Attacking && t >= 0f && t < Step * 1.5f) _presses++;
                _view.Input = inp;
                _view.transform.position += new Vector3(speed * Step, 0f, 0f);
                _view.Tick(Step);
                while (k < frames && t >= k / fps - 1e-4f)
                {
                    data.Frames.Add(Snapshot(name + "_" + k.ToString("00"), hand));
                    k++;
                }
            }
            Debug.Log($"RONIN_BAKE anim={name} frames={frames} fps={fps}");
            return data;
        }

        /// <summary><paramref name="n"/> clip times from <paramref name="t0"/> to <paramref name="t1"/>; a loop stops one step short of t1 (= t0).</summary>
        private static float[] Uniform(int n, float t0, float t1, bool loop)
        {
            var t = new float[n];
            for (int i = 0; i < n; i++) t[i] = Mathf.Lerp(t0, t1, loop ? i / (float)n : i / (float)Mathf.Max(1, n - 1));
            return t;
        }

        private static Transform Bone(HumanBodyBones b) => _view.Model.GetComponent<Animator>().GetBoneTransform(b);

        /// <summary>Hips height above the feet pivot (world units, model at its baked scale) of <paramref name="clip"/> at <paramref name="time"/>.</summary>
        private static float HipsHeight(RoninClip clip, float time)
        {
            _view.Model.localPosition = Vector3.zero;
            _view.SamplePose(clip, time);
            return Bone(HumanBodyBones.Hips).position.y - _view.transform.position.y;
        }

        /// <summary>
        /// Bakes <paramref name="clip"/> by sampling it directly at <paramref name="times"/> (clip seconds), one frame each,
        /// played back at <paramref name="fps"/>. The spring bones run at 120 Hz in between, against a body moving at
        /// <paramref name="speed"/> (wind on the coat); clip time is interpolated across the sub-steps so time-warped
        /// sequences drive the cloth smoothly. Loops pre-roll one whole cycle first so the cloth is already periodic at
        /// frame 0 (clean seam). <paramref name="pinHips"/>: hold the hips at <paramref name="hipsY"/> (airborne poses; the
        /// sim owns the height, so a clip's own rise would double it and float the sprite off its pivot).
        /// </summary>
        private static AnimData SampleClip(string name, RoninClip clip, float fps, bool loop, float[] times, float speed, bool pinHips, float hipsY, Transform hand,
            float clothTime = 1f, bool smear = false)
        {
            var data = new AnimData { Name = name, Fps = fps, Loop = loop };
            float len = _view.ClipLength(clip);
            _view.transform.position = Vector3.zero;
            _view.Model.localPosition = Vector3.zero;
            float scale = _view.transform.localScale.y;
            void Pose(float t)
            {
                if (loop && len > 0f) t = Mathf.Repeat(t, len);
                _view.Model.localPosition = Vector3.zero;
                _view.SamplePose(clip, Mathf.Clamp(t, 0f, len));
                if (pinHips)
                {
                    float dy = Bone(HumanBodyBones.Hips).position.y - _view.transform.position.y - hipsY;
                    _view.Model.localPosition = new Vector3(0f, -dy / scale, 0f);
                }
                FixKatana();
            }
            void Advance(float from, float to, float seconds)
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(seconds / Step));
                for (int s = 1; s <= n; s++)
                {
                    Pose(Mathf.Lerp(from, to, s / (float)n));
                    _view.transform.position += new Vector3(speed * seconds * clothTime / n, 0f, 0f);
                    _view.SimulateCloth(seconds * clothTime / n);
                }
            }
            // Blade tip and guard in cell pixels per frame (diagnostic angles; smear frame choice).
            var tips = new Vector2[times.Length];
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < times.Length; k++)
            {
                Pose(times[k]);
                BladePx(out var g, out tips[k]);
                var d = tips[k] - g;
                sb.Append(Mathf.RoundToInt(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg)).Append(' ');
            }
            Debug.Log($"RONIN_BAKE blade anim={name} angles(deg, 0=forward 90=up)= {sb}");
            var smearFrames = smear ? PickSmearFrames(tips) : new List<int>();
            // Cloth: settle on the first pose, then (loops) run one full cycle so frame 0 continues from frame N-1.
            Pose(times[0]);
            _view.ResetSprings();
            for (int i = 0; i < 72; i++) { _view.transform.position += new Vector3(speed * Step, 0f, 0f); _view.SimulateCloth(Step); }
            if (loop)
                for (int i = 0; i < times.Length; i++)
                {
                    float a = times[i], b = i + 1 < times.Length ? times[i + 1] : times[0] + len;
                    Advance(a, b, 1f / fps);
                }
            Pose(times[0]);
            for (int k = 0; k < times.Length; k++)
            {
                if (k > 0) Advance(times[k - 1], times[k], 1f / fps);
                var fr = Snapshot(name + "_" + k.ToString("00"), hand);
                if (smearFrames.Contains(k))
                {
                    AddSmear(fr, times[k - 1], times[k], Pose);
                    Pose(times[k]);
                    _view.SimulateCloth(0f);
                }
                data.Frames.Add(fr);
            }
            _view.Model.localPosition = Vector3.zero;
            var lf = Bone(HumanBodyBones.LeftFoot);
            Debug.Log($"RONIN_BAKE anim={name} clip={clip} frames={times.Length} fps={fps} loop={loop} clipSpan={times[0]:F2}-{times[times.Length - 1]:F2}/{len:F2} lastLeftFootY={lf.position.y - _view.transform.position.y:F2}");
            return data;
        }

        // ---------------------------------------------------------------- blade smear

        /// <summary>Smear sub-steps between two frames (the swept band is filled between consecutive steps).</summary>
        private const int SmearSteps = 24;
        /// <summary>Frames whose tip moved less than this (cell px) since the previous frame get no smear.</summary>
        private const float SmearMinTravel = 10f;
        /// <summary>A frame is "fast" when its tip travel is at least this share of the clip's fastest frame.</summary>
        private const float SmearFastShare = 0.55f;
        /// <summary>Last frame that may carry a smear (the slashes impact at frame 2; 3 is the first follow-through).</summary>
        private const int SmearLastFrame = 3;
        /// <summary>Guard-to-tip fraction where the band's inner edge sits: trailing end (thin tail) and leading end (full).</summary>
        private const float SmearInnerTrail = 0.8f, SmearInnerLead = 0.3f;
        /// <summary>Quantised intensities of the band, trailing to leading (pixel-art steps, not a gradient).</summary>
        private static readonly float[] SmearLevels = { 0.15f, 0.25f, 0.4f, 0.6f };   // the blade itself (1.0) is the leading edge

        private static Transform _katana;
        private static Vector3 _guardLocal, _tipLocal, _pommelLocal;
        private static Vector3 _katanaRestPos;
        private static Quaternion _katanaRestRot;

        /// <summary>
        /// Blade angle for the clip being baked, in degrees from straight up toward the facing direction (the sword held
        /// upright in a normal grip); NaN = the clip's own blade (slashes, slide, death). Applied by <see cref="FixKatana"/>.
        /// </summary>
        private static float _uprightDeg = float.NaN;

        /// <summary>
        /// Puts the katana back on the hand at its rest offset, then, for upright clips, turns it about the grip so the
        /// blade points <see cref="_uprightDeg"/> from vertical toward the facing side (the hand and arm are untouched).
        /// Idempotent; call after every pose and before every snapshot.
        /// </summary>
        private static void FixKatana()
        {
            if (_katana == null && !InitKatana()) return;
            _katana.localPosition = _katanaRestPos;
            _katana.localRotation = _katanaRestRot;
            if (float.IsNaN(_uprightDeg)) return;
            // The grip is the point of the handle (pommel..guard) nearest the right palm; after the turn it is put
            // back exactly in the palm, so the hand closes round the handle (no floating sword).
            Vector3 palm = Palm();
            Vector3 p = _katana.TransformPoint(_pommelLocal), gd = _katana.TransformPoint(_guardLocal);
            Vector3 ax = gd - p;
            float t = ax.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(palm - p, ax) / ax.sqrMagnitude) : 0.5f;
            t = Mathf.Clamp(t, 0.25f, 0.75f);   // hold the handle, not the pommel cap or the guard
            Vector3 gripLocal = Vector3.Lerp(_pommelLocal, _guardLocal, t);
            Vector3 cur = _katana.TransformDirection(_tipLocal - _guardLocal).normalized;
            float a = _uprightDeg * Mathf.Deg2Rad;
            Vector3 target = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f);   // baked facing +x
            _katana.rotation = Quaternion.FromToRotation(cur, target) * _katana.rotation;
            _katana.position += palm - _katana.TransformPoint(gripLocal);
        }

        /// <summary>Centre of the right (sword) palm: from the wrist toward the middle-finger base when the rig has it.</summary>
        private static Vector3 Palm()
        {
            var hand = Bone(HumanBodyBones.RightHand);
            if (hand == null) return _katana.parent != null ? _katana.parent.position : _katana.position;
            var mid = Bone(HumanBodyBones.RightMiddleProximal) ?? Bone(HumanBodyBones.RightIndexProximal);
            return mid != null ? Vector3.Lerp(hand.position, mid.position, 0.6f) : hand.position;
        }

        /// <summary>Guard and tip of the katana in local space (longest mesh-bounds axis; the end nearer the hand is
        /// the pommel, the guard ~29% of the length from it; same rule as <see cref="BladeTrail"/>).</summary>
        private static bool InitKatana()
        {
            _katana = _view.Katana;
            var mf = _katana != null ? _katana.GetComponent<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null) { _katana = null; return false; }
            Bounds b = mf.sharedMesh.bounds;
            Vector3 e = b.extents;
            Vector3 axis = e.x >= e.y && e.x >= e.z ? Vector3.right : e.y >= e.z ? Vector3.up : Vector3.forward;
            float half = Vector3.Dot(e, axis);
            Vector3 a = b.center - axis * half, c = b.center + axis * half;
            Vector3 hand = _katana.parent != null ? _katana.InverseTransformPoint(_katana.parent.position) : Vector3.zero;
            Vector3 pommel = (a - hand).sqrMagnitude < (c - hand).sqrMagnitude ? a : c;
            _tipLocal = pommel == a ? c : a;
            _pommelLocal = pommel;
            _guardLocal = Vector3.Lerp(pommel, _tipLocal, 0.29f);
            _katanaRestPos = _katana.localPosition;
            _katanaRestRot = _katana.localRotation;
            return true;
        }

        /// <summary>Katana guard and tip in cell pixels (x right, y up from the cell bottom) for the current pose.</summary>
        private static void BladePx(out Vector2 guard, out Vector2 tip)
        {
            guard = tip = Vector2.zero;
            if (_katana == null && !InitKatana()) return;
            PlaceCamera();
            Vector3 g = _cam.WorldToViewportPoint(_katana.TransformPoint(_guardLocal));
            Vector3 t = _cam.WorldToViewportPoint(_katana.TransformPoint(_tipLocal));
            guard = new Vector2(g.x * CW, g.y * CH);
            tip = new Vector2(t.x * CW, t.y * CH);
        }

        /// <summary>The (at most two) fastest frames of a slash by tip travel since the previous frame.</summary>
        private static List<int> PickSmearFrames(Vector2[] tips)
        {
            var travel = new float[tips.Length];
            float max = 0f;
            for (int k = 1; k < tips.Length; k++) { travel[k] = (tips[k] - tips[k - 1]).magnitude; max = Mathf.Max(max, travel[k]); }
            var cand = new List<int>();
            // Only the strike itself (impact at frame 2): the recover swings back fast too, but a smear there reads as a second cut.
            int last = Mathf.Min(tips.Length - 1, SmearLastFrame);
            for (int k = 1; k <= last; k++) if (travel[k] >= SmearMinTravel && travel[k] >= SmearFastShare * max) cand.Add(k);
            cand.Sort((a, b) => travel[b].CompareTo(travel[a]));
            if (cand.Count > 2) cand.RemoveRange(2, cand.Count - 2);
            Debug.Log($"RONIN_BAKE smear frames=[{string.Join(",", cand)}] maxTravel={max:F1}px");
            return cand;
        }

        /// <summary>
        /// Fills the crescent the outer blade swept from <paramref name="t0"/> to <paramref name="t1"/> into
        /// <paramref name="f"/>.Smear: quads between consecutive sub-steps, inner edge moving from
        /// <see cref="SmearInnerTrail"/> (thin tail) to <see cref="SmearInnerLead"/> (full width at the blade), intensity
        /// stepped from dim at the trailing edge to full at the leading edge. Lines thinner than 2 texels are thickened.
        /// </summary>
        private static void AddSmear(Fr f, float t0, float t1, Action<float> pose)
        {
            var inner = new Vector2[SmearSteps + 1];
            var outer = new Vector2[SmearSteps + 1];
            for (int s = 0; s <= SmearSteps; s++)
            {
                float u = s / (float)SmearSteps;
                pose(Mathf.Lerp(t0, t1, u));
                BladePx(out var g, out var t);
                inner[s] = Vector2.Lerp(g, t, Mathf.Lerp(SmearInnerTrail, SmearInnerLead, u * u * (3f - 2f * u)));
                outer[s] = t;
            }
            var sm = new float[CW * CH];
            for (int s = 0; s < SmearSteps; s++)
            {
                float um = (s + 0.5f) / SmearSteps;
                float v = SmearLevels[Mathf.Min(SmearLevels.Length - 1, (int)(um * um * SmearLevels.Length))];
                FillTri(sm, inner[s], outer[s], outer[s + 1], v);
                FillTri(sm, inner[s], outer[s + 1], inner[s + 1], v);
            }
            // >= 2 texels thick everywhere (a 1-texel sliver at the tail would shimmer).
            var src = (float[])sm.Clone();
            for (int y = 0; y < CH; y++)
            for (int x = 0; x < CW; x++)
            {
                int k = y * CW + x;
                if (src[k] <= 0f) continue;
                bool h = x > 0 && src[k - 1] > 0f || x + 1 < CW && src[k + 1] > 0f;
                bool vN = y > 0 && src[k - CW] > 0f || y + 1 < CH && src[k + CW] > 0f;
                if (!h && x + 1 < CW) sm[k + 1] = Mathf.Max(sm[k + 1], src[k]);
                if (!vN && y + 1 < CH) sm[k + CW] = Mathf.Max(sm[k + CW], src[k]);
            }
            int n = 0;
            for (int k = 0; k < sm.Length; k++)
            {
                if ((f.Em[k] & 1) != 0) sm[k] = 0f;     // the blade itself stays the solid leading edge
                if (sm[k] > 0f) n++;
            }
            f.Smear = sm;
            Debug.Log($"RONIN_BAKE smear {f.Name} texels={n}");
        }

        private static void FillTri(float[] dst, Vector2 a, Vector2 b, Vector2 c, float v)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
            int x1 = Mathf.Min(CW - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
            int y1 = Mathf.Min(CH - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
            float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            if (Mathf.Abs(area) < 1e-4f) return;
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float w0 = ((b.x - p.x) * (c.y - p.y) - (b.y - p.y) * (c.x - p.x)) / area;
                float w1 = ((c.x - p.x) * (a.y - p.y) - (c.y - p.y) * (a.x - p.x)) / area;
                float w2 = 1f - w0 - w1;
                const float e = -1e-3f;
                if (w0 < e || w1 < e || w2 < e) continue;
                int k = y * CW + x;
                dst[k] = Mathf.Max(dst[k], v);
            }
        }

        /// <summary>
        /// A locomotion loop: measures the clip's no-skate stride (world units the body travels per cycle when the
        /// planted foot does not slide on screen: the planted foot's mean screen-x speed relative to the pivot x clip
        /// length), then bakes <paramref name="frames"/> frames over one cycle with the cloth run in the real time a
        /// cycle lasts at <paramref name="refSpeed"/>.
        /// </summary>
        private static AnimData Loco(string name, RoninClip clip, int frames, float refSpeed, float hipsY, Transform hand)
        {
            float len = _view.ClipLength(clip);
            float stride = MeasureStride(clip, len);
            float rate = stride > 0f ? refSpeed * len / stride : 1f;
            var data = SampleClip(name, clip, frames / len, true, Uniform(frames, 0f, len, true), refSpeed, false, hipsY, hand, 1f / rate);
            data.Stride = stride;
            Debug.Log($"RONIN_BAKE stride anim={name} len={len:F3} stride={stride:F3}u rate@{refSpeed}={rate:F2} cyclesPerSec={refSpeed / Mathf.Max(stride, 1e-3f):F2}");
            return data;
        }

        private static float MeasureStride(RoninClip clip, float len)
        {
            const int N = 240;
            var lf = Bone(HumanBodyBones.LeftToes) ?? Bone(HumanBodyBones.LeftFoot);
            var rf = Bone(HumanBodyBones.RightToes) ?? Bone(HumanBodyBones.RightFoot);
            _view.transform.position = Vector3.zero;
            _view.Model.localPosition = Vector3.zero;
            var lx = new float[N + 1]; var ly = new float[N + 1]; var rx = new float[N + 1]; var ry = new float[N + 1];
            float minY = float.MaxValue;
            for (int i = 0; i <= N; i++)
            {
                _view.SamplePose(clip, len * i / N);
                lx[i] = lf.position.x; ly[i] = lf.position.y; rx[i] = rf.position.x; ry[i] = rf.position.y;
                minY = Mathf.Min(minY, Mathf.Min(ly[i], ry[i]));
            }
            float sum = 0f; int n = 0;
            float dt = len / N, band = 0.02f;
            for (int i = 0; i < N; i++)
            {
                bool left = ly[i] <= ry[i];
                float y = left ? ly[i] : ry[i];
                if (y > minY + band) continue;
                float v = -((left ? lx[i + 1] - lx[i] : rx[i + 1] - rx[i]) / dt);   // planted foot slides back relative to the pivot
                if (v <= 0f) continue;
                sum += v; n++;
            }
            return n > 0 ? sum / n * len : 0f;
        }

        /// <summary>wantAttack1: 1 = Attack1 (even press count after), 0 = Attack2, -1 = any (air).</summary>
        private static AnimData CaptureAttack(string name, int frames, int wantAttack1, bool air, bool running, RoninInput idle, Transform hand)
        {
            // Resolve flips its combo bit on every press: after n presses Attack1 plays when n is even.
            if (wantAttack1 >= 0)
            {
                bool nextIsAttack1 = ((_presses + 1) & 1) == 0;
                if (nextIsAttack1 != (wantAttack1 == 1))
                {
                    var dummy = Capture("dummy", 24, false, 2, 0f, 0f, 0f, t => { var i = idle; i.Attacking = t < 0.1f; i.StrikeId = _presses; return i; }, hand);
                    Settle(0.6f);
                }
            }
            int strike = _presses + 10;
            return Capture(name, 24, false, frames, 0f, running ? 1.5f : 0f, running ? 6f : 0f, t =>
            {
                var i = idle;
                i.Speed = running ? 6f : 0f;
                i.Grounded = !air;
                i.VelocityY = air ? 2f : 0f;
                i.Attacking = t >= 0f && t < 0.1f;
                i.StrikeId = strike;
                return i;
            }, hand);
        }

        // ---------------------------------------------------------------- clean-up

        private static readonly int[] Dx8 = { -1, 0, 1, -1, 1, -1, 0, 1 }, Dy8 = { -1, -1, -1, 0, 0, 1, 1, 1 };

        private static int Clean(Fr f)
        {
            int changed = 0;
            changed += Despeckle(f);
            changed += Emissive(f);
            // Art bible section 5: the hat brim does not glow. Its (thickened, >= 2 px) band becomes a dark trim in the albedo.
            for (int k = 0; k < f.Em.Length; k++)
            {
                if ((f.Em[k] & 4) == 0) continue;
                f.Col[k] = BrimTrim;
                f.Em[k] &= unchecked((byte)~4);
                changed++;
            }
            return changed;
        }

        /// <summary>Albedo of the hat-brim edge trim (dark, non-emissive).</summary>
        private static readonly Color32 BrimTrim = new Color32(46, 44, 54, 255);

        private static bool In(int x, int y) => x >= 0 && y >= 0 && x < CW && y < CH;

        private static int Despeckle(Fr f)
        {
            int changed = 0;
            var op = (bool[])f.Op.Clone();
            var col = (Color32[])f.Col.Clone();
            for (int y = 0; y < CH; y++)
            for (int x = 0; x < CW; x++)
            {
                int k = y * CW + x;
                if (!op[k] || f.Em[k] != 0) continue;
                int nOp = 0, similar = 0, r = 0, g = 0, b = 0;
                for (int d = 0; d < 8; d++)
                {
                    int nx = x + Dx8[d], ny = y + Dy8[d];
                    if (!In(nx, ny)) continue;
                    int j = ny * CW + nx;
                    if (!op[j]) continue;
                    nOp++;
                    r += col[j].r; g += col[j].g; b += col[j].b;
                    if (Mathf.Abs(col[j].r - col[k].r) + Mathf.Abs(col[j].g - col[k].g) + Mathf.Abs(col[j].b - col[k].b) < 48) similar++;
                }
                if (nOp <= 1) { f.Op[k] = false; changed++; }
                else if (nOp >= 3 && similar == 0) { f.Col[k] = new Color32((byte)(r / nOp), (byte)(g / nOp), (byte)(b / nOp), 255); changed++; }
            }
            return changed;
        }

        private static int Emissive(Fr f)
        {
            int changed = 0;
            for (int bit = 1; bit <= 4; bit <<= 1)
            {
                var seen = new bool[CW * CH];
                var comps = new List<List<int>>();
                for (int s = 0; s < CW * CH; s++)
                {
                    if (seen[s] || (f.Em[s] & bit) == 0) continue;
                    var comp = new List<int>();
                    var stack = new Stack<int>();
                    stack.Push(s); seen[s] = true;
                    while (stack.Count > 0)
                    {
                        int k = stack.Pop(); comp.Add(k);
                        int x = k % CW, y = k / CW;
                        for (int d = 0; d < 8; d++)
                        {
                            int nx = x + Dx8[d], ny = y + Dy8[d];
                            if (!In(nx, ny)) continue;
                            int j = ny * CW + nx;
                            if (!seen[j] && (f.Em[j] & bit) != 0) { seen[j] = true; stack.Push(j); }
                        }
                    }
                    comps.Add(comp);
                }
                foreach (var comp in comps)
                {
                    int minX = CW, maxX = 0, minY = CH, maxY = 0;
                    foreach (int k in comp) { int x = k % CW, y = k / CW; minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
                    int w = maxX - minX + 1, h = maxY - minY + 1;
                    bool thin = w < 2 || h < 2;
                    if (bit == 2)
                    {
                        if (comp.Count <= 3) { GrowBlock(f, comp, bit, minX, minY); changed += 2; }       // eye dot -> 2x2
                        else if (thin) { foreach (int k in comp) f.Em[k] &= unchecked((byte)~bit); changed += comp.Count; } // 1-px cyber line -> base colour
                    }
                    else if (thin || comp.Count < 4) { ThickenLine(f, comp, bit); changed += comp.Count; }
                }
                // Lines that are 1 px wide in places (blade, hat band): thicken to 2 px.
                if (bit != 2)
                {
                    var src = (byte[])f.Em.Clone();
                    var list = new List<int>();
                    for (int k = 0; k < src.Length; k++) if ((src[k] & bit) != 0) list.Add(k);
                    foreach (int k in list)
                    {
                        int x = k % CW, y = k / CW;
                        bool hN = In(x - 1, y) && (src[k - 1] & bit) != 0 || In(x + 1, y) && (src[k + 1] & bit) != 0;
                        bool vN = In(x, y - 1) && (src[k - CW] & bit) != 0 || In(x, y + 1) && (src[k + CW] & bit) != 0;
                        if (hN && vN) continue;
                        if (!hN) Grow(f, k, x + 1, y, bit);
                        if (!vN) Grow(f, k, x, y + 1, bit);
                        if (!hN && !vN) Grow(f, k, x + 1, y + 1, bit);
                    }
                }
            }
            return changed;
        }

        private static void GrowBlock(Fr f, List<int> comp, int bit, int minX, int minY)
        {
            int src = comp[0];
            for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++) Grow(f, src, minX + dx, minY + dy, bit);
        }

        private static void ThickenLine(Fr f, List<int> comp, int bit)
        {
            foreach (int k in comp) { int x = k % CW, y = k / CW; Grow(f, k, x + 1, y, bit); Grow(f, k, x, y + 1, bit); Grow(f, k, x + 1, y + 1, bit); }
        }

        private static void Grow(Fr f, int from, int x, int y, int bit)
        {
            if (!In(x, y)) return;
            int t = y * CW + x;
            if (!f.Op[t]) { f.Op[t] = true; f.Col[t] = f.Col[from]; f.Nrm[t] = f.Nrm[from]; }
            f.Em[t] |= (byte)bit;
        }

        // ---------------------------------------------------------------- output

        [Serializable] private sealed class WH { public int w; public int h; }
        [Serializable] private sealed class RectO { public int x; public int y; public int w; public int h; }
        [Serializable] private sealed class PivO { public float x; public float y; }
        [Serializable] private sealed class HandO { public float x; public float y; }
        [Serializable] private sealed class FrameO { public string name; public RectO rect; public PivO pivot; public HandO hand; }
        [Serializable] private sealed class AnimO { public string name; public float fps; public bool loop; public float stride; public string[] frames; }
        [Serializable] private sealed class TexO { public string color = "ronin_color.png"; public string normal = "ronin_normal.png"; public string emissive = "ronin_emissive.png"; }
        [Serializable]
        private sealed class SheetO
        {
            public string name = "ronin"; public float ppu = Ppu; public WH cell = new WH { w = CW, h = CH }; public WH atlas; public int padding = 2;
            public string pack_note = "frames are trimmed to their opaque bounds and shelf-packed; cell = the bake cell before trimming; pivot = feet point of the cell";
            public string rect_origin = "bottom-left (Unity sprite rect convention); pivot normalised within the rect";
            public string pivot_note = "feet centre (10 px above the untrimmed cell bottom), normalised within each trimmed rect; sprites face right (flip for left)";
            public string hand_note = "frames[].hand = left hand offset from the pivot in world units, unflipped";
            public string stride_note = "animations[].stride = world units the body travels per cycle at 1x with the planted foot not skating (run, sprint; 0 = none)";
            public string emissive_encoding = "R = katana blade (tier), G = eye dots, B = unused (hat brim is a dark albedo trim)";
            public string normal_encoding = "view-space normal: R=+X right, G=+Y up, B=toward camera; transparent texels (128,128,255,0)";
            public TexO textures = new TexO();
            public AnimO[] animations; public FrameO[] frames;
        }

        /// <summary>Opaque bounds of a frame in cell pixels (x0, y0 inclusive, w, h); 1x1 at the pivot if empty.</summary>
        private static RectInt Trim(Fr f)
        {
            int x0 = CW, y0 = CH, x1 = -1, y1 = -1;
            for (int y = 0; y < CH; y++)
            for (int x = 0; x < CW; x++)
            {
                int i = y * CW + x;
                if (!f.Op[i] && (f.Smear == null || f.Smear[i] <= 0f)) continue;
                if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
            }
            return x1 < 0 ? new RectInt(CW / 2, PadBottom, 1, 1) : new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        }

        /// <summary>
        /// Shelf-packs the trimmed frames (tallest first) into an atlas <see cref="AtlasWidth"/> wide, each with a 1-px
        /// transparent border (2 px between neighbours, as before). Returns each frame's rect origin in the atlas
        /// (bottom-left convention) and the atlas height.
        /// </summary>
        private static Vector2Int[] Pack(RectInt[] trims, out int height)
        {
            var order = new int[trims.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) => trims[b].height != trims[a].height ? trims[b].height.CompareTo(trims[a].height) : a.CompareTo(b));
            var at = new Vector2Int[trims.Length];
            int x = 0, y = 0, shelf = 0;   // y grows downward from the atlas top while packing
            foreach (int i in order)
            {
                int w = trims[i].width + 2, h = trims[i].height + 2;
                if (x + w > AtlasWidth) { x = 0; y += shelf; shelf = 0; }
                at[i] = new Vector2Int(x + 1, y + 1);
                x += w;
                shelf = Mathf.Max(shelf, h);
            }
            height = y + shelf;
            for (int i = 0; i < at.Length; i++) at[i].y = height - at[i].y - trims[i].height;   // flip to bottom-left
            return at;
        }

        private static void Write(List<AnimData> list, float scale)
        {
            var all = new List<Fr>();
            foreach (var a in list) all.AddRange(a.Frames);
            var trims = new RectInt[all.Count];
            for (int i = 0; i < trims.Length; i++) trims[i] = Trim(all[i]);
            var origin = Pack(trims, out int ah);
            int aw = AtlasWidth;
            var color = new Color32[aw * ah];
            var normal = new Color32[aw * ah];
            var emissive = new Color32[aw * ah];
            for (int i = 0; i < normal.Length; i++) normal[i] = new Color32(128, 128, 255, 0);
            var sheet = new SheetO { atlas = new WH { w = aw, h = ah } };
            var frames = new List<FrameO>();
            var anims = new List<AnimO>();
            int n = 0;
            foreach (var a in list)
            {
                var names = new List<string>();
                foreach (var f in a.Frames)
                {
                    var tr = trims[n];
                    int ox = origin[n].x, oy = origin[n].y;
                    for (int y = 0; y < tr.height; y++)
                    for (int x = 0; x < tr.width; x++)
                    {
                        int k = (tr.y + y) * CW + tr.x + x, t = (oy + y) * aw + ox + x;
                        float sm = f.Smear != null ? f.Smear[k] : 0f;
                        if (!f.Op[k] && sm <= 0f) continue;
                        // Smear texels outside the body are emissive only (transparent in colour and normal).
                        byte r = (byte)((f.Em[k] & 1) != 0 ? 255 : Mathf.RoundToInt(Mathf.Clamp01(sm) * 255f));
                        emissive[t] = new Color32(r, (byte)((f.Em[k] & 2) != 0 ? 255 : 0), (byte)((f.Em[k] & 4) != 0 ? 255 : 0), 255);
                        if (!f.Op[k]) continue;
                        color[t] = f.Col[k];
                        normal[t] = f.Nrm[k];
                    }
                    frames.Add(new FrameO
                    {
                        // Pivot stays the feet point of the cell (CW / 2, PadBottom), re-expressed in the trimmed rect: a whole
                        // number of pixels from the rect corner, so the quad still lands on the texel grid.
                        name = f.Name, rect = new RectO { x = ox, y = oy, w = tr.width, h = tr.height },
                        pivot = new PivO { x = (CW / 2 - tr.x) / (float)tr.width, y = (PadBottom - tr.y) / (float)tr.height },
                        hand = new HandO { x = f.Hand.x, y = f.Hand.y }
                    });
                    names.Add(f.Name);
                    n++;
                }
                anims.Add(new AnimO { name = a.Name, fps = a.Fps, loop = a.Loop, stride = a.Stride, frames = names.ToArray() });
            }
            sheet.animations = anims.ToArray();
            sheet.frames = frames.ToArray();
            Directory.CreateDirectory(OutDir);
            WritePng(OutDir + "ronin_color.png", color, aw, ah);
            WritePng(OutDir + "ronin_normal.png", normal, aw, ah);
            WritePng(OutDir + "ronin_emissive.png", emissive, aw, ah);
            File.WriteAllText(OutDir + "ronin.json", JsonUtility.ToJson(sheet, true));
            long bytes = 3L * aw * ah * 4;
            Debug.Log($"RONIN_BAKE atlas={aw}x{ah} frames={all.Count} rgba32x3={bytes / 1048576f:F1}MiB");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            // Preview: colour over a dark ground, emissive masks tinted, 2x.
            Directory.CreateDirectory(EvidenceDir);
            var prev = new Color32[aw * ah];
            for (int i = 0; i < prev.Length; i++)
            {
                Color32 bg = new Color32(24, 26, 34, 255);
                var sme = emissive[i];
                if (sme.r > 0 && sme.r < 255)   // smear: tier colour at its intensity over the body or the ground
                {
                    var under = color[i].a > 0 ? color[i] : bg;
                    prev[i] = Color32.Lerp(under, new Color32(120, 200, 255, 255), sme.r / 255f);
                    continue;
                }
                if (color[i].a > 0)
                {
                    var c = color[i];
                    var e = emissive[i];
                    if (e.r > 0) c = new Color32(120, 200, 255, 255);
                    else if (e.g > 0) c = new Color32(255, 60, 70, 255);
                    else if (e.b > 0) c = new Color32(255, 230, 160, 255);
                    prev[i] = c;
                }
                else prev[i] = bg;
            }
            var big = new Color32[aw * 2 * ah * 2];
            for (int y = 0; y < ah * 2; y++) for (int x = 0; x < aw * 2; x++) big[y * aw * 2 + x] = prev[(y / 2) * aw + x / 2];
            WritePng(EvidenceDir + "ronin-baked-sheet.png", big, aw * 2, ah * 2);
        }

        private static void WritePng(string path, Color32[] px, int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.SetPixels32(px);
            File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
        }
    }
}
