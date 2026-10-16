using System.Collections.Generic;
using UnityEngine;

public class Pickable : MonoBehaviour
{
    public delegate void PickUpHandler(GameObject go);

    public PickUpHandler onPickUp;

    public List<string> interactableTags;

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (interactableTags.Contains(collision.tag))
        {
            Interact(collision.gameObject);
        }
    }

    public void Interact(GameObject go)
    {
        onPickUp?.Invoke(go);
    }
}