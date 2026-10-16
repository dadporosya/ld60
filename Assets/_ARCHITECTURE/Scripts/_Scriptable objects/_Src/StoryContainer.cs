using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "StoryContainer", menuName = "Scriptable Objects/StoryContainer")]
public class StoryContainer : ScriptableObject
{
    public P.MoodType mood;
    public string pathLabel;
    
    public List<DialogueContainer> onArrivalDialogues;
    public List<DialogueContainer> onLevelUpDialogues;
    public List<DialogueContainer> endingDialogues;
    
    [TextArea(5, 15)]
    public string briefStory;

    // Loads all DialogueContainers from Resources into the matching lists.
    // Paths: Dialogues/Allies/{OnArrival|OnLevelUp|Ending}/{mood}/{pathLabel}
    public void AssignDialogues()
    {
        onArrivalDialogues = LoadDialogues("OnArrival");
        onLevelUpDialogues = LoadDialogues("OnLevelUp");
        endingDialogues    = LoadDialogues("Ending");
    }

    private List<DialogueContainer> LoadDialogues(string category)
    {
        string path = $"Dialogues/Allies/{category}/{mood}/{pathLabel}";
        DialogueContainer[] loaded = Resources.LoadAll<DialogueContainer>(path);
        if (loaded == null || loaded.Length == 0)
            h.Out($"No dialogues found at Resources/{path}");
        return new List<DialogueContainer>(loaded);
    }
}
