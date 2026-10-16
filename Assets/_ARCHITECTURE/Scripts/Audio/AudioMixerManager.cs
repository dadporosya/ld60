using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using M = System.MathF;

/// <summary>
/// Central access point for the game's <see cref="AudioMixer"/> volumes.
///
/// <para><b>Important:</b> the names below are the mixer's EXPOSED PARAMETER names, not just the
/// group names. A mixer group called "Music" does nothing on its own — its Volume has to be
/// right-clicked in the Audio Mixer window and "Expose to script", and the resulting parameter
/// (which Unity names "MyExposedParam" by default) renamed to match. If they don't match,
/// <see cref="AudioMixer.SetFloat"/> just returns false and the volume silently never changes.
/// <see cref="ValidateExposedParameters"/> catches exactly that case at startup.</para>
/// </summary>
public static class AudioMixerManager
{
    private const string MixerResourcePath = "ARCHITECTURE/Audio/Mixers/MainMixer";

    private static AudioMixer cachedMixer;

    /// <summary>The main mixer, loaded on first use (never during static field init).</summary>
    public static AudioMixer audioMixer
    {
        get
        {
            if (cachedMixer == null)
            {
                cachedMixer = Resources.Load<AudioMixer>(MixerResourcePath);
                if (cachedMixer == null)
                    Debug.LogError($"[AudioMixer] No AudioMixer found at Resources/{MixerResourcePath}. All volume changes will be ignored.");
            }

            return cachedMixer;
        }
    }

    // These must match BOTH the mixer group name (for FindMatchingGroups) and the exposed
    // parameter name (for Set/GetFloat).
    public static string masterGroupName = "Master";
    public static string SFXGroupName = "SFX";
    public static string musicGroupName = "Music";

    /// <summary>Lowest linear level that still converts to a finite dB value (~-80 dB).</summary>
    public const float MinLevel = 0.0001f;

    /// <summary>Groups already warned about, so a slider drag doesn't spam the console.</summary>
    private static readonly HashSet<string> warnedGroups = new HashSet<string>();

    // SETTERS

    /// <summary>
    /// Sets a mixer group's volume from a linear 0..1 level.
    /// Returns false (and warns once) when <paramref name="groupName"/> is not an exposed
    /// parameter on the mixer — the usual reason a working slider changes nothing.
    /// </summary>
    public static bool TrySetMixerGroupVolume(float level, string groupName)
    {
        AudioMixer mixer = audioMixer;
        if (mixer == null) return false;

        // Log10(0) is -Infinity, which makes the mixer parameter go NaN instead of silent.
        if (mixer.SetFloat(groupName, LevelToDb(level))) return true;

        WarnOnce(groupName);
        return false;
    }

    // Kept void so they still bind to System.Action<float> (UI sliders, UnityEvents).
    // Use TrySetMixerGroupVolume when the caller wants to know whether it landed.
    public static void SetMixerGroupVolume(float level, string groupName) => TrySetMixerGroupVolume(level, groupName);

    public static void SetMasterVolume(float level) => SetMixerGroupVolume(level, masterGroupName);
    public static void SetMusicVolume(float level)  => SetMixerGroupVolume(level, musicGroupName);
    public static void SetSFXVolume(float level)    => SetMixerGroupVolume(level, SFXGroupName);

    private static void WarnOnce(string groupName)
    {
        if (!warnedGroups.Add(groupName)) return;

        Debug.LogWarning(
            $"[AudioMixer] '{groupName}' is not an exposed parameter on '{MixerResourcePath}', so its volume " +
            "will never change. Fix: open the Audio Mixer window, select the group, right-click its Volume " +
            $"in the Inspector -> 'Expose ... to script', then rename the parameter (top-right 'Exposed Parameters' " +
            $"dropdown) from 'MyExposedParam' to exactly '{groupName}'.");
    }

    // CONVERSION

    /// <summary>Linear 0..1 level -> dB, with the zero clamped away so Log10 stays finite.</summary>
    public static float LevelToDb(float level)
    {
        return M.Log10(M.Max(level, MinLevel)) * 20f;
    }

    /// <summary>dB -> linear 0..1 level (inverse of <see cref="LevelToDb"/>).</summary>
    public static float DbToLevel(float db)
    {
        return M.Pow(10f, db / 20f);
    }

    // READ-BACK & DEBUG

    /// <summary>
    /// Reads a mixer group's current volume in dB, i.e. what the mixer actually holds right now.
    /// Returns false when the parameter isn't exposed on the mixer or the mixer failed to load.
    /// </summary>
    public static bool TryGetMixerGroupVolumeDb(string groupName, out float db)
    {
        db = 0f;
        AudioMixer mixer = audioMixer;
        return mixer != null && mixer.GetFloat(groupName, out db);
    }

    /// <summary>True when <paramref name="groupName"/> is actually exposed and writable.</summary>
    public static bool IsParameterExposed(string groupName)
    {
        return TryGetMixerGroupVolumeDb(groupName, out _);
    }

    /// <summary>Current volume of a mixer group as a linear 0..1 level, or -1 if unreadable.</summary>
    public static float GetMixerGroupLevel(string groupName)
    {
        return TryGetMixerGroupVolumeDb(groupName, out float db) ? DbToLevel(db) : -1f;
    }

    /// <summary>Human-readable state of a single group, e.g. "Music = -6.02 dB (0.5)".</summary>
    public static string DescribeMixerGroup(string groupName)
    {
        if (audioMixer == null) return groupName + " = <no mixer loaded>";

        return TryGetMixerGroupVolumeDb(groupName, out float db)
            ? $"{groupName} = {db:0.##} dB ({DbToLevel(db):0.###})"
            : $"{groupName} = <parameter NOT exposed - volume changes do nothing>";
    }

    /// <summary>Dumps the current volume of every group to the console.</summary>
    public static void LogCurrentVolumes(string context = "")
    {
        h.Out(
            string.IsNullOrEmpty(context) ? "[AudioMixer]" : "[AudioMixer] " + context,
            DescribeMixerGroup(masterGroupName),
            DescribeMixerGroup(musicGroupName),
            DescribeMixerGroup(SFXGroupName));
    }

    /// <summary>
    /// Checks at startup that every group this class writes to is actually exposed, and that the
    /// group itself exists on the mixer. Runs automatically before the first scene loads, so a
    /// mis-named parameter shows up as a console error instead of as "the slider does nothing".
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void ValidateExposedParameters()
    {
        AudioMixer mixer = audioMixer;
        if (mixer == null) return;

        foreach (string groupName in new[] { masterGroupName, musicGroupName, SFXGroupName })
        {
            if (mixer.FindMatchingGroups(groupName).Length == 0)
                Debug.LogError($"[AudioMixer] No mixer group named '{groupName}' on '{MixerResourcePath}'.");

            if (!IsParameterExposed(groupName))
                WarnOnce(groupName);
        }
    }

    // GROUPS

    public static AudioMixerGroup GetMixerGroup(string groupName)
    {
        AudioMixer mixer = audioMixer;
        if (mixer == null) return null;

        AudioMixerGroup[] groups = mixer.FindMatchingGroups(groupName);
        if (groups.Length != 0) return groups[0];

        Debug.LogError($"[AudioMixer] No mixer group named '{groupName}'. AudioSources will fall back to the default output.");
        return null;
    }

    public static AudioMixerGroup GetMasterGroup() => GetMixerGroup(masterGroupName);
    public static AudioMixerGroup GetSFXGroup()    => GetMixerGroup(SFXGroupName);
    public static AudioMixerGroup GetMusicGroup()  => GetMixerGroup(musicGroupName);
}
