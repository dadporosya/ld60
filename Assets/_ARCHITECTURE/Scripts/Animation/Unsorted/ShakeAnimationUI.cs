using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UI counterpart of <see cref="ShakeAnimation"/>. Reuses all of the base shake logic
/// (Perlin noise, amplitude envelope, deceleration toggle, return-to-rest) but writes the
/// shake offset to the RectTransform's <c>anchoredPosition</c> so it works on UI elements
/// under a Canvas. Only the shake's delta is applied each frame, so it stacks with layout /
/// other movers instead of overwriting them.
///
/// UI lives on the screen plane, so use the X (horizontal) and Y (vertical) axes here; the
/// Z axis has no effect. The ground-plane default (X/Z) is remapped to X/Y automatically.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class ShakeAnimationUI : ShakeAnimation
{
    private RectTransform _rect;

    public override void Awake()
    {
        _rect = transform as RectTransform;

        // ShakeAnimation defaults to the ground plane (X/Z). For UI the meaningful plane is
        // the screen (X/Y), so swap the untouched default over before the base captures state.
        if (targetAxes.Count == 2 && targetAxes.Contains(Axis.X) && targetAxes.Contains(Axis.Z))
            targetAxes = new List<Axis> { Axis.X, Axis.Y };

        base.Awake();
    }

    // Redirect the shake offset onto anchoredPosition (X/Y), applying only the delta so the
    // shake adds to the element's laid-out position rather than replacing it.
    protected override void SetOffset(Vector3 target)
    {
        if (!_rect) return;
        Vector3 delta = target - _appliedOffset;
        _rect.anchoredPosition += new Vector2(delta.x, delta.y);
        _appliedOffset = target;
    }
}
