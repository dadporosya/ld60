using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Global scene loading entry point. Access via <c>SceneLoader.Instance</c>.
/// </summary>
public class SceneLoader : MonoBehaviour
{
    public static SceneLoader Instance;

    [Tooltip("Load scenes asynchronously instead of blocking.")]
    [SerializeField] private bool loadAsync = true;

    /// <summary>True while a scene load is in progress.</summary>
    public bool IsLoading { get; private set; }

    /// <summary>Progress of the current load, 0..1.</summary>
    public float Progress { get; private set; }

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance);
    }

    /// <summary>
    /// Loads a scene by name. The scene must be added to Build Settings.
    /// </summary>
    public void LoadScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            h.Out("SceneLoader: scene name is empty.");
            return;
        }

        sceneName = ResolveSceneName(sceneName);

        if (IsLoading)
        {
            h.Out($"SceneLoader: already loading a scene, ignoring '{sceneName}'.");
            return;
        }

        Time.timeScale = 1f;

        if (!loadAsync)
        {
            SceneManager.LoadScene(sceneName);
            return;
        }

        StartCoroutine(LoadRoutine(sceneName));
    }

    /// <summary>
    /// The name Unity will actually accept for <paramref name="sceneName"/>, looked up in Build
    /// Settings past any ordering prefix: asking for <c>"MainGameplay"</c> when the scene file is
    /// <c>"2. MainGameplay.unity"</c> returns <c>"2. MainGameplay"</c>.
    ///
    /// <para>Renumbering scenes in the Project window renames them, and <see cref="SceneManager"/>
    /// matches names exactly — so without this, every hand-typed scene name in an inspector (a
    /// transition's target, a menu button) breaks the moment the scenes are reordered. Names that
    /// match nothing in Build Settings are returned unchanged, so the load still fails loudly rather
    /// than quietly going somewhere else.</para>
    /// </summary>
    public static string ResolveSceneName(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName)) return sceneName;

        string wanted = sceneName.Trim();
        int count = SceneManager.sceneCountInBuildSettings;

        // An exact match always wins, so two scenes that differ only by prefix stay tellable apart.
        for (int i = 0; i < count; i++)
        {
            string candidate = NameOfBuildScene(i);
            if (string.Equals(candidate, wanted, System.StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        for (int i = 0; i < count; i++)
        {
            string candidate = NameOfBuildScene(i);
            if (!SceneHistory.SameScene(candidate, wanted)) continue;

            h.Out($"SceneLoader: '{wanted}' resolved to '{candidate}' in Build Settings.");
            return candidate;
        }

        return wanted;
    }

    private static string NameOfBuildScene(int buildIndex)
    {
        return System.IO.Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(buildIndex));
    }

    /// <summary>Reloads the currently active scene.</summary>
    public void ReloadCurrentScene()
    {
        LoadScene(SceneManager.GetActiveScene().name);
    }

    private IEnumerator LoadRoutine(string sceneName)
    {
        IsLoading = true;
        Progress = 0f;

        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        if (op == null)
        {
            h.Out($"SceneLoader: scene '{sceneName}' not found in Build Settings.");
            IsLoading = false;
            yield break;
        }

        while (!op.isDone)
        {
            Progress = Mathf.Clamp01(op.progress / 0.9f);
            yield return null;
        }

        Progress = 1f;
        IsLoading = false;
        h.Out($"SceneLoader: loaded scene '{sceneName}'.");
    }
}
