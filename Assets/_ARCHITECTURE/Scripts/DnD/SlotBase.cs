using System;
using UnityEngine;
using UnityEngine.Events;
using PrimeTween;
using UnityEngine.EventSystems;

public class SlotBase : MonoBehaviour,
    IDropHandler,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler,
    IPointerDownHandler
{
    public enum State
    {
        Locked, 
        NotLocked
    }
    public State state = State.NotLocked;

    public enum PlacingType
    {
        Swap,
        Strict
    }

    public PlacingType placingType = PlacingType.Strict;
    
    public DragableObject containingObject;
    
    public UnityEvent OnPlacedObjectEvent = new UnityEvent();
    public UnityEvent OnRemovedObjectEvent = new UnityEvent();

    public virtual void Awake()
    {
        PlaceObject(containingObject);
    }

    private void Start()
    {
        
    }
    public void OnDrop(PointerEventData eventData)
    {
        DragableObject obj =
            eventData.pointerDrag.GetComponent<DragableObject>();

        // h.Out("dropeed");
        
        if (obj)
        {
            PlacingObject(obj);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
    }
    
    public virtual bool PlacingCondition(DragableObject draggingObject=null)
    {
        if (state == State.Locked) return false;
        if (containingObject && placingType != PlacingType.Swap) return false;
        if (containingObject && draggingObject && draggingObject.previousSlot
             && draggingObject.previousSlot.placingType != PlacingType.Swap
            ) return false;
        return true;
    }

    public virtual bool RemovingCondition()
    {
        if (state == State.Locked) return false;
        return true;
    }

    public virtual void PlacingObject(DragableObject draggingObject)
    {
        if (!draggingObject) return;
        
        if (PlacingCondition(draggingObject))
        {
            PlaceObject(draggingObject);
            return;
        }
        
        RejectPlaceObject(draggingObject);
    }
    
    public virtual void PlaceObject(DragableObject draggingObject)
    {
        if (!draggingObject)
        {
            // h.Out("NO OBJECT TO PLACE");
            return;
        }
        // h.Out("PLACING " + name
        //     ,placingType == PlacingType.Swap
        //       ,containingObject
        //       ,draggingObject.previousSlot);
        
        if (placingType == PlacingType.Swap
            && containingObject
            && draggingObject.previousSlot)
        {
            if (!draggingObject.previousSlot.PlacingCondition(containingObject))
            {
                // h.Out("swaping object conditions unmatch'");
                return;
            }
            // h.Out("OK SWAP");
            SlotBase secondSlot = draggingObject.previousSlot;
            secondSlot.containingObject = null;
            secondSlot.PlaceObject(containingObject);
        }
        
        containingObject = draggingObject;
        containingObject.currentSlot = this;
        AlignContainingObject();
        
        OnPlacedObjectEvent.Invoke();
        
        h.Out("Placed successfully");
        
    }

    /// <summary>
    /// Slides whatever the slot is holding back into place.
    ///
    /// <para>Everything here is LOCAL, on purpose. The HUD hangs on a Screen Space - Camera canvas
    /// sitting half a unit in front of the RTS camera, so its world position changes every single
    /// frame the camera pans, leans, zooms or shakes. A world-space tween bakes its destination the
    /// moment it starts, and two thirds of a second later that destination is somewhere behind the
    /// player — which is exactly how a released item came home and then vanished off the screen.
    /// A local destination moves with the canvas and stays correct.</para>
    /// </summary>
    public void AlignContainingObject(DragableObject newObject=null)
    {
        if (newObject) containingObject = newObject;
        if (!containingObject) return;

        float duration = 0.67f;

        Transform objectTransform = containingObject.transform;

        // The usual case: the icon is a child of its slot, so "in place" is simply the origin.
        bool childOfSlot = objectTransform.parent == transform;

        Vector3 localTarget;
        if (childOfSlot)
            localTarget = Vector3.zero;
        else if (objectTransform.parent)
            localTarget = objectTransform.parent.InverseTransformPoint(transform.position);
        else
            localTarget = transform.position;

        Tween.LocalPosition(
            target: objectTransform,
            endValue: localTarget,
            duration: duration,
            ease: Ease.OutQuad
            );

        // A child already inherits the slot's scale; multiplying by it a second time compounded it
        // a little further every time the object was picked up and put back.
        Vector3 scaleTarget = childOfSlot
            ? containingObject.originalScale
            : h.MultiplyVectors(containingObject.originalScale, transform.localScale);

        Tween.Scale(
            objectTransform,
            endValue: scaleTarget,
            duration: duration);
    }
    
    public virtual void RejectPlaceObject(DragableObject draggingObject)
    {
        if (draggingObject.currentSlot)
        {
            draggingObject.currentSlot.AlignContainingObject();
        }
    }

    public virtual void RemoveObject()
    {
        OnRemovedObjectEvent.Invoke();
        
        containingObject = null;
        
    }

    public virtual void RejectRemoveObject()
    {
        
    }
    
    public void OnPointerDown(PointerEventData eventData)
    {
        CursorDnDManager.Instance.SetSuccessfulClick();
    }

    public virtual void OnPointerEnter(PointerEventData eventData)
    {
        OnHoverEnter(eventData);
    }

    public virtual void OnHoverEnter(PointerEventData eventData=null)
    {
        
    }

    public virtual void OnPointerExit(PointerEventData eventData)
    {
        OnHoverExit(eventData);
    }

    public virtual void OnHoverExit(PointerEventData eventData=null)
    {
        // Empty function called when the cursor stops hovering over the object
    }
}
