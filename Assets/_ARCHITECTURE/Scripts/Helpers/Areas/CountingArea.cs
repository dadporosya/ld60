using System;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Events;

public class CountingArea : AreaBase
{
    public List<string> targetTags = new List<string>();
    public List<string> targetTypes = new List<string>();
    
    private HashSet<GameObject> trackedObjects = new HashSet<GameObject>();
    private Collider2D triggerCollider;
    
    public GameObjectEvent onTargetEntered;
    public GameObjectEvent onTargetExited;

    public override void Init() // runs in Start
    {
        base.Init();
        triggerCollider = GetComponent<Collider2D>();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        Add(other.gameObject);
        onTargetEntered?.Invoke(other.gameObject);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        Remove(other.gameObject);
        onTargetExited?.Invoke(other.gameObject);

    }

    public bool Add(GameObject obj)
    {
        if (!IsValidObject(obj)) return false;
        trackedObjects.Add(obj);
        return true;
    }

    public bool Remove(GameObject obj)
    {
        return trackedObjects.Remove(obj);
    }

    private bool IsValidObject(GameObject obj)
    {
        // Check if object has any of the target component types
        if (targetTypes.Count > 0)
        {
            foreach (string componentTypeName in targetTypes)
            {
                if (string.IsNullOrEmpty(componentTypeName))
                    continue;
                    
                System.Type type = System.Type.GetType(componentTypeName);
                if (type == null)
                {
                    Debug.LogWarning($"Type {componentTypeName} not found!");
                    continue;
                }
                
                if (obj.GetComponent(type) != null)
                    return true;
            }
            return false;
        }
        
        // Check if object has any of the target tags
        if (targetTags.Count > 0)
        {
            foreach (string tag in targetTags)
            {
                if (!string.IsNullOrEmpty(tag) && obj.CompareTag(tag))
                    return true;
            }
            return false;
        }
        
        return true;
    }

    

    public int GetObjectCount()
    {
        // Clean up destroyed objects
        trackedObjects.RemoveWhere(obj => obj == null);
        return trackedObjects.Count;
    }

    /// <summary>
    /// Returns the current number of valid targets in the zone.
    /// </summary>
    public int GetCurrentTargetCount()
    {
        // Clean up destroyed objects
        trackedObjects.RemoveWhere(obj => obj == null);
        return trackedObjects.Count;
    }

    public HashSet<GameObject> GetTrackedObjects()
    {
        // Clean up destroyed objects
        trackedObjects.RemoveWhere(obj => obj == null);
        return new HashSet<GameObject>(trackedObjects);
    }

    public void ClearTrackedObjects()
    {
        trackedObjects.Clear();
    }
}
