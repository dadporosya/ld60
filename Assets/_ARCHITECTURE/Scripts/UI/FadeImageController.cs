using UnityEngine;
using UnityEngine.UI;
using PrimeTween;

public class FadeImageController : MonoBehaviour
{
    public Image image;
    public static FadeImageController Instance;

    [Tooltip("Snap to opaque and fade out on Start. Other systems (e.g. intro cutscenes) can turn this off in Awake to own the fade themselves.")]
    public bool enableStartFadeOut = true;
    [SerializeField] private float startFadeDurationOut = 1.67f;
    private Tween _fadeTween;

    /// <summary>A fade (in, out, or the start fade-out) is still running.</summary>
    public bool IsFading => _fadeTween.isAlive;

    /// <summary>The image's current alpha: 0 fully transparent, 1 fully opaque.</summary>
    public float Alpha => image ? image.color.a : 0f;

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance);
        if (!image) image = GetComponent<Image>();
        if (image)
        {
            RectTransform rt = image.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(4000f, 4000f);
        }
    }

    private void Start()
    {
        

        if (enableStartFadeOut)
        {
            // Instantly snap to full alpha, then smoothly fade out to 0.
            SetAlpha(1f);
            Fadeout(startFadeDurationOut);
        }
    }

    /// <summary>Instantly sets the image's alpha.</summary>
    public void SetAlpha(float alpha)
    {
        if (!image) image = GetComponent<Image>();
        _fadeTween.Stop();
        Color c = image.color;
        c.a = alpha;
        image.color = c;
    }

    /// <summary>Modulates the image's alpha to fully opaque (1).</summary>
    public Tween FadeIn(float duration, Ease easing = Ease.Linear, bool useUnscaledTime = false)
    {
        if (!image) image = GetComponent<Image>();
        _fadeTween.Stop();
        _fadeTween = Tween.Alpha(image, 1f, duration, easing, useUnscaledTime: useUnscaledTime);
        return _fadeTween;
    }

    /// <summary>Modulates the image's alpha to fully transparent (0).</summary>
    public Tween Fadeout(float duration, Ease easing = Ease.Linear, bool useUnscaledTime = false)
    {
        if (!image) image = GetComponent<Image>();
        _fadeTween.Stop();
        _fadeTween = Tween.Alpha(image, 0f, duration, easing, useUnscaledTime: useUnscaledTime);
        return _fadeTween;
    }
}
