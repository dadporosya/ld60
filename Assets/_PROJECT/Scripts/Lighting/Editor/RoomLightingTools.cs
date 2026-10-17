using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// One-click setup for the yellow fluorescent "metro station" look:
/// a fake-light rig over a UI room (<see cref="RoomAmbientLight"/> + <see cref="RoomLight"/>s) and colour grading
/// on the scene's global Volume. Everything it creates stays editable afterwards.
/// </summary>
public static class RoomLightingTools
{
    private const string Menu = "Tools/Room Lighting/";

    [MenuItem(Menu + "Create Metro Lighting Over Selected", true)]
    private static bool CanCreateRig() =>
        Selection.activeTransform is RectTransform rt && rt.parent && rt.GetComponentInParent<Canvas>();

    /// <summary>Adds "Room Lighting" as the last sibling of the selected UI room, covering the same rect.</summary>
    [MenuItem(Menu + "Create Metro Lighting Over Selected")]
    private static void CreateRigForSelection()
    {
        GameObject root = CreateRig((RectTransform)Selection.activeTransform);
        Selection.activeGameObject = root;
    }

    public static GameObject CreateRig(RectTransform target)
    {
        Vector2 size = target.rect.size;

        var root = new GameObject("Room Lighting", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(root, "Create Room Lighting");
        var rootRect = (RectTransform)root.transform;
        rootRect.SetParent(target.parent, false);
        rootRect.SetAsLastSibling();
        rootRect.anchorMin = target.anchorMin;
        rootRect.anchorMax = target.anchorMax;
        rootRect.pivot = target.pivot;
        rootRect.anchoredPosition = target.anchoredPosition;
        rootRect.sizeDelta = target.sizeDelta;
        rootRect.localRotation = target.localRotation;
        rootRect.localScale = target.localScale;

        // Order matters: ambient darkens first, lit layers then brighten, glow goes on top.
        Add<RoomAmbientLight>(rootRect, "Ambient", Vector2.zero, size);

        RoomLight tube = Add<RoomLight>(rootRect, "Fluorescent Tube", new Vector2(0f, 0.4f * size.y),
            new Vector2(1.1f * size.x, 0.3f * size.y));
        tube.LightShape = RoomLight.Shape.Tube;
        tube.Falloff = 1.2f;
        tube.Intensity = 1f;
        tube.Flicker = RoomLight.FlickerStyle.Broken;
        tube.Brokenness = 0.5f;

        RoomLight pool = Add<RoomLight>(rootRect, "Floor Light Pool", new Vector2(0f, 0.2f * size.y),
            new Vector2(1.4f * size.x, 1.1f * size.y));
        pool.Color = new Color(1f, 0.85f, 0.5f);
        pool.Intensity = 0.8f;
        pool.FlickerSource = tube;

        RoomLight haze = Add<RoomLight>(rootRect, "Tube Haze", new Vector2(0f, 0.45f * size.y),
            new Vector2(0.8f * size.x, 0.12f * size.y));
        haze.LightMode = RoomLight.Mode.Glow;
        haze.LightShape = RoomLight.Shape.Tube;
        haze.Color = new Color(1f, 0.92f, 0.7f);
        haze.Intensity = 0.12f;
        haze.FlickerSource = tube;

        EditorSceneManager.MarkSceneDirty(root.scene);
        return root;
    }

    private const string VnRigName = "VN Room Lighting";

    /// <summary>
    /// Lays the hub's lighting over the visual novel stage, as seen from the VN frame's angle: the same ambient and
    /// lights (settings copied from the hub rig), re-placed for a wall seen face-on with the ceiling tube just above
    /// the frame. The VN tube follows the hub tube's flicker (same lamp); the rig only shows while a VN dialogue plays.
    /// Re-running replaces the previous VN rig.
    /// </summary>
    [MenuItem(Menu + "Create VN Lighting (Match Hub)")]
    public static void CreateVnRig()
    {
        VisualNovelBackground stage = Object.FindFirstObjectByType<VisualNovelBackground>(FindObjectsInactive.Include);
        if (!stage)
        {
            EditorUtility.DisplayDialog("Room Lighting", "No VisualNovelBackground in the open scene.", "OK");
            return;
        }

        var stageData = new SerializedObject(stage);
        var picture = stageData.FindProperty("foreground").objectReferenceValue as Image;
        if (!picture) picture = stageData.FindProperty("front").objectReferenceValue as Image;
        var overlay = stageData.FindProperty("overlay").objectReferenceValue as Image;

        // The hub rig: the ambient layer outside the stage, and the lights next to it.
        RoomAmbientLight hubAmbient = null;
        foreach (RoomAmbientLight a in Object.FindObjectsByType<RoomAmbientLight>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (!a.transform.IsChildOf(stage.transform)) { hubAmbient = a; break; }

        RoomLight hubTube = null, hubPool = null, hubHaze = null;
        if (hubAmbient && hubAmbient.transform.parent)
        {
            foreach (RoomLight l in hubAmbient.transform.parent.GetComponentsInChildren<RoomLight>(true))
            {
                if (!l.FlickerSource && !hubTube) hubTube = l;
                else if (l.LightMode == RoomLight.Mode.Glow && !hubHaze) hubHaze = l;
                else if (l.LightMode == RoomLight.Mode.Lit && !hubPool) hubPool = l;
            }
        }

        if (!hubAmbient || !hubTube)
            h.Out("Room Lighting: no hub rig found (Ambient + a tube without Flicker Source) - VN rig uses defaults.");

        Transform old = stage.transform.Find(VnRigName);
        if (old) Undo.DestroyObjectImmediate(old.gameObject);

        var root = new GameObject(VnRigName, typeof(RectTransform), typeof(CanvasGroup));
        Undo.RegisterCreatedObjectUndo(root, "Create VN Lighting");
        var rootRect = (RectTransform)root.transform;
        rootRect.SetParent(stage.transform, false);
        // Over the picture images, under the Overlay so fades to black cover the lights too.
        if (overlay && overlay.transform.parent == stage.transform)
            rootRect.SetSiblingIndex(overlay.transform.GetSiblingIndex());
        else rootRect.SetAsLastSibling();
        if (picture) CopyRect(picture.rectTransform, rootRect);
        else SetAnchors(rootRect, Vector2.zero, Vector2.one);

        var rig = root.AddComponent<VisualNovelRoomLighting>();
        var rigData = new SerializedObject(rig);
        rigData.FindProperty("followTarget").objectReferenceValue = picture ? picture.rectTransform : null;
        rigData.ApplyModifiedPropertiesWithoutUndo();

        // Rects are fractions of the frame (0..1, from bottom-left), so they hold at any resolution.
        AddVn<RoomAmbientLight>(rootRect, "Ambient", hubAmbient, new Vector2(0f, 0f), new Vector2(1f, 1f));

        // The tube is just above the frame: its lower half lights the top of the wall.
        RoomLight tube = AddVn<RoomLight>(rootRect, "Fluorescent Tube", hubTube,
            new Vector2(-0.05f, 0.82f), new Vector2(1.05f, 1.18f));
        if (!hubTube)
        {
            tube.LightShape = RoomLight.Shape.Tube;
            tube.Falloff = 1.2f;
            tube.Flicker = RoomLight.FlickerStyle.Broken;
        }
        tube.FlickerSource = hubTube; // same lamp as in the hub: flickers in sync (own flicker while the hub is off)

        // Wash down the wall from the ceiling.
        RoomLight wall = AddVn<RoomLight>(rootRect, "Wall Light Pool", hubPool,
            new Vector2(-0.15f, 0.15f), new Vector2(1.15f, 1.55f));
        if (!hubPool) { wall.Color = new Color(1f, 0.85f, 0.5f); wall.Intensity = 0.8f; }
        wall.FlickerSource = tube;

        // A little light spilling onto the floor at the foot of the wall.
        RoomLight floor = AddVn<RoomLight>(rootRect, "Floor Light Pool", hubPool,
            new Vector2(0f, 0.005f), new Vector2(1f, 0.355f));
        floor.Color = new Color(1f, 0.85f, 0.5f);
        floor.Intensity = 0.35f;
        floor.FlickerSource = tube;

        // Haze at the top edge, where the tube sits just out of frame.
        RoomLight haze = AddVn<RoomLight>(rootRect, "Tube Haze", hubHaze,
            new Vector2(0.125f, 0.93f), new Vector2(0.875f, 1.07f));
        if (!hubHaze)
        {
            haze.LightMode = RoomLight.Mode.Glow;
            haze.LightShape = RoomLight.Shape.Tube;
            haze.Color = new Color(1f, 0.92f, 0.7f);
            haze.Intensity = 0.12f;
        }
        haze.FlickerSource = tube;

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        h.Out($"Room Lighting: '{VnRigName}' created over '{stage.name}'" +
              (hubTube ? $", flickering with '{hubTube.name}'." : "."));
    }

    /// <summary>Adds a light layer stretched over [<paramref name="min"/>, <paramref name="max"/>] of the parent,
    /// with <paramref name="settings"/>' serialized values copied in when given.</summary>
    private static T AddVn<T>(RectTransform parent, string name, T settings, Vector2 min, Vector2 max)
        where T : RoomLightLayer
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create VN Lighting");
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        SetAnchors(rt, min, max);

        var layer = go.AddComponent<T>();
        if (settings)
        {
            EditorUtility.CopySerialized(settings, layer);
            layer.MarkDirty(); // rebuild the texture from the copied shape
        }
        return layer;
    }

    private static void SetAnchors(RectTransform rt, Vector2 min, Vector2 max)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
    }

    private static void CopyRect(RectTransform from, RectTransform to)
    {
        to.anchorMin = from.anchorMin;
        to.anchorMax = from.anchorMax;
        to.pivot = from.pivot;
        to.anchoredPosition = from.anchoredPosition;
        to.sizeDelta = from.sizeDelta;
        to.localRotation = from.localRotation;
        to.localScale = from.localScale;
    }

    private static T Add<T>(RectTransform parent, string name, Vector2 position, Vector2 size) where T : RoomLightLayer
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create Room Lighting");
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
        return go.AddComponent<T>();
    }

    /// <summary>
    /// Adds/overrides URP colour grading on the global Volume's profile (sickly yellow-olive, crushed shadows,
    /// vignette, grain) and enables post-processing on the main camera so it shows. VolFx overrides are untouched.
    /// </summary>
    [MenuItem(Menu + "Apply Metro Grading To Global Volume")]
    public static void ApplyGrading()
    {
        Volume volume = null;
        foreach (Volume v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            if (v.isGlobal && v.sharedProfile) { volume = v; break; }

        if (!volume)
        {
            EditorUtility.DisplayDialog("Room Lighting", "No global Volume with a profile in the open scene.", "OK");
            return;
        }

        VolumeProfile profile = volume.sharedProfile;
        Undo.RecordObject(profile, "Apply Metro Grading");

        Set<ColorAdjustments>(profile, c =>
        {
            c.contrast.Override(22f);
            c.saturation.Override(-18f);
            c.colorFilter.Override(new Color(1f, 0.95f, 0.8f));
        });
        Set<SplitToning>(profile, c =>
        {
            c.shadows.Override(new Color(0.42f, 0.5f, 0.26f));
            c.highlights.Override(new Color(0.92f, 0.8f, 0.52f));
            c.balance.Override(-15f);
        });
        Set<ShadowsMidtonesHighlights>(profile, c =>
        {
            c.shadows.Override(new Vector4(0.92f, 1f, 0.78f, -0.12f));
            c.midtones.Override(new Vector4(1f, 0.98f, 0.86f, 0f));
            c.highlights.Override(new Vector4(1f, 0.96f, 0.84f, 0.02f));
        });
        Set<Vignette>(profile, c =>
        {
            c.color.Override(new Color(0.04f, 0.04f, 0.01f));
            c.intensity.Override(0.35f);
            c.smoothness.Override(0.5f);
        });
        Set<FilmGrain>(profile, c =>
        {
            c.type.Override(FilmGrainLookup.Medium3);
            c.intensity.Override(0.3f);
            c.response.Override(0.8f);
        });
        Set<Bloom>(profile, c =>
        {
            c.threshold.Override(0.9f);
            c.intensity.Override(0.5f);
            c.scatter.Override(0.6f);
            c.tint.Override(new Color(1f, 0.9f, 0.65f));
        });

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        Camera cam = Camera.main;
        if (cam && cam.TryGetComponent(out UniversalAdditionalCameraData data) && !data.renderPostProcessing)
        {
            Undo.RecordObject(data, "Apply Metro Grading");
            data.renderPostProcessing = true;
            EditorUtility.SetDirty(data);
            EditorSceneManager.MarkSceneDirty(cam.gameObject.scene);
            h.Out($"Room Lighting: enabled Post Processing on '{cam.name}' so the Volume grading shows.");
        }

        h.Out($"Room Lighting: metro grading applied to '{profile.name}'.");
    }

    private static void Set<T>(VolumeProfile profile, Action<T> configure) where T : VolumeComponent
    {
        if (!profile.TryGet(out T component))
        {
            component = profile.Add<T>();
            component.name = typeof(T).Name;
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            if (EditorUtility.IsPersistent(profile)) AssetDatabase.AddObjectToAsset(component, profile);
            Undo.RegisterCreatedObjectUndo(component, "Apply Metro Grading");
        }

        Undo.RecordObject(component, "Apply Metro Grading");
        component.active = true;
        configure(component);
        EditorUtility.SetDirty(component);
    }
}
