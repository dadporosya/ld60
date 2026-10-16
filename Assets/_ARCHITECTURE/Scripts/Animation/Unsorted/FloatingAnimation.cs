using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PrimeTween;

public class FloatingAnimation : AnimationBase
{
    [Header("Floating Settings")]
    [Tooltip("Sequence of offset keyframes. Each entry moves the object to initialState + direction*distance, in order. " +
             "e.g. dir (1,0,0) dist 10 then dir (-1,0,0) dist 10 = move +10 on X, then to -10 on X, then (optionally) back to init.")]
    public List<Vector3> directions = new List<Vector3>();
    public List<float> distances = new List<float>();
    [Tooltip("Time to move to each keyframe. Falls back to defaultDuration when missing.")]
    public List<float> durations = new List<float>();
    public float defaultDuration = 1f;
    [Tooltip("Stride through the keyframe list. 1 = visit every keyframe (1,2,3,4...). " +
             "2 = skip every other one (1,3,5,7...), 3 = every third, etc. Rounded, min 1.")]
    public float sharpness = 1f;
    [Tooltip("Easing applied while moving between keyframes.")]
    public Ease ease = Ease.Default;
    public bool randomPhase;
    [Tooltip("Resting local offset the float moves around and returns to. When customInitialState is off, this is captured (0) on Awake.")]
    public Vector3 initialState;

    // The float offset currently baked into transform.localPosition. We only ever
    // apply the *delta* of this each frame so the floating adds to other movement
    // (wandering, parent motion, etc.) instead of overwriting it.
    private Vector3 _appliedOffset;
    // The offset tween currently driving the transform. Tracked so it can be stopped on
    // return/stop — otherwise it keeps writing localPosition after the coroutine stops.
    private Tween _offsetTween;
    private bool _phaseConsumed;

    public override void Awake()
    {
        _appliedOffset = Vector3.zero;

        if (ShouldCaptureInitialState()) initialState = _appliedOffset;
        else SetOffset(initialState);
    }

    // The loop-restart flag is gone: Play() no longer re-enters itself to loop (AnimationBase.Play
    // owns the loop now), so a fresh Play() is the only thing that reaches here and the random
    // phase should be re-rolled every time.
    public override IEnumerator Play()
    {
        _phaseConsumed = false;
        yield return base.Play();
    }

    public override void ReturnToInitialState()
    {
        if (_offsetTween.isAlive) _offsetTween.Stop();
        SetOffset(initialState);
    }

    // The transform was reset to its resting pose by external code, so the float offset it
    // currently represents is the initial one again. Update the bookkeeping only (no move)
    // so the next Play() walks from the initial state instead of a stale final offset.
    public override void SyncToRestingState()
    {
        if (_offsetTween.isAlive) _offsetTween.Stop();
        _appliedOffset = initialState;
    }

    // Applies target as the current float offset, moving the transform by the delta only
    // so other movers of localPosition are not overwritten.
    private void SetOffset(Vector3 target)
    {
        transform.localPosition += target - _appliedOffset;
        _appliedOffset = target;
    }

    public override IEnumerator AnimationCoroutine()
    {
        if (_offsetTween.isAlive) _offsetTween.Stop();

        if (directions.Count == 0)
        {
            yield return base.AnimationCoroutine();
            yield break;
        }

        bool applyPhase = !_phaseConsumed && randomPhase && loop;
        float phase = applyPhase ? h.Range(0f, 1f) : 0f;
        _phaseConsumed = true;

        // Walk the keyframes: move from the current offset to initialState + dir*dist,
        // one after another. We never pass back through initialState between keyframes;
        // the return to initialState only happens at the natural end (below).
        // sharpness is the stride: 1 visits every keyframe, 2 skips every other one, etc.
        int step = Mathf.Max(1, Mathf.RoundToInt(sharpness));
        for (int i = 0; i < directions.Count; i += step)
        {
            Vector3 dir = directions[i].normalized;
            float dist = i < distances.Count ? distances[i] : 0f;
            Vector3 target = initialState + dir * dist;
            float duration = i < durations.Count ? durations[i] : defaultDuration;
            Vector3 from = _appliedOffset;

            if (i == 0 && applyPhase)
            {
                from = Vector3.LerpUnclamped(from, target, phase);
                duration *= (1f - phase);
                SetOffset(from);
            }

            if (duration > 0f)
            {
                _offsetTween = Tween.Custom(from, target, duration, val => SetOffset(val), ease);
                while (_offsetTween.isAlive) yield return null;
            }

            SetOffset(target);
        }

        // A looping pass normally runs straight into the next one (base.AnimationCoroutine skips the return), which
        // suits keyframes that swing back and forth. It doesn't suit a single keyframe: every pass after the first
        // would move from that keyframe to itself, so the object slides over once and then sits there. Ease back
        // to the initial state between passes then, and whenever returnToTheInittialState asks for it.
        if (loop && (returnToTheInittialState || KeyframesPerPass(step) == 1))
            yield return ReturnToInitialStateAnimated();

        yield return base.AnimationCoroutine();
    }

    private int KeyframesPerPass(int step) => (directions.Count + step - 1) / step;

    // Eases the current float offset back to the initial state over returningTime using returningEasing.
    public override IEnumerator ReturnToInitialStateAnimated()
    {
        if (_offsetTween.isAlive) _offsetTween.Stop();

        Vector3 from = _appliedOffset;

        if (returningTime <= 0f)
        {
            SetOffset(initialState);
            yield break;
        }

        _offsetTween = Tween.Custom(from, initialState, returningTime, val => SetOffset(val), returningEasing);
        while (_offsetTween.isAlive) yield return null;

        SetOffset(initialState);
    }
}
