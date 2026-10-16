using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A list of AudioClips — one "frame" of <see cref="RandomContainerAudioAnimation"/>.
/// Wrapped in a class because Unity cannot serialize nested lists directly.
/// </summary>
[System.Serializable]
public class AudioClipContainer
{
    public List<AudioClip> clips = new List<AudioClip>();
}

/// <summary>
/// Plays one random clip from audioClipContainers[i] on the i-th frame, using the same
/// gap timing and SFX settings as <see cref="AudioAnimation"/>.
/// </summary>
public class RandomContainerAudioAnimation : AudioAnimation
{
    [Header("RandomContainerAudioAnimation Settings")]
    [Tooltip("One container per frame; a random clip from container i is played on frame i.")]
    public List<AudioClipContainer> audioClipContainers = new List<AudioClipContainer>();

    protected override int FrameCount => audioClipContainers.Count;

    protected override AudioClip GetClipForFrame(int i)
    {
        if (i < 0 || i >= audioClipContainers.Count) return null;

        AudioClipContainer container = audioClipContainers[i];
        if (container == null || container.clips == null || container.clips.Count == 0) return null;

        return h.RandChoice(container.clips);
    }
}
