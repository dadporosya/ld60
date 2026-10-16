using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;


public class Interactable : MonoBehaviour, IInteractable
{
    public GameObject interactableArea;
    public float interactableRadiusScale=1f;
    public UnityEvent onInteract;

    private GlobalEventManager eventManager;
    public InteractionManager interactionManager;
    // private Action<InputAction.CallbackContext> interactCallback;
    public InteractionIcon interactionIcon;

    // private int stackTest=0;
    void Start()
    {
        interactionManager =  FindFirstObjectByType<InteractionManager>();
        
        // h.Out(interactionManager.interactionIconPrefab);
        InteractionIcon iIconPfb = interactionManager.interactionIconPrefab.GetComponent<InteractionIcon>();
        // h.Out(iIconPfb);
        if (!iIconPfb.affectedByStack) AssignInteractionComponents();
        else interactionIcon = iIconPfb;
        // h.Out(interactionIcon);
        
        if (!interactableArea) interactableArea = h.FindChildrenWithTag(transform, "InteractableArea");
        
        interactableArea.GetComponent<InteractableTrigger>().Init(this);
        interactableArea.transform.localScale *= interactableRadiusScale;

        if (!eventManager) eventManager = FindFirstObjectByType<GlobalEventManager>();

        // interactCallback = OnInteractPerformed;
    }

    private void OnDisable()
    {
        UnassignInteraction();
    }

    public void OnInteract()
    {
        onInteract?.Invoke();
    }

    public void AssignInteraction(UnityEvent newEvent = null)
    {
        if (newEvent != null)
        {
            onInteract = newEvent;
        }

        eventManager.OnInteractPressed += OnInteract;
        
    }

    public void UnassignInteraction()
    {
        try
        {
            eventManager.OnInteractPressed -= OnInteract;
        }
        catch (Exception e)
        { 
            h.Out(e);
        }
    }
    
    public void StartInteraction(GameObject target=null)
    {
        AssignInteraction();
        if (!interactionIcon.affectedByStack) AssignInteractionComponents();
        // Debug.Log("sub: "+ gameObject.name);
        interactionIcon.StartInteractionAnim(transform);
    }

    public void EndInteraction(GameObject target=null)
    {
        UnassignInteraction();
        if (!interactionIcon.affectedByStack) AssignInteractionComponents();
        // Debug.Log("un: "+ gameObject.name);
        interactionIcon.EndInteractionAnim(transform);
    }

    void AssignInteractionComponents()
    {
        if (!interactionManager) interactionManager = FindFirstObjectByType<InteractionManager>();
        if (!interactionIcon) interactionIcon = interactionManager.GetFreeIcon();
    }

}
