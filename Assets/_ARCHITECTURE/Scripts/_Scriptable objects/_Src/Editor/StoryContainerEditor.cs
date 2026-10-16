using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(StoryContainer))]
public class StoryContainerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        StoryContainer container = (StoryContainer)target;

        EditorGUILayout.Space();
        if (GUILayout.Button("Assign Dialogues"))
        {
            container.AssignDialogues();
            EditorUtility.SetDirty(container);
            AssetDatabase.SaveAssets();
        }
    }
}
