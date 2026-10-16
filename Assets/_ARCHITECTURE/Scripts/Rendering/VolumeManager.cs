using UnityEngine;
using UnityEngine.Rendering;
using PrimeTween;

/// <summary>
/// Controls the values of the overrides on a single URP <see cref="Volume"/>.
///
/// Attach this to the GameObject that holds the <see cref="Volume"/> (or assign one in the
/// inspector) and use it to read / set / tween override parameters at runtime without touching
/// the shared profile asset. Accessing <see cref="Volume.profile"/> instantiates a runtime copy,
/// so edits made through this manager do NOT modify the profile asset on disk.
///
/// This is a plain component (one manager per Volume) rather than a static singleton, because a
/// scene can have several volumes (global, local, transitions, etc.). Subclass it to expose typed
/// controls for a specific override — see <c>BloomVolumeManager</c> / <c>VignetteVolumeManager</c>.
/// </summary>
[AddComponentMenu("MyFriendPetya/Rendering/Volume Manager")]
public class VolumeManager : MonoBehaviour
{
    [Tooltip("The Volume whose overrides this manager controls. Auto-grabbed from this GameObject if left empty.")]
    [SerializeField] protected Volume volume;

    /// <summary>The controlled Volume.</summary>
    public Volume Volume => volume;

    /// <summary>The runtime profile instance (a copy — safe to mutate). Null if no Volume is set.</summary>
    public VolumeProfile Profile => volume != null ? volume.profile : null;

    protected virtual void Awake()
    {
        if (volume == null) TryGetComponent(out volume);
        if (volume == null) h.Out($"{name}: VolumeManager has no Volume assigned.");
    }

    // ---------------------------------------------------------------- Weight

    /// <summary>Overall blend weight of the volume (0..1).</summary>
    public float Weight
    {
        get => volume != null ? volume.weight : 0f;
        set { if (volume != null) volume.weight = Mathf.Clamp01(value); }
    }

    /// <summary>Animate the volume weight to <paramref name="to"/> over <paramref name="duration"/> seconds.</summary>
    public Tween TweenWeight(float to, float duration, Ease ease = Ease.Default)
        => Tween.Custom(this, Weight, Mathf.Clamp01(to), duration, (m, v) => m.Weight = v, ease);

    // ---------------------------------------------------------------- Override access

    /// <summary>Try to get an override component of type <typeparamref name="T"/> from the profile.</summary>
    public bool TryGet<T>(out T component) where T : VolumeComponent
    {
        component = null;
        return volume != null && volume.profile != null && volume.profile.TryGet(out component);
    }

    /// <summary>Get an override component of type <typeparamref name="T"/>, or null (with a log) if absent.</summary>
    public T Get<T>() where T : VolumeComponent
    {
        if (TryGet(out T component)) return component;
        h.Out($"{name}: Volume profile has no override of type {typeof(T).Name}.");
        return null;
    }

    /// <summary>Enable or disable an override component as a whole.</summary>
    public void SetActive<T>(bool active) where T : VolumeComponent
    {
        if (TryGet(out T component)) component.active = active;
    }

    // ---------------------------------------------------------------- Float parameters

    /// <summary>Set a float parameter's value (e.g. bloom.intensity), enabling its override state.</summary>
    public void SetFloat(VolumeParameter<float> parameter, float value, bool enableOverride = true)
    {
        if (parameter == null) return;
        if (enableOverride) parameter.overrideState = true;
        parameter.value = value;
    }

    /// <summary>Animate a float parameter to <paramref name="to"/> over <paramref name="duration"/> seconds.</summary>
    public Tween TweenFloat(VolumeParameter<float> parameter, float to, float duration,
                            Ease ease = Ease.Default, bool enableOverride = true)
    {
        if (parameter == null) return default;
        if (enableOverride) parameter.overrideState = true;
        return Tween.Custom(this, parameter.value, to, duration, (m, v) => parameter.value = v, ease);
    }

    // ---------------------------------------------------------------- Generic parameters

    /// <summary>Set any typed parameter's value (float, Color, bool, Vector, ...), enabling its override state.</summary>
    public void SetOverride<T>(VolumeParameter<T> parameter, T value)
    {
        if (parameter == null) return;
        parameter.overrideState = true;
        parameter.value = value;
    }

    /// <summary>Toggle whether a single parameter contributes to the blend.</summary>
    public void SetOverrideActive(VolumeParameter parameter, bool active)
    {
        if (parameter != null) parameter.overrideState = active;
    }
}
