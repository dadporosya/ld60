using UnityEngine;
using PrimeTween;

/// <summary>
/// Cinematic (letterbox) camera frame: four black bars that slide in from the screen edges to
/// narrow the visible picture, like a widescreen crop in a cutscene.
///
/// SETUP: put this on a full-screen stretched RectTransform under the Canvas (anchors 0..1, zero
/// offsets) and assign four child RectTransforms with black Images as the borders. Their anchors
/// are driven by this script, so their own anchoring/size in the inspector does not matter.
/// The borders should have Raycast Target off unless they are meant to block input.
///
/// COVERAGE UNITS: every value is a PERCENTAGE OF THE SCREEN.
/// - Per-border values (start values, <see cref="SetBorder"/>): the share of the screen that this
///   single border covers. 0 = fully off-screen, 50 = the border covers half the screen.
/// - <see cref="NarrowVertical"/> / <see cref="NarrowHorizontal"/> take the TOTAL coverage of the
///   pair: 0 = both borders out of screen, 100 = 50% each, so the whole screen is covered.
/// </summary>
[DisallowMultipleComponent]
public class CinematicCameraFrame : MonoBehaviour
{
    public static CinematicCameraFrame Instance;

    public enum Border { Bottom = 0, Top = 1, Left = 2, Right = 3 }

    [Header("Borders")]
    [SerializeField] private RectTransform bottomBorder;
    [SerializeField] private RectTransform topBorder;
    [SerializeField] private RectTransform leftBorder;
    [SerializeField] private RectTransform rightBorder;

    [Header("Start values (% of the screen each border covers on Start; 50 = half the screen)")]
    [SerializeField, Range(0f, 100f)] private float bottomStart = 0f;
    [SerializeField, Range(0f, 100f)] private float topStart = 0f;
    [SerializeField, Range(0f, 100f)] private float leftStart = 0f;
    [SerializeField, Range(0f, 100f)] private float rightStart = 0f;

    [Header("Defaults (used by the single-argument, UnityEvent-friendly overloads)")]
    [SerializeField] private float defaultDuration = 0.5f;
    [SerializeField] private Ease defaultEase = Ease.OutCubic;

    [Tooltip("Animate on unscaled time. Cutscenes run pause-proof (Time.timeScale can be 0 or slowed " +
             "by screen effects), so the bars must not freeze with the game. Leave this on.")]
    [SerializeField] private bool useUnscaledTime = true;

    [Tooltip("Register as a global singleton so other systems can reach it as CinematicCameraFrame.Instance.")]
    [SerializeField] private bool registerInstance = true;

    /// <summary>Current coverage of each border, in % of the screen. Indexed by <see cref="Border"/>.</summary>
    private readonly float[] _coverage = new float[4];
    private readonly Tween[] _tweens = new Tween[4];

    private void Awake()
    {
        // NOTE: h.CreateStaticInstance is deliberately NOT used here. It destroys *every* copy when
        // it finds more than one in the scene, which would leave Instance null and the frame dead.
        // Not DontDestroyOnLoad either: the frame lives under a Canvas, so it is never a root object.
        if (!registerInstance) return;

        if (Instance && Instance != this)
        {
            h.Out($"CinematicCameraFrame: duplicate on '{name}' - destroying the extra component, keeping '{Instance.name}'.");
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        // Apply the start values instantly, before anything animates them.
        SetBorderInstant(Border.Bottom, bottomStart);
        SetBorderInstant(Border.Top, topStart);
        SetBorderInstant(Border.Left, leftStart);
        SetBorderInstant(Border.Right, rightStart);
    }

    // ------------------------------------------------------------------ public API

    /// <summary>
    /// Moves the bottom and top borders so together they cover <paramref name="amountPercent"/>%
    /// of the screen (0 = both out of screen, 100 = 50% each, whole screen covered).
    /// </summary>
    public void NarrowVertical(float amountPercent, float duration, Ease ease)
    {
        float perBorder = Mathf.Clamp(amountPercent, 0f, 100f) * 0.5f;
        SetBorder(Border.Bottom, perBorder, duration, ease);
        SetBorder(Border.Top, perBorder, duration, ease);
    }

    /// <summary>
    /// Moves the left and right borders so together they cover <paramref name="amountPercent"/>%
    /// of the screen (0 = both out of screen, 100 = 50% each, whole screen covered).
    /// </summary>
    public void NarrowHorizontal(float amountPercent, float duration, Ease ease)
    {
        float perBorder = Mathf.Clamp(amountPercent, 0f, 100f) * 0.5f;
        SetBorder(Border.Left, perBorder, duration, ease);
        SetBorder(Border.Right, perBorder, duration, ease);
    }

    /// <summary>Same, using the serialized default duration/easing (hookable from UnityEvents).</summary>
    public void NarrowVertical(float amountPercent) => NarrowVertical(amountPercent, defaultDuration, defaultEase);

    /// <summary>Same, using the serialized default duration/easing (hookable from UnityEvents).</summary>
    public void NarrowHorizontal(float amountPercent) => NarrowHorizontal(amountPercent, defaultDuration, defaultEase);

    /// <summary>Narrows both pairs at once.</summary>
    public void Narrow(float amountPercent, float duration, Ease ease)
    {
        NarrowVertical(amountPercent, duration, ease);
        NarrowHorizontal(amountPercent, duration, ease);
    }

    /// <summary>Slides every border back out of the screen.</summary>
    public void OpenAll(float duration, Ease ease) => Narrow(0f, duration, ease);

    /// <summary>Slides every border back out of the screen, using the default duration/easing.</summary>
    public void OpenAll() => OpenAll(defaultDuration, defaultEase);

    /// <summary>
    /// Animates one border to <paramref name="percentOfScreen"/>% of the screen
    /// (0 = off-screen, 50 = half the screen, 100 = the whole screen).
    /// </summary>
    public void SetBorder(Border border, float percentOfScreen, float duration, Ease ease)
    {
        percentOfScreen = Mathf.Clamp(percentOfScreen, 0f, 100f);

        int i = (int)border;
        _tweens[i].Stop();

        if (duration <= 0f || !GetRect(border))
        {
            SetBorderInstant(border, percentOfScreen);
            return;
        }

        _tweens[i] = Tween.Custom(_coverage[i], percentOfScreen, duration,
            value => Apply(border, value), ease, useUnscaledTime: useUnscaledTime);
    }

    /// <summary>Snaps one border to <paramref name="percentOfScreen"/>% of the screen, no animation.</summary>
    public void SetBorderInstant(Border border, float percentOfScreen)
    {
        _tweens[(int)border].Stop();
        Apply(border, Mathf.Clamp(percentOfScreen, 0f, 100f));
    }

    /// <summary>Snaps both pairs, no animation (e.g. to prepare a cutscene before it is shown).</summary>
    public void SetInstant(float verticalPercent, float horizontalPercent)
    {
        float v = Mathf.Clamp(verticalPercent, 0f, 100f) * 0.5f;
        float hor = Mathf.Clamp(horizontalPercent, 0f, 100f) * 0.5f;
        SetBorderInstant(Border.Bottom, v);
        SetBorderInstant(Border.Top, v);
        SetBorderInstant(Border.Left, hor);
        SetBorderInstant(Border.Right, hor);
    }

    /// <summary>Current coverage of a single border, in % of the screen.</summary>
    public float GetBorderCoverage(Border border) => _coverage[(int)border];

    /// <summary>Current total coverage of the bottom+top pair, in % of the screen.</summary>
    public float VerticalCoverage => _coverage[(int)Border.Bottom] + _coverage[(int)Border.Top];

    /// <summary>Current total coverage of the left+right pair, in % of the screen.</summary>
    public float HorizontalCoverage => _coverage[(int)Border.Left] + _coverage[(int)Border.Right];

    // ------------------------------------------------------------------ internals

    /// <summary>
    /// Writes the coverage into the border's anchors. Anchors (not sizeDelta) are used so the frame
    /// is resolution- and aspect-independent: a fraction of the parent rect is a fraction of the
    /// screen whatever the canvas scaler does.
    /// </summary>
    private void Apply(Border border, float percentOfScreen)
    {
        _coverage[(int)border] = percentOfScreen;

        RectTransform rect = GetRect(border);
        if (!rect) return;

        float f = percentOfScreen / 100f;

        switch (border)
        {
            case Border.Bottom:
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, f);
                break;
            case Border.Top:
                rect.anchorMin = new Vector2(0f, 1f - f);
                rect.anchorMax = new Vector2(1f, 1f);
                break;
            case Border.Left:
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(f, 1f);
                break;
            case Border.Right:
                rect.anchorMin = new Vector2(1f - f, 0f);
                rect.anchorMax = new Vector2(1f, 1f);
                break;
        }

        // The anchors alone define the rect: kill any leftover offsets/size from the prefab.
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    private RectTransform GetRect(Border border)
    {
        switch (border)
        {
            case Border.Bottom: return bottomBorder;
            case Border.Top: return topBorder;
            case Border.Left: return leftBorder;
            case Border.Right: return rightBorder;
        }
        return null;
    }

    private void OnDestroy()
    {
        for (int i = 0; i < _tweens.Length; i++) _tweens[i].Stop();
        if (Instance == this) Instance = null;
    }
}
