using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The vinyl: shown up close like any showcase object, with a dialogue picked by how much of the song has played
/// (<see cref="SongTimer.Progress"/>): the entry with the highest <see cref="ProgressDialogue.fromProgress"/> the
/// song has already reached.
/// </summary>
public class VinylInteractable : ShowcaseInteractableBase
{
    [Serializable]
    public class ProgressDialogue
    {
        [Tooltip("Used once the song's progress (0 = start, 1 = end) has reached this.")]
        [Range(0f, 1f)] public float fromProgress;
        public DialogueContainer dialogue;
    }

    [Header("Vinyl")]
    [Tooltip("Defaults to SongTimer.Instance.")]
    [SerializeField] private SongTimer song;
    [SerializeField] private List<ProgressDialogue> dialogues = new List<ProgressDialogue>
    {
        new ProgressDialogue { fromProgress = 0f },    // less than a quarter played
        new ProgressDialogue { fromProgress = 0.25f },
        new ProgressDialogue { fromProgress = 0.5f },
        new ProgressDialogue { fromProgress = 0.75f },
    };

    private SongTimer Song => song ? song : song = SongTimer.Instance;

    protected override DialogueContainer CurrentDialogue
    {
        get
        {
            float progress = Song ? Song.Progress : 0f;
            ProgressDialogue best = null;
            foreach (ProgressDialogue entry in dialogues)
                if (entry.dialogue && entry.fromProgress <= progress && (best == null || entry.fromProgress >= best.fromProgress))
                    best = entry;

            return best?.dialogue;
        }
    }
}
