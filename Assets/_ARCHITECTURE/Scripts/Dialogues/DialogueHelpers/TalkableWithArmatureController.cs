using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives multi-object armature dialogues. Holds the concrete scene objects and a set of
/// DialogueContainerWithArmature assets. Works like Talkable: Talk starts a dialogue - but
/// first it binds this controller's objects into the chosen container so its per-node,
/// per-object settings drive the right scene objects, and it plays each object's
/// before-dialogue-start lifecycle before the window opens.
///
/// Objects are bound by index: container slot i is driven by objects[i].
/// </summary>
public class TalkableWithArmatureController : MonoBehaviour
{
    [SerializeField] private bool debug = true;
    [Tooltip("The scene objects this controller drives. Index i is bound to slot i of " +
             "each dialogue's objects list.")]
    public List<TalkableWithArmature> objects = new List<TalkableWithArmature>();

    [Tooltip("Multi-object dialogues this controller can play.")]
    public List<DialogueContainerWithArmature> dialogues = new List<DialogueContainerWithArmature>();

    [SerializeField] private DialogueManager dialogueManager;

    [Tooltip("Deactivate every driven object on start and before each dialogue, so only the " +
             "objects a dialogue actually uses are ever visible.")]
    [SerializeField] private bool disableObjectsOnStart = true;

    private void Awake()
    {
        if (!dialogueManager) dialogueManager = FindFirstObjectByType<DialogueManager>();
    }

    private void Start()
    {
        // Start() rather than Awake() so every object's own Awake has already captured its
        // resting transform before we hide it.
        if (disableObjectsOnStart) DeactivateObjects();
    }

    private void Update()
    {
        if (!debug) return;
        if (Input.GetKeyDown(KeyCode.T)) Talk();
    }

    /// <summary>
    /// Hides every object this controller drives. Dialogues switch back on only the objects
    /// their node grid enables, so a dialogue that uses fewer objects than the controller owns
    /// leaves the rest invisible.
    /// </summary>
    public void DeactivateObjects()
    {
        if (objects == null) return;
        for (int i = 0; i < objects.Count; i++)
            // ForceDeactivate rather than SetActive(false): it also cancels any end cutscene
            // still running on the object. Killing that coroutine by deactivating the GameObject
            // would strand its "end cutscene playing" guard and stop the object from ever
            // deactivating itself again.
            if (objects[i]) objects[i].ForceDeactivate();
    }

    /// <summary>
    /// Starts dialogue dialogueId (random when out of range), binding this controller's
    /// objects to it and running their before-dialogue-start lifecycle first.
    /// </summary>
    public void Talk(int dialogueId = -1)
    {
        if (dialogues == null || dialogues.Count == 0)
        {
            h.Out("TalkableWithArmatureController: no dialogues assigned.");
            return;
        }

        DialogueContainerWithArmature dialogue =
            dialogueId >= 0 && dialogueId < dialogues.Count
                ? dialogues[dialogueId]
                : h.RandChoice(dialogues);

        PlayDialogue(dialogue);
    }

    public void PlayDialogue(DialogueContainerWithArmature dialogue)
    {
        if (!dialogue)
        {
            h.Out($"TalkableWithArmatureController: dialogue is null on {name}.");
            return;
        }

        if (!dialogueManager) dialogueManager = FindFirstObjectByType<DialogueManager>();
        if (!dialogueManager)
        {
            h.Out("TalkableWithArmatureController: no DialogueManager in scene.");
            return;
        }
        h.Out($"Start talking {dialogue}");
        StartCoroutine(TalkRoutine(dialogue));
    }

    /// <summary>
    /// Binds the objects, plays each object's intro lifecycle to completion (mirrors
    /// Talkable), then hands the dialogue to the manager so the window opens afterwards.
    /// </summary>
    private IEnumerator TalkRoutine(DialogueContainerWithArmature dialogue)
    {
        // Clean slate: everything off, the dialogue enables only what its nodes ask for.
        DeactivateObjects();

        dialogue.AssignObjects(objects);

        yield return StartCoroutine(dialogue.RunBeforeDialogueStart(this));

        dialogueManager.StartDialogue(dialogue);
    }
}
