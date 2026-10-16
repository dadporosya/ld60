using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns plain UI prefabs as popups — anything that has no dedicated popup component of its own
/// (decorative windows, images, adware banners, one-off props).
///
/// <code>
/// GameObjectAlertManager.Instance.SpawnAlert();                    // random spot, random prefab
/// GameObjectAlertManager.Instance.SpawnAlert(screenPos);
/// GameObjectAlertManager.Instance.SpawnAlert(go =&gt; go.name = "x"); // per-spawn setup
/// GameObjectAlertManager.Instance.StartSpawning();                 // continuous storm
/// </code>
///
/// Everything about HOW popups are spawned — placement, the continuous loop, the difficulty-driven
/// rate, the budget and the sound throttle — lives in <see cref="PopupManagerBase{TPopup}"/>.
///
/// <b>Why RectTransform and not GameObject.</b> The base class is constrained to
/// <c>where TPopup : Component</c> (it needs Unity's lifetime check and <c>transform</c> for
/// placement), and <see cref="GameObject"/> is not a Component. <see cref="RectTransform"/> is the
/// UI-side equivalent: assign the prefab's root RectTransform to <see cref="PopupManagerBase{TPopup}.pfbs"/>
/// and reach the object itself through <c>popup.gameObject</c>. The GameObject-shaped helpers below
/// wrap that, so callers never have to think about it.
/// </summary>
public class GameObjectAlertManager : PopupManagerBase<RectTransform>
{
    public static GameObjectAlertManager Instance;

    // ================================================================ Setup

    protected override void RegisterInstance()
    {
        h.CreateStaticInstance(this, ref Instance, dontDestroyOnLoad);
    }

    protected override void UnregisterInstance()
    {
        if (Instance == this) Instance = null;
    }

    // ================================================================ Spawning

    /// <summary>
    /// Spawns one popup and hands the caller its <see cref="GameObject"/>.
    /// </summary>
    /// <param name="screenPosition">
    /// Where to put it, in screen pixels. Null = a random point on screen, kept
    /// <see cref="PopupManagerBase{TPopup}.screenMargin"/> pixels away from the edges.
    /// </param>
    /// <param name="sfx">
    /// Spawn sound override. Null = a random clip from <see cref="PopupManagerBase{TPopup}.spawnSfx"/>.
    /// </param>
    /// <param name="configure">
    /// Per-spawn setup. Runs before the prefab's own Start(), so it comes up already configured.
    /// </param>
    /// <returns>The spawned object, or null when nothing was spawned (no prefabs, or budget spent).</returns>
    public GameObject SpawnAlert(Vector2? screenPosition = null, AudioClip sfx = null,
        Action<GameObject> configure = null)
    {
        RectTransform popup = SpawnPopup(screenPosition, sfx,
            rt => configure?.Invoke(rt.gameObject));

        return popup ? popup.gameObject : null;
    }

    /// <summary>Convenience overload for callers that only want the per-spawn setup callback.</summary>
    public GameObject SpawnAlert(Action<GameObject> configure)
    {
        return SpawnAlert(null, null, configure);
    }

    /// <summary>
    /// Spawns a specific prefab, bypassing the random pick from
    /// <see cref="PopupManagerBase{TPopup}.pfbs"/>. Useful when the caller already knows what it wants
    /// on screen and only needs the manager's placement, budget and trimming.
    /// </summary>
    public GameObject SpawnSpecific(RectTransform prefab, Vector2? screenPosition = null,
        AudioClip sfx = null, Action<GameObject> configure = null)
    {
        if (!prefab)
        {
            h.Out($"{GetType().Name}: SpawnSpecific called with no prefab.");
            return null;
        }

        // ChoosePrefab() is what the base loop calls, so a temporary forced pick is how a specific
        // prefab gets routed through the normal spawn path (budget, placement, sfx, trimming).
        RectTransform previous = _forcedPrefab;
        _forcedPrefab = prefab;

        try
        {
            return SpawnAlert(screenPosition, sfx, configure);
        }
        finally
        {
            _forcedPrefab = previous;
        }
    }

    private RectTransform _forcedPrefab;

    protected override RectTransform ChoosePrefab()
    {
        return _forcedPrefab ? _forcedPrefab : base.ChoosePrefab();
    }

    // ================================================================ Aliases

    /// <summary>Objects currently alive. Alias of <see cref="PopupManagerBase{TPopup}.SpawnedPopups"/>.</summary>
    public IReadOnlyList<RectTransform> SpawnedAlerts => SpawnedPopups;

    /// <summary>Objects created since the last ResetBudget().</summary>
    public int TotalAlertsSpawned => TotalPopupsSpawned;

    /// <summary>The canvas objects are spawned on.</summary>
    public Canvas AlertCanvas => PopupCanvas;

    /// <summary>Stops the loop and tears every popup down. Alias of ClearPopups.</summary>
    public void ClearAlerts(bool includeStrays = false) => ClearPopups(includeStrays);
}
