using UnityEditor;
using UnityEngine;

namespace Redline.EditorTools
{
    /// <summary>
    /// Import settings for the character sprite sheets under <c>Assets/Resources/Art/</c>
    /// (<c>&lt;name&gt;_color</c>, <c>_normal</c>, <c>_emissive</c> atlases plus <c>&lt;name&gt;.json</c>,
    /// read at runtime by <see cref="Redline.Game.Art.CharacterSheet"/>). Generic: any sheet dropped
    /// into that folder with the same suffixes gets the same treatment.
    /// <list type="bullet">
    /// <item>Texture type Default (sprites are created at runtime), point filter, no mipmaps,
    /// uncompressed, clamp, no power-of-two rescale (the atlases are 1234 px wide).</item>
    /// <item><c>_normal</c> and <c>_emissive</c> are data: sRGB off.</item>
    /// <item><c>_emissive</c> is Read/Write: the loader splits its R/G/B masks once at load and then
    /// unloads it. Colour and normal stay GPU-only.</item>
    /// </list>
    /// </summary>
    /// <remarks>Editor-only. Reimport the folder after changing these rules.</remarks>
    public sealed class ArtSheetImporter : AssetPostprocessor
    {
        private const string Root = "Assets/Resources/Art/";

        /// <summary>Bump to force a reimport of every sheet when the rules change.</summary>
        public override uint GetVersion() => 1;

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root)) return;
            var importer = (TextureImporter)assetImporter;
            string file = System.IO.Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            bool normal = file.EndsWith("_normal");
            bool emissive = file.EndsWith("_emissive");

            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.anisoLevel = 0;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = !normal && !emissive;
            importer.sRGBTexture = !normal && !emissive;
            importer.isReadable = emissive;
            importer.maxTextureSize = 4096;
        }
    }
}
