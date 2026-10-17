using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>One dice round's goal and what plays after it.</summary>
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
    [Tooltip("Points the dice round must reach to pass.")]
    [Min(0)] public int pointsToPass = 20;
    [Tooltip("Played before the dice round when the level is picked from the phone. Optional.")]
    public VisualNovelDialogueContainer startDialogue;
    [Tooltip("Played when the round reaches Points To Pass.")]
    public VisualNovelDialogueContainer winDialogue;
    [Tooltip("Played when the round falls short.")]
    public VisualNovelDialogueContainer lossDialogue;

    public VisualNovelDialogueContainer DialogueFor(bool passed) => passed ? winDialogue : lossDialogue;
}

/// <summary>
/// The goals of the game: a list of <see cref="Level"/>s, one per dice round, played in order. Holds
/// how many points each round needs and which dialogue plays after a win or a loss. Once the list
/// runs out, the last level repeats. Access via <c>LevelManager.Instance</c>.
///
/// <para>Levels can also be picked in any order from the phone (<see cref="LevelPhoneInteractable"/>):
/// each can be completed once, and the <see cref="Level.isFinal"/> one only after all the others.</para>
/// </summary>
public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance;

    [SerializeField] private List<Level> levels = new List<Level>
    {
        new Level { name = "Level 1", pointsToPass = 10 },
        new Level { name = "Level 2", pointsToPass = 20 },
        new Level { name = "Level 3", pointsToPass = 30, isFinal = true },
    };

    [Tooltip("A level only counts as completed (struck off the phone, towards unlocking the final level) when its " +
             "round was won. Off: playing it is enough.")]
    [SerializeField] private bool onlyWinsComplete = false;

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

    /// <summary>Index of the level whose round was played last; -1 before any round this game.</summary>
    public int LastPlayedIndex { get; private set; } = -1;

    /// <summary>Whether the last played round (<see cref="LastPlayedIndex"/>) was won.</summary>
    public bool LastPlayedPassed { get; private set; }

    public IReadOnlyList<Level> Levels => levels;

    /// <summary>The level the next (or running) round plays; the last one once the list has run out.</summary>
    public Level Current => levels.Count > 0 ? levels[Mathf.Min(CurrentIndex, levels.Count - 1)] : null;

    /// <summary>Points the current level needs.</summary>
    public int PointsToPass => Current?.pointsToPass ?? 0;

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
        if (levels.Count == 0) h.Out("LevelManager: no levels set up, rounds need 0 points.");
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
        h.Out($"LevelManager: {Current?.name ?? "no level"} - {PointsToPass} points to pass.");
        onLevelStarted?.Invoke(CurrentIndex);
    }

    /// <summary>Whether <paramref name="points"/> passes the current level.</summary>
    public bool Passes(int points) => points >= PointsToPass;

    /// <summary>
    /// Records the current level's outcome, moves on to the next level, and returns the dialogue to play
    /// for it (win or loss; may be null).
    /// </summary>
    public VisualNovelDialogueContainer CompleteLevel(bool passed)
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

        return level?.DialogueFor(passed);
    }
}
