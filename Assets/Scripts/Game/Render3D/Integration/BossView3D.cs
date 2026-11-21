using System;
using System.Collections.Generic;
using Redline.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Redline.Game.Render3D.Integration
{
    /// <summary>
    /// Big Tentacle Bill in 3D (<see cref="StoryBoss"/>): a huge dark violet body of lumpy flesh with one great red eye
    /// and a fanged maw, seven segmented tentacles with pale suckers that follow the sim's tentacle points (they flash
    /// when hit and leave a stump when cut off), the mud clumps it spits, and the mud shockwave of its slam. Built from
    /// Unity's sphere and capsule meshes; the pixel camera turns it into chunky pixel art like the rest of the world.
    /// </summary>
    /// <example><code>
    /// var view = new BossView3D(arenaHost, sim.Boss);   // story map with a boss room
    /// view.Sync(sim, Time.time);                         // every frame
    /// view.Dispose();
    /// </code></example>
    public sealed class BossView3D : IDisposable
    {
        private const int Segments = BossTentacle.PointCount - 1;

        private readonly Transform _root;
        private readonly Transform _body;
        private readonly Transform _eye;
        private readonly Transform _pupil;
        private readonly Renderer[] _bodyRenderers;
        private readonly Transform[,] _segs;
        private readonly Renderer[,] _segRenderers;
        private readonly Transform[,] _suckers;
        private readonly Transform[] _stumps;
        private readonly List<Transform> _mud = new List<Transform>();
        private readonly List<Transform> _seal = new List<Transform>();
        private readonly Material _sealMat;
        private readonly Transform[] _waves = new Transform[2];
        private readonly List<Object> _owned = new List<Object>();
        private readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        private readonly Material _mudMat;
        private readonly Mesh _sphere, _capsule;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color Flesh = new Color(0.2f, 0.07f, 0.17f);
        private static readonly Color FleshLight = new Color(0.33f, 0.13f, 0.27f);
        private static readonly Color HitTint = new Color(0.95f, 0.35f, 0.3f);

        public BossView3D(Transform parent, StoryBoss boss)
        {
            _root = new GameObject("BigTentacleBill").transform;
            _root.SetParent(parent, false);
            _sphere = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
            _capsule = Resources.GetBuiltinResource<Mesh>("New-Capsule.fbx");

            var flesh = Own(Proto3DMaterials.Lit3D("billFlesh", Flesh, 0.45f, 0f));
            var fleshLight = Own(Proto3DMaterials.Lit3D("billFleshLight", FleshLight, 0.5f, 0f));
            var sucker = Own(Proto3DMaterials.Lit3D("billSucker", new Color(0.62f, 0.4f, 0.45f), 0.6f, 0f));
            var white = Own(Proto3DMaterials.Lit3D("billEyeWhite", new Color(0.85f, 0.8f, 0.7f), 0.8f, 0f));
            var iris = Own(Proto3DMaterials.Lit3D("billIris", new Color(0.5f, 0.02f, 0.03f), 0.7f, 0f, emissionColor: new Color(1.6f, 0.05f, 0.05f)));
            var black = Own(Proto3DMaterials.Lit3D("billBlack", new Color(0.02f, 0.01f, 0.02f), 0.3f, 0f));
            var tooth = Own(Proto3DMaterials.Lit3D("billTooth", new Color(0.8f, 0.76f, 0.62f), 0.4f, 0f));
            _mudMat = Own(Proto3DMaterials.Lit3D("billMud", new Color(0.24f, 0.16f, 0.09f), 0.25f, 0f));
            _sealMat = Own(Proto3DMaterials.Lit3D("billSealStone", new Color(0.36f, 0.35f, 0.33f), 0.1f, 0f));

            // Body: a big lumpy blob, an eye that follows the player, a fanged maw below it.
            float r = boss.Radius;
            _body = new GameObject("body").transform;
            _body.SetParent(_root, false);
            var parts = new List<Renderer>();
            parts.Add(Part("blob", _sphere, flesh, _body, Vector3.zero, new Vector3(2f * r, 1.85f * r, 1.7f * r)));
            var rnd = new System.Random(7);
            for (int i = 0; i < 9; i++)
            {
                float a = (float)(rnd.NextDouble() * Math.PI * 2.0);
                float s = r * (0.5f + (float)rnd.NextDouble() * 0.45f);
                var pos = new Vector3(Mathf.Cos(a) * r * 0.75f, Mathf.Abs(Mathf.Sin(a)) * r * 0.6f + 0.2f, (float)(rnd.NextDouble() - 0.5) * r * 0.6f);
                parts.Add(Part("lump" + i, _sphere, i % 2 == 0 ? fleshLight : flesh, _body, pos, new Vector3(s, s * 0.85f, s)));
            }
            var eyeAt = new Vector3(-r * 0.25f, r * 0.3f, -r * 0.72f);
            _eye = Part("eye", _sphere, white, _body, eyeAt, Vector3.one * r * 0.85f).transform;
            parts.Add(_eye.GetComponent<Renderer>());
            var irisR = Part("iris", _sphere, iris, _eye, new Vector3(0f, 0f, -0.32f), new Vector3(0.62f, 0.62f, 0.4f));
            _pupil = irisR.transform;
            Part("pupil", _sphere, black, _pupil, new Vector3(0f, 0f, -0.3f), new Vector3(0.45f, 0.9f, 0.4f));
            Part("lid", _sphere, flesh, _body, eyeAt + new Vector3(0f, r * 0.33f, -0.05f), new Vector3(r * 0.95f, r * 0.35f, r * 0.9f));
            var mouthAt = new Vector3(-r * 0.35f, -r * 0.35f, -r * 0.7f);
            Part("maw", _sphere, black, _body, mouthAt, new Vector3(r * 1.1f, r * 0.32f, r * 0.4f));
            for (int i = 0; i < 7; i++)
            {
                float x = -r * 0.85f + i * r * 0.25f;
                Part("fangU" + i, _capsule, tooth, _body, mouthAt + new Vector3(x + r * 0.35f, r * 0.1f, -0.12f), new Vector3(0.14f, 0.2f, 0.14f));
                Part("fangL" + i, _capsule, tooth, _body, mouthAt + new Vector3(x + r * 0.47f, -r * 0.1f, -0.12f), new Vector3(0.12f, 0.16f, 0.12f));
            }
            _bodyRenderers = parts.ToArray();

            // Tentacles: a capsule per segment, suckers on every other one, a stump left when cut off.
            int n = boss.Tentacles.Length;
            _segs = new Transform[n, Segments];
            _segRenderers = new Renderer[n, Segments];
            _suckers = new Transform[n, Segments];
            _stumps = new Transform[n];
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < Segments; k++)
                {
                    var seg = Part("t" + i + "s" + k, _capsule, k % 3 == 1 ? fleshLight : flesh, _root, Vector3.zero, Vector3.one);
                    _segs[i, k] = seg.transform;
                    _segRenderers[i, k] = seg;
                    if (k % 2 == 1) _suckers[i, k] = Part("t" + i + "sucker" + k, _sphere, sucker, seg.transform, new Vector3(0f, 0f, -0.45f), new Vector3(0.55f, 0.18f, 0.35f)).transform;
                }
                var stump = Part("stump" + i, _sphere, fleshLight, _root, Vector3.zero, Vector3.one * 1.1f);
                stump.gameObject.SetActive(false);
                _stumps[i] = stump.transform;
            }

            for (int w = 0; w < 2; w++)
            {
                var wave = Part("wave" + w, _sphere, _mudMat, _root, Vector3.zero, new Vector3(1.2f, 1.4f, 1.6f));
                wave.gameObject.SetActive(false);
                _waves[w] = wave.transform;
            }
            Sync(null, boss, 0f);
        }

        /// <summary>Follows the sim (call every frame).</summary>
        public void Sync(RedlineSim sim, float time) => Sync(sim, sim?.Boss, time);

        private void Sync(RedlineSim sim, StoryBoss b, float time)
        {
            if (b == null) return;
            bool dying = b.Move == BossMove.Dying;
            float sink = dying ? Mathf.Clamp01(b.MoveTime / 2.5f) : 0f;
            float breathe = 1f + 0.03f * Mathf.Sin(time * 2.1f);
            _body.localPosition = new Vector3(b.Body.X, b.Body.Y, 0.6f);
            float mirror = b.Facing > 0 ? -1f : 1f;   // modelled facing left; mirrored when it turns right
            _body.localScale = new Vector3(mirror, 1f, 1f) * breathe * (1f - 0.35f * sink);
            float roll = b.Move == BossMove.Leap || b.Move == BossMove.Spin ? -b.SpinAngle * 0.15f : 0f;
            _body.localRotation = Quaternion.Euler(0f, 0f, dying ? Mathf.Sin(time * 25f) * 6f * (1f - sink) : roll);
            // The eye tracks the player.
            if (sim != null)
            {
                var d = new Vector2(sim.Player.X - b.Body.X, sim.Player.Y + 0.8f - b.Body.Y).normalized;
                _pupil.localPosition = new Vector3(d.x * 0.18f * (b.Facing > 0 ? -1f : 1f), d.y * 0.18f, -0.32f);
            }
            float flash = b.Flash;
            var tint = Color.Lerp(Flesh, HitTint, flash);
            _mpb.SetColor(BaseColorId, tint);
            _bodyRenderers[0].SetPropertyBlock(_mpb);

            for (int i = 0; i < b.Tentacles.Length; i++)
            {
                var t = b.Tentacles[i];
                bool on = !t.Severed && !dying;
                var stump = _stumps[i];
                if (stump.gameObject.activeSelf != t.Severed) stump.gameObject.SetActive(t.Severed);
                if (t.Severed)
                    stump.localPosition = new Vector3(b.Body.X + t.RootOffset.X * 1.15f, b.Body.Y + t.RootOffset.Y * 1.15f, 0f);
                var segColor = Color.Lerp(Flesh, HitTint, t.Flash);
                _mpb.SetColor(BaseColorId, segColor);
                for (int k = 0; k < Segments; k++)
                {
                    var seg = _segs[i, k];
                    if (seg.gameObject.activeSelf != on) seg.gameObject.SetActive(on);
                    if (!on) continue;
                    var a = t.Points[k];
                    var c = t.Points[k + 1];
                    var mid = new Vector3((a.X + c.X) * 0.5f, (a.Y + c.Y) * 0.5f, 0f);
                    var dir = new Vector2(c.X - a.X, c.Y - a.Y);
                    float len = Mathf.Max(0.05f, dir.magnitude);
                    float taper = Mathf.Lerp(1f, 0.32f, k / (float)(Segments - 1));
                    float thick = t.Thickness * 2f * taper;
                    seg.localPosition = mid;
                    seg.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
                    seg.localScale = new Vector3(thick, len * 0.62f + thick * 0.25f, thick);
                    _segRenderers[i, k].SetPropertyBlock(_mpb);
                }
            }

            // Stone slabs closing the floor behind the player once the fight starts.
            if (sim != null)
            {
                var seal = sim.BossSeal;
                while (_seal.Count < seal.Count)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Object.Destroy(go.GetComponent<Collider>());
                    go.GetComponent<Renderer>().sharedMaterial = _sealMat;
                    go.transform.SetParent(_root, false);
                    _seal.Add(go.transform);
                }
                for (int i = 0; i < _seal.Count; i++)
                {
                    bool on = i < seal.Count;
                    if (_seal[i].gameObject.activeSelf != on) _seal[i].gameObject.SetActive(on);
                    if (!on) continue;
                    _seal[i].localPosition = new Vector3(seal[i].x + 0.5f, seal[i].y + 0.5f, 0f);
                    _seal[i].localScale = new Vector3(1.02f, 1f, 2.4f);
                }
            }

            // Mud clumps (the boss's spit) and the slam's mud shockwave.
            if (sim != null)
            {
                int m = 0;
                foreach (var pr in sim.Projectiles)
                {
                    if (!pr.Mud) continue;
                    if (m == _mud.Count) _mud.Add(Part("mud" + m, _sphere, _mudMat, _root, Vector3.zero, Vector3.one).transform);
                    var t = _mud[m++];
                    if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                    t.localPosition = new Vector3(pr.Center.X, pr.Center.Y, 0f);
                    t.localRotation = Quaternion.Euler(time * 300f, time * 200f, 0f);
                    t.localScale = new Vector3(0.75f, 0.6f, 0.7f);
                }
                for (; m < _mud.Count; m++) if (_mud[m].gameObject.activeSelf) _mud[m].gameObject.SetActive(false);
            }
            bool waves = !float.IsNaN(b.WaveLeft);
            for (int w = 0; w < 2; w++)
            {
                var wave = _waves[w];
                if (wave.gameObject.activeSelf != waves) wave.gameObject.SetActive(waves);
                if (!waves) continue;
                float x = w == 0 ? b.WaveLeft : b.WaveRight;
                wave.localPosition = new Vector3(x, b.FloorY + 0.45f + 0.15f * Mathf.Sin(time * 30f + w), 0f);
                wave.localScale = new Vector3(1.1f, 1.2f + 0.3f * Mathf.Sin(time * 22f), 1.8f);
            }
        }

        public void Dispose()
        {
            if (_root != null) Object.Destroy(_root.gameObject);
            foreach (var o in _owned) if (o != null) Object.Destroy(o);
            _owned.Clear();
        }

        private T Own<T>(T o) where T : Object { _owned.Add(o); return o; }

        private static Renderer Part(string name, Mesh mesh, Material mat, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            return r;
        }
    }
}
