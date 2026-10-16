using System.Collections;
using PrimeTween;
using UnityEngine;

/// <summary>
/// Blurs the screen through the VolFx Blur override (<see cref="BlurVolumeManager"/>).
///
/// As a start effect it pulses: fade in, hold, fade back out to the profile's authored values -
/// the "something happens" beat. As a sustained effect it fades in and stays blurred until the
/// next node starts, then fades out.
///
/// Post-processing only reaches what the camera renders: the background must be on a
/// Screen Space - Camera (or World Space) canvas, not Screen Space - Overlay, to be blurred.
/// </summary>
[CreateAssetMenu(fileName = "VN Blur", menuName = "Dialogues/Visual Novel/Effects/Blur")]
public class VisualNovelBlurEffect : VisualNovelEffect
{
    [Header("Target")]
    [Tooltip("BlurVol.m_Radius to reach (0..1).")]
    [Range(0f, 1f)] public float radius = 0.5f;
    [Tooltip("BlurVol.m_Radial to reach (0..1).")]
    [Range(0f, 1f)] public float radial = 0f;

    [Header("Timing")]
    [Min(0f)] public float fadeIn = 0.4f;
    [Tooltip("Start effect only: seconds the blur stays at full strength before fading out.")]
    [Min(0f)] public float hold = 0.8f;
    [Min(0f)] public float fadeOut = 0.6f;
    public Ease ease = Ease.InOutSine;

    public override bool DrivesBlur => true;

    public override IEnumerator Play(VisualNovelTalkable vn)
    {
        BlurVolumeManager blur = Resolve(vn);
        if (!blur) yield break;

        yield return blur.TweenTo(radius, radial, fadeIn, ease).ToYieldInstruction();
        if (hold > 0f) yield return new WaitForSeconds(hold);
        yield return blur.TweenToRest(fadeOut, ease).ToYieldInstruction();
    }

    public override IEnumerator Sustain(VisualNovelTalkable vn)
    {
        BlurVolumeManager blur = Resolve(vn);
        if (!blur) yield break;

        yield return blur.TweenTo(radius, radial, fadeIn, ease).ToYieldInstruction();
        while (true) yield return null;
    }

    public override IEnumerator Release(VisualNovelTalkable vn)
    {
        BlurVolumeManager blur = vn ? vn.Blur : null;
        if (!blur) yield break;

        yield return blur.TweenToRest(fadeOut, ease).ToYieldInstruction();
    }

    private BlurVolumeManager Resolve(VisualNovelTalkable vn)
    {
        BlurVolumeManager blur = vn ? vn.Blur : null;
        if (!blur) h.Out($"{name}: no BlurVolumeManager found - add one to the Volume that holds the VolFx Blur override.");
        return blur;
    }
}
