using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using VolFx;

/// <summary>
/// Runtime control over the <c>Source</c> field of a <see cref="VolFx.VolFx"/> renderer feature
/// living on a URP renderer asset (e.g. <c>PC_Renderer</c>, feature <c>VolFxSelectiveVHS</c>).
///
/// VolFx reads <c>_source._source</c> every frame while building its render pass, so switching it
/// takes effect on the next rendered frame — no renderer rebuild required.
///
/// IMPORTANT: a renderer feature is a serialized asset, not a scene object. Changes made in the
/// Editor persist to the .asset file on disk. This manager therefore caches the original value on
/// <see cref="Awake"/> and restores it in <see cref="OnDestroy"/> / <see cref="OnApplicationQuit"/>
/// (see <see cref="restoreOnExit"/>).
///
/// Usage:
///   VolFxSourceManager.Instance.SetSourceToCamera();
///   VolFxSourceManager.Instance.SetSourceToLayerMask();
/// </summary>
[AddComponentMenu("MyFriendPetya/Rendering/VolFx Source Manager")]
public class VolFxSourceManager : MonoBehaviour
{
    public static VolFxSourceManager Instance;

    [Tooltip("The URP renderer asset holding the feature (e.g. PC_Renderer).")]
    [SerializeField] private ScriptableRendererData rendererData;

    [Tooltip("Name of the VolFx renderer feature to control (as shown in the renderer inspector).")]
    [SerializeField] private string featureName = "VolFxSelectiveVHS";

    [Tooltip("Restore the source value the feature had at startup when the game stops. " +
             "Keep this on so play mode does not dirty the renderer asset.")]
    [SerializeField] private bool restoreOnExit = true;

    private VolFx.VolFx feature;
    private VolFx.VolFx.SourceOptions.Source originalSource;
    private bool cached;

    /// <summary>The controlled VolFx feature (looked up by <see cref="featureName"/> on first access).</summary>
    public VolFx.VolFx Feature => feature != null ? feature : (feature = FindFeature(featureName));

    /// <summary>Current source of the feature.</summary>
    public VolFx.VolFx.SourceOptions.Source Source
    {
        get => Feature != null ? Feature._source._source : VolFx.VolFx.SourceOptions.Source.Camera;
        set => SetSource(value);
    }

    public bool IsCamera    => Source == VolFx.VolFx.SourceOptions.Source.Camera;
    public bool IsLayerMask => Source == VolFx.VolFx.SourceOptions.Source.LayerMask;

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance);
        if (Instance != this) return;

        if (rendererData == null)
        {
            h.Out($"{name}: VolFxSourceManager has no renderer data assigned.");
            return;
        }

        if (Feature == null)
        {
            h.Out($"{name}: VolFx feature '{featureName}' not found on {rendererData.name}.");
            return;
        }

        originalSource = Feature._source._source;
        cached = true;
    }

    // ---------------------------------------------------------------- Public API

    /// <summary>Switch the feature to render the whole camera image (full-screen effect).</summary>
    public void SetSourceToCamera() => SetSource(VolFx.VolFx.SourceOptions.Source.Camera);

    /// <summary>Switch the feature back to the layer mask source (selective effect).</summary>
    public void SetSourceToLayerMask() => SetSource(VolFx.VolFx.SourceOptions.Source.LayerMask);

    /// <summary>Toggle between Camera and LayerMask.</summary>
    public void ToggleSource() { if (IsCamera) SetSourceToLayerMask(); else SetSourceToCamera(); }

    /// <summary>Restore the source the feature had when the game started.</summary>
    public void RestoreOriginalSource() { if (cached) SetSource(originalSource); }

    /// <summary>Set the feature source to an arbitrary value.</summary>
    public void SetSource(VolFx.VolFx.SourceOptions.Source source)
    {
        var f = Feature;
        if (f == null)
        {
            h.Out($"{name}: cannot set source, VolFx feature '{featureName}' is missing.");
            return;
        }

        if (f._source._source == source) return;

        f._source._source = source; // picked up on the next rendered frame
    }

    /// <summary>Enable / disable the whole feature.</summary>
    public void SetFeatureActive(bool active) { if (Feature != null) Feature.SetActive(active); }

    // ---------------------------------------------------------------- Internals

    private VolFx.VolFx FindFeature(string featureName) => FindFeatureAnywhere(featureName, rendererData);

    private void OnDestroy()
    {
        if (Instance == this && restoreOnExit) RestoreOriginalSource();
    }

    private void OnApplicationQuit()
    {
        if (Instance == this && restoreOnExit) RestoreOriginalSource();
    }

    // ---------------------------------------------------------------- Static API (no manager needed)

    // Feature -> the source it had before anything changed it through SetSourceOnFeature. Static,
    // because the thing being edited is a shared renderer ASSET, not a scene object: whoever changes
    // it first owns the "home" value, and every later restore has to land back on that same value.
    private static readonly Dictionary<VolFx.VolFx, VolFx.VolFx.SourceOptions.Source> assetHomeSources =
        new Dictionary<VolFx.VolFx, VolFx.VolFx.SourceOptions.Source>();

    private static bool quitHookRegistered;

    /// <summary>
    /// Finds a VolFx renderer feature by name. Looks in <paramref name="rendererData"/> when one is
    /// given, otherwise sweeps every VolFx feature currently loaded in memory — which includes the
    /// features of the renderer the game is actually rendering with (e.g. <c>PC_Renderer</c>), so no
    /// asset reference has to be wired up in the inspector.
    /// </summary>
    /// <returns>The named feature, the only VolFx feature found when the name doesn't match, or null.</returns>
    public static VolFx.VolFx FindFeatureAnywhere(string featureName, ScriptableRendererData rendererData = null)
    {
        VolFx.VolFx firstVolFx = null;

        if (rendererData != null)
        {
            List<ScriptableRendererFeature> features = rendererData.rendererFeatures;
            for (int i = 0; i < features.Count; i++)
            {
                if (features[i] is not VolFx.VolFx volFx) continue;
                firstVolFx ??= volFx;
                if (volFx.name == featureName) return volFx;
            }

            // no exact name match — fall back to the only VolFx feature on that renderer
            if (firstVolFx != null) return firstVolFx;
        }

        // FindObjectsOfTypeAll reaches loaded assets, not just scene objects — renderer features live
        // on an asset, so this is the only way to reach them without an explicit reference.
        foreach (VolFx.VolFx volFx in Resources.FindObjectsOfTypeAll<VolFx.VolFx>())
        {
            if (volFx == null) continue;
            firstVolFx ??= volFx;
            if (volFx.name == featureName) return volFx;
        }

        return firstVolFx;
    }

    /// <summary>
    /// Sets the source on a feature directly, remembering the value it had the first time it was
    /// touched so <see cref="RestoreFeatureSource"/> (and the play-mode-exit hook) can put it back.
    /// Use this when there is no <see cref="VolFxSourceManager"/> in the scene.
    /// </summary>
    public static void SetSourceOnFeature(VolFx.VolFx feature, VolFx.VolFx.SourceOptions.Source source)
    {
        if (feature == null) return;

        if (!assetHomeSources.ContainsKey(feature)) assetHomeSources[feature] = feature._source._source;
        RegisterQuitHook();

        if (feature._source._source == source) return;
        feature._source._source = source; // picked up on the next rendered frame
    }

    /// <summary>Puts a feature's source back to the value it had before <see cref="SetSourceOnFeature"/> touched it.</summary>
    public static void RestoreFeatureSource(VolFx.VolFx feature)
    {
        if (feature == null) return;
        if (!assetHomeSources.TryGetValue(feature, out VolFx.VolFx.SourceOptions.Source home)) return;

        feature._source._source = home;
        assetHomeSources.Remove(feature);
    }

    /// <summary>Restores every feature changed through <see cref="SetSourceOnFeature"/>.</summary>
    public static void RestoreAllFeatureSources()
    {
        foreach (KeyValuePair<VolFx.VolFx, VolFx.VolFx.SourceOptions.Source> kv in assetHomeSources)
            if (kv.Key != null) kv.Key._source._source = kv.Value;

        assetHomeSources.Clear();
    }

    /// <summary>
    /// Renderer features are serialized assets: a source left on Camera when play mode ends would be
    /// written to the .asset file on disk. Restoring on quit keeps the project clean.
    /// </summary>
    private static void RegisterQuitHook()
    {
        if (quitHookRegistered) return;
        quitHookRegistered = true;

        Application.quitting += RestoreAllFeatureSources;
    }
}
