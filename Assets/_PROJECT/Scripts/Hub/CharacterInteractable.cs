using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The character: shown up close like any showcase object, with a dialogue picked by the level played last
/// (<see cref="LevelManager.LastPlayedIndex"/>) - and whether its round was won. Before any level has been played,
/// or after a level with no entry, the <see cref="defaultDialogue"/> plays.
/// </summary>
public class CharacterInteractable : ShowcaseInteractableBase
{
    [Serializable]
    public class AfterLevel
    {
        [Tooltip("Index in the LevelManager's level list (0 = the first level).")]
        [Min(0)] public int level;
        public DialogueContainer dialogue;
        [Tooltip("Used instead of Dialogue when the level's round was lost. Empty: Dialogue.")]
        public DialogueContainer lossDialogue;
    }

    [Header("Character")]
    [Tooltip("Played while no level has been played yet, and after a level with no entry below.")]
    [SerializeField] private DialogueContainer defaultDialogue;
    [Tooltip("What the character says after each level.")]
    [SerializeField] private List<AfterLevel> afterLevels = new List<AfterLevel>();

    protected override DialogueContainer CurrentDialogue
    {
        get
        {
            LevelManager levels = LevelManager.Instance;
            if (!levels || levels.LastPlayedIndex < 0) return defaultDialogue;

            foreach (AfterLevel entry in afterLevels)
            {
                if (entry.level != levels.LastPlayedIndex) continue;

                DialogueContainer dialogue = !levels.LastPlayedPassed && entry.lossDialogue ? entry.lossDialogue : entry.dialogue;
                if (dialogue) return dialogue;
            }

            return defaultDialogue;
        }
    }
}
