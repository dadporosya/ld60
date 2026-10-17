using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The rifle: shown up close like any showcase object, with a dialogue picked by how many rounds the player has
/// lost this game (<see cref="LevelManager.LevelsLost"/>).
/// </summary>
public class RifleInteractable : ShowcaseInteractableBase
{
    [Header("Rifle")]
    [Tooltip("Index = rounds lost: [0] with no losses, [1] after one, [2] after two... The last one repeats for " +
             "more losses; an empty entry falls back to the one before it.")]
    [SerializeField] private List<DialogueContainer> dialoguesByLosses = new List<DialogueContainer>();

    protected override DialogueContainer CurrentDialogue
    {
        get
        {
            int losses = LevelManager.Instance ? LevelManager.Instance.LevelsLost : 0;
            for (int i = Mathf.Min(losses, dialoguesByLosses.Count - 1); i >= 0; i--)
                if (dialoguesByLosses[i]) return dialoguesByLosses[i];
            return null;
        }
    }
}
