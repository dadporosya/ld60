using UnityEngine;

/// <summary>
/// Someone the hero has to call before the song ends. A call goes: <see cref="callDialogue"/> ->
/// dice round against <see cref="targetScore"/> -> <see cref="goodDialogue"/> (they care) or
/// <see cref="badDialogue"/> (indifference) -> card pick (Hope or Hate).
/// </summary>
[CreateAssetMenu(fileName = "Contact", menuName = "Funeral Song/Contact")]
public class ContactData : ScriptableObject
{
    public string displayName = "Mom";
    [TextArea] public string note;
    [Tooltip("Optional portrait for the dial panel.")]
    public Sprite portrait;

    [Header("Dice")]
    [Tooltip("Points the dice round must reach for this person to show they care.")]
    [Min(0)] public int targetScore = 20;

    [Header("Dialogues")]
    [Tooltip("Dialing and the start of the conversation, played before the dice. Optional.")]
    public VisualNovelDialogueContainer callDialogue;
    [Tooltip("The dice reached the target: they value the hero and don't want him to die.")]
    public VisualNovelDialogueContainer goodDialogue;
    [Tooltip("The dice missed the target: indifference or hostility.")]
    public VisualNovelDialogueContainer badDialogue;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
}
