using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

/// <summary>
/// Web builds cannot play embedded <see cref="VideoClip"/> assets — Unity hands playback to the
/// browser's &lt;video&gt; element, which needs a real URL to fetch. Any VideoPlayer left on
/// <c>VideoSource.VideoClip</c> renders nothing at all in the browser.
///
/// This rewires every such VideoPlayer at runtime to <c>VideoSource.Url</c>, pointing at a copy of
/// the same file in <c>StreamingAssets</c> (matched by clip name, e.g. clip "dragPathDemo" ->
/// <c>StreamingAssets/dragPathDemo.mp4</c>). Nothing has to be wired up in the inspector, and the
/// Editor and standalone builds are untouched.
///
/// Requirements for each clip used on the Web:
///   * a browser-playable copy in Assets/StreamingAssets/ named &lt;clipName&gt;.mp4
///     (H.264 main profile, yuv420p, AAC audio, +faststart)
///
/// See <see cref="k_Extension"/> if you ship .webm instead.
/// </summary>
public static class WebGlVideoClipFallback
{
#if UNITY_WEBGL && !UNITY_EDITOR
    private const string k_Extension = ".mp4";

    private static readonly HashSet<VideoPlayer> s_Patched = new HashSet<VideoPlayer>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => PatchAll();

    /// <summary>Rewire every VideoPlayer currently loaded. Safe to call repeatedly.</summary>
    public static void PatchAll()
    {
        VideoPlayer[] players = Object.FindObjectsByType<VideoPlayer>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (int i = 0; i < players.Length; i++)
            Patch(players[i]);
    }

    private static void Patch(VideoPlayer player)
    {
        if (player == null) return;
        if (player.source == VideoSource.Url) return;   // already URL-driven, nothing to do
        if (s_Patched.Contains(player)) return;

        VideoClip clip = player.clip;
        if (clip == null) return;

        s_Patched.Add(player);

        bool shouldPlay = player.playOnAwake || player.isPlaying;

        player.Stop();
        player.playOnAwake = false;
        player.source      = VideoSource.Url;
        player.url         = Path.Combine(Application.streamingAssetsPath, clip.name + k_Extension);

        player.errorReceived -= OnError;
        player.errorReceived += OnError;

        if (shouldPlay)
        {
            player.prepareCompleted -= OnPrepared;
            player.prepareCompleted += OnPrepared;
        }

        player.Prepare();
    }

    private static void OnPrepared(VideoPlayer player)
    {
        player.prepareCompleted -= OnPrepared;
        player.Play();
    }

    private static void OnError(VideoPlayer player, string message)
    {
        Debug.LogError($"[WebGlVideoClipFallback] '{player.name}' could not play {player.url} — {message}. " +
                       "Check that the file exists in StreamingAssets and is H.264/AAC in an MP4 container.");
    }
#endif
}
