using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How a <see cref="BGMObject"/> is played back by <see cref="BGMManager"/>.
/// <para><b>Default</b> — the track is played as a single looping clip (<see cref="BGMObject.full"/>).</para>
/// <para><b>Advanced</b> — <see cref="BGMObject.intro"/> is played once, then <see cref="BGMObject.loop"/>
/// starts sample-accurately and loops forever; <see cref="BGMManager.StartOutro"/> fades into
/// <see cref="BGMObject.outro"/>.</para>
/// </summary>
public enum BGMPlayType
{
    Default,
    Advanced
}

/// <summary>
/// A background-music track. Holds either a single full clip (Default playback) or an
/// intro / loop / outro triple (Advanced playback). Created via
/// <b>Assets > Create > music > bgmOjbect</b>.
/// </summary>
[CreateAssetMenu(fileName = "BGMObject", menuName = "music/bgmOjbect")]
public class BGMObject : ScriptableObject
{
    [Tooltip("Optional per-track override of BGMManager.playType. Leave as None to use the manager's setting.")]
    public PlayTypeOverride playTypeOverride = PlayTypeOverride.UseManager;

    [Header("Default playback")]
    [Tooltip("The whole track as one clip. Used (and looped) in Default play type.")]
    public AudioClip full;

    [Header("Advanced playback")]
    [Tooltip("Played once at the start. Optional — without it playback starts straight on the loop clip.")]
    public AudioClip intro;

    [Tooltip("Looped forever after the intro finishes.")]
    public AudioClip loop;

    [Tooltip("Faded in by BGMManager.StartOutro(). Optional.")]
    public AudioClip outro;

    public enum PlayTypeOverride
    {
        UseManager,
        Default,
        Advanced
    }

    /// <summary>Resolves the play type for this track against the manager's default.</summary>
    public BGMPlayType ResolvePlayType(BGMPlayType managerDefault)
    {
        switch (playTypeOverride)
        {
            case PlayTypeOverride.Default: return BGMPlayType.Default;
            case PlayTypeOverride.Advanced: return BGMPlayType.Advanced;
            default: return managerDefault;
        }
    }

    /// <summary>Clip used for Default playback (falls back to the loop clip).</summary>
    public AudioClip DefaultClip => full ? full : loop;

    /// <summary>Clip looped in Advanced playback (falls back to the full clip).</summary>
    public AudioClip LoopClip => loop ? loop : full;

    /// <summary>True when this object holds enough data to be played in Advanced mode.</summary>
    public bool CanPlayAdvanced => LoopClip != null;

    /// <summary>
    /// Seconds one complete pass of this track takes: the full clip when there is one, otherwise the
    /// intro plus a single pass of the loop. This is how long <see cref="BGMManager.PlayBGMOnce"/>
    /// sounds for, so it is what a caller waiting the track out should time itself against.
    /// </summary>
    public float OnePassLength
    {
        get
        {
            if (full) return full.length;

            float length = intro ? intro.length : 0f;
            if (LoopClip) length += LoopClip.length;
            return length;
        }
    }

    /// <summary>Every clip referenced by this object — used for preloading.</summary>
    public IEnumerable<AudioClip> AllClips()
    {
        if (full) yield return full;
        if (intro) yield return intro;
        if (loop) yield return loop;
        if (outro) yield return outro;
    }
}
