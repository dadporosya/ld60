using UnityEngine;
using UnityEngine.InputSystem;
using System;
using System.Collections.Generic;

public class InteractableTrigger : MonoBehaviour
{
    public IInteractable parent;
    [SerializeField] public List<string> targetTags = new List<string>() {"Player"};
    
    public void Init(Interactable interactable)
    {
        parent = interactable;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (targetTags.Contains(collision.tag))
        {
            parent.StartInteraction(collision.gameObject);
        }
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        if (targetTags.Contains(collision.tag))
        {
            parent.ContinuousInteraction(collision.gameObject);
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (targetTags.Contains(collision.tag))
        {
            parent.EndInteraction(collision.gameObject);
        }
    }
}