using System;
using System.Collections.Generic;
using UnityEngine;


// allow to change multiple stats by the same value.
// In case if different values for each stat needed, use multiple Stateffects and
// multipleStatusEffect object
public class UnitStatChangeEquipment : StatusEffectBase
{
    [Header("StatChange")]
    public List<string> stats;

    public float defaultConst = 0;
    public float defaultMult = 1;
    public float defaultExponent = 1;

    public float ChangeValue(float value)
    {
        float result =
            Mathf.Pow(value, defaultExponent) * defaultMult + defaultConst;
        // h.Out(value, result);
        return result;
    }

    // Inverse of ChangeValue: x = Pow((y - c) / m, 1/e)
    public float RevertValue(float value)
    {
        if (Mathf.Approximately(defaultMult, 0f)) return value;
        float inner = (value - defaultConst) / defaultMult;
        if (Mathf.Approximately(defaultExponent, 0f)) return value;
        return Mathf.Pow(inner, 1f / defaultExponent);
    }

    // This effect only makes sense on a Unit holder: it reads/writes the Unit's stat container.
    // The holder is a generic GameObject (per the equipment refactor), so we resolve the Unit
    // component from it and no-op when the holder isn't a Unit.
    private Unit HolderUnit => holder ? holder.GetComponent<Unit>() : null;

    public override void Apply(GameObject holderIn = null)
    {
        base.Apply(holderIn);

        Unit unit = HolderUnit;
        if (!unit || unit.stats == null)
        {
            h.Out("UnitStatChangeEquipment requires a Unit holder");
            return;
        }

        foreach (string statKey in stats)
        {
            if (!unit.stats.values.ContainsKey(statKey))
            {
                h.Out(statKey, " key not found");
                continue;
            }

            unit.SetStat(ChangeValue(unit.stats.values[statKey]), statKey);
        }
    }

    public override void OnUpdateStats()
    {
        Unit unit = HolderUnit;
        if (!unit || unit.stats == null) return;
        foreach (string statKey in stats)
        {
            if (!unit.stats.values.ContainsKey(statKey)) continue;
            unit.stats.values[statKey] = ChangeValue(unit.stats.values[statKey]);
        }
    }

    public override void Remove()
    {
        base.Remove();

        Unit unit = HolderUnit;
        if (unit && unit.stats != null)
        {
            foreach (string statKey in stats)
            {
                if (!unit.stats.values.ContainsKey(statKey)) continue;
                unit.SetStat(RevertValue(unit.stats.values[statKey]), statKey);
            }
        }

        Destroy(gameObject);
    }

}
