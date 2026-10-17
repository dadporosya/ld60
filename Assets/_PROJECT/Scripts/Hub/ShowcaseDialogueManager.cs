using System;
using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// "Look at this" dialogues for hub objects: the fade image darkens the screen a bit, the object's picture
/// appears in the middle of the screen, and a regular dialogue (<see cref="DialogueContainer"/>, played by the
/// <see cref="DialogueManager"/>) runs over it. When the dialogue ends, the picture and the fade go away.
/// Access via <c>ShowcaseDialogueManager.Instance</c>; hub objects use it through <see cref="ShowcaseInteractable"/>.
///
/// <para>Keep this component OUTSIDE <see cref="root"/> (the root is disabled between showcases).</para>
/// </summary>
public class ShowcaseDialogueManager : MonoBehaviour
{
    public static ShowcaseDialogueManager Instance;

    public enum SpriteFit
    {
        [Tooltip("The showcase image keeps its own size; the sprite is fitted inside it, keeping its aspect.")]
        KeepImageRect,
        [Tooltip("The image takes the sprite's own size, scaled down to fit Max Size.")]
        NativeSizeFitted,
    }

    [Header("Showcase")]
    [Tooltip("Enabled while a showcase is up, disabled after.")]
    [SerializeField] private GameObject root;
    [Tooltip("Full-screen image that darkens the screen (and blocks hub clicks behind it).")]
    [SerializeField] private Image fadeImage;
    [SerializeField, Range(0f, 1f)] private float fadeAlpha = 0.6f;
    [SerializeField, Min(0f)] private float fadeDuration = 0.35f;
    [Tooltip("The image in the middle of the screen that shows the object's sprite.")]
    [SerializeField] private Image showcaseImage;
    [SerializeField] private SpriteFit spriteFit = SpriteFit.KeepImageRect;
    [Tooltip("Native Size Fitted only: the largest the image may get (UI units).")]
    [SerializeField] private Vector2 maxSize = new Vector2(600f, 600f);

    [Header("Animation")]
    [Tooltip("The picture grows from this scale as it appears.")]
    [SerializeField, Min(0f)] private float appearFromScale = 0.85f;
    [SerializeField, Min(0f)] private float appearDuration = 0.35f;
    [SerializeField] private Ease appearEase = Ease.OutBack;
    [SerializeField, Min(0f)] private float disappearDuration = 0.25f;
    [Tooltip("Seconds after the picture has appeared before the dialogue starts.")]
    [SerializeField, Min(0f)] private float dialogueDelay = 0.1f;

    [Header("Dialogue")]
    [Tooltip("Defaults to DialogueManager.Instance.")]
    [SerializeField] private DialogueManager dialogueManager;
    [Tooltip("Optional speaker for nodes that don't name one (its name, portrait, voice).")]
    [SerializeField] private Talkable defaultSpeaker;

    [Header("Audio")]
    [SerializeField] private AudioClip appearSfx;

    [Header("Events")]
    public UnityEvent onShowcaseStarted = new UnityEvent();
    public UnityEvent onShowcaseEnded = new UnityEvent();

    /// <summary>True from <see cref="Show"/> until the picture and fade are gone.</summary>
    public bool IsShowing { get; private set; }

    private DialogueManager Dialogues => dialogueManager ? dialogueManager : dialogueManager = DialogueManager.Instance;

    private bool initialized;
    private Color imageColor = Color.white;
    private Vector3 imageScale = Vector3.one;
    private Action finishedCallback;
    private DialogueContainer runningDialogue;
    private UnityAction onDialogueEnded;
    private Tween stepTween;

    // The showcase picture's frame animation.
    private List<Sprite> animFrames = new List<Sprite>();
    private List<float> animDurations = new List<float>();
    private bool animLoop;
    private float animTime;
    private int animFrame;

    private void Awake() => Initialize();

    private void Initialize()
    {
        if (initialized) return;
        initialized = true;

        h.CreateStaticInstance(this, ref Instance, setDontDestroy: false);
        onDialogueEnded = OnDialogueEnded;

        if (showcaseImage)
        {
            imageColor = showcaseImage.color;
            imageScale = showcaseImage.transform.localScale;
            showcaseImage.raycastTarget = false;
        }
        if (fadeImage) SetAlpha(fadeImage, 0f);
        if (root && !IsShowing) root.SetActive(false);
    }

    private void Update()
    {
        if (!IsShowing || !showcaseImage || animFrames.Count < 2) return;

        // Unscaled, like the rest of the showcase. A frame with no time of its own holds the animation.
        animTime += Time.unscaledDeltaTime;
        int start = animFrame;
        while (animDurations[animFrame] > 0f && animTime >= animDurations[animFrame])
        {
            bool last = animFrame == animFrames.Count - 1;
            if (last && !animLoop) break;

            animTime -= animDurations[animFrame];
            animFrame = last ? 0 : animFrame + 1;
            if (animFrame == start)
            {
                // A whole lap in one frame (a hitch): drop the rest instead of spinning.
                animTime = 0f;
                break;
            }
        }

        if (showcaseImage.sprite != animFrames[animFrame]) showcaseImage.sprite = animFrames[animFrame];
    }

    private void OnDisable()
    {
        if (dialogueManager && onDialogueEnded != null) dialogueManager.onDialogueEnd.RemoveListener(onDialogueEnded);
    }

    /// <summary>
    /// Shows <paramref name="sprite"/> over the darkened screen and plays <paramref name="dialogue"/>;
    /// <paramref name="onFinished"/> runs once it's all gone again. Without a dialogue the picture closes again
    /// right after appearing. Returns false (no callback) when a showcase is already up.
    /// </summary>
    public bool Show(Sprite sprite, DialogueContainer dialogue, Talkable speaker = null, Action onFinished = null) =>
        Show(sprite ? new[] { sprite } : null, 0f, true, dialogue, speaker, onFinished);

    /// <summary>
    /// Like <see cref="Show(Sprite, DialogueContainer, Talkable, Action)"/>, with the picture animated: it plays
    /// <paramref name="frames"/> at <paramref name="framesPerSecond"/> for as long as the showcase is up -
    /// looping, or stopping on the last frame. A single frame is a still picture.
    /// </summary>
    public bool Show(IReadOnlyList<Sprite> frames, float framesPerSecond, bool loop, DialogueContainer dialogue,
        Talkable speaker = null, Action onFinished = null)
    {
        float duration = framesPerSecond > 0f ? 1f / framesPerSecond : 0f;
        List<float> durations = new List<float>();
        if (frames != null)
            for (int i = 0; i < frames.Count; i++) durations.Add(duration);
        return Show(frames, durations, loop, dialogue, speaker, onFinished);
    }

    /// <summary>
    /// Like <see cref="Show(IReadOnlyList{Sprite}, float, bool, DialogueContainer, Talkable, Action)"/>, with each
    /// frame shown for its own number of seconds (<paramref name="frameDurations"/>[i]; 0 or missing frames
    /// don't advance).
    /// </summary>
    public bool Show(IReadOnlyList<Sprite> frames, IReadOnlyList<float> frameDurations, bool loop,
        DialogueContainer dialogue, Talkable speaker = null, Action onFinished = null)
    {
        Initialize();

        // Empty frames are dropped together with their durations, so the timing stays with its frame.
        List<Sprite> valid = new List<Sprite>();
        List<float> validDurations = new List<float>();
        if (frames != null)
            for (int i = 0; i < frames.Count; i++)
            {
                if (!frames[i]) continue;
                valid.Add(frames[i]);
                validDurations.Add(frameDurations != null && i < frameDurations.Count ? frameDurations[i] : 0f);
            }
        Sprite sprite = valid.Count > 0 ? valid[0] : null;

        if (IsShowing)
        {
            h.Out("ShowcaseDialogueManager: a showcase is already up.");
            return false;
        }

        if (!Dialogues)
        {
            h.Out("ShowcaseDialogueManager: no DialogueManager in the scene.");
            onFinished?.Invoke();
            return false;
        }

        IsShowing = true;
        finishedCallback = onFinished;
        if (root) root.SetActive(true);

        if (fadeImage)
        {
            fadeImage.gameObject.SetActive(true);
            fadeImage.raycastTarget = true;
            Tween.StopAll(fadeImage);
            Tween.Alpha(fadeImage, fadeAlpha, fadeDuration, useUnscaledTime: true);
        }

        if (showcaseImage)
        {
            showcaseImage.gameObject.SetActive(sprite);
            showcaseImage.sprite = sprite;
            // Sized by the first frame; the others are drawn in the same rect.
            FitSprite(sprite);

            animFrames = valid;
            animDurations = validDurations;
            animLoop = loop;
            animTime = 0f;
            animFrame = 0;

            Tween.StopAll(showcaseImage);
            Tween.StopAll(showcaseImage.transform);
            Color hidden = imageColor;
            hidden.a = 0f;
            showcaseImage.color = hidden;
            showcaseImage.transform.localScale = imageScale * appearFromScale;
            Tween.Alpha(showcaseImage, imageColor.a, appearDuration * 0.7f, useUnscaledTime: true);
            Tween.Scale(showcaseImage.transform, imageScale, appearDuration, appearEase, useUnscaledTime: true);
        }

        if (appearSfx && SFXManager.Instance) SFXManager.Instance.PlayClip(appearSfx);
        onShowcaseStarted?.Invoke();

        stepTween.Stop();
        stepTween = Tween.Delay(this, appearDuration + dialogueDelay, manager => manager.StartDialogue(dialogue, speaker),
            useUnscaledTime: true);
        return true;
    }

    /// <summary>Closes the showcase now (its dialogue is ended too). The callback is dropped.</summary>
    public void Cancel()
    {
        if (!IsShowing) return;
        finishedCallback = null;
        stepTween.Stop();
        StopListening();

        if (runningDialogue && Dialogues && Dialogues.DialogueActive) Dialogues.EndDialogue(animate: false);
        DestroyRunningDialogue();
        Hide(animate: false);
    }

    private void StartDialogue(DialogueContainer dialogue, Talkable speaker)
    {
        if (!dialogue)
        {
            Hide(animate: true);
            return;
        }

        // A runtime copy: filling in speakers writes into the nodes, which would otherwise end up in the asset.
        DialogueContainer instance = Instantiate(dialogue);
        instance.name = dialogue.name;
        Talkable source = speaker ? speaker : defaultSpeaker;
        if (source) source.InitDialogue(instance);
        else
        {
            instance.AssignNodeIds();
            foreach (DialogueNode node in instance.nodes) node?.Init();
        }

        runningDialogue = instance;
        if (Dialogues.onDialogueEnd == null) Dialogues.onDialogueEnd = new UnityEvent();
        Dialogues.onDialogueEnd.RemoveListener(onDialogueEnded);
        Dialogues.onDialogueEnd.AddListener(onDialogueEnded);
        Dialogues.StartDialogue(instance, source);
    }

    private void OnDialogueEnded()
    {
        StopListening();
        DestroyRunningDialogue();
        Hide(animate: true);
    }

    private void Hide(bool animate)
    {
        stepTween.Stop();

        if (!animate || disappearDuration <= 0f)
        {
            FinishHide();
            return;
        }

        if (fadeImage)
        {
            Tween.StopAll(fadeImage);
            Tween.Alpha(fadeImage, 0f, disappearDuration, useUnscaledTime: true);
        }
        if (showcaseImage)
        {
            Tween.StopAll(showcaseImage);
            Tween.StopAll(showcaseImage.transform);
            Tween.Alpha(showcaseImage, 0f, disappearDuration, useUnscaledTime: true);
            Tween.Scale(showcaseImage.transform, imageScale * appearFromScale, disappearDuration, Ease.InQuad, useUnscaledTime: true);
        }

        stepTween = Tween.Delay(this, disappearDuration, manager => manager.FinishHide(), useUnscaledTime: true);
    }

    private void FinishHide()
    {
        if (fadeImage)
        {
            Tween.StopAll(fadeImage);
            SetAlpha(fadeImage, 0f);
        }
        if (showcaseImage)
        {
            Tween.StopAll(showcaseImage);
            Tween.StopAll(showcaseImage.transform);
            showcaseImage.transform.localScale = imageScale;
            showcaseImage.color = imageColor;
        }
        if (root) root.SetActive(false);

        IsShowing = false;
        onShowcaseEnded?.Invoke();

        Action callback = finishedCallback;
        finishedCallback = null;
        callback?.Invoke();
    }

    private void FitSprite(Sprite sprite)
    {
        if (!sprite) return;

        if (spriteFit == SpriteFit.KeepImageRect)
        {
            showcaseImage.preserveAspect = true;
            return;
        }

        showcaseImage.SetNativeSize();
        RectTransform rect = showcaseImage.rectTransform;
        Vector2 size = rect.sizeDelta;
        float scale = Mathf.Min(1f, maxSize.x / Mathf.Max(1f, size.x), maxSize.y / Mathf.Max(1f, size.y));
        rect.sizeDelta = size * scale;
    }

    private void StopListening()
    {
        if (Dialogues && onDialogueEnded != null) Dialogues.onDialogueEnd.RemoveListener(onDialogueEnded);
    }

    private void DestroyRunningDialogue()
    {
        if (runningDialogue) Destroy(runningDialogue);
        runningDialogue = null;
    }

    private static void SetAlpha(Graphic graphic, float alpha)
    {
        Color c = graphic.color;
        c.a = alpha;
        graphic.color = c;
    }
}
