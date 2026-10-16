using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TalkableWithModels : Talkable
{
    [Header("Petya Talkable")]
    [SerializeField] private int currentModel = 0;
    public List<GameObject> models = new List<GameObject>();
    public List<AdditionalNodeSettings> nodesAdditionalSettings = new List<AdditionalNodeSettings>();

    [Header("SFX")]
    [SerializeField] private AudioClip beforeDialogueStartSFX;
    [SerializeField] private AudioClip onNodeStartTalkingSFX;
    [SerializeField] private AudioClip onDialogueEndSFX;
    
    [Header("Dialogue Animations")]
    [SerializeField] private AnimationBase beforeDialogueStartAnimation;
    [SerializeField] private AnimationBase onDialogueStartAnimation;
    [SerializeField] private AnimationBase onDialogueEndAnimation;
    [SerializeField] private AnimationBase onNodeStartAnimation;

    private const string EYES_TAG = "Eye";

    private AnimationControllerBase animController;

    // Guards the end-cutscene coroutine against being started twice for the same object
    // (e.g. a mid-dialogue disable followed immediately by the dialogue closing).
    private bool endCutscenePlaying;

    // Handle to the running end-cutscene coroutine, so a re-enable on the very next node can
    // cancel a still-playing end cutscene before it deactivates the object.
    private Coroutine endCutsceneCo;

    /// <summary>
    /// True while the object is leaving the screen: its end animation is still playing, so the
    /// GameObject is active but the object is on its way out and will deactivate itself.
    /// </summary>
    public bool IsPlayingEndCutscene => endCutscenePlaying;

    // Captured on Awake so the object can be returned to its initial state when a dialogue ends.
    private Vector3 initialLocalPosition;
    private Quaternion initialLocalRotation;
    private Vector3 initialLocalScale;

    // False until Awake has run. An object that starts (or is left) inactive never awakes, and
    // restoring an uncaptured "initial" transform would snap it to local zero.
    private bool initialTransformCaptured;

    [Tooltip("Show model 'currentModel' on start. When off, every model starts hidden and is " +
             "only enabled by the dialogue node that uses it.")]
    [SerializeField] private bool enableCurrentModelOnStart = false;

    public override void Awake()
    {
        base.Awake();

        // Remember the starting transform so dialogues can be reset to it later.
        initialLocalPosition = transform.localPosition;
        initialLocalRotation = transform.localRotation;
        initialLocalScale = transform.localScale;
        initialTransformCaptured = true;

        // enable model with currentModel id, disable others
        if (enableCurrentModelOnStart) ActivateModel(currentModel);
        else DisableModels(); // models are hidden by default; a dialogue node turns one on
    }

    /// <summary>
    /// Before the dialogue window opens: enable the model the first node will use and play
    /// the intro animation to completion, then fire the base "before dialogue" events.
    /// Talkable.TalkRoutine yields on this, so the window only opens once it finishes —
    /// keeping the talkable's intro animation strictly before the window's open animation.
    /// </summary>
    protected override IEnumerator BeforeDialogueStartRoutine()
    {
        // Enable the first node's model so the intro animation has something to play on.
        ActivateModel(GetFirstNodeModelId());

        PlaySFX(beforeDialogueStartSFX);

        if (beforeDialogueStartAnimation) yield return beforeDialogueStartAnimation.Play();

        base.BeforeDialogueStart();
    }

    /// <summary>
    /// When the dialogue actually starts: play the on-start animation, then fire the
    /// base "on dialogue start" events.
    /// </summary>
    public override void OnDialogueStart()
    {
        StartCoroutine(OnDialogueStartRoutine());
    }

    private IEnumerator OnDialogueStartRoutine()
    {
        if (onDialogueStartAnimation) yield return onDialogueStartAnimation.Play();

        base.OnDialogueStart();
    }

    /// <summary>
    /// When the dialogue ends: play the end animation first and fire the base
    /// "on dialogue end" events, then disable the model and return the object to
    /// its initial transform.
    /// </summary>
    public override void OnDialogueEnd()
    {
        StartCoroutine(OnDialogueEndRoutine(false));
    }

    /// <summary>
    /// Container-driven dialogue end: plays the end animation and fires the end events,
    /// then disables models and restores the transform. When deactivateAfter is true the
    /// object is also deactivated once the end animation has finished.
    /// </summary>
    public void PlayOnDialogueEnd(bool deactivateAfter)
    {
        if (!isActiveAndEnabled) return;
        if (endCutscenePlaying) return;
        endCutsceneCo = StartCoroutine(OnDialogueEndRoutine(deactivateAfter));
    }

    private IEnumerator OnDialogueEndRoutine(bool deactivateAfter = false)
    {
        if (endCutscenePlaying) yield break;
        endCutscenePlaying = true;

        PlaySFX(onDialogueEndSFX);

        if (onDialogueEndAnimation) yield return onDialogueEndAnimation.Play();

        base.OnDialogueEnd();

        DisableModels();
        ResetTransform();

        // Clear the guard/handle before any deactivation - deactivating the GameObject stops
        // this coroutine, so anything after SetActive(false) would never run.
        endCutscenePlaying = false;
        endCutsceneCo = null;

        if (deactivateAfter) gameObject.SetActive(false);
    }

    // ---------------------------------------------------------------------
    // SFX
    // ---------------------------------------------------------------------

    /// <summary>
    /// Plays a dialogue SFX through the SFXManager. No-op when the clip is unset or the
    /// manager doesn't exist yet (e.g. a dialogue triggered before managers finished Awake).
    /// </summary>
    private static void PlaySFX(AudioClip clip, Vector2? randomPitchRange = null)
    {
        if (!clip || SFXManager.Instance == null) return;
        SFXManager.Instance.PlayClip(clip, randomPitchRange: randomPitchRange);
    }

    /// <summary>Per-node talking SFX, with a slight pitch variation so repeats don't sound identical.</summary>
    private void PlayNodeTalkingSFX()
    {
        PlaySFX(onNodeStartTalkingSFX, new Vector2(0.99f, 1.01f));
    }

    /// <summary>
    /// Resolves the model id used by the first dialogue node (node id 0), falling back
    /// to model 0 when unset or out of range.
    /// </summary>
    private int GetFirstNodeModelId()
    {
        int modelId = 0;
        if (nodesAdditionalSettings.Count > 0 && nodesAdditionalSettings[0] != null)
            modelId = nodesAdditionalSettings[0].modelId;

        if (modelId < 0 || modelId >= models.Count) modelId = 0;
        return modelId;
    }

    /// <summary>
    /// Disables every model object.
    /// </summary>
    private void DisableModels()
    {
        for (int i = 0; i < models.Count; i++)
        {
            if (models[i]) models[i].SetActive(false);
        }
    }

    /// <summary>
    /// Restores the transform captured on Awake (position, rotation, scale).
    /// </summary>
    private void ResetTransform()
    {
        // Nothing captured yet (the object never awoke) - its transform is still the authored
        // one, so leave it alone instead of snapping it to zero.
        if (!initialTransformCaptured) return;

        transform.localPosition = initialLocalPosition;
        transform.localRotation = initialLocalRotation;
        transform.localScale = initialLocalScale;

        // We just snapped the transform back to its resting pose. Let the dialogue
        // animations re-sync their internal offset bookkeeping to it, otherwise an
        // animation with returnToTheInittialState = false keeps stale state and won't
        // replay on the next dialogue.
        if (beforeDialogueStartAnimation) beforeDialogueStartAnimation.SyncToRestingState();
        if (onDialogueStartAnimation) onDialogueStartAnimation.SyncToRestingState();
        if (onDialogueEndAnimation) onDialogueEndAnimation.SyncToRestingState();
    }

    public override void OnNodeStart(DialogueNode node = null)
    {
        int modelId = 0;
        bool enableEyes = false;
        bool talk = true;

        if (node != null
            && node.nodeId >= 0
            && node.nodeId < nodesAdditionalSettings.Count
            && nodesAdditionalSettings[node.nodeId] != null)
        {
            AdditionalNodeSettings nodeSettings = nodesAdditionalSettings[node.nodeId];
            modelId = nodeSettings.modelId;
            enableEyes = nodeSettings.enableEyes;
            talk = nodeSettings.talk;
        }

        // If the node is missing or its modelId is out of range, fall back to model 0.
        if (modelId < 0 || modelId >= models.Count) modelId = 0;

        SetModel(modelId);
        SetEyes(enableEyes);

        if (!talk)
        {
            // Silent node: no talking animation, no per-node animation. Stop whatever the
            // previous node left running.
            if (animController) animController.StopAnimations();
            animController = null;
            return;
        }

        PlayNodeTalkingSFX();

        // play the active model's talking animation controller for the duration of the node
        animController = GetAnimController();
        if (animController) StartCoroutine(animController.PlayAnimations());

        onNodeStartAnimation?.PlayInstantly();
    }

    public override void OnNodeEnd(DialogueNode node = null)
    {
        if (animController) animController.StopAnimations();
    }

    /// <summary>
    /// Finds the Talking animation controller on the currently active model, falling back
    /// to this object's own hierarchy. A controller counts as "talking" when its type is
    /// Talking or its targetTypes include Talking. When nothing on the model is explicitly
    /// typed as Talking, the first controller found is used (keeps older prefabs, whose
    /// controllers predate the type filter, working as before).
    /// </summary>
    private AnimationControllerBase GetAnimController()
    {
        if (currentModel >= 0 && currentModel < models.Count && models[currentModel])
        {
            AnimationControllerBase controller = FindTalkingController(models[currentModel].transform);
            if (controller) return controller;
        }

        return FindTalkingController(transform);
    }

    /// <summary>
    /// Searches <paramref name="root"/>'s hierarchy for a Talking animation controller,
    /// falling back to the first controller of any type.
    /// </summary>
    private static AnimationControllerBase FindTalkingController(Transform root)
    {
        AnimationControllerBase fallback = null;

        foreach (AnimationControllerBase controller in
                 root.GetComponentsInChildren<AnimationControllerBase>(true))
        {
            if (!controller) continue;

            if (controller.type == AnimationPreferences.Type.Talking
                || (controller.targetTypes != null
                    && controller.targetTypes.Contains(AnimationPreferences.Type.Talking)))
                return controller;

            if (!fallback) fallback = controller;
        }

        return fallback;
    }

    /// <summary>
    /// Switches to the model with the given id, disabling the previously active one.
    /// No-op only when that model is already the current one AND its GameObject is really
    /// active - otherwise it activates it. Without the active-state check the first call
    /// with the default modelId (0) would be skipped while the models are still disabled in
    /// the scene, leaving the model hidden for the first dialogue.
    /// </summary>
    private void SetModel(int modelId)
    {
        if (modelId == currentModel
            && modelId >= 0 && modelId < models.Count
            && models[modelId] && models[modelId].activeSelf) return;

        ActivateModel(modelId);
    }

    /// <summary>
    /// Enables the model at <paramref name="modelId"/> and disables every other model.
    /// </summary>
    private void ActivateModel(int modelId)
    {
        for (int i = 0; i < models.Count; i++)
        {
            if (models[i]) models[i].SetActive(i == modelId);
        }
        currentModel = modelId;
    }

    /// <summary>
    /// Enables/disables every object tagged "eyes" inside the currently active model.
    /// </summary>
    private void SetEyes(bool enable)
    {
        if (currentModel < 0 || currentModel >= models.Count) return;
        GameObject model = models[currentModel];
        if (!model) return;

        foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
        {
            if (t.CompareTag(EYES_TAG)) t.gameObject.SetActive(enable);
        }
    }

    // ---------------------------------------------------------------------
    // Container-driven control (used by DialogueContainerWithModels /
    // TalkableWModelsController when this object is one of several objects
    // steered by a single multi-object dialogue). These mirror the private
    // per-node logic in OnNodeStart/OnNodeEnd but take their values from an
    // external source instead of this object's own nodesAdditionalSettings.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Applies one dialogue node's settings to this object: toggles the whole object
    /// on/off, and when enabled selects the model and toggles its eyes.
    ///
    /// When <paramref name="talk"/> is true the object also "speaks" the node - it plays
    /// its onNodeStartAnimation and the active model's Talking animation controller for
    /// the node's duration. When false the object is only staged (model + eyes) and plays
    /// nothing; any talking animation left over from the previous node is stopped.
    /// </summary>
    public void PlayDialogueNode(bool enableObject, int modelId, bool enableEyes, bool talk = true)
    {
        SetupForNode(enableObject, modelId, enableEyes);
        if (!enableObject) return;

        if (!talk)
        {
            // Silent on this node: make sure the previous node's talking animation isn't
            // still running on the (possibly newly switched) model.
            if (animController) animController.StopAnimations();
            animController = null;
            return;
        }

        PlayNodeTalkingSFX();

        // play the active model's talking animation controller for the duration of the node
        animController = GetAnimController();
        if (animController) StartCoroutine(animController.PlayAnimations());

        onNodeStartAnimation?.PlayInstantly();
    }

    /// <summary>
    /// Enable-during-dialogue transition: stages the object for the current node (model + eyes)
    /// and plays its appearing cutscene - the same intro + on-start animations and events a
    /// first-node object gets before the window opens. Used when a node enables an object that
    /// was disabled on the previous node.
    ///
    /// The node itself is NOT started here: the container yields on this routine and only then
    /// runs PlayDialogueNode, so the object has finished appearing before it starts talking.
    /// </summary>
    public IEnumerator EnterDuringDialogueRoutine(int modelId, bool enableEyes)
    {
        PlaySFX(beforeDialogueStartSFX);

        // Stage the object so the appearing animation has the right model to play on,
        // without starting any per-node (talking) animation yet.
        SetupForNode(true, modelId, enableEyes);

        if (beforeDialogueStartAnimation) yield return beforeDialogueStartAnimation.Play();
        BeforeDialogueStart();

        if (onDialogueStartAnimation) yield return onDialogueStartAnimation.Play();
        base.OnDialogueStart();
    }

    /// <summary>
    /// Disable-during-dialogue transition: plays the object's end cutscene (end animation +
    /// end events), then disables its models, restores its transform and deactivates it - the
    /// same lifecycle a last-node object gets when the window closes. Used when a node disables
    /// an object that was enabled on the previous node; the end animation runs alongside the
    /// node start (mirrors the workflow's "runs its end animation simultaneously with node start").
    /// </summary>
    public void DisableDuringDialogue()
    {
        if (animController) animController.StopAnimations();

        // onDialogueEndSFX is played by OnDialogueEndRoutine, which PlayOnDialogueEnd starts.
        PlayOnDialogueEnd(true);
    }

    /// <summary>
    /// Enables/disables the object and, when enabled, selects its model and eyes without
    /// playing any per-node animation. Used to stage an object before its intro animation.
    /// </summary>
    public void SetupForNode(bool enableObject, int modelId, bool enableEyes)
    {
        if (!enableObject)
        {
            // Not used by this node: go all the way off (models hidden, transform restored,
            // GameObject deactivated) rather than just hiding the GameObject, so the object
            // can't come back later still wearing the previous dialogue's model or pose.
            ForceDeactivate();
            return;
        }

        gameObject.SetActive(true);

        // A freshly enabled object is not mid-end-cutscene. Cancel any still-running end
        // cutscene (would otherwise deactivate the object we just enabled) and clear the guard.
        if (endCutsceneCo != null) { StopCoroutine(endCutsceneCo); endCutsceneCo = null; }
        endCutscenePlaying = false;

        // Out-of-range / unset model falls back to model 0, matching OnNodeStart.
        if (modelId < 0 || modelId >= models.Count) modelId = 0;

        SetModel(modelId);
        SetEyes(enableEyes);
    }

    /// <summary>
    /// Puts the character on screen NOW, wearing the model his first dialogue node will use, without
    /// playing a single animation and without starting the dialogue.
    ///
    /// <para>Models are hidden until a dialogue node asks for one (see <see cref="Awake"/>), so simply
    /// switching the object on leaves an empty transform standing there and the character only pops
    /// into existence with his first line. A cutscene that wants him present for a beat before he
    /// speaks calls this after enabling him.</para>
    ///
    /// <para>Harmless to call ahead of the dialogue: <see cref="BeforeDialogueStartRoutine"/> selects
    /// the same model again, which is a no-op once it is already showing.</para>
    /// </summary>
    public void ShowIdle()
    {
        bool enableEyes = nodesAdditionalSettings.Count > 0
                          && nodesAdditionalSettings[0] != null
                          && nodesAdditionalSettings[0].enableEyes;

        SetupForNode(true, GetFirstNodeModelId(), enableEyes);
    }

    /// <summary>
    /// Container-driven "before dialogue start": plays this object's intro animation and
    /// fires its beforeDialogueStart events. The container must have already staged the
    /// object (SetupForNode) so the animation has a target.
    /// </summary>
    public IEnumerator PlayBeforeDialogueStart()
    {
        PlaySFX(beforeDialogueStartSFX);

        if (beforeDialogueStartAnimation) yield return beforeDialogueStartAnimation.Play();
        BeforeDialogueStart();
    }

    /// <summary>Stops the active model's animation controller at the end of a node.</summary>
    public void EndDialogueNode()
    {
        if (animController) animController.StopAnimations();
    }

    /// <summary>
    /// Returns the object to its resting state when a multi-object dialogue ends:
    /// stops animations, disables all models, restores the captured transform, and
    /// deactivates the object.
    /// </summary>
    public void ResetAfterDialogue()
    {
        ForceDeactivate();
    }

    /// <summary>
    /// Unconditionally takes the object off screen: cancels any in-flight end cutscene,
    /// stops animations, disables every model, restores the captured transform and sets the
    /// whole GameObject inactive.
    ///
    /// Used for objects a dialogue doesn't use (no end animation to play - they were never on
    /// screen) and as the teardown every other disable path funnels into. Cancelling the end
    /// cutscene here matters: deactivating a GameObject kills its coroutines, so an end cutscene
    /// interrupted from outside would otherwise leave endCutscenePlaying stuck true and make
    /// every later PlayOnDialogueEnd early-out - the object would then never deactivate again.
    /// </summary>
    public void ForceDeactivate()
    {
        if (endCutsceneCo != null) { StopCoroutine(endCutsceneCo); endCutsceneCo = null; }
        endCutscenePlaying = false;

        if (animController) animController.StopAnimations();
        animController = null;

        DisableModels();
        ResetTransform();

        gameObject.SetActive(false);
    }

    // private void Update()
    // {
    //     if (Input.GetKeyDown(KeyCode.Q))
    //     {
    //         Talk();
    //     }
    // }
}

[Serializable]
public class AdditionalNodeSettings
{
    public int modelId=0;
    public bool enableEyes = false;

    [Tooltip("Does this object talk on this node? When true it plays its onNodeStartAnimation " +
             "and its Talking animation controller for the duration of the node.")]
    public bool talk = true;
}
