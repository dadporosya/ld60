using UnityEngine;
using PrimeTween;
using VolFx;

/// <summary>
/// <see cref="VolumeManager"/> specialised for the VolFx <see cref="BlurVol"/> override.
///
/// Remembers the profile's authored radius / radial values on Awake as the "rest" state, so an
/// effect can blur the screen and then hand it back exactly as it was authored.
/// </summary>
[AddComponentMenu("MyFriendPetya/Rendering/Blur Volume Manager")]
public class BlurVolumeManager : VolumeManager
{
    public static BlurVolumeManager Instance;
    private BlurVol blur;

    /// <summary>The Blur override (lazily fetched from the profile).</summary>
    public BlurVol Blur => blur != null ? blur : (blur = Get<BlurVol>());

    /// <summary>Authored m_Radius, captured on Awake.</summary>
    public float RestRadius { get; private set; }

    /// <summary>Authored m_Radial, captured on Awake.</summary>
    public float RestRadial { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        blur = Get<BlurVol>();
        if (blur != null)
        {
            RestRadius = blur.m_Radius.value;
            RestRadial = blur.m_Radial.value;
        }
        h.CreateStaticInstance(this, ref Instance);
    }

    /// <summary>Base blur strength (BlurVol.m_Radius, 0..1).</summary>
    public float Radius
    {
        get => Blur != null ? Blur.m_Radius.value : 0f;
        set => SetFloat(Blur?.m_Radius, value);
    }

    /// <summary>Radial blur from the centre (BlurVol.m_Radial, 0..1).</summary>
    public float Radial
    {
        get => Blur != null ? Blur.m_Radial.value : 0f;
        set => SetFloat(Blur?.m_Radial, value);
    }

    /// <summary>
    /// Animates radius and radial together to the given values, stopping any blur tween already
    /// running on this manager. Returns a dead tween (nothing to wait for) when the duration is 0
    /// or the profile has no Blur override.
    /// </summary>
    public Tween TweenTo(float radius, float radial, float duration, Ease ease = Ease.Default)
    {
        Tween.StopAll(this);

        if (Blur == null) return default;

        float fromRadius = Radius;
        float fromRadial = Radial;

        if (duration <= 0f)
        {
            Radius = radius;
            Radial = radial;
            return default;
        }

        return Tween.Custom(this, 0f, 1f, duration, (m, t) =>
        {
            m.Radius = Mathf.Lerp(fromRadius, radius, t);
            m.Radial = Mathf.Lerp(fromRadial, radial, t);
        }, ease);
    }

    /// <summary>Animates back to the authored rest values.</summary>
    public Tween TweenToRest(float duration, Ease ease = Ease.Default)
        => TweenTo(RestRadius, RestRadial, duration, ease);

    /// <summary>Snaps back to the authored rest values.</summary>
    public void ResetToRest() => TweenTo(RestRadius, RestRadial, 0f);
}
