using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays a list of AudioClips in order, one per "frame", using the same gap timing
/// convention as <see cref="SpriteChangeAnimation"/> (per-frame gap with a default fallback).
/// All playback goes through <see cref="SFXManager"/>.
/// </summary>
public class AudioAnimation : AnimationBase
{
    [Header("AudioAnimation Settings")]
    [Tooltip("Clips played in order, one per frame.")]
    public List<AudioClip> audioClips = new List<AudioClip>();
    [Tooltip("Gap after the i-th frame. If shorter than the frame list, defaultGapsBetweenFrames is used.")]
    public List<float> gapsBetweenFrames = new List<float>();
    public float defaultGapsBetweenFrames;

    [Header("SFX Settings")]
    [Tooltip("If true, the sound is spatialized (3D) and played at this transform.")]
    public bool sound3D = false;
    [Tooltip("maxDistance = SFXManager's default 3D source maxDistance * this multiplier.")]
    public float maxDistanceMult = 1f;
    [Tooltip("Volume passed to SFXManager. <= 0 means 'use the manager's default volume'.")]
    public float volumeIn = -1f;
    [Tooltip("Explicit pitch. <= 0 means 'use the manager's default pitch'. Ignored if randomPitchRange is set.")]
    public float pitchIn = -1f;
    [Tooltip("Random pitch range (x = min, y = max). Takes priority over pitchIn. Leave at (0,0) to disable.")]
    public Vector2 randomPitchRange = Vector2.zero;

    [Header("Filters")]
    [Tooltip("If true, a low-pass filter with the values below is applied to the played sounds.")]
    public bool lpf = false;
    [Tooltip("Low-pass cutoff frequency in Hz. Everything above it is muffled.")]
    [Range(10f, 22000f)] public float lpfCutoff = 5000f;
    [Range(1f, 10f)] public float lpfResonanceQ = 1f;

    [Tooltip("If true, a high-pass filter with the values below is applied to the played sounds.")]
    public bool hpf = false;
    [Tooltip("High-pass cutoff frequency in Hz. Everything below it is cut.")]
    [Range(10f, 22000f)] public float hpfCutoff = 500f;
    [Range(1f, 10f)] public float hpfResonanceQ = 1f;

    // ──────────────────────────────────────────────
    // Runtime suppression
    // ──────────────────────────────────────────────

    /// <summary>
    /// Hard mute, set by code at runtime and never serialized. A suppressed animation still runs
    /// its coroutine and keeps its timing — it just plays no clip. Used to remove idle-wandering
    /// step sounds outright instead of relying on a very low volume.
    /// </summary>
    [System.NonSerialized] public bool suppressed;

    // ──────────────────────────────────────────────
    // Frame source (overridden by subclasses)
    // ──────────────────────────────────────────────

    protected virtual int FrameCount => audioClips.Count;

    protected virtual AudioClip GetClipForFrame(int i)
    {
        return (i >= 0 && i < audioClips.Count) ? audioClips[i] : null;
    }

    protected float GetGap(int i)
    {
        return (i < gapsBetweenFrames.Count) ? gapsBetweenFrames[i] : defaultGapsBetweenFrames;
    }

    // ──────────────────────────────────────────────
    // Playback
    // ──────────────────────────────────────────────

    /// <summary>
    /// Plays a single clip through <see cref="SFXManager"/> with this component's settings.
    /// Sentinel values (&lt;= 0 for volume/pitch, (0,0) for the random range) fall back to the manager's defaults.
    /// </summary>
    protected void PlaySFX(AudioClip clip)
    {
        if (clip == null) return;
        if (suppressed) return;
        if (SFXManager.Instance == null)
        {
            h.Out("AudioAnimation: no SFXManager instance.");
            return;
        }

        float? volume = volumeIn >= 0f ? volumeIn : (float?)null;
        float? pitch = pitchIn > 0f ? pitchIn : (float?)null;
        Vector2? pitchRange = randomPitchRange != Vector2.zero ? randomPitchRange : (Vector2?)null;

        // maxDistance is derived from the manager's 3D source distance; null keeps the source's own value.
        float? maxDistance = null;
        if (sound3D && SFXManager.Instance.defaultMaxDistance > 0f)
            maxDistance = SFXManager.Instance.defaultMaxDistance * maxDistanceMult;

        SFXManager.Instance.PlayClip(
            clip,
            volumeIn: volume,
            is3D: sound3D,
            maxDistance: maxDistance,
            atTransform: sound3D ? transform : null,
            pitchIn: pitch,
            randomPitchRange: pitchRange,
            lowPassCutoff: lpf ? lpfCutoff : (float?)null,
            lowPassResonanceQ: lpf ? lpfResonanceQ : (float?)null,
            highPassCutoff: hpf ? hpfCutoff : (float?)null,
            highPassResonanceQ: hpf ? hpfResonanceQ : (float?)null);
    }

    public override IEnumerator AnimationCoroutine()
    {
        int count = FrameCount;
        for (int i = 0; i < count; i++)
        {
            PlaySFX(GetClipForFrame(i));
            yield return new WaitForSeconds(GetGap(i));
        }

        yield return base.AnimationCoroutine();
    }
}
