using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PrimeTween;

/// <summary>
/// A single full-screen black Image with rectangular HOLES punched through it: everything behind a
/// hole is seen clearly, everything else is covered by the fader.
///
/// HOW IT WORKS: the Image is drawn with the "UI/ScreenHoles" shader, which receives the holes as a
/// uniform array. For every pixel the shader takes the signed distance to each hole rectangle and
/// forces the fader's alpha to 0 inside it. This script only converts the hole RectTransforms into
/// the canvas' local space (the space UI vertices are already in when a Canvas is batched) and
/// uploads them. One Image, one draw call, holes may move and animate freely.
///
/// SETUP:
/// 1. Full-screen UI Image (anchors 0..1, zero offsets, no sprite, colour black, alpha 1).
/// 2. Add this component to that same GameObject.
/// 3. Drag the RectTransforms that mark the holes into <see cref="holes"/> - or put a
///    <see cref="ScreenHole"/> component on them and they register themselves.
/// The material is created at runtime from the shader, so nothing else needs to be assigned.
///
/// UNITS: corner radius, feather and padding are in canvas units - the same numbers you see in a
/// RectTransform's Width/Height.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class ScreenHolesFader : MonoBehaviour, ICanvasRaycastFilter
{
    /// <summary>Maximum number of simultaneous holes. Must match MAX_HOLES in UIScreenHoles.shader.</summary>
    public const int MaxHoles = 32;

    public const string ShaderName = "UI/ScreenHoles";

    public static ScreenHolesFader Instance;

    [Header("References")]
    [Tooltip("The full-screen Image this fader drives. Auto-filled from this GameObject.")]
    [SerializeField] private Image faderImage;

    [Tooltip("Leave empty to look the shader up by name (\"UI/ScreenHoles\").")]
    [SerializeField] private Shader holeShader;

    [Header("Holes")]
    [Tooltip("RectTransforms that mark where the fader is cut away. Inactive ones are ignored.")]
    [SerializeField] private List<RectTransform> holes = new List<RectTransform>();

    [Tooltip("On enable, also pick up every active ScreenHole component in the scene.")]
    [SerializeField] private bool collectSceneHolesOnEnable = true;

    [Header("Defaults (used by holes without a ScreenHole component), in canvas units")]
    [SerializeField] private float defaultCornerRadius = 0f;
    [SerializeField] private float defaultFeather = 1f;
    [SerializeField] private float defaultPadding = 0f;

    [Header("Behaviour")]
    [Tooltip("Re-upload the hole rects every frame. Turn off if the holes never move, then call UpdateNow() yourself.")]
    [SerializeField] private bool updateEveryFrame = true;

    [Tooltip("Clicks inside a hole pass through to whatever is behind the fader.")]
    [SerializeField] private bool clicksPassThroughHoles = true;

    [Tooltip("Expose this fader as ScreenHolesFader.Instance.")]
    [SerializeField] private bool registerInstance = true;

    private static readonly int HolesId = Shader.PropertyToID("_Holes");
    private static readonly int HoleParamsId = Shader.PropertyToID("_HoleParams");
    private static readonly int HoleCountId = Shader.PropertyToID("_HoleCount");

    private readonly Vector4[] _holeData = new Vector4[MaxHoles];
    private readonly Vector4[] _holeParams = new Vector4[MaxHoles];
    private readonly Vector3[] _worldCorners = new Vector3[4];

    private Material _material;
    private Material _originalMaterial;
    private int _activeCount;
    private Tween _fadeTween;

    public Shader HoleShader
    {
        get => holeShader;
        set
        {
            holeShader = value;
            if (isActiveAndEnabled) EnsureMaterial();
        }
    }

    public float DefaultCornerRadius { get => defaultCornerRadius; set => defaultCornerRadius = value; }
    public float DefaultFeather { get => defaultFeather; set => defaultFeather = value; }
    public float DefaultPadding { get => defaultPadding; set => defaultPadding = value; }

    /// <summary>How many holes are currently uploaded to the shader.</summary>
    public int ActiveHoleCount => _activeCount;

    /// <summary>The Image this fader draws with.</summary>
    public Image FaderImage => faderImage ? faderImage : faderImage = GetComponent<Image>();

    // ------------------------------------------------------------------ lifecycle

    private void Awake()
    {
        if (!faderImage) faderImage = GetComponent<Image>();
        if (faderImage) faderImage.rectTransform.anchoredPosition = Vector2.zero;

        // Not DontDestroyOnLoad: the fader lives under a Canvas, so it is never a root object.
        if (registerInstance && Application.isPlaying) h.CreateStaticInstance(this, ref Instance, false);
    }

    private void OnEnable()
    {
        if (registerInstance && Application.isPlaying && !Instance) Instance = this;

        EnsureMaterial();
        if (collectSceneHolesOnEnable) CollectSceneHoles();
        UpdateNow();
    }

    private void OnDisable()
    {
        _fadeTween.Stop();
        ReleaseMaterial();
        if (Instance == this) Instance = null;
    }

    private void LateUpdate()
    {
        // LateUpdate so any layout group / anchor animation of this frame is already applied.
        if (updateEveryFrame || !Application.isPlaying) UpdateNow();
    }

    private void OnValidate()
    {
#if UNITY_EDITOR
        if (!isActiveAndEnabled) return;

        // Deferred: OnValidate must not create or destroy objects, and the material may be rebuilt.
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this && isActiveAndEnabled) UpdateNow();
        };
#endif
    }

    // ------------------------------------------------------------------ public API

    /// <summary>Adds a hole. Duplicates and nulls are ignored.</summary>
    public void AddHole(RectTransform hole)
    {
        if (!hole || holes.Contains(hole)) return;

        if (holes.Count >= MaxHoles)
        {
            h.Out($"ScreenHolesFader: cannot add '{hole.name}', the limit of {MaxHoles} holes is reached.");
            return;
        }

        holes.Add(hole);
        UpdateNow();
    }

    /// <summary>Removes a hole, so the fader becomes solid there again.</summary>
    public void RemoveHole(RectTransform hole)
    {
        if (!hole || !holes.Remove(hole)) return;
        UpdateNow();
    }

    /// <summary>Removes every hole - the fader becomes a plain solid screen cover.</summary>
    public void ClearHoles()
    {
        holes.Clear();
        UpdateNow();
    }

    /// <summary>Replaces the whole hole list at once.</summary>
    public void SetHoles(IEnumerable<RectTransform> newHoles)
    {
        holes.Clear();
        if (newHoles != null)
        {
            foreach (RectTransform hole in newHoles)
            {
                if (hole && !holes.Contains(hole) && holes.Count < MaxHoles) holes.Add(hole);
            }
        }
        UpdateNow();
    }

    /// <summary>True if this RectTransform is currently listed as a hole.</summary>
    public bool HasHole(RectTransform hole) => hole && holes.Contains(hole);

    /// <summary>
    /// Animates a single hole open (1) or closed (0). Adds a <see cref="ScreenHole"/> component to
    /// the rect if it does not have one, since that is where the per-hole state lives.
    /// </summary>
    public Tween SetHoleStrength(RectTransform hole, float strength, float duration = 0f, Ease ease = Ease.OutCubic, bool useUnscaledTime = false)
    {
        if (!hole) return default;

        ScreenHole settings = hole.GetComponent<ScreenHole>();
        if (!settings) settings = hole.gameObject.AddComponent<ScreenHole>();

        AddHole(hole);
        return settings.TweenStrength(strength, duration, ease, useUnscaledTime: useUnscaledTime);
    }

    /// <summary>Instantly sets the fader's alpha (1 = fully covering, 0 = invisible).</summary>
    public void SetAlpha(float alpha)
    {
        _fadeTween.Stop();
        if (!FaderImage) return;

        Color c = FaderImage.color;
        c.a = Mathf.Clamp01(alpha);
        FaderImage.color = c;
    }

    /// <summary>Fades the whole cover (holes keep their shape) to the given alpha.</summary>
    public Tween FadeTo(float alpha, float duration, Ease ease = Ease.Linear, bool useUnscaledTime = false)
    {
        _fadeTween.Stop();
        if (!FaderImage) return default;

        if (duration <= 0f)
        {
            SetAlpha(alpha);
            return default;
        }

        _fadeTween = Tween.Alpha(FaderImage, Mathf.Clamp01(alpha), duration, ease, useUnscaledTime: useUnscaledTime);
        return _fadeTween;
    }

    /// <summary>Fades the cover in (alpha 1).</summary>
    public Tween FadeIn(float duration, Ease ease = Ease.Linear, bool useUnscaledTime = false) => FadeTo(1f, duration, ease, useUnscaledTime);

    /// <summary>Fades the cover out (alpha 0).</summary>
    public Tween FadeOut(float duration, Ease ease = Ease.Linear, bool useUnscaledTime = false) => FadeTo(0f, duration, ease, useUnscaledTime);

    /// <summary>Recomputes the hole rectangles and uploads them to the shader.</summary>
    public void UpdateNow()
    {
        if (!EnsureMaterial()) return;

        Canvas canvas = FaderImage ? FaderImage.canvas : null;
        if (!canvas) return;

        // UI vertices reach the shader in the space of the Canvas that batches them, so the holes
        // are expressed in exactly the same space. This stays correct for every canvas render mode.
        Transform space = canvas.transform;

        int count = 0;

        for (int i = 0; i < holes.Count; i++)
        {
            if (count >= MaxHoles) break;

            RectTransform hole = holes[i];
            if (!hole || !hole.gameObject.activeInHierarchy) continue;

            ScreenHole settings = hole.GetComponent<ScreenHole>();
            float strength = settings ? settings.Strength : 1f;
            if (strength <= 0f) continue;

            hole.GetWorldCorners(_worldCorners);
            Vector2 a = space.InverseTransformPoint(_worldCorners[0]); // bottom-left
            Vector2 b = space.InverseTransformPoint(_worldCorners[2]); // top-right
            Vector2 min = Vector2.Min(a, b);
            Vector2 max = Vector2.Max(a, b);

            float padding = settings ? settings.Padding : defaultPadding;
            Vector2 center = (min + max) * 0.5f;
            Vector2 half = (max - min) * 0.5f + Vector2.one * padding;
            half = Vector2.Max(half, Vector2.zero);

            float radius = settings && settings.CornerRadius >= 0f ? settings.CornerRadius : defaultCornerRadius;
            float feather = settings && settings.Feather >= 0f ? settings.Feather : defaultFeather;

            _holeData[count] = new Vector4(center.x, center.y, half.x, half.y);
            _holeParams[count] = new Vector4(Mathf.Max(radius, 0f), Mathf.Max(feather, 0.0001f), Mathf.Clamp01(strength), 0f);
            count++;
        }

        for (int i = count; i < MaxHoles; i++)
        {
            _holeData[i] = Vector4.zero;
            _holeParams[i] = Vector4.zero;
        }

        _activeCount = count;

        // Always upload full-length arrays: Unity locks a shader array's size on the first upload.
        _material.SetVectorArray(HolesId, _holeData);
        _material.SetVectorArray(HoleParamsId, _holeParams);
        _material.SetInt(HoleCountId, count);
    }

    // ------------------------------------------------------------------ raycasting

    /// <summary>
    /// Lets pointer events fall through the holes: the fader refuses raycasts that land inside a
    /// hole, so the objects behind it stay clickable while the covered area keeps blocking input.
    /// </summary>
    public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
    {
        if (!clicksPassThroughHoles || _activeCount == 0) return true;

        Canvas canvas = FaderImage ? FaderImage.canvas : null;
        if (!canvas) return true;

        RectTransform canvasRect = (RectTransform)canvas.transform;
        Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : eventCamera;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, cam, out Vector2 local))
            return true;

        for (int i = 0; i < _activeCount; i++)
        {
            Vector4 hole = _holeData[i];
            Vector2 d = new Vector2(Mathf.Abs(local.x - hole.x), Mathf.Abs(local.y - hole.y));
            if (d.x <= hole.z && d.y <= hole.w) return false; // inside a hole -> let the click pass
        }

        return true;
    }

    // ------------------------------------------------------------------ internals

    /// <summary>Picks up every active ScreenHole in the scene that wants to belong to this fader.</summary>
    private void CollectSceneHoles()
    {
        ScreenHole[] sceneHoles = FindObjectsByType<ScreenHole>(FindObjectsSortMode.None);

        for (int i = 0; i < sceneHoles.Length; i++)
        {
            ScreenHole hole = sceneHoles[i];
            if (hole && hole.isActiveAndEnabled) hole.Register();
        }
    }

    /// <summary>Creates (once) the material instance that carries the hole arrays.</summary>
    private bool EnsureMaterial()
    {
        if (!FaderImage) return false;

        if (!holeShader) holeShader = Shader.Find(ShaderName);
        if (!holeShader)
        {
            h.Out($"ScreenHolesFader: shader '{ShaderName}' not found - the fader will render solid.");
            return false;
        }

        if (_material && _material.shader == holeShader)
        {
            if (FaderImage.material != _material) FaderImage.material = _material;
            return true;
        }

        ReleaseMaterial();

        // Keep null if the Image just uses the default UI material, so nothing extra gets serialized.
        _originalMaterial = FaderImage.material == FaderImage.defaultMaterial ? null : FaderImage.material;
        _material = new Material(holeShader)
        {
            name = "ScreenHoles (runtime instance)",
            hideFlags = HideFlags.HideAndDontSave
        };
        FaderImage.material = _material;
        return true;
    }

    private void ReleaseMaterial()
    {
        if (!_material) return;

        if (faderImage && faderImage.material == _material) faderImage.material = _originalMaterial;

        if (Application.isPlaying) Destroy(_material);
        else DestroyImmediate(_material);

        _material = null;
        _originalMaterial = null;
    }
}
