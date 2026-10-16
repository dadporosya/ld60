using PrimeTween;
using UnityEngine;

/// <summary>
/// A looping "SFX texture" that plays under the music (metro rumble, rain, crowd noise).
/// Runs on two AudioSources so a change of texture crossfades instead of cutting. Asking for the
/// clip that is already playing only adjusts its volume - it never restarts.
/// </summary>
public class VisualNovelAmbience : MonoBehaviour
{
    [Tooltip("Created on this GameObject when left empty.")]
    [SerializeField] private AudioSource sourceA;
    [Tooltip("Created on this GameObject when left empty.")]
    [SerializeField] private AudioSource sourceB;
    [Tooltip("Route the sources through the mixer's SFX group, so the SFX volume slider affects them.")]
    [SerializeField] private bool useSfxMixerGroup = true;

    private AudioSource current;

    /// <summary>The texture playing right now, or null.</summary>
    public AudioClip CurrentClip => current && current.isPlaying ? current.clip : null;

    private void Awake()
    {
        sourceA = PrepareSource(sourceA);
        sourceB = PrepareSource(sourceB);
    }

    /// <summary>Crossfades into <paramref name="clip"/>. A null clip fades the layer out.</summary>
    public void Play(AudioClip clip, float volume, float fade)
    {
        if (!clip)
        {
            Stop(fade);
            return;
        }

        if (current && current.clip == clip && current.isPlaying)
        {
            FadeTo(current, volume, fade);
            return;
        }

        AudioSource next = current == sourceA ? sourceB : sourceA;
        if (!next) return;

        FadeOutAndStop(current, fade);

        Tween.StopAll(next);
        next.clip = clip;
        next.volume = 0f;
        next.Play();
        FadeTo(next, volume, fade);

        current = next;
    }

    /// <summary>Fades the layer out and stops it.</summary>
    public void Stop(float fade)
    {
        FadeOutAndStop(current, fade);
        current = null;
    }

    private AudioSource PrepareSource(AudioSource source)
    {
        if (!source) source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        if (useSfxMixerGroup) source.outputAudioMixerGroup = AudioMixerManager.GetSFXGroup();
        return source;
    }

    private static void FadeTo(AudioSource source, float volume, float fade)
    {
        Tween.StopAll(source);
        if (fade <= 0f) source.volume = volume;
        else Tween.AudioVolume(source, volume, fade);
    }

    private static void FadeOutAndStop(AudioSource source, float fade)
    {
        if (!source || !source.isPlaying) return;

        Tween.StopAll(source);
        if (fade <= 0f)
        {
            source.Stop();
            return;
        }

        Tween.AudioVolume(source, 0f, fade).OnComplete(source, s => s.Stop());
    }
}
