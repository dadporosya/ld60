using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "ScriptableObjectsKindsContainer", menuName = "Scriptable Objects/ScriptableObjectsKindsContainer")]
public class ScriptableObjectsKindsContainer : ScriptableObject
{
    [SerializeField] public List<ScriptableObject> rawObject; // mb swtich
    [SerializeField] public List<string> rawKeys;
    [HideInInspector] public Dictionary<string, ScriptableObject> objects = new Dictionary<string, ScriptableObject>();
    [HideInInspector] public List<string> keys;

    void OnEnable()
    {
        objects.Clear();
        keys.Clear();

        // if (rawKeys.Count != rawKeys.Count) throw new System.Exception($"Missing values or keys for {name}");

        if (rawKeys.Count != rawObject.Count)
        {
            rawKeys.Clear();
            foreach (var obj in rawObject)
            {
                rawKeys.Add(obj.name);
            }
        }
        
        for (int i = 0; i < rawKeys.Count; i++)
        {
            // Debug.Log(rawKeys[i]);
            // Debug.Log(rawObject[i]);
            objects.Add(rawKeys[i], rawObject[i]);
            keys.Add(rawKeys[i]);
        }
        //h.Out(objects.Count);
    }

    void OnDisable()
    {
        objects.Clear();
        keys.Clear();
        // rawObject.Clear();
        // rawKeys.Clear();
    }
}