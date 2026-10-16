using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// A button that swaps its sprite to <see cref="hoveredSprite"/> while the pointer
/// is over it, and restores the original sprite when the pointer leaves.
/// On a successful click it plays a random SFX and fires its OnClick animation.
/// Works with a UI <see cref="Image"/> or a world-space <see cref="SpriteRenderer"/>.
/// </summary>
public class SmartButton : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler
{
    [Tooltip("Sprite shown while the button is hovered.")]
    public Sprite hoveredSprite;

    [Tooltip("One of these is played (at random) through the SFXManager on a successful click.")]
    public List<AudioClip> onClickSounds = new List<AudioClip>();

    private Image image;
    private SpriteRenderer spriteRenderer;
    private Sprite initialSprite;

    // The OnClick animation controller found on this object or its children.
    private AnimationControllerBase onClickAnimController;

    private void Awake()
    {
        image = GetComponent<Image>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        initialSprite = CurrentSprite;

        foreach (AnimationControllerBase controller in GetComponentsInChildren<AnimationControllerBase>(true))
        {
            if (controller.type != AnimationPreferences.Type.OnClick) continue;
            onClickAnimController = controller;
            break;
        }

        if (TryGetComponent<Button>(out var btn))
        {
            btn.onClick.AddListener(OnClick);
        }
        
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (hoveredSprite != null) CurrentSprite = hoveredSprite;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        CurrentSprite = initialSprite;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // OnClick();
    }

    private void OnClick()
    {
        if (onClickSounds != null && onClickSounds.Count > 0 && SFXManager.Instance != null)
            SFXManager.Instance.PlayRandomClipIndependently(onClickSounds);

        if (onClickAnimController != null)
            onClickAnimController.StartCoroutine(onClickAnimController.PlayAnimations());
    }

    private Sprite CurrentSprite
    {
        get => image != null ? image.sprite : spriteRenderer != null ? spriteRenderer.sprite : null;
        set
        {
            if (image != null) image.sprite = value;
            else if (spriteRenderer != null) spriteRenderer.sprite = value;
        }
    }
}
