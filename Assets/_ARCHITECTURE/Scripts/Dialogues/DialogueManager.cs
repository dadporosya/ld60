using System;
using System.Collections;
using System.Collections.Generic;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance;
    
    public GameObject dialogueWindow;
    public Image portraitImage;
    public TextMeshProUGUI portraitTitle;
    public TextMeshProUGUI dialogueText;
    private TextAnimation textAnimationComp;
    [HideInInspector] public ScalingText portraitScalingTitle;
    [HideInInspector] public ScalingText dialogueScalingText;
    
    // Set for a dialogue started with a custom text target (see StartDialogue(DialogueContainer,
    // TextMeshProUGUI, ...)): its text is typed into this component instead of dialogueText, the
    // dialogue window stays closed, and the portrait/title are left alone.
    private TextMeshProUGUI textOverride;
    private ScalingText scalingTextOverride;
    private TextAnimation textAnimationOverride;
    private Color textOverrideDefaultColor = Color.white;
    private List<AudioClip> voiceClipsOverride;

    /// <summary>True while the running dialogue types into a custom text component rather than the window.</summary>
    public bool UsingTextOverride => textOverride;

    private TextMeshProUGUI ActiveText => textOverride ? textOverride : dialogueText;
    private ScalingText ActiveScalingText => textOverride ? scalingTextOverride : dialogueScalingText;
    private TextAnimation ActiveTextAnimation => textOverride ? textAnimationOverride : textAnimationComp;

    private bool dialogueActive=false;
    private DialogueContainer dialogue;
    private Queue<DialogueNode> currentParagraphs = new Queue<DialogueNode>();

    /// <summary>True while a dialogue is running (between StartDialogue and EndDialogue).</summary>
    public bool DialogueActive => dialogueActive;

    private Coroutine endDialogueCoroutine;

    /// <summary>True while the dialogue window is playing its close animation.</summary>
    public bool IsClosing => endDialogueCoroutine != null;

    /// <summary>
    /// How many nodes the RUNNING dialogue has started so far. 0 before the first one is on screen,
    /// 1 while the first node is showing, and so on — reset by every <see cref="StartDialogue(DialogueContainer, Talkable)"/>.
    /// Lets an outsider tell "the node I armed against is still up" from "the player has moved on"
    /// without holding a reference to a node.
    /// </summary>
    public int NodesStarted { get; private set; }

    /// <summary>Nodes still queued behind the one on screen.</summary>
    public int RemainingNodeCount => currentParagraphs.Count;

    // ================================================================ Advance holds

    /// <summary>
    /// How many holds are currently placed on the dialogue. While this is above zero the dialogue is
    /// frozen on the node it is showing: no key advances it, no key ends it, and
    /// <see cref="DisplayNextParagraph"/> does nothing even when called from code.
    /// </summary>
    private int advanceHolds;

    /// <summary>The holds among <see cref="advanceHolds"/> that also keep hold-to-skip from skipping.</summary>
    private int skipBlockingHolds;

    /// <summary>True while something is holding the dialogue on its current node.</summary>
    public bool AdvanceHeld => advanceHolds > 0;

    /// <summary>
    /// Freezes the dialogue where it is until the matching <see cref="ReleaseAdvance"/> — how a popup
    /// that interrupts the dialogue (a question the player has to answer) stops the player from just
    /// pressing Space past it. Counted, so several holders can overlap: the dialogue moves on when the
    /// LAST one lets go.
    /// </summary>
    /// <param name="blocksSkip">
    /// Also keep hold-to-skip from skipping the dialogue (a popup that would be left hanging). False for a
    /// hold that only stops the reading, e.g. the collapsed text box. Pass the same to <see cref="ReleaseAdvance"/>.
    /// </param>
    public void HoldAdvance(bool blocksSkip = true)
    {
        advanceHolds++;
        if (blocksSkip) skipBlockingHolds++;
    }

    /// <summary>Drops one hold placed by <see cref="HoldAdvance"/>. Never goes below zero.</summary>
    public void ReleaseAdvance(bool blocksSkip = true)
    {
        if (advanceHolds > 0) advanceHolds--;
        if (blocksSkip && skipBlockingHolds > 0) skipBlockingHolds--;
        if (skipBlockingHolds > advanceHolds) skipBlockingHolds = advanceHolds;
    }

    /// <summary>Drops every hold at once — the escape hatch for a holder that was destroyed.</summary>
    public void ClearAdvanceHolds() => advanceHolds = skipBlockingHolds = 0;

    [Header("Advancing")]
    [Tooltip("Pressing any of these finishes the line being typed, or moves on to the next node once it's typed out.")]
    [SerializeField] private List<Key> advanceKeys = new List<Key> { Key.Space, Key.Enter, Key.NumpadEnter };
    [Tooltip("Mouse buttons that do the same as the advance keys. Empty: the mouse doesn't advance.")]
    [SerializeField] private List<MouseButton> advanceMouseButtons = new List<MouseButton> { MouseButton.Left };
    [Tooltip("A click on a UI button, toggle, slider… (any Selectable) is that control's, not the dialogue's: it " +
             "doesn't advance. Clicks anywhere else, the dialogue window included, do.")]
    [SerializeField] private bool ignoreClicksOnControls = true;

    [Header("Auto skip")]
    [Tooltip("Every node moves on by itself autoSkipDelay seconds after its text has finished typing. " +
             "Off: only nodes with their own auto skip on do. The player can still advance earlier.")]
    [SerializeField] private bool autoSkip = false;
    [Tooltip("Seconds after a node's text finishes typing before it moves on (real time, so it also runs " +
             "over a paused game). A node with its own auto skip on uses its own delay instead.")]
    [SerializeField, Min(0f)] private float autoSkipDelay = 2f;

    /// <summary>Auto skip for every node (see <see cref="autoSkip"/>); nodes can still turn it on for themselves.</summary>
    public bool AutoSkip { get => autoSkip; set => autoSkip = value; }
    public float AutoSkipDelay { get => autoSkipDelay; set => autoSkipDelay = Mathf.Max(0f, value); }

    private Coroutine autoSkipCoroutine;

    private readonly List<RaycastResult> clickRaycastResults = new List<RaycastResult>();

    /// <summary>
    /// Asked about every advancing click with the topmost UI element under it: a claimer that returns true
    /// owns the click (a drawing board the click paints on) and it doesn't advance the dialogue.
    /// Static, so a claimer can register before the manager's Awake.
    /// </summary>
    private static readonly List<Func<GameObject, bool>> clickClaimers = new List<Func<GameObject, bool>>();

    public static void AddClickClaimer(Func<GameObject, bool> claimer)
    {
        if (claimer != null && !clickClaimers.Contains(claimer)) clickClaimers.Add(claimer);
    }

    public static void RemoveClickClaimer(Func<GameObject, bool> claimer) => clickClaimers.Remove(claimer);

    [SerializeField] private float defaultTypeSpeed = 10f;
    [HideInInspector] public float typeSpeed;

    [Header("Typing animation")]
    [Tooltip("Every letter trembles for a moment as it's typed in, then settles. Needs a TextAnimation on the " +
             "text being typed into (the dialogue window's text has one).")]
    [SerializeField] private bool typingAnimation = true;
    [SerializeField] private TextAnimation.AppearTremble typingTremble = new TextAnimation.AppearTremble();

    /// <summary>Letters tremble as they're typed in (see <see cref="typingAnimation"/>). Applies from the next letter.</summary>
    public bool TypingAnimation { get => typingAnimation; set => typingAnimation = value; }

    [Header("Punctuation pauses")]
    [Tooltip("Characters after which the typewriter waits for moderatePauseDuration (sentence enders).")]
    [SerializeField] private List<string> moderatePauseChars = new List<string> { ".", "!", "?" };
    [Tooltip("Seconds to wait after a moderate pause character.")]
    [SerializeField] private float moderatePauseDuration = 0.3f;
    [Tooltip("Characters after which the typewriter waits for smallPauseDuration (clause separators).")]
    [SerializeField] private List<string> smallPauseChars = new List<string> { ",", ";", ":" };
    [Tooltip("Seconds to wait after a small pause character.")]
    [SerializeField] private float smallPauseDuration = 0.12f;
    
    public enum VoicePlaybackMode
    {
        [Tooltip("A new voice clip starts only once the previous one has finished playing.")]
        WaitForPrevious,
        [Tooltip("A new voice clip starts every N typed characters, cutting off the previous one — or on any character once the previous one has finished.")]
        EveryNCharacters
    }

    [Header("Typing voice")]
    [Tooltip("WaitForPrevious: next clip only after the previous finished.\nEveryNCharacters: play a clip every N characters, aborting the previous one.")]
    [SerializeField] private VoicePlaybackMode voicePlaybackMode = VoicePlaybackMode.WaitForPrevious;
    [Tooltip("EveryNCharacters mode: a clip is played on every Nth typed character (1 = every character), and on any character once the previous clip has finished; N is counted from the last clip played.")]
    [Min(1)] [SerializeField] private int voiceEveryNCharacters = 3;
    [Tooltip("EveryNCharacters mode: whitespace doesn't count towards N and never triggers a clip.")]
    [SerializeField] private bool voiceSkipWhitespace = true;
    [Tooltip("Volume of the typing voice clips (0 = silent, 1 = full), on top of the SFX mixer volume.")]
    [Range(0f, 1f)] [SerializeField] private float voiceVolume = 1f;
    [SerializeField] private Vector2 voiceRandomPitchRange = new Vector2(0.98f, 1.02f);
    [Tooltip("Log the name of every played voice clip to the console.")]
    [SerializeField] private bool debug = true;

    /// <summary>Volume of the typing voice clips, 0-1. Applies from the next clip played.</summary>
    public float VoiceVolume
    {
        get => voiceVolume;
        set => voiceVolume = Mathf.Clamp01(value);
    }

    // The source the last voice clip was played on, so we can check/abort only OUR clip.
    private AudioSource voiceSource;

    // The typing node's own voice volume multiplier and pitch range (DialogueNode.voiceVolumeScale / voicePitchOverride).
    private float nodeVoiceVolumeScale = 1f;
    private Vector2? nodeVoicePitch;

    private enum TypingState { notStarted, active, pausedAtStop, finished }
    private TypingState typingState = TypingState.notStarted;

    /// <summary>True while typing is currently stopped at a &lt;stop&gt; tag, waiting for the player to advance.</summary>
    public bool IsPausedAtStop => typingState == TypingState.pausedAtStop;

    private string currentTypingText;
    private int currentTextIndex;
    private bool skipToNextStopRequested;
    
    private Coroutine typeCoroutine;

    // Set while a node is waiting for its enter transitions (objects appearing) to finish.
    // Input is ignored during that window so a keypress can't skip or double-start the node.
    private bool nodeStarting;
    private Coroutine startParagraphCoroutine;

    private DialogueNode paragraph;
    private Talkable currentNodeTalkable;
    private Talkable initiatingTalkable;
    
    [SerializeField] private const float MAX_TYPE_SPEED = 0.1f;

    [SerializeField] private GameObject skipIcon;
    
    [SerializeField] private Material defaultMaterial;

    [Tooltip("Also tint the speaker name label with the speaker's text color.")]
    [SerializeField] private bool tintTitleWithSpeakerColor = false;

    // The window's own colors, captured on Start. Nodes whose speaker defines no text color
    // fall back to these, so a colored speaker never leaks into the next one.
    private Color defaultTextColor = Color.white;
    private Color defaultTitleColor = Color.white;

    public UnityEvent onDialogueEnd;
    public UnityEvent onDialogueStart;

    /// <summary>
    /// Optional async delegate invoked inside StartParagraphRoutine right before a node's text begins typing.
    /// Yields until the routine finishes, allowing cutscenes or camera moves to complete before the text is typed.
    /// Parameters: (DialogueNode node, int nodeIndex).
    /// </summary>
    public Func<DialogueNode, int, IEnumerator> onBeforeNodeTyping;
    public UnityEvent onStartNode;
    public UnityEvent onEndNode;

    private string pathLabel = "Dialogues";
    private string alliesLabel = "/Allies";
    private string placeholderLabel = "/PlaceHolder";
    [SerializeField] private bool disableWindowOnStart = true;

    [SerializeField] private DialogueWindowAnimations windowAnimations;

    // ================================================================ Hold to skip

    [Header("Hold to skip")]
    [Tooltip("Holding one of the skip keys fills the skip indicator; once it is full the whole dialogue is skipped.")]
    [SerializeField] private bool holdToSkip = true;
    [SerializeField] private List<Key> skipKeys = new List<Key> { Key.Escape, Key.Enter, Key.NumpadEnter };
    [Tooltip("Seconds a skip key has to be held to skip.")]
    [SerializeField, Min(0.01f)] private float skipHoldDuration = 1f;
    [Tooltip("Shown only while a skip key is held, hidden otherwise. Keep it outside the " +
             "dialogue window if it should also show for skips without a dialogue (see BeginSkippable). " +
             "Faded through a CanvasGroup, added if missing.")]
    [SerializeField] private GameObject skipIndicatorRoot;
    [Tooltip("The circular fill; its fillAmount goes 0 → 1 while the key is held. Set to Filled / Radial 360 " +
             "on Start if it isn't a Filled image already.")]
    [SerializeField] private Image skipFillImage;
    [Tooltip("Seconds a skip key has to be held before the indicator shows, so a quick Enter to advance " +
             "the text doesn't flash it.")]
    [SerializeField, Min(0f)] private float skipShowDelay = 0.15f;
    [Tooltip("Seconds the indicator takes to fade in/out. 0: instant.")]
    [SerializeField, Min(0f)] private float skipFadeDuration = 0.2f;

    /// <summary>The player held a skip key long enough; fired right before the dialogue ends.</summary>
    public UnityEvent onDialogueSkipped;
    public event Action DialogueSkipped;

    /// <summary>
    /// The last dialogue to end was skipped rather than played to its end. Already set while
    /// <see cref="onDialogueEnd"/> runs; reset by the next dialogue's start.
    /// </summary>
    public bool LastDialogueSkipped { get; private set; }

    private float skipHeldTime;
    // A hold only counts if it started while there was something to skip: a key still held from before
    // (or from the skip that just happened) must be let go first.
    private bool skipArmed;
    private bool skipIndicatorShown;
    private CanvasGroup skipIndicatorGroup;
    private Tween skipIndicatorFade;
    private Action externalSkip;

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance);
    }
    private void Start()
    {
        if (!windowAnimations) windowAnimations = GetComponent<DialogueWindowAnimations>();
        if (!windowAnimations) windowAnimations = FindObjectOfType<DialogueWindowAnimations>();

        // Remember the authored colors before anything (EndDialogue included) overrides them.
        if (dialogueText) defaultTextColor = dialogueText.color;
        if (portraitTitle) defaultTitleColor = portraitTitle.color;

        // Snap the window shut on boot; no open/close animation here.
        if (disableWindowOnStart) EndDialogue(animate: false);
        if (portraitTitle) portraitScalingTitle = portraitTitle.GetComponent<ScalingText>();
        dialogueScalingText = dialogueText.GetComponent<ScalingText>();

        textAnimationComp = dialogueText.GetComponent<TextAnimation>();
        
        typeSpeed = defaultTypeSpeed;
        
        onStartNode.AddListener(DisableSkipIcon);
        onEndNode.AddListener(EnableSkipIcon);

        InitSkipIndicator();
    }

    // The icon's own animations (e.g. a FloatingAnimation), played whenever the icon is shown.
    private AnimationBase[] skipIconAnimations;
    private Coroutine enableSkipIconCoroutine;

    private void EnableSkipIcon()
    {
        if (enableSkipIconCoroutine != null) StopCoroutine(enableSkipIconCoroutine);
        enableSkipIconCoroutine = StartCoroutine(EnableSkipIconCoroutine());
    }

    private IEnumerator EnableSkipIconCoroutine()
    {
        yield return new WaitForSeconds(0.5f);
        enableSkipIconCoroutine = null;
        // Not while the next node is still starting: input is ignored then, so there's nothing to skip.
        if (!skipIcon || textOverride || nodeStarting || !dialogueActive) yield break;

        skipIcon.SetActive(true);
        PlaySkipIconAnimations();
    }

    /// <summary>
    /// Starts the icon's animations. An animation with playOnStart already restarts itself in OnEnable, so only
    /// the ones not running yet are played — otherwise they'd restart from scratch.
    /// </summary>
    private void PlaySkipIconAnimations()
    {
        skipIconAnimations ??= skipIcon.GetComponentsInChildren<AnimationBase>(true);
        foreach (AnimationBase animation in skipIconAnimations)
            if (animation && animation.isActiveAndEnabled && !animation.IsPlaying)
                animation.PlayInstantly();
    }

    private void DisableSkipIcon()
    {
        // A pending show from the previous node's end must not pop the icon up over the node now typing.
        if (enableSkipIconCoroutine != null) StopCoroutine(enableSkipIconCoroutine);
        enableSkipIconCoroutine = null;

        if (!skipIcon || !skipIcon.activeSelf) return;
        skipIcon.SetActive(false);

        // Back to the resting pose, so the next show floats from where the icon belongs. (A PrimeTween
        // offset tween would otherwise keep moving the hidden icon.) Regardless of returnToTheInittialState:
        // hiding the icon isn't the animation ending, and a shown icon must start from its real place.
        skipIconAnimations ??= skipIcon.GetComponentsInChildren<AnimationBase>(true);
        foreach (AnimationBase animation in skipIconAnimations)
            if (animation)
                animation.ReturnToInitialState();
    }

    private void Update()
    {
        // Before the early-outs below: the skip works even while a node is still appearing.
        UpdateSkip();

        // Ignore input while the node's objects are still appearing.
        if (nodeStarting)
            return;

        // Something is holding the dialogue on this node (a choice popup waiting for an answer):
        // neither advancing nor the skip key may get past it.
        if (advanceHolds > 0)
            return;

        if (typingState == TypingState.notStarted)
            return;

        if (AdvancePressed())
        {
            // Pressed while the line is still typing: it's the player cutting it short.
            bool interruptsTyping = typingState == TypingState.active;
            DisplayNextParagraph();
            if (interruptsTyping && windowAnimations && !textOverride) windowAnimations.PlayTypingInterrupted();
        } else if (Input.GetKeyDown(KeyCode.L))
        {
            EndDialogue();
        }
    }

    /// <summary>One of <see cref="advanceKeys"/> or <see cref="advanceMouseButtons"/> went down this frame.</summary>
    private bool AdvancePressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
            foreach (Key key in advanceKeys)
                if (key != Key.None && keyboard[key].wasPressedThisFrame)
                    return true;

        Mouse mouse = Mouse.current;
        if (mouse != null)
            foreach (MouseButton button in advanceMouseButtons)
                if (MouseButtonControl(mouse, button) is { wasPressedThisFrame: true })
                    return !IsClickTaken(mouse.position.ReadValue());

        return false;
    }

    /// <summary>
    /// The topmost UI element under <paramref name="screen"/> keeps the click to itself: it's (part of) an
    /// interactable Selectable — a button etc. (see <see cref="ignoreClicksOnControls"/>) — or a click claimer
    /// owns it (see <see cref="AddClickClaimer"/>).
    /// </summary>
    private bool IsClickTaken(Vector2 screen)
    {
        if (!ignoreClicksOnControls && clickClaimers.Count == 0) return false;

        EventSystem events = EventSystem.current;
        if (!events) return false;

        clickRaycastResults.Clear();
        events.RaycastAll(new PointerEventData(events) { position = screen }, clickRaycastResults);
        if (clickRaycastResults.Count == 0) return false;

        GameObject clicked = clickRaycastResults[0].gameObject;
        if (ignoreClicksOnControls)
        {
            Selectable control = clicked.GetComponentInParent<Selectable>();
            if (control && control.IsInteractable()) return true;
        }

        foreach (Func<GameObject, bool> claimer in clickClaimers)
            if (claimer(clicked))
                return true;
        return false;
    }

    private static ButtonControl MouseButtonControl(Mouse mouse, MouseButton button) => button switch
    {
        MouseButton.Left => mouse.leftButton,
        MouseButton.Right => mouse.rightButton,
        MouseButton.Middle => mouse.middleButton,
        MouseButton.Forward => mouse.forwardButton,
        MouseButton.Back => mouse.backButton,
        _ => null,
    };

    // ================================================================ Hold to skip

    /// <summary>
    /// Makes the skip key work while no dialogue is running: holding it fills the same indicator and calls
    /// <paramref name="onSkip"/> (once) instead of ending a dialogue — e.g. a tutorial's scripted part
    /// between two dialogues. Undo with <see cref="EndSkippable"/>.
    /// </summary>
    public void BeginSkippable(Action onSkip)
    {
        externalSkip = onSkip;
    }

    /// <summary>Drops a <see cref="BeginSkippable"/> callback, if it is still the one set.</summary>
    public void EndSkippable(Action onSkip)
    {
        if (externalSkip == onSkip) externalSkip = null;
    }

    /// <summary>
    /// Skips the running dialogue as a whole (<see cref="onDialogueSkipped"/>, then <see cref="EndDialogue"/>),
    /// or with no dialogue running fires the <see cref="BeginSkippable"/> callback. What a full hold does.
    /// </summary>
    public void Skip()
    {
        skipHeldTime = 0f;
        skipArmed = false;
        SetSkipIndicatorShown(false);

        if (dialogueActive)
        {
            LastDialogueSkipped = true;
            onDialogueSkipped?.Invoke();
            DialogueSkipped?.Invoke();
            EndDialogue();
            return;
        }

        Action callback = externalSkip;
        externalSkip = null;
        callback?.Invoke();
    }

    /// <summary>
    /// Keeps the skip indicator on screen with no key held, as a hint that the skip key works (e.g. the
    /// tutorial, for a player who has seen it before). Its fill stays empty until a key is actually held.
    /// </summary>
    public bool PinSkipIndicator { get; set; }

    private bool CanSkip =>
        holdToSkip && (dialogueActive || externalSkip != null)
                   // A hold is a question the player has to answer; skipping would leave its popup hanging.
                   // (Holds placed with blocksSkip off, like the collapsed text box, don't count.)
                   && skipBlockingHolds == 0;

    private void UpdateSkip()
    {
        bool canSkip = CanSkip;
        bool keyHeld = SkipKeyHeld();

        if (!keyHeld) skipArmed = canSkip;
        else if (!canSkip) skipArmed = false;

        // Unscaled: dialogues also run over a paused game.
        bool holding = canSkip && keyHeld && skipArmed;
        skipHeldTime = holding ? skipHeldTime + Time.unscaledDeltaTime : 0f;

        if (skipFillImage) skipFillImage.fillAmount = Mathf.Clamp01(skipHeldTime / skipHoldDuration);

        // Hidden unless a skip key is actually being held, or something pinned it as a hint.
        SetSkipIndicatorShown(PinSkipIndicator || holding && skipHeldTime >= skipShowDelay);

        if (holding && skipHeldTime >= skipHoldDuration) Skip();
    }

    private bool SkipKeyHeld()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return false;

        foreach (Key key in skipKeys)
            if (key != Key.None && keyboard[key].isPressed)
                return true;
        return false;
    }

    private void InitSkipIndicator()
    {
        if (skipFillImage)
        {
            if (skipFillImage.type != Image.Type.Filled)
            {
                skipFillImage.type = Image.Type.Filled;
                skipFillImage.fillMethod = Image.FillMethod.Radial360;
                skipFillImage.fillOrigin = (int)Image.Origin360.Top;
                skipFillImage.fillClockwise = true;
            }
            skipFillImage.fillAmount = 0f;
        }

        if (!skipIndicatorRoot) return;

        Button skipButton = skipIndicatorRoot.GetComponent<Button>();
        if (!skipButton) skipButton = skipIndicatorRoot.GetComponentInChildren<Button>();
        if (skipButton) skipButton.onClick.AddListener(Skip);

        skipIndicatorGroup = skipIndicatorRoot.GetComponent<CanvasGroup>();
        if (!skipIndicatorGroup) skipIndicatorGroup = skipIndicatorRoot.AddComponent<CanvasGroup>();
        skipIndicatorGroup.blocksRaycasts = false;
        skipIndicatorGroup.interactable = false;

        // A dialogue may already have started (and asked for the hint) before this Start ran.
        skipIndicatorShown = true;
        SetSkipIndicatorShown(false, instant: true);
    }

    private void SetSkipIndicatorShown(bool shown, bool instant = false)
    {
        if (!skipIndicatorRoot || skipIndicatorShown == shown) return;
        skipIndicatorShown = shown;

        skipIndicatorFade.Stop();
        if (shown) skipIndicatorRoot.SetActive(true);

        bool hasButton = skipIndicatorRoot.GetComponent<Button>() != null || skipIndicatorRoot.GetComponentInChildren<Button>() != null;
        if (skipIndicatorGroup)
        {
            skipIndicatorGroup.blocksRaycasts = shown && hasButton;
            skipIndicatorGroup.interactable = shown && hasButton;
        }

        if (instant || skipFadeDuration <= 0f || !skipIndicatorGroup)
        {
            if (skipIndicatorGroup) skipIndicatorGroup.alpha = shown ? 1f : 0f;
            if (!shown) skipIndicatorRoot.SetActive(false);
            return;
        }

        skipIndicatorFade = Tween.Alpha(skipIndicatorGroup, shown ? 1f : 0f, skipFadeDuration, useUnscaledTime: true);
        if (!shown) skipIndicatorFade.OnComplete(skipIndicatorRoot, root => root.SetActive(false));
    }

    public void SetText(
        ScalingText scalingHolder,
        TextMeshProUGUI originalHolder, 
        string text)
    {
        if (scalingHolder)
        {
            scalingHolder.SetText(text);
            return;
        }

        originalHolder.text = text;
    }

    public void StartDialogue(string path)
    {
        string fullPath = pathLabel + path;
        var dCont = Resources.Load<DialogueContainer>(fullPath);
        if (!dCont)
        {
            h.Out("dialogue " + fullPath + " not found");
            return;
        }
        StartDialogue(Instantiate(dCont));
    }

    public void StartDialogue(string path, Talkable talkable)
    {
        string fullPath = pathLabel + path;
        var dCont = Resources.Load<DialogueContainer>(fullPath);
        if (!dCont)
        {
            h.Out("dialogue " + fullPath + " not found");
            return;
        }

        if (!talkable)
        {
            h.Out("no talkable");
            return;
        }
        
        DialogueContainer dInstance = Instantiate(dCont);
        talkable.InitDialogue(dInstance);

        StartDialogue(dInstance, talkable);
    }

    public void StartAllyDialogue(string path)
    {
        string fullPath = alliesLabel + path;
        if (!h.ResourceExists(pathLabel + fullPath))
        {
            h.Out(pathLabel + fullPath + " not found");
            fullPath = alliesLabel + placeholderLabel;
        }
        StartDialogue(fullPath);
    }

    public void StartAllyDialogue(string path, Talkable talkable)
    {
        string fullPath = alliesLabel + path;
        if (!h.ResourceExists(pathLabel + fullPath))
        {
            h.Out(pathLabel + fullPath + " not found.");
            fullPath = alliesLabel + placeholderLabel;
        }
        StartDialogue(fullPath, talkable);
    }
    
    // MAIN OVERRIDE IDK
    public void StartDialogue(DialogueContainer dialogueIn, Talkable source = null)
    {
        ClearTextOverride();
        StartDialogueInternal(dialogueIn, source);
    }

    /// <summary>
    /// Runs a dialogue whose text is typed into <paramref name="textTarget"/> instead of the dialogue
    /// window. The window is never opened, the portrait and speaker title are left alone, and input
    /// (the advance keys/buttons, L to end) works as usual.
    /// </summary>
    /// <param name="voiceClips">Typing voice for every node. Null or empty = each node's speaker voice.</param>
    public void StartDialogue(DialogueContainer dialogueIn, TextMeshProUGUI textTarget,
        List<AudioClip> voiceClips = null, Talkable source = null)
    {
        if (!textTarget)
        {
            StartDialogue(dialogueIn, source);
            return;
        }

        textOverride = textTarget;
        scalingTextOverride = textTarget.GetComponent<ScalingText>();
        textAnimationOverride = textTarget.GetComponent<TextAnimation>();
        textOverrideDefaultColor = textTarget.color;
        voiceClipsOverride = voiceClips != null && voiceClips.Count > 0 ? voiceClips : null;

        StartDialogueInternal(dialogueIn, source);
    }

    private void ClearTextOverride()
    {
        textOverride = null;
        scalingTextOverride = null;
        textAnimationOverride = null;
        voiceClipsOverride = null;
    }

    private void StartDialogueInternal(DialogueContainer dialogueIn, Talkable source)
    {
        initiatingTalkable = source;
        dialogueActive = true;
        LastDialogueSkipped = false;
        windowHidden = false;
        if (!textOverride) dialogueWindow.SetActive(true);
        dialogue = dialogueIn;
        currentParagraphs = new Queue<DialogueNode>(dialogueIn.nodes);

        NodesStarted = 0;
        typingState = TypingState.notStarted;

        SetNodeText("");

        onDialogueStart?.Invoke();

        if (endDialogueCoroutine != null)
        {
            StopCoroutine(endDialogueCoroutine);
            endDialogueCoroutine = null;
        }

        // Play the open animation first; don't show the first paragraph until it finishes.
        if (startDialogueCoroutine != null) StopCoroutine(startDialogueCoroutine);
        startDialogueCoroutine = StartCoroutine(StartDialogueRoutine());
    }

    // Kept so a dialogue ended (skipped) during its open animation doesn't start its first node afterwards.
    private Coroutine startDialogueCoroutine;

    private IEnumerator StartDialogueRoutine()
    {
        if (windowAnimations && !textOverride) yield return windowAnimations.OnDialogueStart();
        // The open animation is done; the dialogue actually starts now.
        initiatingTalkable?.OnDialogueStart();
        // Fire the on-dialogue-start lifecycle on every object of a multi-object dialogue.
        (dialogue as IMultiObjectDialogue)?.OnDialogueStart();
        startDialogueCoroutine = null;
        DisplayNextParagraph();
    }

    public void DisplayNextParagraph()
    {
        // The current node is still bringing its objects on screen - don't advance past it.
        if (nodeStarting) return;

        // Held on this node until whatever put the hold there lets go (see HoldAdvance). Checked here
        // as well as in Update so a call from code can't slip past a popup either.
        if (advanceHolds > 0) return;

        if (typingState == TypingState.pausedAtStop)
        {
            ResumeTyping();
            return;
        }

        if (typingState == TypingState.active)
        {
            if (HasStopTagAhead())
            {
                skipToNextStopRequested = true;
                return;
            }
            StopTyping();
            return;
        }
        if (typingState == TypingState.finished)
        {
            // Advanced before the auto skip got to it (or by it): don't let it fire again on the next node.
            StopAutoSkip();
            typingState = TypingState.notStarted;

            DisplayNextParagraph();
            return;
        }
        
        if (currentParagraphs.Count <= 0 || !dialogueActive)
        {
            EndDialogue();
            return;
        }
        
        if (typingState == TypingState.notStarted)
        {
            StartParagraph();
        }
    }

    // ================================================================ Jumping around

    /// <summary>
    /// Jumps straight to the LAST node of the running dialogue, throwing away every node in between —
    /// how a "skip this" choice keeps the closing line without making the player read the rest.
    /// </summary>
    /// <returns>
    /// False when there was nothing to skip: no dialogue running, or the last node is already the one
    /// on screen.
    /// </returns>
    public bool SkipToLastNode()
    {
        if (!dialogueActive || dialogue == null) return false;

        // Nothing queued behind the current node = the last node IS the current node.
        if (currentParagraphs.Count == 0) return false;

        while (currentParagraphs.Count > 1) currentParagraphs.Dequeue();

        AbandonCurrentNode();
        DisplayNextParagraph();
        return true;
    }

    /// <summary>
    /// Drops the node on screen and leaves the manager ready to start the next one.
    ///
    /// <para>The skipped node still gets its end lifecycle (<see cref="StopTyping"/> fires onNodeEnd),
    /// but only when it was actually typing: a node whose text had already finished fired it once
    /// already, and a node still waiting on its objects' appearing animations never fired onNodeStart,
    /// so neither may fire it a second time.</para>
    /// </summary>
    private void AbandonCurrentNode()
    {
        // Otherwise this coroutine would start typing into the node we are about to replace.
        if (startParagraphCoroutine != null) StopCoroutine(startParagraphCoroutine);
        startParagraphCoroutine = null;
        nodeStarting = false;

        if (typingState == TypingState.active || typingState == TypingState.pausedAtStop) StopTyping();
        StopAutoSkip();

        typingState = TypingState.notStarted;
    }

    // ================================================================ Auto skip

    /// <summary>
    /// Arms the auto skip for the node whose text just finished, if it (or the manager, see
    /// <see cref="autoSkip"/>) asks for one.
    /// </summary>
    private void StartAutoSkip(DialogueNode node)
    {
        StopAutoSkip();
        if (!dialogueActive || node == null) return;

        float delay;
        if (node.autoSkip) delay = node.autoSkipDelay;
        else if (autoSkip) delay = autoSkipDelay;
        else return;

        autoSkipCoroutine = StartCoroutine(AutoSkipRoutine(Mathf.Max(0f, delay), NodesStarted));
    }

    private void StopAutoSkip()
    {
        if (autoSkipCoroutine != null) StopCoroutine(autoSkipCoroutine);
        autoSkipCoroutine = null;
    }

    private IEnumerator AutoSkipRoutine(float delay, int nodeNumber)
    {
        // Unscaled: dialogues also run over a paused game. The countdown freezes while something holds
        // the dialogue on this node (a choice popup, the collapsed window) and resumes once it lets go.
        float waited = 0f;
        while (waited < delay || advanceHolds > 0)
        {
            yield return null;
            if (advanceHolds == 0) waited += Time.unscaledDeltaTime;
        }

        autoSkipCoroutine = null;

        // Only if we're still on the node this was armed for, its text typed out.
        if (!dialogueActive || NodesStarted != nodeNumber || typingState != TypingState.finished) yield break;
        DisplayNextParagraph();
    }

    private void SetNodeText(string text)
    {
        SetText(ActiveScalingText, ActiveText, text);
    }

    /// <summary>
    /// Empties the text box and hides the skip icon without touching the dialogue's progress -
    /// e.g. while a visual novel changes location between two nodes. The next node types into it
    /// as usual.
    /// </summary>
    /// <summary>A node's text is being typed out right now.</summary>
    public bool IsTyping => typingState == TypingState.active;

    /// <summary>
    /// Hides the letters the typewriter hasn't reached yet again, after something made TMP rebuild the
    /// text's mesh mid-typing (a resize of the window does) — a rebuild shows every letter until the
    /// typewriter's next update. Needs a <see cref="TextAnimation"/> on the text, which keeps them.
    /// </summary>
    public void RefreshTypingText()
    {
        if ((typingState == TypingState.active || typingState == TypingState.pausedAtStop) && ActiveTextAnimation)
            ActiveTextAnimation.ForceUpdate();
    }

    private void ResumeTyping()
    {
        DisableSkipIcon();
        typingState = TypingState.active;
        if (windowAnimations && !textOverride && !windowHidden) windowAnimations.PlayTypingStart();
    }

    public static bool IsStopTag(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return false;
        if (!tag.StartsWith("<") || !tag.EndsWith(">")) return false;
        string inner = tag.Substring(1, tag.Length - 2).Trim();
        if (inner.EndsWith("/")) inner = inner.Substring(0, inner.Length - 1).Trim();
        return inner.Equals(HTML.DIALOGUE_STOP_TAG, StringComparison.OrdinalIgnoreCase)
            || inner.StartsWith(HTML.DIALOGUE_STOP_TAG + " ", StringComparison.OrdinalIgnoreCase);
    }

    private bool HasStopTagAhead()
    {
        if (string.IsNullOrEmpty(currentTypingText)) return false;
        int startIndex = Mathf.Clamp(currentTextIndex, 0, currentTypingText.Length);
        
        int searchIdx = startIndex;
        while (searchIdx < currentTypingText.Length)
        {
            int tagStart = currentTypingText.IndexOf('<', searchIdx);
            if (tagStart == -1) break;

            int tagEnd = currentTypingText.IndexOf('>', tagStart);
            if (tagEnd == -1) break;

            string tag = currentTypingText.Substring(tagStart, tagEnd - tagStart + 1);
            if (IsStopTag(tag))
            {
                return true;
            }

            searchIdx = tagEnd + 1;
        }

        return false;
    }

    private IEnumerator WaitForSecondsOrSkip(float duration)
    {
        if (skipToNextStopRequested || typingState != TypingState.active) yield break;
        float waited = 0f;
        while (waited < duration && !skipToNextStopRequested && typingState == TypingState.active)
        {
            yield return null;
            waited += Time.unscaledDeltaTime;
        }
    }

    public void ClearNodeText()
    {
        SetNodeText("");
        DisableSkipIcon();

        // The next node's speaker only replaces the name once it starts typing; don't let the
        // previous speaker's name show in an otherwise empty box until then.
        if ((portraitScalingTitle || portraitTitle) && !textOverride)
            SetText(portraitScalingTitle, portraitTitle, "");
    }
    
    private IEnumerator TypingDialogue(
        string textIn,
        ScalingText scalingTextBox,
        TextMeshProUGUI defaultTextBox,
        List<AudioClip> audioClips=null
        )
    {
        typingState = TypingState.active;
        skipToNextStopRequested = false;
        currentTypingText = textIn;
        currentTextIndex = 0;

        // Slice textIn by color tags
        Queue<string> textSlicesByColor = new Queue<string>();
        string colorTagPattern = @"(<color[^>]*>)";
        System.Text.RegularExpressions.Regex colorRegex = new System.Text.RegularExpressions.Regex(colorTagPattern);

        string[] parts = colorRegex.Split(HTML.RemoveUniqueTags(textIn));

// Recombine each tag with the text that follows it
        for (int i = 0; i < parts.Length; i++)
        {
            if (colorRegex.IsMatch(parts[i]) && i + 1 < parts.Length)
            {
                textSlicesByColor.Enqueue(parts[i] + parts[i + 1]);
                i++; // skip the next part, already consumed
            }
            else if (!string.IsNullOrEmpty(parts[i]))
            {
                textSlicesByColor.Enqueue(parts[i]);
            }
        }

        //h.Out(textSlicesByColor, textSlicesByColor.Count);

        string filteredText = "";
        
        void AddSplit()
        {
            if (textSlicesByColor.Count == 0) return;
            filteredText += textSlicesByColor.Dequeue();
        }

        
        // n
        AddSplit();
        
        string displayedText = "";
        int alphaIndex = 0; // position in filteredText, rich-text tags included
        int charIndex = 0;  // TMP character index of the next letter to show (tags don't count)

        if (ActiveTextAnimation)
        {
            // The previous node's letters may still be trembling; their indices mean other letters now.
            ActiveTextAnimation.ClearAppear();
            ActiveTextAnimation.SetAwake();
            ActiveTextAnimation.ForceUpdate(filteredText); // generate dict keys for animations
            ActiveTextAnimation.SetSleep();
        }
        
        filteredText = HTML.RemoveTags(filteredText, targetTag:HTML.ANIMATION_TAG);
        textIn = HTML.RemoveTags(textIn, targetTag: HTML.ANIMATION_TAG);
        currentTypingText = textIn;
        
        SetText(scalingTextBox, defaultTextBox, filteredText);
        // _textAnimationComponent.cleaned = true;
        int characterCount = textIn.Length;
        // foreach (char c in textIn)

        bool playVoice = audioClips != null && audioClips.Count > 0;
        int voiceCharCounter = 0;

        InvisibleText.HideAllCharacters(defaultTextBox);

        // HideAllCharacters only records the alpha=0 overrides; they are pushed to the
        // mesh inside TextAnimation's next Update. Force that apply now so the fully
        // visible text is never rendered for a frame before typing begins (the "flash").
        if (ActiveTextAnimation) ActiveTextAnimation.ForceUpdate();

        for (int i = 0; i < characterCount; i++)
        {
            currentTextIndex = i;
            char c =  textIn[i];
            
            // Process unique tags
            if (c == '<')
            {
                //todo: mb fix, so it detects only tags, not string like: hello! '< priiiiii, hhasdfhs >'
                int endIndex = textIn.IndexOf('>', i);
                if (endIndex != -1)
                {
                    string tag = textIn.Substring(i, endIndex - i + 1);
                    //h.Out(tag);
                    // Check if tag is in HTML.uniqueTags
                    bool isUniqueTag = false;
                    foreach (string uniqueTag in HTML.ALL_UNIQUE_TAGS)
                    {
                        if (tag.StartsWith("<" + uniqueTag))
                        {
                            isUniqueTag = true;
                            break;
                        }
                    }
                    
                    // If not a unique tag, skip it
                    if (!isUniqueTag)
                    {
                        alphaIndex += tag.Length;
                        // A rich-text tag (<i>, <color>, ...) is no character of TMP's own;
                        // a <sprite> is one.
                        if (tag.StartsWith("<sprite"))
                        {
                            if (skipToNextStopRequested)
                                InvisibleText.ShowCharacterById(defaultTextBox, charIndex++);
                            else
                                ShowTypedCharacter(defaultTextBox, charIndex++);
                        }
                        i = endIndex;
                        continue;
                    }

                    // Process stop tag
                    if (IsStopTag(tag))
                    {
                        i = endIndex;
                        currentTextIndex = endIndex + 1;
                        skipToNextStopRequested = false;

                        if (ActiveTextAnimation) ActiveTextAnimation.ForceUpdate();

                        typingState = TypingState.pausedAtStop;
                        EnableSkipIcon();

                        while (typingState == TypingState.pausedAtStop)
                        {
                            yield return null;
                        }

                        DisableSkipIcon();
                        continue;
                    }
                    
                    // process unique tags (e.g., pause)
                    // Process pause
                    if (tag.Contains(HTML.DIALOGUE_PAUSE_TAG))
                    {
                        int l = HTML.DIALOGUE_PAUSE_TAG.Length + 2; // pause + =

                        string pauseStr = textIn.Substring(i + l, endIndex - i - (l));
                        if (float.TryParse(pauseStr, out float pauseTime))
                        {
                            if (!skipToNextStopRequested)
                            {
                                yield return WaitForSecondsOrSkip(pauseTime);
                            }
                            i = endIndex;
                            continue;
                        }
                    }
                    
                }
            }
            
            if (skipToNextStopRequested)
            {
                InvisibleText.ShowCharacterById(defaultTextBox, charIndex);
            }
            else
            {
                ShowTypedCharacter(defaultTextBox, charIndex);
                // Sound FX of typing
                if (playVoice) TryPlayVoice(audioClips, c, ref voiceCharCounter);
            }
            charIndex++;
            alphaIndex++;
            // SetText(dialogueScalingText, dialogueText, originalText);
            // displayedText = dialogueText.text.originalText.Insert(alphaIndex, HTML_ALPHA);
            
            // // change char alpha instead
            // displayedText = filteredText.Insert(alphaIndex, HTML.ALPHA);
            // SetText(scalingTextBox, defaultTextBox, displayedText);

            if (alphaIndex == filteredText.Length)
            {
                AddSplit();
            }

            // _textAnimationComponent.cleaned = true;
            if (!skipToNextStopRequested)
            {
                yield return WaitForSecondsOrSkip(MAX_TYPE_SPEED / typeSpeed);

                // Breathe after punctuation, on top of the normal per-character delay.
                if (!skipToNextStopRequested)
                {
                    float punctuationPause = GetPauseAfterChar(textIn, i);
                    if (punctuationPause > 0f) yield return WaitForSecondsOrSkip(punctuationPause);
                }
            }
        }
        skipToNextStopRequested = false;
        StopTyping();
    }

    /// <summary>Reveals the typewriter's next letter, trembling in if <see cref="typingAnimation"/> is on.</summary>
    private void ShowTypedCharacter(TextMeshProUGUI textBox, int charIndex)
    {
        InvisibleText.ShowCharacterById(textBox, charIndex);
        if (typingAnimation && ActiveTextAnimation) ActiveTextAnimation.PlayAppear(charIndex, typingTremble);
    }

    /// <summary>
    /// Plays a typing voice clip for the character just shown, according to <see cref="voicePlaybackMode"/>.
    /// </summary>
    private void TryPlayVoice(List<AudioClip> audioClips, char c, ref int charCounter)
    {
        if (!SFXManager.Instance) return;

        switch (voicePlaybackMode)
        {
            case VoicePlaybackMode.WaitForPrevious:
                if (voiceSource && voiceSource.isPlaying) return;
                break;

            case VoicePlaybackMode.EveryNCharacters:
                if (voiceSkipWhitespace && char.IsWhiteSpace(c)) return;
                // Due every Nth character, or as soon as the previous clip has finished playing,
                // so the voice never goes quiet between due characters.
                bool previousDone = !voiceSource || !voiceSource.isPlaying;
                bool due = previousDone || charCounter % Mathf.Max(1, voiceEveryNCharacters) == 0;
                if (!due)
                {
                    charCounter++;
                    return;
                }
                // Count the next N characters from this clip.
                charCounter = 1;
                if (voiceSource) voiceSource.Stop();
                break;
        }

        AudioClip clip = h.RandChoice(audioClips);
        // if (debug) h.Out($"[DialogueManager] Voice clip: {(clip ? clip.name : "null")}");
        voiceSource = SFXManager.Instance.PlayClip(clip,
            volumeIn: voiceVolume * nodeVoiceVolumeScale,
            randomPitchRange: nodeVoicePitch ?? voiceRandomPitchRange,
            playIndependently: false);
    }

    /// <summary>
    /// Extra wait to insert after the character at <paramref name="index"/>, from the
    /// moderate/small punctuation pause lists. 0 when that character isn't a pause character.
    /// </summary>
    private float GetPauseAfterChar(string text, int index)
    {
        char c = text[index];

        float duration;
        if (IsPauseChar(moderatePauseChars, c)) duration = moderatePauseDuration;
        else if (IsPauseChar(smallPauseChars, c)) duration = smallPauseDuration;
        else return 0f;

        if (duration <= 0f) return 0f;

        // Last character of the node - the text is done, no point stalling on it.
        if (index >= text.Length - 1) return 0f;

        char next = text[index + 1];

        // Runs like "..." or "?!" pause once, at the end of the run, not after every mark.
        if (IsPauseChar(moderatePauseChars, next) || IsPauseChar(smallPauseChars, next)) return 0f;

        // Decimals and thousands separators ("3.5", "1,000") aren't punctuation.
        if (char.IsDigit(next) && index > 0 && char.IsDigit(text[index - 1])) return 0f;

        return duration;
    }

    private static bool IsPauseChar(List<string> pauseChars, char c)
    {
        if (pauseChars == null) return false;
        for (int i = 0; i < pauseChars.Count; i++)
        {
            string s = pauseChars[i];
            if (!string.IsNullOrEmpty(s) && s.IndexOf(c) >= 0) return true;
        }
        return false;
    }

    private void StartParagraph()
    {
        paragraph = currentParagraphs.Dequeue();
        if (paragraph == null)
        {
            h.Out("Paragraph == null!!!");
            DisplayNextParagraph();
        }

        if (!string.IsNullOrEmpty(paragraph.speakerId)
            && dialogue != null
            && dialogue.speakers.TryGetValue(paragraph.speakerId, out var speakerGo)
            && speakerGo)
        {
            paragraph.InitFromGameObject(speakerGo);
        }

        // Resolved in StartParagraphRoutine, once the node's speaker is final.
        currentNodeTalkable = null;

        // A node may first have to bring new objects on screen; the node itself only starts
        // once their appearing animations have finished.
        startParagraphCoroutine = StartCoroutine(StartParagraphRoutine());
    }

    /// <summary>
    /// Runs a node's enter/exit transitions first (multi-object dialogues: objects this node
    /// enables play their appearing animation), and only once they are done fires the node-start
    /// lifecycle and begins typing the text.
    /// </summary>
    private IEnumerator StartParagraphRoutine()
    {
        nodeStarting = true;

        // Multi-object dialogues (DialogueContainerWithModels / DialogueContainerWithArmature)
        // drive every object in their objects list from the node's per-object settings. This
        // yields until any newly enabled object has finished appearing, and applies a per-node
        // speaker override before the portrait is read below.
        IMultiObjectDialogue modelsDialogue = dialogue as IMultiObjectDialogue;
        if (modelsDialogue != null) yield return modelsDialogue.OnNodeStart(paragraph, this);

        // If this node's speaker is a Talkable, let it react to the node lifecycle. Resolved
        // here rather than in StartParagraph because the container above is what decides the
        // node's final speaker.
        //
        // A multi-object dialogue is authoritative for the objects it drives: it has just
        // applied this node's model/eyes/talking state to every one of them. Running the
        // speaker's own Talkable.OnNodeStart on top would re-apply that object's private
        // nodesAdditionalSettings - empty for container-driven objects, so it falls back to
        // model 0 - and overwrite the model the container had selected one frame earlier.
        if (modelsDialogue == null
            && paragraph.speakerGameObject
            && paragraph.speakerGameObject.TryGetComponent<Talkable>(out var nodeTalkable))
        {
            currentNodeTalkable = nodeTalkable;
        }

        if (onBeforeNodeTyping != null)
        {
            int nodeIndex = dialogue != null && dialogue.nodes != null ? dialogue.nodes.IndexOf(paragraph) : NodesStarted;
            yield return onBeforeNodeTyping(paragraph, nodeIndex);
        }

        nodeStarting = false;

        // The dialogue may have been ended while we were waiting for the animations.
        if (!dialogueActive) yield break;

        NodesStarted++;

        paragraph?.onNodeStart?.Invoke();
        onStartNode?.Invoke();
        currentNodeTalkable?.OnNodeStart(paragraph);

        // A dialogue typed into a custom text target has no window, so no portrait/title to fill.
        if (portraitImage && !textOverride)
        {
            portraitImage.sprite = paragraph.speakerPortrait;
            portraitImage.material = defaultMaterial;
            if (paragraph.material) portraitImage.material = paragraph.material;
        }

        if ((portraitScalingTitle || portraitTitle) && !textOverride)
            SetText(portraitScalingTitle, portraitTitle, paragraph.speakerName);

        // Must happen before typing starts: changing TMP's color regenerates the mesh, which
        // would otherwise wipe the per-character alpha overrides the typewriter relies on.
        ApplySpeakerTextColor(paragraph);

        if (windowAnimations && !textOverride && !windowHidden) windowAnimations.PlayTypingStart();

        nodeVoiceVolumeScale = paragraph.voiceVolumeScale ?? 1f;
        nodeVoicePitch = paragraph.voicePitchOverride;

        typeCoroutine = StartCoroutine(
            TypingDialogue(
                paragraph.text,
                ActiveScalingText,
                ActiveText,
                audioClips:voiceClipsOverride
                           ?? paragraph.voiceOverride
                           ?? (dialogue && dialogue.HasDefaultVoice ? dialogue.defaultVoiceClips : null)
                           ?? paragraph.speakerVoiceClips
            )
        );
    }
    
    /// <summary>
    /// Tints the dialogue text with the current speaker's color, or restores the window's
    /// own default color when this node's speaker doesn't define one.
    /// </summary>
    private void ApplySpeakerTextColor(DialogueNode node)
    {
        Color color = node != null && node.useCustomTextColor
            ? node.textColor
            : textOverride ? textOverrideDefaultColor : defaultTextColor;

        if (ActiveText) ActiveText.color = color;

        if (tintTitleWithSpeakerColor && portraitTitle && !textOverride)
            portraitTitle.color = node != null && node.useCustomTextColor
                ? node.textColor
                : defaultTitleColor;
    }

    private void StopTyping()
    {
        skipToNextStopRequested = false;
        if (typeCoroutine != null) StopCoroutine(typeCoroutine);
        typeCoroutine = null;
        if (paragraph != null) SetText(ActiveScalingText, ActiveText,
            HTML.RemoveTags(HTML.RemoveUniqueTags(paragraph.text), targetTag: HTML.ANIMATION_TAG));
        typingState = TypingState.finished;
        paragraph?.onNodeEnd?.Invoke();
        onEndNode?.Invoke();
        currentNodeTalkable?.OnNodeEnd(paragraph);
        (dialogue as IMultiObjectDialogue)?.OnNodeEnd(paragraph);
        InvisibleText.ShowAllCharacters(ActiveText);

        // Counted from here: the text is fully on screen now, whether it typed out or the player cut it short.
        StartAutoSkip(paragraph);
    }
    

    public void EndDialogue(bool animate = true)
    {
        typingState = TypingState.notStarted;
        dialogueActive = false;
        StopAutoSkip();
        skipToNextStopRequested = false;
        if (typeCoroutine != null) StopCoroutine(typeCoroutine);
        typeCoroutine = null;

        // A hold belongs to the dialogue that was running; leaving one behind would freeze the NEXT
        // dialogue on its first node.
        ClearAdvanceHolds();

        // Ended during the open animation (a skip): don't let it start the first node afterwards.
        if (startDialogueCoroutine != null) StopCoroutine(startDialogueCoroutine);
        startDialogueCoroutine = null;

        // A node may still be waiting on its objects' appearing animations - drop it, otherwise
        // it would start typing into a closing window.
        if (startParagraphCoroutine != null) StopCoroutine(startParagraphCoroutine);
        startParagraphCoroutine = null;
        nodeStarting = false;

        // Otherwise the next dialogue opens with the icon still up (or pops it up after this one ended).
        DisableSkipIcon();

        // Also when the window just snaps shut: an accent still running would leave its offset behind.
        if (windowAnimations) windowAnimations.StopAccents();

        // Run each driven object's end lifecycle (end animation + events, then reset/disable)
        // before we drop the ref.
        (dialogue as IMultiObjectDialogue)?.OnDialogueEnd();

        dialogue = null;
        currentParagraphs.Clear();
        onBeforeNodeTyping = null;

        // Don't leave the window tinted by whoever spoke last.
        ApplySpeakerTextColor(null);

        onDialogueEnd?.Invoke();
        initiatingTalkable?.OnDialogueEnd();
        initiatingTalkable = null;

        // A dialogue typed into a custom text target never opened the window - nothing to close.
        if (textOverride)
        {
            ClearTextOverride();
            return;
        }

        if (endDialogueCoroutine != null)
        {
            StopCoroutine(endDialogueCoroutine);
            endDialogueCoroutine = null;
        }

        // The dialogue ends and the close animation runs at the same time (unlike start,
        // which waits). The window is only deactivated once the animation finishes.
        if (animate && windowAnimations && dialogueWindow.activeInHierarchy)
            endDialogueCoroutine = StartCoroutine(EndDialogueRoutine());
        else
            dialogueWindow.SetActive(false);
    }

    // ================================================================ Hiding the window mid-dialogue

    // True from the moment HideWindowRoutine starts until ShowWindowRoutine brings the window back.
    // Tracked separately from activeSelf: a close animation that was cut short leaves the window
    // active but shrunk, and it must still count as hidden.
    private bool windowHidden;

    /// <summary>
    /// Closes the dialogue window WITHOUT ending the dialogue (close animation when there is a
    /// <see cref="DialogueWindowAnimations"/>, then deactivated) - e.g. while a visual novel changes
    /// location. Bring it back with <see cref="ShowWindowRoutine"/>. No-op for a dialogue typed into
    /// a custom text target, which has no window.
    /// </summary>
    public IEnumerator HideWindowRoutine()
    {
        if (textOverride || !dialogueWindow || !dialogueActive) yield break;
        if (windowHidden || !dialogueWindow.activeSelf) { windowHidden = true; yield break; }

        windowHidden = true;
        DisableSkipIcon();

        if (windowAnimations && dialogueWindow.activeInHierarchy) yield return windowAnimations.OnDialogueEnd();
        dialogueWindow.SetActive(false);
    }

    /// <summary>Reopens a window closed by <see cref="HideWindowRoutine"/>, open animation included.</summary>
    public IEnumerator ShowWindowRoutine()
    {
        if (textOverride || !dialogueWindow || !dialogueActive) yield break;
        if (!windowHidden && dialogueWindow.activeSelf) yield break;

        windowHidden = false;
        dialogueWindow.SetActive(true);

        if (windowAnimations) yield return windowAnimations.OnDialogueStart();
    }

    private IEnumerator EndDialogueRoutine()
    {
        yield return windowAnimations.OnDialogueEnd();
        dialogueWindow.SetActive(false);
        endDialogueCoroutine = null;
    }
}

// current ver