using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The pause / settings window, laid out like the Win XP file explorer: a folder tree on the left, the
/// contents of the opened folder on the right.
///
/// <para><b>How it is set up.</b> The right half is built by hand — one root GameObject per page, all
/// stacked in the same spot and all listed in <see cref="ContentWindows"/>. The left half is built at
/// runtime: one <see cref="folderPfb"/> is spawned under <see cref="foldersParent"/> per content window,
/// named <c>folderIconTags[i] + " " + folderNames[i]</c>. Clicking the i-th folder enables the i-th
/// content window and disables the rest; the clicked folder is drawn selected (blue band, white text)
/// and swaps to <see cref="openedFolderIconTag"/> while it is open.</para>
///
/// <para><b>Icons.</b> They are plain rich-text tags, not a fixed icon set — normally a TMP
/// <c>&lt;sprite&gt;</c> tag naming a sprite in the sprite asset the folder label uses, e.g.
/// <c>&lt;sprite name="folderClosed"&gt;</c>. <see cref="showFolderIcons"/> turns them off wholesale;
/// <see cref="defaultFolderIconTag"/> and <see cref="openedFolderIconTag"/> are what every folder gets,
/// and <see cref="folderIconTags"/> overrides them per folder.</para>
///
/// <para>Every content window starts disabled, so the window opens on an empty right pane unless
/// <see cref="defaultFolderIndex"/> names one to open.</para>
///
/// <para><b>Where to put this component.</b> On a parent of <see cref="SettingsWindow"/>, not on the
/// window itself — the window is disabled while the game is running, and a component on a disabled
/// object never gets its Awake, so it could never listen for Esc to open itself again.</para>
///
/// <code>
/// SettingsWindowManager.Instance.Open();          // pauses the game, shows the window
/// SettingsWindowManager.Instance.OpenFolder(2);   // shows the third content window
/// SettingsWindowManager.Instance.OpenAt(2);       // opens the window standing on that one
/// SettingsWindowManager.Instance.Close();         // unpauses — same as pressing closeBtn
/// </code>
/// </summary>
public class SettingsWindowManager : MonoBehaviour
{
    public static SettingsWindowManager Instance; // Instance

    /// <summary>The tag a folder is given when nothing else says otherwise — XP's closed folder.</summary>
    public const string DefaultIconTag = "<sprite name=\"folderClosed\">";

    /// <summary>The tag a folder swaps to while it is the open one — XP's opened folder.</summary>
    public const string DefaultOpenedIconTag = "<sprite name=\"folderOpened\">";

    // ================================================================ Inspector

    [Header("Window")]
    [Tooltip("Root of the whole window — this is what Esc toggles. Keep THIS component on a parent of " +
             "it, so the manager stays alive while the window is off.")]
    public GameObject SettingsWindow;

    [Tooltip("The left pane: spawned folders are parented here. A VerticalLayoutGroup is added if it " +
             "has none, and its spacing is overwritten by 'folderSpacing'.")]
    public Transform foldersParent;

    [Tooltip("The window's X / Close button. Pressing it hides the window (and unpauses). Optional — " +
             "leave empty for a window that is only closed with Esc or from code.")]
    public Button closeBtn;

    [Header("Folders")]
    [Tooltip("Prefab spawned once per content window. Needs a Folder component.")]
    public Folder folderPfb;

    [Tooltip("Folder labels, one per content window. A missing/empty entry falls back to the content " +
             "window's own object name.")]
    public List<string> folderNames = new List<string>();

    [Tooltip("Draw an icon in front of every folder name. Off = the tree is names only, and the icon " +
             "tags below are ignored — for a label with no sprite asset, where a <sprite> tag would " +
             "otherwise be drawn as a missing-sprite box.")]
    public bool showFolderIcons = true;

    [Tooltip("Icon tag every folder gets. A rich-text tag, normally a TMP <sprite> naming a sprite in " +
             "the sprite asset the folder label uses. Overridden per folder by 'folderIconTags'.")]
    public string defaultFolderIconTag = DefaultIconTag;

    [Tooltip("Tag a folder swaps to while it is the open one. Empty = folders keep their normal icon " +
             "when opened. Only applied to folders left on 'defaultFolderIconTag' — a folder given its " +
             "own tag below has no 'opened' variant to swap to.")]
    public string openedFolderIconTag = DefaultOpenedIconTag;

    [Tooltip("Icon tag override in front of each folder name, one per content window. An empty entry " +
             "falls back to 'defaultFolderIconTag' (and is then the only kind of folder that swaps to " +
             "the opened icon).")]
    public List<string> folderIconTags = new List<string>();

    [Tooltip("Gap between the spawned folders, in UI units. Written onto the foldersParent's layout " +
             "group, so it is tuned here rather than on that component. -1 = leave whatever spacing " +
             "the layout group already has.")]
    public float folderSpacing = 4f;

    [Tooltip("Add a VerticalLayoutGroup to foldersParent when it has none. Without it every folder is " +
             "spawned at the prefab's own position and they all end up stacked on top of each other. " +
             "Turn off only if the folders are placed by something else.")]
    public bool ensureFoldersLayout = true;

    [Tooltip("Row height forced on each spawned folder. -1 = keep the prefab's own height. Only used " +
             "when this component creates the layout group.")]
    public float folderHeight = -1f;

    [Header("Content")]
    [Tooltip("The pages of the right pane, in folder order. All of them are disabled on build and on " +
             "close — exactly one is on while a folder is open.")]
    public List<GameObject> ContentWindows = new List<GameObject>();

    [Header("Behaviour")]
    [Tooltip("Folder opened when the window is shown. -1 = none, so the right pane starts empty like a " +
             "freshly opened explorer with nothing selected.")]
    public int defaultFolderIndex = -1;

    [Tooltip("Re-apply 'defaultFolderIndex' every time the window opens. Off = the window reopens on " +
             "whatever folder was last looked at.")]
    public bool resetOnOpen = true;

    [Tooltip("Put GameFlowManager into Pause while the window is open, and back to the state it was in " +
             "when it closes. A game already paused by something else (a cutscene) is left alone — that " +
             "something else keeps the unpause.")]
    public bool pauseGameWhileOpen = true;

    [Tooltip("Hide the window on Start. Leave on unless the game is meant to boot into the settings.")]
    public bool closedOnStart = true;

    [Header("Input")]
    [Tooltip("Action that toggles the window. Empty = Esc, bound in code. Point it at UI/Cancel to let " +
             "the player rebind it.")]
    public InputActionReference toggleAction;

    [Tooltip("Let the toggle action close the window as well as open it.")]
    public bool toggleCloses = true;

    // ================================================================ State

    /// <summary>Index of the opened folder, or -1 when no folder is open.</summary>
    public int OpenedIndex { get; private set; } = -1;

    /// <summary>True while the settings window is showing.</summary>
    public bool IsOpen => SettingsWindow && SettingsWindow.activeSelf;

    /// <summary>The spawned folders, in the order of <see cref="ContentWindows"/>.</summary>
    public IReadOnlyList<Folder> Folders => folders;

    /// <summary>Raised with the index of the folder that was opened, or -1 when they were all closed.</summary>
    public event System.Action<int> OnFolderOpened;

    /// <summary>Raised with the new visibility whenever the window is shown or hidden.</summary>
    public event System.Action<bool> OnWindowToggled;

    private readonly List<Folder> folders = new List<Folder>();

    // True while THIS window is the thing holding the game on pause. Without it, closing the window
    // would unpause a game that a cutscene — not the window — had stopped.
    private bool pausedByWindow;

    // Only used when 'toggleAction' is empty: an Esc binding made here so the window works out of the box.
    private InputAction fallbackToggle;

    // ================================================================ Lifecycle

    private void Awake()
    {
        // Not DontDestroyOnLoad: this lives under a canvas, and that call only works on a root object.
        h.CreateStaticInstance(this, ref Instance, false);
        if (Instance != this) return;

        // In Awake, not OnEnable: the close button lives INSIDE the window, so it is disabled together
        // with it and an OnEnable hookup would never run while the window is hidden.
        if (closeBtn) closeBtn.onClick.AddListener(Close);

        Build();
    }

    private void Start()
    {
        if (closedOnStart) SetWindowActive(false);
    }

    private void OnEnable()
    {
        // The window survives this component being switched off, so a manager that comes back while the
        // window is still showing has to take the pause again.
        if (IsOpen) ApplyPause(true);

        if (toggleAction && toggleAction.action != null)
        {
            toggleAction.action.performed += HandleToggleInput;
            toggleAction.action.Enable();
            return;
        }

        fallbackToggle ??= new InputAction($"{name}_ToggleSettings", InputActionType.Button, "<Keyboard>/escape");
        fallbackToggle.performed += HandleToggleInput;
        fallbackToggle.Enable();
    }

    private void OnDisable()
    {
        // Esc is what closes the window, and it stops working the moment this component is off — so hand
        // the pause back here rather than leaving the game frozen with no way to unfreeze it.
        ApplyPause(false);

        if (toggleAction && toggleAction.action != null) toggleAction.action.performed -= HandleToggleInput;

        if (fallbackToggle == null) return;

        fallbackToggle.performed -= HandleToggleInput;
        fallbackToggle.Disable();
    }

    private void OnDestroy()
    {
        ApplyPause(false);

        if (closeBtn) closeBtn.onClick.RemoveListener(Close);

        fallbackToggle?.Dispose();
        fallbackToggle = null;
    }

    private void HandleToggleInput(InputAction.CallbackContext ctx)
    {
        if (IsOpen && !toggleCloses) return;

        Toggle();
    }

    // ================================================================ Building the folder tree

    /// <summary>
    /// Spawns one folder per content window under <see cref="foldersParent"/>, wipes any folders left
    /// from a previous build, and leaves every content window disabled. Safe to call again after the
    /// lists change.
    /// </summary>
    [ContextMenu("Rebuild folders")]
    public void Build()
    {
        ClearFolders();

        if (!foldersParent)
        {
            h.Out($"{name}: SettingsWindowManager has no foldersParent — no folders were built");
            return;
        }

        if (!folderPfb)
        {
            h.Out($"{name}: SettingsWindowManager has no folderPfb — no folders were built");
            return;
        }

        for (int i = 0; i < ContentWindows.Count; i++)
        {
            Folder folder = Instantiate(folderPfb, foldersParent);
            folder.gameObject.SetActive(true);
            folder.Setup(i, NameFor(i), IconFor(i), OpenedIconFor(i), HandleFolderClicked);

            folders.Add(folder);
        }

        ApplyFolderSpacing();

        CloseAllFolders();
    }

    /// <summary>
    /// Stacks the folders and puts <see cref="folderSpacing"/> between them.
    ///
    /// <para>The folders are spawned from one prefab, so without a layout group on
    /// <see cref="foldersParent"/> they all land on the prefab's own anchored position — one on top of
    /// the other. When there is no layout group at all this adds a <see cref="VerticalLayoutGroup"/>
    /// rather than only warning, since a stack of unreadable folders is never what was wanted.</para>
    ///
    /// <para>Re-run on every build, so swapping the prefab or the parent cannot quietly bring back the
    /// layout group's own spacing. A grid keeps its column gap and only has the row gap set.</para>
    /// </summary>
    /// <param name="allowCreate">Off from OnValidate: adding a component there is not allowed by Unity,
    /// so the inspector only ever retunes a layout group that already exists.</param>
    public void ApplyFolderSpacing(bool allowCreate = true)
    {
        if (!foldersParent) return;

        if (TrySetSpacingOnExistingLayout())
        {
            if (allowCreate) ApplyFolderHeights();
            return;
        }

        if (!allowCreate) return;

        if (!ensureFoldersLayout)
        {
            h.Out($"{name}: SettingsWindowManager's foldersParent has no layout group — the folders " +
                  "will all sit on top of each other");

            return;
        }

        VerticalLayoutGroup added = foldersParent.gameObject.AddComponent<VerticalLayoutGroup>();

        added.spacing = Mathf.Max(0f, folderSpacing);
        added.childAlignment = TextAnchor.UpperLeft;

        // Width is stretched so the selection band spans the pane; height is left to the folder itself,
        // so a prefab sized like a text row keeps that row height instead of being stretched to fill.
        added.childControlWidth = true;
        added.childForceExpandWidth = true;
        added.childControlHeight = false;
        added.childForceExpandHeight = false;

        ApplyFolderHeights();
    }

    // Writes the spacing onto whatever layout group foldersParent already has, and says whether it found
    // one. Safe from OnValidate: it only touches serialized values, it never adds a component.
    private bool TrySetSpacingOnExistingLayout()
    {
        // Covers both VerticalLayoutGroup and HorizontalLayoutGroup — they share this base.
        if (foldersParent.TryGetComponent(out HorizontalOrVerticalLayoutGroup layout))
        {
            if (folderSpacing >= 0f) layout.spacing = folderSpacing;
            return true;
        }

        if (foldersParent.TryGetComponent(out GridLayoutGroup grid))
        {
            if (folderSpacing >= 0f) grid.spacing = new Vector2(grid.spacing.x, folderSpacing);
            return true;
        }

        return false;
    }

    // Only does something when folderHeight is set: a fixed row height, which is what makes the gap
    // between folders read as a gap rather than as uneven text rows.
    private void ApplyFolderHeights()
    {
        if (folderHeight < 0f) return;

        foreach (Folder folder in folders)
        {
            if (!folder) continue;

            if (!folder.TryGetComponent(out LayoutElement element))
            {
                element = folder.gameObject.AddComponent<LayoutElement>();
            }

            element.minHeight = folderHeight;
            element.preferredHeight = folderHeight;
        }
    }

    // Destroys the folders this built. A folderPfb that happens to live under foldersParent as a scene
    // template is left alone, so a rebuild does not eat the prefab it builds from.
    private void ClearFolders()
    {
        folders.Clear();

        if (!foldersParent) return;

        foreach (Folder folder in foldersParent.GetComponentsInChildren<Folder>(true))
        {
            if (!folder || (folderPfb && folder == folderPfb)) continue;

            if (Application.isPlaying) Destroy(folder.gameObject);
            else DestroyImmediate(folder.gameObject);
        }
    }

    /// <summary>The i-th folder's label: its <see cref="folderNames"/> entry, or the content window's name.</summary>
    public string NameFor(int index)
    {
        if (folderNames != null && index < folderNames.Count && !string.IsNullOrWhiteSpace(folderNames[index]))
        {
            return folderNames[index];
        }

        GameObject content = (index < ContentWindows.Count) ? ContentWindows[index] : null;
        return content ? content.name : $"Folder {index}";
    }

    /// <summary>
    /// The i-th folder's icon tag: its <see cref="folderIconTags"/> entry, or
    /// <see cref="defaultFolderIconTag"/>. Empty while <see cref="showFolderIcons"/> is off, which is
    /// what draws that folder as a bare name.
    /// </summary>
    public string IconFor(int index)
    {
        if (!showFolderIcons) return string.Empty;

        if (folderIconTags != null && index < folderIconTags.Count &&
            !string.IsNullOrWhiteSpace(folderIconTags[index]))
        {
            return folderIconTags[index];
        }

        return defaultFolderIconTag;
    }

    /// <summary>
    /// The tag the i-th folder swaps to while it is open, or empty for one that must keep its normal
    /// icon: a folder given its own <see cref="folderIconTags"/> entry (an audio icon has no opened
    /// variant), and every folder while <see cref="showFolderIcons"/> is off.
    /// </summary>
    public string OpenedIconFor(int index)
    {
        if (!showFolderIcons) return string.Empty;

        if (folderIconTags != null && index < folderIconTags.Count &&
            !string.IsNullOrWhiteSpace(folderIconTags[index]))
        {
            return string.Empty;
        }

        return openedFolderIconTag;
    }

    // ================================================================ Opening folders

    private void HandleFolderClicked(Folder folder)
    {
        if (!folder) return;

        OpenFolder(folder.Index);
    }

    /// <summary>
    /// Shows the i-th content window and hides the others, selecting the matching folder. An index
    /// outside the list closes everything, so <c>OpenFolder(-1)</c> is the "nothing selected" state.
    /// </summary>
    public void OpenFolder(int index)
    {
        if (index < 0 || index >= ContentWindows.Count)
        {
            CloseAllFolders();
            return;
        }

        for (int i = 0; i < ContentWindows.Count; i++)
        {
            if (ContentWindows[i]) ContentWindows[i].SetActive(i == index);
        }

        // Selection drives both the blue band and the closed→opened icon swap, so it is set on ALL of
        // them, not only on the one that was clicked.
        for (int i = 0; i < folders.Count; i++)
        {
            if (folders[i]) folders[i].SetSelected(i == index);
        }

        OpenedIndex = index;
        OnFolderOpened?.Invoke(index);
    }

    /// <summary>
    /// Index of the content window <paramref name="content"/> IS, or lives under. -1 when it belongs to
    /// no page.
    /// </summary>
    public int IndexOfContent(GameObject content)
    {
        if (!content) return -1;

        for (int i = 0; i < ContentWindows.Count; i++)
        {
            GameObject page = ContentWindows[i];
            if (!page) continue;

            if (page == content || content.transform.IsChildOf(page.transform)) return i;
        }

        return -1;
    }

    /// <summary>
    /// Index of the first content window that holds a <typeparamref name="T"/>, itself or in its
    /// children. -1 when no page has one.
    ///
    /// <para>How a caller asks for a page by what is ON it rather than by a number — an index typed into
    /// an inspector somewhere else silently points at the wrong folder the day a page is reordered.
    /// Disabled children count: every page but the open one is switched off.</para>
    /// </summary>
    public int IndexOfContentWith<T>() where T : Component
    {
        for (int i = 0; i < ContentWindows.Count; i++)
        {
            GameObject page = ContentWindows[i];
            if (!page) continue;

            if (page.GetComponentInChildren<T>(true)) return i;
        }

        return -1;
    }

    /// <summary>Hides every content window and deselects every folder.</summary>
    public void CloseAllFolders()
    {
        foreach (GameObject content in ContentWindows)
        {
            if (content) content.SetActive(false);
        }

        foreach (Folder folder in folders)
        {
            if (folder) folder.SetSelected(false);
        }

        if (LicenceWindowManager.Instance)
        {
            LicenceWindowManager.Instance.DestroySpawnedAlerts();
        }

        OpenedIndex = -1;
        OnFolderOpened?.Invoke(-1);
    }

    // ================================================================ Opening the window

    /// <summary>Shows the window (Esc, or a pause button).</summary>
    public void Open()
    {
        if (IsOpen) return;

        if (MediaPlayerManager.Instance) MediaPlayerManager.Instance.Close();

        SetWindowActive(true);

        if (resetOnOpen) OpenFolder(defaultFolderIndex);
    }

    /// <summary>
    /// Shows the window standing on a named folder, whatever <see cref="defaultFolderIndex"/> says — for
    /// a caller that opens the settings TO something ("load a save"), not just opens the settings.
    ///
    /// <para>The folder is applied after the window is shown, so it also lands on an already-open
    /// window, and it is applied last, so <see cref="resetOnOpen"/> cannot overwrite it. A separate name
    /// rather than an overload of <see cref="Open"/>: an inspector-wired UnityEvent picks its method by
    /// name, and two Opens in that dropdown is how a button ends up on the wrong one.</para>
    /// </summary>
    public void OpenAt(int folderIndex)
    {
        Open();
        OpenFolder(folderIndex);
    }

    /// <summary>Hides the window and hands the game back.</summary>
    public void Close()
    {
        if (!IsOpen) return;

        SetWindowActive(false);
    }

    /// <summary>Opens the window when it is closed and closes it when it is open.</summary>
    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    private void SetWindowActive(bool active)
    {
        if (!SettingsWindow)
        {
            h.Out($"{name}: SettingsWindowManager has no SettingsWindow to show");
            return;
        }

        SettingsWindow.SetActive(active);

        ApplyPause(active);

        if (!active && LicenceWindowManager.Instance)
        {
            LicenceWindowManager.Instance.DestroySpawnedAlerts();
        }

        OnWindowToggled?.Invoke(active);
    }

    /// <summary>
    /// Puts <see cref="GameFlowManager"/> on pause while the window is up and hands the game back when it
    /// goes away.
    ///
    /// <para>Both halves are guarded by <see cref="pausedByWindow"/>, so the window only ever undoes its
    /// OWN pause. That matters in two places: the hide that happens on <see cref="Start"/> would
    /// otherwise call the game into <see cref="GameFlowManager.State.Game"/> before it has begun, and a
    /// settings window opened on top of a cutscene (which paused the game itself) would resume the game
    /// under the cutscene the moment it was closed.</para>
    ///
    /// <para>Unpausing goes through <see cref="GameFlowManager.Resume"/> rather than SetOnGame, so the
    /// game returns to the state it was actually in — and a loss or a finale that landed while the window
    /// was open is left standing.</para>
    /// </summary>
    private void ApplyPause(bool paused)
    {
        if (!GameFlowManager.Instance) return;

        if (paused)
        {
            if (!pauseGameWhileOpen || pausedByWindow) return;

            // Already stopped by something else: let whatever did that own the unpause.
            if (GameFlowManager.Instance.IsPaused()) return;

            GameFlowManager.Instance.Pause();
            pausedByWindow = true;

            return;
        }

        if (!pausedByWindow) return;

        pausedByWindow = false;
        GameFlowManager.Instance.Resume();
    }

    // ================================================================ Editor

#if UNITY_EDITOR
    // Keeps the three parallel lists the same length in the inspector, so adding a content window adds
    // a name and an icon slot too. Both start empty rather than pre-filled, since an empty entry is the
    // "use the default" case and a folder is normally left on it.
    private void OnValidate()
    {
        folderNames ??= new List<string>();
        folderIconTags ??= new List<string>();

        while (folderNames.Count < ContentWindows.Count) folderNames.Add(string.Empty);
        while (folderIconTags.Count < ContentWindows.Count) folderIconTags.Add(string.Empty);

        if (defaultFolderIndex < -1) defaultFolderIndex = -1;
        if (defaultFolderIndex >= ContentWindows.Count) defaultFolderIndex = ContentWindows.Count - 1;

        if (folderSpacing < 0f) folderSpacing = -1f; // any negative means "don't touch the layout group"
        if (folderHeight < 0f) folderHeight = -1f;   // …and "keep the prefab's own height"

        // So the gap can be dialled in from the inspector without entering play mode. Nothing is created
        // from here — Unity forbids AddComponent during OnValidate.
        ApplyFolderSpacing(false);
    }
#endif
}
