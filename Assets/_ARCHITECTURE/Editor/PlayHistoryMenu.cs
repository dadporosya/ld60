using UnityEditor;

/// <summary>
/// <b>Tools &gt; Reset Game To First Enter</b>: wipes the game's progress (<see cref="PlayHistory.ResetToFirstEnter"/>,
/// i.e. every PlayerPrefs key) so the next launch plays as if the game had never been opened.
/// Editor settings (EditorPrefs, e.g. Debug Hotkeys) are left alone. Works in and out of Play mode.
/// </summary>
public static class PlayHistoryMenu
{
    [MenuItem("Tools/Reset Game To First Enter", priority = 50)]
    private static void ResetToFirstEnter()
    {
        if (!EditorUtility.DisplayDialog("Reset Game To First Enter",
                "Delete every PlayerPrefs key (played dialogues, entered scenes, settings)?\n\n" +
                "The next launch plays as a first enter. This can't be undone.",
                "Reset", "Cancel"))
            return;

        PlayHistory.ResetToFirstEnter();
    }
}
