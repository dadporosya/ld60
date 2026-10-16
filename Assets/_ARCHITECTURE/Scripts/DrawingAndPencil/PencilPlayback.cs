using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// Replays a <see cref="PencilRecording"/> through a <see cref="Pencil"/>: the pencil is driven by a virtual
/// pointer (<see cref="Pencil.SetVirtualPointer"/>) that follows the recording, so it moves, presses and paints
/// exactly like the take — marker and particles included — while the real mouse is ignored.
///
/// <para>Before the take starts the pointer glides, unpressed, from wherever the pencil is to the take's first
/// point (lead-in), so the hand doesn't teleport; after it ends the pointer can rest a moment and pull back
/// off the drawing (pull back). See <see cref="Settings"/>.</para>
///
/// <para><b>Mouse.</b> With <see cref="Settings.lockPlayerMouse"/> the real mouse is disabled for the whole
/// playback, so the player can't click or move anything; it is re-enabled at the end with the system cursor
/// put where the pointer ended. With <see cref="Settings.simulateMouse"/> the take also drives a separate
/// virtual Input System mouse (made <c>Mouse.current</c>), so UI and scripts reading the mouse see its
/// movement and its press. It is a device of its own on purpose: feeding the real mouse fights the physical
/// device's events — the button flickers and the real mouse is left stuck afterwards.</para>
/// </summary>
// Before the Pencil's Update, so the pencil reads this frame's pointer rather than last frame's.
[DefaultExecutionOrder(-50)]
public class PencilPlayback : MonoBehaviour
{
    [Serializable]
    public class Settings
    {
        [Tooltip("Playback speed (2 = twice as fast).")]
        [Min(0.01f)] public float speed = 1f;
        [Tooltip("Hold LMB down from the take's first press to its last release, as one stroke dragged along " +
                 "the whole path — gaps where the button was up in the take are pressed through.")]
        public bool continuousPress = true;

        [Header("Lead-in (to the first point)")]
        [Tooltip("Seconds the pointer takes to glide to the take's first point. 0: jump there. " +
                 "Ignored when Lead In Speed is set.")]
        [Min(0f)] public float leadInDuration = 0.5f;
        [Tooltip("Glide speed to the first point, in screen pixels per second — the lead-in then lasts as long " +
                 "as the distance needs. 0: use Lead In Duration.")]
        [Min(0f)] public float leadInSpeed = 0f;
        [Tooltip("Easing of the glide to the first point (0..1 → 0..1).")]
        public AnimationCurve leadInEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Pull back (after the take)")]
        [Tooltip("After the take ends, move the pointer (marker, tip and cursor) off the drawing.")]
        public bool pullBack = true;
        [Tooltip("Seconds the pointer rests on the take's last point before pulling back.")]
        [Min(0f)] public float pullBackDelay = 0.3f;
        [Tooltip("How far to pull back, in screen pixels (+X = right, +Y = up).")]
        public Vector2 pullBackOffset = new Vector2(150f, 0f);
        [Tooltip("Seconds the pull back takes. 0: jump there.")]
        [Min(0f)] public float pullBackDuration = 0.4f;
        public AnimationCurve pullBackEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Mouse")]
        [Tooltip("Disable the real mouse while playing, so the player can't move or click anything.")]
        public bool lockPlayerMouse = true;
        [Tooltip("Drive a virtual Input System mouse with the take — position and LMB — so UI and other " +
                 "scripts reading the mouse see it move and press.")]
        public bool simulateMouse = true;
    }

    [Tooltip("Empty: the Pencil on this object, else the first one in the scene.")]
    [SerializeField] private Pencil pencil;
    [Tooltip("The take is placed relative to this board. Empty: the GameManager's main board.")]
    [SerializeField] private PixelGrid board;
    [Tooltip("Used when Play is called without settings of its own.")]
    [SerializeField] private Settings settings = new Settings();

    public bool IsPlaying { get; private set; }

    /// <summary>The take played to its end (not fired by <see cref="Stop"/>).</summary>
    public event Action OnFinished;

    private PencilRecording _recording;
    private Settings _s;
    private float _time;
    private int _cursor;
    private Vector2 _leadInFrom;
    private float _leadInTime;
    private float _firstPress;
    private float _lastRelease;
    private bool _reachedEnd;

    private const string VirtualMouseName = "PencilPlaybackMouse";

    private Mouse _realMouse;     // the player's mouse, disabled while locked
    private Mouse _virtualMouse;  // the simulated one, only while playing

    public Pencil Pencil
    {
        get
        {
            if (!pencil) pencil = GetComponent<Pencil>();
            if (!pencil) pencil = FindFirstObjectByType<Pencil>();
            return pencil;
        }
    }

    private PixelGrid Board
    {
        get
        {
            if (!board && GameManager.Instance) board = GameManager.Instance.MainBoard;
            if (!board && Pencil && Pencil.Grids.Count > 0) board = Pencil.Grids[0];
            return board;
        }
    }

    /// <summary>
    /// Starts replaying <paramref name="recording"/>, cutting off any take already playing.
    /// <paramref name="overrideSettings"/>: null uses this component's own.
    /// </summary>
    public void Play(PencilRecording recording, Settings overrideSettings = null)
    {
        Stop();

        if (!recording || recording.IsEmpty)
        {
            h.Out("PencilPlayback: empty recording, nothing to play.");
            OnFinished?.Invoke();
            return;
        }

        if (!Pencil || !Board)
        {
            h.Out($"PencilPlayback on '{name}': no pencil or board to play '{recording.name}' on.");
            OnFinished?.Invoke();
            return;
        }

        _s = overrideSettings ?? settings ?? new Settings();
        _recording = recording;
        _cursor = 0;
        FindPressSpan();

        _leadInFrom = Pencil.HasTip ? Pencil.PointerScreen : StartScreen();
        _leadInTime = _s.leadInSpeed > 0f
            ? Vector2.Distance(_leadInFrom, StartScreen()) / _s.leadInSpeed
            : _s.leadInDuration;
        _time = -_leadInTime;
        _reachedEnd = false;

        TakeMouse();
        IsPlaying = true;
    }

    /// <summary>Plays <paramref name="recording"/> and waits for it to end (or be stopped).</summary>
    public IEnumerator PlayRoutine(PencilRecording recording, Settings overrideSettings = null)
    {
        Play(recording, overrideSettings);
        while (IsPlaying) yield return null;
    }

    /// <summary>Cuts the take off and hands the pencil and the mouse back to the player.</summary>
    public void Stop()
    {
        if (!IsPlaying) return;

        IsPlaying = false;
        _recording = null;
        Vector2 end = pencil ? pencil.PointerScreen : Vector2.zero;
        ReleaseMouse(end);
        if (pencil) pencil.ClearVirtualPointer();
    }

    private void OnDisable() => Stop();

    private void Update()
    {
        if (!IsPlaying) return;

        if (!_recording || !pencil || !board)
        {
            Stop();
            return;
        }

        // The last point was handed to the pencil last frame; now it's done. A flag, not a time comparison:
        // Mono may keep a float sum at higher precision, so re-checking "_time >= duration + outro" against
        // a _time clamped to that same sum can stay false forever.
        if (_reachedEnd)
        {
            Stop();
            OnFinished?.Invoke();
            return;
        }

        float duration = _recording.Duration;
        float endTime = duration + (_s.pullBack ? _s.pullBackDelay + _s.pullBackDuration : 0f);

        // Game time, like the paint the pencil lays down: pausing the game pauses the take.
        _time += Time.deltaTime * _s.speed;
        if (_time >= endTime)
        {
            _time = endTime;
            _reachedEnd = true;
        }

        if (_time < 0f)
        {
            // Lead-in: glide to the first point with the button up.
            float t = _leadInTime > 0f ? 1f + _time / _leadInTime : 1f;
            Drive(Vector2.LerpUnclamped(_leadInFrom, StartScreen(), _s.leadInEase.Evaluate(t)), false);
            return;
        }

        if (_time > duration)
        {
            // Pull back: rest on the last point, then move off it with the button up.
            Vector2 end = EndScreen();
            float since = _time - duration - _s.pullBackDelay;
            float t = since <= 0f ? 0f : _s.pullBackDuration > 0f ? Mathf.Clamp01(since / _s.pullBackDuration) : 1f;
            Drive(Vector2.LerpUnclamped(end, end + _s.pullBackOffset, _s.pullBackEase.Evaluate(t)), false);
            return;
        }

        _recording.Evaluate(_time, ref _cursor, out Vector2 position, out bool pressed);
        if (_s.continuousPress) pressed = _time >= _firstPress && _time < _lastRelease;
        Drive(PencilRecording.NormalizedToScreen(board, position), pressed);
    }

    /// <summary>From the take's first press to the release after its last press (or its end).</summary>
    private void FindPressSpan()
    {
        var samples = _recording.samples;
        _firstPress = float.PositiveInfinity;
        _lastRelease = float.NegativeInfinity;

        int last = -1;
        for (int i = 0; i < samples.Count; i++)
        {
            if (!samples[i].pressed) continue;
            if (last < 0) _firstPress = samples[i].time;
            last = i;
        }

        if (last >= 0)
            _lastRelease = last + 1 < samples.Count ? samples[last + 1].time : float.PositiveInfinity;
    }

    private void Drive(Vector2 screen, bool pressed)
    {
        pencil.SetVirtualPointer(screen, pressed);

        // Not locked: pin the system cursor to the pointer every frame instead, overriding the player's moves.
        if (!_s.lockPlayerMouse && _realMouse != null && _realMouse.added) _realMouse.WarpCursorPosition(screen);

        if (_virtualMouse != null)
            InputSystem.QueueStateEvent(_virtualMouse,
                new MouseState { position = screen }.WithButton(MouseButton.Left, pressed));
    }

    private void TakeMouse()
    {
        _realMouse = null;

        // Clear out virtual mice a stuck run left behind (with domain reload off they outlive Play mode), and
        // take the first real one as the player's — re-enabling it if a stuck run left it disabled.
        for (int i = InputSystem.devices.Count - 1; i >= 0; i--)
        {
            if (InputSystem.devices[i] is not Mouse mouse) continue;

            if (mouse.name.StartsWith(VirtualMouseName)) InputSystem.RemoveDevice(mouse);
            else _realMouse = mouse;
        }

        if (_realMouse == null) _realMouse = Mouse.current;

        if (_s.lockPlayerMouse && _realMouse != null && _realMouse.enabled)
            InputSystem.DisableDevice(_realMouse);

        if (_s.simulateMouse)
        {
            _virtualMouse = InputSystem.AddDevice<Mouse>(VirtualMouseName);
            _virtualMouse.MakeCurrent();
        }
    }

    private void ReleaseMouse(Vector2 screen)
    {
        if (_virtualMouse != null)
        {
            if (_virtualMouse.added) InputSystem.RemoveDevice(_virtualMouse);
            _virtualMouse = null;
        }

        if (_realMouse == null || !_realMouse.added) return;

        if (!_realMouse.enabled) InputSystem.EnableDevice(_realMouse);
        _realMouse.MakeCurrent();

        // Hand the cursor back where the pointer ended, not wherever the physical mouse drifted meanwhile —
        // and tell the Input System right away, so nothing reads a stale position before the next OS event.
        if (_s != null && _s.lockPlayerMouse)
        {
            _realMouse.WarpCursorPosition(screen);
            InputState.Change(_realMouse.position, screen);
        }

        _realMouse = null;
    }

    private Vector2 StartScreen() => PencilRecording.NormalizedToScreen(Board, _recording.samples[0].position);

    private Vector2 EndScreen() =>
        PencilRecording.NormalizedToScreen(board, _recording.samples[_recording.samples.Count - 1].position);
}
