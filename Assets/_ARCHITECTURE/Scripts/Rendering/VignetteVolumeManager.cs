using UnityEngine;
using UnityEngine.Rendering.Universal;
using PrimeTween;

/// <summary>
/// <see cref="VolumeManager"/> specialised for the URP <see cref="Vignette"/> override.
/// Handy for effects like damage flashes or focus pulses.
/// </summary>
[AddComponentMenu("MyFriendPetya/Rendering/Vignette Volume Manager")]
public class VignetteVolumeManager : VolumeManager
{
    private Vignette vignette;

    /// <summary>The Vignette override (lazily fetched from the profile).</summary>
    public Vignette Vignette => vignette != null ? vignette : (vignette = Get<Vignette>());

    protected override void Awake()
    {
        base.Awake();
        vignette = Get<Vignette>();
    }

    public void SetIntensity(float value)  => SetFloat(Vignette?.intensity, value);
    public void SetSmoothness(float value) => SetFloat(Vignette?.smoothness, value);
    public void SetColor(Color color)      => SetOverride(Vignette?.color, color);

    public Tween TweenIntensity(float to, float duration, Ease ease = Ease.Default)
        => TweenFloat(Vignette?.intensity, to, duration, ease);

    /// <summary>Pulse the vignette up to <paramref name="peak"/> and back to 0 — e.g. a hit flash.</summary>
    public Sequence PulseIntensity(float peak, float duration, Ease ease = Ease.OutQuad)
        => TweenFloat(Vignette?.intensity, peak, duration * 0.5f, ease)
            .Chain(TweenFloat(Vignette?.intensity, 0f, duration * 0.5f, ease));
}
