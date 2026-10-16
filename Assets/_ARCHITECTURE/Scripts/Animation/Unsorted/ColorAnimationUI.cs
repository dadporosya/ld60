using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI counterpart of <see cref="ColorAnimation"/>. Reuses all of the base keyframe logic
/// (values, durations, easing, multiply mode, return-to-rest) and only changes where the colours
/// are written: UI Graphics (Image / RawImage / TMP text) instead of SpriteRenderers.
///
/// A Graphic is NOT a Renderer and has no SpriteRenderer, so the base class's sweep never sees it
/// — this is why the plain ColorAnimation silently does nothing on a Canvas. <c>Graphic.color</c>
/// is the canvas equivalent of <c>SpriteRenderer.color</c> (it feeds the vertex tint, no material
/// instancing involved), so the tinting itself is identical.
/// </summary>
public class ColorAnimationUI : ColorAnimation
{
    private readonly List<Graphic> _graphics = new();

    protected override int TargetCount => _graphics.Count;

    /// <summary>
    /// Collects this object's Graphic plus every Graphic below it.
    /// </summary>
    protected override void CollectTargets()
    {
        _graphics.Clear();

        if (includeChildren)
        {
            foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
                if (graphic != null && !_graphics.Contains(graphic)) _graphics.Add(graphic);
        }
        else
        {
            Graphic graphic = GetComponent<Graphic>();
            if (graphic != null) _graphics.Add(graphic);
        }

        if (_graphics.Count == 0)
            h.Out($"ColorAnimationUI on '{name}': no Graphic found; the animation will do nothing.");
    }

    protected override Color ReadColorAt(int index) => _graphics[index].color;

    protected override void WriteColorAt(int index, Color color) => _graphics[index].color = color;
}
