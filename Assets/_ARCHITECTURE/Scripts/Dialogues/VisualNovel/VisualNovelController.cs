using System;
using System.Collections;
using PrimeTween;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Plays a visual novel dialogue from anywhere in the game. <see cref="RunNovel"/> opens the visual
/// novel scene and plays the dialogue on that scene's <see cref="VisualNovelTalkable"/>. If the
/// scene is already open, it skips the load. Access via <c>VisualNovelController.Instance</c>.
///
/// <para>The controller does not need to survive the scene load. Once the new scene is in, the
/// dialogue is started from that scene's own <see cref="VisualNovelTalkable"/>, so the controller
/// can sit in any scene, whether or not it is a child of the managers.</para>
///
/// <para>Given a scene to open afterwards (<see cref="RunNovel(VisualNovelDialogueContainer, string)"/>), the
/// controller fades the screen to black once the dialogue and its end fade are over
/// (<see cref="nextSceneFadeDuration"/>, through the novel scene's <see cref="FadeImageController"/>) and opens it.</para>
/// </summary>
public class VisualNovelController : MonoBehaviour
{
    public static VisualNovelController Instance;

    [Tooltip("The scene that holds the visual novel stage (a VisualNovelTalkable). It must be in " +
             "Build Settings. An ordering prefix (\"2. VisualNovel\") is resolved automatically.")]
    [SerializeField] private string sceneName = "VisualNovel";

    [Tooltip("When a dialogue is run with a scene to open afterwards, seconds the screen takes to fade to " +
             "black (FadeImageController) between the dialogue's end and the scene load. 0: straight cut.")]
    [SerializeField, Min(0f)] private float nextSceneFadeDuration = 0.75f;

    /// <summary>
    /// True while the visual novel scene is loading for a <see cref="RunNovel"/> call. It is static
    /// because the load can outlive the controller that started it.
    /// </summary>
    public static bool IsLoading { get; private set; }

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance);
    }

    /// <summary>
    /// Opens the visual novel scene, unless it is already open, and plays <paramref name="dialogue"/>
    /// on its <see cref="VisualNovelTalkable"/>.
    /// </summary>
    public void RunNovel(VisualNovelDialogueContainer dialogue) => RunNovel(dialogue, null);

    /// <summary>
    /// Like <see cref="RunNovel(VisualNovelDialogueContainer)"/>, then opens <paramref name="sceneAfterEnd"/> once
    /// the dialogue is over (played through or skipped), after its end fade and a fade to black. Null or
    /// empty: stays on the novel scene. Ordering prefixes are resolved, as for <see cref="sceneName"/>.
    /// </summary>
    public void RunNovel(VisualNovelDialogueContainer dialogue, string sceneAfterEnd)
    {
        if (!dialogue)
        {
            h.Out("VisualNovelController: dialogue is null.");
            return;
        }

        if (IsLoading)
        {
            h.Out($"VisualNovelController: already opening the visual novel scene, ignoring '{dialogue.name}'.");
            return;
        }

        Action<VisualNovelTalkable> then = null;
        if (!string.IsNullOrWhiteSpace(sceneAfterEnd))
        {
            float fade = nextSceneFadeDuration;
            then = talkable => talkable.StartCoroutine(FadeAndLoad(sceneAfterEnd, fade));
        }

        Scene open = FindLoadedScene(sceneName);
        if (open.IsValid())
        {
            Play(open, dialogue, then);
            return;
        }

        string target = SceneLoader.ResolveSceneName(sceneName);

        // A paused game (timeScale 0) would otherwise freeze the novel's transitions.
        Time.timeScale = 1f;

        AsyncOperation op = SceneManager.LoadSceneAsync(target);
        if (op == null)
        {
            h.Out($"VisualNovelController: scene '{target}' not found in Build Settings.");
            return;
        }

        IsLoading = true;
        op.completed += _ =>
        {
            IsLoading = false;
            Play(FindLoadedScene(target), dialogue, then, waitForStart: true);
        };
    }

    /// <summary>
    /// Plays <paramref name="dialogue"/> on the <see cref="VisualNovelTalkable"/> in <paramref name="scene"/>;
    /// <paramref name="then"/> (may be null) runs with that talkable once the dialogue is over.
    /// </summary>
    private static void Play(Scene scene, VisualNovelDialogueContainer dialogue, Action<VisualNovelTalkable> then,
        bool waitForStart = false)
    {
        VisualNovelTalkable talkable = FindTalkable(scene);
        if (!talkable)
        {
            h.Out($"VisualNovelController: no VisualNovelTalkable in scene '{scene.name}'.");
            return;
        }

        if (!talkable.isActiveAndEnabled)
        {
            h.Out($"VisualNovelController: VisualNovelTalkable '{talkable.name}' is inactive, can't play '{dialogue.name}'.");
            return;
        }

        Action onFinished = then == null ? null : () => then(talkable);
        if (waitForStart) talkable.StartCoroutine(PlayNextFrame(talkable, dialogue, onFinished));
        else talkable.PlayDialogue(dialogue, onFinished);
    }

    /// <summary>
    /// Waits one frame before playing. This lets a freshly loaded scene run its Start methods first:
    /// the stage resets its own state there, and that would otherwise undo the dialogue's first node.
    /// </summary>
    private static IEnumerator PlayNextFrame(VisualNovelTalkable talkable, VisualNovelDialogueContainer dialogue,
        Action onFinished)
    {
        yield return null;
        if (talkable && dialogue) talkable.PlayDialogue(dialogue, onFinished);
    }

    /// <summary>Fades to black through the <see cref="FadeImageController"/> (if any), then opens <paramref name="scene"/>.</summary>
    private static IEnumerator FadeAndLoad(string scene, float fadeDuration)
    {
        FadeImageController fade = FadeImageController.Instance;
        if (fade && fadeDuration > 0f)
        {
            Tween tween = fade.FadeIn(fadeDuration, Ease.InOutSine, useUnscaledTime: true);
            while (tween.isAlive) yield return null;
        }

        LoadScene(scene);
    }

    /// <summary>
    /// Opens <paramref name="scene"/> (ordering prefixes resolved, see <see cref="SceneLoader.ResolveSceneName"/>),
    /// unpausing time first. Logs when it isn't in Build Settings.
    /// </summary>
    public static void LoadScene(string scene)
    {
        if (string.IsNullOrWhiteSpace(scene))
        {
            h.Out("VisualNovelController: no scene to open.");
            return;
        }

        string target = SceneLoader.ResolveSceneName(scene);
        Time.timeScale = 1f;
        if (SceneManager.LoadSceneAsync(target) == null)
            h.Out($"VisualNovelController: scene '{target}' not found in Build Settings.");
    }

    /// <summary>
    /// The loaded scene matching <paramref name="name"/>, ignoring any ordering prefix. Additively
    /// loaded scenes count too. Returns an invalid scene when none matches.
    /// </summary>
    private static Scene FindLoadedScene(string name)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.isLoaded && SceneHistory.SameScene(scene.name, name)) return scene;
        }

        return default;
    }

    /// <summary>The <see cref="VisualNovelTalkable"/> in <paramref name="scene"/>, or any one if the scene has none.</summary>
    private static VisualNovelTalkable FindTalkable(Scene scene)
    {
        VisualNovelTalkable[] all =
            FindObjectsByType<VisualNovelTalkable>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (VisualNovelTalkable talkable in all)
            if (talkable.gameObject.scene == scene) return talkable;

        return all.Length > 0 ? all[0] : null;
    }
}
