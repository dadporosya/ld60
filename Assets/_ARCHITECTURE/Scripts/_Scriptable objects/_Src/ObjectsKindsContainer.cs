using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem;

[CreateAssetMenu(fileName = "ObjectsKindsContainer", menuName = "Scriptable Objects/ObjectsKindsContainer")]
public class ObjectsKindsContainer : ScriptableObject
{
    [SerializeField] public List<GameObject> rawObject; // mb swtich
    [SerializeField] public List<string> rawKeys;
    [HideInInspector] public Dictionary<string, GameObject> objects = new Dictionary<string, GameObject>();
    [HideInInspector] public List<string> keys;

    void OnEnable()
    {
        objects.Clear();
        keys.Clear();

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
    
    public ObjectsWithDistributionContainer ConvertToDistributionContainer()
    {
        ObjectsWithDistributionContainer container = ScriptableObject.CreateInstance<ObjectsWithDistributionContainer>();
        container.rawObject = new List<GameObject>(rawObject);
        container.rawDistribution = new List<float>();
        container.Init();
        return container;
    }
}