using UnityEngine;
using PrimeTween;

/// <summary>
/// Marks a RectTransform as a hole in the <see cref="ScreenHolesFader"/>: the black fader is cut
/// away wherever this rect is, so whatever is behind it is seen clearly.
///
/// SETUP: put this on any UI RectTransform (usually an empty RectTransform with no Image, sized and
/// anchored where the hole should be). It registers itself with the fader on enable and unregisters
/// on disable, so switching the GameObject on/off opens/closes the hole.
///
/// This component is OPTIONAL - the fader can also take plain RectTransforms in its "holes" list.
/// It only exists to give a hole its own corner radius / feather / padding / strength.
///
/// All sizes are in CANVAS UNITS (the same units as the RectTransform's width and height).
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class ScreenHole : MonoBehaviour
{
    [Header("Target fader (leave empty to use ScreenHolesFader.Instance)")]
    [SerializeField] private ScreenHolesFader fader;

    [Tooltip("Register with the fader automatically while this object is enabled.")]
    [SerializeField] private bool autoRegister = true;

    [Header("Shape (negative = use the fader's default)")]
    [Tooltip("Corner rounding of the hole, in canvas units. 0 = sharp rectangle.")]
    [SerializeField] private float cornerRadius = -1f;

    [Tooltip("Width of the soft edge on the inside of the hole, in canvas units. 1 = crisp but anti-aliased.")]
    [SerializeField] private float feather = -1f;

    [Tooltip("Grows (or shrinks, if negative) the hole on every side, in canvas units.")]
    [SerializeField] private float padding = 0f;

    [Header("State")]
    [Tooltip("0 = hole fully closed (fader is solid here), 1 = hole fully open. Tween it to animate the hole.")]
    [SerializeField, Range(0f, 1f)] private float strength = 1f;

    [Header("Gizmo")]
    [SerializeField] private bool drawGizmo = true;
    [SerializeField] private Color gizmoColor = new Color(0.2f, 1f, 0.6f, 0.9f);

    private RectTransform _rect;
    private Tween _strengthTween;

    /// <summary>The RectTransform that defines the hole.</summary>
    public RectTransform Rect => _rect ? _rect : _rect = (RectTransform)transform;

    /// <summary>Corner rounding in canvas units; negative means "use the fader default".</summary>
    public float CornerRadius { get => cornerRadius; set => cornerRadius = value; }

    /// <summary>Soft edge width in canvas units; negative means "use the fader default".</summary>
    public float Feather { get => feather; set => feather = value; }

    /// <summary>Extra size added to every side, in canvas units.</summary>
    public float Padding { get => padding; set => padding = value; }

    /// <summary>0 = closed (no hole), 1 = fully open. Set directly for an instant change.</summary>
    public float Strength
    {
        get => strength;
        set
        {
            _strengthTween.Stop();
            strength = Mathf.Clamp01(value);
        }
    }

    private void OnEnable()
    {
        _rect = (RectTransform)transform;
        if (autoRegister) Register();
    }

    private void OnDisable()
    {
        _strengthTween.Stop();
        Unregister();
    }

    /// <summary>Adds this hole to its fader (safe to call twice - the fader ignores duplicates).</summary>
    public void Register()
    {
        ScreenHolesFader target = ResolveFader();
        if (target) target.AddHole(Rect);
    }

    /// <summary>Removes this hole from its fader, so the fader becomes solid here again.</summary>
    public void Unregister()
    {
        ScreenHolesFader target = ResolveFader();
        if (target) target.RemoveHole(Rect);
    }

    /// <summary>Animates the hole open (strength 0 -&gt; 1).</summary>
    public Tween Open(float duration, Ease ease = Ease.OutCubic, bool useUnscaledTime = false) =>
        TweenStrength(1f, duration, ease, useUnscaledTime);

    /// <summary>Animates the hole closed (strength -&gt; 0), leaving the fader solid here.</summary>
    public Tween Close(float duration, Ease ease = Ease.OutCubic, bool useUnscaledTime = false) =>
        TweenStrength(0f, duration, ease, useUnscaledTime);

    /// <summary>Animates <see cref="Strength"/> to <paramref name="target"/> over <paramref name="duration"/>.</summary>
    public Tween TweenStrength(float target, float duration, Ease ease = Ease.OutCubic, bool useUnscaledTime = false)
    {
        _strengthTween.Stop();

        if (duration <= 0f)
        {
            strength = Mathf.Clamp01(target);
            return default;
        }

        _strengthTween = Tween.Custom(strength, Mathf.Clamp01(target), duration,
            value => strength = value, ease, useUnscaledTime: useUnscaledTime);
        return _strengthTween;
    }

    private ScreenHolesFader ResolveFader()
    {
        if (fader) return fader;
        if (Application.isPlaying && ScreenHolesFader.Instance) return ScreenHolesFader.Instance;

        // No singleton yet (or edit mode): look up the hierarchy first, then the whole scene, so the
        // hole also previews outside play mode and survives a bad script execution order.
        ScreenHolesFader found = GetComponentInParent<ScreenHolesFader>();
        if (found) return found;

        return FindFirstObjectByType<ScreenHolesFader>();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmo) return;

        Vector3[] corners = new Vector3[4];
        ((RectTransform)transform).GetWorldCorners(corners);

        Gizmos.color = gizmoColor;
        for (int i = 0; i < 4; i++) Gizmos.DrawLine(corners[i], corners[(i + 1) % 4]);
    }
}
