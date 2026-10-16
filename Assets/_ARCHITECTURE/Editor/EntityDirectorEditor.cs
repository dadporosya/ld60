#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EntityDirector<>), true)]
public class EntityDirectorEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        EditorGUILayout.Space();

        // Use reflection to read 'off' and call ToggleOff safely
        var offProp = target.GetType().GetProperty("off");
        bool isOff = offProp != null && (bool)offProp.GetValue(target);

        string label = isOff ? "▶  Start Spawning" : "■  Stop Spawning";
        if (GUILayout.Button(label))
        {
            Undo.RecordObject(target, "Toggle EntityDirector off");
            var toggleMethod = target.GetType().GetMethod("ToggleOff");
            toggleMethod?.Invoke(target, null);
            EditorUtility.SetDirty(target);
        }
    }
}
#endif