using System.Collections;
using UnityEngine;

/// <summary>
/// A single shake: the stage's background and/or foreground image (see <see cref="target"/>) is
/// knocked once in one direction and swings back to rest - a door slam, a punch, a heartbeat.
/// Meant as a node's start effect.
///
/// As a sustained effect it still shakes just once, when the node starts, and then holds still
/// until the next node.
///
/// The target moves by a few canvas units, so make the background images slightly larger than the
/// screen or its edges will peek in. Each layer's crossfade image moves with it.
/// </summary>
[CreateAssetMenu(fileName = "VN Single Shake", menuName = "Dialogues/Visual Novel/Effects/Single Shake")]
public class VisualNovelSingleShakeEffect : VisualNovelEffect
{
    [Header("Target")]
    [Tooltip("Which stage images shake: the background, the foreground, or both together.")]
    public VisualNovelLayers target = VisualNovelLayers.Background;

    [Header("Shake")]
    [Tooltip("How far the knock throws the target, in canvas units.")]
    public float distance = 20f;
    [Tooltip("Which way the knock goes (normalized). Zero = a random direction each time.")]
    public Vector2 direction = Vector2.down;
    [Tooltip("Degrees of random spread around the direction, either way.")]
    [Range(0f, 180f)] public float directionSpread = 0f;
    [Tooltip("Seconds from the knock until the target is back at rest.")]
    [Min(0.05f)] public float duration = 0.35f;
    [Tooltip("How many times it swings back and forth before settling. 0.5 = straight out and back, " +
             "1 = out, back past rest, and settle.")]
    [Min(0.5f)] public float swings = 1f;

    [Header("Release")]
    [Tooltip("Seconds to settle if the shake gets cut off halfway (the next node starts mid-shake).")]
    [Min(0f)] public float releaseTime = 0.1f;

    public override IEnumerator Play(VisualNovelTalkable vn)
    {
        VisualNovelBackground stage = vn ? vn.Background : null;
        if (!stage)
        {
            h.Out($"{name}: VisualNovelTalkable has no background to shake.");
            yield break;
        }

        stage.ResetOffset(target, this);
        Vector2 knock = PickDirection() * distance;

        // Marks the shake as running, so Release knows there is something to settle.
        vn.SetEffectState(this, true);

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            stage.SetOffset(target, knock * Swing(t / duration), this);
            yield return null;
        }

        vn.SetEffectState(this, null);
        stage.ResetOffset(target, this);
    }

    /// <summary>One shake when the node starts, then nothing until the controller stops it.</summary>
    public override IEnumerator Sustain(VisualNovelTalkable vn)
    {
        yield return Play(vn);
        while (true) yield return null;
    }

    /// <summary>Settles from wherever the shake was cut off.</summary>
    public override IEnumerator Release(VisualNovelTalkable vn)
    {
        VisualNovelBackground stage = vn ? vn.Background : null;
        if (!stage) yield break;

        // Nothing was shaking: just make sure the target is back at rest.
        if (vn.GetEffectState(this) == null)
        {
            stage.ResetOffset(target, this);
            yield break;
        }
        vn.SetEffectState(this, null);

        Vector2 from = stage.GetOffset(target, this);
        for (float t = 0f; t < releaseTime; t += Time.deltaTime)
        {
            stage.SetOffset(target, from * (1f - t / releaseTime), this);
            yield return null;
        }

        stage.ResetOffset(target, this);
    }

    /// <summary>
    /// 0..1 progress to a roughly -1..1 swing: a sine that dies away to exactly zero at the end,
    /// scaled so the first peak (the knock itself) reaches about full distance and each swing back
    /// is smaller.
    /// </summary>
    private float Swing(float p)
    {
        float firstPeak = 1f - 0.25f / swings;
        float decay = (1f - p) * (1f - p) / (firstPeak * firstPeak);
        return Mathf.Sin(p * swings * 2f * Mathf.PI) * decay;
    }

    private Vector2 PickDirection()
    {
        Vector2 dir = direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : Random.insideUnitCircle.normalized;
        if (dir == Vector2.zero) dir = Vector2.down;

        if (directionSpread > 0f)
        {
            float angle = Random.Range(-directionSpread, directionSpread) * Mathf.Deg2Rad;
            float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
            dir = new Vector2(dir.x * cos - dir.y * sin, dir.x * sin + dir.y * cos);
        }

        return dir;
    }
}
