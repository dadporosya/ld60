using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// A hint sheet that slides into view when opened and back out when closed. The button or the toggle key
/// flips it; the button's label says which key and what it will do.
///
/// <para>With an Open Target: place the sheet where it sits CLOSED. Opened, it moves onto the Open Target
/// (an empty RectTransform outside the sheet); closed, it goes back to where it started.</para>
///
/// <para>Without one: place the sheet as it sits OPEN (anchored to the top of the screen), with the toggle
/// button as a child at its lower edge - inside the sheet or hanging below it. The closed position is worked
/// out from the button: the sheet goes up until the button's top edge reaches where the sheet's top edge was.</para>
///
/// <para>With Auto Open on, every time the sheet is shown (its panel turned on as gameplay starts) it
/// starts closed and slides open by itself a moment later.</para>
///
/// <para>With a Combinations Text it lists the dice combinations as sprite icons with their points, from the
/// DiceManager's scoring table (<see cref="RefreshCombinations"/>) - written as the scene starts, even while
/// the sheet is hidden, and again each time it is shown.</para>
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class HintPaper : MonoBehaviour
{
    [SerializeField] private Button toggleButton;
    [Tooltip("Keyboard key that does the same as clicking the toggle button. None: no key.")]
    [SerializeField] private Key toggleKey = Key.S;
    [Tooltip("Used when Auto Open is off: whether the sheet starts open.")]
    [SerializeField] private bool startOpen = false;

    [Header("Auto open")]
    [Tooltip("Each time the sheet is shown, start closed and slide open by itself.")]
    [SerializeField] private bool autoOpen = true;
    [Tooltip("Only the first time it is shown; later it keeps whatever the player left it at.")]
    [SerializeField] private bool autoOpenOnlyOnce = false;
    [SerializeField, Min(0f)] private float autoOpenDelay = 0.5f;
    [SerializeField, Min(0f)] private float autoOpenDuration = 0.7f;
    [SerializeField] private Ease autoOpenEase = Ease.OutBack;

    [Header("Open position")]
    [Tooltip("Empty object the sheet moves onto when opened (its pivot onto this object's pivot); closed, it " +
             "goes back to where it sits in the scene. Keep it outside the sheet. Empty: the sheet sits open in " +
             "the scene and closes upward (Closed Position).")]
    [SerializeField] private RectTransform openTarget;

    [Header("Closed position (no Open Target)")]
    [Tooltip("Work the closed position out from the toggle button. Off: use Closed Offset.")]
    [SerializeField] private bool autoClosedOffset = true;
    [Tooltip("Extra amount of the sheet left showing above the button when closed (UI units). Negative hides part of the button.")]
    [SerializeField] private float extraPeek = 0f;
    [Tooltip("Used when Auto Closed Offset is off: how far up the sheet moves to close (UI units).")]
    [SerializeField] private float closedOffset = 400f;

    [Header("Animation")]
    [SerializeField, Min(0f)] private float openDuration = 0.45f;
    [SerializeField] private Ease openEase = Ease.OutBack;
    [SerializeField, Min(0f)] private float closeDuration = 0.35f;
    [SerializeField] private Ease closeEase = Ease.InOutSine;
    [Tooltip("Ignore time scale (keeps working while the game is paused).")]
    [SerializeField] private bool useUnscaledTime = true;

    [Header("Button label (optional)")]
    [Tooltip("Empty: the first TMP text under the toggle button.")]
    [SerializeField] private TMP_Text toggleLabel;
    [Tooltip("Shown while closed. {0} is the toggle key.")]
    [SerializeField] private string showLabel = "({0}) show hint";
    [Tooltip("Shown while open. {0} is the toggle key.")]
    [SerializeField] private string closeLabel = "({0}) close hint";

    [Header("Button pulse")]
    [Tooltip("Scale the button grows to and back when clicked or the key is pressed. 1: no pulse.")]
    [SerializeField] private float pulseScale = 1.06f;
    [Tooltip("Seconds for the grow, and again for the shrink back.")]
    [SerializeField, Min(0f)] private float pulseDuration = 0.07f;

    [Header("Toggle icon (optional)")]
    [Tooltip("Turned 180 degrees while open, e.g. an arrow pointing down when closed and up when open.")]
    [SerializeField] private RectTransform toggleIcon;
    [Tooltip("Or swap the icon's sprite instead (leave empty to keep it).")]
    [SerializeField] private Image toggleIconImage;
    [SerializeField] private Sprite openIconSprite;
    [SerializeField] private Sprite closedIconSprite;

    [Header("Combinations (optional)")]
    [Tooltip("Filled with the DiceManager's scoring table: one line per combination, its dice as sprite icons, then its points.")]
    [SerializeField] private TMP_Text combinationsText;
    [Tooltip("One line. {0}: the combination's dice icons, {1}: its points, {2}: its name.")]
    [SerializeField] private string combinationLine = "{0} - {1}";
    [Tooltip("Put between a combination's dice icons. Empty: the icons touch (spacing then comes from the sprite asset's glyph advance).")]
    [SerializeField] private string iconGap = "";
    [Tooltip("Leave out combinations worth no points (Nothing). One or Three is always listed - it saves a reroll from busting.")]
    [SerializeField] private bool skipZeroPoints = true;
    [Tooltip("One or Three's line: it is worth no points, it only saves a reroll from busting. Same placeholders as " +
             "Combination Line. Empty: Combination Line.")]
    [SerializeField] private string safeFaceLine = "{0} - no bust";
    [Tooltip("Show each combination with example faces (5 5 5 3 3 for a full house). Off: x / y icons.")]
    [SerializeField] private bool exampleFaces = true;
    [Tooltip("Written under the combinations, after an empty line. {0}: the faces that save a reroll (\"1 or 3\"). Empty: none.")]
    [SerializeField, TextArea] private string bustLine =
        "Bust - if the dice you rolled make no combination (and show no {0}), you lose all your points.";

    [Header("Audio")]
    [SerializeField] private AudioClip openSfx;
    [SerializeField] private AudioClip closeSfx;

    [Header("Events")]
    public UnityEvent onOpened = new UnityEvent();
    public UnityEvent onClosed = new UnityEvent();

    public bool IsOpen { get; private set; }

    private RectTransform rect;
    // Where the sheet sits in the scene: closed with an Open Target, open without.
    private Vector2 startPosition;
    private bool initialized;
    private bool autoOpened;
    private Tween moveTween;
    private Tween iconTween;
    private Tween autoOpenTween;
    private Tween pulseTween;
    private float iconRestAngle;
    private Transform pulseTarget;
    private Vector3 pulseRestScale;

    private Vector2 OpenPosition => openTarget ? TargetPosition() : startPosition;
    private Vector2 ClosedPosition => openTarget ? startPosition : startPosition + Vector2.up * ClosedShift();

    private void Awake()
    {
        rect = (RectTransform)transform;
        if (toggleButton)
        {
            toggleButton.onClick.AddListener(OnToggleInput);
            pulseTarget = toggleButton.transform;
            pulseRestScale = pulseTarget.localScale;
            if (!toggleLabel) toggleLabel = toggleButton.GetComponentInChildren<TMP_Text>(true);
        }
        if (toggleIcon) iconRestAngle = toggleIcon.localEulerAngles.z;
    }

    private void OnEnable()
    {
        // The first time, Start does it once the layout is built.
        if (initialized)
        {
            RefreshCombinations();
            TryAutoOpen();
        }
    }

    private void Start()
    {
        RefreshCombinations();
        // Sizes come from the layout, which may only be built by now.
        Canvas.ForceUpdateCanvases();
        Initialize();
        TryAutoOpen();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void HookSceneLoads()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    /// <summary>
    /// Every scene start fills its hint papers' combinations from the scoring table - hidden ones too, so the
    /// text is right before a paper is ever shown.
    /// </summary>
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        foreach (HintPaper paper in FindObjectsByType<HintPaper>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (paper.gameObject.scene == scene) paper.RefreshCombinations();
    }

    /// <summary>Writes the scoring table into <see cref="combinationsText"/>: "dice icons - points" per combination.</summary>
    public void RefreshCombinations()
    {
        if (!combinationsText) return;

        DiceManager dice = DiceManager.Instance ? DiceManager.Instance : FindFirstObjectByType<DiceManager>(FindObjectsInactive.Include);
        if (!dice)
        {
            h.Out("HintPaper: no DiceManager to list the combinations from.");
            return;
        }

        System.Text.StringBuilder text = new System.Text.StringBuilder();
        foreach (CombinationScore entry in dice.Combinations)
        {
            if (entry == null || !entry.enabled) continue;
            bool safeFace = entry.combination == PokerDiceCombination.SafeFace;
            if (skipZeroPoints && entry.points == 0 && !safeFace) continue;
            string icons = exampleFaces
                ? DiceIcons.Example(entry.combination, iconGap ?? "")
                : DiceIcons.Combination(entry.combination, iconGap ?? "");
            if (icons.Length == 0) continue;

            string line = safeFace && !string.IsNullOrEmpty(safeFaceLine) ? safeFaceLine : combinationLine;
            if (text.Length > 0) text.Append('\n');
            text.AppendFormat(line, icons, entry.points, entry.displayName);
        }

        if (!string.IsNullOrEmpty(bustLine))
        {
            if (text.Length > 0) text.Append("\n\n");
            text.AppendFormat(bustLine, string.Join(" or ", PokerDiceEvaluator.SafeFaces));
        }
        combinationsText.text = text.ToString();
    }

    private void Update()
    {
        if (toggleKey == Key.None) return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard[toggleKey].wasPressedThisFrame) return;
        if (toggleButton && !toggleButton.IsInteractable()) return;
        OnToggleInput();
    }

    private void OnDisable()
    {
        moveTween.Stop();
        iconTween.Stop();
        autoOpenTween.Stop();
        pulseTween.Stop();
        if (pulseTarget) pulseTarget.localScale = pulseRestScale;
        if (initialized) Snap(IsOpen);
    }

    private void Initialize()
    {
        if (initialized) return;
        initialized = true;

        startPosition = rect.anchoredPosition;
        if (openTarget && openTarget.IsChildOf(rect))
            h.Out($"HintPaper: Open Target '{openTarget.name}' is inside the sheet and moves with it. Put it outside.");
        Snap(startOpen);
    }

    /// <summary>Snaps the sheet closed and has it slide open after <see cref="autoOpenDelay"/>.</summary>
    private void TryAutoOpen()
    {
        if (!autoOpen || (autoOpenOnlyOnce && autoOpened)) return;

        moveTween.Stop();
        Snap(false);
        autoOpenTween.Stop();
        autoOpenTween = Tween.Delay(this, autoOpenDelay, paper => paper.AutoOpenNow(), useUnscaledTime);
    }

    private void AutoOpenNow()
    {
        autoOpened = true;
        Move(true, true, autoOpenDuration, autoOpenEase);
    }

    /// <summary>The toggle button was clicked or the toggle key pressed.</summary>
    private void OnToggleInput()
    {
        Pulse();
        Toggle();
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open() => SetOpen(true);

    public void Close() => SetOpen(false);

    public void SetOpen(bool open, bool animate = true)
    {
        // The player (or a script) got there first.
        autoOpenTween.Stop();
        Move(open, animate, open ? openDuration : closeDuration, open ? openEase : closeEase);
    }

    private void Move(bool open, bool animate, float duration, Ease ease)
    {
        Initialize();
        if (open == IsOpen && !moveTween.isAlive) return;

        IsOpen = open;
        moveTween.Stop();

        if (!animate || duration <= 0f || !isActiveAndEnabled)
            Snap(open);
        else
        {
            Vector2 target = open ? OpenPosition : ClosedPosition;
            moveTween = Tween.UIAnchoredPosition(rect, target, duration, ease, useUnscaledTime: useUnscaledTime);
            AnimateIcon(open, duration);
            UpdateLabel(open);
        }

        AudioClip clip = open ? openSfx : closeSfx;
        if (animate && clip && SFXManager.Instance) SFXManager.Instance.PlayClip(clip);

        if (open) onOpened?.Invoke();
        else onClosed?.Invoke();
    }

    /// <summary>
    /// How far up the sheet moves to close: from its top edge down to the toggle button's top edge, so
    /// the button is all that stays on screen.
    /// </summary>
    private float ClosedShift()
    {
        if (!autoClosedOffset || !toggleButton) return closedOffset;

        RectTransform button = (RectTransform)toggleButton.transform;
        Vector3[] corners = new Vector3[4];
        button.GetWorldCorners(corners);

        // Corner 1 is top-left; measured in the sheet's own space, then scaled into its parent's.
        float buttonTop = rect.InverseTransformPoint(corners[1]).y;
        float sheetTop = rect.rect.yMax;
        return (sheetTop - buttonTop - extraPeek) * rect.localScale.y;
    }

    /// <summary>
    /// The anchored position that puts the sheet's pivot on <see cref="openTarget"/>'s: the gap between the two
    /// pivots, measured in the sheet's parent, added to where the sheet is now.
    /// </summary>
    private Vector2 TargetPosition()
    {
        Transform parent = rect.parent;
        Vector3 from = parent ? parent.InverseTransformPoint(rect.position) : rect.position;
        Vector3 to = parent ? parent.InverseTransformPoint(openTarget.position) : openTarget.position;
        return rect.anchoredPosition + (Vector2)(to - from);
    }

    private void Snap(bool open)
    {
        IsOpen = open;
        rect.anchoredPosition = open ? OpenPosition : ClosedPosition;
        AnimateIcon(open, 0f);
        UpdateLabel(open);
    }

    /// <summary>A slight grow-and-back of the toggle button.</summary>
    private void Pulse()
    {
        if (!pulseTarget || !isActiveAndEnabled || pulseDuration <= 0f || Mathf.Approximately(pulseScale, 1f)) return;

        pulseTween.Stop();
        pulseTarget.localScale = pulseRestScale;
        pulseTween = Tween.Scale(pulseTarget, pulseRestScale * pulseScale, pulseDuration, Ease.OutQuad,
            cycles: 2, cycleMode: CycleMode.Yoyo, useUnscaledTime: useUnscaledTime);
    }

    private void UpdateLabel(bool open)
    {
        if (!toggleLabel) return;
        toggleLabel.text = string.Format(open ? closeLabel : showLabel, KeyName());
    }

    private string KeyName()
    {
        if (toggleKey == Key.None) return "";
        // The enum name, not the layout's display name, so S stays S on any keyboard layout.
        string name = toggleKey.ToString();
        return name.StartsWith("Digit") ? name.Substring("Digit".Length) : name;
    }

    private void AnimateIcon(bool open, float duration)
    {
        if (toggleIconImage)
        {
            Sprite sprite = open ? openIconSprite : closedIconSprite;
            if (sprite) toggleIconImage.sprite = sprite;
        }

        if (!toggleIcon) return;

        iconTween.Stop();
        float target = iconRestAngle + (open ? 180f : 0f);
        if (duration > 0f)
        {
            // Angles, not a rotation: a half turn as a quaternion could spin either way.
            float from = target + Mathf.DeltaAngle(target, toggleIcon.localEulerAngles.z);
            iconTween = Tween.Custom(toggleIcon, from, target, duration, SetIconAngle, Ease.OutQuad,
                useUnscaledTime: useUnscaledTime);
        }
        else SetIconAngle(toggleIcon, target);
    }

    private static void SetIconAngle(RectTransform icon, float angle)
    {
        Vector3 euler = icon.localEulerAngles;
        euler.z = angle;
        icon.localEulerAngles = euler;
    }
}
