using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The phone with the level list, in three steps:
/// <list type="number">
/// <item>Clicked, it's shown up close like any showcase object, and its dialogue plays (the Dialogues list, in
/// order, the last one repeating; none: straight to the list).</item>
/// <item>Then the level list appears over it (<see cref="ShowcaseDialogueManager.ShowList(IReadOnlyList{Sprite}, IReadOnlyList{float}, bool, DialogueContainer, IReadOnlyList{ShowcaseDialogueManager.ListLine}, Action{int}, Action, Talkable)"/>),
/// one line per <see cref="LevelManager"/> level; completed levels are struck through (keeping their color) and can't be picked.</item>
/// <item>Picking a level plays its picked dialogue with the list still on screen, then starts the level
/// (<see cref="FuneralGameManager.PlayLevel"/>).</item>
/// </list>
/// A final level, picked before every other level is completed, plays its <see cref="Level.lockedDialogue"/> (else
/// <see cref="finalLockedDialogue"/>) over the list instead of starting; the list stays up after it.
/// </summary>
public class LevelPhoneInteractable : ShowcaseInteractable
{
    [Serializable]
    public class LevelDialogue
    {
        [Tooltip("Index in the LevelManager's level list (0 = the first level).")]
        [Min(0)] public int level;
        public DialogueContainer dialogue;
    }

    [Header("Level picked")]
    [Tooltip("Played over the phone, with the list still on screen, once a level is picked; the level starts after " +
             "it. Used for levels with no entry below. Empty: the level starts right away.")]
    [SerializeField] private DialogueContainer pickedDialogue;
    [Tooltip("Per level: played instead of Picked Dialogue.")]
    [SerializeField] private List<LevelDialogue> levelPickedDialogues = new List<LevelDialogue>();
    [Tooltip("Played over the phone when a final level is picked before everything else is completed - for final " +
             "levels without a Locked Dialogue of their own (set in the LevelManager).")]
    [SerializeField] private DialogueContainer finalLockedDialogue;

    [Header("Line text")]
    [Tooltip("{0} = level name.")]
    [SerializeField] private string levelFormat = "{0}";
    [Tooltip("{0} = level name.")]
    [SerializeField] private string completedFormat = "<s>{0}</s>";
    [Tooltip("{0} = level name. The final level while it's still locked.")]
    [SerializeField] private string lockedFinalFormat = "{0}";

    // Levels are started from the hub only.
    protected override bool UsableDuringDice => false;

    protected override bool IsAvailable() =>
        LevelManager.Instance && LevelManager.Instance.Levels.Count > 0 && !ShowcaseBusy;

    protected override void Interact()
    {
        ShowcaseDialogueManager showcase = FindShowcase();
        if (!showcase) return;

        DialogueContainer intro = CurrentDialogue;
        List<ShowcaseDialogueManager.ListLine> lines = BuildLines(LevelManager.Instance);
        GetPicture(out List<Sprite> frames, out List<float> durations, out bool loop);
        // Only a pick closes the list - unless nothing can be picked, which would leave the player stuck.
        bool cancellable = !lines.Exists(line => line.interactable);
        if (showcase.ShowList(frames, durations, loop, intro, lines, PickLevel, speaker: Speaker, cancellable: cancellable))
            OnDialogueShown(intro);
    }

    private List<ShowcaseDialogueManager.ListLine> BuildLines(LevelManager levels)
    {
        List<ShowcaseDialogueManager.ListLine> lines = new List<ShowcaseDialogueManager.ListLine>();
        for (int i = 0; i < levels.Levels.Count; i++)
        {
            Level level = levels.Levels[i];
            bool completed = levels.IsCompleted(i);
            bool locked = !completed && levels.IsFinal(i) && !levels.FinalUnlocked;

            string format = completed ? completedFormat : locked ? lockedFinalFormat : levelFormat;
            DialogueContainer lockedDialogue = level.lockedDialogue ? level.lockedDialogue : finalLockedDialogue;
            lines.Add(new ShowcaseDialogueManager.ListLine
            {
                text = string.Format(format, level.name),
                // A locked final level is still clickable (and lights up): it says why it won't start, and never does.
                interactable = !completed,
                // Completed levels are only struck through (completedFormat), not faded.
                keepColor = completed,
                blocked = locked,
                dialogue = locked ? lockedDialogue : null,
                pickDialogue = locked ? null : PickedDialogueFor(i),
            });
        }
        return lines;
    }

    private DialogueContainer PickedDialogueFor(int level)
    {
        foreach (LevelDialogue entry in levelPickedDialogues)
            if (entry.level == level && entry.dialogue) return entry.dialogue;
        return pickedDialogue;
    }

    private static void PickLevel(int index)
    {
        if (FuneralGameManager.Instance) FuneralGameManager.Instance.PlayLevel(index);
    }
}
