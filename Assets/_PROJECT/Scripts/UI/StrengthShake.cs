using PrimeTween;
using UnityEngine;

/// <summary>
/// Keeps a UI element shaking for as long as it is "strong": nothing at strength 0, wildest at 1. Set it
/// with <see cref="SetStrength"/>, e.g. how good the hand on the dice is.
///
/// <para>A change of strength also kicks: a short burst on top of the steady shake, plus a scale punch,
/// both bigger the stronger the new value. Like <see cref="CountingText"/>, the shake is an offset on top
/// of the element's own position and rotation, so it works inside layout groups and goes back to rest.</para>
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class StrengthShake : MonoBehaviour
{
    [Header("Steady shake")]
    [Tooltip("Maps strength (0..1) to how much of the max shake is used. Ease-in keeps weak hands calm.")]
    [SerializeField] private AnimationCurve strengthCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("How far it jitters at full strength (UI units).")]
    [SerializeField, Min(0f)] private float maxOffset = 6f;
    [Tooltip("Degrees it wobbles either way at full strength.")]
    [SerializeField, Min(0f)] private float maxRotation = 4f;
    [Tooltip("Jitters per second at the weakest shake.")]
    [SerializeField, Min(0.1f)] private float minFrequency = 8f;
    [Tooltip("Jitters per second at full strength.")]
    [SerializeField, Min(0.1f)] private float maxFrequency = 35f;
    [Tooltip("Seconds it takes to ease into a new strength.")]
    [SerializeField, Min(0f)] private float blendTime = 0.15f;

    [Header("Kick on change")]
    [Tooltip("Extra shake right after the strength changes, as a multiple of the steady one.")]
    [SerializeField, Min(0f)] private float kickMultiplier = 2.5f;
    [Tooltip("Seconds the kick takes to settle into the steady shake.")]
    [SerializeField, Min(0.01f)] private float kickDuration = 0.35f;
    [Tooltip("Scale punch at full strength (weaker hands punch less). 0: none.")]
    [SerializeField, Min(0f)] private float maxPunchScale = 0.25f;

    /// <summary>The strength asked for (0..1).</summary>
    public float Strength { get; private set; }

    private RectTransform rect;
    private float amount;
    private float kick;
    private float time;
    private float seed;
    private Vector2 appliedOffset;
    private float appliedAngle;
    private Vector3 restScale;
    private bool restScaleKnown;
    private Tween punchTween;

    private void Awake()
    {
        rect = (RectTransform)transform;
        seed = Random.value * 100f;
        RememberRest();
    }

    private void OnDisable()
    {
        punchTween.Stop();
        if (restScaleKnown) transform.localScale = restScale;
        amount = 0f;
        kick = 0f;
        ApplyShake(Vector2.zero, 0f);
    }

    /// <summary>How hard to shake, 0..1. A different value than before kicks, unless <paramref name="kickOnChange"/> is off.</summary>
    public void SetStrength(float strength, bool kickOnChange = true)
    {
        strength = Mathf.Clamp01(strength);
        if (Mathf.Approximately(strength, Strength)) return;
        Strength = strength;

        if (!kickOnChange || strength <= 0f || !isActiveAndEnabled) return;

        float shaped = Shaped(strength);
        kick = 1f;
        // Jump straight to the new strength, so the kick is felt now and not after the blend.
        amount = Mathf.Max(amount, shaped);

        if (maxPunchScale > 0f && shaped > 0f)
        {
            RememberRest();
            punchTween.Stop();
            transform.localScale = restScale;
            punchTween = Tween.PunchScale(transform, Vector3.one * (maxPunchScale * shaped), 0.3f);
        }
    }

    private void Update()
    {
        float target = Shaped(Strength);
        amount = blendTime > 0f ? Mathf.MoveTowards(amount, target, Time.deltaTime / blendTime) : target;
        kick = Mathf.MoveTowards(kick, 0f, Time.deltaTime / kickDuration);

        if (amount <= 0f && kick <= 0f)
        {
            if (appliedOffset != Vector2.zero || appliedAngle != 0f) ApplyShake(Vector2.zero, 0f);
            return;
        }

        // Stronger shakes are also faster.
        time += Time.deltaTime * Mathf.Lerp(minFrequency, maxFrequency, amount);
        float power = amount * (1f + kick * kick * kickMultiplier);

        float x = Mathf.PerlinNoise(seed, time) * 2f - 1f;
        float y = Mathf.PerlinNoise(seed + 31.7f, time) * 2f - 1f;
        float r = Mathf.PerlinNoise(seed + 63.1f, time) * 2f - 1f;
        ApplyShake(new Vector2(x, y) * (maxOffset * power), r * maxRotation * power);
    }

    private float Shaped(float strength) => strength <= 0f ? 0f : Mathf.Max(0f, strengthCurve.Evaluate(strength));

    /// <summary>Adds only the change in shake, so the element's own position (layout, other tweens) is kept.</summary>
    private void ApplyShake(Vector2 offset, float angle)
    {
        if (!rect) rect = (RectTransform)transform;

        rect.anchoredPosition += offset - appliedOffset;
        appliedOffset = offset;

        Vector3 euler = rect.localEulerAngles;
        euler.z += angle - appliedAngle;
        rect.localEulerAngles = euler;
        appliedAngle = angle;
    }

    private void RememberRest()
    {
        if (restScaleKnown) return;
        restScale = transform.localScale;
        restScaleKnown = true;
    }
}
