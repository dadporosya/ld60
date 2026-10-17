using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Cuts the <see cref="ScreenHolesFader"/>'s holes out of a UI element - the dialogue window, a panel: wherever
/// a hole is open the element turns transparent, so it never covers what the hole points at.
///
/// SETUP: put this on the element's root. Its Graphic (and, with <see cref="includeChildren"/>, every Graphic
/// below it) gets a <see cref="ScreenHoleCutoutTarget"/> that swaps their material for a hole-cutting copy at
/// render time. Supported: Images / RawImages on the default UI material (drawn with "UI/ScreenHoles") and
/// TextMeshPro text on "TextMeshPro/Distance Field" (drawn with "TextMeshPro/Distance Field ScreenHoles").
/// Graphics on any other shader are left as they are.
///
/// Untick <see cref="cutOutHoles"/> (or set <see cref="CutOutHoles"/>) to draw the element normally again.
/// </summary>
[DisallowMultipleComponent]
public class ScreenHoleCutout : MonoBehaviour
{
    public const string TextShaderName = "TextMeshPro/Distance Field ScreenHoles";
    private const string DefaultUIShaderName = "UI/Default";
    private const string DefaultTextShaderName = "TextMeshPro/Distance Field";

    [Tooltip("Cut the screen holes out of this element. Off: it draws as usual.")]
    [SerializeField] private bool cutOutHoles = true;

    [Tooltip("Leave empty to use ScreenHolesFader.Instance.")]
    [SerializeField] private ScreenHolesFader fader;

    [Tooltip("Also cut every Graphic below this object (its text, icons...). Off: only the Graphic on this " +
             "object fades away in the holes, and its children (e.g. a dialogue box's text) stay on top.")]
    [SerializeField] private bool includeChildren = false;

    [Tooltip("Fader alpha at which the cut is complete. Below it the cut fades with the fader, so a faded-out " +
             "fader cuts nothing. 0 = cut at full strength whatever the fader's alpha.")]
    [SerializeField, Min(0f)] private float fullCutAtFaderAlpha = 0.3f;

    [Header("Shaders (filled in automatically)")]
    [SerializeField] private Shader imageShader;
    [SerializeField] private Shader textShader;

    private readonly List<ScreenHoleCutoutTarget> targets = new List<ScreenHoleCutoutTarget>();
    private readonly Vector4[] holeData = new Vector4[ScreenHolesFader.MaxHoles];
    private readonly Vector4[] holeParams = new Vector4[ScreenHolesFader.MaxHoles];

    /// <summary>Whether the holes are cut out of this element. Off: it draws as usual.</summary>
    public bool CutOutHoles
    {
        get => cutOutHoles;
        set
        {
            if (cutOutHoles == value) return;
            cutOutHoles = value;
            RefreshMaterials();
        }
    }

    private ScreenHolesFader Fader => fader ? fader : ScreenHolesFader.Instance;

    private void Reset() => FindShaders();

    private void OnValidate()
    {
        FindShaders();
        if (Application.isPlaying && isActiveAndEnabled) RefreshMaterials();
    }

    private void OnEnable()
    {
        FindShaders();
        CollectTargets();
        RefreshMaterials();
    }

    private void OnDisable() => RefreshMaterials();

    // Graphics added below later (TMP fallback-font sub meshes, spawned icons) get cut too.
    private void OnTransformChildrenChanged()
    {
        if (!includeChildren) return;
        CollectTargets();
        RefreshMaterials();
    }

    private void LateUpdate()
    {
        if (!cutOutHoles) return;

        ScreenHolesFader source = Fader;
        float strength = source && source.isActiveAndEnabled ? FaderStrength(source) : 0f;

        for (int i = targets.Count - 1; i >= 0; i--)
        {
            ScreenHoleCutoutTarget target = targets[i];
            if (!target)
            {
                targets.RemoveAt(i);
                continue;
            }

            target.UploadHoles(strength > 0f ? source : null, strength, holeData, holeParams);
        }
    }

    /// <summary>Re-reads the Graphics to cut (call after adding Graphics below this object by hand).</summary>
    public void CollectTargets()
    {
        List<Graphic> graphics = new List<Graphic>();
        if (includeChildren) GetComponentsInChildren(true, graphics);
        else if (TryGetComponent(out Graphic own)) graphics.Add(own);

        foreach (Graphic graphic in graphics)
        {
            if (!graphic.TryGetComponent(out ScreenHoleCutoutTarget target))
                target = graphic.gameObject.AddComponent<ScreenHoleCutoutTarget>();

            target.Bind(this);
            if (!targets.Contains(target)) targets.Add(target);
        }
    }

    /// <summary>
    /// The hole-cutting shader for a material on <paramref name="shader"/>, or null when it can't be cut.
    /// A material already on a hole shader keeps it.
    /// </summary>
    public Shader CutShaderFor(Shader shader)
    {
        if (!shader) return null;
        if (shader == imageShader || shader == textShader) return shader;
        if (shader.name == DefaultUIShaderName) return imageShader;
        if (shader.name == DefaultTextShaderName) return textShader;
        return null;
    }

    /// <summary>True while targets should render with the cut material.</summary>
    public bool Cutting => cutOutHoles && isActiveAndEnabled;

    private void RefreshMaterials()
    {
        for (int i = targets.Count - 1; i >= 0; i--)
        {
            if (targets[i]) targets[i].Refresh();
            else targets.RemoveAt(i);
        }
    }

    private float FaderStrength(ScreenHolesFader source)
    {
        if (fullCutAtFaderAlpha <= 0f) return 1f;
        Image image = source.FaderImage;
        return image ? Mathf.Clamp01(image.color.a / fullCutAtFaderAlpha) : 1f;
    }

    private void FindShaders()
    {
        if (!imageShader) imageShader = Shader.Find(ScreenHolesFader.ShaderName);
        if (!textShader) textShader = Shader.Find(TextShaderName);
    }
}
