using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

class MultipleEquipmentInTail : EquipmentBase
{
    [SerializeField] private bool applyEachPrefab = false;
    public List<EquipmentBase> prefabs;
    // [HideInInspector] public List<EquipmentBase> instances;
    
    // equipment prefab id: <tail member id: instance>
    [HideInInspector] public List<Dictionary<int, EquipmentBase>> instances = new List<Dictionary<int, EquipmentBase>>();
    public List<P.PositionType> positionTypes;
    
    public List<int> specificDistances = new List<int>();
    public List<int> specificPositions= new List<int>();

    public override void Init(GameObject holderIn = null)
    {
        if (holderIn) holder = holderIn;
        h.Out("empty init");
    }
    private void Awake()
    {
        if (!holder && transform.parent) holder = transform.parent.gameObject;
        StartLifetime();
        InitInstancesList();
    }

    private void InitInstancesList()
    {
        if (instances == null) instances = new List<Dictionary<int, EquipmentBase>>();
        instances.Clear();
        for (int i = 0; i < prefabs.Count; i++)
        {
            instances.Add(new Dictionary<int, EquipmentBase>());
        }
    }

    public void Reset()
    {
        Apply();
    }
    
    public override void Apply(GameObject holderIn = null)
    {
        // Tail-positioning logic removed: it required the TailManager /
        // FollowingObjInTail scripts, which are not part of this project.
        if (holderIn) holder = holderIn;
        if (!holder)
        {
            h.Out("no holder");
            return;
        }
    }

    public override void Remove()
    {
        foreach (var l in instances)
        {
            foreach (var kv in l)
            {
                EquipmentBase instance = kv.Value;
                instance.Remove();
                Destroy(instance.gameObject);
            }
        }

        InitInstancesList();
    }

    public void OnDestroy()
    {
        Remove();
    }
}