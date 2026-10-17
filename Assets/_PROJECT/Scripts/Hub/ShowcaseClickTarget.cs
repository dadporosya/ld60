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

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) onClick?.Invoke();
    }

    public void OnPointerEnter(PointerEventData eventData) => onHover?.Invoke(true);

    public void OnPointerExit(PointerEventData eventData) => onHover?.Invoke(false);
}
