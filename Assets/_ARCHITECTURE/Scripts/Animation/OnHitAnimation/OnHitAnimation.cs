using System.Collections;
using UnityEngine;

public class OnHitAnimation : AnimationBase
{
    [Header("OnHit Settings")]
    public float duration = 0.2f;
    // Color the sprite blends toward on hit.
    public Color hitColor = Color.white;

    private Unit unit;
    private MasterMaterialController _matController;

    [SerializeField] private bool addListenerToUnit = false;

    public override void Awake()
    {
        base.Awake();

        type = AnimationPreferences.Type.OnHit;
        
        _matController = GetComponent<MasterMaterialController>();
        if (!_matController)
            _matController = gameObject.AddComponent<MasterMaterialController>();

        _matController.SetHitEffect(true);
        _matController.SetHitColor(hitColor);

        unit = GetComponent<Unit>();
        if (!unit) unit = GetComponentInParent<Unit>();
        if (!unit) return;

        if (addListenerToUnit) unit.onTakeDamage.AddListener(() => StartCoroutine(Play()));
    }

    public override void ReturnToInitialState()
    {
        _matController.SetHitBlend(0f);
    }

    public override IEnumerator AnimationCoroutine()
    {
        
        float half = duration * 0.5f;
        float elapsed = 0f;

        while (elapsed < half)
        {
            elapsed += Time.deltaTime;
            _matController.SetHitBlend(Mathf.Clamp01(elapsed / half));
            yield return null;
        }

        _matController.SetHitBlend(1f);
        elapsed = 0f;

        while (elapsed < half)
        {
            elapsed += Time.deltaTime;
            _matController.SetHitBlend(Mathf.Clamp01(1f - elapsed / half));
            // h.Out("'blend to hiiiit", Mathf.Clamp01(1f - elapsed / half));
            yield return null;
        }

        yield return base.AnimationCoroutine();
    }
}