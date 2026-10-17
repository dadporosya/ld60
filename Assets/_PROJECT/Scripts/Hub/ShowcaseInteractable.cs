using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Hub object that, when clicked, is shown up close: the screen darkens, its picture appears in the middle
/// and a regular dialogue plays over it (<see cref="ShowcaseDialogueManager"/>). Several dialogues play in
/// order, the last one repeating.
/// </summary>
public class ShowcaseInteractable : HubInteractable
{
    [Header("Showcase")]
    [Tooltip("The picture shown in the middle of the screen. Empty: this object's own Image sprite.")]
    [SerializeField] private Sprite showcaseSprite;
    [Tooltip("Played in order; after the last one, the last one repeats.")]
    [SerializeField] private List<DialogueContainer> dialogues = new List<DialogueContainer>();
    [Tooltip("Optional speaker for nodes that don't name one. Empty: the manager's Default Speaker.")]
    [SerializeField] private Talkable speaker;

    private int next;

    private Sprite Sprite
    {
        get
        {
            if (showcaseSprite) return showcaseSprite;
            Image image = GetComponentInChildren<Image>();
            return image ? image.sprite : null;
        }
    }

    protected override bool IsAvailable() =>
        dialogues.Count > 0 && (!ShowcaseDialogueManager.Instance || !ShowcaseDialogueManager.Instance.IsShowing);

    protected override void Interact()
    {
        ShowcaseDialogueManager showcase = ShowcaseDialogueManager.Instance;
        if (!showcase) showcase = FindFirstObjectByType<ShowcaseDialogueManager>(FindObjectsInactive.Include);
        if (!showcase)
        {
            h.Out($"ShowcaseInteractable '{name}': no ShowcaseDialogueManager in the scene.");
            return;
        }

        DialogueContainer dialogue = dialogues[Mathf.Min(next, dialogues.Count - 1)];
        next++;
        showcase.Show(Sprite, dialogue, speaker);
    }
}
