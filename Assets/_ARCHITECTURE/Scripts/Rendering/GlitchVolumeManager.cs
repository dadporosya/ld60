using UnityEngine;
using PrimeTween;
using VolFx;

/// <summary>
/// <see cref="VolumeManager"/> specialised for the VolFx <see cref="GlitchVol"/> override
/// (digital noise + mosaic glitch). Caches the override and exposes typed helpers.
///
/// <see cref="GlitchVol"/> has its own <c>_weight</c> parameter that scales the whole effect —
/// exposed here as <see cref="EffectWeight"/>. That is separate from the containing Volume's blend
/// weight (the inherited <see cref="VolumeManager.Weight"/>).
/// </summary>
[AddComponentMenu("MyFriendPetya/Rendering/Glitch Volume Manager")]
public class GlitchVolumeManager : VolumeManager
{
    public static GlitchVolumeManager Instance;
    private GlitchVol glitch;

    /// <summary>The Glitch override (lazily fetched from the profile).</summary>
    public GlitchVol Glitch => glitch != null ? glitch : (glitch = Get<GlitchVol>());

    protected override void Awake()
    {
        base.Awake();
        glitch = Get<GlitchVol>();
        h.CreateStaticInstance(this, ref Instance);
    }

    // ---------------------------------------------------------------- Effect strength

    /// <summary>Overall strength of the glitch effect (GlitchVol._weight, 0..1).</summary>
    public float EffectWeight
    {
        get => Glitch != null ? Glitch._weight.value : 0f;
        set => SetFloat(Glitch?._weight, value);
    }

    public Tween TweenEffectWeight(float to, float duration, Ease ease = Ease.Default)
        => TweenFloat(Glitch?._weight, to, duration, ease);

    public Tween FadeIn(float duration, Ease ease = Ease.Default)  => TweenEffectWeight(1f, duration, ease);
    public Tween FadeOut(float duration, Ease ease = Ease.Default) => TweenEffectWeight(0f, duration, ease);

    // ---------------------------------------------------------------- Noise

    public void SetPower(float value)      => SetFloat(Glitch?._power, value);      // 0..1
    public void SetScale(float value)      => SetFloat(Glitch?._scale, value);      // 0..1
    public void SetDispersion(float value) => SetFloat(Glitch?._dispersion, value); // 0..1
    public void SetPeriod(float value)     => SetFloat(Glitch?._period, value);     // 0..1
    public void SetChaotic(float value)    => SetFloat(Glitch?._chaotic, value);    // 0..1
    public void SetNoLockNoise(bool value) => SetOverride(Glitch?._noLockNoise, value);
    public void SetNoiseColor(GradientValue gradient) => SetOverride(Glitch?._color, gradient);

    // ---------------------------------------------------------------- Mosaic

    public void SetDensity(float value) => SetFloat(Glitch?._density, value); // 0..1
    public void SetLock(float value)    => SetFloat(Glitch?._lock, value);    // 0..1
    public void SetSharpen(float value) => SetFloat(Glitch?._sharpen, value); // 0..1
    public void SetCrush(float value)   => SetFloat(Glitch?._crush, value);   // 0..1
    public void SetGrid(float value)    => SetFloat(Glitch?._grid, value);    // 0..1
    public void SetScreen(float value)  => SetFloat(Glitch?._screen, value);  // 0..1
    public void SetNoLock(bool value)   => SetOverride(Glitch?._noLock, value);
    public void SetBleed(GradientValue gradient) => SetOverride(Glitch?._bleed, gradient);

    // ---------------------------------------------------------------- Convenience tweens

    public Tween TweenPower(float to, float duration, Ease ease = Ease.Default)
        => TweenFloat(Glitch?._power, to, duration, ease);

    public Tween TweenDensity(float to, float duration, Ease ease = Ease.Default)
        => TweenFloat(Glitch?._density, to, duration, ease);

    /// <summary>
    /// A quick glitch burst: snap the effect to <paramref name="peak"/> strength, then ease back to
    /// <paramref name="settle"/>. Handy for damage hits, teleports, corruption stingers.
    /// </summary>
    public Sequence Glitchburst(float peak = 1f, float settle = 0f, float duration = 0.35f, Ease ease = Ease.OutQuad)
    {
        SetFloat(Glitch?._weight, peak);
        return Tween.Custom(this, peak, settle, duration, (m, v) => SetFloat(m.Glitch?._weight, v), ease)
            .Group(TweenPower(peak, duration, ease));
    }
}
