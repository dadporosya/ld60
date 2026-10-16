using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Remembers which scene the game came FROM, so a scene can behave differently depending on how it
/// was entered — the Desktop opening on a fresh boot is not the Desktop you come back to after a
/// battle (see <see cref="DesktopReturnDialogueManager"/>).
///
/// <para><b>Why it is static and self-installing.</b> Nothing is left for a designer to wire: the
/// hooks are installed once per run through <see cref="RuntimeInitializeOnLoadMethod"/>, before the
/// first scene comes up, and they work no matter who did the load — <see cref="SceneLoader"/>,
/// a raw <c>SceneManager.LoadScene</c> inside a cutscene, or a save being applied.</para>
///
/// <para><b>Reloads.</b> A scene that loads itself again (a save being applied on top, a window
/// restarting the scene) would make the naive "what was the previous scene" answer be the scene
/// itself, which is never what a caller means. <see cref="PreviousDifferentSceneName"/> — what
/// <see cref="CameFrom(string,bool)"/> uses by default — steps back over those repeats.</para>
///
/// <para><b>When the value is valid.</b> <see cref="SceneManager.sceneLoaded"/> runs after every
/// <c>Awake</c> in the new scene and before the first <c>Start</c>, so the values are already
/// correct in <c>Start</c> but are still the ones from the PREVIOUS load during <c>Awake</c>. Read
/// them from <c>Start</c>, or subscribe to <see cref="SceneEntered"/>, which fires in that same
/// window.</para>
/// </summary>
public static class SceneHistory
{
    /// <summary>
    /// The scene that was active immediately before the current one — the raw answer, repeats
    /// included. Empty on the first scene of the run. Most callers want
    /// <see cref="PreviousDifferentSceneName"/>.
    /// </summary>
    public static string PreviousSceneName { get; private set; } = "";

    /// <summary>The scene that is active now, as of the last single-mode load.</summary>
    public static string CurrentSceneName { get; private set; } = "";

    /// <summary>
    /// The last scene Unity unloaded. Recorded separately from the load bookkeeping so the history
    /// still says something useful if a load ever slips past <see cref="SceneManager.sceneLoaded"/>.
    /// </summary>
    public static string LastUnloadedSceneName { get; private set; } = "";

    /// <summary>Every scene entered this run, oldest first. Kept for debugging and flow checks.</summary>
    public static readonly List<string> History = new List<string>();

    /// <summary>
    /// The most recent scene in the history that is NOT the current one — i.e. where the player
    /// actually came from, looking past the current scene reloading itself any number of times.
    /// Empty when nothing else has been visited this run.
    /// </summary>
    public static string PreviousDifferentSceneName
    {
        get
        {
            // The last entry is the current scene; walk back over every repeat of it.
            for (int i = History.Count - 2; i >= 0; i--)
            {
                string entry = History[i];
                if (string.IsNullOrEmpty(entry)) continue;
                if (!Same(entry, CurrentSceneName)) return entry;
            }
            return "";
        }
    }

    /// <summary>
    /// Fired once per single-mode scene load, after every <c>Awake</c> and before the first
    /// <c>Start</c>. Arguments are (from, to); <c>from</c> is empty for the first scene of the run.
    ///
    /// <para>It is a static event, so anything that subscribes MUST unsubscribe in
    /// <c>OnDestroy</c> — a scene object that doesn't would be called again after it is gone.</para>
    /// </summary>
    public static event Action<string, string> SceneEntered;

    // The frame the last History entry was written on, so one load recorded twice (see Install)
    // cannot show up as the scene having reloaded itself.
    private static int lastRecordedFrame = -1;

    /// <summary>
    /// Statics outlive a play session when the Editor skips the domain reload, and the next run
    /// would start out believing it had come from wherever the last one ended. Wipe them first.
    ///
    /// <para>The subscriptions outlive it too — a handler left on <see cref="SceneManager"/> keeps
    /// firing in Edit Mode and in the next run — so they are dropped here as well.</para>
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;

        PreviousSceneName = "";
        CurrentSceneName = "";
        LastUnloadedSceneName = "";
        History.Clear();
        SceneEntered = null;
        lastRecordedFrame = -1;
    }

    /// <summary>
    /// <para><b>-= before +=.</b> With the Editor's domain reload turned off the delegate survives
    /// Stop, so a plain += would add a SECOND handler on the next Play, a third on the one after
    /// that, and every load would be recorded once per handler.</para>
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;

        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    /// <summary>
    /// Records the scene the run STARTED in. <see cref="SceneManager.sceneLoaded"/> is not raised
    /// for it when the Editor enters Play Mode on an already-open scene, which left the first scene
    /// of every Editor run missing from the history — and with it the answer to "did we come from
    /// the battle" for the very first transition of that run.
    ///
    /// <para>Runs after that scene is up and before the first <c>Start</c>, and does nothing when
    /// the event did fire.</para>
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RecordStartScene()
    {
        if (History.Count > 0) return;

        Scene active = SceneManager.GetActiveScene();
        if (!active.IsValid() || string.IsNullOrEmpty(active.name)) return;

        CurrentSceneName = active.name;
        History.Add(active.name);
        lastRecordedFrame = Time.frameCount;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Something loaded on top of the scene (an additive UI scene) is not a change of place.
        if (mode != LoadSceneMode.Single) return;

        // The same scene, in the same frame, is one load: a handler that slipped past Install's
        // -= (an old build of this file, a second subscriber elsewhere) would otherwise write the
        // scene in twice and make a normal load look like a self-reload.
        if (History.Count > 0 && lastRecordedFrame == Time.frameCount &&
            Same(History[History.Count - 1], scene.name))
            return;

        PreviousSceneName = CurrentSceneName;
        CurrentSceneName = scene.name;

        History.Add(scene.name);
        lastRecordedFrame = Time.frameCount;

        SceneEntered?.Invoke(PreviousSceneName, CurrentSceneName);
    }

    private static void OnSceneUnloaded(Scene scene)
    {
        LastUnloadedSceneName = scene.name;
    }

    /// <summary>
    /// True when the current scene was entered from <paramref name="sceneName"/>.
    ///
    /// <para>With <paramref name="ignoreSelfReloads"/> on (the default) the current scene reloading
    /// itself in between does not break the answer: coming back from the battle and then having the
    /// Desktop reload still counts as having come from the battle. Turn it off for a strict "the
    /// scene immediately before this one" test.</para>
    ///
    /// <para>Case-insensitive and whitespace-tolerant, because scene names here carry a numeric
    /// prefix ("2. MainGameplay") and are easy to mistype in an inspector field.</para>
    /// </summary>
    public static bool CameFrom(string sceneName, bool ignoreSelfReloads = true)
    {
        if (string.IsNullOrWhiteSpace(sceneName)) return false;

        if (Same(PreviousSceneName, sceneName)) return true;

        return ignoreSelfReloads && Same(PreviousDifferentSceneName, sceneName);
    }

    /// <summary>True when the current scene is the first one of the run (nothing came before it).</summary>
    public static bool IsFirstSceneOfSession => History.Count <= 1;

    /// <summary>True when the current scene was entered by reloading itself.</summary>
    public static bool CurrentSceneWasReloaded => Same(PreviousSceneName, CurrentSceneName);

    /// <summary>True when <paramref name="sceneName"/> was visited at any point this run.</summary>
    public static bool WasVisited(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName)) return false;
        for (int i = 0; i < History.Count; i++)
            if (Same(History[i], sceneName)) return true;
        return false;
    }

    /// <summary>The whole run as one line, for logging: <c>0. Intro > 1. Desktop > 2. MainGameplay</c>.</summary>
    public static string DescribeHistory()
    {
        return History.Count == 0 ? "(no scene loads recorded)" : string.Join(" > ", History);
    }

    /// <summary>
    /// Pretends the current scene was entered from <paramref name="sceneName"/>. For testing a
    /// scene you pressed Play in directly, which otherwise has no history at all.
    /// </summary>
    public static void ForcePreviousScene(string sceneName)
    {
        PreviousSceneName = sceneName ?? "";
    }

    /// <summary>
    /// Whether two names mean the same scene. Case-insensitive, whitespace-tolerant, and tolerant of
    /// the ordering prefix scene files here carry: <c>"MainGameplay"</c> and <c>"2. MainGameplay"</c>
    /// are the same scene.
    ///
    /// <para>The prefix is why this exists. Renumbering the scenes in the Project window renames them,
    /// and every inspector field naming a scene by hand — <c>onlyAfterScene</c>, a transition's target
    /// — would silently stop matching. Comparing past the prefix means the numbering is presentation,
    /// not something the game's wiring depends on.</para>
    /// </summary>
    public static bool SameScene(string a, string b) => Same(a, b);

    /// <summary>
    /// A scene name without its ordering prefix: the leading digits and the separator after them, as in
    /// <c>"2. MainGameplay"</c>, <c>"02 - MainGameplay"</c>, <c>"2_MainGameplay"</c>. Returned unchanged
    /// when there is no prefix, or when stripping it would leave nothing.
    /// </summary>
    public static string StripOrderPrefix(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return sceneName;

        int i = 0;
        while (i < sceneName.Length && char.IsDigit(sceneName[i])) i++;
        if (i == 0) return sceneName;

        // The digits only count as a prefix when something separates them from the name proper —
        // otherwise "2001" would be read as a prefix in front of nothing.
        while (i < sceneName.Length && (sceneName[i] == '.' || sceneName[i] == '-' ||
                                        sceneName[i] == '_' || char.IsWhiteSpace(sceneName[i]))) i++;

        string rest = sceneName.Substring(i);
        return string.IsNullOrWhiteSpace(rest) ? sceneName : rest;
    }

    private static bool Same(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;

        if (string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase)) return true;

        return string.Equals(StripOrderPrefix(a.Trim()), StripOrderPrefix(b.Trim()),
                             StringComparison.OrdinalIgnoreCase);
    }
}
