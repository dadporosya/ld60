using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace PrimeTween {
    [StructLayout(LayoutKind.Explicit, Size =
        #if UNITY_ASSERTIONS && !PRIME_TWEEN_DISABLE_ASSERTIONS
        128
        #else
        64
        #endif
    )]
    internal struct UnmanagedTweenData {
        [FieldOffset(0)] internal Flags flags;
        [FieldOffset(4)] internal float elapsedTimeTotal;
        [FieldOffset(8)] internal float timeScale;
        [FieldOffset(12)] internal int cyclesDone;
        [FieldOffset(16)] internal int cyclesTotal;
        [FieldOffset(20)] internal float waitDelay;
        [FieldOffset(24)] internal float cycleDuration;
        [FieldOffset(28)] internal float startDelay;
        [FieldOffset(32)] internal float animationDuration;
        [FieldOffset(36)] internal Ease ease;
        [FieldOffset(37)] internal CycleMode cycleMode;
        [FieldOffset(38)] internal TweenAnimation.TweenType tweenType;
        [FieldOffset(39)] internal _UpdateType updateType;
        [FieldOffset(40)] internal float easedInterpolationFactor;
        [FieldOffset(44)] internal int cyclesDoneBeforeJob;
        [FieldOffset(48)] internal TweenAnimation.ValueWrapper startValue;

        #if UNITY_ASSERTIONS && !PRIME_TWEEN_DISABLE_ASSERTIONS
        [FieldOffset(64)] internal long id;
        #else
        internal long id { get => -1; set { } }
        #endif

        float calcTFromElapsedTimeTotal(float _elapsedTimeTotal, out State newState) {
            // key timeline points: 0 | startDelay | duration | 1 | endDelay | onComplete
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (_elapsedTimeTotal == float.MaxValue) {
                Assert.AreNotEqual(-1, cyclesTotal);
                Assert.IsTrue(cyclesDone <= cyclesTotal);
                cyclesDone = cyclesTotal;
                newState = State.After;
                return 1f;
            }
            _elapsedTimeTotal -= waitDelay; // waitDelay is applied before calculating cycles
            if (_elapsedTimeTotal < 0f) {
                cyclesDone = TweenData.iniCyclesDone;
                newState = State.Before;
                return 0f;
            }
            Assert.IsTrue(_elapsedTimeTotal >= 0f);
            Assert.AreNotEqual(float.MaxValue, _elapsedTimeTotal);
            if (cycleDuration == 0f) {
                if (cyclesTotal == -1) {
                    // add max one cycle per frame
                    if (timeScale > 0f) {
                        if (cyclesDone == TweenData.iniCyclesDone) {
                            cyclesDone = 1;
                        } else {
                            cyclesDone++;
                        }
                    } else if (timeScale != 0f) {
                        if (cyclesDone != TweenData.iniCyclesDone) {
                            cyclesDone--;
                        }
                        if (cyclesDone == TweenData.iniCyclesDone) {
                            newState = State.Before;
                            return 0f;
                        }
                    }
                    newState = State.Running;
                    return 1f;
                }
                Assert.AreNotEqual(-1, cyclesTotal);
                if (_elapsedTimeTotal == 0f) {
                    cyclesDone = TweenData.iniCyclesDone;
                    newState = State.Before;
                    return 0f;
                }
                Assert.IsTrue(cyclesDone <= cyclesTotal);
                cyclesDone = cyclesTotal;
                newState = State.After;
                return 1f;
            }
            Assert.AreNotEqual(0f, cycleDuration);
            float cyclesFloat = _elapsedTimeTotal / cycleDuration;
            cyclesDone = cyclesTotal != -1 && cyclesFloat >= cyclesTotal ? cyclesTotal : (int)cyclesFloat; // casting a huge float to int overflows
            if (cyclesTotal != -1 && cyclesDone == cyclesTotal) {
                newState = State.After;
                return 1f;
            }
            float elapsedTimeInCycle = _elapsedTimeTotal - cycleDuration * cyclesDone - startDelay;
            if (elapsedTimeInCycle < 0f) {
                newState = State.Before;
                return 0f;
            }
            Assert.IsTrue(elapsedTimeInCycle >= 0f);
            if (animationDuration == 0f) {
                newState = State.After;
                return 1f;
            }
            Assert.AreNotEqual(0f, animationDuration);
            float result = elapsedTimeInCycle / animationDuration;
            if (result > 1f) {
                newState = State.After;
                return 1f;
            }
            newState = State.Running;
            Assert.IsTrue(result >= 0f);
            return result;
        }

        internal bool canManipulate() => !isInSequence || tweenType == TweenAnimation.TweenType.MainSequence;

        internal bool IsMainSequenceRoot() => tweenType == TweenAnimation.TweenType.MainSequence;
        internal bool IsSequenceRoot() => tweenType == TweenAnimation.TweenType.MainSequence || tweenType == TweenAnimation.TweenType.NestedSequence;

        internal bool trySetPause(bool isPaused) {
            if (this.isPaused == isPaused) {
                return false;
            }
            this.isPaused = isPaused;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool GetFlag(Flags flag) => (flags & flag) != 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetFlag(Flags flag, bool value) {
            if (value) {
                flags |= flag;
            } else {
                flags &= ~flag;
            }
        }

        internal bool isPaused {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => GetFlag(Flags.IsPaused);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => SetFlag(Flags.IsPaused, value);
        }
        internal bool isInSequence {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => GetFlag(Flags.IsInSequence);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => SetFlag(Flags.IsInSequence, value);
        }
        internal bool useUnscaledTime {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => GetFlag(Flags.UseUnscaledTime);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => SetFlag(Flags.UseUnscaledTime, value);
        }
        internal bool isValueChanged {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => GetFlag(Flags.IsValueChanged);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => SetFlag(Flags.IsValueChanged, value);
        }
        internal bool isDone {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => GetFlag(Flags.IsDone);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => SetFlag(Flags.IsDone, value);
        }
        internal bool startFromCurrent {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => GetFlag(Flags.StartFromCurrent);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => SetFlag(Flags.StartFromCurrent, value);
        }
        internal bool isAlive {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => GetFlag(Flags.IsAlive);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => SetFlag(Flags.IsAlive, value);
        }
        internal bool isUpdatedInJob {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => GetFlag(Flags.IsUpdatedInJob);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => SetFlag(Flags.IsUpdatedInJob, value);
        }
        internal bool hasOnUpdate {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => GetFlag(Flags.HasOnUpdate);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => SetFlag(Flags.HasOnUpdate, value);
        }
        internal bool resetOnComplete {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => GetFlag(Flags.ResetOnComplete);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => SetFlag(Flags.ResetOnComplete, value);
        }
        internal bool IsResetting {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => GetFlag(Flags.IsResetting);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => SetFlag(Flags.IsResetting, value);
        }
        internal bool stoppedEmergently {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => GetFlag(Flags.StoppedEmergently);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => SetFlag(Flags.StoppedEmergently, value);
        }
        internal bool isAdditive {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => GetFlag(Flags.Additive);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => SetFlag(Flags.Additive, value);
        }
        internal bool warnEndValueEqualsCurrent {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => GetFlag(Flags.WarnEndValueEqualsCurrent);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => SetFlag(Flags.WarnEndValueEqualsCurrent, value);
        }
        internal bool isSequenceInverted {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => GetFlag(Flags.IsSequenceInverted);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => SetFlag(Flags.IsSequenceInverted, value);
        }
        internal bool shakeSign {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => GetFlag(Flags.ShakeSign);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => SetFlag(Flags.ShakeSign, value);
        }
        internal bool isPunch {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => GetFlag(Flags.ShakePunch);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => SetFlag(Flags.ShakePunch, value);
        }
        internal bool isUpdating {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => GetFlag(Flags.IsUpdating);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => SetFlag(Flags.IsUpdating, value);
        }
        internal bool warnIgnoredOnCompleteIfTargetDestroyed {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => GetFlag(Flags.WarnIgnoredOnCompleteIfTargetDestroyed);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => SetFlag(Flags.WarnIgnoredOnCompleteIfTargetDestroyed, value);
        }
        internal bool IsCustomEaseSameStartEndValues {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => GetFlag(Flags.IsCustomEaseSameStartEndValues);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => SetFlag(Flags.IsCustomEaseSameStartEndValues, value);
        }
        internal bool ModificationIsNotAllowed {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => GetFlag(Flags.ModificationIsNotAllowed);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => SetFlag(Flags.ModificationIsNotAllowed, value);
        }
        internal bool HasCancellationToken {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => GetFlag(Flags.HasCancellationToken);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => SetFlag(Flags.HasCancellationToken, value);
        }
        internal bool HasWait {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => GetFlag(Flags.HasWait);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => SetFlag(Flags.HasWait, value);
        }
        internal bool IsWaitUntil {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => GetFlag(Flags.IsWaitUntil);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => SetFlag(Flags.IsWaitUntil, value);
        }
        internal bool IsCompleting {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => GetFlag(Flags.IsCompleting);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => SetFlag(Flags.IsCompleting, value);
        }

        internal bool IsDone(int cyclesDiff) {
            Assert.IsTrue(cyclesTotal == -1 || cyclesDone <= cyclesTotal);
            if (timeScale > 0f) {
                return cyclesDiff > 0 && cyclesDone == cyclesTotal;
            } else {
                return cyclesDiff < 0 && cyclesDone == TweenData.iniCyclesDone;
            }
        }

        internal bool CanUpdateData() {
            return isAlive && !isInSequence && ease != Ease.Custom && !startFromCurrent;
        }

        internal float UpdateData(float newElapsedTimeTotal) {
            elapsedTimeTotal = newElapsedTimeTotal;

            int oldCyclesDone = cyclesDone;
            float t = calcTFromElapsedTimeTotal(elapsedTimeTotal, out State newState);
            int cyclesDiff = cyclesDone - oldCyclesDone;
            isDone = IsDone(cyclesDiff);

            bool valueChanged = newState == State.Running || GetState() != newState;
            isValueChanged = valueChanged;
            if (isValueChanged) {
                // print($"new state: {GetState()}/{newState}, cycles: {cyclesDone}/{cyclesTotal} (diff: {cyclesDiff}), elapsedTimeTotal: {elapsedTimeTotal}, t: {t}");
                SetState(newState);
            } else {
                // print($"same state: {newState}, cycles: {cyclesDone}/{cyclesTotal} (diff: {cyclesDiff}), elapsedTimeTotal: {elapsedTimeTotal}");
            }
            return t;
        }

        // A callback of an earlier animation accesses this tween after the job has updated it, but before the manager has reported the job's result.
        // Undo the job's update, so the manager updates this tween on the main thread, the same way as tweens that are not updated in the job.
        internal void TryRollBackJobUpdate() {
            if (isUpdatedInJob) {
                isUpdatedInJob = false;
                var manager = PrimeTweenManager.Instance;
                // a float subtraction doesn't always restore the exact old value, but the error is within 1 ulp
                elapsedTimeTotal -= (useUnscaledTime ? manager.jobUnscaledDeltaTime : manager.jobDeltaTime) * timeScale;
                cyclesDone = cyclesDoneBeforeJob;
                var state = GetJobState();
                SetState(state);
                float t;
                if (state == State.Running) {
                    t = calcTFromElapsedTimeTotal(elapsedTimeTotal, out _);
                    cyclesDone = cyclesDoneBeforeJob;
                } else {
                    t = state == State.After ? 1f : 0f;
                }
                easedInterpolationFactor = calcEasedT(t);
            }
        }

        [System.Diagnostics.Conditional("PRIME_TWEEN_SAFETY_CHECKS")]
        internal void print(string msg) {
            Debug.Log($"[{Time.frameCount}] id:{id}  {msg} {tweenType}");
        }

        internal float calcEasedT(float t) {
            switch (cycleMode) {
                case CycleMode.Restart:
                    return evaluate(t);
                case CycleMode.Incremental:
                    return evaluate(t) + clampCyclesDone(cyclesDone);
                case (CycleMode)_CycleMode.YoyoChildren:
                case CycleMode.Yoyo:
                    return IsForwardCycle(cyclesDone) ? evaluate(t) : 1 - evaluate(t);
                case CycleMode.Rewind:
                    return IsForwardCycle(cyclesDone) ? evaluate(t) : evaluate(1 - t);
                default:
                    throw new Exception();
            }
        }

        float evaluate(float t) {
            return StandardEasing.Evaluate(t, ease);
        }

        internal int clampCyclesDone(int cyclesDone_) {
            if (cyclesDone_ == TweenData.iniCyclesDone) {
                return 0;
            }
            if (cyclesDone_ == cyclesTotal) {
                Assert.AreNotEqual(-1, cyclesTotal);
                return cyclesTotal - 1;
            }
            return cyclesDone_;
        }

        internal bool IsForwardCycle(int cycle) => clampCyclesDone(cycle) % 2 == 0;

        internal int getCyclesDone() {
            int result = cyclesDone;
            if (result == TweenData.iniCyclesDone) {
                return 0;
            }
            Assert.IsTrue(result >= 0);
            return result;
        }

        internal float calcDurationWithWaitDependencies() {
            int cycles = cyclesTotal;
            Assert.IsTrue(-1 != cycles, id, "It's impossible to calculate the duration of an infinite tween (cycles == -1).");
            Assert.AreNotEqual(0, cycles);
            return waitDelay + cycleDuration * cycles;
        }

        internal PropType propType => Utils.TweenTypeToTweenData(tweenType).Item1;

        internal bool IsDefault() {
            unsafe {
                UnmanagedTweenData defaultValue = default;
                return UnsafeUtility.MemCmp(UnsafeUtility.AddressOf(ref this), UnsafeUtility.AddressOf(ref defaultValue), UnsafeUtility.SizeOf<UnmanagedTweenData>()) == 0;
            }
        }

        const Flags stateFlags = Flags.StateLow | Flags.StateHigh;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal State GetState() {
            return (State)(((uint)flags & (uint)stateFlags) / (uint)Flags.StateLow);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetState(State state) {
            flags = (flags & ~stateFlags) | (Flags)((uint)state * (uint)Flags.StateLow);
        }

        const Flags callbackDirectionFlags = Flags.CallbackDirectionLow | Flags.CallbackDirectionHigh;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal TweenAnimation.CallbackDirection GetCallbackDirection() {
            return (TweenAnimation.CallbackDirection)(((uint)flags & (uint)callbackDirectionFlags) / (uint)Flags.CallbackDirectionLow);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetCallbackDirection(TweenAnimation.CallbackDirection direction) {
            flags = (flags & ~callbackDirectionFlags) | (Flags)((uint)direction * (uint)Flags.CallbackDirectionLow);
        }

        const Flags jobStateFlags = Flags.JobStateLow | Flags.JobStateHigh;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        State GetJobState() {
            return (State)(((uint)flags & unchecked((uint)jobStateFlags)) / (uint)Flags.JobStateLow);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SetJobState(State state) {
            flags = (flags & ~jobStateFlags) | (Flags)((uint)state * (uint)Flags.JobStateLow);
        }
    }
}
