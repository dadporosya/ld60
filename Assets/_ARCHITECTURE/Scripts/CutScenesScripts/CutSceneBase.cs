using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;


public class CutSceneBase : MonoBehaviour
{
    public List<IEnumerator> cutsceneSteps = new List<IEnumerator>();
    [SerializeField] private bool runOnStart = false;

    [Tooltip("If off (default), the cutscene instance is destroyed automatically once the step sequence " +
             "finishes. If on, finishing the steps will NOT destroy it — the cutscene stays alive until " +
             "DestroyCutscene() is called (e.g. from a dialogue-end callback), so end methods that outlive " +
             "the step loop can still run.")]
    [SerializeField] protected bool customDestroy = false;

    /// <summary>
    /// Whether this cutscene plays itself when the scene starts. Exposed so something that has to act
    /// BEFORE that <c>Start</c> — an opening black frame put up at scene load — can tell whether the
    /// cutscene is actually going to run. Mirrors <see cref="CutSceneManager.RunsOnStart"/>.
    /// </summary>
    public bool RunsOnStart => runOnStart;

    private bool initialized=false;

    // True on the clone Run() creates. Never serialized, so a fresh clone always starts false.
    [System.NonSerialized] private bool isRunningInstance = false;

    private void Awake()
    {
        Init();
    }

    void Start()
    {
        if (runOnStart && !isRunningInstance) Run();
    }

    public virtual void Init()
    {
        initialized = true;
    }

    public void Run() => RunInstance();

    /// <summary>
    /// Like <see cref="Run"/>, but returns the running clone (null if nothing was started), so the
    /// caller can talk to it — e.g. hand it a callback for when it is done.
    /// </summary>
    public CutSceneBase RunInstance()
    {
        // if (!initialized) Init();

        // A running instance must never run itself again: it would Instantiate another clone, whose
        // Start() would Run() again, and so on — an exponential clone bomb that kills the engine
        // within a couple of frames. This is why the source object with runOnStart on is safe but its
        // clone is not.
        if (isRunningInstance)
        {
            h.Out($"{name}: Run() called on an already-running cutscene instance — ignored.");
            return null;
        }

        CutSceneBase instance = Instantiate(this);

        // The clone inherits every serialized field, runOnStart included. Both flags below stop the
        // clone's Start() from kicking off another Run().
        instance.runOnStart = false;
        instance.isRunningInstance = true;

        instance.StartCoroutine(instance.ExecuteSequence(instance.gameObject));
        return instance;
    }

    public IEnumerator ExecuteSequence(GameObject instanceToDestroy = null)
    {
        // h.Out("Execute Sequence");
        if (!initialized) Init();
        // h.Out(cutsceneSteps);

        // Remember what to tear down so DestroyCutscene() can finish the job later when customDestroy is on.
        if (instanceToDestroy) this.instanceToDestroy = instanceToDestroy;

        foreach (IEnumerator step in cutsceneSteps)
        {
            // h.Out(step);
            yield return StartCoroutine(step);
        }

        h.Out("Cutscene complete.");

        // When customDestroy is on, the sequence finishing is NOT the end of the cutscene — something
        // still running (a dialogue, a delayed callback, ...) owns the lifetime and must call
        // DestroyCutscene() when it is truly done. Otherwise, clean up immediately as before.
        if (customDestroy) yield break;

        DestroyCutscene();
    }

    /// <summary>
    /// Destroys this running cutscene instance. Call this when <see cref="customDestroy"/> is on and the
    /// cutscene's real end point is later than the step sequence finishing (e.g. from a dialogue-end
    /// callback), so end methods like DialogueEnd get to run before the object is torn down.
    /// </summary>
    public void DestroyCutscene()
    {
        Destroy(instanceToDestroy ? instanceToDestroy : gameObject);
    }

    // The GameObject to destroy when the cutscene ends (set in ExecuteSequence, defaults to this GameObject).
    private GameObject instanceToDestroy;

}