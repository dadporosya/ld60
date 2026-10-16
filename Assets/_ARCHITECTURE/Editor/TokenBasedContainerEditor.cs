#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TokenBasedContainerNonGenericBase), true)]
public class TokenBasedContainerEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();

        if (GUILayout.Button("Clear Namespaces"))
        {
            var container = (TokenBasedContainerNonGenericBase)target;
            Undo.RecordObject(container, "Clear Namespaces");
            container.ClearNamesAndIds();
            EditorUtility.SetDirty(container);
        }
    }
}
#endif
