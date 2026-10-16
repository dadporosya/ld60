#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PurchaseItemBase), true)]
public class PurchaseItemBaseEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawPropertiesExcluding(serializedObject, "rarity");

        EditorGUILayout.Space();

        List<string> options = P.raritiesNames.Values.ToList();
        SerializedProperty rarityProp = serializedObject.FindProperty("rarity");
        int current = options.IndexOf(rarityProp.stringValue);
        int selected = EditorGUILayout.Popup("Rarity", current < 0 ? 0 : current, options.ToArray());
        rarityProp.stringValue = options[selected];

        serializedObject.ApplyModifiedProperties();
    }
}
#endif
