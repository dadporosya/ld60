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
    [Tooltip("The picture shown in the middle of the screen. Empty: this object's own Image sprite. " +
             "Ignored when Showcase Frames are set.")]
    [SerializeField] private Sprite showcaseSprite;
    [Tooltip("Animated picture: these frames play in order while the showcase is up. Empty: the still Showcase Sprite.")]
    [SerializeField] private List<Sprite> showcaseFrames = new List<Sprite>();
    [SerializeField, Min(0.1f)] private float framesPerSecond = 8f;
    [Tooltip("Start over after the last frame. Off: stop on the last frame.")]
    [SerializeField] private bool loopFrames = true;
    [Tooltip("Played in order; after the last one, the last one repeats.")]
    [SerializeField] private List<DialogueContainer> dialogues = new List<DialogueContainer>();
    [Tooltip("Optional speaker for nodes that don't name one. Empty: the manager's Default Speaker.")]
    [SerializeField] private Talkable speaker;

    private int next;

    private bool HasFrames
    {
        get
        {
            foreach (Sprite frame in showcaseFrames)
                if (frame) return true;
            return false;
        }
    }

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
        if (HasFrames) showcase.Show(showcaseFrames, framesPerSecond, loopFrames, dialogue, speaker);
        else showcase.Show(Sprite, dialogue, speaker);
    }
}
