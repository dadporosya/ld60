using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
using UnityEngine.Events;

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
    public LowPassSettings lowPass = new LowPassSettings();
    public HighPassSettings highPass = new HighPassSettings();
    public ReverbSettings reverb = new ReverbSettings();

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

        highPass.ApplyTo(go);
        lowPass.ApplyTo(go);
        reverb.ApplyTo(go);
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
