using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Implemented by components that want to be driven from the shared <see cref="UpdateTicker"/>
/// instead of owning a MonoBehaviour <c>Update()</c>.
/// </summary>
public interface ITickable { void Tick(); }

/// <summary>Late-phase counterpart of <see cref="ITickable"/> (runs in LateUpdate).</summary>
public interface ILateTickable { void LateTick(); }

/// <summary>
/// One MonoBehaviour that ticks many components.
///
/// WHY: every MonoBehaviour with an <c>Update()</c> costs a managed-to-native call each frame
/// even when the body does nothing. The scene census counted 783 such callbacks per frame, and
/// BehaviourUpdate was the single largest script cost in the capture (median 3.9 ms). Components
/// that implement <see cref="ITickable"/> and register here collapse into ONE native call plus a
/// plain C# loop, which is roughly an order of magnitude cheaper per participant.
///
/// The ticker spawns itself before the first scene loads and survives scene changes, so callers
/// only ever have to Register/Unregister — usually from OnEnable/OnDisable.
/// </summary>
[DefaultExecutionOrder(-100)]
public sealed class UpdateTicker : MonoBehaviour
{
    private static UpdateTicker _instance;
    private static bool _quitting;

    // Null slots mark removed entries. Compacting happens outside the iteration, so a component
    // may safely unregister itself (or another one) from inside its own Tick.
    private static readonly List<ITickable> Tickables = new List<ITickable>(512);
    private static readonly List<ILateTickable> LateTickables = new List<ILateTickable>(64);
    private static int _tickHoles;
    private static int _lateHoles;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetStatics()
    {
        // Domain reload can be disabled in the editor, so statics survive a play-mode restart.
        Tickables.Clear();
        LateTickables.Clear();
        _tickHoles = 0;
        _lateHoles = 0;
        _instance = null;
        _quitting = false;
    }

    private static void EnsureInstance()
    {
        if (_instance != null || _quitting) return;

        GameObject go = new GameObject("~UpdateTicker") { hideFlags = HideFlags.DontSave };
        _instance = go.AddComponent<UpdateTicker>();
        DontDestroyOnLoad(go);
    }

    public static void Register(ITickable t)
    {
        if (t == null || _quitting) return;
        EnsureInstance();
        if (Tickables.Contains(t)) return;
        Tickables.Add(t);
    }

    public static void Unregister(ITickable t)
    {
        if (t == null) return;
        int i = Tickables.IndexOf(t);
        if (i < 0) return;
        Tickables[i] = null;
        _tickHoles++;
    }

    public static void RegisterLate(ILateTickable t)
    {
        if (t == null || _quitting) return;
        EnsureInstance();
        if (LateTickables.Contains(t)) return;
        LateTickables.Add(t);
    }

    public static void UnregisterLate(ILateTickable t)
    {
        if (t == null) return;
        int i = LateTickables.IndexOf(t);
        if (i < 0) return;
        LateTickables[i] = null;
        _lateHoles++;
    }

    private void Update()
    {
        // Snapshot the count: anything registered from inside a Tick starts next frame, which
        // matches how Unity treats a component enabled mid-Update.
        int count = Tickables.Count;
        for (int i = 0; i < count; i++)
        {
            ITickable t = Tickables[i];
            if (t != null) t.Tick();
        }

        if (_tickHoles > 32) Compact(Tickables, ref _tickHoles);
    }

    private void LateUpdate()
    {
        int count = LateTickables.Count;
        for (int i = 0; i < count; i++)
        {
            ILateTickable t = LateTickables[i];
            if (t != null) t.LateTick();
        }

        if (_lateHoles > 32) Compact(LateTickables, ref _lateHoles);
    }

    private static void Compact<T>(List<T> list, ref int holes) where T : class
    {
        int write = 0;
        for (int read = 0; read < list.Count; read++)
        {
            if (list[read] == null) continue;
            list[write++] = list[read];
        }
        list.RemoveRange(write, list.Count - write);
        holes = 0;
    }

    private void OnApplicationQuit() => _quitting = true;

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }
}
