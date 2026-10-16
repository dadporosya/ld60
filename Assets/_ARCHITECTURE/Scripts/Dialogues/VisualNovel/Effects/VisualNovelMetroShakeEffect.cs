using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Riding a metro car: the stage's background and/or foreground image (see <see cref="target"/>)
/// constantly rumbles a little on X and Y, and every few seconds jolts on Y as the wheels cross a
/// rail joint - a bounce that swings and settles, optionally followed by a second, smaller one
/// from the car's other bogie.
///
/// Meant as a sustained effect: it fades in when the node starts, keeps riding through every node
/// that lists the same asset, and fades out when a node without it starts. As a start effect it
/// rides for <see cref="playDuration"/> seconds.
///
/// The target moves by a few canvas units, so make the background images slightly larger than the
/// screen or its edges will peek in during a bounce. Each layer's crossfade image moves with it.
/// </summary>
[CreateAssetMenu(fileName = "VN Metro Shake", menuName = "Dialogues/Visual Novel/Effects/Metro Shake")]
public class VisualNovelMetroShakeEffect : VisualNovelEffect
{
    [Header("Target")]
    [Tooltip("Which stage images ride: the background, the foreground, or both together.")]
    public VisualNovelLayers target = VisualNovelLayers.Background;

    [Header("Rumble (constant)")]
    [Tooltip("Max drift in canvas units on each axis.")]
    public Vector2 rumbleAmplitude = new Vector2(2.5f, 1.5f);
    [Tooltip("How fast the rumble wanders. Higher = more rattly, lower = more swaying.")]
    [Min(0.01f)] public float rumbleFrequency = 9f;

    [Header("Bounces (rail joints)")]
    public bool bounces = true;
    [Tooltip("Peak Y offset of a bounce in canvas units. Positive = the view jolts up first.")]
    public float bounceHeight = 9f;
    [Tooltip("Seconds between bounces, picked at random between X and Y each time.")]
    public Vector2 bounceInterval = new Vector2(1.6f, 3.2f);
    [Tooltip("How long one bounce takes to settle.")]
    [Min(0.05f)] public float bounceDuration = 0.45f;
    [Tooltip("How many times a bounce swings up and down before it settles.")]
    [Min(0.5f)] public float bounceSwings = 1.5f;
    [Tooltip("Rail joints come in pairs (front and back wheels): a second, smaller bounce shortly after the first.")]
    public bool doubleBounce = true;
    [Min(0f)] public float doubleBounceDelay = 0.22f;
    [Range(0f, 1f)] public float doubleBounceScale = 0.6f;

    [Header("Envelope")]
    [Tooltip("Seconds to ramp up to full strength.")]
    [Min(0f)] public float fadeIn = 0.8f;
    [Tooltip("Seconds to die down when the ride ends or the effect is released.")]
    [Min(0f)] public float fadeOut = 0.8f;
    [Tooltip("Start effect only: how long the ride lasts, fades included.")]
    [Min(0f)] public float playDuration = 4f;

    /// <summary>A bounce in flight: when it started (ride time) and how strong it is.</summary>
    private struct Bounce
    {
        public float start;
        public float scale;
    }

    public override IEnumerator Play(VisualNovelTalkable vn)
    {
        yield return Ride(vn, playDuration, endless: false);
    }

    public override IEnumerator Sustain(VisualNovelTalkable vn)
    {
        yield return Ride(vn, 0f, endless: true);
    }

    /// <summary>
    /// Dies down from wherever the ride was stopped: the rumble keeps going (it samples the same
    /// noise, so it doesn't jump) while its strength and any half-finished bounce fade to rest.
    /// </summary>
    public override IEnumerator Release(VisualNovelTalkable vn)
    {
        VisualNovelBackground stage = vn ? vn.Background : null;
        if (!stage) yield break;

        // Nothing was riding: just make sure the target is back at rest.
        if (!(vn.GetEffectState(this) is float seed))
        {
            stage.ResetOffset(target, this);
            yield break;
        }
        vn.SetEffectState(this, null);

        // Whatever the rumble doesn't explain (a bounce mid-swing) fades out alongside it.
        Vector2 residual = stage.GetOffset(target, this) - Rumble(seed);

        for (float t = 0f; t < fadeOut; t += Time.deltaTime)
        {
            float strength = 1f - t / fadeOut;
            stage.SetOffset(target, (Rumble(seed) + residual) * strength, this);
            yield return null;
        }

        stage.ResetOffset(target, this);
    }

    private IEnumerator Ride(VisualNovelTalkable vn, float duration, bool endless)
    {
        VisualNovelBackground stage = vn ? vn.Background : null;
        if (!stage)
        {
            h.Out($"{name}: VisualNovelTalkable has no background to shake.");
            yield break;
        }

        stage.ResetOffset(target, this);

        // Each ride gets its own stretch of noise; Release reads it back to carry on seamlessly.
        float seed = Random.Range(0f, 1000f);
        vn.SetEffectState(this, seed);

        List<Bounce> active = new List<Bounce>();
        float nextBounce = NextInterval();

        for (float t = 0f; endless || t < duration; t += Time.deltaTime)
        {
            float strength = Envelope(t, duration, endless);
            Vector2 offset = Rumble(seed);

            if (bounces && t >= nextBounce)
            {
                active.Add(new Bounce { start = t, scale = 1f });
                if (doubleBounce)
                    active.Add(new Bounce { start = t + doubleBounceDelay, scale = doubleBounceScale });
                nextBounce = t + NextInterval();
            }

            for (int i = active.Count - 1; i >= 0; i--)
            {
                float age = t - active[i].start;
                if (age > bounceDuration)
                {
                    active.RemoveAt(i);
                    continue;
                }
                offset.y += BounceOffset(age) * active[i].scale;
            }

            stage.SetOffset(target, offset * strength, this);
            yield return null;
        }

        vn.SetEffectState(this, null);
        stage.ResetOffset(target, this);
    }

    /// <summary>
    /// The constant rumble: smooth noise on each axis. Sampled at Time.time (not ride time) so a
    /// Release that picks up where Sustain stopped lands on the same value.
    /// </summary>
    private Vector2 Rumble(float seed)
    {
        float t = Time.time * rumbleFrequency;
        return new Vector2(
            (Mathf.PerlinNoise(seed, t) * 2f - 1f) * rumbleAmplitude.x,
            (Mathf.PerlinNoise(seed + 71.3f, t) * 2f - 1f) * rumbleAmplitude.y);
    }

    /// <summary>
    /// One bounce: a swing that decays to exactly zero at <see cref="bounceDuration"/>. Nothing
    /// before it starts, so a queued second bounce stays silent until its turn.
    /// </summary>
    private float BounceOffset(float age)
    {
        if (age < 0f) return 0f;
        float p = age / bounceDuration;
        float decay = (1f - p) * (1f - p);
        return bounceHeight * Mathf.Sin(p * bounceSwings * 2f * Mathf.PI) * decay;
    }

    /// <summary>0..1 strength: ramps in over fadeIn, and for a timed ride out over its last fadeOut.</summary>
    private float Envelope(float t, float duration, bool endless)
    {
        float strength = fadeIn > 0f ? Mathf.Clamp01(t / fadeIn) : 1f;
        if (!endless && fadeOut > 0f) strength = Mathf.Min(strength, Mathf.Clamp01((duration - t) / fadeOut));
        return strength;
    }

    private float NextInterval()
    {
        float min = Mathf.Max(0.1f, Mathf.Min(bounceInterval.x, bounceInterval.y));
        float max = Mathf.Max(min, Mathf.Max(bounceInterval.x, bounceInterval.y));
        return Random.Range(min, max);
    }
}
