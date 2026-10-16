using UnityEngine;

/// <summary>
/// Tracks dialogue and scene progression in PlayerPrefs across runs.
/// </summary>
public static class PlayHistory
{
    private const string PlayedKeyPrefix = "PlayHistory.Played.";
    private const string DialoguePlayedKeyPrefix = "PlayHistory.Dialogue.";

    public static bool HasPlayed(string sceneName) => PlayerPrefs.GetInt(PlayedKeyPrefix + sceneName, 0) == 1;

    public static void MarkPlayed(string sceneName)
    {
        if (HasPlayed(sceneName)) return;
        PlayerPrefs.SetInt(PlayedKeyPrefix + sceneName, 1);
        PlayerPrefs.Save();
    }

    public static bool HasPlayedDialogue(string dialogueName)
    {
        if (string.IsNullOrEmpty(dialogueName)) return false;
        return PlayerPrefs.GetInt(DialoguePlayedKeyPrefix + dialogueName, 0) == 1;
    }

    public static void MarkDialoguePlayed(string dialogueName)
    {
        if (string.IsNullOrEmpty(dialogueName) || HasPlayedDialogue(dialogueName)) return;
        PlayerPrefs.SetInt(DialoguePlayedKeyPrefix + dialogueName, 1);
        PlayerPrefs.Save();
    }

    public static void ResetToFirstEnter()
    {
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        h.Out("PlayHistory: reset to first enter — every PlayerPrefs key deleted.");
    }
}
