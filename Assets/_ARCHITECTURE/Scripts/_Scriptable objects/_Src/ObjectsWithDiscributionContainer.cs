using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ObjectsWithDistributionContainer", menuName = "Scriptable Objects/Objects With Distribution Container")]
public class ObjectsWithDistributionContainer : ScriptableObject
{
    [SerializeField] public List<GameObject> rawObject; // mb swtich
    [SerializeField] public List<float> rawDistribution;
    [HideInInspector] public Dictionary<GameObject, float> objects = new Dictionary<GameObject, float>();

    public void Init()
    {
        objects.Clear();

        if (rawDistribution.Count != rawObject.Count)
        {
            rawDistribution.Clear();
            foreach (var obj in rawObject)
            {
                rawDistribution.Add(1f);
            }
        }
        
        for (int i = 0; i < rawObject.Count; i++)
        {
            // Debug.Log(rawKeys[i]);
            // Debug.Log(rawObject[i]);
            objects.Add(rawObject[i], rawDistribution[i]);
            // h.Out(rawObject[i].name, rawDistribution[i]);
        }
        //h.Out(objects.Count);
    }
    void OnEnable()
    {
        Init();
    }

    void OnDisable()
    {
        objects.Clear();
    }
}
