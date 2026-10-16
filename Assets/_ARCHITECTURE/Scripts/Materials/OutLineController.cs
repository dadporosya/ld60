using UnityEngine;

/// <summary>
/// Draws a sprite outline by mirroring this object's SpriteRenderer onto a slightly larger child
/// renderer that uses the outline material.
///
/// PERFORMANCE NOTES (see PerfLogs/scene_census_*.txt):
///   * The outline child is now created LAZILY, the first time the outline is actually switched
///     on. The scene census counted 217 OutlineControllers, i.e. 217 extra GameObjects, 217 extra
///     SpriteRenderers and 217 cloned materials — over half of the 401 active renderers and most
///     of the 285 unique materials that set the SetPass floor — for an effect that is off for
///     most of them.
///   * Each outline still gets its OWN material instance. Interning them by configuration was
///     tried and reverted: sharing is only safe while nothing writes per-object values, and
///     SetColor/SetThickness do exactly that.
///   * Turning the outline off now disables the renderer instead of only zeroing a shader flag.
///     A shader flag still costs a draw call, a SetPass and full-quad overdraw; a disabled
///     renderer costs nothing.
///   * The change tracker runs from the shared <see cref="UpdateTicker"/> rather than from 217
///     separate Update() callbacks, and only while an outline is actually visible.
/// </summary>
public class OutlineController : MonoBehaviour, ITickable
{
    [SerializeField] private bool fill = true;
    [SerializeField] private float outlineThickness = 100f; // must match _Thickness in material
    [SerializeField] private Color outlineColor = Color.white;

    [Tooltip("Whether the outline starts visible. Callers can override this at runtime with SetEnabled(). " +
             "While it is off, no outline GameObject, renderer or material is created at all.")]
    [SerializeField] private bool startEnabled = true;

    private SpriteRenderer sourceRenderer;
    private SpriteRenderer instanceRenderer;
    private TrackedSpriteRenderer tracker;

    // Resources.Load is a dictionary lookup plus a load path resolve every single call. 217
    // controllers all asking for the same material at Awake made that a measurable load-time cost,
    // so it is resolved once.
    public const string OutlineMaterialPath = "ARCHITECTURE/Materials/DefaultOutlineMaterial";

    private static Material _outlineSource;
    private static Material OutlineSource
    {
        get
        {
            if (_outlineSource == null) _outlineSource = Resources.Load<Material>(OutlineMaterialPath);
            return _outlineSource;
        }
    }

    private static readonly int ThicknessId     = Shader.PropertyToID("_Thickness");
    private static readonly int SolidOutlineId  = Shader.PropertyToID("_SolidOutline");
    private static readonly int OutlineEnabledId = Shader.PropertyToID("_OutlineEnabled");

    // The state callers asked for. Kept separate from `instanceRenderer` because SetEnabled is
    // routinely called from another component's Awake, and Awake order between two components is
    // undefined: the old code returned early when the outline child did not exist yet, so a
    // SetEnabled(false) issued before this Awake was silently dropped and the outline stayed on.
    private bool wantEnabled;
    private bool awoken;
    private bool ticking;

    private void Awake()
    {
        // SetEnabled may already have run and set wantEnabled; only fall back to the inspector
        // value when nobody has expressed a preference yet.
        if (!awoken)
        {
            wantEnabled = startEnabled;
            awoken = true;
        }

        // The original Awake clamped this via SetThickness before scaling the outline; keep that.
        outlineThickness = Mathf.Clamp(outlineThickness, 0f, 100f);

        sourceRenderer = GetComponent<SpriteRenderer>();
        ApplyEnabledState();
    }

    private void OnEnable()
    {
        if (awoken) ApplyEnabledState();
    }

    private void OnDisable() => StopTicking();

    private void OnDestroy() => StopTicking();

    // Builds the outline child. Only ever called when the outline is genuinely needed.
    private bool EnsureOutline()
    {
        if (instanceRenderer) return true;

        if (!sourceRenderer) sourceRenderer = GetComponent<SpriteRenderer>();
        if (!sourceRenderer) return false;

        var outlineObj = new GameObject("Outline");
        outlineObj.transform.SetParent(transform, false);
        instanceRenderer = outlineObj.AddComponent<SpriteRenderer>();
        instanceRenderer.material = new Material(OutlineSource);
        instanceRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
        instanceRenderer.sortingOrder   = sourceRenderer.sortingOrder - 1;

        ApplyMaterial();
        ApplyFillMode();
        CopyRenderer();
        ApplyOutlineScale();

        tracker = new TrackedSpriteRenderer(sourceRenderer, OnSourceChanged);
        return true;
    }

    // Writes the current thickness/colour into this outline's own material instance.
    private void ApplyMaterial()
    {
        if (!instanceRenderer) return;

        Material m = instanceRenderer.material;
        if (m == null) return;

        m.SetFloat(ThicknessId, Mathf.Clamp(outlineThickness, 0f, 100f));
        m.SetColor(SolidOutlineId, outlineColor);
        m.SetFloat(OutlineEnabledId, 1f);
    }

    private void ApplyEnabledState()
    {
        if (wantEnabled)
        {
            if (!EnsureOutline()) return;
            instanceRenderer.enabled = true;
            StartTicking();
        }
        else
        {
            if (instanceRenderer) instanceRenderer.enabled = false;
            StopTicking();
        }
    }

    private void StartTicking()
    {
        if (ticking || !isActiveAndEnabled) return;
        UpdateTicker.Register(this);
        ticking = true;
    }

    private void StopTicking()
    {
        if (!ticking) return;
        UpdateTicker.Unregister(this);
        ticking = false;
    }

    /// <summary>
    /// Driven by <see cref="UpdateTicker"/>. Only mirrors the source renderer; the debug
    /// Input.GetKey(G/F) thickness nudge that used to live here has been removed — it polled the
    /// keyboard twice per outline per frame (434 polls a frame at 217 outlines) and would have
    /// shipped in the build.
    /// </summary>
    public void Tick() => tracker?.Tick();

    private void OnSourceChanged()
    {
        CopyRenderer();
        ApplyOutlineScale();
    }

    private void CopyRenderer()
    {
        if (!instanceRenderer || !sourceRenderer) return;
        instanceRenderer.sprite          = sourceRenderer.sprite;
        instanceRenderer.flipX           = sourceRenderer.flipX;
        instanceRenderer.flipY           = sourceRenderer.flipY;
        instanceRenderer.drawMode        = sourceRenderer.drawMode;
        instanceRenderer.size            = sourceRenderer.size;
        instanceRenderer.maskInteraction = sourceRenderer.maskInteraction;
        instanceRenderer.sortingLayerID  = sourceRenderer.sortingLayerID;
        instanceRenderer.sortingOrder    = sourceRenderer.sortingOrder - 1;
    }

    private void ApplyOutlineScale()
    {
        if (!instanceRenderer || !sourceRenderer) return;
        Sprite spr = sourceRenderer.sprite;
        if (spr == null) return;

        // Texture size in pixels
        float texW = spr.texture.width;
        float texH = spr.texture.height;

        // Match shader formula: sprite is shrunk by (thickness*2) px on each axis
        // so we expand the outline object by the inverse ratio
        float scaleX = (texW + outlineThickness * 2f) / texW;
        float scaleY = (texH + outlineThickness * 2f) / texH;

        instanceRenderer.transform.localScale = new Vector3(scaleX, scaleY, 1f);
    }

    private void ApplyFillMode()
    {
        if (!instanceRenderer) return;
        // Vertex colour, not a material property, so this stays per-object and does not fork the
        // interned material.
        var c = instanceRenderer.color;
        c.a = fill ? 0f : 1f / 255f;
        instanceRenderer.color = c;
    }

    public void SetColor(Color color)
    {
        outlineColor = color;
        ApplyMaterial();
        ApplyFillMode();
    }

    public void SetEnabled(bool value)
    {
        wantEnabled = value;
        awoken = true;           // a preference has been expressed; Awake must not overwrite it
        enabled = value;         // preserved from the original API
        ApplyEnabledState();
    }

    public void SetThickness(float value)
    {
        outlineThickness = Mathf.Clamp(value, 0f, 100f);
        ApplyMaterial();
        ApplyOutlineScale();
    }

    public void ChangeThickness(float value)
    {
        SetThickness(outlineThickness + value);
    }
}

// Tracks a SpriteRenderer and invokes a callback only when something changes
public class TrackedSpriteRenderer
{
    private readonly SpriteRenderer  _r;
    private readonly System.Action   _onChange;

    private Sprite      _sprite;
    private bool        _flipX, _flipY;
    private Vector2     _size;
    private int         _sortingLayer, _sortingOrder;
    private SpriteMaskInteraction _mask;
    private SpriteDrawMode        _drawMode;

    public TrackedSpriteRenderer(SpriteRenderer r, System.Action onChange)
    {
        _r        = r;
        _onChange = onChange;
        Snapshot();
    }

    public void Tick()
    {
        if (_r == null) return;

        if (_r.sprite          != _sprite       ||
            _r.flipX           != _flipX        ||
            _r.flipY           != _flipY        ||
            _r.size            != _size         ||
            _r.drawMode        != _drawMode     ||
            _r.maskInteraction != _mask         ||
            _r.sortingLayerID  != _sortingLayer ||
            _r.sortingOrder    != _sortingOrder)
        {
            Snapshot();
            _onChange?.Invoke();
        }
    }

    private void Snapshot()
    {
        _sprite       = _r.sprite;
        _flipX        = _r.flipX;
        _flipY        = _r.flipY;
        _size         = _r.size;
        _drawMode     = _r.drawMode;
        _mask         = _r.maskInteraction;
        _sortingLayer = _r.sortingLayerID;
        _sortingOrder = _r.sortingOrder;
    }
}
