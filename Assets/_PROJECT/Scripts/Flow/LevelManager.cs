using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using PrimeTween;

/// <summary>
/// One level: its name, whether it's final, and what plays around its dice round. How many points the round needs
/// isn't the level's: it depends on how many rounds came before it (<see cref="LevelManager.PointsToPass"/>).
/// </summary>
[Serializable]
public class Level
{
    [Tooltip("Shown on the phone's level list, and in the logs.")]
    public string name = "Level";
    [Tooltip("Can only be started from the phone once every other level is completed.")]
    public bool isFinal;
    [Tooltip("Final levels: played over the phone's list when the level is picked before every other level is " +
             "completed. The list stays up after it.")]
    public DialogueContainer lockedDialogue;
    [Tooltip("Played before the dice round when the level is picked from the phone. Optional.")]
    public VisualNovelDialogueContainer startDialogue;
    [Tooltip("The other end of the line in this level's outcome dialogues: speaker id \"2\" " +
             "(LevelManager's Other Speaker Id) - its voice clips and text color. Optional.")]
    public GameObject speakerTalkable;
    [Tooltip("Bad: played when the round falls short (the player lost).")]
    [FormerlySerializedAs("lossDialogue")]
    public VisualNovelDialogueContainer badDialogue;
    [Tooltip("Neutral: played when the round reaches the points it needs, but not the risky goal.")]
    [FormerlySerializedAs("winDialogue")]
    public VisualNovelDialogueContainer neutralDialogue;
    [Tooltip("Hopeful: played when the round reaches the risky goal. Empty = the Neutral Dialogue.")]
    public VisualNovelDialogueContainer hopefulDialogue;

    public VisualNovelDialogueContainer DialogueFor(bool passed, bool risky)
    {
        if (!passed) return badDialogue;
        return risky && hopefulDialogue ? hopefulDialogue : neutralDialogue;
    }
}

/// <summary>
/// The goals of the game: a list of <see cref="Level"/>s, one per dice round, played in order, with which dialogue
/// plays after a win or a loss. Once the list runs out, the last level repeats. How many points a round needs comes
/// from a separate list, by how many rounds have been played (<see cref="pointsByRound"/>): the first round played
/// needs the first entry, whichever level it is. Access via <c>LevelManager.Instance</c>.
///
/// <para>Levels can also be picked in any order from the phone (<see cref="LevelPhoneInteractable"/>):
/// each can be completed once, and the <see cref="Level.isFinal"/> one only after all the others.</para>
/// </summary>
public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance;

    [SerializeField] private List<Level> levels = new List<Level>
    {
        new Level { name = "Level 1" },
        new Level { name = "Level 2" },
        new Level { name = "Level 3", isFinal = true },
    };

    [Tooltip("Points the dice round needs to pass, by how many rounds have been played this game: [0] for the first " +
             "round, [1] for the second, ... - whichever level is picked. The last one repeats for later rounds.")]
    [SerializeField] private List<int> pointsByRound = new List<int> { 173, 293, 381, 586 };

    [Tooltip("A level only counts as completed (struck off the phone, towards unlocking the final level) when its " +
             "round was won. Off: playing it is enough.")]
    [SerializeField] private bool onlyWinsComplete = false;

    [Tooltip("Played after a round when its level has no Bad / Neutral / Hopeful Dialogue for the outcome. Optional.")]
    [SerializeField] private VisualNovelDialogueContainer defaultEndDialogue;

    [Header("Outcome calls")]
    [Tooltip("Speaker id \"1\" in the outcome dialogues (Player Speaker Id): the hero's Talkable - its voice " +
             "clips and text color.")]
    [SerializeField] private GameObject playerTalkable;
    [Tooltip("Outcome dialogue nodes with this speaker id are spoken by the Player Talkable.")]
    [SerializeField] private string playerSpeakerId = "1";
    [Tooltip("Outcome dialogue nodes with this speaker id are spoken by the level's Speaker Talkable.")]
    [SerializeField] private string otherSpeakerId = "2";
    [Tooltip("Looped over the black screen before an outcome dialogue, until its first line starts typing " +
             "(the phone ringing while the black fades away). Empty: no ringing.")]
    [SerializeField] private AudioClip callStartLoop;
    [SerializeField, Range(0f, 1f)] private float callStartVolume = 1f;
    [Tooltip("Seconds the screen stays black with the phone ringing before the outcome dialogue starts fading in.")]
    [SerializeField, Min(0f)] private float ringHold = 1.5f;
    [Tooltip("Seconds the ringing fades out over once the dialogue's first line starts.")]
    [SerializeField, Min(0f)] private float ringFadeOut = 0.25f;

    [Header("Debug")]
    [Tooltip("Debug only: the digit keys (top row or numpad) run a level's dice round straight away - " +
             "1 runs levels[0], 2 runs levels[1], ..., 9 runs levels[8], 0 runs levels[9]. Works from the hub " +
             "or the intro, and goes back to the hub afterwards.")]
    [SerializeField] private bool debugKeys = false;

    /// <summary>Debug hotkeys, in level order: index i runs levels[i].</summary>
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

    [Header("Events")]
    /// <summary>The level index (0-based) a round starts on.</summary>
    public UnityEvent<int> onLevelStarted = new UnityEvent<int>();
    public UnityEvent<int> onLevelWon = new UnityEvent<int>();
    public UnityEvent<int> onLevelLost = new UnityEvent<int>();

    /// <summary>Index of the level the next (or running) round plays.</summary>
    public int CurrentIndex { get; private set; }

    public int LevelsWon { get; private set; }
    public int LevelsLost { get; private set; }

    /// <summary>Rounds played (won or lost) this game; the running round isn't counted until it's over.</summary>
    public int RoundsPlayed => LevelsWon + LevelsLost;

    /// <summary>Index of the level whose round was played last; -1 before any round this game.</summary>
    public int LastPlayedIndex { get; private set; } = -1;

    /// <summary>Whether the last played round (<see cref="LastPlayedIndex"/>) was won.</summary>
    public bool LastPlayedPassed { get; private set; }

    public IReadOnlyList<Level> Levels => levels;

    /// <summary>The level the next (or running) round plays; the last one once the list has run out.</summary>
    public Level Current => levels.Count > 0 ? levels[Mathf.Min(CurrentIndex, levels.Count - 1)] : null;

    /// <summary>Points the next (or running) round needs: <see cref="pointsByRound"/>[<see cref="RoundsPlayed"/>], the last entry once they run out.</summary>
    public int PointsToPass => pointsByRound.Count > 0 ? Mathf.Max(0, pointsByRound[Mathf.Min(RoundsPlayed, pointsByRound.Count - 1)]) : 0;

    /// <summary>True once every level in the list has been played.</summary>
    public bool AllLevelsPlayed => CurrentIndex >= levels.Count;

    private readonly HashSet<int> completed = new HashSet<int>();

    public bool IsCompleted(int index) => completed.Contains(index);

    public bool IsFinal(int index) => index >= 0 && index < levels.Count && levels[index].isFinal;

    /// <summary>True once every level that isn't final is completed.</summary>
    public bool FinalUnlocked
    {
        get
        {
            for (int i = 0; i < levels.Count; i++)
                if (!levels[i].isFinal && !completed.Contains(i)) return false;
            return true;
        }
    }

    /// <summary>Whether levels[<paramref name="index"/>] can be started from the phone now.</summary>
    public bool CanStart(int index) =>
        index >= 0 && index < levels.Count && !IsCompleted(index) && (!IsFinal(index) || FinalUnlocked);

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance, setDontDestroy: false);
        if (levels.Count == 0) h.Out("LevelManager: no levels set up.");
        if (pointsByRound.Count == 0) h.Out("LevelManager: Points By Round is empty, rounds need 0 points.");
    }

    private void Update()
    {
        if (!DebugHotkeys.Allowed(debugKeys)) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        for (int i = 0; i < DebugDigitKeys.Length; i++)
        {
            if (!keyboard[DebugDigitKeys[i]].wasPressedThisFrame &&
                !keyboard[DebugNumpadKeys[i]].wasPressedThisFrame) continue;

            DebugRunLevel(i);
            return;
        }
    }

    /// <summary>Debug: makes levels[<paramref name="index"/>] current and runs its round, or logs why it can't.</summary>
    public void DebugRunLevel(int index)
    {
        if (index < 0 || index >= levels.Count)
        {
            h.Out($"LevelManager: no level at index {index} ({levels.Count} set up).");
            return;
        }

        FuneralGameManager game = FuneralGameManager.Instance;
        if (!game)
        {
            h.Out("LevelManager: no FuneralGameManager to run the level.");
            return;
        }

        if (!game.CanRunDebugLevel)
        {
            h.Out($"LevelManager: can't run {levels[index].name} now ({game.CurrentPhase}) - only from the hub or the intro.");
            return;
        }

        JumpTo(index);
        game.DebugRunCurrentLevel();
    }

    /// <summary>Makes levels[<paramref name="index"/>] the current level (clamped to the list).</summary>
    public void JumpTo(int index)
    {
        CurrentIndex = Mathf.Clamp(index, 0, Mathf.Max(0, levels.Count - 1));
    }

    /// <summary>Back to the first level (a new game).</summary>
    public void ResetLevels()
    {
        CurrentIndex = 0;
        LevelsWon = 0;
        LevelsLost = 0;
        LastPlayedIndex = -1;
        LastPlayedPassed = false;
        completed.Clear();
    }

    /// <summary>Call as the current level's round starts.</summary>
    public void BeginLevel()
    {
        h.Out($"LevelManager: {Current?.name ?? "no level"} (round {RoundsPlayed + 1}) - {PointsToPass} points to pass.");
        onLevelStarted?.Invoke(CurrentIndex);
    }

    /// <summary>Whether <paramref name="points"/> passes the current level.</summary>
    public bool Passes(int points) => points >= PointsToPass;

    /// <summary>
    /// Records the current level's outcome, moves on to the next level, and returns the dialogue to play
    /// for it (bad, neutral or hopeful - <paramref name="risky"/>: the risky goal was reached too - else
    /// <see cref="defaultEndDialogue"/>; may be null), its speakers set to <see cref="playerTalkable"/> and the
    /// level's <see cref="Level.speakerTalkable"/>.
    /// </summary>
    public VisualNovelDialogueContainer CompleteLevel(bool passed, bool risky = false)
    {
        Level level = Current;
        // Past the end of the list the last level repeats; it's that one being completed.
        int index = Mathf.Min(CurrentIndex, levels.Count - 1);

        if (passed) LevelsWon++;
        else LevelsLost++;
        if (index >= 0 && (passed || !onlyWinsComplete)) completed.Add(index);
        LastPlayedIndex = index;
        LastPlayedPassed = passed;
        CurrentIndex++;

        if (passed) onLevelWon?.Invoke(index);
        else onLevelLost?.Invoke(index);

        VisualNovelDialogueContainer dialogue = level?.DialogueFor(passed, risky);
        return WithSpeakers(dialogue ? dialogue : defaultEndDialogue, level);
    }

    // Runtime copies of the outcome dialogues, by source asset, with the scene's talkables as speakers.
    private readonly Dictionary<VisualNovelDialogueContainer, VisualNovelDialogueContainer> speakerCopies =
        new Dictionary<VisualNovelDialogueContainer, VisualNovelDialogueContainer>();

    /// <summary>
    /// A runtime copy of <paramref name="dialogue"/> whose speaker ids <see cref="playerSpeakerId"/> and
    /// <see cref="otherSpeakerId"/> point at <see cref="playerTalkable"/> and the level's talkable. The asset
    /// itself can't hold scene objects; the copy is what the visual novel stage instantiates and plays.
    /// </summary>
    private VisualNovelDialogueContainer WithSpeakers(VisualNovelDialogueContainer dialogue, Level level)
    {
        if (!dialogue) return null;

        GameObject other = level?.speakerTalkable;
        if (!playerTalkable && !other) return dialogue;

        if (!speakerCopies.TryGetValue(dialogue, out VisualNovelDialogueContainer copy) || !copy)
        {
            copy = Instantiate(dialogue);
            copy.name = dialogue.name;
            speakerCopies[dialogue] = copy;
        }

        SetSpeaker(copy, playerSpeakerId, playerTalkable);
        SetSpeaker(copy, otherSpeakerId, other);
        return copy;
    }

    private static void SetSpeaker(DialogueContainer dialogue, string id, GameObject talkable)
    {
        if (string.IsNullOrEmpty(id) || !talkable) return;

        int index = dialogue.speakerIds.IndexOf(id);
        if (index < 0)
        {
            index = dialogue.speakerIds.Count;
            dialogue.speakerIds.Add(id);
        }

        while (dialogue.speakerPrefabs.Count <= index) dialogue.speakerPrefabs.Add(null);
        dialogue.speakerPrefabs[index] = talkable;
    }

    private void OnDestroy()
    {
        StopOutcomeRing();
        foreach (VisualNovelDialogueContainer copy in speakerCopies.Values)
            if (copy) Destroy(copy);
        speakerCopies.Clear();
    }

    // ---------------------------------------------------------------------
    // Ringing before an outcome dialogue
    // ---------------------------------------------------------------------

    private AudioSource ringSource;

    /// <summary>
    /// Starts looping <see cref="callStartLoop"/> until the next dialogue line starts typing (or
    /// <see cref="StopOutcomeRing"/>). Returns how long to hold the black before the dialogue starts.
    /// </summary>
    public float StartOutcomeRing()
    {
        if (!callStartLoop) return 0f;

        if (!ringSource)
        {
            ringSource = gameObject.AddComponent<AudioSource>();
            ringSource.playOnAwake = false;
            ringSource.loop = true;
            ringSource.spatialBlend = 0f;
            ringSource.outputAudioMixerGroup = AudioMixerManager.GetSFXGroup();
        }

        Tween.StopAll(ringSource);
        ringSource.clip = callStartLoop;
        ringSource.volume = callStartVolume;
        ringSource.Play();

        DialogueManager manager = DialogueManager.Instance;
        if (manager)
        {
            manager.onStartNode.RemoveListener(StopOutcomeRing);
            manager.onStartNode.AddListener(StopOutcomeRing);
        }

        return ringHold;
    }

    /// <summary>Fades the ringing out (if it's on).</summary>
    public void StopOutcomeRing()
    {
        DialogueManager manager = DialogueManager.Instance;
        if (manager) manager.onStartNode.RemoveListener(StopOutcomeRing);

        if (!ringSource || !ringSource.isPlaying) return;

        Tween.StopAll(ringSource);
        if (ringFadeOut <= 0f) ringSource.Stop();
        else Tween.AudioVolume(ringSource, 0f, ringFadeOut, useUnscaledTime: true).OnComplete(ringSource, s => s.Stop());
    }
}
