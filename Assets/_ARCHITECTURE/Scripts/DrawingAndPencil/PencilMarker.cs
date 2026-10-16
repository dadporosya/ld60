using PrimeTween;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The pencil's cursor: a UI element that replaces the system cursor. Over the board it rides the
/// <see cref="Pencil"/>'s tip (so what you see is where the paint goes); anywhere else — off the board, while
/// paused, after win/loss — it follows the mouse like a normal cursor.
///
/// <para><b>Hotspot.</b> One point of the marker's sprite is the pointing point: it is what sits exactly on
/// the mouse / tip, so it is where clicks land and paint goes — the rest of the sprite (a hand, a knife…)
/// just hangs off it. Pick it in the inspector by clicking on the sprite preview, drag it in the Scene view,
/// or use the sprite's own pivot from the Sprite Editor / PSD Importer (<see cref="HotspotSource"/>).</para>
///
/// <para>At runtime the hotspot becomes the RectTransform's <c>pivot</c>, so positioning the marker puts the
/// hotspot on the point, and the stroke punch (and any rotation/scale) happens around it. That mapping holds
/// for a <b>Simple</b> Image, with or without Preserve Aspect.</para>
///
/// <para><b>Rotation.</b> With <see cref="RotationType.PickAndPoke"/> the marker is the character's hand,
/// reaching in from off-screen: it swings around the hotspot as it moves along X — anticlockwise towards
/// the right edge, clockwise towards the left — so the far end of the arm always leaves the screen through
/// the nearer side instead of showing its cut-off end. While LMB is held it tilts a little further
/// anticlockwise, so a click reads as a poke. In either mode <c>rotationOffset</c> adds a constant tilt
/// around the hotspot on top.</para>
///
/// <para>The marker's graphics never take raycasts — it is always right under the pointer, so it would
/// otherwise eat every click.</para>
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class PencilMarker : MonoBehaviour
{
    public enum HotspotSource
    {
        Custom,     // the hotspot field below
        SpritePivot // the Image sprite's own pivot
    }

    public enum RotationType
    {
        None,       // keeps its authored rotation
        PickAndPoke // a hand reaching in: swings around the hotspot with the screen X
    }

    [Tooltip("Optional. Over the board the marker rides this pencil's tip; elsewhere it follows the mouse.")]
    [SerializeField] private Pencil pencil;

    [Header("Hotspot")]
    [Tooltip("Custom: the point below, picked on the sprite in this inspector or in the Scene view. " +
             "SpritePivot: the Image sprite's pivot (Sprite Editor / PSD Importer).")]
    [SerializeField] private HotspotSource hotspotSource = HotspotSource.Custom;
    [Tooltip("The pointing point, normalized over the sprite: (0,0) = bottom-left, (1,1) = top-right.")]
    [SerializeField] private Vector2 hotspot = new Vector2(0.5f, 0.5f);

    [Header("Cursor")]
    [Tooltip("Hide the system cursor while the marker is shown.")]
    [SerializeField] private bool hideSystemCursor = true;
    [Tooltip("Act as the cursor everywhere. Off: shown only over the board and during a stroke — the system " +
             "cursor takes over elsewhere.")]
    [SerializeField] private bool showOffBoard = true;
    [Tooltip("Draw above all other UI: gives the marker its own override-sorting Canvas.")]
    [SerializeField] private bool drawOnTop = true;
    [SerializeField] private int sortingOrder = 30000;

    [Header("On the board")]
    [Tooltip("Snap to the tip's cell (pixel look) instead of the tip's exact position.")]
    [SerializeField] private bool snapToCell = true;
    [Tooltip("Resize the marker to exactly one cell (times the brush diameter). Off for a hand-drawn sprite.")]
    [SerializeField] private bool sizeToCell = false;
    [Tooltip("Pulse size on stroke start, relative to the marker's scale (0.25 = +25%).")]
    [SerializeField] private float punch = 0.25f;
    [SerializeField] private float punchDuration = 0.15f;

    [Header("Rotation")]
    [Tooltip("None: keeps its authored rotation. PickAndPoke: the marker is the character's hand — it swings " +
             "around the hotspot as it moves along X (right = anticlockwise, left = clockwise), so the end of " +
             "the arm stays off-screen.")]
    [SerializeField] private RotationType rotationType = RotationType.None;
    [Tooltip("Constant extra rotation around the hotspot, in degrees (positive = anticlockwise), on top of the " +
             "authored rotation — and of the swing and press tilt with PickAndPoke. Tilts the sprite's rest " +
             "pose without re-exporting it.")]
    [SerializeField, Range(-180f, 180f)] private float rotationOffset = 0f;
    [Tooltip("PickAndPoke: rotation at the screen's side edges, in degrees.")]
    [SerializeField, Range(0f, 180f)] private float maxAngle = 35f;
    [Tooltip("PickAndPoke: the hotspot's screen X (0 = left edge, 1 = right edge) → rotation, from -1 (full " +
             "Max Angle clockwise) to +1 (full anticlockwise). The default is linear, upright at the centre.")]
    [SerializeField] private AnimationCurve angleByScreenX = AnimationCurve.Linear(0f, -1f, 1f, 1f);
    [Tooltip("PickAndPoke: seconds the arm takes to swing to its new angle. 0 = instant.")]
    [SerializeField, Min(0f)] private float rotationSmoothTime = 0.08f;
    [Tooltip("PickAndPoke: extra tilt while LMB is held — a click pokes, a drag keeps it tilted. " +
             "Positive = anticlockwise, in degrees. 0 = off.")]
    [SerializeField] private float pressAngle = 12f;
    [Tooltip("PickAndPoke: seconds the press tilt takes to go in and back out. 0 = instant.")]
    [SerializeField, Min(0f)] private float pressSmoothTime = 0.04f;

    [Header("Press squish")]
    [Tooltip("Scale while LMB is held — X across the hand, Y along it. Squished around the hotspot, so the " +
             "hand feels like it presses down onto the paper. (1, 1) = off.")]
    [SerializeField] private Vector2 pressSquish = new Vector2(0.95f, 0.88f);
    [Tooltip("Seconds the squish takes to go in and back out. 0 = instant.")]
    [SerializeField, Min(0f)] private float squishSmoothTime = 0.05f;

    [Header("Press shake")]
    [Tooltip("Optional. Played when LMB goes down; a looping shake is stopped on release. Put it on this " +
             "marker (its offset is added on top of the cursor position) or on a child of it.")]
    [SerializeField] private ShakeAnimationUI pressShake;

    /// <summary>The marker is currently drawn.</summary>
    public bool IsShown => _shown;

    /// <summary>The marker is on the pencil tip rather than on the mouse.</summary>
    public bool IsOnTip { get; private set; }

    /// <summary>World position of the hotspot — the point the marker points at.</summary>
    public Vector3 HotspotWorldPosition
    {
        get
        {
            RectTransform rt = RectT;
            Rect r = rt.rect;
            Vector2 hs = NormalizedHotspot;
            return rt.TransformPoint(new Vector3(r.xMin + hs.x * r.width, r.yMin + hs.y * r.height, 0f));
        }
    }

    /// <summary>
    /// The hotspot in normalized sprite coords, wherever it comes from. Setting it switches the source to
    /// <see cref="HotspotSource.Custom"/> (e.g. when swapping to a sprite with a different tip at runtime).
    /// </summary>
    public Vector2 NormalizedHotspot
    {
        get
        {
            if (hotspotSource == HotspotSource.Custom) return hotspot;

            Sprite sprite = MarkerSprite;
            return sprite ? sprite.pivot / sprite.rect.size : new Vector2(0.5f, 0.5f);
        }
        set
        {
            hotspotSource = HotspotSource.Custom;
            hotspot = value;
        }
    }

    public HotspotSource Source => hotspotSource;

    /// <summary>The sprite the hotspot is picked on (the marker's own Image), or null.</summary>
    public Sprite MarkerSprite
    {
        get
        {
            Image image = _image ? _image : GetComponent<Image>();
            return image ? image.sprite : null;
        }
    }

    private RectTransform _rect;
    private Image _image;
    private Canvas _rootCanvas;
    private Graphic[] _graphics = new Graphic[0];
    private bool _shown = true;
    private bool _cursorHidden;
    private Quaternion _baseRotation;
    private float _swingAngle;
    private float _swingVelocity;
    private float _pressTilt;
    private float _pressVelocity;
    private bool _hasAngle;
    private bool _rotated;
    private Vector3 _baseScale;
    private float _squish;         // 0 = at rest, 1 = fully squished
    private float _squishVelocity;
    private Vector3 _punchOffset;  // added on top of the scale while a stroke-start punch plays
    private Tween _punchTween;
    private Vector2 _shakeOffset;
    private Tween _shakeTween;

    public Vector2 ShakeOffset => _shakeOffset;

    private RectTransform RectT => _rect ? _rect : (RectTransform)transform;

    private Camera EventCamera =>
        _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _rootCanvas.worldCamera;

    private void Awake()
    {
        _rect = (RectTransform)transform;
        _image = GetComponent<Image>();
        _baseRotation = _rect.localRotation;
        _baseScale = _rect.localScale;

        Canvas canvas = GetComponentInParent<Canvas>();
        _rootCanvas = canvas ? canvas.rootCanvas : null;
        if (!_rootCanvas)
        {
            h.Out($"PencilMarker on '{name}' is not under a Canvas — disabled.");
            enabled = false;
            return;
        }

        // Hidden by switching its graphics off, never by deactivating it: the GameObject stays active so
        // tweens on it keep running and anything else living on it (the Pencil itself) keeps updating.
        _graphics = GetComponentsInChildren<Graphic>(true);
        foreach (Graphic graphic in _graphics) graphic.raycastTarget = false;

        if (drawOnTop)
        {
            Canvas own = GetComponent<Canvas>();
            if (!own) own = gameObject.AddComponent<Canvas>();
            own.overrideSorting = true;
            own.sortingOrder = sortingOrder;
        }

        ApplyHotspot();
    }

    private void OnEnable()
    {
        if (pencil) pencil.OnStrokeBegan += Punch;
    }

    private void OnDisable()
    {
        if (pencil) pencil.OnStrokeBegan -= Punch;
        _punchTween.Stop();
        _punchOffset = Vector3.zero;
        _shakeTween.Stop();
        _shakeOffset = Vector2.zero;
        SetShown(false);
        SetSystemCursorHidden(false);
    }

    // The OS can hand the cursor back visible when the window regains focus.
    private void OnApplicationFocus(bool focus)
    {
        if (focus && _cursorHidden) SetSystemCursorHidden(true, force: true);
    }

    // LateUpdate: after the Pencil has moved its tip this frame.
    private void LateUpdate()
    {
        Mouse mouse = Mouse.current;
        // A playback drives the pencil with its own pointer: the marker shows that one, not the mouse.
        bool playback = pencil && pencil.IsPlayingBack;
        if (mouse == null && !playback)
        {
            SetShown(false);
            SetSystemCursorHidden(false);
            return;
        }

        Vector2 screen = playback ? pencil.PointerScreen : mouse.position.ReadValue();
        bool pressed = playback ? pencil.PointerPressed : mouse.leftButton.isPressed;
        bool inWindow = screen.x >= 0f && screen.y >= 0f && screen.x <= Screen.width && screen.y <= Screen.height;

        IsOnTip = RidesTip();
        bool show = IsOnTip || (showOffBoard && inWindow);
        SetShown(show);
        SetSystemCursorHidden(show && hideSystemCursor);
        UpdatePressShake(playback ? pencil.VirtualPressedThisFrame : mouse.leftButton.wasPressedThisFrame,
            playback ? pencil.VirtualReleasedThisFrame : mouse.leftButton.wasReleasedThisFrame);
        if (!show) return;

        ApplyHotspot(); // cheap; picks up sprite swaps and inspector tweaks
        if (sizeToCell && pencil) ResizeToCell();

        _rect.position = IsOnTip ? TipPoint() : ScreenToWorld(screen);

        // Setting the position outright just wiped the shake's own write, so add its offset back on top.
        // (A shake on a child moves the child and needs nothing from here.)
        if (pressShake && pressShake.transform == transform)
            _rect.anchoredPosition += (Vector2)pressShake.AppliedOffset;

        if (_shakeOffset != Vector2.zero)
            _rect.anchoredPosition += _shakeOffset;

        UpdateRotation(pressed);
        UpdateScale(pressed);
    }

    private void UpdatePressShake(bool pressedThisFrame, bool releasedThisFrame)
    {
        if (!pressShake || !pressShake.isActiveAndEnabled) return;

        if (pressedThisFrame && _shown) pressShake.PlayInstantly();
        else if (releasedThisFrame && pressShake.loop && pressShake.IsPlaying) pressShake.Stop();
    }

    /// <summary>
    /// On the tip while the pencil is live and the mouse is over the board — or mid-stroke anywhere, or while
    /// the pencil is being pulled away (the tip, not the mouse, is where the pull has it this frame).
    /// </summary>
    private bool RidesTip() =>
        pencil && pencil.isActiveAndEnabled && pencil.HasTip
        && (pencil.IsPulled || pencil.IsPlayingBack
            || pencil.CanDraw && (pencil.IsCursorOnBoard || pencil.IsDrawing));

    private Vector3 TipPoint() =>
        snapToCell && pencil.IsTipOnBoard ? pencil.Grid.CellToWorld(pencil.TipCell) : pencil.TipWorldPosition;

    private Vector3 ScreenToWorld(Vector2 screen)
    {
        RectTransform plane = _rect.parent as RectTransform;
        if (!plane) plane = (RectTransform)_rootCanvas.transform;

        return RectTransformUtility.ScreenPointToWorldPointInRectangle(plane, screen, EventCamera, out Vector3 world)
            ? world
            : _rect.position;
    }

    /// <summary>
    /// The rotation offset, plus with PickAndPoke the angle the marker's screen X asks for and the press tilt
    /// while LMB is held. The pivot is the hotspot, so the hand swings around its fingertip / tool tip and
    /// the pointing point never moves.
    /// </summary>
    private void UpdateRotation(bool pressed)
    {
        if (rotationType != RotationType.PickAndPoke)
        {
            _hasAngle = false;
            if (rotationOffset != 0f)
            {
                _rect.localRotation = _baseRotation * Quaternion.Euler(0f, 0f, rotationOffset);
                _rotated = true;
                return;
            }

            // Nothing to add (e.g. switched off live in the inspector): hand the authored rotation back once.
            if (_rotated) _rect.localRotation = _baseRotation;
            _rotated = false;
            return;
        }

        float screenX = RectTransformUtility.WorldToScreenPoint(EventCamera, _rect.position).x;
        float t = Screen.width > 0 ? Mathf.Clamp01(screenX / Screen.width) : 0.5f;
        _swingAngle = Damp(_swingAngle, angleByScreenX.Evaluate(t) * maxAngle, ref _swingVelocity, rotationSmoothTime);
        _pressTilt = Damp(_pressTilt, pressed ? pressAngle : 0f, ref _pressVelocity, pressSmoothTime);
        _hasAngle = true;

        // Positive Z turns a UI element anticlockwise on screen.
        _rect.localRotation = _baseRotation * Quaternion.Euler(0f, 0f, rotationOffset + _swingAngle + _pressTilt);
        _rotated = true;
    }

    /// <summary>Eases towards <paramref name="target"/>; snaps when there is no angle yet (just shown).</summary>
    private float Damp(float current, float target, ref float velocity, float smoothTime)
    {
        if (!_hasAngle || smoothTime <= 0f)
        {
            velocity = 0f;
            return target;
        }

        // Unscaled, like the pencil tip: a paused timeScale must not freeze the hand.
        return Mathf.SmoothDampAngle(current, target, ref velocity, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
    }

    private void ResizeToCell()
    {
        PixelGrid grid = pencil.Grid;
        if (!grid || !grid.Rect) return;

        // CellSize is in the board's units; go through world scale in case the marker is scaled differently.
        Vector3 boardScale = grid.Rect.lossyScale;
        Vector3 ownScale = _rect.parent ? _rect.parent.lossyScale : Vector3.one;
        if (ownScale.x == 0f || ownScale.y == 0f) return; // parent collapsed, e.g. mid intro tween

        Vector2 size = grid.CellSize * pencil.BrushDiameter;
        _rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x * boardScale.x / ownScale.x);
        _rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y * boardScale.y / ownScale.y);
    }

    /// <summary>
    /// Makes the hotspot the pivot. A Simple Image maps the sprite onto the rect 1:1 — and with Preserve
    /// Aspect it anchors the fitted sprite by the pivot, which puts the pivot on the same sprite point — so
    /// the normalized hotspot is the pivot either way.
    /// </summary>
    private void ApplyHotspot()
    {
        Vector2 pivot = NormalizedHotspot;
        if (_rect.pivot != pivot) _rect.pivot = pivot;
    }

    /// <summary>
    /// The marker owns its scale: the authored scale, squished while LMB is held, plus the punch. Both scale
    /// around the pivot — the hotspot — so the pointing point stays put.
    /// </summary>
    private void UpdateScale(bool pressed)
    {
        _squish = squishSmoothTime <= 0f
            ? (pressed ? 1f : 0f)
            : Mathf.SmoothDamp(_squish, pressed ? 1f : 0f, ref _squishVelocity, squishSmoothTime, Mathf.Infinity,
                Time.unscaledDeltaTime);

        Vector3 squished = Vector3.LerpUnclamped(Vector3.one, new Vector3(pressSquish.x, pressSquish.y, 1f), _squish);
        _rect.localScale = Vector3.Scale(_baseScale, squished) + _punchOffset;
    }

    private void Punch()
    {
        if (punch <= 0f) return;

        // The punch only drives an offset; UpdateScale adds it on top of the squish instead of the two
        // tweens fighting over localScale.
        _punchTween.Stop();
        _punchOffset = Vector3.zero;
        _punchTween = Tween.PunchCustom(this, Vector3.zero, new ShakeSettings(_baseScale * punch, punchDuration),
            (marker, offset) => marker._punchOffset = offset);
    }

    /// <summary>Shakes the marker slightly around its current position.</summary>
    public void Shake(float strength = 10f, float duration = 0.25f, float frequency = 25f)
    {
        _shakeTween.Stop();
        _shakeOffset = Vector2.zero;
        if (strength <= 0f || duration <= 0f) return;

        Vector3 axes = new Vector3(1f, 1f, 0f);
        _shakeTween = Tween.ShakeCustom(this, Vector3.zero,
            new ShakeSettings(axes * strength, duration, frequency, enableFalloff: true, useUnscaledTime: true),
            (marker, offset) => marker._shakeOffset = offset)
            .OnComplete(this, marker => marker._shakeOffset = Vector2.zero);
    }

    private void SetShown(bool shown)
    {
        if (_shown == shown) return;

        _shown = shown;
        if (!shown) _hasAngle = false; // reappearing elsewhere: snap to that angle rather than swing into it
        foreach (Graphic graphic in _graphics)
            if (graphic) graphic.enabled = shown;
    }

    private void SetSystemCursorHidden(bool hidden, bool force = false)
    {
        if (_cursorHidden == hidden && !force) return;

        _cursorHidden = hidden;
        if (CursorIconManager.Instance) CursorIconManager.Instance.SetCursorVisible(!hidden);
        else Cursor.visible = !hidden;
    }
}
