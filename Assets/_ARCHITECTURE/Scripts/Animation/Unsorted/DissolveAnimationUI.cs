using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI counterpart of <see cref="DissolveAnimation"/>. Reuses all of the base keyframe logic
/// (values, durations, easing, return-to-rest) and only changes where the materials come from:
/// UI Graphics (Image / RawImage / TMP text) instead of Renderers.
///
/// A Graphic is NOT a Renderer, so the base class's GetComponentsInChildren&lt;Renderer&gt; sweep
/// never sees it — this is why the plain DissolveAnimation silently does nothing on a Canvas.
/// UI also needs the MasterMatUI variant of the shader, because the canvas shader carries the
/// stencil/mask properties the worldspace MasterMat lacks. Both variants expose the same
/// property names, so the dissolve driving itself is identical.
/// </summary>
public class DissolveAnimationUI : DissolveAnimation
{
    /// <summary>
    /// Collects the material off this object's Graphic plus every Graphic below it, assigning
    /// the MasterMatUI variant to any that don't already carry a dissolve-capable material.
    /// </summary>
    protected override void CollectMaterials()
    {
        _materials.Clear();

        if (includeChildren)
        {
            foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
                TryAdd(ResolveMaterial(graphic));
        }
        else
        {
            TryAdd(ResolveMaterial(GetComponent<Graphic>()));
        }

        if (_materials.Count == 0)
            h.Out($"DissolveAnimationUI on '{name}': no Graphic with a dissolve-capable material found; the animation will do nothing.");
    }

    // Unlike Renderer.material, Graphic.material does NOT clone on access — it hands back
    // whatever is assigned, which may well be a shared project asset. Writing the dissolve
    // amount into that would leak the effect onto every other element using it (and dirty the
    // asset on disk in the editor), so anything that isn't already a per-object instance gets
    // instanced here before we touch it.
    private static Material ResolveMaterial(Graphic graphic)
    {
        if (graphic == null) return null;

        Material current = graphic.material;

        // Graphic.defaultMaterial is the built-in UI material every Graphic falls back to when
        // nothing is assigned; it has no dissolve properties, so swap in MasterMatUI.
        bool needsMasterMat = current == null
                              || current == graphic.defaultMaterial
                              || !current.HasProperty(DissolveAmountId);

        if (needsMasterMat)
        {
            Material source = MasterMaterialController.UiSource;
            if (source == null) return null;
            Material instance = new Material(source);
            graphic.material = instance;
            return instance;
        }

        // Already dissolve-capable, but still the shared asset (MasterMatUI straight from
        // Resources, or the same material shared with other elements) — instance it so this
        // object dissolves on its own.
        if (current == MasterMaterialController.UiSource)
        {
            Material instance = new Material(current);
            graphic.material = instance;
            return instance;
        }

        // A per-object instance already, most likely created by MasterMaterialController.
        // Drive it directly so both components write to the material that is actually drawn.
        return current;
    }
}
