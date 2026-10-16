using UnityEditor;
using UnityEngine;

/// <summary>
/// Gives the old TV's <c>screen</c> mesh proper 0..1 UVs on import, so a render texture fed into its
/// material (<c>TvScreenMat</c>, driven by the TvCamera) fills the whole screen.
///
/// <para><b>Why it exists.</b> The screen as exported from Blender is not unwrapped for that:
/// <c>old-tv-flatscreen.fbx</c> has all four UVs at (0, 0), so the whole screen samples one texel and
/// shows a flat colour; the curved screen in <c>old tv.fbx</c> only covers a strip of UV space. Instead
/// of relying on a manual unwrap, the UVs are planar-projected onto the screen's own face here.</para>
///
/// <para><b>How the projection is oriented.</b> The face direction is the averaged vertex normal; the
/// texture's up is the model's world up projected onto that face, and its right is the viewer's right
/// when looking at the screen. So the camera's image lands upright and unmirrored whatever axis the
/// model was exported with.</para>
///
/// <para>Only touches meshes named <see cref="ScreenNodeName"/> in models under <see cref="ModelFolder"/>.
/// Bump <see cref="GetVersion"/> after changing the projection so Unity reimports those models.</para>
/// </summary>
public class TvScreenUVPostprocessor : AssetPostprocessor
{
    private const string ModelFolder = "Assets/_PROJECT/Models/old-tv/";
    private const string ScreenNodeName = "screen";

    public override uint GetVersion() => 1;

    private void OnPostprocessModel(GameObject root)
    {
        if (!assetPath.StartsWith(ModelFolder)) return;

        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.name == ScreenNodeName && filter.sharedMesh != null)
                FitUVs(filter.sharedMesh, filter.transform);
        }
    }

    private void FitUVs(Mesh mesh, Transform screen)
    {
        var vertices = mesh.vertices;
        var normals = mesh.normals;

        var facing = Vector3.zero;
        foreach (var n in normals) facing += n;
        if (facing.sqrMagnitude < 1e-8f)
        {
            Debug.LogWarning($"{assetPath}: '{ScreenNodeName}' has no usable normals, UVs left as exported.");
            return;
        }
        facing.Normalize();

        // Texture up = model up laid onto the screen face; fall back to local forward for a screen facing up.
        var up = Vector3.ProjectOnPlane(screen.InverseTransformDirection(Vector3.up), facing);
        if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(Vector3.forward, facing);
        up.Normalize();
        // The viewer looks along -facing, so their right is Cross(up, -facing).
        var right = Vector3.Cross(facing, up);

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        var projected = new Vector2[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            projected[i] = new Vector2(Vector3.Dot(vertices[i], right), Vector3.Dot(vertices[i], up));
            min = Vector2.Min(min, projected[i]);
            max = Vector2.Max(max, projected[i]);
        }

        var size = max - min;
        if (size.x < 1e-6f || size.y < 1e-6f)
        {
            Debug.LogWarning($"{assetPath}: '{ScreenNodeName}' is degenerate, UVs left as exported.");
            return;
        }

        var uvs = new Vector2[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
            uvs[i] = new Vector2((projected[i].x - min.x) / size.x, (projected[i].y - min.y) / size.y);

        mesh.uv = uvs;
        mesh.RecalculateTangents();
    }
}
