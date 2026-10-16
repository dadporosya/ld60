using UnityEngine;

/// <summary>
/// The sound of the pencil on a <see cref="PixelGrid"/>: one clip looped for as long as the board is being painted,
/// faded in and out so it never clicks.
///
/// <para>Optionally a second loop (<see cref="overdoseLoop"/>) takes over while the pencil paints overdone
/// (<see cref="SaturationZone.TooMuch"/>) cells or overdoses them — drawing a line a second time, say — and for
/// <see cref="overdoseHold"/> seconds after, so it holds along the whole retraced path instead of flickering cell by
/// cell. Without it the regular loop plays whatever is painted.</para>
///
/// <para>How loud it is can follow how saturated the cell under the pencil is (<see cref="volumeBySaturation"/>).</para>
///
/// <para>The board runs it (<see cref="Tick"/> once a frame); the loops play on AudioSources added to the board,
/// through the SFX mixer group.</para>
/// </summary>
[System.Serializable]
public class BoardDrawSfx
{
    [Tooltip("Looped for as long as the pencil paints this board. Empty: the board is silent.")]
    public AudioClip loop;
    [Range(0f, 1f)] public float volume = 1f;

    [Header("Volume by saturation")]
    [Tooltip("Saturation of the cell under the pencil (X, 0..1) → volume multiplier (Y, 0..1), on top of the " +
             "volumes here. Flat at 1: the saturation changes nothing. Match its keys to the zone boundaries in " +
             "the board's Saturation Settings (Perfect Min, Too Much Min).")]
    public AnimationCurve volumeBySaturation = AnimationCurve.Constant(0f, 1f, 1f);
    [Tooltip("Seconds the volume takes to follow the curve all the way from 0 to 1, so it glides from cell to " +
             "cell instead of jumping. 0: instantly.")]
    [Min(0f)] public float saturationSmoothing = 0.1f;
    [Tooltip("The overdose loop follows the curve too. Off: it always plays at its own volume.")]
    public bool overdoseFollowsCurve = true;

    [Header("Overdose (optional)")]
    [Tooltip("Looped instead while the pencil paints overdone cells or overdoses them (going over a line again). " +
             "Empty: the loop above plays whatever is painted.")]
    public AudioClip overdoseLoop;
    [Range(0f, 1f)] public float overdoseVolume = 1f;
    [Tooltip("Seconds the overdose loop holds on after the last overdone cell was painted, so it carries along " +
             "the whole retraced path instead of flickering between cells.")]
    [Min(0f)] public float overdoseHold = 0.15f;

    [Header("Fades")]
    [Tooltip("Seconds a loop takes to come in when painting starts (and when the two loops swap).")]
    [Min(0f)] public float fadeIn = 0.05f;
    [Tooltip("Seconds a loop takes to die away when painting stops (and when the two loops swap).")]
    [Min(0f)] public float fadeOut = 0.15f;
    [Tooltip("Start a loop from a random point of its clip every time it comes in, so strokes don't all open " +
             "on the same sound.")]
    public bool randomStart = true;

    private AudioSource _source;
    private AudioSource _overdoseSource;
    private float _gain;
    private float _overdoseGain;
    /// <summary><see cref="volumeBySaturation"/>'s multiplier, eased towards the cell under the pencil.</summary>
    private float _saturationGain = 1f;

    /// <summary>
    /// Call once a frame. <paramref name="painting"/>: the board was painted this frame;
    /// <paramref name="overdosing"/>: overdone cells were, within <see cref="overdoseHold"/>;
    /// <paramref name="saturation"/>: of the cell under the pencil (0..1), or negative when there is none.
    /// </summary>
    public void Tick(GameObject host, bool painting, bool overdosing, float saturation)
    {
        bool overdose = painting && overdosing && overdoseLoop;

        // Off a cell (a masked gap under a wide brush) the last cell's volume holds.
        if (painting && saturation >= 0f)
        {
            // A curve with no keys evaluates to 0, which would mute the board: treat it as flat.
            float target = volumeBySaturation != null && volumeBySaturation.length > 0
                ? Mathf.Clamp01(volumeBySaturation.Evaluate(Mathf.Clamp01(saturation)))
                : 1f;
            // A stroke starting from silence opens at its cell's volume instead of gliding there from the last stroke's.
            bool silent = _gain <= 0f && _overdoseGain <= 0f;
            _saturationGain = silent || saturationSmoothing <= 0f
                ? target
                : Mathf.MoveTowards(_saturationGain, target, Time.unscaledDeltaTime / saturationSmoothing);
        }

        Fade(host, ref _source, ref _gain, loop, painting && !overdose && loop, volume * _saturationGain);
        Fade(host, ref _overdoseSource, ref _overdoseGain, overdoseLoop, overdose,
            overdoseVolume * (overdoseFollowsCurve ? _saturationGain : 1f));
    }

    /// <summary>Cuts both loops off at once (the board was disabled).</summary>
    public void Silence()
    {
        _gain = 0f;
        _overdoseGain = 0f;
        if (_source) _source.Stop();
        if (_overdoseSource) _overdoseSource.Stop();
    }

    private void Fade(GameObject host, ref AudioSource source, ref float gain, AudioClip clip, bool on, float volume)
    {
        if (!on && gain <= 0f) return;

        // Unscaled: the pencil is an input device, and draws through pauses too.
        float time = on ? fadeIn : fadeOut;
        float target = on ? 1f : 0f;
        gain = time > 0f ? Mathf.MoveTowards(gain, target, Time.unscaledDeltaTime / time) : target;

        if (!source) source = CreateSource(host);
        // The clip may be swapped in the inspector mid-game.
        if (on && source.clip != clip)
        {
            source.Stop();
            source.clip = clip;
        }
        source.volume = gain * volume;

        if (gain <= 0f) source.Stop();
        else if (!source.isPlaying && source.clip)
        {
            if (randomStart) source.timeSamples = Random.Range(0, source.clip.samples);
            source.Play();
        }
    }

    private static AudioSource CreateSource(GameObject host)
    {
        AudioSource source = host.AddComponent<AudioSource>();
        source.outputAudioMixerGroup = AudioMixerManager.GetSFXGroup();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        return source;
    }
}
