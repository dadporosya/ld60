#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Applies the project's audio import policy to newly imported clips so the
/// Decompress-On-Load-with-no-background-loading combination that froze the game on every music change
/// can never come back through a fresh import.
///
/// <para>Only first-time imports are touched (<see cref="AssetImporter.importSettingsMissing"/>), so
/// deliberate per-clip tweaks made in the Inspector afterwards are left alone. To re-apply the policy to
/// existing clips, use <b>Tools > Audio > Fix Import Settings</b>.</para>
/// </summary>
public class AudioImportPostprocessor : AssetPostprocessor
{
    /// <summary>Clips at least this long are imported as streaming music. Matches AudioImportSettingsTool.</summary>
    private const float StreamingLengthThreshold = 15f;

    private const float MusicQuality = 0.7f;

    private static readonly string[] MusicPathHints = { "/bgm/", "/music/", "/soundtrack/" };

    private void OnPreprocessAudio()
    {
        if (assetImporter is not AudioImporter importer) return;
        if (!importer.importSettingsMissing) return; // already configured — respect it

        // Length is not known before the first import, so new clips are classified by folder only.
        // Anything dropped outside a music folder that turns out to be long gets caught by the
        // Tools > Audio > Audit Import Settings report.
        bool music = IsMusicPath(assetPath);

        AudioImporterSampleSettings settings = importer.defaultSampleSettings;

        if (music)
        {
            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = MusicQuality;
            settings.preloadAudioData = false;
        }
        else
        {
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.preloadAudioData = true;
        }

        importer.defaultSampleSettings = settings;

        // Load In Background is an importer-level flag, not part of the per-platform sample settings.
        importer.loadInBackground = true;
    }

    private static bool IsMusicPath(string path)
    {
        string lower = path.Replace('\\', '/').ToLowerInvariant();
        foreach (string hint in MusicPathHints)
            if (lower.Contains(hint)) return true;
        return false;
    }
}
#endif
