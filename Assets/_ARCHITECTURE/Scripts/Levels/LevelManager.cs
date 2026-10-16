using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Spawns the playable level into the one gameplay scene and hides the level chooser while it is up.
/// Access via <c>LevelManager.Instance</c>.
///
/// <para>Levels are prefabs, not scenes — the map and the level live in the same scene, and entering
/// a level is "disable the chooser, instantiate the prefab". <see cref="TransitionManager"/> calls
/// <see cref="SpawnLevel"/> and <see cref="UnloadLevel"/> behind the cloud cover.</para>
///
/// <para><b>Game state.</b> <see cref="State"/> is where the game is: choosing a level on the map,
/// beating one, or having just beaten it. Nothing in this project decides on its own that a level is
/// beaten yet — call <see cref="CompleteLevel"/> from whatever does.</para>
///
/// <para>The previous project's tile/level-settings/popup subsystem (<c>Level</c>, <c>LevelSettings</c>,
/// <c>TileBase</c>, <c>PopupManager</c>) never came over; this manager keeps only the parts that do not
/// depend on it.</para>
/// </summary>
public class LevelManager : MonoBehaviour
{
    /// <summary>Where the game is. Read it as <c>LevelManager.Instance.State</c>.</summary>
    public enum GameState
    {
        /// <summary>On the city map, picking a level. No level is loaded.</summary>
        ChoosingLevel,

        /// <summary>A level is loaded and being played — the main state.</summary>
        BeatingLevel,

        /// <summary>The loaded level has been beaten.</summary>
        LevelCompleted
    }

    /// <summary>Fires with the new state whenever <see cref="State"/> changes.</summary>
    [System.Serializable]
    public class GameStateEvent : UnityEvent<GameState> { }

    public static LevelManager Instance;

    [Tooltip("Level prefabs to choose from; each spawn picks one at random. Its pivot should sit at the " +
             "level's centre, on the ground: the camera is framed on the spawn point, and the pivot lands there.")]
    [SerializeField] private List<GameObject> levelPrefabs = new List<GameObject>();

    [Tooltip("Where the level is spawned, and the point the camera looks at once it is in. Empty = this " +
             "object's own transform. Keep it OUTSIDE the chooser root, or it is disabled along with it.")]
    [SerializeField] private Transform levelSpawnPoint;

    [Tooltip("Root of the level chooser (the city map). Disabled while a level is loaded, enabled again " +
             "when it is unloaded.")]
    [SerializeField] private GameObject levelChooserRoot;

    [Header("Events")]
    public GameStateEvent OnStateChanged = new GameStateEvent();

    [Tooltip("Fired once when the level is beaten.")]
    public UnityEvent OnLevelCompleted = new UnityEvent();

    /// <summary>The spawned level instance, or null while on the chooser.</summary>
    public GameObject CurrentLevel { get; private set; }

    /// <summary>The prefabs a spawn picks from.</summary>
    public IReadOnlyList<GameObject> LevelPrefabs => levelPrefabs;

    /// <summary>The point the camera should look at inside the level.</summary>
    public Vector3 LevelFocusPoint => SpawnPoint.position;

    private Transform SpawnPoint => levelSpawnPoint ? levelSpawnPoint : transform;

    private GameState state = GameState.ChoosingLevel;

    /// <summary>Where the game is. Set through <see cref="SetState"/>, which fires <see cref="OnStateChanged"/>.</summary>
    public GameState State => state;

    /// <summary>True while a level is loaded and not yet beaten — the state gameplay runs in.</summary>
    public bool IsBeatingLevel => state == GameState.BeatingLevel;

    /// <summary>True while the player is on the city map with no level loaded.</summary>
    public bool IsChoosingLevel => state == GameState.ChoosingLevel;

    /// <summary>True once the loaded level has been beaten.</summary>
    public bool IsLevelCompleted => state == GameState.LevelCompleted;

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance);
    }

    /// <summary>A random non-empty entry of <see cref="levelPrefabs"/>, or null.</summary>
    public GameObject RandomLevelPrefab()
    {
        List<GameObject> valid = new List<GameObject>();
        foreach (GameObject prefab in levelPrefabs)
            if (prefab) valid.Add(prefab);

        return valid.Count > 0 ? valid[Random.Range(0, valid.Count)] : null;
    }

    /// <summary>
    /// Replaces any loaded level with a fresh instance of a random prefab and hides the chooser.
    /// Returns the instance, or null when there is nothing to spawn (the chooser is then left as it was).
    /// </summary>
    public GameObject SpawnLevel()
    {
        GameObject prefab = RandomLevelPrefab();
        if (!prefab)
        {
            h.Out("LevelManager: no level prefabs assigned, nothing to spawn.");
            return null;
        }

        DestroyCurrentLevel();

        Transform point = SpawnPoint;
        CurrentLevel = Instantiate(prefab, point.position, point.rotation);

        if (levelChooserRoot) levelChooserRoot.SetActive(false);

        SetState(GameState.BeatingLevel);

        h.Out($"LevelManager: spawned level '{prefab.name}'.");
        return CurrentLevel;
    }

    /// <summary>Destroys the loaded level and shows the chooser again.</summary>
    public void UnloadLevel()
    {
        DestroyCurrentLevel();
        if (levelChooserRoot) levelChooserRoot.SetActive(true);

        SetState(GameState.ChoosingLevel);
    }

    // ---- state -----------------------------------------------------------------------------

    /// <summary>
    /// Moves the game to <paramref name="next"/> and fires <see cref="OnStateChanged"/>. Setting the
    /// state it is already in does nothing, so listeners never see a state twice.
    /// </summary>
    public void SetState(GameState next)
    {
        if (state == next) return;

        state = next;
        h.Out($"LevelManager: state -> {next}.");
        OnStateChanged.Invoke(next);
    }

    /// <summary>Marks the loaded level beaten and fires <see cref="OnLevelCompleted"/>.</summary>
    public void CompleteLevel()
    {
        if (!IsBeatingLevel) return;

        SetState(GameState.LevelCompleted);
        OnLevelCompleted.Invoke();
    }

    /// <summary>
    /// Takes the player back to the city map: flies out through <see cref="TransitionManager"/> when
    /// there is one, otherwise unloads the level on the spot. Either way the level ends up gone and
    /// the state <see cref="GameState.ChoosingLevel"/> — the transition calls
    /// <see cref="UnloadLevel"/> itself, behind the cloud cover.
    /// </summary>
    public void ReturnToLevelSelection()
    {
        if (TransitionManager.Instance && CurrentLevel)
        {
            TransitionManager.Instance.TransitionToLevelSelection();
            return;
        }

        if (!TransitionManager.Instance)
            h.Out("LevelManager: no TransitionManager, returning to the chooser without the flight out.");

        UnloadLevel();
    }

    /// <summary>
    /// World-space box around every renderer of the loaded level — what the RTS camera is clamped to
    /// while the level is up. False when no level is loaded or it has nothing rendered.
    /// </summary>
    public bool TryGetLevelBounds(out Bounds bounds)
    {
        bounds = default;
        if (!CurrentLevel) return false;

        bool found = false;
        foreach (Renderer r in CurrentLevel.GetComponentsInChildren<Renderer>())
        {
            if (found) bounds.Encapsulate(r.bounds);
            else { bounds = r.bounds; found = true; }
        }

        return found;
    }

    private void DestroyCurrentLevel()
    {
        if (CurrentLevel) Destroy(CurrentLevel);
        CurrentLevel = null;
    }
}
