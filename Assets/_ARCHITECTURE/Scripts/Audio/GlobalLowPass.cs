using UnityEngine;

/// <summary>
/// A low-pass filter over everything the player hears: an <see cref="AudioLowPassFilter"/> on the scene's
/// <see cref="AudioListener"/>, which filters the final mix — every AudioSource at once, whichever manager
/// (<see cref="SFXManager"/>, <see cref="BGMManager"/>) or mixer group it plays through, including sounds that
/// start after the filter is on.
///
/// <para><see cref="SetCutoff"/> turns it on (or moves it); <see cref="Clear"/> takes it off again. Whoever turns it
/// on must clear it — the filter otherwise stays for as long as its listener lives.</para>
/// </summary>
public static class GlobalLowPass
{
    /// <summary>Cutoff (Hz) at which the filter lets everything through.</summary>
    public const float OpenCutoff = 22000f;
    /// <summary>Lowest cutoff (Hz) an <see cref="AudioLowPassFilter"/> takes.</summary>
    public const float MinCutoff = 10f;

    private static AudioLowPassFilter filter;
    // The filter was put on the listener by this class (rather than authored there), so Clear removes it.
    private static bool added;
    private static float authoredCutoff;
    private static float authoredResonance;
    private static bool authoredEnabled;

    /// <summary>The filter is on.</summary>
    public static bool IsActive => filter;

    /// <summary>
    /// Filters everything heard down to <paramref name="cutoff"/> Hz. Call it every frame to sweep the cutoff.
    /// No-op without an AudioListener in the scene.
    /// </summary>
    public static void SetCutoff(float cutoff, float resonanceQ = 1f)
    {
        if (!filter && !Acquire()) return;

        filter.enabled = true;
        filter.cutoffFrequency = Mathf.Clamp(cutoff, MinCutoff, OpenCutoff);
        filter.lowpassResonanceQ = Mathf.Max(1f, resonanceQ);
    }

    /// <summary>Takes the filter off: removed if this class added it, put back as it was authored otherwise.</summary>
    public static void Clear()
    {
        if (!filter) return;

        if (added) Object.Destroy(filter);
        else
        {
            filter.cutoffFrequency = authoredCutoff;
            filter.lowpassResonanceQ = authoredResonance;
            filter.enabled = authoredEnabled;
        }
        filter = null;
    }

    /// <summary>
    /// The cutoff <paramref name="t"/> (0..1) of the way from <paramref name="from"/> to <paramref name="to"/> Hz,
    /// moving evenly in pitch (octaves) rather than in Hz — a straight Hz blend spends nearly all of its range
    /// where nothing audible changes, then closes all at once.
    /// </summary>
    public static float Blend(float from, float to, float t)
    {
        from = Mathf.Clamp(from, MinCutoff, OpenCutoff);
        to = Mathf.Clamp(to, MinCutoff, OpenCutoff);
        return from * Mathf.Pow(to / from, Mathf.Clamp01(t));
    }

    private static bool Acquire()
    {
        AudioListener listener = Object.FindFirstObjectByType<AudioListener>();
        if (!listener)
        {
            h.Out("GlobalLowPass: no AudioListener in the scene to filter.");
            return false;
        }

        added = !listener.TryGetComponent(out filter);
        if (added) filter = listener.gameObject.AddComponent<AudioLowPassFilter>();
        else
        {
            authoredCutoff = filter.cutoffFrequency;
            authoredResonance = filter.lowpassResonanceQ;
            authoredEnabled = filter.enabled;
        }
        return true;
    }
}
