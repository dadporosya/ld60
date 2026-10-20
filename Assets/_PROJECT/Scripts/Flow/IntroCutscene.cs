using System;
using System.Collections;
using System.Collections.Generic;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The game's opening: on black, the player picks the game's language (<see cref="chooseLanguage"/>), then a
/// shaking content warning (<see cref="showWarning"/>) fades out completely before the intro visual novel dialogue
/// fades in; then the <see cref="FadeImageController"/> covers the stage with black and fades away again, revealing
/// the hub.
///
/// <para>Put it on its OWN GameObject (or a prefab): <see cref="CutSceneBase.Run"/> clones the whole
/// GameObject it sits on. <see cref="FuneralGameManager"/> plays it through <see cref="Play"/>; with
/// <c>runOnStart</c> it plays by itself instead.</para>
///
/// <para>Cutting the dialogue off (<see cref="HubDialogueRunner.Interrupt"/>, e.g. a debug round) ends the
/// cutscene on the spot, without the fades and without the callback - like the plain intro dialogue.</para>
///
/// <para>When the intro doesn't run, the game stays in the localization settings' Default Language
/// (Tools > Localization).</para>
/// </summary>
public class IntroCutscene : CutSceneBase
{
    [Header("Language choice")]
    [Tooltip("Before the warning, on the same black screen: the player picks the game's language among the " +
             "localization settings' languages (Tools > Localization). Skipped when there is only one.")]
    [SerializeField] private bool chooseLanguage = true;
    [Tooltip("Dimmed line above the options: this word in every language of the choice (Language · Язык · Idioma). " +
             "Empty: no line.")]
    [SerializeField, Localized] private string languageTitle = "Language";
    [Tooltip("Size of the options; the title line is half of it.")]
    [SerializeField, Min(1f)] private float languageFontSize = 56f;
    [Tooltip("The options look like this death cutscene's fork (its line template, line color and hover red). " +
             "Empty: the FuneralGameManager's Death Cutscene; none at all: the colors below.")]
    [SerializeField] private DeathCutscene languageLookFrom;
    [SerializeField] private Color languageColor = Color.white;
    [Tooltip("The highlighted option - the same dark red as the death cutscene's choice.")]
    [SerializeField] private Color languageSelectedColor = new Color(0.55f, 0.04f, 0.04f, 1f);
    [SerializeField] private Color languageTitleColor = new Color(1f, 1f, 1f, 0.35f);
    [Tooltip("Distance between two options (UI units).")]
    [SerializeField, Min(1f)] private float languageSpacing = 90f;
    [Tooltip("Seconds the options fade in, and again out once one is picked. The screen stays black.")]
    [SerializeField, Min(0f)] private float languageFade = 0.4f;
    [Tooltip("Seconds after the options come up before a key / click counts (so a stray press doesn't pick).")]
    [SerializeField, Min(0f)] private float languageInputLockout = 0.3f;
    [Tooltip("Played when the highlight moves to another option. Optional.")]
    [SerializeField] private AudioClip languageMoveSfx;
    [Tooltip("Played when an option is picked. Optional.")]
    [SerializeField] private AudioClip languagePickSfx;

    [Header("Warning")]
    [Tooltip("The cutscene opens on a black frame with the warning text before the dialogue.")]
    [SerializeField] private bool showWarning = true;
    [SerializeField, TextArea, Localized] private string warningText = "This game raise heavy topics. Take care";
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
    [Tooltip("Empty: TextMeshPro's default font. The language choice uses it too.")]
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

    // The mouse is over a language option: the cursor shows its button icon.
    private bool cursorOnOption;

    // The black screen the language choice and the warning sit on, kept up (their text faded out) until the
    // dialogue starts under it.
    private CanvasGroup screen;
    private Canvas screenCanvas;

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
        cutsceneSteps.Add(LanguageStep());
        cutsceneSteps.Add(WarningStep());
        cutsceneSteps.Add(DialogueStep());
        cutsceneSteps.Add(FadeStep());
    }

    // ---------------------------------------------------------------------
    // Language choice
    // ---------------------------------------------------------------------

    private IEnumerator LanguageStep()
    {
        IReadOnlyList<LocalizationSettings.Language> languages = Loc.Languages;
        if (!chooseLanguage || languages.Count < 2) yield break;

        if (!screen) screen = MakeScreen();
        TakeForkLook();
        CanvasGroup group = MakeLanguageOptions(languages, out List<TextMeshProUGUI> options);

        int selected = 0;
        for (int i = 0; i < languages.Count; i++)
            if (string.Equals(languages[i].code, Loc.Current, StringComparison.OrdinalIgnoreCase)) selected = i;
        HighlightLanguage(options, selected, instant: true);

        if (languageFade > 0f)
        {
            group.alpha = 0f;
            Tween.Alpha(group, 1f, languageFade, Ease.Linear, useUnscaledTime: true);
        }

        float shown = 0f;
        Mouse mouse = Mouse.current;
        Vector2 lastMouse = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
        while (true)
        {
            yield return null;
            shown += Time.unscaledDeltaTime;
            if (shown < languageInputLockout) continue;

            int previous = selected;
            bool picked = ReadLanguageInput(options, ref selected, ref lastMouse);
            if (selected != previous)
            {
                HighlightLanguage(options, selected, instant: false);
                PlaySfx(languageMoveSfx);
            }
            if (picked) break;
        }

        SetCursorOnOption(false);
        PlaySfx(languagePickSfx);
        Loc.Set(languages[selected].code);
        h.Out($"IntroCutscene: language {languages[selected].code}.");

        // Let the press pass, so it doesn't also skip the warning that comes next.
        yield return null;

        Tween.StopAll(group);
        if (languageFade > 0f)
            yield return Tween.Alpha(group, 0f, languageFade, Ease.Linear, useUnscaledTime: true).ToYieldInstruction();
        Destroy(group.gameObject);
    }

    /// <summary>
    /// Moves <paramref name="selected"/> with the arrows / WASD / d-pad / mouse; true once an option is picked
    /// (Enter, Space, a click on it, its number key, the gamepad's south button).
    /// </summary>
    private bool ReadLanguageInput(List<TextMeshProUGUI> options, ref int selected, ref Vector2 lastMouse)
    {
        int count = options.Count;
        bool picked = false;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame ||
                keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame) selected--;
            if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame ||
                keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame ||
                keyboard.tabKey.wasPressedThisFrame) selected++;
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame ||
                keyboard.spaceKey.wasPressedThisFrame) picked = true;
            for (int i = 0; i < count && i < 9; i++)
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame || keyboard[Key.Numpad1 + i].wasPressedThisFrame)
                {
                    selected = i;
                    picked = true;
                }
        }

        Gamepad gamepad = Gamepad.current;
        if (gamepad != null)
        {
            if (gamepad.dpad.up.wasPressedThisFrame || gamepad.dpad.left.wasPressedThisFrame) selected--;
            if (gamepad.dpad.down.wasPressedThisFrame || gamepad.dpad.right.wasPressedThisFrame) selected++;
            if (gamepad.buttonSouth.wasPressedThisFrame || gamepad.startButton.wasPressedThisFrame) picked = true;
        }

        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            Vector2 position = mouse.position.ReadValue();
            int hovered = OptionAt(options, position);
            SetCursorOnOption(hovered >= 0);
            // The highlight follows the mouse only while it moves, so it doesn't fight the keys.
            if (hovered >= 0 && (position - lastMouse).sqrMagnitude > 1f) selected = hovered;
            lastMouse = position;
            if (hovered >= 0 && mouse.leftButton.wasPressedThisFrame)
            {
                selected = hovered;
                picked = true;
            }
        }

        selected = (selected % count + count) % count;
        return picked;
    }

    private int OptionAt(List<TextMeshProUGUI> options, Vector2 screenPosition)
    {
        Camera cam = screenCanvas && screenCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? screenCanvas.worldCamera : null;
        for (int i = 0; i < options.Count; i++)
            if (RectTransformUtility.RectangleContainsScreenPoint(options[i].rectTransform, screenPosition, cam)) return i;
        return -1;
    }

    private void HighlightLanguage(List<TextMeshProUGUI> options, int selected, bool instant)
    {
        for (int i = 0; i < options.Count; i++)
        {
            TextMeshProUGUI option = options[i];
            bool on = i == selected;
            Color color = on ? languageSelectedColor : languageColor;

            Tween.StopAll(option);
            if (instant) option.color = color;
            else Tween.Color(option, color, 0.15f, Ease.OutQuad, useUnscaledTime: true);
        }
    }

    /// <summary>The options, one line per language in its own name, centred on the black; and the title line above.</summary>
    private CanvasGroup MakeLanguageOptions(IReadOnlyList<LocalizationSettings.Language> languages,
        out List<TextMeshProUGUI> options)
    {
        GameObject root = new GameObject("Language Choice", typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(screen.transform, false);
        Stretch((RectTransform)root.transform, Vector2.zero);

        options = new List<TextMeshProUGUI>();
        float top = (languages.Count - 1) * 0.5f * languageSpacing;
        for (int i = 0; i < languages.Count; i++)
        {
            string name = !string.IsNullOrEmpty(languages[i].nativeName) ? languages[i].nativeName : languages[i].code;
            TextMeshProUGUI option = MakeLabel(root.transform, "Option " + languages[i].code, name, languageFontSize,
                new Vector2(0f, top - i * languageSpacing), new Vector2(700f, languageSpacing), optionTemplate);
            options.Add(option);
        }

        if (!string.IsNullOrEmpty(languageTitle))
        {
            // The word in every language, each once: "Language · Язык · Idioma".
            List<string> words = new List<string>();
            foreach (LocalizationSettings.Language language in languages)
            {
                string word = Loc.T(languageTitle, language.code);
                if (!words.Contains(word)) words.Add(word);
            }
            TextMeshProUGUI title = MakeLabel(root.transform, "Title", string.Join("   ·   ", words),
                languageFontSize * 0.5f, new Vector2(0f, top + languageSpacing * 1.25f), new Vector2(1600f, languageSpacing));
            title.color = languageTitleColor;
        }

        return root.GetComponent<CanvasGroup>();
    }

    // The death cutscene's fork line, copied for each option (its font and material); null: plain labels.
    private TextMeshProUGUI optionTemplate;

    /// <summary>Takes the death cutscene's fork look: its line template and its line / hover colors.</summary>
    private void TakeForkLook()
    {
        DeathCutscene source = languageLookFrom;
        if (!source && FuneralGameManager.Instance) source = FuneralGameManager.Instance.DeathCutscene;
        if (!source) source = FindFirstObjectByType<DeathCutscene>(FindObjectsInactive.Include);
        if (!source) return;

        ChoiceForkLook look = source.ForkLook;
        optionTemplate = look.lineTemplate;
        languageColor = look.lineColor;
        languageSelectedColor = look.lineHoverColor;
        if (!languageMoveSfx) languageMoveSfx = look.hoverSfx;
        if (!languagePickSfx) languagePickSfx = look.clickSfx;
    }

    private TextMeshProUGUI MakeLabel(Transform parent, string objectName, string text, float size, Vector2 position,
        Vector2 rectSize, TextMeshProUGUI template = null)
    {
        TextMeshProUGUI label;
        if (template)
        {
            label = Instantiate(template, parent);
            label.name = objectName;
            label.gameObject.SetActive(true);
        }
        else
        {
            label = new GameObject(objectName, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            label.transform.SetParent(parent, false);
            if (warningFont) label.font = warningFont;
        }

        RectTransform rect = label.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localScale = Vector3.one;
        rect.anchoredPosition = position;
        rect.sizeDelta = rectSize;

        label.fontSize = size;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        label.text = text;
        return label;
    }

    /// <summary>
    /// Tells the cursor whether the pointer is on an option, so it shows its button icon there (the options aren't
    /// Selectables, which it would find by itself).
    /// </summary>
    private void SetCursorOnOption(bool on)
    {
        if (cursorOnOption == on) return;
        cursorOnOption = on;
        if (CursorIconManager.Instance) CursorIconManager.Instance.SetButtonHover(on);
    }

    // The cutscene may be cut off while the choice is up.
    private void OnDestroy() => SetCursorOnOption(false);

    private static void PlaySfx(AudioClip clip)
    {
        if (clip && SFXManager.Instance) SFXManager.Instance.PlayClip(clip);
    }

    // ---------------------------------------------------------------------
    // Warning
    // ---------------------------------------------------------------------

    private IEnumerator WarningStep()
    {
        if (!showWarning) yield break;

        if (!screen) screen = MakeScreen();
        CanvasGroup warningText = MakeWarningText();

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

    /// <summary>Fades the black screen away (the dialogue fading in under it), then removes it.</summary>
    private void RevealDialogue()
    {
        if (!screen) return;

        GameObject screenObject = screen.gameObject;
        if (dialogueFadeIn <= 0f)
        {
            Destroy(screenObject);
            return;
        }
        // It goes with the cutscene if the dialogue gets cut off.
        Tween.Alpha(screen, 0f, dialogueFadeIn, dialogueFadeInEase, useUnscaledTime: true)
            .OnComplete(screenObject, Destroy);
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
    /// for the language choice and the warning. The returned group fades the whole screen.
    /// </summary>
    private CanvasGroup MakeScreen()
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
        screenCanvas = canvas;

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Blocks clicks into whatever is behind it, too.
        GameObject black = new GameObject("Black", typeof(RectTransform), typeof(Image));
        black.transform.SetParent(go.transform, false);
        Stretch((RectTransform)black.transform, Vector2.zero);
        black.GetComponent<Image>().color = Color.black;

        return go.GetComponent<CanvasGroup>();
    }

    /// <summary>The warning text centred on the black screen, shaking. The returned group fades the text alone.</summary>
    private CanvasGroup MakeWarningText()
    {
        // Faded through its own group: TextAnimation rewrites the vertex colors every frame.
        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasGroup));
        textObject.transform.SetParent(screen.transform, false);
        Stretch((RectTransform)textObject.transform, new Vector2(-200f, -200f));
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        if (warningFont) text.font = warningFont;
        text.fontSize = warningFontSize;
        text.color = warningColor;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.text = Loc.T(warningText);

        if (warningShakeStrength > 0f)
        {
            TextAnimation animation = textObject.AddComponent<TextAnimation>();
            animation.animationTarget = TextAnimation.AnimationTarget.WholeText;
            animation.animations = new List<TextAnimation.AnimationType> { TextAnimation.AnimationType.Shake };
            animation.shakeStrength = warningShakeStrength;
            animation.shakeSpeed = warningShakeSpeed;
        }

        return textObject.GetComponent<CanvasGroup>();
    }

    private static void Stretch(RectTransform rect, Vector2 sizeDelta)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = sizeDelta;
    }

    // ---------------------------------------------------------------------
    // Dialogue and fade
    // ---------------------------------------------------------------------

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
