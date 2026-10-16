// using UnityEngine;
// using UnityEditor;
// using System.Collections.Generic;

// public class UnitPrefabGeneratorWindow : EditorWindow
// {
//     GameObject unitBase;
//     List<CMSEntityPfb> entitiesData;

//     [MenuItem("Tools/Unit Prefab Generator")]
//     public static void ShowWindow()
//     {
//         GetWindow<UnitPrefabGeneratorWindow>("Unit Prefab Generator");
//     }

//     void OnGUI()
//     {
//         GUILayout.Label("Prefab Generator", EditorStyles.boldLabel);

//         unitBase = (GameObject)EditorGUILayout.ObjectField(
//             "Unit Base Prefab",
//             unitBase,
//             typeof(GameObject),
//             false
//         );

//         SerializedObject so = new SerializedObject(this);
//         SerializedProperty listProp = so.FindProperty("entitiesData");

//         EditorGUILayout.PropertyField(listProp, true);
//         so.ApplyModifiedProperties();

//         GUILayout.Space(10);

//         if (GUILayout.Button("Generate Prefabs"))
//         {
//             GeneratePrefabs();
//         }
//     }

//     void GeneratePrefabs()
//     {
//         if (unitBase == null || entitiesData == null)
//         {
//             Debug.LogError("Missing references");
//             return;
//         }

//         foreach (var cms in entitiesData)
//         {
//             UnitPrefabCreator.CreatePrefab(cms, unitBase);
//         }
//     }
// }