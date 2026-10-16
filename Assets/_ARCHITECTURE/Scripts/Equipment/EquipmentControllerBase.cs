using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Roughly - status effect and turret (and mb other stuff) controller
/// Appends and removes that this from the unit
/// Is containing in unit and dnd object in inventory
///
/// Init - Create instance and Apply (if apply==true)
/// Apply - apply existing instance
/// Remove - remove existing instance
/// </summary>

[CreateAssetMenu(fileName = "Equipment Controller", menuName = "Equipment/Equipment Controller")]
public class EquipmentControllerBase : ScriptableObject, IRecyclable
{
    [Header("Appearance")]
    public bool applyOnStart = false;
    public GameObject holder;
    public Sprite icon;
    public Material material;

    [Header("Equipment Settings")]
    public List<EquipmentBase> prefabs = new List<EquipmentBase>();
    [HideInInspector] public List<EquipmentBase> instances = new List<EquipmentBase>();

    public P.EquipmentType type = P.EquipmentType.Any;

    [Header("Other")]
    public EquipmentDNDObject equipmentDndObjectPrefab;

    [SerializeField] private bool defaultRecycleBonus = true;
    public int onRecycleXpAmount = 1;

    public void Merge(
        EquipmentControllerBase controller,
        bool applyOnMerge = true,
        bool destroyOtherOnMerge = true
        )
    {
        if (!controller) return;

        if (controller.type != type && controller.type != P.EquipmentType.Any && type != P.EquipmentType.Any)
        {
            h.Out("Equipment controller type mismatch");
        }

        foreach (EquipmentBase prefab in controller.prefabs)
        {
            prefabs.Add(prefab);
        }

        foreach (var instance in controller.instances)
        {
            instances.Add(instance);
        }
        
        if (destroyOtherOnMerge) Destroy(controller);
    }
    
    private void OnEnable()
    {
        if (prefabs != null && prefabs.Count > 0 && prefabs[0])
        {
            if (!icon) icon = prefabs[0].AvatarSprite;
            material = prefabs[0].material;
        }
    }

    public void Init(
        GameObject holderIn = null,
        bool apply = true,
        bool createControllerInstance = false)
    {
        if (holderIn) holder = holderIn;
        if (!holder)
        {
            h.Out("No holder to init");
            return;
        }
        if (apply) Apply(createControllerInstance: createControllerInstance);
    }

    public virtual void Apply(GameObject holderIn = null, bool createControllerInstance = false)
    {
        if (holderIn)
        {
            Init(holderIn, createControllerInstance: createControllerInstance);
            return;
        }

        if (!holder) return;
        if (instances.Count > 0) Remove();

        foreach (EquipmentBase prefab in prefabs)
        {
            if (!prefab) continue;
            EquipmentBase instance = Instantiate(prefab, holder.transform);
            instances.Add(instance);
        }

        // Unit-specific slot bookkeeping only applies when the holder is actually a Unit;
        // generic GameObject holders just carry the instantiated equipment instances.
        Unit holderUnit = holder.GetComponent<Unit>();
        if (holderUnit)
        {
            holderUnit.AppendEquipment(
                this,
                init: false,
                createInstance: createControllerInstance
            );
        }
    }

    public virtual void Remove(bool destroyEquipmentInstances=true)
    {
        if (holder)
        {
            Unit holderUnit = holder.GetComponent<Unit>();
            if (holderUnit) holderUnit.RemoveEquipment(this, false);
        }

        if (destroyEquipmentInstances)
        {
            foreach (EquipmentBase instance in instances)
            {
                if (!instance) continue;
                instance.Remove();
                Destroy(instance.gameObject);
            }
            instances.Clear();
        }
        
        holder = null;
    }

    public EquipmentDNDObject GenerateEquipmentDndObject(
        EquipmentDNDObject prefab = null,
        Transform parent = null,
        SlotBase slot = null)
    {
        if (prefab == null) prefab = equipmentDndObjectPrefab;
        // TODO
        // if (!prefab) prefab = R.Prefabs.UI.DNDEquipmentItem.GetComponent<EquipmentDNDObject>();
        EquipmentDNDObject dndObject = Instantiate(prefab);
        dndObject.Init(this);
        if (slot) slot.PlaceObject(dndObject);
        dndObject.transform.SetParent(parent);
        return dndObject;
    }

    public virtual void OnRecycle()
    {
        if (defaultRecycleBonus)
        {
            // XP reward removed: it required the PlayerXpManager script,
            // which is not part of this project.
        }
    }
}
