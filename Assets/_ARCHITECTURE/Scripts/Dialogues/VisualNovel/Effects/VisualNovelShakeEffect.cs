using System.Collections;
using EZCameraShake;
using PrimeTween;
using UnityEngine;

/// <summary>
/// Shakes the visual novel's background (the controller's shake target, a UI RectTransform)
/// and/or the camera through EZ Camera Shake.
///
/// As a start effect it is one jolt that dies out - the screamer hit. As a sustained effect it
/// is a steady rumble for the whole node (a train passing, an earthquake), faded out when the
/// next node starts.
/// </summary>
[CreateAssetMenu(fileName = "VN Shake", menuName = "Dialogues/Visual Novel/Effects/Shake")]
public class VisualNovelShakeEffect : VisualNovelEffect
{
    public enum ShakeTarget { Background, Camera, Both }

    public ShakeTarget target = ShakeTarget.Background;

    [Header("Background - start effect")]
    [Tooltip("Max offset in canvas units, per axis.")]
    public Vector2 strength = new Vector2(40f, 40f);
    [Min(0.01f)] public float duration = 0.4f;
    [Tooltip("Shakes per second.")]
    [Min(0.01f)] public float frequency = 25f;

    [Header("Background - sustained rumble")]
    public Vector2 sustainedStrength = new Vector2(6f, 6f);
    [Min(0.01f)] public float sustainedFrequency = 12f;

    [Header("Camera (EZ Camera Shake)")]
    public float magnitude = 4f;
    public float roughness = 8f;
    [Min(0f)] public float fadeIn = 0.05f;
    [Min(0f)] public float fadeOut = 0.4f;
    [Tooltip("Magnitude of the sustained rumble.")]
    public float sustainedMagnitude = 1f;

    private bool ShakesBackground => target != ShakeTarget.Camera;
    private bool ShakesCamera => target != ShakeTarget.Background;

    public override IEnumerator Play(VisualNovelTalkable vn)
    {
        float wait = 0f;

        RectTransform rect = vn ? vn.ShakeTarget : null;
        if (ShakesBackground && rect)
        {
            // A shake is relative to where it starts: start from rest, not from mid-shake.
            Tween.StopAll(rect);
            vn.ResetShakeTarget();
            Tween.ShakeLocalPosition(rect, strength, duration, frequency);
            wait = duration;
        }

        if (ShakesCamera && CameraShaker.Instance)
        {
            CameraShaker.Instance.ShakeOnce(magnitude, roughness, fadeIn, fadeOut);
            wait = Mathf.Max(wait, fadeIn + fadeOut);
        }

        if (wait > 0f) yield return new WaitForSeconds(wait);
    }

    public override IEnumerator Sustain(VisualNovelTalkable vn)
    {
        RectTransform rect = vn ? vn.ShakeTarget : null;
        if (ShakesBackground && rect)
        {
            Tween.StopAll(rect);
            vn.ResetShakeTarget();
            // No falloff and endless cycles: a steady rumble until Release stops it.
            Tween.ShakeLocalPosition(rect, new ShakeSettings(sustainedStrength, duration: 1f,
                frequency: sustainedFrequency, enableFalloff: false, cycles: -1));
        }

        if (ShakesCamera && CameraShaker.Instance)
            vn.SetEffectState(this, CameraShaker.Instance.StartShake(sustainedMagnitude, roughness, fadeIn));

        while (true) yield return null;
    }

    public override IEnumerator Release(VisualNovelTalkable vn)
    {
        if (!vn) yield break;

        RectTransform rect = vn.ShakeTarget;
        if (ShakesBackground && rect)
        {
            Tween.StopAll(rect);
            vn.ResetShakeTarget();
        }

        if (vn.GetEffectState(this) is CameraShakeInstance instance)
        {
            instance.StartFadeOut(fadeOut);
            vn.SetEffectState(this, null);
        }
    }
}
