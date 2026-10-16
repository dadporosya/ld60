using System;
using JetBrains.Annotations;
using UnityEngine;

namespace PrimeTween {
    /// Global PrimeTween configuration.
    [PublicAPI]
    public static partial class PrimeTweenConfig {
        internal static PrimeTweenManager Instance => PrimeTweenManager.Instance;

        /// <summary>
        /// If <see cref="PrimeTweenManager"/> instance is already created, <see cref="SetTweensCapacity"/> will allocate garbage,
        ///     so it's recommended to use it when no active gameplay is happening. For example, on game launch or when loading a level.
        /// <para>To set initial capacity before <see cref="PrimeTweenManager"/> is created, call <see cref="SetTweensCapacity"/> from a method
        /// with <see cref="RuntimeInitializeOnLoadMethodAttribute"/> and <see cref="RuntimeInitializeLoadType.BeforeSplashScreen"/>. See the example below.</para>
        /// </summary>
        /// <example>
        /// <code>
        /// [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        /// static void beforeSplashScreen() {
        ///     PrimeTweenConfig.SetTweensCapacity(42);
        /// }
        /// </code>
        /// </example>
        public static void SetTweensCapacity(int capacity) {
            if (capacity < 0) {
                Debug.LogError($"Invalid '{nameof(capacity)}' value: {capacity}. Capacity can't be negative.");
                return;
            }
            if (PrimeTweenManager.HasInstance && !PrimeTweenManager.Instance.isDestroyed) {
                if (PrimeTweenManager.Instance.updateDepth != 0) {
                    Debug.LogError($"{nameof(SetTweensCapacity)}() can't be called from animation callbacks.");
                    return;
                }
                PrimeTweenManager.Instance.SetTweensCapacity(capacity);
            } else {
                PrimeTweenManager.customInitialCapacity = capacity;
            }
        }

        public static Ease defaultEase {
            get => Instance.defaultEase;
            set {
                if (value == Ease.Custom || value == Ease.Default) {
                    Debug.LogError("defaultEase can't be Ease.Custom or Ease.Default.");
                    return;
                }
                Instance.defaultEase = value;
            }
        }

        public static UpdateType defaultUpdateType {
            get => new UpdateType(Instance.defaultUpdateType);
            set {
                if (value == UpdateType.Default) {
                    Debug.LogError(nameof(defaultUpdateType) + " can't be " + nameof(_UpdateType.Default) + ".");
                    return;
                }
                Instance.defaultUpdateType = value.enumValue;
            }
        }
        
        public static bool warnTweenOnDisabledTarget {
            get => Instance.warnTweenOnDisabledTarget;
            set => Instance.warnTweenOnDisabledTarget = value;
        }
        
        public static bool warnZeroDuration {
            get => Instance.warnZeroDuration;
            set => Instance.warnZeroDuration = value;
        }

        public static bool warnStructBoxingAllocationInCoroutine {
            get => Instance.warnStructBoxingAllocationInCoroutine;
            set => Instance.warnStructBoxingAllocationInCoroutine = value;
        }

        public static bool validateCustomCurves {
            get => Instance.validateCustomCurves;
            set => Instance.validateCustomCurves = value;
        }

        public static bool warnBenchmarkWithAsserts {
            get => Instance.warnBenchmarkWithAsserts;
            set => Instance.warnBenchmarkWithAsserts = value;
        }

        internal const bool defaultUseUnscaledTimeForShakes = false;

        public static bool warnEndValueEqualsCurrent {
            get => Instance.warnEndValueEqualsCurrent;
            set => Instance.warnEndValueEqualsCurrent = value;
        }

        /// <summary>Sets the global default value for the 'warnIfTargetDestroyed' parameter.<br/>
        /// NOTE: it's not recommended to disable warnings. Instead, consider 'warnIfTargetDestroyed: false' on a case-by-case basis on non-business critical animations.<br/>
        /// See more: https://github.com/KyryloKuzyk/PrimeTween/discussions/4#discussioncomment-15189187</summary>
        public static bool warnIfTargetDestroyed {
            get => Instance.warnIfTargetDestroyed;
            set => Instance.warnIfTargetDestroyed = value;
        }

        /// <summary>The default cancellation mode of <seealso cref="Tween.SetCancellationToken"/>.</summary>
        public static TweenSettings.CancellationMode defaultCancellationMode {
            get => Instance.defaultCancellationMode;
            set => Instance.defaultCancellationMode = value;
        }

        #if PRIME_TWEEN_EXPERIMENTAL
        public static void ManualUpdate(UpdateType updateType, float? deltaTime = null, float? unscaledDeltaTime = null) {
            if (deltaTime is float dt && float.IsNaN(dt) || unscaledDeltaTime is float udt && float.IsNaN(udt)) {
                Debug.LogError($"Invalid {nameof(deltaTime)} ({deltaTime}) or {nameof(unscaledDeltaTime)} ({unscaledDeltaTime}).");
                return;
            }
            // with the max timeScale and the min duration, one update crosses max 10 * 100 / 0.0001 = 10^7 cycles, which is below the 2^24 limit of Tween.elapsedTimeTotal. A bigger deltaTime overflows 'int cyclesDone'
            const float maxDeltaTime = 10f;
            if (deltaTime > maxDeltaTime || unscaledDeltaTime > maxDeltaTime) {
                Debug.LogError($"{nameof(deltaTime)} ({deltaTime}) and {nameof(unscaledDeltaTime)} ({unscaledDeltaTime}) should be <= {maxDeltaTime}.");
                if (deltaTime > maxDeltaTime) {
                    deltaTime = maxDeltaTime;
                }
                if (unscaledDeltaTime > maxDeltaTime) {
                    unscaledDeltaTime = maxDeltaTime;
                }
            }
            if (Instance.updateDepth != 0) {
                Debug.LogError($"{nameof(ManualUpdate)}() can't be called from animation callbacks.");
                return;
            }
            if (updateType != UpdateType.Manual) {
                Instance.enabled = false;
            }
            Instance.UpdateTweens(updateType.enumValue, deltaTime, unscaledDeltaTime);
        }
        #endif

        #if PRIME_TWEEN_EXPERIMENTAL
        public static void ManualUpdateApplyStartValues(UpdateType updateType) {
            if (Instance.updateDepth != 0) {
                Debug.LogError($"{nameof(ManualUpdateApplyStartValues)}() can't be called from animation callbacks.");
                return;
            }
            if (updateType != UpdateType.Manual) {
                Instance.enabled = false;
            }
            Instance.ApplyStartValues(updateType.enumValue);
        }
        #endif

        #if PRIME_TWEEN_EXPERIMENTAL
        public
        #else
        internal
        #endif
        static void ManualInitialize() {
            if (!PrimeTweenManager.HasInstance) {
                PrimeTweenManager.CreateInstanceAndDontDestroy();
            } else {
                const string error = "'" + nameof(PrimeTweenManager) + "' is already created. Use this method only to create '" + nameof(PrimeTweenManager) + "' before '" + nameof(RuntimeInitializeLoadType) + "." + nameof(RuntimeInitializeLoadType.BeforeSceneLoad) + "'.";
                Debug.LogError(error);
            }
        }
    }
}
