using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Base for the fake 2D lighting layers on a UI canvas (UI Images are unlit, so URP 2D lights can't reach them).
/// Each layer is a RawImage drawn with the "UI/Room Light" shader, which blends onto everything the canvas drew
/// before it — so put the layers AFTER the objects they light in the hierarchy.
/// Material and texture are generated (also in edit mode, for placing lights) and never saved with the scene.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RawImage))]
public abstract class RoomLightLayer : MonoBehaviour
{
    public const string ShaderName = "UI/Room Light";

    protected static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int MultiplyModeId = Shader.PropertyToID("_MultiplyMode");
    private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");

    [Tooltip("Auto-assigned. Kept as a reference so the shader is included in builds.")]
    [SerializeField] private Shader shader;

    protected RawImage image;
    protected Material material;
    protected Texture2D texture;
    private bool dirty = true;

    protected virtual void Reset()
    {
        shader = Shader.Find(ShaderName);
    }

    protected virtual void OnEnable()
    {
        image = GetComponent<RawImage>();
        image.raycastTarget = false;
        dirty = true;
        Rebuild();
    }

    protected virtual void OnDisable()
    {
        if (image) image.material = null;
    }

    protected virtual void OnDestroy()
    {
        DestroySafe(material);
        DestroySafe(texture);
    }

    protected virtual void OnValidate()
    {
        if (!shader) shader = Shader.Find(ShaderName);
        dirty = true;
    }

    protected virtual void Update()
    {
        if (dirty) Rebuild();
    }

    /// <summary>Regenerates the texture and material settings on the next Update.</summary>
    protected void MarkDirty() => dirty = true;

    private void Rebuild()
    {
        dirty = false;
        if (!image) return;

        if (!material)
        {
            if (!shader) shader = Shader.Find(ShaderName);
            if (!shader)
            {
                h.Out($"{GetType().Name} '{name}': shader '{ShaderName}' not found.");
                return;
            }

            material = new Material(shader) { name = $"{name} (RoomLight)", hideFlags = HideFlags.DontSave };
        }

        ConfigureMaterial();
        BuildTexture();
        image.texture = texture;
        image.material = material;
        OnRebuilt();
    }

    protected abstract void ConfigureMaterial();
    protected abstract void BuildTexture();
    protected virtual void OnRebuilt() { }

    protected void SetBlend(UnityEngine.Rendering.BlendMode src, UnityEngine.Rendering.BlendMode dst, bool multiply)
    {
        material.SetFloat(SrcBlendId, (float)src);
        material.SetFloat(DstBlendId, (float)dst);
        material.SetFloat(MultiplyModeId, multiply ? 1f : 0f);
    }

    /// <summary>Sets a float on the material and on the copy a Mask may render with instead.</summary>
    protected void SetMaterialFloat(int id, float value)
    {
        if (!material) return;
        material.SetFloat(id, value);
        Material rendered = image ? image.materialForRendering : null;
        if (rendered && rendered != material) rendered.SetFloat(id, value);
    }

    /// <summary>Reuses <see cref="texture"/> when the size matches, otherwise reallocates it.</summary>
    protected Texture2D GetTexture(int width, int height)
    {
        if (texture && texture.width == width && texture.height == height) return texture;
        DestroySafe(texture);
        texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = $"{name} (RoomLight)",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave,
        };
        return texture;
    }

    protected static void Stretch(RectTransform t)
    {
        t.anchorMin = Vector2.zero;
        t.anchorMax = Vector2.one;
        t.offsetMin = Vector2.zero;
        t.offsetMax = Vector2.zero;
    }

    private static void DestroySafe(Object o)
    {
        if (!o) return;
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }
}
