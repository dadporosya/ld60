using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hub object that plays a visual novel dialogue when clicked - the gun ("it's not time yet"), photos,
/// anything that only talks. Several dialogues are played in order, at random, or in order with the
/// last one repeating.
/// </summary>
public class DialogueInteractable : HubInteractable
{
    public enum Order
    {
        [Tooltip("In list order; after the last one, the last one repeats.")]
        SequentialThenLast,
        [Tooltip("In list order, starting over after the last one.")]
        Loop,
        [Tooltip("A random one each time.")]
        Random,
    }

    [Header("Dialogue")]
    [SerializeField] private List<VisualNovelDialogueContainer> dialogues = new List<VisualNovelDialogueContainer>();
    [SerializeField] private Order order = Order.SequentialThenLast;

    private int next;

    protected override bool IsAvailable() => dialogues.Count > 0;

    protected override void Interact()
    {
        FuneralGameManager game = FuneralGameManager.Instance;
        VisualNovelDialogueContainer dialogue = PickDialogue();
        if (game) game.PlayHubDialogue(dialogue);
        else if (HubDialogueRunner.Instance) HubDialogueRunner.Instance.Play(dialogue);
    }

    private VisualNovelDialogueContainer PickDialogue()
    {
        if (order == Order.Random) return h.RandChoice(dialogues);

        VisualNovelDialogueContainer dialogue = dialogues[Mathf.Min(next, dialogues.Count - 1)];
        next++;
        if (order == Order.Loop && next >= dialogues.Count) next = 0;
        return dialogue;
    }
}
