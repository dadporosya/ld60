using System.Collections;
using UnityEngine;

/// <summary>
/// Base for every visual-novel node effect (shake, blur, flash, ...). An effect is a
/// ScriptableObject preset: nodes reference the asset directly, and each variation (a light
/// rumble vs. a screamer jolt) is just another asset with different numbers.
///
/// Because one asset can be used by many nodes - and shared across scenes - an effect must not
/// keep per-run state in its own fields. It works on the stage the <see cref="VisualNovelTalkable"/>
/// passed in, and anything it has to remember between <see cref="Sustain"/> and
/// <see cref="Release"/> goes through <see cref="VisualNovelTalkable.SetEffectState"/>.
///
/// Lifecycle, all run as coroutines on the controller:
/// <list type="bullet">
/// <item><see cref="Play"/> - a node's start effect: runs once and finishes on its own.</item>
/// <item><see cref="Sustain"/> - a node's sustained effect: runs until the next node starts or
/// the dialogue ends, then the controller stops it.</item>
/// <item><see cref="Release"/> - called after a Play or Sustain was stopped from outside; brings
/// the stage back to rest.</item>
/// </list>
/// </summary>
public abstract class VisualNovelEffect : ScriptableObject
{
    /// <summary>
    /// True for effects that drive the <see cref="BlurVolumeManager"/>. A sustained one is held off
    /// (paused, when already running) while a FadeThroughBlur / CrossfadeBlur transition runs, since both would
    /// write the same blur radius.
    /// </summary>
    public virtual bool DrivesBlur => false;

    /// <summary>
    /// True for a start effect whose <see cref="Play"/> leaves the stage changed on purpose (a zoom
    /// that holds): the controller still calls <see cref="Release"/> after it has finished, when the
    /// next node starts or the dialogue ends. A node waiting for its completion only waits for Play.
    /// </summary>
    public virtual bool ReleaseAtNodeEnd => false;

    /// <summary>One-shot at the start of a node. Must finish on its own.</summary>
    public abstract IEnumerator Play(VisualNovelTalkable vn);

    /// <summary>
    /// Runs for the whole node and never finishes on its own: the controller stops it when the
    /// next node starts (unless that node sustains the same effect) and then calls
    /// <see cref="Release"/>. The default repeats <see cref="Play"/> back to back.
    /// </summary>
    public virtual IEnumerator Sustain(VisualNovelTalkable vn)
    {
        while (true)
        {
            yield return Play(vn);
            // An instant Play must not spin the loop forever within a single frame.
            yield return null;
        }
    }

    /// <summary>
    /// Brings the stage back to rest after <see cref="Play"/> or <see cref="Sustain"/> was stopped
    /// from outside. Must be safe to call when nothing is running.
    /// </summary>
    public virtual IEnumerator Release(VisualNovelTalkable vn)
    {
        yield break;
    }
}
