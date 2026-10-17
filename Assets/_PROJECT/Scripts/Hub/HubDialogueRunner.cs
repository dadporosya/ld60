using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Plays visual novel dialogues on a <see cref="VisualNovelTalkable"/> in the SAME scene as the hub,
/// the dice table and the phone - unlike <see cref="VisualNovelController"/>, which opens the visual
/// novel scene. The stage is hidden again before the callback runs, so the hub is back on screen when
/// the game carries on. Access via <c>HubDialogueRunner.Instance</c>.
///
/// <para>A dialogue can be cut off with <see cref="Interrupt"/> (the song ran out): its callback is
/// then dropped, so the flow it belonged to never continues.</para>
/// </summary>
public class HubDialogueRunner : MonoBehaviour
{
    public static HubDialogueRunner Instance;

    [Tooltip("The visual novel stage in this scene. Defaults to the first one found.")]
    [SerializeField] private VisualNovelTalkable talkable;

    // Bumped on every Play / Interrupt: a callback only runs if its dialogue is still the current one.
    private int playId;
    private Coroutine interruptRoutine;

    /// <summary>True while a dialogue is playing (or waiting for an interrupted one to clear).</summary>
    public bool IsPlaying => interruptRoutine != null || (Talkable && Talkable.IsPlaying);

    private VisualNovelTalkable Talkable
    {
        get
        {
            if (!talkable) talkable = FindFirstObjectByType<VisualNovelTalkable>(FindObjectsInactive.Include);
            return talkable;
        }
    }

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance, setDontDestroy: false);
    }

    /// <summary>
    /// Plays <paramref name="dialogue"/>, then <paramref name="onFinished"/>. A null dialogue just runs
    /// the callback, so optional dialogues (a contact without a greeting) need no special case.
    /// </summary>
    public void Play(VisualNovelDialogueContainer dialogue, Action onFinished = null)
    {
        if (!dialogue)
        {
            onFinished?.Invoke();
            return;
        }

        if (!Talkable)
        {
            h.Out($"HubDialogueRunner: no VisualNovelTalkable in scene, skipping '{dialogue.name}'.");
            onFinished?.Invoke();
            return;
        }

        if (IsPlaying)
        {
            h.Out($"HubDialogueRunner: a dialogue is already playing, ignoring '{dialogue.name}'.");
            return;
        }

        int id = ++playId;
        Talkable.PlayDialogue(dialogue, () =>
        {
            if (id == playId) onFinished?.Invoke();
        }, hideStageOnFinish: true);
    }

    /// <summary>Cuts the current dialogue off. Its callback never runs.</summary>
    public void Interrupt()
    {
        playId++;
        DialogueManager manager = DialogueManager.Instance;
        if (manager && manager.DialogueActive) manager.EndDialogue(animate: false);
    }

    /// <summary>
    /// Cuts off whatever is playing and plays <paramref name="dialogue"/> once the stage is free - for
    /// an ending that must start no matter what the player was in the middle of.
    /// </summary>
    public void InterruptAndPlay(VisualNovelDialogueContainer dialogue, Action onFinished = null)
    {
        Interrupt();
        if (interruptRoutine != null) StopCoroutine(interruptRoutine);
        interruptRoutine = StartCoroutine(InterruptRoutine(dialogue, onFinished));
    }

    private IEnumerator InterruptRoutine(VisualNovelDialogueContainer dialogue, Action onFinished)
    {
        // The talkable may still be staging its first node (the manager has no dialogue yet), so keep
        // ending until it lets go.
        while (Talkable && Talkable.IsPlaying)
        {
            DialogueManager manager = DialogueManager.Instance;
            if (manager && manager.DialogueActive) manager.EndDialogue(animate: false);
            yield return null;
        }

        interruptRoutine = null;
        Play(dialogue, onFinished);
    }
}
