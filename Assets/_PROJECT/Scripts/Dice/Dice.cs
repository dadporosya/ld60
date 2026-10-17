using System;
using PrimeTween;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One poker die. It only shows and animates itself; the <see cref="DiceManager"/> decides when it
/// rolls, where it lands, what it shows and whether it may be locked (clicking a die asks the manager).
///
/// <para>UI: the faces show on <see cref="image"/> (a raycast target, so the die can be clicked) under a
/// Canvas with a GraphicRaycaster. The die's root travels across the table (its parent, the dice area);
/// lifting, spinning and the lock offset run on <see cref="visual"/> (the Image's transform by default).
/// Offsets are in the canvas's UI units.</para>
/// </summary>
public class Dice : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image image;
    [Tooltip("What lifts, spins and scales. Defaults to the Image's transform.")]
    [SerializeField] private Transform visual;
    [Tooltip("Optional: shown while the die is locked (a pin, a frame).")]
    [SerializeField] private GameObject lockedIndicator;

    /// <summary>The face showing, 1..6.</summary>
    public int Value { get; private set; } = 1;

    public bool IsLocked { get; private set; }

    public bool IsRolling => rollTween.isAlive;

    /// <summary>Where the die sits on the table: the root's local position, lift and lock offset excluded.</summary>
    public Vector3 TablePosition => tablePosition;

    /// <summary>Half the die's largest side, in its parent's units - how much room it needs around its centre.</summary>
    public float Radius
    {
        get
        {
            float size = 0f;
            if (transform is RectTransform root)
                size = Mathf.Max(root.rect.width * Mathf.Abs(root.localScale.x), root.rect.height * Mathf.Abs(root.localScale.y));
            if (visual is RectTransform face && visual != transform)
            {
                Vector3 scale = Vector3.Scale(face.localScale, transform.localScale);
                size = Mathf.Max(size, face.rect.width * Mathf.Abs(scale.x), face.rect.height * Mathf.Abs(scale.y));
            }
            return size * 0.5f;
        }
    }

    private DiceManager manager;
    private bool initialized;
    private Vector3 restScale;
    private Vector3 visualRestPosition;
    private Color baseColor = Color.white;

    // The pose: the table position plus what is layered on top of it.
    private Vector3 tablePosition;
    private Vector3 lockOffset;
    private float lift;

    private Tween rollTween;
    private Tween lockTween;
    private Tween punchTween;
    private Tween tintTween;
    private Tween outlineTween;
    private Outline outline;
    private bool hovered;
    private bool outlineShown;

    // The roll in progress.
    private DiceRollAnimation anim;
    private Sprite[] faces;
    private Sprite[] tumbleFrames;
    private int rollResult;
    private float startAngle;
    private float endAngle;
    private Vector3 travelFrom;
    private Vector3 travelTo;
    private int lastFrameStep;
    private int lastFrameIndex = -1;
    private Action<Dice> onLanded;

    private bool VisualIsRoot => visual == transform;

    /// <summary>Called by the manager before the die is used. Remembers the rest pose.</summary>
    public void Init(DiceManager owner)
    {
        manager = owner;
        if (initialized) return;
        initialized = true;

        if (!image) image = GetComponentInChildren<Image>(true);
        if (!visual) visual = image ? image.transform : transform;
        CheckSetup();
        SetUpOutline();

        restScale = visual.localScale;
        visualRestPosition = VisualIsRoot ? Vector3.zero : visual.localPosition;
        tablePosition = transform.localPosition;
        baseColor = Tint;
        if (lockedIndicator) lockedIndicator.SetActive(false);
    }

    private void OnDisable()
    {
        StopTweens();
        hovered = false;
        ShowOutline(false, instant: true);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
        RefreshOutline();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        RefreshOutline();
    }

    /// <summary>
    /// Outlined while hovered - and, with <see cref="DiceOutlineStyle.onlyWhenLockable"/>, only while clicking it
    /// would lock / unlock it. The manager calls this whenever that may have changed (a roll starts or ends).
    /// </summary>
    public void RefreshOutline()
    {
        DiceOutlineStyle style = manager ? manager.hoverOutline : null;
        bool show = hovered && style != null && style.enabled && (!style.onlyWhenLockable || manager.CanLockNow);
        ShowOutline(show);
    }

    private void SetUpOutline()
    {
        DiceOutlineStyle style = manager ? manager.hoverOutline : null;
        if (style == null || !style.enabled || !image) return;

        outline = image.GetComponent<Outline>();
        if (!outline) outline = image.gameObject.AddComponent<Outline>();
        outline.effectDistance = style.width;
        outline.useGraphicAlpha = true;
        ShowOutline(false, instant: true);
    }

    private void ShowOutline(bool show, bool instant = false)
    {
        if (!outline) return;
        DiceOutlineStyle style = manager ? manager.hoverOutline : null;
        Color color = style != null ? style.color : Color.white;
        Color target = show ? color : new Color(color.r, color.g, color.b, 0f);

        // Already there (or on its way): nothing to restart.
        if (!instant && outlineShown == show) return;
        outlineShown = show;

        outlineTween.Stop();
        float duration = style != null ? style.fadeDuration : 0f;
        if (instant || duration <= 0f || !isActiveAndEnabled)
        {
            outline.effectColor = target;
            outline.enabled = show;
            return;
        }

        // Hidden outlines are switched off, so idle dice don't pay for the extra geometry.
        outline.enabled = true;
        outlineTween = Tween.Custom(outline, outline.effectColor, target, duration, (o, c) => o.effectColor = c);
        if (!show) outlineTween.OnComplete(outline, o => o.enabled = false);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (manager) manager.ToggleLock(this);
    }

    /// <summary>Puts the die at <paramref name="localPosition"/> (in its parent) without animating.</summary>
    public void PlaceAt(Vector3 localPosition)
    {
        tablePosition = localPosition;
        ApplyPose();
    }

    // ---------------------------------------------------------------------
    // Rolling
    // ---------------------------------------------------------------------

    /// <summary>
    /// Throws the die: it travels to <paramref name="landAt"/> (local, in its parent), lifting (scaling up)
    /// and tumbling through <paramref name="frames"/> while it spins, and lands on <paramref name="result"/>.
    /// <paramref name="landed"/> runs once it is down.
    /// </summary>
    public void Roll(int result, DiceRollAnimation animation, Sprite[] faceSprites, Sprite[] frames, float delay,
        Vector3 landAt, Action<Dice> landed)
    {
        StopTweens();
        lift = 0f;
        visual.localScale = restScale;
        ApplyPose();

        anim = animation;
        faces = faceSprites;
        tumbleFrames = frames != null && frames.Length > 0 ? frames : faceSprites;
        rollResult = Mathf.Clamp(result, 1, PokerDiceEvaluator.Faces);
        onLanded = landed;
        travelFrom = tablePosition;
        travelTo = landAt;

        float direction = UnityEngine.Random.value < 0.5f ? -1f : 1f;
        startAngle = visual.localEulerAngles.z;
        endAngle = UnityEngine.Random.Range(-anim.restTilt, anim.restTilt) + 360f * anim.spins * direction;
        lastFrameStep = -1;

        rollTween = Tween.Custom(this, 0f, 1f, anim.duration, (die, t) => die.ApplyRoll(t), startDelay: delay)
            .OnComplete(this, die => die.Land());
    }

    private void ApplyRoll(float t)
    {
        float height = anim.liftCurve.Evaluate(t);
        visual.localScale = restScale * Mathf.LerpUnclamped(1f, anim.liftScale, height);
        lift = anim.liftHeight * height;
        tablePosition = Vector3.LerpUnclamped(travelFrom, travelTo, anim.travelCurve.Evaluate(t));
        ApplyPose();

        float angle = Mathf.LerpUnclamped(startAngle, endAngle, anim.spinCurve.Evaluate(t));
        visual.localEulerAngles = new Vector3(0f, 0f, angle);

        int step = Mathf.FloorToInt(t * anim.duration / Mathf.Max(anim.frameInterval, 0.01f));
        if (step != lastFrameStep && tumbleFrames != null && tumbleFrames.Length > 0)
        {
            lastFrameStep = step;
            SetSprite(tumbleFrames[NextFrameIndex()]);
        }
    }

    /// <summary>A random frame, never the same one twice in a row.</summary>
    private int NextFrameIndex()
    {
        if (tumbleFrames.Length == 1) return 0;
        int index = UnityEngine.Random.Range(0, tumbleFrames.Length - 1);
        if (index >= lastFrameIndex) index++;
        lastFrameIndex = index;
        return index;
    }

    private void Land()
    {
        ShowFace(rollResult);
        visual.localScale = restScale;
        lift = 0f;
        tablePosition = travelTo;
        ApplyPose();
        visual.localEulerAngles = new Vector3(0f, 0f, endAngle % 360f);

        if (anim.landPunch > 0f && anim.landPunchDuration > 0f)
            punchTween = Tween.PunchScale(visual, Vector3.one * anim.landPunch, anim.landPunchDuration);

        Action<Dice> landed = onLanded;
        onLanded = null;
        landed?.Invoke(this);
    }

    /// <summary>Shows <paramref name="value"/> without a roll.</summary>
    public void ShowFace(int value)
    {
        Value = Mathf.Clamp(value, 1, PokerDiceEvaluator.Faces);
        if (faces != null && faces.Length >= Value) SetSprite(faces[Value - 1]);
    }

    public void SetFaces(Sprite[] faceSprites) => faces = faceSprites;

    // ---------------------------------------------------------------------
    // Locking
    // ---------------------------------------------------------------------

    public void SetLocked(bool locked, DiceLockStyle style)
    {
        IsLocked = locked;
        if (lockedIndicator) lockedIndicator.SetActive(locked);

        Vector3 offset = locked ? (Vector3)style.offset : Vector3.zero;
        Color tint = locked ? baseColor * style.lockedTint : baseColor;

        lockTween.Stop();
        tintTween.Stop();
        if (style.duration > 0f && isActiveAndEnabled)
        {
            lockTween = Tween.Custom(this, lockOffset, offset, style.duration, (die, v) =>
            {
                die.lockOffset = v;
                die.ApplyPose();
            }, Ease.OutQuad);
            tintTween = Tween.Custom(this, Tint, tint, style.duration, (die, c) => die.Tint = c);
        }
        else
        {
            lockOffset = offset;
            ApplyPose();
            Tint = tint;
        }
    }

    /// <summary>Unlocked, at rest, ready for a new round. Keeps its face and its place on the table.</summary>
    public void ResetDice()
    {
        if (!initialized) return;
        StopTweens();
        IsLocked = false;
        lockOffset = Vector3.zero;
        lift = 0f;
        if (lockedIndicator) lockedIndicator.SetActive(false);
        Tint = baseColor;
        visual.localScale = restScale;
        visual.localEulerAngles = Vector3.zero;
        ApplyPose();
    }

    /// <summary>Stops a roll in flight (the round was cancelled). The die stays where it was; the landing callback is dropped.</summary>
    public void StopRoll()
    {
        onLanded = null;
        rollTween.Stop();
        if (!initialized) return;
        lift = 0f;
        visual.localScale = restScale;
        ApplyPose();
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>Writes the table position and the lift / lock offsets onto the root and the visual.</summary>
    private void ApplyPose()
    {
        Vector3 offset = lockOffset + Vector3.up * lift;
        if (VisualIsRoot)
        {
            transform.localPosition = tablePosition + offset;
            return;
        }

        transform.localPosition = tablePosition;
        visual.localPosition = visualRestPosition + offset;
    }

    private void StopTweens()
    {
        rollTween.Stop();
        lockTween.Stop();
        punchTween.Stop();
        tintTween.Stop();
    }

    /// <summary>Warns about UI setups where the die can't be clicked.</summary>
    private void CheckSetup()
    {
        if (!image) h.Out($"Dice '{name}': no Image to show the faces on.");
        else if (!image.raycastTarget) h.Out($"Dice '{name}': its Image isn't a raycast target, so it can't be clicked to lock.");
    }

    private void SetSprite(Sprite sprite)
    {
        if (image) image.sprite = sprite;
    }

    private Color Tint
    {
        get => image ? image.color : Color.white;
        set
        {
            if (image) image.color = value;
        }
    }
}
