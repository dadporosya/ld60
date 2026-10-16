#if PRIME_TWEEN_SAFETY_CHECKS && UNITY_ASSERTIONS
#define SAFETY_CHECKS
#endif
#if PRIME_TWEEN_INSPECTOR_DEBUGGING && UNITY_EDITOR
#define ENABLE_SERIALIZATION
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using UnityEngine;
using TweenType = PrimeTween.TweenAnimation.TweenType;

namespace PrimeTween {
    /// <summary>An ordered group of tweens and callbacks. Tweens in a sequence can run in parallel to one another with <see cref="Group"/> and sequentially with <see cref="Chain"/>.<br/>
    /// To make tweens in a Sequence overlap each other, use <see cref="TweenSettings.startDelay"/> and <see cref="TweenSettings.endDelay"/>.</summary>
    /// <example><code>
    /// Sequence.Create()
    ///     .Group(Tween.PositionX(transform, endValue: 10f, duration: 1.5f))
    ///     .Group(Tween.Scale(transform, endValue: 2f, duration: 0.5f)) // position and localScale tweens will run in parallel because they are 'grouped'
    ///     .Chain(Tween.Rotation(transform, endValue: new Vector3(0f, 0f, 45f), duration: 1f)) // rotation tween is 'chained' so it will start when both previous tweens are finished (after 1.5 seconds)
    ///     .ChainCallback(() =&gt; Debug.Log("Sequence completed"));
    /// </code></example>
    #if ENABLE_SERIALIZATION
    [Serializable]
    #endif
    public
        #if !ENABLE_SERIALIZATION && UNITY_2020_3_OR_NEWER
        readonly // duration setter produces error in Unity <= 2019.4.40: error CS1604: Cannot assign to 'this' because it is read-only
        #endif
        partial struct Sequence : IEquatable<Sequence> {
        internal
            #if !ENABLE_SERIALIZATION && UNITY_2020_3_OR_NEWER
            readonly
            #endif
            Tween root;

        const int emptySequenceTag = -43;
        internal bool IsCreated => root.IsCreated;
        long id => root.id;

        /// Sequence is 'alive' when any of its nested animations is 'alive'.
        public bool isAlive => root.isAlive;

        /// Elapsed time of the current cycle.
        public float elapsedTime {
            get => root.elapsedTime;
            set => root.elapsedTime = value;
        }

        /// The total number of cycles. Returns -1 to indicate an infinite number of cycles.
        public int cyclesTotal => root.cyclesTotal;
        public int cyclesDone => root.cyclesDone;
        /// The duration of one cycle.
        public float duration {
            get => root.duration;
            private set {
                Assert.IsTrue(isAlive);
                var rootTween = root.tween;
                ref var rt = ref rootTween.managedData;
                ref var d = ref rootTween.data;
                Assert.IsTrue(d.IsMainSequenceRoot());
                Assert.AreEqual(0f, elapsedTimeTotal);
                if (value != 0f) {
                    // Insert(atTime) is not clamped to minDuration, so a Sequence can have a tiny duration and cross a huge number of cycles in one update. Zero duration is fine: an infinite Sequence crosses max one cycle per frame
                    value = Mathf.Max(TweenSettings.minDuration, value);
                }
                Assert.IsTrue(value >= d.cycleDuration);
                Assert.IsTrue(value >= d.animationDuration);
                Assert.AreEqual(0f, d.startDelay);
                d.animationDuration = value;
                TweenData.CalculateCycleDuration(0f, ref d);
                rt.endValueOrDiff = value.ToContainer();
                TweenData.CacheDiff(ref d, ref rt);
            }
        }

        /// Elapsed time of all cycles.
        public float elapsedTimeTotal {
            get => root.elapsedTimeTotal;
            set => root.elapsedTimeTotal = value;
        }

        /// <summary>The duration of all cycles. If cycles == -1, returns <see cref="float.PositiveInfinity"/>.</summary>
        public float durationTotal => root.durationTotal;

        /// Normalized progress of the current cycle expressed in 0..1 range.
        public float progress {
            get => root.progress;
            set => root.progress = value;
        }

        /// Normalized progress of all cycles expressed in 0..1 range.
        public float progressTotal {
            get => root.progressTotal;
            set => root.progressTotal = value; // p1 todo setters bypass the TryManipulate check. But adding the check would result in a breaking change
        }

        bool TryManipulate(bool checkRecursive = true) => root.TryManipulate(checkRecursive);

        public static Sequence Create(int cycles = 1, SequenceCycleMode cycleMode = SequenceCycleMode.Restart, Ease sequenceEase = Ease.Linear, bool useUnscaledTime = false, UpdateType updateType = default) {
            if (PrimeTweenManager.Instance.isDestroyed) {
                return default;
            }
            var tween = PrimeTweenManager.FetchTween(updateType.enumValue);
            ref var rt = ref tween.managedData;
            ref var d = ref tween.data;

            if (cycleMode == (SequenceCycleMode)CycleMode.Incremental) {
                Debug.LogError($"Sequence doesn't support CycleMode.Incremental. Parameter {nameof(sequenceEase)} is applied to the sequence's timeline, and incrementing the timeline doesn't make sense. For the same reason, {nameof(sequenceEase)} is clamped to [0:1] range.");
                cycleMode = SequenceCycleMode.Restart;
            }
            if (sequenceEase == Ease.Custom) {
                Debug.LogError("Sequence doesn't support Ease.Custom.");
                sequenceEase = Ease.Linear;
            }
            if (sequenceEase == Ease.Default) {
                sequenceEase = Ease.Linear;
            }
            var settings = new TweenSettings(0f, sequenceEase, cycles, (CycleMode)cycleMode, 0f, 0f, useUnscaledTime, updateType);
            tween.intParam = emptySequenceTag;
            tween.Setup(PrimeTweenManager.dummyTarget, ref settings, false, TweenType.MainSequence, ref rt, ref d);
            var root = PrimeTweenManager.AddTween(ref rt, ref d);
            Assert.IsTrue(root.isAliveInternal);
            Assert.IsTrue(d.startValue.IsDefault());
            Assert.IsTrue(rt.endValueOrDiff.IsDefault());
            return new Sequence(root);
        }

        public static Sequence Create(Tween firstTween) {
            return Create().Group(firstTween);
        }

        internal Sequence(ColdData cold) => root = new Tween(cold); // used for testing and convert ColdData back to Sequence

        Sequence(Tween rootTween) {
            root = rootTween;
            SetSequence(rootTween);
            Assert.IsTrue(isAlive);
            Assert.AreEqual(0f, duration);
            Assert.IsTrue(durationTotal == 0f || float.IsPositiveInfinity(durationTotal));
        }

        /// <summary>Groups <paramref name="tween"/> with the 'previous' animation in this Sequence.<br/>
        /// The 'previous' animation is the animation used in the preceding Group, Chain, or Insert operation.<br/>
        /// Grouped animations start at the same time and run in parallel.</summary>
        public Sequence Group(Tween tween) {
            if (TryManipulate()) {
                Insert(GetLastInSelfOrRoot().data.waitDelay, tween);
            }
            return this;
        }

        void AddLinkedReference(Tween tween) {
            ColdData last;
            if (root.tween.next != null) {
                last = GetLast();
                var lastInSelf = GetLastInSelfOrRoot();
                Assert.AreNotEqual(root.id, lastInSelf.id);
                Assert.IsNull(lastInSelf.nextSibling);
                lastInSelf.nextSibling = tween.tween;
                Assert.IsNull(tween.tween.prevSibling);
                tween.tween.prevSibling = lastInSelf;
            } else {
                last = root.tween;
            }

            Assert.IsNull(last.next);
            Assert.IsNull(tween.tween.prev);
            last.next = tween.tween;
            tween.tween.prev = last;
            root.tween.intParam = emptySequenceTag - emptySequenceTag; // set to 0 in a way to be able to search the code better
        }

        ColdData GetLast() {
            ColdData result = default;
            foreach (var current in GetAllTweens()) {
                result = current;
            }
            Assert.IsNotNull(result);
            Assert.IsNull(result.next);
            return result;
        }

        /// <summary>Places <paramref name="tween"/> after all previously added animations in this sequence. Chained animations run sequentially after one another.</summary>
        public Sequence Chain(Tween tween) {
            if (TryManipulate()) {
                Insert(duration, tween);
            }
            return this;
        }

        /// <summary>Places <paramref name="tween"/> inside this Sequence at time <paramref name="atTime"/>, overlapping with other animations.<br/>
        /// The total sequence duration is increased if the inserted <paramref name="tween"/> doesn't fit inside the current sequence duration.</summary>
        public Sequence Insert(float atTime, Tween tween) {
            if (!ValidateCanAdd(tween)) {
                return this;
            }
            if (tween.tween.sequence != null) {
                Debug.LogError($"{Constants.nestTwiceError} Tween: {tween.tween.managedData.GetDescription()}");
                return this;
            }
            SetSequence(tween);
            Insert_internal(atTime, tween);
            return this;
        }

        void Insert_internal(float atTime, Tween other) {
            Assert.AreEqual(0f, other.tween.data.waitDelay);
            if (atTime < 0f) {
                Debug.LogError($"Inserting at negative time ({atTime}) is not allowed.");
                atTime = 0f;
            } else if (float.IsNaN(atTime)) {
                Debug.LogError("Inserting at NaN time is not allowed. Inserting at 0 instead.");
                atTime = 0f;
            }
            ref var otherData = ref other.tween.data;
            otherData.waitDelay = atTime;
            // ValidateCanAdd() reports that these settings of a child are ignored, so reset them. For example, a child with 'timeScale <= 0' reports its completion when the Sequence moves backward
            otherData.isPaused = false;
            otherData.timeScale = 1f;
            duration = Mathf.Max(duration, Mathf.Min(other.durationWithWaitDelay, float.MaxValue)); // the sum of finite values can overflow to infinity
            AddLinkedReference(other);
        }

        /// <summary>Schedules <paramref cref="callback"/> after all previously added tweens.</summary>
        /// <param name="warnIfTargetDestroyed">https://github.com/KyryloKuzyk/PrimeTween/discussions/4</param>
        public Sequence ChainCallback([NotNull] Action callback, bool? warnIfTargetDestroyed = null) {
            if (TryManipulate()) {
                InsertCallback(duration, callback, warnIfTargetDestroyed);
            }
            return this;
        }

        public Sequence InsertCallback(float atTime, Action callback, bool? warnIfTargetDestroyed = null) {
            if (!ValidateCanModifyDuration()) { // validate before creating the callback, or it would be invoked even if Insert() fails
                return this;
            }
            var delay = PrimeTweenManager.DelayWithoutDurationCheck(PrimeTweenManager.dummyTarget, 0f, false);
            Assert.IsTrue(delay.HasValue);
            delay.Value.tween.managedData.OnComplete(callback, warnIfTargetDestroyed);
            return Insert(atTime, delay.Value);
        }

        /// <summary>Schedules <paramref cref="callback"/> after all previously added tweens. Passing 'target' allows to write a non-allocating callback.</summary>
        /// <param name="warnIfTargetDestroyed">https://github.com/KyryloKuzyk/PrimeTween/discussions/4</param>
        public Sequence ChainCallback<T>([NotNull] T target, [NotNull] Action<T> callback, bool? warnIfTargetDestroyed = null) where T: class {
            if (TryManipulate()) {
                InsertCallback(duration, target, callback, warnIfTargetDestroyed);
            }
            return this;
        }

        /// <inheritdoc cref="ChainCallback"/>
        /// <seealso cref="Tween.Delay&lt;T,TPayload&gt;"/>
        public Sequence ChainCallback<T, TPayload>([NotNull] T target, TPayload payload, [NotNull] Action<T, TPayload> callback, bool? warnIfTargetDestroyed = null) where T : class where TPayload : unmanaged {
            if (TryManipulate()) {
                InsertCallback(duration, target, payload, callback, warnIfTargetDestroyed);
            }
            return this;
        }

        public Sequence InsertCallback<T>(float atTime, [NotNull] T target, Action<T> callback, bool? warnIfTargetDestroyed = null) where T: class {
            if (!ValidateCanModifyDuration()) {
                return this;
            }
            var delay = CreateCallback(target, callback, warnIfTargetDestroyed);
            if (!delay.HasValue) {
                return this;
            }
            return Insert(atTime, delay.Value);
        }

        /// <inheritdoc cref="InsertCallback"/>
        /// <seealso cref="Tween.Delay&lt;T,TPayload&gt;"/>
        public Sequence InsertCallback<T, TPayload>(float atTime, [NotNull] T target, TPayload payload, [NotNull] Action<T, TPayload> callback, bool? warnIfTargetDestroyed = null) where T : class where TPayload : unmanaged {
            if (ValidateCanModifyDuration() && TweenData.CheckPayloadSize<TPayload>()) {
                var delay = PrimeTweenManager.DelayWithoutDurationCheck(target, 0f, false);
                if (delay.HasValue) {
                    delay.Value.tween.managedData.OnComplete(target, payload, callback, warnIfTargetDestroyed);
                    return Insert(atTime, delay.Value);
                }
            }
            return this;
        }

#if PRIME_TWEEN_EXPERIMENTAL
        /// <summary>Groups <paramref name="callback"/> with the 'previous' animation in this Sequence.<br/>
        /// The 'previous' animation is the animation used in the preceding Group/Chain/Insert() method call.<br/>
        /// Can be thought of as an "OnStart" callback of a group.</summary>
        /// <param name="warnIfTargetDestroyed">https://github.com/KyryloKuzyk/PrimeTween/discussions/4</param>
        public Sequence GroupCallback([NotNull] Action callback, bool? warnIfTargetDestroyed = null) {
            if (TryManipulate()) {
                InsertCallback(GetLastInSelfOrRoot().data.waitDelay, callback, warnIfTargetDestroyed);
            }
            return this;
        }

        /// <summary>Groups <paramref name="callback"/> with the 'previous' animation in this Sequence.<br/>
        /// The 'previous' animation is the animation used in the preceding Group/Chain/Insert() method call.<br/>
        /// Can be thought of as an "OnStart" callback of a group.</summary>
        /// <param name="warnIfTargetDestroyed">https://github.com/KyryloKuzyk/PrimeTween/discussions/4</param>
        public Sequence GroupCallback<T>([NotNull] T target, [NotNull] Action<T> callback, bool? warnIfTargetDestroyed = null) where T: class {
            if (TryManipulate()) {
                InsertCallback(GetLastInSelfOrRoot().data.waitDelay, target, callback, warnIfTargetDestroyed);
            }
            return this;
        }

        /// <inheritdoc cref="GroupCallback"/>
        /// <seealso cref="Tween.Delay&lt;T,TPayload&gt;"/>
        public Sequence GroupCallback<T, TPayload>([NotNull] T target, TPayload payload, [NotNull] Action<T, TPayload> callback, bool? warnIfTargetDestroyed = null) where T : class where TPayload : unmanaged {
            if (TryManipulate()) {
                InsertCallback(GetLastInSelfOrRoot().data.waitDelay, target, payload, callback, warnIfTargetDestroyed);
            }
            return this;
        }
#endif

        internal static Tween? CreateCallback<T>([NotNull] T target, Action<T> callback, bool? warnIfTargetDestroyed = null) where T: class {
            var delay = PrimeTweenManager.DelayWithoutDurationCheck(target, 0f, false);
            if (!delay.HasValue) {
                return null;
            }
            delay.Value.tween.managedData.OnComplete(target, callback, warnIfTargetDestroyed);
            return delay.Value;
        }

        /// <summary>Schedules delay after all previously added tweens.</summary>
        public Sequence ChainDelay(float duration) {
            return Chain(Tween.Delay(duration));
        }


        /// <summary>Returns 'true' if the sequence is currently waiting for WaitUntil or WaitWhile.</summary>
        public bool isWaiting => TryManipulate() && root.tween.waitPredicateShouldWait != null;

        /// <summary>Suspends the sequence until (or while) the user-provided <paramref name="predicate"/> returns true. Doesn't influence <see cref="duration"/> and other properties.<br/>
        /// Only supported on sequences with 1 cycle and <see cref="Ease.Linear"/>.<br/>
        /// Manipulating sequence directly via properties like <see cref="elapsedTimeTotal"/> bypasses the suspension.</summary>
        public Sequence ChainWaitWhile<T>(               [CanBeNull] T target, Predicate<T> predicate, float? timeout = default) where T : class => InsertWait(duration, target, predicate, false, timeout);
        /// <inheritdoc cref="ChainWaitWhile"/>
        public Sequence ChainWaitUntil<T>(               [CanBeNull] T target, Predicate<T> predicate, float? timeout = default) where T : class => InsertWait(duration, target, predicate, true,  timeout);
        /// <inheritdoc cref="ChainWaitWhile"/>
        public Sequence InsertWaitWhile<T>(float atTime, [CanBeNull] T target, Predicate<T> predicate, float? timeout = default) where T : class => InsertWait(atTime,   target, predicate, false, timeout);
        /// <inheritdoc cref="ChainWaitWhile"/>
        public Sequence InsertWaitUntil<T>(float atTime, [CanBeNull] T target, Predicate<T> predicate, float? timeout = default) where T : class => InsertWait(atTime,   target, predicate, true,  timeout);

        Sequence InsertWait<T>(float atTime, [CanBeNull] T target, Predicate<T> predicate, bool isWaitUntil, float? timeout = default) where T : class {
            if (!ValidateCanModifyDuration() || !ValidateWaitIsSupported()) {
                return this;
            }
            if (TweenData.IsDestroyedUnityObject(target)) {
                Debug.LogError($"'{nameof(target)}' is null or has been destroyed.");
                return this;
            }
            if (predicate == null) {
                Debug.LogError($"'{nameof(predicate)}' is null.");
                return this;
            }
            if (timeout is float _timeout && (float.IsNaN(_timeout) || _timeout <= 0f)) {
                Debug.LogError($"'{nameof(timeout)}' should be greater than 0, but was {_timeout}.");
                timeout = default;
            }
            if (!(PrimeTweenManager.DelayWithoutDurationCheck(target ?? PrimeTweenManager.dummyTarget, 0f, false, isCallback: false) is Tween waitWhile)) {
                return this;
            }
            root.tween.data.HasWait = true; // propagate up to the root
            waitWhile.tween.data.HasWait = true;
            if (isWaitUntil) {
                waitWhile.tween.data.IsWaitUntil = true;
            }
            waitWhile.tween.waitPredicateTarget = target;
            waitWhile.tween.waitPredicate = predicate;
            waitWhile.tween.waitTimeout = timeout ?? float.PositiveInfinity;
            waitWhile.tween.waitPredicateShouldWait = cold => {
                var _predicate = cold.waitPredicate as Predicate<T>;
                Assert.IsNotNull(_predicate);
                var waitForTarget = cold.waitPredicateTarget as T;
                if (TweenData.IsDestroyedUnityObject(waitForTarget)) {
                    return false;
                }
                try {
                    return _predicate(waitForTarget) ^ cold.data.IsWaitUntil;
                } catch (Exception e) {
                    cold.managedData.LogErrorWithStackTrace($"Wait '{nameof(predicate)}' raised exception {e}, aborting wait.");
                    return false;
                }
            };
            return Insert(atTime, waitWhile);
        }

        bool ValidateWaitIsSupported() {
            ref var d = ref root.tween.data;
            if (d.cyclesTotal != 1 || d.ease != Ease.Linear) {
                Debug.LogError($"WaitUntil and WaitWhile are only supported on a sequences with 'cycles: 1' and 'sequenceEase: {nameof(Ease.Linear)}', " +
                               $"but this Sequence has 'cycles: {d.cyclesTotal}' and 'sequenceEase: {d.ease}'.");
                return false;
            }
            return true;
        }

        ColdData GetLastInSelfOrRoot() {
            Assert.IsTrue(isAlive);
            var result = root.tween;
            foreach (var current in GetSelfChildren()) {
                result = current;
            }
            Assert.IsNotNull(result);
            Assert.IsNull(result.nextSibling);
            return result;
        }

        void SetSequence(Tween handle) {
            Assert.IsTrue(IsCreated);
            Assert.IsTrue(handle.isAliveInternal);
            var tween = handle.tween;
            Assert.IsNull(tween.sequence);
            tween.sequence = root.tween;
            tween.data.isInSequence = true;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "CompareOfFloatsByEqualityOperator")]
        bool ValidateCanModifyDuration() {
            if (!Tween.ValidateCanModify(root)) {
                return false;
            }
            // ModificationIsNotAllowed is set only when the manager picks up the sequence, but the elapsed time can be set before that. Changing the duration of a moved sequence would desync it from its children
            if (elapsedTimeTotal != 0f) {
                Debug.LogError(Constants.modificationIsNotAllowedError);
                return false;
            }
            return true;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "CompareOfFloatsByEqualityOperator")]
        bool ValidateCanAdd(Tween other) {
            if (!ValidateCanModifyDuration()) {
                return false;
            }
            if (!other.isAliveInternal) {
                Debug.LogError(Constants.addDeadTweenToSequenceError);
                return false;
            }
            if (other.tween.data.isUpdating) {
                // adding a tween to a Sequence from its own onValueChange would release the tween when it completes in this update
                Debug.LogError(Constants.recursiveCallError);
                return false;
            }
            if (!other.ValidateIsNotCompleting()) {
                return false; // reparenting would prevent the completing sequence from releasing its children
            }
            var otherData = other.tween.data;
            if (otherData.cyclesTotal == -1) {
                Debug.LogError(Constants.infiniteTweenInSequenceError);
                return false;
            }
            if (otherData.HasCancellationToken) {
                Debug.LogError("Adding an animation with a CancellationToken (" + nameof(Tween.SetCancellationToken) + "()) to a Sequence is not allowed because the Sequence controls the lifetime of all its children.\n" +
                               "Call " + nameof(SetCancellationToken) + "() on the Sequence itself instead.");
                return false;
            }
            var thisData = root.tween.data;
            if (otherData.isPaused && otherData.isPaused != thisData.isPaused) {
                warnIgnoredChildrenSetting(nameof(isPaused), thisData.isPaused, otherData.isPaused);
            }
            if (otherData.timeScale != 1f && otherData.timeScale != thisData.timeScale) {
                warnIgnoredChildrenSetting(nameof(timeScale), thisData.timeScale, otherData.timeScale);
            }
            if (otherData.useUnscaledTime && otherData.useUnscaledTime != thisData.useUnscaledTime) {
                warnIgnoredChildrenSetting(nameof(TweenSettings.useUnscaledTime), thisData.useUnscaledTime, otherData.useUnscaledTime);
            }
            if (otherData.updateType != PrimeTweenManager.Instance.defaultUpdateType && otherData.updateType != thisData.updateType) {
                warnIgnoredChildrenSetting(nameof(TweenSettings.updateType), thisData.updateType, otherData.updateType);
            }
            void warnIgnoredChildrenSetting(string settingName, object sequenceSetting, object childSetting) {
                Debug.LogError($"'{settingName}' was ignored after adding child animation to the Sequence (Sequence has '{sequenceSetting}', but the child had '{childSetting}').\n" +
                               $"Parent Sequence controls '{settingName}' of all its children animations. To prevent this error:\n" +
                               $"- Use the default value of '{settingName}' in child animation.\n" +
                               $"- OR use the same '{settingName}' in child animation.\n");
            }

            other.tween.data.TryRollBackJobUpdate(); // otherwise, the manager will apply the job's result and complete the tween on its own while the Sequence controls it
            return true;
        }

        /// Stops all tweens in the Sequence, ignoring callbacks.
        public void Stop() {
            if (isAlive && TryManipulate(false)) {
                Assert.IsTrue(root.tween.data.IsMainSequenceRoot());
                ReleaseTweens();
                Assert.IsFalse(isAlive);
            }
        }

        /// <summary>Immediately completes the sequence.<br/>
        /// If the sequence has infinite cycles (cycles == -1), completes only the current cycle. To choose where the sequence should stop (at the 'start' or at the 'end') in case of infinite cycles, use <see cref="SetRemainingCycles(bool stopAtEndValue)"/> before calling Complete().</summary>
        public void Complete() {
            if (isAlive && TryManipulate(false)) {
                if (root.tween.data.IsResetting) {
                    return;
                }
                if (!root.tween.data.IsCompleting) { // a callback can call Complete() while the sequence is already completing
                    root.isPaused = false; // releases a paused sequence that has already completed. See CompleteContractSequenceCompleteCompletesSequencePausedAtTheEnd
                    if (!isAlive) {
                        return;
                    }
                    if (timeScale > 0f && root.tween.data.GetState() == State.After) {
                        // the sequence has completed while paused and then became longer (for example, with InsertWaitWhile()), so the update below would see no change
                        ReleaseTweens();
                        return;
                    }
                    if (cyclesTotal == -1 || root.tween.data.cycleMode == CycleMode.Restart) {
                        SetRemainingCycles(1);
                    } else {
                        int cyclesLeft = cyclesTotal - cyclesDone;
                        SetRemainingCycles(cyclesLeft % 2 == 1 ? 1 : 2);
                    }
                }
                // A callback can reassign the variable this method was called on. A readonly struct receives 'this' by reference, so 'root' would refer to the new Sequence after the callback
                var rootHandle = root;
                // bypass the 'isPaused' setter: it's not allowed while the sequence is completing, but a paused sequence can complete when it's moved to the end, and its callback can call Complete(). See CompleteContractSequenceCompleteFromCallbackCompletesPausedSequence
                rootHandle.tween.data.isPaused = false;
                Assert.IsTrue(rootHandle.tween.data.IsMainSequenceRoot());
                using (new PrimeTweenManager.UpdateDepthScope(PrimeTweenManager.Instance)) {
                    rootHandle.tween.managedData.UpdateSequence(timeScale > 0f ? float.MaxValue : TweenData.negativeElapsedTime, false, true, false, false, ref rootHandle.tween.data);
                    Assert.IsFalse(rootHandle.isAliveInternal);
                }
            }
        }

        internal void EmergencyStop() {
            Assert.IsTrue(isAlive);
            Assert.IsTrue(root.tween.data.IsMainSequenceRoot());
            ReleaseTweens(t => t.WarnOnCompleteIgnored(false));
        }

        internal void ReleaseTweens([CanBeNull] Action<TweenData> beforeKill = null) {
            var enumerator = GetAllTweens();
            enumerator.MoveNext();
            var current = enumerator.Current;
            Assert.IsTrue(current.data.isAlive);
            while (true) {
                ColdData next = enumerator.MoveNext() ? enumerator.Current : null;
                ColdData tween = current;
                ref var rt = ref tween.managedData;
                ref var d = ref tween.data;

                Assert.IsTrue(d.isAlive);
                beforeKill?.Invoke(rt);
                rt.Kill(ref d);
                Assert.IsFalse(d.isAlive);
                ReleaseTween(ref rt);
                if (next == null) {
                    break;
                }
                current = next;
            }
            Assert.IsFalse(isAlive); // not IsCreated because this may be a local variable in the user's codebase
        }

        static void ReleaseTween(ref TweenData tween) {
            // Debug.Log($"[{Time.frameCount}] ReleaseTween {tween.id}");
            Assert.AreNotEqual(0, tween.sequence.id);
            tween.cold.next = null;
            tween.cold.prev = null;
            tween.cold.prevSibling = null;
            tween.cold.nextSibling = null;
            tween.cold.sequence = null;

            ref var d = ref tween.cold.data;
            d.isInSequence = false;
            if (d.IsSequenceRoot()) {
                d.tweenType = TweenType.Disabled;
                Assert.IsFalse(d.IsSequenceRoot());
            }
        }

        internal SequenceChildrenEnumerator GetAllChildren() {
            var enumerator = GetAllTweens();
            var movedNext = enumerator.MoveNext(); // skip self
            Assert.IsTrue(movedNext);
            Assert.AreEqual(root.tween, enumerator.Current);
            return enumerator;
        }

        /// <summary>Stops the sequence when it reaches the 'end' or returns to the 'start' for the next time.<br/>
        /// For example, if you have an infinite sequence (cycles == -1) with CycleMode.Yoyo/Rewind, and you wish to stop it when it reaches the 'end', then set <paramref cref="stopAtEndValue"/> to true.
        /// To stop the animation at the 'beginning', set <paramref cref="stopAtEndValue"/> to false.</summary>
        public void SetRemainingCycles(bool stopAtEndValue) {
            root.SetRemainingCycles(stopAtEndValue);
        }

        /// <summary>Sets the number of remaining cycles.<br/>
        /// This method modifies the <see cref="cyclesTotal"/> so that the sequence will complete after the number of <paramref cref="cycles"/>.<br/>
        /// To set the initial number of cycles, use Sequence.Create(cycles: numCycles) instead.<br/><br/>
        /// Setting cycles to -1 will repeat the sequence indefinitely.<br/>
        /// </summary>
        public void SetRemainingCycles(int cycles) {
            root.SetRemainingCycles(cycles);
        }

        public bool isPaused {
            get => root.isPaused;
            set => root.isPaused = value;
        }

        internal SequenceDirectEnumerator GetSelfChildren(bool isForward = true) => new SequenceDirectEnumerator(this, isForward);
        internal SequenceChildrenEnumerator GetAllTweens() => new SequenceChildrenEnumerator(this);

        public override string ToString() => root.ToString();

        internal struct SequenceDirectEnumerator {
            readonly Sequence sequence;
            ColdData current;
            readonly bool isEmpty;
            readonly bool isForward;
            bool isStarted;

            internal SequenceDirectEnumerator(Sequence s, bool isForward) {
                Assert.IsTrue(s.isAlive, s.id);
                sequence = s;
                this.isForward = isForward;
                isStarted = false;
                isEmpty = IsSequenceEmpty(s);
                if (isEmpty) {
                    current = null;
                    return;
                }
                current = sequence.root.tween.next;
                Assert.IsTrue(current != null && current.id != sequence.root.tween.nextSibling?.id);
                if (!isForward) {
                    while (true) {
                        var next = current.nextSibling;
                        if (next == null) {
                            break;
                        }
                        current = next;
                    }
                }
                Assert.IsNotNull(current);
            }

            static bool IsSequenceEmpty(Sequence s) {
                // tests: SequenceNestingDifferentSettings(), TestSequenceEnumeratorWithEmptySequences()
                return s.root.tween.intParam == emptySequenceTag;
            }

            public
                #if UNITY_2020_2_OR_NEWER
                readonly
                #endif
                SequenceDirectEnumerator GetEnumerator() {
                Assert.IsTrue(sequence.isAlive);
                return this;
            }

            public
                #if UNITY_2020_2_OR_NEWER
                readonly
                #endif
                ColdData Current {
                get {
                    Assert.IsTrue(sequence.isAlive);
                    Assert.IsNotNull(current);
                    Assert.IsNotNull(current.sequence);
                    return current;
                }
            }

            public bool MoveNext() {
                if (isEmpty) {
                    return false;
                }
                Assert.IsTrue(current.data.isAlive, current.id);
                if (!isStarted) {
                    isStarted = true;
                    return true;
                }
                current = isForward ? current.nextSibling : current.prevSibling;
                return current != null;
            }
        }

        internal struct SequenceChildrenEnumerator {
            readonly Sequence sequence;
            ColdData current;
            bool isStarted;

            internal SequenceChildrenEnumerator(Sequence s) {
                Assert.IsTrue(s.isAlive);
                Assert.IsTrue(s.root.tween.data.IsMainSequenceRoot());
                sequence = s;
                current = default;
                isStarted = false;
            }

            public
                #if UNITY_2020_2_OR_NEWER
                readonly
                #endif
                SequenceChildrenEnumerator GetEnumerator() {
                Assert.IsTrue(sequence.isAlive);
                return this;
            }

            public
                #if UNITY_2020_2_OR_NEWER
                readonly
                #endif
                ColdData Current {
                get {
                    Assert.IsNotNull(current);
                    Assert.IsNotNull(current.sequence);
                    return current;
                }
            }

            public bool MoveNext() {
                if (!isStarted) {
                    Assert.IsNull(current);
                    current = sequence.root.tween;
                    isStarted = true;
                    return true;
                }
                Assert.IsTrue(current.data.isAlive);
                current = current.next;
                return current != null;
            }
        }

        /// <summary>Places <paramref name="sequence"/> after all previously added animations in this sequence. Chained animations run sequentially after one another.</summary>
        public Sequence Chain(Sequence sequence) {
            if (TryManipulate()) {
                Insert(duration, sequence);
            }
            return this;
        }

        /// <summary>Groups <paramref name="sequence"/> with the 'previous' animation in this Sequence.<br/>
        /// The 'previous' animation is the animation used in the preceding Group/Chain/Insert() method call.<br/>
        /// Grouped animations start at the same time and run in parallel.</summary>
        public Sequence Group(Sequence sequence) {
            if (TryManipulate()) {
                Insert(GetLastInSelfOrRoot().data.waitDelay, sequence);
            }
            return this;
        }

        /// <summary>Places <paramref name="sequence"/> inside this Sequence at time <paramref name="atTime"/>, overlapping with other animations.<br/>
        /// The total sequence duration is increased if the inserted <paramref name="sequence"/> doesn't fit inside the current sequence duration.</summary>
        public Sequence Insert(float atTime, Sequence sequence) {
            if (!ValidateCanAdd(sequence.root)) {
                return this;
            }

            if (sequence.root.tween.data.tweenType != TweenType.MainSequence) {
                Debug.LogError(Constants.nestTwiceError);
                return this;
            }
            if (sequence.root.id == id) {
                Debug.LogError("It's not allowed to add a Sequence to itself.");
                return this;
            }
            if (sequence.root.tween.data.HasWait) {
                if (!ValidateWaitIsSupported()) {
                    return this;
                }
                root.tween.data.HasWait = true;
            }
            sequence.root.tween.data.tweenType = TweenType.NestedSequence;

            Insert_internal(atTime, sequence.root);
            ValidateSequenceEnumerator();
            return this;
        }

        /// <summary>Custom timeScale. To smoothly animate timeScale over time, use <see cref="Tween.TweenTimeScale"/> method.</summary>
        public float timeScale {
            get => root.timeScale;
            set => root.timeScale = value;
        }

        [System.Diagnostics.Conditional("SAFETY_CHECKS")]
        void ValidateSequenceEnumerator() {
            var buffer = new List<TweenData> {
                root.tween.managedData
            };
            foreach (var t in GetAllTweens()) {
                // Debug.Log($"----- {t}");
                if (t.data.IsSequenceRoot()) {
                    foreach (var ch in new Sequence(t.sequence).GetSelfChildren()) {
                        // Debug.Log(ch);
                        buffer.Add(ch.managedData);
                    }
                }
            }
            if (buffer.Count != buffer.Select(_ => _.id).Distinct().Count()) {
                Debug.LogError($"{root.id}, duplicates in ValidateSequenceEnumerator():\n{string.Join("\n", buffer)}");
            }
        }

        public Sequence OnComplete(Action onComplete, bool? warnIfTargetDestroyed = null) {
            root.OnComplete(onComplete, warnIfTargetDestroyed);
            return this;
        }

        public Sequence OnComplete<T>(T target, Action<T> onComplete, bool? warnIfTargetDestroyed = null) where T : class {
            root.OnComplete(target, onComplete, warnIfTargetDestroyed);
            return this;
        }

        public override int GetHashCode() => root.GetHashCode();
        public bool Equals(Sequence other) => root.Equals(other.root);

        [Obsolete(ObsoleteMessages.resetOnCompleteRenamed, true)]
        #if PRIME_TWEEN_EXPERIMENTAL
        public
        #endif
        Sequence ResetOnComplete() => ResetOnCompletion();

        /// <inheritdoc cref="Tween.ResetOnCompletion"/>
        #if PRIME_TWEEN_EXPERIMENTAL
        public
        #endif
        Sequence ResetOnCompletion() {
            root.ResetOnCompletion();
            return this;
        }

        public enum SequenceCycleMode : byte {
            [Tooltip(Constants.cycleModeRestartTooltip)]
            Restart = _CycleMode.Restart,
            [Tooltip("Preserves easing of animations when '" + nameof(TweenAnimation) + "' is moving backward. Useful for having the same motion on the backward cycle.")]
            YoyoChildren = _CycleMode.YoyoChildren,
            [Tooltip(Constants.cycleModeYoyoTooltip + " Use '" + nameof(YoyoChildren) + "' to preserve easing of animations on the backward cycle.")]
            Yoyo = CycleMode.Yoyo,
            [Tooltip(Constants.cycleModeRewindTooltip)]
            Rewind = _CycleMode.Rewind
        }
    }
}
