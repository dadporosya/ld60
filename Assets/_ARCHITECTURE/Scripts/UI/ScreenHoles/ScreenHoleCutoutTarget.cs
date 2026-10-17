using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Added by <see cref="ScreenHoleCutout"/> to each Graphic it cuts. At render time it swaps the Graphic's
/// material for a copy on a hole-cutting shader, and uploads the fader's holes (in this Graphic's canvas
/// space) to that copy every frame. Other code may still change the Graphic's material freely: the copy
/// follows whatever material comes in.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("")]
public class ScreenHoleCutoutTarget : MonoBehaviour, IMaterialModifier
{
    private ScreenHoleCutout owner;
    private Graphic graphic;

    // The material that came in and the hole-cutting copy made from it.
    private Material sourceMaterial;
    private Material cutMaterial;
    private int holeCount;

    public void Bind(ScreenHoleCutout cutout)
    {
        owner = cutout;
        if (!graphic) graphic = GetComponent<Graphic>();
    }

    /// <summary>Makes the Graphic pick its material again (the cut was switched on or off).</summary>
    public void Refresh()
    {
        if (!graphic) graphic = GetComponent<Graphic>();
        if (graphic) graphic.SetMaterialDirty();
    }

    public Material GetModifiedMaterial(Material baseMaterial)
    {
        if (!baseMaterial || !owner || !owner.Cutting) return baseMaterial;

        if (cutMaterial && sourceMaterial == baseMaterial) return cutMaterial;

        Shader shader = owner.CutShaderFor(baseMaterial.shader);
        if (!shader) return baseMaterial;

        DestroyCutMaterial();
        sourceMaterial = baseMaterial;
        // A copy keeps the source's textures, colours and keywords; only the shader changes.
        cutMaterial = new Material(baseMaterial)
        {
            shader = shader,
            name = baseMaterial.name + " (ScreenHoles cut)",
            hideFlags = HideFlags.HideAndDontSave
        };
        holeCount = -1;
        return cutMaterial;
    }

    /// <summary>
    /// Uploads <paramref name="fader"/>'s holes (scaled by <paramref name="strength"/>) to the cut material.
    /// A null fader clears them. The arrays are scratch buffers shared by every target.
    /// </summary>
    public void UploadHoles(ScreenHolesFader fader, float strength, Vector4[] holeData, Vector4[] holeParams)
    {
        if (!cutMaterial) return;
        if (!graphic) graphic = GetComponent<Graphic>();

        Canvas canvas = graphic ? graphic.canvas : null;
        int count = fader && canvas ? fader.CollectHoles(canvas.transform, holeData, holeParams, strength) : 0;

        // Nothing open now and nothing open last frame: the material already says so.
        if (count == 0 && holeCount == 0) return;

        if (count == 0)
        {
            // CollectHoles didn't run (no fader): the shared buffers may still hold another target's holes.
            System.Array.Clear(holeData, 0, holeData.Length);
            System.Array.Clear(holeParams, 0, holeParams.Length);
        }

        ScreenHolesFader.UploadHoles(cutMaterial, holeData, holeParams, count);
        holeCount = count;
    }

    private void OnDestroy() => DestroyCutMaterial();

    private void DestroyCutMaterial()
    {
        if (cutMaterial)
        {
            if (Application.isPlaying) Destroy(cutMaterial);
            else DestroyImmediate(cutMaterial);
        }

        cutMaterial = null;
        sourceMaterial = null;
    }
}
