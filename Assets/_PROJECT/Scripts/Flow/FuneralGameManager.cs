using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Runs Funeral Song: the hub, the calls and the two endings, all in one scene.
///
/// <para><b>Flow.</b> Intro dialogue -> the song starts -> Hub. From the hub the player clicks things
/// (<see cref="HubInteractable"/>); the phone opens the dial panel, and a call goes: the contact's
/// call dialogue -> dice round against the current level's points (<see cref="LevelManager"/>) -> the
/// level's win or loss dialogue -> card pick (Hope or
/// Hate) -> back to the hub (between the round and the pick the screen fades to black and back, see
/// <see cref="fadeAroundRoundEnd"/>), the name struck off the list. Once everyone has been called: the good
/// ending (calling dad). If the song ends first, whatever is happening is cut off: the bad ending.
/// Both endings close the game.</para>
///
/// <para>Alternatively the phone shows the level list (<see cref="LevelPhoneInteractable"/>): a picked level
/// goes start dialogue -> dice -> win / loss dialogue -> card pick -> hub (<see cref="PlayLevel"/>); finishing
/// the final level plays the good ending.</para>
/// </summary>
public class FuneralGameManager : MonoBehaviour
{
    public static FuneralGameManager Instance;

    public enum Phase
    {
        Intro,
        Hub,
        Phone,
        Call,
        Dice,
        CardDraft,
        Ending,
    }

    [Header("Systems (default to their Instance)")]
    [SerializeField] private SongTimer song;
    [SerializeField] private HubDialogueRunner dialogues;
    [SerializeField] private DiceManager diceManager;
    [SerializeField] private CardManager cardManager;
    [SerializeField] private PhoneDialPanel dialPanel;
    [SerializeField] private LevelManager levelManager;

    [Header("Contacts")]
    [Tooltip("Everyone the hero has to call, in list order.")]
    [SerializeField] private List<ContactData> contacts = new List<ContactData>();

    [Header("Story")]
    [Tooltip("Played before the song starts. Optional.")]
    [SerializeField] private VisualNovelDialogueContainer introDialogue;
    [Tooltip("Start the song once the intro is over (otherwise something else calls StartSong).")]
    [SerializeField] private bool startSongAfterIntro = true;
    [Tooltip("Everyone has been called: the hero calls his father. \"I love you, dad.\"")]
    [SerializeField] private VisualNovelDialogueContainer goodEndingDialogue;
    [Tooltip("The song ran out: the gun, his eyes, black, the shot - and \"You understand that it's not the only way out, right?\"")]
    [SerializeField] private VisualNovelDialogueContainer badEndingDialogue;
    [Tooltip("Seconds the song fades out over when the good ending starts. Negative: it keeps playing.")]
    [SerializeField] private float songFadeOnGoodEnding = 3f;
    [Tooltip("Finishing the final level (picked from the phone's level list) plays the good ending.")]
    [SerializeField] private bool goodEndingAfterFinalLevel = true;

    [Header("Round end")]
    [Tooltip("After a dice round the FadeImageController covers the screen: black -> win / loss dialogue -> " +
             "black -> card pick revealed. Off: no fades.")]
    [SerializeField] private bool fadeAroundRoundEnd = true;
    [Tooltip("Seconds the dice table fades to black over once the round is over.")]
    [SerializeField, Min(0f)] private float roundEndCoverDuration = 0.6f;
    [Tooltip("Seconds the black fades away over, revealing the win / loss dialogue.")]
    [SerializeField, Min(0f)] private float dialogueRevealDuration = 0.6f;
    [Tooltip("Seconds the screen fades to black over once the win / loss dialogue is over.")]
    [SerializeField, Min(0f)] private float dialogueCoverDuration = 0.6f;
    [Tooltip("Seconds the black fades away over, revealing the card pick.")]
    [SerializeField, Min(0f)] private float cardsRevealDuration = 0.6f;

    [Header("Game over")]
    [SerializeField] private bool quitAfterEnding = true;

    [Header("Debug")]
    [SerializeField] private bool startOnPlay = true;

    [Header("Events")]
    public UnityEvent onContactsChanged = new UnityEvent();
    public UnityEvent<ContactData> onCallStarted = new UnityEvent<ContactData>();
    /// <summary>The contact, and whether the dice reached their target.</summary>
    public UnityEvent<ContactData, bool> onCallEnded = new UnityEvent<ContactData, bool>();
    public UnityEvent onGoodEnding = new UnityEvent();
    public UnityEvent onBadEnding = new UnityEvent();

    public Phase CurrentPhase { get; private set; } = Phase.Intro;

    /// <summary>True while the player is free to click around the hub.</summary>
    public bool IsInHub => CurrentPhase == Phase.Hub && NothingPlaying;

    /// <summary>True while the player may look at things (showcases): in the hub, and during a dice round.</summary>
    public bool CanLookAround => (CurrentPhase == Phase.Hub || CurrentPhase == Phase.Dice) && NothingPlaying;

    private bool NothingPlaying => !Dialogues.IsPlaying && !ShowcaseOpen &&
                                   !(DialogueManager.Instance && DialogueManager.Instance.DialogueActive);

    private static bool ShowcaseOpen => ShowcaseDialogueManager.Instance && ShowcaseDialogueManager.Instance.IsShowing;

    public IReadOnlyList<ContactData> Contacts => contacts;

    // Called contacts, and whether the dice reached the target for each.
    private readonly Dictionary<ContactData, bool> calls = new Dictionary<ContactData, bool>();

    private SongTimer Song => Resolve(ref song, SongTimer.Instance);
    private HubDialogueRunner Dialogues
    {
        get
        {
            if (!dialogues) dialogues = HubDialogueRunner.Instance;
            // Every step of the flow goes through it, so make sure there is one.
            if (!dialogues) dialogues = gameObject.AddComponent<HubDialogueRunner>();
            return dialogues;
        }
    }
    private DiceManager Dice => Resolve(ref diceManager, DiceManager.Instance);
    private CardManager Cards => Resolve(ref cardManager, CardManager.Instance);
    private PhoneDialPanel DialPanel => Resolve(ref dialPanel, null);
    private LevelManager Levels => Resolve(ref levelManager, LevelManager.Instance);

    /// <summary>
    /// The assigned system, else its Instance, else one found in the scene - inactive ones included, since
    /// UI panels usually start hidden and a hidden object has run no Awake to set its Instance yet.
    /// </summary>
    private static T Resolve<T>(ref T field, T instance) where T : Object
    {
        if (!field) field = instance;
        if (!field) field = FindFirstObjectByType<T>(FindObjectsInactive.Include);
        return field;
    }

    public bool HasCalled(ContactData contact) => contact && calls.ContainsKey(contact);

    public bool WasCallGood(ContactData contact) => contact && calls.TryGetValue(contact, out bool good) && good;

    public int GoodCalls
    {
        get
        {
            int count = 0;
            foreach (bool good in calls.Values)
                if (good) count++;
            return count;
        }
    }

    public bool HasUncalledContacts
    {
        get
        {
            foreach (ContactData contact in contacts)
                if (contact && !HasCalled(contact)) return true;
            return false;
        }
    }

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance, setDontDestroy: false);
    }

    private void Start()
    {
        if (Song) Song.onSongEnded.AddListener(OnSongEnded);
        if (startOnPlay) StartGame();
    }

    private void OnDestroy()
    {
        if (song) song.onSongEnded.RemoveListener(OnSongEnded);
    }

    // ---------------------------------------------------------------------
    // Start
    // ---------------------------------------------------------------------

    public void StartGame()
    {
        calls.Clear();
        phoneLevel = -1;
        CancelRoundEnd();
        if (Levels) Levels.ResetLevels();
        if (Dice) Dice.ResetTutorial();
        if (Cards) Cards.UpgradeNextChoice = false;
        onContactsChanged?.Invoke();
        SetPhase(Phase.Intro);

        Dialogues.Play(introDialogue, () =>
        {
            if (startSongAfterIntro) StartSong();
            SetPhase(Phase.Hub);
        });
    }

    public void StartSong()
    {
        if (Song && !Song.HasStarted) Song.Play();
    }

    // ---------------------------------------------------------------------
    // Hub
    // ---------------------------------------------------------------------

    /// <summary>A hub object's dialogue: plays it, then the hub is back.</summary>
    public void PlayHubDialogue(VisualNovelDialogueContainer dialogue)
    {
        if (!dialogue || CurrentPhase != Phase.Hub) return;
        Dialogues.Play(dialogue);
    }

    /// <summary>The phone: pick who to call.</summary>
    public void OpenPhone()
    {
        if (!IsInHub || !HasUncalledContacts) return;

        List<ContactData> uncalled = new List<ContactData>();
        foreach (ContactData contact in contacts)
            if (contact && !HasCalled(contact)) uncalled.Add(contact);

        if (!DialPanel)
        {
            // No panel: call the next person on the list.
            Call(uncalled[0]);
            return;
        }

        SetPhase(Phase.Phone);
        DialPanel.Show(uncalled, Call, () =>
        {
            if (CurrentPhase == Phase.Phone) SetPhase(Phase.Hub);
        });
    }

    // The level picked from the phone's level list whose round is running; -1 for none.
    private int phoneLevel = -1;

    /// <summary>
    /// The phone's level list (<see cref="LevelPhoneInteractable"/>): plays levels[<paramref name="index"/>] -
    /// its start dialogue, dice round, win / loss dialogue, card pick, then the hub (or the good ending after the
    /// final level). Ignored unless the level can be started (<see cref="LevelManager.CanStart"/>).
    /// </summary>
    public void PlayLevel(int index)
    {
        if (!IsInHub || !Levels) return;
        if (!Levels.CanStart(index))
        {
            h.Out($"FuneralGameManager: level {index} can't be started now.");
            return;
        }

        Levels.JumpTo(index);
        phoneLevel = index;
        SetPhase(Phase.Call);
        Dialogues.Play(Levels.Current.startDialogue, () => StartDice(null));
    }

    // ---------------------------------------------------------------------
    // A call
    // ---------------------------------------------------------------------

    /// <summary>Calls <paramref name="contact"/>: dialogue, dice, outcome, card.</summary>
    // ---------------------------------------------------------------------
    // Debug
    // ---------------------------------------------------------------------

    // The running round was started by DebugRunCurrentLevel: no contact, back to the hub after it.
    private bool debugRound;

    /// <summary>Debug level rounds can start from the hub or during the intro.</summary>
    public bool CanRunDebugLevel => CurrentPhase == Phase.Hub || CurrentPhase == Phase.Intro;

    /// <summary>
    /// Debug: runs the <see cref="LevelManager"/>'s current level without a call - dice round, win / loss
    /// dialogue, card pick, then the hub. A hub dialogue or the intro still playing is cut off (the song
    /// then doesn't start by itself).
    /// </summary>
    public void DebugRunCurrentLevel()
    {
        if (!CanRunDebugLevel) return;

        if (Dialogues.IsPlaying) Dialogues.Interrupt();
        debugRound = true;
        StartDice(null);
    }

    public void Call(ContactData contact)
    {
        if (!contact || HasCalled(contact) || CurrentPhase == Phase.Ending) return;

        // Struck off the list as soon as the number is dialed: there is no calling twice.
        calls[contact] = false;
        onContactsChanged?.Invoke();
        onCallStarted?.Invoke(contact);

        SetPhase(Phase.Call);
        Dialogues.Play(contact.callDialogue, () => StartDice(contact));
    }

    private void StartDice(ContactData contact)
    {
        if (CurrentPhase == Phase.Ending) return;

        if (!Dice)
        {
            h.Out("FuneralGameManager: no DiceManager, the call counts as failed.");
            EndCall(contact, false);
            return;
        }

        SetPhase(Phase.Dice);
        if (!Levels) h.Out("FuneralGameManager: no LevelManager, the round needs 0 points.");
        else Levels.BeginLevel();

        int pointsToPass = Levels ? Levels.PointsToPass : 0;
        // Showcases stay clickable during the round; its outcome dialogue waits for one still open.
        Dice.StartTurn(pointsToPass, Cards ? Cards.PermanentCards : null, result =>
        {
            // The risky goal reached: the card pick after this call offers upgraded cards.
            if (result.riskyPassed && Cards) Cards.UpgradeNextChoice = true;
            AfterShowcase(() => EndCall(contact, Levels ? Levels.Passes(result.total) : result.passed));
        });
    }

    /// <summary>Runs <paramref name="action"/> now, or once the open showcase (and its dialogue) is gone.</summary>
    private static void AfterShowcase(System.Action action)
    {
        if (!ShowcaseOpen)
        {
            action();
            return;
        }

        UnityEvent ended = ShowcaseDialogueManager.Instance.onShowcaseEnded;
        UnityAction once = null;
        once = () =>
        {
            ended.RemoveListener(once);
            action();
        };
        ended.AddListener(once);
    }

    private void EndCall(ContactData contact, bool passed)
    {
        if (CurrentPhase == Phase.Ending) return;

        // A debug round (LevelManager's level keys) has no contact.
        if (contact)
        {
            calls[contact] = passed;
            onContactsChanged?.Invoke();
        }

        SetPhase(Phase.Call);
        // The level decides what plays after the round, and moves on to the next one.
        VisualNovelDialogueContainer outcome = Levels ? Levels.CompleteLevel(passed) : null;
        CardMood mood = passed ? CardMood.Hope : CardMood.Hate;
        int id = ++roundEndId;
        roundEndFading = true;

        // Black over the dice table -> the outcome dialogue -> black again -> the card pick, revealed.
        RoundEndFade(true, roundEndCoverDuration, id, () =>
        {
            // No dialogue (none assigned, or an empty one): straight from the black to the card pick.
            if (!outcome || outcome.nodes == null || outcome.nodes.Count == 0)
            {
                OfferCardRevealed(contact, passed, mood, id);
                return;
            }

            // Revealed first: a dialogue that can't play calls back at once, and its cover then takes over.
            RoundEndFade(false, dialogueRevealDuration, id, null);
            // The stage stays up until the black covers it, so the dice table doesn't flash in between.
            Dialogues.Play(outcome, () => RoundEndFade(true, dialogueCoverDuration, id, () =>
            {
                Dialogues.HideStage();
                OfferCardRevealed(contact, passed, mood, id);
            }), hideStage: false);
        });
    }

    // Bumped by every round end (and by what cuts one off): a fade callback only runs while its round end is current.
    private int roundEndId;
    // A round end's fades are running: the screen may be black.
    private bool roundEndFading;

    /// <summary>Puts the card pick up behind the black, then fades the black away.</summary>
    private void OfferCardRevealed(ContactData contact, bool passed, CardMood mood, int id)
    {
        onCallEnded?.Invoke(contact, passed);
        OfferCard(mood);
        // Even without a pick (no cards, or the good ending started): whatever comes next needs to be seen.
        roundEndFading = false;
        RoundEndFade(false, cardsRevealDuration, id, null);
    }

    /// <summary>
    /// Fades the <see cref="FadeImageController"/> to black (<paramref name="cover"/>) or clear, then
    /// <paramref name="then"/> - unless round end <paramref name="id"/> was cut off meanwhile. Without fades
    /// (none in the scene, or <see cref="fadeAroundRoundEnd"/> off) it just runs <paramref name="then"/>.
    /// </summary>
    private void RoundEndFade(bool cover, float duration, int id, System.Action then)
    {
        if (id != roundEndId) return;

        FadeImageController fade = fadeAroundRoundEnd ? FadeImageController.Instance : null;
        if (!fade)
        {
            then?.Invoke();
            return;
        }

        if (duration <= 0f)
        {
            fade.SetAlpha(cover ? 1f : 0f);
            then?.Invoke();
            return;
        }

        Tween tween = cover ? fade.FadeIn(duration, useUnscaledTime: true) : fade.Fadeout(duration, useUnscaledTime: true);
        if (then != null) tween.OnComplete(() => { if (id == roundEndId) then(); });
    }

    /// <summary>Cuts off a round end's fades, clearing the screen if it was dark.</summary>
    private void CancelRoundEnd()
    {
        roundEndId++;
        if (!roundEndFading) return;

        roundEndFading = false;
        FadeImageController fade = FadeImageController.Instance;
        if (fade) fade.Fadeout(cardsRevealDuration, useUnscaledTime: true);
    }

    private void OfferCard(CardMood mood)
    {
        if (CurrentPhase == Phase.Ending) return;

        if (!Cards)
        {
            AfterCall();
            return;
        }

        // The CardManager applies the pick itself (keeps a permanent card, fires an immediate one).
        SetPhase(Phase.CardDraft);
        if (!Cards.ChooseCards(Cards.DefaultChoiceCount, mood, _ => AfterCall())) AfterCall();
    }

    private void AfterCall()
    {
        if (CurrentPhase == Phase.Ending) return;

        if (debugRound)
        {
            // Didn't strike anyone off, so it can't finish the list either.
            debugRound = false;
            SetPhase(Phase.Hub);
            return;
        }

        if (phoneLevel >= 0)
        {
            bool final = Levels && Levels.IsFinal(phoneLevel);
            phoneLevel = -1;
            if (final && goodEndingAfterFinalLevel) GoodEnding();
            else SetPhase(Phase.Hub);
            return;
        }

        if (HasUncalledContacts) SetPhase(Phase.Hub);
        else GoodEnding();
    }

    // ---------------------------------------------------------------------
    // Endings
    // ---------------------------------------------------------------------

    /// <summary>Everyone was called in time: the hero calls his father.</summary>
    public void GoodEnding()
    {
        if (CurrentPhase == Phase.Ending) return;
        SetPhase(Phase.Ending);

        if (Song && songFadeOnGoodEnding >= 0f) Song.Stop(songFadeOnGoodEnding);

        onGoodEnding?.Invoke();
        Dialogues.InterruptAndPlay(goodEndingDialogue, QuitGame);
    }

    /// <summary>The song is over: cuts off whatever is happening and plays the bad ending.</summary>
    public void BadEnding()
    {
        if (CurrentPhase == Phase.Ending) return;
        SetPhase(Phase.Ending);

        if (Dice) Dice.CancelTurn();
        if (Cards) Cards.Cancel();
        CancelRoundEnd();
        if (ShowcaseDialogueManager.Instance) ShowcaseDialogueManager.Instance.Cancel();
        if (DialPanel) DialPanel.Hide();

        onBadEnding?.Invoke();
        Dialogues.InterruptAndPlay(badEndingDialogue, QuitGame);
    }

    private void OnSongEnded() => BadEnding();

    private void QuitGame()
    {
        if (!quitAfterEnding) return;

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

    private void SetPhase(Phase phase)
    {
        if (CurrentPhase == phase) return;
        CurrentPhase = phase;
        h.Out($"FuneralGameManager: {phase}");
    }
}
