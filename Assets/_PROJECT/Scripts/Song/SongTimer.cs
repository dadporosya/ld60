using PrimeTween;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The gramophone's song, and the game's clock: when it stops playing, the hero runs out of time.
/// Plays once (never loops) on its own AudioSource and fires <see cref="onSongEnded"/> when the
/// clip runs out - not when it is stopped on purpose (<see cref="Stop"/>).
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class SongTimer : MonoBehaviour
{
    public static SongTimer Instance;

    [Tooltip("Empty: the AudioSource's own clip.")]
    [SerializeField] private AudioClip song;
    [SerializeField] private bool playOnStart = false;

    public UnityEvent onSongStarted = new UnityEvent();
    public UnityEvent onSongEnded = new UnityEvent();

    private AudioSource source;
    private Tween volumeTween;
    private float baseVolume = 1f;

    public bool HasStarted { get; private set; }
    public bool HasEnded { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsPlaying => HasStarted && !HasEnded && !IsPaused;

    public float Length => source && source.clip ? source.clip.length : 0f;
    public float Elapsed => HasEnded ? Length : source ? source.time : 0f;
    public float Remaining => Mathf.Max(0f, Length - Elapsed);
    /// <summary>0 at the start of the song, 1 once it is over.</summary>
    public float Progress => Length > 0f ? Mathf.Clamp01(Elapsed / Length) : 0f;

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance, setDontDestroy: false);
        source = GetComponent<AudioSource>();
        source.loop = false;
        source.playOnAwake = false;
        if (song) source.clip = song;
        baseVolume = source.volume;
    }

    private void Start()
    {
        if (playOnStart) Play();
    }

    private void Update()
    {
        // isPlaying also goes false on Pause, which IsPaused covers.
        if (HasStarted && !HasEnded && !IsPaused && !source.isPlaying) End();
    }

    /// <summary>Starts the song from the beginning.</summary>
    public void Play()
    {
        if (!source.clip)
        {
            h.Out("SongTimer: no song assigned.");
            return;
        }

        volumeTween.Stop();
        source.volume = baseVolume;
        source.time = 0f;
        source.Play();
        HasStarted = true;
        HasEnded = false;
        IsPaused = false;
        onSongStarted?.Invoke();
    }

    public void Pause()
    {
        if (!IsPlaying) return;
        IsPaused = true;
        source.Pause();
    }

    public void Resume()
    {
        if (!HasStarted || HasEnded || !IsPaused) return;
        IsPaused = false;
        source.UnPause();
    }

    /// <summary>Moves the needle by <paramref name="seconds"/> (negative: rewinds).</summary>
    public void Seek(float seconds)
    {
        if (!HasStarted || HasEnded || !source.clip) return;
        source.time = Mathf.Clamp(source.time + seconds, 0f, Mathf.Max(0f, Length - 0.01f));
    }

    /// <summary>Stops the song WITHOUT it counting as running out (the good ending), fading over <paramref name="fade"/>.</summary>
    public void Stop(float fade = 0f)
    {
        HasEnded = true;
        IsPaused = false;
        volumeTween.Stop();
        if (fade > 0f && source.isPlaying)
            volumeTween = Tween.AudioVolume(source, 0f, fade).OnComplete(source, s => s.Stop());
        else source.Stop();
    }

    private void End()
    {
        HasEnded = true;
        h.Out("SongTimer: the song is over.");
        onSongEnded?.Invoke();
    }
}
