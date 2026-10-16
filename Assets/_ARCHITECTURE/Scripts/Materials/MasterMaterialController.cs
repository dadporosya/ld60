using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Drives the MasterMat shader properties on this object's renderer, and optionally on every
// renderer below it.
//
// Works with both worldspace renderers (SpriteRenderer/MeshRenderer/...) and UI Graphics
// (Image/RawImage/TMP): a UI Graphic is NOT a Renderer, so it has to be handled separately,
// and it needs the UI variant of the material (MasterMatUI) because the canvas shader carries
// the stencil/mask properties the regular MasterMat lacks. Both variants expose the same
// property names, so everything below this point is shared.
//
// EVERY OBJECT GETS ITS OWN MATERIAL. An earlier version interned materials by configuration so
// identically-configured objects could share one, which cut SetPass calls a lot — but it was
// wrong: forking a shared material back into a private one relies on Renderer.material cloning
// on read, and that does not reliably clone a renderer Unity already considers instanced. When
// it failed to clone, a per-object animated write (a death pixelation ramp, a hit flash) went
// straight into the material every other unit was sharing, and those units visibly picked up the
// wrong pixel resolution. Sharing is only safe here if nothing ever writes per-object values,
// and this controller exists precisely to write per-object values, so it stays per-object.
//
// What IS kept from that pass, because none of it is shared state:
//   * shader property names resolved to IDs once instead of hashed on every write
//   * the two source materials loaded once instead of once per renderer (the generated R
//     accessor calls Resources.Load on every property read, so this was 457+ loads at startup)
//   * SetHitBlend / SetPixelResolution writing the single float that changed rather than
//     re-uploading all 30 properties every frame of an animation
public class MasterMaterialController : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("Assign a MasterMat instance to every renderer below this one. Renderers that have their own MasterMaterialController (or sit under one that propagates) are left alone.")]
    [SerializeField] private bool assignMatToChildren=true;

    private Renderer _rend;
    private Graphic _graphic;
    private Material _mat;
    private readonly List<Material> _childMats = new();

    [Header("Brightness")]
    [SerializeField] private bool brightness;
    [SerializeField] private float brightnessAmount;

    [Header("Contrast")]
    [SerializeField] private bool contrast;
    [SerializeField] private float contrastAmount;

    [Header("Dissolve")]
    [SerializeField] private bool dissolve;
    [Tooltip("0 = fully visible, 1 = fully dissolved.")]
    [Range(0f, 1f)][SerializeField] private float dissolveAmount;
    [SerializeField] private float dissolveScale = 10f;
    [SerializeField] private float dissolveOutlineThickness = 0.1f;
    [SerializeField] private float dissolveTwirl;
    [SerializeField] private float dissolveTwirlStrength = 1f;
    [SerializeField] private Color dissolveEdgeColor = Color.white;

    [Header("Distortion")]
    [SerializeField] private bool distortion;
    [SerializeField] private float distortionScale = 10f;
    [SerializeField] private float distortionStrength = 1f;
    [SerializeField] private Color distortionDirection = new Color(0f, 1f, 0f, 0f);

    [Header("Emission")]
    [SerializeField] private bool emission;
    [SerializeField] private float emissionMult = 1.9f;

    [Header("Hit")]
    [SerializeField] private bool hitEffect;
    [SerializeField] private float hitBlend;
    [SerializeField] private float hitGlow = 5f;
    [SerializeField] private Color hitColor = Color.white;

    [Header("Hue Shift")]
    [SerializeField] private bool hue;
    [SerializeField] private float hueShiftAmount;
    [SerializeField] private bool hueShiftAnimation;
    [SerializeField] private float hueShiftAnimationSpeed;

    [Header("Negative")]
    [SerializeField] private bool negative;
    [SerializeField] private float negativeAmount;

    [Header("Pixelation")]
    [SerializeField] private bool pixelation;
    [SerializeField] private float pixelResolution = 16f;

    [Header("Saturation")]
    [SerializeField] private bool saturation;
    [SerializeField] private float saturationAmount;

    // ------------------------------------------------------------------ shader property IDs
    private static readonly int BrightnessId       = Shader.PropertyToID("_Brightness");
    private static readonly int BrightnessAmountId = Shader.PropertyToID("_Brightness_Amount");
    private static readonly int ContrastId         = Shader.PropertyToID("_Contrast");
    private static readonly int ContrastAmountId   = Shader.PropertyToID("_Contrast_Amount");
    private static readonly int DissolveToggleId   = Shader.PropertyToID("_Disslove");     // sic, as in the shader
    private static readonly int DissolveAmountId   = Shader.PropertyToID("_Dissolve_amount");
    private static readonly int DissolveScaleId    = Shader.PropertyToID("_DissloveScale");
    private static readonly int DissolveThickId    = Shader.PropertyToID("_DissloveOutlinesthickness");
    private static readonly int DissolveTwirlId    = Shader.PropertyToID("_DissloveTwirl");
    private static readonly int DissolveTwirlStrId = Shader.PropertyToID("_DissloveTwirlStrength");
    private static readonly int EdgeColorId        = Shader.PropertyToID("_EdgeColor");
    private static readonly int DistortionId       = Shader.PropertyToID("_Distortion");
    private static readonly int DistortionScaleId  = Shader.PropertyToID("_DistortionScale");
    private static readonly int DistortionStrId    = Shader.PropertyToID("_DistortionStrength");
    private static readonly int DistortionDirId    = Shader.PropertyToID("_Distortion_Direction");
    private static readonly int EmissionId         = Shader.PropertyToID("_Emission");
    private static readonly int EmissionMultId     = Shader.PropertyToID("_Emission_Mult");
    private static readonly int HitEffectId        = Shader.PropertyToID("_Hit_Effect");
    private static readonly int HitBlendId         = Shader.PropertyToID("_Hit_Blend");
    private static readonly int HitGlowId          = Shader.PropertyToID("_Hit_Glow");
    private static readonly int HitColorId         = Shader.PropertyToID("_Hit_Color");
    private static readonly int HueId              = Shader.PropertyToID("_Hue");
    private static readonly int HueShiftAmountId   = Shader.PropertyToID("_HueShiftAmount");
    private static readonly int HueShiftAnimId     = Shader.PropertyToID("_HueShiftAnimation");
    private static readonly int HueShiftSpeedId    = Shader.PropertyToID("_HueShiftAnimationSpeed");
    private static readonly int NegativeId         = Shader.PropertyToID("_Negative");
    private static readonly int NegativeAmountId   = Shader.PropertyToID("_Negative_Amount");
    private static readonly int PixelationId       = Shader.PropertyToID("_Pixelation");
    private static readonly int PixelResolutionId  = Shader.PropertyToID("_Pixel_Resolution");
    private static readonly int SaturationId       = Shader.PropertyToID("_Saturation");
    private static readonly int SaturationAmountId = Shader.PropertyToID("_Saturation_Amount");

    // ------------------------------------------------------------------ source materials
    // Cached only to avoid paying the Resources.Load on every read; the materials handed out below
    // are still fresh clones, one per renderer.
    public const string WorldMaterialPath = "ARCHITECTURE/Materials/MasterMat";
    public const string UiMaterialPath    = "ARCHITECTURE/Materials/MasterMatUI";

    private static Material _worldSource;
    private static Material _uiSource;

    private static Material WorldSource
    {
        get
        {
            if (_worldSource == null) _worldSource = Resources.Load<Material>(WorldMaterialPath);
            return _worldSource;
        }
    }

    /// <summary>The shared UI master material, loaded once. Null when it is missing from Resources.</summary>
    public static Material UiSource
    {
        get
        {
            if (_uiSource == null) _uiSource = Resources.Load<Material>(UiMaterialPath);
            return _uiSource;
        }
    }

    private void Awake()
    {
        _rend = GetComponent<Renderer>();
        // Only look for a Graphic when there is no Renderer: the two are mutually exclusive
        // in practice, and a Renderer takes priority if something ever has both.
        if (_rend == null) _graphic = GetComponent<Graphic>();

        if (_rend != null)
        {
            _mat = NewMat(false);
            _rend.material = _mat;
        }
        else if (_graphic != null)
        {
            _mat = NewMat(true);
            _graphic.material = _mat;
        }

        if (assignMatToChildren)
            SetupChildren();

        ApplyAll();
    }

    // ui = true picks the canvas variant of the shader. Both share property names.
    private static Material NewMat(bool ui)
    {
        Material source = ui ? UiSource : WorldSource;
        return source != null ? new Material(source) : null;
    }

    private void SetupChildren()
    {
        _childMats.Clear();

        // Renderer, not SpriteRenderer: models built from MeshRenderer/SkinnedMeshRenderer
        // parts were previously skipped entirely and never received the MasterMat.
        foreach (var rend in GetComponentsInChildren<Renderer>(true))
        {
            if (rend.gameObject == gameObject) continue;
            if (IsOwnedByNestedController(rend.transform)) continue;

            var childMat = NewMat(false);
            if (childMat == null) continue;
            rend.material = childMat;
            _childMats.Add(childMat);
        }

        // UI children. Graphic does not derive from Renderer, so the loop above never sees
        // Images/RawImages/TMP text — this is why the effects silently did nothing on UI
        // prefabs. These get the MasterMatUI variant instead.
        foreach (var graphic in GetComponentsInChildren<Graphic>(true))
        {
            if (graphic.gameObject == gameObject) continue;
            if (IsOwnedByNestedController(graphic.transform)) continue;

            var childMat = NewMat(true);
            if (childMat == null) continue;
            graphic.material = childMat;
            _childMats.Add(childMat);
        }
    }

    // True when some MasterMaterialController below this one already claims that renderer.
    // Without this check both controllers would assign their own new Material to the same
    // renderer in Awake (order is undefined), and the loser would keep writing properties to
    // a material that is no longer rendered — the effect silently does nothing.
    private bool IsOwnedByNestedController(Transform t)
    {
        for (var cur = t; cur != null && cur != transform; cur = cur.parent)
        {
            var nested = cur.GetComponent<MasterMaterialController>();
            if (nested == null) continue;
            // A nested controller always owns its own renderer, and owns the ones further
            // down only when it is set to propagate to its children.
            if (cur == t || nested.assignMatToChildren) return true;
        }
        return false;
    }

    // Writes one property to this object's own materials. Every material reached here belongs to
    // this object alone, so a per-frame animated write cannot touch anything else.
    private void SetLocalFloat(int id, float value)
    {
        if (_mat != null) _mat.SetFloat(id, value);
        for (int i = 0; i < _childMats.Count; i++)
            if (_childMats[i] != null) _childMats[i].SetFloat(id, value);
    }

    public void SetPixelation(bool value)
    {
        pixelation = value;
        ApplyAll();
    }

    /// <summary>
    /// Sets the pixelation toggle and resolution together. Used for spawn-time setup
    /// (see <see cref="SpawnMaterialDefaults"/>).
    /// </summary>
    public void ConfigurePixelation(bool enabled, float resolution)
    {
        pixelation = enabled;
        pixelResolution = resolution;
        ApplyAll();
    }

    // Fast path for per-frame animation: writes only the property that changed.
    public void SetPixelResolution(float value)
    {
        pixelResolution = value;
        SetLocalFloat(PixelResolutionId, value);
    }

    public void SetHitEffect(bool value)
    {
        hitEffect = value;
        ApplyAll();
    }

    // Fast path for per-frame animation: writes only the property that changed.
    public void SetHitBlend(float value)
    {
        hitBlend = value;
        SetLocalFloat(HitBlendId, value);
    }

    public void SetHitColor(Color value)
    {
        hitColor = value;
        ApplyAll();
    }

    public void SetDissolve(bool value)
    {
        dissolve = value;
        ApplyAll();
    }

    public float GetDissolveAmount() => dissolveAmount;

    // Fast path for per-frame animation: writes only the dissolve amount instead of
    // re-uploading every property through ApplyAll().
    public void SetDissolveAmount(float value)
    {
        dissolveAmount = value;
        SetLocalFloat(DissolveAmountId, value);
    }

    public void ApplyAll()
    {
        ApplyTo(_mat);
        foreach (var m in _childMats)
            ApplyTo(m);
    }

    private void ApplyTo(Material m)
    {
        if (m == null) return;

        m.SetFloat(BrightnessId, brightness ? 1f : 0f);
        m.SetFloat(BrightnessAmountId, brightnessAmount);

        m.SetFloat(ContrastId, contrast ? 1f : 0f);
        m.SetFloat(ContrastAmountId, contrastAmount);

        m.SetFloat(DissolveToggleId, dissolve ? 1f : 0f);
        m.SetFloat(DissolveAmountId, dissolveAmount);
        m.SetFloat(DissolveScaleId, dissolveScale);
        m.SetFloat(DissolveThickId, dissolveOutlineThickness);
        m.SetFloat(DissolveTwirlId, dissolveTwirl);
        m.SetFloat(DissolveTwirlStrId, dissolveTwirlStrength);
        m.SetColor(EdgeColorId, dissolveEdgeColor);

        m.SetFloat(DistortionId, distortion ? 1f : 0f);
        m.SetFloat(DistortionScaleId, distortionScale);
        m.SetFloat(DistortionStrId, distortionStrength);
        m.SetColor(DistortionDirId, distortionDirection);

        m.SetFloat(EmissionId, emission ? 1f : 0f);
        m.SetFloat(EmissionMultId, emissionMult);

        m.SetFloat(HitEffectId, hitEffect ? 1f : 0f);
        m.SetFloat(HitBlendId, hitBlend);
        m.SetFloat(HitGlowId, hitGlow);
        m.SetColor(HitColorId, hitColor);

        m.SetFloat(HueId, hue ? 1f : 0f);
        m.SetFloat(HueShiftAmountId, hueShiftAmount);
        m.SetFloat(HueShiftAnimId, hueShiftAnimation ? 1f : 0f);
        m.SetFloat(HueShiftSpeedId, hueShiftAnimationSpeed);

        m.SetFloat(NegativeId, negative ? 1f : 0f);
        m.SetFloat(NegativeAmountId, negativeAmount);

        m.SetFloat(PixelationId, pixelation ? 1f : 0f);
        m.SetFloat(PixelResolutionId, pixelResolution);

        m.SetFloat(SaturationId, saturation ? 1f : 0f);
        m.SetFloat(SaturationAmountId, saturationAmount);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_mat != null || _childMats.Count > 0)
            ApplyAll();
    }
#endif
}
