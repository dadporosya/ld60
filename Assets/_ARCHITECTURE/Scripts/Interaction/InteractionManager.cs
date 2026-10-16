using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class InteractionManager : MonoBehaviour
{
    public float duration = 0.2f;
    public float scaleMultiplier = 1f;
    public Vector3 translateVector = new Vector3(0, 0.5f, 0);
    public List<InteractionIcon> interactionIcons;
    [SerializeField] public GameObject interactionIconPrefab;

    public InteractionIcon GetFreeIcon()
    {
        foreach (var icon in interactionIcons)
        {
            if (!icon.isInteracting) return icon;
        }
        
        InteractionIcon newIcon = Instantiate(interactionIconPrefab, transform).GetComponent<InteractionIcon>();
        newIcon.manager = this;
        return newIcon;
    }

}
