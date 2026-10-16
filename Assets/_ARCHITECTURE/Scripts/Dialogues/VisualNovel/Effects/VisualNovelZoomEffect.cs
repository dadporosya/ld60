using System.Collections;
using PrimeTween;
using UnityEngine;

/// <summary>
/// Zooms the stage's background and/or foreground image (see <see cref="target"/>) in towards
/// <see cref="focus"/>, which stays in place on screen.
///
/// As a start effect it pushes in, holds, and eases back to rest - the "look closer" beat - or,
/// with <see cref="holdUntilNodeEnd"/>, stays pushed in until the next node starts. As a
/// sustained effect it pushes in and stays there until a node without it starts, then eases back
/// (list it on the following nodes too to keep the stage zoomed across them).
///
/// Zooming starts from wherever the layers are, so it picks up smoothly from a zoom still easing
/// back. Each layer's crossfade image scales with it, and shake effects still apply on top.
/// Zooming out (below 1) shows the images' edges.
/// </summary>
[CreateAssetMenu(fileName = "VN Zoom", menuName = "Dialogues/Visual Novel/Effects/Zoom")]
public class VisualNovelZoomEffect : VisualNovelEffect
{
    [Header("Target")]
    [Tooltip("Which stage images zoom: the background, the foreground, or both together.")]
    public VisualNovelLayers target = VisualNovelLayers.Both;
    [Tooltip("Scale to reach: 1 = rest, 1.2 = 20% closer.")]
    [Min(0.01f)] public float zoom = 1.2f;
    [Tooltip("Point that stays in place while zooming, 0..1 across each image: (0.5, 0.5) = centre, " +
             "(0.5, 0.75) = upper middle.")]
    public Vector2 focus = new Vector2(0.5f, 0.5f);

    [Header("Timing")]
    [Tooltip("Seconds to push in.")]
    [Min(0f)] public float zoomIn = 0.6f;
    [Tooltip("Start effect only: stay at full zoom until the next node starts (or the dialogue ends), " +
             "then ease back. Wait For Completion then only waits for the push in.")]
    public bool holdUntilNodeEnd = false;
    [Tooltip("Start effect only, without Hold Until Node End: seconds held at full zoom before easing back.")]
    [Min(0f)] public float hold = 0.5f;
    [Tooltip("Seconds to ease back to rest - at the end of a start effect, or when released.")]
    [Min(0f)] public float zoomOut = 0.6f;
    public Ease ease = Ease.InOutSine;

    // The controller releases a held zoom after Play has finished (see VisualNovelEffect).
    public override bool ReleaseAtNodeEnd => holdUntilNodeEnd;

    public override IEnumerator Play(VisualNovelTalkable vn)
    {
        VisualNovelBackground stage = Resolve(vn);
        if (!stage) yield break;

        yield return ZoomTo(vn, stage, zoom, zoomIn);
        if (holdUntilNodeEnd) yield break;

        if (hold > 0f) yield return new WaitForSeconds(hold);
        yield return ZoomTo(vn, stage, 1f, zoomOut);
    }

    public override IEnumerator Sustain(VisualNovelTalkable vn)
    {
        VisualNovelBackground stage = Resolve(vn);
        if (!stage) yield break;

        yield return ZoomTo(vn, stage, zoom, zoomIn);
        while (true) yield return null;
    }

    /// <summary>
    /// Eases back to rest from wherever the zoom was stopped - unless another zoom has taken the
    /// layers over since (the next node's own zoom), which is left alone.
    /// </summary>
    public override IEnumerator Release(VisualNovelTalkable vn)
    {
        VisualNovelBackground stage = vn ? vn.Background : null;
        if (!stage) yield break;

        object mine = vn.GetEffectState(this);
        if (stage.ZoomOwner != null && stage.ZoomOwner != mine) yield break;

        yield return ZoomTo(vn, stage, 1f, zoomOut);
    }

    /// <summary>
    /// Eases the zoom from where it is now to <paramref name="to"/>. The newest run owns the
    /// layers (<see cref="VisualNovelBackground.ZoomOwner"/>): an older one still easing stops
    /// there, so two runs never pull the same layers opposite ways. The run stays recorded as this
    /// asset's last one, which is how <see cref="Release"/> tells whether the layers are still its own.
    /// </summary>
    private IEnumerator ZoomTo(VisualNovelTalkable vn, VisualNovelBackground stage, float to, float duration)
    {
        object run = new object();
        stage.ZoomOwner = run;
        vn.SetEffectState(this, run);

        float from = stage.GetZoom(target);

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            if (stage.ZoomOwner != run) yield break;
            stage.SetZoom(target, Mathf.LerpUnclamped(from, to, Easing.Evaluate(t / duration, ease)), focus);
            yield return null;
        }

        if (stage.ZoomOwner != run) yield break;
        stage.SetZoom(target, to, focus);
    }

    private VisualNovelBackground Resolve(VisualNovelTalkable vn)
    {
        VisualNovelBackground stage = vn ? vn.Background : null;
        if (!stage) h.Out($"{name}: VisualNovelTalkable has no background to zoom.");
        return stage;
    }
}
