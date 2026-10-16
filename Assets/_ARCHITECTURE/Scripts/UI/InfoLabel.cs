using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The floating info card shown when the player rests the cursor on something — an item icon in the
/// HUD, and anything else that later wants to explain itself without being clicked.
///
/// <para>Two texts and nothing else: <see cref="title"/> (what the thing is called) and
/// <see cref="content"/> (what it does). Either may be left empty; the object carrying it is switched
/// off for that showing, so a card with no description does not leave a gap under its title.</para>
///
/// <para>There is only ever ONE card on screen. <see cref="Show"/> is static and reuses a single
/// instance built from the prefab the first time it is asked for — a second hover does not spawn a
/// second card, it retargets the one that exists. That also means the caller never has to track
/// what it opened: <see cref="Hide"/> closes whatever is up.</para>
///
/// <para>The card sits BESIDE what it describes — to the right of it by default (see
/// <see cref="Side"/>) — rather than over it, and flips to the other side when the screen edge is in
/// the way.</para>
///
/// <para>The card never takes raycasts. It is deliberately placed under the cursor's neighbourhood,
/// and a tooltip that could be hovered would flicker itself out of existence the moment it appeared
/// over the icon that opened it.</para>
/// </summary>
[DisallowMultipleComponent]
public class InfoLabel : MonoBehaviour
{
    /// <summary>Which side of the described thing the card opens on.</summary>
    public enum Side
    {
        Right,
        Left,
        Above,
        Below
    }

    [Header("Texts")]
    [Tooltip("Headline of the card — the item's name. Left empty, a TMP_Text named 'Title' in the " +
             "children is used.")]
    public TMP_Text title;

    [Tooltip("Body of the card — the item's description. Left empty, the first TMP_Text in the " +
             "children that is not the title is used.")]
    public TMP_Text content;

    [Header("Layout")]
    [Tooltip("Background whose height is grown to fit the texts. Optional — leave it empty when the " +
             "card is driven by a Content Size Fitter instead.")]
    [SerializeField] private Image backgroundImage;

    [Tooltip("Space kept above the title and below the description when the background is resized.")]
    [Min(0f)] [SerializeField] private float backgroundPadding = 14f;

    [Tooltip("Space kept between the title and the description.")]
    [Min(0f)] [SerializeField] private float sectionSpacing = 6f;

    [Tooltip("Which side of the hovered thing the card sits on. Right is the default: the card " +
             "opens beside the icon rather than over it, so the thing being described stays visible " +
             "while it is read.")]
    [SerializeField] private Side placement = Side.Right;

    [Tooltip("Space between the edge of the hovered thing and the near edge of the card, in canvas " +
             "units.")]
    [Min(0f)] [SerializeField] private float gap = 12f;

    [Tooltip("Extra nudge applied after the side placement, in canvas units. Normally zero — it is " +
             "for fine-tuning a card that sits fractionally off against a particular HUD.")]
    [SerializeField] private Vector2 offset = Vector2.zero;

    [Tooltip("Card flips to the opposite side when its chosen side would run off the canvas, so a " +
             "slot at the right edge of the screen gets its card on the left instead of clamped " +
             "back over itself.")]
    [SerializeField] private bool flipWhenClipped = true;

    [Tooltip("Keeps the whole card inside the canvas, so a slot at the screen edge does not push half " +
             "of its description off screen.")]
    [SerializeField] private bool clampToCanvas = true;

    [Tooltip("Kept clear of the canvas edges when clamping.")]
    [SerializeField] private float screenPadding = 12f;

    [Tooltip("Card trails the cursor while it is open, instead of staying pinned where it opened.")]
    [SerializeField] private bool followCursor = false;

    [Tooltip("Seconds the follow takes to catch up with the cursor. 0 = instant.")]
    [Min(0f)] [SerializeField] private float followSmoothing = 0.05f;

    [Header("Appearance animation")]
    [Min(0f)] [SerializeField] private float fadeInDuration = 0.18f;
    [Min(0f)] [SerializeField] private float fadeOutDuration = 0.12f;

    [Tooltip("Scale the card grows from as it appears. 1 = no growth.")]
    [Min(0.01f)] [SerializeField] private float spawnScale = 0.9f;

    [Tooltip("Distance the card drifts over the fade in, in canvas units — it starts that much " +
             "closer to the thing it describes and slides out to its place, so it reads as coming " +
             "out of the icon. 0 = no drift.")]
    [SerializeField] private float spawnDrift = 10f;

    [SerializeField] private Ease showEase = Ease.OutBack;
    [SerializeField] private Ease hideEase = Ease.InQuad;

    // ── Static access ────────────────────────────────────────────

    /// <summary>The card currently on screen, or null when nothing is showing.</summary>
    public static InfoLabel Current { get; private set; }

    /// <summary>
    /// Prefab the shared card is built from. Set it once from anywhere (a manager's Awake, an
    /// inspector field on a caller); when it is left null the prefab is loaded from
    /// <c>Resources/<see cref="ResourcesPath"/></c> instead, which is what lets an icon the slot
    /// generated at runtime — with no serialized reference to hand — still show a card.
    /// </summary>
    public static InfoLabel Prefab;

    public const string ResourcesPath = "ARCHITECTURE/Prefabs/InfoLabel";

    // Set once the Resources lookup has been tried, so a project that simply has no card prefab
    // does not pay for (or log) the lookup on every hover.
    private static bool resourcesLookupDone;
    private static bool warnedAboutMissingPrefab;

    /// <summary>
    /// Shows the card with these texts, next to <paramref name="source"/>.
    ///
    /// <para>Safe to call while a card is already up — the existing one is retargeted and rewritten
    /// rather than replaced, so moving the cursor along a row of slots reads as one card sliding
    /// across rather than a stutter of spawns.</para>
    /// </summary>
    /// <param name="titleText">Headline — the item's name.</param>
    /// <param name="contentText">Body — the item's description. May be empty.</param>
    /// <param name="source">What the card describes; it is placed relative to this. Null anchors it
    /// to the cursor.</param>
    /// <param name="prefab">Card prefab to build from, when the shared one has not been built yet.
    /// Null falls back to <see cref="Prefab"/> and then to Resources.</param>
    public static InfoLabel Show(string titleText, string contentText,
                                 RectTransform source = null, InfoLabel prefab = null)
    {
        InfoLabel label = GetOrCreate(prefab);
        if (!label) return null;

        label.ShowInstance(titleText, contentText, source);
        return label;
    }

    /// <summary>
    /// Closes the card on screen. <paramref name="only"/> limits that to one particular card, so a
    /// hover that has already been superseded by another one cannot close its successor — pass the
    /// source rect the caller showed the card for.
    /// </summary>
    public static void Hide(RectTransform only = null)
    {
        if (!Current) return;
        if (only && Current.anchor != only) return;

        Current.HideInstance();
    }

    /// <summary>The shared card, built on first use. Null when no prefab can be found.</summary>
    private static InfoLabel GetOrCreate(InfoLabel prefab)
    {
        if (Current) return Current;

        InfoLabel source = prefab ? prefab : ResolvePrefab();

        if (!source)
        {
            if (!warnedAboutMissingPrefab)
            {
                warnedAboutMissingPrefab = true;
                h.Out("InfoLabel: no card prefab. Assign one on the hovered object, set " +
                      $"InfoLabel.Prefab, or put a prefab at Resources/{ResourcesPath}.");
            }
            return null;
        }

        Transform canvas = ResolveCanvas();
        if (!canvas) return null;

        Current = Instantiate(source, canvas);
        Current.name = source.name;

        // Parked until the first Show. Done here rather than in Awake so a prefab authored with its
        // root switched off still initialises correctly — Awake would otherwise re-disable the card
        // on the very frame it was being switched on.
        Current.Init();
        Current.gameObject.SetActive(false);

        return Current;
    }

    private static InfoLabel ResolvePrefab()
    {
        if (Prefab) return Prefab;

        if (!resourcesLookupDone)
        {
            resourcesLookupDone = true;
            Prefab = Resources.Load<InfoLabel>(ResourcesPath);
        }

        return Prefab;
    }

    private static Transform ResolveCanvas()
    {
        Canvas canvas = null;

        GameObject tagged = GameObject.FindGameObjectWithTag("MainCanvas");
        if (tagged) canvas = tagged.GetComponent<Canvas>();

        if (!canvas) canvas = FindFirstObjectByType<Canvas>();

        if (!canvas)
        {
            h.Out("InfoLabel: no Canvas in the scene to put the card on.");
            return null;
        }

        return canvas.transform;
    }

    // ── Instance ─────────────────────────────────────────────────

    private RectTransform rect;
    private RectTransform canvasRect;
    private Canvas parentCanvas;
    private CanvasGroup group;

    // What the card is describing. Kept so Hide(source) can tell whether the card still belongs to
    // the caller asking it to close.
    private RectTransform anchor;

    private bool isShowing;
    private bool initialized;

    private void Awake() => Init();

    /// <summary>
    /// Caches the card's own components. Idempotent, and called both from Awake and from the first
    /// <see cref="Show"/> — a card whose root was authored inactive gets no Awake until it is shown,
    /// and it must not be reading a null CanvasGroup at that point.
    /// </summary>
    private void Init()
    {
        if (initialized) return;
        initialized = true;

        rect = (RectTransform)transform;

        group = GetComponent<CanvasGroup>();
        if (!group) group = gameObject.AddComponent<CanvasGroup>();

        // The card explains what is under the cursor; it must never become what is under the cursor.
        group.blocksRaycasts = false;
        group.interactable = false;

        ResolveTexts();
        ResolveCanvasRefs();

        group.alpha = 0f;
    }

    private void ResolveTexts()
    {
        if (title && content) return;

        TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);

        foreach (TMP_Text text in texts)
        {
            if (!title && text.name.ToLowerInvariant().Contains("title")) title = text;
        }

        // Whatever is left over, in hierarchy order: first free text is the title if none was named
        // one, the next is the body.
        foreach (TMP_Text text in texts)
        {
            if (text == title) continue;

            if (!title) { title = text; continue; }
            if (!content) { content = text; break; }
        }
    }

    private void ResolveCanvasRefs()
    {
        parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas) canvasRect = (RectTransform)parentCanvas.transform;
    }

    /// <summary>Writes the texts, moves the card into place and fades it in.</summary>
    private void ShowInstance(string titleText, string contentText, RectTransform source)
    {
        Init();

        anchor = source;

        // Read before the card is switched on: a card already on screen — mid fade-out, or simply
        // being retargeted at the next slot — carries on from the alpha it is at rather than
        // snapping back to invisible, which would read as a flicker rather than a move.
        float startAlpha = gameObject.activeSelf ? group.alpha : 0f;

        gameObject.SetActive(true);
        transform.SetAsLastSibling();

        SetTexts(titleText, contentText);

        Tween.StopAll(transform);
        Tween.StopAll(group);
        spawnMoveRunning = false;

        // The rise only plays for a card that is actually appearing. Replaying it on every retarget
        // would drop the card and lift it again each time the cursor crossed into the next slot.
        Reposition(instant: true, animateDrift: startAlpha <= 0f);

        group.alpha = startAlpha;
        transform.localScale = Vector3.one * Mathf.Lerp(spawnScale, 1f, startAlpha);

        isShowing = true;

        float fadeIn = fadeInDuration * (1f - startAlpha);

        if (fadeIn > 0f) Tween.Alpha(group, endValue: 1f, duration: fadeIn);
        else group.alpha = 1f;

        if (fadeInDuration > 0f)
            Tween.Scale(transform, endValue: Vector3.one, duration: fadeInDuration, ease: showEase);
        else
            transform.localScale = Vector3.one;
    }

    /// <summary>Fades the card out and parks it, ready to be shown again.</summary>
    private void HideInstance()
    {
        if (!isShowing) return;
        isShowing = false;

        anchor = null;

        Tween.StopAll(transform);
        Tween.StopAll(group);
        spawnMoveRunning = false;

        if (fadeOutDuration <= 0f)
        {
            group.alpha = 0f;
            gameObject.SetActive(false);
            return;
        }

        Tween.Alpha(group, endValue: 0f, duration: fadeOutDuration, ease: hideEase)
             .OnComplete(this, label =>
             {
                 // Another Show may have come in during the fade; that one owns the card now.
                 if (label.isShowing) return;
                 label.gameObject.SetActive(false);
             });

        Tween.Scale(transform, endValue: Vector3.one * spawnScale,
                    duration: fadeOutDuration, ease: hideEase);
    }

    /// <summary>
    /// Fills the two texts. An empty string switches its text object off entirely rather than
    /// leaving a blank line, so a card with only a title closes up around it.
    /// </summary>
    public void SetTexts(string titleText, string contentText)
    {
        if (title)
        {
            bool has = !string.IsNullOrWhiteSpace(titleText);
            title.text = has ? titleText : "";
            if (title.gameObject.activeSelf != has) title.gameObject.SetActive(has);
        }

        if (content)
        {
            bool has = !string.IsNullOrWhiteSpace(contentText);
            content.text = has ? contentText : "";
            if (content.gameObject.activeSelf != has) content.gameObject.SetActive(has);
        }

        ScaleBackground();
    }

    /// <summary>
    /// Grows the background to hold both texts. Only touches the height — the width is whatever the
    /// card was authored at, which is what keeps the description wrapping to a readable column
    /// instead of stretching into one long line.
    ///
    /// <para>The pivot the background was authored with does not matter for placement: the card is
    /// re-centred on the thing it describes every time it is shown, after this has run.</para>
    /// </summary>
    private void ScaleBackground()
    {
        if (!backgroundImage) return;

        // TMP has not measured the new strings yet at this point; reading preferredHeight without
        // this would size the card to the text it showed on the PREVIOUS hover.
        if (title) LayoutRebuilder.ForceRebuildLayoutImmediate(title.rectTransform);
        if (content) LayoutRebuilder.ForceRebuildLayoutImmediate(content.rectTransform);

        bool hasTitle = title && title.gameObject.activeSelf;
        bool hasContent = content && content.gameObject.activeSelf;

        float height = backgroundPadding * 2f;
        if (hasTitle) height += title.preferredHeight;
        if (hasContent) height += content.preferredHeight;
        if (hasTitle && hasContent) height += sectionSpacing;

        RectTransform bg = backgroundImage.rectTransform;
        Vector2 size = bg.sizeDelta;
        size.y = height;
        bg.sizeDelta = size;
    }

    // True while the appearance tween is still moving the card. The per-frame follow stands aside
    // for it; otherwise the two would pull the card in opposite directions for the length of the fade.
    private bool spawnMoveRunning;

    private void LateUpdate()
    {
        if (!isShowing || spawnMoveRunning) return;
        if (!followCursor && !anchor) return;

        Reposition(instant: false);
    }

    /// <summary>
    /// Puts the card beside its anchor (or the cursor) on <see cref="placement"/>'s side, flipped to
    /// the opposite side rather than clamped when that one would run off screen.
    /// </summary>
    private void Reposition(bool instant, bool animateDrift = false)
    {
        if (!canvasRect) ResolveCanvasRefs();
        if (!canvasRect) return;

        Vector2 size = rect.rect.size;

        // The box the card has to sit beside: the hovered thing, or a zero-size box at the cursor
        // when there is nothing to anchor to.
        Rect target = (anchor && !followCursor) ? AnchorRectInCanvas() : PointerRectInCanvas();

        Side side = placement;
        Vector2 centre = CentreFor(side, target, size);

        // Flipped before clamping: a card pushed back over the icon it describes hides the thing the
        // player is looking at, which is worse than showing it on the other side.
        if (flipWhenClipped && !FitsOnSide(side, centre, size))
        {
            Vector2 flipped = CentreFor(Opposite(side), target, size);

            if (FitsOnSide(Opposite(side), flipped, size))
            {
                side = Opposite(side);
                centre = flipped;
            }
        }

        if (clampToCanvas) centre = Clamp(centre, size);

        // anchoredPosition addresses the PIVOT, not the centre, so a card authored with any pivot
        // other than the middle would otherwise sit half a card away from where it was placed.
        Vector2 local = centre + Vector2.Scale(rect.pivot - new Vector2(0.5f, 0.5f), size);

        if (instant || followSmoothing <= 0f)
        {
            bool drift = animateDrift && spawnDrift != 0f && fadeInDuration > 0f;

            // Started in against the icon and tweened out to the real target, so the drift and the
            // fade are one movement rather than two that can disagree about where the card ends up.
            rect.anchoredPosition = drift ? local - Direction(side) * spawnDrift : local;

            if (drift)
            {
                spawnMoveRunning = true;
                Tween.UIAnchoredPosition(rect, endValue: local,
                                         duration: fadeInDuration, ease: showEase)
                     .OnComplete(this, label => label.spawnMoveRunning = false);
            }

            return;
        }

        rect.anchoredPosition = Vector2.Lerp(
            rect.anchoredPosition, local, 1f - Mathf.Exp(-Time.unscaledDeltaTime / followSmoothing));
    }

    /// <summary>
    /// The hovered thing's rect, expressed in the canvas's own space.
    ///
    /// <para>Done as pure transform math (world → canvas local) rather than by going out to screen
    /// space and back. On a Screen Space - Camera canvas the plane the UI lives on travels with the
    /// game camera, and Unity only moves it just before rendering — so a camera panned during Update
    /// leaves this LateUpdate holding a new camera matrix and last frame's canvas plane. Projecting
    /// through the camera there reads the icon as being wherever the camera has moved to, and the
    /// card takes off across the screen for as long as the player keeps panning. The card and the
    /// icon sit in the same hierarchy, so their relationship never needs a camera at all.</para>
    /// </summary>
    private Rect AnchorRectInCanvas()
    {
        Vector3[] corners = new Vector3[4];
        anchor.GetWorldCorners(corners);

        // Bottom-left and top-right are enough — the UI is unrotated, so the box they describe is
        // the box the card has to avoid.
        Vector3 min = canvasRect.InverseTransformPoint(corners[0]);
        Vector3 max = canvasRect.InverseTransformPoint(corners[2]);

        return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y),
                               Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
    }

    /// <summary>A zero-size rect at the cursor, so the cursor can be placed beside like anything else.</summary>
    private Rect PointerRectInCanvas()
    {
        Vector2 screen = PointerPosition();

        // A screen-space canvas fills the screen, so the conversion is a plain scale — and, as with
        // the anchor above, one that cannot be thrown off by a camera that has already moved this
        // frame. Only a World Space canvas genuinely needs the camera to answer this.
        if (parentCanvas && parentCanvas.renderMode != RenderMode.WorldSpace)
        {
            float scale = parentCanvas.scaleFactor > 0f ? parentCanvas.scaleFactor : 1f;
            return new Rect(screen / scale + canvasRect.rect.min, Vector2.zero);
        }

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, screen, EventCamera(), out Vector2 local);

        return new Rect(local, Vector2.zero);
    }

    /// <summary>
    /// Where the card's centre goes to sit on <paramref name="side"/> of <paramref name="target"/>,
    /// clear of it by <see cref="gap"/>, and centred on it along the other axis.
    /// </summary>
    private Vector2 CentreFor(Side side, Rect target, Vector2 size)
    {
        Vector2 centre = target.center;

        switch (side)
        {
            case Side.Right: centre.x = target.xMax + gap + size.x * 0.5f; break;
            case Side.Left:  centre.x = target.xMin - gap - size.x * 0.5f; break;
            case Side.Above: centre.y = target.yMax + gap + size.y * 0.5f; break;
            case Side.Below: centre.y = target.yMin - gap - size.y * 0.5f; break;
        }

        return centre + offset;
    }

    private static Side Opposite(Side side) => side switch
    {
        Side.Right => Side.Left,
        Side.Left  => Side.Right,
        Side.Above => Side.Below,
        _          => Side.Above
    };

    /// <summary>Unit vector pointing away from the described thing, on the given side.</summary>
    private static Vector2 Direction(Side side) => side switch
    {
        Side.Right => Vector2.right,
        Side.Left  => Vector2.left,
        Side.Above => Vector2.up,
        _          => Vector2.down
    };

    /// <summary>
    /// Whether the card still has room on the side it was put on. Only the placement axis is
    /// tested — a card that is simply taller than the space above it is not a reason to move it from
    /// the right of the icon to the left, and clamping handles the other axis.
    /// </summary>
    private bool FitsOnSide(Side side, Vector2 centre, Vector2 size)
    {
        Rect canvasArea = canvasRect.rect;
        Vector2 half = size * 0.5f;

        return side switch
        {
            Side.Right => centre.x + half.x <= canvasArea.xMax - screenPadding,
            Side.Left  => centre.x - half.x >= canvasArea.xMin + screenPadding,
            Side.Above => centre.y + half.y <= canvasArea.yMax - screenPadding,
            _          => centre.y - half.y >= canvasArea.yMin + screenPadding
        };
    }

    /// <summary>Keeps the card's box inside the canvas.</summary>
    private Vector2 Clamp(Vector2 centre, Vector2 size)
    {
        Rect canvasArea = canvasRect.rect;
        Vector2 half = size * 0.5f;

        float minX = canvasArea.xMin + screenPadding + half.x;
        float maxX = canvasArea.xMax - screenPadding - half.x;
        float minY = canvasArea.yMin + screenPadding + half.y;
        float maxY = canvasArea.yMax - screenPadding - half.y;

        // A card wider or taller than the canvas would give an inverted range; centring it is the
        // only sane answer there.
        centre.x = minX <= maxX ? Mathf.Clamp(centre.x, minX, maxX) : canvasArea.center.x;
        centre.y = minY <= maxY ? Mathf.Clamp(centre.y, minY, maxY) : canvasArea.center.y;

        return centre;
    }

    /// <summary>
    /// Cursor position, read through the new Input System when it is available and through the legacy
    /// Input class otherwise — the project uses both, so neither can be assumed.
    /// </summary>
    private static Vector2 PointerPosition()
    {
        if (Mouse.current != null) return Mouse.current.position.ReadValue();
        return Input.mousePosition;
    }

    private Camera EventCamera()
    {
        if (!parentCanvas) return null;
        return parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : parentCanvas.worldCamera;
    }

    private void OnDestroy()
    {
        if (Current == this) Current = null;
    }
}
