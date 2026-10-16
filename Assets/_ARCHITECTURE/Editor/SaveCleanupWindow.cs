using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only tool for wiping the game's save files.
///
/// <para><b>Why it exists.</b> Saves live in <c>Application.persistentDataPath/&lt;saveFolderName&gt;</c>,
/// which is nowhere near the project folder — so clearing them by hand means hunting down an OS-specific
/// path. This does it from the menu bar: <b>Tools &gt; Save Load &gt; Delete All Saves</b>.</para>
///
/// <para><b>Which folder it points at.</b> <c>persistentDataPath/Saves</c> — see
/// <see cref="ResolveSaveFolder"/>, which is also where to put the lookup back if a save manager with a
/// configurable folder name returns. The path it is about to touch is always shown before anything is
/// deleted.</para>
///
/// <para><b>What it deletes.</b> Only <c>*.json</c> files directly inside that folder. The folder itself
/// and anything else in it are left alone, so a mis-typed folder name can't take an unrelated directory
/// with it.</para>
///
/// <para>Works in and out of play mode.</para>
/// </summary>
public class SaveCleanupWindow : EditorWindow
{
    private const string DefaultFolderName = "Saves";

    private Vector2 scroll;
    private string folder = "";
    private string folderSource = "";
    private List<string> saves = new List<string>();

    [MenuItem("Tools/Save Load/Delete All Saves", false, 100)]
    public static void ShowWindow()
    {
        SaveCleanupWindow window = GetWindow<SaveCleanupWindow>(true, "Delete All Saves");
        window.minSize = new Vector2(420f, 260f);
        window.Refresh();
        window.Show();
    }

    /// <summary>Menu shortcut that skips the window: confirm, wipe, done.</summary>
    [MenuItem("Tools/Save Load/Delete All Saves (no window)", false, 101)]
    public static void DeleteAllImmediate()
    {
        string path = ResolveSaveFolder(out _);
        List<string> found = FindSaves(path);

        if (found.Count == 0)
        {
            EditorUtility.DisplayDialog("Delete All Saves", "Nothing to delete.\n\n" + path, "OK");
            return;
        }

        if (!Confirm(found.Count, path)) return;

        DeleteAll(path, found);
    }

    // ──────────────────────────────────────────────
    // Window
    // ──────────────────────────────────────────────

    private void OnFocus() => Refresh();

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Save folder", EditorStyles.boldLabel);
        EditorGUILayout.SelectableLabel(folder, EditorStyles.textField,
            GUILayout.Height(EditorGUIUtility.singleLineHeight));
        EditorGUILayout.LabelField(folderSource, EditorStyles.miniLabel);

        EditorGUILayout.Space();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Refresh")) Refresh();

            using (new EditorGUI.DisabledScope(!Directory.Exists(folder)))
                if (GUILayout.Button("Open folder")) EditorUtility.RevealInFinder(folder);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"Saves ({saves.Count})", EditorStyles.boldLabel);

        if (saves.Count == 0)
        {
            EditorGUILayout.HelpBox(
                Directory.Exists(folder) ? "This folder has no save files." : "This folder does not exist yet.",
                MessageType.Info);
        }
        else
        {
            using (EditorGUILayout.ScrollViewScope view = new EditorGUILayout.ScrollViewScope(scroll))
            {
                scroll = view.scrollPosition;

                foreach (string save in new List<string>(saves))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(Path.GetFileNameWithoutExtension(save));

                        if (GUILayout.Button("Delete", GUILayout.Width(60f)))
                        {
                            DeleteOne(save);
                            Refresh();
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }
        }

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(saves.Count == 0))
        {
            GUI.backgroundColor = new Color(1f, 0.55f, 0.55f);

            if (GUILayout.Button($"Delete all {saves.Count} save(s)", GUILayout.Height(30f)))
            {
                if (Confirm(saves.Count, folder))
                {
                    DeleteAll(folder, saves);
                    Refresh();
                    GUIUtility.ExitGUI();
                }
            }

            GUI.backgroundColor = Color.white;
        }
    }

    private void Refresh()
    {
        folder = ResolveSaveFolder(out folderSource);
        saves = FindSaves(folder);
        Repaint();
    }

    // ──────────────────────────────────────────────
    // Work
    // ──────────────────────────────────────────────

    /// <summary>
    /// The folder saves are written into: <c>persistentDataPath/Saves</c>.
    ///
    /// <para>This used to read the folder name off the save manager in the open scenes, so a project
    /// that renamed it was still cleaned correctly. That manager did not come over from the previous
    /// project, so there is one folder name again — put the lookup back here when a save manager with
    /// its own <c>saveFolderName</c> exists.</para>
    /// </summary>
    private static string ResolveSaveFolder(out string source)
    {
        source = $"The default \"{DefaultFolderName}\" folder — no save manager in the project to ask.";
        return Path.Combine(Application.persistentDataPath, DefaultFolderName);
    }

    /// <summary>Full paths of the save files in <paramref name="path"/>, alphabetically.</summary>
    private static List<string> FindSaves(string path)
    {
        List<string> found = new List<string>();
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return found;

        try
        {
            found.AddRange(Directory.GetFiles(path, "*.json", SearchOption.TopDirectoryOnly));
            found.Sort(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"SaveCleanupWindow: could not read '{path}' — {e.Message}");
        }

        return found;
    }

    private static bool Confirm(int count, string path) => EditorUtility.DisplayDialog(
        "Delete All Saves",
        $"Permanently delete {count} save file(s)?\n\n{path}\n\nThis cannot be undone.",
        "Delete", "Cancel");

    private static void DeleteAll(string path, List<string> files)
    {
        int deleted = 0;
        foreach (string file in files)
            if (DeleteOne(file)) deleted++;

        h.Out($"SaveCleanupWindow: deleted {deleted}/{files.Count} save file(s) in '{path}'.");
    }

    private static bool DeleteOne(string file)
    {
        try
        {
            File.Delete(file);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"SaveCleanupWindow: could not delete '{file}' — {e.Message}");
            return false;
        }
    }
}
