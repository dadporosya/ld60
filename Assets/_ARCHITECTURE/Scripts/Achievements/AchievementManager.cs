using System;
using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Screen corner or edge position where achievement alerts appear.
/// </summary>
public enum AlertPosition
{
    [Tooltip("Bottom-right corner of the screen.")]
    BottomRight = 0,

    [Tooltip("Bottom-left corner of the screen.")]
    BottomLeft = 1,

    [Tooltip("Top-right corner of the screen.")]
    TopRight = 2,

    [Tooltip("Top-left corner of the screen.")]
    TopLeft = 3,

    [Tooltip("Top center edge of the screen.")]
    TopCenter = 4,

    [Tooltip("Bottom center edge of the screen.")]
    BottomCenter = 5
}

/// <summary>
/// Direction from which the achievement alert slides onto the screen.
/// </summary>
public enum AlertSlideDirection
{
    [Tooltip("Automatically selects the natural slide direction based on the chosen AlertPosition.")]
    Auto = 0,

    [Tooltip("Slides onto screen from the right edge.")]
    FromRight = 1,

    [Tooltip("Slides onto screen from the left edge.")]
    FromLeft = 2,

    [Tooltip("Slides onto screen from the bottom edge.")]
    FromBottom = 3,

    [Tooltip("Slides onto screen from the top edge.")]
    FromTop = 4
}

/// <summary>
/// Singleton manager responsible for granting achievements, tracking unlock states and progress,
/// playing grant SFX/VFX, and animating achievement alerts on the screen.
/// </summary>
public class AchievementManager : MonoBehaviour
{
    public static AchievementManager Instance;

    [Header("Debug")]
    [Tooltip("If true, pressing P in play mode will spawn an empty achievement alert for animation testing.")]
    public bool debugMode = false;

    [Header("Prefabs & References")]
    [Tooltip("Prefab of the achievement alert popup instantiated when an achievement is granted.")]
    public AchievementAlert achievementAlertPfb;

    [Tooltip("Target canvas where achievement alerts are spawned. Auto-resolved if empty.")]
    public Canvas uiCanvas;

    [Tooltip("Target parent container for spawned alerts. Falls back to uiCanvas root.")]
    public RectTransform alertParent;

    [Tooltip("Scene tag used to find the UI alert parent container.")]
    public string alertParentTag = "AlertParent";

    [Header("Audio & Effects")]
    [Tooltip("Audio clips played when an achievement is granted.")]
    public List<AudioClip> grantSFX = new List<AudioClip>();

    [Tooltip("Optional particle VFX prefab instantiated when an achievement is granted.")]
    public GameObject grantVFXPfb;

    [Header("Position & Layout")]
    [Tooltip("Screen corner or edge where achievement alerts appear.")]
    public AlertPosition alertPosition = AlertPosition.BottomRight;

    [Tooltip("Direction from which the alert slides onto the screen. 'Auto' selects the natural direction based on alertPosition.")]
    public AlertSlideDirection slideDirection = AlertSlideDirection.Auto;

    [Tooltip("Screen margin offset (in canvas units) from the selected screen corner/edge.")]
    public Vector2 screenOffset = new Vector2(25f, 25f);

    [Header("Timing & Animation")]
    [Tooltip("Duration (in seconds) the alert remains on screen between entrance and exit animations.")]
    [Min(0.5f)] public float displayDuration = 3.5f;

    [Tooltip("Duration of the slide-in entrance animation.")]
    [Min(0.05f)] public float moveInDuration = 0.45f;

    [Tooltip("Duration of the slide-out exit animation.")]
    [Min(0.05f)] public float moveOutDuration = 0.35f;

    [Tooltip("Delay in seconds after an alert finishes its exit animation before displaying the next queued achievement alert.")]
    [Min(0f)] public float delayBetweenAlerts = 0.111f;

    [Tooltip("Easing curve for the entrance slide.")]
    public Ease moveInEase = Ease.OutBack;

    [Tooltip("Easing curve for the exit slide.")]
    public Ease moveOutEase = Ease.InBack;

    [Tooltip("Whether tween animations use unscaled time so alerts animate while the game is paused.")]
    public bool useUnscaledTime = true;

    [Header("Registry")]
    [Tooltip("List of all authored achievements in the project.")]
    public List<AchievementData> allAchievements = new List<AchievementData>();

    /// <summary>
    /// Raised whenever an achievement is successfully granted for the first time.
    /// </summary>
    public event Action<AchievementData> OnAchievementGranted;

    private readonly List<AchievementAlert> activeAlerts = new List<AchievementAlert>();
    private readonly Queue<AchievementData> pendingAlertQueue = new Queue<AchievementData>();
    private bool isAlertAnimating;
    private bool canvasResolved;

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance, false);
    }

    private void Start()
    {
        ResolveCanvas();
        LoadAllAchievements();
    }

    /// <summary>
    /// Ensures all tracked achievements load their persistent state from PlayerPrefs.
    /// </summary>
    public void LoadAllAchievements()
    {
        if (allAchievements == null) return;
        foreach (AchievementData ach in allAchievements)
        {
            if (ach) ach.LoadFromPrefs();
        }
    }

    private void Update()
    {
        if (!debugMode) return;

        bool pPressed = false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            pPressed = Keyboard.current.pKey.wasPressedThisFrame;
        }
#else
        pPressed = Input.GetKeyDown(KeyCode.P);
#endif

        if (pPressed)
        {
            SpawnEmptyAchievement();
        }
    }

    private void OnDestroy()
    {
        pendingAlertQueue.Clear();
        isAlertAnimating = false;
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Spawns an empty achievement alert popup for animation and visual testing.
    /// </summary>
    [ContextMenu("Spawn Empty Achievement Alert (Debug)")]
    public void SpawnEmptyAchievement()
    {
        EnqueueAlert(null);
    }

    /// <summary>
    /// Checks whether an achievement is already granted/unlocked.
    /// </summary>
    public bool IsUnlocked(AchievementData data)
    {
        if (!data) return false;
        return data.unlocked;
    }

    /// <summary>
    /// Grants an achievement if it has not been unlocked yet.
    /// Spawns an achievement alert in the bottom-left of the canvas, plays SFX, triggers VFX,
    /// and animates the popup on and off screen.
    /// </summary>
    /// <param name="data">The achievement data ScriptableObject to grant.</param>
    public void GrantAchievement(AchievementData data)
    {
        if (!data)
        {
            h.Out("AchievementManager.GrantAchievement: data is null, skipping.");
            return;
        }

        // If achievement was already granted, do not proceed
        if (data.unlocked)
        {
            return;
        }

        // Mark unlocked
        data.Unlock();

        // Register in allAchievements list if not already present
        if (!allAchievements.Contains(data))
        {
            allAchievements.Add(data);
        }

        // Queue alert popup and animation
        EnqueueAlert(data);

        OnAchievementGranted?.Invoke(data);
    }

    /// <summary>
    /// Grants an achievement by its unique achievement name if found in <see cref="allAchievements"/>.
    /// </summary>
    public void GrantAchievementByName(string achievementName)
    {
        if (string.IsNullOrEmpty(achievementName)) return;

        AchievementData data = allAchievements.Find(a => a && a.achievementName.Equals(achievementName, StringComparison.OrdinalIgnoreCase));
        if (data)
        {
            GrantAchievement(data);
        }
        else
        {
            h.Out($"AchievementManager.GrantAchievementByName: '{achievementName}' not found in allAchievements.");
        }
    }

    /// <summary>
    /// Adds progress to a Value-based achievement and grants it if the desired value is reached.
    /// </summary>
    /// <param name="data">The achievement data.</param>
    /// <param name="amount">Amount to increment progress by.</param>
    public void AddProgress(AchievementData data, float amount)
    {
        if (!data || data.unlocked) return;

        bool unlockedNow = data.AddProgress(amount);
        if (unlockedNow)
        {
            if (!allAchievements.Contains(data)) allAchievements.Add(data);
            EnqueueAlert(data);
            OnAchievementGranted?.Invoke(data);
        }
    }

    /// <summary>
    /// Sets the progress for a Value-based achievement and grants it if the desired value is reached.
    /// </summary>
    /// <param name="data">The achievement data.</param>
    /// <param name="value">New current progress value.</param>
    public void SetProgress(AchievementData data, float value)
    {
        if (!data || data.unlocked) return;

        bool unlockedNow = data.SetProgress(value);
        if (unlockedNow)
        {
            if (!allAchievements.Contains(data)) allAchievements.Add(data);
            EnqueueAlert(data);
            OnAchievementGranted?.Invoke(data);
        }
    }

    /// <summary>
    /// Enqueues an achievement alert to be displayed sequentially.
    /// If no alert is currently animating, starts playing immediately.
    /// </summary>
    public void EnqueueAlert(AchievementData data)
    {
        pendingAlertQueue.Enqueue(data);

        if (!isAlertAnimating)
        {
            ProcessNextAlert();
        }
    }

    /// <summary>
    /// Dequeues and displays the next queued achievement alert popup.
    /// </summary>
    protected virtual void ProcessNextAlert()
    {
        if (pendingAlertQueue.Count == 0)
        {
            isAlertAnimating = false;
            return;
        }

        isAlertAnimating = true;
        AchievementData nextData = pendingAlertQueue.Dequeue();

        PlayGrantSound();
        SpawnAndAnimateAlert(nextData);
    }

    /// <summary>
    /// Plays an achievement grant SFX clip via <see cref="SFXManager"/>.
    /// </summary>
    protected virtual void PlayGrantSound()
    {
        if (grantSFX == null || grantSFX.Count == 0) return;
        if (!SFXManager.Instance) return;

        SFXManager.Instance.PlayRandomClip(grantSFX, is3D: false, playIndependently: true);
    }

    /// <summary>
    /// Spawns the achievement alert on the screen canvas and runs its slide-in/slide-out animation
    /// according to <see cref="alertPosition"/> and <see cref="slideDirection"/>.
    /// When the exit animation completes, waits <see cref="delayBetweenAlerts"/> seconds before processing the next alert.
    /// </summary>
    protected virtual void SpawnAndAnimateAlert(AchievementData data)
    {
        if (!achievementAlertPfb)
        {
            h.Out("AchievementManager: No achievementAlertPfb assigned, skipping alert visual spawn.");
            ScheduleNextAlert();
            return;
        }

        ResolveCanvas();

        Transform parentTransform = alertParent ? (Transform)alertParent : (uiCanvas ? uiCanvas.transform : transform);
        AchievementAlert alertInstance = Instantiate(achievementAlertPfb, parentTransform);
        alertInstance.gameObject.SetActive(true);
        alertInstance.InitFromData(data);

        RectTransform alertRect = alertInstance.transform as RectTransform;
        if (!alertRect)
        {
            ScheduleNextAlert();
            return;
        }

        // Configure anchors, pivot, target on-screen position, and initial off-screen position
        CalculateAlertPositions(alertRect, out Vector2 targetPos, out Vector2 offscreenPos);
        alertRect.anchoredPosition = offscreenPos;

        // Spawn VFX if assigned
        SpawnGrantVFX(alertInstance.transform);

        activeAlerts.Add(alertInstance);

        bool finished = false;
        void OnAlertFinished()
        {
            if (finished) return;
            finished = true;

            if (alertInstance)
            {
                activeAlerts.Remove(alertInstance);
                Destroy(alertInstance.gameObject);
            }

            ScheduleNextAlert();
        }

        // Sequence PrimeTween animation: slide in -> hold -> slide out -> finish
        Sequence seq = Sequence.Create(useUnscaledTime: useUnscaledTime)
            .Chain(Tween.UIAnchoredPosition(alertRect, targetPos, moveInDuration, moveInEase))
            .ChainDelay(displayDuration)
            .Chain(Tween.UIAnchoredPosition(alertRect, offscreenPos, moveOutDuration, moveOutEase))
            .ChainCallback(OnAlertFinished);

        // Cancel sequence cleanly if user clicks close button mid-animation
        alertInstance.OnClosed += alert =>
        {
            if (seq.isAlive) seq.Stop();
            OnAlertFinished();
        };
    }

    /// <summary>
    /// Schedules processing the next queued achievement alert after <see cref="delayBetweenAlerts"/> seconds.
    /// </summary>
    protected virtual void ScheduleNextAlert()
    {
        if (pendingAlertQueue.Count == 0)
        {
            isAlertAnimating = false;
            return;
        }

        if (delayBetweenAlerts > 0f)
        {
            Tween.Delay(delayBetweenAlerts, () => ProcessNextAlert(), useUnscaledTime: useUnscaledTime);
        }
        else
        {
            ProcessNextAlert();
        }
    }

    /// <summary>
    /// Computes the anchor, pivot, target on-screen position, and off-screen starting position
    /// for the alert popup based on <see cref="alertPosition"/> and <see cref="slideDirection"/>.
    /// </summary>
    protected virtual void CalculateAlertPositions(RectTransform alertRect, out Vector2 targetPos, out Vector2 offscreenPos)
    {
        Vector2 anchor;
        Vector2 pivot;
        Vector2 sign;

        switch (alertPosition)
        {
            case AlertPosition.BottomRight:
                anchor = new Vector2(1f, 0f);
                pivot = new Vector2(1f, 0f);
                sign = new Vector2(-1f, 1f);
                break;
            case AlertPosition.BottomLeft:
                anchor = new Vector2(0f, 0f);
                pivot = new Vector2(0f, 0f);
                sign = new Vector2(1f, 1f);
                break;
            case AlertPosition.TopRight:
                anchor = new Vector2(1f, 1f);
                pivot = new Vector2(1f, 1f);
                sign = new Vector2(-1f, -1f);
                break;
            case AlertPosition.TopLeft:
                anchor = new Vector2(0f, 1f);
                pivot = new Vector2(0f, 1f);
                sign = new Vector2(1f, -1f);
                break;
            case AlertPosition.TopCenter:
                anchor = new Vector2(0.5f, 1f);
                pivot = new Vector2(0.5f, 1f);
                sign = new Vector2(1f, -1f);
                break;
            case AlertPosition.BottomCenter:
                anchor = new Vector2(0.5f, 0f);
                pivot = new Vector2(0.5f, 0f);
                sign = new Vector2(1f, 1f);
                break;
            default:
                anchor = new Vector2(1f, 0f);
                pivot = new Vector2(1f, 0f);
                sign = new Vector2(-1f, 1f);
                break;
        }

        alertRect.anchorMin = anchor;
        alertRect.anchorMax = anchor;
        alertRect.pivot = pivot;

        if (alertPosition == AlertPosition.TopCenter || alertPosition == AlertPosition.BottomCenter)
        {
            targetPos = new Vector2(screenOffset.x, sign.y * screenOffset.y);
        }
        else
        {
            targetPos = new Vector2(sign.x * screenOffset.x, sign.y * screenOffset.y);
        }

        float width = alertRect.rect.width > 0f ? alertRect.rect.width : 360f;
        float height = alertRect.rect.height > 0f ? alertRect.rect.height : 160f;
        const float margin = 80f;

        RectTransform parentRect = alertRect.parent as RectTransform;
        float parentWidth = parentRect ? parentRect.rect.width : (Screen.width > 0 ? Screen.width : 1920f);
        float parentHeight = parentRect ? parentRect.rect.height : (Screen.height > 0 ? Screen.height : 1080f);

        AlertSlideDirection effectiveDir = slideDirection;
        if (effectiveDir == AlertSlideDirection.Auto)
        {
            switch (alertPosition)
            {
                case AlertPosition.BottomRight:
                case AlertPosition.TopRight:
                    effectiveDir = AlertSlideDirection.FromRight;
                    break;
                case AlertPosition.BottomLeft:
                case AlertPosition.TopLeft:
                    effectiveDir = AlertSlideDirection.FromLeft;
                    break;
                case AlertPosition.TopCenter:
                    effectiveDir = AlertSlideDirection.FromTop;
                    break;
                case AlertPosition.BottomCenter:
                    effectiveDir = AlertSlideDirection.FromBottom;
                    break;
                default:
                    effectiveDir = AlertSlideDirection.FromRight;
                    break;
            }
        }

        switch (effectiveDir)
        {
            case AlertSlideDirection.FromRight:
                if (pivot.x >= 0.9f)
                    offscreenPos = new Vector2(width + margin, targetPos.y);
                else if (pivot.x <= 0.1f)
                    offscreenPos = new Vector2(parentWidth + margin, targetPos.y);
                else
                    offscreenPos = new Vector2(parentWidth * 0.5f + width * 0.5f + margin, targetPos.y);
                break;

            case AlertSlideDirection.FromLeft:
                if (pivot.x <= 0.1f)
                    offscreenPos = new Vector2(-width - margin, targetPos.y);
                else if (pivot.x >= 0.9f)
                    offscreenPos = new Vector2(-parentWidth - margin, targetPos.y);
                else
                    offscreenPos = new Vector2(-parentWidth * 0.5f - width * 0.5f - margin, targetPos.y);
                break;

            case AlertSlideDirection.FromBottom:
                if (pivot.y <= 0.1f)
                    offscreenPos = new Vector2(targetPos.x, -height - margin);
                else if (pivot.y >= 0.9f)
                    offscreenPos = new Vector2(targetPos.x, -parentHeight - margin);
                else
                    offscreenPos = new Vector2(targetPos.x, -parentHeight * 0.5f - height * 0.5f - margin);
                break;

            case AlertSlideDirection.FromTop:
                if (pivot.y >= 0.9f)
                    offscreenPos = new Vector2(targetPos.x, height + margin);
                else if (pivot.y <= 0.1f)
                    offscreenPos = new Vector2(targetPos.x, parentHeight + margin);
                else
                    offscreenPos = new Vector2(targetPos.x, parentHeight * 0.5f + height * 0.5f + margin);
                break;

            default:
                offscreenPos = new Vector2(width + margin, targetPos.y);
                break;
        }
    }

    /// <summary>
    /// Spawns particle VFX attached to the alert or at the target position.
    /// </summary>
    protected virtual void SpawnGrantVFX(Transform parent)
    {
        if (!grantVFXPfb) return;

        GameObject vfx = Instantiate(grantVFXPfb, parent);
        vfx.transform.localPosition = Vector3.zero;

        ParticleSystem ps = vfx.GetComponent<ParticleSystem>();
        if (!ps) ps = vfx.GetComponentInChildren<ParticleSystem>();

        if (ps)
        {
            ps.Play();
        }
    }

    /// <summary>
    /// Finds and caches the target UI Canvas and alert parent container.
    /// </summary>
    public void ResolveCanvas()
    {
        if (canvasResolved && uiCanvas && alertParent) return;

        if (!alertParent && !string.IsNullOrEmpty(alertParentTag))
        {
            GameObject tagged = GameObject.FindWithTag(alertParentTag);
            if (tagged) alertParent = tagged.transform as RectTransform;
        }

        if (!uiCanvas)
        {
            if (alertParent) uiCanvas = alertParent.GetComponentInParent<Canvas>();
            if (!uiCanvas) uiCanvas = FindFirstObjectByType<Canvas>();
        }

        if (uiCanvas)
        {
            uiCanvas = uiCanvas.rootCanvas ? uiCanvas.rootCanvas : uiCanvas;
            if (!alertParent) alertParent = uiCanvas.transform as RectTransform;
        }

        canvasResolved = uiCanvas != null;
    }

    /// <summary>
    /// Resets all tracked achievements to locked status (useful for testing or starting a new game).
    /// </summary>
    [ContextMenu("Reset All Achievements")]
    public void ResetAllAchievements()
    {
        pendingAlertQueue.Clear();
        isAlertAnimating = false;

        foreach (AchievementData ach in allAchievements)
        {
            if (ach) ach.ResetLock();
        }
        h.Out("AchievementManager: All achievements have been reset to locked.");
    }
}
