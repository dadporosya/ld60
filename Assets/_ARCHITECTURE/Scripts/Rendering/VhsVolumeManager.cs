using UnityEngine;
using PrimeTween;
using VolFx;

/// <summary>
/// <see cref="VolumeManager"/> specialised for the VolFx <see cref="VhsVol"/> override
/// (the retro VHS / CRT post effect). Caches the override and exposes typed helpers for its
/// parameters.
///
/// Note: <see cref="VhsVol"/> has its own <c>_weight</c> parameter that scales the whole effect —
/// exposed here as <see cref="EffectWeight"/>. That is separate from the containing Volume's blend
/// weight (the inherited <see cref="VolumeManager.Weight"/>).
/// </summary>
[AddComponentMenu("MyFriendPetya/Rendering/VHS Volume Manager")]
public class VhsVolumeManager : VolumeManager
{
    public static VhsVolumeManager Instance;
    private VhsVol vhs;

    /// <summary>The VHS override (lazily fetched from the profile).</summary>
    public VhsVol Vhs => vhs != null ? vhs : (vhs = Get<VhsVol>());

    protected override void Awake()
    {
        base.Awake();
        vhs = Get<VhsVol>();
        h.CreateStaticInstance(this, ref Instance);
    }

    // ---------------------------------------------------------------- Effect strength

    /// <summary>Overall strength of the VHS effect (VhsVol._weight, 0..1).</summary>
    public float EffectWeight
    {
        get => Vhs != null ? Vhs._weight.value : 0f;
        set => SetFloat(Vhs?._weight, value);
    }

    /// <summary>Fade the whole effect in to <paramref name="to"/> (default full).</summary>
    public Tween TweenEffectWeight(float to, float duration, Ease ease = Ease.Default)
        => TweenFloat(Vhs?._weight, to, duration, ease);

    /// <summary>Fade the effect fully in over <paramref name="duration"/> seconds.</summary>
    public Tween FadeIn(float duration, Ease ease = Ease.Default) => TweenEffectWeight(1f, duration, ease);

    /// <summary>Fade the effect out over <paramref name="duration"/> seconds.</summary>
    public Tween FadeOut(float duration, Ease ease = Ease.Default) => TweenEffectWeight(0f, duration, ease);

    // ---------------------------------------------------------------- Tape / distortion

    public void SetTape(float value)    => SetFloat(Vhs?._tape, value);       // 0..2
    public void SetShades(float value)  => SetFloat(Vhs?._shades, value);     // 0..3
    public void SetRocking(float value) => SetFloat(Vhs?._rocking, value);    // 0..0.1
    public void SetSqueeze(float value) => SetFloat(Vhs?._squeeze, value);    // 0..1

    // ---------------------------------------------------------------- Noise

    public void SetDensity(float value)    => SetFloat(Vhs?._density, value);    // 0..1
    public void SetIntensity(float value)  => SetFloat(Vhs?._intensity, value);  // 0..1
    public void SetScale(float value)      => SetFloat(Vhs?._scale, value);      // 0.3..3
    public void SetFlickering(float value) => SetFloat(Vhs?._flickering, value); // -1..1
    public void SetLines(bool enabled)     => SetOverride(Vhs?._lines, enabled);

    // ---------------------------------------------------------------- Glow

    public void SetColor(Color color) => SetOverride(Vhs?._color, color);
    public void SetBleed(float value) => SetFloat(Vhs?._bleed, value);          // 0..3

    // ---------------------------------------------------------------- Animation speeds

    public void SetFlow(float value)      => SetFloat(Vhs?._flow, value);       // 0..24
    public void SetPulsation(float value) => SetFloat(Vhs?._pulsation, value);  // 0..14

    // No scanline setters here: the VhsVol shipped with the installed VolFx has no _sl_* parameters in
    // either define configuration — scanlines are their own override, VolFx's ScanlinesVol ("VolFx/
    // Scanlines (Tv)"), so drive them through a VolumeManager for that component instead.

    // ---------------------------------------------------------------- Convenience tweens

    /// <summary>Animate the white-noise intensity.</summary>
    public Tween TweenIntensity(float to, float duration, Ease ease = Ease.Default)
        => TweenFloat(Vhs?._intensity, to, duration, ease);

    /// <summary>Animate the tape-noise impact.</summary>
    public Tween TweenTape(float to, float duration, Ease ease = Ease.Default)
        => TweenFloat(Vhs?._tape, to, duration, ease);

    /// <summary>
    /// A quick glitch burst: snap the effect to <paramref name="peak"/> strength, then ease back to
    /// <paramref name="settle"/>. Handy for damage hits, teleports, UI stingers, etc.
    /// </summary>
    public Sequence Glitch(float peak = 1f, float settle = 0f, float duration = 0.35f, Ease ease = Ease.OutQuad)
    {
        SetFloat(Vhs?._weight, peak);
        return Tween.Custom(this, peak, settle, duration, (m, v) => SetFloat(m.Vhs?._weight, v), ease)
            .Group(TweenTape(peak * 2f, duration, ease));
    }
}
