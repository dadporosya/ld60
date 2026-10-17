using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Runs Funeral Song: the hub, the calls and the two endings, all in one scene.
///
/// <para><b>Flow.</b> Intro dialogue -> the song starts -> Hub. From the hub the player clicks things
/// (<see cref="HubInteractable"/>); the phone opens the dial panel, and a call goes: the contact's
/// call dialogue -> dice round against the current level's points (<see cref="LevelManager"/>) -> the
/// level's win or loss dialogue -> card pick (Hope or
/// Hate) -> back to the hub, the name struck off the list. Once everyone has been called: the good
/// ending (calling dad). If the song ends first, whatever is happening is cut off: the bad ending.
/// Both endings close the game.</para>
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
    public bool IsInHub => CurrentPhase == Phase.Hub && !Dialogues.IsPlaying;

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
        if (Levels) Levels.ResetLevels();
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
        Dice.StartRound(pointsToPass, Cards ? Cards.PermanentCards : null,
            result => EndCall(contact, Levels ? Levels.Passes(result.score.total) : result.passed));
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
        Dialogues.Play(outcome, () =>
        {
            onCallEnded?.Invoke(contact, passed);
            OfferCard(passed ? CardMood.Hope : CardMood.Hate);
        });
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

        if (Dice) Dice.CancelRound();
        if (Cards) Cards.Cancel();
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
