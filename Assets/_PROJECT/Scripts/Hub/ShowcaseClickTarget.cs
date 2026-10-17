using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Forwards UI clicks and hovers on its Graphic to whoever set the callbacks - the
/// <see cref="ShowcaseDialogueManager"/>'s list lines and its fade image use it.
/// </summary>
public class ShowcaseClickTarget : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    public Action onClick;
    /// <summary>True on enter, false on exit.</summary>
    public Action<bool> onHover;
    /// <summary>Only clicks on this object's own Graphic count, not ones bubbling up from its children.</summary>
    public bool directHitsOnly;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (directHitsOnly && eventData.pointerCurrentRaycast.gameObject != gameObject) return;
        onClick?.Invoke();
    }

    public void OnPointerEnter(PointerEventData eventData) => onHover?.Invoke(true);

    public void OnPointerExit(PointerEventData eventData) => onHover?.Invoke(false);
}
