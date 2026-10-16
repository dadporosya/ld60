using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-object settings for a single dialogue node. There is one of these for every
/// object in DialogueContainerWithArmature.objects, on every node.
/// </summary>
[Serializable]
public class ArmatureObjectNodeSettings
{
    [Tooltip("Enable or disable this object when the node starts. Off by default: an object " +
             "is only shown on the nodes that explicitly switch it on.")]
    public bool enableObject = false;

    [Tooltip("Clip played when the node starts (index into the object's animations list). " +
             "-1 = no clip, the object keeps idling.")]
    public int animationId = -1;

    [Tooltip("Loop the clip until the next node starts. When off it plays once, then a random " +
             "defaultIdle clip of the object loops.")]
    public bool loop = false;

    [Tooltip("Does this object talk on this node? When true the object plays its talking SFX " +
             "and onNodeStartAnimation. When false it only plays the node's clip.")]
    public bool talk = true;
}

/// <summary>
/// Settings for one dialogue node across every object in the container.
/// objectSettings is kept parallel to DialogueContainerWithArmature.objects
/// (one entry per object).
/// </summary>
[Serializable]
public class MultiArmatureNodeSettings
{
    [Tooltip("Optional: index of the object (in the container's objects list) that speaks " +
             "this node - its name/portrait/voice fill the dialogue window. -1 = leave the " +
             "node's own speaker fields untouched.")]
    public int speakerObjectIndex = -1;

    [Tooltip("One entry per object in the container. Order matches the objects list.")]
    public List<ArmatureObjectNodeSettings> objectSettings = new List<ArmatureObjectNodeSettings>();
}

/// <summary>
/// A DialogueContainer override that steers several TalkableWithArmature objects at once.
///
/// The objects list defines how many objects this dialogue controls (leave the entries
/// null in the asset - a TalkableWithArmatureController assigns the real scene objects at
/// runtime via AssignObjects).
///
/// nodeSettings is a grid kept parallel to the base nodes list: one MultiArmatureNodeSettings
/// per node, each holding one ArmatureObjectNodeSettings per object. When the DialogueManager
/// starts a node it calls OnNodeStart, which applies that node's row to every object.
/// Dialogue-level lifecycle (RunBeforeDialogueStart / OnDialogueStart / OnDialogueEnd) is
/// forwarded to each object so their intro/start/end animations, clips and events fire,
/// mirroring the single-object Talkable flow.
/// </summary>
[CreateAssetMenu(fileName = "DialogueContainerWithArmature", menuName = "Dialogues/DialogueContainerWithArmature")]
public class DialogueContainerWithArmature : DialogueContainer, IMultiObjectDialogue
{
    [Header("Multi-object dialogue")]
    [Tooltip("Objects this dialogue controls. Leave null in the asset to just define the " +
             "count; a TalkableWithArmatureController assigns the real scene objects at runtime.")]
    public List<TalkableWithArmature> objects = new List<TalkableWithArmature>();

    [Tooltip("Per-node, per-object settings. Kept in sync with 'nodes' x 'objects' " +
             "automatically. Index i matches nodes[i]; each row has one entry per object.")]
    public List<MultiArmatureNodeSettings> nodeSettings = new List<MultiArmatureNodeSettings>();

    /// <summary>
    /// The scene objects bound for the current playthrough. Runtime-only: never serialized into
    /// the asset, so playing a dialogue can't grow (and permanently dirty) the authored slot list.
    /// Exactly as long as the authored 'objects' list - a controller with more objects than this
    /// dialogue declares simply doesn't drive the extras, so they stay disabled.
    /// </summary>
    [NonSerialized] private List<TalkableWithArmature> boundObjects;

    /// <summary>
    /// Objects the caller handed over that this dialogue has no slot for. They are never driven,
    /// so the dialogue force-deactivates them once at bind time and never touches them again.
    /// </summary>
    [NonSerialized] private List<TalkableWithArmature> unboundObjects;

    /// <summary>
    /// What the previous node wanted each bound object's enabled state to be. Used instead of
    /// reading GameObject.activeSelf, which lies during a disable transition: an object whose
    /// end cutscene is still playing is off as far as the dialogue is concerned but its
    /// GameObject is still active until the animation finishes.
    /// </summary>
    [NonSerialized] private bool[] prevEnabled;

    /// <summary>Objects actually driven at runtime, falling back to the authored slots in the editor.</summary>
    private List<TalkableWithArmature> Bound => boundObjects ?? objects;

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
        if (nodeSettings == null) nodeSettings = new List<MultiArmatureNodeSettings>();

        int nodeCount = nodes != null ? nodes.Count : 0;
        int objectCount = objects != null ? objects.Count : 0;

        while (nodeSettings.Count < nodeCount) nodeSettings.Add(new MultiArmatureNodeSettings());
        while (nodeSettings.Count > nodeCount) nodeSettings.RemoveAt(nodeSettings.Count - 1);

        for (int i = 0; i < nodeSettings.Count; i++)
        {
            MultiArmatureNodeSettings row = nodeSettings[i];
            if (row == null) { row = new MultiArmatureNodeSettings(); nodeSettings[i] = row; }
            if (row.objectSettings == null) row.objectSettings = new List<ArmatureObjectNodeSettings>();

            while (row.objectSettings.Count < objectCount) row.objectSettings.Add(new ArmatureObjectNodeSettings());
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
    public void AssignObjects(List<TalkableWithArmature> runtimeObjects)
    {
        if (objects == null) objects = new List<TalkableWithArmature>();
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

        boundObjects = new List<TalkableWithArmature>(slotCount);
        for (int i = 0; i < slotCount; i++)
            boundObjects.Add(i < runtimeObjects.Count ? runtimeObjects[i] : null);

        // Everything the caller owns past this dialogue's slots.
        if (runtimeObjects.Count > slotCount)
        {
            unboundObjects = new List<TalkableWithArmature>();
            for (int i = slotCount; i < runtimeObjects.Count; i++)
                if (runtimeObjects[i]) unboundObjects.Add(runtimeObjects[i]);
        }

        prevEnabled = new bool[boundObjects.Count];

        DeactivateUnusedObjects();
    }

    /// <summary>
    /// Takes every object this dialogue will never show fully off screen: the objects the caller
    /// owns beyond this dialogue's slot count, plus bound objects that no node in the grid ever
    /// enables. Both are deactivated outright - they never appear, so there is no end cutscene
    /// to play for them.
    /// </summary>
    public void DeactivateUnusedObjects()
    {
        if (unboundObjects != null)
            for (int i = 0; i < unboundObjects.Count; i++)
                if (unboundObjects[i]) unboundObjects[i].ForceDeactivate();

        List<TalkableWithArmature> bound = Bound;
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
            MultiArmatureNodeSettings row = nodeSettings[n];
            if (row?.objectSettings == null) continue;
            if (slot >= row.objectSettings.Count) continue;

            ArmatureObjectNodeSettings s = row.objectSettings[slot];
            if (s != null && s.enableObject) return true;
        }

        return false;
    }

    /// <summary>
    /// Enables the objects node 0 uses, then plays each active object's intro animation/clip and
    /// beforeDialogueStart events, waiting for them all. Call this before StartDialogue so
    /// the intro plays before the window opens (mirrors Talkable's before-dialogue step).
    /// </summary>
    public IEnumerator RunBeforeDialogueStart(MonoBehaviour runner)
    {
        StageNode(0);

        List<TalkableWithArmature> bound = Bound;
        if (bound == null) yield break;

        List<Coroutine> running = new List<Coroutine>();
        for (int i = 0; i < bound.Count; i++)
        {
            TalkableWithArmature obj = bound[i];
            if (obj && obj.isActiveAndEnabled)
                running.Add(runner.StartCoroutine(obj.PlayBeforeDialogueStart()));
        }
        for (int i = 0; i < running.Count; i++) yield return running[i];
    }

    /// <summary>Runs each active object's on-dialogue-start lifecycle (animation + events).</summary>
    public void OnDialogueStart()
    {
        List<TalkableWithArmature> bound = Bound;
        if (bound == null) return;
        for (int i = 0; i < bound.Count; i++)
        {
            TalkableWithArmature obj = bound[i];
            if (obj && obj.isActiveAndEnabled) obj.OnDialogueStart();
        }
    }

    /// <summary>
    /// Applies a node's enable state to every object WITHOUT starting node clips.
    ///
    /// Every bound object is visited, not just the ones the row has settings for: an object with
    /// no entry (or no row at all) counts as disabled and is taken fully off screen, so a
    /// half-authored grid can never leave a leftover object standing there.
    /// </summary>
    private void StageNode(int nodeIdx)
    {
        List<TalkableWithArmature> bound = Bound;
        if (bound == null) return;

        MultiArmatureNodeSettings row =
            nodeIdx >= 0 && nodeSettings != null && nodeIdx < nodeSettings.Count
                ? nodeSettings[nodeIdx]
                : null;

        EnsurePrevEnabled(bound.Count);

        for (int i = 0; i < bound.Count; i++)
        {
            ArmatureObjectNodeSettings s = SettingsFor(row, i);
            bool enable = s != null && s.enableObject;

            if (prevEnabled != null && i < prevEnabled.Length) prevEnabled[i] = enable;

            TalkableWithArmature obj = bound[i];
            if (!obj) continue;

            obj.SetupForNode(enable);
        }
    }

    /// <summary>Per-object settings for a slot on a row, or null when the row doesn't cover it.</summary>
    private static ArmatureObjectNodeSettings SettingsFor(MultiArmatureNodeSettings row, int slot)
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
    /// cutscene: newly enabled objects play their appearing cutscene; newly disabled objects play
    /// their end cutscene and then deactivate. Objects enabled on the first node are handled
    /// earlier by RunBeforeDialogueStart; objects still enabled on the last node run their end
    /// cutscene when the window closes (OnDialogueEnd).
    ///
    /// This routine WAITS for every appearing cutscene to finish.
    ///
    /// Phase 2 - the node itself. Only once everything has appeared do the objects start the
    /// node (node clip, talking SFX, per-node animation). The DialogueManager yields on this
    /// routine, so the node's text does not start typing until the appearing animations are done.
    /// </summary>
    public IEnumerator OnNodeStart(DialogueNode node, MonoBehaviour runner)
    {
        if (node == null) yield break;

        int idx = node.nodeId;
        if (nodeSettings == null || idx < 0 || idx >= nodeSettings.Count) yield break;

        MultiArmatureNodeSettings row = nodeSettings[idx];
        if (row == null) yield break;

        // Resolve who is speaking this node and fill the window's name/portrait/voice from
        // them. Nothing calls Talkable.InitDialogue on a multi-object dialogue, so without
        // this the node keeps its empty speaker fields - most visibly, no voice clips.
        TalkableWithArmature speaker = ResolveSpeaker(row);
        if (speaker) node.InitFromGameObject(speaker.gameObject);

        List<TalkableWithArmature> bound = Bound;
        if (bound == null) yield break;

        EnsurePrevEnabled(bound.Count);

        // ---- Phase 1: enable/disable transitions ----
        List<Coroutine> appearing = new List<Coroutine>();
        for (int i = 0; i < bound.Count; i++)
        {
            ArmatureObjectNodeSettings s = SettingsFor(row, i);
            bool willEnable = s != null && s.enableObject;

            // What the previous node wanted, NOT gameObject.activeSelf: an object whose end
            // cutscene is still playing is already "off" for the dialogue even though its
            // GameObject is still active for another few frames.
            bool wasEnabled = i < prevEnabled.Length && prevEnabled[i];
            if (i < prevEnabled.Length) prevEnabled[i] = willEnable;

            TalkableWithArmature obj = bound[i];
            if (!obj) continue;

            if (willEnable && !wasEnabled && idx != 0)
            {
                // Enabled mid-dialogue: appear first, the node starts after (phase 2).
                appearing.Add(runner.StartCoroutine(obj.EnterDuringDialogueRoutine()));
            }
            else if (!willEnable && wasEnabled)
            {
                // Disabled mid-dialogue: end cutscene runs alongside this node, then the
                // object deactivates.
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
            TalkableWithArmature obj = bound[i];
            if (!obj) continue;

            ArmatureObjectNodeSettings s = SettingsFor(row, i);

            // Objects this node disables are mid-end-cutscene (or already gone) - leave them be,
            // deactivating them here would cut their end animation short.
            if (s == null || !s.enableObject) continue;

            obj.PlayDialogueNode(true, s.animationId, s.loop, s.talk);
        }
    }

    /// <summary>
    /// Picks the object that speaks a node: the explicit speakerObjectIndex when set, otherwise
    /// the first object the node both enables and lets talk. Returns null only when the row
    /// stages no talking object at all (then the node keeps whatever speaker it already had).
    /// </summary>
    private TalkableWithArmature ResolveSpeaker(MultiArmatureNodeSettings row)
    {
        List<TalkableWithArmature> bound = Bound;
        if (bound == null) return null;

        if (row.speakerObjectIndex >= 0
            && row.speakerObjectIndex < bound.Count
            && bound[row.speakerObjectIndex])
            return bound[row.speakerObjectIndex];

        for (int i = 0; i < bound.Count; i++)
        {
            ArmatureObjectNodeSettings s = SettingsFor(row, i);
            if (s == null || !s.enableObject || !s.talk) continue;
            if (bound[i]) return bound[i];
        }

        return null;
    }

    /// <summary>
    /// Nothing to stop: a node's clip keeps playing (or has already handed over to idle) until
    /// the next node starts.
    /// </summary>
    public void OnNodeEnd(DialogueNode node) { }

    /// <summary>
    /// Runs each object's dialogue-end lifecycle. Objects the last node left on screen play
    /// their end animation/clip and then deactivate; objects already on their way out are left
    /// to finish; everything else is deactivated outright. Either way every object this dialogue
    /// touched ends up inactive.
    /// </summary>
    public void OnDialogueEnd()
    {
        List<TalkableWithArmature> bound = Bound;
        if (bound == null) return;
        for (int i = 0; i < bound.Count; i++)
        {
            TalkableWithArmature obj = bound[i];
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
