using Unity.VisualScripting;
using UnityEngine;

/// <summary>
/// Runs cutscenes, and remembers whether the one it is holding — its <see cref="currentCutScene"/> /
/// <see cref="currentCutScenePath"/>, the beat a scene opens with when <see cref="runOnStart"/> is on —
/// has already been played.
///
/// <para><b>Why the bookkeeping is static.</b> This is a DontDestroyOnLoad singleton, and a scene's own
/// manager destroys itself when one already exists — but its <see cref="Start"/> still runs to the end,
/// cutscene included. So "was the opening cutscene played" cannot live on an instance: the instance that
/// plays it is often the one that is about to disappear. <see cref="AssignedCutscenePlayed"/> is a static
/// for exactly that reason, and it is what <see cref="SaveLoadManager"/> writes into a save.</para>
///
/// <para><b>What a load does with it.</b> A save that was taken after the opening cutscene had played
/// calls <see cref="ArmStartSuppression"/> before the scene switch, and the fresh scene's manager then
/// skips its <see cref="runOnStart"/> cutscene — loading a run in progress must not replay the intro.
/// The arming names the scene it belongs to and is consumed by that scene's start, so it can neither be
/// swallowed by a scene that comes up in between nor leak into a later, legitimate start (a restart, a
/// new game).</para>
/// </summary>
public class CutSceneManager : MonoBehaviour
{
    public static CutSceneManager Instance;

    public CutSceneBase currentCutScene;
    public string currentCutScenePath;

    public string defaultLabel = "Cutscenes/";

    [SerializeField] private bool runOnStart=false;

    /// <summary>
    /// Whether this manager plays its assigned cutscene when the scene starts. Exposed so something
    /// that has to act BEFORE that <see cref="Start"/> — the gameplay intro's opening black frame, put
    /// up at scene load — can tell whether the cutscene is actually going to run.
    /// </summary>
    public bool RunsOnStart => runOnStart;

    // ──────────────────────────────────────────────
    // Assigned-cutscene bookkeeping (saved / restored)
    // ──────────────────────────────────────────────

    /// <summary>
    /// True once the assigned cutscene (<see cref="currentCutScene"/> or
    /// <see cref="currentCutScenePath"/>) has been run in this session. Saved by
    /// <see cref="SaveLoadManager"/> and restored with the save.
    /// </summary>
    public static bool AssignedCutscenePlayed { get; private set; }

    /// <summary>Name of the assigned cutscene that was played. Kept so a save file reads sensibly.</summary>
    public static string AssignedCutsceneName { get; private set; } = "";

    /// <summary>
    /// Scene whose opening cutscene is to be skipped, armed by <see cref="SaveLoadManager"/> just before
    /// a load switches scenes, when the save says the assigned cutscene had already been played. Empty =
    /// nothing is suppressed.
    ///
    /// <para>It is a scene name rather than a bare flag so a scene that comes up in between (a loading
    /// screen, an intro) cannot swallow it — only the scene the save is going to be applied in consumes
    /// it. See <see cref="ArmStartSuppression"/>.</para>
    /// </summary>
    private static string suppressStartCutsceneInScene = "";

    /// <summary>True while a scene start is waiting to have its opening cutscene skipped.</summary>
    public static bool StartCutsceneSuppressionArmed => !string.IsNullOrEmpty(suppressStartCutsceneInScene);

    /// <summary>
    /// Arms the skip for <paramref name="sceneName"/> (empty or null = the next scene that starts,
    /// whichever it is). Call it BEFORE the scene switch — a scene's cutscene runs in its own
    /// <c>Start</c>, which is far too early for anything on the far side to intervene.
    /// </summary>
    public static void ArmStartSuppression(string sceneName)
    {
        suppressStartCutsceneInScene = string.IsNullOrWhiteSpace(sceneName) ? "*" : sceneName.Trim();
    }

    /// <summary>Drops an armed skip that was never used, so it cannot ambush a later scene start.</summary>
    public static void ClearStartSuppression()
    {
        suppressStartCutsceneInScene = "";
    }

    /// <summary>
    /// True when this scene start is the one that was armed — and takes the arming with it, so exactly
    /// one scene start is ever skipped.
    /// </summary>
    private static bool ConsumeStartSuppression()
    {
        if (!StartCutsceneSuppressionArmed) return false;

        // A scene that is not the one the load is headed for leaves the arming alone and opens normally.
        if (suppressStartCutsceneInScene != "*" &&
            !string.Equals(suppressStartCutsceneInScene,
                           UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                           System.StringComparison.OrdinalIgnoreCase))
            return false;

        suppressStartCutsceneInScene = "";
        return true;
    }

    /// <summary>
    /// Statics outlive a play session when the Editor skips the domain reload, and the next run would
    /// start believing the intro had already been played. Wipe them before anything loads.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        AssignedCutscenePlayed = false;
        AssignedCutsceneName = "";
        suppressStartCutsceneInScene = "";
    }

    /// <summary>Records that the assigned cutscene has been played. Safe to call more than once.</summary>
    public static void MarkAssignedCutscenePlayed(string cutsceneName = null)
    {
        AssignedCutscenePlayed = true;
        if (!string.IsNullOrEmpty(cutsceneName)) AssignedCutsceneName = cutsceneName;
    }

    /// <summary>Puts the played/not-played bookkeeping back to what a save says it was.</summary>
    public static void RestorePlayedState(bool played, string cutsceneName = null)
    {
        AssignedCutscenePlayed = played;
        AssignedCutsceneName = cutsceneName ?? "";
    }

    private void Start()
    {
        h.CreateStaticInstance(this, ref Instance);

        if (!runOnStart) return;

        // Consumed here rather than at the top: a manager with nothing to run on start is not the one
        // the skip was armed for, and must not eat it.
        if (ConsumeStartSuppression())
        {
            h.Out($"{name}: start cutscene skipped — the loaded save says it had already been played.");
            return;
        }

        RunCutscene();
    }

    public void RunCutscene()
    {
        if (currentCutScene)
        {
            MarkAssignedCutscenePlayed(currentCutScene.name);
            RunCutscene(currentCutScene);
            return;
        }

        if (currentCutScenePath != "")
        {
            MarkAssignedCutscenePlayed(currentCutScenePath);
            RunCutscene(currentCutScenePath);
            return;
        }

        h.Out("Current cutscene is not assigned");
    }

    public void RunCutscene(CutSceneBase scene)
    {
        // run pfb
        scene.Run();
        // Destroy(scene.gameObject);
    }

    public void RunCutscene(string path)
    {
        string fullPath = defaultLabel + path;
        var cutScene = Resources.Load<CutSceneBase>(fullPath);
        if (!cutScene)
        {
            h.Out("cutscene " + fullPath + " is not found");
            return;
        }
        cutScene.Run();

    }

    public CutSceneBase LoadCutscene(string path)
    {
        return Resources.Load<CutSceneBase>(defaultLabel + path);
    }
}
