using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-object settings for a single dialogue node. There is one of these for every
/// object in DialogueContainerWithModels.objects, on every node.
/// </summary>
[Serializable]
public class ModelObjectNodeSettings
{
    [Tooltip("Enable or disable this object when the node starts. Off by default: an object " +
             "is only shown on the nodes that explicitly switch it on.")]
    public bool enableObject = false;

    [Tooltip("Which model on the object to activate (index into its models list).")]
    public int modelId = 0;

    [Tooltip("Enable the eyes of the selected model.")]
    public bool enableEyes = false;

    [Tooltip("Does this object talk on this node? When true the object plays its " +
             "onNodeStartAnimation and its Talking animation controller for the duration " +
             "of the node. When false it just stands there with the chosen model/eyes.")]
    public bool talk = true;
}

/// <summary>
/// Settings for one dialogue node across every object in the container.
/// objectSettings is kept parallel to DialogueContainerWithModels.objects
/// (one entry per object).
/// </summary>
[Serializable]
public class MultiModelNodeSettings
{
    [Tooltip("Optional: index of the object (in the container's objects list) that speaks " +
             "this node - its name/portrait/voice fill the dialogue window. -1 = leave the " +
             "node's own speaker fields untouched.")]
    public int speakerObjectIndex = -1;

    [Tooltip("One entry per object in the container. Order matches the objects list.")]
    public List<ModelObjectNodeSettings> objectSettings = new List<ModelObjectNodeSettings>();
}

/// <summary>
/// A DialogueContainer override that steers several TalkableWithModels objects at once.
///
/// The objects list defines how many objects this dialogue controls (leave the entries
/// null in the asset - a TalkableWModelsController assigns the real scene objects at
/// runtime via AssignObjects).
///
/// nodeSettings is a grid kept parallel to the base nodes list: one MultiModelNodeSettings
/// per node, each holding one ModelObjectNodeSettings per object. When the DialogueManager
/// starts a node it calls OnNodeStart, which applies that node's row to every object.
/// Dialogue-level lifecycle (RunBeforeDialogueStart / OnDialogueStart / OnDialogueEnd) is
/// forwarded to each object so their intro/start/end animations and events fire, mirroring
/// the single-object Talkable flow.
/// </summary>
[CreateAssetMenu(fileName = "DialogueContainerWithModels", menuName = "Dialogues/DialogueContainerWithModels")]
public class DialogueContainerWithModels : DialogueContainer, IMultiObjectDialogue
{
    [Header("Multi-object dialogue")]
    [Tooltip("Objects this dialogue controls. Leave null in the asset to just define the " +
             "count; a TalkableWModelsController assigns the real scene objects at runtime.")]
    public List<TalkableWithModels> objects = new List<TalkableWithModels>();

    [Tooltip("Per-node, per-object settings. Kept in sync with 'nodes' x 'objects' " +
             "automatically. Index i matches nodes[i]; each row has one entry per object.")]
    public List<MultiModelNodeSettings> nodeSettings = new List<MultiModelNodeSettings>();

    /// <summary>
    /// The scene objects bound for the current playthrough. Runtime-only: never serialized into
    /// the asset, so playing a dialogue can't grow (and permanently dirty) the authored slot list.
    /// Exactly as long as the authored 'objects' list - a controller with more objects than this
    /// dialogue declares simply doesn't drive the extras, so they stay disabled.
    /// </summary>
    [NonSerialized] private List<TalkableWithModels> boundObjects;

    /// <summary>
    /// Objects the caller handed over that this dialogue has no slot for. They are never driven,
    /// so the dialogue force-deactivates them once at bind time and never touches them again.
    /// </summary>
    [NonSerialized] private List<TalkableWithModels> unboundObjects;

    /// <summary>
    /// What the previous node wanted each bound object's enabled state to be. Used instead of
    /// reading GameObject.activeSelf, which lies during a disable transition: an object whose
    /// end cutscene is still playing is off as far as the dialogue is concerned but its
    /// GameObject is still active until the animation finishes.
    /// </summary>
    [NonSerialized] private bool[] prevEnabled;

    /// <summary>Objects actually driven at runtime, falling back to the authored slots in the editor.</summary>
    private List<TalkableWithModels> Bound => boundObjects ?? objects;

    protected override void OnEnable()
    {
        base.OnEnable();
        SyncNodeSettings();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        SyncNodeSettings();
    }
#endif

    /// <summary>
    /// Resizes nodeSettings so there is exactly one row per node, and one per-object entry
    /// per row. Preserves existing authored values.
    /// </summary>
    public void SyncNodeSettings()
    {
        if (nodeSettings == null) nodeSettings = new List<MultiModelNodeSettings>();

        int nodeCount = nodes != null ? nodes.Count : 0;
        int objectCount = objects != null ? objects.Count : 0;

        while (nodeSettings.Count < nodeCount) nodeSettings.Add(new MultiModelNodeSettings());
        while (nodeSettings.Count > nodeCount) nodeSettings.RemoveAt(nodeSettings.Count - 1);

        for (int i = 0; i < nodeSettings.Count; i++)
        {
            MultiModelNodeSettings row = nodeSettings[i];
            if (row == null) { row = new MultiModelNodeSettings(); nodeSettings[i] = row; }
            if (row.objectSettings == null) row.objectSettings = new List<ModelObjectNodeSettings>();

            while (row.objectSettings.Count < objectCount) row.objectSettings.Add(new ModelObjectNodeSettings());
            while (row.objectSettings.Count > objectCount) row.objectSettings.RemoveAt(row.objectSettings.Count - 1);
        }
    }

    /// <summary>
    /// Binds the concrete scene objects this dialogue should drive. Called by the controller
    /// right before the dialogue starts. Slot i of the asset becomes runtimeObjects[i].
    ///
    /// Only as many objects as this dialogue declares are bound: a controller that owns four
    /// objects while the dialogue authored two drives the first two, and the other two are
    /// deactivated here and never touched again. Bound objects that no node ever enables are
    /// deactivated too. The binding is runtime only - the asset's own list and node grid are
    /// left exactly as authored.
    /// </summary>
    public void AssignObjects(List<TalkableWithModels> runtimeObjects)
    {
        if (objects == null) objects = new List<TalkableWithModels>();
        SyncNodeSettings();

        unboundObjects = null;
        prevEnabled = null;

        if (runtimeObjects == null)
        {
            boundObjects = null;
            return;
        }

        // How many slots this dialogue actually authored. An asset that declares none is
        // un-authored: fall back to the controller's list (its grid is empty, so nothing is
        // enabled anyway).
        int slotCount = objects.Count > 0 ? objects.Count : runtimeObjects.Count;

        boundObjects = new List<TalkableWithModels>(slotCount);
        for (int i = 0; i < slotCount; i++)
            boundObjects.Add(i < runtimeObjects.Count ? runtimeObjects[i] : null);

        // Everything the caller owns past this dialogue's slots.
        if (runtimeObjects.Count > slotCount)
        {
            unboundObjects = new List<TalkableWithModels>();
            for (int i = slotCount; i < runtimeObjects.Count; i++)
                if (runtimeObjects[i]) unboundObjects.Add(runtimeObjects[i]);
        }

        prevEnabled = new bool[boundObjects.Count];

        DeactivateUnusedObjects();
    }

    /// <summary>
    /// Takes every object this dialogue will never show fully off screen: the objects the caller
    /// owns beyond this dialogue's slot count, plus bound objects that no node in the grid ever
    /// enables. Both are deactivated outright (models off, transform restored, GameObject
    /// inactive) - they never appear, so there is no end cutscene to play for them.
    ///
    /// The dialogue owns this rather than the caller: a dialogue started without a
    /// TalkableWModelsController (straight through DialogueManager) still hides what it doesn't use.
    /// </summary>
    public void DeactivateUnusedObjects()
    {
        if (unboundObjects != null)
            for (int i = 0; i < unboundObjects.Count; i++)
                if (unboundObjects[i]) unboundObjects[i].ForceDeactivate();

        List<TalkableWithModels> bound = Bound;
        if (bound == null) return;

        for (int i = 0; i < bound.Count; i++)
        {
            if (!bound[i]) continue;
            if (IsObjectUsed(i)) continue;
            bound[i].ForceDeactivate();
        }
    }

    /// <summary>
    /// True when any node in the grid switches the object at <paramref name="slot"/> on. A slot
    /// no row enables is unused by this dialogue - even if the object is wired up in the scene.
    /// </summary>
    public bool IsObjectUsed(int slot)
    {
        if (nodeSettings == null) return false;

        for (int n = 0; n < nodeSettings.Count; n++)
        {
            MultiModelNodeSettings row = nodeSettings[n];
            if (row?.objectSettings == null) continue;
            if (slot >= row.objectSettings.Count) continue;

            ModelObjectNodeSettings s = row.objectSettings[slot];
            if (s != null && s.enableObject) return true;
        }

        return false;
    }

    /// <summary>
    /// Stages node 0 on every object, then plays each active object's intro animation and
    /// beforeDialogueStart events, waiting for them all. Call this before StartDialogue so
    /// the intro plays before the window opens (mirrors Talkable's before-dialogue step).
    /// </summary>
    public IEnumerator RunBeforeDialogueStart(MonoBehaviour runner)
    {
        // Stage the first node so intro animations have the right model to play on.
        StageNode(0);

        List<TalkableWithModels> bound = Bound;
        if (bound == null) yield break;

        List<Coroutine> running = new List<Coroutine>();
        for (int i = 0; i < bound.Count; i++)
        {
            TalkableWithModels obj = bound[i];
            if (obj && obj.isActiveAndEnabled)
                running.Add(runner.StartCoroutine(obj.PlayBeforeDialogueStart()));
        }
        for (int i = 0; i < running.Count; i++) yield return running[i];
    }

    /// <summary>Runs each active object's on-dialogue-start lifecycle (animation + events).</summary>
    public void OnDialogueStart()
    {
        List<TalkableWithModels> bound = Bound;
        if (bound == null) return;
        for (int i = 0; i < bound.Count; i++)
        {
            TalkableWithModels obj = bound[i];
            if (obj && obj.isActiveAndEnabled) obj.OnDialogueStart();
        }
    }

    /// <summary>
    /// Applies a node's enable/model/eyes to every object WITHOUT playing per-node anims.
    ///
    /// Every bound object is visited, not just the ones the row has settings for: an object with
    /// no entry (or no row at all) counts as disabled and is taken fully off screen, so a
    /// half-authored grid can never leave a leftover object standing there.
    /// </summary>
    private void StageNode(int nodeIdx)
    {
        List<TalkableWithModels> bound = Bound;
        if (bound == null) return;

        MultiModelNodeSettings row =
            nodeIdx >= 0 && nodeSettings != null && nodeIdx < nodeSettings.Count
                ? nodeSettings[nodeIdx]
                : null;

        EnsurePrevEnabled(bound.Count);

        for (int i = 0; i < bound.Count; i++)
        {
            ModelObjectNodeSettings s = SettingsFor(row, i);
            bool enable = s != null && s.enableObject;

            if (prevEnabled != null && i < prevEnabled.Length) prevEnabled[i] = enable;

            TalkableWithModels obj = bound[i];
            if (!obj) continue;

            obj.SetupForNode(enable, s != null ? s.modelId : 0, s != null && s.enableEyes);
        }
    }

    /// <summary>Per-object settings for a slot on a row, or null when the row doesn't cover it.</summary>
    private static ModelObjectNodeSettings SettingsFor(MultiModelNodeSettings row, int slot)
    {
        if (row?.objectSettings == null) return null;
        if (slot < 0 || slot >= row.objectSettings.Count) return null;
        return row.objectSettings[slot];
    }

    /// <summary>Makes sure the previous-state array exists and matches the bound object count.</summary>
    private void EnsurePrevEnabled(int count)
    {
        if (prevEnabled == null || prevEnabled.Length != count) prevEnabled = new bool[count];
    }

    /// <summary>
    /// Applies a node's row to every object, in two phases.
    ///
    /// Phase 1 - transitions. Objects whose enabled state differs from the previous node play a
    /// cutscene: newly enabled objects are staged (model + eyes) and play their appearing
    /// cutscene; newly disabled objects play their end cutscene and then deactivate. Objects
    /// enabled on the first node are handled earlier by RunBeforeDialogueStart (their cutscene
    /// runs before the window opens); objects still enabled on the last node run their end
    /// cutscene when the window closes (OnDialogueEnd) - so node 0 skips the enable case and the
    /// last node's still-enabled objects are left untouched until dialogue end.
    ///
    /// This routine WAITS for every appearing cutscene to finish.
    ///
    /// Phase 2 - the node itself. Only once everything has appeared do the objects start the
    /// node (talking animation + per-node animation). The DialogueManager yields on this routine,
    /// so the node's text does not start typing until the appearing animations are done.
    /// </summary>
    public IEnumerator OnNodeStart(DialogueNode node, MonoBehaviour runner)
    {
        if (node == null) yield break;

        int idx = node.nodeId;
        if (nodeSettings == null || idx < 0 || idx >= nodeSettings.Count) yield break;

        MultiModelNodeSettings row = nodeSettings[idx];
        if (row == null) yield break;

        // Resolve who is speaking this node and fill the window's name/portrait/voice from
        // them. Nothing calls Talkable.InitDialogue on a multi-object dialogue, so without
        // this the node keeps its empty speaker fields - most visibly, no voice clips, which
        // makes the typewriter play silently.
        TalkableWithModels speaker = ResolveSpeaker(row);
        if (speaker) node.InitFromGameObject(speaker.gameObject);

        List<TalkableWithModels> bound = Bound;
        if (bound == null) yield break;

        EnsurePrevEnabled(bound.Count);

        // ---- Phase 1: enable/disable transitions ----
        // Every bound object is visited: one with no entry on this row counts as disabled, so an
        // object the grid stops mentioning still leaves the screen instead of lingering.
        List<Coroutine> appearing = new List<Coroutine>();
        for (int i = 0; i < bound.Count; i++)
        {
            ModelObjectNodeSettings s = SettingsFor(row, i);
            bool willEnable = s != null && s.enableObject;

            // What the previous node wanted, NOT gameObject.activeSelf: an object whose end
            // cutscene is still playing is already "off" for the dialogue even though its
            // GameObject is still active for another few frames.
            bool wasEnabled = i < prevEnabled.Length && prevEnabled[i];
            if (i < prevEnabled.Length) prevEnabled[i] = willEnable;

            TalkableWithModels obj = bound[i];
            if (!obj) continue;

            if (willEnable && !wasEnabled && idx != 0)
            {
                // Enabled mid-dialogue: appear first, the node starts after (phase 2).
                appearing.Add(runner.StartCoroutine(
                    obj.EnterDuringDialogueRoutine(s.modelId, s.enableEyes)));
            }
            else if (!willEnable && wasEnabled)
            {
                // Disabled mid-dialogue: end cutscene runs alongside this node, then the
                // object deactivates. (Objects still enabled on the last node run their end
                // cutscene when the window closes, in OnDialogueEnd.)
                obj.DisableDuringDialogue();
            }
            else if (!willEnable && obj.gameObject.activeSelf && !obj.IsPlayingEndCutscene)
            {
                // Off on both nodes but somehow still on screen (re-entered dialogue, a killed
                // coroutine, an object switched on by hand): no cutscene is owed, just hide it.
                obj.ForceDeactivate();
            }
        }

        // Wait for every appearing animation before the node actually starts.
        for (int i = 0; i < appearing.Count; i++) yield return appearing[i];

        // ---- Phase 2: start the node on every object that stays on screen ----
        for (int i = 0; i < bound.Count; i++)
        {
            TalkableWithModels obj = bound[i];
            if (!obj) continue;

            ModelObjectNodeSettings s = SettingsFor(row, i);

            // Objects this node disables are mid-end-cutscene (or already gone) - leave them be,
            // deactivating them here would cut their end animation short.
            if (s == null || !s.enableObject) continue;

            obj.PlayDialogueNode(true, s.modelId, s.enableEyes, s.talk);
        }
    }

    /// <summary>
    /// Picks the object that speaks a node: the explicit speakerObjectIndex when set, otherwise
    /// the first object the node both enables and lets talk. Returns null only when the row
    /// stages no talking object at all (then the node keeps whatever speaker it already had).
    /// </summary>
    private TalkableWithModels ResolveSpeaker(MultiModelNodeSettings row)
    {
        List<TalkableWithModels> bound = Bound;
        if (bound == null) return null;

        if (row.speakerObjectIndex >= 0
            && row.speakerObjectIndex < bound.Count
            && bound[row.speakerObjectIndex])
            return bound[row.speakerObjectIndex];

        for (int i = 0; i < bound.Count; i++)
        {
            ModelObjectNodeSettings s = SettingsFor(row, i);
            if (s == null || !s.enableObject || !s.talk) continue;
            if (bound[i]) return bound[i];
        }

        return null;
    }

    /// <summary>Ends the node on every driven object (stops their per-node animations).</summary>
    public void OnNodeEnd(DialogueNode node)
    {
        List<TalkableWithModels> bound = Bound;
        if (bound == null) return;
        for (int i = 0; i < bound.Count; i++)
            if (bound[i]) bound[i].EndDialogueNode();
    }

    /// <summary>
    /// Runs each object's dialogue-end lifecycle. Objects the last node left on screen play
    /// their end animation and then deactivate; objects already on their way out are left to
    /// finish; everything else is deactivated outright. Either way every object this dialogue
    /// touched ends up inactive.
    /// </summary>
    public void OnDialogueEnd()
    {
        List<TalkableWithModels> bound = Bound;
        if (bound == null) return;
        for (int i = 0; i < bound.Count; i++)
        {
            TalkableWithModels obj = bound[i];
            if (!obj) continue;

            // Already leaving (disabled by a node, end animation still running) - it deactivates
            // itself when the animation finishes, so don't cut it short.
            if (obj.IsPlayingEndCutscene) continue;

            // Only objects the dialogue actually left on screen have an end cutscene owed to
            // them. Anything else (never used, or already gone) is reset outright.
            bool onScreen = obj.isActiveAndEnabled
                            && i < (prevEnabled?.Length ?? 0)
                            && prevEnabled[i];

            if (onScreen) obj.PlayOnDialogueEnd(true);
            else obj.ForceDeactivate();
        }

        // Objects past this dialogue's slots were deactivated at bind time; make sure nothing
        // switched them back on in the meantime.
        if (unboundObjects != null)
            for (int i = 0; i < unboundObjects.Count; i++)
                if (unboundObjects[i]) unboundObjects[i].ForceDeactivate();

        // Drop the runtime binding so a later dialogue can't inherit it.
        boundObjects = null;
        unboundObjects = null;
        prevEnabled = null;
    }
}
