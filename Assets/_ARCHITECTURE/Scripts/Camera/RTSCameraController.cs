using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// RTS-style camera control:
/// - Panning via screen edges and/or WASD/Arrow keys.
/// - Zoom via mouse wheel.
/// - Movement rotation: the camera rig tilts slightly while the camera is moving.
///
/// The movement tilt is applied to <see cref="cameraParent"/> (the rig root), NOT to
/// <see cref="mainCamera"/>. That leaves the camera's own local rotation free, so an
/// EZ CameraShaker (or any other effect) can drive <c>mainCamera.localRotation</c> and
/// its shake composes on top of the rig lean instead of being overwritten every frame.
///
/// EXECUTION ORDER (do not remove the attribute): this component must finish writing the camera
/// pose before anything raycasts through the camera. Unity orders same-priority scripts by
/// registration, and disabling then re-enabling a behaviour - which
/// <see cref="CinematicMapSpotlightsCutscene"/> does to every controller - re-registers it at the
/// END of the update list. That silently moved this script after CursorPathManager, which then cast
/// its cursor rays from the previous frame's un-zoomed pose while the frame rendered from the zoomed
/// one, so clicks and drags landed away from the cursor by exactly the zoom offset.
/// </summary>
[DefaultExecutionOrder(-200)]
public class RTSCameraController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform cameraParent;
    [SerializeField] private Transform mainCamera;

    [Header("Panning")]
    [SerializeField] private float panSpeed = 30f;

    [Tooltip("Movement speed multiplier while Shift is held.")]
    [SerializeField] private float acceleration = 2f;

    [Tooltip("Enable camera movement by placing the cursor near the screen edge.")]
    [SerializeField] private bool enableCursorMovement = true;

    [Tooltip("Pan when the cursor is near a screen edge.")]
    [SerializeField] private bool enableEdgePan = true;

    [SerializeField] private float edgeSize = 20f;
    [SerializeField] private bool requireCursorInWindow = true;

    [Tooltip("Pan with WASD and the arrow keys.")]
    [SerializeField] private bool enableKeyboardPan = true;

    [Header("Zoom")]
    [SerializeField] private float zoomStep = 2f;
    [SerializeField] private float zoomSmoothing = 10f;
    [SerializeField] private float maxZoom = -10f;
    [SerializeField] private float minZoom = 20f;
    [SerializeField] private float initialZoom = 40f;

    [Tooltip("Ignore the wheel while the cursor is over UI that scrolls itself (the upgrades list, " +
             "any other Scroll View). Off = the wheel zooms the camera even while scrolling a list.")]
    [SerializeField] private bool blockZoomOverScrollableUI = true;
    
    [Header("Map bounds")]
    [Tooltip("Keep the camera inside the map. Off = the camera pans forever in every direction, the " +
             "way it did before bounds existed.")]
    [SerializeField] private bool enableBounds = true;

    [Tooltip("The map's mesh. Its world-space renderer bounds are the area the camera may not leave — " +
             "drop the ground/map object in here and nothing else is needed.")]
    [SerializeField] private Renderer mapMesh;

    [Tooltip("Used when Map Mesh is empty: the collider whose bounds define the area instead. Handy " +
             "when the map is drawn by several meshes but has one collider over the whole thing.")]
    [SerializeField] private Collider mapBoundsCollider;

    [Tooltip("Last resort, used when neither of the two above is set: the allowed area in WORLD space. " +
             "Only the X and Z of centre/size matter — Y is ignored except as the height of the plane " +
             "the look-at point is measured on. Left at zero size (the default) it counts as unset, so " +
             "an unwired controller pans freely instead of being locked to a mystery box.")]
    [SerializeField] private Bounds manualBounds = new Bounds(Vector3.zero, Vector3.zero);

    [Tooltip("Shrinks the allowed area inwards on every side, in world units. Positive keeps the " +
             "camera further inside the map; negative lets it hang over the edge.")]
    [SerializeField] private float boundsPadding = 0f;

    [Tooltip("Clamp the point the camera is LOOKING at (where its forward ray meets the map plane) " +
             "instead of the rig's own position. On = the edge of the map is the edge of the view, at " +
             "any tilt or zoom. Off = only the rig itself is kept inside the area, which is cheaper " +
             "but lets a zoomed-out camera see past the edge.")]
    [SerializeField] private bool clampLookAtPoint = true;

    [Tooltip("Draw the clamped area in the Scene view while this object is selected.")]
    [SerializeField] private bool drawBoundsGizmo = true;

    [Header("Edge Movement Rotation")]
    [SerializeField] private bool enableMovementRotation = true;

    [Tooltip("Maximum tilt around the X axis when moving forward/backward.")]
    [SerializeField] private float maxXRotation = 8f;

    [Tooltip("Maximum tilt around the Y axis when moving left/right.")]
    [SerializeField] private float maxYRotation = 8f;

    [SerializeField] private float rotationSmoothing = 8f;

    private Vector3 _cameraBaseLocalPos;
    private Quaternion _cameraBaseLocalRotation;
    private Quaternion _parentBaseLocalRotation;

    private float _zoomOffset;
    private float _targetZoomOffset;

    private Vector2 _currentRotationOffset;

    // Reused so the per-frame UI raycast in HandleZoom allocates nothing.
    private PointerEventData _zoomPointerData;
    private readonly List<RaycastResult> _zoomRaycastResults = new List<RaycastResult>();

    // One log line, not one per frame, when bounds are on but no area is configured.
    private bool warnedAboutMissingBounds;

    /// <summary>The rig root that pans. Scripted camera moves (e.g. <see cref="TransitionManager"/>)
    /// move this, never the camera itself, so the zoom offset is intact when control comes back.</summary>
    public Transform CameraParent => cameraParent;

    public Transform MainCamera => mainCamera;

    /// <summary>
    /// Confines the camera to a new world-space area, replacing the map mesh / collider / manual
    /// bounds set in the inspector. Used when a spawned level takes over from the map: without it the
    /// first frame after re-enabling would clamp the rig straight back onto the (now hidden) map.
    /// </summary>
    public void SetMapBounds(Bounds bounds)
    {
        mapMesh = null;
        mapBoundsCollider = null;
        manualBounds = bounds;
        warnedAboutMissingBounds = false;
    }

    /// <summary>
    /// The three fields <see cref="SetMapBounds"/> overwrites, so a caller can put back exactly what
    /// was there before. Kept as one value because which of the three is in use matters: a map wired
    /// up with a mesh must come back as that mesh, not as a snapshot of where it happened to be.
    /// </summary>
    public struct MapBoundsState
    {
        public Renderer mapMesh;
        public Collider mapBoundsCollider;
        public Bounds manualBounds;
    }

    /// <summary>The current bounds setup, to hand back to <see cref="RestoreMapBounds"/> later.</summary>
    public MapBoundsState GetMapBounds()
    {
        return new MapBoundsState
        {
            mapMesh = mapMesh,
            mapBoundsCollider = mapBoundsCollider,
            manualBounds = manualBounds
        };
    }

    /// <summary>
    /// Puts a saved bounds setup back. Used when a level is unloaded and the map takes over again:
    /// without it the camera would still be clamped to the level that is no longer there.
    /// </summary>
    public void RestoreMapBounds(MapBoundsState state)
    {
        mapMesh = state.mapMesh;
        mapBoundsCollider = state.mapBoundsCollider;
        manualBounds = state.manualBounds;
        warnedAboutMissingBounds = false;
    }

    private void Awake()
    {
        if (!cameraParent)
            cameraParent = transform;

        if (!mainCamera && Camera.main)
            mainCamera = Camera.main.transform;

        if (mainCamera)
        {
            _cameraBaseLocalPos = mainCamera.localPosition;
            _cameraBaseLocalRotation = mainCamera.localRotation;
        }

        _parentBaseLocalRotation = cameraParent.localRotation;
    }

    private void Start()
    {
        // A rig authored (or saved) outside the map snaps in on the first frame rather than letting
        // the player pan back from nowhere.
        ClampToBounds();
    }

    private void Update()
    {
        HandlePan();
        HandleZoom();

        // Last, on purpose: the look-at point depends on the rig's lean (HandlePan) and on the zoom
        // offset (HandleZoom), so clamping before either would clamp a pose that no longer exists.
        ClampToBounds();
    }

    private void HandlePan()
    {
        if (!cameraParent)
            return;

        Vector3 forward = mainCamera ? mainCamera.forward : Vector3.forward;
        forward = Vector3.ProjectOnPlane(forward, Vector3.up);

        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        forward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, forward);

        Vector2 input = Vector2.zero;

        if (enableCursorMovement && enableEdgePan)
            input += ReadEdgeInput();

        if (enableKeyboardPan)
            input += ReadKeyboardInput();

        Vector3 move = right * input.x + forward * input.y;

        float speed = panSpeed;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null &&
            (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed))
        {
            speed *= acceleration;
        }

        if (move != Vector3.zero)
            cameraParent.position += move.normalized * speed * Time.deltaTime;

        HandleMovementRotation(input);
    }

    private Vector2 ReadEdgeInput()
    {
        if (Mouse.current == null)
            return Vector2.zero;

        Vector2 mouse = Mouse.current.position.ReadValue();

        if (requireCursorInWindow &&
            (mouse.x < 0 ||
             mouse.y < 0 ||
             mouse.x > Screen.width ||
             mouse.y > Screen.height))
        {
            return Vector2.zero;
        }

        Vector2 input = Vector2.zero;

        if (mouse.x <= edgeSize)
            input.x -= 1f;
        else if (mouse.x >= Screen.width - edgeSize)
            input.x += 1f;

        if (mouse.y <= edgeSize)
            input.y -= 1f;
        else if (mouse.y >= Screen.height - edgeSize)
            input.y += 1f;

        return input;
    }

    private Vector2 ReadKeyboardInput()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
            return Vector2.zero;

        Vector2 input = Vector2.zero;

        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
            input.x -= 1f;

        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
            input.x += 1f;

        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
            input.y -= 1f;

        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
            input.y += 1f;

        return input;
    }

    private void HandleZoom()
    {
        if (!mainCamera)
            return;

        if (Mouse.current != null)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;

            if (!Mathf.Approximately(scroll, 0f) && !IsPointerOverScrollableUI())
            {
                // Ordered explicitly: with the default maxZoom/minZoom the negated bounds come out
                // reversed (min 10, max -20), and Mathf.Clamp with min > max snaps every scroll to
                // one end or the other — zoomStep then has no effect at all.
                float zoomLow = Mathf.Min(-maxZoom, -minZoom);
                float zoomHigh = Mathf.Max(-maxZoom, -minZoom);

                _targetZoomOffset = Mathf.Clamp(
                    _targetZoomOffset + Mathf.Sign(scroll) * zoomStep,
                    zoomLow,
                    zoomHigh);
            }
        }

        _zoomOffset = Mathf.Lerp(
            _zoomOffset,
            _targetZoomOffset,
            zoomSmoothing * Time.deltaTime);


        Vector3 localForward = _cameraBaseLocalRotation * Vector3.forward;
        mainCamera.localPosition = _cameraBaseLocalPos + localForward * _zoomOffset;
    }

    /// <summary>
    /// True when the cursor is over UI that consumes the wheel itself (a ScrollRect, e.g. the
    /// upgrades list). Without this the wheel both scrolled the list and zoomed the camera, because
    /// this controller reads the mouse device directly and never goes through the EventSystem.
    /// Only scroll handlers block: plain HUD graphics do nothing with the wheel, so zooming over
    /// them stays available.
    /// </summary>
    private bool IsPointerOverScrollableUI()
    {
        if (!blockZoomOverScrollableUI)
            return false;

        EventSystem events = EventSystem.current;
        if (!events || Mouse.current == null)
            return false;

        if (_zoomPointerData == null)
            _zoomPointerData = new PointerEventData(events);

        _zoomPointerData.position = Mouse.current.position.ReadValue();

        _zoomRaycastResults.Clear();
        events.RaycastAll(_zoomPointerData, _zoomRaycastResults);

        for (int i = 0; i < _zoomRaycastResults.Count; i++)
        {
            GameObject go = _zoomRaycastResults[i].gameObject;
            if (!go) continue;

            // GetEventHandler walks up the hierarchy, so a frame inside a scroll view's content
            // counts as the scroll view - which is exactly what the EventSystem itself would do
            // with the wheel event.
            if (ExecuteEvents.GetEventHandler<IScrollHandler>(go)) return true;
        }

        return false;
    }

    private void HandleMovementRotation(Vector2 movementInput)
    {
        if (!cameraParent)
            return;

        if (!enableMovementRotation)
        {
            _currentRotationOffset = Vector2.Lerp(
                _currentRotationOffset,
                Vector2.zero,
                rotationSmoothing * Time.deltaTime);
        }
        else
        {
            Vector2 targetRotation = new Vector2(
                movementInput.y * maxXRotation,
                movementInput.x * maxYRotation);

            _currentRotationOffset = Vector2.Lerp(
                _currentRotationOffset,
                targetRotation,
                rotationSmoothing * Time.deltaTime);
        }

        // Apply the movement "lean" to the rig root, not to the camera itself.
        // The camera inherits this tilt as a child, while its own local rotation
        // stays free for the CameraShaker so shakes are no longer overwritten.
        cameraParent.localRotation =
            _parentBaseLocalRotation *
            Quaternion.Euler(
                _currentRotationOffset.x,
                _currentRotationOffset.y,
                0f);
    }

    // ---------------------------------------------------------------- Map bounds

    /// <summary>
    /// Keeps the camera over the map. Which point is held inside the area depends on
    /// <see cref="clampLookAtPoint"/>: either the rig's own position, or the point the camera is
    /// actually looking at — where its forward ray meets the map plane. The second is what "the
    /// camera cannot leave the map" means to a player, because it holds no matter how far the camera
    /// is zoomed out or how much the rig is leaning.
    /// </summary>
    private void ClampToBounds()
    {
        if (!enableBounds || !cameraParent) return;
        if (!TryGetMapBounds(out Bounds bounds)) return;

        float minX = bounds.min.x + boundsPadding;
        float maxX = bounds.max.x - boundsPadding;
        float minZ = bounds.min.z + boundsPadding;
        float maxZ = bounds.max.z - boundsPadding;

        // Padding wider than the map itself would invert the range and make Clamp meaningless;
        // collapse it to the centre instead, which is the only point that satisfies it.
        if (minX > maxX) minX = maxX = bounds.center.x;
        if (minZ > maxZ) minZ = maxZ = bounds.center.z;

        Vector3 position = cameraParent.position;

        // Offset from the rig to the point being held inside the area. Zero when the rig itself is
        // what is clamped.
        Vector2 offset = clampLookAtPoint ? LookAtOffset(bounds.center.y) : Vector2.zero;

        float x = Mathf.Clamp(position.x + offset.x, minX, maxX) - offset.x;
        float z = Mathf.Clamp(position.z + offset.y, minZ, maxZ) - offset.y;

        cameraParent.position = new Vector3(x, position.y, z);
    }

    /// <summary>
    /// The area the camera is confined to, in world space: the map mesh's renderer bounds, else the
    /// collider's, else the manual bounds. Returns false when none of the three is usable, in which
    /// case nothing is clamped at all — an unset reference must not lock the camera to a point.
    /// </summary>
    private bool TryGetMapBounds(out Bounds bounds)
    {
        if (mapMesh)
        {
            bounds = mapMesh.bounds;
            return true;
        }

        if (mapBoundsCollider)
        {
            bounds = mapBoundsCollider.bounds;
            return true;
        }

        if (manualBounds.size.x > 0f && manualBounds.size.z > 0f)
        {
            bounds = manualBounds;
            return true;
        }

        // Bounds are on but nothing says where the map is. Warned about once — panning is left alone,
        // because silently pinning the camera to the origin is far harder to diagnose than a log line.
        if (!warnedAboutMissingBounds && Application.isPlaying)
        {
            warnedAboutMissingBounds = true;
            h.Out("RTSCameraController: bounds are enabled but no Map Mesh / Map Bounds Collider / " +
                  "Manual Bounds is set — the camera is not being clamped.");
        }

        bounds = default;
        return false;
    }

    /// <summary>
    /// XZ offset from the rig to the point where the camera's forward ray crosses the map plane at
    /// <paramref name="planeY"/>. Returns zero when there is no such point — the camera is level with
    /// the plane, or pointing away from it — which degrades to clamping the rig itself.
    /// </summary>
    private Vector2 LookAtOffset(float planeY)
    {
        if (!mainCamera) return Vector2.zero;

        Vector3 origin = mainCamera.position;
        Vector3 direction = mainCamera.forward;

        if (Mathf.Abs(direction.y) < 0.0001f) return Vector2.zero;

        float distance = (planeY - origin.y) / direction.y;
        if (distance <= 0f) return Vector2.zero;

        Vector3 hit = origin + direction * distance;
        return new Vector2(hit.x - cameraParent.position.x, hit.z - cameraParent.position.z);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawBoundsGizmo || !enableBounds) return;
        if (!TryGetMapBounds(out Bounds bounds)) return;

        float width = Mathf.Max(bounds.size.x - boundsPadding * 2f, 0f);
        float depth = Mathf.Max(bounds.size.z - boundsPadding * 2f, 0f);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(
            new Vector3(bounds.center.x, bounds.center.y, bounds.center.z),
            new Vector3(width, 0.1f, depth));
    }
}
