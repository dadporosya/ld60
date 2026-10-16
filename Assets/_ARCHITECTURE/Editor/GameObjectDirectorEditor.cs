#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GameObjectDirector))]
public class GameObjectDirectorEditor : EntityDirectorEditor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        EditorGUILayout.Space();

        GUI.enabled = Application.isPlaying;
        if (GUILayout.Button("■  Stop Token Income"))
        {
            var method = target.GetType().GetMethod("StopTokenIncome");
            method?.Invoke(target, null);
        }
        GUI.enabled = true;
    }
}
#endif
