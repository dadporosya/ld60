using System;
using System.Collections.Generic;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>How a <see cref="ChoiceFork"/> looks and sounds.</summary>
[Serializable]
public class ChoiceForkLook
{
    [Tooltip("Where the lines go (centred in it). Empty: a screen-space overlay canvas made at runtime.")]
    public RectTransform parent;
    [Tooltip("Sorting order of the runtime canvas (no Parent). Keep it above the fade image's canvas.")]
    public int sortingOrder = 100;
    [Tooltip("Copied once per line (font, size, material). Empty: a plain TextMeshProUGUI with the settings below.")]
    public TextMeshProUGUI lineTemplate;
    [Tooltip("No Line Template: the font. Empty: TextMeshPro's default font.")]
    public TMP_FontAsset font;
    [Min(1f)] public float fontSize = 56f;
    public Color lineColor = Color.white;
    public Color lineHoverColor = new Color(0.55f, 0.04f, 0.04f, 1f);
    [Tooltip("Width (UI units) the lines are spread across, side by side: each gets an equal share, its text centred " +
             "in it (two lines: ---Yes------No---).")]
    [Min(0f)] public float width = 1200f;
    [Tooltip("Extra room (UI units) around each line's text that still counts as hovering it.")]
    [Min(0f)] public float hitPadding = 16f;
    [Tooltip("Offset of the lines from the centre of the Parent / screen (UI units).")]
    public Vector2 offset = Vector2.zero;
    [Tooltip("Seconds the lines fade in / out over.")]
    [Min(0f)] public float fadeDuration = 0.35f;
    public AudioClip hoverSfx;
    public AudioClip clickSfx;
}

/// <summary>
/// Clickable lines side by side in the middle of the screen (Yes / No), one of which the player picks.
/// Made at runtime with <see cref="Create"/>, shown with <see cref="Show"/>. Hover and click are read straight from
/// the mouse against each line's text, so no other canvas's raycast target can swallow them. Unscaled time.
/// </summary>
public class ChoiceFork : MonoBehaviour
{
    private ChoiceForkLook look;
    private RectTransform canvas;
    private RectTransform container;
    private CanvasGroup group;
    private readonly List<TextMeshProUGUI> lines = new List<TextMeshProUGUI>();
    private Action<int> onPicked;
    private bool open;
    private int hoveredLine = -1;
    private bool cursorOnLine;

    /// <summary>True from <see cref="Show"/> until the pick has faded out.</summary>
    public bool IsShowing { get; private set; }

    /// <summary>
    /// A fork with <paramref name="look"/>, on a new GameObject under <paramref name="owner"/> (null: a root
    /// object; keep it out of other canvases, or the runtime canvas isn't an overlay of its own).
    /// </summary>
    public static ChoiceFork Create(ChoiceForkLook look, Transform owner = null)
    {
        GameObject go = new GameObject("Choice Fork");
        if (owner) go.transform.SetParent(owner, false);
        ChoiceFork fork = go.AddComponent<ChoiceFork>();
        fork.look = look ?? new ChoiceForkLook();
        return fork;
    }

    /// <summary>
    /// Shows one line per title and waits for a click. The picked line stays lit while the others fade out with
    /// it; then <paramref name="picked"/> runs with its index.
    /// </summary>
    public void Show(IReadOnlyList<string> titles, Action<int> picked)
    {
        Clear();
        RectTransform row = Container();
        onPicked = picked;

        for (int i = 0; i < titles.Count; i++)
        {
            TextMeshProUGUI line = MakeLine(row);
            line.text = titles[i] ?? "";
            line.color = look.lineColor;
            // Hover and click are read in Update; the lines block nothing.
            line.raycastTarget = false;
            lines.Add(line);
        }

        IsShowing = true;
        open = true;
        group.blocksRaycasts = true;
        Tween.StopAll(group);
        if (look.fadeDuration > 0f)
        {
            group.alpha = 0f;
            Tween.Alpha(group, 1f, look.fadeDuration, useUnscaledTime: true);
        }
        else group.alpha = 1f;
    }

    /// <summary>Takes the lines away at once, without a pick.</summary>
    public void Clear()
    {
        open = false;
        IsShowing = false;
        onPicked = null;
        hoveredLine = -1;
        SetCursorOnLine(false);
        foreach (TextMeshProUGUI line in lines)
            if (line) Destroy(line.gameObject);
        lines.Clear();
        if (group)
        {
            Tween.StopAll(group);
            group.alpha = 0f;
            group.blocksRaycasts = false;
        }
    }

    private void OnDestroy()
    {
        SetCursorOnLine(false);
        // A canvas made under another parent goes with this fork.
        if (canvas && !canvas.IsChildOf(transform)) Destroy(canvas.gameObject);
        else if (container && !container.IsChildOf(transform)) Destroy(container.gameObject);
    }

    private void Update()
    {
        if (!open) return;

        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        Vector2 pointer = mouse.position.ReadValue();
        int hovered = -1;
        for (int i = 0; i < lines.Count; i++)
            if (IsOver(lines[i], pointer))
            {
                hovered = i;
                break;
            }

        if (hovered != hoveredLine)
        {
            if (hoveredLine >= 0 && hoveredLine < lines.Count && lines[hoveredLine])
                lines[hoveredLine].color = look.lineColor;
            hoveredLine = hovered;
            if (hovered >= 0)
            {
                lines[hovered].color = look.lineHoverColor;
                PlaySfx(look.hoverSfx);
            }
            SetCursorOnLine(hovered >= 0);
        }

        if (hovered >= 0 && mouse.leftButton.wasPressedThisFrame) Pick(hovered);
    }

    private void Pick(int index)
    {
        open = false;
        group.blocksRaycasts = false;
        SetCursorOnLine(false);
        PlaySfx(look.clickSfx);
        if (lines[index]) lines[index].color = look.lineHoverColor;

        Action<int> picked = onPicked;
        Action done = () =>
        {
            Clear();
            picked?.Invoke(index);
        };

        if (look.fadeDuration > 0f) Tween.Alpha(group, 0f, look.fadeDuration, useUnscaledTime: true).OnComplete(done);
        else done();
    }

    /// <summary>Whether <paramref name="screen"/> is on <paramref name="line"/>'s text (plus the hit padding).</summary>
    private bool IsOver(TextMeshProUGUI line, Vector2 screen)
    {
        if (!line || !line.canvas) return false;

        Canvas root = line.canvas.rootCanvas;
        Camera cam = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(line.rectTransform, screen, cam, out Vector2 local))
            return false;

        Bounds bounds = line.textBounds;
        bounds.Expand(new Vector3(look.hitPadding * 2f, look.hitPadding * 2f, 0f));
        return local.x >= bounds.min.x && local.x <= bounds.max.x && local.y >= bounds.min.y && local.y <= bounds.max.y;
    }

    /// <summary>
    /// The centred row the lines go into (made once): the look's width wide, split into equal cells, one per
    /// line, each text centred in its cell.
    /// </summary>
    private RectTransform Container()
    {
        if (container) return container;

        RectTransform parent = look.parent ? look.parent : Canvas();
        GameObject go = new GameObject("Fork Lines", typeof(RectTransform), typeof(CanvasGroup),
            typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        container = (RectTransform)go.transform;
        container.SetParent(parent, false);
        container.anchorMin = container.anchorMax = container.pivot = new Vector2(0.5f, 0.5f);
        container.anchoredPosition = look.offset;
        container.sizeDelta = new Vector2(look.width, 0f);

        HorizontalLayoutGroup layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 0f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = go.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        group = go.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        return container;
    }

    /// <summary>A screen-space overlay canvas over everything (the look's sorting order), on this fork.</summary>
    private RectTransform Canvas()
    {
        if (canvas) return canvas;

        GameObject go = new GameObject("Fork Canvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);

        Canvas c = go.GetComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = look.sortingOrder;

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvas = (RectTransform)go.transform;
        return canvas;
    }

    private TextMeshProUGUI MakeLine(RectTransform row)
    {
        TextMeshProUGUI line;
        if (look.lineTemplate)
        {
            line = Instantiate(look.lineTemplate, row);
            line.gameObject.SetActive(true);
        }
        else
        {
            GameObject go = new GameObject("Fork Line", typeof(RectTransform));
            go.transform.SetParent(row, false);
            line = go.AddComponent<TextMeshProUGUI>();
            if (look.font) line.font = look.font;
            line.fontSize = look.fontSize;
            line.textWrappingMode = TextWrappingModes.NoWrap;
        }

        line.alignment = TextAlignmentOptions.Center;

        // Every cell exactly as wide, whatever its text: the row splits evenly.
        LayoutElement cell = line.GetComponent<LayoutElement>();
        if (!cell) cell = line.gameObject.AddComponent<LayoutElement>();
        cell.minWidth = 0f;
        cell.preferredWidth = 0f;
        cell.flexibleWidth = 1f;
        return line;
    }

    /// <summary>
    /// Tells the cursor whether the pointer is on a line, so it shows its button icon there (the lines aren't
    /// Selectables, which it would find by itself).
    /// </summary>
    private void SetCursorOnLine(bool on)
    {
        if (cursorOnLine == on) return;
        cursorOnLine = on;
        if (CursorIconManager.Instance) CursorIconManager.Instance.SetButtonHover(on);
    }

    private static void PlaySfx(AudioClip clip)
    {
        if (clip && SFXManager.Instance) SFXManager.Instance.PlayClip(clip);
    }
}
