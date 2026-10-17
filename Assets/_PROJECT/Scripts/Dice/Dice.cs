using System;
using PrimeTween;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One poker die. It only shows and animates itself; the <see cref="DiceManager"/> decides when it
/// rolls, what it lands on and whether it may be locked (clicking a die asks the manager).
///
/// <para>UI: the faces show on <see cref="image"/> (a raycast target, so the die can be clicked) under a
/// Canvas with a GraphicRaycaster. Animation runs on <see cref="visual"/> (the Image's transform by
/// default; offsets are in the canvas's local units). When the dice sit in a layout group, give each
/// die a plain root and put the Image on a child: the layout owns the root's position.</para>
/// </summary>
public class Dice : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private Image image;
    [Tooltip("What lifts, spins and scales. Defaults to the image / sprite renderer's transform.")]
    [SerializeField] private Transform visual;
    [Tooltip("Optional: shown while the die is locked (a pin, a frame).")]
    [SerializeField] private GameObject lockedIndicator;

    /// <summary>The face showing, 1..6.</summary>
    public int Value { get; private set; } = 1;

    public bool IsLocked { get; private set; }

    public bool IsRolling => rollTween.isAlive;

    private DiceManager manager;
    private bool initialized;
    private Vector3 restScale;
    private Vector3 restPosition;
    private Color baseColor = Color.white;
    private Vector3 lockOffset;

    private Tween rollTween;
    private Tween moveTween;
    private Tween punchTween;
    private Tween tintTween;

    // The roll in progress.
    private DiceRollAnimation anim;
    private Sprite[] faces;
    private Sprite[] tumbleFrames;
    private int rollResult;
    private float startAngle;
    private float endAngle;
    private int lastFrameStep;
    private int lastFrameIndex = -1;
    private Action<Dice> onLanded;

    /// <summary>Called by the manager before the die is used. Remembers the rest pose.</summary>
    public void Init(DiceManager owner)
    {
        manager = owner;
        if (initialized) return;
        initialized = true;

        if (!image) image = GetComponentInChildren<Image>(true);
        if (!visual) visual = image ? image.transform : transform;
        CheckSetup();

        restScale = visual.localScale;
        restPosition = visual.localPosition;
        baseColor = Tint;
        if (lockedIndicator) lockedIndicator.SetActive(false);
    }

    private void OnDisable() => StopTweens();

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (manager) manager.ToggleLock(this);
    }

    // ---------------------------------------------------------------------
    // Rolling
    // ---------------------------------------------------------------------

    /// <summary>
    /// Throws the die: it lifts (scales up), tumbles through <paramref name="frames"/> while spinning,
    /// and falls back onto <paramref name="result"/>. <paramref name="landed"/> runs once it is down.
    /// </summary>
    public void Roll(int result, DiceRollAnimation animation, Sprite[] faceSprites, Sprite[] frames, float delay,
        Action<Dice> landed)
    {
        StopTweens();
        SnapToRest();

        anim = animation;
        faces = faceSprites;
        tumbleFrames = frames != null && frames.Length > 0 ? frames : faceSprites;
        rollResult = Mathf.Clamp(result, 1, PokerDiceEvaluator.Faces);
        onLanded = landed;

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
        visual.localPosition = RestPosition + Vector3.up * (anim.liftHeight * height);

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
        visual.localPosition = RestPosition;
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

        lockOffset = locked ? (Vector3)style.offset : Vector3.zero;
        Color tint = locked ? baseColor * style.lockedTint : baseColor;

        moveTween.Stop();
        tintTween.Stop();
        if (style.duration > 0f && isActiveAndEnabled)
        {
            moveTween = Tween.LocalPosition(visual, RestPosition, style.duration, Ease.OutQuad);
            tintTween = Tween.Custom(this, Tint, tint, style.duration, (die, c) => die.Tint = c);
        }
        else
        {
            visual.localPosition = RestPosition;
            Tint = tint;
        }
    }

    /// <summary>Unlocked, at rest, ready for a new round. Keeps the face it shows.</summary>
    public void ResetDice()
    {
        if (!initialized) return;
        StopTweens();
        IsLocked = false;
        lockOffset = Vector3.zero;
        if (lockedIndicator) lockedIndicator.SetActive(false);
        Tint = baseColor;
        SnapToRest();
        visual.localEulerAngles = Vector3.zero;
    }

    /// <summary>Stops a roll in flight (the round was cancelled). The landing callback is dropped.</summary>
    public void StopRoll()
    {
        onLanded = null;
        rollTween.Stop();
        if (initialized) SnapToRest();
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private Vector3 RestPosition => restPosition + lockOffset;

    private void SnapToRest()
    {
        visual.localScale = restScale;
        visual.localPosition = RestPosition;
    }

    private void StopTweens()
    {
        rollTween.Stop();
        moveTween.Stop();
        punchTween.Stop();
        tintTween.Stop();
    }

    /// <summary>Warns about UI setups where the die can't be clicked or the layout would fight the animation.</summary>
    private void CheckSetup()
    {
        if (!image) h.Out($"Dice '{name}': no Image to show the faces on.");
        else if (!image.raycastTarget) h.Out($"Dice '{name}': its Image isn't a raycast target, so it can't be clicked to lock.");

        // A layout group positions its children: it would snap the die's root back every rebuild
        // (and its rest position isn't known yet when the die initializes).
        if (visual == transform && transform.parent && transform.parent.GetComponent<LayoutGroup>())
            h.Out($"Dice '{name}': it sits in a layout group, so put its Image on a child object - " +
                  "the layout owns the root's position, and lifting / locking would be overridden.");
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
