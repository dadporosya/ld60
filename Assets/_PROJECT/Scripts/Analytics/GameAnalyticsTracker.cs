using System.Collections.Generic;
using System.Text.RegularExpressions;
using GameAnalyticsSDK;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Sends Funeral Song's events to GameAnalytics. Sets itself up: no scene object needed. On play it initializes
/// the SDK (keys come from Window > GameAnalytics > Select Settings), makes sure a <see cref="GameAnalytics"/>
/// object exists, and adds a tracker to every loaded scene that listens to the game's own UnityEvents.
///
/// <para><b>Progression.</b> <c>Run</c> starts with the intro, completes on the good ending and fails on the bad
/// one (score: levels won). Each dice round is <c>Run:&lt;level name&gt;</c>, completed or failed with the dice
/// total as score; a round cut off by the song running out fails.</para>
///
/// <para><b>Design events</b> (value in brackets):
/// <c>Song:Started</c>,
/// <c>Hub:Interact:&lt;object&gt;</c> [song seconds],
/// <c>Phone:Opened</c> [song seconds],
/// <c>Call:Started:&lt;contact&gt;</c> [song seconds],
/// <c>Call:Passed|Failed:&lt;contact&gt;</c> [song seconds],
/// <c>Dice:RoundScore:&lt;level&gt;</c> [points],
/// <c>Dice:Total:&lt;level&gt;</c> [points],
/// <c>Dice:Margin:&lt;level&gt;</c> [total - target],
/// <c>Dice:Rolls:&lt;level&gt;</c> [rolls this turn],
/// <c>Dice:RiskyGoal:&lt;level&gt;</c> [total],
/// <c>Card:Chosen:Hope|Hate:&lt;card&gt;</c>,
/// <c>Ending:Good</c> [song seconds],
/// <c>Ending:Bad</c> [levels won],
/// <c>Game:Quit:&lt;phase&gt;</c> [song seconds] - closed before an ending.</para>
///
/// <para>The SDK only sends from builds; in the editor it logs the calls when Info Log Editor is on in its
/// settings.</para>
/// </summary>
public class GameAnalyticsTracker : MonoBehaviour
{
    // GameAnalytics' allowed characters for an event part; anything else is dropped.
    private static readonly Regex InvalidChars = new Regex(@"[^A-Za-z0-9\s\-_\.\(\)\!\?]");
    private const int MaxPartLength = 64;

    private FuneralGameManager game;
    private LevelManager levels;
    private DiceManager dice;
    private CardManager cards;
    private SongTimer song;

    private FuneralGameManager.Phase? lastPhase;
    private bool runActive;
    // The level progression started and not yet completed / failed; null for none.
    private string activeLevel;
    private int lastDiceTotal;
    private int rollsThisTurn;

    // ---------------------------------------------------------------------
    // Setup
    // ---------------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (!FindFirstObjectByType<GameAnalytics>(FindObjectsInactive.Include))
            new GameObject("GameAnalytics").AddComponent<GameAnalytics>();
        if (!GameAnalytics.Initialized) GameAnalytics.Initialize();

        AddTo(SceneManager.GetActiveScene());
        // The first scene's sceneLoaded has already fired by now; this catches the later ones.
        SceneManager.sceneLoaded += (scene, mode) => AddTo(scene);
    }

    private static void AddTo(Scene scene)
    {
        GameObject go = new GameObject(nameof(GameAnalyticsTracker));
        SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<GameAnalyticsTracker>();
    }

    // Awake rather than Start: FuneralGameManager starts the game in its own Start, and the order of Starts isn't fixed.
    private void Awake()
    {
        game = Find(FuneralGameManager.Instance);
        levels = Find(LevelManager.Instance);
        dice = Find(DiceManager.Instance);
        cards = Find(CardManager.Instance);
        song = Find(SongTimer.Instance);

        if (game)
        {
            game.onCallStarted.AddListener(OnCallStarted);
            game.onCallEnded.AddListener(OnCallEnded);
            game.onGoodEnding.AddListener(OnGoodEnding);
            game.onBadEnding.AddListener(OnBadEnding);
        }

        if (levels)
        {
            levels.onLevelStarted.AddListener(OnLevelStarted);
            levels.onLevelWon.AddListener(index => OnLevelEnded(index, true));
            levels.onLevelLost.AddListener(index => OnLevelEnded(index, false));
        }

        if (dice)
        {
            dice.onTurnStarted.AddListener(() => rollsThisTurn = 0);
            dice.onRolled.AddListener(() => rollsThisTurn++);
            dice.onRoundFinished.AddListener(OnRoundFinished);
            dice.onTurnFinished.AddListener(OnTurnFinished);
        }

        if (cards) cards.onCardChosen.AddListener(OnCardChosen);
        if (song) song.onSongStarted.AddListener(() => Design("Song:Started"));

        foreach (HubInteractable interactable in FindObjectsByType<HubInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string id = Part(interactable.gameObject.name);
            interactable.onInteract.AddListener(() => Design($"Hub:Interact:{id}", SongSeconds));
        }
    }

    // The game's systems register their Instance in Awake; ones on inactive objects haven't yet.
    private static T Find<T>(T instance) where T : Object =>
        instance ? instance : FindFirstObjectByType<T>(FindObjectsInactive.Include);

    // ---------------------------------------------------------------------
    // Phases (FuneralGameManager has no events for these)
    // ---------------------------------------------------------------------

    private void Update()
    {
        if (!game) return;

        FuneralGameManager.Phase phase = game.CurrentPhase;
        if (lastPhase == phase) return;
        lastPhase = phase;

        if (phase == FuneralGameManager.Phase.Intro) StartRun();
        else if (phase == FuneralGameManager.Phase.Phone) Design("Phone:Opened", SongSeconds);
    }

    private void StartRun()
    {
        // A restart mid-run: the old run is abandoned, not failed - close nothing, just start over.
        activeLevel = null;
        runActive = true;
        GameAnalytics.NewProgressionEvent(GAProgressionStatus.Start, "Run");
    }

    private void OnApplicationQuit()
    {
        if (game && game.CurrentPhase != FuneralGameManager.Phase.Ending)
            Design($"Game:Quit:{game.CurrentPhase}", SongSeconds);
    }

    // ---------------------------------------------------------------------
    // Levels and dice
    // ---------------------------------------------------------------------

    private void OnLevelStarted(int index)
    {
        activeLevel = LevelName(index);
        lastDiceTotal = 0;
        GameAnalytics.NewProgressionEvent(GAProgressionStatus.Start, "Run", activeLevel);
    }

    private void OnLevelEnded(int index, bool won)
    {
        string level = LevelName(index);
        GameAnalytics.NewProgressionEvent(won ? GAProgressionStatus.Complete : GAProgressionStatus.Fail,
            "Run", level, lastDiceTotal);
        if (activeLevel == level) activeLevel = null;
    }

    private void OnRoundFinished(int score) => Design($"Dice:RoundScore:{CurrentLevel}", score);

    private void OnTurnFinished(int total)
    {
        lastDiceTotal = total;
        string level = CurrentLevel;
        Design($"Dice:Total:{level}", total);
        Design($"Dice:Margin:{level}", total - dice.Target);
        Design($"Dice:Rolls:{level}", rollsThisTurn);
        if (dice.RiskyTarget > 0 && total >= dice.RiskyTarget) Design($"Dice:RiskyGoal:{level}", total);
    }

    private void OnCardChosen(CardData card)
    {
        if (!card) return;
        // The pick follows the round: a won round offers Hope cards, a lost one Hate.
        string mood = levels && !levels.LastPlayedPassed ? "Hate" : "Hope";
        Design($"Card:Chosen:{mood}:{Part(card.name)}");
    }

    // ---------------------------------------------------------------------
    // Calls and endings
    // ---------------------------------------------------------------------

    private void OnCallStarted(ContactData contact)
    {
        if (contact) Design($"Call:Started:{Part(contact.DisplayName)}", SongSeconds);
    }

    // Also fires after phone-list levels, with no contact: those are covered by the level progression.
    private void OnCallEnded(ContactData contact, bool passed)
    {
        if (contact) Design($"Call:{(passed ? "Passed" : "Failed")}:{Part(contact.DisplayName)}", SongSeconds);
    }

    private void OnGoodEnding()
    {
        Design("Ending:Good", SongSeconds);
        EndRun(GAProgressionStatus.Complete);
    }

    private void OnBadEnding()
    {
        // The song cut a round off: that level is lost too.
        if (activeLevel != null)
        {
            GameAnalytics.NewProgressionEvent(GAProgressionStatus.Fail, "Run", activeLevel, dice ? dice.BankedScore : 0);
            activeLevel = null;
        }

        Design("Ending:Bad", LevelsWon);
        EndRun(GAProgressionStatus.Fail);
    }

    private void EndRun(GAProgressionStatus status)
    {
        if (!runActive) return;
        runActive = false;
        GameAnalytics.NewProgressionEvent(status, "Run", LevelsWon);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private float SongSeconds => song ? Mathf.Round(song.Elapsed) : 0f;

    private int LevelsWon => levels ? levels.LevelsWon : 0;

    private string CurrentLevel => levels ? LevelName(Mathf.Min(levels.CurrentIndex, levels.Levels.Count - 1)) : "Unknown";

    private string LevelName(int index)
    {
        IReadOnlyList<Level> list = levels ? levels.Levels : null;
        if (list == null || index < 0 || index >= list.Count) return $"Level {index + 1}";
        return Part(string.IsNullOrWhiteSpace(list[index].name) ? $"Level {index + 1}" : list[index].name);
    }

    /// <summary><paramref name="text"/> as a valid GameAnalytics event part: allowed characters only, 1-64 long.</summary>
    private static string Part(string text)
    {
        string clean = InvalidChars.Replace(text ?? "", "").Trim();
        if (clean.Length > MaxPartLength) clean = clean.Substring(0, MaxPartLength).Trim();
        return clean.Length > 0 ? clean : "Unknown";
    }

    private static void Design(string eventId) => GameAnalytics.NewDesignEvent(eventId);

    private static void Design(string eventId, float value) => GameAnalytics.NewDesignEvent(eventId, value);
}
