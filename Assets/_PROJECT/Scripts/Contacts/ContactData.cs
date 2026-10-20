using UnityEngine;

/// <summary>
/// Someone the hero has to call before the song ends. A call plays <see cref="callDialogue"/>, then a
/// dice round; the points it needs and the dialogue after it come from the current level
/// (<see cref="LevelManager"/>), not from the contact.
/// </summary>
[CreateAssetMenu(fileName = "Contact", menuName = "Funeral Song/Contact")]
public class ContactData : ScriptableObject
{
    [Localized] public string displayName = "Mom";
    [TextArea, Localized] public string note;
    [Tooltip("Optional portrait for the dial panel.")]
    public Sprite portrait;

    [Header("Dialogues")]
    [Tooltip("Dialing and the start of the conversation, played before the dice. Optional.")]
    public VisualNovelDialogueContainer callDialogue;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
}
