using System.Collections;
using System.Collections.Generic;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Plays visual-novel dialogues (<see cref="VisualNovelDialogueContainer"/>) and owns their stage:
/// the background layer, the ambience layer, the shake target and the blur volume. Works like
/// TalkableWithArmatureController - Talk starts a dialogue, the controller binds itself to it, and
/// the DialogueManager then drives the nodes while the container forwards each node here.
///
/// Per node (see <see cref="VisualNovelNodeSettings"/>):
/// <list type="number">
/// <item>Sustained effects of the previous node that this node doesn't continue are released
/// (unless it keeps them all - keepPreviousSustainedEffects), and the previous node's pending
/// SFX / unfinished start effects are cancelled.</item>
/// <item>Music and ambience change.</item>
/// <item>The background transition starts.</item>
/// <item>SFX and start effects fire - after the transition, or with it. Sustained effects start
/// with it too, or as soon as the new location is in place. Blur effects never run during a
/// FadeThroughBlur / CrossfadeBlur transition (running ones pause for it). SFX timed by the
/// transition (VisualNovelSfxCue.timing) play part-way through it instead.</item>
/// <item>The text starts typing once the transition and any blocking start effect are done.</item>
/// </list>
///
/// Keep this component (and the ambience) OUTSIDE <see cref="root"/>: the dialogue-end fade runs
/// on it after the root is told to hide.
/// </summary>
public class VisualNovelTalkable : MonoBehaviour
{
    [Tooltip("Debug only: the digit keys (top row or numpad) start a dialogue - 1 plays dialogues[0], " +
             "2 plays dialogues[1], ..., 9 plays dialogues[8], 0 plays dialogues[9].")]
    [SerializeField] private bool debug = false;

    /// <summary>Debug hotkeys, in dialogue order: index i starts dialogues[i].</summary>
    private static readonly Key[] DebugDigitKeys =
    {
        Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
        Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9, Key.Digit0,
    };

    private static readonly Key[] DebugNumpadKeys =
    {
        Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5,
        Key.Numpad6, Key.Numpad7, Key.Numpad8, Key.Numpad9, Key.Numpad0,
    };

    [Header("Dialogues")]
    public List<VisualNovelDialogueContainer> dialogues = new List<VisualNovelDialogueContainer>();
    [SerializeField] private DialogueManager dialogueManager;

    [Tooltip("Optional narrator: nodes marked 'speaker is this' take its name, portrait, voice and " +
             "text color. Nodes with their own DialogueSpeaker / speaker GameObject keep theirs.")]
    public Talkable narrator;

    [Tooltip("Typing voice for nodes whose speaker has no voice of its own, used instead of the " +
             "SFXManager's default dialogue voice. Empty = the SFXManager's default.")]
    public List<AudioClip> defaultVoiceClips = new List<AudioClip>();

    [Tooltip("Optional: type the text into this component instead of the dialogue window (the " +
             "window, portrait and speaker name are then left alone).")]
    [SerializeField] private TextMeshProUGUI textTarget;

    [Header("Text box")]
    [Tooltip("Play the first node's transition (and its audio and cues) before the dialogue window " +
             "opens, so the text box only appears once the first location is on screen.")]
    [SerializeField] private bool openTextBoxAfterFirstTransition = true;
    // What the text box does on later transitions is set per node: VisualNovelNodeSettings.textBox.

    [Header("Stage")]
    [Tooltip("Root of the visual novel's UI. Activated when a dialogue starts.")]
    [SerializeField] private GameObject root;
    [Tooltip("Deactivate the root once a dialogue has ended (after its end fade).")]
    [SerializeField] private bool hideRootOnEnd = true;
    [SerializeField] private VisualNovelBackground background;
    [Tooltip("Added to this GameObject when left empty.")]
    [SerializeField] private VisualNovelAmbience ambience;
    [Tooltip("What the Shake effect moves (Metro Shake moves the background's layers instead). " +
             "Defaults to the background's RectTransform.")]
    [SerializeField] private RectTransform shakeTarget;
    [Tooltip("Blur used by blur effects. Defaults to BlurVolumeManager.Instance.")]
    [SerializeField] private BlurVolumeManager blur;

    [Header("Skip hint (returning players)")]
    [Tooltip("On: show the skip hint for returning players (or when replaying from EndingMenu / play again).")]
    [SerializeField] private bool showSkipHintForReturningPlayers = true;
    [Tooltip("Shown as the dialogue's first node starts playing if the player has played before, so they know it " +
             "can be skipped. Keep it on a canvas; it's hidden on load. Empty: the DialogueManager's hold-to-skip indicator is shown instead.")]
    [SerializeField] private GameObject skipHint;
    [Tooltip("The hint goes away when the dialogue starts this node, counted from 1. 2 = on the first node only; 1 = never.")]
    [SerializeField, Min(1)] private int hideSkipHintAtNode = 2;
    [Tooltip("Seconds the hint fades in/out (through a CanvasGroup, added if missing). 0: instant.")]
    [SerializeField, Min(0f)] private float skipHintFadeDuration = 0.25f;

    [Header("Events")]
    public UnityEvent onDialogueStart = new UnityEvent();
    public UnityEvent onDialogueEnd = new UnityEvent();

    /// <summary>Runtime copy of the dialogue being played, or null.</summary>
    public VisualNovelDialogueContainer CurrentDialogue { get; private set; }

    public bool IsPlaying => CurrentDialogue;

    public VisualNovelBackground Background => background;

    public RectTransform ShakeTarget => shakeTarget;

    /// <summary>Where the shake target sits at rest; shakes are offsets from it.</summary>
    public Vector3 ShakeTargetRestPosition => shakeTargetRestPosition;

    public BlurVolumeManager Blur
    {
        get
        {
            if (!blur) blur = BlurVolumeManager.Instance;
            if (!blur) blur = FindFirstObjectByType<BlurVolumeManager>();
            return blur;
        }
    }

    // Rest pose of the shake target, so shakes always start from and return to it.
    private Vector3 shakeTargetRestPosition;

    // Per-effect runtime state (effects are shared assets and can't keep it themselves).
    private readonly Dictionary<VisualNovelEffect, object> effectState = new Dictionary<VisualNovelEffect, object>();

    // Sustained effects running right now, by asset.
    private readonly Dictionary<VisualNovelEffect, Coroutine> sustained = new Dictionary<VisualNovelEffect, Coroutine>();

    // Start effects of the current node, so unfinished ones can be cut short and released.
    private readonly List<OneShot> oneShots = new List<OneShot>();

    // Finished start effects that hold their end state (ReleaseAtNodeEnd) from a node that is over,
    // released once the next node's own start effects have fired - so a zoom handed on to the next
    // node's zoom goes straight from one to the other instead of snapping back in between.
    private readonly List<OneShot> heldShots = new List<OneShot>();

    // The current node's cue routine (fires its SFX and effects, possibly after a delay).
    private Coroutine cuesRoutine;
    private CueRun currentRun;
    private readonly List<Coroutine> delayedSfx = new List<Coroutine>();

    private Coroutine endRoutine;
    private Coroutine startRoutine;

    // Called once the current dialogue is over (see PlayDialogue(dialogue, onFinished)).
    private System.Action finishedCallback;

    // Hide the stage before finishedCallback runs instead of leaving it up for the callback.
    private bool hideStageBeforeCallback;

    // The first node, already staged before the window opened; PlayNode skips it once.
    private DialogueNode preStagedNode;

    private bool _returningPlayer;
    private int _nodesShown;
    private bool _skipHintShown;
    private UnityAction _onNodeStarted;

    private void Awake()
    {
        // The stage's blur transitions (FadeThroughBlur, CrossfadeBlur) use the same blur as the blur effects.
        if (background && blur) background.Blur = blur;
        if (!dialogueManager) dialogueManager = FindFirstObjectByType<DialogueManager>();
        if (!ambience) ambience = GetComponent<VisualNovelAmbience>();
        if (!ambience) ambience = gameObject.AddComponent<VisualNovelAmbience>();
        if (!shakeTarget && background) shakeTarget = background.transform as RectTransform;
        if (shakeTarget) shakeTargetRestPosition = shakeTarget.localPosition;
        if (skipHint) skipHint.SetActive(false);
        _onNodeStarted = OnNodeStarted;
    }

    private void OnDisable()
    {
        SetSkipHint(false);
        if (dialogueManager && _onNodeStarted != null) dialogueManager.onStartNode.RemoveListener(_onNodeStarted);
    }

    private void Start()
    {
        if (root && !IsPlaying) root.SetActive(false);
    }

    private void Update()
    {
        if (!DebugHotkeys.Allowed(debug)) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        for (int i = 0; i < DebugDigitKeys.Length; i++)
        {
            if (!keyboard[DebugDigitKeys[i]].wasPressedThisFrame &&
                !keyboard[DebugNumpadKeys[i]].wasPressedThisFrame) continue;

            DebugTalk(i);
            return;
        }
    }

    /// <summary>Debug hotkey: starts dialogues[<paramref name="index"/>], or logs why it can't.</summary>
    private void DebugTalk(int index)
    {
        if (dialogues == null || index >= dialogues.Count)
        {
            h.Out($"VisualNovelTalkable: no dialogue at index {index} on {name} " +
                  $"({dialogues?.Count ?? 0} assigned).");
            return;
        }

        PlayDialogue(dialogues[index]);
    }

    // ---------------------------------------------------------------------
    // Starting a dialogue
    // ---------------------------------------------------------------------

    /// <summary>Starts dialogue <paramref name="dialogueId"/> (random when out of range).</summary>
    public void Talk(int dialogueId = -1)
    {
        if (dialogues == null || dialogues.Count == 0)
        {
            h.Out("VisualNovelTalkable: no dialogues assigned.");
            return;
        }

        VisualNovelDialogueContainer dialogue =
            dialogueId >= 0 && dialogueId < dialogues.Count
                ? dialogues[dialogueId]
                : h.RandChoice(dialogues);

        PlayDialogue(dialogue);
    }

    public void PlayDialogue(VisualNovelDialogueContainer dialogue) => PlayDialogue(dialogue, null);

    /// <summary>
    /// Plays <paramref name="dialogue"/>; <paramref name="onFinished"/> is called once it is over (played
    /// through or skipped) and its end fade is done. With a callback the stage is NOT hidden afterwards: it is
    /// left as the dialogue ended it for whatever the callback does next (e.g. a fade and a scene load).
    /// </summary>
    public void PlayDialogue(VisualNovelDialogueContainer dialogue, System.Action onFinished) =>
        PlayDialogue(dialogue, onFinished, false);

    /// <summary>
    /// Like <see cref="PlayDialogue(VisualNovelDialogueContainer, System.Action)"/>; with
    /// <paramref name="hideStageOnFinish"/> the stage is hidden as usual before <paramref name="onFinished"/>
    /// runs - for gameplay that carries on in the same scene (the hub) rather than loading another one.
    /// </summary>
    public void PlayDialogue(VisualNovelDialogueContainer dialogue, System.Action onFinished, bool hideStageOnFinish)
    {
        if (!dialogue)
        {
            h.Out($"VisualNovelTalkable: dialogue is null on {name}.");
            return;
        }

        if (IsPlaying)
        {
            h.Out($"VisualNovelTalkable: {name} is already playing {CurrentDialogue.name}.");
            return;
        }

        if (!dialogueManager) dialogueManager = FindFirstObjectByType<DialogueManager>();
        if (!dialogueManager)
        {
            h.Out("VisualNovelTalkable: no DialogueManager in scene.");
            return;
        }

        string dialogueKey = dialogue ? dialogue.name : "";
        _returningPlayer = !string.IsNullOrEmpty(dialogueKey) && PlayHistory.HasPlayedDialogue(dialogueKey);
        if (!string.IsNullOrEmpty(dialogueKey)) PlayHistory.MarkDialoguePlayed(dialogueKey);
        _nodesShown = 0;

        // Play a runtime copy: filling in speakers writes into the nodes, and in the Editor that
        // would otherwise be saved into the asset.
        VisualNovelDialogueContainer instance = Instantiate(dialogue);
        instance.name = dialogue.name;
        InitSpeakers(instance);
        instance.Bind(this);
        CurrentDialogue = instance;
        finishedCallback = onFinished;
        hideStageBeforeCallback = hideStageOnFinish;

        PrepareStage(instance);

        startRoutine = StartCoroutine(StartRoutine(instance));
    }

    /// <summary>
    /// Stages the first node first when <see cref="openTextBoxAfterFirstTransition"/> is on, then
    /// hands the dialogue to the manager, which opens the window and types the first line.
    /// </summary>
    private IEnumerator StartRoutine(VisualNovelDialogueContainer dialogue)
    {
        if (openTextBoxAfterFirstTransition && dialogue.nodes.Count > 0 && dialogue.nodes[0] != null)
        {
            DialogueNode first = dialogue.nodes[0];
            yield return PlayNode(dialogue, first);
            // The window waits for the whole transition, even when the node itself wouldn't.
            if (background) yield return background.WaitForTransition();

            VisualNovelNodeSettings s = dialogue.SettingsFor(first.nodeId);
            if (s != null && s.textBoxDelay > 0f) yield return new WaitForSecondsRealtime(s.textBoxDelay);
            preStagedNode = first;
        }

        startRoutine = null;

        if (!dialogueManager) dialogueManager = FindFirstObjectByType<DialogueManager>();
        if (dialogueManager && _onNodeStarted != null)
        {
            dialogueManager.onStartNode.RemoveListener(_onNodeStarted);
            dialogueManager.onStartNode.AddListener(_onNodeStarted);
        }

        if (textTarget) dialogueManager.StartDialogue(dialogue, textTarget);
        else dialogueManager.StartDialogue(dialogue);
    }

    /// <summary>Fills each node's speaker from its DialogueSpeaker / GameObject, or the narrator.</summary>
    private void InitSpeakers(VisualNovelDialogueContainer dialogue)
    {
        if (narrator)
        {
            narrator.InitDialogue(dialogue);
            return;
        }

        dialogue.AssignNodeIds();
        for (int i = 0; i < dialogue.nodes.Count; i++)
            dialogue.nodes[i]?.Init();
    }

    /// <summary>Shows the stage and puts it in the dialogue's starting state.</summary>
    private void PrepareStage(VisualNovelDialogueContainer dialogue)
    {
        // A previous dialogue may still be fading out, its effects still running under the fade.
        if (endRoutine != null)
        {
            StopCoroutine(endRoutine);
            StopEffectsOnEnd(release: false);
        }
        endRoutine = null;

        if (root) root.SetActive(true);

        if (background)
        {
            background.AdditionalStretching = dialogue.AdditionalStretching;
            background.ResetState();
            if (dialogue.startCovered) background.Cover(dialogue.startColor);
        }

        ResetShakeTarget();
    }

    // ---------------------------------------------------------------------
    // Node lifecycle (called by VisualNovelDialogueContainer)
    // ---------------------------------------------------------------------

    public void OnDialogueStart(VisualNovelDialogueContainer dialogue)
    {
        onDialogueStart?.Invoke();
    }

    /// <summary>
    /// Stages a node. The DialogueManager yields on this, so the node's text waits for the
    /// transition (when waitForTransition is on) and for every start effect marked waitForCompletion.
    /// </summary>
    public IEnumerator PlayNode(VisualNovelDialogueContainer dialogue, DialogueNode node)
    {
        // Already staged before the window opened (see StartRoutine): don't replay it.
        if (node == preStagedNode)
        {
            preStagedNode = null;
            yield break;
        }

        CancelNodeCues(deferHeld: true);

        VisualNovelNodeSettings s = dialogue.SettingsFor(node.nodeId);

        // Keep Previous carries everything still running into this node (and on, while nodes keep it).
        if (s == null || !s.keepPreviousSustainedEffects) ReleaseSustainedExcept(s?.sustainedEffects);

        if (s == null)
        {
            ReleaseHeldShots(null);
            yield break;
        }

        // Mid-dialogue only: the first node's window is opened by the manager itself (after the
        // first transition when openTextBoxAfterFirstTransition is on), so there's nothing to close.
        bool midDialogue = dialogueManager && dialogueManager.DialogueActive && dialogueManager.NodesStarted > 0;
        // Only a transition that actually changes the picture touches the window.
        bool changesPicture = background
            ? background.WouldChange(s.transition, s.BackgroundLook, s.ForegroundLook, s.fadeColor)
            : s.transition != VisualNovelTransition.KeepPrevious;
        bool reopen = false;

        if (changesPicture && midDialogue)
        {
            // Close first; the new picture only starts coming in once the window is gone.
            if (s.textBox == VisualNovelTextBox.CloseAndReopen && !dialogueManager.UsingTextOverride)
            {
                yield return dialogueManager.HideWindowRoutine();
                reopen = true;
            }

            // The previous line shouldn't hang over the new picture (or greet it when the window
            // reopens) - whatever the Text Box mode.
            dialogueManager.ClearNodeText();
        }

        ApplyVoice(s, node, dialogue);
        ApplyMusic(s, dialogue);
        ApplyAmbience(s, dialogue);

        if (background)
            background.Play(s.transition, s.BackgroundLook, s.ForegroundLook,
                            s.transitionDuration, s.fadeColor, s.holdOnColor, s.blurRadius);

        // A blur transition drives the blur itself: blur effects still running from earlier nodes
        // step aside for it and pick up again once it's done.
        if (background && background.IsBlurring) PauseBlurEffectsForTransition();

        StartSustainedEffects(s);

        CueRun run = new CueRun();
        currentRun = run;
        cuesRoutine = StartCoroutine(CuesRoutine(s, run));

        // A window left up with Keep stays up through the delay instead: the node starts talking
        // textBoxDelay seconds after the transition.
        bool keepDelay = midDialogue && s.textBox == VisualNovelTextBox.Keep && s.textBoxDelay > 0f;

        // A closed window stays closed for the whole transition, whatever the node's own setting.
        if ((s.waitForTransition || reopen || keepDelay) && background) yield return background.WaitForTransition();

        if ((reopen || keepDelay) && s.textBoxDelay > 0f) yield return new WaitForSecondsRealtime(s.textBoxDelay);

        // Also reopens a window left closed by a node the player skipped past mid-transition.
        if (midDialogue) yield return dialogueManager.ShowWindowRoutine();

        if (HasBlockingEffect(s))
            while (!run.finished) yield return null;
    }

    /// <summary>Tells <see cref="PlayNode"/> when the node's blocking cues are done (or were cancelled).</summary>
    private class CueRun
    {
        public bool finished;
    }

    /// <summary>A start effect in flight. 'done' is set by the effect's own routine when it finishes.</summary>
    private class OneShot
    {
        public VisualNovelEffect effect;
        public Coroutine routine;
        public bool done;
    }

    public void OnDialogueEnd(VisualNovelDialogueContainer dialogue)
    {
        preStagedNode = null;

        bool fade = background && dialogue.fadeToColorOnEnd;
        float fadeDuration = dialogue.endFadeDuration;
        Color fadeColor = dialogue.endColor;
        bool releaseEffects = dialogue.releaseEffectsOnEnd;

        // With an end fade the effects keep running under it (a shaking screen shakes on into the
        // black) and stop once it's over; without one they stop now.
        bool endFade = fade && isActiveAndEnabled;
        if (!endFade) StopEffectsOnEnd(releaseEffects);

        ApplyEndAudio(dialogue);

        onDialogueEnd?.Invoke();

        SetSkipHint(false);
        if (dialogueManager && _onNodeStarted != null)
            dialogueManager.onStartNode.RemoveListener(_onNodeStarted);

        CurrentDialogue = null;

        // The runtime copy is done with: the manager drops its reference right after this call.
        Destroy(dialogue);

        System.Action finished = finishedCallback;
        finishedCallback = null;
        bool hideBeforeFinished = hideStageBeforeCallback;
        hideStageBeforeCallback = false;

        if (isActiveAndEnabled)
            endRoutine = StartCoroutine(EndRoutine(fade, fadeDuration, fadeColor, finished, endFade, releaseEffects,
                                                   hideBeforeFinished));
        else
        {
            HideStage();
            finished?.Invoke();
        }
    }

    private void OnNodeStarted()
    {
        _nodesShown++;
        SetSkipHint(showSkipHintForReturningPlayers && _returningPlayer && _nodesShown < hideSkipHintAtNode);
    }

    /// <summary>Fades the <see cref="skipHint"/> in or out; hidden at the end of the fade out.</summary>
    private void SetSkipHint(bool shown)
    {
        if (shown == _skipHintShown) return;
        _skipHintShown = shown;

        if (!skipHint)
        {
            DialogueManager manager = dialogueManager ? dialogueManager : DialogueManager.Instance;
            if (manager) manager.PinSkipIndicator = shown;
            else if (shown) h.Out("VisualNovelTalkable: no Skip Hint assigned and no DialogueManager to show its skip indicator.");
            return;
        }

        CanvasGroup group = skipHint.GetComponent<CanvasGroup>();
        if (!group) group = skipHint.AddComponent<CanvasGroup>();
        Tween.StopAll(group);

        if (shown)
        {
            skipHint.SetActive(true);
            group.alpha = 0f;
        }

        if (skipHintFadeDuration <= 0f)
        {
            group.alpha = shown ? 1f : 0f;
            if (!shown) skipHint.SetActive(false);
            return;
        }

        // Unscaled: visual novel also runs over unscaled time.
        Tween fade = Tween.Alpha(group, shown ? 1f : 0f, skipHintFadeDuration, useUnscaledTime: true);
        if (!shown) fade.OnComplete(skipHint, target => target.SetActive(false));
    }

    /// <summary>
    /// Stops the last node's cues and every sustained effect: released back to rest, or frozen where
    /// they are (the stage keeps its zoom, offset, ...).
    /// </summary>
    private void StopEffectsOnEnd(bool release)
    {
        if (release)
        {
            CancelNodeCues();
            ReleaseSustainedExcept(null);
        }
        else
        {
            CancelNodeCues(release: false);
            StopSustainedWithoutRelease();
        }
    }

    /// <summary>
    /// Fades the stage out, then stops the effects still running when <paramref name="stopEffects"/>,
    /// then hides it — or, with <paramref name="finished"/>, hands over to that instead.
    /// </summary>
    private IEnumerator EndRoutine(bool fade, float duration, Color color, System.Action finished,
                                   bool stopEffects, bool releaseEffects, bool hideBeforeFinished)
    {
        if (fade)
        {
            background.Play(VisualNovelTransition.FadeToColor, default, default, duration, color);
            yield return background.WaitForTransition();
        }

        if (stopEffects) StopEffectsOnEnd(releaseEffects);

        endRoutine = null;
        if (finished == null || hideBeforeFinished) HideStage();
        finished?.Invoke();
    }

    private void HideStage()
    {
        if (hideRootOnEnd && root)
        {
            if (background)
            {
                background.AdditionalStretching = false;
                background.ResetState();
            }
            root.SetActive(false);
        }
        else if (background)
        {
            background.AdditionalStretching = false;
        }

        ResetShakeTarget();
    }

    // ---------------------------------------------------------------------
    // Cues: SFX, start effects, sustained effects
    // ---------------------------------------------------------------------

    private IEnumerator CuesRoutine(VisualNovelNodeSettings s, CueRun run)
    {
        // Started together with the transition, so sounds timed by it count from here. A transition
        // that changes nothing never runs: its sounds play at once.
        float transitionLength = background && background.IsTransitioning ? s.TransitionLength : 0f;
        PlaySfx(s, VisualNovelSfxTiming.Transition, transitionLength);

        if (s.cuesAfterTransition && background) yield return background.WaitForTransition();

        PlaySfx(s, VisualNovelSfxTiming.NodeCues, 0f);

        List<OneShot> blocking = null;
        if (s.startEffects != null)
            for (int i = 0; i < s.startEffects.Count; i++)
            {
                VisualNovelEffectCue cue = s.startEffects[i];
                if (cue == null || !cue.effect) continue;

                // Registered before it starts: an effect with nothing to wait for finishes inside
                // StartCoroutine itself.
                OneShot shot = new OneShot { effect = cue.effect };
                oneShots.Add(shot);
                shot.routine = StartCoroutine(OneShotRoutine(cue, shot));

                if (cue.waitForCompletion)
                {
                    if (blocking == null) blocking = new List<OneShot>();
                    blocking.Add(shot);
                }
            }

        // The node's own start effects have taken over the stage: whatever the previous node held
        // can let go now (an effect restarted by this node is left to its new run).
        ReleaseHeldShots(s.startEffects);

        if (blocking != null)
            for (int i = 0; i < blocking.Count; i++)
                while (!blocking[i].done) yield return null;

        run.finished = true;
        cuesRoutine = null;
    }

    /// <summary>
    /// Plays (or schedules, with their delay) the node's sounds with the given <paramref name="timing"/>.
    /// Transition-timed ones wait their Transition Point of <paramref name="transitionLength"/> first.
    /// </summary>
    private void PlaySfx(VisualNovelNodeSettings s, VisualNovelSfxTiming timing, float transitionLength)
    {
        if (s.sfx == null) return;

        for (int i = 0; i < s.sfx.Count; i++)
        {
            VisualNovelSfxCue cue = s.sfx[i];
            if (cue == null || !cue.clip || cue.timing != timing) continue;

            float wait = cue.delay;
            if (timing == VisualNovelSfxTiming.Transition) wait += cue.transitionPoint * transitionLength;

            if (wait > 0f) delayedSfx.Add(StartCoroutine(DelayedSfx(cue, wait)));
            else PlaySfx(cue);
        }
    }

    private IEnumerator DelayedSfx(VisualNovelSfxCue cue, float wait)
    {
        yield return new WaitForSeconds(wait);
        PlaySfx(cue);
    }

    private static void PlaySfx(VisualNovelSfxCue cue)
    {
        if (!SFXManager.Instance) return;
        SFXManager.Instance.PlayClip(cue.clip, volumeIn: cue.volume);
    }

    private IEnumerator OneShotRoutine(VisualNovelEffectCue cue, OneShot shot)
    {
        if (cue.delay > 0f) yield return new WaitForSeconds(cue.delay);
        yield return cue.effect.Play(this);
        shot.done = true;
    }

    /// <summary>
    /// Registers the node's sustained effects. With cuesAfterTransition they start as soon as the new
    /// location is in place (the swap at the peak of a blur or color) rather than when the whole
    /// transition - blur clearing, cover fading - has played out.
    /// </summary>
    private void StartSustainedEffects(VisualNovelNodeSettings s)
    {
        if (s.sustainedEffects == null) return;

        for (int i = 0; i < s.sustainedEffects.Count; i++)
        {
            VisualNovelEffectCue cue = s.sustainedEffects[i];
            if (cue == null || !cue.effect) continue;

            // Carried over from the previous node: keep it running, don't restart it.
            if (sustained.ContainsKey(cue.effect)) continue;

            sustained[cue.effect] = StartCoroutine(SustainedRoutine(cue.effect, cue.delay, s.cuesAfterTransition));
        }
    }

    private IEnumerator SustainedRoutine(VisualNovelEffect effect, float delay, bool afterArrival)
    {
        if (background)
        {
            if (afterArrival) yield return background.WaitForArrival();
            // Both would write the blur radius every frame, and the effect would win.
            while (effect.DrivesBlur && background.IsBlurring) yield return null;
        }

        if (delay > 0f) yield return new WaitForSeconds(delay);
        yield return effect.Sustain(this);
    }

    /// <summary>
    /// Stops the running sustained blur effects and restarts each once the blur transition is done.
    /// They stay registered, so the node still carries them over or releases them as usual.
    /// </summary>
    private void PauseBlurEffectsForTransition()
    {
        List<VisualNovelEffect> paused = null;
        foreach (KeyValuePair<VisualNovelEffect, Coroutine> pair in sustained)
        {
            if (!pair.Key.DrivesBlur) continue;
            if (paused == null) paused = new List<VisualNovelEffect>();
            paused.Add(pair.Key);
        }

        if (paused == null) return;

        for (int i = 0; i < paused.Count; i++)
        {
            VisualNovelEffect effect = paused[i];
            if (sustained[effect] != null) StopCoroutine(sustained[effect]);
            sustained[effect] = StartCoroutine(SustainedRoutine(effect, 0f, false));
        }
    }

    private static bool HasBlockingEffect(VisualNovelNodeSettings s)
    {
        if (s.startEffects == null) return false;
        for (int i = 0; i < s.startEffects.Count; i++)
            if (s.startEffects[i] != null && s.startEffects[i].effect && s.startEffects[i].waitForCompletion)
                return true;
        return false;
    }

    /// <summary>
    /// Drops what the previous node still had in flight: cues not fired yet, delayed SFX, and start
    /// effects that haven't finished (those are released, so nothing is left half-shaken or blurred).
    /// </summary>
    /// <summary>
    /// Stops the current node's cues. Unfinished start effects are cut short and released. Finished
    /// ones that hold their end state (<see cref="VisualNovelEffect.ReleaseAtNodeEnd"/>) are released
    /// too, or with <paramref name="deferHeld"/> handed to <see cref="heldShots"/> for the next node's
    /// cues to release. With <paramref name="release"/> off nothing is released: the stage stays as it is.
    /// </summary>
    private void CancelNodeCues(bool deferHeld = false, bool release = true)
    {
        if (cuesRoutine != null) StopCoroutine(cuesRoutine);
        cuesRoutine = null;

        // Whoever waits on the cancelled cues must not wait forever.
        if (currentRun != null) currentRun.finished = true;
        currentRun = null;

        for (int i = 0; i < delayedSfx.Count; i++)
            if (delayedSfx[i] != null) StopCoroutine(delayedSfx[i]);
        delayedSfx.Clear();

        for (int i = 0; i < oneShots.Count; i++)
        {
            OneShot shot = oneShots[i];
            // A finished effect is at rest - unless it holds its end state until the node is over.
            if (shot.done && !shot.effect.ReleaseAtNodeEnd) continue;
            if (!shot.done && shot.routine != null) StopCoroutine(shot.routine);

            if (shot.done && deferHeld && release) heldShots.Add(shot);
            else if (release && isActiveAndEnabled) StartCoroutine(shot.effect.Release(this));
        }
        oneShots.Clear();

        // Still held from an earlier node whose cues never fired (skipped past): carried on when
        // deferring, otherwise let go now.
        if (!deferHeld) ReleaseHeldShots(null, release);
    }

    /// <summary>
    /// Releases the start effects held over from the previous node, except those listed in
    /// <paramref name="restarted"/>: this node plays them again, and their new run owns the stage.
    /// </summary>
    private void ReleaseHeldShots(List<VisualNovelEffectCue> restarted, bool release = true)
    {
        for (int i = 0; i < heldShots.Count; i++)
        {
            VisualNovelEffect effect = heldShots[i].effect;
            if (!release || Contains(restarted, effect)) continue;
            if (isActiveAndEnabled) StartCoroutine(effect.Release(this));
        }
        heldShots.Clear();
    }

    /// <summary>Stops every sustained effect where it is, without releasing it.</summary>
    private void StopSustainedWithoutRelease()
    {
        foreach (KeyValuePair<VisualNovelEffect, Coroutine> pair in sustained)
            if (pair.Value != null) StopCoroutine(pair.Value);
        sustained.Clear();
    }

    /// <summary>
    /// Stops and releases every sustained effect not listed in <paramref name="keep"/>. Pass null to
    /// release them all.
    /// </summary>
    private void ReleaseSustainedExcept(List<VisualNovelEffectCue> keep)
    {
        if (sustained.Count == 0) return;

        List<VisualNovelEffect> released = null;
        foreach (KeyValuePair<VisualNovelEffect, Coroutine> pair in sustained)
        {
            if (Contains(keep, pair.Key)) continue;
            if (released == null) released = new List<VisualNovelEffect>();
            released.Add(pair.Key);
        }

        if (released == null) return;

        for (int i = 0; i < released.Count; i++)
        {
            VisualNovelEffect effect = released[i];
            if (sustained[effect] != null) StopCoroutine(sustained[effect]);
            sustained.Remove(effect);
            if (isActiveAndEnabled) StartCoroutine(effect.Release(this));
        }
    }

    private static bool Contains(List<VisualNovelEffectCue> cues, VisualNovelEffect effect)
    {
        if (cues == null) return false;
        for (int i = 0; i < cues.Count; i++)
            if (cues[i] != null && cues[i].effect == effect) return true;
        return false;
    }

    // ---------------------------------------------------------------------
    // Audio
    // ---------------------------------------------------------------------

    /// <summary>
    /// Sets the node's typing voice. Runs before the text types; the node belongs to this
    /// dialogue's runtime copy, so nothing is written into the asset.
    /// </summary>
    private void ApplyVoice(VisualNovelNodeSettings s, DialogueNode node, VisualNovelDialogueContainer dialogue)
    {
        node.voiceVolumeScale = s.voiceVolume;
        node.voicePitchOverride = s.overrideVoicePitch ? s.voicePitchRange : (Vector2?)null;

        if (s.voice == VisualNovelVoice.Silent)
        {
            node.voiceOverride = new List<AudioClip>();
            return;
        }

        if (s.voice == VisualNovelVoice.Custom && s.voiceClips != null && s.voiceClips.Count > 0)
        {
            node.voiceOverride = s.voiceClips;
            return;
        }

        // The dialogue's default voice (played by the DialogueManager), else the speaker's own -
        // unless it would fall back to the global default, in which case this visual novel's
        // default voice takes its place.
        node.voiceOverride = null;
        if (dialogue.HasDefaultVoice) return;
        if (HasDefaultVoice && UsesGlobalDefaultVoice(node)) node.voiceOverride = defaultVoiceClips;
    }

    private bool HasDefaultVoice => defaultVoiceClips != null && defaultVoiceClips.Count > 0;

    /// <summary>
    /// True when the node's speaker has no voice of its own. Checked by reference as well:
    /// Talkable.Awake fills an empty voice list with the SFXManager's default list itself, so a
    /// narrator without clips would otherwise look like it has a voice.
    /// </summary>
    private static bool UsesGlobalDefaultVoice(DialogueNode node)
    {
        List<AudioClip> clips = node.speakerVoiceClips;
        if (clips == null || clips.Count == 0) return true;
        return SFXManager.Instance && ReferenceEquals(clips, SFXManager.Instance.defaultDialogueVoiceList);
    }

    private static void ApplyMusic(VisualNovelNodeSettings s, VisualNovelDialogueContainer dialogue)
    {
        BGMManager bgm = BGMManager.Instance;
        if (!bgm) return;

        switch (s.music)
        {
            case VisualNovelAudioChange.Play:
                // Before the track: a new one fades in to this level, one already playing eases to it.
                if (s.bgm || s.bgmClip) bgm.SetVolumeScale(s.musicVolume * dialogue.musicVolume, s.musicFade);
                if (s.bgm) bgm.PlayBGM(s.bgm, s.musicFade, fadeInTime: s.musicFadeIn);
                else if (s.bgmClip) bgm.PlayMusic(s.bgmClip, s.musicFade, s.musicFadeIn);
                break;

            case VisualNovelAudioChange.Stop:
                bgm.FadeOutMusic(s.musicFade);
                break;
        }
    }

    private void ApplyAmbience(VisualNovelNodeSettings s, VisualNovelDialogueContainer dialogue)
    {
        if (!ambience) return;

        switch (s.ambience)
        {
            case VisualNovelAudioChange.Play:
                ambience.Play(s.ambienceClip, s.ambienceVolume * dialogue.ambienceVolume, s.ambienceFade);
                break;

            case VisualNovelAudioChange.Stop:
                ambience.Stop(s.ambienceFade);
                break;
        }
    }

    private void ApplyEndAudio(VisualNovelDialogueContainer dialogue)
    {
        BGMManager bgm = BGMManager.Instance;
        if (bgm)
        {
            switch (dialogue.musicOnEnd)
            {
                // The dialogue's Music Volume goes with its music: whatever plays next is back at
                // full volume. Keep leaves it on, since the dialogue's track plays on.
                case VisualNovelMusicOnEnd.FadeOut:
                    bgm.FadeOutMusic(dialogue.EndMusicFadeDuration);
                    bgm.SetVolumeScale(1f);
                    break;
                case VisualNovelMusicOnEnd.ReturnToDefault:
                    bgm.SetVolumeScale(1f, dialogue.EndMusicFadeDuration);
                    bgm.ReturnToDefault(dialogue.EndMusicFadeDuration);
                    break;
            }
        }

        if (ambience && dialogue.stopAmbienceOnEnd) ambience.Stop(dialogue.endAmbienceFade);
    }

    // ---------------------------------------------------------------------
    // Effect helpers
    // ---------------------------------------------------------------------

    /// <summary>Puts the shake target back at its rest position.</summary>
    public void ResetShakeTarget()
    {
        if (shakeTarget) shakeTarget.localPosition = shakeTargetRestPosition;
    }

    /// <summary>Runtime state an effect stored for itself, or null.</summary>
    public object GetEffectState(VisualNovelEffect effect)
    {
        return effect && effectState.TryGetValue(effect, out object state) ? state : null;
    }

    /// <summary>Stores runtime state for an effect. Null clears it.</summary>
    public void SetEffectState(VisualNovelEffect effect, object state)
    {
        if (!effect) return;
        if (state == null) effectState.Remove(effect);
        else effectState[effect] = state;
    }
}
