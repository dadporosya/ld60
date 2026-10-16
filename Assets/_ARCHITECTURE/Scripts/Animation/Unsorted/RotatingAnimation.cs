using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PrimeTween;

public class RotatingAnimation : AnimationBase
{
    [Header("Rotating Settings")]
    [Tooltip("Sequence of additive rotation keyframes, in degrees per axis. Each entry rotates the object BY this amount " +
             "on top of the previous keyframe. e.g. frames (20,0,0) then (30,0,0) from rest 0 = 0 -> 20 -> 50 -> (looped) back to 0.")]
    public List<Vector3> frames = new List<Vector3>();
    [Tooltip("Time to perform each keyframe's rotation. Falls back to defaultDuration when missing.")]
    public List<float> durations = new List<float>();
    public float defaultDuration = 0.2f;
    [Tooltip("Easing applied while rotating between keyframes.")]
    public Ease ease = Ease.Default;
    public bool randomPhase;
    [Tooltip("Resting local rotation offset (degrees) the animation starts from and returns to. When customInitialState is off, this is captured (0) on Awake.")]
    public Vector3 initialState;

    // The rotation offset (euler degrees) currently baked into transform.localEulerAngles. Only
    // the *delta* is ever applied, so this composes with other rotators (FlipByVelocity3D etc.)
    // instead of overwriting them.
    private Vector3 _appliedOffset;
    // The rotation tween currently driving the transform. Tracked so it can be stopped on
    // return/stop — otherwise it keeps writing rotation after the coroutine stops.
    private Tween _rotationTween;
    private bool _phaseConsumed;

    public override void Awake()
    {
        _appliedOffset = Vector3.zero;

        if (ShouldCaptureInitialState()) initialState = _appliedOffset;
        else SetOffset(initialState);
    }

    public override IEnumerator Play()
    {
        _phaseConsumed = false;
        yield return base.Play();
    }

    public override void ReturnToInitialState()
    {
        if (_rotationTween.isAlive) _rotationTween.Stop();
        SetOffset(initialState);
    }

    public override void SyncToRestingState()
    {
        if (_rotationTween.isAlive) _rotationTween.Stop();
        _appliedOffset = initialState;
    }

    // Applies target as the current rotation offset, rotating the transform by the delta only.
    private void SetOffset(Vector3 target)
    {
        transform.localEulerAngles += target - _appliedOffset;
        _appliedOffset = target;
    }

    // Tweens the offset from its current value to target over duration.
    private IEnumerator TweenOffset(Vector3 target, float duration, Ease tweenEase)
    {
        if (duration > 0f)
        {
            _rotationTween = Tween.Custom(_appliedOffset, target, duration, val => SetOffset(val), tweenEase);
            while (_rotationTween.isAlive) yield return null;
        }
        SetOffset(target);
    }

    public override IEnumerator AnimationCoroutine()
    {
        if (_rotationTween.isAlive) _rotationTween.Stop();

        if (frames.Count == 0)
        {
            yield return base.AnimationCoroutine();
            yield break;
        }

        bool applyPhase = !_phaseConsumed && randomPhase && loop;
        float phase = applyPhase ? h.Range(0f, 1f) : 0f;
        _phaseConsumed = true;

        // Each frame rotates BY its amount on top of the previous one (additive), e.g.
        // frames 20, 30 walk the offset initial -> +20 -> +50.
        Vector3 target = initialState;
        for (int i = 0; i < frames.Count; i++)
        {
            target += frames[i];
            float duration = i < durations.Count ? durations[i] : defaultDuration;

            if (i == 0 && applyPhase)
            {
                SetOffset(Vector3.LerpUnclamped(_appliedOffset, target, phase));
                duration *= (1f - phase);
            }

            yield return TweenOffset(target, duration, ease);
        }

        // A looping pass ends back at the initial state so the next pass starts from rest
        // (0 -> 20 -> 50 -> 0 -> 20 ...). AnimationBase skips its own return while looping.
        if (loop) yield return ReturnToInitialStateAnimated();

        yield return base.AnimationCoroutine();
    }

    // Eases back to the initial state over returningTime using returningEasing, taking the
    // shortest way per axis (a full 360 spin doesn't unwind all the way back).
    public override IEnumerator ReturnToInitialStateAnimated()
    {
        if (_rotationTween.isAlive) _rotationTween.Stop();

        // Re-express the current offset as the equivalent angle closest to initialState, without
        // moving the object, so the tween below takes the short way round.
        Vector3 cur = _appliedOffset;
        _appliedOffset = new Vector3(
            initialState.x + Mathf.DeltaAngle(initialState.x, cur.x),
            initialState.y + Mathf.DeltaAngle(initialState.y, cur.y),
            initialState.z + Mathf.DeltaAngle(initialState.z, cur.z));

        _rotationTween = default;
        float duration = Mathf.Max(0f, returningTime);
        if (duration > 0f)
        {
            _rotationTween = Tween.Custom(_appliedOffset, initialState, duration, val => SetOffset(val),
                returningEasing == Ease.Default ? Ease.OutQuad : returningEasing);
            while (_rotationTween.isAlive) yield return null;
        }
        SetOffset(initialState);
    }
}
