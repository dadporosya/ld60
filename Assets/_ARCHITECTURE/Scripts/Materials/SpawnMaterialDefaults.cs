using UnityEngine;

/// <summary>
/// The MasterMat state units and buildings are given the moment they come into the world.
///
/// Prefabs ship with pixelation off at resolution 16, and a runtime-added controller (the one
/// <c>OnHitAnimation</c> puts on the model child) starts from the script defaults, so the value
/// has to be pushed at spawn rather than baked into a single prefab.
///
/// It goes through <see cref="MasterMaterialController.ConfigurePixelation"/>, which is the
/// interned path: every object given the same settings keeps sharing one material instead of
/// cloning its own, so applying this to 216 units and 34 buildings does not cost 250 extra
/// SetPass calls.
/// </summary>
public static class SpawnMaterialDefaults
{
    /// <summary>Shader pixel resolution every unit and building spawns with.</summary>
    public const float PixelResolution = 720f;

    /// <summary>Whether the pixelation effect itself is switched on at spawn.</summary>
    public const bool PixelationEnabled = true;

    /// <summary>
    /// Applies the spawn pixelation settings to every MasterMaterialController on
    /// <paramref name="root"/> and below it, inactive ones included.
    /// </summary>
    public static void Apply(GameObject root)
    {
        if (root == null) return;

        foreach (MasterMaterialController controller in root.GetComponentsInChildren<MasterMaterialController>(true))
            controller.ConfigurePixelation(PixelationEnabled, PixelResolution);
    }
}
