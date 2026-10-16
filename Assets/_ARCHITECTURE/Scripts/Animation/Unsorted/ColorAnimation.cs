using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PrimeTween;

/// <summary>
/// Tweens the tint of a SpriteRenderer (and, optionally, every SpriteRenderer below it) through
/// a list of colour keyframes.
///
/// Operating principle: mirrors <see cref="DissolveAnimation"/> — on Start it collects its
/// targets, captures the resting colour, then walks <see cref="values"/> in order, tweening from
/// the current colour to each entry over the matching entry in <see cref="durations"/>.
/// <c>values = [red, white]</c> flashes red and eases back to white.
///
/// Colours are written to <c>SpriteRenderer.color</c>, not to the material, so this stacks with
/// MasterMaterialController and the dissolve/outline effects without fighting them.
///
/// For UI elements under a Canvas use <see cref="ColorAnimationUI"/> — a UI Graphic is not a
/// Renderer and is never picked up by the sweep below.
/// </summary>
public class ColorAnimation : AnimationBase
{
    [Header("Color Settings")]
    [Tooltip("Sequence of colors to tween through, in order.")]
    public List<Color> values = new List<Color>();

    [Tooltip("On: colors blend into each other smoothly over their duration. Off: each color is applied instantly and simply held for its duration (a hard flip-book, like SpriteChangeAnimation).")]
    public bool softBlending = true;

    [Tooltip("With softBlending on, the time taken to reach each keyframe; with it off, how long each keyframe is held before the next. Entries are matched to values by index; anything missing falls back to defaultDuration.")]
    public List<float> durations = new List<float>();

    [Tooltip("Duration used for keyframes with no matching entry in durations.")]
    public float defaultDuration = 1f;

    [Tooltip("Easing applied while blending between keyframes. Unused when softBlending is off.")]
    public Ease ease = Ease.Default;

    [Tooltip("Also tint child renderers. Leave on when this sits on a model built from several parts.")]
    public bool includeChildren = true;

    [Tooltip("Tint only the RGB channels and leave each target's own alpha untouched. Turn on when a separate fade animation drives the alpha.")]
    public bool ignoreAlpha = false;

    [Tooltip("Multiply the keyframe colors by each target's resting color instead of replacing it, so differently tinted parts keep their relationship (white keyframe = unchanged).")]
    public bool multiplyByInitialColor = false;

    [Tooltip("Resting color. When customInitialState is off this is captured from the target instead.")]
    public Color initialState = Color.white;

    // The resting colour of every target, captured alongside the targets themselves. Kept
    // per-target so multiplyByInitialColor and the return-to-rest can restore parts that started
    // out with different tints.
    protected readonly List<Color> _initialColors = new();

    // The tween currently writing the colour. Tracked so it can be stopped on return/stop;
    // otherwise it keeps writing the property after the coroutine is gone.
    private Tween _colorTween;

    // The last colour handed to SetColor — the value the tweens actually interpolate. See
    // CurrentDriveColor for why this can't always be read back off the renderer.
    private Color _driveColor = Color.white;

    private readonly List<SpriteRenderer> _renderers = new();

    protected virtual int TargetCount => _renderers.Count;
    protected bool HasTargets => TargetCount > 0;

    public override void Awake()
    {
        // Deliberately empty: targets are collected in Start, matching DissolveAnimation, so any
        // component that swaps sprites/graphics in its own Awake has already run.
    }

    public override void Start()
    {
        CollectTargets();
        CaptureInitialColors();

        // With customInitialState on, the inspector value replaces whatever the targets were
        // authored with — that single colour becomes the resting state for all of them.
        if (ShouldCaptureInitialState()) initialState = ReadColor();
        else OverrideInitialColors(initialState);

        base.Start();
    }

    /// <summary>
    /// Fills the target list with the renderers to tint. Override to source targets from
    /// somewhere other than SpriteRenderers (see <see cref="ColorAnimationUI"/>).
    /// </summary>
    protected virtual void CollectTargets()
    {
        _renderers.Clear();

        if (includeChildren)
        {
            foreach (SpriteRenderer rend in GetComponentsInChildren<SpriteRenderer>(true))
                if (rend != null && !_renderers.Contains(rend)) _renderers.Add(rend);
        }
        else
        {
            SpriteRenderer rend = GetComponent<SpriteRenderer>();
            if (rend != null) _renderers.Add(rend);
        }

        if (!HasTargets)
            h.Out($"ColorAnimation on '{name}': no SpriteRenderer found; the animation will do nothing.");
    }

    // Reads the current colour of a single target. Index is guaranteed to be in range.
    protected virtual Color ReadColorAt(int index) => _renderers[index].color;

    // Writes a colour to a single target. Index is guaranteed to be in range.
    protected virtual void WriteColorAt(int index, Color color) => _renderers[index].color = color;

    private void CaptureInitialColors()
    {
        _initialColors.Clear();
        for (int i = 0; i < TargetCount; i++) _initialColors.Add(ReadColorAt(i));
    }

    // The first target stands in for the whole group when a single "current colour" is needed
    // (capturing the initial state, and the from-value of each tween).
    protected Color ReadColor() => HasTargets ? ReadColorAt(0) : initialState;

    protected void SetColor(Color value)
    {
        _driveColor = value;

        for (int i = 0; i < TargetCount; i++)
        {
            Color initial = i < _initialColors.Count ? _initialColors[i] : Color.white;
            Color target = multiplyByInitialColor ? value * initial : value;
            if (ignoreAlpha) target.a = ReadColorAt(i).a;
            WriteColorAt(i, target);
        }
    }

    private void OverrideInitialColors(Color color)
    {
        for (int i = 0; i < _initialColors.Count; i++) _initialColors[i] = color;
        RestoreInitialColors();
    }

    // Restores every target to the colour it was captured with, so parts that started out with
    // different tints don't all collapse onto the single initialState value.
    private void RestoreInitialColors()
    {
        _driveColor = multiplyByInitialColor ? Color.white : initialState;

        for (int i = 0; i < TargetCount; i++)
        {
            if (i >= _initialColors.Count) continue;
            Color initial = _initialColors[i];
            if (ignoreAlpha) initial.a = ReadColorAt(i).a;
            WriteColorAt(i, initial);
        }
    }

    public override void ReturnToInitialState()
    {
        if (_colorTween.isAlive) _colorTween.Stop();
        RestoreInitialColors();
    }

    // The colour was reset externally, so the targets already hold their resting tint. Just drop
    // the running tween so the next Play() starts from the right value without writing anything.
    public override void SyncToRestingState()
    {
        if (_colorTween.isAlive) _colorTween.Stop();
    }

    public override IEnumerator AnimationCoroutine()
    {
        if (_colorTween.isAlive) _colorTween.Stop();

        if (!HasTargets || values.Count == 0)
        {
            yield return base.AnimationCoroutine();
            yield break;
        }

        // Walk the keyframes. With softBlending the duration is spent travelling to the colour;
        // without it the colour is applied at once and the duration is spent sitting on it.
        for (int i = 0; i < values.Count; i++)
        {
            Color target = values[i];
            float duration = i < durations.Count ? durations[i] : defaultDuration;

            if (!softBlending)
            {
                SetColor(target);
                if (duration > 0f) yield return new WaitForSeconds(duration);
                continue;
            }

            Color from = CurrentDriveColor();

            if (duration > 0f)
            {
                _colorTween = Tween.Custom(from, target, duration, val => SetColor(val), ease);
                while (_colorTween.isAlive) yield return null;
            }

            // Guarantees the exact keyframe colour, and covers duration <= 0 (instant step).
            SetColor(target);
        }

        yield return base.AnimationCoroutine();
    }

    // Eases the colour back to the resting value over returningTime. With softBlending off the
    // return is a hard cut too, so it skips straight to RestoreInitialColors.
    public override IEnumerator ReturnToInitialStateAnimated()
    {
        if (_colorTween.isAlive) _colorTween.Stop();
        if (!HasTargets) yield break;

        Color from = CurrentDriveColor();
        Color to = multiplyByInitialColor ? Color.white : initialState;

        if (softBlending && returningTime > 0f && from != to)
        {
            _colorTween = Tween.Custom(from, to, returningTime, val => SetColor(val), returningEasing);
            while (_colorTween.isAlive) yield return null;
        }

        RestoreInitialColors();
    }

    // The value being tweened is the *driving* colour, which is not what sits on the renderer when
    // multiplyByInitialColor is on (there the renderer holds drive * initial, so the renderer can't
    // be read back). _driveColor tracks it instead; when not multiplying, the renderer is the
    // truth and is read directly so external tint changes are respected.
    private Color CurrentDriveColor() => multiplyByInitialColor ? _driveColor : ReadColor();
}
