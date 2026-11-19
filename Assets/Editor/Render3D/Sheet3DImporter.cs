using UnityEditor;
using UnityEngine;

namespace Redline.Proto3DEditor
{
    /// <summary>
    /// Guard for the 3D actor renderers' normal sheets (<c>Assets/Resources/Art/**/&lt;name&gt;_normal.png</c>).
    /// <see cref="Redline.EditorTools.ArtSheetImporter"/> already imports them as data (sRGB off, point, uncompressed,
    /// Default type); this rule runs after it and pins the two settings the 3D path depends on, so a later change to
    /// the 2D importer cannot silently break 3D lighting:
    /// <list type="bullet">
    /// <item>sRGB off: the encoded vectors must be sampled linearly.</item>
    /// <item>Texture type stays Default (not NormalMap): URP Lit's UnpackNormal then reads RG on desktop
    /// (RGorAG with A = 255 on body texels) or RGB on mobile; both decode the sheet's R=+X, G=+Y, B=toward-camera
    /// encoding. NormalMap type would recompress and reswizzle the pixel-art normals.</item>
    /// </list>
    /// Transparent texels are (128,128,255,0) and are alpha-clipped by the colour sheet, so no fix-up is needed.
    /// </summary>
    /// <remarks>Editor-only. Logs a warning if a normal sheet arrives with an unexpected type.</remarks>
    public sealed class Sheet3DImporter : AssetPostprocessor
    {
        private const string Root = "Assets/Resources/Art/";

        /// <summary>Runs after ArtSheetImporter (default order 0).</summary>
        public override int GetPostprocessOrder() => 10;

        /// <summary>Bump to force a reimport when the rule changes.</summary>
        public override uint GetVersion() => 1;

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root)) return;
            if (!System.IO.Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant().EndsWith("_normal")) return;
            var importer = (TextureImporter)assetImporter;
            if (importer.textureType != TextureImporterType.Default)
                Debug.LogWarning("Sheet3DImporter: " + assetPath + " is " + importer.textureType + "; the 3D sprite path expects Default (linear).");
            importer.sRGBTexture = false;
        }
    }
}
