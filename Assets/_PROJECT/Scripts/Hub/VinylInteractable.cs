using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The vinyl: shown up close like any showcase object, with a dialogue picked by how many levels have been
/// passed this game (<see cref="LevelManager"/>): <see cref="dialogues"/>[0] before any, [1] after one, ... -
/// the last entry once they run out.
/// </summary>
public class VinylInteractable : ShowcaseInteractableBase
{
    public enum LevelCount
    {
        /// <summary>Levels played, won or lost.</summary>
        Played,
        /// <summary>Levels won only.</summary>
        Won,
    }

    [Serializable]
    public class LevelDialogue
    {
        public DialogueContainer dialogue;
    }

    [Header("Vinyl")]
    [Tooltip("Defaults to LevelManager.Instance.")]
    [SerializeField] private LevelManager levels;
    [Tooltip("Which levels count: every one played (won or lost), or only the ones won.")]
    [SerializeField] private LevelCount count = LevelCount.Played;
    [Tooltip("By levels passed: [0] before any, [1] after one, [2] after two, ... Past the end of the list: the " +
             "last entry. An empty entry: the closest one before it.")]
    [SerializeField] private List<LevelDialogue> dialogues = new List<LevelDialogue>
    {
        new LevelDialogue(), // no level passed yet
        new LevelDialogue(), // one
        new LevelDialogue(), // two
        new LevelDialogue(), // three or more
    };

    private LevelManager Levels => levels ? levels : levels = LevelManager.Instance;

    /// <summary>Levels passed so far, as <see cref="count"/> counts them.</summary>
    public int LevelsPassed => !Levels ? 0 : count == LevelCount.Won ? Levels.LevelsWon : Levels.RoundsPlayed;

    protected override DialogueContainer CurrentDialogue
    {
        get
        {
            // The entry for the count, else the nearest filled one before it.
            for (int i = Mathf.Min(LevelsPassed, dialogues.Count - 1); i >= 0; i--)
                if (dialogues[i] != null && dialogues[i].dialogue) return dialogues[i].dialogue;
            return null;
        }
    }
}
