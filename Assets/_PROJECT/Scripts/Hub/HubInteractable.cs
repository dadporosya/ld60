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
/// card pick running), and while a parent CanvasGroup is non-interactable.</para>
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

    [Header("Hover")]
    [Tooltip("What scales up on hover. Defaults to this transform.")]
    [SerializeField] private Transform visual;
    [SerializeField, Min(0f)] private float hoverScale = 1.06f;
    [SerializeField, Min(0f)] private float hoverDuration = 0.15f;
    [Tooltip("Optional: shown while hovered (outline, glow, label). Its Graphics stop being raycast " +
             "targets, so showing it never steals the hover from the object.")]
    [SerializeField] private GameObject hoverHighlight;

    [Header("Events")]
    public UnityEvent onHoverEnter = new UnityEvent();
    public UnityEvent onHoverExit = new UnityEvent();
    public UnityEvent onInteract = new UnityEvent();

    private Vector3 restScale;
    private Tween hoverTween;
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
        (!FuneralGameManager.Instance || FuneralGameManager.Instance.IsInHub) && CanvasGroupsAllow();

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
        hovered = false;
        if (visual) visual.localScale = restScale;
        if (hoverHighlight) hoverHighlight.SetActive(false);
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

        if (hoverHighlight) hoverHighlight.SetActive(value);
        if (!visual) return;

        hoverTween.Stop();
        Vector3 target = value ? restScale * hoverScale : restScale;
        if (hoverDuration > 0f) hoverTween = Tween.Scale(visual, target, hoverDuration, Ease.OutQuad);
        else visual.localScale = target;

        if (value) onHoverEnter?.Invoke();
        else onHoverExit?.Invoke();
    }
}
