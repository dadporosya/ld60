using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PrimeTween;

// Like SquishAnimation, but the frames are ABSOLUTE local scales instead of deltas from
// the resting scale. A frame of (0,0,0) scales the object to zero; a frame of (1,1,1) is
// the object at unit scale, regardless of what initialState is.
public class ScalingAnimation : AnimationBase
{
    [Header("Scaling Settings")]
    [Tooltip("Absolute local scale per frame. The object interpolates exactly to these values (e.g. (0,0,0) = fully collapsed).")]
    public List<Vector3> scales = new List<Vector3>();
    public List<float> durations = new List<float>();
    public float defaultDuration = 0.3f;
    [Tooltip("Easing applied to the scaling's time progression.")]
    public Ease ease = Ease.Default;
    [Tooltip("Resting local scale returned to at the end. When customInitialState is off, this is captured on Awake.")]
    public Vector3 initialState = Vector3.one;

    public override void Awake()
    {
        base.Awake();

        if (ShouldCaptureInitialState()) initialState = transform.localScale;
        else transform.localScale = initialState;
    }

    public override void ReturnToInitialState()
    {
        transform.localScale = initialState;
    }

    public override IEnumerator AnimationCoroutine()
    {
        // Chain the frames: ramp from the current pose into each absolute scale and hold
        // there, never resetting between frames. The object settles at the last frame; the
        // base handles the single, optional return (ReturnToInitialStateAnimated).
        //
        // Seed from the CURRENT localScale, not from initialState: on a looped restart with
        // returnToTheInittialState off the object still sits at the last frame, so the first
        // frame must ramp smoothly from there instead of snapping.
        Vector3 current = transform.localScale;

        for (int i = 0; i < scales.Count; i++)
        {
            Vector3 from = current;
            Vector3 to = scales[i];
            float dur = i < durations.Count ? durations[i] : defaultDuration;

            if (dur <= 0f)
            {
                transform.localScale = to;
                current = to;
                continue;
            }

            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / dur);
                float easedT = ease == Ease.Custom ? t : Easing.Evaluate(t, ease);
                transform.localScale = Vector3.LerpUnclamped(from, to, easedT);
                yield return null;
            }

            transform.localScale = to;
            current = to;
        }

        yield return base.AnimationCoroutine();
    }

    // Eases the scale back to the initial state over returningTime using returningEasing.
    public override IEnumerator ReturnToInitialStateAnimated()
    {
        if (returningTime <= 0f)
        {
            transform.localScale = initialState;
            yield break;
        }

        Vector3 from = transform.localScale;
        float elapsed = 0f;
        while (elapsed < returningTime)
        {
            elapsed += Time.deltaTime;
            float t = EvaluateReturningEasing(Mathf.Clamp01(elapsed / returningTime));
            transform.localScale = Vector3.LerpUnclamped(from, initialState, t);
            yield return null;
        }

        transform.localScale = initialState;
    }
}
