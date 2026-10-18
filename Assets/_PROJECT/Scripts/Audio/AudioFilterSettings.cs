using System;
using UnityEngine;

// Filter settings shown in the Inspector and put on an AudioSource's GameObject (a filter there affects every
// AudioSource on that object). Used by SongManager (the music) and AmbientNoise.

/// <summary>A low-pass filter: cuts everything above <see cref="cutoff"/>.</summary>
[Serializable]
public class LowPassSettings
{
    [Tooltip("Off: plays unfiltered.")]
    public bool enabled = false;
    [Tooltip("Hz. Everything above it is cut - lower sounds more muffled. 22000: fully open.")]
    [Range(GlobalLowPass.MinCutoff, GlobalLowPass.OpenCutoff)] public float cutoff = 5000f;
    [Tooltip("Boost right at the cutoff. 1: none.")]
    [Range(1f, 10f)] public float resonanceQ = 1f;

    /// <summary>Puts the filter on <paramref name="go"/> - added the first time it's enabled, bypassed while off.</summary>
    public void ApplyTo(GameObject go)
    {
        AudioLowPassFilter filter = go.GetComponent<AudioLowPassFilter>();
        if (!filter && enabled) filter = go.AddComponent<AudioLowPassFilter>();
        if (!filter) return;

        filter.enabled = enabled;
        filter.cutoffFrequency = Mathf.Clamp(cutoff, GlobalLowPass.MinCutoff, GlobalLowPass.OpenCutoff);
        filter.lowpassResonanceQ = Mathf.Max(1f, resonanceQ);
    }
}

/// <summary>A high-pass filter: cuts everything below <see cref="cutoff"/>.</summary>
[Serializable]
public class HighPassSettings
{
    [Tooltip("Off: plays unfiltered.")]
    public bool enabled = false;
    [Tooltip("Hz. Everything below it is cut - higher sounds thinner, like a small speaker. 10: fully open.")]
    [Range(GlobalLowPass.MinCutoff, GlobalLowPass.OpenCutoff)] public float cutoff = 500f;
    [Tooltip("Boost right at the cutoff. 1: none.")]
    [Range(1f, 10f)] public float resonanceQ = 1f;

    /// <summary>Puts the filter on <paramref name="go"/> - added the first time it's enabled, bypassed while off.</summary>
    public void ApplyTo(GameObject go)
    {
        AudioHighPassFilter filter = go.GetComponent<AudioHighPassFilter>();
        if (!filter && enabled) filter = go.AddComponent<AudioHighPassFilter>();
        if (!filter) return;

        filter.enabled = enabled;
        filter.cutoffFrequency = Mathf.Clamp(cutoff, GlobalLowPass.MinCutoff, GlobalLowPass.OpenCutoff);
        filter.highpassResonanceQ = Mathf.Max(1f, resonanceQ);
    }
}

/// <summary>A reverb. Levels are in millibels (-10000: silent, 0: full).</summary>
[Serializable]
public class ReverbSettings
{
    [Tooltip("Off: plays dry.")]
    public bool enabled = false;
    [Tooltip("A ready-made room. User: the settings below.")]
    public AudioReverbPreset preset = AudioReverbPreset.User;

    [Header("User preset")]
    [Tooltip("The unprocessed sound's level.")]
    [Range(-10000f, 0f)] public float dryLevel = 0f;
    [Tooltip("The room effect's overall level.")]
    [Range(-10000f, 0f)] public float room = -1000f;
    [Tooltip("The room effect at high frequencies - lower: a darker room.")]
    [Range(-10000f, 0f)] public float roomHF = -100f;
    [Tooltip("Seconds the reverb tail takes to die out.")]
    [Range(0.1f, 20f)] public float decayTime = 1.49f;
    [Tooltip("High frequencies' decay against the low ones' - under 1: highs die out first.")]
    [Range(0.1f, 2f)] public float decayHFRatio = 0.83f;
    [Tooltip("The early reflections' level.")]
    [Range(-10000f, 1000f)] public float reflectionsLevel = -2602f;
    [Tooltip("The late reverb's level - how wet it sounds.")]
    [Range(-10000f, 2000f)] public float reverbLevel = 200f;
    [Tooltip("Seconds between the early reflections and the late reverb.")]
    [Range(0f, 0.1f)] public float reverbDelay = 0.011f;
    [Tooltip("Echo density of the tail, %.")]
    [Range(0f, 100f)] public float diffusion = 100f;
    [Tooltip("Modal density of the tail, %.")]
    [Range(0f, 100f)] public float density = 100f;

    /// <summary>Puts the reverb on <paramref name="go"/> - added the first time it's enabled, bypassed while off.</summary>
    public void ApplyTo(GameObject go)
    {
        AudioReverbFilter filter = go.GetComponent<AudioReverbFilter>();
        if (!filter && enabled) filter = go.AddComponent<AudioReverbFilter>();
        if (!filter) return;

        filter.enabled = enabled;
        if (preset != AudioReverbPreset.User)
        {
            filter.reverbPreset = preset;
            return;
        }

        // Setting any of these switches the filter to the User preset.
        filter.dryLevel = dryLevel;
        filter.room = room;
        filter.roomHF = roomHF;
        filter.decayTime = decayTime;
        filter.decayHFRatio = decayHFRatio;
        filter.reflectionsLevel = reflectionsLevel;
        filter.reverbLevel = reverbLevel;
        filter.reverbDelay = reverbDelay;
        filter.diffusion = diffusion;
        filter.density = density;
    }
}
