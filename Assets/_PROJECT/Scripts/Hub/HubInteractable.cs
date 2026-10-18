using PrimeTween;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Base for the hub room's clickable objects (gramophone, gun, phone, ...). Subclasses only say what a
/// click does (<see cref="Interact"/>) and, optionally, when the object is available (<see cref="IsAvailable"/>).
///
/// <para>UI: put it on an Image (or a parent of one) under a Canvas with a GraphicRaycaster; the scene
/// needs an EventSystem. Clicks land on any raycast-target Graphic of the object or its children.
/// Clicks are ignored unless the <see cref="FuneralGameManager"/> is in the hub (no dialogue, dice or
/// card pick running) - or, for objects <see cref="UsableDuringDice"/>, in a dice round - and while a parent
/// CanvasGroup is non-interactable.</para>
/// </summary>
[DisallowMultipleComponent]
public abstract class HubInteractable : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Interaction")]
    [SerializeField] private bool interactable = true;
    [Tooltip("Clicks on the Image's transparent pixels go through (0 = the whole rect is clickable). " +
             "Needs Read/Write enabled on the sprite's texture import settings.")]
    [SerializeField, Range(0f, 1f)] private float alphaHitThreshold = 0f;
    [Tooltip("Can only be used once per game.")]
    [SerializeField] private bool singleUse = false;
    [Tooltip("Played on click (through the SFXManager).")]
    [SerializeField] private AudioClip clickSfx;
    [Tooltip("While hovered, the cursor treats it as a button: button icon, hover and press sounds.")]
    [SerializeField] private bool countsAsButton = true;

    [Header("Hover")]
    [Tooltip("What scales up on hover. Defaults to this transform.")]
    [SerializeField] private Transform visual;
    [SerializeField, Min(0f)] private float hoverScale = 1.06f;
    [SerializeField, Min(0f)] private float hoverDuration = 0.15f;
    [Tooltip("Optional: shown while hovered (outline, glow, label). Its Graphics stop being raycast " +
             "targets, so showing it never steals the hover from the object.")]
    [SerializeField] private GameObject hoverHighlight;

    [Header("Outline on hover")]
    [SerializeField] private bool outlineOnHover = true;
    [Tooltip("The Image that gets outlined. Defaults to this object's Image, else the first one in its children.")]
    [SerializeField] private Graphic outlineTarget;
    [SerializeField] private Color outlineColor = new Color(1f, 0.95f, 0.75f, 1f);
    [Tooltip("Outline thickness (UI units). Unity's UI Outline traces the sprite's shape by offsetting it; keep it " +
             "small (2-6) or the copies show.")]
    [SerializeField] private Vector2 outlineWidth = new Vector2(4f, 4f);
    [SerializeField, Min(0f)] private float outlineFadeDuration = 0.12f;

    [Header("Events")]
    public UnityEvent onHoverEnter = new UnityEvent();
    public UnityEvent onHoverExit = new UnityEvent();
    public UnityEvent onInteract = new UnityEvent();

    private Vector3 restScale;
    private Tween hoverTween;
    private Outline outline;
    private Tween outlineTween;
    private bool hovered;
    private bool used;

    /// <summary>True when a click would do something right now.</summary>
    public bool CanInteract =>
        interactable && !(singleUse && used) && GameAllowsInteraction && IsAvailable();

    public bool Interactable
    {
        get => interactable;
        set
        {
            interactable = value;
            if (!value) SetHovered(false);
        }
    }

    private bool GameAllowsInteraction =>
        (!FuneralGameManager.Instance ||
         (UsableDuringDice ? FuneralGameManager.Instance.CanLookAround : FuneralGameManager.Instance.IsInHub)) &&
        CanvasGroupsAllow();

    /// <summary>Also clickable while a dice round is running (not only in the hub).</summary>
    protected virtual bool UsableDuringDice => false;

    protected virtual void Awake()
    {
        if (!visual) visual = transform;
        restScale = visual.localScale;

        if (hoverHighlight)
        {
            foreach (Graphic graphic in hoverHighlight.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
            hoverHighlight.SetActive(false);
        }

        SetUpHitArea();
        SetUpOutline();
    }

    /// <summary>Applies <see cref="alphaHitThreshold"/> and warns about setups that can't be clicked.</summary>
    private void SetUpHitArea()
    {
        if (!GetComponentInParent<Canvas>(true))
            h.Out($"HubInteractable '{name}': not under a Canvas, it won't receive UI clicks.");

        bool hasTarget = false;
        foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
        {
            if (hoverHighlight && graphic.transform.IsChildOf(hoverHighlight.transform)) continue;
            if (graphic.raycastTarget) hasTarget = true;
        }
        if (!hasTarget)
            h.Out($"HubInteractable '{name}': no raycast-target Graphic (Image) on it or its children, it can't be clicked.");

        if (alphaHitThreshold <= 0f) return;

        Image image = GetComponent<Image>();
        if (!image || !image.sprite) return;
        if (image.sprite.texture && image.sprite.texture.isReadable) image.alphaHitTestMinimumThreshold = alphaHitThreshold;
        else h.Out($"HubInteractable '{name}': enable Read/Write on '{image.sprite.name}' for Alpha Hit Threshold to work.");
    }

    /// <summary>Like a Selectable: a parent CanvasGroup with interactable off disables the object.</summary>
    private bool CanvasGroupsAllow()
    {
        Transform t = transform;
        while (t)
        {
            CanvasGroup group = t.GetComponent<CanvasGroup>();
            if (group && group.enabled)
            {
                if (!group.interactable) return false;
                if (group.ignoreParentGroups) return true;
            }
            t = t.parent;
        }
        return true;
    }

    protected virtual void OnDisable()
    {
        hoverTween.Stop();
        if (hovered) SetCursorButtonHover(false);
        hovered = false;
        if (visual) visual.localScale = restScale;
        if (hoverHighlight) hoverHighlight.SetActive(false);
        ShowOutline(false, instant: true);
    }

    /// <summary>Adds a UI Outline to the target Image (or reuses one on it), hidden until hovered.</summary>
    private void SetUpOutline()
    {
        if (!outlineOnHover) return;

        if (!outlineTarget) outlineTarget = GetComponent<Graphic>();
        if (!outlineTarget)
            foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
            {
                if (hoverHighlight && graphic.transform.IsChildOf(hoverHighlight.transform)) continue;
                outlineTarget = graphic;
                break;
            }

        if (!outlineTarget)
        {
            h.Out($"HubInteractable '{name}': no Image to outline.");
            return;
        }

        outline = outlineTarget.GetComponent<Outline>();
        if (!outline) outline = outlineTarget.gameObject.AddComponent<Outline>();
        outline.effectDistance = outlineWidth;
        outline.useGraphicAlpha = true;
        ShowOutline(false, instant: true);
    }

    private void ShowOutline(bool show, bool instant = false)
    {
        if (!outline) return;

        outlineTween.Stop();
        Color hidden = outlineColor;
        hidden.a = 0f;

        if (instant || outlineFadeDuration <= 0f || !isActiveAndEnabled)
        {
            outline.effectColor = show ? outlineColor : hidden;
            outline.enabled = show;
            return;
        }

        // Hidden outlines are switched off, so idle objects don't pay for the extra geometry.
        outline.enabled = true;
        outlineTween = Tween.Custom(outline, outline.effectColor, show ? outlineColor : hidden, outlineFadeDuration,
            (o, c) => o.effectColor = c);
        if (!show) outlineTween.OnComplete(outline, o => o.enabled = false);
    }

    /// <summary>What a click does.</summary>
    protected abstract void Interact();

    /// <summary>Extra condition on top of the game state (e.g. the phone needs someone left to call).</summary>
    protected virtual bool IsAvailable() => true;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        TryInteract();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (CanInteract) SetHovered(true);
    }

    public void OnPointerExit(PointerEventData eventData) => SetHovered(false);

    /// <summary>Interacts if allowed. Also callable from a UnityEvent or a debug key.</summary>
    public void TryInteract()
    {
        if (!CanInteract) return;

        used = true;
        // Whatever the click opens covers the hub; the pointer won't leave the object to clear it.
        SetHovered(false);

        if (clickSfx && SFXManager.Instance) SFXManager.Instance.PlayClip(clickSfx);
        onInteract?.Invoke();
        Interact();
    }

    private void SetHovered(bool value)
    {
        if (hovered == value) return;
        hovered = value;

        SetCursorButtonHover(value);
        if (hoverHighlight) hoverHighlight.SetActive(value);
        ShowOutline(value);
        if (!visual) return;

        hoverTween.Stop();
        Vector3 target = value ? restScale * hoverScale : restScale;
        if (hoverDuration > 0f) hoverTween = Tween.Scale(visual, target, hoverDuration, Ease.OutQuad);
        else visual.localScale = target;

        if (value) onHoverEnter?.Invoke();
        else onHoverExit?.Invoke();
    }

    /// <summary>Tells the cursor the pointer is on / off a button (reference counted there, so calls must pair up).</summary>
    private void SetCursorButtonHover(bool value)
    {
        if (countsAsButton && CursorIconManager.Instance) CursorIconManager.Instance.SetButtonHover(value);
    }
}
