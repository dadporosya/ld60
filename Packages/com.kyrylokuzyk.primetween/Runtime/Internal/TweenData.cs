#if PRIME_TWEEN_SAFETY_CHECKS && UNITY_ASSERTIONS
#define SAFETY_CHECKS
#endif
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using JetBrains.Annotations;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using TweenType = PrimeTween.TweenAnimation.TweenType;

namespace PrimeTween {
    internal partial class ColdData {
        internal TweenArray _tweenArray;
        internal int _index;

        internal ColdData sequence;
        internal ColdData prev;
        internal ColdData next;
        internal ColdData prevSibling;
        internal ColdData nextSibling;

        [CanBeNull] internal Action<TweenData> onComplete;
        [CanBeNull] internal object onCompleteCallback;
        [CanBeNull] internal object onCompleteTarget;

        internal OnValueChangeDelegate onValueChange;
        internal object customOnValueChange; // or TweenAnimation.context

        internal AnimationCurve customEase;
        internal ParametricEase parametricEase;
        internal float parametricEaseStrength;
        internal float parametricEasePeriod;

        internal long longParam;
        internal TweenAnimation.ValueWrapper prevVal;
        internal ShakeData shakeData;
        
        [CanBeNull] internal object onUpdateTarget;
        internal object onUpdateCallback;
        internal Action<TweenData> onUpdate;

        internal long id = -1;
        internal CancellationToken cancellationToken;
        internal TweenSettings.CancellationMode cancellationMode;

        [CanBeNull] internal object waitPredicateTarget;
        [CanBeNull] internal object waitPredicate;
        [CanBeNull] internal Predicate<ColdData> waitPredicateShouldWait;

        #if UNITY_EDITOR
        internal string debugDescription;
        internal int indexInTweenAnimation;
        #endif



        internal bool hasData {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _tweenArray != null && _index >= 0 && _tweenArray.GetData().Length > _index;
        }

        internal unsafe ref UnmanagedTweenData data {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                Assert.IsTrue(hasData, null, nameof(hasData));
                return ref *(_tweenArray._dataPtr + _index);
            }
        }

        internal ref TweenData managedData {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                Assert.IsTrue(hasData);
                return ref _tweenArray[_index];
            }
        }

        internal int intParam {
            get => (int)longParam;
            set => longParam = value;
        }

        /// _getter is null for custom tweens
        internal void Setup([CanBeNull] object _target, ref TweenSettings _settings, bool _startFromCurrent, TweenType _tweenType, ref TweenData rt, ref UnmanagedTweenData d) {
            Assert.IsTrue(hasData);
            Assert.AreNotEqual(0, d.id);
            Assert.AreEqual(d.id, id);
            _settings.SetValidValues();
            Assert.IsTrue(_settings.cycles >= -1);
            Assert.AreNotEqual(TweenType.Disabled, _tweenType);

            var manager = PrimeTweenManager.Instance;
            var propType = Utils.TweenTypeToTweenData(_tweenType).Item1;
            Assert.AreNotEqual(PropType.None, propType);
            d.tweenType = _tweenType;
            if (_settings.ease == Ease.Default) {
                _settings.ease = manager.defaultEase;
            } else if (_settings.ease == Ease.Custom && _settings.parametricEase == ParametricEase.None) {
                AnimationCurve curve = _settings.customEase;
                if (curve != null && TweenSettings.ValidateCustomCurveKeyframes(curve)) {
                    var startKey = curve[0];
                    var endKey = curve[curve.length - 1];
                    d.IsCustomEaseSameStartEndValues = Mathf.Approximately(startKey.value, endKey.value);
                } else {
                    Debug.LogError($"Ease type is Ease.Custom, but {nameof(TweenSettings.customEase)} is not configured correctly.", _target as UnityEngine.Object);
                    _settings.ease = manager.defaultEase;
                }
            }

            Revive(ref d);

            d.flags |= Flags.WarnIgnoredOnCompleteIfTargetDestroyed;
            d.SetState(State.Before);
            d.easedInterpolationFactor = float.MinValue;
            d.cyclesDone = TweenData.iniCyclesDone;
            d.timeScale = 1f;

            d.animationDuration = _settings.duration;
            d.ease = _settings.ease;
            d.cyclesTotal = _settings.cycles;
            d.cycleMode = _settings.cycleMode;
            d.startDelay = _settings.startDelay;
            d.useUnscaledTime = _settings.useUnscaledTime;
            d.startFromCurrent = _startFromCurrent;

            customEase = _settings.customEase;
            parametricEase = _settings.parametricEase;
            parametricEaseStrength = _settings.parametricEaseStrength;
            parametricEasePeriod = _settings.parametricEasePeriod;
            TweenData.CalculateCycleDuration(_settings.endDelay, ref d);
            Assert.IsTrue(d.cycleDuration >= 0);

            if (propType == PropType.Quaternion) {
                // Quaternion.identity
                prevVal.x = prevVal.y = prevVal.z = 0f;
                prevVal.w = 1f;
            } else {
                prevVal.Reset();
            }
            d.warnEndValueEqualsCurrent = manager.warnEndValueEqualsCurrent;

            if (!_startFromCurrent) {
                TweenData.CacheDiff(ref d, ref rt);
            }
            rt.target = _target;
        }

        void Revive(ref UnmanagedTweenData d) {
            // managedData.print("revive");
            Assert.IsFalse(d.isAlive);
            d.isAlive = true;
            #if UNITY_EDITOR
            debugDescription = null;
            indexInTweenAnimation = -1;
            #endif
        }

        internal ColdData FindMainSequence() {
            Assert.IsNotNull(sequence);
            var mainSequence = sequence;
            while (true) {
                var previous = mainSequence.prev;
                if (previous == null) {
                    break;
                }
                var parent = previous.sequence;
                if (parent == null) {
                    break;
                }
                mainSequence = parent;
            }
            return mainSequence;
        }

        internal ref float waitTimeout => ref parametricEaseStrength;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    internal struct TweenData : IEquatable<TweenData> {
        [FieldOffset(0)] internal TweenAnimation.ValueWrapper endValueOrDiff;
        /// Holds a reference to tween's target. If the target is UnityEngine.Object, the tween will gracefully stop when the target is destroyed. That is, destroying an object with running tweens is perfectly ok.
        /// Keep in mind: when animating plain C# objects (not derived from UnityEngine.Object), the plugin will hold a strong reference to the object for the entire tween duration.
        ///     If the plain C# target holds a reference to UnityEngine.Object and animates its properties, then it's the user's responsibility to ensure that UnityEngine.Object still exists.
        [FieldOffset(16)] [CanBeNull] internal object target;
        /// Item can be null if the list is accessed from the <see cref="UpdateAndCheckIfRunning"/> via onValueChange() or onComplete()
        [FieldOffset(24)] /*[CanBeNull]*/ internal ColdData cold;



        internal const float negativeElapsedTime = -1000f;

        internal ref ColdData sequence => ref cold.sequence;
        internal ref ShakeData shakeData => ref cold.shakeData;

        #if UNITY_EDITOR
        internal ref string debugDescription => ref cold.debugDescription;
        #endif
        internal ref long id => ref cold.id;

        internal const int iniCyclesDone = -1;

        internal bool UpdateAndCheckIfRunning(float dt, ref UnmanagedTweenData d) {
            Assert.IsFalse(d.isUpdating);
            if (!d.isAlive) {
                return d.isInSequence; // don't release a tween until sequence.ReleaseTweens()
            }
            // check before the 'isPaused' early-out: cancellation should stop paused animations too
            if (d.HasCancellationToken && cold.cancellationToken.IsCancellationRequested) {
                Tween.CancelTweenOrSequence(new Tween(cold));
                return d.isAlive; // returning 'false' for an animation that survived the cancellation would return it to the pool while it's still alive
            }
            if (!d.isPaused) {
                if (d.isUpdatedInJob) {
                    d.isUpdatedInJob = false;
                    bool isDone = d.isDone;
                    if (d.isValueChanged) {
                        bool res = ReportOnValueChange(ref d);
                        if (!res) {
                            return false;
                        }
                    }

                    // a callback can stop the tween emergently, for example, by destroying the target and completing the tween
                    if (isDone && d.isAlive) {
                        // tween
                        if (!d.isPaused) {
                            Kill(ref d);
                        }
                        ReportOnComplete(ref d); // tween
                        return d.isAlive; // a tween paused by its last callback stays alive until it's unpaused
                    }
                    return true;
                }
                return SetElapsedTimeTotal(d.elapsedTimeTotal + dt * d.timeScale, true, ref d);
            }
            if (IsUnityTargetDestroyed()) {
                EmergencyStop(true, ref d);
                return false;
            }
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            return d.isAlive;
        }

        internal bool SetElapsedTimeTotal(float newElapsedTimeTotal, bool earlyExitSequenceIfPaused, ref UnmanagedTweenData d) {
            Assert.IsFalse(d.isUpdatedInJob);
            if (d.isInSequence) {
                Assert.IsTrue(sequence.data.isAlive, id);
                if (d.IsMainSequenceRoot()) {
                    Assert.IsTrue(sequence == cold, id);
                    Assert.IsFalse(d.isUpdating);
                    if (d.HasWait
                        && earlyExitSequenceIfPaused // ignore HasWait when called from progress and elapsedTime
                       ) {
                        if (cold.waitPredicateShouldWait != null) {
                            float dt = newElapsedTimeTotal - d.elapsedTimeTotal;
                            cold.waitTimeout -= dt;
                            if (!cold.waitPredicateShouldWait(cold) || cold.waitTimeout <= 0f) {
                                // Reset main sequence wait data
                                cold.waitPredicateTarget = null;
                                cold.waitPredicate = null;
                                cold.waitPredicateShouldWait = null;
                                cold.data.IsWaitUntil = false;
                            }
                        } else {
                            // Callbacks of children can't seek the sequence while it iterates them, see Tween.SetElapsedTimeTotal()
                            d.isUpdating = true;
                            UpdateSequenceUpToFirstWait(newElapsedTimeTotal, ref d);
                        }
                    } else {
                        d.isUpdating = true;
                        UpdateSequence(newElapsedTimeTotal, false, earlyExitSequenceIfPaused, true, false, ref d);
                    }
                    d.isUpdating = false;
                }
            } else {
                UpdateAndSetElapsedTimeTotal(newElapsedTimeTotal, out int cyclesDiff, false, ref d);
                if (!d.stoppedEmergently && d.isAlive && d.IsDone(cyclesDiff)) { // tween
                    if (!d.isPaused) {
                        Kill(ref d);
                    }
                    ReportOnComplete(ref d); // tween
                }
            }
            return d.isAlive;
        }

        internal void UpdateSequence(float _elapsedTimeTotal, bool isRestart, bool earlyExitSequenceIfPaused, bool allowSkipChildrenUpdate, bool invertEase, ref UnmanagedTweenData d) {
            Assert.IsTrue(d.IsSequenceRoot());

            bool isRootBackwardCycle = (d.clampCyclesDone(d.cyclesDone) % 2 != 0) ^ d.isSequenceInverted;
            float prevElapsedTime = FloatVal(d.startValue, d.easedInterpolationFactor, endValueOrDiff);
            bool invertRootEase = (d.cyclesTotal == 1 && isRootBackwardCycle && d.cycleMode == CycleMode.Rewind) ^ invertEase;
            if (!UpdateAndSetElapsedTimeTotal(_elapsedTimeTotal, out int cyclesDiff, invertRootEase, ref d) && allowSkipChildrenUpdate) { // update sequence root
                return;
            }

            if (d.cycleMode == (CycleMode)_CycleMode.YoyoChildren && isRootBackwardCycle) {
                invertEase = !invertEase;
            }
            bool isRestartToBeginning = isRestart && cyclesDiff < 0;
            Assert.IsTrue(!isRestartToBeginning || d.cyclesDone == 0 || d.cyclesDone == iniCyclesDone);
            if (cyclesDiff != 0 && !isRestartToBeginning) {
                // print($"           sequence cyclesDiff: {cyclesDiff}");
                if (isRestart) {
                    Assert.IsTrue(cyclesDiff > 0);
                    if (d.cyclesDone == d.cyclesTotal) {
                        cyclesDiff = 1;
                    } else {
                        // a restarting parent moves this sequence to its first cycle, not to the end
                        isRestart = false;
                    }
                }
                int cyclesDiffAbs = Mathf.Abs(cyclesDiff);
                int newCyclesDone = d.cyclesDone;
                if (d.IsMainSequenceRoot() && d.IsDone(cyclesDiff)) {
                    // the callbacks invoked by this update can't prevent the sequence from completing. See CompleteContractSequenceIsDeadIfItsLastCallbackPausesIt
                    d.IsCompleting = true;
                }
                d.cyclesDone -= cyclesDiff;
                int cyclesDelta = cyclesDiff > 0 ? 1 : -1;
                const int maxSequenceCyclesPerUpdate = 1000;
                if (cyclesDiffAbs > maxSequenceCyclesPerUpdate) {
                    // Every crossed cycle moves the children to absolute positions and depends only on 'cyclesDone', so skipping the earlier cycles doesn't change the final state
                    LogErrorWithStackTrace($"Sequence crossed {cyclesDiffAbs} cycles in one update, but max {maxSequenceCyclesPerUpdate} cycles are processed per update. The callbacks of the skipped cycles will not be invoked. Sequence: {GetDescription()}");
                    d.cyclesDone += (cyclesDiffAbs - maxSequenceCyclesPerUpdate) * cyclesDelta;
                    cyclesDiffAbs = maxSequenceCyclesPerUpdate;
                }
                float interpolationFactor = cyclesDelta > 0 ? 1f : 0f;
                for (int i = 0; i < cyclesDiffAbs; i++) {
                    Assert.IsTrue(!isRestart || i == 0);
                    if (d.cyclesDone == d.cyclesTotal || d.cyclesDone == iniCyclesDone) {
                        // do nothing when moving backward from the last cycle or forward from the -1 cycle
                        d.cyclesDone += cyclesDelta;
                        continue;
                    }

                    float easedT = CalcEasedT(interpolationFactor, d.cyclesDone, false, ref d);
                    bool isForwardCycle = (easedT > 0.5f) ^ d.isSequenceInverted;

                    // complete the previous cycles by forcing all children tweens to 0f or 1f
                    // print($" (i:{i}) force child to isForwardCycle: {isForwardCycle}");
                    float forceChildrenToPosElapsedTime = isForwardCycle ? float.MaxValue : negativeElapsedTime;
                    foreach (var tween in GetSequenceSelfChildren(isForwardCycle)) {
                        tween.managedData.UpdateSequenceChild(forceChildrenToPosElapsedTime, isRestart, invertEase, ref tween.data);
                        if (isEarlyExitAfterChildUpdate(ref d)) {
                            return;
                        }
                    }

                    d.cyclesDone += cyclesDelta;
                    if (cyclesDelta > 0 && d.cyclesDone == d.cyclesTotal) {
                        // a callback has shortened the sequence with SetRemainingCycles(), so it completes now without crossing the rest of the cycles
                        break;
                    }
                    var sequenceCycleMode = d.cycleMode;
                    if (sequenceCycleMode == CycleMode.Restart && d.cyclesDone != d.cyclesTotal && d.cyclesDone != iniCyclesDone) { // '&& cyclesDone != 0' check is wrong because we should do the restart when moving from 1 to 0 cyclesDone
                        // print($"restartChildren to pos: {!isForwardCycle}");
                        float restartChildrenElapsedTime = !isForwardCycle ? float.MaxValue : negativeElapsedTime;
                        prevElapsedTime = restartChildrenElapsedTime;
                        foreach (var tween in GetSequenceSelfChildren(!isForwardCycle)) {
                            tween.managedData.UpdateSequenceChild(restartChildrenElapsedTime, true, invertEase, ref tween.data);
                            if (isEarlyExitAfterChildUpdate(ref d)) {
                                return;
                            }
                            Assert.IsTrue(isForwardCycle || tween.data.cyclesDone == tween.data.cyclesTotal, id);
                            Assert.IsTrue(!isForwardCycle || tween.data.cyclesDone <= 0, id);
                            Assert.IsTrue(isForwardCycle || tween.data.GetState() == State.After, id);
                            Assert.IsTrue(!isForwardCycle || tween.data.GetState() == State.Before, id);
                        }
                    }
                }
                Assert.IsTrue(newCyclesDone == d.cyclesDone || d.cyclesDone == d.cyclesTotal, id);
                if (d.IsDone(cyclesDiff)) { // sequence
                    if (d.IsMainSequenceRoot()) {
                        if (d.resetOnComplete) {
                            d.IsResetting = true;
                            ResetSequence(sequence);
                            d.IsResetting = false;
                            if (!d.isAlive) {
                                return; // a reset callback stopped the sequence
                            }
                        }
                        if (!d.isPaused) {
                            new Sequence(sequence).ReleaseTweens();
                        } else {
                            // the sequence was paused before it reached the end, so it stays alive until it's unpaused. See UnpauseCompletedSequenceShouldRelease
                            d.IsCompleting = false;
                        }
                        // children are no longer iterated, so the sequence's own OnComplete can seek it
                        d.isUpdating = false;
                    }
                    ReportOnComplete(ref d, false); // sequence
                    return;
                }
            }

            float encompassingElapsedTime = Mathf.Clamp(FloatVal(d.startValue, d.easedInterpolationFactor, endValueOrDiff), 0f, d.cycleDuration);
            bool isForward = encompassingElapsedTime > prevElapsedTime;
            foreach (var t in GetSequenceSelfChildren(isForward)) {
                t.managedData.UpdateSequenceChild(encompassingElapsedTime, isRestart, invertEase, ref t.data);
                if (isEarlyExitAfterChildUpdate(ref d)) {
                    return;
                }
            }

            bool isEarlyExitAfterChildUpdate(ref UnmanagedTweenData d2) {
                if (!d2.isAlive) {
                    return true;
                }
                return earlyExitSequenceIfPaused && d2.isPaused; // access isPaused via root tween to bypass the cantManipulateNested check
            }
        }

        bool UpdateSequenceUpToFirstWait(float newElapsedTimeTotal, ref UnmanagedTweenData d) {
            Assert.IsTrue(d.IsSequenceRoot());
            Assert.AreEqual(1, d.cyclesTotal);
            Assert.AreEqual(Ease.Linear, d.ease);
            Assert.IsFalse(d.isSequenceInverted);

            float encompassingElapsedTime = newElapsedTimeTotal - d.waitDelay;
            foreach (var child in GetSequenceSelfChildren(true)) {
                ref var childData = ref child.data;
                ref TweenData childManagedData = ref child.managedData;
                if (childData.IsSequenceRoot()) {
                    if (childData.HasWait) {
                        if (childManagedData.UpdateSequenceUpToFirstWait(encompassingElapsedTime, ref childData)) {
                            return true;
                        }
                    } else {
                        // A sibling's wait doesn't restrict this sequence's cycles, easing, or direction.
                        childManagedData.UpdateSequenceChild(encompassingElapsedTime, false, false, ref childData);
                    }
                } else {
                    // update tweens and execute OnComplete up to the first suspending HasWait
                    if (childData.elapsedTimeTotal < encompassingElapsedTime // update tweens only forward
                        && childManagedData.UpdateAndSetElapsedTimeTotal(encompassingElapsedTime, out int cyclesDiff, false, ref childData)
                       ) {
                        if (!d.isAlive || d.isPaused) {
                            return false;
                        }
                        if (!childData.stoppedEmergently && childData.isAlive) {
                            bool shouldWait = childData.HasWait && child.waitPredicateShouldWait(child);
                            if (!d.isAlive) {
                                // The predicate can stop or complete the sequence and unlink all its children.
                                return true;
                            }
                            if (shouldWait) {
                                // Copy wait data to the main sequence
                                var mainSequence = child.FindMainSequence();
                                mainSequence.waitPredicateTarget = child.waitPredicateTarget;
                                mainSequence.waitPredicate = child.waitPredicate;
                                mainSequence.waitPredicateShouldWait = child.waitPredicateShouldWait;
                                mainSequence.waitTimeout = child.waitTimeout;
                                mainSequence.data.IsWaitUntil = childData.IsWaitUntil;
                                return true;
                            } else if (cyclesDiff != 0
                                       && childData.IsDone(cyclesDiff) // sequence child
                                       && childData.GetCallbackDirection() != TweenAnimation.CallbackDirection.BackwardOnly
                                      ) {
                                childManagedData.ReportOnComplete(ref childData); // sequence child
                            }
                        }
                    }
                }
                if (!d.isAlive) {
                    // a callback has stopped or completed the sequence, so its children can't be enumerated anymore
                    return true;
                }
            }

            if (UpdateAndSetElapsedTimeTotal(newElapsedTimeTotal, out int seqCyclesDiff, false, ref d)) { // update sequence root if no children exited early because of ChainWait
                if (d.IsDone(seqCyclesDiff)) { // sequence
                    if (d.IsMainSequenceRoot()) {
                        if (d.resetOnComplete) {
                            d.IsResetting = true;
                            ResetSequence(sequence);
                            d.IsResetting = false;
                            if (!d.isAlive) {
                                return false;
                            }
                        }
                        if (!d.isPaused) {
                            new Sequence(sequence).ReleaseTweens();
                        }
                        d.isUpdating = false;
                    }
                    ReportOnComplete(ref d, false); // sequence
                }
            }
            return false;
        }

        internal static void ResetSequence(ColdData seq, bool toEnd = false) {
            ref var seqData = ref seq.data;
            Assert.IsTrue(seqData.isAlive);

            bool isResetToEnd = seq.data.isSequenceInverted ^ toEnd;
            foreach (var child in new Sequence(seq).GetSelfChildren(isForward: isResetToEnd)) {
                ref var childData = ref child.data;

                int tweenCyclesTotal = childData.cyclesTotal;
                Assert.AreNotEqual(-1, tweenCyclesTotal); // nested animations can't be infinite
                childData.cyclesDone = isResetToEnd ? tweenCyclesTotal : iniCyclesDone; // setting the cyclesDone to tweenCyclesTotal prevents callback from firing because isDone requires 'cyclesDiff > 0'

                // Also update nested sequence root
                childData.SetState(State.None); // guarantees propagation of state change
                float elapsedTimeTotal = isResetToEnd ? float.MaxValue : negativeElapsedTime;
                bool isValueChanged = child.managedData.UpdateAndSetElapsedTimeTotal(elapsedTimeTotal, out _, false, ref childData);
                Assert.IsTrue(isValueChanged);
                if (!seqData.isAlive) {
                    return;
                }
                Assert.AreEqual(isResetToEnd ? State.After : State.Before, childData.GetState());

                if (childData.IsSequenceRoot()) {
                    ResetSequence(child, isResetToEnd);
                    if (!seqData.isAlive) {
                        return; // a nested reset callback can stop the entire sequence
                    }
                }
            }
        }

        internal static void InvertSequence(Sequence seq) {
            var root = seq.root.tween;
            ref var d = ref root.data;
            (d.startValue, root.managedData.endValueOrDiff) = (root.managedData.endValueOrDiff, d.startValue); // sequence uses startValue and endValue as bounds for time calculation. Swapping them makes the sequence go in the opposite direction.
            d.isSequenceInverted = true;
            CacheDiff(ref d, ref root.managedData);
        }

        Sequence.SequenceDirectEnumerator GetSequenceSelfChildren(bool isForward) {
            Assert.IsTrue(sequence.data.isAlive, id);
            return new Sequence(sequence).GetSelfChildren(isForward);
        }

        void UpdateSequenceChild(float encompassingElapsedTime, bool isRestart, bool invertEase, ref UnmanagedTweenData d) {
            if (d.IsSequenceRoot()) {
                UpdateSequence(encompassingElapsedTime, isRestart, true, true, invertEase, ref d);
            } else {
                UpdateAndSetElapsedTimeTotal(encompassingElapsedTime, out int cyclesDiff, invertEase, ref d);
                if (!d.stoppedEmergently && d.isAlive && cyclesDiff != 0) {
                    var callbackDirection = d.GetCallbackDirection();
                    if (d.tweenType == TweenType.Callback && (int)callbackDirection != 0) {
                        if (!isRestart) {
                            if (callbackDirection != TweenAnimation.CallbackDirection.BackwardOnly && cyclesDiff > 0 && d.cyclesDone == d.cyclesTotal) {
                                cold.onComplete?.Invoke(this);
                            }
                            if (callbackDirection != TweenAnimation.CallbackDirection.ForwardOnly && cyclesDiff < 0 && d.cyclesDone == iniCyclesDone) {
                                cold.onComplete?.Invoke(this);
                            }
                        }
                    } else if (d.IsDone(cyclesDiff)) { // sequence child
                        ReportOnComplete(ref d); // sequence child
                    }
                }
            }
        }

        bool UpdateAndSetElapsedTimeTotal(float newElapsedTimeTotal, out int cyclesDiff, bool invertEase, ref UnmanagedTweenData d) {
            int oldCyclesDone = d.cyclesDone;
            float t = d.UpdateData(newElapsedTimeTotal);
            cyclesDiff = d.cyclesDone - oldCyclesDone;
            if (d.isValueChanged) {
                d.easedInterpolationFactor = CalcEasedT(t, d.cyclesDone, invertEase, ref d);

                TryCacheDiff(ref d);
                ReportOnValueChange(ref d);
                return true;
            }
            return false;
        }


        [System.Diagnostics.Conditional("PRIME_TWEEN_SAFETY_CHECKS")]
        internal void print(string msg) {
            // Debug.Log($"[{Time.frameCount}] id:{id}  {msg}  tweenType:{cold.data.tweenType} {GetDescription()}", target as UnityEngine.Object);
        }

        internal void Reset(ref UnmanagedTweenData d) {
            Assert.IsFalse(d.isUpdating);
            Assert.IsFalse(d.isAlive);
            Assert.IsNull(cold.sequence);
            Assert.IsNull(cold.prev);
            Assert.IsNull(cold.next);
            Assert.IsNull(cold.prevSibling);
            Assert.IsNull(cold.nextSibling);
            Assert.IsFalse(d.isInSequence);
            if (cold.shakeData.isAlive) {
                cold.shakeData.Reset(target, d.tweenType);
            }
            #if UNITY_EDITOR
            debugDescription = null;
            #endif
            target = null;
            cold.customEase = null;
            cold.customOnValueChange = null;
            cold.onValueChange = null;
            cold.onComplete = null;
            cold.onCompleteCallback = null;
            cold.onCompleteTarget = null;
            cold.waitPredicateTarget = null;
            cold.waitPredicate = null;
            cold.waitPredicateShouldWait = null;
            cold.cancellationToken = CancellationToken.None;
            cold.cancellationMode = default;
            clearOnUpdate(ref d);

            Assert.IsTrue(cold.hasData);
            Assert.AreEqual(id, d.id);
            d = default; // reset the data before returning tween to pool
            id = -1;
        }

        /// <param name="warnIfTargetDestroyed">https://github.com/KyryloKuzyk/PrimeTween/discussions/4</param>
        internal void OnComplete([CanBeNull] Action _onComplete, bool? warnIfTargetDestroyed) {
            if (_onComplete == null) {
                return;
            }
            if (!ValidateOnCompleteAssignment()) {
                return;
            }
            cold.data.warnIgnoredOnCompleteIfTargetDestroyed = warnIfTargetDestroyed ?? PrimeTweenManager.Instance.warnIfTargetDestroyed;
            cold.onCompleteCallback = _onComplete;
            cold.onComplete = tween => {
                var callback = tween.cold.onCompleteCallback as Action;
                Assert.IsNotNull(callback);
                try {
                    callback();
                } catch (Exception e) {
                    tween.HandleOnCompleteException(e);
                }
            };
        }

        internal void OnComplete<T>([CanBeNull] T _target, [CanBeNull] Action<T> _onComplete, bool? warnIfTargetDestroyed) where T : class {
            if (_target == null || IsDestroyedUnityObject(_target)) {
                Debug.LogError($"{nameof(_target)} is null or has been destroyed. {Constants.onCompleteCallbackIgnored}");
                return;
            }
            if (_onComplete == null) {
                return;
            }
            if (!ValidateOnCompleteAssignment()) {
                return;
            }
            cold.data.warnIgnoredOnCompleteIfTargetDestroyed = warnIfTargetDestroyed ?? PrimeTweenManager.Instance.warnIfTargetDestroyed;
            cold.onCompleteTarget = _target;
            cold.onCompleteCallback = _onComplete;
            cold.onComplete = tween => {
                var callback = tween.cold.onCompleteCallback as Action<T>;
                Assert.IsNotNull(callback);
                var _onCompleteTarget = tween.cold.onCompleteTarget as T;
                if (IsDestroyedUnityObject(_onCompleteTarget)) {
                    tween.WarnOnCompleteIgnored(true);
                    return;
                }
                try {
                    callback(_onCompleteTarget);
                } catch (Exception e) {
                    tween.HandleOnCompleteException(e);
                }
            };
        }

        // Fields that a Delay tween leaves unused, laid out back to back so an arbitrary unmanaged payload can be split across them.
        // shakeData.frequency is excluded because a non-zero value marks the tween as a live shake.
        // All fields are 4-byte aligned, so the struct has no tail padding that would silently swallow payload bytes.
        // Payload bytes never pass through a float-typed value: a typed float load/store may widen the value (Mono without float32 mode, x87),
        // which quiets signaling NaNs, corrupting arbitrary payload bits.
        // So float destinations are held as int structs here and copied with StoreBits/LoadBits.
        struct DelayPayloadLayout {
            internal Int4 startValue, endValueOrDiff, prevVal;
            internal Int2 longParam;
            internal int parametricEaseStrength, parametricEasePeriod;
            internal int shakeT;
            internal Int3 shakeFrom, shakeTo;
            internal int shakeSymmetryFactor;
            internal int shakeFalloffEaseInt;
            internal Int3 shakeStrengthPerAxis;
            internal int shakePrevInterpolationFactor;
            internal int shakePrevCyclesDone;
        }

        struct Int2 { internal int x, y; }
        struct Int3 { internal int x, y, z; }
        struct Int4 { internal int x, y, z, w; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe void StoreBits<TField, TBits>(ref TField field, TBits bits) where TField : unmanaged where TBits : unmanaged {
            Assert.AreEqual(sizeof(TField), sizeof(TBits));
            fixed (TField* p = &field) {
                *(TBits*)p = bits;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe void LoadBits<TField, TBits>(ref TField field, out TBits bits) where TField : unmanaged where TBits : unmanaged {
            Assert.AreEqual(sizeof(TField), sizeof(TBits));
            fixed (TField* p = &field) {
                bits = *(TBits*)p;
            }
        }

        internal static bool CheckPayloadSize<TPayload>() where TPayload : unmanaged {
            int maxPayloadSize = UnsafeUtility.SizeOf<DelayPayloadLayout>();
            if (UnsafeUtility.SizeOf<TPayload>() > maxPayloadSize) {
                Debug.LogError($"Payload '{typeof(TPayload)}' is {UnsafeUtility.SizeOf<TPayload>()} bytes, but the max supported size is {maxPayloadSize} bytes.");
                return false;
            }
            return true;
        }

        internal unsafe void OnComplete<T, TPayload>([CanBeNull] T _target, TPayload payload, [CanBeNull] Action<T, TPayload> _onComplete, bool? warnIfTargetDestroyed) where T : class where TPayload : unmanaged {
            if (_target == null || IsDestroyedUnityObject(_target)) {
                Debug.LogError($"{nameof(_target)} is null or has been destroyed. {Constants.onCompleteCallbackIgnored}");
                return;
            }
            if (_onComplete == null) {
                return;
            }
            if (!ValidateOnCompleteAssignment()) {
                return;
            }
            ref var d = ref cold.data;
            ref var shake = ref cold.shakeData;
            Assert.IsTrue(d.tweenType == TweenType.Delay || d.tweenType == TweenType.Callback);
            Assert.IsTrue(UnsafeUtility.SizeOf<TPayload>() <= UnsafeUtility.SizeOf<DelayPayloadLayout>());
            Assert.IsFalse(shake.isAlive);
            d.warnIgnoredOnCompleteIfTargetDestroyed = warnIfTargetDestroyed ?? PrimeTweenManager.Instance.warnIfTargetDestroyed;

            var l = new DelayPayloadLayout();
            UnsafeUtility.MemCpy(&l, &payload, UnsafeUtility.SizeOf<TPayload>());
            StoreBits(ref d.startValue.vector4, l.startValue);
            StoreBits(ref endValueOrDiff.vector4, l.endValueOrDiff);
            StoreBits(ref cold.prevVal.vector4, l.prevVal);
            StoreBits(ref cold.longParam, l.longParam);
            StoreBits(ref cold.parametricEaseStrength, l.parametricEaseStrength);
            StoreBits(ref cold.parametricEasePeriod, l.parametricEasePeriod);
            StoreBits(ref shake.t, l.shakeT);
            StoreBits(ref shake.from, l.shakeFrom);
            StoreBits(ref shake.to, l.shakeTo);
            StoreBits(ref shake.symmetryFactor, l.shakeSymmetryFactor);
            shake.falloffEaseInt = l.shakeFalloffEaseInt;
            StoreBits(ref shake.strengthPerAxis, l.shakeStrengthPerAxis);
            StoreBits(ref shake.prevInterpolationFactor, l.shakePrevInterpolationFactor);
            shake.prevCyclesDone = l.shakePrevCyclesDone;

            cold.onCompleteTarget = _target;
            cold.onCompleteCallback = _onComplete;
            cold.onComplete = tween => {
                var callback = tween.cold.onCompleteCallback as Action<T, TPayload>;
                Assert.IsNotNull(callback);
                var _onCompleteTarget = tween.cold.onCompleteTarget as T;
                if (IsDestroyedUnityObject(_onCompleteTarget)) {
                    tween.WarnOnCompleteIgnored(true);
                    return;
                }
                var cold2 = tween.cold;
                ref var shake2 = ref cold2.shakeData;
                var l2 = new DelayPayloadLayout();
                LoadBits(ref cold2.data.startValue.vector4, out l2.startValue);
                LoadBits(ref tween.endValueOrDiff.vector4, out l2.endValueOrDiff);
                LoadBits(ref cold2.prevVal.vector4, out l2.prevVal);
                LoadBits(ref cold2.longParam, out l2.longParam);
                LoadBits(ref cold2.parametricEaseStrength, out l2.parametricEaseStrength);
                LoadBits(ref cold2.parametricEasePeriod, out l2.parametricEasePeriod);
                LoadBits(ref shake2.t, out l2.shakeT);
                LoadBits(ref shake2.from, out l2.shakeFrom);
                LoadBits(ref shake2.to, out l2.shakeTo);
                LoadBits(ref shake2.symmetryFactor, out l2.shakeSymmetryFactor);
                l2.shakeFalloffEaseInt = shake2.falloffEaseInt;
                LoadBits(ref shake2.strengthPerAxis, out l2.shakeStrengthPerAxis);
                LoadBits(ref shake2.prevInterpolationFactor, out l2.shakePrevInterpolationFactor);
                l2.shakePrevCyclesDone = shake2.prevCyclesDone;
                TPayload _payload = default;
                UnsafeUtility.MemCpy(&_payload, &l2, UnsafeUtility.SizeOf<TPayload>());
                try {
                    callback(_onCompleteTarget, _payload);
                } catch (Exception e) {
                    tween.HandleOnCompleteException(e);
                }
            };
        }

        void HandleOnCompleteException(Exception e) {
            // Design decision: if a tween is inside a Sequence and the user's tween.OnComplete() throws an exception, the Sequence should continue
            LogErrorWithStackTrace($"Tween's onComplete callback raised exception, tween: {GetDescription()}");
            Debug.LogException(e, target as UnityEngine.Object);
        }

        internal void LogErrorWithStackTrace(string msg) => Assert.LogErrorWithStackTrace(msg, cold.id, target);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsDestroyedUnityObject<T>(T obj) where T: class => obj is UnityEngine.Object unityObject && unityObject == null;

        bool ValidateOnCompleteAssignment() {
            if (cold.onCompleteTarget != null || cold.onCompleteCallback != null || cold.onComplete != null) {
                LogErrorWithStackTrace(Constants.onCompleteDuplicateError);
                return false;
            }
            return true;
        }

        internal bool ReportOnValueChange(ref UnmanagedTweenData d) {
            // print($"ReportOnValueChange {d.easedInterpolationFactor}, {Vector4Val(d.startValue, d.easedInterpolationFactor, endValueOrDiff)}, {d.startValue}, {endValueOrDiff}");
            Assert.IsFalse(d.startFromCurrent);
            bool hasOnUpdate = d.hasOnUpdate;
            try {
                // The value setter can fail even if the Unity target is not destroyed. For example, ScrollRect.SetNormalizedPosition can throw null ref if m_Content is not populated: https://github.com/needle-mirror/com.unity.ugui/blob/a601a2bf30161c47959231b627a8c40f64d69a68/Runtime/UI/Core/ScrollRect.cs#L1031.
                // Also, this try-catch catches exceptions in user-provided setter callbacks in Custom tweens.
                if (!Utils.SetAnimatedValue(ref this, ref d)) {
                    return false;
                }
                if (hasOnUpdate && !d.stoppedEmergently && d.isAlive) {
                    d.isUpdating = true;
                    cold.onUpdate?.Invoke(this);
                    d.isUpdating = false;
                }
            } catch (Exception e) {
                Debug.LogException(e, target as UnityEngine.Object);
                Assert.LogWarningWithStackTrace($"Tween was stopped because of exception in '{nameof(ColdData.onValueChange)}', tween: {GetDescription()}\n", id, target);
                EmergencyStop(false, ref d);
                return false;
            }
            return true;
        }

        void TryCacheDiff(ref UnmanagedTweenData d) {
            if (d.startFromCurrent) {
                d.startFromCurrent = false;
                if (!ShakeData.TryTakeStartValueFromOtherShake(ref this, ref d)) {
                    if (!IsUnityTargetDestroyed()) {
                        d.startValue = Utils.GetAnimatedValue(target, d.tweenType, cold.intParam); // p2 todo getter can potentially throw even if Unity target is not destroyed. For example, this is the case for ScrollRect.SetNormalizedPosition
                    }
                }
                if (d.warnEndValueEqualsCurrent && !shakeData.isAlive && EndValueEqualsCurrent(ref d)) {
                    Assert.LogWarningWithStackTrace($"Tween's 'endValue' equals to the current animated value: {d.startValue.vector4}, tween: {GetDescription()}.\n" +
                                      $"{Constants.buildWarningCanBeDisabledMessage(nameof(PrimeTweenConfig.warnEndValueEqualsCurrent))}\n", id, target);
                }
                CacheDiff(ref d, ref this);
            }
        }

        bool EndValueEqualsCurrent(ref UnmanagedTweenData d) {
            // For quaternions, q and -q represent the same rotation, so compare by angle instead of by raw components.
            return d.propType == PropType.Quaternion
                ? TweenAnimation.ValueWrapper.QuaternionAngle(d.startValue, endValueOrDiff) == 0f
                : d.startValue.vector4 == endValueOrDiff.vector4;
        }

        void ReportOnComplete(ref UnmanagedTweenData d, bool canResetOnComplete = true) {
            // print($"ReportOnComplete() {d.easedInterpolationFactor}");
            // a startFromCurrent tween can return to its start before its first value change, so it never reads the start value
            Assert.IsTrue(!d.startFromCurrent || d.cyclesDone == iniCyclesDone);
            // An animation completes at its end or at its start. The direction can't be checked with the timeScale: the job computes the completion before the main loop invokes callbacks, and a callback of an earlier animation can change the timeScale
            Assert.IsTrue(d.cyclesDone == d.cyclesTotal || d.cyclesDone == iniCyclesDone);
            if (d.cyclesDone == d.cyclesTotal) {
                if (canResetOnComplete && d.resetOnComplete && !d.isInSequence) {
                    // reset Tween
                    UpdateAndSetElapsedTimeTotal(negativeElapsedTime, out _, false, ref d);
                }
                cold.onComplete?.Invoke(this);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool IsUnityTargetDestroyed() {
            // must use target here instead of unityTarget
            // unityTarget has the SerializeField attribute, so if ReferenceEquals(unityTarget, null), then Unity will populate the field with non-null UnityEngine.Object when a new scene is loaded in the Editor
            // https://github.com/KyryloKuzyk/PrimeTween/issues/32
            return IsDestroyedUnityObject(target);
        }

        internal bool HasOnComplete => cold.onComplete != null;

        [NotNull]
        internal string GetDescription() {
            var _sb = PrimeTweenManager.Instance._stringBuilder;
            _sb.Clear();
            var d = cold.data;
            if (!d.isAlive) {
                _sb.Append(" - ");
            }

            // _sb.Append(id).Append(" ");

            if (sequence != null) {
                var currentSequence = sequence;
                while (true) {
                    if (id != currentSequence.id) {
                        _sb.Append(" · "); // p2 todo animations are ordered by creation time, not by sequence nesting depth
                    }
                    var _prev = currentSequence.prev;
                    if (_prev == null) {
                        break;
                    }
                    var parent = _prev.sequence;
                    if (parent == null) {
                        break;
                    }
                    currentSequence = parent;
                }
            }

            switch (d.tweenType) {
                case TweenType.Delay:
                    _sb.Append("Delay");
                    break;
                case TweenType.Callback:
                    _sb.Append("Callback");
                    break;
                case TweenType.MainSequence:
                case TweenType.NestedSequence:
                    _sb.Append("Sequence ");
                    break;
                default:
                    _sb.Append(d.tweenType);
                    break;
            }

            const string separator = "  /  ";
            if (target != PrimeTweenManager.dummyTarget) {
                _sb.Append(separator);
                string targetName =
                    target is UnityEngine.Object unityObject && unityObject != null ? unityObject.name :
                    cold.customOnValueChange is UnityEngine.Object tweenAnimationContext && tweenAnimationContext != null ? tweenAnimationContext.name :
                    target?.GetType().Name;
                _sb.Append(targetName);
            }
            if (d.tweenType != TweenType.Callback) {
                _sb.Append(separator).AppendFormat("{0:0.0#}s", d.animationDuration);
            }
            return _sb.ToString();
        }

        internal static void CalculateCycleDuration(float endDelay, ref UnmanagedTweenData d) {
            Assert.IsTrue(endDelay >= 0f);
            d.cycleDuration = Mathf.Min(d.startDelay + d.animationDuration + endDelay, float.MaxValue); // the sum of finite values can overflow to infinity
        }

        internal static double DoubleVal(TweenAnimation.ValueWrapper startValue, float t, TweenAnimation.ValueWrapper delta) => startValue.DoubleVal + delta.DoubleVal * t;
        internal static float FloatVal(TweenAnimation.ValueWrapper startValue, float t, TweenAnimation.ValueWrapper delta) => startValue.single + delta.single * t;
        internal static Color ColorVal(TweenAnimation.ValueWrapper startValue, float t, TweenAnimation.ValueWrapper delta) => startValue.color + delta.color * t;
        internal static Vector2 Vector2Val(TweenAnimation.ValueWrapper startValue, float t, TweenAnimation.ValueWrapper delta) => startValue.vector2 + delta.vector2 * t;
        internal static Vector3 Vector3Val(TweenAnimation.ValueWrapper startValue, float t, TweenAnimation.ValueWrapper delta) => startValue.vector3 + delta.vector3 * t;
        internal static Vector4 Vector4Val(TweenAnimation.ValueWrapper startValue, float t, TweenAnimation.ValueWrapper delta) => startValue.vector4 + delta.vector4 * t;
        internal static Rect RectVal(TweenAnimation.ValueWrapper startValue, float t, TweenAnimation.ValueWrapper delta) => new Rect(
            startValue.x + delta.x * t,
            startValue.y + delta.y * t,
            startValue.z + delta.z * t,
            startValue.w + delta.w * t);
        internal static Quaternion QuaternionVal(TweenAnimation.ValueWrapper startValue, float t, TweenAnimation.ValueWrapper delta) => Quaternion.SlerpUnclamped(startValue.quaternion, delta.quaternion, t);

        float CalcEasedT(float t, int cyclesDone_, bool invertEase, ref UnmanagedTweenData d) {
            if (invertEase) {
                float oneMinusT = CalcEasedTInternal(1f - t, cyclesDone_, ref d);
                return d.IsCustomEaseSameStartEndValues ? oneMinusT : 1f - oneMinusT;
            } else {
                return CalcEasedTInternal(t, cyclesDone_, ref d);
            }
        }

        internal float CalcEasedTInternal(float t, int cyclesDone_, ref UnmanagedTweenData d) {
            switch (d.cycleMode) {
                case CycleMode.Restart:
                    return Evaluate(t, ref d);
                case CycleMode.Incremental:
                    return Evaluate(t, ref d) + d.clampCyclesDone(cyclesDone_);
                case (CycleMode)_CycleMode.YoyoChildren:
                case CycleMode.Yoyo:
                    return d.IsForwardCycle(cyclesDone_) ? Evaluate(t, ref d) : 1 - Evaluate(t, ref d);
                case CycleMode.Rewind:
                    return d.IsForwardCycle(cyclesDone_) ? Evaluate(t, ref d) : Evaluate(1 - t, ref d);
                default:
                    throw new Exception();
            }
        }

        float Evaluate(float t, ref UnmanagedTweenData d) {
            if (d.ease == Ease.Custom) {
                if (cold.parametricEase != ParametricEase.None) {
                    return Easing.EvaluateParametricEase(t, ref this, ref d);
                }
                return cold.customEase.Evaluate(t);
            }
            return StandardEasing.Evaluate(t, d.ease);
        }

        internal static void CacheDiff(ref UnmanagedTweenData d, ref TweenData rt) {
            // print($"CacheDiff, startValue: {d.startValue}, endValue: {endValue}");
            Assert.IsFalse(d.startFromCurrent);
            var propType = d.propType;
            Assert.AreNotEqual(PropType.None, propType);
            switch (propType) {
                case PropType.Quaternion:
                    d.startValue.QuaternionNormalize();
                    rt.endValueOrDiff.QuaternionNormalize();
                    break;
                case PropType.Double:
                    rt.endValueOrDiff.DoubleVal -= d.startValue.DoubleVal;
                    rt.endValueOrDiff.z = 0f;
                    rt.endValueOrDiff.w = 0f;
                    break;
                default:
                    rt.endValueOrDiff.vector4 -= d.startValue.vector4;
                    break;
            }
            // rt.print($"CacheDiff, diff: {rt.endValueOrDiff}");
        }

        internal void ForceComplete(ref UnmanagedTweenData d) {
            Assert.IsNull(sequence);
            Kill(ref d); // protects from recursive call
            int cyclesTotal_;
            if (d.timeScale > 0f) {
                cyclesTotal_ = d.cyclesTotal;
            if (cyclesTotal_ == -1) {
                // same as SetRemainingCycles(1)
                cyclesTotal_ = d.getCyclesDone() + 1;
                d.cyclesTotal = cyclesTotal_;
            }
            } else {
                cyclesTotal_ = iniCyclesDone;
            }
            d.cyclesDone = cyclesTotal_;
            d.easedInterpolationFactor = CalcEasedT(1f, cyclesTotal_, false, ref d);

            TryCacheDiff(ref d);
            ReportOnValueChange(ref d);

            if (d.stoppedEmergently) {
                return;
            }
            ReportOnComplete(ref d);
            Assert.IsFalse(d.isAlive);
        }

        internal void WarnOnCompleteIgnored(bool isTargetDestroyed) {
            if (HasOnComplete && cold.data.warnIgnoredOnCompleteIfTargetDestroyed) {
                cold.onComplete = null;
                var msg = $"{Constants.onCompleteCallbackIgnored} Tween: {GetDescription()}.\n";
                if (isTargetDestroyed) {
                    msg += "\nIf you use tween.OnComplete(), Tween.Delay(), or sequence.ChainDelay() only for cosmetic purposes, you can turn off this error by passing 'warnIfTargetDestroyed: false' parameter to the method.\n" +
                           "More info: https://github.com/KyryloKuzyk/PrimeTween/discussions/4\n\n" +
                           "Not recommended: it's also possible to disable this setting globally with '" + nameof(PrimeTweenConfig) + "." + nameof(PrimeTweenConfig.warnIfTargetDestroyed) + " = false', but doing so will silent potential logic errors and might introduce subtle hard-to-debug issues to your project.\n";
                }
                Assert.LogErrorWithStackTrace(msg, id, target ?? cold.onCompleteTarget);
            }
        }

        internal void EmergencyStop(bool isTargetDestroyed, ref UnmanagedTweenData d) {
            if (sequence != null) {
                ColdData mainSequence = cold.FindMainSequence();
                Assert.IsTrue(mainSequence.data.isAlive);
                Assert.IsTrue(mainSequence.data.IsMainSequenceRoot());
                new Sequence(mainSequence).EmergencyStop();
            } else if (d.isAlive) {
                // EmergencyStop() can be called after ForceComplete() and a caught exception in Tween.Custom()
                Kill(ref d);
            }
            d.stoppedEmergently = true;
            WarnOnCompleteIgnored(isTargetDestroyed);
            Assert.IsFalse(d.isAlive);
            Assert.IsNull(sequence);
        }

        internal void Kill(ref UnmanagedTweenData d) {
            // print($"kill {GetDescription()}");
            Assert.IsTrue(d.isAlive);
            d.isAlive = false;
            #if UNITY_EDITOR
            debugDescription = null;
            #endif
        }

        internal void SetOnUpdate<T>(T _target, [NotNull] Action<T, Tween> _onUpdate) where T : class {
            if (cold.onUpdate != null) {
                LogErrorWithStackTrace(Constants.onUpdateDuplicateError);
                return;
            }
            if (_onUpdate == null) {
                LogErrorWithStackTrace("'onUpdate' is null.");
                return;
            }
            cold.onUpdateTarget = _target;
            cold.onUpdateCallback = _onUpdate;
            cold.onUpdate = reusableTween => reusableTween.invokeOnUpdate<T>();
            cold.data.hasOnUpdate = true;
        }

        void invokeOnUpdate<T>() where T : class {
            var callback = cold.onUpdateCallback as Action<T, Tween>;
            Assert.IsNotNull(callback);
            var _onUpdateTarget = cold.onUpdateTarget as T;
            if (IsDestroyedUnityObject(_onUpdateTarget)) {
                LogErrorWithStackTrace($"OnUpdate() will not be called again because OnUpdate()'s target has been destroyed, tween: {GetDescription()}");
                clearOnUpdate(ref cold.data);
                return;
            }
            try {
                callback(_onUpdateTarget, new Tween(cold));
            } catch (Exception e) {
                LogErrorWithStackTrace($"OnUpdate() will not be called again because it thrown exception, tween: {GetDescription()}");
                Debug.LogException(e, _onUpdateTarget as UnityEngine.Object);
                clearOnUpdate(ref cold.data);
            }
        }

        void clearOnUpdate(ref UnmanagedTweenData d) {
            cold.onUpdateTarget = null;
            cold.onUpdateCallback = null;
            cold.onUpdate = null;
            d.hasOnUpdate = false;
        }

        public override string ToString() {
            return GetDescription();
        }

        public bool Equals(TweenData other) {
            return Equals(cold, other.cold);
        }

        public override bool Equals(object obj) {
            return obj is TweenData other && Equals(other);
        }

        public override int GetHashCode() {
            return (cold != null ? cold.GetHashCode() : 0);
        }
    }

    [Flags]
    internal enum Flags {
        Additive = 1 << 0,
        ShakeSign = 1 << 1,
        ShakePunch = 1 << 2,
        WarnEndValueEqualsCurrent = 1 << 3,
        WarnIgnoredOnCompleteIfTargetDestroyed = 1 << 4,
        ResetOnComplete = 1 << 5,
        IsUpdating = 1 << 6,
        StoppedEmergently = 1 << 7,
        IsAlive = 1 << 8,
        StateLow = 1 << 9,
        StateHigh = 1 << 10,
        IsResetting = 1 << 11, // Complete() called by a callback of the reset is ignored: the interrupted completion finishes the sequence. See ResetSequence()
        StartFromCurrent = 1 << 12,
        IsDone = 1 << 13,
        IsValueChanged = 1 << 14,
        UseUnscaledTime = 1 << 15,
        IsInSequence = 1 << 16,
        IsPaused = 1 << 17,
        IsUpdatedInJob = 1 << 18,
        HasOnUpdate = 1 << 19,
        HasWait = 1 << 20,
        IsSequenceInverted = 1 << 21,
        IsCustomEaseSameStartEndValues = 1 << 22,
        CallbackDirectionLow = 1 << 23, // TweenAnimation.CallbackDirection packed into two bits, 0 means disabled
        CallbackDirectionHigh = 1 << 24,
        // Unused = 1 << 25,
        ModificationIsNotAllowed = 1 << 26,
        HasCancellationToken = 1 << 27,
        IsWaitUntil = 1 << 28,
        IsCompleting = 1 << 29,
        JobStateLow = 1 << 30,
        JobStateHigh = 1 << 31,
    }

    internal enum State : byte {
        None,
        Before,
        Running,
        After,
    }

    internal enum _CycleMode : byte {
        Restart = 0,
        Yoyo = 1,
        Incremental = 2,
        Rewind = 3,
        YoyoChildren = 4
    }

    internal delegate void OnValueChangeDelegate(ref TweenData rt, ref UnmanagedTweenData d);
}
