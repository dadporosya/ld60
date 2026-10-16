using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector for <see cref="VisualNovelDialogueContainer"/>. The container keeps its per-node
/// visual novel settings in a list parallel to the base nodes list; this draws each node's text
/// and its settings together as one foldout, and adds/removes/moves both lists in lockstep.
/// </summary>
[CustomEditor(typeof(VisualNovelDialogueContainer))]
public class VisualNovelDialogueContainerEditor : Editor
{
    private const int PreviewLength = 48;

    private SerializedProperty nodesProp;
    private SerializedProperty settingsProp;

    private enum NodeAction { None, AddAfter, Remove, MoveUp, MoveDown }

    private void OnEnable()
    {
        nodesProp = serializedObject.FindProperty("nodes");
        settingsProp = serializedObject.FindProperty("nodeSettings");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawPropertiesExcluding(serializedObject, "m_Script", "nodes", "nodeSettings");

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField($"Nodes ({nodesProp.arraySize})", EditorStyles.boldLabel);

        NodeAction action = NodeAction.None;
        int actionIndex = -1;

        for (int i = 0; i < nodesProp.arraySize; i++)
        {
            SerializedProperty node = nodesProp.GetArrayElementAtIndex(i);
            SerializedProperty row = i < settingsProp.arraySize ? settingsProp.GetArrayElementAtIndex(i) : null;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            node.isExpanded = EditorGUILayout.Foldout(node.isExpanded, NodeLabel(i, node, row), true);
            if (node.isExpanded)
            {
                EditorGUI.indentLevel++;
                DrawChildren(node);

                if (row != null)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Visual novel", EditorStyles.boldLabel);
                    DrawChildren(row);
                }
                EditorGUI.indentLevel--;

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(i == 0))
                    if (GUILayout.Button("Move up", EditorStyles.miniButtonLeft)) { action = NodeAction.MoveUp; actionIndex = i; }
                using (new EditorGUI.DisabledScope(i == nodesProp.arraySize - 1))
                    if (GUILayout.Button("Move down", EditorStyles.miniButtonMid)) { action = NodeAction.MoveDown; actionIndex = i; }
                if (GUILayout.Button("Insert after", EditorStyles.miniButtonMid)) { action = NodeAction.AddAfter; actionIndex = i; }
                if (GUILayout.Button("Remove", EditorStyles.miniButtonRight)) { action = NodeAction.Remove; actionIndex = i; }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        if (GUILayout.Button("Add node"))
        {
            action = NodeAction.AddAfter;
            actionIndex = nodesProp.arraySize - 1;
        }

        serializedObject.ApplyModifiedProperties();

        if (action != NodeAction.None) Apply(action, actionIndex);
    }

    /// <summary>
    /// Structural edits go through the lists directly (with Undo) rather than SerializedProperty:
    /// a new node then gets fresh defaults instead of a copy of its neighbour - a copied node would
    /// replay the previous node's SFX and effects.
    /// </summary>
    private void Apply(NodeAction action, int index)
    {
        VisualNovelDialogueContainer container = (VisualNovelDialogueContainer)target;
        Undo.RecordObject(container, "Edit visual novel nodes");
        container.SyncNodeSettings();

        switch (action)
        {
            case NodeAction.AddAfter:
                container.nodes.Insert(index + 1, new DialogueNode());
                container.nodeSettings.Insert(index + 1, new VisualNovelNodeSettings());
                break;

            case NodeAction.Remove:
                container.nodes.RemoveAt(index);
                container.nodeSettings.RemoveAt(index);
                break;

            case NodeAction.MoveUp:
                Swap(container, index, index - 1);
                break;

            case NodeAction.MoveDown:
                Swap(container, index, index + 1);
                break;
        }

        container.AssignNodeIds();
        EditorUtility.SetDirty(container);
        serializedObject.Update();
    }

    private static void Swap(VisualNovelDialogueContainer c, int a, int b)
    {
        (c.nodes[a], c.nodes[b]) = (c.nodes[b], c.nodes[a]);
        (c.nodeSettings[a], c.nodeSettings[b]) = (c.nodeSettings[b], c.nodeSettings[a]);
    }

    private static void DrawChildren(SerializedProperty parent)
    {
        SerializedProperty it = parent.Copy();
        SerializedProperty end = parent.GetEndProperty();
        if (!it.NextVisible(true)) return;

        while (!SerializedProperty.EqualContents(it, end))
        {
            EditorGUILayout.PropertyField(it, true);
            if (!it.NextVisible(false)) break;
        }
    }

    private static string NodeLabel(int index, SerializedProperty node, SerializedProperty row)
    {
        string speaker = node.FindPropertyRelative("speakerName").stringValue;
        string text = node.FindPropertyRelative("text").stringValue ?? "";
        text = text.Replace('\n', ' ');
        if (text.Length > PreviewLength) text = text.Substring(0, PreviewLength) + "...";

        string transition = "";
        if (row != null)
        {
            SerializedProperty t = row.FindPropertyRelative("transition");
            if (t.enumValueIndex != (int)VisualNovelTransition.KeepPrevious)
                transition = $"  [{t.enumDisplayNames[t.enumValueIndex]}]";
        }

        return string.IsNullOrEmpty(speaker)
            ? $"#{index}  {text}{transition}"
            : $"#{index}  {speaker}: {text}{transition}";
    }
}
