using UnityEngine;
using UnityEngine.Rendering;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// Switches the active render pipeline between the game's boot pipeline (URP with the 2D renderer) and the 3D
    /// pipeline (<c>Assets/Render3D/Resources/Proto3D_URP.asset</c>: Forward+, additional-light shadows, rendering
    /// layers). Swaps <see cref="QualitySettings.renderPipeline"/> for the session only; nothing is saved.
    /// </summary>
    /// <example>
    /// <code>
    /// Render3DMode.Enable();   // before building Arena3DRenderer / Pixel3DCamera
    /// ...
    /// Render3DMode.Disable();  // back to the 2D pipeline (e.g. when returning to a 2D level)
    /// </code>
    /// </example>
    /// <remarks>
    /// Main thread only. UI Toolkit panels do not depend on the pipeline and keep drawing at native resolution.
    /// The asset sits under a Resources folder so it ships in builds.
    /// </remarks>
    public static class Render3DMode
    {
        /// <summary>Resources path of the 3D pipeline asset.</summary>
        public const string PipelineResource = "Proto3D_URP";

        private static RenderPipelineAsset _previous;
        private static RenderPipelineAsset _asset;

        /// <summary>True while the 3D pipeline is active (set by <see cref="Enable"/>).</summary>
        public static bool IsEnabled { get; private set; }

        /// <summary>Activates the 3D pipeline. Returns false (and logs) if the asset is missing. Idempotent.</summary>
        public static bool Enable()
        {
            if (IsEnabled) return true;
            if (_asset == null) _asset = Resources.Load<RenderPipelineAsset>(PipelineResource);
            if (_asset == null)
            {
                Debug.LogError("Render3DMode: missing Resources/" + PipelineResource);
                return false;
            }
            _previous = QualitySettings.renderPipeline;
            QualitySettings.renderPipeline = _asset;
            IsEnabled = true;
            return true;
        }

        /// <summary>Restores the pipeline that was active before <see cref="Enable"/>. Idempotent.</summary>
        public static void Disable()
        {
            if (!IsEnabled) return;
            QualitySettings.renderPipeline = _previous;
            _previous = null;
            IsEnabled = false;
        }
    }
}
