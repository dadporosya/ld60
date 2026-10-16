using System.Collections;
using UnityEngine;

/// <summary>
/// A plain shake: the stage's background and/or foreground image (see <see cref="target"/>)
/// jitters on X and Y. No bounces, no rhythm - the Metro Shake without the metro.
///
/// As a start effect it shakes for <see cref="playDuration"/> seconds, fading in and out (turn on
/// <see cref="decay"/> for a hit that dies away over the whole duration). As a sustained effect it
/// fades in, shakes until a node without it starts, then fades out.
///
/// The target moves by a few canvas units, so make the background images slightly larger than the
/// screen or its edges will peek in. Each layer's crossfade image moves with it.
/// </summary>
[CreateAssetMenu(fileName = "VN Layer Shake", menuName = "Dialogues/Visual Novel/Effects/Layer Shake")]
public class VisualNovelLayerShakeEffect : VisualNovelEffect
{
    [Header("Target")]
    [Tooltip("Which stage images shake: the background, the foreground, or both together.")]
    public VisualNovelLayers target = VisualNovelLayers.Background;

    [Header("Shake")]
    [Tooltip("Max offset in canvas units on each axis.")]
    public Vector2 amplitude = new Vector2(6f, 6f);
    [Tooltip("How fast it shakes. Higher = more jittery, lower = more wobbly.")]
    [Min(0.01f)] public float frequency = 18f;

    [Header("Envelope")]
    [Tooltip("Seconds to ramp up to full strength.")]
    [Min(0f)] public float fadeIn = 0.1f;
    [Tooltip("Seconds to die down when the shake ends or the effect is released.")]
    [Min(0f)] public float fadeOut = 0.3f;
    [Tooltip("Start effect only: how long the shake lasts, fades included.")]
    [Min(0f)] public float playDuration = 0.6f;
    [Tooltip("Start effect only: strength falls off over the whole duration (a hit), instead of " +
             "holding steady until the fade-out.")]
    public bool decay = false;

    public override IEnumerator Play(VisualNovelTalkable vn)
    {
        yield return Shake(vn, playDuration, endless: false);
    }

    public override IEnumerator Sustain(VisualNovelTalkable vn)
    {
        yield return Shake(vn, 0f, endless: true);
    }

    /// <summary>
    /// Dies down from wherever the shake was stopped: it keeps sampling the same noise (so it
    /// doesn't jump) while its strength fades to rest.
    /// </summary>
    public override IEnumerator Release(VisualNovelTalkable vn)
    {
        VisualNovelBackground stage = vn ? vn.Background : null;
        if (!stage) yield break;

        // Nothing was shaking: just make sure the target is back at rest.
        if (!(vn.GetEffectState(this) is float seed))
        {
            stage.ResetOffset(target, this);
            yield break;
        }
        vn.SetEffectState(this, null);

        // How strong the shake was when it got stopped, so the fade starts from there.
        Vector2 noise = Noise(seed);
        Vector2 offset = stage.GetOffset(target, this);
        float from = noise.magnitude > 0.0001f ? Mathf.Clamp01(offset.magnitude / noise.magnitude) : 1f;

        for (float t = 0f; t < fadeOut; t += Time.deltaTime)
        {
            stage.SetOffset(target, Noise(seed) * from * (1f - t / fadeOut), this);
            yield return null;
        }

        stage.ResetOffset(target, this);
    }

    private IEnumerator Shake(VisualNovelTalkable vn, float duration, bool endless)
    {
        VisualNovelBackground stage = vn ? vn.Background : null;
        if (!stage)
        {
            h.Out($"{name}: VisualNovelTalkable has no background to shake.");
            yield break;
        }

        stage.ResetOffset(target, this);

        // Each shake gets its own stretch of noise; Release reads it back to carry on seamlessly.
        float seed = Random.Range(0f, 1000f);
        vn.SetEffectState(this, seed);

        for (float t = 0f; endless || t < duration; t += Time.deltaTime)
        {
            stage.SetOffset(target, Noise(seed) * Envelope(t, duration, endless), this);
            yield return null;
        }

        vn.SetEffectState(this, null);
        stage.ResetOffset(target, this);
    }

    /// <summary>
    /// Smooth noise on each axis, -amplitude..amplitude. Sampled at Time.time (not shake time) so a
    /// Release that picks up where Sustain stopped lands on the same value.
    /// </summary>
    private Vector2 Noise(float seed)
    {
        float t = Time.time * frequency;
        return new Vector2(
            (Mathf.PerlinNoise(seed, t) * 2f - 1f) * amplitude.x,
            (Mathf.PerlinNoise(seed + 71.3f, t) * 2f - 1f) * amplitude.y);
    }

    /// <summary>0..1 strength: ramps in over fadeIn; a timed shake also fades out (or decays throughout).</summary>
    private float Envelope(float t, float duration, bool endless)
    {
        float strength = fadeIn > 0f ? Mathf.Clamp01(t / fadeIn) : 1f;
        if (endless) return strength;

        if (decay && duration > 0f)
        {
            float left = Mathf.Clamp01(1f - t / duration);
            return strength * left * left;
        }

        if (fadeOut > 0f) strength = Mathf.Min(strength, Mathf.Clamp01((duration - t) / fadeOut));
        return strength;
    }
}
