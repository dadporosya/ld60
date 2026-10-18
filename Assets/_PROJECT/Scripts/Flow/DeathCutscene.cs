using System;
using System.Collections;
using System.Collections.Generic;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// The hero's death, in four steps:
/// <list type="number">
/// <item>The visual novel dialogue (<see cref="vnDialogue"/>), cutting off whatever dialogue was playing.</item>
/// <item>The <see cref="FadeImageController"/> covers the screen with black, every other sound (music, ambients,
/// whatever still plays) fades out, and <see cref="darkAmbient"/> loops in. After <see cref="deathSfxDelay"/>
/// the <see cref="deathSfx"/> plays once, and the dark ambient may start going with it
/// (<see cref="darkAmbientFadeOutOnShot"/>).</item>
/// <item>A delay, then <see cref="afterAmbient"/> fades in and the plain text box dialogue
/// (<see cref="afterDialogue"/>, played by the <see cref="DialogueManager"/>) runs over the black.</item>
/// <item>The fork: clickable lines in the middle of the screen (<see cref="fork"/>, Yes / No by default), each
/// playing its own dialogue. Whichever was picked, the game closes after it.</item>
/// </list>
///
/// <para>Put it on its OWN GameObject (or a prefab): <see cref="CutSceneBase.Run"/> clones the whole GameObject
/// it sits on. <see cref="FuneralGameManager.BadEnding"/> plays it through <see cref="Play"/>; with
/// <c>runOnStart</c> it plays by itself instead. Everything runs on unscaled time.</para>
///
/// <para>The fork lines go on a screen-space overlay canvas made at runtime (<see cref="forkSortingOrder"/>),
/// unless <see cref="forkParent"/> is set. The text box dialogue shows over the black because the dialogue
/// window's canvas draws above the fade image's.</para>
/// </summary>
public class DeathCutscene : CutSceneBase
{
    /// <summary>One clickable line of the fork, and what it plays once clicked.</summary>
    [Serializable]
    public class ForkOption
    {
        public string title = "Yes";
        [Tooltip("Played (text box) once the line is clicked. Optional.")]
        public DialogueContainer dialogue;
    }

    [Header("1. Visual novel dialogue")]
    [SerializeField] private VisualNovelDialogueContainer vnDialogue;

    [Header("2. Black screen")]
    [Tooltip("Seconds the screen fades to black over once the visual novel dialogue is over.")]
    [SerializeField, Min(0f)] private float blackFadeDuration = 1.5f;
    [SerializeField] private Ease blackFadeEase = Ease.Linear;
    [Tooltip("Seconds every other sound (music, ambients, anything still playing) fades out over, starting with the black.")]
    [SerializeField, Min(0f)] private float soundsFadeOut = 2f;
    [Tooltip("Looped over the black. Empty: silence.")]
    [SerializeField] private AudioClip darkAmbient;
    [SerializeField, Range(0f, 1f)] private float darkAmbientVolume = 0.6f;
    [SerializeField, Min(0f)] private float darkAmbientFadeIn = 2f;
    [Tooltip("Seconds after the screen is black before the death sound plays.")]
    [SerializeField, Min(0f)] private float deathSfxDelay = 3f;
    [Tooltip("Played once over the black (e.g. the shot). Empty: nothing.")]
    [SerializeField] private AudioClip deathSfx;
    [SerializeField, Range(0f, 1f)] private float deathSfxVolume = 1f;
    [Tooltip("Seconds the dark ambient fades out over, starting the moment the death sound plays. " +
             "Negative: it keeps playing until the new ambient swaps it out (Swap Ambients).")]
    [SerializeField] private float darkAmbientFadeOutOnShot = -1f;

    [Header("3. After the shot")]
    [Tooltip("Seconds after the death sound before the new ambient fades in.")]
    [SerializeField, Min(0f)] private float afterShotDelay = 3f;
    [Tooltip("Looped from here on. Empty: the dark ambient keeps playing.")]
    [SerializeField] private AudioClip afterAmbient;
    [SerializeField, Range(0f, 1f)] private float afterAmbientVolume = 0.6f;
    [Tooltip("Seconds the new ambient fades in over (and the dark ambient out, when it's swapped).")]
    [SerializeField, Min(0f)] private float afterAmbientFadeIn = 3f;
    [Tooltip("The dark ambient fades out while the new one fades in. Off: both loop together.")]
    [SerializeField] private bool swapAmbients = true;
    [Tooltip("Seconds after the new ambient starts fading in before the text box dialogue starts.")]
    [SerializeField, Min(0f)] private float afterDialogueDelay = 1f;
    [Tooltip("Plain text box dialogue over the black. The first fork comes up after its last node.")]
    [SerializeField] private DialogueContainer afterDialogue;
    [Tooltip("Optional speaker for the text box dialogues' nodes that don't name one (name, portrait, voice).")]
    [SerializeField] private Talkable speaker;

    [Header("4. Forks")]
    [Tooltip("Seconds after a dialogue's last node before a fork comes up (lets the text box close).")]
    [SerializeField, Min(0f)] private float forkDelay = 0.5f;
    [Tooltip("Shown after the text box dialogue: each line plays its own dialogue, and the game closes after it. " +
             "Empty: the game closes straight away.")]
    [FormerlySerializedAs("firstFork")]
    [SerializeField] private List<ForkOption> fork = new List<ForkOption>
    {
        new ForkOption { title = "Yes" },
        new ForkOption { title = "No" },
    };
    [Tooltip("Seconds the ambients fade out over before the game closes.")]
    [SerializeField, Min(0f)] private float endFadeOut = 1.5f;

    [Header("Fork look")]
    [Tooltip("Where the fork lines go (centred in it). Empty: a screen-space overlay canvas made at runtime.")]
    [SerializeField] private RectTransform forkParent;
    [Tooltip("Sorting order of the runtime canvas (no Fork Parent). Keep it above the fade image's canvas.")]
    [SerializeField] private int forkSortingOrder = 100;
    [Tooltip("Copied once per line (font, size, material). Empty: a plain TextMeshProUGUI with the settings below.")]
    [SerializeField] private TextMeshProUGUI lineTemplate;
    [Tooltip("No Line Template: the font. Empty: TextMeshPro's default font.")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField, Min(1f)] private float fontSize = 56f;
    [SerializeField] private Color lineColor = Color.white;
    [SerializeField] private Color lineHoverColor = new Color(0.55f, 0.04f, 0.04f, 1f);
    [Tooltip("Width (UI units) the lines are spread across, side by side: each gets an equal share, its text centred " +
             "in it (two lines: ---Yes------No---).")]
    [SerializeField, Min(0f)] private float forkWidth = 1200f;
    [Tooltip("Extra room (UI units) around each line's text that still counts as hovering it.")]
    [SerializeField, Min(0f)] private float hitPadding = 16f;
    [Tooltip("Offset of the lines from the centre of the Fork Parent / screen (UI units).")]
    [SerializeField] private Vector2 forkOffset = Vector2.zero;
    [Tooltip("Seconds the lines fade in / out over.")]
    [SerializeField, Min(0f)] private float forkFadeDuration = 0.35f;
    [SerializeField] private AudioClip lineHoverSfx;
    [SerializeField] private AudioClip lineClickSfx;

    private AudioSource darkSource;
    private AudioSource afterSource;
    private AudioSource sfxSource;

    // The fork currently on screen.
    private RectTransform forkCanvas;
    private RectTransform forkContainer;
    private CanvasGroup forkGroup;
    private readonly List<TextMeshProUGUI> forkLines = new List<TextMeshProUGUI>();
    private bool forkOpen;
    private int forkPick = -1;
    private int hoveredLine = -1;
    private bool cursorOnLine;

    /// <summary>Runs the cutscene. It never hands control back: it ends by reloading the scene or closing the game.</summary>
    public void Play() => RunInstance();

    public override void Init()
    {
        base.Init();
        cutsceneSteps.Clear();
        cutsceneSteps.Add(DialogueStep());
        cutsceneSteps.Add(BlackStep());
        cutsceneSteps.Add(AfterShotStep());
        cutsceneSteps.Add(ForkStep());
    }

    private void OnDestroy()
    {
        SetCursorOnLine(false);
    }

    /// <summary>
    /// Hover and click on the fork lines, read straight from the mouse against each line's text - so no other
    /// canvas's raycast target can swallow them.
    /// </summary>
    private void Update()
    {
        if (!forkOpen) return;

        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        Vector2 pointer = mouse.position.ReadValue();
        int hovered = -1;
        for (int i = 0; i < forkLines.Count; i++)
            if (IsOver(forkLines[i], pointer))
            {
                hovered = i;
                break;
            }

        if (hovered != hoveredLine)
        {
            if (hoveredLine >= 0 && hoveredLine < forkLines.Count && forkLines[hoveredLine])
                forkLines[hoveredLine].color = lineColor;
            hoveredLine = hovered;
            if (hovered >= 0)
            {
                forkLines[hovered].color = lineHoverColor;
                PlayUiSfx(lineHoverSfx);
            }
            SetCursorOnLine(hovered >= 0);
        }

        if (hovered >= 0 && mouse.leftButton.wasPressedThisFrame) PickLine(forkLines[hovered], hovered);
    }

    /// <summary>Whether <paramref name="screen"/> is on <paramref name="line"/>'s text (plus <see cref="hitPadding"/>).</summary>
    private bool IsOver(TextMeshProUGUI line, Vector2 screen)
    {
        if (!line || !line.canvas) return false;

        Canvas canvas = line.canvas.rootCanvas;
        Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(line.rectTransform, screen, cam, out Vector2 local))
            return false;

        Bounds bounds = line.textBounds;
        bounds.Expand(new Vector3(hitPadding * 2f, hitPadding * 2f, 0f));
        return local.x >= bounds.min.x && local.x <= bounds.max.x && local.y >= bounds.min.y && local.y <= bounds.max.y;
    }

    // ---------------------------------------------------------------------
    // Steps
    // ---------------------------------------------------------------------

    private IEnumerator DialogueStep()
    {
        HubDialogueRunner runner = HubDialogueRunner.Instance;
        if (!runner || !vnDialogue) yield break;

        bool done = false;
        // Cuts off whatever is playing; the stage stays up so the black covers it, rather than the hub flashing in between.
        runner.InterruptAndPlay(vnDialogue, () => done = true, hideStage: false);

        while (!done && runner.IsPlaying) yield return null;
        if (!done) h.Out("DeathCutscene: the visual novel dialogue was cut off, the cutscene goes on.");
    }

    private IEnumerator BlackStep()
    {
        FadeImageController fade = FadeImageController.Instance;
        HubDialogueRunner runner = HubDialogueRunner.Instance;

        FadeOutOtherSounds(soundsFadeOut);
        PlayLoop(ref darkSource, darkAmbient, darkAmbientVolume, darkAmbientFadeIn);

        if (!fade) h.Out("DeathCutscene: no FadeImageController, the screen doesn't go black.");
        else if (blackFadeDuration <= 0f) fade.SetAlpha(1f);
        else yield return fade.FadeIn(blackFadeDuration, blackFadeEase, useUnscaledTime: true).ToYieldInstruction();

        if (runner) runner.HideStage();

        if (deathSfxDelay > 0f) yield return new WaitForSecondsRealtime(deathSfxDelay);

        if (deathSfx)
        {
            if (!sfxSource) sfxSource = MakeSource("Death SFX", false);
            sfxSource.PlayOneShot(deathSfx, deathSfxVolume);
        }

        // The shot takes the room with it: the dark ambient goes from here, not when the next one comes in.
        if (darkAmbientFadeOutOnShot >= 0f) FadeOutLoop(darkSource, darkAmbientFadeOutOnShot);
    }

    private IEnumerator AfterShotStep()
    {
        if (afterShotDelay > 0f) yield return new WaitForSecondsRealtime(afterShotDelay);

        if (afterAmbient)
        {
            if (swapAmbients) FadeOutLoop(darkSource, afterAmbientFadeIn);
            PlayLoop(ref afterSource, afterAmbient, afterAmbientVolume, afterAmbientFadeIn);
        }

        if (afterDialogueDelay > 0f) yield return new WaitForSecondsRealtime(afterDialogueDelay);
        yield return PlayTextDialogue(afterDialogue);
    }

    private IEnumerator ForkStep()
    {
        if (fork.Count > 0)
        {
            yield return WaitForTextBox();
            yield return ShowFork(fork);
            ForkOption picked = fork[forkPick];
            yield return PlayTextDialogue(picked?.dialogue);
            yield return WaitForTextBox();
        }
        else h.Out("DeathCutscene: no fork options, the game closes.");

        FadeOutLoop(darkSource, endFadeOut);
        FadeOutLoop(afterSource, endFadeOut);
        if (endFadeOut > 0f) yield return new WaitForSecondsRealtime(endFadeOut);

        QuitGame();
    }

    // ---------------------------------------------------------------------
    // Text box dialogues
    // ---------------------------------------------------------------------

    /// <summary>Plays <paramref name="dialogue"/> in the <see cref="DialogueManager"/>'s text box and waits for its end.</summary>
    private IEnumerator PlayTextDialogue(DialogueContainer dialogue)
    {
        if (!dialogue) yield break;

        DialogueManager manager = DialogueManager.Instance;
        if (!manager)
        {
            h.Out($"DeathCutscene: no DialogueManager, skipping '{dialogue.name}'.");
            yield break;
        }

        while (manager && manager.DialogueActive) yield return null;
        if (!manager) yield break;

        // A runtime copy: filling in speakers writes into the nodes, which would otherwise end up in the asset.
        DialogueContainer instance = Instantiate(dialogue);
        instance.name = dialogue.name;
        if (speaker) speaker.InitDialogue(instance);
        else
        {
            instance.AssignNodeIds();
            foreach (DialogueNode node in instance.nodes) node?.Init();
        }

        bool ended = false;
        UnityAction onEnded = () => ended = true;
        if (manager.onDialogueEnd == null) manager.onDialogueEnd = new UnityEvent();
        manager.onDialogueEnd.AddListener(onEnded);
        manager.StartDialogue(instance, speaker);

        while (!ended && manager) yield return null;

        if (manager) manager.onDialogueEnd.RemoveListener(onEnded);
        Destroy(instance);
    }

    /// <summary>Lets the text box finish closing, then <see cref="forkDelay"/>.</summary>
    private IEnumerator WaitForTextBox()
    {
        DialogueManager manager = DialogueManager.Instance;
        while (manager && (manager.DialogueActive || manager.IsClosing)) yield return null;
        if (forkDelay > 0f) yield return new WaitForSecondsRealtime(forkDelay);
    }

    // ---------------------------------------------------------------------
    // Forks
    // ---------------------------------------------------------------------

    /// <summary>Shows one line per option and waits for a click; the picked index is left in <see cref="forkPick"/>.</summary>
    private IEnumerator ShowFork(IReadOnlyList<ForkOption> options)
    {
        BuildFork(options);

        while (forkPick < 0) yield return null;

        // The picked line stays lit while the others fade out with it.
        if (forkFadeDuration > 0f)
            yield return Tween.Alpha(forkGroup, 0f, forkFadeDuration, useUnscaledTime: true).ToYieldInstruction();
        ClearFork();
    }

    private void BuildFork(IReadOnlyList<ForkOption> options)
    {
        ClearFork();
        forkPick = -1;
        RectTransform container = ForkContainer();

        for (int i = 0; i < options.Count; i++)
        {
            ForkOption option = options[i];
            TextMeshProUGUI line = MakeLine(container);
            line.text = option != null ? option.title : "";
            line.color = lineColor;
            // Hover and click are read in Update; the lines block nothing.
            line.raycastTarget = false;
            forkLines.Add(line);
        }

        hoveredLine = -1;
        forkOpen = true;
        forkGroup.blocksRaycasts = true;
        Tween.StopAll(forkGroup);
        if (forkFadeDuration > 0f)
        {
            forkGroup.alpha = 0f;
            Tween.Alpha(forkGroup, 1f, forkFadeDuration, useUnscaledTime: true);
        }
        else forkGroup.alpha = 1f;
    }

    private void PickLine(TextMeshProUGUI line, int index)
    {
        if (!forkOpen) return;
        forkOpen = false;
        forkGroup.blocksRaycasts = false;
        SetCursorOnLine(false);
        PlayUiSfx(lineClickSfx);
        if (line) line.color = lineHoverColor;
        forkPick = index;
    }

    private void ClearFork()
    {
        forkOpen = false;
        hoveredLine = -1;
        SetCursorOnLine(false);
        foreach (TextMeshProUGUI line in forkLines)
            if (line) Destroy(line.gameObject);
        forkLines.Clear();
        if (forkGroup)
        {
            Tween.StopAll(forkGroup);
            forkGroup.alpha = 0f;
            forkGroup.blocksRaycasts = false;
        }
    }

    /// <summary>
    /// The centred row the lines go into (made once): <see cref="forkWidth"/> wide, split into equal cells, one
    /// per line, each text centred in its cell.
    /// </summary>
    private RectTransform ForkContainer()
    {
        if (forkContainer) return forkContainer;

        RectTransform parent = forkParent ? forkParent : ForkCanvas();
        GameObject go = new GameObject("Death Fork", typeof(RectTransform), typeof(CanvasGroup),
            typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        forkContainer = (RectTransform)go.transform;
        forkContainer.SetParent(parent, false);
        forkContainer.anchorMin = forkContainer.anchorMax = forkContainer.pivot = new Vector2(0.5f, 0.5f);
        forkContainer.anchoredPosition = forkOffset;
        forkContainer.sizeDelta = new Vector2(forkWidth, 0f);

        HorizontalLayoutGroup layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 0f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = go.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        forkGroup = go.GetComponent<CanvasGroup>();
        forkGroup.alpha = 0f;
        forkGroup.blocksRaycasts = false;
        return forkContainer;
    }

    /// <summary>A screen-space overlay canvas over everything (<see cref="forkSortingOrder"/>), on this cutscene.</summary>
    private RectTransform ForkCanvas()
    {
        if (forkCanvas) return forkCanvas;

        GameObject go = new GameObject("Death Fork Canvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);

        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = forkSortingOrder;

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        forkCanvas = (RectTransform)go.transform;
        return forkCanvas;
    }

    private TextMeshProUGUI MakeLine(RectTransform container)
    {
        TextMeshProUGUI line;
        if (lineTemplate)
        {
            line = Instantiate(lineTemplate, container);
            line.gameObject.SetActive(true);
        }
        else
        {
            GameObject go = new GameObject("Fork Line", typeof(RectTransform));
            go.transform.SetParent(container, false);
            line = go.AddComponent<TextMeshProUGUI>();
            if (font) line.font = font;
            line.fontSize = fontSize;
            line.textWrappingMode = TextWrappingModes.NoWrap;
        }

        line.alignment = TextAlignmentOptions.Center;

        // Every cell exactly as wide, whatever its text: the row splits evenly.
        LayoutElement cell = line.GetComponent<LayoutElement>();
        if (!cell) cell = line.gameObject.AddComponent<LayoutElement>();
        cell.minWidth = 0f;
        cell.preferredWidth = 0f;
        cell.flexibleWidth = 1f;
        return line;
    }

    /// <summary>
    /// Tells the cursor whether the pointer is on a line, so it shows its button icon there (the lines aren't
    /// Selectables, which it would find by itself).
    /// </summary>
    private void SetCursorOnLine(bool on)
    {
        if (cursorOnLine == on) return;
        cursorOnLine = on;
        if (CursorIconManager.Instance) CursorIconManager.Instance.SetButtonHover(on);
    }

    private void PlayUiSfx(AudioClip clip)
    {
        if (clip && SFXManager.Instance) SFXManager.Instance.PlayClip(clip);
    }

    // ---------------------------------------------------------------------
    // Audio
    // ---------------------------------------------------------------------

    /// <summary>
    /// Fades out the music (<see cref="SongManager"/> / <see cref="BGMManager"/>), every <see cref="AmbientNoise"/>,
    /// and any other AudioSource playing right now (stopped once silent, its volume put back for later use).
    /// The cutscene's own sources are left alone.
    /// </summary>
    private void FadeOutOtherSounds(float duration)
    {
        HashSet<AudioSource> handled = new HashSet<AudioSource>();

        BGMManager bgm = BGMManager.Instance;
        if (SongManager.Instance) SongManager.Instance.Stop(duration);
        else if (bgm) bgm.FadeOutMusic(duration);
        if (bgm) handled.UnionWith(bgm.MusicSources);

        foreach (AmbientNoise noise in FindObjectsByType<AmbientNoise>(FindObjectsSortMode.None))
        {
            noise.Stop(duration);
            handled.UnionWith(noise.GetComponents<AudioSource>());
        }

        foreach (AudioSource source in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
        {
            if (!source.isPlaying || handled.Contains(source) || source.transform.IsChildOf(transform)) continue;

            Tween.StopAll(source);
            float volume = source.volume;
            if (duration <= 0f)
            {
                source.Stop();
                continue;
            }

            Tween.AudioVolume(source, 0f, duration, useUnscaledTime: true).OnComplete(source, s =>
            {
                s.Stop();
                s.volume = volume;
            });
        }
    }

    private AudioSource MakeSource(string sourceName, bool loop)
    {
        GameObject go = new GameObject(sourceName);
        go.transform.SetParent(transform, false);
        AudioSource source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;
        source.outputAudioMixerGroup = AudioMixerManager.GetSFXGroup();
        return source;
    }

    /// <summary>Starts <paramref name="clip"/> looping on <paramref name="source"/> (made if needed) and fades it in.</summary>
    private void PlayLoop(ref AudioSource source, AudioClip clip, float volume, float fadeIn)
    {
        if (!clip) return;
        if (!source) source = MakeSource($"Death Ambient ({clip.name})", true);

        Tween.StopAll(source);
        source.clip = clip;
        source.volume = fadeIn > 0f ? 0f : volume;
        source.Play();
        if (fadeIn > 0f) Tween.AudioVolume(source, volume, fadeIn, useUnscaledTime: true);
    }

    private static void FadeOutLoop(AudioSource source, float duration)
    {
        if (!source || !source.isPlaying) return;

        Tween.StopAll(source);
        if (duration <= 0f) source.Stop();
        else Tween.AudioVolume(source, 0f, duration, useUnscaledTime: true).OnComplete(source, s => s.Stop());
    }

    // ---------------------------------------------------------------------
    // The end
    // ---------------------------------------------------------------------

    private static void QuitGame()
    {
        if (ShutdownManager.Instance) ShutdownManager.Instance.CloseGameImmediately();
        else
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
