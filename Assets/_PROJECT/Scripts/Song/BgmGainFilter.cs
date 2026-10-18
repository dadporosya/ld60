using UnityEngine;

/// <summary>
/// Scales what the AudioSource on its object plays by <see cref="gain"/>, on top of the source's own volume (which
/// the <see cref="BGMManager"/>'s fades drive), so it never fights a crossfade. A change ramps over one audio buffer,
/// so a sweep doesn't crackle. Added to the music sources by <see cref="SongManager"/>.
/// </summary>
[DisallowMultipleComponent]
public class BgmGainFilter : MonoBehaviour
{
    [Range(0f, 1f)] public float gain = 1f;

    // The gain the last buffer ended on (audio thread).
    private float applied = 1f;

    private void OnAudioFilterRead(float[] data, int channels)
    {
        float from = applied;
        float to = gain;
        applied = to;
        if (from == 1f && to == 1f) return;

        int frames = data.Length / channels;
        float step = frames > 1 ? (to - from) / (frames - 1) : 0f;
        for (int f = 0; f < frames; f++)
        {
            float g = frames > 1 ? from + step * f : to;
            int start = f * channels;
            for (int c = 0; c < channels; c++) data[start + c] *= g;
        }
    }
}
