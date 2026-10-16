using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Put this on UI elements (such as scrollbars, ScrollRects, sliders, or draggable widgets)
/// to notify <see cref="CursorIconManager"/> when the player is dragging them,
/// causing the cursor icon to switch to the drag icon.
/// </summary>
public class CursorDragTarget : MonoBehaviour,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler
{
    [Tooltip("If true, dragging this element reports an active drag state to CursorIconManager.")]
    [SerializeField] private bool enabledDragCursor = true;

    private bool isDragging;

    public bool IsDragging => isDragging;

    public void OnBeginDrag(PointerEventData eventData)
    {
        SetDragging(true);
        ForwardDragToParent(eventData, ExecuteEvents.beginDragHandler);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging)
        {
            SetDragging(true);
        }
        ForwardDragToParent(eventData, ExecuteEvents.dragHandler);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        SetDragging(false);
        ForwardDragToParent(eventData, ExecuteEvents.endDragHandler);
    }

    /// <summary>
    /// If this component is attached to a child element without its own ScrollRect/Scrollbar,
    /// forwards drag events up the hierarchy so parent scroll systems never stop receiving drags.
    /// </summary>
    private void ForwardDragToParent<T>(PointerEventData eventData, ExecuteEvents.EventFunction<T> function) where T : IEventSystemHandler
    {
        if (!GetComponent<ScrollRect>() && !GetComponent<Scrollbar>() && transform.parent)
        {
            ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, eventData, function);
        }
    }

    private void Update()
    {
        // Fallback: If mouse button was released outside window bounds or without pointer-up triggering, end drag
        if (isDragging && !Input.GetMouseButton(0))
        {
            SetDragging(false);
        }
    }

    private void OnDisable()
    {
        SetDragging(false);
    }

    public void SetDragging(bool dragging)
    {
        if (!enabledDragCursor && dragging) return;
        if (isDragging == dragging) return;

        isDragging = dragging;

        if (CursorIconManager.Instance)
        {
            CursorIconManager.Instance.SetExternalDrag(dragging);
        }
    }
}
