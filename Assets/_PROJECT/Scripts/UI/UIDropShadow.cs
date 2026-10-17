using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Soft drop shadow around the edges of an opaque, rectangular UI Image (unlike the built-in <see cref="Shadow"/>,
/// which is a hard-edged offset copy).
///
/// <para>Adds a feathered rectangle behind the graphic's own mesh: a solid core plus rings that fade out towards
/// the edge. The shadow samples the texel at the middle of the sprite, so that texel must be opaque.</para>
/// </summary>
[RequireComponent(typeof(Graphic))]
[DisallowMultipleComponent]
public class UIDropShadow : BaseMeshEffect
{
    [Tooltip("Shadow colour; alpha is the opacity at the core.")]
    [SerializeField] private Color color = new Color(0f, 0f, 0f, 0.6f);
    [Tooltip("Shift of the shadow relative to the graphic, in canvas units.")]
    [SerializeField] private Vector2 offset = new Vector2(8f, -12f);
    [Tooltip("How far the solid core grows past the graphic's edges (negative = shrinks).")]
    [SerializeField] private float spread = 0f;
    [Tooltip("Width of the soft falloff, in canvas units.")]
    [SerializeField, Min(0f)] private float blur = 40f;
    [Tooltip("Number of rings in the falloff; more = smoother gradient.")]
    [SerializeField, Range(1, 12)] private int steps = 6;

    private static readonly List<UIVertex> Stream = new List<UIVertex>();
    private static readonly List<UIVertex> Output = new List<UIVertex>();

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || vh.currentVertCount == 0)
            return;

        Stream.Clear();
        vh.GetUIVertexStream(Stream);

        // Bounds and centre UV of the graphic's own mesh (respects Preserve Aspect).
        Vector2 min = Stream[0].position, max = min;
        Vector4 uv = Vector4.zero;
        foreach (UIVertex v in Stream)
        {
            min = Vector2.Min(min, v.position);
            max = Vector2.Max(max, v.position);
            uv += v.uv0;
        }
        uv /= Stream.Count;

        Rect core = Rect.MinMaxRect(min.x + offset.x - spread + blur * 0.5f, min.y + offset.y - spread + blur * 0.5f,
                                    max.x + offset.x + spread - blur * 0.5f, max.y + offset.y + spread - blur * 0.5f);

        Output.Clear();
        AddQuad(core, core, uv, 1f, 1f);
        for (int i = 0; i < steps; i++)
        {
            float t0 = (float)i / steps, t1 = (float)(i + 1) / steps;
            AddQuad(Grow(core, blur * t0), Grow(core, blur * t1), uv, Falloff(t0), Falloff(t1));
        }

        Output.AddRange(Stream);
        vh.Clear();
        vh.AddUIVertexTriangleStream(Output);
    }

    private static Rect Grow(Rect r, float by) => Rect.MinMaxRect(r.xMin - by, r.yMin - by, r.xMax + by, r.yMax + by);

    // Smooth fade from 1 at the core to 0 at the outer edge.
    private static float Falloff(float t) => 1f - Mathf.SmoothStep(0f, 1f, t);

    /// <summary>Fills the area between <paramref name="inner"/> and <paramref name="outer"/>
    /// (or the whole rect when they are equal) with the given alphas.</summary>
    private void AddQuad(Rect inner, Rect outer, Vector4 uv, float innerAlpha, float outerAlpha)
    {
        if (inner == outer)
        {
            AddTri(V(inner.xMin, inner.yMin, uv, innerAlpha), V(inner.xMin, inner.yMax, uv, innerAlpha), V(inner.xMax, inner.yMax, uv, innerAlpha));
            AddTri(V(inner.xMax, inner.yMax, uv, innerAlpha), V(inner.xMax, inner.yMin, uv, innerAlpha), V(inner.xMin, inner.yMin, uv, innerAlpha));
            return;
        }

        Vector2[] i = { new(inner.xMin, inner.yMin), new(inner.xMin, inner.yMax), new(inner.xMax, inner.yMax), new(inner.xMax, inner.yMin) };
        Vector2[] o = { new(outer.xMin, outer.yMin), new(outer.xMin, outer.yMax), new(outer.xMax, outer.yMax), new(outer.xMax, outer.yMin) };
        for (int s = 0; s < 4; s++)
        {
            int n = (s + 1) % 4;
            UIVertex a = V(i[s].x, i[s].y, uv, innerAlpha), b = V(i[n].x, i[n].y, uv, innerAlpha);
            UIVertex c = V(o[n].x, o[n].y, uv, outerAlpha), d = V(o[s].x, o[s].y, uv, outerAlpha);
            AddTri(a, d, c);
            AddTri(c, b, a);
        }
    }

    private void AddTri(UIVertex a, UIVertex b, UIVertex c)
    {
        Output.Add(a);
        Output.Add(b);
        Output.Add(c);
    }

    private UIVertex V(float x, float y, Vector4 uv, float alpha)
    {
        UIVertex v = UIVertex.simpleVert;
        v.position = new Vector3(x, y, 0f);
        v.uv0 = uv;
        Color c = color;
        c.a *= alpha;
        v.color = c;
        return v;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        if (graphic != null)
            graphic.SetVerticesDirty();
    }
#endif
}
