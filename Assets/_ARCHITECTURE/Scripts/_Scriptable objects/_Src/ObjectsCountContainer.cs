using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEditor;

[CreateAssetMenu(fileName = "ObjectsCountContainer", menuName = "Scriptable Objects/ObjectsCountContainer")]
public class ObjectsCountContainer : ScriptableObject
{
    public List<string> rawKeys;
    public List<float> rawValues;
    public float defaultValue = 0f;

    [SerializeField] private bool initFromKindsContainer=true;
    public ObjectsKindsContainer data;
    
    [SerializeField] private bool initFromKeyContainer = false;
    public KeyContainer keyData;

    
    [SerializeField] private bool initFromDefaultValues = false;
    public ObjectsCountContainer defaultData;
    
    //todo: add key list, which might be influenced by rand init
    public float maxValue=0f;
    public float minSum=1f;
    public float maxSum=1f;
    public bool randValues=false;

    [HideInInspector] public Dictionary<string, float> values = new Dictionary<string, float>();
    [HideInInspector] public List<string> keys = new List<string>();

    public void Init(ObjectsCountContainer newData)
    {
        Init(newData.values.Keys.ToList(), newData.values.Values.ToList());
    }

    public void Init(List<string> keysIn, List<float> valuesIn, bool rand=false)
    {
        if (keysIn == null) return;
        InitValues(keysIn, valuesIn);
        // //h.Out("Initititit");
        // //h.Out(values);
        // //h.Out(keys);
        if (rand)
        {
            SetValuesZero();
            GenerateRandomValues();
        }
    }

    void OnEnable()
    {
        values.Clear();
        keys.Clear();

        var k = rawKeys;
        
        if (initFromKindsContainer && data)
        {
            k = data.keys;
        } else if (initFromDefaultValues && defaultData)
        {
            k = defaultData.keys;
        } else if (keyData && initFromKeyContainer)
        {
            k = keyData.keys;
        }
        // Create all necessary keys from kinds container with default value 0
        Init(k, rawValues);
        
        if (initFromDefaultValues && defaultData)
        {
            // Assign default values
            h.AssignValuesToDict(ref values, defaultData.values);
            // Add keys from defaultData that are missing in values
            foreach (var kv in defaultData.values)
            {
                if (!values.ContainsKey(kv.Key))
                {
                    values[kv.Key] = kv.Value;
                    keys.Add(kv.Key);
                }
            }
        }
        
        // Assign specific values, provided in the inspector
        h.AssignValuesToDict(ref values, rawKeys, rawValues);
    }

    void OnDisable()
    {
        // rawKeys.Clear();
        // rawValues.Clear();
        keys.Clear();
        values.Clear();
    }

    void GenerateRandomValues(float quota=0f, float max=0f)
    {
        if (keys.Count == 0 || values.Count == 0) InitValues(rawKeys, rawValues);
        ////h.Out("GENERATING");
        ////h.Out(values);
        // EditorApplication.isPaused = true;   // pause

        if (quota == 0f) quota = UnityEngine.Random.Range(minSum, maxSum);
        if (max == 0f) max = Math.Min(maxValue, quota);

        ////h.Out($"max: {max}, quota: {quota}");

        int n = keys.Count;

        List<int> remaining = new List<int>(); // reaminig indexes
        for (int i = 0; i < n; i++) remaining.Add(i);

        float currentV;
        int currentI;

        float min = 1f;

        for (int i = 0; i < n-1 && quota > 0f; i++)
        {
            currentV = UnityEngine.Random.Range(min, Math.Min(max, quota));

            min = 0f;
            currentI = h.RandChoice(remaining);
            remaining.Remove(currentI);
            values[keys[currentI]] = currentV;

            ////h.Out($"{keys[currentI]}: {values[keys[currentI]]} = {currentV}");

            quota -= currentV;
        }

        if (quota > 0f)
        {
            currentI = h.RandChoice(remaining);
            remaining.Remove(currentI);
            values[keys[currentI]] = quota;

            ////h.Out($"{keys[currentI]}: {values[keys[currentI]]} = {quota}");

            quota = 0f;
        }
    }

    public void SetValue(string key, float newValue)
    {
        values[key] = newValue;
    }

    public void ChangeValue(string key, float delta)
    {
        values[key] += delta;
    }

    public void Show()
    {
        ////h.Out(values);
    }

    public void SetValuesZero()
    {
        ////h.Out("clear");
        foreach(KeyValuePair<string, float> kv in values)
        {
            values[kv.Key] = defaultValue;
        }
        ////h.Out(values);
    }

    public void InitValues(List<string> keysIn, List<float> valuesIn=null)
    {
        if (valuesIn==null
            || valuesIn.Count != keysIn.Count)
        {
            valuesIn = h.CreateList(keysIn.Count, defaultValue);
        }
        
        values.Clear();
        keys.Clear();

        for (int i = 0; i < keysIn.Count; i++)
        {
            values[keysIn[i]] = valuesIn[i];
            keys.Add(keysIn[i]);
        }
    }
}
