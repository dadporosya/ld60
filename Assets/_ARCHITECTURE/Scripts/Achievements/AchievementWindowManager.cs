using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls the achievements window inside the settings menu.
/// Populates a scrollable list of achievement items using the <see cref="AchievementAlert"/> prefab
/// for all authored or tracked achievements.
/// </summary>
public class AchievementWindowManager : MonoBehaviour
{
    [Header("Prefabs & UI Elements")]
    [Tooltip("Prefab instantiated for each achievement entry in the list.")]
    public AchievementAlert achievementAlertPfb;

    [Tooltip("The parent transform (Content rect of the ScrollRect) where achievement items will be spawned.")]
    public Transform listContainer;

    [Tooltip("The scroll rect component holding the achievement list.")]
    public ScrollRect scrollRect;

    [Tooltip("Optional scrollbar component.")]
    public Scrollbar scrollbar;

    [Header("Layout Settings")]
    [Tooltip("Automatically ensures VerticalLayoutGroup and ContentSizeFitter exist on listContainer.")]
    public bool ensureLayoutGroup = true;

    [Tooltip("Vertical spacing between achievement entries in UI units.")]
    public float itemSpacing = 6f;

    [Tooltip("Padding around the achievement entries list.")]
    public RectOffset listPadding;

    [Tooltip("Forced height for each achievement item row. -1 = auto-detect from prefab height.")]
    public float itemHeight = -1f;

    [Header("Data")]
    [Tooltip("Specific achievements to show. If empty, automatically pulls from AchievementManager.Instance.allAchievements.")]
    public List<AchievementData> achievements = new List<AchievementData>();

    [Tooltip("If true and 'achievements' list is empty, automatically pulls from AchievementManager.Instance.allAchievements.")]
    public bool useManagerAchievementsIfEmpty = true;

    [Header("Behaviour")]
    [Tooltip("Automatically refreshes/rebuilds the list when this window/tab is enabled.")]
    public bool refreshOnEnable = true;

    [Tooltip("Hides or disables close buttons on spawned list items so clicking them does not delete rows.")]
    public bool disableCloseButtonsOnListItems = true;

    [Header("Drag Cursor")]
    [Tooltip("Changes cursor icon to drag icon while the player is dragging the scrollbar.")]
    public bool changeCursorOnScrollbarDrag = true;

    [Tooltip("Changes cursor icon to drag icon while the player is dragging the viewport/content.")]
    public bool changeCursorOnContentDrag = true;

    [Header("Locked Look")]
    [Tooltip("Applies distinct visual styling (dimming, locked text) to locked achievements.")]
    public bool styleLockedAchievements = true;

    [Tooltip("Tint color applied to the icon when the achievement is locked.")]
    public Color lockedIconColor = new Color(0.35f, 0.35f, 0.35f, 0.6f);

    [Tooltip("Tint color applied to the icon when the achievement is unlocked.")]
    public Color unlockedIconColor = Color.white;

    [Tooltip("Alpha transparency applied to text components when the achievement is locked / ungranted.")]
    [Range(0f, 1f)] public float lockedTextAlpha = 0.45f;

    [Tooltip("Alpha transparency applied to text components when the achievement is unlocked / granted.")]
    [Range(0f, 1f)] public float unlockedTextAlpha = 1f;

    [Tooltip("Hide the description of locked achievements and show placeholder instead.")]
    public bool hideLockedDescription = false;

    [Tooltip("Placeholder text shown when a locked achievement description is hidden.")]
    public string lockedDescriptionPlaceholder = "???";

    private readonly List<AchievementAlert> spawnedItems = new List<AchievementAlert>();

    private void Awake()
    {
        listPadding ??= new RectOffset(6, 6, 6, 6);
        ResolveRefs();
        SetupDragCursorHooks();
    }

    private void OnEnable()
    {
        SetupDragCursorHooks();
        if (refreshOnEnable)
        {
            PopulateList();
        }
    }

    private void OnDisable()
    {
        CleanupDragCursorHooks();
    }

    /// <summary>
    /// Auto-resolves missing ScrollRect, Viewport, and list container references and configurations.
    /// </summary>
    public void ResolveRefs()
    {
        if (!scrollRect)
        {
            scrollRect = GetComponentInChildren<ScrollRect>(true);
            if (!scrollRect && transform.parent)
            {
                scrollRect = transform.parent.GetComponentInChildren<ScrollRect>(true);
            }
            if (!scrollRect)
            {
                scrollRect = FindFirstObjectByType<ScrollRect>();
            }
        }

        // Self-heal Viewport if its anchors or size collapsed to 0
        if (scrollRect && scrollRect.viewport)
        {
            RectTransform vp = scrollRect.viewport;
            if (vp.anchorMax == Vector2.zero || (vp.rect.width <= 0f && vp.rect.height <= 0f))
            {
                vp.anchorMin = Vector2.zero;
                vp.anchorMax = Vector2.one;
                vp.anchoredPosition = Vector2.zero;
                vp.sizeDelta = Vector2.zero;
                vp.pivot = new Vector2(0f, 1f);
            }
        }

        // Ensure listContainer points to scrollRect.content if missing or pointing to the root window
        if (!listContainer || listContainer == transform || (transform.parent && listContainer == transform.parent))
        {
            if (scrollRect && scrollRect.content)
            {
                listContainer = scrollRect.content;
            }
        }

        // Configure content RectTransform anchors for proper top-to-bottom scrolling
        if (listContainer is RectTransform crt)
        {
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = Vector2.zero;
        }

        if (!scrollbar && scrollRect)
        {
            scrollbar = scrollRect.verticalScrollbar ? scrollRect.verticalScrollbar : scrollRect.horizontalScrollbar;
        }

        EnsureLayoutComponents();
        SetupDragCursorHooks();
    }

    /// <summary>
    /// Configures or adds VerticalLayoutGroup and ContentSizeFitter to the list container.
    /// </summary>
    public void EnsureLayoutComponents()
    {
        if (!ensureLayoutGroup || !listContainer) return;

        if (!listContainer.TryGetComponent(out VerticalLayoutGroup vlg))
        {
            vlg = listContainer.gameObject.AddComponent<VerticalLayoutGroup>();
        }

        vlg.spacing = itemSpacing;
        if (listPadding != null)
        {
            vlg.padding = new RectOffset(listPadding.left, listPadding.right, listPadding.top, listPadding.bottom);
        }
        else
        {
            vlg.padding = new RectOffset(6, 6, 6, 6);
        }
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        if (!listContainer.TryGetComponent(out ContentSizeFitter csf))
        {
            csf = listContainer.gameObject.AddComponent<ContentSizeFitter>();
        }
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
    }

    /// <summary>
    /// Populates the scrollable list with an <see cref="AchievementAlert"/> for each achievement.
    /// </summary>
    [ContextMenu("Populate List")]
    public void PopulateList()
    {
        ClearList();
        ResolveRefs();

        if (!listContainer)
        {
            h.Out($"{name}: AchievementWindowManager has no listContainer assigned.");
            return;
        }

        if (!achievementAlertPfb)
        {
            h.Out($"{name}: AchievementWindowManager has no achievementAlertPfb assigned.");
            return;
        }

        List<AchievementData> listToDisplay = GetAchievementsToDisplay();

        if (listToDisplay == null || listToDisplay.Count == 0)
        {
            h.Out($"{name}: AchievementWindowManager found 0 achievements to display.");
            return;
        }

        float defaultHeight = 120f;
        if (achievementAlertPfb.transform is RectTransform pfbRt && pfbRt.rect.height > 0f)
        {
            defaultHeight = pfbRt.rect.height;
        }
        float targetHeight = itemHeight > 0f ? itemHeight : defaultHeight;

        foreach (AchievementData data in listToDisplay)
        {
            if (!data) continue;

            AchievementAlert item = Instantiate(achievementAlertPfb, listContainer, false);
            item.transform.localPosition = Vector3.zero;
            item.transform.localScale = Vector3.one;
            item.transform.localRotation = Quaternion.identity;

            if (!item.TryGetComponent(out LayoutElement le))
            {
                le = item.gameObject.AddComponent<LayoutElement>();
            }
            le.minHeight = targetHeight;
            le.preferredHeight = targetHeight;

            item.gameObject.SetActive(true);
            item.InitFromData(data);

            if (disableCloseButtonsOnListItems)
            {
                item.SetCloseButtonsActive(false);
            }

            if (styleLockedAchievements)
            {
                ApplyLockedStyling(item, data);
            }

            spawnedItems.Add(item);
        }

        // Force layout update and reset scroll position to top
        if (listContainer is RectTransform contentRect)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
        }

        if (scrollRect)
        {
            scrollRect.verticalNormalizedPosition = 1f;
        }
    }

    /// <summary>
    /// Clears and destroys all previously spawned achievement items.
    /// </summary>
    [ContextMenu("Clear List")]
    public void ClearList()
    {
        foreach (AchievementAlert item in spawnedItems)
        {
            if (item)
            {
                if (Application.isPlaying) Destroy(item.gameObject);
                else DestroyImmediate(item.gameObject);
            }
        }
        spawnedItems.Clear();

        if (listContainer)
        {
            foreach (AchievementAlert leftover in listContainer.GetComponentsInChildren<AchievementAlert>(true))
            {
                if (leftover && leftover != achievementAlertPfb)
                {
                    if (Application.isPlaying) Destroy(leftover.gameObject);
                    else DestroyImmediate(leftover.gameObject);
                }
            }
        }
    }

    /// <summary>
    /// Returns the active list of achievements to display, falling back to AchievementManager if needed.
    /// </summary>
    private List<AchievementData> GetAchievementsToDisplay()
    {
        if (achievements != null && achievements.Count > 0)
        {
            return achievements;
        }

        if (useManagerAchievementsIfEmpty)
        {
            if (AchievementManager.Instance != null && AchievementManager.Instance.allAchievements != null && AchievementManager.Instance.allAchievements.Count > 0)
            {
                return AchievementManager.Instance.allAchievements;
            }

            AchievementManager mgr = FindFirstObjectByType<AchievementManager>();
            if (mgr != null && mgr.allAchievements != null && mgr.allAchievements.Count > 0)
            {
                return mgr.allAchievements;
            }

            AchievementData[] loaded = Resources.FindObjectsOfTypeAll<AchievementData>();
            if (loaded != null && loaded.Length > 0)
            {
                List<AchievementData> sorted = new List<AchievementData>(loaded);
                sorted.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                return sorted;
            }
        }

        return new List<AchievementData>();
    }

    /// <summary>
    /// Applies visual styling depending on whether the achievement is locked or unlocked.
    /// </summary>
    private void ApplyLockedStyling(AchievementAlert item, AchievementData data)
    {
        bool isUnlocked = data.unlocked;

        if (item.icon)
        {
            item.icon.color = isUnlocked ? unlockedIconColor : lockedIconColor;
        }

        float targetAlpha = isUnlocked ? unlockedTextAlpha : lockedTextAlpha;

        if (item.achievementNameTMP) ApplyTextAlpha(item.achievementNameTMP, targetAlpha);
        if (item.descriptionTMP) ApplyTextAlpha(item.descriptionTMP, targetAlpha);
        if (item.titleTMP) ApplyTextAlpha(item.titleTMP, targetAlpha);
        if (item.progressTMP) ApplyTextAlpha(item.progressTMP, targetAlpha);

        // Fallback for any other TMP_Text children
        foreach (TMP_Text tmp in item.GetComponentsInChildren<TMP_Text>(true))
        {
            if (tmp) ApplyTextAlpha(tmp, targetAlpha);
        }

        if (!isUnlocked && hideLockedDescription)
        {
            item.descriptionText = lockedDescriptionPlaceholder;
            item.ApplyTexts();
        }
    }

    private void ApplyTextAlpha(TMP_Text textComponent, float alpha)
    {
        if (!textComponent) return;
        Color c = textComponent.color;
        c.a = alpha;
        textComponent.color = c;
    }

    [ContextMenu("Unlock All (Debug)")]
    private void DebugUnlockAll()
    {
        List<AchievementData> list = GetAchievementsToDisplay();
        foreach (AchievementData ach in list)
        {
            if (ach) ach.Unlock();
        }
        PopulateList();
    }

    [ContextMenu("Reset All (Debug)")]
    private void DebugResetAll()
    {
        List<AchievementData> list = GetAchievementsToDisplay();
        foreach (AchievementData ach in list)
        {
            if (ach) ach.ResetLock();
        }
        PopulateList();
    }

    /// <summary>
    /// Ensures drag hooks are registered on the scrollbar and scroll rect to update cursor icon during dragging.
    /// </summary>
    public void SetupDragCursorHooks()
    {
        if (changeCursorOnScrollbarDrag && scrollbar)
        {
            AttachCursorDragHook(scrollbar.gameObject);
        }

        if (changeCursorOnContentDrag && scrollRect)
        {
            AttachCursorDragHook(scrollRect.gameObject);
        }
    }

    /// <summary>
    /// Alias for <see cref="SetupDragCursorHooks"/>.
    /// </summary>
    public void SetupScrollbarCursorDrag() => SetupDragCursorHooks();

    /// <summary>
    /// Removes or resets any active drag states on the scrollbar and scroll rect.
    /// </summary>
    public void CleanupDragCursorHooks()
    {
        if (scrollbar && scrollbar.TryGetComponent(out CursorDragTarget sdt))
        {
            sdt.SetDragging(false);
        }
        if (scrollRect && scrollRect.TryGetComponent(out CursorDragTarget rdt))
        {
            rdt.SetDragging(false);
        }
    }

    /// <summary>
    /// Alias for <see cref="CleanupDragCursorHooks"/>.
    /// </summary>
    public void CleanupScrollbarCursorDrag() => CleanupDragCursorHooks();

    private void AttachCursorDragHook(GameObject target)
    {
        if (!target) return;
        if (!target.TryGetComponent(out CursorDragTarget _))
        {
            target.AddComponent<CursorDragTarget>();
        }
    }
}
