using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using PrimeTween;

public class CursorDnDManager : MonoBehaviour
{
    public static CursorDnDManager Instance;

    [Header("Drag")]
    public DragableObject currentDraggingObject;

    private DragableObject currentReleasedObject;
    
    [Header("Hover")]
    public DragableObject currentHoverObject;

    [SerializeField] private float followSpeed = 20f;

    [SerializeField] private Canvas canvas;
    private RectTransform canvasRect;

    /// <summary>
    /// The rect a dragged object is re-parented under for the duration of the drag.
    /// <para>Two things go wrong while an icon stays inside its slot. It draws inside its slot's
    /// little corner of the hierarchy, so <c>SetAsLastSibling</c> only lifts it above its own
    /// siblings and it slides BEHIND any panel later in the HUD. And its anchoredPosition is
    /// measured against the slot rather than the canvas, so every coordinate this manager computes
    /// has to be corrected by wherever the slot happens to be.</para>
    /// </summary>
    public RectTransform DragLayer => canvasRect;

    // Where the drag began, and where the object sat at that moment. Everything during the drag is a
    // DELTA from these two, which is what makes the follow immune to the camera's pose (see
    // ScreenToCanvasDelta).
    private Vector2 dragStartScreenPos;
    private Vector2 dragStartAnchoredPos;
    private Vector2 dragTargetPos;
    
    [SerializeField] private float onClickScaleMultiplier = 1.1f;
    [SerializeField] private float onHoverScaleMult = 1.2f;

    [Header("Click Detection")]
    public static bool SuccessfulClick { get; private set; }

    private Coroutine emptyClickCoroutine;

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance);

        if (!canvas)
        {
            // FindGameObjectWithTag returns null when nothing carries the tag; dereferencing that
            // straight away took the whole manager out in Awake and left drag-and-drop dead.
            GameObject tagged = GameObject.FindGameObjectWithTag("MainCanvas");
            if (tagged) canvas = tagged.GetComponent<Canvas>();
        }
        if (!canvas) canvas = FindFirstObjectByType<Canvas>();

        if (canvas)
            canvasRect = canvas.GetComponent<RectTransform>();

        SetEmptyClick();
    }

    private void Update()
    {
        HandleRelease();
        HandleDragging();
        HandleHovering();
    }

 
    

    private void HandleHovering()
    {
        if (currentDraggingObject) return;
    }
    
    public void SetHover(DragableObject obj)
    {
        if (currentHoverObject == obj || currentDraggingObject)
            return;
        
        ClearHover();
        
        currentHoverObject = obj;
        
        Tween.Scale(
            currentHoverObject.transform,
            endValue: currentHoverObject.originalScale * onHoverScaleMult,
            duration: 0.3f);
        currentHoverObject.OnHoverStart();
    }

    public void ClearHover()
    {
        if (!currentHoverObject || currentDraggingObject)
            return;
        
        Tween.Scale(
            currentHoverObject.transform,
            endValue: currentHoverObject.originalScale,
            duration: 0.3f);
        
        currentHoverObject.OnHoverEnd();

        currentHoverObject = null;
    }
    
    private void HandleDragging()
    {
        if (!currentDraggingObject)
            return;

        RectTransform rect = currentDraggingObject.RectTransform;

        if (!rect || !canvas || !canvasRect)
            return;

        dragTargetPos =
            dragStartAnchoredPos +
            ScreenToCanvasDelta((Vector2)Input.mousePosition - dragStartScreenPos);

        // Framerate-independent exponential follow, written straight to the rect.
        //
        // Deliberately not a tween. Starting a fresh 1/followSpeed tween every frame left dozens of
        // them alive on the same property at once, each one easing toward a target that was already
        // stale when it was created — and none of them stopped when the drag ended, so they went on
        // fighting the slot's own return animation afterwards.
        //
        // Unscaled time so the icon still tracks the cursor while the game is paused.
        float t = 1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime);
        rect.anchoredPosition = Vector2.Lerp(rect.anchoredPosition, dragTargetPos, t);
    }

    /// <summary>
    /// Cursor movement in pixels, expressed in canvas units.
    ///
    /// <para>Straight through <see cref="Canvas.scaleFactor"/> rather than by intersecting the
    /// cursor ray with the canvas plane. The HUD canvas is Screen Space - Camera, half a unit in
    /// front of an RTS camera that pans, leans into its own movement, zooms and shakes — and Unity
    /// only re-poses that canvas at render time. So a ray cast from where the camera is NOW against
    /// where the canvas was LAST frame intersects somewhere arbitrary, which is what sent a held
    /// item flying off to a random corner of the screen the moment the player moved the camera.</para>
    /// </summary>
    private Vector2 ScreenToCanvasDelta(Vector2 screenDelta)
    {
        // A world-space canvas has no pixel-to-unit factor to speak of, so it keeps the old path.
        if (canvas.renderMode == RenderMode.WorldSpace)
        {
            Camera worldCam = canvas.worldCamera ? canvas.worldCamera : Camera.main;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, Input.mousePosition, worldCam, out Vector2 now);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, dragStartScreenPos, worldCam, out Vector2 start);

            return now - start;
        }

        float scale = canvas.scaleFactor;
        if (scale <= 0f) scale = 1f;

        return screenDelta / scale;
    }


    private void HandleRelease()
    {
        if (!currentDraggingObject)
            return;
        
        if (!Input.GetMouseButtonUp(0)) return;
        
        void OnRelease()
        {
            currentReleasedObject.Release();
        }

        currentReleasedObject = currentDraggingObject;
        currentDraggingObject = null;
        
        OnRelease();
    }

    public void StartDragging(DragableObject dragObject)
    {
        SetSuccessfulClick();
        
        // Re-parents the object onto the drag layer, so everything below is measured against the
        // canvas rather than against whatever small rect the object was living in.
        if (!dragObject.StartDragging()) return;
        
        currentDraggingObject = dragObject;

        Tween.StopAll(currentDraggingObject.transform);

        Sequence.Create(cycles: 1).
            Group(
            Tween.Scale(
                target: currentDraggingObject.transform,
                endValue: currentDraggingObject.transform.localScale * onClickScaleMultiplier,
                duration: 0.07f,
                ease: Ease.OutBounce
            ))
            .Chain(
            Tween.Scale(
                target: currentDraggingObject.transform,
                endValue: currentDraggingObject.originalScale * onHoverScaleMult,
                duration: 0.1f,
                ease: Ease.OutQuad
            )
        );

        RectTransform rect = dragObject.RectTransform;

        dragStartScreenPos = Input.mousePosition;
        dragStartAnchoredPos = rect ? rect.anchoredPosition : Vector2.zero;
        dragTargetPos = dragStartAnchoredPos;
    }

    /// <summary>
    /// Call from SlotBase's IPointerClickHandler or IPointerDownHandler.
    /// </summary>
    public void RegisterSlotClick(SlotBase slot)
    {
        SetSuccessfulClick();
    }

    
    public void SetSuccessfulClick()
    {
        SuccessfulClick = true;
        if (emptyClickCoroutine != null) StopCoroutine(emptyClickCoroutine);
        emptyClickCoroutine = StartCoroutine(SetEmptyClickCoroutine());
    }
    
    public void SetEmptyClick()
    {
        SuccessfulClick = false;
    }
    
    private IEnumerator SetEmptyClickCoroutine(float duration = 0.001f)
    {
        float timeElapsed = 0;
        while (timeElapsed < duration)
        {
            timeElapsed += Time.deltaTime;
            yield return null;
        }
        SetEmptyClick();
    }
}
