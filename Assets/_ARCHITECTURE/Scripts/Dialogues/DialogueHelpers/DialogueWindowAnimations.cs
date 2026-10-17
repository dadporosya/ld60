using System.Collections;
using UnityEngine;
using PrimeTween;
using UnityEngine.Serialization;

// Open/close animation for the dialogue window. The window scales up from zero in two
// beats (X first with a sliver of Y, then Y to full) on start, and reverses on end. Scaling is about
// the pivot, which is moved to the window's top edge (pivotOnTopEdge) so the window unrolls downward.
// DialogueManager drives these: it waits for OnDialogueStart before showing text, and
// runs OnDialogueEnd alongside tearing the dialogue down (see DialogueManager).
public class DialogueWindowAnimations : MonoBehaviour
{
    public DialogueManager dialogueManager;

    [Header("Pivot")]
    [Tooltip("On Awake, moves the window's pivot to its top edge without moving the window, so the open " +
             "grows down from the top and the typing bounce lifts the bottom edge.")]
    [SerializeField] private bool pivotOnTopEdge = true;

    [Header("Timing")]
    [Tooltip("Duration of the X open/close beat.")]
    [SerializeField] private float xDuration = 0.18f;
    [Tooltip("Duration of the Y open/close beat.")]
    [SerializeField] private float yDuration = 0.18f;
    [Tooltip("How far Y opens while X expands, before the full Y beat. 0..1.")]
    [SerializeField] private float ySliver = 0.1f;
    [SerializeField] private Ease openEase = Ease.OutBack;
    [SerializeField] private Ease closeEase = Ease.InBack;
    [SerializeField] private float delayBeforeStart = 0.5f;
    [SerializeField] private AudioClip openSfx;

    [Tooltip("Played when the window closes. Left empty, the Windows XP minimize sound is loaded from " +
             "Resources instead; nothing is played when that is missing too.")]
    [SerializeField] private AudioClip closeSfx;

    // Where the close sound used to be pulled from, kept as the fallback for a prefab that has no
    // closeSfx assigned. SFXManager.PlayClip(string) no-ops when nothing is at the path.
    private const string CloseSfxFallbackPath = "PROJECT/Audio/WinXP/Windows XP Minimize";

    [Header("Typing starts: height bounce")]
    [Tooltip("When a node starts typing, the window's rect gets a little shorter and grows back (the edge " +
             "opposite its pivot moves).")]
    [SerializeField] private bool typingStartBounce = true;
    [Tooltip("Canvas units the window's height shrinks by.")]
    [FormerlySerializedAs("bounceRise")]
    [SerializeField, Min(0f)] private float bounceShrink = 12f;
    [SerializeField, Min(0f)] private float bounceUpDuration = 0.08f;
    [SerializeField, Min(0f)] private float bounceBackDuration = 0.3f;
    [SerializeField] private Ease bounceUpEase = Ease.OutQuad;
    [SerializeField] private Ease bounceBackEase = Ease.OutBack;

    [Header("Typing interrupted: shake")]
    [Tooltip("When the player finishes the line early (advance pressed while typing), the window dips down and comes back.")]
    [SerializeField] private bool interruptShake = true;
    [Tooltip("Canvas units the window dips.")]
    [SerializeField, Min(0f)] private float shakeDrop = 8f;
    [SerializeField, Min(0f)] private float shakeDownDuration = 0.05f;
    [SerializeField, Min(0f)] private float shakeBackDuration = 0.25f;
    [SerializeField] private Ease shakeDownEase = Ease.OutCubic;
    [SerializeField] private Ease shakeBackEase = Ease.OutBack;

    private Transform Window =>
        dialogueManager && dialogueManager.dialogueWindow
            ? dialogueManager.dialogueWindow.transform
            : null;

    // The accents (bounce, shake) are applied as offsets ON TOP of whatever else moves or sizes the
    // window (DialogueWindowCollapse slides its anchoredPosition), so both can run at once: only the
    // change in offset is written each frame. Their tweens target this component, not the window, so
    // the open/close Tween.StopAll(window) doesn't cut them off with an offset left applied.
    private Tween bounce;
    private Tween shake;
    private float bounceOffset;
    private float shakeOffset;
    private float appliedOffsetY;
    private float appliedHeightDelta;

    // Awake, not Start: DialogueWindowCollapse records the window's resting anchoredPosition in its Start,
    // and that has to be measured with the pivot already moved.
    private void Awake()
    {
        if (!dialogueManager) dialogueManager = GetComponent<DialogueManager>();
        if (!dialogueManager) dialogueManager = FindObjectOfType<DialogueManager>();

        if (pivotOnTopEdge && Window is RectTransform w) SetPivotY(w, 1f);
    }

    // Moving the pivot alone would drag the rect with it, so the anchored position is shifted back by the
    // same distance. Uses the unscaled size: the window rests at scale 1.
    private static void SetPivotY(RectTransform rect, float pivotY)
    {
        float shift = (pivotY - rect.pivot.y) * rect.rect.height;
        rect.pivot = new Vector2(rect.pivot.x, pivotY);
        rect.anchoredPosition += new Vector2(0f, shift);
    }

    /// <summary>A node begins typing: the window's rect gets a little shorter and grows back.</summary>
    public void PlayTypingStart()
    {
        if (!typingStartBounce || bounceShrink <= 0f || !(Window is RectTransform w) || !w.gameObject.activeInHierarchy) return;

        bounce.Stop();
        bounce = PlayAccent(bounceShrink, bounceUpDuration, bounceBackDuration, bounceUpEase, bounceBackEase,
            (self, value) => self.SetBounce(value));
    }

    /// <summary>The player cut the typing short: the window dips down a bit and comes back.</summary>
    public void PlayTypingInterrupted()
    {
        if (!interruptShake || shakeDrop <= 0f || !(Window is RectTransform w) || !w.gameObject.activeInHierarchy) return;

        shake.Stop();
        shake = PlayAccent(shakeDrop, shakeDownDuration, shakeBackDuration, shakeDownEase, shakeBackEase,
            (self, value) => self.SetShake(value));
    }

    /// <summary>Cuts the bounce and the shake short, putting the window back where they found it.</summary>
    public void StopAccents()
    {
        bounce.Stop();
        shake.Stop();
        bounceOffset = 0f;
        shakeOffset = 0f;
        ApplyAccentOffset();
        ApplyBounceHeight();
    }

    // 0 → amount over outDuration, then back to 0 over backDuration, each leg with its own ease.
    // One linear tween sampled piecewise, so a single handle stops the whole thing. Unscaled: dialogues
    // also run over a paused game.
    private Tween PlayAccent(float amount, float outDuration, float backDuration, Ease outEase, Ease backEase,
        System.Action<DialogueWindowAnimations, float> apply)
    {
        float total = outDuration + backDuration;
        if (total <= 0f)
        {
            apply(this, 0f);
            return default;
        }

        float split = outDuration / total;
        return Tween.Custom(this, 0f, 1f, total, (self, t) =>
        {
            float value = t < split
                ? amount * Easing.Evaluate(t / split, outEase)
                : amount * (1f - Easing.Evaluate((t - split) / (1f - split), backEase));
            apply(self, value);
        }, Ease.Linear, useUnscaledTime: true);
    }

    private void SetBounce(float value)
    {
        bounceOffset = value;
        ApplyBounceHeight();
    }

    private void SetShake(float value)
    {
        shakeOffset = value;
        ApplyAccentOffset();
    }

    private void ApplyBounceHeight()
    {
        if (!(Window is RectTransform w))
        {
            appliedHeightDelta = 0f;
            return;
        }

        float target = -bounceOffset;
        if (Mathf.Approximately(target, appliedHeightDelta)) return;

        Vector2 size = w.sizeDelta;
        size.y += target - appliedHeightDelta;
        w.sizeDelta = size;
        appliedHeightDelta = target;

        // The text inside is resized with the window, and TMP rebuilding its mesh for the new rect resets
        // every letter's alpha — the line being typed would flash in whole until the typewriter's next
        // update. Rebuild now and have the typewriter hide the letters it hasn't reached yet, before the
        // frame renders.
        if (dialogueManager && (dialogueManager.IsTyping || dialogueManager.IsPausedAtStop))
        {
            Canvas.ForceUpdateCanvases();
            dialogueManager.RefreshTypingText();
        }
    }

    private void ApplyAccentOffset()
    {
        if (!(Window is RectTransform w))
        {
            appliedOffsetY = 0f;
            return;
        }

        float target = -shakeOffset;
        Vector2 position = w.anchoredPosition;
        position.y += target - appliedOffsetY;
        w.anchoredPosition = position;
        appliedOffsetY = target;
    }

    // Grow from zero: X to 1 with a sliver of Y, then Y to full.
    public IEnumerator OnDialogueStart()
    {
        Transform w = Window;
        if (!w) yield break;

        StopAccents();
        Tween.StopAll(w);
        w.localScale = Vector3.zero;
        
        SFXManager.Instance.PlayClip(
            openSfx
            );
        
        yield return Tween.Scale(w, new Vector3(1f, ySliver, 1f), xDuration, openEase)
            .ToYieldInstruction();
        yield return Tween.Scale(w, Vector3.one, yDuration, openEase)
            .ToYieldInstruction();

        yield return new WaitForSeconds(delayBeforeStart);
        
        w.localScale = Vector3.one;
    }

    // Opposite of the open: Y collapses to a sliver, then everything to zero.
    public IEnumerator OnDialogueEnd()
    {
        Transform w = Window;
        if (!w) yield break;

        StopAccents();
        Tween.StopAll(w);

        if (SFXManager.Instance)
        {
            if (closeSfx) SFXManager.Instance.PlayClip(closeSfx);
            else SFXManager.Instance.PlayClip(CloseSfxFallbackPath);
        }
        
        yield return Tween.Scale(w, new Vector3(1f, ySliver, 1f), yDuration, closeEase)
            .ToYieldInstruction();
        yield return Tween.Scale(w, Vector3.zero, xDuration, closeEase)
            .ToYieldInstruction();
        
        w.localScale = Vector3.zero;
    }
}
