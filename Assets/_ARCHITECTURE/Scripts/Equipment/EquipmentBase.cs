using System.Collections;
using UnityEngine;

public abstract class EquipmentBase : StatUIShownBase
{
    public GameObject holder;
    public P.EquipmentType equipmentType;

    public float lifetime = 3f;
    public bool infiniteLifetime = true;

    private Coroutine lifetimeCoroutine;

    // Holders are plain GameObjects. Equipment that needs unit-specific behaviour resolves a
    // Unit component from the holder itself (see UnitStatChangeEquipment).
    public abstract void Init(GameObject holderIn = null);
    public abstract void Apply(GameObject holderIn = null);
    public abstract void Remove();

    public virtual void Start()
    {
        Apply();
    }

    protected void StartLifetime()
    {
        if (infiniteLifetime) return;
        if (lifetimeCoroutine != null) StopCoroutine(lifetimeCoroutine);
        lifetimeCoroutine = StartCoroutine(LifetimeCoroutine());
    }

    protected void StopLifetime()
    {
        if (lifetimeCoroutine == null) return;
        StopCoroutine(lifetimeCoroutine);
        lifetimeCoroutine = null;
    }

    private IEnumerator LifetimeCoroutine()
    {
        float timeElapsed = 0f;
        while (timeElapsed < lifetime)
        {
            while (GameFlowManager.Instance.IsPaused())
                yield return null;
            h.Out(timeElapsed);
            timeElapsed += Time.deltaTime;
            yield return null;
        }
        Remove();
    }
}
