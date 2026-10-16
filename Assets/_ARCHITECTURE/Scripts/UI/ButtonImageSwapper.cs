using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Swaps the button's sprite depending on pointer state:
/// pressed > hovered > normal. Works with a UI <see cref="Image"/> or a
/// world-space <see cref="SpriteRenderer"/>.
/// If a <see cref="Button"/> is present, non-interactable state falls back to the normal sprite.
/// </summary>
[DisallowMultipleComponent]
public class ButtonImageSwapper : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler
{
    [Tooltip("Default sprite. If left empty, the sprite present at Awake is used.")]
    public Sprite normalSprite;

    [Tooltip("Sprite shown while the pointer is over the button.")]
    public Sprite hoveredSprite;

    [Tooltip("Sprite shown while the button is held down. Takes priority over hovered.")]
    public Sprite pressedSprite;

    [Tooltip("Resize the Image to the sprite's native size after every swap.")]
    public bool setNativeSize = false;

    private Image image;
    private SpriteRenderer spriteRenderer;
    private Button button;

    private bool isHovered;
    private bool isPressed;

    private void Awake()
    {
        image = GetComponent<Image>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        button = GetComponent<Button>();

        if (normalSprite == null) normalSprite = CurrentSprite;
    }

    private void OnEnable()
    {
        isHovered = false;
        isPressed = false;
        Refresh();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovered = true;
        Refresh();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        Refresh();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        isPressed = true;
        Refresh();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        isPressed = false;
        Refresh();
    }

    /// <summary>Re-applies the sprite that matches the current state.</summary>
    public void Refresh()
    {
        Sprite target = normalSprite;

        if (button == null || button.interactable)
        {
            if (isPressed && pressedSprite != null) target = pressedSprite;
            else if (isHovered && hoveredSprite != null) target = hoveredSprite;
        }

        if (target == null || target == CurrentSprite) return;

        CurrentSprite = target;
        if (setNativeSize && image != null) image.SetNativeSize();
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
