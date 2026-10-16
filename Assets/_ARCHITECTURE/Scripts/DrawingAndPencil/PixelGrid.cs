using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Which cells of a <see cref="PixelGrid"/> can be drawn on.</summary>
public enum GridMode
{
    Full,        // every cell
    SpriteShape  // only cells over the opaque part of a sprite (± padding)
}

/// <summary>
/// The drawable area, as a UI element: splits this RectTransform into a grid of <see cref="PixelCell"/>s
/// and shows them through a point-filtered texture on a stretched RawImage overlay, so every cell renders
/// as one hard pixel. By default the overlay multiplies onto the board (shader
/// <c>_PROJECT/shaders/Resources/PixelGridMultiply</c>), so the sheet's line art shows through the paint.
///
/// <para>The Image on this object is the area's background (the "sheet") and is what receives the
/// pencil's press — UI drawn on top of the board blocks strokes by itself. The overlay is inserted as the
/// first child, so any children you add (e.g. the pencil's cursor marker) draw above the pixels.</para>
///
/// <para>Two modes (<see cref="GridMode"/>): <see cref="GridMode.Full"/> makes every cell drawable;
/// <see cref="GridMode.SpriteShape"/> keeps only the cells over the opaque part of a sprite, padded out
/// or in by <c>maskPadding</c> cells. Masked cells don't exist for gameplay: <see cref="GetCell"/> returns
/// null for them, painting skips them and they are left out of <see cref="CellCount"/> and the zone
/// counts. Coordinates stay purely geometric, so the pencil still moves across masked gaps.</para>
///
/// <para>The board also sounds: <see cref="drawSfx"/> loops a clip while paint lands on it, and optionally another
/// while the paint lands on overdone cells or overdoses them (see <see cref="BoardDrawSfx"/>).</para>
/// </summary>
[RequireComponent(typeof(Image))]
public class PixelGrid : MonoBehaviour, IPointerDownHandler
{
    [Tooltip("Number of cells across the board.")]
    [SerializeField, Min(1)] private int width = 64;

    [Tooltip("Derive the row count from the board's aspect ratio so cells stay square.")]
    [SerializeField] private bool autoHeight = true;
    [SerializeField, Min(1)] private int height = 64;

    [Header("Shape")]
    [Tooltip("Full: every cell is drawable. SpriteShape: only cells over the opaque part of the mask sprite.")]
    [SerializeField] private GridMode mode = GridMode.Full;

    [Tooltip("Sprite whose opaque part becomes the drawable area, stretched over the whole board. " +
             "Empty = use this board's own Image sprite, placed the way the Image draws it " +
             "(Preserve Aspect included). Doesn't need Read/Write enabled.")]
    [SerializeField] private Sprite maskSprite;

    [Tooltip("How much of a cell's area must be opaque (alpha-weighted) for it to count as inside the sprite.")]
    [SerializeField, Range(0.01f, 1f)] private float maskCoverage = 0.5f;

    [Tooltip("In cells. Positive grows the drawable area past the sprite's edge, negative shrinks it inwards.")]
    [SerializeField] private float maskPadding;

    [Tooltip("What masked cells show — transparent by default. Tint it to preview the mask.")]
    [SerializeField] private Color maskedColor = new Color(0f, 0f, 0f, 0f);

    [Header("Look")]
    [Tooltip("On: the pixels multiply onto the board like a Multiply layer, so its line art shows through " +
             "the paint. Off: plain alpha blending — painted pixels cover the board.")]
    [SerializeField] private bool multiplyBlend = true;

    public SaturationSettings settings = new SaturationSettings();

    [Header("SFX")]
    [Tooltip("The sound of the pencil on this board: a loop while it's painted, and optionally another one " +
             "while it's being overdosed.")]
    public BoardDrawSfx drawSfx = new BoardDrawSfx();

    /// <summary>(cell, from, to) — fired whenever a cell crosses into a different zone.</summary>
    public event Action<Vector2Int, SaturationZone, SaturationZone> OnCellZoneChanged;

    /// <summary>Left button went down on the board itself (not on UI covering it).</summary>
    public event Action OnPressed;

    /// <summary>The pencil painted this board (<see cref="Dwell"/> or <see cref="Pass"/>) — fires every frame of a stroke.</summary>
    public event Action OnPainted;

    /// <summary>
    /// (cell, sameStroke) — the pencil went over paint already on this cell: laid by an earlier stroke
    /// (sameStroke false), or by the current stroke before it moved away (sameStroke true, see
    /// <see cref="SaturationSettings.selfOverlapGap"/>). Fires once per cell per crossing, before that
    /// frame's paint is added — a listener may <see cref="Overdose"/> the cell instead.
    /// </summary>
    public event Action<Vector2Int, bool> OnOverlap;

    /// <summary>
    /// A stroke ended (see <see cref="EndStroke"/>), with the cells it painted first — ones that were empty
    /// before it touched them. Read their zones to judge the line it left.
    /// </summary>
    public event Action<IReadOnlyList<Vector2Int>> OnStrokeEnded;

    /// <summary>A stroke began on this board (see <see cref="BeginStroke"/>).</summary>
    public event Action OnStrokeBegan;

    public int Width => width;
    public int Height => height;
    public GridMode Mode => mode;
    public RectTransform Rect => _rect;

    /// <summary>Size of one cell in the board's local (UI) units.</summary>
    public Vector2 CellSize => new Vector2(_rect.rect.width / width, _rect.rect.height / height);

    private RectTransform _rect;
    private Canvas _canvas;
    private Image _image;
    /// <summary>Row-major, bottom row first; null where the cell is masked out.</summary>
    private PixelCell[] _cells;
    private int _activeCount;
    private Color32[] _pixels;
    private Texture2D _texture;
    private RawImage _overlay;
    private Material _overlayMaterial;
    private readonly int[] _zoneCounts = new int[Enum.GetValues(typeof(SaturationZone)).Length];
    private int _strokeId;
    /// <summary>The current stroke's brush centres, in order, each distinct from the one before.</summary>
    private readonly List<Vector2Int> _strokePath = new List<Vector2Int>();
    /// <summary>Cells the current stroke painted first (empty until it touched them).</summary>
    private readonly List<Vector2Int> _strokeNewCells = new List<Vector2Int>();
    private bool _strokeOpen;
    private int _brushRadius;
    /// <summary>Frame paint last landed on a cell of this board (see <see cref="Saturate"/>).</summary>
    private int _paintFrame = -1;
    /// <summary>Unscaled time the pencil last painted an overdone cell, or overdosed one.</summary>
    private float _overdosePaintTime = float.NegativeInfinity;
    private bool _dirty;
    private (GridMode, Sprite, float, float) _builtMask;

    private void Awake()
    {
        _rect = (RectTransform)transform;
        _image = GetComponent<Image>();
        _canvas = GetComponentInParent<Canvas>();
        if (!_canvas)
        {
            h.Out($"PixelGrid on '{name}' is not under a Canvas — disabled.");
            enabled = false;
        }
    }

    // Start, not Awake: by then the Canvas has laid out, so the board's rect (and aspect) is final.
    private void Start() => Build();

    private void Build()
    {
        height = ResolveHeight();

        _cells = new PixelCell[width * height];
        _pixels = new Color32[_cells.Length];

        _texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "PixelGrid"
        };

        var go = new GameObject("PixelGrid Overlay", typeof(RectTransform), typeof(RawImage));
        go.layer = gameObject.layer;

        var rt = (RectTransform)go.transform;
        rt.SetParent(_rect, false);
        rt.SetAsFirstSibling();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        _overlay = go.GetComponent<RawImage>();
        _overlay.texture = _texture;
        _overlay.raycastTarget = false;
        ApplyBlend();

        ApplyMask();
        LateUpdate();
    }

    private void LateUpdate()
    {
        // LateUpdate: every pencil has had its Update, so whether this frame painted the board is known.
        // The cell under the pencil is the stroke's last brush centre.
        PixelCell under = _strokePath.Count > 0 ? GetCell(_strokePath[_strokePath.Count - 1]) : null;
        drawSfx.Tick(gameObject, _paintFrame == Time.frameCount,
            Time.unscaledTime - _overdosePaintTime <= drawSfx.overdoseHold, under != null ? under.saturation : -1f);

        if (!_dirty) return;

        _texture.SetPixels32(_pixels);
        _texture.Apply(false);
        _dirty = false;
    }

    private void OnDisable() => drawSfx.Silence();

    private void OnDestroy()
    {
        if (_texture) Destroy(_texture);
        if (_overlayMaterial) Destroy(_overlayMaterial);
    }

    // Lets the mask be tuned live in Play mode. Only a change to the mask settings rebuilds it, since a
    // rebuild wipes the drawing.
    private void OnValidate()
    {
        if (!Application.isPlaying || _cells == null) return;

        ApplyBlend();
        if (_builtMask != MaskKey) RebuildMask();
    }

    /// <summary>Points the overlay at the multiply material, or back at the default UI material.</summary>
    private void ApplyBlend()
    {
        if (multiplyBlend && !_overlayMaterial)
        {
            Shader shader = Resources.Load<Shader>("PixelGridMultiply");
            if (shader) _overlayMaterial = new Material(shader) { name = "PixelGrid Multiply" };
            else h.Out($"PixelGrid '{name}': shader Resources/PixelGridMultiply not found — using normal blending.");
        }

        _overlay.material = multiplyBlend ? _overlayMaterial : null;
    }

    // The grid is only built in Play mode; these let the mask be computed in Edit mode too (see PixelGridEditor).
    private RectTransform RectT => _rect ? _rect : (RectTransform)transform;
    private Image Img => _image ? _image : GetComponent<Image>();

    /// <summary>Row count the grid has (or will have once built): derived from the aspect when autoHeight.</summary>
    public int ResolveHeight()
    {
        Rect r = RectT.rect;
        return autoHeight && r.width > 0f && r.height > 0f
            ? Mathf.Max(1, Mathf.RoundToInt(width * r.height / r.width))
            : height;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) OnPressed?.Invoke();
    }

    // ---------------------------------------------------------------- coordinates

    private Camera EventCamera
    {
        get
        {
            Canvas root = _canvas.rootCanvas;
            return root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        }
    }

    /// <summary>
    /// <paramref name="screenPos"/> in the board's local space (not clamped to the board). "Local" below
    /// always means this space.
    /// </summary>
    public bool ScreenToLocal(Vector2 screenPos, out Vector2 local)
    {
        local = default;
        return _cells != null
               && RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, screenPos, EventCamera, out local);
    }

    /// <summary>Cell containing <paramref name="local"/>; false when it is outside the board.</summary>
    public bool LocalToCell(Vector2 local, out Vector2Int cell)
    {
        cell = default;
        if (_cells == null) return false;

        Rect r = _rect.rect;
        float u = (local.x - r.xMin) / r.width;
        float v = (local.y - r.yMin) / r.height;

        cell = new Vector2Int(Mathf.FloorToInt(u * width), Mathf.FloorToInt(v * height));
        return InBounds(cell);
    }

    /// <summary>Cell under <paramref name="screenPos"/>; false when it is outside the board.</summary>
    public bool ScreenToCell(Vector2 screenPos, out Vector2Int cell)
    {
        cell = default;
        return ScreenToLocal(screenPos, out Vector2 local) && LocalToCell(local, out cell);
    }

    public Vector2 CellToLocal(Vector2Int cell)
    {
        Rect r = _rect.rect;
        Vector2 size = CellSize;
        return new Vector2(r.xMin + (cell.x + 0.5f) * size.x, r.yMin + (cell.y + 0.5f) * size.y);
    }

    public Vector3 LocalToWorld(Vector2 local) => _rect.TransformPoint(local);

    /// <summary>Screen position of <paramref name="local"/> — the inverse of <see cref="ScreenToLocal"/>.</summary>
    public Vector2 LocalToScreen(Vector2 local) =>
        RectTransformUtility.WorldToScreenPoint(EventCamera, LocalToWorld(local));

    /// <summary>World position of the cell's centre (assign to a UI element's <c>position</c>).</summary>
    public Vector3 CellToWorld(Vector2Int cell) => LocalToWorld(CellToLocal(cell));

    /// <summary>What the cell currently shows (transparent when untouched).</summary>
    public Color CellColor(Vector2Int cell)
    {
        PixelCell c = GetCell(cell);
        return c != null ? settings.ColorOf(c.saturation) : settings.emptyColor;
    }

    public bool InBounds(Vector2Int cell) => cell.x >= 0 && cell.y >= 0 && cell.x < width && cell.y < height;

    /// <summary>The cell's state; null when it is outside the board or masked out.</summary>
    public PixelCell GetCell(Vector2Int cell) =>
        _cells != null && InBounds(cell) ? _cells[cell.y * width + cell.x] : null;

    /// <summary>Whether the cell can be painted (inside the board and not masked out).</summary>
    public bool IsDrawable(Vector2Int cell) => GetCell(cell) != null;

    public int CountInZone(SaturationZone zone) => _zoneCounts[(int)zone];

    /// <summary>Number of drawable cells (masked-out cells excluded).</summary>
    public int CellCount => _cells != null ? _activeCount : 0;

    // ---------------------------------------------------------------- painting

    /// <summary>Call on pencil press. Painted cells the new stroke goes over saturate faster.</summary>
    public void BeginStroke()
    {
        // A stroke nobody ended (its pencil got disabled) ends here.
        EndStroke();

        _strokeId++;
        _strokePath.Clear();
        _strokeNewCells.Clear();
        _strokeOpen = true;
        OnStrokeBegan?.Invoke();
    }

    /// <summary>Call on pencil release (or whenever the stroke is cut off). Fires <see cref="OnStrokeEnded"/>; no-op without a stroke.</summary>
    public void EndStroke()
    {
        if (!_strokeOpen) return;

        _strokeOpen = false;
        OnStrokeEnded?.Invoke(_strokeNewCells);
    }

    /// <summary>The pencil rests on <paramref name="center"/> for <paramref name="seconds"/>.</summary>
    public void Dwell(Vector2Int center, int brushRadius, float seconds)
    {
        StepTo(center, brushRadius);
        ForEachInBrush(center, brushRadius, c => Saturate(c, settings.baseRate * seconds));
        OnPainted?.Invoke();
    }

    /// <summary>The pencil only crossed <paramref name="center"/> on its way somewhere else.</summary>
    public void Pass(Vector2Int center, int brushRadius)
    {
        StepTo(center, brushRadius);
        ForEachInBrush(center, brushRadius, c => Saturate(c, settings.passSaturation));
        OnPainted?.Invoke();
    }

    private void StepTo(Vector2Int center, int brushRadius)
    {
        _brushRadius = brushRadius;
        if (_strokePath.Count == 0 || _strokePath[_strokePath.Count - 1] != center) _strokePath.Add(center);
    }

    /// <summary>Adds <paramref name="amount"/> (before the rework multiplier) to one cell.</summary>
    public void Saturate(Vector2Int c, float amount)
    {
        PixelCell cell = GetCell(c);
        if (cell == null || amount <= 0f) return;

        // Paint landed on a drawable cell: the pencil is heard (not while it crosses a masked gap).
        _paintFrame = Time.frameCount;
        if (_strokeOpen && cell.saturation <= 0f) _strokeNewCells.Add(c);

        // A new layer of paint: the first touch, a new stroke over it, or the pencil crossing back over
        // this already painted cell after having left it for a while.
        bool newStroke = cell.lastStrokeId != _strokeId;
        bool crossedAgain = Time.time - cell.lastTouchTime >= settings.reworkDelay;
        if (cell.layers == 0 || newStroke || crossedAgain) cell.layers++;

        // Checked before the saturation cap: going over a maxed-out line still counts.
        bool overlap = cell.saturation > 0f && (newStroke || LeftSince(c, cell.lastTouchStep));

        cell.lastStrokeId = _strokeId;
        cell.lastTouchTime = Time.time;
        cell.lastTouchStep = Mathf.Max(0, _strokePath.Count - 1);

        if (overlap)
        {
            float before = cell.saturation;
            OnOverlap?.Invoke(c, !newStroke);
            // A listener punished the crossing by setting the cell's paint itself (see Overdose): show exactly
            // that shade this frame. Lingering on it from the next frame on darkens it as usual.
            if (cell.saturation != before)
            {
                _overdosePaintTime = Time.unscaledTime;
                return;
            }
        }

        SetSaturation(c, cell, cell.saturation + amount * settings.ReworkFactor(cell.layers));
        if (cell.zone == SaturationZone.TooMuch) _overdosePaintTime = Time.unscaledTime;
    }

    /// <summary>
    /// Darkens a crossed cell, firing the zone change like any other paint, per
    /// <see cref="SaturationSettings.crossColorMode"/>. <see cref="CrossColorMode.Additive"/>: adds
    /// <see cref="SaturationSettings.crossAddSaturation"/> to what the cell has. <see cref="CrossColorMode.Fixed"/>:
    /// raises it to a random shade just past the <see cref="SaturationZone.TooMuch"/> threshold (see
    /// <see cref="SaturationSettings.overdoseSpread"/>) — or, with <see cref="SaturationSettings.crossUnderChance"/>,
    /// to a shade just below it, which leaves the cell short of overdone; cells already darker than the shade
    /// picked keep their colour. Painting keeps darkening it afterwards, all the way to the darkest colour.
    /// </summary>
    public void Overdose(Vector2Int c)
    {
        PixelCell cell = GetCell(c);
        if (cell == null) return;

        if (settings.crossColorMode == CrossColorMode.Additive)
        {
            SetSaturation(c, cell, cell.saturation + settings.crossAddSaturation);
            return;
        }

        float min = settings.tooMuchMin;
        if (UnityEngine.Random.value < settings.crossUnderChance)
        {
            float lo = Mathf.Min(settings.crossUnderMin, settings.crossUnderMax);
            float hi = Mathf.Max(settings.crossUnderMin, settings.crossUnderMax);
            // Never all the way to 0: that would read as untouched.
            SetSaturation(c, cell, Mathf.Max(0.01f, min - UnityEngine.Random.Range(lo, hi)));
            return;
        }

        SetSaturation(c, cell, UnityEngine.Random.Range(min, Mathf.Min(1f, min + settings.overdoseSpread)));
    }

    private void SetSaturation(Vector2Int c, PixelCell cell, float saturation)
    {
        saturation = Mathf.Min(1f, saturation);
        if (saturation <= cell.saturation) return; // saturation only ever grows

        cell.saturation = saturation;
        _pixels[c.y * width + c.x] = settings.ColorOf(cell.saturation);
        _dirty = true;

        SaturationZone zone = settings.ZoneOf(cell.saturation);
        if (zone == cell.zone) return;

        SaturationZone from = cell.zone;
        cell.zone = zone;
        _zoneCounts[(int)from]--;
        _zoneCounts[(int)zone]++;
        OnCellZoneChanged?.Invoke(c, from, zone);
    }

    /// <summary>
    /// Whether the current stroke's brush has been out of reach of <paramref name="c"/> since path step
    /// <paramref name="since"/> — i.e. coming back to it now is crossing the stroke's own line, not
    /// just lingering on it.
    /// </summary>
    private bool LeftSince(Vector2Int c, int since)
    {
        int reach = _brushRadius + settings.selfOverlapGap;
        for (int i = since + 1; i < _strokePath.Count; i++)
        {
            Vector2Int d = _strokePath[i] - c;
            if (Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y)) > reach) return true;
        }
        return false;
    }

    /// <summary>Wipes every cell back to empty (does not fire zone events).</summary>
    public void Clear()
    {
        if (_cells == null) return;

        Color32 empty = settings.emptyColor;
        Color32 masked = maskedColor;
        for (int i = 0; i < _cells.Length; i++)
        {
            _cells[i]?.Reset();
            _pixels[i] = _cells[i] != null ? empty : masked;
        }

        _strokeNewCells.Clear();
        Array.Clear(_zoneCounts, 0, _zoneCounts.Length);
        _zoneCounts[(int)SaturationZone.Empty] = _activeCount;
        _dirty = true;
    }

    // ---------------------------------------------------------------- shape / mask

    /// <summary>
    /// Switches the board's shape at runtime and wipes it. <paramref name="sprite"/> replaces the mask
    /// sprite when given; null keeps the current one.
    /// </summary>
    public void SetMode(GridMode newMode, Sprite sprite = null)
    {
        mode = newMode;
        if (sprite) maskSprite = sprite;
        RebuildMask();
    }

    /// <summary>Recomputes which cells are drawable from the current settings, then wipes the board.</summary>
    public void RebuildMask()
    {
        if (_cells != null) ApplyMask();
    }

    private (GridMode, Sprite, float, float) MaskKey => (mode, maskSprite, maskCoverage, maskPadding);

    /// <summary>The sprite SpriteShape mode reads: the mask sprite, or the board's own Image sprite.</summary>
    public Sprite MaskSourceSprite => maskSprite ? maskSprite : Img.sprite;

    /// <summary>
    /// Computes the drawable-cell flags (row-major, bottom row first) from the current settings, without
    /// building the grid — works in Edit mode. Null means every cell is drawable (Full mode, or no sprite).
    /// </summary>
    public bool[] ComputeMask(out int cols, out int rows)
    {
        cols = width;
        rows = ResolveHeight();
        return mode == GridMode.SpriteShape ? BuildSpriteMask(cols, rows) : null;
    }

    private void ApplyMask()
    {
        _builtMask = MaskKey;

        bool[] drawable = mode == GridMode.SpriteShape ? BuildSpriteMask(width, height) : null;

        _activeCount = 0;
        for (int i = 0; i < _cells.Length; i++)
        {
            bool on = drawable == null || drawable[i];
            _cells[i] = on ? _cells[i] ?? new PixelCell() : null;
            if (on) _activeCount++;
        }

        if (_activeCount == 0)
            h.Out($"PixelGrid '{name}': the sprite mask left no drawable cells — check the sprite, coverage and padding.");

        Clear();
    }

    /// <summary>Per-cell "inside the sprite" flags, padded; null (= every cell) when there is no sprite.</summary>
    private bool[] BuildSpriteMask(int cols, int rows)
    {
        Sprite sprite = MaskSourceSprite;
        if (!sprite)
        {
            h.Out($"PixelGrid '{name}': SpriteShape mode but no mask sprite and no Image sprite — using the full board.");
            return null;
        }

        Color32[] src = ReadSpritePixels(sprite, out int sw, out int sh);
        Rect place = MaskPlacement();

        // A cell is inside when the alpha averaged over the sprite pixels under it reaches the coverage.
        var solid = new bool[cols * rows];
        for (int y = 0; y < rows; y++)
        {
            float v0 = ((float)y / rows - place.yMin) / place.height * sh;
            float v1 = ((float)(y + 1) / rows - place.yMin) / place.height * sh;
            if (v1 <= 0f || v0 >= sh) continue;
            int py0 = Mathf.Clamp(Mathf.FloorToInt(v0), 0, sh - 1);
            int py1 = Mathf.Clamp(Mathf.CeilToInt(v1), py0 + 1, sh);

            for (int x = 0; x < cols; x++)
            {
                float u0 = ((float)x / cols - place.xMin) / place.width * sw;
                float u1 = ((float)(x + 1) / cols - place.xMin) / place.width * sw;
                if (u1 <= 0f || u0 >= sw) continue;
                int px0 = Mathf.Clamp(Mathf.FloorToInt(u0), 0, sw - 1);
                int px1 = Mathf.Clamp(Mathf.CeilToInt(u1), px0 + 1, sw);

                long alpha = 0;
                for (int py = py0; py < py1; py++)
                for (int px = px0; px < px1; px++)
                    alpha += src[py * sw + px].a;

                solid[y * cols + x] = alpha >= maskCoverage * 255f * (px1 - px0) * (py1 - py0);
            }
        }

        return Pad(solid, maskPadding, cols, rows);
    }

    /// <summary>
    /// Where the mask sprite sits on the board, in normalized (0..1) board coordinates. A dedicated mask
    /// sprite is stretched over the whole board; the board's own sprite follows the Image's Preserve Aspect.
    /// </summary>
    public Rect MaskPlacement()
    {
        var place = new Rect(0f, 0f, 1f, 1f);
        Image image = Img;
        Sprite sprite = MaskSourceSprite;
        if (maskSprite || !sprite || !image.preserveAspect || image.type != Image.Type.Simple) return place;

        RectTransform rt = RectT;
        Rect r = rt.rect;
        float spriteAspect = sprite.rect.width / sprite.rect.height;
        float boardAspect = r.width / r.height;
        Vector2 pivot = rt.pivot;

        // Same fit as Image.PreserveSpriteAspectRatio: shrink one axis, anchored by the pivot.
        if (spriteAspect > boardAspect)
        {
            place.height = boardAspect / spriteAspect;
            place.y = (1f - place.height) * pivot.y;
        }
        else
        {
            place.width = spriteAspect / boardAspect;
            place.x = (1f - place.width) * pivot.x;
        }
        return place;
    }

    /// <summary>
    /// The sprite's pixels (bottom row first), read through the GPU so the texture doesn't need Read/Write
    /// enabled and may be compressed or packed in an atlas.
    /// </summary>
    private static Color32[] ReadSpritePixels(Sprite sprite, out int sw, out int sh)
    {
        Texture2D tex = sprite.texture;
        Rect area;
        try { area = sprite.textureRect; }      // throws for tightly packed atlas sprites
        catch (Exception) { area = sprite.rect; }

        int sx = Mathf.Clamp(Mathf.FloorToInt(area.x), 0, tex.width - 1);
        int sy = Mathf.Clamp(Mathf.FloorToInt(area.y), 0, tex.height - 1);
        sw = Mathf.Clamp(Mathf.RoundToInt(area.width), 1, tex.width - sx);
        sh = Mathf.Clamp(Mathf.RoundToInt(area.height), 1, tex.height - sy);

        RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        RenderTexture previous = RenderTexture.active;
        Graphics.Blit(tex, rt);
        RenderTexture.active = rt;

        var read = new Texture2D(sw, sh, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(sx, sy, sw, sh), 0, 0, false);

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        Color32[] pixels = read.GetPixels32();
        if (Application.isPlaying) Destroy(read);
        else DestroyImmediate(read);
        return pixels;
    }

    /// <summary>
    /// Grows (<paramref name="padding"/> &gt; 0) or shrinks (&lt; 0) the solid area by a disc of that
    /// radius, in cells. Outside the board counts as empty, so shrinking also pulls away from its edges.
    /// </summary>
    private static bool[] Pad(bool[] solid, float padding, int cols, int rows)
    {
        float radius = Mathf.Abs(padding);
        if (radius < 1f) return solid;

        bool grow = padding > 0f;
        int ri = Mathf.FloorToInt(radius);
        float r2 = radius * radius;
        var result = (bool[])solid.Clone();

        for (int y = 0; y < rows; y++)
        for (int x = 0; x < cols; x++)
        {
            // Only cells on the mask's edge can change anything: stamp a disc around each one.
            if (solid[y * cols + x] != grow || !IsMaskEdge(solid, x, y, cols, rows)) continue;

            for (int dy = -ri; dy <= ri; dy++)
            for (int dx = -ri; dx <= ri; dx++)
            {
                int cx = x + dx, cy = y + dy;
                if (dx * dx + dy * dy <= r2 && cx >= 0 && cy >= 0 && cx < cols && cy < rows)
                    result[cy * cols + cx] = grow;
            }
        }

        // Shrink away from the board's own border too.
        if (!grow)
            for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
                if (Mathf.Min(Mathf.Min(x + 1, cols - x), Mathf.Min(y + 1, rows - y)) <= radius)
                    result[y * cols + x] = false;

        return result;
    }

    /// <summary>A cell with a 4-neighbour that differs from it (outside the board counts as empty).</summary>
    private static bool IsMaskEdge(bool[] solid, int x, int y, int cols, int rows)
    {
        bool self = solid[y * cols + x];
        return (x > 0 && solid[y * cols + x - 1]) != self
               || (x < cols - 1 && solid[y * cols + x + 1]) != self
               || (y > 0 && solid[(y - 1) * cols + x]) != self
               || (y < rows - 1 && solid[(y + 1) * cols + x]) != self;
    }

    // ---------------------------------------------------------------- brush

    private void ForEachInBrush(Vector2Int center, int radius, Action<Vector2Int> action)
    {
        if (radius <= 0)
        {
            action(center);
            return;
        }

        int r2 = radius * radius;
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
            if (dx * dx + dy * dy <= r2)
                action(new Vector2Int(center.x + dx, center.y + dy));
    }

    /// <summary>
    /// Bresenham line from <paramref name="from"/> to <paramref name="to"/>, both ends excluded —
    /// the cells a fast stroke skipped over between two frames.
    /// </summary>
    public static void CellsBetween(Vector2Int from, Vector2Int to, List<Vector2Int> result)
    {
        result.Clear();

        int dx = Mathf.Abs(to.x - from.x), sx = from.x < to.x ? 1 : -1;
        int dy = -Mathf.Abs(to.y - from.y), sy = from.y < to.y ? 1 : -1;
        int err = dx + dy;
        int x = from.x, y = from.y;

        while (true)
        {
            if (x == to.x && y == to.y) break;

            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }

            if (x == to.x && y == to.y) break;
            result.Add(new Vector2Int(x, y));
        }
    }
}
