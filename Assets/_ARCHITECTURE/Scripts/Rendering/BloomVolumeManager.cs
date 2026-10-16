using UnityEngine;
using UnityEngine.Rendering.Universal;
using PrimeTween;

/// <summary>
/// <see cref="VolumeManager"/> specialised for the URP <see cref="Bloom"/> override.
/// Caches the Bloom component and exposes typed helpers for its parameters.
/// </summary>
[AddComponentMenu("MyFriendPetya/Rendering/Bloom Volume Manager")]
public class BloomVolumeManager : VolumeManager
{
    private Bloom bloom;

    /// <summary>The Bloom override (lazily fetched from the profile).</summary>
    public Bloom Bloom => bloom != null ? bloom : (bloom = Get<Bloom>());

    protected override void Awake()
    {
        base.Awake();
        bloom = Get<Bloom>();
    }

    public void SetIntensity(float value) => SetFloat(Bloom?.intensity, value);
    public void SetThreshold(float value) => SetFloat(Bloom?.threshold, value);
    public void SetScatter(float value)   => SetFloat(Bloom?.scatter, value);
    public void SetTint(Color color)      => SetOverride(Bloom?.tint, color);

    public Tween TweenIntensity(float to, float duration, Ease ease = Ease.Default)
        => TweenFloat(Bloom?.intensity, to, duration, ease);

    public Tween TweenScatter(float to, float duration, Ease ease = Ease.Default)
        => TweenFloat(Bloom?.scatter, to, duration, ease);
}
