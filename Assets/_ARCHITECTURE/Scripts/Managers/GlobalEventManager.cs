using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class GlobalEventManager : MonoBehaviour
{

    [SerializeField] private InputActionReference interactAction;

    public event Action OnInteractPressed;

    private void OnEnable()
    {
        interactAction.action.performed += HandleInteract;
        interactAction.action.Enable();
    }

    private void OnDisable()
    {
        interactAction.action.performed -= HandleInteract;
        interactAction.action.Disable();
    }

    private void HandleInteract(InputAction.CallbackContext ctx)
    {
        OnInteractPressed?.Invoke();
    }
}