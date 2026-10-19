using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// The music's low-pass, high-pass and volume on the Web, where Unity's audio filter components and
/// <c>OnAudioFilterRead</c> (<see cref="BgmGainFilter"/>) don't run: <c>Plugins/WebGL/MusicFilter.jslib</c> puts
/// Web Audio nodes on the music's channels instead. <see cref="SongManager"/> marks its music sources
/// (<see cref="Mark"/>) and hands its settings over every frame (<see cref="Apply"/>). The reverb has no Web
/// counterpart. Does nothing outside Web builds.
/// </summary>
public static class WebMusicFilter
{
    /// <summary>
    /// The spatial blend the music sources are given so the plugin can tell their channels apart from every other
    /// sound's. Small enough to be inaudible; exactly representable, so it reaches the browser unchanged.
    /// </summary>
    public const float Tag = 1f / 8192f;

#if UNITY_WEBGL && !UNITY_EDITOR
    /// <summary>This build plays its music through the plugin's filters.</summary>
    public static bool Enabled => true;

    [DllImport("__Internal")]
    private static extern void MusicFilter_Apply(float tag, bool lowPassOn, float lowCutoff, float lowQ,
        bool highPassOn, float highCutoff, float highQ, float gain);
#else
    /// <summary>This build plays its music through the plugin's filters.</summary>
    public static bool Enabled => false;

    private static void MusicFilter_Apply(float tag, bool lowPassOn, float lowCutoff, float lowQ,
        bool highPassOn, float highCutoff, float highQ, float gain) { }
#endif

    /// <summary>Marks <paramref name="source"/> as music, for <see cref="Apply"/> to filter.</summary>
    public static void Mark(AudioSource source)
    {
        source.spatialBlend = Tag;
        // Now (barely) 3D: keep the camera's movement from bending the music's pitch.
        source.dopplerLevel = 0f;
    }

    /// <summary>Puts the filters and <paramref name="volume"/> (0..1) on every marked source's sound.</summary>
    public static void Apply(LowPassSettings lowPass, HighPassSettings highPass, float volume)
    {
        MusicFilter_Apply(Tag,
            lowPass.enabled, Mathf.Clamp(lowPass.cutoff, GlobalLowPass.MinCutoff, GlobalLowPass.OpenCutoff), lowPass.resonanceQ,
            highPass.enabled, Mathf.Clamp(highPass.cutoff, GlobalLowPass.MinCutoff, GlobalLowPass.OpenCutoff), highPass.resonanceQ,
            Mathf.Clamp01(volume));
    }
}
