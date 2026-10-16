using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Background music manager.
/// <para>Supports two playback modes (<see cref="BGMPlayType"/>):</para>
/// <list type="bullet">
/// <item><b>Default</b> — a single clip is crossfaded in and looped (legacy behaviour).</item>
/// <item><b>Advanced</b> — a <see cref="BGMObject"/>'s intro clip is played once and its loop clip is
/// scheduled to start sample-accurately right after it, looping forever. <see cref="StartOutro()"/>
/// then fades into the outro clip.</item>
/// </list>
/// Playback runs on a small pool of AudioSources so a crossfade, a scheduled loop and an outro can
/// all overlap without stealing each other's source.
///
/// <para>The manager also owns a <b>default track</b> — the music the game returns to. It starts out as
/// the track assigned in the inspector, one-shot playback (<see cref="PlayBGMOnce"/>,
/// <see cref="PlayMusicOnce"/>, an outro) crossfades back into its LOOP when it finishes, and it can be
/// replaced at runtime — a cutscene whose theme is meant to stick calls <see cref="SetDefaultBGM"/>,
/// <see cref="PlayBGMAsNewDefault"/>, or passes becomeDefault to a one-shot.</para>
/// </summary>
public class BGMManager : AudioManagerBase
{
    public static BGMManager Instance; // Instance

    [Header("Tracks")]
    public AudioClip deafultBGMusic;
    public List<AudioClip> bgTracks = new List<AudioClip>();

    [Tooltip("Optional BGMObject played on Start instead of deafultBGMusic.")]
    public BGMObject defaultBGMObject;

    [Tooltip("BGMObjects preloaded on Start (and usable by PlayRandomBgmObject).")]
    public List<BGMObject> bgmObjects = new List<BGMObject>();

    private string defaultBGMusicPath = "Audio/Music/bgMusicTest";

    [Header("Playback")]
    [Tooltip("Default: play a track as one looping clip. Advanced: intro clip → looped loop clip (+ outro).")]
    public BGMPlayType playType = BGMPlayType.Advanced;

    [Tooltip("Seconds of lead time given to the audio thread when scheduling the intro/loop chain.")]
    [SerializeField, Range(0.01f, 0.5f)] private float scheduleLead = 0.1f;

    [Tooltip("Schedule lead used when a clip is imported as Streaming. Streamed clips have to fill their " +
             "first buffer from disk before they can sound, so they need more head-room than in-memory " +
             "clips or a sample-accurate intro->loop handover can start late.")]
    [SerializeField, Range(0.05f, 1f)] private float streamingScheduleLead = 0.3f;

    [Tooltip("Fade-in duration for the track started on Start(). Set to 0 to start the music instantly at full volume, with no fade in.")]
    [SerializeField, Min(0f)] private float startTrackFadeTime = 1.5f;

    [Tooltip("Start the default track automatically on Start(). Turn off to stay silent until something calls a Play* method (or PlayDefaultTrack()). Tracks are still preloaded either way.")]
    [SerializeField] private bool playOnStart = true;

    [Tooltip("When a one-shot track (PlayBGMOnce / PlayMusicOnce / an outro) finishes, crossfade back into the LOOP of the default track. Individual calls can override this.")]
    [SerializeField] private bool returnToDefaultWhenFinished = true;

    [Tooltip("Seconds the music still playing when this scene is left takes to fade out. It carries on over the scene change while the next scene's music fades in (that scene's fade-in time), which makes the crossfade. 0: cut off with the scene.")]
    [SerializeField, Min(0f)] private float sceneChangeFadeOut = 1.5f;

    [Header("Volume")]
    [Tooltip("Volume the background music plays at. All fades target this value instead of 1.")]
    [SerializeField, Range(0f, 1f)] private float bgmVolume = 1f;

    [Header("Mute & Hotkeys")]
    [Tooltip("Enable hotkeys to toggle music mute.")]
    [SerializeField] private bool enableMuteHotkeys = true;

    [Tooltip("List of hotkeys that toggle music mute.")]
    [SerializeField] private List<KeyCode> muteHotkeys = new List<KeyCode> { KeyCode.M };

#if ENABLE_INPUT_SYSTEM
    [Tooltip("Optional Input System keys that toggle music mute.")]
    [SerializeField] private List<Key> muteKeys = new List<Key>();
#endif

    [Tooltip("Whether the background music is currently muted.")]
    [SerializeField] private bool isMuted = false;

    [Tooltip("Keep mute state across scene changes during the play session.")]
    [SerializeField] private bool persistAcrossScenes = true;

    /// <summary>Raised when the music mute state changes.</summary>
    public event Action<bool> OnMuteChanged;

    /// <summary>Raised when the clip started by <see cref="StartOutro()"/> has finished playing.</summary>
    public event Action OnOutroFinished;

    /// <summary>The BGMObject currently playing (null when a bare AudioClip is playing).</summary>
    public BGMObject CurrentBGMObject { get; private set; }

    /// <summary>
    /// The clip that is audible right now (the intro while it plays, then the loop), or null when
    /// nothing is playing. Together with <see cref="CurrentBGMObject"/> this is what a cutscene needs
    /// to remember before swapping the music out and put back afterwards.
    /// </summary>
    public AudioClip CurrentClip => current ? current.clip : null;

    /// <summary>True while any music source is audible.</summary>
    public bool IsPlaying => current && current.isPlaying;

    /// <summary>The AudioSource that is audible right now, or null when nothing is playing.
    /// Used by <see cref="AudioVisualizer"/> and <see cref="MediaPlayerManager"/> to read
    /// spectrum data or control playback.</summary>
    public AudioSource CurrentAudioSource => current;

    /// <summary>
    /// Playback position, in seconds, of the clip that is audible right now — 0 when nothing plays.
    /// Together with <see cref="CurrentBGMObject"/> this is what the save system stores so a loaded
    /// game can pick the music up where it left off.
    /// </summary>
    public float CurrentClipTime => current && current.clip ? current.time : 0f;

    /// <summary>True while the outro clip started by <see cref="StartOutro()"/> is playing.</summary>
    public bool IsPlayingOutro { get; private set; }

    // DSP time at which the last-started NON-LOOPING playback (a one-shot track, an outro, a playlist
    // entry) runs out of samples. Kept in audio time on purpose: it is decided the moment the sources
    // are scheduled, it keeps ticking while the game is paused (the music does too), and it does not
    // drift the way "clip length counted down from now" does.
    [System.NonSerialized] private double scheduledEndDsp = -1d;

    /// <summary>
    /// Seconds until the music that is playing runs out — the honest answer to "how much of this track
    /// is left", asked by anything that wants to hand over exactly at the end.
    ///
    /// <para>-1 when there is nothing to end: the current track loops forever, or nothing was ever
    /// started. 0 once the last non-looping track has already finished, so a caller that arrives late
    /// is told to act now instead of waiting for something that is over.</para>
    /// </summary>
    public float SecondsUntilTrackEnds
    {
        get
        {
            if (scheduledEndDsp < 0d) return -1f;
            return Mathf.Max(0f, (float)(scheduledEndDsp - AudioSettings.dspTime));
        }
    }

    /// <summary>Records when the non-looping playback just scheduled will run out.</summary>
    private void NoteScheduledEnd(double endDsp) => scheduledEndDsp = endDsp;

    /// <summary>Forgets any scheduled end — what is playing now loops forever, or nothing plays at all.</summary>
    private void ClearScheduledEnd() => scheduledEndDsp = -1d;

    // ──────────────────────────────────────────────
    // The default track (runtime, replaceable)
    // ──────────────────────────────────────────────

    // The inspector fields above are only the STARTING default. The track the manager falls back to
    // can change during a run — a castle-capture cutscene, say, hands the game a new theme that every
    // later "back to the music" is supposed to land on — so the live default is kept here instead of
    // being read off the serialized fields.
    [System.NonSerialized] private BGMObject defaultObjectRuntime;
    [System.NonSerialized] private AudioClip defaultClipRuntime;
    [System.NonSerialized] private bool defaultInitialised;

    /// <summary>The BGMObject the manager currently treats as the default track (null when the default is a bare clip).</summary>
    // ──────────────────────────────────────────────
    // Registry of tracks that have actually been heard
    // ──────────────────────────────────────────────

    // A save stores a track by NAME, and whoever reads it back has to turn that name into the asset
    // again. Most tracks are not reachable from this manager: a cutscene's theme is a reference held
    // by the cutscene, so the manager only ever sees it as an argument. Every track that goes past
    // here is therefore remembered, which makes a same-session save/load able to find it by name.
    //
    // Static because it must survive the scene being reloaded around it (a load that switches scenes
    // rebuilds this manager), and cleared per play session by the domain reload.
    private static readonly List<BGMObject> playedObjects = new List<BGMObject>();

    /// <summary>Every <see cref="BGMObject"/> this manager has played or been given as a default.</summary>
    public static IReadOnlyList<BGMObject> PlayedObjects => playedObjects;

    /// <summary>One of them by name, or null. Case-insensitive, to match how saves are read back.</summary>
    public static BGMObject FindPlayedObject(string objectName)
    {
        if (string.IsNullOrEmpty(objectName)) return null;

        for (int i = playedObjects.Count - 1; i >= 0; i--)
        {
            BGMObject candidate = playedObjects[i];
            if (candidate && string.Equals(candidate.name, objectName, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
    }

    private static void Remember(BGMObject obj)
    {
        if (!obj || playedObjects.Contains(obj)) return;
        playedObjects.Add(obj);
    }

    public BGMObject DefaultBGMObject { get { EnsureDefaultInitialised(); return defaultObjectRuntime; } }

    /// <summary>The bare clip the manager currently treats as the default track (null when the default is a BGMObject).</summary>
    public AudioClip DefaultBGMClip { get { EnsureDefaultInitialised(); return defaultClipRuntime; } }

    /// <summary>True when there is something for <see cref="ReturnToDefault"/> to come back to.</summary>
    public bool HasDefaultTrack => DefaultBGMObject || DefaultBGMClip;

    private void EnsureDefaultInitialised()
    {
        if (defaultInitialised) return;
        defaultInitialised = true;
        defaultObjectRuntime = defaultBGMObject;
        defaultClipRuntime = deafultBGMusic;
        Remember(defaultBGMObject);
    }

    /// <summary>
    /// Makes <paramref name="obj"/> the track the manager comes back to from now on — this is how a
    /// cutscene promotes its own theme to "the game's music". Does NOT change what is playing right
    /// now; the swap is heard the next time <see cref="ReturnToDefault"/> runs (or right away if the
    /// caller used <see cref="PlayBGMAsNewDefault"/> / the becomeDefault flag).
    /// </summary>
    public void SetDefaultBGM(BGMObject obj, bool preload = true)
    {
        Remember(obj);
        defaultInitialised = true;
        defaultObjectRuntime = obj;
        defaultClipRuntime = null;
        if (preload) PreloadBGMObject(obj);

        h.Out("BGM: default track is now", obj ? obj.name : "none");
    }

    /// <summary>Bare-AudioClip twin of <see cref="SetDefaultBGM"/>.</summary>
    public void SetDefaultMusic(AudioClip clip, bool preload = true)
    {
        defaultInitialised = true;
        defaultObjectRuntime = null;
        defaultClipRuntime = clip;
        if (preload) RequestLoad(clip);

        h.Out("BGM: default track is now", clip ? clip.name : "none");
    }

    /// <summary>Puts the default back to whatever is assigned on the manager in the inspector.</summary>
    public void ResetDefaultBGM()
    {
        defaultInitialised = true;
        defaultObjectRuntime = defaultBGMObject;
        defaultClipRuntime = deafultBGMusic;
    }

    /// <summary>
    /// Crossfades back into the default track, coming in on its LOOP clip rather than replaying the
    /// intro — an intro is a one-time opening, so hearing it again announces that the music restarted,
    /// whereas landing on the loop just sounds like the track was there all along.
    ///
    /// <para>A no-op when the default is already playing (the same-track guard), and a plain fade-out
    /// when no default is assigned.</para>
    /// </summary>
    public void ReturnToDefault(float fadeTime = 1.5f)
    {
        EnsureDefaultInitialised();

        if (defaultObjectRuntime) PlayBGMFromLoop(defaultObjectRuntime, fadeTime);
        else if (defaultClipRuntime) PlayMusic(defaultClipRuntime, fadeTime);
        else
        {
            h.Out("ReturnToDefault: no default track assigned, fading music out instead");
            FadeOutMusic(fadeTime);
        }
    }

    /// <summary>
    /// Promotes <paramref name="obj"/> to the new default AND starts it right away (intro → loop,
    /// looping forever). The one-call version of "this cutscene's theme is the game's music now".
    /// </summary>
    public void PlayBGMAsNewDefault(BGMObject obj, float fadeTime = 1.5f)
    {
        if (!obj)
        {
            h.Out("PlayBGMAsNewDefault: BGMObject is null");
            return;
        }

        SetDefaultBGM(obj);
        PlayBGM(obj, fadeTime);
    }

    /// <summary>Bare-AudioClip twin of <see cref="PlayBGMAsNewDefault"/>.</summary>
    public void PlayMusicAsNewDefault(AudioClip clip, float fadeTime = 1.5f)
    {
        if (!clip)
        {
            h.Out("PlayMusicAsNewDefault: clip is null");
            return;
        }

        SetDefaultMusic(clip);
        PlayMusic(clip, fadeTime);
    }

    // ──────────────────────────────────────────────
    // Source pool
    // ──────────────────────────────────────────────

    private readonly List<AudioSource> sourcePool = new List<AudioSource>();
    private readonly HashSet<AudioSource> reserved = new HashSet<AudioSource>();

    /// <summary>Source holding the clip that is audible right now (intro while it plays, then the loop).</summary>
    private AudioSource current;

    /// <summary>Source holding the scheduled/looping clip in Advanced playback.</summary>
    private AudioSource loopSource;

    // ──────────────────────────────────────────────
    // Same-track guard
    // ──────────────────────────────────────────────

    // What the last ACCEPTED Play* call asked for. Recorded synchronously, the instant the request is
    // taken, because every Play* is served by a coroutine that first waits for the clip's audio data —
    // for those frames nothing is audible yet, so the AudioSources alone cannot answer "is this
    // already playing?". Without this record, two requests for the same track landing close together
    // (a cutscene swapping music in and something else asking for the same track) would restart it.
    private BGMObject requestedObject;
    private AudioClip requestedClip;
    private bool hasRequest;

    // True between a request being accepted and its routine actually putting a source on the air.
    // While it is up, the recorded request is trusted even though nothing is audible yet.
    private bool requestStartingUp;

    /// <summary>
    /// Records an accepted request so <see cref="IsAlreadyPlaying"/> can recognise a repeat of it
    /// before the sources have caught up. Call this right before starting the routine that serves it.
    /// </summary>
    private void NoteRequest(BGMObject obj, AudioClip clip)
    {
        requestedObject = obj;
        requestedClip = clip;
        hasRequest = true;
        requestStartingUp = true;
    }

    /// <summary>Called by a routine the moment its source is on the air.</summary>
    private void RequestIsLive() => requestStartingUp = false;

    /// <summary>Forgets the recorded request (the music was stopped or handed to the outro).</summary>
    private void ClearRequest()
    {
        requestedObject = null;
        requestedClip = null;
        hasRequest = false;
        requestStartingUp = false;
    }

    /// <summary>
    /// Target volume of the background music (0..1). Setting it applies immediately to the
    /// currently playing sources, and every later fade/crossfade fades up to this value.
    /// </summary>
    public float BgmVolume
    {
        get => bgmVolume;
        set
        {
            bgmVolume = Mathf.Clamp01(value);
            ApplyVolumeToActiveSources();
        }
    }

    /// <summary>Sets <see cref="BgmVolume"/>. Convenient for UI sliders / UnityEvents.</summary>
    public void SetBgmVolume(float volume) => BgmVolume = volume;

    private static bool? sessionMuted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetMuteStatics()
    {
        sessionMuted = null;
    }

    /// <summary>True while the background music is muted.</summary>
    public bool IsMuted
    {
        get => isMuted;
        set => SetMuted(value);
    }

    /// <summary>List of hotkeys that toggle music mute.</summary>
    public List<KeyCode> MuteHotkeys
    {
        get => muteHotkeys;
        set => muteHotkeys = value;
    }

#if ENABLE_INPUT_SYSTEM
    /// <summary>Optional Input System keys that toggle music mute.</summary>
    public List<Key> MuteKeys
    {
        get => muteKeys;
        set => muteKeys = value;
    }
#endif

    /// <summary>Whether hotkey checking for mute is enabled.</summary>
    public bool EnableMuteHotkeys
    {
        get => enableMuteHotkeys;
        set => enableMuteHotkeys = value;
    }

    /// <summary>Toggles mute on the background music.</summary>
    public void ToggleMute() => SetMuted(!isMuted);

    /// <summary>Mutes the background music.</summary>
    public void Mute() => SetMuted(true);

    /// <summary>Unmutes the background music.</summary>
    public void Unmute() => SetMuted(false);

    /// <summary>Sets whether the background music is muted.</summary>
    public void SetMuted(bool muted)
    {
        if (isMuted == muted) return;
        isMuted = muted;
        if (persistAcrossScenes) sessionMuted = muted;

        ApplyMuteToActiveSources();
        OnMuteChanged?.Invoke(isMuted);

        h.Out("BGM: music is now", isMuted ? "muted" : "unmuted");
    }

    /// <summary>Applies current mute state to all pooled and active sources.</summary>
    private void ApplyMuteToActiveSources()
    {
        if (sourcePool != null)
        {
            for (int i = 0; i < sourcePool.Count; i++)
            {
                if (sourcePool[i]) sourcePool[i].mute = isMuted;
            }
        }
        if (defaultAudioSource) defaultAudioSource.mute = isMuted;
        if (current) current.mute = isMuted;
        if (loopSource) loopSource.mute = isMuted;
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            ApplyVolumeToActiveSources();
            ApplyMuteToActiveSources();
        }
    }

    private void ApplyVolumeToActiveSources()
    {
        if (current) current.volume = TargetVolume;
        if (loopSource) loopSource.volume = TargetVolume;
    }

    // Runtime multiplier on top of bgmVolume, owned by whoever is staging the music (a visual-novel
    // dialogue's Music Volume). Not serialized: every scene's manager starts back at 1.
    [System.NonSerialized] private float volumeScale = 1f;

    // Unscaled time at which the last fade towards an audible volume ends.
    [System.NonSerialized] private float fadeInEndTime;

    /// <summary>The volume every fade-in lands on: <see cref="BgmVolume"/> times <see cref="VolumeScale"/>.</summary>
    private float TargetVolume => bgmVolume * volumeScale;

    /// <summary>Multiplier applied on top of <see cref="BgmVolume"/> (0..1). See <see cref="SetVolumeScale"/>.</summary>
    public float VolumeScale => volumeScale;

    /// <summary>
    /// Scales the music on top of <see cref="BgmVolume"/> without touching that setting. The music
    /// playing now eases to the new level over <paramref name="fadeTime"/> seconds; a track started
    /// after this call fades in to it by itself.
    /// </summary>
    public void SetVolumeScale(float scale, float fadeTime = 0f)
    {
        scale = Mathf.Clamp01(scale);
        if (Mathf.Approximately(scale, volumeScale)) return;
        volumeScale = scale;

        // Nothing audible to move, or the track is still starting up and will read the new target.
        if (requestStartingUp || !(current || loopSource) || !isActiveAndEnabled) return;

        // Outlast a fade-in still running on these sources, or it would finish on the old level.
        float fade = Mathf.Max(fadeTime, fadeInEndTime - Time.unscaledTime);
        StartCoroutine(FadeGroup(fade, TargetVolume, current, loopSource));
    }

    /// <summary>
    /// Sets the pitch on all currently active music sources.
    /// </summary>
    public void SetPitch(float pitch)
    {
        if (current) current.pitch = pitch;
        if (loopSource) loopSource.pitch = pitch;
        if (defaultAudioSource) defaultAudioSource.pitch = pitch;
    }

    /// <summary>
    /// Current pitch of the active music source, or 1 if none.
    /// </summary>
    public float CurrentPitch => current ? current.pitch : 1f;

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance);

        if (persistAcrossScenes && sessionMuted.HasValue)
            isMuted = sessionMuted.Value;

        // Three sources cover the worst case: outgoing track + intro + scheduled loop.
        for (int i = 0; i < 3; i++) CreatePooledSource();

        InitSource(ref defaultAudioSource, AudioMixerManager.GetMusicGroup());
        if (defaultAudioSource) defaultAudioSource.mute = isMuted;
    }

    private AudioSource CreatePooledSource()
    {
        AudioSource source = null;
        InitSource(ref source, AudioMixerManager.GetMusicGroup());
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.mute = isMuted;

        // Kept outside the scene, so the music isn't cut off when the scene (and this manager) goes:
        // OnDestroy hands the sources over to fade out there.
        Transform persistentRoot = BGMSceneCrossfade.Root;
        if (persistentRoot) source.transform.SetParent(persistentRoot, false);

        sourcePool.Add(source);
        return source;
    }

    /// <summary>
    /// The scene is being left (or the manager removed): whatever is still playing fades out over
    /// <see cref="sceneChangeFadeOut"/> on the persistent object while the next scene's manager
    /// brings its own music in.
    /// </summary>
    private void OnDestroy()
    {
        BGMSceneCrossfade.Release(sourcePool, sceneChangeFadeOut);
        sourcePool.Clear();
        reserved.Clear();
    }

    /// <summary>Takes a free (idle, unreserved) source out of the pool, growing it if needed.</summary>
    private AudioSource AcquireSource()
    {
        foreach (AudioSource source in sourcePool)
        {
            if (reserved.Contains(source) || source.isPlaying) continue;
            reserved.Add(source);
            ResetSource(source);
            return source;
        }

        AudioSource fresh = CreatePooledSource();
        reserved.Add(fresh);
        return fresh;
    }

    private void ResetSource(AudioSource source)
    {
        if (!source) return;
        source.Stop();
        source.clip = null;
        source.loop = false;
        source.pitch = 1f;
        source.volume = 0f;
        source.time = 0f;
        source.mute = isMuted;
    }

    /// <summary>Stops a source and returns it to the pool.</summary>
    private void ReleaseSource(AudioSource source)
    {
        if (!source) return;
        ResetSource(source);
        reserved.Remove(source);
        if (current == source) current = null;
        if (loopSource == source) loopSource = null;
    }

    new private void Start()
    {
        base.Start();
        var clip = Resources.Load<AudioClip>(defaultBGMusicPath);
        if (clip) deafultBGMusic = clip;

        // Take the serialized fields as the starting default (unless something already set one).
        EnsureDefaultInitialised();

        if (bgmObjects != null)
            foreach (BGMObject obj in bgmObjects) Remember(obj);

        // Kick off async loading of every playlist track up front so that starting a track later
        // never has to decompress/stream-open on the main thread (that was the mid-game freeze).
        PreloadAllTracks();

        if (playOnStart) PlayDefaultTrack();
    }

    private void Update()
    {
        if (!enableMuteHotkeys) return;
        if (IsInputFieldFocused()) return;

        if (IsMuteHotkeyPressed())
        {
            ToggleMute();
        }
    }

    private static bool IsInputFieldFocused()
    {
        var currentSelected = UnityEngine.EventSystems.EventSystem.current != null
            ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject
            : null;

        if (currentSelected != null)
        {
            if (currentSelected.GetComponent<TMPro.TMP_InputField>() != null ||
                currentSelected.GetComponent<UnityEngine.UI.InputField>() != null)
                return true;
        }

        return false;
    }

    private bool IsMuteHotkeyPressed()
    {
        if (muteHotkeys != null)
        {
            for (int i = 0; i < muteHotkeys.Count; i++)
            {
                KeyCode code = muteHotkeys[i];
                if (code == KeyCode.None) continue;

                try
                {
                    if (Input.GetKeyDown(code)) return true;
                }
                catch (InvalidOperationException)
                {
                    // Thrown if legacy input is explicitly turned off in PlayerSettings
                }

#if ENABLE_INPUT_SYSTEM
                if (Keyboard.current != null && IsNewInputKeyPressed(Keyboard.current, code))
                    return true;
#endif
            }
        }

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && muteKeys != null)
        {
            for (int i = 0; i < muteKeys.Count; i++)
            {
                Key k = muteKeys[i];
                if (k != Key.None && Keyboard.current[k].wasPressedThisFrame)
                    return true;
            }
        }
#endif

        return false;
    }

#if ENABLE_INPUT_SYSTEM
    private static bool IsNewInputKeyPressed(Keyboard keyboard, KeyCode code)
    {
        Key key = ToInputSystemKey(code);
        if (key != Key.None)
        {
            try
            {
                return keyboard[key].wasPressedThisFrame;
            }
            catch
            {
                return false;
            }
        }
        return false;
    }

    private static Key ToInputSystemKey(KeyCode keyCode)
    {
        switch (keyCode)
        {
            case KeyCode.None: return Key.None;
            case KeyCode.Space: return Key.Space;
            case KeyCode.Return: return Key.Enter;
            case KeyCode.Tab: return Key.Tab;
            case KeyCode.Escape: return Key.Escape;
            case KeyCode.Backspace: return Key.Backspace;
            case KeyCode.Delete: return Key.Delete;
            case KeyCode.LeftShift: return Key.LeftShift;
            case KeyCode.RightShift: return Key.RightShift;
            case KeyCode.LeftControl: return Key.LeftCtrl;
            case KeyCode.RightControl: return Key.RightCtrl;
            case KeyCode.LeftAlt: return Key.LeftAlt;
            case KeyCode.RightAlt: return Key.RightAlt;
            case KeyCode.UpArrow: return Key.UpArrow;
            case KeyCode.DownArrow: return Key.DownArrow;
            case KeyCode.LeftArrow: return Key.LeftArrow;
            case KeyCode.RightArrow: return Key.RightArrow;
            default:
                if (Enum.TryParse<Key>(keyCode.ToString(), true, out Key key))
                    return key;
                return Key.None;
        }
    }
#endif

    /// <summary>
    /// Starts the track configured on the manager — <see cref="defaultBGMObject"/> when set, otherwise
    /// <see cref="deafultBGMusic"/>. Called automatically from Start() when <see cref="playOnStart"/> is
    /// on; call it by hand to bring the music in later when it is off.
    /// </summary>
    /// <param name="fadeTime">Fade-in duration. Negative uses the inspector's start fade time.</param>
    public void PlayDefaultTrack(float fadeTime = -1f)
    {
        float fade = fadeTime < 0f ? startTrackFadeTime : fadeTime;
        EnsureDefaultInitialised();

        if (defaultObjectRuntime) PlayBGM(defaultObjectRuntime, fade);
        else if (defaultClipRuntime) PlayMusic(defaultClipRuntime, fade);
        else h.Out("Default music is not found'");
    }

    // ──────────────────────────────────────────────
    // Preloading
    // ──────────────────────────────────────────────

    /// <summary>
    /// Requests background loading of the default track, every clip in <see cref="bgTracks"/> and every
    /// clip of every <see cref="BGMObject"/> known to the manager.
    ///
    /// <para>Streamed clips are skipped (they need no preload) and so are clips that would only load
    /// synchronously, so this never hitches no matter how the library is imported. See
    /// <see cref="RequestLoad"/>.</para>
    /// </summary>
    public void PreloadAllTracks()
    {
        RequestLoad(deafultBGMusic);

        if (bgTracks != null)
            foreach (var track in bgTracks) RequestLoad(track);

        PreloadBGMObject(defaultBGMObject);

        if (bgmObjects != null)
            foreach (var obj in bgmObjects) PreloadBGMObject(obj);
    }

    /// <summary>Requests background loading of every clip referenced by <paramref name="obj"/>.</summary>
    public static void PreloadBGMObject(BGMObject obj)
    {
        if (!obj) return;
        foreach (var clip in obj.AllClips()) RequestLoad(clip);
    }

    /// <summary>
    /// Asks Unity to bring <paramref name="clip"/>'s audio data into memory.
    ///
    /// <para><b>The important part:</b> <c>AudioClip.LoadAudioData()</c> is only asynchronous when the clip
    /// was imported with <i>Load In Background</i> on. With it off, the call decompresses the whole clip
    /// synchronously on the main thread — for a multi-minute music track that is the seconds-long freeze
    /// that used to happen right before a new soundtrack started. So a clip that cannot load in the
    /// background is skipped here rather than stalling the game, and reported once so the import setting
    /// gets fixed (<b>Tools > Audio > Fix Import Settings</b>).</para>
    /// </summary>
    /// <param name="clip">Clip to load.</param>
    /// <param name="allowBlocking">When true, a clip that can only load synchronously is loaded anyway.
    /// Used on the play path, where the alternative is <c>Play()</c> stalling for the same duration
    /// anyway; left false for bulk preloading, which must never hitch.</param>
    private static void RequestLoad(AudioClip clip, bool allowBlocking = false)
    {
        if (!clip) return;
        if (clip.loadType == AudioClipLoadType.Streaming) return; // streamed clips need no preload
        if (clip.loadState == AudioDataLoadState.Loaded || clip.loadState == AudioDataLoadState.Loading) return;

        if (!clip.loadInBackground)
        {
            WarnBlockingClip(clip);
            if (!allowBlocking) return;
        }

        clip.LoadAudioData();
    }

    /// <summary>Names each mis-imported clip once instead of spamming the log every frame.</summary>
    private static void WarnBlockingClip(AudioClip clip)
    {
        if (!warnedBlockingClips.Add(clip.name)) return;
        h.Out($"BGM: clip '{clip.name}' is imported as {clip.loadType} with 'Load In Background' OFF, " +
              "so loading it blocks the main thread and freezes the game. " +
              "Run Tools > Audio > Fix Import Settings.");
    }

    private static readonly HashSet<string> warnedBlockingClips = new HashSet<string>();

    /// <summary>
    /// Yields until <paramref name="clip"/>'s audio data is resident, so the following Play() call
    /// costs nothing on the main thread. Streamed clips return immediately — they have nothing to load.
    /// </summary>
    private static IEnumerator EnsureLoaded(AudioClip clip)
    {
        if (!clip || clip.loadType == AudioClipLoadType.Streaming) yield break;

        RequestLoad(clip, allowBlocking: true);
        while (clip.loadState == AudioDataLoadState.Loading)
            yield return null;
    }

    /// <summary>Exact clip duration in seconds (sample-accurate, unlike AudioClip.length).</summary>
    private static double ClipDuration(AudioClip clip)
    {
        if (!clip || clip.frequency <= 0) return 0d;
        return (double)clip.samples / clip.frequency;
    }

    /// <summary>
    /// Head-room to put between now and a <c>PlayScheduled</c> start time. Streamed clips get the longer
    /// <see cref="streamingScheduleLead"/> because their first buffer comes off disk rather than out of
    /// memory; everything else uses <see cref="scheduleLead"/>.
    /// </summary>
    private float LeadFor(params AudioClip[] clips)
    {
        foreach (AudioClip clip in clips)
            if (clip && clip.loadType == AudioClipLoadType.Streaming)
                return Mathf.Max(scheduleLead, streamingScheduleLead);

        return scheduleLead;
    }

    // ──────────────────────────────────────────────
    // Public API — BGMObject
    // ──────────────────────────────────────────────

    /// <summary>
    /// Plays a <see cref="BGMObject"/>. In Default play type its <see cref="BGMObject.full"/> clip is
    /// crossfaded in and looped; in Advanced play type the intro clip is played once and the loop clip
    /// is scheduled to start seamlessly right after it and loops forever.
    /// </summary>
    /// <param name="obj">Track to play.</param>
    /// <param name="fadeTime">Crossfade duration in seconds.</param>
    /// <param name="playTypeOverride">Forces a play type for this call, ignoring <see cref="playType"/>
    /// and the object's own override.</param>
    /// <param name="fadeInTime">Fade-in of the new track alone, while the old one still fades out over
    /// <paramref name="fadeTime"/>. Negative: the same as <paramref name="fadeTime"/>.</param>
    public void PlayBGM(BGMObject obj, float fadeTime = 1.5f, BGMPlayType? playTypeOverride = null, float fadeInTime = -1f)
    {
        if (!obj)
        {
            h.Out("PlayBGM: BGMObject is null");
            return;
        }

        Remember(obj);

        BGMPlayType type = playTypeOverride ?? obj.ResolvePlayType(playType);

        if (type == BGMPlayType.Advanced && !obj.CanPlayAdvanced)
        {
            h.Out($"PlayBGM: '{obj.name}' has no loop clip, falling back to Default play type");
            type = BGMPlayType.Default;
        }

        if (type == BGMPlayType.Default)
        {
            AudioClip clip = obj.DefaultClip;
            if (!clip)
            {
                h.Out($"PlayBGM: '{obj.name}' has no clip to play");
                return;
            }

            if (IsAlreadyPlaying(obj, clip))
            {
                h.Out($"PlayBGM: '{obj.name}' is already playing — left alone.");
                return;
            }

            StopMusicRoutines();
            CurrentBGMObject = obj;
            IsPlayingOutro = false;
            NoteRequest(obj, clip);
            StartCoroutine(PlaySingleClipRoutine(clip, fadeTime, loop: true, fadeInTime));
            return;
        }

        if (IsAlreadyPlaying(obj, obj.LoopClip))
        {
            h.Out($"PlayBGM: '{obj.name}' is already playing — left alone.");
            return;
        }

        StopMusicRoutines();
        CurrentBGMObject = obj;
        IsPlayingOutro = false;
        NoteRequest(obj, obj.LoopClip);
        StartCoroutine(PlayAdvancedRoutine(obj, fadeTime, fadeInTime));
    }

    /// <summary>
    /// Plays <paramref name="obj"/> through exactly ONCE and then falls silent — the full clip when the
    /// track has one, otherwise the intro followed by a single pass of the loop clip (scheduled
    /// sample-accurately, same as Advanced playback, just without the repeat).
    ///
    /// <para>What happens when it ends is decided by <paramref name="returnToDefault"/>: by default the
    /// manager crossfades back into the loop of its default track (see <see cref="ReturnToDefault"/>),
    /// which is the normal "a one-off track played, now the game's music comes back" flow. Pass false to
    /// fall silent instead and let the caller own the ending. <see cref="BGMObject.OnePassLength"/> is
    /// how long one pass takes, and <see cref="IsPlaying"/> goes false when it is over.</para>
    /// </summary>
    /// <param name="obj">Track to play once.</param>
    /// <param name="fadeTime">Crossfade duration in seconds.</param>
    /// <param name="becomeDefault">Promote this track to the manager's default first, so the music that
    /// comes back afterwards — and every later <see cref="ReturnToDefault"/> — is this one. This is the
    /// castle-capture case: the cutscene's theme replaces the old default for the rest of the run.</param>
    /// <param name="returnToDefault">Overrides the manager's "return to default when finished" setting
    /// for this call only. Leave null to use it.</param>
    public void PlayBGMOnce(BGMObject obj, float fadeTime = 1.5f, bool becomeDefault = false, bool? returnToDefault = null)
    {
        if (!obj)
        {
            h.Out("PlayBGMOnce: BGMObject is null");
            return;
        }

        Remember(obj);

        // Promote first, so the default is right even if the guard below leaves the music alone.
        if (becomeDefault) SetDefaultBGM(obj);
        bool chain = returnToDefault ?? returnToDefaultWhenFinished;

        // Already on the air: restarting it from bar one is exactly the jolt this guard exists to
        // avoid, so the track is left running. The caller's "wait for it to finish" simply times out
        // on its own safety cap and whatever it does next (typically restoring the previous track,
        // which is this same one) is another no-op.
        if (IsAlreadyPlaying(obj, obj.full ? obj.full : obj.LoopClip))
        {
            h.Out($"PlayBGMOnce: '{obj.name}' is already playing — left alone.");
            return;
        }

        StopMusicRoutines();
        CurrentBGMObject = obj;
        IsPlayingOutro = false;
        NoteRequest(obj, obj.full ? obj.full : obj.LoopClip);
        StartCoroutine(PlayOnceThenDefaultRoutine(obj, null, fadeTime, chain));
    }

    /// <summary>
    /// Plays a single <paramref name="clip"/> once and then falls silent. The bare-AudioClip twin of
    /// <see cref="PlayBGMOnce"/>.
    /// </summary>
    public void PlayMusicOnce(AudioClip clip, float fadeTime = 1.5f, bool becomeDefault = false, bool? returnToDefault = null)
    {
        if (!clip) return;

        if (becomeDefault) SetDefaultMusic(clip);
        bool chain = returnToDefault ?? returnToDefaultWhenFinished;

        if (IsAlreadyPlaying(null, clip))
        {
            h.Out($"PlayMusicOnce: '{clip.name}' is already playing — left alone.");
            return;
        }

        StopMusicRoutines();
        CurrentBGMObject = null;
        IsPlayingOutro = false;
        NoteRequest(null, clip);
        StartCoroutine(PlayOnceThenDefaultRoutine(null, clip, fadeTime, chain));
    }

    /// <summary>
    /// Crossfades straight into <paramref name="obj"/>'s LOOP clip and loops it, skipping the intro.
    ///
    /// <para>This is how a track is <i>resumed</i> after something else interrupted it. An intro is a
    /// one-time opening — hearing it a second time announces that the music restarted, whereas coming
    /// back in on the loop just sounds like the track was there all along.</para>
    /// </summary>
    public void PlayBGMFromLoop(BGMObject obj, float fadeTime = 1.5f)
    {
        if (!obj)
        {
            h.Out("PlayBGMFromLoop: BGMObject is null");
            return;
        }

        Remember(obj);

        AudioClip loop = obj.LoopClip;
        if (!loop)
        {
            // Nothing loop-shaped to come back on — the normal path is the honest fallback.
            PlayBGM(obj, fadeTime);
            return;
        }

        if (IsAlreadyPlaying(obj, loop))
        {
            h.Out($"PlayBGMFromLoop: '{obj.name}' is already playing — left alone.");
            return;
        }

        StopMusicRoutines();
        CurrentBGMObject = obj;
        IsPlayingOutro = false;
        NoteRequest(obj, loop);
        StartCoroutine(PlaySingleClipRoutine(loop, fadeTime, loop: true));
    }

    /// <summary>
    /// Seeks the audible music to <paramref name="time"/> seconds — the counterpart of
    /// <see cref="CurrentClipTime"/>, used to resume a track where a save left it.
    ///
    /// <para>Tracks are started from a coroutine that fades them in, so calling this straight after
    /// a Play* call would find nothing playing yet. The seek therefore waits (up to
    /// <paramref name="timeout"/> seconds) for a source to come up before it applies; pass 0 to seek
    /// only what is playing this instant. A time past the end of the clip wraps around, so a loop
    /// that has been running for a while lands where it would have been.</para>
    /// </summary>
    public void SeekCurrent(float time, float timeout = 2f)
    {
        if (!isActiveAndEnabled) return;
        StartCoroutine(SeekRoutine(time, timeout));
    }

    private IEnumerator SeekRoutine(float time, float timeout)
    {
        float deadline = Time.unscaledTime + Mathf.Max(0f, timeout);

        while (!(current && current.clip && current.isPlaying))
        {
            if (Time.unscaledTime >= deadline) yield break;
            yield return null;
        }

        AudioClip clip = current.clip;
        float length = clip ? clip.length : 0f;
        if (length <= 0f) yield break;

        // Stop a hair short of the end: seeking exactly to the length is out of range, and landing on
        // the last sample of a non-looping clip would end it the moment it started.
        current.time = Mathf.Clamp(Mathf.Repeat(Mathf.Max(0f, time), length), 0f, length - 0.05f);
    }

    /// <summary>Plays a random track from <see cref="bgmObjects"/>, never repeating the current one.</summary>
    public void PlayRandomBgmObject(float fadeTime = 1.5f)
    {
        if (bgmObjects == null || bgmObjects.Count == 0)
        {
            h.Out("bgmObjects is empty");
            return;
        }

        BGMObject obj = h.RandChoice(bgmObjects);
        while (bgmObjects.Count > 1 && obj == CurrentBGMObject)
            obj = h.RandChoice(bgmObjects);

        PlayBGM(obj, fadeTime);
    }

    /// <summary>
    /// Fades the current music into <see cref="BGMObject.outro"/> of the track that is playing.
    /// The loop stops as the outro fades in; when the outro ends everything is stopped and
    /// <see cref="OnOutroFinished"/> is raised.
    /// </summary>
    /// <param name="fadeTime">Crossfade duration in seconds.</param>
    /// <param name="onComplete">Optional callback invoked when the outro clip has finished.</param>
    /// <param name="returnToDefault">Overrides the manager's "return to default when finished" setting
    /// for this call only. Leave null to use it.</param>
    public void StartOutro(float fadeTime = 1.5f, Action onComplete = null, bool? returnToDefault = null)
    {
        StartOutro(CurrentBGMObject, fadeTime, onComplete, returnToDefault);
    }

    /// <summary>Fades the current music into the outro clip of <paramref name="obj"/>.</summary>
    public void StartOutro(BGMObject obj, float fadeTime = 1.5f, Action onComplete = null, bool? returnToDefault = null)
    {
        bool chain = returnToDefault ?? returnToDefaultWhenFinished;

        if (!obj || !obj.outro)
        {
            // Nothing to fade into — just fade the music out so callers still get a clean ending.
            h.Out("StartOutro: no outro clip, fading music out instead");
            FadeOutMusic(fadeTime);
            onComplete?.Invoke();
            OnOutroFinished?.Invoke();
            if (chain && !hasRequest) ReturnToDefault(fadeTime);
            return;
        }

        StopMusicRoutines();
        IsPlayingOutro = true;
        ClearRequest();
        StartCoroutine(OutroRoutine(obj.outro, fadeTime, onComplete, chain));
    }

    // ──────────────────────────────────────────────
    // Public API — plain AudioClips (legacy)
    // ──────────────────────────────────────────────

    /// <summary>Crossfades to <paramref name="newClip"/> and loops it (legacy Default behaviour).</summary>
    /// <param name="fadeInTime">Fade-in of the new clip alone, while the old one still fades out over
    /// <paramref name="fadeTime"/>. Negative: the same as <paramref name="fadeTime"/>.</param>
    public void PlayMusic(AudioClip newClip, float fadeTime = 1.5f, float fadeInTime = -1f)
    {
        if (!newClip) return;
        if (current && current.clip == newClip && current.isPlaying) return;
        if (IsAlreadyPlaying(null, newClip))
        {
            h.Out($"PlayMusic: '{newClip.name}' is already playing — left alone.");
            return;
        }

        StopMusicRoutines();
        CurrentBGMObject = null;
        IsPlayingOutro = false;
        NoteRequest(null, newClip);
        StartCoroutine(PlaySingleClipRoutine(newClip, fadeTime, loop: true, fadeInTime));
    }

    /// <summary>
    /// Starts playing a random track from <see cref="bgTracks"/>. When that clip ends,
    /// another random track is picked and crossfaded in, looping the playlist indefinitely.
    /// </summary>
    public void PlayRandomBgTrack(float fadeTime = 1.5f)
    {
        if (bgTracks == null || bgTracks.Count == 0)
        {
            h.Out("bgTracks is empty");
            return;
        }

        var clip = h.RandChoice(bgTracks);
        // Avoid immediately repeating the same track when there's a choice.
        while (bgTracks.Count > 1 && current && clip == current.clip)
            clip = h.RandChoice(bgTracks);

        StopMusicRoutines();
        CurrentBGMObject = null;
        IsPlayingOutro = false;
        NoteRequest(null, clip);
        StartCoroutine(PlaylistRoutine(clip, fadeTime));
    }

    /// <summary>
    /// Plays <paramref name="clip"/> once (no loop) and, when it finishes, crossfades into a random
    /// track from <see cref="bgTracks"/> and keeps looping that playlist. Used by cutscenes that swap
    /// in a one-off soundtrack and want the normal background music to resume afterwards.
    /// </summary>
    public void PlayMusicThenRandomBgTracks(AudioClip clip, float fadeTime = 1.5f)
    {
        if (!clip)
        {
            // Nothing to play up front — just go straight to the random playlist.
            PlayRandomBgTrack(fadeTime);
            return;
        }

        StopMusicRoutines();
        CurrentBGMObject = null;
        IsPlayingOutro = false;
        NoteRequest(null, clip);
        // PlaylistRoutine plays the clip once, waits for it to end, then chains PlayRandomBgTrack.
        StartCoroutine(PlaylistRoutine(clip, fadeTime));
    }

    /// <summary>
    /// Fades all currently playing music down to silence over <paramref name="fadeTime"/> seconds,
    /// then stops the sources so a later Play call starts clean.
    /// </summary>
    public void FadeOutMusic(float fadeTime = 1.5f)
    {
        StopMusicRoutines();
        CurrentBGMObject = null;
        IsPlayingOutro = false;
        current = null;
        loopSource = null;
        ClearRequest();
        ClearScheduledEnd();
        FadeOutOthers(fadeTime);
    }

    // ──────────────────────────────────────────────
    // Routines
    // ──────────────────────────────────────────────

    /// <summary>
    /// True when the manager is already playing — or has just been asked to play — exactly this
    /// track, so the caller should leave the music alone instead of restarting it.
    ///
    /// <para>Restarting a track that is already playing is always audible: the intro comes back, the
    /// crossfade dips the level, and the piece jumps back to bar one. A castle cutscene that swaps in
    /// the track the player is already listening to (or restores it afterwards) must therefore be a
    /// no-op, not a restart.</para>
    ///
    /// <para>Two things are checked, because either on its own has a blind spot. The live sources
    /// answer for music that is genuinely audible; the recorded request answers for the frames
    /// between a Play* call and its coroutine getting a source on the air, where the sources still
    /// hold the PREVIOUS track and would happily accept the same request a second time.</para>
    /// </summary>
    private bool IsAlreadyPlaying(BGMObject obj, AudioClip clip)
    {
        if (IsPlayingOutro) return false;

        // Same thing asked for twice while the first request is still starting up. The clip must be
        // real: two "nothing" requests are not the same track, they are two empty BGMObjects, and
        // matching them would silently swallow the second call.
        if (clip && hasRequest && requestedObject == obj && requestedClip == clip
            && (requestStartingUp || IsPlaying))
            return true;

        if (CurrentBGMObject != obj) return false;
        return current && current.isPlaying && (current.clip == clip || (loopSource && loopSource.clip == clip));
    }

    /// <summary>
    /// Stops every music coroutine. Sources are intentionally left playing — the routine started next
    /// fades them out via <see cref="FadeOutOthers"/>, so nothing is ever cut off abruptly.
    /// </summary>
    private void StopMusicRoutines()
    {
        StopAllCoroutines();
    }

    /// <summary>
    /// Crossfades a single clip in on a fresh source, fading every other source out over
    /// <paramref name="fadeTime"/>. <paramref name="fadeInTime"/> is the new clip's own fade-in
    /// (negative: the same as <paramref name="fadeTime"/>).
    /// </summary>
    private IEnumerator PlaySingleClipRoutine(AudioClip clip, float fadeTime, bool loop, float fadeInTime = -1f)
    {
        // Make sure the clip is fully resident before Play(), otherwise Unity decompresses it
        // synchronously on the main thread and the game hitches.
        yield return EnsureLoaded(clip);

        float fadeIn = fadeInTime < 0f ? fadeTime : fadeInTime;
        bool instant = fadeIn <= 0f;

        AudioSource source = AcquireSource();
        source.clip = clip;
        source.loop = loop;
        // With no fade in, come in at full volume right away instead of spending a frame silent.
        source.volume = instant ? TargetVolume : 0f;
        source.Play();

        // A looping clip never ends; a one-shot ends one clip length from the Play() above.
        if (loop) ClearScheduledEnd();
        else NoteScheduledEnd(AudioSettings.dspTime + ClipDuration(clip));

        FadeOutOthers(fadeTime, source);
        current = source;
        loopSource = loop ? source : null;
        RequestIsLive();

        if (instant) yield break;

        // Give the streamer a frame to fill its buffer before the fade starts.
        yield return null;

        yield return FadeGroup(fadeIn, TargetVolume, source);
    }

    /// <summary>
    /// Advanced playback: crossfade the intro in, schedule the loop clip to start sample-accurately
    /// at the intro's end, then hand ownership over to the loop source.
    /// </summary>
    private IEnumerator PlayAdvancedRoutine(BGMObject obj, float fadeTime, float fadeInTime = -1f)
    {
        AudioClip intro = obj.intro;
        AudioClip loop = obj.LoopClip;

        yield return EnsureLoaded(intro);
        yield return EnsureLoaded(loop);

        if (!intro)
        {
            // No intro authored — behave like a plain looping track.
            yield return PlaySingleClipRoutine(loop, fadeTime, loop: true, fadeInTime);
            yield break;
        }

        float fadeIn = fadeInTime < 0f ? fadeTime : fadeInTime;
        bool instant = fadeIn <= 0f;
        float startVolume = instant ? TargetVolume : 0f;

        AudioSource introSource = AcquireSource();
        AudioSource nextLoopSource = AcquireSource();

        double startDsp = AudioSettings.dspTime + LeadFor(intro, loop);
        double loopDsp = startDsp + ClipDuration(intro);

        introSource.clip = intro;
        introSource.loop = false;
        introSource.volume = startVolume;
        introSource.PlayScheduled(startDsp);

        nextLoopSource.clip = loop;
        nextLoopSource.loop = true;
        nextLoopSource.volume = startVolume;
        nextLoopSource.PlayScheduled(loopDsp);

        FadeOutOthers(fadeTime, introSource, nextLoopSource);
        current = introSource;
        loopSource = nextLoopSource;
        ClearScheduledEnd(); // the loop clip repeats forever — there is no end to hand over at
        RequestIsLive();

        // Both sources are faded together, so a fade that outlasts a short intro still sounds right.
        yield return FadeGroup(fadeIn, TargetVolume, introSource, nextLoopSource);

        // Wait for the handover, then give the intro source back to the pool.
        while (AudioSettings.dspTime < loopDsp) yield return null;

        ReleaseSource(introSource);
        current = nextLoopSource;
        loopSource = nextLoopSource;
    }

    /// <summary>
    /// One complete pass of <paramref name="obj"/>, then silence. A track authored as a single full
    /// clip is simply played through; an intro/loop pair is scheduled exactly like Advanced playback,
    /// except the loop source does not repeat.
    /// </summary>
    private IEnumerator PlayOnceRoutine(BGMObject obj, float fadeTime)
    {
        if (obj.full)
        {
            yield return PlaySingleClipRoutine(obj.full, fadeTime, loop: false);
            yield break;
        }

        AudioClip intro = obj.intro;
        AudioClip tail = obj.LoopClip;

        if (!intro)
        {
            yield return PlaySingleClipRoutine(tail, fadeTime, loop: false);
            yield break;
        }

        yield return EnsureLoaded(intro);
        yield return EnsureLoaded(tail);

        AudioSource introSource = AcquireSource();
        AudioSource tailSource = AcquireSource();

        double startDsp = AudioSettings.dspTime + LeadFor(intro, tail);
        double tailDsp = startDsp + ClipDuration(intro);

        introSource.clip = intro;
        introSource.loop = false;
        introSource.volume = 0f;
        introSource.PlayScheduled(startDsp);

        tailSource.clip = tail;
        tailSource.loop = false; // the one difference from PlayAdvancedRoutine
        tailSource.volume = 0f;
        tailSource.PlayScheduled(tailDsp);

        FadeOutOthers(fadeTime, introSource, tailSource);
        current = introSource;
        loopSource = null;
        NoteScheduledEnd(tailDsp + ClipDuration(tail));
        RequestIsLive();

        yield return FadeGroup(fadeTime, TargetVolume, introSource, tailSource);

        // Hand over at the seam, then give the intro source back to the pool.
        while (AudioSettings.dspTime < tailDsp) yield return null;

        ReleaseSource(introSource);
        current = tailSource;
    }

    /// <summary>
    /// Plays a one-shot track through and then — unless the caller opted out — crossfades back into the
    /// loop of the default track, so the game's music picks up where the interruption left it.
    /// </summary>
    private IEnumerator PlayOnceThenDefaultRoutine(BGMObject obj, AudioClip clip, float fadeTime, bool returnToDefault)
    {
        if (obj) yield return PlayOnceRoutine(obj, fadeTime);
        else yield return PlaySingleClipRoutine(clip, fadeTime, loop: false);

        if (!returnToDefault) yield break;

        AudioSource source = current;

        // Wait the track out, stalling while the game is paused — the music keeps running in the pause
        // menu, so the return has to wait for it there too.
        while (source && source.clip &&
               (source.isPlaying || (GameFlowManager.Instance && GameFlowManager.Instance.IsPaused())))
            yield return null;

        ReturnToDefault(fadeTime);
    }

    /// <summary>Crossfades into the outro clip, then stops everything and notifies listeners.</summary>
    private IEnumerator OutroRoutine(AudioClip outro, float fadeTime, Action onComplete, bool returnToDefault)
    {
        yield return EnsureLoaded(outro);

        AudioSource source = AcquireSource();
        source.clip = outro;
        source.loop = false;
        source.volume = 0f;
        source.Play();

        FadeOutOthers(fadeTime, source);
        current = source;
        loopSource = null;
        NoteScheduledEnd(AudioSettings.dspTime + ClipDuration(outro));

        yield return null;
        yield return FadeGroup(fadeTime, TargetVolume, source);

        // Wait for the outro to finish, stalling while the game is paused.
        while (source.isPlaying || (GameFlowManager.Instance && GameFlowManager.Instance.IsPaused()))
            yield return null;

        ReleaseSource(source);
        IsPlayingOutro = false;
        CurrentBGMObject = null;
        ClearRequest();

        onComplete?.Invoke();
        OnOutroFinished?.Invoke();

        // A listener that started its own music has the last word — NoteRequest raised hasRequest, so
        // the fallback stands down instead of stomping on it. (Those Play* calls stop this coroutine,
        // but a stopped coroutine still runs to its next yield, so this line is reached either way.)
        if (returnToDefault && !hasRequest) ReturnToDefault(fadeTime);
    }

    private IEnumerator PlaylistRoutine(AudioClip clip, float fadeTime)
    {
        // Crossfade in the new clip without looping so we can detect when it ends.
        yield return PlaySingleClipRoutine(clip, fadeTime, loop: false);

        AudioSource source = current;

        // Wait for the clip to finish (stalling while the game is paused).
        while (source && source.clip == clip &&
               (source.isPlaying || (GameFlowManager.Instance && GameFlowManager.Instance.IsPaused())))
            yield return null;

        // Clip ended, chain into the next random track.
        PlayRandomBgTrack(fadeTime);
    }

    // ──────────────────────────────────────────────
    // Fading
    // ──────────────────────────────────────────────

    /// <summary>
    /// Fades every reserved source that is not listed in <paramref name="keep"/> down to silence and
    /// releases it back to the pool.
    /// </summary>
    private void FadeOutOthers(float fadeTime, params AudioSource[] keep)
    {
        // Copy first: the fade coroutines mutate `reserved` when they release their source.
        var toFade = new List<AudioSource>();
        foreach (AudioSource source in reserved)
        {
            if (Array.IndexOf(keep, source) >= 0) continue;
            toFade.Add(source);
        }

        foreach (AudioSource source in toFade)
            StartCoroutine(FadeOutAndRelease(source, fadeTime));
    }

    private IEnumerator FadeOutAndRelease(AudioSource source, float fadeTime)
    {
        yield return FadeGroup(fadeTime, 0f, source);
        ReleaseSource(source);
    }

    /// <summary>
    /// Fades the given sources from their current volume to <paramref name="target"/>.
    /// Uses unscaled time so music keeps fading while the game is paused.
    /// </summary>
    private IEnumerator FadeGroup(float fadeTime, float target, params AudioSource[] sources)
    {
        if (sources == null || sources.Length == 0) yield break;

        if (target > 0f) fadeInEndTime = Time.unscaledTime + Mathf.Max(0f, fadeTime);

        var startVolumes = new float[sources.Length];
        for (int i = 0; i < sources.Length; i++)
            startVolumes[i] = sources[i] ? sources[i].volume : 0f;

        if (fadeTime <= 0f)
        {
            foreach (AudioSource source in sources)
                if (source) source.volume = target;
            yield break;
        }

        float t = 0f;
        while (t < fadeTime)
        {
            t += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(t / fadeTime);

            for (int i = 0; i < sources.Length; i++)
                if (sources[i]) sources[i].volume = Mathf.Lerp(startVolumes[i], target, normalized);

            yield return null;
        }

        foreach (AudioSource source in sources)
            if (source) source.volume = target;
    }
}
