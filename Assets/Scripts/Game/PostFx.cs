using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Redline.Game
{
    /// <summary>
    /// URP post-processing built from code: bloom makes the neon glow, a vignette frames the
    /// arena, and a short red vignette pulse marks damage taken.
    /// </summary>
    /// <example>
    /// <code>
    /// var fx = gameObject.AddComponent&lt;PostFx&gt;();
    /// fx.Init(Camera.main);
    /// fx.PulseDamage();
    /// </code>
    /// </example>
    /// <remarks>Main-thread only. Destroys the profile it creates.</remarks>
    public sealed class PostFx : MonoBehaviour
    {
        private const float BaseVignette = 0.32f;
        private static readonly Color VignetteColor = new Color(0.02f, 0f, 0.06f);
        private static readonly Color DamageColor = new Color(0.55f, 0f, 0.12f);

        private VolumeProfile _profile;
        private Vignette _vignette;
        private float _damagePulse;

        /// <summary>Enables post-processing on the camera and creates a global volume.</summary>
        public void Init(Camera camera)
        {
            var cameraData = camera.GetUniversalAdditionalCameraData();
            if (cameraData != null) cameraData.renderPostProcessing = true;

            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "RedlinePostFx";

            var bloom = _profile.Add<Bloom>(true);
            bloom.threshold.Override(0.55f);
            bloom.intensity.Override(1.6f);
            bloom.scatter.Override(0.65f);

            _vignette = _profile.Add<Vignette>(true);
            _vignette.intensity.Override(BaseVignette);
            _vignette.smoothness.Override(0.45f);
            _vignette.color.Override(VignetteColor);

            var volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = _profile;
        }

        /// <summary>Flashes the screen edges red for a moment.</summary>
        public void PulseDamage()
        {
            _damagePulse = 1f;
        }

        private void Update()
        {
            if (_vignette == null || _damagePulse <= 0f) return;
            _damagePulse = Mathf.Max(0f, _damagePulse - Time.deltaTime * 3f);
            _vignette.intensity.value = Mathf.Lerp(BaseVignette, 0.5f, _damagePulse);
            _vignette.color.value = Color.Lerp(VignetteColor, DamageColor, _damagePulse);
        }

        private void OnDestroy()
        {
            if (_profile != null) Destroy(_profile);
        }
    }
}
