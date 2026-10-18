using System;
using System.Collections;
using System.Collections.Generic;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The game's opening: a shaking content warning on black (<see cref="showWarning"/>) that fades out completely before
/// the intro visual novel dialogue fades in, then the <see cref="FadeImageController"/> covers
/// the stage with black and fades away again, revealing the hub.
///
/// <para>Put it on its OWN GameObject (or a prefab): <see cref="CutSceneBase.Run"/> clones the whole
/// GameObject it sits on. <see cref="FuneralGameManager"/> plays it through <see cref="Play"/>; with
/// <c>runOnStart</c> it plays by itself instead.</para>
///
/// <para>Cutting the dialogue off (<see cref="HubDialogueRunner.Interrupt"/>, e.g. a debug round) ends the
/// cutscene on the spot, without the fades and without the callback - like the plain intro dialogue.</para>
/// </summary>
public class IntroCutscene : CutSceneBase
{
    [Header("Warning")]
    [Tooltip("The cutscene opens on a black frame with the warning text before the dialogue.")]
    [SerializeField] private bool showWarning = true;
    [SerializeField, TextArea] private string warningText = "This game raise heavy topics. Take care";
    [Tooltip("Seconds the warning stays up, unless a key / click skips it.")]
    [SerializeField, Min(0f)] private float warningDuration = 6f;
    [Tooltip("Seconds after the warning comes up before a key / click can skip it (so a stray press doesn't).")]
    [SerializeField, Min(0f)] private float warningSkipLockout = 0.5f;
    [Tooltip("Seconds the warning text fades away over. The screen stays black until it's gone.")]
    [SerializeField, Min(0f)] private float warningFadeOut = 0.5f;
    [Tooltip("Seconds the black fades away over once the warning is gone, fading the dialogue in.")]
    [SerializeField, Min(0f)] private float dialogueFadeIn = 1f;
    [SerializeField] private Ease dialogueFadeInEase = Ease.Linear;
    [Tooltip("Shake on the warning text (TextAnimation). 0: no shake.")]
    [SerializeField, Min(0f)] private float warningShakeStrength = 3f;
    [SerializeField, Min(0f)] private float warningShakeSpeed = 25f;
    [Tooltip("Sorting order of the warning's runtime canvas. Keep it above everything else.")]
    [SerializeField] private int warningSortingOrder = 1000;
    [Tooltip("The camera the warning's Screen Space - Camera canvas renders through, so post-processing applies to " +
             "it like the rest of the UI. Empty: the main camera. No camera at all: an overlay canvas, unprocessed.")]
    [SerializeField] private Camera warningCamera;
    [Tooltip("How far in front of the camera the warning's canvas sits. Keep it closer than the other " +
             "Screen Space - Camera canvases.")]
    [SerializeField, Min(0.01f)] private float warningPlaneDistance = 1f;
    [Tooltip("Empty: TextMeshPro's default font.")]
    [SerializeField] private TMP_FontAsset warningFont;
    [SerializeField, Min(1f)] private float warningFontSize = 48f;
    [SerializeField] private Color warningColor = Color.white;

    [Header("Dialogue")]
    [SerializeField] private VisualNovelDialogueContainer dialogue;

    [Header("Fade")]
    [Tooltip("Seconds the dialogue stage fades to black over once the dialogue is over.")]
    [SerializeField, Min(0f)] private float fadeInDuration = 1f;
    [Tooltip("Seconds the screen stays black (the stage is hidden behind it).")]
    [SerializeField, Min(0f)] private float blackHoldDuration = 0.5f;
    [Tooltip("Seconds the black fades away over, revealing the hub.")]
    [SerializeField, Min(0f)] private float fadeOutDuration = 1.5f;
    [SerializeField] private Ease fadeInEase = Ease.Linear;
    [SerializeField] private Ease fadeOutEase = Ease.Linear;

    // Set on the running clone by Play; called once the hub is revealed.
    private Action onFinished;

    // The warning's black screen, kept up (text faded out) until the dialogue starts under it.
    private CanvasGroup warning;

    /// <summary>Runs the cutscene, then <paramref name="finished"/> once the hub is back on screen.</summary>
    public void Play(Action finished)
    {
        IntroCutscene instance = RunInstance() as IntroCutscene;
        if (instance) instance.onFinished = finished;
        else finished?.Invoke();
    }

    public override void Init()
    {
        base.Init();
        cutsceneSteps.Clear();
        cutsceneSteps.Add(WarningStep());
        cutsceneSteps.Add(DialogueStep());
        cutsceneSteps.Add(FadeStep());
    }

    private IEnumerator WarningStep()
    {
        if (!showWarning) yield break;

        warning = MakeWarning(out CanvasGroup warningText);

        float elapsed = 0f;
        while (elapsed < warningDuration)
        {
            if (elapsed >= warningSkipLockout && AnyPressThisFrame())
            {
                // Let the press pass, so it doesn't also advance the dialogue that starts next.
                yield return null;
                break;
            }
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }

        // The text fades out completely on the black; the dialogue only starts (and fades in) after.
        if (warningFadeOut <= 0f) warningText.alpha = 0f;
        else yield return Tween.Alpha(warningText, 0f, warningFadeOut, Ease.Linear, useUnscaledTime: true)
            .ToYieldInstruction();
    }

    /// <summary>Fades the warning's black away (the dialogue fading in under it), then removes it.</summary>
    private void RevealDialogue()
    {
        if (!warning) return;

        GameObject warningObject = warning.gameObject;
        if (dialogueFadeIn <= 0f)
        {
            Destroy(warningObject);
            return;
        }
        // It goes with the cutscene if the dialogue gets cut off.
        Tween.Alpha(warning, 0f, dialogueFadeIn, dialogueFadeInEase, useUnscaledTime: true)
            .OnComplete(warningObject, Destroy);
    }

    private static bool AnyPressThisFrame()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.anyKey.wasPressedThisFrame) return true;

        Mouse mouse = Mouse.current;
        if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame ||
                              mouse.middleButton.wasPressedThisFrame)) return true;

        Gamepad gamepad = Gamepad.current;
        return gamepad != null && (gamepad.buttonSouth.wasPressedThisFrame || gamepad.startButton.wasPressedThisFrame);
    }

    /// <summary>
    /// A Screen Space - Camera canvas on this cutscene (post-processed like the rest of the UI): full-screen black,
    /// the warning text centred on it, shaking.
    /// <paramref name="textGroup"/> fades the text alone; the returned group fades the whole screen.
    /// </summary>
    private CanvasGroup MakeWarning(out CanvasGroup textGroup)
    {
        GameObject go = new GameObject("Warning Canvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        go.transform.SetParent(transform, false);

        // Through the camera, so the post-processing volumes apply to it too.
        Canvas canvas = go.GetComponent<Canvas>();
        Camera cam = warningCamera ? warningCamera : Camera.main;
        if (cam)
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = Mathf.Max(warningPlaneDistance, cam.nearClipPlane + 0.01f);
        }
        else
        {
            h.Out("IntroCutscene: no camera for the warning, it's drawn as an overlay without post-processing.");
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }
        canvas.sortingOrder = warningSortingOrder;

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Blocks clicks into whatever is behind it, too.
        GameObject black = new GameObject("Black", typeof(RectTransform), typeof(Image));
        black.transform.SetParent(go.transform, false);
        Stretch((RectTransform)black.transform, Vector2.zero);
        black.GetComponent<Image>().color = Color.black;

        // Faded through its own group: TextAnimation rewrites the vertex colors every frame.
        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasGroup));
        textObject.transform.SetParent(go.transform, false);
        textGroup = textObject.GetComponent<CanvasGroup>();
        Stretch((RectTransform)textObject.transform, new Vector2(-200f, -200f));
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        if (warningFont) text.font = warningFont;
        text.fontSize = warningFontSize;
        text.color = warningColor;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.text = warningText;

        if (warningShakeStrength > 0f)
        {
            TextAnimation animation = textObject.AddComponent<TextAnimation>();
            animation.animationTarget = TextAnimation.AnimationTarget.WholeText;
            animation.animations = new List<TextAnimation.AnimationType> { TextAnimation.AnimationType.Shake };
            animation.shakeStrength = warningShakeStrength;
            animation.shakeSpeed = warningShakeSpeed;
        }

        return go.GetComponent<CanvasGroup>();
    }

    private static void Stretch(RectTransform rect, Vector2 sizeDelta)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = sizeDelta;
    }

    private IEnumerator DialogueStep()
    {
        HubDialogueRunner runner = HubDialogueRunner.Instance;
        if (!runner || !dialogue)
        {
            RevealDialogue();
            yield break;
        }

        bool done = false;
        // The stage stays up so the black covers it, rather than the hub flashing in between.
        runner.Play(dialogue, () => done = true, hideStage: false);
        RevealDialogue();

        // The stage's own end fade (Fade To Color On End) still runs after the dialogue is over: only a dropped
        // callback means it was cut off.
        while (!done && (runner.IsPlaying || runner.IsFinishing)) yield return null;

        if (!done)
        {
            // Interrupted: its callback was dropped, so nothing else of the intro plays.
            h.Out("IntroCutscene: dialogue was cut off, cutscene ends.");
            DestroyCutscene();
            StopAllCoroutines();
        }
    }

    private IEnumerator FadeStep()
    {
        FadeImageController fade = FadeImageController.Instance;
        HubDialogueRunner runner = HubDialogueRunner.Instance;

        if (!fade)
        {
            if (runner) runner.HideStage();
            Finish();
            yield break;
        }

        yield return Fade(fade, 1f, fadeInDuration, fadeInEase);
        if (runner) runner.HideStage();

        if (blackHoldDuration > 0f) yield return new WaitForSecondsRealtime(blackHoldDuration);

        yield return Fade(fade, 0f, fadeOutDuration, fadeOutEase);
        Finish();
    }

    private static IEnumerator Fade(FadeImageController fade, float alpha, float duration, Ease ease)
    {
        if (duration <= 0f)
        {
            fade.SetAlpha(alpha);
            yield break;
        }

        Tween tween = alpha > 0f
            ? fade.FadeIn(duration, ease, useUnscaledTime: true)
            : fade.Fadeout(duration, ease, useUnscaledTime: true);
        yield return tween.ToYieldInstruction();
    }

    private void Finish()
    {
        Action callback = onFinished;
        onFinished = null;
        callback?.Invoke();
    }
}
