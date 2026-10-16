#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot fixer for audio import settings.
///
/// <para><b>Why this exists.</b> Every clip in this project was imported as <i>Decompress On Load</i> with
/// <i>Load In Background</i> off. With those two settings, <c>AudioClip.LoadAudioData()</c> — and
/// <c>AudioSource.Play()</c> on a clip whose data is not resident — decompresses the entire track on the
/// main thread. For a 40 MB Vorbis music track that is a multi-second, whole-game freeze, and it is why
/// <see cref="BGMManager"/>'s <c>EnsureLoaded</c> coroutine could never help: the blocking call returns
/// already-Loaded, so there was never a <c>Loading</c> state to yield through.</para>
///
/// <para><b>What it does.</b> Long clips (music) become <see cref="AudioClipLoadType.Streaming"/> + Vorbis:
/// nothing is decompressed up front, RAM cost is a small ring buffer, and <see cref="BGMManager"/> already
/// skips preloading streamed clips entirely. Short clips (SFX, voice lines) stay
/// <see cref="AudioClipLoadType.DecompressOnLoad"/> — that is correct for them — but get <i>Load In
/// Background</i> turned on so a cold one never stalls a frame either.</para>
///
/// <para>Run it from <b>Tools > Audio > Fix Import Settings</b>. <b>Audit Import Settings</b> reports what
/// would change without touching anything.</para>
/// </summary>
public static class AudioImportSettingsTool
{
    // ──────────────────────────────────────────────
    // Policy
    // ──────────────────────────────────────────────

    /// <summary>Clips at least this long are treated as music and switched to streaming.</summary>
    private const float StreamingLengthThreshold = 15f;

    /// <summary>Vorbis quality for streamed music (0..1). 0.7 is transparent enough for BGM.</summary>
    private const float MusicQuality = 0.7f;

    /// <summary>Path fragments that force a clip to be treated as music regardless of length.</summary>
    private static readonly string[] MusicPathHints = { "/bgm/", "/music/", "/soundtrack/" };

    /// <summary>Platform names whose per-platform overrides get cleared so the default settings win.</summary>
    private static readonly string[] OverridablePlatforms =
    {
        "Standalone", "Windows", "OSX", "Linux", "Android", "iPhone", "WebGL",
        "PS4", "PS5", "XboxOne", "Switch", "tvOS", "VisionOS"
    };

    // ──────────────────────────────────────────────
    // Menu entries
    // ──────────────────────────────────────────────

    [MenuItem("Tools/Audio/Fix Import Settings", false, 100)]
    public static void FixAll() => Run(dryRun: false);

    [MenuItem("Tools/Audio/Audit Import Settings", false, 101)]
    public static void AuditAll() => Run(dryRun: true);

    [MenuItem("Tools/Audio/Fix Import Settings (Selection Only)", false, 120)]
    public static void FixSelection()
    {
        string[] guids = Selection.assetGUIDs;
        if (guids == null || guids.Length == 0)
        {
            h.Out("Audio import fix: nothing selected.");
            return;
        }

        var paths = new List<string>();
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.IsValidFolder(path))
                paths.AddRange(FindAudioPaths(path));
            else if (IsAudioPath(path))
                paths.Add(path);
        }

        Run(dryRun: false, explicitPaths: paths.Distinct().ToList());
    }

    // ──────────────────────────────────────────────
    // Implementation
    // ──────────────────────────────────────────────

    private static void Run(bool dryRun, List<string> explicitPaths = null)
    {
        List<string> paths = explicitPaths ?? FindAudioPaths("Assets");
        if (paths.Count == 0)
        {
            h.Out("Audio import fix: no audio assets found.");
            return;
        }

        var report = new StringBuilder();
        int changed = 0;
        int streamed = 0;

        try
        {
            if (!dryRun) AssetDatabase.StartAssetEditing();

            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];

                if (EditorUtility.DisplayCancelableProgressBar(
                        dryRun ? "Auditing audio" : "Fixing audio import settings",
                        path, (float)i / paths.Count))
                    break;

                if (AssetImporter.GetAtPath(path) is not AudioImporter importer) continue;

                bool music = IsMusic(path, importer);
                AudioImporterSampleSettings desired = importer.defaultSampleSettings;

                // Load In Background is an importer-level flag, not part of the per-platform sample
                // settings struct, so it is tracked separately from `desired`.
                const bool desiredLoadInBackground = true;

                if (music)
                {
                    desired.loadType = AudioClipLoadType.Streaming;
                    desired.compressionFormat = AudioCompressionFormat.Vorbis;
                    desired.quality = MusicQuality;
                    desired.preloadAudioData = false;
                }
                else
                {
                    desired.loadType = AudioClipLoadType.DecompressOnLoad;
                    desired.preloadAudioData = true;
                }

                bool hadOverride = false;
                foreach (string platform in OverridablePlatforms)
                    if (importer.ContainsSampleSettingsOverride(platform))
                        hadOverride = true;

                bool backgroundOk = importer.loadInBackground == desiredLoadInBackground;

                if (Matches(importer.defaultSampleSettings, desired) && backgroundOk && !hadOverride) continue;

                changed++;
                if (music) streamed++;
                report.AppendLine(
                    $"  {(music ? "MUSIC   " : "SFX     ")} {path}\n" +
                    $"      {Describe(importer.defaultSampleSettings, importer.loadInBackground)}  ->  " +
                    $"{Describe(desired, desiredLoadInBackground)}" +
                    (hadOverride ? "  (+ platform overrides cleared)" : ""));

                if (dryRun) continue;

                foreach (string platform in OverridablePlatforms)
                    if (importer.ContainsSampleSettingsOverride(platform))
                        importer.ClearSampleSettingOverride(platform);

                importer.defaultSampleSettings = desired;
                importer.loadInBackground = desiredLoadInBackground;
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }
        }
        finally
        {
            if (!dryRun) AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
        }

        string verb = dryRun ? "would change" : "changed";
        h.Out($"Audio import {(dryRun ? "audit" : "fix")}: {paths.Count} clip(s) scanned, {changed} {verb} " +
              $"({streamed} switched to Streaming).\n{report}");

        if (!dryRun && changed > 0) AssetDatabase.Refresh();
    }

    /// <summary>
    /// A clip counts as music when it sits in a BGM/Music folder or is longer than
    /// <see cref="StreamingLengthThreshold"/>. Path wins over length so a short music stinger that lives
    /// with the soundtrack is still streamed with the rest of it.
    /// </summary>
    private static bool IsMusic(string path, AudioImporter importer)
    {
        string lower = path.Replace('\\', '/').ToLowerInvariant();
        foreach (string hint in MusicPathHints)
            if (lower.Contains(hint)) return true;

        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        return clip && clip.length >= StreamingLengthThreshold;
    }

    private static bool Matches(AudioImporterSampleSettings a, AudioImporterSampleSettings b) =>
        a.loadType == b.loadType &&
        a.compressionFormat == b.compressionFormat &&
        Mathf.Approximately(a.quality, b.quality) &&
        a.preloadAudioData == b.preloadAudioData;

    private static string Describe(AudioImporterSampleSettings s, bool loadInBackground) =>
        $"{s.loadType}/{s.compressionFormat} q{s.quality:0.00} " +
        $"preload:{(s.preloadAudioData ? "on" : "off")} bg:{(loadInBackground ? "on" : "off")}";

    private static bool IsAudioPath(string path)
    {
        string lower = path.ToLowerInvariant();
        return lower.EndsWith(".wav") || lower.EndsWith(".mp3") || lower.EndsWith(".ogg") ||
               lower.EndsWith(".aif") || lower.EndsWith(".aiff") || lower.EndsWith(".flac") ||
               lower.EndsWith(".mod") || lower.EndsWith(".it") || lower.EndsWith(".s3m") ||
               lower.EndsWith(".xm");
    }

    private static List<string> FindAudioPaths(string root)
    {
        return AssetDatabase.FindAssets("t:AudioClip", new[] { root })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(IsAudioPath)
            .Distinct()
            .OrderBy(p => p)
            .ToList();
    }
}
#endif
