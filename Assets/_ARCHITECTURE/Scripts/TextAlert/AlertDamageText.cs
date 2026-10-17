using UnityEngine;
using TMPro;
using PrimeTween;

public class AlertDamageText : MonoBehaviour
{
    public TMP_Text damageText;
    public float holdDuration   = 0.6f;
    public float fadeOutDuration = 0.4f;
    public float speed          = 2f;

    private void Start()
    {
        if (!damageText) damageText = GetComponent<TMP_Text>();
        if (!damageText) damageText = GetComponentInChildren<TMP_Text>();
        PlayAnimation();
    }

    private void Update()
    {
        // Local: in canvas units under a UI parent; the same as world space for an alert without a parent.
        transform.localPosition += Vector3.up * speed * Time.deltaTime;
    }

    public void PlayAnimation()
    {
        Sequence.Create()
            .ChainDelay(holdDuration)
            .Chain(Tween.Scale(damageText.transform, Vector3.zero, fadeOutDuration, Ease.InQuad))
            .Group(Tween.Alpha(damageText, 0f, fadeOutDuration))
            .ChainCallback(() => Destroy(gameObject));
    }
}
