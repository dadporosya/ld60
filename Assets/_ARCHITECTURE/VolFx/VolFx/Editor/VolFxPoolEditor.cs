using UnityEditor;
using VolFx.Tools;

namespace VolFx.Editor
{
    [CustomEditor(typeof(VolFxPool), true)]
    public class VolFxPoolEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawDefaultInspector();
            
            serializedObject.ApplyModifiedProperties();
        }
    }
}