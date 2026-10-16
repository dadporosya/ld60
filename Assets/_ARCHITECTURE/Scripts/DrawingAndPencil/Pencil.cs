using System;
using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// <summary>
/// Hard pixel pencil. The pencil has a <b>tip</b> that follows the cursor — almost instantly while
/// hovering, but with a heavy drag while drawing — and all painting happens under the tip, not the
/// cursor. The cursor marker (<see cref="PencilMarker"/>) sits on the tip, so what you see is where the
/// paint goes.
///
/// <para>While LMB is held the cell under the tip keeps saturating (longer stay → darker), and the cells
/// the tip skipped between frames are filled in. Going back over painted cells saturates them faster
/// (see <see cref="SaturationSettings"/>).</para>
///
/// <para>A stroke starts only when the press lands on the board (<see cref="PixelGrid.OnPressed"/>), so UI
/// on top of the board blocks drawing. Once started, the stroke follows the mouse until release.</para>
///
/// <para>The pencil can draw on several boards (<see cref="grids"/>). While hovering it moves to whichever of
/// them is under the cursor; a stroke stays on the board it started on. <see cref="Grid"/> is that current
/// board.</para>
///
/// <para><see cref="PullTo"/> yanks the pencil away: it ends the stroke and drags the tip, the marker and the
/// system cursor to a screen point (the <see cref="GameManager"/> does this when a line is crossed).</para>
///
/// <para><b>Playback.</b> <see cref="SetVirtualPointer"/> swaps the mouse for a scripted pointer (see
/// <see cref="PencilPlayback"/>, which replays a <see cref="PencilRecording"/>): the tip follows it, a press
/// over a board starts a stroke and a release ends it, and real clicks are ignored until
/// <see cref="ClearVirtualPointer"/>. Playback draws even while the game isn't in its Game state (a tutorial).</para>
///
/// <para><b>Dialogues.</b> The pencil also draws while a mid-game dialogue pauses the game
/// (<see cref="GameManager.DrawingThroughDialogue"/>), and a click that would start a stroke on one of its boards
/// is the pencil's: it doesn't advance the dialogue (see <see cref="DialogueManager.AddClickClaimer"/>). The same goes
/// for the tutorial's dialogues when it lets the player draw (<see cref="TutorialCutscene.PlayerMayDraw"/>).</para>
/// </summary>
public class Pencil : MonoBehaviour
{
    [Tooltip("Boards this pencil can draw on. Where boards overlap, the earlier one in the list wins. " +
             "Empty: every PixelGrid in the scene.")]
    [SerializeField] private List<PixelGrid> grids = new List<PixelGrid>();

    // The single board this component used to hold; moved into the list on load.
    [SerializeField, HideInInspector, FormerlySerializedAs("grid")] private PixelGrid legacyGrid;

    [Tooltip("0 = a single pixel, like Photoshop's 1px pencil.")]
    [SerializeField, Min(0)] private int brushRadius = 0;

    [Header("Tip")]
    [Tooltip("Seconds the tip takes to catch up with the cursor while hovering. 0 = glued to the cursor.")]
    [SerializeField, Min(0f)] private float hoverFollowTime = 0.02f;
    [Tooltip("Seconds the tip takes to catch up with the cursor while drawing — the pen drags behind.")]
    [SerializeField, Min(0f)] private float drawFollowTime = 0.15f;

    /// <summary>Master switch — the game manager turns this off on a loss, and on a win once the end game button is pressed.</summary>
    public bool inputEnabled = true;

    public bool IsDrawing { get; private set; }

    /// <summary>
    /// Input is on and the game is running (or only paused for a mid-game dialogue, or held in the tutorial that
    /// lets the player draw, or won with the main board left drawable, or a playback drives the pencil) — a press
    /// on the current board would start a stroke.
    /// </summary>
    public bool CanDraw => CanDrawOn(Grid);

    /// <summary><see cref="CanDraw"/>, for a press on <paramref name="board"/>.</summary>
    public bool CanDrawOn(PixelGrid board) =>
        inputEnabled && (_virtual || GameFlowManager.Instance == null || GameFlowManager.Instance.IsGame()
                         || GameManager.Instance && GameManager.Instance.DrawingThroughDialogue
                         || GameManager.Instance && GameManager.Instance.MayDrawAfterWin(board)
                         || TutorialCutscene.PlayerMayDraw);

    /// <summary>A scripted pointer drives the pencil instead of the mouse (see <see cref="SetVirtualPointer"/>).</summary>
    public bool IsPlayingBack => _virtual;

    /// <summary>Screen position of the pointer the pencil followed this frame: the mouse, or the virtual pointer.</summary>
    public Vector2 PointerScreen { get; private set; }

    /// <summary>The pointer's button is down: LMB, or the virtual pointer's press.</summary>
    public bool PointerPressed { get; private set; }

    /// <summary>The virtual pointer was pressed / released in this frame's Update (playback only).</summary>
    public bool VirtualPressedThisFrame => _virtual && _virtualPressFrame == Time.frameCount;
    public bool VirtualReleasedThisFrame => _virtual && _virtualReleaseFrame == Time.frameCount;

    /// <summary>A <see cref="PullTo"/> is dragging the pencil and the cursor; no stroke can start meanwhile.</summary>
    public bool IsPulled => _pull.isAlive;

    public IReadOnlyList<PixelGrid> Grids
    {
        get
        {
            ResolveGrids();
            return grids;
        }
    }

    /// <summary>The board the pencil is on: the one under the cursor while hovering, the stroke's board while drawing.</summary>
    public PixelGrid Grid
    {
        get
        {
            ResolveGrids();
            return grid;
        }
    }

    /// <summary>Fired when the pencil moves onto another board.</summary>
    public event Action<PixelGrid> OnGridChanged;

    /// <summary>Brush width in cells.</summary>
    public int BrushDiameter => brushRadius * 2 + 1;

    /// <summary>The tip has a position yet (it gets one the first time the cursor maps onto the board's plane).</summary>
    public bool HasTip => _hasTip;

    /// <summary>The mouse itself (not the trailing tip) is over a cell of the board.</summary>
    public bool IsCursorOnBoard { get; private set; }

    /// <summary>The tip is over a cell of the board.</summary>
    public bool IsTipOnBoard { get; private set; }

    /// <summary>Cell under the tip; only meaningful while <see cref="IsTipOnBoard"/>.</summary>
    public Vector2Int TipCell { get; private set; }

    public Vector3 TipWorldPosition => Grid.LocalToWorld(_tip);

    public event Action OnStrokeBegan;

    private PixelGrid grid; // current board
    private bool _gridsResolved;
    private readonly Dictionary<PixelGrid, Action> _pressHandlers = new Dictionary<PixelGrid, Action>();

    private Vector2 _tip; // local to the current board
    private Vector2 _tipVelocity;
    private bool _hasTip;
    private Vector2Int _lastCell;
    private bool _hasLastCell;
    private readonly List<Vector2Int> _lineBuffer = new List<Vector2Int>();
    private Tween _pull;
    private Vector2 _pullScreen; // where the pull has the cursor this frame
    private Vector2 _shakeOffset;
    private Tween _shakeTween;

    public Vector2 ShakeOffset => _shakeOffset;

    private bool _virtual;
    private Vector2 _virtualScreen;
    private bool _virtualPressed;
    private bool _virtualWasPressed; // as of the last Update, for press/release edges
    private int _virtualPressFrame = -1;
    private int _virtualReleaseFrame = -1;

    private void OnValidate() => MigrateLegacyGrid();

    private void Awake() => ResolveGrids();

    private void OnEnable()
    {
        ResolveGrids();
        foreach (PixelGrid board in grids)
        {
            if (!board || _pressHandlers.ContainsKey(board)) continue;
            Action handler = () => HandleBoardPressed(board);
            board.OnPressed += handler;
            _pressHandlers[board] = handler;
        }
        DialogueManager.AddClickClaimer(ClaimsClick);
    }

    private void OnDisable()
    {
        foreach (KeyValuePair<PixelGrid, Action> pair in _pressHandlers)
            if (pair.Key) pair.Key.OnPressed -= pair.Value;
        _pressHandlers.Clear();
        DialogueManager.RemoveClickClaimer(ClaimsClick);
        if (IsDrawing && grid) grid.EndStroke();
        IsDrawing = false;
        _pull.Stop();
        _shakeTween.Stop();
        _shakeOffset = Vector2.zero;
    }

    private void HandleBoardPressed(PixelGrid board)
    {
        // During playback the virtual pointer starts the strokes (see Update); real clicks don't draw.
        if (_virtual || IsDrawing || IsPulled || !CanDrawOn(board)) return;

        SetGrid(board);

        // The stroke starts exactly where the player clicked, wherever the trailing tip was.
        Mouse mouse = Mouse.current;
        if (mouse != null && grid.ScreenToLocal(mouse.position.ReadValue(), out Vector2 cursor)) SnapTip(cursor);
        RefreshTip();

        if (!IsTipOnBoard)
            h.Out($"Pencil: '{grid.name}' was pressed, but the cursor maps outside its grid — " +
                  "check the Canvas' Render Camera.");

        BeginStroke();
    }

    /// <summary>A click on <paramref name="clicked"/> lands on one of the boards and would start a stroke there.</summary>
    private bool ClaimsClick(GameObject clicked)
    {
        if (_virtual || IsPulled || !clicked) return false;

        // The object the press goes to, exactly as the EventSystem will route it (the board, unless something on it takes presses).
        GameObject pressed = ExecuteEvents.GetEventHandler<IPointerDownHandler>(clicked);
        foreach (PixelGrid board in grids)
            if (board && board.gameObject == pressed)
                return CanDrawOn(board);
        return false;
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;
        if ((mouse == null && !_virtual) || !Grid) return;

        Vector2 screen;
        bool pressed;
        bool virtualPress = false;
        if (_virtual)
        {
            screen = _virtualScreen;
            pressed = _virtualPressed;
            virtualPress = pressed && !_virtualWasPressed;
            if (virtualPress) _virtualPressFrame = Time.frameCount;
            else if (!pressed && _virtualWasPressed) _virtualReleaseFrame = Time.frameCount;
            _virtualWasPressed = pressed;
        }
        else
        {
            screen = mouse.position.ReadValue();
            pressed = mouse.leftButton.isPressed;
        }

        bool pulled = IsPulled;
        if (pulled)
        {
            // The pull owns the cursor: whatever the mouse did this frame, it goes where the pull has it.
            screen = _pullScreen;
            if (!_virtual) mouse.WarpCursorPosition(screen);
        }

        PointerScreen = screen;
        PointerPressed = pressed;

        if (!IsDrawing)
        {
            // Hovering: move onto the board under the cursor; off every board, stay on the last one.
            PixelGrid hovered = GridUnder(screen);
            if (hovered) SetGrid(hovered);
            // The virtual pointer's own "click": a press that lands on a board starts a stroke there.
            virtualPress = virtualPress && hovered && !pulled;
        }
        else virtualPress = false;

        bool cursorOnBoard = false;
        if (grid.ScreenToLocal(screen, out Vector2 cursor))
        {
            // The pull drags the tip itself, so no hover lag on top of it; a stroke starts exactly where it's pressed.
            if (pulled || virtualPress) SnapTip(cursor);
            else MoveTip(cursor);
            cursorOnBoard = grid.LocalToCell(cursor, out _);
        }

        IsCursorOnBoard = cursorOnBoard;
        RefreshTip();

        if (!CanDraw)
        {
            if (IsDrawing) EndStroke();
            return;
        }

        if (virtualPress) BeginStroke();
        if (!IsDrawing) return;

        if (!pressed)
        {
            EndStroke();
            return;
        }

        if (!IsTipOnBoard)
        {
            // Stroke continues outside the board, but don't bridge a line across the gap on re-entry.
            _hasLastCell = false;
            return;
        }

        // Painting can end the stroke mid-frame (crossing a line pulls the pencil away): stop right there.
        Vector2Int cell = TipCell;
        if (_hasLastCell && cell != _lastCell)
        {
            PixelGrid.CellsBetween(_lastCell, cell, _lineBuffer);
            foreach (Vector2Int skipped in _lineBuffer)
            {
                grid.Pass(skipped, brushRadius);
                if (!IsDrawing) return;
            }
        }

        grid.Dwell(cell, brushRadius, Time.deltaTime);
        if (!IsDrawing) return;
        _lastCell = cell;
        _hasLastCell = true;
    }

    /// <summary>First board in the list with a cell under <paramref name="screen"/>, or null.</summary>
    private PixelGrid GridUnder(Vector2 screen)
    {
        foreach (PixelGrid board in grids)
            if (board && board.isActiveAndEnabled && board.ScreenToCell(screen, out _))
                return board;
        return null;
    }

    private void SetGrid(PixelGrid board)
    {
        if (!board || board == grid) return;

        // The tip is stored in the board's local space: carry it over so it doesn't jump.
        if (grid && _hasTip)
        {
            _tip = board.transform.InverseTransformPoint(grid.LocalToWorld(_tip));
            _tipVelocity = Vector2.zero;
        }

        grid = board;
        _hasLastCell = false;
        OnGridChanged?.Invoke(board);
    }

    private void ResolveGrids()
    {
        if (_gridsResolved) return;
        _gridsResolved = true;

        MigrateLegacyGrid();
        grids.RemoveAll(board => !board);
        if (grids.Count == 0)
            grids.AddRange(FindObjectsByType<PixelGrid>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
        if (grids.Count == 0) h.Out($"Pencil on '{name}': no PixelGrid to draw on.");

        grid = grids.Count > 0 ? grids[0] : null;
    }

    private void MigrateLegacyGrid()
    {
        if (!legacyGrid) return;
        if (!grids.Contains(legacyGrid)) grids.Insert(0, legacyGrid);
        legacyGrid = null;
    }

    private void MoveTip(Vector2 cursor)
    {
        float followTime = IsDrawing ? drawFollowTime : hoverFollowTime;

        if (!_hasTip || followTime <= 0f || !float.IsFinite(_tip.x) || !float.IsFinite(_tip.y))
        {
            SnapTip(cursor);
            return;
        }

        // Unscaled: the pen is an input device — a paused or slowed timeScale must not leave it frozen.
        _tip = Vector2.SmoothDamp(_tip, cursor, ref _tipVelocity, followTime, Mathf.Infinity, Time.unscaledDeltaTime);
    }

    private void SnapTip(Vector2 cursor)
    {
        _tip = cursor;
        _tipVelocity = Vector2.zero;
        _hasTip = true;
    }

    private void RefreshTip()
    {
        Vector2Int tipCell = default;
        IsTipOnBoard = _hasTip && grid.LocalToCell(_tip, out tipCell);
        if (IsTipOnBoard) TipCell = tipCell;
    }

    private void BeginStroke()
    {
        IsDrawing = true;
        _hasLastCell = false;
        grid.BeginStroke();
        OnStrokeBegan?.Invoke();
    }

    private void EndStroke()
    {
        IsDrawing = false;
        _hasLastCell = false;
        if (grid) grid.EndStroke();
    }

    /// <summary>Ends the stroke being drawn, if any; the next one needs a fresh press.</summary>
    public void CancelStroke()
    {
        if (IsDrawing) EndStroke();
    }

    /// <summary>
    /// Drives the pencil with a scripted pointer instead of the mouse from now on (call it every frame the
    /// pointer changes): <paramref name="screen"/> is where it points, <paramref name="pressed"/> whether its
    /// button is down. Undo with <see cref="ClearVirtualPointer"/>.
    /// </summary>
    public void SetVirtualPointer(Vector2 screen, bool pressed)
    {
        if (!_virtual)
        {
            // Taking over: whatever the mouse was drawing ends, and a press only counts once it's seen going down.
            if (IsDrawing) EndStroke();
            _virtualWasPressed = false;
        }

        _virtual = true;
        _virtualScreen = screen;
        _virtualPressed = pressed;
    }

    /// <summary>Hands the pencil back to the mouse; a stroke the virtual pointer was drawing ends.</summary>
    public void ClearVirtualPointer()
    {
        if (!_virtual) return;

        _virtual = false;
        _virtualPressed = false;
        _virtualWasPressed = false;
        if (IsDrawing) EndStroke();
    }

    /// <summary>
    /// Ends the stroke and drags the pencil — tip, marker and system cursor together — to
    /// <paramref name="screenPoint"/> over <paramref name="duration"/> seconds, starting from where the tip
    /// is (not the mouse, which runs ahead of it mid-stroke). The mouse is overridden until it arrives, and a
    /// new stroke needs a fresh press after that.
    /// </summary>
    public void PullTo(Vector2 screenPoint, float duration, Ease ease = Ease.OutCubic)
    {
        if (IsDrawing) EndStroke();

        Mouse mouse = _virtual ? null : Mouse.current; // a playback's pointer isn't the system cursor
        Vector2 from = _hasTip && Grid ? grid.LocalToScreen(_tip)
            : mouse != null ? mouse.position.ReadValue() : screenPoint;

        _pull.Stop();
        _pullScreen = from;
        if (duration <= 0f)
        {
            _pullScreen = screenPoint;
            mouse?.WarpCursorPosition(screenPoint);
            return;
        }

        // Unscaled, like the tip: a pause mustn't leave the cursor stuck mid-pull.
        // The last step lands after Update may have already run this frame, so place the cursor on arrival too.
        _pull = Tween.Custom(this, from, screenPoint, duration, (pencil, point) => pencil._pullScreen = point,
                ease, useUnscaledTime: true)
            .OnComplete(this, pencil =>
            {
                if (!pencil._virtual) Mouse.current?.WarpCursorPosition(pencil._pullScreen);
            });
    }

    /// <summary>
    /// Shakes the pencil / marker slightly around its current position.
    /// </summary>
    public void Shake(float strength = 10f, float duration = 0.25f, float frequency = 25f)
    {
        PencilMarker marker = GetComponent<PencilMarker>();
        if (marker)
        {
            marker.Shake(strength, duration, frequency);
            return;
        }

        _shakeTween.Stop();
        _shakeOffset = Vector2.zero;
        if (strength <= 0f || duration <= 0f) return;

        Vector3 axes = new Vector3(1f, 1f, 0f);
        _shakeTween = Tween.ShakeCustom(this, Vector3.zero,
            new ShakeSettings(axes * strength, duration, frequency, enableFalloff: true, useUnscaledTime: true),
            (p, offset) => p._shakeOffset = offset)
            .OnComplete(this, p => p._shakeOffset = Vector2.zero);
    }
}
