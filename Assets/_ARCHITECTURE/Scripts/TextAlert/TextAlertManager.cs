using UnityEngine;

public class TextAlertManager : MonoBehaviour
{
    public static TextAlertManager Instance;

    [Header("Damage Alert Settings")]
    [SerializeField] private AlertDamageText damageTextPfb;
    [SerializeField] private float holdDuration    = 0.6f;
    [SerializeField] private float fadeOutDuration = 0.4f;
    [SerializeField] private float speed           = 1f;
    [SerializeField] private float spread = 1f;
    [SerializeField] private Vector3 alertOffset = Vector3.zero;

    [Tooltip("Size of every alert, as a multiplier of the prefab's own scale. 1 = as authored.")]
    [Min(0f)] [SerializeField] private float alertScale = 1f;

    [Header("Text Alert Settings (CreateTextAlert)")]
    [Tooltip("Prefab for text popups. Empty: the damage prefab. A UI prefab (RectTransform) spawns under UI Parent.")]
    [SerializeField] private AlertDamageText textAlertPfb;
    [Tooltip("Where UI popups spawn. Empty: the root canvas of the anchor they pop up at.")]
    [SerializeField] private RectTransform uiParent;
    [SerializeField] private float textHoldDuration    = 0.9f;
    [SerializeField] private float textFadeOutDuration = 0.5f;
    [Tooltip("Rise speed, in the parent's units per second (canvas units for UI popups).")]
    [SerializeField] private float textSpeed = 60f;
    [Tooltip("Random offset around the anchor, in the parent's units.")]
    [SerializeField] private float textSpread = 12f;

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance);
    }

    public void CreateDamageAlert(int damage, Vector3 position, Color? color = null, string label = null, Vector3 offset = default)
    {
        AlertDamageText alert = Instantiate(
            damageTextPfb,
            position + alertOffset + offset + h.RandomPositionInCircle(spread),
            Quaternion.identity);
        // The root, not damageText: AlertDamageText tweens the text's own scale down to zero on fade.
        alert.transform.localScale *= alertScale;
        alert.damageText.text  = label != null ? $"{label}{damage}" : damage.ToString();
        alert.holdDuration    = holdDuration;
        alert.fadeOutDuration = fadeOutDuration;
        alert.speed           = speed;
        if (color.HasValue) alert.damageText.color = color.Value;
    }

    public void CreateDamageAlert(int damage, Transform target, Color? color = null, string label = null, Vector3 offset = default)
    {
        if (target != null)
            CreateDamageAlert(damage, target.position, color, label, offset);
    }

    /// <summary>
    /// Pops <paramref name="text"/> (rich text allowed) up at <paramref name="anchor"/>, rising and fading. A UI
    /// prefab spawns under <see cref="uiParent"/> (else the anchor's root canvas); <paramref name="offset"/> and
    /// the spread are in that parent's units.
    /// </summary>
    public AlertDamageText CreateTextAlert(string text, Transform anchor, Color? color = null, Vector2 offset = default)
    {
        AlertDamageText prefab = textAlertPfb ? textAlertPfb : damageTextPfb;
        if (!prefab || !anchor) return null;

        bool ui = prefab.transform is RectTransform;
        Transform parent = ui ? UIParentFor(anchor) : null;

        AlertDamageText alert = Instantiate(prefab, parent, false);
        alert.transform.position = anchor.position;
        Vector3 local = alert.transform.localPosition + (Vector3)(offset + Random.insideUnitCircle * textSpread);
        if (ui) local.z = 0f;
        alert.transform.localPosition = local;
        alert.transform.localScale *= alertScale;

        alert.damageText.text = text;
        alert.holdDuration    = textHoldDuration;
        alert.fadeOutDuration = textFadeOutDuration;
        alert.speed           = textSpeed;
        if (color.HasValue) alert.damageText.color = color.Value;
        return alert;
    }

    private Transform UIParentFor(Transform anchor)
    {
        if (uiParent) return uiParent;
        Canvas canvas = anchor.GetComponentInParent<Canvas>();
        return canvas ? canvas.rootCanvas.transform : anchor.parent;
    }
}
