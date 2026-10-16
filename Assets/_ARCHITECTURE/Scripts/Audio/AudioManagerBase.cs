using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class AudioManagerBase : MonoBehaviour
{
    public AudioSource defaultAudioSource;

    [Header("3D sound defaults")]
    public AudioSource default3DAudioSource;

    [HideInInspector] public float defaultMaxDistance;


    [Header("Pitch")]
    [Tooltip("Pitch used when none is passed in. 1 = normal.")]
    [Range(-3f, 3f)] public float defaultPitch = 1f;
    [Tooltip("If enabled, sounds played without an explicit pitch get a random pitch from defaultRandomPitchRange.")]
    public bool useDefaultRandomPitch = false;
    [Tooltip("Random pitch range (x = min, y = max) used when useDefaultRandomPitch is on.")]
    public Vector2 defaultRandomPitchRange = new Vector2(0.95f, 1.05f);

    public void Start()
    {
        if (default3DAudioSource)
            defaultMaxDistance = default3DAudioSource.maxDistance;

        PrepareTemplates();
    }

    /// <summary>
    /// Clears playOnAwake on the template AudioSources.
    /// <para>Every spawned sound is an Instantiate() of one of these templates. If a template has
    /// playOnAwake on, the clone tries to play the instant it awakes — before PlayClipIndependently
    /// has had a chance to assign a clip — and Unity logs "Only custom filters can be played..."
    /// every single time. A capture measured 11,288 of these in 40 seconds (4.7 per frame), each
    /// one paying for a managed stack trace: ~1.3 ms and ~84 KB of garbage per frame, for a warning
    /// nobody wanted.</para>
    /// </summary>
    private void PrepareTemplates()
    {
        if (defaultAudioSource) defaultAudioSource.playOnAwake = false;
        if (default3DAudioSource) default3DAudioSource.playOnAwake = false;
    }

    /// <summary>
    /// Resolves the pitch a sound should play at.
    /// Priority: explicit <paramref name="randomPitchRange"/> → explicit <paramref name="pitchIn"/> →
    /// <see cref="defaultRandomPitchRange"/> (if <see cref="useDefaultRandomPitch"/>) → <see cref="defaultPitch"/>.
    /// </summary>
    public float ResolvePitch(float? pitchIn = null, Vector2? randomPitchRange = null)
    {
        if (randomPitchRange.HasValue)
            return UnityEngine.Random.Range(randomPitchRange.Value.x, randomPitchRange.Value.y);

        if (pitchIn.HasValue) return pitchIn.Value;

        if (useDefaultRandomPitch)
            return UnityEngine.Random.Range(defaultRandomPitchRange.x, defaultRandomPitchRange.y);

        return defaultPitch;
    }

    public void InitSource(ref AudioSource source, AudioMixerGroup mixerGroup = null)
    {
        if (!source)
        {
            GameObject go = new GameObject("AudioSource");
            go.transform.SetParent(transform); // optional
            source = go.AddComponent<AudioSource>();
        }

        source.outputAudioMixerGroup = mixerGroup
            ? mixerGroup
            : AudioMixerManager.GetMasterGroup();
    }

    // ──────────────────────────────────────────────
    // 3D helpers
    // ──────────────────────────────────────────────

    /// <summary>
    /// Resolves the world position a spawned source should play at.
    /// Priority: explicit pos → atTransform → parent → this manager.
    /// </summary>
    private Vector3 ResolvePosition(Transform parent, Transform atTransform, Vector3? pos)
    {
        if (pos.HasValue) return pos.Value;
        if (atTransform) return atTransform.position;
        if (parent) return parent.position;
        return transform.position;
    }

    /// <summary>
    /// Applies spatialization settings to a spawned AudioSource.
    /// </summary>
    private void Configure3D(AudioSource source, bool is3D, float? maxDistance)
    {

        if (!is3D)
        {
            source.spatialBlend = 0f; // fully 2D
            return;
        }

        source.spatialBlend = 1f; // fully 3D
        source.maxDistance = maxDistance ?? source.maxDistance;
    }

    /// <summary>
    /// Adds/configures low-pass and high-pass filter components on a spawned AudioSource.
    /// A null cutoff means "no filter" — an existing filter component is then disabled so a
    /// cloned source never carries the prefab's filter settings unintentionally.
    /// </summary>
    private void ConfigureFilters(AudioSource source, float? lowPassCutoff, float? lowPassResonanceQ,
        float? highPassCutoff, float? highPassResonanceQ)
    {
        AudioLowPassFilter lowPass = source.GetComponent<AudioLowPassFilter>();
        if (lowPassCutoff.HasValue)
        {
            if (!lowPass) lowPass = source.gameObject.AddComponent<AudioLowPassFilter>();
            lowPass.enabled = true;
            lowPass.cutoffFrequency = lowPassCutoff.Value;
            if (lowPassResonanceQ.HasValue) lowPass.lowpassResonanceQ = lowPassResonanceQ.Value;
        }
        else if (lowPass) lowPass.enabled = false;

        AudioHighPassFilter highPass = source.GetComponent<AudioHighPassFilter>();
        if (highPassCutoff.HasValue)
        {
            if (!highPass) highPass = source.gameObject.AddComponent<AudioHighPassFilter>();
            highPass.enabled = true;
            highPass.cutoffFrequency = highPassCutoff.Value;
            if (highPassResonanceQ.HasValue) highPass.highpassResonanceQ = highPassResonanceQ.Value;
        }
        else if (highPass) highPass.enabled = false;
    }

    /// <summary>
    /// Instantiates a copy of the default AudioSource, placed and spatialized as requested.
    /// </summary>
    private AudioSource SpawnSource(Transform parent, bool is3D, float? maxDistance,
        Transform atTransform, Vector3? pos)
    {
        Vector3 position = ResolvePosition(parent, atTransform, pos);

        // A 3D sound follows atTransform if one was given; otherwise it stays where it was spawned.
        Transform sourceParent = is3D
            ? (atTransform ? atTransform : parent)
            : (parent ? parent : transform);

        AudioSource template = is3D ? default3DAudioSource : defaultAudioSource;

        // Belt and braces: a spawn can happen before Start(), and the clone would warn on Awake.
        if (template && template.playOnAwake) template.playOnAwake = false;

        AudioSource source = Instantiate(template, position, Quaternion.identity, sourceParent);
        Configure3D(source, is3D, maxDistance);
        return source;
    }

    // ──────────────────────────────────────────────
    // Independent playback (spawns its own AudioSource)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Plays audio clip.
    /// </summary>
    /// <param name="clip">Clip</param>
    /// <param name="volumeIn">Clip's volume</param>
    /// <param name="parent">Parent, where clip's AudioSource would be instantiated. If == null: no instantiation
    /// (plays in manager)</param>
    /// <param name="is3D">If true: spatial blend = 1 (fully 3D), maxDistance applied, source placed at
    /// <paramref name="pos"/> / <paramref name="atTransform"/>.</param>
    /// <param name="maxDistance">3D max distance. If null: <see cref="defaultMaxDistance"/>.</param>
    /// <param name="atTransform">Transform to play at (and follow). Optional.</param>
    /// <param name="pos">Explicit world position to play at. Takes priority over <paramref name="atTransform"/>.</param>
    /// <param name="pitchIn">Explicit pitch. If null: <see cref="defaultPitch"/> (or default random range).</param>
    /// <param name="randomPitchRange">Random pitch range (x = min, y = max). Takes priority over <paramref name="pitchIn"/>.</param>
    /// <param name="lowPassCutoff">Low-pass filter cutoff frequency (Hz). If null: no low-pass filter.</param>
    /// <param name="lowPassResonanceQ">Low-pass resonance Q. Only used when <paramref name="lowPassCutoff"/> is set.</param>
    /// <param name="highPassCutoff">High-pass filter cutoff frequency (Hz). If null: no high-pass filter.</param>
    /// <param name="highPassResonanceQ">High-pass resonance Q. Only used when <paramref name="highPassCutoff"/> is set.</param>
    /// <returns>The spawned AudioSource, so callers can stop it early. Null if nothing was played.</returns>
    public AudioSource PlayClipIndependently(AudioClip clip, float? volumeIn = null, Transform parent = null,
        bool is3D = false, float? maxDistance = null, Transform atTransform = null, Vector3? pos = null,
        float? pitchIn = null, Vector2? randomPitchRange = null,
        float? lowPassCutoff = null, float? lowPassResonanceQ = null,
        float? highPassCutoff = null, float? highPassResonanceQ = null)
    {
        try
        {
            if (clip == null) return null;

            float volume = volumeIn ?? defaultAudioSource.volume;
            float pitch = ResolvePitch(pitchIn, randomPitchRange);

            AudioSource audioSource = SpawnSource(parent, is3D, maxDistance, atTransform, pos);
            ConfigureFilters(audioSource, lowPassCutoff, lowPassResonanceQ, highPassCutoff, highPassResonanceQ);

            audioSource.clip = clip;
            audioSource.volume = volume;
            audioSource.pitch = pitch;

            audioSource.Play();

            // Pitch scales playback speed, so the real lifetime is length / |pitch|.
            float clipLength = audioSource.clip.length / Mathf.Max(Mathf.Abs(pitch), 0.01f);
            Destroy(audioSource.gameObject, clipLength);

            // h.Out("Played");
            return audioSource;
        }
        catch (Exception e)
        {
            h.Out($"Error playing audio clip: {e.Message}");
            return null;
        }
    }

    public void PlayClipIndependently(string path, float? volumeIn = null, Transform parent = null,
        bool is3D = false, float? maxDistance = null, Transform atTransform = null, Vector3? pos = null,
        float? pitchIn = null, Vector2? randomPitchRange = null)
    {
        PlayClipIndependently(Resources.Load<AudioClip>(path), volumeIn, parent,
            is3D, maxDistance, atTransform, pos, pitchIn, randomPitchRange);
    }

    public void PlayRandomClipIndependently(List<AudioClip> clip, float? volumeIn = null, Transform parent = null,
        bool is3D = false, float? maxDistance = null, Transform atTransform = null, Vector3? pos = null,
        float? pitchIn = null, Vector2? randomPitchRange = null)
    {
        PlayClipIndependently(h.RandChoice(clip), volumeIn, parent,
            is3D, maxDistance, atTransform, pos, pitchIn, randomPitchRange);
    }

    public void PlayRandomClipIndependently(List<string> paths, float? volumeIn = null, Transform parent = null,
        bool is3D = false, float? maxDistance = null, Transform atTransform = null, Vector3? pos = null,
        float? pitchIn = null, Vector2? randomPitchRange = null)
    {
        List<AudioClip> clips = new List<AudioClip>();
        foreach (string path in paths)
        {
            AudioClip clip = Resources.Load<AudioClip>(path);
            if (clip != null) clips.Add(clip);
        }
        PlayRandomClipIndependently(clips, volumeIn, parent,
            is3D, maxDistance, atTransform, pos, pitchIn, randomPitchRange);
    }

    public void PlayAudioResource(AudioResource resource, float? volumeIn = null, Transform parent = null,
        bool is3D = false, float? maxDistance = null, Transform atTransform = null, Vector3? pos = null,
        float? pitchIn = null, Vector2? randomPitchRange = null)
    {
        float volume = volumeIn ?? defaultAudioSource.volume;
        float pitch = ResolvePitch(pitchIn, randomPitchRange);

        // 3D always needs its own positioned source, so only fall back to the shared
        // manager source for plain 2D playback with no parent.
        if (parent == null && !is3D)
        {
            defaultAudioSource.volume = volume;
            defaultAudioSource.pitch = pitch;
            defaultAudioSource.resource = resource;
            defaultAudioSource.Play();
            return;
        }

        AudioSource audioSource = SpawnSource(parent, is3D, maxDistance, atTransform, pos);

        audioSource.resource = resource;
        audioSource.volume = volume;
        audioSource.pitch = pitch;

        audioSource.Play();

        // ⚠️ We cannot reliably get length from AudioResource
        Destroy(audioSource.gameObject, 10f); // fallback lifetime

        h.Out("Played");
    }

    public void PlayAudioResource(string path, float? volumeIn = null, Transform parent = null,
        bool is3D = false, float? maxDistance = null, Transform atTransform = null, Vector3? pos = null,
        float? pitchIn = null, Vector2? randomPitchRange = null)
    {
        AudioResource resource = Resources.Load<AudioResource>(path);
        if (resource != null)
        {
            PlayAudioResource(resource, volumeIn, parent, is3D, maxDistance, atTransform, pos,
                pitchIn, randomPitchRange);
        }
    }

    // ──────────────────────────────────────────────
    // Playback on the manager's shared AudioSource
    // ──────────────────────────────────────────────

    /// <param name="pitchIn">Explicit pitch. If null: <see cref="defaultPitch"/> (or default random range).</param>
    /// <param name="randomPitchRange">Random pitch range (x = min, y = max). Takes priority over <paramref name="pitchIn"/>.</param>
    /// <param name="playIndependently">if true, instantiates new audiosource. else, played on shared audiosource</param>
    /// <param name="lowPassCutoff">Low-pass filter cutoff frequency (Hz). If null: no low-pass filter.</param>
    /// <param name="highPassCutoff">High-pass filter cutoff frequency (Hz). If null: no high-pass filter.</param>
    /// <returns>The AudioSource the clip is playing on, so callers can stop it early.</returns>
    public AudioSource PlayClip(AudioClip clip, float? volumeIn = null,
        bool is3D = false, float? maxDistance = null, Transform atTransform = null, Vector3? pos = null,
        float? pitchIn = null, Vector2? randomPitchRange = null, bool playIndependently = true,
        float? lowPassCutoff = null, float? lowPassResonanceQ = null,
        float? highPassCutoff = null, float? highPassResonanceQ = null)
    {
        // 3D can't use the shared source (it lives on the manager), so spawn an independent one.
        if (is3D)
        {
            return PlayClipIndependently(clip, volumeIn, null, true, maxDistance, atTransform, pos,
                pitchIn, randomPitchRange, lowPassCutoff, lowPassResonanceQ, highPassCutoff, highPassResonanceQ);
        }

        // Filters live on the source's GameObject, so they can't be applied to the shared
        // manager source without leaking onto every later sound — force an independent one.
        if (playIndependently || lowPassCutoff.HasValue || highPassCutoff.HasValue)
        {
            return PlayClipIndependently(clip, volumeIn, null, false, maxDistance, atTransform, pos,
                pitchIn, randomPitchRange, lowPassCutoff, lowPassResonanceQ, highPassCutoff, highPassResonanceQ);
        }

        float volumeToUse = volumeIn ?? defaultAudioSource.volume;
        defaultAudioSource.volume = volumeToUse;
        defaultAudioSource.pitch = ResolvePitch(pitchIn, randomPitchRange);
        defaultAudioSource.clip = clip;
        defaultAudioSource.Play();
        return defaultAudioSource;
    }

    public void PlayClip(string path, float? volumeIn = null,
        bool is3D = false, float? maxDistance = null, Transform atTransform = null, Vector3? pos = null,
        float? pitchIn = null, Vector2? randomPitchRange = null)
    {
        AudioClip clip = Resources.Load<AudioClip>(path);
        if (clip != null)
        {
            PlayClipIndependently(clip, volumeIn, null, is3D, maxDistance, atTransform, pos,
                pitchIn, randomPitchRange);
        }
    }

    public void PlayRandomClip(List<AudioClip> clip, float? volumeIn = null,
        bool is3D = false, float? maxDistance = null, Transform atTransform = null, Vector3? pos = null,
        float? pitchIn = null, Vector2? randomPitchRange = null, bool playIndependently = true)
    {
        PlayClip(h.RandChoice(clip), volumeIn, is3D, maxDistance, atTransform, pos,
            pitchIn, randomPitchRange,playIndependently);
        // PlayClipIndependently(h.RandChoice(clip), volumeIn, null, is3D, maxDistance, atTransform, pos,
        //     pitchIn, randomPitchRange);
    }

    public void PlayRandomClip(List<string> paths, float? volumeIn = null,
        bool is3D = false, float? maxDistance = null, Transform atTransform = null, Vector3? pos = null,
        float? pitchIn = null, Vector2? randomPitchRange = null)
    {
        List<AudioClip> clips = new List<AudioClip>();
        foreach (string path in paths)
        {
            AudioClip clip = Resources.Load<AudioClip>(path);
            if (clip != null) clips.Add(clip);
        }
        PlayClipIndependently(h.RandChoice(clips), volumeIn, null, is3D, maxDistance, atTransform, pos,
            pitchIn, randomPitchRange);
    }
}
