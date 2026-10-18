using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Controls the visual appearance of the cursor: which icon is shown
/// (default / click / drag / any custom one) and whether the cursor is visible at all.
///
/// Two rendering modes:
///  - <b>Hardware</b> (default): uses <see cref="Cursor.SetCursor"/>. The OS composites the cursor on
///    top of the finished frame, so post-processing and Volumes can never affect it.
///  - <b>In scene</b> (<see cref="renderCursorInScene"/>): hides the hardware cursor and draws the icon
///    as a <see cref="SpriteRenderer"/> sitting just in front of the camera. Because it is real scene
///    geometry rendered by the camera, the URP Volume stack (bloom, color grading, vignette, chromatic
///    aberration, custom passes) applies to it.
///
/// Singleton, access via <see cref="Instance"/>.
/// </summary>
public class CursorIconManager : MonoBehaviour
{
    public static CursorIconManager Instance;

    public enum CursorState
    {
        Default,
        Click,
        Drag,
        Button,
        Custom
    }

    /// <summary>
    /// A cursor texture plus the hotspot (the pixel of the texture that acts as the "point").
    /// Optionally animated: with <see cref="animate"/> on, the cursor cycles through <see cref="frames"/>,
    /// switching texture every <see cref="gapBetweenFrames"/> seconds.
    /// </summary>
    [Serializable]
    public class CursorIcon
    {
        [Tooltip("Still icon. Used when animate is off or no frames are set.")]
        public Texture2D texture;

        [Tooltip("Hotspot in normalized texture coords (0,0 = top-left, 1,1 = bottom-right). Ignored if useCenterHotspot is on.")]
        public Vector2 normalizedHotspot = Vector2.zero;

        [Tooltip("If on, the hotspot is the center of the texture.")]
        public bool useCenterHotspot = false;

        [Header("Animation")]
        [Tooltip("Cycle through the frames below instead of showing the still texture.")]
        public bool animate = false;

        [Tooltip("Animation frames, played in order and looped. Textures must be readable (Read/Write on) " +
                 "with the Cursor texture type for the hardware cursor.")]
        public List<Texture2D> frames = new List<Texture2D>();

        [Tooltip("Seconds each frame is shown (real time, unaffected by pause).")]
        public float gapBetweenFrames = 0.2f;

        public bool IsAnimated => animate && FrameCount > 1;

        /// <summary>Number of non-null animation frames.</summary>
        public int FrameCount
        {
            get
            {
                if (frames == null) return 0;
                int count = 0;
                for (int i = 0; i < frames.Count; i++)
                    if (frames[i]) count++;
                return count;
            }
        }

        public bool IsValid => texture != null || (animate && FrameCount > 0);

        /// <summary>
        /// The texture to show on animation frame <paramref name="index"/> (wrapped, null frames skipped).
        /// Falls back to the still texture when not animating.
        /// </summary>
        public Texture2D GetFrame(int index)
        {
            int count = animate ? FrameCount : 0;
            if (count == 0) return texture;

            int target = ((index % count) + count) % count;
            for (int i = 0; i < frames.Count; i++)
            {
                if (!frames[i]) continue;
                if (target-- == 0) return frames[i];
            }

            return texture;
        }

        public Vector2 NormalizedHotspot => useCenterHotspot ? new Vector2(0.5f, 0.5f) : normalizedHotspot;

        /// <summary>Hotspot in pixels of <paramref name="frame"/>, so frames of different sizes line up.</summary>
        public Vector2 GetPixelHotspot(Texture2D frame)
        {
            if (!frame) return Vector2.zero;

            Vector2 normalized = NormalizedHotspot;
            return new Vector2(
                normalized.x * frame.width,
                normalized.y * frame.height);
        }

        public Vector2 PixelHotspot => GetPixelHotspot(GetFrame(0));
    }

    public float sfxVolume = 1f;
    
    [Header("Icons")]
    [SerializeField] private CursorIcon defaultIcon;
    [SerializeField] private AudioClip clickSound;
    [SerializeField] private CursorIcon clickIcon;
    [SerializeField] private AudioClip dragReleaseSound;
    [SerializeField] private CursorIcon dragIcon;

    [Tooltip("Shown while the pointer is over an interactable button / Selectable.")]
    [SerializeField] private CursorIcon buttonIcon;

    [Tooltip("Optional. Shown while the mouse is held down over a button. Falls back to the button icon.")]
    [SerializeField] private CursorIcon buttonClickIcon;

    [Tooltip("Optional. Played once when the pointer enters a button.")]
    [SerializeField] private AudioClip buttonHoverSound;

    [Tooltip("Optional. Played when the mouse is pressed over a button. Falls back to the click sound.")]
    [SerializeField] private AudioClip buttonClickSound;

    [Tooltip("Random pitch range (x = min, y = max) of the button click sound.")]
    [SerializeField] private Vector2 buttonClickPitchRange = new Vector2(0.9f, 1.1f);

    [Header("Button Hover Shake")]
    [Tooltip("Shake the cursor icon when the pointer enters a button.")]
    [SerializeField] private bool shakeOnButtonHover = false;

    [Tooltip("How far the icon jumps from its place, in screen pixels.")]
    [SerializeField] private float buttonShakeStrength = 3f;

    [Tooltip("Seconds the shake lasts, fading out. 0 or less = shake for as long as the button is hovered.")]
    [SerializeField] private float buttonShakeDuration = 0f;

    [Tooltip("New shake offsets per second (real time).")]
    [SerializeField] private float buttonShakeFrequency = 30f;

    [Tooltip("Hardware mode only: while shaking, let Unity draw the cursor (CursorMode.ForceSoftware) " +
             "instead of the OS. The OS rebuilds its cursor on every hotspot change, which flickers.")]
    [SerializeField] private bool softwareCursorWhileShaking = true;

    [Header("Behaviour")]
    [Tooltip("Automatically swap between default / click / drag / button icons based on mouse, DnD and UI state.")]
    [SerializeField] private bool autoHandleState = true;

    [Tooltip("Automatically detect UI buttons (Selectable) under the pointer and show the button icon.")]
    [SerializeField] private bool autoDetectButtons = true;

    [Tooltip("Hardware cursor mode. Auto uses hardware cursors where supported, ForceSoftware always renders in software.")]
    [SerializeField] private CursorMode cursorMode = CursorMode.Auto;

    [SerializeField] private bool visibleOnStart = true;

    [Header("In-Scene Rendering")]
    [Tooltip("Draw the cursor as a sprite in front of the camera instead of using the hardware cursor. " +
             "Only this mode is affected by post-processing and Volumes.")]
    [SerializeField] private bool renderCursorInScene = false;

    [Tooltip("Camera the in-scene cursor is drawn in front of. Defaults to CameraController.Instance.cam, then Camera.main.")]
    [SerializeField] private Camera sceneCursorCamera;

    [Tooltip("Distance from the camera the cursor sprite sits at. Small values keep it in front of all geometry.")]
    [SerializeField] private float sceneCursorDistance = 1f;

    [Tooltip("Size multiplier. At 1 the sprite covers exactly the same pixels the hardware cursor would.")]
    [SerializeField] private float sceneCursorScale = 1f;

    [Tooltip("Optional material for the cursor sprite. Leave empty to use the default sprite material.")]
    [SerializeField] private Material sceneCursorMaterial;

    [SerializeField] private string sceneCursorSortingLayer = "Default";
    [SerializeField] private int sceneCursorSortingOrder = 32000;

    public CursorState State { get; private set; } = CursorState.Default;
    public bool IsVisible { get; private set; } = true;

    private CursorIcon currentIcon;

    // Animation playback of currentIcon
    private int frameIndex;
    private float frameTimer;

    // Shake: offset of the drawn icon from the pointer, in screen pixels (y up)
    private bool shaking;
    private bool shakeTiedToButton;
    private float shakeDuration;
    private float shakeTimeLeft;
    private float shakeStrength;
    private float shakeFrequency;
    private float shakeStepTimer;
    private Vector2 shakeOffset;

    // Last Cursor.SetCursor call, so identical ones are skipped
    private bool hardwareCursorApplied;
    private Texture2D appliedCursorTexture;
    private Vector2Int appliedCursorHotspot;
    private CursorMode appliedCursorMode;

    /// <summary>True while the cursor icon is shaking.</summary>
    public bool IsShaking => shaking;

    private SpriteRenderer sceneCursorRenderer;
    private Camera resolvedSceneCamera;
    // Keyed by texture + pivot so repeated SetCustomIcon calls with the same texture reuse one sprite.
    private readonly Dictionary<(Texture2D, Vector2), Sprite> spriteCache =
        new Dictionary<(Texture2D, Vector2), Sprite>();

    /// <summary>
    /// When true the cursor is drawn as scene geometry (and is therefore affected by post-processing);
    /// when false the OS hardware cursor is used. Safe to change at runtime.
    /// </summary>
    public bool RenderCursorInScene
    {
        get => renderCursorInScene;
        set
        {
            if (renderCursorInScene == value) return;
            renderCursorInScene = value;
            ApplyRenderMode();
        }
    }

    private bool wasDraging = false;

    // Button hover tracking
    private bool buttonClickShown = false;
    private int externalButtonHoverCount = 0;
    private int externalDragCount = 0;
    // Auto-detected Selectable under the pointer, so moving straight onto a neighbouring button replays the hover sound.
    private Selectable hoveredSelectable;
    private PointerEventData pointerData;
    private static readonly List<RaycastResult> raycastResults = new List<RaycastResult>();

    /// <summary>True while something is telling the cursor it is over a button.</summary>
    public bool IsOverButton { get; private set; }

    // The project uses the Input System only; the legacy Input class throws.
    private static Vector2 MousePosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance);
    }

    private void Start()
    {
        SetCursorVisible(visibleOnStart);
        SetDefaultIcon();
        ApplyRenderMode();
    }

    private void Update()
    {
        if (autoHandleState) HandleAutoState();

        AnimateCurrentIcon();
        UpdateShake();
    }

    // SHAKE

    /// <summary>
    /// Shakes the cursor icon around the pointer (the click point itself stays put).
    /// </summary>
    /// <param name="duration">Seconds, fading out. 0 or less = until <see cref="StopShake"/>.</param>
    /// <param name="strength">Max offset in screen pixels.</param>
    /// <param name="frequency">New offsets per second.</param>
    public void ShakeCursor(float duration, float strength, float frequency = 30f)
    {
        StartShake(duration, strength, frequency, tiedToButton: false);
    }

    /// <summary>Stops any shake and puts the icon back on the pointer.</summary>
    public void StopShake()
    {
        if (!shaking) return;

        shaking = false;
        shakeTiedToButton = false;
        shakeOffset = Vector2.zero;
        if (!renderCursorInScene) ApplyCurrentFrame();
    }

    private void StartShake(float duration, float strength, float frequency, bool tiedToButton)
    {
        shaking = true;
        shakeTiedToButton = tiedToButton;
        shakeDuration = duration;
        shakeTimeLeft = duration;
        shakeStrength = strength;
        shakeFrequency = frequency;
        // Jump on the very first update instead of waiting a step.
        shakeStepTimer = float.MaxValue;
    }

    private void UpdateShake()
    {
        if (!shaking) return;

        // A hover shake ends as soon as the pointer leaves the button.
        if (shakeTiedToButton && State != CursorState.Button)
        {
            StopShake();
            return;
        }

        float dt = Time.unscaledDeltaTime;
        bool timed = shakeDuration > 0f;
        if (timed)
        {
            shakeTimeLeft -= dt;
            if (shakeTimeLeft <= 0f)
            {
                StopShake();
                return;
            }
        }

        float step = 1f / Mathf.Max(1f, shakeFrequency);
        if (shakeStepTimer != float.MaxValue) shakeStepTimer += dt;
        if (shakeStepTimer < step) return;
        shakeStepTimer = shakeStepTimer == float.MaxValue ? 0f : shakeStepTimer % step;

        float fade = timed ? shakeTimeLeft / shakeDuration : 1f;
        shakeOffset = UnityEngine.Random.insideUnitCircle * (shakeStrength * fade);

        // The in-scene sprite picks the offset up in LateUpdate.
        if (!renderCursorInScene) ApplyCurrentFrame();
    }

    /// <summary>
    /// Advances the current icon's animation, swapping the cursor texture every
    /// <see cref="CursorIcon.gapBetweenFrames"/> seconds of real time.
    /// </summary>
    private void AnimateCurrentIcon()
    {
        if (!IsVisible || currentIcon == null || !currentIcon.IsAnimated) return;

        float gap = Mathf.Max(0.01f, currentIcon.gapBetweenFrames);
        frameTimer += Time.unscaledDeltaTime;
        if (frameTimer < gap) return;

        int steps = Mathf.FloorToInt(frameTimer / gap);
        frameTimer -= steps * gap;
        frameIndex = (frameIndex + steps) % currentIcon.FrameCount;

        ApplyCurrentFrame();
    }

    private void LateUpdate()
    {
        if (renderCursorInScene) UpdateSceneCursorTransform();
    }

    // STATE

    private void HandleAutoState()
    {
        if (State == CursorState.Custom) return;

        bool isDragging =
            (CursorDnDManager.Instance != null &&
             CursorDnDManager.Instance.currentDraggingObject != null) ||
            externalDragCount > 0;

        if (isDragging)
        {
            if (State != CursorState.Drag) SetDragIcon();
            return;
        } else if (wasDraging)
        {
            wasDraging = false;
            if (dragReleaseSound) SFXManager.Instance.PlayClip(dragReleaseSound, volumeIn:sfxVolume, playIndependently:true);
        }

        bool pressed = Mouse.current != null && Mouse.current.leftButton.isPressed;
        Selectable hovered = null;
        bool overSelectable = autoDetectButtons && IsPointerOverButton(out hovered);
        bool overButton = overSelectable || externalButtonHoverCount > 0;
        IsOverButton = overButton;

        bool switchedButton = State == CursorState.Button && hovered && hovered != hoveredSelectable;
        hoveredSelectable = hovered;

        if (overButton)
        {
            if (switchedButton) PlayButtonHoverSound();
            if (State != CursorState.Button || buttonClickShown != pressed) SetButtonIcon(pressed);
            return;
        }

        if (pressed)
        {
            if (State != CursorState.Click) SetClickIcon();
            return;
        }

        if (State != CursorState.Default) SetDefaultIcon();
    }

    /// <summary>
    /// Raycasts the UI at the pointer and reports whether an interactable <see cref="Selectable"/>
    /// (Button, Toggle, Slider, ...) is under it.
    /// </summary>
    private bool IsPointerOverButton(out Selectable hovered)
    {
        hovered = null;
        EventSystem es = EventSystem.current;
        if (es == null) return false;

        if (pointerData == null) pointerData = new PointerEventData(es);
        pointerData.position = MousePosition;

        raycastResults.Clear();
        es.RaycastAll(pointerData, raycastResults);

        for (int i = 0; i < raycastResults.Count; i++)
        {
            GameObject go = raycastResults[i].gameObject;
            if (go == null) continue;

            Selectable selectable = go.GetComponentInParent<Selectable>();
            if (selectable && selectable.isActiveAndEnabled && selectable.interactable)
            {
                hovered = selectable;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Lets non-UI "buttons" (world objects, custom widgets) drive the button cursor.
    /// Call with true on enter and false on exit; calls are reference counted.
    /// </summary>
    public void SetButtonHover(bool hovering)
    {
        externalButtonHoverCount = Mathf.Max(0, externalButtonHoverCount + (hovering ? 1 : -1));
    }

    /// <summary>Clears every external button-hover registration.</summary>
    public void ClearButtonHover()
    {
        externalButtonHoverCount = 0;
    }

    /// <summary>
    /// Lets external systems (such as scrollbars or custom drag widgets) report an active drag.
    /// Call with true when drag starts and false when it ends; calls are reference counted.
    /// </summary>
    public void SetExternalDrag(bool dragging)
    {
        externalDragCount = Mathf.Max(0, externalDragCount + (dragging ? 1 : -1));
        if (dragging && State != CursorState.Drag)
        {
            SetDragIcon();
        }
    }

    /// <summary>Clears every external drag registration.</summary>
    public void ClearExternalDrag()
    {
        externalDragCount = 0;
    }

    // ICON SETTERS

    /// <summary>Switches the cursor back to the default icon.</summary>
    public void SetDefaultIcon()
    {
        State = CursorState.Default;
        buttonClickShown = false;
        ApplyIcon(defaultIcon);
    }

    /// <summary>
    /// Switches the cursor to the button icon, shown while hovering (or clicking) a button.
    /// </summary>
    /// <param name="clicked">True while the mouse is held down over the button.</param>
    public void SetButtonIcon(bool clicked = false)
    {
        bool entering = State != CursorState.Button;

        if (entering)
        {
            PlayButtonHoverSound();

            if (shakeOnButtonHover)
                StartShake(buttonShakeDuration, buttonShakeStrength, buttonShakeFrequency, tiedToButton: true);
        }
        else if (clicked && !buttonClickShown)
        {
            AudioClip sound = buttonClickSound ? buttonClickSound : clickSound;
            if (sound)
                SFXManager.Instance.PlayClip(
                    sound,
                    randomPitchRange: buttonClickPitchRange,
                    volumeIn: sfxVolume, playIndependently: true);
        }

        State = CursorState.Button;
        buttonClickShown = clicked;

        CursorIcon icon = clicked && buttonClickIcon != null && buttonClickIcon.IsValid
            ? buttonClickIcon
            : buttonIcon;

        if (icon == null || !icon.IsValid)
            icon = clicked && clickIcon.IsValid ? clickIcon : defaultIcon;

        ApplyIcon(icon);
    }

    private void PlayButtonHoverSound()
    {
        if (buttonHoverSound && SFXManager.Instance)
            SFXManager.Instance.PlayClip(
                buttonHoverSound,
                randomPitchRange: new Vector2(0.95f, 1.05f),
                volumeIn: sfxVolume, playIndependently: true);
    }

    /// <summary>Switches the cursor to the click icon.</summary>
    public void SetClickIcon()
    {
        if (clickSound)
            SFXManager.Instance.PlayClip(
                clickSound,
                // volumeIn:2f,
                randomPitchRange: new Vector2(0.9f, 1.1f),
                volumeIn:sfxVolume, playIndependently:true
            ); 
        State = CursorState.Click;
        buttonClickShown = false;
        ApplyIcon(clickIcon.IsValid ? clickIcon : defaultIcon);
    }

    /// <summary>Switches the cursor to the drag icon.</summary>
    public void SetDragIcon()
    {
        wasDraging = true;
        State = CursorState.Drag;
        buttonClickShown = false;
        ApplyIcon(dragIcon.IsValid ? dragIcon : defaultIcon);
    }

    /// <summary>
    /// Switches the cursor to an arbitrary custom icon. While a custom icon is set,
    /// automatic state handling is suspended until <see cref="ResetIcon"/> is called.
    /// </summary>
    public void SetCustomIcon(CursorIcon icon)
    {
        if (icon == null || !icon.IsValid)
        {
            h.Out("CursorIconManager: SetCustomIcon called with an invalid icon, falling back to default.");
            SetDefaultIcon();
            return;
        }

        State = CursorState.Custom;
        buttonClickShown = false;
        ApplyIcon(icon);
    }

    /// <summary>
    /// Switches the cursor to an arbitrary custom texture.
    /// </summary>
    /// <param name="texture">Texture to use as the cursor.</param>
    /// <param name="normalizedHotspot">Hotspot in normalized texture coords (0..1).</param>
    public void SetCustomIcon(Texture2D texture, Vector2 normalizedHotspot = default)
    {
        SetCustomIcon(new CursorIcon
        {
            texture = texture,
            normalizedHotspot = normalizedHotspot,
            useCenterHotspot = false
        });
    }

    /// <summary>
    /// Drops any custom icon and returns to the automatically handled default icon.
    /// </summary>
    public void ResetIcon()
    {
        SetDefaultIcon();
    }

    private void ApplyIcon(CursorIcon icon)
    {
        // A new icon starts its animation from the first frame; re-applying the same one keeps playing.
        if (icon != currentIcon)
        {
            frameIndex = 0;
            frameTimer = 0f;
        }

        currentIcon = icon;
        ApplyCurrentFrame();
    }

    /// <summary>Pushes the current icon's current animation frame to the active render mode.</summary>
    private void ApplyCurrentFrame()
    {
        CursorIcon icon = currentIcon;
        Texture2D frame = icon != null && icon.IsValid ? icon.GetFrame(frameIndex) : null;

        if (renderCursorInScene)
        {
            ApplySceneIcon(icon, frame);
            return;
        }

        if (!frame)
        {
            // null texture = OS default cursor
            SetHardwareCursor(null, Vector2.zero, cursorMode);
            return;
        }

        Vector2 hotspot = icon.GetPixelHotspot(frame);
        CursorMode mode = cursorMode;
        if (shaking)
        {
            // The hardware cursor can't be drawn off the pointer, so the hotspot moves the other way instead.
            // Cursor coords are y-down, the offset is y-up. Unity requires the hotspot inside the texture,
            // so an icon with its hotspot on an edge can only shake away from that edge.
            hotspot = new Vector2(
                Mathf.Clamp(hotspot.x - shakeOffset.x, 0f, frame.width - 1),
                Mathf.Clamp(hotspot.y + shakeOffset.y, 0f, frame.height - 1));

            if (softwareCursorWhileShaking) mode = CursorMode.ForceSoftware;
        }

        SetHardwareCursor(frame, hotspot, mode);
    }

    /// <summary>
    /// <see cref="Cursor.SetCursor"/>, skipped when nothing would change. Every real call makes the OS
    /// rebuild its cursor, which blinks when done every frame (as a shake does).
    /// </summary>
    private void SetHardwareCursor(Texture2D texture, Vector2 hotspot, CursorMode mode)
    {
        Vector2Int pixel = Vector2Int.RoundToInt(hotspot);
        if (hardwareCursorApplied &&
            appliedCursorTexture == texture &&
            appliedCursorHotspot == pixel &&
            appliedCursorMode == mode)
            return;

        hardwareCursorApplied = true;
        appliedCursorTexture = texture;
        appliedCursorHotspot = pixel;
        appliedCursorMode = mode;

        Cursor.SetCursor(texture, pixel, mode);
    }

    // IN-SCENE RENDERING

    /// <summary>
    /// Switches between hardware and in-scene rendering, cleaning up whichever mode is being left.
    /// </summary>
    private void ApplyRenderMode()
    {
        if (renderCursorInScene)
        {
            // The OS cursor must go away, otherwise both are visible at once.
            SetHardwareCursor(null, Vector2.zero, cursorMode);
            Cursor.visible = false;

            EnsureSceneCursor();
            ApplyCurrentFrame();
            UpdateSceneCursorTransform();
        }
        else
        {
            if (sceneCursorRenderer) sceneCursorRenderer.enabled = false;

            Cursor.visible = IsVisible;
            ApplyIcon(currentIcon);
        }
    }

    /// <summary>Creates the sprite object used for in-scene rendering, parented to the render camera.</summary>
    private void EnsureSceneCursor()
    {
        if (sceneCursorRenderer) return;

        Camera cam = ResolveSceneCamera();

        GameObject go = new GameObject("SceneCursor");
        go.hideFlags = HideFlags.DontSave;
        if (cam) go.transform.SetParent(cam.transform, false);

        sceneCursorRenderer = go.AddComponent<SpriteRenderer>();
        sceneCursorRenderer.sortingLayerName = sceneCursorSortingLayer;
        sceneCursorRenderer.sortingOrder = sceneCursorSortingOrder;
        if (sceneCursorMaterial) sceneCursorRenderer.sharedMaterial = sceneCursorMaterial;
    }

    private Camera ResolveSceneCamera()
    {
        if (sceneCursorCamera) return sceneCursorCamera;
        if (resolvedSceneCamera) return resolvedSceneCamera;

        if (CameraController.Instance && CameraController.Instance.cam)
            resolvedSceneCamera = CameraController.Instance.cam;
        else
            resolvedSceneCamera = Camera.main;

        return resolvedSceneCamera;
    }

    /// <summary>Pushes one of an icon's textures (its current frame) into the scene sprite renderer.</summary>
    private void ApplySceneIcon(CursorIcon icon, Texture2D frame)
    {
        EnsureSceneCursor();
        if (!sceneCursorRenderer) return;

        if (icon == null || !frame)
        {
            sceneCursorRenderer.enabled = false;
            return;
        }

        sceneCursorRenderer.sprite = GetSprite(icon, frame);
        sceneCursorRenderer.enabled = IsVisible;
    }

    /// <summary>
    /// Builds (and caches) a sprite for one of an icon's textures. Pixels-per-unit is 1 so one texture
    /// pixel equals one world unit before scaling, which makes the on-screen size math below trivial.
    /// The sprite pivot is the cursor hotspot, converted from top-left-origin cursor coords to
    /// bottom-left-origin UV.
    /// </summary>
    private Sprite GetSprite(CursorIcon icon, Texture2D texture)
    {
        Vector2 normalized = icon.NormalizedHotspot;

        // Flip Y: cursor hotspots are top-left based, sprite pivots are bottom-left based.
        Vector2 pivot = new Vector2(normalized.x, 1f - normalized.y);

        var key = (texture, pivot);
        if (spriteCache.TryGetValue(key, out Sprite cached) && cached) return cached;

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            pivot,
            pixelsPerUnit: 1f);

        sprite.name = $"CursorSprite_{texture.name}";
        spriteCache[key] = sprite;
        return sprite;
    }

    /// <summary>
    /// Places the cursor sprite at the mouse position, a fixed distance in front of the camera, and
    /// scales it so it covers the same number of screen pixels as the texture regardless of zoom.
    /// </summary>
    private void UpdateSceneCursorTransform()
    {
        if (!sceneCursorRenderer || !sceneCursorRenderer.sprite) return;

        Camera cam = ResolveSceneCamera();
        if (!cam)
        {
            sceneCursorRenderer.enabled = false;
            return;
        }

        // Re-parent if the camera was resolved after the object was created.
        if (sceneCursorRenderer.transform.parent != cam.transform)
            sceneCursorRenderer.transform.SetParent(cam.transform, false);

        Vector3 mouse = MousePosition;

        // Hide while the pointer is outside the game view instead of smearing it against the edge.
        bool onScreen =
            mouse.x >= 0f && mouse.x <= Screen.width &&
            mouse.y >= 0f && mouse.y <= Screen.height;

        sceneCursorRenderer.enabled = IsVisible && onScreen;
        if (!sceneCursorRenderer.enabled) return;

        mouse.x += shakeOffset.x;
        mouse.y += shakeOffset.y;

        float distance = Mathf.Max(sceneCursorDistance, cam.nearClipPlane + 0.01f);

        sceneCursorRenderer.transform.position =
            cam.ScreenToWorldPoint(new Vector3(mouse.x, mouse.y, distance));
        sceneCursorRenderer.transform.rotation = cam.transform.rotation;

        // World units covered by one screen pixel at that distance.
        float worldPerPixel = cam.orthographic
            ? (cam.orthographicSize * 2f) / Screen.height
            : (2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) / Screen.height;

        float scale = worldPerPixel * Mathf.Max(0.0001f, sceneCursorScale);
        sceneCursorRenderer.transform.localScale = new Vector3(scale, scale, 1f);
    }

    private void OnDestroy()
    {
        if (Application.isPlaying)
        {
            foreach (Sprite sprite in spriteCache.Values)
                if (sprite) Destroy(sprite);

            if (sceneCursorRenderer) Destroy(sceneCursorRenderer.gameObject);
        }

        spriteCache.Clear();

        // Leave the player with a working OS cursor.
        if (renderCursorInScene) Cursor.visible = true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying || Instance != this) return;
        ApplyRenderMode();
    }
#endif

    // VISIBILITY

    /// <summary>Makes the cursor appear.</summary>
    public void ShowCursor()
    {
        SetCursorVisible(true);
    }

    /// <summary>Makes the cursor disappear.</summary>
    public void HideCursor()
    {
        SetCursorVisible(false);
    }

    /// <summary>Toggles cursor visibility.</summary>
    public void ToggleCursor()
    {
        SetCursorVisible(!IsVisible);
    }

    /// <summary>Shows or hides the cursor.</summary>
    public void SetCursorVisible(bool visible)
    {
        IsVisible = visible;

        if (renderCursorInScene)
        {
            // The hardware cursor stays hidden in this mode; the sprite carries the visibility.
            Cursor.visible = false;
            if (sceneCursorRenderer)
                sceneCursorRenderer.enabled = visible && sceneCursorRenderer.sprite != null;
            if (visible) ApplyIcon(currentIcon);
            return;
        }

        Cursor.visible = visible;

        if (visible) ApplyIcon(currentIcon);
    }

    // ICON GETTERS (for other systems that want to reuse the configured textures)

    public CursorIcon DefaultIcon => defaultIcon;
    public CursorIcon ClickIcon => clickIcon;
    public CursorIcon DragIcon => dragIcon;
    public CursorIcon ButtonIcon => buttonIcon;
    public CursorIcon ButtonClickIcon => buttonClickIcon;
}
