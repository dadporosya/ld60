using System;
using UnityEngine;

/// <summary>
/// Which band a cell's saturation falls into. Saturation only ever grows, so a cell only ever moves
/// forward through these (possibly skipping one in a single frame on a fast rework).
/// </summary>
public enum SaturationZone
{
    Empty,      // never touched
    NotEnough,  // white → light red: does nothing
    Perfect,    // red: grants progress
    TooMuch     // dark red: costs health
}

/// <summary>How a cell the pencil crosses over (see <see cref="PixelGrid.Overdose"/>) changes colour.</summary>
public enum CrossColorMode
{
    Fixed,    // jumps to a shade around the Too Much threshold
    Additive  // gets a fixed amount of saturation on top of what it has
}

/// <summary>
/// Tuning for how fast cells saturate, where the zone boundaries lie and what colour each saturation is.
/// Saturation is normalized: 0 = untouched, 1 = fully overdone.
/// </summary>
[Serializable]
public class SaturationSettings
{
    [Header("Speed")]
    [Tooltip("Saturation per second while the pencil rests on a cell during that cell's first stroke.")]
    [Min(0f)] public float baseRate = 0.6f;

    [Tooltip("The k-th time the pencil works a cell (new stroke over it, or crossing back over already " +
             "painted pixels) its rate is baseRate * k^reworkExponent (2 → 1x, 4x, 9x…).")]
    [Min(0f)] public float reworkExponent = 2f;

    [Tooltip("Seconds a painted cell must be left alone before crossing it again counts as a new layer. " +
             "Keeps the pencil wobbling on a cell border from compounding the speed-up every frame.")]
    [Min(0f)] public float reworkDelay = 0.3f;

    [Tooltip("Flat saturation a cell gets when the pencil only passes through it between frames " +
             "(fast strokes), scaled by the same rework factor. Keeps quick lines gap-free.")]
    [Min(0f)] public float passSaturation = 0.05f;

    [Header("Overlap")]
    [Tooltip("In cells, on top of the brush radius. A stroke touching its own paint only counts as crossing " +
             "its line once the tip has been farther than this from that cell — so wobbling back one cell " +
             "at a slow pace doesn't.")]
    [Min(0)] public int selfOverlapGap = 2;

    [Tooltip("How a crossed cell changes colour. Fixed: it jumps to a shade around Too Much Min (Overdose " +
             "Spread, Cross Under Chance/Min/Max). Additive: it gets Cross Add Saturation on top of what it " +
             "has, so every crossing makes it a bit darker. The crossing costs health either way.")]
    public CrossColorMode crossColorMode = CrossColorMode.Fixed;

    [Tooltip("Additive mode: saturation (0..1) each crossing adds to a crossed cell.")]
    [Range(0f, 1f)] public float crossAddSaturation = 0.15f;

    [Tooltip("Fixed mode: a crossed (overdosed) cell gets a random saturation from Too Much Min up to this much above it, " +
             "so the crossing shows as a few shades of the first harmful colour rather than one flat one.")]
    [Range(0f, 1f)] public float overdoseSpread = 0.1f;

    [Tooltip("Fixed mode: chance a crossed cell gets a shade just below Too Much Min instead (see Cross Under Min/Max), " +
             "so some of the crossing shows lighter than the overdone colour and doesn't overdose the cell.")]
    [Range(0f, 1f)] public float crossUnderChance = 0.3f;

    [Tooltip("Fixed mode: how far below Too Much Min such a lighter crossed cell lands, at least (saturation, 0..1).")]
    [Range(0f, 1f)] public float crossUnderMin = 0.02f;

    [Tooltip("Fixed mode: how far below Too Much Min such a lighter crossed cell lands, at most (saturation, 0..1).")]
    [Range(0f, 1f)] public float crossUnderMax = 0.15f;

    [Header("Zones")]
    [Range(0f, 1f)] public float perfectMin = 0.45f;
    [Range(0f, 1f)] public float tooMuchMin = 0.75f;

    [Header("Colours")]
    [Tooltip("Colour over saturation 0..1. Set the gradient mode to Fixed for hard, stepped zones.")]
    public Gradient colors = DefaultGradient();

    [Tooltip("Colour of untouched cells — transparent so the drawing area sprite shows through.")]
    public Color emptyColor = new Color(1f, 1f, 1f, 0f);

    public SaturationZone ZoneOf(float saturation)
    {
        if (saturation <= 0f) return SaturationZone.Empty;
        if (saturation >= tooMuchMin) return SaturationZone.TooMuch;
        if (saturation >= perfectMin) return SaturationZone.Perfect;
        return SaturationZone.NotEnough;
    }

    /// <summary>Speed multiplier for a cell on its <paramref name="layers"/>-th working (1-based).</summary>
    public float ReworkFactor(int layers) => Mathf.Pow(Mathf.Max(1, layers), reworkExponent);

    public Color ColorOf(float saturation)
    {
        if (saturation <= 0f) return emptyColor;

        Color c = colors.Evaluate(Mathf.Clamp01(saturation));
        c.a = 1f; // pencil, not brush: a touched pixel is always fully opaque
        return c;
    }

    private static Gradient DefaultGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(1f, 0.6f, 0.6f), 0.3f),   // light red
                new GradientColorKey(new Color(0.9f, 0.1f, 0.1f), 0.6f), // red (perfect)
                new GradientColorKey(new Color(0.3f, 0f, 0f), 1f),       // bloody dark red
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }
}
