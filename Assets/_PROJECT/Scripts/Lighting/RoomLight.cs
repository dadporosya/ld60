using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A soft 2D light on a UI canvas, sized by its RectTransform. <see cref="Mode.Lit"/> brightens what's under it
/// in proportion to its colour (a light pool on the floor, lit objects); <see cref="Mode.Glow"/> adds light on top
/// (haze around a fixture). Put it after the objects it lights, and after a <see cref="RoomAmbientLight"/>.
/// In play mode it flickers like a fluorescent tube — <see cref="FlickerStyle.Broken"/> is a half-dead metro lamp
/// that chatters, cuts out, re-strikes and browns out. Other lights can follow its flicker.
/// </summary>
public class RoomLight : RoomLightLayer
{
    public enum Mode { Lit, Glow }
    public enum Shape { Radial, Tube }
    public enum FlickerStyle { None, Fluorescent, Broken }

    private enum BrokenState { Stable, Chatter, Out, Strike, WarmUp, Brownout }

    private static readonly int ColorId = Shader.PropertyToID("_Color");

    [Header("Light")]
    [SerializeField] private Mode mode = Mode.Lit;
    [SerializeField] private Shape shape = Shape.Radial;
    [SerializeField] private Color color = new Color(1f, 0.86f, 0.5f);
    [SerializeField, Min(0f)] private float intensity = 1f;
    [Tooltip("Share of the radius at full brightness.")]
    [SerializeField, Range(0f, 0.95f)] private float core = 0.05f;
    [Tooltip("Higher = light drops off faster from the centre.")]
    [SerializeField, Range(0.3f, 4f)] private float falloff = 1.6f;
    [Tooltip("Tube: share of the width that stays bright before the rounded ends fade.")]
    [SerializeField, Range(0f, 0.95f)] private float tubeLength = 0.6f;

    [Header("Flicker (play mode)")]
    [Tooltip("Follow another light's flicker instead of flickering on its own (e.g. a pool lit by a tube).")]
    [SerializeField] private RoomLight flickerSource;
    [SerializeField] private FlickerStyle flickerStyle = FlickerStyle.Fluorescent;
    [Tooltip("Constant subtle brightness noise.")]
    [SerializeField, Range(0f, 0.5f)] private float hum = 0.04f;
    [SerializeField, Min(0f)] private float humSpeed = 9f;

    [Header("Fluorescent")]
    [Tooltip("Average stutters per minute (0 = never).")]
    [SerializeField, Min(0f)] private float stuttersPerMinute = 3f;
    [Tooltip("How much a stutter dims the light.")]
    [SerializeField, Range(0f, 1f)] private float stutterDepth = 0.7f;
    [SerializeField] private Vector2 stutterDuration = new Vector2(0.08f, 0.45f);

    [Header("Broken")]
    [Tooltip("0 = mostly holds up, 1 = constantly dying.")]
    [SerializeField, Range(0f, 1f)] private float brokenness = 0.5f;
    [Tooltip("Brightness left while the tube is out (the glowing ends).")]
    [SerializeField, Range(0f, 0.5f)] private float offLevel = 0.05f;
    [Tooltip("Seconds the tube stays dead before it tries to re-strike.")]
    [SerializeField] private Vector2 outDuration = new Vector2(0.3f, 2.5f);
    [Tooltip("Flashes while re-striking.")]
    [SerializeField] private Vector2Int strikeFlashes = new Vector2Int(2, 6);
    [Tooltip("Tint while failing — dying tubes go dim pink/orange.")]
    [SerializeField] private Color failTint = new Color(1f, 0.55f, 0.6f);

    [Header("Broken: sound (optional)")]
    [Tooltip("Looping buzz whose volume follows the brightness.")]
    [SerializeField] private AudioSource buzz;
    [SerializeField, Range(0f, 1f)] private float buzzVolume = 0.3f;
    [Tooltip("Ticks played through SFXManager on each re-strike flash.")]
    [SerializeField] private List<AudioClip> strikeClicks = new List<AudioClip>();
    [SerializeField, Range(0f, 1f)] private float clickVolume = 0.5f;

    private float seed;
    private float onAmount = 1f;
    private Tween onTween;
    private Tween intensityTween;

    // Fluorescent
    private float nextStutter;
    private float stutterEnd;

    // Broken
    private BrokenState state;
    private float stateStart;
    private float stateEnd;
    private float stepEnd;      // chatter / strike sub-steps
    private float stepValue;
    private int flashesLeft;
    private bool flashLit;

    /// <summary>Current flicker brightness (0..1+), for lights that follow this one.</summary>
    public float FlickerMultiplier { get; private set; } = 1f;

    /// <summary>Current flicker tint (white when healthy), for lights that follow this one.</summary>
    public Color FlickerTint { get; private set; } = Color.white;

    public bool IsOn { get; private set; } = true;

    public float Intensity
    {
        get => intensity;
        set => intensity = Mathf.Max(0f, value);
    }

    public Color Color
    {
        get => color;
        set
        {
            color = value;
            if (image) image.color = color;
        }
    }

    public Mode LightMode { get => mode; set { mode = value; MarkDirty(); } }
    public Shape LightShape { get => shape; set { shape = value; MarkDirty(); } }
    public float TubeLength { get => tubeLength; set { tubeLength = Mathf.Clamp(value, 0f, 0.95f); MarkDirty(); } }
    public float Falloff { get => falloff; set { falloff = Mathf.Clamp(value, 0.3f, 4f); MarkDirty(); } }
    public RoomLight FlickerSource { get => flickerSource; set => flickerSource = value; }
    public FlickerStyle Flicker { get => flickerStyle; set { flickerStyle = value; EnterStable(Time.time); } }
    public float StuttersPerMinute { get => stuttersPerMinute; set => stuttersPerMinute = Mathf.Max(0f, value); }
    public float Brokenness { get => brokenness; set => brokenness = Mathf.Clamp01(value); }

    protected override void OnEnable()
    {
        seed = Random.value * 100f;
        ScheduleStutter(Time.time);
        EnterStable(Time.time);
        if (Application.isPlaying && buzz)
        {
            buzz.loop = true;
            if (!buzz.isPlaying) buzz.Play();
        }
        base.OnEnable();
    }

    protected override void OnDestroy()
    {
        onTween.Stop();
        intensityTween.Stop();
        base.OnDestroy();
    }

    protected override void ConfigureMaterial()
    {
        if (mode == Mode.Lit) SetBlend(BlendMode.DstColor, BlendMode.One, false);
        else SetBlend(BlendMode.One, BlendMode.One, false);
    }

    protected override void OnRebuilt()
    {
        image.color = color;
        ApplyIntensity();
    }

    protected override void Update()
    {
        base.Update();
        ApplyIntensity();
    }

    private void ApplyIntensity()
    {
        if (!Application.isPlaying)
        {
            FlickerMultiplier = 1f;
            FlickerTint = Color.white;
        }
        else if (flickerSource && flickerSource != this)
        {
            FlickerMultiplier = flickerSource.FlickerMultiplier;
            FlickerTint = flickerSource.FlickerTint;
        }
        else
        {
            float t = Time.time;
            float humValue = 1f + (Mathf.PerlinNoise(seed, t * humSpeed) - 0.5f) * 2f * hum;
            FlickerTint = Color.white;
            FlickerMultiplier = flickerStyle switch
            {
                FlickerStyle.Fluorescent => humValue * FluorescentFlicker(t),
                FlickerStyle.Broken => humValue * BrokenFlicker(t),
                _ => humValue,
            };
        }

        float brightness = FlickerMultiplier * onAmount;
        SetMaterialFloat(IntensityId, intensity * brightness);
        SetMaterialColor(ColorId, FlickerTint);
        if (buzz && Application.isPlaying) buzz.volume = buzzVolume * Mathf.Clamp01(brightness);
    }

    private void SetMaterialColor(int id, Color value)
    {
        if (!material) return;
        material.SetColor(id, value);
        Material rendered = image ? image.materialForRendering : null;
        if (rendered && rendered != material) rendered.SetColor(id, value);
    }

    // ---------- Fluorescent: steady with the odd stutter ----------

    private float FluorescentFlicker(float t)
    {
        if (t >= nextStutter && stuttersPerMinute > 0f)
        {
            stutterEnd = t + Random.Range(stutterDuration.x, stutterDuration.y);
            ScheduleStutter(stutterEnd);
        }

        // Hard on/off chatter instead of a smooth dip.
        return t < stutterEnd && Mathf.PerlinNoise(seed + 13f, t * 35f) > 0.45f ? 1f - stutterDepth : 1f;
    }

    private void ScheduleStutter(float from)
    {
        nextStutter = stuttersPerMinute > 0f
            ? from - Mathf.Log(Mathf.Max(1e-4f, Random.value)) * 60f / stuttersPerMinute
            : float.PositiveInfinity;
    }

    // ---------- Broken: a half-dead tube cycling through failures ----------

    private float BrokenFlicker(float t)
    {
        if (t >= stateEnd) NextBrokenState(t);

        float stateT = Mathf.InverseLerp(stateStart, stateEnd, t);
        float value;
        float fail;

        switch (state)
        {
            case BrokenState.Chatter:
                // Random hard steps between full, half and dead.
                if (t >= stepEnd)
                {
                    stepEnd = t + Random.Range(0.02f, 0.09f);
                    float r = Random.value;
                    stepValue = r < 0.45f ? 1f : r < 0.75f ? offLevel : Random.Range(0.35f, 0.7f);
                }
                value = stepValue;
                fail = 1f - stepValue;
                break;

            case BrokenState.Out:
                // Only the ends glow, unsteadily.
                value = offLevel * (0.5f + Mathf.PerlinNoise(seed + 7f, t * 18f));
                fail = 1f;
                break;

            case BrokenState.Strike:
                if (t >= stepEnd)
                {
                    if (flashLit || flashesLeft <= 0)
                    {
                        flashLit = false;
                        if (flashesLeft <= 0) { EnterState(BrokenState.WarmUp, t, Random.Range(0.3f, 0.9f)); return 0.5f; }
                        stepEnd = t + Random.Range(0.06f, 0.35f);
                    }
                    else
                    {
                        flashLit = true;
                        flashesLeft--;
                        stepValue = Random.Range(0.6f, 1.15f);
                        stepEnd = t + Random.Range(0.03f, 0.09f);
                        PlayClick();
                    }
                }
                value = flashLit ? stepValue : offLevel;
                fail = flashLit ? 0.4f : 1f;
                break;

            case BrokenState.WarmUp:
                // Comes up from half brightness with a small overshoot, pink fading out.
                float e = 1f - Mathf.Pow(1f - stateT, 3f);
                value = Mathf.Lerp(0.5f, 1f, e) + Mathf.Sin(stateT * Mathf.PI) * 0.12f;
                fail = 1f - e;
                break;

            case BrokenState.Brownout:
                // Sags and wobbles slowly, with a quick dip now and then.
                value = stepValue + (Mathf.PerlinNoise(seed + 3f, t * 3f) - 0.5f) * 0.3f;
                if (Mathf.PerlinNoise(seed + 21f, t * 25f) > 0.78f) value *= 0.5f;
                fail = 0.6f;
                break;

            default: // Stable — a faint fast strobe the more broken it is.
                value = Mathf.PerlinNoise(seed + 5f, t * 60f) > 0.55f ? 1f - 0.1f * brokenness : 1f;
                fail = 0f;
                break;
        }

        FlickerTint = Color.Lerp(Color.white, failTint, Mathf.Clamp01(fail));
        return Mathf.Max(0f, value);
    }

    private void NextBrokenState(float t)
    {
        switch (state)
        {
            case BrokenState.Stable:
                // More broken = more often out, less chatter-only.
                float r = Random.value;
                float outChance = Mathf.Lerp(0.15f, 0.45f, brokenness);
                if (r < outChance) EnterState(BrokenState.Out, t, RandomOutDuration());
                else if (r < outChance + 0.2f) EnterBrownout(t);
                else EnterState(BrokenState.Chatter, t, Random.Range(0.15f, Mathf.Lerp(0.5f, 1.4f, brokenness)));
                break;

            case BrokenState.Chatter:
                // Chatter sometimes ends with the tube giving up.
                if (Random.value < Mathf.Lerp(0.1f, 0.5f, brokenness)) EnterState(BrokenState.Out, t, RandomOutDuration());
                else EnterStable(t);
                break;

            case BrokenState.Out:
                flashesLeft = Random.Range(strikeFlashes.x, strikeFlashes.y + 1);
                flashLit = false;
                stepEnd = t;
                EnterState(BrokenState.Strike, t, float.PositiveInfinity); // ends itself after the last flash
                break;

            case BrokenState.Brownout:
                if (Random.value < 0.5f) EnterState(BrokenState.Chatter, t, Random.Range(0.1f, 0.4f));
                else EnterStable(t);
                break;

            default: // WarmUp
                EnterStable(t);
                break;
        }
    }

    private void EnterStable(float t)
    {
        float stableFor = Mathf.Lerp(9f, 1.2f, brokenness) * Random.Range(0.5f, 1.5f);
        EnterState(BrokenState.Stable, t, stableFor);
    }

    private void EnterBrownout(float t)
    {
        stepValue = Random.Range(0.35f, 0.7f);
        EnterState(BrokenState.Brownout, t, Random.Range(1f, 4f));
    }

    private void EnterState(BrokenState next, float t, float duration)
    {
        state = next;
        stateStart = t;
        stateEnd = t + duration;
        stepEnd = t;
    }

    private float RandomOutDuration() =>
        Random.Range(outDuration.x, outDuration.y) * Mathf.Lerp(0.6f, 1.4f, brokenness);

    private void PlayClick()
    {
        if (strikeClicks.Count == 0 || !SFXManager.Instance) return;
        SFXManager.Instance.PlayRandomClipIndependently(strikeClicks, clickVolume);
    }

    /// <summary>Fades the light on or off.</summary>
    public void SetOn(bool on, float duration = 0.2f)
    {
        IsOn = on;
        onTween.Stop();
        float target = on ? 1f : 0f;
        if (duration <= 0f) onAmount = target;
        else onTween = Tween.Custom(this, onAmount, target, duration, (l, v) => l.onAmount = v, Ease.OutQuad);
    }

    public void TweenIntensity(float target, float duration)
    {
        intensityTween.Stop();
        if (duration <= 0f) Intensity = target;
        else intensityTween = Tween.Custom(this, intensity, target, duration, (l, v) => l.Intensity = v, Ease.OutQuad);
    }

    protected override void BuildTexture()
    {
        int width = shape == Shape.Tube ? 256 : 128;
        int height = shape == Shape.Tube ? 64 : 128;
        float inner = shape == Shape.Tube ? tubeLength : 0f;

        Texture2D tex = GetTexture(width, height);
        var pixels = new Color32[width * height];

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            // -1..1 across the rect; the tube keeps a flat bright middle and fades over the rounded ends.
            float px = Mathf.Abs((x + 0.5f) / width * 2f - 1f);
            float py = Mathf.Abs((y + 0.5f) / height * 2f - 1f);
            float qx = Mathf.Max(0f, px - inner) / (1f - inner);
            float d = Mathf.Sqrt(qx * qx + py * py);

            // Reaches zero just inside the edge so the clamped border is dark.
            float b = Mathf.Pow(1f - Mathf.Clamp01(Mathf.InverseLerp(core, 0.97f, d)), falloff);
            b = b * b * (3f - 2f * b); // smooth the tail
            pixels[y * width + x] = new Color32(255, 255, 255, (byte)(b * 255f));
        }

        tex.SetPixels32(pixels);
        tex.Apply(false);
    }
}
