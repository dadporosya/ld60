using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns <see cref="WinXPMessageAlert"/> popups on the UI canvas.
///
/// Operating principle — every Win XP popup in the scene is owned by one of these. Anything that
/// wants an alert asks a manager for one instead of instantiating a prefab itself:
///
/// <code>
/// WinXpAlertManager.Instance.SpawnAlert();                                  // random spot, random text
/// WinXpAlertManager.Instance.SpawnAlert(screenPos, "Error", "Access denied");
/// WinXpAlertManager.Instance.StartSpawning();                               // continuous storm
/// </code>
///
/// Everything about HOW popups are spawned — placement, the continuous loop, the difficulty-driven
/// rate, the budget and the sound throttle — lives in <see cref="PopupManagerBase{TPopup}"/>. What is
/// left here is only what is specific to Win XP alerts: the <see cref="titles"/> / <see cref="contents"/>
/// text pools, and the fact that the popup plays its own sound (so the manager hands it the clip list
/// instead of playing anything itself).
///
/// <b>Text.</b> Every alert this manager spawns — the continuous storm's included — gets a random title
/// from <see cref="titles"/> and a random body from <see cref="contents"/>, applied in
/// <see cref="ConfigurePopup"/> before the popup's own Start() runs. An empty pool means the prefab's
/// own text is kept. <see cref="SpawnAlert"/> can override either half explicitly for a one-off.
///
/// <b>Several managers per scene.</b> A scene is MEANT to hold more than one of these: the ordinary
/// Win XP alerts come from one, the adware storm (see <c>SecondCastleCapture</c>) from another with
/// its own rate and budget. So this is deliberately NOT the destroy-the-duplicates singleton the
/// other managers use — every manager registers itself and they all keep working side by side.
/// <see cref="Instance"/> is only "the default one", the manager anything that just asks for an alert
/// gets; side managers are taken out of that race with <see cref="isDefaultInstance"/> and are looked
/// up explicitly instead (by reference or by tag).
/// </summary>
public class WinXpAlertManager : PopupManagerBase<WinXPMessageAlert>
{
    /// <summary>
    /// The default alert manager — what everything that just wants "an alert" (capturing events,
    /// cutscenes) spawns through. It is the first registered manager with
    /// <see cref="isDefaultInstance"/> on, falling back to whichever registered first.
    /// </summary>
    public static WinXpAlertManager Instance;

    // Every manager alive in the scene. Not the h.CreateStaticInstance singleton pattern on purpose:
    // that one DESTROYS duplicates, and the regular + adware managers are meant to coexist.
    private static readonly List<WinXpAlertManager> managers = new List<WinXpAlertManager>();

    /// <summary>Every alert manager currently alive (destroyed ones are dropped on read).</summary>
    public static IReadOnlyList<WinXpAlertManager> Managers
    {
        get
        {
            managers.RemoveAll(m => !m);
            return managers;
        }
    }

    // Statics survive a play session when Enter Play Mode Options disable the domain reload, so the
    // registry is cleared on every load rather than filled with entries from the last run.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry()
    {
        managers.Clear();
        Instance = null;
    }

    // ================================================================ Instance

    [Header("Instance")]
    [Tooltip("Let this manager become WinXpAlertManager.Instance — the one anything that just asks for " +
             "an alert (capturing events, cutscenes) spawns through. Turn it OFF on side managers, like " +
             "the adware one, so they can share the scene without stealing the default; those are looked " +
             "up explicitly by reference or by tag instead.")]
    public bool isDefaultInstance = true;

    // ================================================================ Content

    [Header("Alert text")]
    [Tooltip("Pool of window titles. A random one is written into the alert's title TMP whenever the " +
             "caller doesn't pass one. Empty = the prefab's own title is kept.")]
    public List<string> titles = new List<string>();

    [Tooltip("Pool of alert bodies. A random one is written into the alert's message TMP whenever the " +
             "caller doesn't pass one. Empty = the prefab's own body is kept.")]
    [TextArea] public List<string> contents = new List<string>();

    // ================================================================ Setup

    /// <summary>
    /// Registers this manager and, when it is allowed to, claims <see cref="Instance"/>. Duplicates are
    /// never destroyed here: a second manager in the scene is a feature (the adware storm has its own
    /// rate and budget), not an accident.
    /// </summary>
    protected override void RegisterInstance()
    {
        managers.RemoveAll(m => !m);
        if (!managers.Contains(this)) managers.Add(this);

        // A manager that opted out never becomes the default; a manager that opted in takes the slot
        // from a stand-in that had only been holding it as a fallback.
        if (!Instance || (isDefaultInstance && !Instance.isDefaultInstance))
        {
            Instance = this;
        }
        else if (isDefaultInstance && Instance != this)
        {
            h.Out($"WinXpAlertManager: '{name}' and '{Instance.name}' both claim to be the default " +
                  "alert manager — '" + Instance.name + "' keeps it. Turn 'isDefaultInstance' off on " +
                  "the side manager (e.g. the adware one) so the choice isn't left to Awake order.");
        }

        // Only ever valid on a root object, and these managers normally live under a canvas.
        if (dontDestroyOnLoad && !transform.parent) DontDestroyOnLoad(gameObject);
    }

    protected override void UnregisterInstance()
    {
        managers.Remove(this);

        if (Instance != this) return;

        // Hand the default slot to another manager rather than leaving callers with a null Instance.
        managers.RemoveAll(m => !m);
        Instance = null;

        foreach (WinXpAlertManager manager in managers)
        {
            if (!manager.isDefaultInstance) continue;
            Instance = manager;
            return;
        }

        if (managers.Count > 0) Instance = managers[0];
    }

    // ================================================================ Spawning

    /// <summary>
    /// Spawns one popup.
    /// </summary>
    /// <param name="screenPosition">
    /// Where to put it, in screen pixels. Null = a random point on screen, kept
    /// <see cref="PopupManagerBase{TPopup}.screenMargin"/> pixels away from the edges.
    /// </param>
    /// <param name="title">Window title. Null = a random one from <see cref="titles"/>.</param>
    /// <param name="content">Message body. Null = a random one from <see cref="contents"/>.</param>
    /// <param name="sfx">
    /// Popup sound. Null = <see cref="PopupManagerBase{TPopup}.spawnSfx"/>, or the prefab's own list
    /// when that is empty too.
    /// </param>
    /// <returns>The spawned popup, or null when nothing was spawned (no prefabs, or budget spent).</returns>
    public WinXPMessageAlert SpawnAlert(Vector2? screenPosition = null, string title = null,
        string content = null, AudioClip sfx = null)
    {
        // The random text is applied to EVERY popup in ConfigurePopup below, so there is nothing left
        // to do here unless the caller actually named a title or a body.
        if (title == null && content == null) return SpawnPopup(screenPosition, sfx);

        // Runs before the popup's Start(), so it opens already carrying its text — no frame where the
        // prefab's default content is visible. The ?? keeps whatever ConfigurePopup picked for the half
        // the caller did not override.
        return SpawnPopup(screenPosition, sfx,
            alert => alert.SetText(title ?? alert.titleText, content ?? alert.alertText));
    }

    /// <summary>
    /// Gives every fresh alert a random title and body from <see cref="titles"/> /
    /// <see cref="contents"/>.
    ///
    /// <para>This has to live HERE rather than in <see cref="SpawnAlert"/>: the continuous spawn loop
    /// pays out its popups through <see cref="PopupManagerBase{TPopup}.SpawnPopup"/> directly and never
    /// goes through SpawnAlert, so a storm configured with text pools would otherwise show nothing but
    /// the prefab's placeholder text on every window but the first.</para>
    /// </summary>
    protected override void ConfigurePopup(WinXPMessageAlert alert, WinXPMessageAlert prefab)
    {
        if (!alert) return;

        // Both resolvers fall back to the instance's own (i.e. the prefab's) text when the matching
        // pool is empty, so an unconfigured manager leaves its prefabs exactly as they are.
        alert.SetText(ResolveTitle(null, alert), ResolveContent(null, alert));
    }

    /// <summary>Convenience overload for callers that only care about the text.</summary>
    public WinXPMessageAlert SpawnAlert(string title, string content = null, AudioClip sfx = null)
    {
        return SpawnAlert((Vector2?)null, title, content, sfx);
    }

    // The instance is a fresh clone, so its texts are still the prefab's — that is the fallback.
    private string ResolveTitle(string title, WinXPMessageAlert alert)
    {
        if (title != null) return title;
        return (titles != null && titles.Count > 0) ? h.RandChoice(titles) : alert.titleText;
    }

    private string ResolveContent(string content, WinXPMessageAlert alert)
    {
        if (content != null) return content;
        return (contents != null && contents.Count > 0) ? h.RandChoice(contents) : alert.alertText;
    }

    /// <summary>
    /// The alert plays its own sound on Start, so the manager doesn't play anything — it decides what
    /// the popup will play. Emptying the clip list is how an alert is spawned silently, which is what
    /// the base class's sound throttle does at high spawn rates.
    /// </summary>
    protected override void ApplySpawnSfx(WinXPMessageAlert alert, AudioClip sfx)
    {
        if (!MayPlaySpawnSound())
        {
            alert.alertSFX = new List<AudioClip>();
            return;
        }

        if (sfx) alert.alertSFX = new List<AudioClip> { sfx };
        else if (spawnSfx != null && spawnSfx.Count > 0) alert.alertSFX = new List<AudioClip>(spawnSfx);
    }

    /// <summary>Alerts close themselves properly (close sound + destroy).</summary>
    protected override void ClosePopup(WinXPMessageAlert alert)
    {
        if (alert) alert.Close();
    }

    // ================================================================ Backwards-compatible aliases

    /// <summary>Alerts currently alive. Alias of <see cref="PopupManagerBase{TPopup}.SpawnedPopups"/>.</summary>
    public IReadOnlyList<WinXPMessageAlert> SpawnedAlerts => SpawnedPopups;

    /// <summary>Alerts created since the last ResetBudget().</summary>
    public int TotalAlertsSpawned => TotalPopupsSpawned;

    /// <summary>The canvas alerts are spawned on.</summary>
    public Canvas AlertCanvas => PopupCanvas;

    /// <summary>Stops the loop and tears every popup down. Alias of ClearPopups.</summary>
    public void ClearAlerts(bool includeStrays = false) => ClearPopups(includeStrays);
}
