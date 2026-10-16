using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PrimeTween;

// A transform shake built on AnimationBase. Perlin-noise displaces the object on the
// chosen axes with an amplitude that decays over the duration, then it settles back to
// rest. Like FloatingAnimation, only the *delta* of the shake offset is written to
// localPosition each frame, so the shake adds to other movers (wandering, parent motion)
// instead of overwriting them.
//
// World is horizontal: X = right/left, Z = forward/back, Y = up (see CLAUDE.md). The Axis
// enum keeps that XZ-first order so a ground shake defaults to the horizontal plane.
public class ShakeAnimation : AnimationBase
{
    public enum Axis { X, Z, Y }

    [Header("Shake Settings")]
    [Tooltip("Axes affected by the shake. World is XZ-horizontal, Y is up.")]
    public List<Axis> targetAxes = new List<Axis> { Axis.X, Axis.Z };
    [Tooltip("How long the shake lasts, in seconds.")]
    public float duration = 0.5f;
    [Tooltip("When true, the shake decelerates: its amplitude decays to zero over the duration " +
             "(controlled by 'sharpening'). When false, the shake keeps full strength for the whole " +
             "duration and stops abruptly at the end.")]
    public bool deceleration = false;
    [Tooltip("Envelope exponent controlling how quickly the shake decays to zero. " +
             "1 = linear falloff, higher = punchier (amplitude drops fast then lingers small), " +
             "<1 = sustains longer before dying out. Only used when 'deceleration' is on.")]
    public float sharpening = 2f;
    [Tooltip("Peak displacement (local units) at the start of the shake.")]
    public float strength = 0.3f;
    [Tooltip("Oscillation speed of the shake noise. Higher = more rapid, jittery shaking.")]
    public float frequency = 25f;
    [Tooltip("Resting local offset the shake moves around and returns to. " +
             "When customInitialState is off, this is captured (0) on Awake.")]
    public Vector3 initialState;

    // The shake offset currently baked into transform.localPosition. Only the delta of
    // this is applied each frame so the shake stacks with other localPosition movers.
    // Protected so UI variants (ShakeAnimationUI) can reuse the same bookkeeping while
    // writing the offset to a different target (e.g. RectTransform.anchoredPosition).
    protected Vector3 _appliedOffset;

    /// <summary>
    /// The shake offset currently baked into the transform. Lets a script that sets the transform's
    /// position outright every frame (e.g. <see cref="PencilMarker"/>) add the shake back on top.
    /// </summary>
    public Vector3 AppliedOffset => _appliedOffset;

    public override void Awake()
    {
        base.Awake();
        _appliedOffset = Vector3.zero;

        if (ShouldCaptureInitialState()) initialState = _appliedOffset;
        else SetOffset(initialState);
    }

    public override void ReturnToInitialState()
    {
        SetOffset(initialState);
    }

    // The transform was reset to rest by external code, so the offset it represents is the
    // initial one again. Update bookkeeping only (no move) so the next Play() starts clean.
    public override void SyncToRestingState()
    {
        _appliedOffset = initialState;
    }

    // Applies target as the current shake offset, moving the transform by the delta only
    // so other movers of localPosition are not overwritten. Virtual so UI variants can
    // redirect the offset onto a RectTransform instead of localPosition.
    protected virtual void SetOffset(Vector3 target)
    {
        transform.localPosition += target - _appliedOffset;
        _appliedOffset = target;
    }

    // make shake anim
    public override IEnumerator AnimationCoroutine()
    {
        if (duration > 0f && targetAxes.Count > 0)
        {
            // Independent Perlin streams per axis so X/Y/Z aren't correlated, re-seeded
            // each play so successive shakes look different.
            float seedX = h.Range(0f, 1000f);
            float seedY = h.Range(0f, 1000f);
            float seedZ = h.Range(0f, 1000f);

            bool shakeX = targetAxes.Contains(Axis.X);
            bool shakeY = targetAxes.Contains(Axis.Y);
            bool shakeZ = targetAxes.Contains(Axis.Z);

            float exponent = Mathf.Max(0.0001f, sharpening);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                // With deceleration on, amplitude eases from full strength to zero over the
                // duration; off, it stays at full strength the whole time.
                float amplitude = deceleration ? strength * Mathf.Pow(1f - t, exponent) : strength;
                float noiseT = elapsed * frequency;

                Vector3 shake = Vector3.zero;
                if (shakeX) shake.x = SampleNoise(seedX, noiseT) * amplitude;
                if (shakeY) shake.y = SampleNoise(seedY, noiseT) * amplitude;
                if (shakeZ) shake.z = SampleNoise(seedZ, noiseT) * amplitude;

                SetOffset(initialState + shake);
                yield return null;
            }

            SetOffset(initialState);
        }

        yield return base.AnimationCoroutine();
    }

    // Perlin noise remapped from [0,1] to [-1,1] so the shake swings both ways around rest.
    private static float SampleNoise(float seed, float noiseT)
    {
        return Mathf.PerlinNoise(seed, noiseT) * 2f - 1f;
    }

    // Eases the current shake offset back to rest over returningTime using returningEasing,
    // used when the shake is Stop()ped mid-way (the natural end already settles at rest).
    public override IEnumerator ReturnToInitialStateAnimated()
    {
        if (returningTime <= 0f)
        {
            SetOffset(initialState);
            yield break;
        }

        Vector3 from = _appliedOffset;
        float elapsed = 0f;
        while (elapsed < returningTime)
        {
            elapsed += Time.deltaTime;
            float t = EvaluateReturningEasing(Mathf.Clamp01(elapsed / returningTime));
            SetOffset(Vector3.LerpUnclamped(from, initialState, t));
            yield return null;
        }

        SetOffset(initialState);
    }
}
