using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hub object that, when clicked, is shown up close: the screen darkens, its picture appears in the middle
/// and a regular dialogue plays over it (<see cref="ShowcaseDialogueManager"/>). Several dialogues play in
/// order, the last one repeating.
/// </summary>
public class ShowcaseInteractable : ShowcaseInteractableBase
{
    [Header("Dialogue")]
    [Tooltip("Played in order; after the last one, the last one repeats.")]
    [SerializeField] private List<DialogueContainer> dialogues = new List<DialogueContainer>();

    private int next;

    protected override DialogueContainer CurrentDialogue =>
        dialogues.Count > 0 ? dialogues[Mathf.Min(next, dialogues.Count - 1)] : null;

    protected override void OnDialogueShown(DialogueContainer dialogue) => next++;
}
