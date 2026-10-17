using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Ambient light for a UI room: multiplies everything drawn before it by a vertical gradient with darkened
/// corners (white = untouched, darker = shadow). Put it after the room and its objects, before any
/// <see cref="RoomLight"/>s, which then brighten the areas they hit.
/// </summary>
public class RoomAmbientLight : RoomLightLayer
{
    private const int TextureSize = 64;

    [Header("Gradient")]
    [Tooltip("Light at the top of the area (where the light comes from).")]
    [SerializeField] private Color top = new Color(0.9f, 0.84f, 0.58f);
    [Tooltip("Light at the bottom of the area.")]
    [SerializeField] private Color bottom = new Color(0.4f, 0.4f, 0.22f);
    [Tooltip("Above 1 the bottom shade reaches higher, below 1 the top light reaches lower.")]
    [SerializeField, Range(0.2f, 4f)] private float curve = 1.3f;

    [Header("Corners")]
    [SerializeField] private Color cornerColor = new Color(0.22f, 0.22f, 0.1f);
    [SerializeField, Range(0f, 1f)] private float cornerStrength = 0.65f;
    [Tooltip("Distance from the centre (0..1) where corner darkening starts.")]
    [SerializeField, Range(0f, 1f)] private float cornerStart = 0.45f;

    [Header("Layout")]
    [Tooltip("0 = no effect, 1 = full effect.")]
    [SerializeField, Range(0f, 1f)] private float strength = 1f;
    [Tooltip("Keep the layer stretched over its parent.")]
    [SerializeField] private bool stretchToParent = true;

    public float Strength
    {
        get => strength;
        set
        {
            strength = Mathf.Clamp01(value);
            if (image) image.color = new Color(1f, 1f, 1f, strength);
        }
    }

    protected override void ConfigureMaterial()
    {
        SetBlend(BlendMode.DstColor, BlendMode.Zero, true);
    }

    protected override void OnRebuilt()
    {
        if (stretchToParent && transform is RectTransform rt) Stretch(rt);
        image.color = new Color(1f, 1f, 1f, strength);
    }

    protected override void BuildTexture()
    {
        Texture2D tex = GetTexture(TextureSize, TextureSize);
        var pixels = new Color[TextureSize * TextureSize];
        float last = TextureSize - 1;

        for (int y = 0; y < TextureSize; y++)
        {
            float v = y / last;
            Color row = Color.Lerp(bottom, top, Mathf.Pow(v, curve));
            for (int x = 0; x < TextureSize; x++)
            {
                float dx = x / last * 2f - 1f;
                float dy = v * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / Mathf.Sqrt(2f);
                float corner = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cornerStart, 1f, d)) * cornerStrength;
                Color c = row * Color.Lerp(Color.white, cornerColor, corner);
                c.a = 1f;
                pixels[y * TextureSize + x] = c;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false);
    }
}
