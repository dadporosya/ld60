using System;
using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
using UnityEngine.Events;

/// <summary>A low-pass filter on the music alone (SFX stay clear).</summary>
[Serializable]
public class BgmLowPass
{
    [Tooltip("Off: the music plays unfiltered.")]
    public bool enabled = false;
    [Tooltip("Hz. Everything above it is cut - lower sounds more muffled. 22000: fully open.")]
    [Range(GlobalLowPass.MinCutoff, GlobalLowPass.OpenCutoff)] public float cutoff = 5000f;
    [Tooltip("Boost right at the cutoff. 1: none.")]
    [Range(1f, 10f)] public float resonanceQ = 1f;
}

/// <summary>A high-pass filter on the music alone (SFX stay clear).</summary>
[Serializable]
public class BgmHighPass
{
    [Tooltip("Off: the music plays unfiltered.")]
    public bool enabled = false;
    [Tooltip("Hz. Everything below it is cut - higher sounds thinner, like a small speaker. 10: fully open.")]
    [Range(GlobalLowPass.MinCutoff, GlobalLowPass.OpenCutoff)] public float cutoff = 500f;
    [Tooltip("Boost right at the cutoff. 1: none.")]
    [Range(1f, 10f)] public float resonanceQ = 1f;
}

/// <summary>A reverb on the music alone (SFX stay dry). Levels are in millibels (-10000: silent, 0: full).</summary>
[Serializable]
public class BgmReverb
{
    [Tooltip("Off: the music plays dry.")]
    public bool enabled = false;
    [Tooltip("A ready-made room. User: the settings below.")]
    public AudioReverbPreset preset = AudioReverbPreset.User;

    [Header("User preset")]
    [Tooltip("The unprocessed music's level.")]
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
}

/// <summary>
/// Picks Funeral Song's background music and hands it to the <see cref="BGMManager"/>: the start track once the
/// game starts, then, after each round's final cutscene (its level's win / loss dialogue), the track for how many
/// rounds have been played (<see cref="bgmByRound"/>) - whichever level it was, like
/// <see cref="LevelManager.PointsToPass"/>. Every track loops and becomes the BGMManager's default, so dialogues
/// that swap the music in come back to it. Just music: nothing ends when a track does.
/// Access via <c>SongManager.Instance</c>.
/// </summary>
public class SongManager : MonoBehaviour
{
    public static SongManager Instance;

    [Tooltip("Defaults to BGMManager.Instance.")]
    [SerializeField] private BGMManager bgmManager;

    [Tooltip("Played when the game starts (after the intro dialogue).")]
    [SerializeField] private BGMObject startBgm;
    [Tooltip("Seconds the start track fades in over.")]
    [SerializeField, Min(0f)] private float startFade = 1.5f;
    [Tooltip("Played once a round's final cutscene (win / loss dialogue) is over, by how many rounds have been " +
             "played this game: [0] after the first round, [1] after the second, ... - whichever level it was. " +
             "An empty entry, or a round past the end of the list: the music carries on as it is.")]
    [SerializeField] private List<BGMObject> bgmByRound = new List<BGMObject>();
    [Tooltip("Seconds a round's track crossfades in over once its final cutscene is over.")]
    [SerializeField, Min(0f)] private float levelFade = 1.5f;

    [Header("Effects")]
    [Tooltip("The music's overall volume, on top of the BGMManager's volume and fades (and dialogues' music volume).")]
    [Range(0f, 1f)] public float volume = 1f;
    [Tooltip("Put on every music source, so it carries over crossfades and track changes. Editable in play mode.")]
    public BgmLowPass lowPass = new BgmLowPass();
    public BgmHighPass highPass = new BgmHighPass();
    public BgmReverb reverb = new BgmReverb();

    /// <summary>The track that just started.</summary>
    public UnityEvent<BGMObject> onBgmChanged = new UnityEvent<BGMObject>();

    /// <summary>The track last started through the manager; null before the first one, or after <see cref="Stop"/>.</summary>
    public BGMObject Current { get; private set; }

    private BGMManager Bgm => bgmManager ? bgmManager : bgmManager = BGMManager.Instance;

    // The effects changed since they were last put on the sources, and how many sources had them then.
    private bool effectsDirty = true;
    private int filteredSources = -1;
    private Tween volumeTween;
    private Tween lowPassTween;
    private Tween highPassTween;
    private Tween reverbTween;

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance, setDontDestroy: false);
    }

    private void OnValidate() => effectsDirty = true;

    // Late: a crossfade that grew the source pool this frame gets the filters before it's heard much.
    private void LateUpdate()
    {
        BGMManager bgm = Bgm;
        if (!bgm) return;

        int count = bgm.MusicSourceCount;
        if (!effectsDirty && count == filteredSources) return;
        effectsDirty = false;
        filteredSources = count;

        foreach (AudioSource source in bgm.MusicSources) ApplyEffects(source);
    }

    /// <summary>
    /// Puts <see cref="volume"/>, <see cref="lowPass"/>, <see cref="highPass"/> and <see cref="reverb"/> on
    /// <paramref name="source"/>, adding each filter when first needed.
    /// </summary>
    private void ApplyEffects(AudioSource source)
    {
        GameObject go = source.gameObject;

        BgmGainFilter gain = go.GetComponent<BgmGainFilter>();
        if (!gain && volume < 1f) gain = go.AddComponent<BgmGainFilter>();
        if (gain) gain.gain = Mathf.Clamp01(volume);

        AudioHighPassFilter hpf = go.GetComponent<AudioHighPassFilter>();
        if (!hpf && highPass.enabled) hpf = go.AddComponent<AudioHighPassFilter>();
        if (hpf)
        {
            hpf.enabled = highPass.enabled;
            hpf.cutoffFrequency = Mathf.Clamp(highPass.cutoff, GlobalLowPass.MinCutoff, GlobalLowPass.OpenCutoff);
            hpf.highpassResonanceQ = Mathf.Max(1f, highPass.resonanceQ);
        }

        AudioLowPassFilter lpf = go.GetComponent<AudioLowPassFilter>();
        if (!lpf && lowPass.enabled) lpf = go.AddComponent<AudioLowPassFilter>();
        if (lpf)
        {
            lpf.enabled = lowPass.enabled;
            lpf.cutoffFrequency = Mathf.Clamp(lowPass.cutoff, GlobalLowPass.MinCutoff, GlobalLowPass.OpenCutoff);
            lpf.lowpassResonanceQ = Mathf.Max(1f, lowPass.resonanceQ);
        }

        AudioReverbFilter rf = go.GetComponent<AudioReverbFilter>();
        if (!rf && reverb.enabled) rf = go.AddComponent<AudioReverbFilter>();
        if (!rf) return;

        rf.enabled = reverb.enabled;
        if (reverb.preset != AudioReverbPreset.User)
        {
            rf.reverbPreset = reverb.preset;
            return;
        }

        // Setting any of these switches the filter to the User preset.
        rf.dryLevel = reverb.dryLevel;
        rf.room = reverb.room;
        rf.roomHF = reverb.roomHF;
        rf.decayTime = reverb.decayTime;
        rf.decayHFRatio = reverb.decayHFRatio;
        rf.reflectionsLevel = reverb.reflectionsLevel;
        rf.reverbLevel = reverb.reverbLevel;
        rf.reverbDelay = reverb.reverbDelay;
        rf.diffusion = reverb.diffusion;
        rf.density = reverb.density;
    }

    /// <summary>Puts the effects back on the music after their fields were changed from code.</summary>
    public void RefreshEffects() => effectsDirty = true;

    /// <summary>Fades the music's overall <see cref="volume"/> (0..1) to <paramref name="target"/> over <paramref name="duration"/> seconds.</summary>
    public void TweenVolume(float target, float duration)
    {
        volumeTween.Stop();
        target = Mathf.Clamp01(target);
        effectsDirty = true;

        if (duration <= 0f || !isActiveAndEnabled)
        {
            volume = target;
            return;
        }

        volumeTween = Tween.Custom(this, volume, target, duration, (manager, v) =>
        {
            manager.volume = v;
            manager.effectsDirty = true;
        });
    }

    /// <summary>Sets the music's overall <see cref="volume"/> (0..1) at once. Convenient for UnityEvents.</summary>
    public void SetVolume(float target) => TweenVolume(target, 0f);

    /// <summary>Turns the music's high-pass on or off.</summary>
    public void SetHighPassEnabled(bool on)
    {
        highPassTween.Stop();
        highPass.enabled = on;
        effectsDirty = true;
    }

    /// <summary>
    /// Sweeps the music's high-pass to <paramref name="cutoff"/> Hz over <paramref name="duration"/> seconds, evenly in
    /// pitch (see <see cref="GlobalLowPass.Blend"/>). Turns the filter on.
    /// </summary>
    public void TweenHighPassCutoff(float cutoff, float duration)
    {
        highPassTween.Stop();
        highPass.enabled = true;
        effectsDirty = true;

        float from = highPass.cutoff;
        if (duration <= 0f || !isActiveAndEnabled)
        {
            highPass.cutoff = cutoff;
            return;
        }

        highPassTween = Tween.Custom(this, 0f, 1f, duration, (manager, t) =>
        {
            manager.highPass.cutoff = GlobalLowPass.Blend(from, cutoff, t);
            manager.effectsDirty = true;
        });
    }

    /// <summary>Turns the music's low-pass on or off.</summary>
    public void SetLowPassEnabled(bool on)
    {
        lowPassTween.Stop();
        lowPass.enabled = on;
        effectsDirty = true;
    }

    /// <summary>
    /// Sweeps the music's low-pass to <paramref name="cutoff"/> Hz over <paramref name="duration"/> seconds, evenly in
    /// pitch (see <see cref="GlobalLowPass.Blend"/>). Turns the filter on.
    /// </summary>
    public void TweenLowPassCutoff(float cutoff, float duration)
    {
        lowPassTween.Stop();
        lowPass.enabled = true;
        effectsDirty = true;

        float from = lowPass.cutoff;
        if (duration <= 0f || !isActiveAndEnabled)
        {
            lowPass.cutoff = cutoff;
            return;
        }

        lowPassTween = Tween.Custom(this, 0f, 1f, duration, (manager, t) =>
        {
            manager.lowPass.cutoff = GlobalLowPass.Blend(from, cutoff, t);
            manager.effectsDirty = true;
        });
    }

    /// <summary>Turns the music's reverb on or off.</summary>
    public void SetReverbEnabled(bool on)
    {
        reverbTween.Stop();
        reverb.enabled = on;
        effectsDirty = true;
    }

    /// <summary>
    /// Fades the reverb's late level (how wet it sounds, mB) to <paramref name="level"/> over <paramref name="duration"/>
    /// seconds. Turns the reverb on, on the User preset.
    /// </summary>
    public void TweenReverbLevel(float level, float duration)
    {
        reverbTween.Stop();
        reverb.enabled = true;
        reverb.preset = AudioReverbPreset.User;
        effectsDirty = true;

        if (duration <= 0f || !isActiveAndEnabled)
        {
            reverb.reverbLevel = level;
            return;
        }

        reverbTween = Tween.Custom(this, reverb.reverbLevel, level, duration, (manager, v) =>
        {
            manager.reverb.reverbLevel = v;
            manager.effectsDirty = true;
        });
    }

    /// <summary>Starts <see cref="startBgm"/>.</summary>
    public void PlayStartBgm() => Play(startBgm, startFade);

    /// <summary>
    /// The final cutscene of round <paramref name="roundsPlayed"/> (1 for the first round played this game) is over:
    /// starts <see cref="bgmByRound"/>[<paramref name="roundsPlayed"/> - 1]. No track for it leaves the music as it is.
    /// </summary>
    public void PlayAfterRound(int roundsPlayed)
    {
        int index = roundsPlayed - 1;
        if (index < 0 || index >= bgmByRound.Count) return;

        BGMObject bgm = bgmByRound[index];
        if (bgm) Play(bgm, levelFade);
    }

    /// <summary>Crossfades into <paramref name="bgm"/>, looping, and makes it the BGMManager's default.</summary>
    public void Play(BGMObject bgm, float fade)
    {
        if (!bgm)
        {
            h.Out("SongManager: no track to play.");
            return;
        }

        if (!Bgm)
        {
            h.Out($"SongManager: no BGMManager to play {bgm.name}.");
            return;
        }

        Current = bgm;
        Bgm.PlayBGMAsNewDefault(bgm, fade);
        onBgmChanged?.Invoke(bgm);
    }

    /// <summary>Fades the music out over <paramref name="fade"/> seconds.</summary>
    public void Stop(float fade = 0f)
    {
        Current = null;
        if (Bgm) Bgm.FadeOutMusic(fade);
    }
}
