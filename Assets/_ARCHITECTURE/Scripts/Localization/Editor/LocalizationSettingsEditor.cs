using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The settings' inspector: the Default Language as a dropdown of the game's languages (the language shown when the
/// intro's language choice doesn't run), plus shortcuts to the Translations window.
/// </summary>
[CustomEditor(typeof(LocalizationSettings))]
public class LocalizationSettingsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        LocalizationSettings settings = (LocalizationSettings)target;
        serializedObject.Update();

        EditorGUILayout.HelpBox("The game starts in the Default Language. When the intro runs, its language choice " +
                                "replaces it for the session (or for good, with Remember Choice).", MessageType.None);

        // Default language as a dropdown.
        List<LocalizationSettings.Language> languages = settings.languages;
        string[] names = new string[languages.Count];
        int index = -1;
        for (int i = 0; i < languages.Count; i++)
        {
            names[i] = languages[i] != null ? $"{languages[i].nativeName} ({languages[i].code})" : "-";
            if (languages[i] != null && languages[i].code == settings.defaultLanguage) index = i;
        }
        EditorGUI.BeginChangeCheck();
        int picked = EditorGUILayout.Popup(new GUIContent("Default Language",
            serializedObject.FindProperty("defaultLanguage").tooltip), index, names);
        if (EditorGUI.EndChangeCheck() && picked >= 0)
            LocalizationWindow.SetDefaultLanguage(settings, languages[picked].code);
        if (index < 0)
            EditorGUILayout.HelpBox($"'{settings.defaultLanguage}' isn't one of the languages: the game starts in " +
                                    "the first one.", MessageType.Warning);

        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("languages"), true);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("rememberChoice"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("table"));
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        if (GUILayout.Button("Open Translations")) LocalizationWindow.Open();
        if (settings.rememberChoice && GUILayout.Button("Forget the player's saved language (PlayerPrefs)"))
            Loc.ForgetChoice();
    }
}

public static class LocalizationSettingsMenu
{
    private const string DefaultFolder = "Assets/_PROJECT/Localization";

    public static LocalizationSettings FindSettings()
    {
        LocalizationSettings settings = Resources.Load<LocalizationSettings>(LocalizationSettings.ResourcePath);
        if (settings) return settings;
        foreach (string guid in AssetDatabase.FindAssets("t:LocalizationSettings"))
        {
            settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(AssetDatabase.GUIDToAssetPath(guid));
            if (settings) return settings;
        }
        return null;
    }

    public static LocalizationSettings FindOrCreateSettings()
    {
        LocalizationSettings settings = FindSettings();
        if (settings) return settings;

        string folder = DefaultFolder + "/Resources";
        Directory.CreateDirectory(folder);
        settings = ScriptableObject.CreateInstance<LocalizationSettings>();
        AssetDatabase.CreateAsset(settings, $"{folder}/{LocalizationSettings.ResourcePath}.asset");
        CreateTable(settings);
        return settings;
    }

    /// <summary>An empty CSV (header only) for <paramref name="settings"/>, assigned as its table.</summary>
    public static void CreateTable(LocalizationSettings settings)
    {
        Directory.CreateDirectory(DefaultFolder);
        string path = AssetDatabase.GenerateUniqueAssetPath(DefaultFolder + "/Translations.csv");

        LocTable table = new LocTable();
        foreach (LocalizationSettings.Language language in settings.languages)
            if (language != null && !string.IsNullOrEmpty(language.code)) table.AddLanguage(language.code);
        File.WriteAllText(path, table.ToCsv(), new UTF8Encoding(true));
        AssetDatabase.ImportAsset(path);

        settings.table = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssetIfDirty(settings);
    }

    /// <summary>Scans the project into the translation table and saves it, without opening the window.</summary>
    [MenuItem("Tools/Localization/Scan Project Into Table", priority = 20)]
    public static void ScanAndSave()
    {
        LocalizationSettings settings = FindOrCreateSettings();
        if (!settings.table) CreateTable(settings);
        string path = AssetDatabase.GetAssetPath(settings.table);

        LocTable table = LocTable.Parse(File.ReadAllText(path));
        foreach (LocalizationSettings.Language language in settings.languages)
            if (language != null && !string.IsNullOrEmpty(language.code)) table.AddLanguage(language.code);
        int added = LocalizationWindow.Merge(table, LocalizationScanner.Scan(), out int unused);

        File.WriteAllText(path, table.ToCsv(), new UTF8Encoding(true));
        AssetDatabase.ImportAsset(path);
        Loc.Reload();
        h.Out($"Localization: {table.Rows.Count} lines in {path} - {added} new, {unused} unused.");
    }
}
