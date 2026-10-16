using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PrimeTween;

/// <summary>
/// Modulates the MasterMat <c>_Dissolve_amount</c> property through a list of keyframe values.
///
/// Operating principle: on Start this grabs the live MasterMat instance off the holder's own
/// Renderer plus every Renderer below it (SpriteRenderers included), then tweens
/// <c>_Dissolve_amount</c> from one entry of <see cref="values"/> to the next. 0 = fully
/// visible, 1 = fully dissolved, so <c>values = [1, 0.3]</c> dissolves out completely, then
/// eases back to 30%.
///
/// Materials are written to directly — no MasterMaterialController is required. Renderers whose
/// material lacks <c>_Dissolve_amount</c> (particles, non-MasterMat parts) are skipped, so the
/// whole model dissolves as one without touching anything that can't dissolve.
///
/// For UI elements under a Canvas use <see cref="DissolveAnimationUI"/> — a UI Graphic is not a
/// Renderer and needs the MasterMatUI variant of the shader.
/// </summary>
public class DissolveAnimation : AnimationBase
{
    // The project's shader spells the toggle "_Disslove" (sic) and the amount "_Dissolve_amount".
    // Cached as IDs so the per-frame tween callback doesn't re-hash the strings.
    protected static readonly int DissolveAmountId = Shader.PropertyToID("_Dissolve_amount");
    protected static readonly int DissolveToggleId = Shader.PropertyToID("_Disslove");

    [Header("Dissolve Settings")]
    [Tooltip("Sequence of dissolve amounts to tween through, in order. 0 = fully visible, 1 = fully dissolved.")]
    public List<float> values = new List<float>();

    [Tooltip("Time to reach each keyframe. Entries are matched to values by index; anything missing falls back to defaultDuration.")]
    public List<float> durations = new List<float>();

    [Tooltip("Duration used for keyframes with no matching entry in durations.")]
    public float defaultDuration = 1f;

    [Tooltip("Easing applied while moving between keyframes.")]
    public Ease ease = Ease.Default;

    [Tooltip("Switches the shader's dissolve effect on for the duration of the animation and off again once it returns to the initial state.")]
    public bool toggleDissolveOnPlay = true;

    [Tooltip("Also collect materials from child renderers. Leave on when this sits on a model built from several parts.")]
    public bool includeChildren = true;

    [Tooltip("Resting dissolve amount. When customInitialState is off this is captured from the material instead.")]
    [Range(0f, 1f)] public float initialState;

    // Every material this animation drives. Populated in Start (see CollectMaterials) and kept
    // for the object's lifetime — renderers don't swap materials at runtime in this project.
    protected readonly List<Material> _materials = new();

    // The tween currently writing the dissolve amount. Tracked so it can be stopped on
    // return/stop; otherwise it keeps writing the property after the coroutine is gone.
    private Tween _dissolveTween;

    protected bool HasMaterials => _materials.Count > 0;

    public override void Awake()
    {
        // Deliberately empty: material collection waits until Start. MasterMaterialController
        // assigns its MasterMat instances in Awake and component Awake order is undefined, so
        // collecting here could capture the shared asset instead of the live instance.
    }

    public override void Start()
    {
        CollectMaterials();

        if (ShouldCaptureInitialState()) initialState = ReadAmount();
        else SetAmount(initialState);

        base.Start();
    }

    /// <summary>
    /// Fills <see cref="_materials"/> with the live MasterMat instances to drive. Override this
    /// to source materials from somewhere other than Renderers (see DissolveAnimationUI).
    /// </summary>
    protected virtual void CollectMaterials()
    {
        _materials.Clear();

        if (includeChildren)
        {
            foreach (Renderer rend in GetComponentsInChildren<Renderer>(true))
                TryAdd(ResolveMaterial(rend));
        }
        else
        {
            TryAdd(ResolveMaterial(GetComponent<Renderer>()));
        }

        if (!HasMaterials)
            h.Out($"DissolveAnimation on '{name}': no material with _Dissolve_amount found; the animation will do nothing.");
    }

    // Renderer.material returns the per-object instance, cloning the shared asset on first
    // access so we never write into the project's MasterMat. When MasterMaterialController has
    // already assigned an instance, the getter hands back that same instance rather than
    // cloning again, so both components keep driving the one material that is actually drawn.
    private static Material ResolveMaterial(Renderer rend)
    {
        if (rend == null) return null;
        Material shared = rend.sharedMaterial;
        // Check the shared reference first: reading .material on a renderer that has no
        // dissolve material would clone it for nothing.
        return shared != null && shared.HasProperty(DissolveAmountId) ? rend.material : null;
    }

    protected void TryAdd(Material mat)
    {
        if (mat != null && !_materials.Contains(mat)) _materials.Add(mat);
    }

    // All materials are written in lockstep, so any one of them reports the current amount.
    protected float ReadAmount() => HasMaterials ? _materials[0].GetFloat(DissolveAmountId) : initialState;

    protected void SetAmount(float value)
    {
        float clamped = Mathf.Clamp01(value);
        foreach (Material mat in _materials)
            if (mat != null) mat.SetFloat(DissolveAmountId, clamped);
    }

    protected void SetDissolveEnabled(bool value)
    {
        foreach (Material mat in _materials)
            if (mat != null) mat.SetFloat(DissolveToggleId, value ? 1f : 0f);
    }

    public override void ReturnToInitialState()
    {
        if (_dissolveTween.isAlive) _dissolveTween.Stop();
        SetAmount(initialState);
        if (toggleDissolveOnPlay) SetDissolveEnabled(false);
    }

    // The material was reset externally, so it already holds the initial amount. Just drop the
    // running tween so the next Play() starts from the right value without writing anything.
    public override void SyncToRestingState()
    {
        if (_dissolveTween.isAlive) _dissolveTween.Stop();
    }

    public override IEnumerator AnimationCoroutine()
    {
        if (_dissolveTween.isAlive) _dissolveTween.Stop();

        if (!HasMaterials || values.Count == 0)
        {
            yield return base.AnimationCoroutine();
            yield break;
        }

        if (toggleDissolveOnPlay) SetDissolveEnabled(true);

        // Walk the keyframes: tween from wherever the material currently sits to each value in turn.
        for (int i = 0; i < values.Count; i++)
        {
            float target = Mathf.Clamp01(values[i]);
            float duration = i < durations.Count ? durations[i] : defaultDuration;
            float from = ReadAmount();

            if (duration > 0f)
            {
                _dissolveTween = Tween.Custom(from, target, duration, val => SetAmount(val), ease);
                while (_dissolveTween.isAlive) yield return null;
            }

            // Guarantees the exact keyframe value, and covers duration <= 0 (instant step).
            SetAmount(target);
        }

        yield return base.AnimationCoroutine();
    }

    // Eases the dissolve amount back to the resting value over returningTime.
    public override IEnumerator ReturnToInitialStateAnimated()
    {
        if (_dissolveTween.isAlive) _dissolveTween.Stop();
        if (!HasMaterials) yield break;

        float from = ReadAmount();

        if (returningTime > 0f && !Mathf.Approximately(from, initialState))
        {
            _dissolveTween = Tween.Custom(from, initialState, returningTime, val => SetAmount(val), returningEasing);
            while (_dissolveTween.isAlive) yield return null;
        }

        SetAmount(initialState);
        if (toggleDissolveOnPlay) SetDissolveEnabled(false);
    }
}
