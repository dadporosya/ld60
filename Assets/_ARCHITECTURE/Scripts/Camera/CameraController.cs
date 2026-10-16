using System;
using UnityEngine;
using PrimeTween;

public class CameraController : MonoBehaviour
{
    public static CameraController Instance;

    [HideInInspector] public CameraFlowTargeting cameraFlowTargeting;

    public Camera cam;
    private Tween zoomTween;

    [HideInInspector] public float initialZoom;

    [SerializeField] private float minZoom = 3f;
    [SerializeField] private float maxZoom = 10f;

    [Header("Zoom")]
    [SerializeField] private float defaultZoomDuration = 0.25f;

    private void Start()
    {
        h.CreateStaticInstance(this, ref Instance);

        if (!cameraFlowTargeting)
            cameraFlowTargeting = transform.parent.GetComponent<CameraFlowTargeting>();

        if (!cam)
            cam = GetComponent<Camera>();

        initialZoom = GetZoom();
    }

    public void SetTarget(Transform target)
    {
        cameraFlowTargeting.target = target;
    }

    /// <summary>
    /// Smoothly sets camera zoom level.
    /// </summary>
    public void SetZoom(
        float zoomSize,
        bool instant = false,
        float zoomDuration=-1,
        Ease zoomEase = Ease.OutCubic)
    {
        if (!cam)
            cam = GetComponent<Camera>();
        
        if (zoomDuration < 0) zoomDuration = defaultZoomDuration;

        zoomSize = Mathf.Clamp(zoomSize, minZoom, maxZoom);

        // Stop previous tween
        if (zoomTween.isAlive)
            zoomTween.Stop();

        if (instant)
        {
            cam.orthographicSize = zoomSize;
            return;
        }

        zoomTween = Tween.Custom(
            cam.orthographicSize,
            zoomSize,
            zoomDuration,
            value => cam.orthographicSize = value,
            ease: zoomEase
        );
    }

    /// <summary>
    /// Smooth zoom in/out.
    /// Positive = zoom in.
    /// Negative = zoom out.
    /// </summary>
    public void Zoom(float zoomDelta, bool instant = false,float zoomDuration=-1)
    {
        if (!cam)
            cam = GetComponent<Camera>();

        SetZoom(cam.orthographicSize - zoomDelta, instant, zoomDuration: zoomDuration);
    }

    public void SetInitialZoom(bool instant=false)
    {
        SetZoom(initialZoom, instant);
    }

    /// <summary>
    /// Current zoom level.
    /// </summary>
    public float GetZoom()
    {
        if (!cam)
            cam = GetComponent<Camera>();

        return cam.orthographicSize;
    }
}