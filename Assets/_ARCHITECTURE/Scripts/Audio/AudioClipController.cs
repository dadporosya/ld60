using UnityEngine;

/// <summary>
/// Generates modified copies of an AudioClip by resampling (pitch) and scaling (volume).
/// Resampling changes duration proportionally to the pitch factor.
/// </summary>
[System.Serializable]
public static class AudioClipController
{
    // ──────────────────────────────────────────────
    // Fields
    // ──────────────────────────────────────────────
    

    [Header("Base Preferences")]
    [Range(0.1f, 3f)]  const float basePitch  = 1f;
    [Range(0f,   1f)]  const float baseVolume = 1f;

    [Header("Delta Clamp Limits")]
    const float minPitch  = 0.1f;
    const float maxPitch  = 3f;
    const float minVolume = 0f;
    const float maxVolume = 1f;

    // ──────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────

    /// <summary>
    /// Returns a new AudioClip with modified pitch and volume.
    /// </summary>
    /// <param name="pitch">Base pitch multiplier (1 = original speed/pitch).</param>
    /// <param name="volume">Base volume scale (0–1).</param>
    /// <param name="dPitch">Delta: exact amount added to pitch.</param>
    /// <param name="dVolume">Delta: exact amount added to volume.</param>
    /// <returns>New AudioClip instance, or null if source clip is missing.</returns>
    public static AudioClip Generate(AudioClip clip, float pitch = 1f, float volume = 1f,
                              float dPitch = 0f, float dVolume = 0f, float? length=null)
    {
        if (clip == null)
        {
            Debug.LogWarning("[AudioClipController] No AudioClip assigned.");
            return null;
        }

        float finalPitch  = Mathf.Clamp(pitch  + dPitch,  minPitch,  maxPitch);
        float finalVolume = Mathf.Clamp(volume + dVolume, minVolume, maxVolume);

        float[] originalSamples = GetSourceSamples(clip);
        float[] pitchedSamples  = Resample(originalSamples, clip.samples, clip.channels, finalPitch);
        ApplyVolume(pitchedSamples, finalVolume);

        // Crop the clip to the specified length if provided
        if (length != null)
        {
            int targetSampleCount = Mathf.RoundToInt(length.Value * clip.frequency);
            int pitchedTargetSampleCount = Mathf.RoundToInt(targetSampleCount / finalPitch);
            
            if (pitchedTargetSampleCount < pitchedSamples.Length / clip.channels)
            {
                int newLength = pitchedTargetSampleCount * clip.channels;
                float[] croppedSamples = new float[newLength];
                System.Array.Copy(pitchedSamples, croppedSamples, newLength);
                pitchedSamples = croppedSamples;
            }
        }

        return BuildClip(clip, pitchedSamples, finalPitch, finalVolume);
    }



    // ──────────────────────────────────────────────
    // Private Helpers
    // ──────────────────────────────────────────────

    /// <summary>
    /// Reads all interleaved PCM samples from the source clip.
    /// </summary>
    private static float[] GetSourceSamples(AudioClip clip)
    {
        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);
        return samples;
    }

    /// <summary>
    /// Resamples interleaved PCM data by a pitch factor using linear interpolation.
    /// pitchFactor > 1 → higher pitch, shorter clip.
    /// pitchFactor < 1 → lower pitch,  longer clip.
    /// </summary>
    private static float[] Resample(float[] src, int srcSampleCount,
                                     int channels, float pitchFactor)
    {
        // New sample count is inversely proportional to pitch
        int newSampleCount = Mathf.Max(1, Mathf.RoundToInt(srcSampleCount / pitchFactor));
        float[] dst = new float[newSampleCount * channels];

        for (int i = 0; i < newSampleCount; i++)
        {
            // Map destination index back to a floating-point source position
            float srcPos = i * pitchFactor;
            int   idxA   = Mathf.FloorToInt(srcPos);
            int   idxB   = Mathf.Min(idxA + 1, srcSampleCount - 1); // clamp to avoid OOB
            float t      = srcPos - idxA; // interpolation weight

            for (int c = 0; c < channels; c++)
            {
                float sampleA = src[idxA * channels + c];
                float sampleB = src[idxB * channels + c];
                dst[i * channels + c] = Mathf.LerpUnclamped(sampleA, sampleB, t);
            }
        }

        return dst;
    }

    /// <summary>
    /// Scales every sample in-place by the volume factor.
    /// </summary>
    private static void ApplyVolume(float[] samples, float volume)
    {
        for (int i = 0; i < samples.Length; i++)
            samples[i] *= volume;
    }

    /// <summary>
    /// Creates and populates a new AudioClip from the processed sample buffer.
    /// </summary>
    private static AudioClip BuildClip(AudioClip clip, float[] samples, float pitch, float volume)
    {
        int newSampleCount = samples.Length / clip.channels;

        AudioClip result = AudioClip.Create(
            name:       $"{clip.name}_p{pitch:F2}_v{volume:F2}",
            lengthSamples: newSampleCount,
            channels:   clip.channels,
            frequency:  clip.frequency,
            stream:     false             // false = load all data upfront (safe for short SFX)
        );

        result.SetData(samples, 0);
        return result;
    }
}