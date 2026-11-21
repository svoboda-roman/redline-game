using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// Cheap planar floor reflection for the 3D mode. A disabled helper camera mirrors the
    /// <see cref="Pixel3DCamera"/> world camera about the floor plane (y = <see cref="PlaneY"/>) and renders the scene
    /// into a half-resolution HDR texture once per frame (no shadows, no post, no MSAA); the
    /// <c>Proto3D/FloorReflection</c> overlay materials on the deck, puddles and flood water sample it in screen space.
    /// </summary>
    /// <remarks>
    /// The mirror camera is a proper (non-mirrored) camera at the reflected position, pitched the other way, so its
    /// image is the reflection flipped vertically and the shader samples it at (u, 1 - v): no oblique projection, so
    /// fog stays right. Everything that sits under the floor between the mirror camera and the floor (the deck body,
    /// near shade, framers, the overlays themselves) lives on <see cref="HiddenLayer"/>, which the mirror camera culls.
    /// Runs after other LateUpdates (execution order 10000) so it uses this frame's camera pose. Cost: one extra
    /// URP camera at 1/4 of the pixels (≈ 245×125), shadow maps not re-rendered. Main thread only; no per-frame allocation.
    /// </remarks>
    /// <example>
    /// <code>
    /// var refl = root.gameObject.AddComponent&lt;FloorReflection3D&gt;();
    /// refl.PlaneY = 1f;
    /// refl.Register(deckReflectionMaterial);
    /// </code>
    /// </example>
    [DefaultExecutionOrder(10000)]
    public sealed class FloorReflection3D : MonoBehaviour
    {
        /// <summary>Layer culled by the mirror camera (objects under or on the floor plane).</summary>
        public const int HiddenLayer = 28;

        private static readonly int ReflTexId = Shader.PropertyToID("_ReflTex");

        /// <summary>World height of the mirror plane (the deck top).</summary>
        public float PlaneY = 1f;

        /// <summary>Resolution divisor relative to the world camera's target (2 = half width and height).</summary>
        public int Downsample = 2;

        /// <summary>Render the mirror every <see cref="Interval"/> frames (2 = 30 Hz at 60 fps; the soft reflection hides the lag).</summary>
        public int Interval = 2;

        /// <summary>Mirror far clip (world units): the far city rows and sky are left to the background colour.</summary>
        public float MaxDistance = 150f;

        /// <summary>Background colour (fog colour) where nothing is reflected.</summary>
        public Color Background = Color.black;

        /// <summary>Mean CPU time of the mirror render (ms, culling + submission, measured on the main thread).</summary>
        public float CpuMs { get; private set; }

        /// <summary>The reflection texture (null until the first render).</summary>
        public RenderTexture Texture => _rt;

        private readonly List<Material> _materials = new List<Material>();
        private readonly Stopwatch _watch = new Stopwatch();
        private Camera _cam;
        private RenderTexture _rt;
        private int _frames;
        private double _msSum;
        private bool _logged;

        /// <summary>Adds a material whose <c>_ReflTex</c> receives the reflection texture.</summary>
        public void Register(Material m)
        {
            if (m == null) return;
            _materials.Add(m);
            if (_rt != null) m.SetTexture(ReflTexId, _rt);
        }

        private void LateUpdate()
        {
            var rig = Pixel3DCamera.Latest;
            var src = rig != null ? rig.WorldCamera : null;
            if (src == null || src.targetTexture == null) return;
            bool fresh = _rt == null;
            Ensure(src);
            if (!fresh && Interval > 1 && Time.frameCount % Interval != 0) return;
            Place(src);
            _watch.Restart();
            _cam.Render();
            _watch.Stop();
            _msSum += _watch.Elapsed.TotalMilliseconds;
            if (++_frames % 60 == 0)
            {
                CpuMs = (float)(_msSum / 60.0) / Mathf.Max(1, Interval);
                _msSum = 0.0;
                if (!_logged) { _logged = true; Debug.Log($"[Reflection] {_rt.width}x{_rt.height} mirror every {Interval} frames: {CpuMs * Interval:F2} ms CPU per render, {CpuMs:F2} ms/frame amortised (mean of 60 renders)"); }
            }
        }

        private void Ensure(Camera src)
        {
            int w = Mathf.Max(16, src.targetTexture.width / Downsample), h = Mathf.Max(8, src.targetTexture.height / Downsample);
            if (_rt != null && (_rt.width != w || _rt.height != h))
            {
                _cam.targetTexture = null;
                _rt.Release(); Destroy(_rt); _rt = null;
            }
            if (_rt == null)
            {
                _rt = new RenderTexture(w, h, 16, RenderTextureFormat.DefaultHDR)
                {
                    name = "FloorReflection", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, useMipMap = false,
                };
                _rt.Create();
                foreach (var m in _materials) if (m != null) m.SetTexture(ReflTexId, _rt);
            }
            if (_cam == null)
            {
                var go = new GameObject("FloorReflection Camera");
                go.transform.SetParent(transform, false);
                _cam = go.AddComponent<Camera>();
                _cam.enabled = false; // rendered explicitly; stays out of Camera.allCameras
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.allowMSAA = false;
                _cam.allowHDR = true;
                _cam.cullingMask = src.cullingMask & ~(1 << HiddenLayer);
                var d = _cam.GetUniversalAdditionalCameraData();
                d.renderPostProcessing = false;
                d.renderShadows = false;
                d.antialiasing = AntialiasingMode.None;
                d.requiresColorOption = CameraOverrideOption.Off;
                d.requiresDepthOption = CameraOverrideOption.Off;
                d.volumeLayerMask = 0;
            }
            _cam.backgroundColor = Background;
            _cam.targetTexture = _rt;
        }

        private void Place(Camera src)
        {
            var t = src.transform;
            var p = t.position; p.y = 2f * PlaneY - p.y;
            var f = t.forward; f.y = -f.y;
            var u = t.up;
            // Mirror frame (Mr, Mu, Mf) is left-handed; (Mr, -Mu, Mf) is a proper camera whose image is flipped in v.
            _cam.transform.SetPositionAndRotation(p, Quaternion.LookRotation(f, new Vector3(-u.x, u.y, -u.z)));
            _cam.fieldOfView = src.fieldOfView;
            _cam.aspect = src.aspect;
            _cam.nearClipPlane = src.nearClipPlane;
            _cam.farClipPlane = Mathf.Min(src.farClipPlane, MaxDistance);
        }

        private void OnDestroy()
        {
            if (_cam != null) _cam.targetTexture = null;
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
        }
    }
}
