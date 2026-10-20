using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Finds the English text the game shows, for the translation table: [Localized] fields of ScriptableObjects,
/// prefabs and the open scenes (walking into their [Serializable] classes and lists), <see cref="LocalizedText"/>
/// lines, and string literals passed to <c>Loc.T</c> / <c>Loc.F</c> in code. TextMeshPro labels are collected
/// on the side: they only count as used when the table already has their line (most labels are placeholders that
/// code overwrites).
/// </summary>
public static class LocalizationScanner
{
    public class Line
    {
        public string Source;
        public readonly List<string> Places = new List<string>();
    }

    public class Result
    {
        /// <summary>The lines found, in the order found (asset path order; a dialogue's nodes in order).</summary>
        public readonly List<Line> Lines = new List<Line>();
        /// <summary>Texts of TextMeshPro labels, with where they sit.</summary>
        public readonly Dictionary<string, Line> Labels = new Dictionary<string, Line>(StringComparer.Ordinal);
        private readonly Dictionary<string, Line> bySource = new Dictionary<string, Line>(StringComparer.Ordinal);

        public void Add(string text, string place)
        {
            string source = LocTable.Clean(text);
            if (!IsText(source)) return;
            if (!bySource.TryGetValue(source, out Line line))
            {
                line = new Line { Source = source };
                bySource[source] = line;
                Lines.Add(line);
            }
            if (!line.Places.Contains(place)) line.Places.Add(place);
        }

        public void AddLabel(string text, string place)
        {
            string source = LocTable.Clean(text);
            if (!IsText(source)) return;
            if (!Labels.TryGetValue(source, out Line line)) Labels[source] = line = new Line { Source = source };
            if (!line.Places.Contains(place)) line.Places.Add(place);
        }

        public bool Contains(string source) => bySource.ContainsKey(source);
    }

    /// <summary>Text worth translating: has a letter outside its rich text tags and {placeholders}.</summary>
    public static bool IsText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        string bare = Regex.Replace(text, @"<[^>]*>|\{[^}]*\}", "");
        foreach (char c in bare)
            if (char.IsLetter(c)) return true;
        return false;
    }

    public static Result Scan()
    {
        Result result = new Result();
        try
        {
            EditorUtility.DisplayProgressBar("Localization", "Scanning assets...", 0.1f);
            ScanScriptableObjects(result);
            EditorUtility.DisplayProgressBar("Localization", "Scanning open scenes...", 0.4f);
            ScanOpenScenes(result);
            EditorUtility.DisplayProgressBar("Localization", "Scanning prefabs...", 0.6f);
            ScanPrefabs(result);
            EditorUtility.DisplayProgressBar("Localization", "Scanning code...", 0.9f);
            ScanCode(result);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        return result;
    }

    // -------------------------------------------------------------------------------------------------------------
    // Assets, scenes, prefabs
    // -------------------------------------------------------------------------------------------------------------

    private static void ScanScriptableObjects(Result result)
    {
        List<string> paths = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets" }))
            paths.Add(AssetDatabase.GUIDToAssetPath(guid));
        paths.Sort(StringComparer.OrdinalIgnoreCase);

        foreach (string path in paths)
        {
            // Only load what can hold text: the type is known without loading the asset.
            Type type = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (type == null || !MayHoldText(type)) continue;
            ScriptableObject asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (asset) Walk(asset, "", Short(path), result, 0);
        }
    }

    private static void ScanOpenScenes(Result result)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            foreach (GameObject root in scene.GetRootGameObjects()) ScanGameObject(root, scene.name, result);
        }
    }

    private static void ScanPrefabs(Result result)
    {
        List<string> paths = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            paths.Add(AssetDatabase.GUIDToAssetPath(guid));
        paths.Sort(StringComparer.OrdinalIgnoreCase);

        foreach (string path in paths)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab) ScanGameObject(prefab, Short(path), result);
        }
    }

    private static void ScanGameObject(GameObject root, string owner, Result result)
    {
        foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (!behaviour) continue;
            string place = owner + ": " + PathOf(behaviour.transform);

            if (behaviour is LocalizedText localized) result.Add(localized.Source, place);
            else if (behaviour is TMP_Text label) result.AddLabel(label.text, place);
            else if (MayHoldText(behaviour.GetType()))
                Walk(behaviour, "", place + " (" + behaviour.GetType().Name + ")", result, 0);
        }
    }

    // -------------------------------------------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------------------------------------------

    private static readonly Dictionary<Type, FieldInfo[]> fieldCache = new Dictionary<Type, FieldInfo[]>();
    private static readonly Dictionary<Type, bool> holdsCache = new Dictionary<Type, bool>();

    private static void Walk(object target, string path, string place, Result result, int depth)
    {
        if (target == null || depth > 12) return;

        foreach (FieldInfo field in SerializedFields(target.GetType()))
        {
            bool localized = field.IsDefined(typeof(LocalizedAttribute), true);
            Type type = field.FieldType;
            string fieldPath = path.Length == 0 ? field.Name : path + "." + field.Name;
            object value = field.GetValue(target);
            if (value == null) continue;

            if (type == typeof(string))
            {
                if (localized) result.Add((string)value, place + " " + fieldPath);
                continue;
            }

            if (value is IList list)
            {
                Type element = ElementType(type);
                if (element == typeof(string))
                {
                    if (!localized) continue;
                    for (int i = 0; i < list.Count; i++) result.Add(list[i] as string, $"{place} {fieldPath}[{i}]");
                }
                else if (element != null && MayHoldText(element))
                {
                    for (int i = 0; i < list.Count; i++) Walk(list[i], $"{fieldPath}[{i}]", place, result, depth + 1);
                }
                continue;
            }

            if (MayHoldText(type)) Walk(value, fieldPath, place, result, depth + 1);
        }
    }

    /// <summary>Whether <paramref name="type"/> has a [Localized] field, itself or in what it serializes.</summary>
    private static bool MayHoldText(Type type)
    {
        if (type == null) return false;
        if (holdsCache.TryGetValue(type, out bool holds)) return holds;

        holdsCache[type] = false; // a type that contains itself
        holds = false;
        if (IsWalkable(type))
            foreach (FieldInfo field in SerializedFields(type))
            {
                if (field.IsDefined(typeof(LocalizedAttribute), true)) holds = true;
                else
                {
                    Type fieldType = ElementType(field.FieldType) ?? field.FieldType;
                    holds = fieldType != typeof(string) && MayHoldText(fieldType);
                }
                if (holds) break;
            }

        holdsCache[type] = holds;
        return holds;
    }

    /// <summary>Types whose fields Unity serializes: components, ScriptableObjects, and plain [Serializable] classes / structs of the project.</summary>
    private static bool IsWalkable(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type.IsArray || type.IsGenericType) return false;
        if (typeof(MonoBehaviour).IsAssignableFrom(type) || typeof(ScriptableObject).IsAssignableFrom(type)) return true;
        if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return false;
        string space = type.Namespace ?? "";
        if (space.StartsWith("System") || space.StartsWith("UnityEngine") || space.StartsWith("Unity.") ||
            space.StartsWith("TMPro")) return false;
        return type.IsDefined(typeof(SerializableAttribute), false);
    }

    private static Type ElementType(Type type)
    {
        if (type.IsArray) return type.GetElementType();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return type.GetGenericArguments()[0];
        return null;
    }

    private static FieldInfo[] SerializedFields(Type type)
    {
        if (fieldCache.TryGetValue(type, out FieldInfo[] cached)) return cached;

        List<FieldInfo> fields = new List<FieldInfo>();
        for (Type t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(ScriptableObject) && t != typeof(object); t = t.BaseType)
            foreach (FieldInfo field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.IsInitOnly || field.IsLiteral || field.IsDefined(typeof(NonSerializedAttribute), false)) continue;
                if (!field.IsPublic && !field.IsDefined(typeof(SerializeField), false) &&
                    !field.IsDefined(typeof(SerializeReference), false)) continue;
                fields.Add(field);
            }

        cached = fields.ToArray();
        fieldCache[type] = cached;
        return cached;
    }

    // -------------------------------------------------------------------------------------------------------------
    // Code
    // -------------------------------------------------------------------------------------------------------------

    // Loc.T("...") / Loc.F("...", ...) with a plain or verbatim literal as the first argument.
    private static readonly Regex CodeLiteral = new Regex(
        @"\bLoc\s*\.\s*[TF]\s*\(\s*(?:@""(?<verbatim>(?:[^""]|"""")*)""|""(?<plain>(?:[^""\\\r\n]|\\.)*)"")");

    private static void ScanCode(Result result)
    {
        string[] files = Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        foreach (string file in files)
        {
            string[] lines = File.ReadAllLines(file);
            string place = Short("Assets" + file.Substring(Application.dataPath.Length).Replace('\\', '/'));
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("*")) continue;
                foreach (Match match in CodeLiteral.Matches(lines[i]))
                {
                    string text = match.Groups["verbatim"].Success
                        ? match.Groups["verbatim"].Value.Replace("\"\"", "\"")
                        : Unescape(match.Groups["plain"].Value);
                    result.Add(text, $"{place}.cs:{i + 1}");
                }
            }
        }
    }

    private static string Unescape(string literal)
    {
        if (literal.IndexOf('\\') < 0) return literal;
        StringBuilder text = new StringBuilder(literal.Length);
        for (int i = 0; i < literal.Length; i++)
        {
            char c = literal[i];
            if (c != '\\' || i + 1 >= literal.Length)
            {
                text.Append(c);
                continue;
            }
            char next = literal[++i];
            switch (next)
            {
                case 'n': text.Append('\n'); break;
                case 't': text.Append('\t'); break;
                case 'r': text.Append('\r'); break;
                case '0': text.Append('\0'); break;
                default: text.Append(next); break;
            }
        }
        return text.ToString();
    }

    // -------------------------------------------------------------------------------------------------------------

    /// <summary>An asset path without "Assets/", the project folder and the extension.</summary>
    private static string Short(string path)
    {
        if (path.StartsWith("Assets/")) path = path.Substring("Assets/".Length);
        foreach (string folder in new[] { "_PROJECT/", "_ARCHITECTURE/" })
            if (path.StartsWith(folder)) path = path.Substring(folder.Length);
        string extension = Path.GetExtension(path);
        return extension.Length > 0 ? path.Substring(0, path.Length - extension.Length) : path;
    }

    private static string PathOf(Transform transform)
    {
        string path = transform.name;
        for (Transform t = transform.parent; t; t = t.parent) path = t.name + "/" + path;
        return path;
    }
}
