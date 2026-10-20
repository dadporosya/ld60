using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The player's language. All of the game's text is written in English (the source language) - in dialogue assets,
/// inspector fields and code - and passes through <see cref="T"/> / <see cref="F"/> where it goes on screen; the
/// translation table (<see cref="LocalizationSettings.table"/>: a CSV with a row per English line and a column per
/// language) gives it in <see cref="Current"/>. A line the table doesn't translate stays English.
///
/// <para>Static labels need nothing: once a scene has loaded, and on every language change, each TextMeshPro text
/// whose text is an English line of the table shows its translation (<see cref="RefreshSceneTexts"/>). Prefabs
/// spawned later can use <see cref="LocalizedText"/>.</para>
///
/// <para>The game starts in the settings' Default Language (or, with Remember Choice, the player's last pick);
/// <see cref="Set"/> changes it - the intro's language choice does. Outside Play mode <see cref="T"/> returns the
/// English text, so editor code never bakes a translation into a scene or prefab.</para>
/// </summary>
public static class Loc
{
    private const string PrefsKey = "Loc.Language";

    /// <summary>The language changed; scene labels are already swapped.</summary>
    public static event Action Changed;

    private struct Swept
    {
        public TMP_Text text;
        public string source;
        public string written;
    }

    private static bool loaded;
    private static LocalizationSettings settings;
    private static LocTable table = new LocTable();
    private static string current;

    // Trimmed English line -> its translation into the current language (only lines that have one).
    private static readonly Dictionary<string, string> exact = new Dictionary<string, string>(StringComparer.Ordinal);
    // The same, for text whose case differs from the table's ("Three Of A Kind" for "Three of a Kind").
    private static readonly Dictionary<string, string> loose = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    // Every English line of the table, translated or not: the labels the scene sweep takes over.
    private static readonly HashSet<string> sources = new HashSet<string>(StringComparer.Ordinal);
    // Scene labels the sweep has written, by instance id: what it wrote, and the English line it came from.
    private static readonly Dictionary<int, Swept> swept = new Dictionary<int, Swept>();

    /// <summary>The settings asset, or null if the project has none (then only English exists).</summary>
    public static LocalizationSettings Settings
    {
        get
        {
            EnsureLoaded();
            return settings;
        }
    }

    /// <summary>Code of the language the game shows (en, ru, es...).</summary>
    public static string Current
    {
        get
        {
            EnsureLoaded();
            return current;
        }
    }

    /// <summary>Code of the language the game is written in: the table's first column.</summary>
    public static string Source
    {
        get
        {
            EnsureLoaded();
            if (!string.IsNullOrEmpty(table.SourceLanguage)) return table.SourceLanguage;
            return settings && settings.languages.Count > 0 && settings.languages[0] != null
                ? settings.languages[0].code
                : "en";
        }
    }

    /// <summary>The languages the game can be played in (at least the source one), in the settings' order.</summary>
    public static IReadOnlyList<LocalizationSettings.Language> Languages
    {
        get
        {
            EnsureLoaded();
            if (settings && settings.languages.Count > 0) return settings.languages;
            return new[] { new LocalizationSettings.Language { code = Source, nativeName = Source } };
        }
    }

    /// <summary>How <paramref name="code"/>'s language calls itself; the code itself if the settings don't say.</summary>
    public static string NativeName(string code)
    {
        LocalizationSettings.Language language = Settings ? settings.Find(code) : null;
        return language != null && !string.IsNullOrEmpty(language.nativeName) ? language.nativeName : code;
    }

    public static bool IsAvailable(string code)
    {
        if (string.IsNullOrEmpty(code)) return false;
        if (Settings && settings.languages.Count > 0) return settings.IndexOf(code) >= 0;
        return string.Equals(code, Source, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------------------------------------------
    // Translating
    // -------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// <paramref name="text"/> in the current language: its translation from the table (spaces and line breaks
    /// around it kept), or the text itself when the table has none.
    /// </summary>
    public static string T(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
#if UNITY_EDITOR
        if (!Application.isPlaying) return text;
#endif
        EnsureLoaded();
        if (exact.Count == 0) return text;
        return Lookup(text, out string translation) ? translation : text;
    }

    /// <summary><paramref name="text"/> in <paramref name="language"/>, whichever language the game shows.</summary>
    public static string T(string text, string language)
    {
        if (string.IsNullOrEmpty(text)) return text;
        EnsureLoaded();
        int column = table.Column(language);
        if (column <= 0) return text;

        string key = Key(text, out int start, out int end);
        if (key.Length == 0) return text;
        foreach (LocTable.Row row in table.Rows)
        {
            if (!string.Equals(row.Source, key, StringComparison.Ordinal)) continue;
            string translation = row.Get(column);
            return translation.Length == 0 ? text : Wrap(text, translation, start, end);
        }
        return text;
    }

    /// <summary>
    /// <c>string.Format</c> with the translated <paramref name="format"/>: "Round {0}" -> "Раунд {0}" -> "Раунд 2".
    /// A translation that breaks the placeholders falls back to the English format.
    /// </summary>
    public static string F(string format, params object[] args)
    {
        if (string.IsNullOrEmpty(format)) return format;
        string translated = T(format);
        try
        {
            return string.Format(translated, args);
        }
        catch (FormatException)
        {
            if (ReferenceEquals(translated, format)) throw;
            h.Out($"Loc: the {current} translation of \"{format}\" has broken {{placeholders}}; showing English.");
            return string.Format(format, args);
        }
    }

    /// <summary>Whether <paramref name="text"/> is one of the table's English lines (translated or not).</summary>
    public static bool Has(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        EnsureLoaded();
        string key = Key(text, out _, out _);
        return key.Length > 0 && sources.Contains(key);
    }

    private static bool Lookup(string text, out string result)
    {
        result = null;
        string key = Key(text, out int start, out int end);
        if (key.Length == 0) return false;
        if (!exact.TryGetValue(key, out string translation) && !loose.TryGetValue(key, out translation)) return false;
        result = Wrap(text, translation, start, end);
        return true;
    }

    /// <summary>The table key of <paramref name="text"/>: trimmed (<paramref name="start"/>..<paramref name="end"/> of it), plain line breaks.</summary>
    private static string Key(string text, out int start, out int end)
    {
        start = 0;
        end = text.Length;
        while (start < end && char.IsWhiteSpace(text[start])) start++;
        while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
        if (start == end) return "";

        string key = start == 0 && end == text.Length ? text : text.Substring(start, end - start);
        return key.IndexOf('\r') >= 0 ? LocTable.Clean(key) : key;
    }

    /// <summary><paramref name="translation"/> with the spaces / line breaks the original had around its text.</summary>
    private static string Wrap(string text, string translation, int start, int end)
    {
        if (start == 0 && end == text.Length) return translation;
        return text.Substring(0, start) + translation + text.Substring(end);
    }

    // -------------------------------------------------------------------------------------------------------------
    // Changing the language
    // -------------------------------------------------------------------------------------------------------------

    /// <summary>Shows the game in <paramref name="code"/> from now on: swaps the scene's labels and raises <see cref="Changed"/>.</summary>
    public static void Set(string code)
    {
        EnsureLoaded();
        if (!IsAvailable(code))
        {
            h.Out($"Loc: there is no language '{code}' in the localization settings.");
            return;
        }

        code = Canonical(code);
        if (settings && settings.rememberChoice)
        {
            PlayerPrefs.SetString(PrefsKey, code);
            PlayerPrefs.Save();
        }
        if (code == current) return;

        current = code;
        Rebuild();
        RefreshSceneTexts();
        Changed?.Invoke();
    }

    /// <summary>Reads the settings and the table again (the Translations window saved them), keeping the language.</summary>
    public static void Reload()
    {
        string keep = loaded ? current : null;
        loaded = false;
        EnsureLoaded();
        if (keep != null && IsAvailable(keep) && Canonical(keep) != current)
        {
            current = Canonical(keep);
            Rebuild();
        }
        RefreshSceneTexts();
        Changed?.Invoke();
    }

    /// <summary>Forgets the language the player picked (Remember Choice): the next launch starts in the default one.</summary>
    public static void ForgetChoice()
    {
        PlayerPrefs.DeleteKey(PrefsKey);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Every TextMeshPro text in the loaded scenes (inactive ones too) whose text is an English line of the table
    /// shows it in the current language; ones this already swapped follow a language change. Text something else
    /// wrote since is left alone. Play mode only.
    /// </summary>
    public static void RefreshSceneTexts()
    {
        if (!Application.isPlaying) return;
        EnsureLoaded();

        TMP_Text[] texts = UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (TMP_Text text in texts) Refresh(text);

        // Destroyed labels.
        List<int> gone = null;
        foreach (KeyValuePair<int, Swept> entry in swept)
            if (!entry.Value.text) (gone ??= new List<int>()).Add(entry.Key);
        if (gone != null)
            foreach (int id in gone) swept.Remove(id);
    }

    private static void Refresh(TMP_Text text)
    {
        if (!text || text.TryGetComponent(out LocalizedText _)) return;

        int id = text.GetInstanceID();
        string now = text.text;
        string source;
        if (swept.TryGetValue(id, out Swept entry) && entry.written == now) source = entry.source;
        else if (Has(now)) source = now;
        else
        {
            swept.Remove(id);
            return;
        }

        string shown = T(source);
        if (shown != now) text.text = shown;
        swept[id] = new Swept { text = text, source = source, written = shown };
    }

    // -------------------------------------------------------------------------------------------------------------
    // Loading
    // -------------------------------------------------------------------------------------------------------------

    // Domain reload is off in this project: statics survive between play sessions unless reset here.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Changed = null;
        loaded = false;
        swept.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        EnsureLoaded();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // Also when entering Play mode without a scene reload (no sceneLoaded then).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AfterFirstScene() => RefreshSceneTexts();

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => RefreshSceneTexts();

    private static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;

        settings = Resources.Load<LocalizationSettings>(LocalizationSettings.ResourcePath);
        if (!settings) h.Out("Loc: no LocalizationSettings in a Resources folder - the game stays in its own language.");
        table = LocTable.Parse(settings && settings.table ? settings.table.text : null);
        current = StartLanguage();
        Rebuild();
    }

    private static string StartLanguage()
    {
        if (settings && settings.rememberChoice)
        {
            string saved = PlayerPrefs.GetString(PrefsKey, "");
            if (IsAvailable(saved)) return Canonical(saved);
        }
        if (settings && IsAvailable(settings.defaultLanguage)) return Canonical(settings.defaultLanguage);
        return Languages[0].code;
    }

    /// <summary>The settings' spelling of <paramref name="code"/> ("RU" -> "ru").</summary>
    private static string Canonical(string code)
    {
        LocalizationSettings.Language language = settings ? settings.Find(code) : null;
        return language != null ? language.code : code;
    }

    private static void Rebuild()
    {
        exact.Clear();
        loose.Clear();
        sources.Clear();

        int column = table.Column(current);
        foreach (LocTable.Row row in table.Rows)
        {
            string source = row.Source;
            if (source.Length == 0) continue;
            sources.Add(source);

            if (column <= 0) continue;
            string translation = row.Get(column);
            if (translation.Length == 0 || exact.ContainsKey(source)) continue;
            exact[source] = translation;
            if (!loose.ContainsKey(source)) loose[source] = translation;
        }
    }
}
