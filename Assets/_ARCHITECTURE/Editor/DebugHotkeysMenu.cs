using UnityEditor;

/// <summary>
/// <b>Tools &gt; Debug Hotkeys</b>: turns every debug hotkey in the game on or off at once
/// (<see cref="DebugHotkeys"/>). The choice is remembered per machine and works in and out of Play mode.
/// </summary>
[InitializeOnLoad]
public static class DebugHotkeysMenu
{
    private const string PrefsKey = "LD60.DebugHotkeys.Enabled";
    private const string EnabledPath = "Tools/Debug Hotkeys/Enabled";
    private const string EnablePath = "Tools/Debug Hotkeys/Enable All";
    private const string DisablePath = "Tools/Debug Hotkeys/Disable All";

    // Runs after every domain reload (opening the project, recompiling, entering Play mode).
    static DebugHotkeysMenu()
    {
        DebugHotkeys.Enabled = EditorPrefs.GetBool(PrefsKey, true);
    }

    [MenuItem(EnabledPath, priority = 0)]
    private static void Toggle() => Set(!DebugHotkeys.Enabled);

    [MenuItem(EnabledPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(EnabledPath, DebugHotkeys.Enabled);
        return true;
    }

    [MenuItem(EnablePath, priority = 20)]
    private static void EnableAll() => Set(true);

    [MenuItem(EnablePath, true)]
    private static bool EnableAllValidate() => !DebugHotkeys.Enabled;

    [MenuItem(DisablePath, priority = 21)]
    private static void DisableAll() => Set(false);

    [MenuItem(DisablePath, true)]
    private static bool DisableAllValidate() => DebugHotkeys.Enabled;

    private static void Set(bool enabled)
    {
        EditorPrefs.SetBool(PrefsKey, enabled);
        DebugHotkeys.Enabled = enabled;
    }
}
