using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the <see cref="AudioMixerManager"/> volumes from up to three UI <see cref="Slider"/>s
/// (master / BGM / SFX). Put this on the settings panel and drag the sliders in.
///
/// Any slider may be left empty — only the assigned ones are wired up.
/// Sliders are expected to be normalized (min 0, max 1); the value is read through
/// <see cref="Slider.normalizedValue"/> semantics so other ranges still work.
///
/// The mixer takes a LINEAR level (0..1) and converts it to dB internally, so the slider's
/// value maps straight through. A level of 0 is clamped to <see cref="MinLevel"/> so the
/// log conversion never produces -Infinity (which silently breaks the mixer parameter).
/// </summary>
public class SliderAudioMixerManager : MonoBehaviour
{
    /// <summary>Lowest linear level sent to the mixer (~-80 dB, effectively silence).</summary>
    private const float MinLevel = 0.0001f;

    [Header("Sliders")]
    [Tooltip("Controls the Master mixer group. Optional.")]
    [SerializeField] private Slider masterSlider;

    [Tooltip("Controls the Music/BGM mixer group. Optional.")]
    [SerializeField] private Slider bgmSlider;

    [Tooltip("Controls the SFX mixer group. Optional.")]
    [SerializeField] private Slider sfxSlider;

    [Header("Persistence")]
    [Tooltip("Save/load slider values to PlayerPrefs so volume survives between sessions.")]
    [SerializeField] private bool persist = true;

    [Tooltip("Value used the first time the game runs (no saved prefs yet).")]
    [Range(0f, 1f)][SerializeField] private float defaultVolume = 1f;

    [Header("Debug")]
    [Tooltip("Log the mixer's actual volumes to the console every time a slider changes. " +
             "Useful for catching mixer parameters that aren't exposed.")]
    [SerializeField] private bool logMixerValues;

    private const string MasterKey = "Volume_Master";
    private const string BGMKey    = "Volume_BGM";
    private const string SFXKey    = "Volume_SFX";

    private void Awake()
    {
        // Push the stored (or default) values into both the sliders and the mixer before
        // anything can play, so the UI and the audio never disagree on the first frame.
        Initialize(masterSlider, MasterKey, AudioMixerManager.SetMasterVolume);
        Initialize(bgmSlider,    BGMKey,    AudioMixerManager.SetMusicVolume);
        Initialize(sfxSlider,    SFXKey,    AudioMixerManager.SetSFXVolume);

        if (logMixerValues) AudioMixerManager.LogCurrentVolumes("after init");
    }

    private void OnEnable()
    {
        if (masterSlider != null) masterSlider.onValueChanged.AddListener(OnMasterChanged);
        if (bgmSlider    != null) bgmSlider.onValueChanged.AddListener(OnBGMChanged);
        if (sfxSlider    != null) sfxSlider.onValueChanged.AddListener(OnSFXChanged);
    }

    private void OnDisable()
    {
        if (masterSlider != null) masterSlider.onValueChanged.RemoveListener(OnMasterChanged);
        if (bgmSlider    != null) bgmSlider.onValueChanged.RemoveListener(OnBGMChanged);
        if (sfxSlider    != null) sfxSlider.onValueChanged.RemoveListener(OnSFXChanged);

        if (persist) PlayerPrefs.Save();
    }

    // Public so they can also be hooked up straight from the Slider's inspector UnityEvent.
    public void OnMasterChanged(float value) => Apply(masterSlider, value, MasterKey, AudioMixerManager.SetMasterVolume, AudioMixerManager.masterGroupName);
    public void OnBGMChanged(float value)    => Apply(bgmSlider,    value, BGMKey,    AudioMixerManager.SetMusicVolume,  AudioMixerManager.musicGroupName);
    public void OnSFXChanged(float value)    => Apply(sfxSlider,    value, SFXKey,    AudioMixerManager.SetSFXVolume,    AudioMixerManager.SFXGroupName);

    /// <summary>Sets a slider (and therefore the mixer) from code, e.g. a "reset to defaults" button.</summary>
    public void SetMasterVolume(float level) => SetSlider(masterSlider, level, OnMasterChanged);
    public void SetBGMVolume(float level)    => SetSlider(bgmSlider,    level, OnBGMChanged);
    public void SetSFXVolume(float level)    => SetSlider(sfxSlider,    level, OnSFXChanged);

    /// <summary>Prints the mixer's current volumes to the console. Also usable from a debug button.</summary>
    [ContextMenu("Log mixer volumes")]
    public void LogMixerVolumes() => AudioMixerManager.LogCurrentVolumes("manual dump");

    private void Initialize(Slider slider, string key, System.Action<float> setVolume)
    {
        if (slider == null) return;

        float stored = persist ? PlayerPrefs.GetFloat(key, defaultVolume) : defaultVolume;
        float value  = Mathf.Lerp(slider.minValue, slider.maxValue, Mathf.Clamp01(stored));

        // SetValueWithoutNotify: the listeners aren't registered yet in Awake, but this also
        // keeps Initialize safe to call again later without double-firing.
        slider.SetValueWithoutNotify(value);
        setVolume(ToLevel(slider, value));
    }

    private void Apply(Slider slider, float value, string key, System.Action<float> setVolume, string groupName)
    {
        if (slider == null) return;

        float level = ToLevel(slider, value);
        setVolume(level);

        if (persist) PlayerPrefs.SetFloat(key, level);

        if (logMixerValues) LogChange(groupName, level);
    }

    /// <summary>
    /// Reports what was requested next to what the mixer actually ended up holding, so a
    /// parameter that isn't exposed (or a group name typo) shows up immediately.
    /// </summary>
    private static void LogChange(string groupName, float requestedLevel)
    {
        h.Out($"[AudioMixer] {groupName} slider -> level {requestedLevel:0.###} " +
              $"({AudioMixerManager.LevelToDb(requestedLevel):0.##} dB requested)",
              "now: " + AudioMixerManager.DescribeMixerGroup(AudioMixerManager.masterGroupName),
              AudioMixerManager.DescribeMixerGroup(AudioMixerManager.musicGroupName),
              AudioMixerManager.DescribeMixerGroup(AudioMixerManager.SFXGroupName));
    }

    private static void SetSlider(Slider slider, float level, System.Action<float> onChanged)
    {
        if (slider == null) return;

        level = Mathf.Clamp01(level);
        slider.SetValueWithoutNotify(Mathf.Lerp(slider.minValue, slider.maxValue, level));
        onChanged(slider.value);
    }

    /// <summary>Normalizes a slider value to 0..1 and clamps away the zero that would break Log10.</summary>
    private static float ToLevel(Slider slider, float value)
    {
        float range = slider.maxValue - slider.minValue;
        float normalized = Mathf.Approximately(range, 0f)
            ? 0f
            : Mathf.Clamp01((value - slider.minValue) / range);

        return Mathf.Max(normalized, MinLevel);
    }
}
