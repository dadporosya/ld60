using PrimeTween;
using UnityEngine;

/// <summary>
/// Pre-sizes PrimeTween's tween pool.
///
/// The capture logged "Tweens capacity has been increased from 200 to 400 ... 400 to 800 ...
/// 800 to 1600" during a single 40 second run. Each of those is a reallocation of the whole pool
/// plus a warning with a managed stack trace, and it happens mid-gameplay — exactly when a hitch
/// is most visible. The scene runs ~950 looping animations, so 1600 was not headroom, it was the
/// ceiling being hit three times in a row.
///
/// Raise <see cref="Capacity"/> if the warning ever comes back.
/// </summary>
public static class TweenPoolWarmup
{
    private const int Capacity = 2048;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Warmup()
    {
        PrimeTweenConfig.SetTweensCapacity(Capacity);
    }
}
