using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The gramophone: clicking it makes the hero say how much of the song is left. The line is picked
/// by the song's progress (<see cref="SongTimer.Progress"/>): the first entry whose
/// <see cref="ProgressLine.untilProgress"/> is above the current progress.
/// </summary>
public class GramophoneInteractable : HubInteractable
{
    [Serializable]
    public class ProgressLine
    {
        [Tooltip("Used while the song's progress (0 = start, 1 = end) is below this.")]
        [Range(0f, 1f)] public float untilProgress = 0.25f;
        public VisualNovelDialogueContainer dialogue;
    }

    [Header("Gramophone")]
    [Tooltip("Defaults to SongTimer.Instance.")]
    [SerializeField] private SongTimer song;

    [Tooltip("Sorted by Until Progress at runtime.")]
    [SerializeField] private List<ProgressLine> lines = new List<ProgressLine>
    {
        new ProgressLine { untilProgress = 0.25f }, // "It's just the first quarter of the song."
        new ProgressLine { untilProgress = 0.5f },  // "The song is approaching its midpoint."
        new ProgressLine { untilProgress = 0.75f }, // "The half has already been played."
        new ProgressLine { untilProgress = 1f },    // "I love this part. Only a quarter left."
    };

    private SongTimer Song => song ? song : song = SongTimer.Instance;

    protected override void Awake()
    {
        base.Awake();
        lines.Sort((a, b) => a.untilProgress.CompareTo(b.untilProgress));
    }

    protected override bool IsAvailable() => lines.Count > 0;

    protected override void Interact()
    {
        float progress = Song ? Song.Progress : 0f;
        VisualNovelDialogueContainer dialogue = LineFor(progress);

        if (FuneralGameManager.Instance) FuneralGameManager.Instance.PlayHubDialogue(dialogue);
        else if (HubDialogueRunner.Instance) HubDialogueRunner.Instance.Play(dialogue);
    }

    private VisualNovelDialogueContainer LineFor(float progress)
    {
        foreach (ProgressLine line in lines)
            if (progress < line.untilProgress) return line.dialogue;

        return lines[lines.Count - 1].dialogue;
    }
}
