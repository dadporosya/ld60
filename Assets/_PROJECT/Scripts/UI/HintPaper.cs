using PrimeTween;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// A hint sheet hanging from the top of the screen. Opened, it slides down into view; closed, it slides
/// back up out of the screen until only its toggle button (at the sheet's lower edge) is left showing.
///
/// <para>Put it on the sheet's RectTransform, as it sits when OPEN (anchored to the top of the screen),
/// with the toggle button as a child at its lower edge - inside the sheet or hanging below it. The closed
/// position is worked out from the button: the sheet goes up until the button's top edge reaches where
/// the sheet's top edge was.</para>
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class HintPaper : MonoBehaviour
{
    [SerializeField] private Button toggleButton;
    [SerializeField] private bool startOpen = false;

    [Header("Closed position")]
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

    [Header("Toggle icon (optional)")]
    [Tooltip("Turned 180 degrees while open, e.g. an arrow pointing down when closed and up when open.")]
    [SerializeField] private RectTransform toggleIcon;
    [Tooltip("Or swap the icon's sprite instead (leave empty to keep it).")]
    [SerializeField] private Image toggleIconImage;
    [SerializeField] private Sprite openIconSprite;
    [SerializeField] private Sprite closedIconSprite;

    [Header("Audio")]
    [SerializeField] private AudioClip openSfx;
    [SerializeField] private AudioClip closeSfx;

    [Header("Events")]
    public UnityEvent onOpened = new UnityEvent();
    public UnityEvent onClosed = new UnityEvent();

    public bool IsOpen { get; private set; }

    private RectTransform rect;
    private Vector2 openPosition;
    private bool initialized;
    private Tween moveTween;
    private Tween iconTween;
    private float iconRestAngle;

    private Vector2 ClosedPosition => openPosition + Vector2.up * ClosedShift();

    private void Awake()
    {
        rect = (RectTransform)transform;
        if (toggleButton) toggleButton.onClick.AddListener(Toggle);
        if (toggleIcon) iconRestAngle = toggleIcon.localEulerAngles.z;
    }

    private void Start()
    {
        // Sizes come from the layout, which may only be built by now.
        Canvas.ForceUpdateCanvases();
        Initialize();
    }

    private void OnDisable()
    {
        moveTween.Stop();
        iconTween.Stop();
        if (initialized) Snap(IsOpen);
    }

    private void Initialize()
    {
        if (initialized) return;
        initialized = true;

        openPosition = rect.anchoredPosition;
        Snap(startOpen);
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
        Initialize();
        if (open == IsOpen && !moveTween.isAlive) return;

        IsOpen = open;
        moveTween.Stop();

        float duration = open ? openDuration : closeDuration;
        if (!animate || duration <= 0f || !isActiveAndEnabled)
            Snap(open);
        else
        {
            Vector2 target = open ? openPosition : ClosedPosition;
            moveTween = Tween.UIAnchoredPosition(rect, target, duration, open ? openEase : closeEase,
                useUnscaledTime: useUnscaledTime);
            AnimateIcon(open, duration);
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

    private void Snap(bool open)
    {
        IsOpen = open;
        rect.anchoredPosition = open ? openPosition : ClosedPosition;
        AnimateIcon(open, 0f);
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
