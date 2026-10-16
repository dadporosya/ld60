using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Slightly shifts the object it is attached to toward (or away from) the cursor.
/// Works for both UI (RectTransform under a Canvas) and world-space objects — the mode is
/// auto-detected, so a single component covers both cases.
///
/// The offset is applied as a *delta* on top of whatever else moves the transform
/// (tweens, wandering, layout), so it never overwrites other movement.
/// </summary>
[AddComponentMenu("Cursor/Cursor Parallax")]
[DisallowMultipleComponent]
public class CursorParallax : MonoBehaviour
{
    public enum Mode
    {
        Auto,   // UI when this is a RectTransform, World otherwise
        UI,
        World
    }

    // What the cursor direction is measured from.
    public enum Origin
    {
        ObjectPosition, // direction from the object's own resting screen position to the cursor
        ScreenCenter    // direction from the screen centre to the cursor (classic layered parallax)
    }

    // Which world axes the screen-space direction is mapped onto.
    public enum WorldPlane
    {
        CameraFacing, // camera right/up — for billboards, sprites, anything facing the camera
        GroundXZ      // world XZ ground plane (project convention: X = right, Z = forward, Y stays fixed)
    }

    [Header("Setup")]
    [Tooltip("UI / World handling. Auto picks UI for RectTransforms and World for everything else.")]
    public Mode mode = Mode.Auto;
    [Tooltip("Camera used for World mode and for Screen Space - Camera canvases. " +
             "Falls back to the canvas camera, then CameraController.Instance.cam, then Camera.main.")]
    public Camera targetCamera;
    [Tooltip("World mode only: which plane the offset is built on.")]
    public WorldPlane worldPlane = WorldPlane.CameraFacing;

    [Header("Motion")]
    [Tooltip("Maximum distance the object is displaced. World mode: world units. UI mode: canvas units " +
             "(≈ pixels of the CanvasScaler reference resolution).")]
    public float maxOffset = 0.25f;
    [Tooltip("Cursor distance, as a fraction of screen height, at which the full maxOffset is reached. " +
             "Smaller = reacts harder to small cursor movement.")]
    public float radius = 0.5f;
    [Tooltip("Clamp the offset to maxOffset. Off = the object keeps drifting the further the cursor goes.")]
    public bool clampToMaxOffset = true;
    [Tooltip("Move away from the cursor instead of toward it. Useful for background layers.")]
    public bool invert;
    [Tooltip("Where the direction to the cursor is measured from.")]
    public Origin origin = Origin.ObjectPosition;
    [Tooltip("Per-axis scale of the screen-space direction. (1,0) = horizontal only, (0,1) = vertical only.")]
    public Vector2 axisMultiplier = Vector2.one;
    [Tooltip("Deadzone as a fraction of screen height. Cursor movement inside it produces no offset.")]
    public float deadZone = 0f;

    [Header("Smoothing")]
    [Tooltip("Approximate time to catch up with the cursor. 0 = instant, snappy. ~0.1-0.3 = soft follow.")]
    public float smoothTime = 0.12f;
    [Tooltip("Ignore Time.timeScale, so the effect keeps running while the game is paused. Recommended for UI.")]
    public bool useUnscaledTime = true;
    [Tooltip("Ease back to the resting position while the cursor is outside the game view.")]
    public bool returnWhenCursorOffScreen = true;

    // Offset currently baked into the transform — only its delta is ever applied.
    private Vector3 _appliedOffset;
    private Vector3 _currentOffset;
    private Vector3 _velocity;

    private RectTransform _rect;
    private Canvas _canvas;
    private bool _isUI;

    private void Awake()
    {
        _rect = transform as RectTransform;
        _isUI = mode == Mode.UI || (mode == Mode.Auto && _rect != null);
        if (_isUI && _rect == null) _isUI = false; // UI mode without a RectTransform is meaningless
        if (_isUI) _canvas = GetComponentInParent<Canvas>();
    }

    private void OnEnable()
    {
        _appliedOffset = Vector3.zero;
        _currentOffset = Vector3.zero;
        _velocity = Vector3.zero;
    }

    private void OnDisable()
    {
        // Give the transform back exactly what we took, so nothing drifts.
        ApplyOffset(Vector3.zero);
        _currentOffset = Vector3.zero;
        _velocity = Vector3.zero;
    }

    // LateUpdate so the parallax is layered on top of movement done in Update.
    private void LateUpdate()
    {
        Vector3 target = Vector3.zero;

        if (TryGetPointerPosition(out Vector2 pointer) && (!returnWhenCursorOffScreen || IsOnScreen(pointer)))
            target = CalculateOffset(pointer);

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        if (smoothTime <= 0f || dt <= 0f)
            _currentOffset = target;
        else
            _currentOffset = Vector3.SmoothDamp(_currentOffset, target, ref _velocity, smoothTime, Mathf.Infinity, dt);

        ApplyOffset(_currentOffset);
    }

    /// <summary>Screen-space direction to the cursor, turned into a local-space offset.</summary>
    private Vector3 CalculateOffset(Vector2 pointer)
    {
        Vector2 originScreen = GetOriginScreenPosition();

        // Normalise by screen height so the effect is resolution independent.
        Vector2 delta = (pointer - originScreen) / Mathf.Max(1f, Screen.height);

        float distance = delta.magnitude;
        if (distance <= deadZone) return Vector3.zero;
        if (deadZone > 0f) delta *= (distance - deadZone) / distance;

        Vector2 amount = delta / Mathf.Max(0.0001f, radius);
        if (clampToMaxOffset) amount = Vector2.ClampMagnitude(amount, 1f);

        amount = new Vector2(amount.x * axisMultiplier.x, amount.y * axisMultiplier.y) * maxOffset;
        if (invert) amount = -amount;

        return _isUI ? new Vector3(amount.x, amount.y, 0f) : WorldOffsetToLocal(amount);
    }

    /// <summary>Maps a screen-space offset onto world axes, then into this transform's local space.</summary>
    private Vector3 WorldOffsetToLocal(Vector2 amount)
    {
        Camera cam = ResolveCamera();
        if (!cam) return Vector3.zero;

        Vector3 right;
        Vector3 up;

        if (worldPlane == WorldPlane.GroundXZ)
        {
            // Screen X -> camera right on the ground, screen Y -> camera forward on the ground. Y stays fixed.
            right = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized;
            up = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
            if (up.sqrMagnitude < 0.0001f) up = Vector3.forward;
        }
        else
        {
            right = cam.transform.right;
            up = cam.transform.up;
        }

        Vector3 world = right * amount.x + up * amount.y;

        // localPosition lives in the parent's space, so convert before applying.
        return transform.parent ? transform.parent.InverseTransformVector(world) : world;
    }

    /// <summary>Screen position the direction is measured from (the object's *resting* position, not the offset one).</summary>
    private Vector2 GetOriginScreenPosition()
    {
        if (origin == Origin.ScreenCenter)
            return new Vector2(Screen.width, Screen.height) * 0.5f;

        // Undo the offset we are currently applying, otherwise the object chases itself.
        Vector3 appliedWorld = transform.parent
            ? transform.parent.TransformVector(_appliedOffset)
            : _appliedOffset;
        Vector3 restWorld = transform.position - appliedWorld;

        if (_isUI)
        {
            Camera uiCam = (_canvas && _canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                ? _canvas.worldCamera
                : null;
            return RectTransformUtility.WorldToScreenPoint(uiCam, restWorld);
        }

        Camera cam = ResolveCamera();
        if (!cam) return new Vector2(Screen.width, Screen.height) * 0.5f;
        return cam.WorldToScreenPoint(restWorld);
    }

    private Camera ResolveCamera()
    {
        if (targetCamera) return targetCamera;
        if (_isUI && _canvas && _canvas.worldCamera) return _canvas.worldCamera;
        if (CameraController.Instance && CameraController.Instance.cam) return CameraController.Instance.cam;
        return Camera.main;
    }

    // Applies the delta only, so tweens/layout moving this transform are preserved.
    private void ApplyOffset(Vector3 target)
    {
        Vector3 delta = target - _appliedOffset;
        if (delta == Vector3.zero) return;

        if (_isUI && _rect) _rect.anchoredPosition3D += delta;
        else transform.localPosition += delta;

        _appliedOffset = target;
    }

    private static bool TryGetPointerPosition(out Vector2 position)
    {
        if (Mouse.current != null)
        {
            position = Mouse.current.position.ReadValue();
            return true;
        }

        if (Pointer.current != null)
        {
            position = Pointer.current.position.ReadValue();
            return true;
        }

        position = Vector2.zero;
        return false;
    }

    private static bool IsOnScreen(Vector2 p) =>
        p.x >= 0f && p.x <= Screen.width && p.y >= 0f && p.y <= Screen.height;

    /// <summary>Snaps the object back to its resting position and clears the accumulated offset.</summary>
    public void ResetOffset()
    {
        ApplyOffset(Vector3.zero);
        _currentOffset = Vector3.zero;
        _velocity = Vector3.zero;
    }
}
