using PrimeTween;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// A looping ambient noise bed under the music (room tone, traffic, fridge hum). Plays on its own AudioSource
/// through the mixer's SFX group, fades in as the scene starts, and can be faded or stopped from code or a
/// UnityEvent. Create one from <b>GameObject &gt; Audio &gt; Ambient Noise</b>.
/// </summary>
public class AmbientNoise : MonoBehaviour
{
    [Tooltip("The loop. It should loop seamlessly.")]
    [SerializeField] private AudioClip clip;
    [SerializeField, Range(0f, 1f)] private float volume = 0.4f;
    [SerializeField, Range(0.1f, 3f)] private float pitch = 1f;
    [Tooltip("Stereo position: -1 left, 0 centre, 1 right.")]
    [SerializeField, Range(-1f, 1f)] private float pan = 0f;

    [Header("Effects")]
    [Tooltip("Put on the source's GameObject. Editable in play mode.")]
    public LowPassSettings lowPass = new LowPassSettings();
    public HighPassSettings highPass = new HighPassSettings();
    public ReverbSettings reverb = new ReverbSettings();

    [Header("Playback")]
    [SerializeField] private bool playOnStart = true;
    [Tooltip("Seconds it fades in over when it starts.")]
    [SerializeField, Min(0f)] private float fadeIn = 2f;
    [Tooltip("Start the loop at a random point, so it doesn't open on the same sound every time.")]
    [SerializeField] private bool randomStartTime = true;
    [Tooltip("Route it through the mixer's SFX group, so the SFX volume slider affects it.")]
    [SerializeField] private bool useSfxMixerGroup = true;
    [Tooltip("Created on this GameObject when left empty.")]
    [SerializeField] private AudioSource source;

    private Tween fadeTween;
    private Tween panTween;
    private Tween lowPassTween;
    private Tween highPassTween;

    /// <summary>The loop is playing (fading out counts until it stops).</summary>
    public bool IsPlaying => source && source.isPlaying;

    /// <summary>The volume it plays at once faded in.</summary>
    public float Volume => volume;

    private void Awake()
    {
        if (!source) source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.volume = 0f;
        source.pitch = pitch;
        if (useSfxMixerGroup) source.outputAudioMixerGroup = AudioMixerManager.GetSFXGroup();
        ApplyEffects();
    }

    private void Start()
    {
        if (playOnStart) Play(fadeIn);
    }

    private void OnDestroy()
    {
        fadeTween.Stop();
        panTween.Stop();
        lowPassTween.Stop();
        highPassTween.Stop();
    }

    // Inspector tweaks are heard straight away in play mode.
    private void OnValidate()
    {
        if (!Application.isPlaying || !source) return;
        ApplyEffects();
        if (source.isPlaying && !fadeTween.isAlive) source.volume = volume;
    }

    /// <summary>Puts <see cref="pitch"/>, <see cref="pan"/> and the filters on the source.</summary>
    private void ApplyEffects()
    {
        source.pitch = pitch;
        source.panStereo = pan;
        highPass.ApplyTo(source.gameObject);
        lowPass.ApplyTo(source.gameObject);
        reverb.ApplyTo(source.gameObject);
    }

    /// <summary>Puts the effects back on the source after <see cref="lowPass"/>, <see cref="highPass"/> or <see cref="reverb"/> were changed from code.</summary>
    public void RefreshEffects()
    {
        if (source) ApplyEffects();
    }

    /// <summary>Moves the loop to <paramref name="target"/> (-1 left .. 1 right) over <paramref name="duration"/> seconds.</summary>
    public void TweenPan(float target, float duration)
    {
        panTween.Stop();
        target = Mathf.Clamp(target, -1f, 1f);
        if (duration <= 0f || !source || !isActiveAndEnabled)
        {
            pan = target;
            if (source) source.panStereo = pan;
            return;
        }

        panTween = Tween.Custom(this, pan, target, duration, (a, v) =>
        {
            a.pan = v;
            a.source.panStereo = v;
        });
    }

    /// <summary>Sets <see cref="pan"/> at once (-1 left .. 1 right). Convenient for UnityEvents and sliders.</summary>
    public void SetPan(float target) => TweenPan(target, 0f);

    /// <summary>Sweeps the low-pass to <paramref name="cutoff"/> Hz over <paramref name="duration"/> seconds, evenly in pitch. Turns it on.</summary>
    public void TweenLowPassCutoff(float cutoff, float duration)
    {
        lowPassTween.Stop();
        lowPass.enabled = true;
        lowPassTween = SweepCutoff(lowPass.cutoff, cutoff, duration, (a, v) =>
        {
            a.lowPass.cutoff = v;
            if (a.source) a.lowPass.ApplyTo(a.source.gameObject);
        });
    }

    /// <summary>Sweeps the high-pass to <paramref name="cutoff"/> Hz over <paramref name="duration"/> seconds, evenly in pitch. Turns it on.</summary>
    public void TweenHighPassCutoff(float cutoff, float duration)
    {
        highPassTween.Stop();
        highPass.enabled = true;
        highPassTween = SweepCutoff(highPass.cutoff, cutoff, duration, (a, v) =>
        {
            a.highPass.cutoff = v;
            if (a.source) a.highPass.ApplyTo(a.source.gameObject);
        });
    }

    /// <summary>Turns the low-pass on or off.</summary>
    public void SetLowPassEnabled(bool on)
    {
        lowPassTween.Stop();
        lowPass.enabled = on;
        RefreshEffects();
    }

    /// <summary>Turns the high-pass on or off.</summary>
    public void SetHighPassEnabled(bool on)
    {
        highPassTween.Stop();
        highPass.enabled = on;
        RefreshEffects();
    }

    /// <summary>Turns the reverb on or off.</summary>
    public void SetReverbEnabled(bool on)
    {
        reverb.enabled = on;
        RefreshEffects();
    }

    /// <summary>Sweeps a cutoff from <paramref name="from"/> to <paramref name="to"/> Hz, handing each step to <paramref name="set"/>.</summary>
    private Tween SweepCutoff(float from, float to, float duration, System.Action<AmbientNoise, float> set)
    {
        if (duration <= 0f || !isActiveAndEnabled)
        {
            set(this, to);
            return default;
        }

        return Tween.Custom(this, 0f, 1f, duration, (a, t) => set(a, GlobalLowPass.Blend(from, to, t)));
    }

    /// <summary>Starts the loop (if it isn't playing) and fades it in to <see cref="Volume"/> over <paramref name="fade"/> seconds.</summary>
    public void Play(float fade)
    {
        if (!clip)
        {
            h.Out($"AmbientNoise '{name}': no clip to play.");
            return;
        }

        if (!source.isPlaying || source.clip != clip)
        {
            fadeTween.Stop();
            source.clip = clip;
            source.volume = 0f;
            source.pitch = pitch;
            source.time = randomStartTime ? Random.Range(0f, clip.length * 0.95f) : 0f;
            source.Play();
        }

        FadeTo(volume, fade);
    }

    /// <summary>Starts the loop with <see cref="fadeIn"/>. Convenient for UnityEvents.</summary>
    public void Play() => Play(fadeIn);

    /// <summary>Swaps in <paramref name="newClip"/>: fades the old one out, then the new one in, over <paramref name="fade"/> seconds each.</summary>
    public void Play(AudioClip newClip, float fade)
    {
        if (newClip == clip && IsPlaying)
        {
            FadeTo(volume, fade);
            return;
        }

        clip = newClip;
        if (!IsPlaying || fade <= 0f)
        {
            if (source.isPlaying) source.Stop();
            Play(fade);
            return;
        }

        fadeTween.Stop();
        fadeTween = Tween.AudioVolume(source, 0f, fade).OnComplete(this, a =>
        {
            a.source.Stop();
            a.Play(fade);
        });
    }

    /// <summary>Fades the loop out over <paramref name="fade"/> seconds and stops it.</summary>
    public void Stop(float fade)
    {
        if (!IsPlaying) return;

        fadeTween.Stop();
        if (fade <= 0f) source.Stop();
        else fadeTween = Tween.AudioVolume(source, 0f, fade).OnComplete(source, s => s.Stop());
    }

    /// <summary>Stops the loop at once. Convenient for UnityEvents.</summary>
    public void Stop() => Stop(0f);

    /// <summary>Changes <see cref="Volume"/>; a playing loop eases to it over <paramref name="fade"/> seconds.</summary>
    public void SetVolume(float newVolume, float fade)
    {
        volume = Mathf.Clamp01(newVolume);
        if (IsPlaying) FadeTo(volume, fade);
    }

    /// <summary>Sets <see cref="Volume"/> at once. Convenient for UnityEvents and sliders.</summary>
    public void SetVolume(float newVolume) => SetVolume(newVolume, 0f);

    private void FadeTo(float target, float fade)
    {
        fadeTween.Stop();
        if (fade <= 0f) source.volume = target;
        else fadeTween = Tween.AudioVolume(source, target, fade);
    }

#if UNITY_EDITOR
    [MenuItem("GameObject/Audio/Ambient Noise", false, 10)]
    private static void Create(MenuCommand command)
    {
        GameObject go = new GameObject("Ambient Noise", typeof(AmbientNoise));
        GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
        Undo.RegisterCreatedObjectUndo(go, "Create Ambient Noise");
        Selection.activeObject = go;
    }
#endif
}
