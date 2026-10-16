using System;
using PrimeTween;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;

public class DragableObject :
    MonoBehaviour,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler,
    IPointerEnterHandler,
    IPointerExitHandler,
    IDropHandler,
    IPointerClickHandler,
    IPointerDownHandler
{
    public enum State
    {
        Dragging,
        Static
    }

    [HideInInspector]
    public State state = State.Static;

    [Header("Hover")]
    [SerializeField] private float hoverScaleMultiplier = 1.15f;
    [SerializeField] private float hoverDuration = 0.12f;

    public RectTransform RectTransform { get; private set; }

    [SerializeField] private CanvasGroup canvasGroup;

    public Vector3 originalScale;
    
    public UnityEvent OnStartDragging = new UnityEvent();
    public UnityEvent OnRelease = new UnityEvent();

    public SlotBase currentSlot;
    [HideInInspector] public SlotBase previousSlot;

    // Where the object lived before the drag lifted it onto the canvas-wide drag layer. Restored on
    // release so the slot can align it in its own local space again, and so it goes back to the
    // same place in the draw order instead of permanently on top of the HUD.
    private Transform dragOriginalParent;
    private int dragOriginalSiblingIndex = -1;

    
    private void Awake()
    {
        RectTransform = GetComponent<RectTransform>();

        canvasGroup = GetComponent<CanvasGroup>();

        if (!canvasGroup)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        
        // h.Out(transform.localScale);
        originalScale = transform.localScale;
    }

    public virtual void Start()
    {
        
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (!currentSlot)
            return;

        DragableObject dragged =
            eventData.pointerDrag.GetComponent<DragableObject>();

        if (!dragged || dragged == this)
            return;

        currentSlot.PlacingObject(dragged);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        CursorDnDManager.Instance.StartDragging(this);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        ;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        CursorDnDManager.Instance.SetSuccessfulClick();
    }

    public void OnDrag(PointerEventData eventData)
    {
    }

    public void OnEndDrag(PointerEventData eventData)
    {
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (state == State.Dragging)
            return;

        CursorDnDManager.Instance.SetHover(this);

        OnHoverEnter();
    }

    public virtual void OnHoverEnter()
    {
        
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (state == State.Dragging)
            return;

        CursorDnDManager.Instance.ClearHover();

        OnHoverExit();
    }
    
    public virtual void OnHoverExit()
    {
        
    }

    
    

    public virtual bool StartDragging()
    {
        if (currentSlot)
        {
            if (!currentSlot.RemovingCondition())
            {
                h.Out("cannot be removed");
                return false;
            }
            
            previousSlot = currentSlot;

            currentSlot.RemoveObject();

            currentSlot = null;
        }
        
        state = State.Dragging;

        canvasGroup.blocksRaycasts = false;

        MoveToDragLayer();

        transform.SetAsLastSibling();

        OnStartDragging?.Invoke();

        return true;
    }

    public virtual void Release()
    {
        state = State.Static;

        canvasGroup.blocksRaycasts = true;

        // Home first, then align. The slot positions its contents in LOCAL space, so it has to be
        // talking about the parent the object is actually going to live under.
        ReturnFromDragLayer();

        // Clears the pick-up scale sequence, and any alignment a slot already started from OnDrop —
        // the EventSystem fires that before this, so it was aimed at the drag layer rather than at
        // the parent the object has now. Whichever branch follows starts the real one.
        Tween.StopAll(transform);

        if (!currentSlot && previousSlot)
        {
            previousSlot.PlaceObject(this);
            currentSlot = previousSlot;
        }
        else if (currentSlot)
        {
            currentSlot.AlignContainingObject();
        }

        OnRelease?.Invoke();
        
        
    }

    /// <summary>
    /// Lifts the object out of its slot and onto the canvas for the duration of the drag: it draws
    /// above the whole HUD instead of only above its own slot's children, and its anchoredPosition
    /// becomes a plain canvas coordinate.
    /// </summary>
    private void MoveToDragLayer()
    {
        RectTransform dragLayer =
            CursorDnDManager.Instance ? CursorDnDManager.Instance.DragLayer : null;

        if (!dragLayer || transform.parent == dragLayer)
            return;

        dragOriginalParent = transform.parent;
        dragOriginalSiblingIndex = transform.GetSiblingIndex();

        transform.SetParent(dragLayer, worldPositionStays: true);
    }

    /// <summary>Puts the object back under the parent (and at the depth) it was picked up from.</summary>
    private void ReturnFromDragLayer()
    {
        if (!dragOriginalParent)
        {
            dragOriginalParent = null;
            dragOriginalSiblingIndex = -1;
            return;
        }

        Transform parent = dragOriginalParent;
        int siblingIndex = dragOriginalSiblingIndex;

        dragOriginalParent = null;
        dragOriginalSiblingIndex = -1;

        transform.SetParent(parent, worldPositionStays: true);

        if (siblingIndex >= 0)
            transform.SetSiblingIndex(Mathf.Min(siblingIndex, parent.childCount - 1));
    }

    protected virtual void OnDisable()
    {
        // A slot torn down mid-drag would otherwise leave the object orphaned on the drag layer,
        // which outlives the slot and would keep it alive and visible over the HUD forever.
        ReturnFromDragLayer();
    }

    public void OnHoverStart()
    {
        
    }

    public void OnHoverEnd()
    {
        
    }
}
