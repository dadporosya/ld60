using UnityEngine;
using PrimeTween;
using VolFx;

/// <summary>
/// <see cref="VolumeManager"/> specialised for the VolFx <see cref="JpegVol"/> override
/// (blocky JPEG compression / datamosh look). Caches the override and exposes typed helpers.
///
/// <see cref="JpegVol"/> has no single "weight" parameter — <c>_intensity</c> is the primary driver
/// (0 = off), exposed here as <see cref="Intensity"/>.
/// </summary>
[AddComponentMenu("MyFriendPetya/Rendering/Jpeg Volume Manager")]
public class JpegVolumeManager : VolumeManager
{
    public static JpegVolumeManager Instance;
    private JpegVol jpeg;

    /// <summary>The Jpeg override (lazily fetched from the profile).</summary>
    public JpegVol Jpeg => jpeg != null ? jpeg : (jpeg = Get<JpegVol>());

    protected override void Awake()
    {
        base.Awake();
        jpeg = Get<JpegVol>();
        h.CreateStaticInstance(this, ref Instance);
    }

    // ---------------------------------------------------------------- Compression

    /// <summary>Primary effect amount (JpegVol._intensity, -5..5, 0 = off).</summary>
    public float Intensity
    {
        get => Jpeg != null ? Jpeg._intensity.value : 0f;
        set => SetFloat(Jpeg?._intensity, value);
    }

    public void SetIntensity(float value)     => SetFloat(Jpeg?._intensity, value);     // -5..5
    public void SetBlockSize(float value)     => SetFloat(Jpeg?._blockSize, value);     // 0.1..200
    public void SetQuantization(float value)  => SetFloat(Jpeg?._quantization, value);  // 2..32
    public void SetQuantSpread(float value)   => SetFloat(Jpeg?._quantSpread, value);   // 0..3
    public void SetDistortionScale(float value) => SetFloat(Jpeg?._distortionScale, value); // 0..7

    // ---------------------------------------------------------------- YCbCr

    public void SetApplyToY(float value)      => SetFloat(Jpeg?._applyToY, value);      // 0..20
    public void SetApplyToChroma(float value) => SetFloat(Jpeg?._applyToChroma, value); // 0..20
    public void SetApplyToGlitch(float value) => SetFloat(Jpeg?._applyToGlitch, value); // 0..20

    // ---------------------------------------------------------------- Animation

    public void SetFps(float value)      => SetFloat(Jpeg?._fps, value);      // 0..60
    public void SetFpsBreak(float value) => SetFloat(Jpeg?._fpsBreak, value); // 0..1

    // ---------------------------------------------------------------- Scanlines

    public void SetScanlineDrift(float value) => SetFloat(Jpeg?._scanlineDrift, value); // 0..1
    public void SetScanlineRes(float value)   => SetFloat(Jpeg?._scanlineRes, value);   // 120..720
    public void SetScanlinesFps(float value)  => SetFloat(Jpeg?._scanlinesFps, value);  // 1..120

    // ---------------------------------------------------------------- Channel shift

    public void SetChannelShiftPow(float value)    => SetFloat(Jpeg?._channelShiftPow, value);    // -10..10
    public void SetChannelShiftX(float value)      => SetFloat(Jpeg?._channelShiftX, value);      // -1..1
    public void SetChannelShiftY(float value)      => SetFloat(Jpeg?._channelShiftY, value);      // -1..1
    public void SetChannelShiftSpread(float value) => SetFloat(Jpeg?._channelShiftSpread, value); // 0..1

    // ---------------------------------------------------------------- Noise

    public void SetNoise(float value)          => SetFloat(Jpeg?._noise, value);      // -1.37..1.75
    public void SetNoiseBilinear(bool enabled) => SetOverride(Jpeg?._noiseBilinear, enabled);
    public void SetDistortionTexture(Texture tex) => SetOverride(Jpeg?._distortionTex, tex);

    // ---------------------------------------------------------------- Convenience tweens

    public Tween TweenIntensity(float to, float duration, Ease ease = Ease.Default)
        => TweenFloat(Jpeg?._intensity, to, duration, ease);

    public Tween TweenBlockSize(float to, float duration, Ease ease = Ease.Default)
        => TweenFloat(Jpeg?._blockSize, to, duration, ease);

    public Tween TweenQuantization(float to, float duration, Ease ease = Ease.Default)
        => TweenFloat(Jpeg?._quantization, to, duration, ease);

    /// <summary>
    /// A quick compression burst: snap intensity to <paramref name="peak"/>, then ease back to
    /// <paramref name="settle"/>. Good for hits, glitchy transitions, corruption stingers.
    /// </summary>
    public Tween Burst(float peak = 3f, float settle = 0f, float duration = 0.35f, Ease ease = Ease.OutQuad)
    {
        SetFloat(Jpeg?._intensity, peak);
        return Tween.Custom(this, peak, settle, duration, (m, v) => SetFloat(m.Jpeg?._intensity, v), ease);
    }
}
