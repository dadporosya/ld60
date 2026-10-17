using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Base for hub objects that, when clicked, are shown up close: the screen darkens, the object's picture (still or
/// animated) appears in the middle and a regular dialogue plays over it (<see cref="ShowcaseDialogueManager"/>).
/// Subclasses only say which dialogue plays right now (<see cref="CurrentDialogue"/>); the object can't be clicked
/// while that is empty.
/// </summary>
public abstract class ShowcaseInteractableBase : HubInteractable
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
    [Tooltip("Optional speaker for nodes that don't name one. Empty: the manager's Default Speaker.")]
    [SerializeField] private Talkable speaker;
    [Tooltip("Can also be looked at while a dice round is running.")]
    [SerializeField] private bool usableDuringDice = true;

    protected override bool UsableDuringDice => usableDuringDice;

    protected Talkable Speaker => speaker;

    /// <summary>The dialogue a click plays right now; null: nothing to say, the object can't be clicked.</summary>
    protected abstract DialogueContainer CurrentDialogue { get; }

    /// <summary>Called once a click has put <paramref name="dialogue"/> on screen.</summary>
    protected virtual void OnDialogueShown(DialogueContainer dialogue) { }

    /// <summary>True while some showcase is up (this object's or another's).</summary>
    protected static bool ShowcaseBusy => ShowcaseDialogueManager.Instance && ShowcaseDialogueManager.Instance.IsShowing;

    protected override bool IsAvailable() => CurrentDialogue && !ShowcaseBusy;

    protected override void Interact()
    {
        ShowcaseDialogueManager showcase = FindShowcase();
        if (!showcase) return;

        DialogueContainer dialogue = CurrentDialogue;
        GetPicture(out List<Sprite> frames, out List<float> durations, out bool loop);
        if (showcase.Show(frames, durations, loop, dialogue, speaker)) OnDialogueShown(dialogue);
    }

    protected ShowcaseDialogueManager FindShowcase()
    {
        ShowcaseDialogueManager showcase = ShowcaseDialogueManager.Instance;
        if (!showcase) showcase = FindFirstObjectByType<ShowcaseDialogueManager>(FindObjectsInactive.Include);
        if (!showcase) h.Out($"{GetType().Name} '{name}': no ShowcaseDialogueManager in the scene.");
        return showcase;
    }

    /// <summary>
    /// The picture to show, as frames with their own seconds each: the Showcase Frames, else this object's UI
    /// sprite animation, else one still sprite.
    /// </summary>
    protected void GetPicture(out List<Sprite> frames, out List<float> durations, out bool loop)
    {
        loop = loopFrames;
        if (HasFrames)
        {
            frames = new List<Sprite>(showcaseFrames);
            durations = new List<float>();
            for (int i = 0; i < frames.Count; i++) durations.Add(1f / framesPerSecond);
            return;
        }

        if (TryGetUIAnimationFrames(out frames, out durations)) return;

        frames = new List<Sprite> { Sprite };
        durations = new List<float> { 0f };
    }

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
