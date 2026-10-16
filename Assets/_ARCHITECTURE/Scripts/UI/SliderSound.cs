using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plays a SFX through the <see cref="SFXManager"/> every time the attached <see cref="Slider"/>'s
/// value changes. Put this on the same GameObject as the Slider.
///
/// A slider dragged with the mouse fires onValueChanged every frame, so raw playback would spam
/// hundreds of clips. Two independent throttles are applied:
/// <see cref="minStep"/> (how far the value must move before the next tick) and
/// <see cref="minInterval"/> (how much unscaled time must pass). Both must be satisfied.
/// </summary>
[RequireComponent(typeof(Slider))]
public class SliderSound : MonoBehaviour
{
    [Tooltip("One of these is played (at random) through the SFXManager on each value change.")]
    public List<AudioClip> tickSounds = new List<AudioClip>();

    [Tooltip("Volume passed to the SFXManager. If < 0, the manager's default volume is used.")]
    [SerializeField] private float volume = -1f;

    [Header("Throttling")]
    [Tooltip("Minimum change in the slider's NORMALIZED value (0..1) before the next sound. 0 = every change.")]
    [Range(0f, 1f)][SerializeField] private float minStep = 0.05f;

    [Tooltip("Minimum unscaled seconds between sounds. 0 = no time limit.")]
    [SerializeField] private float minInterval = 0.04f;

    [Header("Pitch")]
    [Tooltip("If enabled, pitch is lerped across pitchRange by the slider's normalized value " +
             "(low value = low pitch), instead of using the SFXManager's default pitch.")]
    [SerializeField] private bool pitchFollowsValue = false;

    [Tooltip("Pitch at the slider's min (x) and max (y). Only used when pitchFollowsValue is on.")]
    [SerializeField] private Vector2 pitchRange = new Vector2(0.9f, 1.2f);

    private Slider slider;
    private float lastPlayedNormalized;
    private float lastPlayedTime = float.NegativeInfinity;

    private void Awake()
    {
        slider = GetComponent<Slider>();
        lastPlayedNormalized = Normalized;
    }

    private void OnEnable()
    {
        // Re-sync so re-enabling the object (e.g. reopening a menu) never fires a spurious tick.
        lastPlayedNormalized = Normalized;
        slider.onValueChanged.AddListener(OnValueChanged);
    }

    private void OnDisable() => slider.onValueChanged.RemoveListener(OnValueChanged);

    private void OnValueChanged(float value)
    {
        float normalized = Normalized;

        if (minStep > 0f && Mathf.Abs(normalized - lastPlayedNormalized) < minStep) return;
        if (minInterval > 0f && Time.unscaledTime - lastPlayedTime < minInterval) return;

        lastPlayedNormalized = normalized;
        lastPlayedTime = Time.unscaledTime;

        Play(normalized);
    }

    /// <summary>Plays one tick. Public so it can also be fired manually (e.g. from a UnityEvent).</summary>
    public void Play(float normalized)
    {
        if (tickSounds == null || tickSounds.Count == 0) return;
        if (SFXManager.Instance == null) return;

        float? pitch = pitchFollowsValue
            ? Mathf.Lerp(pitchRange.x, pitchRange.y, normalized)
            : (float?)null;

        SFXManager.Instance.PlayRandomClipIndependently(
            tickSounds,
            volume >= 0f ? volume : (float?)null,
            pitchIn: pitch);
    }

    // Slider.normalizedValue is unreliable for a zero-width range, so guard the divide.
    private float Normalized
    {
        get
        {
            float range = slider.maxValue - slider.minValue;
            return Mathf.Approximately(range, 0f)
                ? 0f
                : Mathf.Clamp01((slider.value - slider.minValue) / range);
        }
    }
}
