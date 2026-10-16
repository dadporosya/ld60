using System.Collections.Generic;
using System.Text;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Shows the game's credits - who made it, what was used, thanks - as a list of text "frames".
///
/// <para>Two ways of showing the same <see cref="frames"/> list, picked with <see cref="mode"/>:</para>
/// <list type="bullet">
/// <item><b>scroll</b> - every frame is glued into one long text that crawls upward at
/// <see cref="scrollSpeed"/> px/sec. Clicking/pressing space jumps to the next frame instead of
/// waiting for it.</item>
/// <item><b>slideShow</b> - one frame at a time in a single <see cref="TMP_Text"/>, swapped every
/// <see cref="slideChangingRate"/> seconds. Clicking/pressing space swaps immediately.</item>
/// </list>
///
/// <para>Scene setup for scroll mode: put the text inside a masked "viewport" RectTransform and
/// author its position so the text block starts just BELOW the viewport's bottom edge - that
/// authored position is captured as the scroll start, and everything moves up from there.</para>
/// </summary>
public class CreditsManager : MonoBehaviour
{
    public static CreditsManager Instance;

    public enum Mode
    {
        scroll,
        slideShow
    }

    [System.Serializable] public class FrameEvent : UnityEvent<int> { }

    // ================================================================ Content

    [Header("Content")]
    [SerializeField] private Mode mode = Mode.scroll;

    [Tooltip("One entry per credits frame. Rich text (TMP tags) is allowed.")]
    [SerializeField, TextArea(2, 8)] private List<string> frames = new List<string>();

    // ================================================================ References

    [Header("References")]
    [Tooltip("The single text field the credits are written into: the whole crawl in scroll mode, " +
             "the current frame in slide show mode.")]
    [SerializeField] private TMP_Text text;

    [Tooltip("Scroll mode: the masked area the text crawls through. Falls back to the text's parent.")]
    [SerializeField] private RectTransform viewport;

    [Tooltip("Optional object holding the whole credits UI - enabled while playing, disabled after.")]
    [SerializeField] private GameObject creditsRoot;

    [Tooltip("Optional - fades slides in and out in slide show mode.")]
    [SerializeField] private CanvasGroup fadeGroup;

    // ================================================================ Scroll

    [Header("Scroll mode")]
    [Tooltip("Pixels per second the credits crawl upward.")]
    [SerializeField] private float scrollSpeed = 60f;

    [Tooltip("Inserted between two frames when they are glued into the single scrolling text.")]
    [SerializeField] private string scrollFrameSeparator = "\n\n";

    [Tooltip("Seconds the jump to the next frame takes. 0 = instant snap.")]
    [SerializeField] private float skipDuration = 0.35f;

    [Tooltip("Where the skipped-to frame lands in the viewport: 0 = bottom edge, 0.5 = middle, 1 = top.")]
    [SerializeField, Range(0f, 1f)] private float skipAlignment = 0.5f;

    [Tooltip("Extra pixels scrolled after the last line has left the screen, before the credits end.")]
    [SerializeField] private float scrollEndPadding = 0f;

    [Tooltip("Resize the text RectTransform to its content height on start, so long credits aren't clipped.")]
    [SerializeField] private bool resizeTextToFitContent = true;

    // ================================================================ Slide show

    [Header("Slide show mode")]
    [Tooltip("Gap between frames, in seconds - how long one slide stays on screen.")]
    [SerializeField] private float slideChangingRate = 4f;

    [Tooltip("Off = slides only change on player input.")]
    [SerializeField] private bool autoAdvanceSlides = true;

    [Tooltip("Seconds of fade out/in between two slides. Needs a fadeGroup. 0 = hard cut.")]
    [SerializeField] private float slideFadeDuration = 0.25f;

    [Tooltip("Played every time the slide changes - a random clip from the list each time, so a few " +
             "variations keep a long credits sequence from sounding like a metronome. Empty = silent.")]
    [SerializeField] private List<AudioClip> slideChangeSFX = new List<AudioClip>();

    [Tooltip("Volume of the slide change sound. Negative = the SFXManager's own default.")]
    [SerializeField] private float slideChangeVolume = -1f;

    [Tooltip("Random pitch range for the slide change sound. 1,1 = no variation.")]
    [SerializeField] private Vector2 slideChangePitchRange = new Vector2(1f, 1f);

    [Tooltip("Also play it for the very first slide. Off = only the changes AFTER it make a sound.")]
    [SerializeField] private bool playSfxOnFirstSlide = true;

    // ================================================================ Playback

    [Header("Playback")]
    [SerializeField] private bool playOnStart = false;

    [Tooltip("Run on unscaled time, so the credits still play with Time.timeScale at 0.")]
    [SerializeField] private bool useUnscaledTime = true;

    [Tooltip("Off = the player can't skip frames.")]
    [SerializeField] private bool acceptInput = true;

    [Tooltip("Keys (and mouse buttons) that show the next frame.")]
    [SerializeField] private List<KeyCode> advanceKeys = new List<KeyCode>
    {
        KeyCode.Space, KeyCode.Return, KeyCode.Mouse0
    };

    [SerializeField] private bool loop = false;

    [Tooltip("Disable creditsRoot while the credits aren't playing.")]
    [SerializeField] private bool hideRootWhenNotPlaying = true;

    [Tooltip("Stall the credits while GameFlowManager reports a paused state. Usually off: the " +
             "credits themselves tend to run during a non-Game state.")]
    [SerializeField] private bool stallWhileGameIsPaused = false;

    [Header("Events")]
    public UnityEvent onCreditsStart;
    public UnityEvent onCreditsEnd;
    [Tooltip("Fired with the index of every frame as it comes on screen.")]
    public FrameEvent onFrameShown;

    // ================================================================ State

    private bool playing;
    private bool paused;
    private int currentFrame = -1;

    // slide show
    private float slideTimer;
    private int slidesShown;   // this run only - reset by Play(), so a loop's first slide still sounds

    // scroll
    private RectTransform textRect;
    private Vector2 scrollStartPos;
    private float travel;          // px scrolled up from the authored start position
    private float totalTravel;     // travel at which the credits are over
    private float contentHeight;
    private readonly List<float> frameOffsets = new List<float>(); // px from content top to each frame's top
    private Tween skipTween;
    private Sequence fadeSequence;
    private bool finishAfterSkip;

    /// <summary>True between <see cref="Play()"/> and the end of the credits, pause included.</summary>
    public bool IsPlaying => playing;

    /// <summary>True while the credits are running and actually advancing.</summary>
    public bool IsRunning => playing && !paused;

    /// <summary>Index of the frame on screen, -1 before the first one.</summary>
    public int CurrentFrame => currentFrame;

    public int FrameCount => frames != null ? frames.Count : 0;

    // Settable so a cutscene (see CreditCutscene) can drive the credits from its own inspector
    // instead of the authored values. Changing any of these mid-play only takes effect on the
    // next Play() for the ones the layout is built from (frames, mode).

    public List<string> Frames
    {
        get => frames;
        set => frames = value ?? new List<string>();
    }

    public Mode PlayMode
    {
        get => mode;
        set => mode = value;
    }

    /// <summary>Scroll mode crawl speed, in pixels per second.</summary>
    public float ScrollSpeed
    {
        get => scrollSpeed;
        set => scrollSpeed = value;
    }

    /// <summary>Slide show gap between frames, in seconds.</summary>
    public float SlideChangingRate
    {
        get => slideChangingRate;
        set => slideChangingRate = value;
    }

    /// <summary>Clips a slide change picks from. Empty = silent.</summary>
    public List<AudioClip> SlideChangeSFX
    {
        get => slideChangeSFX;
        set => slideChangeSFX = value ?? new List<AudioClip>();
    }

    public bool Loop
    {
        get => loop;
        set => loop = value;
    }

    /// <summary>Whether clicks / space show the next frame.</summary>
    public bool AcceptInput
    {
        get => acceptInput;
        set => acceptInput = value;
    }

    private float DeltaTime => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

    // ================================================================ Lifecycle

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance, setDontDestroy: false);

        if (!text) text = GetComponentInChildren<TMP_Text>(true);
        if (text) textRect = text.rectTransform;
        if (!viewport && textRect) viewport = textRect.parent as RectTransform;
    }

    private void Start()
    {
        if (hideRootWhenNotPlaying && creditsRoot && !playOnStart) creditsRoot.SetActive(false);
        if (playOnStart) Play();
    }

    private void Update()
    {
        if (!playing || paused) return;
        if (stallWhileGameIsPaused && GameFlowManager.Instance && GameFlowManager.Instance.IsPaused()) return;

        if (acceptInput && AdvancePressed()) Next();

        // Next() may have ended the credits.
        if (!playing) return;

        if (mode == Mode.scroll) TickScroll(DeltaTime);
        else TickSlideShow(DeltaTime);
    }

    private bool AdvancePressed()
    {
        if (advanceKeys == null) return false;
        for (int i = 0; i < advanceKeys.Count; i++)
            if (Input.GetKeyDown(advanceKeys[i])) return true;
        return false;
    }

    // ================================================================ Public API

    /// <summary>Starts the credits from the top, in the authored <see cref="mode"/>.</summary>
    public void Play()
    {
        if (!text)
        {
            h.Out("CreditsManager: no text assigned, nothing to show");
            return;
        }
        if (FrameCount == 0)
        {
            h.Out("CreditsManager: frames list is empty");
            return;
        }

        StopTweens();

        playing = true;
        paused = false;
        currentFrame = -1;
        slidesShown = 0;
        finishAfterSkip = false;

        if (creditsRoot) creditsRoot.SetActive(true);
        if (fadeGroup) fadeGroup.alpha = 1f;

        onCreditsStart?.Invoke();

        if (mode == Mode.scroll) BeginScroll();
        else ShowSlide(0);
    }

    /// <summary>Replaces the frames, then plays.</summary>
    public void Play(List<string> framesIn)
    {
        if (framesIn != null) frames = new List<string>(framesIn);
        Play();
    }

    /// <summary>Switches mode, then plays.</summary>
    public void Play(Mode modeIn)
    {
        mode = modeIn;
        Play();
    }

    /// <summary>
    /// Shows the next frame: swaps the slide, or jumps the crawl to the next frame. Ends the
    /// credits (or loops) when there is no next frame.
    /// </summary>
    public void Next()
    {
        if (!playing) return;

        if (mode == Mode.scroll) SkipToFrame(currentFrame + 1);
        else ShowSlide(currentFrame + 1);
    }

    public void Pause() => paused = true;

    public void Resume() => paused = false;

    /// <summary>Ends the credits early. Fires <see cref="onCreditsEnd"/> like a natural end.</summary>
    public void Stop() => Finish();

    // ================================================================ Scroll mode

    private void BeginScroll()
    {
        BuildScrollText();

        travel = 0f;
        scrollStartPos = textRect.anchoredPosition;
        totalTravel = contentHeight + ViewportHeight() + scrollEndPadding;

        ApplyTravel();
        RefreshFrameFromTravel();
    }

    /// <summary>
    /// Glues every frame into one string, measures it, and records how far below the top of that
    /// block each frame starts - the distances the skip jumps between.
    /// </summary>
    private void BuildScrollText()
    {
        StringBuilder sb = new StringBuilder();
        List<int> frameSourceIndices = new List<int>(frames.Count);

        for (int i = 0; i < frames.Count; i++)
        {
            if (i > 0) sb.Append(scrollFrameSeparator);
            frameSourceIndices.Add(sb.Length);
            sb.Append(frames[i]);
        }

        text.text = sb.ToString();
        text.ForceMeshUpdate();

        if (resizeTextToFitContent)
        {
            textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, text.preferredHeight);
            text.ForceMeshUpdate();
        }

        MeasureFrames(frameSourceIndices);
    }

    private void MeasureFrames(List<int> frameSourceIndices)
    {
        frameOffsets.Clear();
        contentHeight = 0f;

        TMP_TextInfo info = text.textInfo;
        if (info == null || info.lineCount == 0)
        {
            for (int i = 0; i < frames.Count; i++) frameOffsets.Add(0f);
            return;
        }

        // TMP's line metrics are in the text rect's local space, Y growing upward.
        float top = info.lineInfo[0].ascender;
        float bottom = info.lineInfo[info.lineCount - 1].descender;
        contentHeight = Mathf.Max(0f, top - bottom);

        for (int i = 0; i < frameSourceIndices.Count; i++)
        {
            int line = LineOfSourceIndex(info, frameSourceIndices[i]);
            frameOffsets.Add(line < 0 ? 0f : top - info.lineInfo[line].ascender);
        }
    }

    /// <summary>
    /// Which laid-out line the character at <paramref name="sourceIndex"/> of the raw string ended
    /// up on. Rich text tags don't produce characters, so the raw index is matched through
    /// <see cref="TMP_CharacterInfo.index"/> rather than assumed to line up.
    /// </summary>
    private static int LineOfSourceIndex(TMP_TextInfo info, int sourceIndex)
    {
        int count = Mathf.Min(info.characterCount, info.characterInfo.Length);
        for (int i = 0; i < count; i++)
            if (info.characterInfo[i].index >= sourceIndex) return info.characterInfo[i].lineNumber;
        return -1;
    }

    private void TickScroll(float dt)
    {
        // The skip tween owns `travel` while it runs.
        if (skipTween.isAlive) return;

        travel += scrollSpeed * dt;

        if (travel >= totalTravel)
        {
            travel = totalTravel;
            ApplyTravel();
            EndOrLoop();
            return;
        }

        ApplyTravel();
        RefreshFrameFromTravel();
    }

    private void ApplyTravel()
    {
        if (!textRect) return;
        textRect.anchoredPosition = scrollStartPos + Vector2.up * travel;
    }

    /// <summary>Announces every frame whose top has crawled into the viewport since the last check.</summary>
    private void RefreshFrameFromTravel()
    {
        int shown = currentFrame;
        for (int i = currentFrame + 1; i < frameOffsets.Count; i++)
        {
            if (travel < frameOffsets[i]) break;
            shown = i;
        }

        while (currentFrame < shown)
        {
            currentFrame++;
            onFrameShown?.Invoke(currentFrame);
        }
    }

    /// <summary>Travel at which frame <paramref name="index"/> sits at <see cref="skipAlignment"/> of the viewport.</summary>
    private float TravelForFrame(int index)
    {
        if (index < 0 || index >= frameOffsets.Count) return travel;
        return frameOffsets[index] + skipAlignment * ViewportHeight();
    }

    private void SkipToFrame(int index)
    {
        StopSkipTween();

        bool toEnd = index >= frameOffsets.Count;
        float target = toEnd ? totalTravel : TravelForFrame(index);

        // Never scroll backwards: a frame already past its skip position just means "go further".
        if (target <= travel && !toEnd)
        {
            RefreshFrameFromTravel();
            return;
        }

        finishAfterSkip = toEnd && !loop;

        if (skipDuration <= 0f)
        {
            travel = target;
            ApplyTravel();
            RefreshFrameFromTravel();
            if (toEnd) EndOrLoop();
            return;
        }

        skipTween = Tween.Custom(travel, target, skipDuration, t =>
            {
                travel = t;
                ApplyTravel();
                RefreshFrameFromTravel();
            },
            Ease.OutQuad,
            useUnscaledTime: useUnscaledTime)
            .OnComplete(this, m => m.OnSkipComplete());
    }

    private void OnSkipComplete()
    {
        if (!finishAfterSkip && travel < totalTravel) return;
        finishAfterSkip = false;
        EndOrLoop();
    }

    private float ViewportHeight()
    {
        if (viewport) return viewport.rect.height;
        if (textRect && textRect.parent is RectTransform parentRect) return parentRect.rect.height;
        return Screen.height;
    }

    // ================================================================ Slide show mode

    private void ShowSlide(int index)
    {
        if (index >= frames.Count)
        {
            EndOrLoop();
            return;
        }

        currentFrame = Mathf.Max(0, index);
        slideTimer = 0f;

        if (fadeGroup && slideFadeDuration > 0f)
        {
            fadeSequence.Stop();
            // Fade the old slide out, swap the text at the turn, fade the new one in.
            float half = slideFadeDuration * 0.5f;
            fadeSequence = Sequence
                .Create(Tween.Alpha(fadeGroup, 0f, half, useUnscaledTime: useUnscaledTime))
                .ChainCallback(this, m => m.ApplySlideText())
                .Chain(Tween.Alpha(fadeGroup, 1f, half, useUnscaledTime: useUnscaledTime));
        }
        else
        {
            ApplySlideText();
        }
    }

    private void ApplySlideText()
    {
        text.text = frames[currentFrame];

        PlaySlideChangeSfx();
        slidesShown++;

        onFrameShown?.Invoke(currentFrame);
    }

    /// <summary>
    /// One random clip from <see cref="slideChangeSFX"/>, on the frame the text actually swaps -
    /// so a faded change sounds at the turn of the fade, not when it started.
    /// </summary>
    private void PlaySlideChangeSfx()
    {
        if (slideChangeSFX == null || slideChangeSFX.Count == 0) return;
        if (slidesShown == 0 && !playSfxOnFirstSlide) return;

        if (!SFXManager.Instance)
        {
            h.Out("CreditsManager: no SFXManager instance, skipping the slide change sound");
            return;
        }

        SFXManager.Instance.PlayRandomClip(
            slideChangeSFX,
            volumeIn: slideChangeVolume >= 0f ? slideChangeVolume : (float?)null,
            is3D: false,
            randomPitchRange: slideChangePitchRange,
            playIndependently: true);
    }

    private void TickSlideShow(float dt)
    {
        if (!autoAdvanceSlides || slideChangingRate <= 0f) return;

        slideTimer += dt;
        if (slideTimer < slideChangingRate) return;

        slideTimer = 0f;
        ShowSlide(currentFrame + 1);
    }

    // ================================================================ Ending

    private void EndOrLoop()
    {
        if (loop)
        {
            currentFrame = -1;
            if (mode == Mode.scroll)
            {
                travel = 0f;
                ApplyTravel();
                RefreshFrameFromTravel();
            }
            else
            {
                ShowSlide(0);
            }
            return;
        }

        Finish();
    }

    private void Finish()
    {
        if (!playing) return;

        StopTweens();
        playing = false;
        paused = false;

        if (hideRootWhenNotPlaying && creditsRoot) creditsRoot.SetActive(false);

        onCreditsEnd?.Invoke();
    }

    private void StopTweens()
    {
        StopSkipTween();
        fadeSequence.Stop();
    }

    private void StopSkipTween()
    {
        skipTween.Stop();
        finishAfterSkip = false;
    }

    private void OnDisable()
    {
        StopTweens();
    }
}
