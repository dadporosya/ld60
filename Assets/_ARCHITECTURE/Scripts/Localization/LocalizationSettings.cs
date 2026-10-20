using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The game's languages, the one it starts in, and the translation table. Loaded by <see cref="Loc"/> from a
/// Resources folder (<see cref="ResourcePath"/>); Tools > Localization > Settings selects it.
/// </summary>
[CreateAssetMenu(fileName = "LocalizationSettings", menuName = "Localization/Settings")]
public class LocalizationSettings : ScriptableObject
{
    /// <summary>Where <see cref="Loc"/> loads it from: any Resources folder, under this name.</summary>
    public const string ResourcePath = "LocalizationSettings";

    [Serializable]
    public class Language
    {
        [Tooltip("The language's column in the translation table: en, ru, es...")]
        public string code = "en";
        [Tooltip("How the language calls itself (English, Русский, Español): shown in the intro's language choice.")]
        public string nativeName = "English";
    }

    [Tooltip("The languages the game can be played in, in the order the intro offers them. The first one is the " +
             "language the game is written in: its text is the translation table's key column.")]
    public List<Language> languages = new List<Language> { new Language() };

    [Tooltip("The language the game starts in: what is shown when the intro (and its language choice) doesn't run.")]
    public string defaultLanguage = "en";

    [Tooltip("Start in the language the player picked last time (saved in PlayerPrefs) instead of the Default " +
             "Language. Off: every launch starts in the Default Language until the intro's choice changes it.")]
    public bool rememberChoice = false;

    [Tooltip("The translations: a CSV with one column per language code (the first holds the English text) and a " +
             "'context' column. Edit it in Tools > Localization > Translations, or in any spreadsheet app.")]
    public TextAsset table;

    public Language Find(string code)
    {
        int index = IndexOf(code);
        return index >= 0 ? languages[index] : null;
    }

    public int IndexOf(string code)
    {
        if (string.IsNullOrEmpty(code) || languages == null) return -1;
        for (int i = 0; i < languages.Count; i++)
            if (languages[i] != null && string.Equals(languages[i].code, code, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }
}
