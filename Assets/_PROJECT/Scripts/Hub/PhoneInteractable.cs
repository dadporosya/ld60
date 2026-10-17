using UnityEngine;

/// <summary>
/// The phone: clicking it opens the dial panel to pick who to call next
/// (<see cref="FuneralGameManager.OpenPhone"/>). Unavailable once everyone has been called.
/// </summary>
public class PhoneInteractable : HubInteractable
{
    [Tooltip("Played when there is nobody left to call (optional).")]
    [SerializeField] private VisualNovelDialogueContainer nobodyLeftDialogue;

    protected override bool IsAvailable()
    {
        FuneralGameManager game = FuneralGameManager.Instance;
        return game && (game.HasUncalledContacts || nobodyLeftDialogue);
    }

    protected override void Interact()
    {
        FuneralGameManager game = FuneralGameManager.Instance;
        if (game.HasUncalledContacts) game.OpenPhone();
        else game.PlayHubDialogue(nobodyLeftDialogue);
    }
}
