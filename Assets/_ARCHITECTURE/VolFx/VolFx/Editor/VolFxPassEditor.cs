using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

//  VolFx © NullTale - https://x.com/NullTale
namespace VolFx.Editor
{
    [CustomEditor(typeof(VolFx), true)]
    public class VolFxPassEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawDefaultInspector();
            
            serializedObject.ApplyModifiedProperties();
        }
    }
}