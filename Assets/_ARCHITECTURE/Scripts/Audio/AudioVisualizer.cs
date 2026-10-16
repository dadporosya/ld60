/*
    https://rumpledcode.com/
*/
using UnityEngine;

/// <summary>
/// Drives a row of bars from the spectrum of whatever is playing.
///
/// <para><b>Web builds.</b> <c>AudioSource.GetSpectrumData</c> is not implemented in Unity's Web
/// (WebGL) player — it silently fills the buffer with zeros, so the bars stand still in the browser
/// while working fine in the editor. There, the spectrum is computed here instead: the raw PCM window
/// around the playhead is read with <c>AudioClip.GetData</c> (which Web DOES support for clips whose
/// Load Type is "Decompress on Load", as the BGM clips are) and run through the FFT below. Bin layout
/// matches Unity's: <c>spectrumData[i]</c> covers <c>i * (sampleRate / 2) / spectrumData.Length</c> Hz,
/// so bar heights read the same in both paths and <see cref="amplification"/> stays tuned.</para>
/// </summary>
public class AudioVisualizer : MonoBehaviour
{
    /// <summary>Where the spectrum numbers come from.</summary>
    public enum SpectrumSourceMode
    {
        /// <summary>Unity's GetSpectrumData everywhere except Web builds, where the FFT below is used.</summary>
        Auto = 0,
        /// <summary>Always Unity's GetSpectrumData (dead bars in a browser).</summary>
        UnitySpectrum = 1,
        /// <summary>Always the built-in FFT over the clip's samples. Use it to preview the Web path in the editor.</summary>
        ClipSamplesFFT = 2
    }

    [Header("References")]
    [Tooltip("Manual audio source. Ignored when Use BGM Manager is on.")]
    public AudioSource audioSource;

    [Tooltip("When enabled, automatically uses BGMManager's current audio source instead of the manual reference.")]
    public bool useBGMManager;

    public Transform[] bars;
 
    [Header("Settings")]
    public FrequencyFocusWindow frequencyFocusWindow;
    public float amplification = 1.0f;
    public float baseHeight = 0.0f;
    [Tooltip("Maximum bar height (Y scale). 0 or negative = unlimited.")]
    public float maxBarHeight = 0f;
    public FFTWindow fftWindow;
    public bool useDecibels;

    [Header("Web fallback")]
    [Tooltip("Auto = Unity's spectrum in the editor and standalone, own FFT over the clip's samples in Web builds " +
             "(where Unity's spectrum is not implemented). Set to Clip Samples FFT to preview the Web path here.")]
    public SpectrumSourceMode spectrumSourceMode = SpectrumSourceMode.Auto;

    [Tooltip("Extra multiplier applied to the fallback FFT only, in case it reads slightly hotter or quieter " +
             "than Unity's own spectrum. 1 = no correction.")]
    public float fallbackGain = 1f;

    [Header("State")]
    public float[] spectrumData;

    // FFT scratch. The transform is twice the size of spectrumData so bin i lands on the same
    // frequency Unity's GetSpectrumData would put there.
    private float[] _samples;   // interleaved PCM straight out of AudioClip.GetData
    private float[] _re;
    private float[] _im;
    private float[] _window;    // cached window function coefficients
    private FFTWindow _windowKind = (FFTWindow)(-1);
    private bool _fallbackWarned;

    void Awake()
    {
        // Must be a power of 2 number, between 64 and 8192
        spectrumData = new float[4096];
    }

    /// <summary>
    /// Resolves the audio source to sample from — either the BGMManager's current source or the
    /// manually assigned one.
    /// </summary>
    private AudioSource ResolveSource()
    {
        if (useBGMManager && BGMManager.Instance)
            return BGMManager.Instance.CurrentAudioSource;

        return audioSource;
    }

    /// <summary>True when the spectrum has to be computed here instead of asked of Unity.</summary>
    private bool UseSampleFallback
    {
        get
        {
            if (spectrumSourceMode == SpectrumSourceMode.ClipSamplesFFT) return true;
            if (spectrumSourceMode == SpectrumSourceMode.UnitySpectrum) return false;

#if UNITY_WEBGL && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }
    }

    void Update()
    {
        if (bars == null || bars.Length == 0) return;

        AudioSource source = ResolveSource();
        if (!source || !source.isPlaying) return;

        if (UseSampleFallback)
        {
            // A failed read (clip still streaming in, not decompressed on load) leaves the previous
            // frame's bars up rather than collapsing them to zero.
            if (!FillSpectrumFromClip(source)) return;
        }
        else
        {
            source.GetSpectrumData(spectrumData, 0, fftWindow);
        }

        var blockSize = Mathf.Max(1, spectrumData.Length / bars.Length / (int)frequencyFocusWindow);
        for (int i = 0; i < bars.Length; ++i)
        {
            float sum = 0;
            for (int j = 0; j < blockSize; j++)
            {
                sum += spectrumData[i * blockSize + j];
            }
            sum /= blockSize;
            float amplitude = Mathf.Clamp(sum, 1e-7f, 1f);
            var scale = bars[i].localScale;
            if (useDecibels)
            {
                scale.y = -Mathf.Log10(amplitude) * amplification / 200;
            }
            else
            {
                scale.y = sum * amplification + baseHeight;
            }
            if (maxBarHeight > 0f)
                scale.y = Mathf.Min(scale.y, maxBarHeight);
            bars[i].localScale = scale;
        }
    }

    // ──────────────────────────────────────────────
    // Sample-based spectrum (the Web path)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Reads the PCM window that is playing right now and fills <see cref="spectrumData"/> with its
    /// magnitude spectrum. False = nothing usable was read and the caller should keep the last frame.
    /// </summary>
    private bool FillSpectrumFromClip(AudioSource source)
    {
        AudioClip clip = source.clip;
        if (!clip || clip.loadState != AudioDataLoadState.Loaded) return false;

        int bins = spectrumData.Length;
        int n = bins * 2;                       // FFT size: bins usable bins come out of it
        int channels = Mathf.Max(1, clip.channels);
        if (clip.samples < n) return false;     // clip too short to fill one window

        int needed = n * channels;
        if (_samples == null || _samples.Length != needed) _samples = new float[needed];
        if (_re == null || _re.Length != n) { _re = new float[n]; _im = new float[n]; }

        // Clamped rather than wrapped: the seam of a wrapped read is a click the FFT would show as
        // broadband noise across every bar.
        int offset = Mathf.Clamp(Mathf.FloorToInt(source.time * clip.frequency), 0, clip.samples - n);

        if (!clip.GetData(_samples, offset))
        {
            if (!_fallbackWarned)
            {
                _fallbackWarned = true;
                h.Out("AudioVisualizer: AudioClip.GetData failed for", clip.name,
                      "- in Web builds the clip's Load Type has to be 'Decompress on Load' for the visualizer to read it.");
            }
            return false;
        }

        BuildWindow(n);

        // Channel 0 only, windowed — the same channel the GetSpectrumData call above asks for, so
        // the two paths describe the same signal.
        for (int i = 0; i < n; i++)
        {
            _re[i] = _samples[i * channels] * _window[i];
            _im[i] = 0f;
        }

        FFT(_re, _im);

        // 1/n normalisation, which puts a full-scale sine at ~0.5 in its bin — the scale Unity (through
        // FMOD) reports — so amplification does not have to be retuned per platform. If the browser bars
        // still come out consistently taller or shorter than in the editor, correct it with fallbackGain
        // rather than by re-tuning amplification, which both paths share.
        float norm = 1f / n * Mathf.Max(0f, fallbackGain);
        for (int i = 0; i < bins; i++)
        {
            float re = _re[i];
            float im = _im[i];
            spectrumData[i] = Mathf.Sqrt(re * re + im * im) * norm;
        }

        return true;
    }

    /// <summary>Caches the coefficients of <see cref="fftWindow"/> for an n-sample window.</summary>
    private void BuildWindow(int n)
    {
        if (_window != null && _window.Length == n && _windowKind == fftWindow) return;

        _window = new float[n];
        _windowKind = fftWindow;

        for (int i = 0; i < n; i++)
        {
            float t = (float)i / (n - 1);           // 0..1 across the window
            float w;
            switch (fftWindow)
            {
                case FFTWindow.Triangle:
                    w = 1f - Mathf.Abs(2f * t - 1f);
                    break;
                case FFTWindow.Hamming:
                    w = 0.54f - 0.46f * Mathf.Cos(2f * Mathf.PI * t);
                    break;
                case FFTWindow.Hanning:
                    w = 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * t));
                    break;
                case FFTWindow.Blackman:
                    w = 0.42f - 0.5f * Mathf.Cos(2f * Mathf.PI * t) + 0.08f * Mathf.Cos(4f * Mathf.PI * t);
                    break;
                case FFTWindow.BlackmanHarris:
                    w = 0.35875f
                        - 0.48829f * Mathf.Cos(2f * Mathf.PI * t)
                        + 0.14128f * Mathf.Cos(4f * Mathf.PI * t)
                        - 0.01168f * Mathf.Cos(6f * Mathf.PI * t);
                    break;
                default: // FFTWindow.Rectangular
                    w = 1f;
                    break;
            }
            _window[i] = w;
        }
    }

    /// <summary>
    /// In-place iterative radix-2 Cooley-Tukey FFT. Length must be a power of two — it always is here,
    /// since it is twice <see cref="spectrumData"/>'s power-of-two length.
    /// </summary>
    private static void FFT(float[] re, float[] im)
    {
        int n = re.Length;

        // Bit-reversal permutation.
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;

            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            float ang = -2f * Mathf.PI / len;
            float wRe = Mathf.Cos(ang);
            float wIm = Mathf.Sin(ang);

            for (int i = 0; i < n; i += len)
            {
                float curRe = 1f;
                float curIm = 0f;

                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k;
                    int b = i + k + len / 2;

                    float tRe = re[b] * curRe - im[b] * curIm;
                    float tIm = re[b] * curIm + im[b] * curRe;

                    re[b] = re[a] - tRe;
                    im[b] = im[a] - tIm;
                    re[a] += tRe;
                    im[a] += tIm;

                    float nextRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = nextRe;
                }
            }
        }
    }
}
 
public enum FrequencyFocusWindow
{
    Entire = 1,
    FirstHalf = 2,
    FirstQuarter = 4,
    FirstEight = 8,
    FirstSixteenth = 16
}
