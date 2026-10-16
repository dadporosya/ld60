using UnityEngine;
using UnityEngine.UI;
using PrimeTween;

/// <summary>
/// Fades a UI <see cref="Image"/> in/out by tweening the "_Dissolve_amount" property of its
/// MasterMatUI material directly (not through <see cref="MasterMaterialController"/>).
/// 0 = fully visible, 1 = fully dissolved.
/// </summary>
public class DissolveFadeImageController : MonoBehaviour
{
    private const string DissolveAmountProp = "_Dissolve_amount";
    private const string DissolveToggleProp = "_Disslove";

    public Image image;
    public static DissolveFadeImageController Instance;

    [SerializeField] private bool enableStartFadeOut = true;
    [SerializeField] private float startFadeDurationOut = 1.67f;

    private Material _mat;
    private Tween _fadeTween;

    private void Awake()
    {
        if (!image) image = GetComponent<Image>();
        EnsureMaterial();
    }

    private void Start()
    {
        h.CreateStaticInstance(this, ref Instance);

        if (enableStartFadeOut)
        {
            FadeIn(0);
            Fadeout(startFadeDurationOut);
        }
    }

    // Instances the material so edits never touch the asset on disk, and enables the
    // dissolve toggle so the amount actually shows.
    private void EnsureMaterial()
    {
        if (!image) image = GetComponent<Image>();
        if (image == null || image.material == null) return;

        _mat = new Material(image.material);
        image.material = _mat;
        _mat.SetFloat(DissolveToggleProp, 1f);
    }

    /// <summary>Modulates dissolve to 0 (fully visible).</summary>
    public Tween FadeIn(float duration, Ease easing = Ease.Default)
    {
        if (_mat == null) EnsureMaterial();
        _fadeTween.Stop();
        _fadeTween = Tween.Custom(_mat.GetFloat(DissolveAmountProp), 0f, duration,
            val => _mat.SetFloat(DissolveAmountProp, val), easing);
        return _fadeTween;
    }

    /// <summary>Modulates dissolve to 1 (fully dissolved).</summary>
    public Tween Fadeout(float duration, Ease easing = Ease.Default)
    {
        if (_mat == null) EnsureMaterial();
        _fadeTween.Stop();
        _fadeTween = Tween.Custom(_mat.GetFloat(DissolveAmountProp), 1f, duration,
            val => _mat.SetFloat(DissolveAmountProp, val), easing);
        return _fadeTween;
    }
}
