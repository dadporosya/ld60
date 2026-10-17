using PrimeTween;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Turns the cursor into a flashlight: the scene is covered in darkness except for a soft circle of light
/// around the pointer, with an optional warm glow in the middle.
///
/// <para>UI: put it on a GameObject under the Canvas that holds the objects to light, as the LAST child so it
/// draws over them (canvases drawn later, e.g. dialogue, stay unaffected). It builds two
/// full-canvas RawImages at runtime (darkness + glow); neither is a raycast target, so clicks go through.
/// UI Images are unlit, so a Light can't reach them — this fakes it with the default UI shader.</para>
/// </summary>
[DisallowMultipleComponent]
public class PointLightManager : MonoBehaviour
{
    public static PointLightManager Instance;

    [Header("Light")]
    [SerializeField] private bool lightOn = true;
    [Tooltip("Radius of the lit circle, in canvas units.")]
    [SerializeField, Min(1f)] private float radius = 260f;
    [Tooltip("Share of the radius that is fully lit; the rest fades into darkness.")]
    [SerializeField, Range(0f, 0.95f)] private float core = 0.35f;
    [Tooltip("Colour and opacity of the unlit area.")]
    [SerializeField] private Color darkness = new Color(0f, 0f, 0.02f, 0.88f);
    [Tooltip("Glow over the lit area (alpha = strength, 0 = none).")]
    [SerializeField] private Color glow = new Color(1f, 0.85f, 0.55f, 0.18f);
    [Tooltip("Glow radius relative to the light radius.")]
    [SerializeField, Range(0.1f, 1.5f)] private float glowScale = 0.8f;

    [Header("Follow")]
    [Tooltip("Seconds for the light to catch up with the cursor (0 = glued to it).")]
    [SerializeField, Min(0f)] private float followSmoothing = 0.06f;
    [Tooltip("Shrinks the light away while the cursor is outside the game window.")]
    [SerializeField] private bool hideOffScreen = true;

    [Header("Flicker")]
    [SerializeField, Range(0f, 0.3f)] private float radiusFlicker = 0.03f;
    [SerializeField, Range(0f, 0.5f)] private float glowFlicker = 0.15f;
    [SerializeField, Min(0f)] private float flickerSpeed = 6f;

    [Header("Fade")]
    [SerializeField, Min(0f)] private float fadeDuration = 0.35f;

    private const int TextureSize = 256;

    private Canvas canvas;
    private RectTransform rect;
    private RawImage darknessImage;
    private RawImage glowImage;
    private Texture2D darknessTexture;
    private Texture2D glowTexture;
    private float builtCore = -1f;

    private Vector2 lightPos;
    private Vector2 lightVelocity;
    private bool hasPos;
    private float visibility;   // 0..1, eased by on/off fades and the off-screen hide
    private float offScreenScale = 1f;
    private float radiusMultiplier = 1f;
    private Tween fadeTween;
    private Tween radiusTween;

    public bool IsOn => lightOn;

    /// <summary>Light position in screen pixels (smoothed).</summary>
    public Vector2 ScreenPosition { get; private set; }

    public float Radius
    {
        get => radius;
        set => radius = Mathf.Max(1f, value);
    }

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance, false);

        canvas = GetComponentInParent<Canvas>();
        if (!canvas)
        {
            h.Out($"PointLightManager '{name}': not under a Canvas, the flashlight overlay can't be drawn.");
            enabled = false;
            return;
        }

        rect = GetComponent<RectTransform>();
        if (!rect) rect = gameObject.AddComponent<RectTransform>();
        Stretch(rect);

        darknessImage = CreateLayer("Darkness");
        glowImage = CreateLayer("Glow");
        RebuildTextures();

        visibility = lightOn ? 1f : 0f;
    }

    private void OnDestroy()
    {
        fadeTween.Stop();
        radiusTween.Stop();
        if (darknessTexture) Destroy(darknessTexture);
        if (glowTexture) Destroy(glowTexture);
        if (Instance == this) Instance = null;
    }

    private void OnValidate()
    {
        if (Application.isPlaying && darknessImage && !Mathf.Approximately(builtCore, core)) RebuildTextures();
    }

    private void LateUpdate()
    {
        bool hasPointer = TryGetPointerPosition(out Vector2 pointer);
        bool onScreen = hasPointer && IsOnScreen(pointer);

        if (hasPointer)
        {
            if (!hasPos || followSmoothing <= 0f) lightPos = pointer;
            else lightPos = Vector2.SmoothDamp(lightPos, pointer, ref lightVelocity, followSmoothing,
                Mathf.Infinity, Time.unscaledDeltaTime);
            hasPos = true;
        }

        float hideTarget = !hideOffScreen || onScreen ? 1f : 0f;
        offScreenScale = Mathf.MoveTowards(offScreenScale, hideTarget,
            Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeDuration));

        ScreenPosition = lightPos;
        Apply();
    }

    private void Apply()
    {
        float t = Time.unscaledTime * flickerSpeed;
        float radiusNoise = 1f + (Mathf.PerlinNoise(t, 0.3f) - 0.5f) * 2f * radiusFlicker;
        float glowNoise = 1f + (Mathf.PerlinNoise(0.7f, t) - 0.5f) * 2f * glowFlicker;

        // Off, the darkness stays and the light circle closes; it isn't a "lights on" switch.
        float lit = visibility * offScreenScale;
        float r = radius * radiusMultiplier * radiusNoise * lit;

        Vector2 local = Vector2.zero;
        bool inside = hasPos && RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, lightPos,
            canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out local);

        if (!inside || r < 0.5f)
        {
            darknessImage.uvRect = new Rect(-10f, -10f, 0.01f, 0.01f); // samples the dark clamped edge
            glowImage.enabled = false;
        }
        else
        {
            darknessImage.uvRect = CircleUv(local, r);
            glowImage.enabled = glow.a > 0f;
            glowImage.uvRect = CircleUv(local, r * glowScale);
        }

        darknessImage.color = darkness;
        Color g = glow;
        g.a = Mathf.Clamp01(glow.a * glowNoise * lit);
        glowImage.color = g;
    }

    /// <summary>uvRect that maps the texture onto a circle of radius <paramref name="r"/> at <paramref name="center"/>
    /// (rect-local); outside it the clamped edge pixels fill the rest of the rect.</summary>
    private Rect CircleUv(Vector2 center, float r)
    {
        Rect area = rect.rect;
        float d = r * 2f;
        Vector2 fromCorner = center - area.min;
        return new Rect(-(fromCorner.x - r) / d, -(fromCorner.y - r) / d, area.width / d, area.height / d);
    }

    /// <summary>Turns the flashlight on or off (the darkness stays; the light circle opens/closes).</summary>
    public void SetLight(bool on, bool instant = false)
    {
        lightOn = on;
        fadeTween.Stop();
        float target = on ? 1f : 0f;
        if (instant || fadeDuration <= 0f) visibility = target;
        else fadeTween = Tween.Custom(this, visibility, target, fadeDuration, (m, v) => m.visibility = v,
            Ease.OutQuad, useUnscaledTime: true);
    }

    public void Toggle() => SetLight(!lightOn);

    /// <summary>Shows or hides the whole effect (darkness included).</summary>
    public void SetOverlayVisible(bool visible)
    {
        if (darknessImage) darknessImage.gameObject.SetActive(visible);
        if (glowImage) glowImage.gameObject.SetActive(visible);
    }

    /// <summary>Scales the radius over time (e.g. 1.5 to widen the beam, 1 to restore).</summary>
    public void TweenRadius(float multiplier, float duration)
    {
        radiusTween.Stop();
        if (duration <= 0f) radiusMultiplier = multiplier;
        else radiusTween = Tween.Custom(this, radiusMultiplier, multiplier, duration, (m, v) => m.radiusMultiplier = v,
            Ease.OutQuad, useUnscaledTime: true);
    }

    private RawImage CreateLayer(string layerName)
    {
        var go = new GameObject(layerName, typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(transform, false);
        Stretch((RectTransform)go.transform);

        var image = go.GetComponent<RawImage>();
        image.raycastTarget = false;
        return image;
    }

    private static void Stretch(RectTransform t)
    {
        t.anchorMin = Vector2.zero;
        t.anchorMax = Vector2.one;
        t.offsetMin = Vector2.zero;
        t.offsetMax = Vector2.zero;
        t.localScale = Vector3.one;
    }

    private void RebuildTextures()
    {
        if (darknessTexture) Destroy(darknessTexture);
        if (glowTexture) Destroy(glowTexture);

        // Darkness: clear inside the core, fading to opaque well before the edge so the clamped border is solid.
        darknessTexture = BuildRadialTexture("FlashlightDarkness", d => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(core, 0.92f, d)));
        // Glow: strongest in the centre, gone before the edge so the clamped border is clear.
        glowTexture = BuildRadialTexture("FlashlightGlow", d => Mathf.Pow(1f - Mathf.Clamp01(d / 0.92f), 2f));

        darknessImage.texture = darknessTexture;
        glowImage.texture = glowTexture;
        builtCore = core;
    }

    /// <summary>White texture whose alpha is <paramref name="alpha"/>(distance from centre, 0..1 at the edge).</summary>
    private static Texture2D BuildRadialTexture(string textureName, System.Func<float, float> alpha)
    {
        var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
        {
            name = textureName,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        var pixels = new Color32[TextureSize * TextureSize];
        float half = (TextureSize - 1) * 0.5f;
        for (int y = 0; y < TextureSize; y++)
        for (int x = 0; x < TextureSize; x++)
        {
            float dx = (x - half) / half;
            float dy = (y - half) / half;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(d)) * 255f));
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private static bool TryGetPointerPosition(out Vector2 position)
    {
        if (Mouse.current != null)
        {
            position = Mouse.current.position.ReadValue();
            return true;
        }

        if (Pointer.current != null)
        {
            position = Pointer.current.position.ReadValue();
            return true;
        }

        position = Vector2.zero;
        return false;
    }

    private static bool IsOnScreen(Vector2 p) =>
        p.x >= 0f && p.x <= Screen.width && p.y >= 0f && p.y <= Screen.height;
}
