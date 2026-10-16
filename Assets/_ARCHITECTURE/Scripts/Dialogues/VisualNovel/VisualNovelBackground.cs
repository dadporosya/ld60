using System.Collections;
using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
using UnityEngine.UI;

/// <summary>What one image layer of the stage shows: a sprite and the material it is drawn with.</summary>
public struct VisualNovelLook
{
    /// <summary>Null = keep the sprite the layer shows now (nothing, if it shows nothing).</summary>
    public Sprite sprite;
    /// <summary>Null = keep the material the layer is drawn with now.</summary>
    public Material material;
    /// <summary>The layer shows nothing; <see cref="sprite"/> is ignored.</summary>
    public bool clear;
    /// <summary>When true, this look scales so it covers the entire screen on any resolution / aspect ratio.</summary>
    public bool stretch;

    public VisualNovelLook(Sprite sprite, Material material, bool clear = false, bool stretch = false)
    {
        this.sprite = sprite;
        this.material = material;
        this.clear = clear;
        this.stretch = stretch;
    }
}

/// <summary>Which of the stage's image layers something applies to.</summary>
public enum VisualNovelLayers { Background, Foreground, Both }

/// <summary>
/// The visual novel's picture: a background layer, a foreground layer over it, a full-screen color
/// cover, and the transitions between one node's picture and the next (<see cref="VisualNovelTransition"/>).
/// FadeThroughBlur and CrossfadeBlur also drive the <see cref="BlurVolumeManager"/>'s radius.
///
/// Hierarchy, bottom to top: <see cref="front"/> (the background on screen), <see cref="back"/>
/// (the incoming background during a crossfade), <see cref="foreground"/> and
/// <see cref="foregroundBack"/> (the same pair for the foreground) and <see cref="overlay"/> (the
/// color cover for fades to and from black). Missing Back / Foreground images are created at
/// runtime in the right place. All of them should stretch over the whole canvas.
///
/// Both layers change together, through the same transition. Only one transition runs at a time.
/// Starting a new one - or <see cref="Complete"/> - snaps the running one straight to its end
/// state, so a player skipping through nodes never leaves two fades fighting over the same images.
/// </summary>
public class VisualNovelBackground : MonoBehaviour
{
    [Tooltip("Image showing the current background.")]
    [SerializeField] private Image front;
    [Tooltip("Image the incoming background fades in on during a crossfade. Must sit ABOVE Front. " +
             "Created at runtime when empty.")]
    [SerializeField] private Image back;
    [Tooltip("Image showing the current foreground, drawn over the background. Must sit above Back. " +
             "Created at runtime (a copy of Front's rect) when empty.")]
    [SerializeField] private Image foreground;
    [Tooltip("Image the incoming foreground fades in on during a crossfade. Must sit ABOVE Foreground. " +
             "Created at runtime when empty.")]
    [SerializeField] private Image foregroundBack;
    [Tooltip("Full-screen image used for fades to and from a color. Must sit above everything else.")]
    [SerializeField] private Image overlay;
    [SerializeField] private Ease fadeEase = Ease.InOutSine;
    [Tooltip("Blur used by the FadeThroughBlur / CrossfadeBlur transitions. Defaults to BlurVolumeManager.Instance.")]
    [SerializeField] private BlurVolumeManager blur;

    /// <summary>A pair of images showing one layer: the one on screen and the one fading in over it.</summary>
    private class Layer
    {
        public Image current;
        public Image incoming;
        /// <summary>The image's material as set up in the scene, restored by <see cref="ResetState"/>.</summary>
        public Material sceneMaterial;
        /// <summary>
        /// Crossfade the old image out while the new one fades in. The background doesn't: fading
        /// an opaque picture out would show whatever is behind the canvas halfway through. The
        /// foreground does: its old picture would otherwise show through the new one's gaps.
        /// </summary>
        public bool fadeOutOld;
        /// <summary>Where the two images sit at rest; <see cref="SetOffset"/> moves them from here.</summary>
        public Vector3 currentRest;
        public Vector3 incomingRest;
        /// <summary>The images' scale at rest; <see cref="SetZoom"/> multiplies it.</summary>
        public Vector3 currentRestScale = Vector3.one;
        public Vector3 incomingRestScale = Vector3.one;
        public Vector3 baseCurrentRestScale = Vector3.one;
        public Vector3 baseIncomingRestScale = Vector3.one;
        public bool currentStretch;
        public bool incomingStretch;

        /// <summary>
        /// When additionalStretching or per-look stretch is active, rescales rest scale so the rendered image covers the screen.
        /// </summary>
        public void UpdateScaling(bool additionalStretching, RectTransform stageRect)
        {
            if (current)
            {
                float scale = (additionalStretching || currentStretch) ? ComputeScaleToCover(current, stageRect) : 1f;
                currentRestScale = baseCurrentRestScale * scale;
            }
            if (incoming)
            {
                float scale = (additionalStretching || incomingStretch) ? ComputeScaleToCover(incoming, stageRect) : 1f;
                incomingRestScale = baseIncomingRestScale * scale;
            }
            Apply();
        }

        private static float ComputeScaleToCover(Image img, RectTransform stageRect)
        {
            if (!img || !img.sprite) return 1f;
            RectTransform rect = img.rectTransform;
            RectTransform parent = rect ? rect.parent as RectTransform : null;

            float viewportWidth = 0f;
            float viewportHeight = 0f;

            if (stageRect != null && stageRect.rect.width > 0f && stageRect.rect.height > 0f)
            {
                viewportWidth = stageRect.rect.width;
                viewportHeight = stageRect.rect.height;
            }
            else if (parent != null && parent.rect.width > 0f && parent.rect.height > 0f)
            {
                viewportWidth = parent.rect.width;
                viewportHeight = parent.rect.height;
            }
            else if (rect != null && rect.rect.width > 0f && rect.rect.height > 0f)
            {
                viewportWidth = rect.rect.width;
                viewportHeight = rect.rect.height;
            }
            else
            {
                viewportWidth = Screen.width;
                viewportHeight = Screen.height;
            }

            if (viewportWidth <= 0f || viewportHeight <= 0f) return 1f;

            if (img.preserveAspect)
            {
                Vector2 spriteSize = img.sprite.rect.size;
                if (spriteSize.x <= 0f || spriteSize.y <= 0f) return 1f;
                float spriteAspect = spriteSize.x / spriteSize.y;
                float viewportAspect = viewportWidth / viewportHeight;

                // When preserveAspect is on, Unity fits the sprite into the RectTransform:
                // If viewport is narrower/taller than sprite (viewportAspect < spriteAspect),
                // drawn height is viewportWidth / spriteAspect. Scale up to fill height.
                if (viewportAspect < spriteAspect)
                {
                    float drawnHeight = viewportWidth / spriteAspect;
                    if (drawnHeight > 0f)
                        return Mathf.Max(1f, viewportHeight / drawnHeight);
                }
                // If viewport is wider than sprite (viewportAspect > spriteAspect),
                // drawn width is viewportHeight * spriteAspect. Scale up to fill width.
                else if (viewportAspect > spriteAspect)
                {
                    float drawnWidth = viewportHeight * spriteAspect;
                    if (drawnWidth > 0f)
                        return Mathf.Max(1f, viewportWidth / drawnWidth);
                }
            }
            return 1f;
        }

        // Each shake moves the layer by its own offset and they add up, so a one-off knock still
        // shows on top of a sustained jitter instead of the last one written each frame winning.
        private readonly Dictionary<object, Vector2> offsets = new Dictionary<object, Vector2>();
        private Vector2 offset;
        private float zoom = 1f;
        // Point that stays put while zooming, in parent units from the current image's pivot.
        private Vector2 zoomPivotShift;

        /// <summary>
        /// Sets <paramref name="source"/>'s share of the offset; both images (so a crossfade rides
        /// along) move by the sum of every source's share from rest.
        /// </summary>
        public void SetOffset(object source, Vector2 sourceOffset)
        {
            if (sourceOffset == Vector2.zero) offsets.Remove(source);
            else offsets[source] = sourceOffset;
            RecalculateOffset();
        }

        /// <summary>Back to rest: drops every source's share.</summary>
        public void ClearOffsets()
        {
            offsets.Clear();
            RecalculateOffset();
        }

        /// <summary><paramref name="source"/>'s share of the offset, or the total for null.</summary>
        public Vector2 GetOffset(object source)
        {
            if (source == null) return offset;
            return offsets.TryGetValue(source, out Vector2 o) ? o : Vector2.zero;
        }

        private void RecalculateOffset()
        {
            offset = Vector2.zero;
            foreach (Vector2 o in offsets.Values) offset += o;
            Apply();
        }

        public Vector2 Offset => offset;

        public float Zoom => zoom;

        /// <summary>
        /// Scales both images by <paramref name="zoom"/> from rest, around <paramref name="focus"/>
        /// (0..1 across the current image's rect, 0.5/0.5 = its centre), which stays in place.
        /// </summary>
        public void SetZoom(float zoom, Vector2 focus)
        {
            this.zoom = zoom;
            zoomPivotShift = Vector2.zero;
            if (current)
            {
                RectTransform rect = current.rectTransform;
                zoomPivotShift = Vector2.Scale((focus - rect.pivot) * rect.rect.size, currentRestScale);
            }
            Apply();
        }

        /// <summary>
        /// Writes offset and zoom onto both images. Scaling about the pivot moves the focus point by
        /// (zoom - 1) * its distance from the pivot, so the images are shifted back by that much.
        /// </summary>
        private void Apply()
        {
            Vector3 shift = (Vector3)(offset + (1f - zoom) * zoomPivotShift);
            if (current)
            {
                current.rectTransform.localPosition = currentRest + shift;
                current.rectTransform.localScale = currentRestScale * zoom;
            }
            if (incoming)
            {
                incoming.rectTransform.localPosition = incomingRest + shift;
                incoming.rectTransform.localScale = incomingRestScale * zoom;
            }
        }

        /// <summary>
        /// The look's material. When it has none: a new picture gets the scene's material (the
        /// previous one may be a procedural shader that ignores the sprite and would hide it),
        /// otherwise the layer keeps the one it is drawn with now.
        /// </summary>
        public Material MaterialFor(VisualNovelLook look)
        {
            if (look.material) return look.material;
            if (look.sprite && !look.clear) return sceneMaterial;
            return current ? current.material : sceneMaterial;
        }

        /// <summary>
        /// <paramref name="look"/> with its empty fields filled from what the layer shows now, so it
        /// no longer depends on the layer's state: a null sprite then means "show nothing".
        /// </summary>
        public VisualNovelLook Resolve(VisualNovelLook look)
        {
            Material material = MaterialFor(look);
            if (look.clear) return new VisualNovelLook(null, material, true, false);
            Sprite sprite = look.sprite ? look.sprite : current ? current.sprite : null;
            bool stretch = look.sprite ? look.stretch : currentStretch;
            return new VisualNovelLook(sprite, material, !sprite, stretch);
        }

        /// <summary>True when the layer already shows <paramref name="look"/>.</summary>
        public bool Shows(VisualNovelLook look)
        {
            if (!current) return true;
            bool empty = !current.enabled || !current.sprite;
            if (!look.sprite) return empty;
            return !empty && current.sprite == look.sprite && current.material == MaterialFor(look) && currentStretch == look.stretch;
        }
    }

    private readonly Layer backgroundLayer = new Layer();
    private readonly Layer foregroundLayer = new Layer { fadeOutOld = true };

    private Coroutine running;
    private bool transitioning;
    private int transitionId;
    // The running transition has put its new pictures in place (see WaitForArrival).
    private bool arrived;

    // End state of the running transition, applied at once when it is cut short.
    private bool endChangesLooks;
    private VisualNovelLook endBackground;
    private VisualNovelLook endForeground;
    private float endOverlayAlpha;
    private bool endClearsBlur;

    /// <summary>The background on screen (or about to be, while a transition runs).</summary>
    public Sprite Current => front ? front.sprite : null;

    public bool IsTransitioning => transitioning;

    /// <summary>
    /// True while a transition that drives the blur itself (FadeThroughBlur, CrossfadeBlur) is running. Blur
    /// effects must keep off the blur until it's done, or they overwrite it every frame.
    /// </summary>
    public bool IsBlurring => transitioning && endClearsBlur;

    /// <summary>Blur the FadeThroughBlur / CrossfadeBlur transitions animate (the assigned one, else the scene's).</summary>
    public BlurVolumeManager Blur
    {
        get
        {
            if (!blur) blur = BlurVolumeManager.Instance;
            if (!blur) blur = FindFirstObjectByType<BlurVolumeManager>();
            return blur;
        }
        set => blur = value;
    }

    /// <summary>True while the color overlay hides the picture, even partly.</summary>
    public bool IsCovered => overlay && overlay.enabled && overlay.color.a > 0f;

    private bool additionalStretching;
    /// <summary>
    /// When true, images on the background and foreground layers are scaled up so they cover the entire screen on any aspect ratio.
    /// </summary>
    public bool AdditionalStretching
    {
        get => additionalStretching;
        set
        {
            if (additionalStretching == value) return;
            additionalStretching = value;
            UpdateAllScaling();
        }
    }

    /// <summary>Backward compatibility alias for <see cref="AdditionalStretching"/>.</summary>
    public bool AdditionalScaling
    {
        get => AdditionalStretching;
        set => AdditionalStretching = value;
    }

    public void UpdateAllScaling()
    {
        RectTransform stageRect = transform as RectTransform;
        backgroundLayer.UpdateScaling(additionalStretching, stageRect);
        foregroundLayer.UpdateScaling(additionalStretching, stageRect);
    }

    private void OnRectTransformDimensionsChange()
    {
        UpdateAllScaling();
    }

    private void Awake()
    {
        EnsureStageCoverage(front);
        EnsureStageCoverage(back);

        // A crossfade needs two separate images per layer: the old picture underneath, the new one
        // fading in on top. Build the second one rather than fade an image against itself.
        if (front && (!back || back == front)) back = CreateImage(front, "Back (auto)", front);

        if (front && !foreground)
        {
            foreground = CreateImage(front, "Foreground (auto)", back ? back : front);
            foreground.sprite = null;
            foreground.material = null;
        }

        if (foreground && (!foregroundBack || foregroundBack == foreground))
            foregroundBack = CreateImage(foreground, "Foreground Back (auto)", foreground);

        EnsureStageCoverage(foreground);
        EnsureStageCoverage(foregroundBack);

        InitLayer(backgroundLayer, front, back);
        InitLayer(foregroundLayer, foreground, foregroundBack);

        EnsureOverlayCoverage();
        if (overlay && overlay.color.a <= 0f) overlay.enabled = false;
    }

    private static void EnsureStageCoverage(Image img)
    {
        if (!img) return;
        RectTransform rt = img.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
    }

    private void EnsureOverlayCoverage()
    {
        if (!overlay) return;
        RectTransform rt = overlay.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(4000f, 4000f);
    }

    private static void InitLayer(Layer layer, Image current, Image incoming)
    {
        layer.current = current;
        layer.incoming = incoming;
        layer.currentStretch = false;
        layer.incomingStretch = false;

        if (current)
        {
            layer.sceneMaterial = current.material;
            layer.currentRest = current.rectTransform.localPosition;
            layer.currentRestScale = current.rectTransform.localScale;
            layer.baseCurrentRestScale = current.rectTransform.localScale;
            current.enabled = current.sprite;
        }

        if (incoming)
        {
            layer.incomingRest = incoming.rectTransform.localPosition;
            layer.incomingRestScale = incoming.rectTransform.localScale;
            layer.baseIncomingRestScale = incoming.rectTransform.localScale;
            SetAlpha(incoming, 0f);
            incoming.enabled = false;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (front && back == front)
            Debug.LogWarning($"{name}: Front and Back are the same Image. A crossfade needs a second " +
                             "Image above Front - one will be created at runtime.", this);
        if (foreground && foregroundBack == foreground)
            Debug.LogWarning($"{name}: Foreground and Foreground Back are the same Image. A crossfade " +
                             "needs a second Image above Foreground - one will be created at runtime.", this);
        if (overlay && (overlay == front || overlay == back || overlay == foreground || overlay == foregroundBack))
            Debug.LogWarning($"{name}: Overlay must be its own Image, above every other one.", this);
    }
#endif

    /// <summary>
    /// Makes a stage image: a copy of <paramref name="template"/>'s rect and image settings, placed
    /// as the sibling right above <paramref name="below"/> and fully transparent.
    /// </summary>
    private Image CreateImage(Image template, string goName, Image below)
    {
        RectTransform source = template.rectTransform;

        GameObject go = new GameObject(goName, typeof(RectTransform), typeof(Image));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(source.parent, false);
        rect.anchorMin = source.anchorMin;
        rect.anchorMax = source.anchorMax;
        rect.pivot = source.pivot;
        rect.anchoredPosition = source.anchoredPosition;
        rect.sizeDelta = source.sizeDelta;
        rect.localRotation = source.localRotation;
        rect.localScale = source.localScale;
        rect.SetSiblingIndex(below.rectTransform.GetSiblingIndex() + 1);

        Image image = go.GetComponent<Image>();
        image.type = template.type;
        image.preserveAspect = template.preserveAspect;
        image.material = template.material;
        image.raycastTarget = false;
        image.color = template.color;
        image.enabled = false;

        EnsureStageCoverage(image);
        h.Out($"{name}: no '{goName}' image assigned - created one above '{below.name}'.");
        return image;
    }

    // ---------------------------------------------------------------------
    // Instant state changes
    // ---------------------------------------------------------------------

    /// <summary>Covers the screen with <paramref name="color"/> at once.</summary>
    public void Cover(Color color)
    {
        Complete();
        SetOverlay(color, 1f);
    }

    /// <summary>Drops the color cover at once.</summary>
    public void Uncover()
    {
        Complete();
        SetOverlay(overlay ? overlay.color : Color.black, 0f);
    }

    /// <summary>Clears everything: no background, no foreground, no cover, scene materials back on, layers at rest.</summary>
    public void ResetState()
    {
        Complete();
        ResetOffset();
        ResetZoom();
        ZoomOwner = null;
        ApplyLooks(new VisualNovelLook(null, backgroundLayer.sceneMaterial, true),
                   new VisualNovelLook(null, foregroundLayer.sceneMaterial, true));
        SetOverlay(overlay ? overlay.color : Color.black, 0f);
    }

    // ---------------------------------------------------------------------
    // Layer offsets (for shake effects)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Moves <paramref name="layers"/> by <paramref name="offset"/> canvas units from where they sit
    /// at rest, as <paramref name="source"/>'s share (an effect passes itself): the offsets of
    /// different sources add up, so shakes running at once stack. Each layer's incoming image
    /// moves with it, so a crossfade rides along.
    /// </summary>
    public void SetOffset(VisualNovelLayers layers, Vector2 offset, object source = null)
    {
        source ??= this;
        if (layers != VisualNovelLayers.Foreground) backgroundLayer.SetOffset(source, offset);
        if (layers != VisualNovelLayers.Background) foregroundLayer.SetOffset(source, offset);
    }

    /// <summary>
    /// How far <paramref name="source"/> moves <paramref name="layers"/> from rest, or how far they
    /// sit from rest in total for null (the background's, for Both).
    /// </summary>
    public Vector2 GetOffset(VisualNovelLayers layers, object source = null)
    {
        Layer layer = layers == VisualNovelLayers.Foreground ? foregroundLayer : backgroundLayer;
        return layer.GetOffset(source);
    }

    /// <summary>
    /// Takes <paramref name="source"/>'s share off <paramref name="layers"/>, or puts them fully
    /// back at rest for null.
    /// </summary>
    public void ResetOffset(VisualNovelLayers layers = VisualNovelLayers.Both, object source = null)
    {
        if (source != null)
        {
            SetOffset(layers, Vector2.zero, source);
            return;
        }
        if (layers != VisualNovelLayers.Foreground) backgroundLayer.ClearOffsets();
        if (layers != VisualNovelLayers.Background) foregroundLayer.ClearOffsets();
    }

    // ---------------------------------------------------------------------
    // Layer zoom (for zoom effects)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Scales <paramref name="layers"/> by <paramref name="zoom"/> (1 = rest) around
    /// <paramref name="focus"/> - 0..1 across each layer's image, 0.5/0.5 = its centre - which stays
    /// where it is on screen. Each layer's incoming image scales with it, so a crossfade rides along;
    /// shake offsets still apply on top.
    /// </summary>
    public void SetZoom(VisualNovelLayers layers, float zoom, Vector2 focus)
    {
        if (layers != VisualNovelLayers.Foreground) backgroundLayer.SetZoom(zoom, focus);
        if (layers != VisualNovelLayers.Background) foregroundLayer.SetZoom(zoom, focus);
    }

    /// <summary>
    /// The zoom run animating the layers now. A zoom effect claims it when it starts and stops as
    /// soon as another run takes it, so two zooms - a release easing back and the next node's zoom
    /// in, say - never pull the layers opposite ways.
    /// </summary>
    public object ZoomOwner { get; set; }

    /// <summary>The zoom <paramref name="layers"/> sit at (the background's, for Both).</summary>
    public float GetZoom(VisualNovelLayers layers)
    {
        return layers == VisualNovelLayers.Foreground ? foregroundLayer.Zoom : backgroundLayer.Zoom;
    }

    /// <summary>Puts <paramref name="layers"/> back at their rest scale.</summary>
    public void ResetZoom(VisualNovelLayers layers = VisualNovelLayers.Both)
    {
        SetZoom(layers, 1f, new Vector2(0.5f, 0.5f));
    }

    // ---------------------------------------------------------------------
    // Transitions
    // ---------------------------------------------------------------------

    /// <summary>
    /// True when this transition would change anything on screen. A Cut or Crossfade to the
    /// picture already showing (uncovered) does nothing; neither does fading to a color the screen
    /// is already fully covered with. A running transition always counts as a change.
    /// </summary>
    public bool WouldChange(VisualNovelTransition type, VisualNovelLook backgroundLook,
                            VisualNovelLook foregroundLook, Color color)
    {
        if (transitioning) return true;

        backgroundLook = backgroundLayer.Resolve(backgroundLook);
        foregroundLook = foregroundLayer.Resolve(foregroundLook);

        switch (type)
        {
            case VisualNovelTransition.KeepPrevious:
                return false;

            case VisualNovelTransition.Cut:
            case VisualNovelTransition.Crossfade:
            case VisualNovelTransition.FadeThroughBlur:
            case VisualNovelTransition.CrossfadeBlur:
                return IsCovered || !backgroundLayer.Shows(backgroundLook) || !foregroundLayer.Shows(foregroundLook);

            case VisualNovelTransition.FadeToColor:
                if (!overlay) return false;
                Color current = overlay.color;
                bool fullyCovered = overlay.enabled && current.a >= 1f;
                return !fullyCovered || current.r != color.r || current.g != color.g || current.b != color.b;

            default:
                return true;
        }
    }

    /// <summary>
    /// Starts a transition to <paramref name="backgroundLook"/> and <paramref name="foregroundLook"/>,
    /// cutting any running one short first. Wait for it with <see cref="WaitForTransition"/>.
    /// A look's empty sprite / material keep the layer's current one - even when the transition
    /// changes the other layer, or goes through a color.
    /// </summary>
    public void Play(VisualNovelTransition type, VisualNovelLook backgroundLook, VisualNovelLook foregroundLook,
                     float duration, Color color, float holdOnColor = 0f, float blurRadius = 1f)
    {
        Complete();

        // Filled in once, from the settled state: the routines apply these at different moments
        // (and Complete may apply them mid-way), by which time the layers may already have changed.
        backgroundLook = backgroundLayer.Resolve(backgroundLook);
        foregroundLook = foregroundLayer.Resolve(foregroundLook);

        switch (type)
        {
            case VisualNovelTransition.KeepPrevious:
                return;

            case VisualNovelTransition.Cut:
                ApplyLooks(backgroundLook, foregroundLook);
                SetOverlay(color, 0f);
                return;

            case VisualNovelTransition.Crossfade:
                Begin(true, backgroundLook, foregroundLook, 0f,
                      CrossfadeRoutine(backgroundLook, foregroundLook, duration));
                return;

            case VisualNovelTransition.FadeThroughColor:
                Begin(true, backgroundLook, foregroundLook, 0f,
                      FadeThroughColorRoutine(backgroundLook, foregroundLook, duration, color, holdOnColor));
                return;

            case VisualNovelTransition.FadeToColor:
                Begin(false, default, default, 1f, FadeOverlayRoutine(color, 1f, duration));
                return;

            case VisualNovelTransition.FadeThroughBlur:
                if (!Blur)
                {
                    h.Out($"{name}: FadeThroughBlur needs a BlurVolumeManager - crossfading instead.");
                    Begin(true, backgroundLook, foregroundLook, 0f,
                          CrossfadeRoutine(backgroundLook, foregroundLook, duration));
                    return;
                }
                Begin(true, backgroundLook, foregroundLook, 0f,
                      FadeThroughBlurRoutine(backgroundLook, foregroundLook, duration, blurRadius, holdOnColor),
                      clearsBlur: true);
                return;

            case VisualNovelTransition.CrossfadeBlur:
                if (!Blur)
                {
                    h.Out($"{name}: CrossfadeBlur needs a BlurVolumeManager - crossfading instead.");
                    Begin(true, backgroundLook, foregroundLook, 0f,
                          CrossfadeRoutine(backgroundLook, foregroundLook, duration));
                    return;
                }
                Begin(true, backgroundLook, foregroundLook, 0f,
                      CrossfadeBlurRoutine(backgroundLook, foregroundLook, duration, blurRadius, holdOnColor),
                      clearsBlur: true);
                return;
        }
    }

    /// <summary>Yields until the transition running right now has finished (or been cut short).</summary>
    public IEnumerator WaitForTransition()
    {
        int id = transitionId;
        while (transitioning && transitionId == id) yield return null;
    }

    /// <summary>
    /// Yields until the running transition has put the new location in place - the swap at the
    /// peak of a blur or color, the new picture going under a cover, or the end of a plain
    /// crossfade - or until it has finished (or been cut short). Returns at once when nothing runs.
    /// </summary>
    public IEnumerator WaitForArrival()
    {
        int id = transitionId;
        while (transitioning && transitionId == id && !arrived) yield return null;
    }

    /// <summary>Snaps the running transition to its end state. No-op when nothing runs.</summary>
    public void Complete()
    {
        if (!transitioning) return;

        if (running != null) StopCoroutine(running);
        running = null;
        transitioning = false;

        StopTweens();

        if (endChangesLooks) ApplyLooks(endBackground, endForeground);
        SetOverlay(overlay ? overlay.color : Color.black, endOverlayAlpha);
        if (endClearsBlur && Blur) Blur.TweenTo(0f, Blur.Radial, 0f);
        HideIncoming(backgroundLayer);
        HideIncoming(foregroundLayer);
    }

    private void Begin(bool changesLooks, VisualNovelLook backgroundLook, VisualNovelLook foregroundLook,
                       float overlayAlpha, IEnumerator routine, bool clearsBlur = false)
    {
        endChangesLooks = changesLooks;
        endClearsBlur = clearsBlur;
        endBackground = backgroundLook;
        endForeground = foregroundLook;
        endOverlayAlpha = overlayAlpha;

        transitionId++;
        transitioning = true;
        arrived = false;

        // Can't run a coroutine on an inactive stage: land on the end state at once.
        if (!isActiveAndEnabled)
        {
            Complete();
            return;
        }

        // A transition with nothing to wait for finishes inside StartCoroutine, before the handle
        // is even assigned - which is why 'transitioning' (cleared by Run) is the source of truth.
        running = StartCoroutine(Run(routine));
    }

    private IEnumerator Run(IEnumerator routine)
    {
        yield return routine;
        running = null;
        transitioning = false;
    }

    /// <summary>
    /// The old picture dissolves into the new one, both layers at once. From a covered screen the
    /// new picture is put under the cover and the cover fades away instead - dissolving between two
    /// pictures nobody can see would look like a plain fade-in anyway.
    /// </summary>
    private IEnumerator CrossfadeRoutine(VisualNovelLook backgroundLook, VisualNovelLook foregroundLook, float duration)
    {
        if (IsCovered)
        {
            ApplyLooks(backgroundLook, foregroundLook);
            yield return Fade(overlay, 0f, duration);
            SetOverlay(overlay.color, 0f);
            yield break;
        }

        Tween backgroundTween = StartCrossfade(backgroundLayer, backgroundLook, duration);
        Tween foregroundTween = StartCrossfade(foregroundLayer, foregroundLook, duration);
        if (backgroundTween.isAlive) yield return backgroundTween.ToYieldInstruction();
        if (foregroundTween.isAlive) yield return foregroundTween.ToYieldInstruction();

        ApplyLooks(backgroundLook, foregroundLook);
        HideIncoming(backgroundLayer);
        HideIncoming(foregroundLayer);
    }

    /// <summary>
    /// Starts fading one layer towards <paramref name="look"/> and returns the tween to wait for
    /// (a dead one when there is nothing to fade - the end state is applied afterwards anyway).
    /// </summary>
    private Tween StartCrossfade(Layer layer, VisualNovelLook look, float duration)
    {
        if (!layer.current || !layer.incoming || layer.Shows(look)) return default;

        if (!look.sprite)
        {
            // Crossfade to "nothing": the current picture simply fades away.
            return FadeTween(layer.current, 0f, duration);
        }

        layer.incoming.sprite = look.sprite;
        layer.incoming.material = layer.MaterialFor(look);
        layer.incoming.enabled = true;
        layer.incomingStretch = look.stretch;
        layer.UpdateScaling(additionalStretching, transform as RectTransform);
        SetAlpha(layer.incoming, 0f);

        if (layer.fadeOutOld && layer.current.enabled) FadeTween(layer.current, 0f, duration);
        return FadeTween(layer.incoming, 1f, duration);
    }

    /// <summary>Old picture -> color -> new picture. An already covered screen only fades in.</summary>
    private IEnumerator FadeThroughColorRoutine(VisualNovelLook backgroundLook, VisualNovelLook foregroundLook,
                                                float duration, Color color, float hold)
    {
        float half = duration * 0.5f;

        // Scale the fade-out by how much of it is left, so a half-covered screen doesn't take the
        // full half-duration to finish covering.
        float alpha = overlay && overlay.enabled ? overlay.color.a : 0f;
        ShowOverlay(color, alpha);
        yield return Fade(overlay, 1f, half * (1f - alpha));

        ApplyLooks(backgroundLook, foregroundLook);

        if (hold > 0f) yield return new WaitForSeconds(hold);

        yield return Fade(overlay, 0f, half);
        SetOverlay(color, 0f);
    }

    /// <summary>
    /// Old picture -> full blur -> new picture -> blur cleared to 0. At the peak the pictures swap
    /// at once, or crossfade over <paramref name="hold"/> seconds while the blur stays maxed. From a
    /// covered screen the new picture goes under the cover already blurred, and the cover fades
    /// away while the blur clears. Only the radius moves; the radial blur is left as it is.
    /// </summary>
    private IEnumerator FadeThroughBlurRoutine(VisualNovelLook backgroundLook, VisualNovelLook foregroundLook,
                                               float duration, float radius, float hold)
    {
        BlurVolumeManager b = Blur;
        float radial = b.Radial;
        float half = duration * 0.5f;

        if (IsCovered)
        {
            b.TweenTo(radius, radial, 0f);
            ApplyLooks(backgroundLook, foregroundLook);
            if (hold > 0f) yield return new WaitForSeconds(hold);

            Tween clear = b.TweenTo(0f, radial, half, fadeEase);
            yield return Fade(overlay, 0f, half);
            if (clear.isAlive) yield return clear.ToYieldInstruction();
            SetOverlay(overlay.color, 0f);
            yield break;
        }

        // Scale the blur-up by how much of it is left, so an already blurred screen doesn't take
        // the full half-duration to get there.
        float left = radius > 0f ? Mathf.Clamp01(1f - b.Radius / radius) : 0f;
        Tween up = b.TweenTo(radius, radial, half * left, fadeEase);
        if (up.isAlive) yield return up.ToYieldInstruction();

        if (hold > 0f)
        {
            Tween backgroundTween = StartCrossfade(backgroundLayer, backgroundLook, hold);
            Tween foregroundTween = StartCrossfade(foregroundLayer, foregroundLook, hold);
            if (backgroundTween.isAlive) yield return backgroundTween.ToYieldInstruction();
            if (foregroundTween.isAlive) yield return foregroundTween.ToYieldInstruction();
        }

        ApplyLooks(backgroundLook, foregroundLook);
        HideIncoming(backgroundLayer);
        HideIncoming(foregroundLayer);

        Tween down = b.TweenTo(0f, radial, half, fadeEase);
        if (down.isAlive) yield return down.ToYieldInstruction();
    }

    /// <summary>
    /// Crossfade and blur together: the old picture dissolves into the new one over the whole
    /// transition while the blur rises to <paramref name="radius"/> by the halfway point, stays
    /// there for <paramref name="hold"/> seconds, and clears to 0 by the end. From a covered screen
    /// the new picture goes under the cover already blurred, and the cover fades away while the
    /// blur clears. Only the radius moves; the radial blur is left as it is.
    /// </summary>
    private IEnumerator CrossfadeBlurRoutine(VisualNovelLook backgroundLook, VisualNovelLook foregroundLook,
                                             float duration, float radius, float hold)
    {
        BlurVolumeManager b = Blur;
        float radial = b.Radial;
        float half = duration * 0.5f;

        if (IsCovered)
        {
            b.TweenTo(radius, radial, 0f);
            ApplyLooks(backgroundLook, foregroundLook);
            if (hold > 0f) yield return new WaitForSeconds(hold);

            Tween clear = b.TweenTo(0f, radial, duration, fadeEase);
            yield return Fade(overlay, 0f, duration);
            if (clear.isAlive) yield return clear.ToYieldInstruction();
            SetOverlay(overlay.color, 0f);
            yield break;
        }

        float start = Time.time;
        Tween backgroundTween = StartCrossfade(backgroundLayer, backgroundLook, duration + hold);
        Tween foregroundTween = StartCrossfade(foregroundLayer, foregroundLook, duration + hold);

        // Scale the blur-up by how much of it is left, so an already blurred screen doesn't take
        // the full half-duration to get there - then sit at the peak until the crossfade's midpoint
        // (plus the hold), so the blur is at its strongest while both pictures are mixed.
        float left = radius > 0f ? Mathf.Clamp01(1f - b.Radius / radius) : 0f;
        Tween up = b.TweenTo(radius, radial, half * left, fadeEase);
        if (up.isAlive) yield return up.ToYieldInstruction();

        float untilDown = half + hold - (Time.time - start);
        if (untilDown > 0f) yield return new WaitForSeconds(untilDown);

        Tween down = b.TweenTo(0f, radial, half, fadeEase);
        if (backgroundTween.isAlive) yield return backgroundTween.ToYieldInstruction();
        if (foregroundTween.isAlive) yield return foregroundTween.ToYieldInstruction();

        ApplyLooks(backgroundLook, foregroundLook);
        HideIncoming(backgroundLayer);
        HideIncoming(foregroundLayer);

        if (down.isAlive) yield return down.ToYieldInstruction();
    }

    private IEnumerator FadeOverlayRoutine(Color color, float to, float duration)
    {
        float alpha = overlay && overlay.enabled ? overlay.color.a : 0f;
        ShowOverlay(color, alpha);
        yield return Fade(overlay, to, duration);
        SetOverlay(color, to);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private IEnumerator Fade(Image image, float to, float duration)
    {
        Tween tween = FadeTween(image, to, duration);
        if (tween.isAlive) yield return tween.ToYieldInstruction();
    }

    /// <summary>Starts an alpha fade; a zero duration sets the alpha at once and returns a dead tween.</summary>
    private Tween FadeTween(Image image, float to, float duration)
    {
        if (!image) return default;

        if (duration <= 0f)
        {
            SetAlpha(image, to);
            return default;
        }

        return Tween.Alpha(image, to, duration, fadeEase);
    }

    private void ApplyLooks(VisualNovelLook backgroundLook, VisualNovelLook foregroundLook)
    {
        ApplyLook(backgroundLayer, backgroundLook);
        ApplyLook(foregroundLayer, foregroundLook);
        UpdateAllScaling();
        // Every transition routine applies its end looks exactly once, at the moment the new
        // location is in place: that's the arrival WaitForArrival waits for.
        arrived = true;
    }

    private static void ApplyLook(Layer layer, VisualNovelLook look)
    {
        Image image = layer.current;
        if (!image) return;
        Tween.StopAll(image);
        image.sprite = look.sprite;
        image.material = layer.MaterialFor(look);
        image.enabled = look.sprite;
        layer.currentStretch = look.stretch;
        SetAlpha(image, 1f);
    }

    private void SetOverlay(Color color, float alpha)
    {
        if (!overlay) return;
        color.a = alpha;
        overlay.color = color;
        overlay.enabled = alpha > 0f;
    }

    /// <summary>Sets the overlay's color and alpha and keeps it enabled, ready to be faded.</summary>
    private void ShowOverlay(Color color, float alpha)
    {
        if (!overlay) return;
        color.a = alpha;
        overlay.color = color;
        overlay.enabled = true;
    }

    private static void HideIncoming(Layer layer)
    {
        Image image = layer.incoming;
        if (!image) return;
        Tween.StopAll(image);
        SetAlpha(image, 0f);
        image.enabled = false;
        layer.incomingStretch = false;
        layer.incomingRestScale = layer.baseIncomingRestScale;
    }

    private void StopTweens()
    {
        if (front) Tween.StopAll(front);
        if (back) Tween.StopAll(back);
        if (foreground) Tween.StopAll(foreground);
        if (foregroundBack) Tween.StopAll(foregroundBack);
        if (overlay) Tween.StopAll(overlay);
    }

    private static void SetAlpha(Graphic graphic, float alpha)
    {
        if (!graphic) return;
        Color c = graphic.color;
        c.a = alpha;
        graphic.color = c;
    }
}
