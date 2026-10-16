using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Put this on anything that should show the button cursor but is not an interactable
/// <see cref="UnityEngine.UI.Selectable"/> (custom widgets, world-space clickables with a collider).
/// UI Buttons are detected automatically by <see cref="CursorIconManager"/> and do not need it.
/// </summary>
public class CursorButtonHoverTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private bool hovering;

    public void OnPointerEnter(PointerEventData eventData) => SetHover(true);
    public void OnPointerExit(PointerEventData eventData) => SetHover(false);

    private void OnMouseEnter() => SetHover(true);
    private void OnMouseExit() => SetHover(false);

    private void OnDisable() => SetHover(false);

    private void SetHover(bool value)
    {
        if (hovering == value) return;
        hovering = value;

        if (CursorIconManager.Instance) CursorIconManager.Instance.SetButtonHover(value);
    }
}
