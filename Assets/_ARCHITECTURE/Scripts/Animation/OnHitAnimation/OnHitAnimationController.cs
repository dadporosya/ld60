using System.Collections.Generic;
using UnityEngine;

public class OnHitAnimationController : AnimationControllerBase
{
    private Unit unit;
    public override void Awake()
    {
        base.Awake();
        
        if (!targetTypes.Contains(AnimationPreferences.Type.OnHit)) targetTypes.Add(AnimationPreferences.Type.OnHit);
        
        unit = GetComponent<Unit>();
        if (!unit) unit = GetComponentInParent<Unit>();
        if (!unit) return;

        unit.onTakeDamage.AddListener(() => { StartCoroutine(PlayAnimations()); });
    }
}