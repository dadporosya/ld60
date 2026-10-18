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

    /// <summary>
    /// Locked before the last roll: it stays locked for the rest of the round, can't be clicked any more and shows
    /// the sealed outline. A die locked since the last roll can still be unlocked.
    /// </summary>
    public bool IsSealed { get; private set; }

    /// <summary>What the die does (a plain die, or one won in the dice choice). The manager picks its faces by it.</summary>
    public DiceKind Kind { get; private set; } = DiceKind.Default;

    /// <summary>The card a special die shows while hovered long enough (null for a plain die).</summary>
    public CardFace Info { get; private set; }

    /// <summary>Destroyed by a card for the rest of the round: off the table, not scored.</summary>
    public bool IsRemoved { get; private set; }

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
    // The Image's own color, and that with the kind's tint (what the die shows unlocked).
    private Color defaultColor = Color.white;
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
    private Color outlineColor;

    // The roll in progress.
    private DiceRollAnimation anim;
    private Sprite[] faces;
    private Sprite[] tumbleFrames;
    private int rollResult;
    private float startAngle;
    private float endAngle;
    private Vector3 travelFrom;
    private Vector3 travelTo;
    // A freed die still sits in its lock pose: the roll eases it out (offset and tint) as it travels.
    private Vector3 lockFrom;
    private Vector3 lockTo;
    private Color tintFrom;
    private Color tintTo;
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
        defaultColor = Tint;
        baseColor = defaultColor * kindTint;
        Tint = baseColor;
        if (lockedIndicator) lockedIndicator.SetActive(false);
    }

    private void OnDisable()
    {
        StopTweens();
        Unhover();
        HideOutlineNow();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
        RefreshOutline();
        if (manager) manager.SetDieHovered(this, true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        Unhover();
        RefreshOutline();
    }

    /// <summary>The pointer is gone (or the die is): its info card goes too.</summary>
    private void Unhover()
    {
        hovered = false;
        if (manager) manager.SetDieHovered(this, false);
    }

    /// <summary>
    /// A sealed die always shows the sealed outline. Others are outlined while hovered - and, with
    /// <see cref="DiceOutlineStyle.onlyWhenLockable"/>, only while clicking it would lock / unlock it or right
    /// clicking it would use a card. The manager calls this whenever that may have changed (a roll starts or ends).
    /// </summary>
    public void RefreshOutline()
    {
        DiceOutlineStyle style = manager ? manager.hoverOutline : null;
        if (style == null) return;

        if (IsSealed && !IsRemoved)
        {
            ShowOutline(true, style.sealedColor);
            return;
        }

        bool show = hovered && !IsRemoved && style.enabled &&
                    (!style.onlyWhenLockable || manager.CanLockNow || manager.HasDieAction(this));
        ShowOutline(show, style.color);
    }

    private void SetUpOutline()
    {
        // Set up even with the hover outline off: sealed dice still need theirs.
        DiceOutlineStyle style = manager ? manager.hoverOutline : null;
        if (style == null || !image) return;

        outline = image.GetComponent<Outline>();
        if (!outline) outline = image.gameObject.AddComponent<Outline>();
        outline.effectDistance = style.width;
        outline.useGraphicAlpha = true;
        outlineColor = style.color;
        HideOutlineNow();
    }

    private void HideOutlineNow() => ShowOutline(false, outlineColor, instant: true);

    private void ShowOutline(bool show, Color color, bool instant = false)
    {
        if (!outline) return;
        DiceOutlineStyle style = manager ? manager.hoverOutline : null;
        Color target = show ? color : new Color(color.r, color.g, color.b, 0f);

        // Already there (or on its way): nothing to restart.
        if (!instant && outlineShown == show && (!show || outlineColor == color)) return;
        outlineShown = show;
        outlineColor = color;

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

    /// <summary>Left click locks / unlocks; right click uses a card on the die (reroll, destroy, flip). Not once sealed.</summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (!manager || IsSealed) return;
        if (eventData.button == PointerEventData.InputButton.Left) manager.ToggleLock(this);
        else if (eventData.button == PointerEventData.InputButton.Right) manager.UseDieAction(this);
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
        // Still locked (a free reroll of a locked die): it keeps the lock pose. Unlocked: out of it, from wherever
        // it is - a freed die, or one whose unlock was cut short by the throw.
        lockFrom = lockOffset;
        lockTo = IsLocked ? lockOffset : Vector3.zero;
        tintFrom = Tint;
        tintTo = IsLocked ? Tint : baseColor;

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
        float travel = anim.travelCurve.Evaluate(t);
        tablePosition = Vector3.LerpUnclamped(travelFrom, travelTo, travel);
        lockOffset = Vector3.Lerp(lockFrom, lockTo, travel);
        Tint = Color.Lerp(tintFrom, tintTo, travel);
        ApplyPose();

        float angle = Mathf.LerpUnclamped(startAngle, endAngle, anim.spinCurve.Evaluate(t));
        visual.localEulerAngles = new Vector3(0f, 0f, angle);

        float interval = Mathf.Max(anim.frameInterval, 0.01f);
        int step = Mathf.FloorToInt(t * anim.duration / interval);
        if (step == lastFrameStep) return;
        lastFrameStep = step;

        // The last frame before landing is the result, so the die lands on the face it keeps.
        bool lastFrame = anim.duration * (1f - t) <= interval;
        if (lastFrame && faces != null && faces.Length >= rollResult) SetSprite(faces[rollResult - 1]);
        else if (tumbleFrames != null && tumbleFrames.Length > 0) SetSprite(tumbleFrames[NextFrameIndex()]);
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
        lockOffset = lockTo;
        Tint = tintTo;
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

    private Color kindTint = Color.white;

    /// <summary>
    /// Makes it a <paramref name="kind"/> die, colored <paramref name="tint"/> (over the Image's own color), showing
    /// <paramref name="info"/> while hovered.
    /// </summary>
    public void SetKind(DiceKind kind, Color tint, CardFace info = null)
    {
        Kind = kind;
        kindTint = tint;
        Info = info;
        if (!initialized) return;

        baseColor = defaultColor * tint;
        tintTween.Stop();
        Color shown = IsLocked && manager ? baseColor * manager.lockStyle.lockedTint : baseColor;
        if (IsRolling) tintTo = shown;
        else Tint = shown;
    }

    // ---------------------------------------------------------------------
    // Card actions
    // ---------------------------------------------------------------------

    private const float FlipDuration = 0.18f;
    private const float RemoveDuration = 0.2f;

    /// <summary>Turns over to <paramref name="value"/>: it counts at once, the face swaps halfway through the turn.</summary>
    public void Flip(int value)
    {
        Value = Mathf.Clamp(value, 1, PokerDiceEvaluator.Faces);
        if (!isActiveAndEnabled)
        {
            ShowFace(Value);
            return;
        }

        punchTween.Stop();
        float half = FlipDuration * 0.5f;
        punchTween = Tween.ScaleX(visual, 0f, half, Ease.InQuad)
            .OnComplete(this, die =>
            {
                die.ShowFace(die.Value);
                die.punchTween = Tween.ScaleX(die.visual, die.restScale.x, half, Ease.OutQuad);
            });
    }

    /// <summary>Destroyed for the round: it shrinks away and leaves the table until <see cref="Restore"/>.</summary>
    public void Remove()
    {
        IsRemoved = true;
        Unhover();
        HideOutlineNow();
        if (lockedIndicator) lockedIndicator.SetActive(false);

        StopTweens();
        if (!isActiveAndEnabled)
        {
            gameObject.SetActive(false);
            return;
        }
        punchTween = Tween.Scale(visual, Vector3.zero, RemoveDuration, Ease.InBack)
            .OnComplete(this, die => die.gameObject.SetActive(false));
    }

    /// <summary>Back on the table after being destroyed (a new round).</summary>
    public void Restore()
    {
        if (!IsRemoved) return;
        IsRemoved = false;
        gameObject.SetActive(true);
        if (initialized) visual.localScale = restScale;
    }

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

    /// <summary>Locked before a roll: locked for the rest of the round, with the sealed outline (see <see cref="IsSealed"/>).</summary>
    public void Seal()
    {
        if (!IsLocked || IsSealed) return;
        IsSealed = true;
        RefreshOutline();
    }

    /// <summary>
    /// No longer locked (nor sealed), but left where the lock put it (no snap): the next <see cref="Roll"/> eases it out.
    /// </summary>
    public void Unlock()
    {
        IsLocked = false;
        IsSealed = false;
        if (lockedIndicator) lockedIndicator.SetActive(false);
        RefreshOutline();
    }

    /// <summary>Unlocked, at rest, ready for a new round. Keeps its face and its place on the table.</summary>
    public void ResetDice()
    {
        if (!initialized) return;
        StopTweens();
        IsLocked = false;
        IsSealed = false;
        RefreshOutline();
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
