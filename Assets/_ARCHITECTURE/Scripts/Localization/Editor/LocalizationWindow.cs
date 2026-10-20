using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Localization > Translations: every line of the translation table side by side in all languages, to read
/// and edit. Scan pulls the game's current English text into the table (new lines appear untranslated, lines no
/// longer used are flagged); Save writes the CSV. The toolbar also picks the Default Language - the one the game
/// starts in when the intro's language choice doesn't run - and, in Play mode, switches the live language.
/// </summary>
public class LocalizationWindow : EditorWindow
{
    private const string UnusedContext = "UNUSED";
    private const string ManualContext = "manual";

    private enum Filter { All, Untranslated, Unused }

    private LocalizationSettings settings;
    private LocTable table;
    private string tablePath;
    private bool dirty;

    private string search = "";
    private Filter filter;
    private bool[] shownLanguages = new bool[0];
    private string newLine = "";
    private Vector2 scroll;

    // Rows on screen after the search / filter, with their heights (laid out for layoutWidth).
    private readonly List<int> visibleRows = new List<int>();
    private readonly List<float> rowHeights = new List<float>();
    private float layoutWidth = -1f;
    private bool layoutDirty = true;
    private Rect tableArea;

    private GUIStyle cellStyle, sourceStyle, contextStyle;

    [MenuItem("Tools/Localization/Translations", priority = 0)]
    public static void Open()
    {
        LocalizationWindow window = GetWindow<LocalizationWindow>();
        window.titleContent = new GUIContent("Translations");
        window.minSize = new Vector2(640f, 320f);
        window.Show();
    }

    [MenuItem("Tools/Localization/Settings", priority = 1)]
    public static void SelectSettings()
    {
        LocalizationSettings asset = LocalizationSettingsMenu.FindOrCreateSettings();
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }

    private void OnEnable() => Load();

    private void OnFocus()
    {
        // The CSV may have been edited in a spreadsheet app meanwhile.
        if (!dirty && table != null && tablePath != null && File.Exists(tablePath) &&
            File.GetLastWriteTimeUtc(tablePath) > loadedAt) Load();
    }

    private DateTime loadedAt;

    private void Load()
    {
        settings = LocalizationSettingsMenu.FindSettings();
        tablePath = settings && settings.table ? AssetDatabase.GetAssetPath(settings.table) : null;
        table = tablePath != null && File.Exists(tablePath) ? LocTable.Parse(File.ReadAllText(tablePath)) : new LocTable();
        loadedAt = DateTime.UtcNow;
        if (settings) MatchLanguages();
        dirty = false;
        layoutDirty = true;
        Repaint();
    }

    /// <summary>Gives the table a column for every language of the settings, source language first.</summary>
    private void MatchLanguages()
    {
        foreach (LocalizationSettings.Language language in settings.languages)
            if (language != null && !string.IsNullOrEmpty(language.code)) table.AddLanguage(language.code);
        if (shownLanguages.Length != table.Languages.Count)
        {
            bool[] shown = new bool[table.Languages.Count];
            for (int i = 0; i < shown.Length; i++) shown[i] = i >= shownLanguages.Length || shownLanguages[i];
            shownLanguages = shown;
        }
    }

    // -------------------------------------------------------------------------------------------------------------

    private void OnGUI()
    {
        MakeStyles();

        if (!settings)
        {
            EditorGUILayout.HelpBox("No LocalizationSettings asset in a Resources folder.", MessageType.Warning);
            if (GUILayout.Button("Create Localization Settings"))
            {
                LocalizationSettingsMenu.FindOrCreateSettings();
                Load();
            }
            return;
        }

        DrawToolbar();
        DrawFilters();

        if (settings.table == null)
        {
            EditorGUILayout.HelpBox("The settings have no translation table (CSV).", MessageType.Warning);
            if (GUILayout.Button("Create translation table"))
            {
                LocalizationSettingsMenu.CreateTable(settings);
                Load();
            }
            return;
        }

        DrawTable();
        DrawFooter();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button(new GUIContent("Scan", "Collect the English text of the dialogues, [Localized] fields, " +
                                                          "the open scenes, prefabs and Loc.T / Loc.F calls in code."),
                    EditorStyles.toolbarButton, GUILayout.Width(50f))) Scan();

            using (new EditorGUI.DisabledScope(!dirty))
            {
                if (GUILayout.Button(dirty ? "Save*" : "Save", EditorStyles.toolbarButton, GUILayout.Width(50f))) Save();
                if (GUILayout.Button("Revert", EditorStyles.toolbarButton, GUILayout.Width(52f))) Load();
            }

            if (GUILayout.Button(new GUIContent("Open CSV", "Open the table in your spreadsheet app. Save it there as " +
                                                              "CSV UTF-8; this window reloads it when it gets focus."),
                    EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                if (dirty) Save();
                EditorUtility.OpenWithDefaultApp(tablePath);
            }
            if (GUILayout.Button("Settings", EditorStyles.toolbarButton, GUILayout.Width(60f))) SelectSettings();

            GUILayout.FlexibleSpace();

            GUILayout.Label(new GUIContent("Default language", "What the game starts in - and what it shows when the " +
                                                                "intro (with its language choice) doesn't run."),
                EditorStyles.miniLabel);
            string chosen = LanguagePopup(settings.defaultLanguage, 110f);
            if (chosen != settings.defaultLanguage) SetDefaultLanguage(settings, chosen);

            if (Application.isPlaying)
            {
                GUILayout.Space(8f);
                GUILayout.Label(new GUIContent("Live", "Switch the running game's language."), EditorStyles.miniLabel);
                string live = LanguagePopup(Loc.Current, 110f);
                if (live != Loc.Current) Loc.Set(live);
            }
        }
    }

    private string LanguagePopup(string code, float width)
    {
        List<LocalizationSettings.Language> languages = settings.languages;
        string[] names = new string[languages.Count];
        int index = -1;
        for (int i = 0; i < languages.Count; i++)
        {
            names[i] = $"{languages[i].nativeName} ({languages[i].code})";
            if (string.Equals(languages[i].code, code, StringComparison.OrdinalIgnoreCase)) index = i;
        }
        int picked = EditorGUILayout.Popup(index, names, EditorStyles.toolbarPopup, GUILayout.Width(width));
        return picked >= 0 && picked < languages.Count ? languages[picked].code : code;
    }

    public static void SetDefaultLanguage(LocalizationSettings settings, string code)
    {
        Undo.RecordObject(settings, "Default Language");
        settings.defaultLanguage = code;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssetIfDirty(settings);
        if (!Application.isPlaying) Loc.Reload();
    }

    private void DrawFilters()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            EditorGUI.BeginChangeCheck();
            search = GUILayout.TextField(search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120f));
            filter = (Filter)EditorGUILayout.EnumPopup(filter, EditorStyles.toolbarPopup, GUILayout.Width(100f));
            GUILayout.Space(8f);
            for (int i = 1; i < table.Languages.Count; i++)
                shownLanguages[i] = GUILayout.Toggle(shownLanguages[i], table.Languages[i], EditorStyles.toolbarButton,
                    GUILayout.Width(36f));
            if (EditorGUI.EndChangeCheck()) layoutDirty = true;

            GUILayout.FlexibleSpace();
            GUILayout.Label(Stats(), EditorStyles.miniLabel);
        }
    }

    private string Stats()
    {
        StringBuilder text = new StringBuilder();
        text.Append(table.Rows.Count).Append(" lines");
        for (int c = 1; c < table.Languages.Count; c++)
        {
            int done = 0;
            foreach (LocTable.Row row in table.Rows)
                if (row.Get(c).Length > 0) done++;
            text.Append("   ").Append(table.Languages[c]).Append(' ').Append(done).Append('/').Append(table.Rows.Count);
        }
        int unused = 0;
        foreach (LocTable.Row row in table.Rows)
            if (IsUnused(row)) unused++;
        if (unused > 0) text.Append("   unused ").Append(unused);
        return text.ToString();
    }

    // -------------------------------------------------------------------------------------------------------------
    // Table
    // -------------------------------------------------------------------------------------------------------------

    private List<int> Columns()
    {
        List<int> columns = new List<int> { 0 };
        for (int i = 1; i < table.Languages.Count; i++)
            if (shownLanguages[i]) columns.Add(i);
        return columns;
    }

    private void DrawTable()
    {
        List<int> columns = Columns();

        // Header.
        Rect header = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
        float columnWidth = ColumnWidth(header.width, columns.Count);
        for (int i = 0; i < columns.Count; i++)
        {
            Rect cell = new Rect(header.x + 4f + i * columnWidth, header.y, columnWidth - 6f, header.height);
            string code = table.Languages[columns[i]];
            string name = Loc.NativeName(code);
            GUI.Label(cell, i == 0 ? $"{name} ({code}) - source, edit it in the game's assets" : $"{name} ({code})",
                EditorStyles.boldLabel);
        }

        Rect area = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        if (Event.current.type == EventType.Repaint) tableArea = area;
        area = tableArea;
        if (area.width <= 1f) return;

        float width = area.width - 16f;
        if (layoutDirty || !Mathf.Approximately(width, layoutWidth)) Layout(width, columns);

        float total = 0f;
        foreach (float height in rowHeights) total += height;

        scroll = GUI.BeginScrollView(area, scroll, new Rect(0f, 0f, width, total));
        columnWidth = ColumnWidth(width, columns.Count);
        float y = 0f;
        for (int v = 0; v < visibleRows.Count; v++)
        {
            float height = rowHeights[v];
            if (y + height >= scroll.y && y <= scroll.y + area.height)
                DrawRow(new Rect(0f, y, width, height), visibleRows[v], columns, columnWidth);
            y += height;
        }
        GUI.EndScrollView();
    }

    private static float ColumnWidth(float width, int columns) => (width - 26f) / Mathf.Max(1, columns);

    private void Layout(float width, List<int> columns)
    {
        layoutWidth = width;
        layoutDirty = false;
        visibleRows.Clear();
        rowHeights.Clear();

        float columnWidth = ColumnWidth(width, columns.Count) - 6f;
        for (int r = 0; r < table.Rows.Count; r++)
        {
            LocTable.Row row = table.Rows[r];
            if (!Matches(row, columns)) continue;

            float height = sourceStyle.CalcHeight(new GUIContent(row.Source), columnWidth);
            for (int i = 1; i < columns.Count; i++)
                height = Mathf.Max(height, cellStyle.CalcHeight(new GUIContent(row.Get(columns[i]) + " "), columnWidth));
            if (row.Context.Length > 0) height += contextStyle.CalcHeight(new GUIContent(row.Context), columnWidth);

            visibleRows.Add(r);
            rowHeights.Add(height + 8f);
        }
    }

    private bool Matches(LocTable.Row row, List<int> columns)
    {
        switch (filter)
        {
            case Filter.Unused when !IsUnused(row):
                return false;
            case Filter.Untranslated:
            {
                bool missing = false;
                for (int i = 1; i < columns.Count; i++) missing |= row.Get(columns[i]).Length == 0;
                if (!missing) return false;
                break;
            }
        }

        if (string.IsNullOrEmpty(search)) return true;
        for (int c = 0; c < table.Languages.Count; c++)
            if (row.Get(c).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return row.Context.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void DrawRow(Rect rect, int index, List<int> columns, float columnWidth)
    {
        LocTable.Row row = table.Rows[index];
        bool unused = IsUnused(row);

        if (index % 2 == 1) EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.06f));
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(0f, 0f, 0f, 0.2f));

        float x = rect.x + 4f;
        float cellWidth = columnWidth - 6f;

        // Source line, and where it's used.
        float sourceHeight = sourceStyle.CalcHeight(new GUIContent(row.Source), cellWidth);
        Rect sourceRect = new Rect(x, rect.y + 4f, cellWidth, sourceHeight);
        if (unused) EditorGUI.DrawRect(sourceRect, new Color(1f, 0.6f, 0f, 0.15f));
        EditorGUI.SelectableLabel(sourceRect, row.Source, sourceStyle);
        if (row.Context.Length > 0)
            GUI.Label(new Rect(x, sourceRect.yMax, cellWidth, rect.yMax - sourceRect.yMax - 4f), row.Context, contextStyle);

        // Translations.
        for (int i = 1; i < columns.Count; i++)
        {
            int column = columns[i];
            Rect cell = new Rect(x + i * columnWidth, rect.y + 4f, cellWidth, rect.height - 8f);
            string text = row.Get(column);
            if (text.Length == 0) EditorGUI.DrawRect(cell, new Color(1f, 0.2f, 0.2f, 0.18f));

            EditorGUI.BeginChangeCheck();
            string edited = EditorGUI.TextArea(cell, text, cellStyle);
            if (EditorGUI.EndChangeCheck())
            {
                row.Set(column, edited);
                dirty = true;
                layoutDirty = true;
            }
        }

        // Remove (unused lines only: used ones would come back with the next scan).
        Rect remove = new Rect(rect.xMax - 22f, rect.y + 4f, 20f, 18f);
        if (unused && GUI.Button(remove, new GUIContent("x", "Remove this unused line"), EditorStyles.miniButton))
        {
            table.Rows.RemoveAt(index);
            dirty = true;
            layoutDirty = true;
            GUIUtility.ExitGUI();
        }
    }

    private void DrawFooter()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label(new GUIContent("Add line", "An English line the scan can't find (text built at runtime). " +
                                                        "It's kept even when no scan finds it."),
                EditorStyles.miniLabel, GUILayout.Width(52f));
            newLine = GUILayout.TextField(newLine, EditorStyles.toolbarTextField, GUILayout.MinWidth(200f));
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(newLine)))
                if (GUILayout.Button("Add", EditorStyles.toolbarButton, GUILayout.Width(40f))) AddLine();

            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Remove unused", "Delete every line the last scan didn't find."),
                    EditorStyles.toolbarButton, GUILayout.Width(100f))) RemoveUnused();
        }
    }

    private void AddLine()
    {
        string source = LocTable.Clean(newLine);
        if (Find(source) == null)
        {
            LocTable.Row row = new LocTable.Row(table.Languages.Count) { Context = ManualContext };
            row.Set(0, source);
            table.Rows.Add(row);
            dirty = true;
        }
        search = source;
        newLine = "";
        layoutDirty = true;
        GUI.FocusControl(null);
    }

    private void RemoveUnused()
    {
        int count = table.Rows.FindAll(IsUnused).Count;
        if (count == 0 || !EditorUtility.DisplayDialog("Remove unused lines",
                $"Delete the {count} lines the last scan didn't find, with their translations?", "Delete", "Cancel"))
            return;
        table.Rows.RemoveAll(IsUnused);
        dirty = true;
        layoutDirty = true;
    }

    private static bool IsUnused(LocTable.Row row) => row.Context.StartsWith(UnusedContext, StringComparison.Ordinal);

    private LocTable.Row Find(string source)
    {
        foreach (LocTable.Row row in table.Rows)
            if (string.Equals(row.Source, source, StringComparison.Ordinal)) return row;
        return null;
    }

    // -------------------------------------------------------------------------------------------------------------
    // Scan / save
    // -------------------------------------------------------------------------------------------------------------

    private void Scan()
    {
        int added = Merge(table, LocalizationScanner.Scan(), out int unused);
        dirty = true;
        layoutDirty = true;
        ShowNotification(new GUIContent($"{added} new lines, {unused} unused"));
    }

    /// <summary>
    /// Puts <paramref name="scan"/>'s lines into <paramref name="table"/> in the order found (translations kept),
    /// lines the table has that the scan didn't find at the end, flagged unused. Returns how many lines are new.
    /// </summary>
    public static int Merge(LocTable table, LocalizationScanner.Result scan, out int unused)
    {
        Dictionary<string, LocTable.Row> existing = new Dictionary<string, LocTable.Row>(StringComparer.Ordinal);
        foreach (LocTable.Row row in table.Rows)
            if (!existing.ContainsKey(row.Source)) existing[row.Source] = row;

        List<LocTable.Row> rows = new List<LocTable.Row>();
        HashSet<LocTable.Row> used = new HashSet<LocTable.Row>();
        int added = 0;

        foreach (LocalizationScanner.Line line in scan.Lines)
        {
            if (!existing.TryGetValue(line.Source, out LocTable.Row row))
            {
                row = new LocTable.Row(table.Languages.Count);
                row.Set(0, line.Source);
                added++;
            }
            row.Context = Context(line.Places);
            rows.Add(row);
            used.Add(row);
        }

        // Scene / prefab labels only keep lines that are already in the table.
        foreach (LocTable.Row row in table.Rows)
        {
            if (used.Contains(row) || !scan.Labels.TryGetValue(row.Source, out LocalizationScanner.Line label)) continue;
            row.Context = Context(label.Places);
            rows.Add(row);
            used.Add(row);
        }

        unused = 0;
        foreach (LocTable.Row row in table.Rows)
        {
            if (used.Contains(row)) continue;
            if (!row.Context.StartsWith(ManualContext, StringComparison.Ordinal))
            {
                row.Context = UnusedContext + " - not found in the project";
                unused++;
            }
            rows.Add(row);
        }

        table.Rows.Clear();
        table.Rows.AddRange(rows);
        return added;
    }

    private static string Context(List<string> places)
    {
        const int Shown = 3;
        string text = string.Join("; ", places.GetRange(0, Mathf.Min(Shown, places.Count)));
        return places.Count > Shown ? $"{text} (+{places.Count - Shown} more)" : text;
    }

    private void Save()
    {
        if (string.IsNullOrEmpty(tablePath)) return;
        // With a BOM, so Excel reads it as UTF-8.
        File.WriteAllText(tablePath, table.ToCsv(), new UTF8Encoding(true));
        AssetDatabase.ImportAsset(tablePath);
        loadedAt = DateTime.UtcNow;
        dirty = false;
        Loc.Reload();
    }

    private void OnDestroy()
    {
        if (dirty && EditorUtility.DisplayDialog("Translations", "Save the changes to the translation table?", "Save", "Discard"))
            Save();
    }

    private void MakeStyles()
    {
        if (cellStyle != null) return;
        cellStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
        sourceStyle = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = false };
        contextStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
        contextStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
    }
}
