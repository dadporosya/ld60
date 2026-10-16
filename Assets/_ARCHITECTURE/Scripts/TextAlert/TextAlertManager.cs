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
}
