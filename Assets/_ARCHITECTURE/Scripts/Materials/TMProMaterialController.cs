using UnityEngine;
using TMPro;
using PrimeTween;

/// <summary>
/// Controls the material of a TextMeshPro text and lets other code modulate its parameters
/// at runtime. Works on an instanced material (fontMaterial) so the shared material asset
/// is never modified.
/// </summary>
public class TMProMaterialController : MonoBehaviour
{
    [SerializeField] private TMP_Text text;

    private Material material;
    private Material originalSharedMaterial;

    private void Awake()
    {
        if (!text) text = GetComponent<TMP_Text>();
        if (!text) text = GetComponentInChildren<TMP_Text>();
        if (!text) return;

        // Cache the shared asset before instancing so ResetMaterial can restore it.
        originalSharedMaterial = text.fontSharedMaterial;
        // fontMaterial returns a per-instance copy, so edits don't leak to the shared asset.
        material = text.fontMaterial;
    }

    /// <summary>The instanced material being controlled, or null if no text was found.</summary>
    public Material Material => material;

    // --- Generic parameter setters -------------------------------------------------

    public void SetFloat(string property, float value)
    {
        if (material) material.SetFloat(property, value);
    }

    public void SetFloat(int propertyId, float value)
    {
        if (material) material.SetFloat(propertyId, value);
    }

    public void SetColor(string property, Color value)
    {
        if (material) material.SetColor(property, value);
    }

    public void SetColor(int propertyId, Color value)
    {
        if (material) material.SetColor(propertyId, value);
    }

    public void SetVector(string property, Vector4 value)
    {
        if (material) material.SetVector(property, value);
    }

    public void SetVector(int propertyId, Vector4 value)
    {
        if (material) material.SetVector(propertyId, value);
    }

    public void SetTexture(string property, Texture value)
    {
        if (material) material.SetTexture(property, value);
    }

    public void SetTexture(int propertyId, Texture value)
    {
        if (material) material.SetTexture(propertyId, value);
    }

    // --- Common TextMeshPro shader shortcuts ---------------------------------------

    public void SetFaceColor(Color color) => SetColor(ShaderUtilities.ID_FaceColor, color);

    public void SetFaceDilate(float dilate) => SetFloat(ShaderUtilities.ID_FaceDilate, dilate);

    public void SetOutlineColor(Color color) => SetColor(ShaderUtilities.ID_OutlineColor, color);

    public void SetOutlineWidth(float width) => SetFloat(ShaderUtilities.ID_OutlineWidth, width);

    public void SetGlowColor(Color color) => SetColor(ShaderUtilities.ID_GlowColor, color);

    public void SetGlowPower(float power) => SetFloat(ShaderUtilities.ID_GlowPower, power);

    /// <summary>Restores the text back to its original shared material.</summary>
    public void ResetMaterial()
    {
        if (!text || !originalSharedMaterial) return;
        text.fontSharedMaterial = originalSharedMaterial;
        material = text.fontMaterial;
    }

    // --- Glow flickering -----------------------------------------------------------

    private Tween glowTween;

    /// <summary>
    /// Continuously modulates the Glow power from min to max and back to min every
    /// <paramref name="period"/> seconds (looping until stopped).
    /// </summary>
    public void StartGlowingFlickering(float period, float min = 0f, float max = 1f)
    {
        if (!material) return;

        glowTween.Stop();
        // A full min -> max -> min cycle spans one period, so each leg takes period / 2.
        glowTween = Tween.Custom(
            min,
            max,
            period * 0.5f,
            value => SetGlowPower(value),
            cycles: -1,
            cycleMode: CycleMode.Yoyo);
    }

    /// <summary>
    /// Stops the flickering by modulating the Glow power to <paramref name="targetValue"/>
    /// over <paramref name="fadeOutTime"/> seconds.
    /// </summary>
    public void StopGlowingFlickering(float fadeOutTime, float targetValue = 0f)
    {
        glowTween.Stop();
        if (!material) return;

        float current = material.GetFloat(ShaderUtilities.ID_GlowPower);
        glowTween = Tween.Custom(
            current,
            targetValue,
            fadeOutTime,
            value => SetGlowPower(value));
    }
}
