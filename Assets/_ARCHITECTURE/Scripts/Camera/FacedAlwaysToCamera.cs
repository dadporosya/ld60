using System.Collections.Generic;
using UnityEngine;

/// Driven from the shared <see cref="UpdateTicker"/>'s LateUpdate phase instead of its own
/// LateUpdate, and the axis locks are flattened to three bools in Awake so the per-frame path
/// stops doing three List.Contains scans.
public class FacedAlwaysToCamera : MonoBehaviour, ILateTickable
{
    public enum Axes
    {
        X, Y, Z
    }
    
    public List<Axes> lockedAxes;
    public Vector3 rotationOffs;

    [Tooltip("Camera to face. Falls back to Camera.main when left empty.")]
    [SerializeField] private Camera targetCamera;

    private bool _lockX, _lockY, _lockZ;
    private Transform _cachedCameraTransform;
    private Transform _self;

    private void Awake()
    {
        if (!targetCamera) targetCamera = Camera.main;
        if (targetCamera) _cachedCameraTransform = targetCamera.transform;
        _self = transform;
        CacheLocks();
    }

    private void CacheLocks()
    {
        _lockX = _lockY = _lockZ = false;
        if (lockedAxes == null) return;
        for (int i = 0; i < lockedAxes.Count; i++)
        {
            switch (lockedAxes[i])
            {
                case Axes.X: _lockX = true; break;
                case Axes.Y: _lockY = true; break;
                case Axes.Z: _lockZ = true; break;
            }
        }
    }

    /// <summary>Call after changing <see cref="lockedAxes"/> at runtime.</summary>
    public void RefreshLockedAxes() => CacheLocks();

    private void OnEnable() => UpdateTicker.RegisterLate(this);

    private void OnDisable() => UpdateTicker.UnregisterLate(this);

    private void OnDestroy() => UpdateTicker.UnregisterLate(this);

    public void LateTick()
    {
        if (!_cachedCameraTransform)
        {
            if (!targetCamera) targetCamera = Camera.main;
            if (!targetCamera) return;
            _cachedCameraTransform = targetCamera.transform;
        }

        if (!_self) _self = transform;

        // Rotation that makes this object face the camera, plus the configured offset.
        Vector3 currentEuler = _self.eulerAngles;
        Vector3 facingEuler = (Quaternion.LookRotation(_self.position - _cachedCameraTransform.position) * Quaternion.Euler(rotationOffs)).eulerAngles;

        // Keep the current angle on any locked axis, use the facing angle otherwise.
        Vector3 result = new Vector3(
            _lockX ? currentEuler.x : facingEuler.x,
            _lockY ? currentEuler.y : facingEuler.y,
            _lockZ ? currentEuler.z : facingEuler.z);

        _self.eulerAngles = result;
    }
}