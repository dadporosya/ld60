using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Closes the game — and, by default, asks first.
///
/// <para><b>The question.</b> <see cref="CloseGame"/> does not quit: it puts a
/// <see cref="WinXpChooseWindow"/> on screen ("Turn Off The Computer" / "Are you Sure?") and only the
/// Yes button goes on to the real shutdown. Everything already wired to CloseGame (the desktop's power
/// button, the restart window) therefore gets the confirmation for free. Callers that must not be
/// questioned — a cutscene ending the game, a debug key — call <see cref="CloseGameConfirmed"/> (SFX,
/// then quit) or <see cref="CloseGameImmediately"/> (quit now).</para>
///
/// <para>The prefab is <see cref="confirmPopupPfb"/>: any WinXpChooseWindow with at least TWO choice
/// buttons (listed in its <c>choiceButtons</c>, or sitting under its <c>buttonParent</c>) — the first
/// becomes Yes, the second No. With no prefab assigned the manager falls straight through to the
/// shutdown, so a scene that never had one behaves exactly as it did before.</para>
///
/// <para>Closing the window by its X (or anything else destroying it) counts as No: only choice 0 ever
/// quits.</para>
/// </summary>
public class ShutdownManager : MonoBehaviour
{
    public static ShutdownManager Instance;

    // ================================================================ Confirmation

    [Header("Confirmation")]
    [Tooltip("The Yes / No window CloseGame() asks with. Needs a WinXpChooseWindow with at least TWO " +
             "choice buttons (or a buttonParent holding them) — the first becomes Yes, the second No. " +
             "Empty = no question is asked and the game closes straight away.")]
    public WinXpChooseWindow confirmPopupPfb;

    [Tooltip("Ask before closing. Off = CloseGame() shuts the game down without a question, the same as " +
             "calling CloseGameConfirmed() directly.")]
    public bool askBeforeClosing = true;

    [Tooltip("Title of the confirmation window.")]
    public string confirmTitle = "Turn Off The Computer";

    [Tooltip("Body of the confirmation window — the actual question.")]
    [TextArea] public string confirmMessage = "Are you Sure?";

    [Tooltip("Label of the choice that closes the game.")]
    public string yesLabel = "Yes";

    [Tooltip("Label of the choice that only dismisses the window.")]
    public string noLabel = "No";

    [Tooltip("Where the window is spawned. Empty = the object tagged below, or the root canvas in the " +
             "scene. This manager survives scene loads, so a canvas dragged in here does not — leave it " +
             "empty unless the manager lives in the scene it spawns into.")]
    public RectTransform popupParent;

    [Tooltip("Tag of the container the window goes inside, used when 'popup parent' is empty. Leave " +
             "empty to skip the lookup and go straight to the canvas root.")]
    public string popupParentTag = "AlertParent";

    [Tooltip("Where the window lands inside its parent, in UI units from the centre.")]
    public Vector2 popupOffset = Vector2.zero;

    [Tooltip("Sound played when the confirmation opens — one clip is picked at random. This REPLACES " +
             "the prefab's own alert sounds, the same way WinXpAlertManager hands its alerts a clip. " +
             "Empty = the prefab keeps whatever it plays by itself.")]
    public List<AudioClip> confirmSFX = new List<AudioClip>();

    [Tooltip("Silence the confirmation's spawn sound entirely, prefab list included. Beats the clips " +
             "above — for a shutdown that is meant to creep up quietly.")]
    public bool silentConfirm = false;

    // ================================================================ Shutdown SFX

    [Header("Shutdown SFX")]
    [Tooltip("Clip played before the game closes. If null, the game closes immediately.")]
    public AudioClip shutdownSFX;

    [Tooltip("Volume for the shutdown clip. If < 0, the SFXManager default is used.")]
    public float shutdownSFXVolume = -1f;

    [Tooltip("Extra delay (seconds) after the clip finishes, before quitting.")]
    public float extraDelayAfterSFX = 0f;

    // ================================================================ Runtime state

    private bool isClosing;

    /// <summary>The confirmation currently on screen, so a second CloseGame() cannot stack another.</summary>
    private WinXpChooseWindow openConfirm;

    /// <summary>The confirmation currently on screen, or null.</summary>
    public WinXpChooseWindow OpenConfirmation => openConfirm;

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance);
    }

    // ================================================================ Public API

    /// <summary>
    /// Asks "Are you sure?" and closes the game if the player says yes. With no
    /// <see cref="confirmPopupPfb"/> assigned (or <see cref="askBeforeClosing"/> off) it closes right
    /// away, so this stays safe to wire to a button in any scene.
    /// </summary>
    public void CloseGame()
    {
        if (isClosing) return;

        if (!askBeforeClosing || !confirmPopupPfb)
        {
            CloseGameConfirmed();
            return;
        }

        AskBeforeClosing();
    }

    /// <summary>
    /// Puts the Yes / No window on screen. Yes runs <see cref="CloseGameConfirmed"/>, No only closes the
    /// window — and so does the X, since a dismissed question is not a yes.
    /// </summary>
    /// <returns>The spawned window, the one already open, or null when it could not be spawned.</returns>
    public WinXpChooseWindow AskBeforeClosing()
    {
        if (isClosing) return null;

        // A second copy of the same question is how a player ends up pressing Yes on a window they
        // never read.
        if (openConfirm)
        {
            h.Out("ShutdownManager: the shutdown confirmation is already open.");
            return openConfirm;
        }

        if (!confirmPopupPfb)
        {
            h.Out("ShutdownManager: no confirmPopupPfb assigned — nothing to ask with.");
            return null;
        }

        RectTransform parent = ResolvePopupParent();
        if (!parent)
        {
            h.Out("ShutdownManager: no canvas to spawn the confirmation on — closing without asking.");
            CloseGameConfirmed();
            return null;
        }

        WinXpChooseWindow window = Instantiate(confirmPopupPfb, parent);

        // All of this runs before the window's own Start(), so it opens already carrying its text and
        // its choices instead of showing the prefab's placeholders for a frame.
        window.SetText(confirmTitle, confirmMessage);
        window.SetChoices(
            (yesLabel, CloseGameConfirmed),
            (noLabel, null)); // No is the window closing, which closeOnChoice already does

        ApplyConfirmSFX(window);

        if (window.transform is RectTransform rt) rt.anchoredPosition = popupOffset;

        openConfirm = window;
        window.OnClosed += HandleConfirmClosed;

        return window;
    }

    /// <summary>
    /// Decides what the confirmation plays as it opens. The window plays its own sound on Start (see
    /// <see cref="WinXPMessageAlert.alertSFX"/>), so this only fills that list in — set before Start, so
    /// the sound lands on the frame the window appears rather than one after it.
    /// </summary>
    private void ApplyConfirmSFX(WinXpChooseWindow window)
    {
        if (!window) return;

        if (silentConfirm)
        {
            window.alertSFX = new List<AudioClip>();
            return;
        }

        // Empty list = the prefab's own clips are left exactly as they are.
        if (confirmSFX == null || confirmSFX.Count == 0) return;

        window.alertSFX = new List<AudioClip>(confirmSFX);
    }

    /// <summary>Closes the open confirmation without answering it. The game keeps running.</summary>
    public void CancelClose()
    {
        if (!openConfirm) return;

        WinXpChooseWindow window = openConfirm;
        openConfirm = null;
        window.OnClosed -= HandleConfirmClosed;

        window.CloseSilently();
    }

    /// <summary>
    /// Plays <see cref="shutdownSFX"/> (if set) and closes the game once it has finished. This is the
    /// shutdown itself — it does NOT ask, so it is what the Yes button and anything that has already
    /// made the decision calls.
    /// </summary>
    public void CloseGameConfirmed()
    {
        if (isClosing) return;
        isClosing = true;

        // The window is on its way out anyway when Yes was pressed; this covers the other callers.
        DropConfirmation();

        if (shutdownSFX == null || SFXManager.Instance == null)
        {
            Quit();
            return;
        }

        StartCoroutine(CloseGameAfterSFX());
    }

    /// <summary>
    /// Closes the game immediately, skipping both the question and the shutdown SFX.
    /// </summary>
    public void CloseGameImmediately()
    {
        isClosing = true;
        DropConfirmation();
        Quit();
    }

    // ================================================================ Internals

    private void HandleConfirmClosed(WinXPMessageAlert popup)
    {
        popup.OnClosed -= HandleConfirmClosed;

        if (openConfirm == popup) openConfirm = null;

        // Nothing else to do: only choice 0 quits, and it does so through its own callback. A window
        // that went away any other way (No, the X, a scene change) is a "no".
    }

    /// <summary>Forgets the open window without closing it — used when the shutdown has already started.</summary>
    private void DropConfirmation()
    {
        if (!openConfirm) return;

        openConfirm.OnClosed -= HandleConfirmClosed;
        openConfirm = null;
    }

    /// <summary>
    /// Where the confirmation is spawned: the explicit parent, then the object tagged
    /// <see cref="popupParentTag"/>, then the root canvas. Resolved on every ask rather than cached —
    /// this manager outlives the scene its canvas belongs to.
    /// </summary>
    private RectTransform ResolvePopupParent()
    {
        if (popupParent) return popupParent;

        RectTransform tagged = FindTaggedRectTransform(popupParentTag);
        if (tagged) return tagged;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (!canvas) canvas = FindFirstObjectByType<Canvas>();
        if (!canvas) return null;

        canvas = canvas.rootCanvas ? canvas.rootCanvas : canvas;
        return canvas.transform as RectTransform;
    }

    /// <summary>
    /// First UI object carrying <paramref name="tag"/>, inactive ones included — the container may well
    /// start switched off. An undefined tag is not an error: the lookup is simply skipped.
    /// </summary>
    private RectTransform FindTaggedRectTransform(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return null;

        Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (Transform t in all)
        {
            if (!t || !t.gameObject.scene.IsValid()) continue;

            bool matches;
            try
            {
                matches = t.CompareTag(tag);
            }
            catch (UnityException)
            {
                // The tag isn't defined in this project — asking every transform about it would throw
                // once per object, so stop after the first.
                h.Out($"ShutdownManager: the tag '{tag}' is not defined — spawning on the canvas root.");
                return null;
            }

            if (!matches) continue;

            RectTransform rt = t as RectTransform;
            if (rt) return rt;
        }

        return null;
    }

    private IEnumerator CloseGameAfterSFX()
    {
        h.Out("Playing shutdown SFX");

        float? volume = shutdownSFXVolume >= 0f ? shutdownSFXVolume : (float?)null;
        AudioSource source = SFXManager.Instance.PlayClip(shutdownSFX, volume);

        float pitch = source ? Mathf.Max(Mathf.Abs(source.pitch), 0.01f) : 1f;
        float waitTime = shutdownSFX.length / pitch + extraDelayAfterSFX;

        // Realtime: the game may be paused (timeScale == 0) when shutting down.
        yield return new WaitForSecondsRealtime(waitTime);

        Quit();
    }

    private void Quit()
    {
        h.Out("Closing game");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
