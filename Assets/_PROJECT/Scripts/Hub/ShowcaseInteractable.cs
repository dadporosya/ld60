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
    [Tooltip("Animated picture: these frames play in order while the showcase is up. Empty: the frames of this " +
             "object's UI sprite animation (see below), else the still Showcase Sprite.")]
    [SerializeField] private List<Sprite> showcaseFrames = new List<Sprite>();
    [Tooltip("Showcase Frames only: their speed.")]
    [SerializeField, Min(0.1f)] private float framesPerSecond = 8f;
    [Tooltip("Without Showcase Frames, play the frames of this object's UIImageSpriteChangeAnimation, with its timing.")]
    [SerializeField] private bool useUIAnimationFrames = true;
    [Tooltip("Which UI sprite animation to take the frames from. Empty: the first one on this object or its children.")]
    [SerializeField] private UIImageSpriteChangeAnimation uiAnimation;
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
        else if (TryGetUIAnimationFrames(out List<Sprite> frames, out List<float> durations))
            showcase.Show(frames, durations, loopFrames, dialogue, speaker);
        else showcase.Show(Sprite, dialogue, speaker);
    }

    /// <summary>
    /// The frames (and their timing) of this object's own <see cref="UIImageSpriteChangeAnimation"/>, for a
    /// showcase without Showcase Frames: the picture in the middle moves the way the object in the hub does.
    /// </summary>
    private bool TryGetUIAnimationFrames(out List<Sprite> frames, out List<float> durations)
    {
        frames = new List<Sprite>();
        durations = new List<float>();
        if (!useUIAnimationFrames) return false;

        UIImageSpriteChangeAnimation animation = uiAnimation ? uiAnimation : GetComponentInChildren<UIImageSpriteChangeAnimation>(true);
        if (!animation || animation.frames == null) return false;

        // The animation shows its initial sprite for empty frames; so does the showcase.
        Sprite fallback = animation.initialSprite ? animation.initialSprite : Sprite;
        for (int i = 0; i < animation.frames.Count; i++)
        {
            Sprite frame = animation.frames[i] ? animation.frames[i] : fallback;
            if (!frame) continue;

            frames.Add(frame);
            durations.Add(i < animation.gapsBetweenFrames.Count ? animation.gapsBetweenFrames[i] : animation.defaultGapsBetweenFrames);
        }

        return frames.Count > 0;
    }
}
