using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Redline.Game
{
    /// <summary>
    /// URP post-processing built from code for the rainy night-city look: bloom on lamps, halos and
    /// gameplay glows; cool, desaturated, slightly low-contrast grading with blacks lifted toward
    /// slate-blue (and highlights kept warm so sodium lamps stay sodium); a subtle vignette; fine
    /// film grain; and a short red vignette pulse when damage is taken. Gameplay effects stay
    /// readable because they are bright, saturated and unlit: grading pulls them less than the scene.
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
        private const float BaseVignette = 0.34f;
        private const float VignetteSmoothness = 0.5f;
        private const float BloomThreshold = 0.62f, BloomIntensity = 1.25f, BloomScatter = 0.5f;
        private const float Saturation = -24f, Contrast = 10f, PostExposure = 0f;
        /// <summary>Lift (rgb tint, w offset): raises the blacks toward slate-blue.</summary>
        private static readonly Vector4 Lift = new Vector4(0.97f, 0.99f, 1.04f, 0f);
        /// <summary>Gamma: a touch cooler in the mid-tones.</summary>
        private static readonly Vector4 Gamma = new Vector4(0.98f, 1f, 1.03f, 0f);
        /// <summary>Gain: highlights a touch warm so sodium light reads warm against the cool night.</summary>
        private static readonly Vector4 Gain = new Vector4(1.03f, 1f, 0.97f, 0f);
        private const float GrainIntensity = 0.2f, GrainResponse = 0.8f;
        private static readonly Color VignetteColor = new Color(0.015f, 0.025f, 0.04f);
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
            bloom.threshold.Override(BloomThreshold);
            bloom.intensity.Override(BloomIntensity);
            bloom.scatter.Override(BloomScatter);

            var adjust = _profile.Add<ColorAdjustments>(true);
            adjust.saturation.Override(Saturation);
            adjust.contrast.Override(Contrast);
            adjust.postExposure.Override(PostExposure);

            var lgg = _profile.Add<LiftGammaGain>(true);
            lgg.lift.Override(Lift);
            lgg.gamma.Override(Gamma);
            lgg.gain.Override(Gain);

            var grain = _profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin2);
            grain.intensity.Override(GrainIntensity);
            grain.response.Override(GrainResponse);

            _vignette = _profile.Add<Vignette>(true);
            _vignette.intensity.Override(BaseVignette);
            _vignette.smoothness.Override(VignetteSmoothness);
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
