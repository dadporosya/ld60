using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PrimeTween;

public class SquishAnimation : AnimationBase
{
    [Header("Squish Settings")]
    [Tooltip("Peak scale delta per frame (per axis). Negative = squish, positive = stretch. Z is supported.")]
    public List<Vector3> squishes = new List<Vector3>();
    public List<float> durations = new List<float>();
    public float defaultDuration = 0.3f;
    [Tooltip("Easing applied to the squish's time progression.")]
    public Ease ease = Ease.Default;
    [Tooltip("Resting local scale the squish pulses from and returns to. When customInitialState is off, this is captured on Awake.")]
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
        // Chain squishes: ramp from the previous pose into each target and hold there,
        // never resetting to rest between them. The object settles at the last squish;
        // the base handles the single, optional return (ReturnToInitialStateAnimated)
        // at the natural end when returnToTheInittialState is on.
        //
        // Seed from the CURRENT pose, not an assumed rest: on a looped restart with
        // returnToTheInittialState off the object is still at the last squish, so the
        // first squish must ramp smoothly from there instead of snapping to initialState.
        Vector3 currentDelta = CurrentDelta();

        for (int i = 0; i < squishes.Count; i++)
        {
            Vector3 fromDelta = currentDelta;
            Vector3 toDelta = squishes[i];
            float dur = i < durations.Count ? durations[i] : defaultDuration;

            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / dur);
                float easedT = ease == Ease.Custom ? t : Easing.Evaluate(t, ease);
                Vector3 delta = Vector3.LerpUnclamped(fromDelta, toDelta, easedT);
                ApplySquish(delta, 1f);
                yield return null;
            }

            currentDelta = toDelta;
        }

        yield return base.AnimationCoroutine();
    }

    // Inverse of ApplySquish: recovers the current squish delta from the live scale,
    // so a chain can resume from wherever the object currently sits (e.g. a looped
    // restart that never returned to the initial state).
    private Vector3 CurrentDelta()
    {
        Vector3 scale = transform.localScale;
        return new Vector3(
            initialState.x != 0f ? scale.x / initialState.x - 1f : 0f,
            initialState.y != 0f ? scale.y / initialState.y - 1f : 0f,
            initialState.z != 0f ? scale.z / initialState.z - 1f : 0f
        );
    }

    private void ApplySquish(Vector3 delta, float shaped)
    {
        transform.localScale = new Vector3(
            initialState.x * (1f + delta.x * shaped),
            initialState.y * (1f + delta.y * shaped),
            initialState.z * (1f + delta.z * shaped)
        );
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
