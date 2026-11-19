using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Redline.Game.Atmosphere
{
    /// <summary>
    /// Creates URP 2D lights from code. Shadows are off (nothing casts them, and they cost a
    /// render texture per light). Normal-mapped lighting is opt-in per light.
    /// </summary>
    /// <example>
    /// <code>
    /// Lights2D.Global(root, AtmosphereConfig.GlobalLight, AtmosphereConfig.GlobalIntensity);
    /// var lamp = Lights2D.Point(root, "Lamp", pos, sodium, 1.5f, 6.5f, 0.4f, 140f, 60f, 0.6f, normalDistance: 2.2f);
    /// lamp.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);   // cone points down
    /// </code>
    /// </example>
    /// <remarks>
    /// Main-thread only. In URP 17.3 (Unity 6.3) <c>Light2D.normalMapQuality</c> and
    /// <c>normalMapDistance</c> are read-only; their serialized fields are written through
    /// <see cref="JsonUtility.FromJsonOverwrite(string, object)"/>, which goes through Unity's
    /// serializer. The field names (<c>m_NormalMapQuality</c>, <c>m_NormalMapDistance</c>,
    /// <c>m_ComponentVersion</c>) are package internals: re-check them when URP is upgraded.
    /// </remarks>
    public static class Lights2D
    {
        // m_ComponentVersion 3 = current (skips the legacy upgrade that would reset the quality);
        // m_NormalMapQuality 0 = Fast.
        private const string NormalJsonHead = "{\"m_ComponentVersion\":3,\"m_NormalMapQuality\":0,\"m_NormalMapDistance\":";

        /// <summary>The one global light of a scene (ambient for every lit sprite).</summary>
        public static Light2D Global(Transform parent, Color color, float intensity)
        {
            var go = new GameObject("GlobalLight2D");
            go.transform.SetParent(parent, false);
            var light = go.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.color = color;
            light.intensity = intensity;
            light.shadowsEnabled = false;
            return light;
        }

        /// <summary>
        /// A point (or spot, when <paramref name="outerAngle"/> &lt; 360) light. The cone points along
        /// the light's local up axis. <paramref name="normalDistance"/> &gt; 0 enables Fast normal mapping.
        /// </summary>
        public static Light2D Point(Transform parent, string name, Vector3 localPosition, Color color, float intensity,
            float outerRadius, float innerRadius, float outerAngle, float innerAngle, float falloff, float normalDistance = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var light = go.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.pointLightOuterRadius = outerRadius;
            light.pointLightInnerRadius = innerRadius;
            light.pointLightOuterAngle = outerAngle;
            light.pointLightInnerAngle = innerAngle;
            light.falloffIntensity = falloff;
            light.shadowsEnabled = false;
            if (normalDistance > 0f)
                JsonUtility.FromJsonOverwrite(NormalJsonHead + normalDistance.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}", light);
            return light;
        }
    }
}
