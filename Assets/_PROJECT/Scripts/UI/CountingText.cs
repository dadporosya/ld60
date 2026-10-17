using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A number that counts to its new value instead of jumping: 10 - 11 - 12 - 13 ... (or down), shaking
/// while it counts. Put it on a TextMeshProUGUI and call <see cref="SetValue"/>.
///
/// <para>Small changes tick one by one every <see cref="stepDuration"/>; big ones are squeezed into
/// <see cref="maxDuration"/>, so they skip numbers. The shake is applied as an offset on top of the
/// text's own position and rotation, so it works inside layout groups and goes back to rest after.</para>
/// </summary>
[RequireComponent(typeof(TextMeshProUGUI))]
public class CountingText : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI text;
    [Tooltip("{0} = the number.")]
    [SerializeField] private string format = "{0}";

    [Header("Counting")]
    [Tooltip("Seconds per number.")]
    [SerializeField, Min(0.001f)] private float stepDuration = 0.04f;
    [Tooltip("Longest a count may take; bigger changes skip numbers to fit.")]
    [SerializeField, Min(0f)] private float maxDuration = 0.8f;
    [SerializeField] private Ease ease = Ease.OutQuad;
    [Tooltip("Optional tick played on every number shown (through the SFXManager).")]
    [SerializeField] private AudioClip tickSfx;
    [SerializeField, Range(0f, 1f)] private float tickVolume = 0.5f;

    [Header("Shake while counting")]
    [SerializeField] private bool shake = true;
    [Tooltip("How far the text jitters (UI units).")]
    [SerializeField, Min(0f)] private float shakeStrength = 4f;
    [Tooltip("Degrees the text wobbles either way.")]
    [SerializeField, Min(0f)] private float shakeRotation = 3f;
    [Tooltip("Jitters per second.")]
    [SerializeField, Min(1f)] private float shakeFrequency = 30f;
    [Tooltip("Seconds the shake takes to settle once the count is done.")]
    [SerializeField, Min(0f)] private float shakeFadeOut = 0.2f;
    [Tooltip("Scale punch when the count starts. 0: none.")]
    [SerializeField, Min(0f)] private float punchScale = 0.15f;

    [Header("Tint while counting")]
    [SerializeField] private bool tintWhileCounting = false;
    [SerializeField] private Color countingUpColor = new Color(0.6f, 1f, 0.6f);
    [SerializeField] private Color countingDownColor = new Color(1f, 0.55f, 0.55f);

    [Header("Events")]
    /// <summary>Every number shown on the way.</summary>
    public UnityEvent<int> onStep = new UnityEvent<int>();
    public UnityEvent onCountFinished = new UnityEvent();

    /// <summary>How the number is written ({0} = the number).</summary>
    public string Format
    {
        get => format;
        set
        {
            format = string.IsNullOrEmpty(value) ? "{0}" : value;
            if (text && !string.IsNullOrEmpty(text.text)) text.text = string.Format(format, Displayed);
        }
    }

    /// <summary>The value being counted to (or shown).</summary>
    public int Value { get; private set; }

    /// <summary>The number on screen right now.</summary>
    public int Displayed { get; private set; }

    public bool IsCounting => countTween.isAlive;

    /// <summary>The text's resting colour: shown whenever it isn't tinted for a count.</summary>
    public Color BaseColor
    {
        get
        {
            RememberRest();
            return baseColor;
        }
        set
        {
            RememberRest();
            baseColor = value;
            baseColorKnown = true;
            if (text && !(tintWhileCounting && IsCounting)) text.color = value;
        }
    }

    private RectTransform rect;
    private Color baseColor;
    private bool baseColorKnown;
    private Tween countTween;
    private Tween punchTween;

    // Shake state: what is currently added to the text's own pose.
    private float shakeAmount;
    private float shakeTime;
    private float shakeSeed;
    private Vector2 appliedOffset;
    private float appliedAngle;
    private Vector3 restScale;
    private bool restScaleKnown;

    private void Reset() => text = GetComponent<TextMeshProUGUI>();

    private void Awake()
    {
        if (!text) text = GetComponent<TextMeshProUGUI>();
        rect = transform as RectTransform;
        RememberRest();
        shakeSeed = Random.value * 100f;
    }

    private void OnDisable()
    {
        countTween.Stop();
        punchTween.Stop();
        Displayed = Value;
        shakeAmount = 0f;
        ApplyShake(Vector2.zero, 0f);
        if (restScaleKnown) transform.localScale = restScale;
        if (baseColorKnown && text) text.color = baseColor;
    }

    /// <summary>Counts from the number on screen to <paramref name="value"/>. Instant when <paramref name="animate"/> is off.</summary>
    public void SetValue(int value, bool animate = true)
    {
        if (!text) text = GetComponent<TextMeshProUGUI>();
        RememberRest();

        // Already there (or on its way). An emptied text still gets the number written.
        bool blank = text && string.IsNullOrEmpty(text.text);
        if (value == Value && (IsCounting || !blank)) return;

        Value = value;
        countTween.Stop();

        int from = Displayed;
        if (!animate || from == value || !isActiveAndEnabled)
        {
            Show(value);
            Finish();
            return;
        }

        float duration = Mathf.Abs(value - from) * stepDuration;
        if (maxDuration > 0f) duration = Mathf.Min(duration, maxDuration);

        if (tintWhileCounting && text) text.color = value > from ? countingUpColor : countingDownColor;
        if (punchScale > 0f && restScaleKnown)
        {
            punchTween.Stop();
            transform.localScale = restScale;
            punchTween = Tween.PunchScale(transform, Vector3.one * punchScale, Mathf.Max(0.1f, Mathf.Min(duration, 0.3f)));
        }
        if (shake) shakeAmount = 1f;

        countTween = Tween.Custom(this, (float)from, value, duration, (counter, v) => counter.Step(v), ease)
            .OnComplete(this, counter =>
            {
                counter.Show(counter.Value);
                counter.Finish();
            });
    }

    /// <summary>Shows <paramref name="value"/> at once, no count.</summary>
    public void SetInstant(int value) => SetValue(value, animate: false);

    /// <summary>Empties the text. The next value counts up from <paramref name="countFrom"/>.</summary>
    public void Clear(int countFrom = 0)
    {
        countTween.Stop();
        Value = countFrom;
        Displayed = countFrom;
        shakeAmount = 0f;
        if (text) text.text = "";
        if (baseColorKnown && text) text.color = baseColor;
    }

    private void Update()
    {
        if (shakeAmount <= 0f && appliedOffset == Vector2.zero && appliedAngle == 0f) return;

        // Full strength while counting, then it settles.
        if (!IsCounting)
            shakeAmount = shakeFadeOut > 0f ? Mathf.MoveTowards(shakeAmount, 0f, Time.deltaTime / shakeFadeOut) : 0f;

        shakeTime += Time.deltaTime * shakeFrequency;
        float x = Mathf.PerlinNoise(shakeSeed, shakeTime) * 2f - 1f;
        float y = Mathf.PerlinNoise(shakeSeed + 31.7f, shakeTime) * 2f - 1f;
        float r = Mathf.PerlinNoise(shakeSeed + 63.1f, shakeTime) * 2f - 1f;

        ApplyShake(new Vector2(x, y) * (shakeStrength * shakeAmount), r * shakeRotation * shakeAmount);
    }

    private void Step(float value)
    {
        int rounded = Mathf.RoundToInt(value);
        if (rounded != Displayed) Show(rounded);
    }

    private void Show(int value)
    {
        bool changed = value != Displayed || (text && string.IsNullOrEmpty(text.text));
        Displayed = value;
        if (text) text.text = string.Format(format, value);
        if (!changed) return;

        if (tickSfx && SFXManager.Instance && IsCounting) SFXManager.Instance.PlayClip(tickSfx, tickVolume);
        onStep?.Invoke(value);
    }

    private void Finish()
    {
        if (baseColorKnown && text) text.color = baseColor;
        onCountFinished?.Invoke();
    }

    /// <summary>Adds only the change in shake, so the text's own position (layout, other tweens) is kept.</summary>
    private void ApplyShake(Vector2 offset, float angle)
    {
        if (!rect) rect = transform as RectTransform;
        if (!rect) return;

        rect.anchoredPosition += offset - appliedOffset;
        appliedOffset = offset;

        Vector3 euler = rect.localEulerAngles;
        euler.z += angle - appliedAngle;
        rect.localEulerAngles = euler;
        appliedAngle = angle;
    }

    private void RememberRest()
    {
        if (!restScaleKnown)
        {
            restScale = transform.localScale;
            restScaleKnown = true;
        }

        if (!baseColorKnown && text)
        {
            baseColor = text.color;
            baseColorKnown = true;
        }
    }
}
