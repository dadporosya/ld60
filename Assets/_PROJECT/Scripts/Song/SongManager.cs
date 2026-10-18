using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Picks Funeral Song's background music and hands it to the <see cref="BGMManager"/>: the start track once the
/// game starts, then, after each round's final cutscene (its level's win / loss dialogue), the track for how many
/// rounds have been played (<see cref="bgmByRound"/>) - whichever level it was, like
/// <see cref="LevelManager.PointsToPass"/>. Every track loops and becomes the BGMManager's default, so dialogues
/// that swap the music in come back to it. Just music: nothing ends when a track does.
/// Access via <c>SongManager.Instance</c>.
/// </summary>
public class SongManager : MonoBehaviour
{
    public static SongManager Instance;

    [Tooltip("Defaults to BGMManager.Instance.")]
    [SerializeField] private BGMManager bgmManager;

    [Tooltip("Played when the game starts (after the intro dialogue).")]
    [SerializeField] private BGMObject startBgm;
    [Tooltip("Seconds the start track fades in over.")]
    [SerializeField, Min(0f)] private float startFade = 1.5f;
    [Tooltip("Played once a round's final cutscene (win / loss dialogue) is over, by how many rounds have been " +
             "played this game: [0] after the first round, [1] after the second, ... - whichever level it was. " +
             "An empty entry, or a round past the end of the list: the music carries on as it is.")]
    [SerializeField] private List<BGMObject> bgmByRound = new List<BGMObject>();
    [Tooltip("Seconds a round's track crossfades in over once its final cutscene is over.")]
    [SerializeField, Min(0f)] private float levelFade = 1.5f;

    /// <summary>The track that just started.</summary>
    public UnityEvent<BGMObject> onBgmChanged = new UnityEvent<BGMObject>();

    /// <summary>The track last started through the manager; null before the first one, or after <see cref="Stop"/>.</summary>
    public BGMObject Current { get; private set; }

    private BGMManager Bgm => bgmManager ? bgmManager : bgmManager = BGMManager.Instance;

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance, setDontDestroy: false);
    }

    /// <summary>Starts <see cref="startBgm"/>.</summary>
    public void PlayStartBgm() => Play(startBgm, startFade);

    /// <summary>
    /// The final cutscene of round <paramref name="roundsPlayed"/> (1 for the first round played this game) is over:
    /// starts <see cref="bgmByRound"/>[<paramref name="roundsPlayed"/> - 1]. No track for it leaves the music as it is.
    /// </summary>
    public void PlayAfterRound(int roundsPlayed)
    {
        int index = roundsPlayed - 1;
        if (index < 0 || index >= bgmByRound.Count) return;

        BGMObject bgm = bgmByRound[index];
        if (bgm) Play(bgm, levelFade);
    }

    /// <summary>Crossfades into <paramref name="bgm"/>, looping, and makes it the BGMManager's default.</summary>
    public void Play(BGMObject bgm, float fade)
    {
        if (!bgm)
        {
            h.Out("SongManager: no track to play.");
            return;
        }

        if (!Bgm)
        {
            h.Out($"SongManager: no BGMManager to play {bgm.name}.");
            return;
        }

        Current = bgm;
        Bgm.PlayBGMAsNewDefault(bgm, fade);
        onBgmChanged?.Invoke(bgm);
    }

    /// <summary>Fades the music out over <paramref name="fade"/> seconds.</summary>
    public void Stop(float fade = 0f)
    {
        Current = null;
        if (Bgm) Bgm.FadeOutMusic(fade);
    }
}
