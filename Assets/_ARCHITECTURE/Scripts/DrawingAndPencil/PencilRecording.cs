using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A recorded pencil performance: the pointer's position and button over time, as captured by
/// <see cref="PencilRecorder"/> and replayed by <see cref="PencilPlayback"/>.
///
/// <para>Positions are normalized over the board they were recorded on — (0,0) = its bottom-left corner,
/// (1,1) = its top-right, values outside 0..1 are off the board — so a replay lands on the same spot of the
/// board whatever the screen size. The raw pointer is stored, not the pencil tip: the replay goes through the
/// <see cref="Pencil"/> again, which adds the same tip lag the recording had.</para>
/// </summary>
[CreateAssetMenu(fileName = "PencilRecording", menuName = "Scriptable Objects/Pencil Recording")]
public class PencilRecording : ScriptableObject
{
    [Serializable]
    public struct Sample
    {
        [Tooltip("Seconds since the recording started.")]
        public float time;
        [Tooltip("Pointer position normalized over the board: (0,0) bottom-left, (1,1) top-right.")]
        public Vector2 position;
        public bool pressed;

        public Sample(float time, Vector2 position, bool pressed)
        {
            this.time = time;
            this.position = position;
            this.pressed = pressed;
        }
    }

    [Tooltip("In time order. Filled by the PencilRecorder.")]
    public List<Sample> samples = new List<Sample>();

    /// <summary>Seconds from the first sample to the last.</summary>
    public float Duration => samples.Count > 0 ? samples[samples.Count - 1].time : 0f;

    public bool IsEmpty => samples.Count == 0;

    /// <summary>
    /// The pointer at <paramref name="time"/>: the position interpolated between the samples around it, the
    /// button as of the sample at or before it. <paramref name="cursor"/> is the index of that sample; start it
    /// at 0 and pass it back every call so a playback moving forward doesn't search from the start each frame.
    /// </summary>
    public void Evaluate(float time, ref int cursor, out Vector2 position, out bool pressed)
    {
        position = default;
        pressed = false;
        if (samples.Count == 0) return;

        cursor = Mathf.Clamp(cursor, 0, samples.Count - 1);
        while (cursor > 0 && samples[cursor].time > time) cursor--;
        while (cursor < samples.Count - 1 && samples[cursor + 1].time <= time) cursor++;

        Sample a = samples[cursor];
        pressed = a.pressed;
        if (cursor == samples.Count - 1 || time <= a.time)
        {
            position = a.position;
            return;
        }

        Sample b = samples[cursor + 1];
        float span = b.time - a.time;
        position = span > 0f ? Vector2.Lerp(a.position, b.position, (time - a.time) / span) : b.position;
    }

    /// <summary>
    /// Normalized board position of <paramref name="screen"/> (see the class summary); false when the screen
    /// point doesn't map onto the board's plane at all.
    /// </summary>
    public static bool ScreenToNormalized(PixelGrid board, Vector2 screen, out Vector2 normalized)
    {
        normalized = default;
        if (!board || !board.ScreenToLocal(screen, out Vector2 local)) return false;

        Rect r = board.Rect.rect;
        if (r.width <= 0f || r.height <= 0f) return false;

        normalized = new Vector2((local.x - r.xMin) / r.width, (local.y - r.yMin) / r.height);
        return true;
    }

    /// <summary>The screen point at <paramref name="normalized"/> on <paramref name="board"/> — the inverse of <see cref="ScreenToNormalized"/>.</summary>
    public static Vector2 NormalizedToScreen(PixelGrid board, Vector2 normalized)
    {
        Rect r = board.Rect.rect;
        return board.LocalToScreen(new Vector2(r.xMin + normalized.x * r.width, r.yMin + normalized.y * r.height));
    }
}
