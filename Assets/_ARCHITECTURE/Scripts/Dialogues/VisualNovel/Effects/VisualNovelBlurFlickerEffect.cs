using System.Collections;
using PrimeTween;
using UnityEngine;

/// <summary>
/// Flickers the VolFx Blur radius (<see cref="BlurVolumeManager"/>) in beats: wait, flicker weak,
/// wait, flicker weak, wait, flicker strong... - vision going in and out.
///
/// Every beat waits a random time, then rolls which peak it is: <see cref="strong"/> with its
/// chance, otherwise <see cref="weak"/> with its chance, otherwise a quiet beat (just another
/// wait). A peak is a burst of one or more quick flickers up to a random radius and back down to
/// the base. <see cref="strongAfter"/> can force a strong peak so a long run of weak ones always
/// ends in a hit.
///
/// As a sustained effect it flickers until the next node starts, then fades back to the profile's
/// authored radius. As a start effect it flickers for <see cref="playDuration"/> seconds.
///
/// Post-processing only reaches what the camera renders: the background must be on a
/// Screen Space - Camera (or World Space) canvas, not Screen Space - Overlay, to be blurred.
/// </summary>
[CreateAssetMenu(fileName = "VN Blur Flicker", menuName = "Dialogues/Visual Novel/Effects/Blur Flicker")]
public class VisualNovelBlurFlickerEffect : VisualNovelEffect
{
    /// <summary>One kind of peak (weak or strong): how likely it is and how its flickers look.</summary>
    [System.Serializable]
    public class Peak
    {
        [Tooltip("Chance (0..1) that a beat is this kind of peak.")]
        [Range(0f, 1f)] public float chance = 0.5f;
        [Tooltip("BlurVol.m_Radius each flicker reaches, picked at random between X and Y (0..1).")]
        public Vector2 radius = new Vector2(0.2f, 0.35f);
        [Tooltip("Flickers in one peak, picked at random between X and Y (inclusive).")]
        public Vector2Int flickers = new Vector2Int(1, 2);
        [Tooltip("Seconds for a flicker to rise to its radius.")]
        [Min(0f)] public float attack = 0.05f;
        [Tooltip("Seconds a flicker stays at its radius.")]
        [Min(0f)] public float hold = 0.03f;
        [Tooltip("Seconds for a flicker to fall back to the base.")]
        [Min(0f)] public float decay = 0.15f;
        [Tooltip("Seconds at the base between the flickers of one peak, random between X and Y.")]
        public Vector2 gap = new Vector2(0.04f, 0.12f);
    }

    [Header("Base")]
    [Tooltip("Rest between peaks at the profile's authored radius. Off = rest at Base Radius.")]
    public bool baseIsRest = true;
    [Tooltip("BlurVol.m_Radius between peaks when Base Is Rest is off (0..1).")]
    [Range(0f, 1f)] public float baseRadius = 0.05f;
    [Tooltip("Constant wobble around the base, in radius units. 0 = steady between peaks.")]
    [Range(0f, 0.5f)] public float jitter = 0f;
    [Tooltip("How fast the wobble wanders.")]
    [Min(0.01f)] public float jitterFrequency = 6f;

    [Header("Beats")]
    [Tooltip("Seconds of waiting before each beat, random between X and Y.")]
    public Vector2 wait = new Vector2(0.6f, 1.8f);
    [Tooltip("Rolled after Strong: a beat that isn't strong is weak with this chance, else quiet.")]
    public Peak weak = new Peak
    {
        chance = 0.6f,
        radius = new Vector2(0.15f, 0.3f),
        flickers = new Vector2Int(1, 2),
        attack = 0.04f, hold = 0.02f, decay = 0.12f,
    };
    [Tooltip("Rolled first.")]
    public Peak strong = new Peak
    {
        chance = 0.2f,
        radius = new Vector2(0.6f, 0.9f),
        flickers = new Vector2Int(2, 4),
        attack = 0.06f, hold = 0.08f, decay = 0.35f,
    };
    [Tooltip("Force a strong peak after this many beats without one. 0 = pure chance.")]
    [Min(0)] public int strongAfter = 0;

    [Header("Envelope")]
    [Tooltip("Seconds to blend from the current radius into the flicker.")]
    [Min(0f)] public float fadeIn = 0.3f;
    [Tooltip("Seconds to fade back to the authored radius when the flicker ends or is released.")]
    [Min(0f)] public float fadeOut = 0.5f;
    [Tooltip("Start effect only: how long it flickers before fading out.")]
    [Min(0f)] public float playDuration = 4f;
    public Ease fadeOutEase = Ease.InOutSine;

    /// <summary>One run's state - kept out of the asset's fields, since the asset is shared.</summary>
    private class Run
    {
        public BlurVolumeManager blur;
        public float start;
        public float from;
        public float seed;
        /// <summary>Radius on top of the base right now (the current flicker).</summary>
        public float level;
    }

    public override bool DrivesBlur => true;

    public override IEnumerator Play(VisualNovelTalkable vn)
    {
        BlurVolumeManager blur = Resolve(vn);
        if (!blur) yield break;

        yield return Flicker(blur, playDuration, endless: false);
        yield return blur.TweenToRest(fadeOut, fadeOutEase).ToYieldInstruction();
    }

    public override IEnumerator Sustain(VisualNovelTalkable vn)
    {
        BlurVolumeManager blur = Resolve(vn);
        if (!blur) yield break;

        yield return Flicker(blur, 0f, endless: true);
    }

    public override IEnumerator Release(VisualNovelTalkable vn)
    {
        BlurVolumeManager blur = vn ? vn.Blur : null;
        if (!blur) yield break;

        yield return blur.TweenToRest(fadeOut, fadeOutEase).ToYieldInstruction();
    }

    private IEnumerator Flicker(BlurVolumeManager blur, float duration, bool endless)
    {
        // Take over from any blur tween still running (a Blur effect's fade, a previous release).
        Tween.StopAll(blur);

        Run run = new Run
        {
            blur = blur,
            start = Time.time,
            from = blur.Radius,
            seed = Random.Range(0f, 1000f),
        };

        int sinceStrong = 0;

        while (endless || Time.time - run.start < duration)
        {
            yield return Rest(run, RandomRange(wait));

            Peak peak = RollPeak(ref sinceStrong);
            if (peak == null) continue;

            int count = Mathf.Max(1, Random.Range(
                Mathf.Min(peak.flickers.x, peak.flickers.y),
                Mathf.Max(peak.flickers.x, peak.flickers.y) + 1));

            for (int i = 0; i < count; i++)
            {
                if (i > 0) yield return Rest(run, RandomRange(peak.gap));
                yield return FlickerOnce(run, peak);
            }
        }
    }

    /// <summary>Strong first, then weak, else a quiet beat (null).</summary>
    private Peak RollPeak(ref int sinceStrong)
    {
        float roll = Random.value;
        bool forced = strongAfter > 0 && sinceStrong >= strongAfter;

        if (forced || roll < strong.chance)
        {
            sinceStrong = 0;
            return strong;
        }

        sinceStrong++;
        return roll < strong.chance + weak.chance ? weak : null;
    }

    /// <summary>One flicker: rise fast, hold, fall back to the base.</summary>
    private IEnumerator FlickerOnce(Run run, Peak peak)
    {
        float height = RandomRange(peak.radius) - Base(run);

        for (float t = 0f; t < peak.attack; t += Time.deltaTime)
        {
            float p = t / peak.attack;
            run.level = height * (1f - (1f - p) * (1f - p));
            Apply(run);
            yield return null;
        }

        run.level = height;
        yield return Rest(run, peak.hold, height);

        for (float t = 0f; t < peak.decay; t += Time.deltaTime)
        {
            float p = 1f - t / peak.decay;
            run.level = height * p * p;
            Apply(run);
            yield return null;
        }

        run.level = 0f;
        Apply(run);
    }

    /// <summary>Stays at <paramref name="level"/> above the base for a while, jitter still running.</summary>
    private IEnumerator Rest(Run run, float seconds, float level = 0f)
    {
        run.level = level;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            Apply(run);
            yield return null;
        }
    }

    private void Apply(Run run)
    {
        float wobble = jitter > 0f
            ? (Mathf.PerlinNoise(run.seed, Time.time * jitterFrequency) * 2f - 1f) * jitter
            : 0f;
        float target = Mathf.Clamp01(Base(run) + wobble + run.level);

        float blend = fadeIn > 0f ? Mathf.Clamp01((Time.time - run.start) / fadeIn) : 1f;
        run.blur.Radius = Mathf.Lerp(run.from, target, blend);
    }

    private float Base(Run run) => baseIsRest ? run.blur.RestRadius : baseRadius;

    private static float RandomRange(Vector2 range)
        => Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));

    private BlurVolumeManager Resolve(VisualNovelTalkable vn)
    {
        BlurVolumeManager blur = vn ? vn.Blur : null;
        if (!blur) h.Out($"{name}: no BlurVolumeManager found - add one to the Volume that holds the VolFx Blur override.");
        return blur;
    }
}
