using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Hides the dialogue window's text box by sliding it down off the bottom of the screen, leaving only
// its toggle button (a tab on the window's top edge) peeking in; pressing the button again slides the
// box back up. The dialogue itself keeps running underneath - only the box moves. An optional icon
// Image (a separate object) swaps between an expanded and a collapsed sprite with the box's state.
//
// The button has to be a child of the window so it slides along with it. Use the component's context
// menu "Create Toggle Button" to build one on the window's top edge.
public class DialogueWindowCollapse : MonoBehaviour
{
    [SerializeField] private DialogueManager dialogueManager;
    [Tooltip("What slides. Empty: the DialogueManager's dialogueWindow.")]
    [SerializeField] private RectTransform window;
    [Tooltip("The toggle. A child of the window, on its top edge: its bottom edge is what stays at the " +
             "bottom of the screen while the box is down.")]
    [SerializeField] private Button toggleButton;
    [Tooltip("Optional: turned 180° while the box is down (an arrow pointing down flips to point up).")]
    [SerializeField] private RectTransform arrowIcon;

    [Header("Icon Sprite")]
    [Tooltip("Optional: an Image (its own object, e.g. on the button) whose sprite swaps with the box's state.")]
    [SerializeField] private Image stateIcon;
    [Tooltip("Shown while the box is up. Empty: the icon's sprite as authored.")]
    [SerializeField] private Sprite expandedSprite;
    [Tooltip("Shown while the box is down.")]
    [SerializeField] private Sprite collapsedSprite;

    [Header("Motion")]
    [SerializeField, Min(0f)] private float slideDuration = 0.35f;
    [SerializeField] private Ease collapseEase = Ease.InOutCubic;
    [SerializeField] private Ease expandEase = Ease.OutCubic;
    [Tooltip("Canvas units of window left showing below the button while the box is down. 0: only the button.")]
    [SerializeField] private float peek = 0f;
    [SerializeField] private AudioClip collapseSfx;
    [SerializeField] private AudioClip expandSfx;

    [Header("Input")]
    [Tooltip("While the box is down the dialogue doesn't advance, so nothing gets read past unseen - the " +
             "player brings the box back first. Hold-to-skip still skips the whole dialogue.")]
    [SerializeField] private bool holdAdvanceWhileCollapsed = true;
    [Tooltip("Keyboard shortcut doing the same as the button. None: the button only.")]
    [SerializeField] private Key toggleKey = Key.None;
    [Tooltip("Keyboard shortcut that only closes the box (slides it down). None: no key.")]
    [SerializeField] private Key collapseKey = Key.DownArrow;
    [Tooltip("Keyboard shortcut that only opens the box (slides it back up). None: no key.")]
    [SerializeField] private Key expandKey = Key.UpArrow;

    /// <summary>The text box is down (or on its way down).</summary>
    public bool Collapsed { get; private set; }

    private float shownY;
    private Quaternion arrowRestRotation;
    // Whether we placed a hold on the dialogue that still has to be released.
    private bool holdingAdvance;
    // Tweens target this component, not the window: DialogueWindowAnimations stops every tween on the
    // window's transform when it opens/closes, which would freeze a slide halfway.
    private Tween slide;
    private Tween arrowTurn;
    private readonly Vector3[] corners = new Vector3[4];

    private void Start()
    {
        if (!dialogueManager) dialogueManager = GetComponent<DialogueManager>();
        if (!dialogueManager) dialogueManager = DialogueManager.Instance;
        if (!window && dialogueManager && dialogueManager.dialogueWindow)
            window = dialogueManager.dialogueWindow.transform as RectTransform;

        if (!window)
        {
            h.Out("DialogueWindowCollapse: no window to slide");
            enabled = false;
            return;
        }

        shownY = window.anchoredPosition.y;
        if (arrowIcon) arrowRestRotation = arrowIcon.localRotation;
        if (stateIcon && !expandedSprite) expandedSprite = stateIcon.sprite;

        if (toggleButton) toggleButton.onClick.AddListener(Toggle);
        else h.Out("DialogueWindowCollapse: no toggle button assigned");

        if (dialogueManager)
        {
            // Every dialogue opens with the box up; the one that ended took its hold with it.
            dialogueManager.onDialogueStart.AddListener(ResetExpanded);
            dialogueManager.onDialogueEnd.AddListener(ForgetHold);
        }
    }

    private void OnDestroy()
    {
        if (toggleButton) toggleButton.onClick.RemoveListener(Toggle);
        if (dialogueManager)
        {
            dialogueManager.onDialogueStart.RemoveListener(ResetExpanded);
            dialogueManager.onDialogueEnd.RemoveListener(ForgetHold);
        }
        ReleaseHold();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (Pressed(keyboard, toggleKey)) Toggle();
        else if (Pressed(keyboard, collapseKey)) Collapse();
        else if (Pressed(keyboard, expandKey)) Expand();
    }

    private static bool Pressed(Keyboard keyboard, Key key) =>
        key != Key.None && keyboard[key].wasPressedThisFrame;

    public void Toggle()
    {
        if (Collapsed) Expand();
        else Collapse();
    }

    /// <summary>Slides the text box down until only the toggle button is left on screen.</summary>
    public void Collapse()
    {
        if (Collapsed || !CanSlide()) return;
        Collapsed = true;

        if (holdAdvanceWhileCollapsed && dialogueManager && !holdingAdvance)
        {
            dialogueManager.HoldAdvance(blocksSkip: false);
            holdingAdvance = true;
        }

        PlaySfx(collapseSfx);
        UpdateStateIcon();
        SlideTo(HiddenY(), collapseEase);
    }

    /// <summary>Slides the text box back up to where it was authored.</summary>
    public void Expand()
    {
        if (!Collapsed) return;
        Collapsed = false;

        ReleaseHold();
        PlaySfx(expandSfx);
        UpdateStateIcon();
        SlideTo(shownY, expandEase);
    }

    // Only while a dialogue is on screen, and not before its first node has started: a hold placed
    // during the open animation would swallow the call that starts the first node.
    private bool CanSlide() =>
        window.gameObject.activeInHierarchy
        && (!dialogueManager || (dialogueManager.DialogueActive && dialogueManager.NodesStarted > 0));

    private void SlideTo(float y, Ease ease)
    {
        slide.Stop();
        // Unscaled: dialogues also run over a paused game.
        if (slideDuration > 0f)
            slide = Tween.Custom(this, window.anchoredPosition.y, y, slideDuration,
                (self, value) => self.SetWindowY(value), ease, useUnscaledTime: true);
        else SetWindowY(y);

        if (!arrowIcon) return;
        arrowTurn.Stop();
        Quaternion target = Collapsed ? arrowRestRotation * Quaternion.Euler(0f, 0f, 180f) : arrowRestRotation;
        if (slideDuration > 0f)
            arrowTurn = Tween.LocalRotation(arrowIcon, target, slideDuration, ease, useUnscaledTime: true);
        else arrowIcon.localRotation = target;
    }

    private void UpdateStateIcon()
    {
        if (!stateIcon) return;
        Sprite sprite = Collapsed ? collapsedSprite : expandedSprite;
        if (sprite) stateIcon.sprite = sprite;
    }

    private void SetWindowY(float y)
    {
        Vector2 position = window.anchoredPosition;
        position.y = y;
        window.anchoredPosition = position;
    }

    /// <summary>
    /// The window's anchored Y at which the button's bottom edge (minus <see cref="peek"/>) sits on the
    /// bottom edge of the screen. Measured from the current layout, so it follows resolution changes.
    /// </summary>
    private float HiddenY()
    {
        RectTransform parent = window.parent as RectTransform;
        Canvas canvas = window.GetComponentInParent<Canvas>();
        RectTransform edge = toggleButton ? toggleButton.transform as RectTransform : null;
        if (!parent || !canvas || !edge || Mathf.Approximately(window.lossyScale.y, 0f)) return shownY;

        // In the window's own space, so a scale mid open/close animation doesn't skew it. The window rests
        // at scale 1 (DialogueWindowAnimations leaves it there), so this is also the distance in the parent.
        edge.GetWorldCorners(corners);
        float edgeBottom = window.InverseTransformPoint(corners[0]).y - peek;

        ((RectTransform)canvas.rootCanvas.transform).GetWorldCorners(corners);
        float screenBottom = parent.InverseTransformPoint(corners[0]).y;

        // anchoredPosition and localPosition differ by a constant (the anchors), so shift one by the other.
        float pivotY = screenBottom - edgeBottom;
        return window.anchoredPosition.y + (pivotY - window.localPosition.y);
    }

    private void ResetExpanded()
    {
        ReleaseHold();
        Collapsed = false;
        slide.Stop();
        arrowTurn.Stop();
        SetWindowY(shownY);
        if (arrowIcon) arrowIcon.localRotation = arrowRestRotation;
        UpdateStateIcon();
    }

    // EndDialogue already dropped every hold before firing onDialogueEnd - releasing ours now would
    // eat a hold that belongs to the next dialogue.
    private void ForgetHold() => holdingAdvance = false;

    private void ReleaseHold()
    {
        if (!holdingAdvance) return;
        holdingAdvance = false;
        if (dialogueManager) dialogueManager.ReleaseAdvance(blocksSkip: false);
    }

    private static void PlaySfx(AudioClip clip)
    {
        if (clip && SFXManager.Instance) SFXManager.Instance.PlayClip(clip);
    }

#if UNITY_EDITOR
    // Builds a tab-shaped button sitting on the window's top edge, centred, with an arrow on it.
    [ContextMenu("Create Toggle Button")]
    private void CreateToggleButton()
    {
        RectTransform parent = window;
        if (!parent && dialogueManager && dialogueManager.dialogueWindow)
            parent = dialogueManager.dialogueWindow.transform as RectTransform;
        if (!parent && TryGetComponent(out DialogueManager manager) && manager.dialogueWindow)
            parent = manager.dialogueWindow.transform as RectTransform;
        if (!parent)
        {
            h.Out("DialogueWindowCollapse: assign the window (or the DialogueManager) first.");
            return;
        }

        GameObject buttonObject = new GameObject("CollapseButton", typeof(RectTransform), typeof(Image), typeof(Button));
        Undo.RegisterCreatedObjectUndo(buttonObject, "Create Toggle Button");
        buttonObject.layer = parent.gameObject.layer;

        RectTransform buttonRect = (RectTransform)buttonObject.transform;
        buttonRect.SetParent(parent, false);
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 1f);
        buttonRect.pivot = new Vector2(0.5f, 0f);
        buttonRect.anchoredPosition = Vector2.zero;
        buttonRect.sizeDelta = new Vector2(120f, 36f);

        Image background = buttonObject.GetComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.6f);
        buttonObject.GetComponent<Button>().targetGraphic = background;

        // A "<" turned to point down: plain ASCII, so it is in every TMP font.
        GameObject arrowObject = new GameObject("Arrow", typeof(RectTransform), typeof(TextMeshProUGUI));
        arrowObject.layer = parent.gameObject.layer;
        RectTransform arrowRect = (RectTransform)arrowObject.transform;
        arrowRect.SetParent(buttonRect, false);
        arrowRect.anchorMin = Vector2.zero;
        arrowRect.anchorMax = Vector2.one;
        arrowRect.sizeDelta = Vector2.zero;
        arrowRect.localRotation = Quaternion.Euler(0f, 0f, 90f);

        TextMeshProUGUI arrow = arrowObject.GetComponent<TextMeshProUGUI>();
        arrow.text = "<";
        arrow.fontSize = 28f;
        arrow.alignment = TextAlignmentOptions.Center;
        arrow.raycastTarget = false;

        Undo.RecordObject(this, "Create Toggle Button");
        window = parent;
        toggleButton = buttonObject.GetComponent<Button>();
        arrowIcon = arrowRect;
        EditorUtility.SetDirty(this);
        Selection.activeGameObject = buttonObject;
    }
#endif
}
