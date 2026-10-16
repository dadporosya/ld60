using System;
using UnityEngine;
// This component is a gameobject inside the unit
public class StatusEffectBase : EquipmentBase
{
    public int level = 1; // ?

    public enum EffectType
    {
        Positive,
        Negative
    }
    public EffectType type = EffectType.Positive;

    public override void Init(GameObject holderIn=null)
    {
        if (holderIn) holder = holderIn;
        if (!holder) AssignHolder();

        if (!holder) return;
        transform.SetParent(holder.transform);
        Apply();
    }
    private void Awake()
    {
        equipmentType = P.EquipmentType.Effect;
        if (!holder) AssignHolder();
    }

    private void AssignHolder()
    {
        if (transform.parent) holder = transform.parent.gameObject;
    }

    public override void Apply(GameObject holderIn=null)
    {
        if (holderIn)
        {
            Init(holderIn);
            return;
        }
        if (!holder) AssignHolder();
        if (!holder) return;

        // individual code for each effect

        StartLifetime();
    }

    public override void Remove()
    {
        StopLifetime();
        if (!holder) return;
    }

    /// <summary>
    /// Called by Unit.UpdateStats() after base stats have been reset to their
    /// level-scaled values.  Override to reapply this effect's modifications
    /// directly to unit.stats.values without firing stat-change events (to avoid
    /// recursion).  Default is a no-op so subclasses that don't touch stats don't
    /// need to override.
    /// </summary>
    public virtual void OnUpdateStats() { }
}
