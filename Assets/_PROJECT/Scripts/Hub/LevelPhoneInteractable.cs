using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The phone with the level list: clicking it shows the phone up close (<see cref="ShowcaseDialogueManager.ShowList"/>)
/// with one clickable line per <see cref="LevelManager"/> level. Picking a line starts that level
/// (<see cref="FuneralGameManager.PlayLevel"/>); completed levels are struck through and can't be picked. The final
/// level, picked before every other level is completed, plays <see cref="finalLockedDialogue"/> over the phone
/// instead of starting.
/// </summary>
public class LevelPhoneInteractable : HubInteractable
{
    [Header("Phone")]
    [Tooltip("The picture shown in the middle of the screen. Empty: this object's own Image sprite.")]
    [SerializeField] private Sprite phoneSprite;
    [Tooltip("Played over the phone when the final level is picked before everything else is completed.")]
    [SerializeField] private DialogueContainer finalLockedDialogue;
    [Tooltip("Optional speaker for the dialogue's nodes that don't name one. Empty: the showcase's Default Speaker.")]
    [SerializeField] private Talkable speaker;

    [Header("Line text")]
    [Tooltip("{0} = level name.")]
    [SerializeField] private string levelFormat = "{0}";
    [Tooltip("{0} = level name.")]
    [SerializeField] private string completedFormat = "<s>{0}</s>";
    [Tooltip("{0} = level name. The final level while it's still locked.")]
    [SerializeField] private string lockedFinalFormat = "{0}";

    private Sprite Sprite
    {
        get
        {
            if (phoneSprite) return phoneSprite;
            Image image = GetComponentInChildren<Image>();
            return image ? image.sprite : null;
        }
    }

    protected override bool IsAvailable() =>
        LevelManager.Instance && LevelManager.Instance.Levels.Count > 0 &&
        (!ShowcaseDialogueManager.Instance || !ShowcaseDialogueManager.Instance.IsShowing);

    protected override void Interact()
    {
        ShowcaseDialogueManager showcase = ShowcaseDialogueManager.Instance;
        if (!showcase) showcase = FindFirstObjectByType<ShowcaseDialogueManager>(FindObjectsInactive.Include);
        if (!showcase)
        {
            h.Out($"LevelPhoneInteractable '{name}': no ShowcaseDialogueManager in the scene.");
            return;
        }

        showcase.ShowList(Sprite, BuildLines(LevelManager.Instance), PickLevel, speaker: speaker);
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
            lines.Add(new ShowcaseDialogueManager.ListLine
            {
                text = string.Format(format, level.name),
                // A locked final level is still clickable: it says why it won't start.
                interactable = !completed && (!locked || finalLockedDialogue),
                dialogue = locked ? finalLockedDialogue : null,
            });
        }
        return lines;
    }

    private static void PickLevel(int index)
    {
        if (FuneralGameManager.Instance) FuneralGameManager.Instance.PlayLevel(index);
    }
}
