// Commented out in full: this is the custom inspector for Surgut.SpaceDebris, a component from the
// previous project that never came over. Uncomment if SpaceDebris is ever brought into this project.

// #if UNITY_EDITOR
// using Surgut;
// using UnityEditor;
// using UnityEngine;
//
// /// <summary>
// /// Adds the re-scatter buttons under <see cref="SpaceDebris"/>'s fields. The field is a pure function
// /// of its seed, so "Regenerate" only re-runs it (useful after the bounds move, or when chunks were lost
// /// to a domain reload), while "New Seed" is the one that actually gives a different-looking field.
// /// </summary>
// [CustomEditor(typeof(SpaceDebris))]
// [CanEditMultipleObjects]
// public class SpaceDebrisEditor : UnityEditor.Editor
// {
//     public override void OnInspectorGUI()
//     {
//         DrawDefaultInspector();
//
//         EditorGUILayout.Space();
//
//         using (new EditorGUILayout.HorizontalScope())
//         {
//             if (GUILayout.Button("Regenerate"))
//                 foreach (Object t in targets)
//                     Regenerate((SpaceDebris)t);
//
//             if (GUILayout.Button("New Seed"))
//             {
//                 // Through the serialized object so the change is undoable and marks the scene dirty —
//                 // the seed is the only part of the field that is actually saved.
//                 SerializedProperty seed = serializedObject.FindProperty("seed");
//                 seed.intValue = Random.Range(int.MinValue, int.MaxValue);
//                 serializedObject.ApplyModifiedProperties();
//
//                 foreach (Object t in targets)
//                     Regenerate((SpaceDebris)t);
//             }
//         }
//     }
//
//     private static void Regenerate(SpaceDebris debris)
//     {
//         if (!debris) return;
//
//         debris.Rebuild();
//
//         // The chunks themselves are DontSave, so nothing to dirty there — just redraw.
//         SceneView.RepaintAll();
//     }
// }
// #endif
