#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ObjectsCountContainer))]
public class ObjectsCountContainerEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var container = (ObjectsCountContainer)target;
        bool initFromKinds = serializedObject.FindProperty("initFromKindsContainer").boolValue;
        bool initFromDefault = serializedObject.FindProperty("initFromDefaultValues").boolValue;

        if (initFromKinds && container.data != null)
        {
            EditorGUILayout.Space();

            if (GUILayout.Button("Generate Keys"))
            {
                GenerateKeysFromObjects(container);
            }
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Generate Keys For All"))
        {
            GenerateKeysFromObjectsForAll();
        }

        if (initFromDefault && container.defaultData != null)
        {
            EditorGUILayout.Space();

            if (GUILayout.Button("Generate Default Keys"))
            {
                GenerateDefaultKeys(container);
            }
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Generate Default Keys For All Containers"))
        {
            GenerateDefaultKeysForAll();
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Reset All Values"))
        {
            Undo.RecordObject(container, "Reset All Values");
            container.rawKeys.Clear();
            container.rawValues.Clear();
            EditorUtility.SetDirty(container);
        }
    }

    static void GenerateKeysFromObjects(ObjectsCountContainer container)
    {
        Undo.RecordObject(container, "Generate Keys");

        container.rawKeys.Clear();
        foreach (var go in container.data.rawObject)
        {
            if (go) container.rawKeys.Add(go.name);
        }

        EditorUtility.SetDirty(container);
    }

    static void GenerateKeysFromObjectsForAll()
    {
        string[] guids = AssetDatabase.FindAssets("t:ObjectsCountContainer");
        int count = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<ObjectsCountContainer>(path);
            if (asset == null) continue;

            SerializedObject so = new SerializedObject(asset);
            bool initFromKinds = so.FindProperty("initFromKindsContainer").boolValue;
            if (!initFromKinds || asset.data == null) continue;

            GenerateKeysFromObjects(asset);
            count++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"Generated keys for {count} ObjectsCountContainer(s).");
    }

    static void GenerateDefaultKeys(ObjectsCountContainer container)
    {
        Undo.RecordObject(container, "Generate Default Keys");

        foreach (var kv in container.defaultData.values)
        {
            if (!container.rawKeys.Contains(kv.Key))
            {
                container.rawKeys.Add(kv.Key);
                container.rawValues.Add(kv.Value);
            }
        }

        EditorUtility.SetDirty(container);
    }

    static void GenerateDefaultKeysForAll()
    {
        string[] guids = AssetDatabase.FindAssets("t:ObjectsCountContainer");
        int count = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<ObjectsCountContainer>(path);

            if (asset == null) continue;

            SerializedObject so = new SerializedObject(asset);
            bool initFromDefault = so.FindProperty("initFromDefaultValues").boolValue;

            if (!initFromDefault || asset.defaultData == null) continue;

            GenerateDefaultKeys(asset);
            count++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"Generated default keys for {count} ObjectsCountContainer(s).");
    }
}
#endif
