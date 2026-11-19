using UnityEditor;
using UnityEngine;

namespace Redline.EditorTools
{
    /// <summary>
    /// Import settings for Redline's recorded audio under <c>Assets/Resources/Audio/</c>
    /// (production/audio/music-sfx-overhaul/direction.md 2.3 and 5):
    /// <list type="bullet">
    /// <item><c>Music/</c>: Vorbis quality 0.6, Streaming, preload off, stereo.</item>
    /// <item><c>Sfx/Ambience/</c>: Vorbis, Compressed In Memory, stereo (the rain and city loops).</item>
    /// <item>Other <c>Sfx/</c>: Vorbis, forced to mono; Decompress On Load for short clips
    /// (files under <see cref="LongClipBytes"/>), Compressed In Memory for longer ones.</item>
    /// </list>
    /// </summary>
    /// <example>Bump <see cref="Version"/> after changing a rule: Unity then reimports the folder.</example>
    /// <remarks>Editor-only.</remarks>
    public sealed class AudioImport : AssetPostprocessor
    {
        private const string Root = "Assets/Resources/Audio/";
        private const uint Version = 1;
        /// <summary>SFX files above this many bytes (about 3 s of Vorbis) stay compressed in memory.</summary>
        private const long LongClipBytes = 48 * 1024;
        private const float MusicQuality = 0.6f;
        private const float SfxQuality = 0.7f;

        public override uint GetVersion() => Version;

        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Root)) return;
            var importer = (AudioImporter)assetImporter;
            bool music = assetPath.StartsWith(Root + "Music/");
            bool ambience = assetPath.StartsWith(Root + "Sfx/Ambience/");
            var s = importer.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            if (music)
            {
                s.loadType = AudioClipLoadType.Streaming;
                s.quality = MusicQuality;
                s.preloadAudioData = false;
                importer.forceToMono = false;
                importer.loadInBackground = true;
            }
            else
            {
                s.quality = SfxQuality;
                s.preloadAudioData = true;
                importer.forceToMono = !ambience;
                // Long SFX (the 13-49 s katana rings, the death grunts) would be large decompressed;
                // the encoded file size stands in for the length, so the decision is made before import.
                bool longClip = new System.IO.FileInfo(assetPath).Length > LongClipBytes;
                s.loadType = ambience || longClip ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            }
            importer.defaultSampleSettings = s;
        }
    }
}
