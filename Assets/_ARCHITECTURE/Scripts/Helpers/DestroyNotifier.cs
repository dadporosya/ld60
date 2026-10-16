using System;
using UnityEngine;
using UnityEngine.Events;

public class DestroyNotifier : MonoBehaviour
{
    public UnityEvent onDestroyed =  new UnityEvent();
    public void AddOnDestroyListener(UnityAction action)
    {
        onDestroyed.AddListener(action);
    }

    private void OnDestroy()
    {
        onDestroyed?.Invoke();
    }
}
