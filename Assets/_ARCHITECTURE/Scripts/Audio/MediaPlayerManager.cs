using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Simple media-player UI that drives <see cref="BGMManager"/> through a track list.
/// <para>Wire the five buttons in the inspector; the manager enables / disables Next / Prev
/// automatically when the index reaches the bounds of the list.</para>
/// </summary>
public class MediaPlayerManager : MonoBehaviour
{
    public static MediaPlayerManager Instance;

    [Header("Tracks")]
    [Tooltip("Playlist. The BGMManager's track is switched to whichever entry is selected.")]
    public List<AudioClip> tracks = new List<AudioClip>();

    [Tooltip("Display names for each track (matched by index: tracks[i] ↔ songNames[i]).")]
    public List<string> songNames = new List<string>();

    [Tooltip("Artist names for each track (matched by index: tracks[i] ↔ artistNames[i]).")]
    public List<string> artistNames = new List<string>();

    [Header("UI")]
    [Tooltip("Root of the media player window. This is what Open/Close toggles.")]
    public GameObject window;

    [Tooltip("TMP label that shows the name of the currently playing song.")]
    public TMP_Text songLabel;

    [Header("UI Buttons")]
    public Button pauseButton;
    public Button stopButton;
    public Button nextButton;
    public Button prevButton;
    public Button muteButton;
    public Button nextButton2;
    public Button prevButton2;
    public Button closeButton;

    [Header("Achievements")]
    [Tooltip("Achievement granted when player has started playing all the OST tracks in the media player.")]
    [SerializeField] private AchievementData allOstAchievement;

    [Header("State (read-only)")]
    [SerializeField] private int currentIndex;
    [SerializeField] private bool isPaused;
    [SerializeField] private bool isMuted;

    private readonly HashSet<int> _playedTrackIndices = new HashSet<int>();

    /// <summary>Index of the track that is currently selected in the playlist.</summary>
    public int CurrentIndex => currentIndex;

    /// <summary>True while playback is paused.</summary>
    public bool IsPaused => isPaused;

    /// <summary>True while muted.</summary>
    public bool IsMuted => isMuted;

    /// <summary>True while the media player window is showing.</summary>
    public bool IsOpen => window && window.activeSelf;

    private float volumeBeforeMute;

    // ──────────────────────────────────────────────
    // Lifecycle
    // ──────────────────────────────────────────────

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance);

        if (closeButton) closeButton.onClick.AddListener(Close);
    }

    private void Start()
    {
        if (pauseButton) pauseButton.onClick.AddListener(TogglePause);
        if (stopButton)  stopButton.onClick.AddListener(Stop);
        if (nextButton)  nextButton.onClick.AddListener(Next);
        if (prevButton)  prevButton.onClick.AddListener(Prev);
        if (muteButton)  muteButton.onClick.AddListener(ToggleMute);
        if (nextButton2) nextButton2.onClick.AddListener(Next);
        if (prevButton2) prevButton2.onClick.AddListener(Prev);

        RefreshButtonStates();
        ChangeSongLabel();

        if (BGMManager.Instance)
        {
            isMuted = BGMManager.Instance.IsMuted;
            BGMManager.Instance.OnMuteChanged += HandleMuteChanged;
        }

        if (IsOpen && tracks != null && tracks.Count > 0)
        {
            _playedTrackIndices.Add(currentIndex);
            CheckAllOstAchievement();
        }
    }

    private void OnDestroy()
    {
        if (BGMManager.Instance)
            BGMManager.Instance.OnMuteChanged -= HandleMuteChanged;
    }

    private void HandleMuteChanged(bool muted)
    {
        isMuted = muted;
    }

    // ──────────────────────────────────────────────
    // Window
    // ──────────────────────────────────────────────

    /// <summary>Shows the media player window.</summary>
    public void Open()
    {
        if (!window || IsOpen) return;

        if (SettingsWindowManager.Instance) SettingsWindowManager.Instance.Close();

        window.SetActive(true);

        if (tracks != null && tracks.Count > 0)
        {
            _playedTrackIndices.Add(currentIndex);
            CheckAllOstAchievement();
        }
    }

    /// <summary>Hides the media player window.</summary>
    public void Close()
    {
        if (!window || !IsOpen) return;
        window.SetActive(false);
    }

    /// <summary>Opens the window when it is closed and closes it when it is open.</summary>
    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    // ──────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────

    /// <summary>Plays the track at <paramref name="index"/> through BGMManager.</summary>
    public void PlayTrack(int index, float fadeTime = 1.5f)
    {
        if (tracks == null || tracks.Count == 0) return;

        currentIndex = Mathf.Clamp(index, 0, tracks.Count - 1);
        isPaused = false;

        AudioClip clip = tracks[currentIndex];
        if (!clip)
        {
            h.Out("MediaPlayerManager: track at index", currentIndex, "is null");
            return;
        }

        if (BGMManager.Instance)
            BGMManager.Instance.PlayMusic(clip, fadeTime);

        _playedTrackIndices.Add(currentIndex);
        CheckAllOstAchievement();

        RefreshButtonStates();
        ChangeSongLabel();
    }

    /// <summary>
    /// Checks if all tracks in the playlist have been started and grants allOstAchievement.
    /// </summary>
    private void CheckAllOstAchievement()
    {
        if (!allOstAchievement || !AchievementManager.Instance || tracks == null || tracks.Count == 0) return;

        if (allOstAchievement.goalType == AchievementType.Value)
        {
            AchievementManager.Instance.SetProgress(allOstAchievement, _playedTrackIndices.Count);
        }

        if (_playedTrackIndices.Count >= tracks.Count)
        {
            AchievementManager.Instance.GrantAchievement(allOstAchievement);
            h.Out("MediaPlayerManager: player has started playing all ", tracks.Count, " OST tracks. Achievement granted!");
        }
    }

    /// <summary>Toggles pause on the BGMManager's current audio source.</summary>
    public void TogglePause()
    {
        AudioSource source = GetBGMSource();
        if (!source) return;

        if (isPaused)
        {
            source.UnPause();
            isPaused = false;
        }
        else
        {
            source.Pause();
            isPaused = true;
        }
    }

    /// <summary>Stops playback and rewinds to the beginning of the current track.</summary>
    public void Stop()
    {
        AudioSource source = GetBGMSource();
        if (!source) return;

        source.Pause();
        source.time = 0f;
        isPaused = true;
    }

    /// <summary>Advances to the next track in the playlist.</summary>
    public void Next()
    {
        if (tracks == null || tracks.Count == 0) return;
        if (currentIndex >= tracks.Count - 1) return;

        PlayTrack(currentIndex + 1);
    }

    /// <summary>Goes back to the previous track in the playlist.</summary>
    public void Prev()
    {
        if (tracks == null || tracks.Count == 0) return;
        if (currentIndex <= 0) return;

        PlayTrack(currentIndex - 1);
    }

    /// <summary>Toggles mute on the BGMManager.</summary>
    public void ToggleMute()
    {
        if (BGMManager.Instance)
        {
            BGMManager.Instance.ToggleMute();
            isMuted = BGMManager.Instance.IsMuted;
        }
        else
        {
            isMuted = !isMuted;
        }
    }

    // ──────────────────────────────────────────────
    // Internals
    // ──────────────────────────────────────────────

    private AudioSource GetBGMSource()
    {
        if (!BGMManager.Instance) return null;
        return BGMManager.Instance.CurrentAudioSource;
    }

    /// <summary>
    /// Updates <see cref="songLabel"/> to "{artist} - {song}" for the current track index.
    /// Falls back to the AudioClip name when no songName is defined for that index.
    /// </summary>
    private void ChangeSongLabel()
    {
        if (!songLabel) return;

        string song = (songNames != null && currentIndex < songNames.Count && !string.IsNullOrEmpty(songNames[currentIndex]))
            ? songNames[currentIndex]
            : (tracks != null && currentIndex < tracks.Count && tracks[currentIndex])
                ? tracks[currentIndex].name
                : null;

        if (string.IsNullOrEmpty(song))
        {
            songLabel.text = "";
            return;
        }

        string artist = (artistNames != null && currentIndex < artistNames.Count)
            ? artistNames[currentIndex]
            : null;

        songLabel.text = string.IsNullOrEmpty(artist) ? song : $"{artist} - {song}";
    }

    private void RefreshButtonStates()
    {
        if (tracks == null || tracks.Count == 0)
        {
            if (prevButton)  prevButton.interactable  = false;
            if (nextButton)  nextButton.interactable  = false;
            if (prevButton2) prevButton2.interactable = false;
            if (nextButton2) nextButton2.interactable = false;
            return;
        }

        bool canPrev = currentIndex > 0;
        bool canNext = currentIndex < tracks.Count - 1;

        if (prevButton)  prevButton.interactable  = canPrev;
        if (nextButton)  nextButton.interactable  = canNext;
        if (prevButton2) prevButton2.interactable = canPrev;
        if (nextButton2) nextButton2.interactable = canNext;
    }
}
