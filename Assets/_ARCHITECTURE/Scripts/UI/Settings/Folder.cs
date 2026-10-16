using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One row of the Win XP style folder tree on the left of <see cref="SettingsWindowManager"/> — a
/// <see cref="TMP_Text"/> that behaves like a button.
///
/// <para><b>What it draws.</b> The label is always rebuilt as <c>iconTag + " " + folderName</c>, so the
/// sprite icon and the name are one piece of text. When the folder is the opened one it is drawn the way
/// XP draws a selected tree item: a blue band behind icon and name, white text.</para>
///
/// <para><b>The band.</b> With no <see cref="highlightImage"/> assigned the band is TMP's own
/// <c>&lt;mark&gt;</c> tag, so the prefab really can be nothing but a TMP_Text. It fills the name only —
/// <c>&lt;sprite&gt; &lt;mark&gt;name&lt;/mark&gt;</c> — leaving the icon unpainted, which is how XP
/// marks a tree item; <see cref="bandIncludesIcon"/> runs it under the icon as well. TMP paints the
/// mark quads over the glyphs rather than behind them, so the colour is faded to
/// <see cref="markAlpha"/> and the name stays readable through it. Assign an Image behind the text
/// instead when the band has to be opaque, or sized/padded by the layout rather than by the
/// glyphs.</para>
///
/// <para><b>The icon.</b> Icons are plain rich-text tags — normally a TMP <c>&lt;sprite&gt;</c> tag
/// resolved against the sprite asset on the label — so nothing here knows about a fixed icon set;
/// whatever is typed into <see cref="iconTag"/> is what gets drawn. <see cref="iconTag"/> is the
/// folder's normal (closed) icon and <see cref="openedIconTag"/> the one it swaps to while it is
/// selected. Leave <see cref="openedIconTag"/> empty for a folder given some other icon (audio, …) that
/// has no "opened" variant, and leave both empty for a folder drawn as a bare name.</para>
///
/// <para><b>Clicks.</b> Handled through <see cref="IPointerClickHandler"/>, so no Button component is
/// needed (the TMP_Text only has to be a raycast target). If the prefab does carry a
/// <see cref="Button"/>, that one is used instead and the pointer handler steps aside, so the click is
/// never counted twice.</para>
///
/// <para>Normally spawned and configured by the manager via <see cref="Setup"/>; the inspector values
/// are only what a hand-placed folder falls back to.</para>
/// </summary>
public class Folder : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Refs")]
    [Tooltip("The label. Left empty it is taken from this object or its children on Awake.")]
    public TMP_Text label;

    [Tooltip("Optional band behind the text. Empty = the selection band is drawn with TMP's <mark> tag, " +
             "which hugs the glyphs instead of filling the row.")]
    public Image highlightImage;

    [Header("Content")]
    [Tooltip("Name shown next to the icon. Overwritten by the manager's folderNames entry.")]
    public string folderName = "Folder";

    [Tooltip("Rich-text tag drawn in front of the name — normally a TMP <sprite> tag naming a sprite in " +
             "the sprite asset the label uses, e.g. <sprite name=\"folderClosed\">. Overwritten by the " +
             "manager's folderIconTags entry. Empty = no icon, just the name.")]
    public string iconTag = SettingsWindowManager.DefaultIconTag;

    [Tooltip("Tag drawn instead of 'iconTag' while this folder is the open one — the XP opened-folder " +
             "icon. Empty = the folder keeps its normal icon when opened, which is what an icon with no " +
             "'opened' variant (audio, …) wants.")]
    public string openedIconTag = SettingsWindowManager.DefaultOpenedIconTag;

    [Header("Look")]
    [Tooltip("Selection band colour. XP's #316AC5.")]
    public Color selectionColor = new Color(0.192f, 0.416f, 0.773f, 1f);

    [Tooltip("Band colour while hovered but not selected. Ignored when 'highlightOnHover' is off.")]
    public Color hoverColor = new Color(0.792f, 0.867f, 1f, 1f);

    [Tooltip("Text colour of the opened folder — white, on the blue band.")]
    public Color selectedTextColor = Color.white;

    [Tooltip("Text colour of every other folder.")]
    public Color normalTextColor = Color.black;

    [Tooltip("Draw the pale band under the pointer. Turn off for the plain XP tree look.")]
    public bool highlightOnHover = true;

    [Range(0f, 1f)]
    [Tooltip("Opacity of the <mark> band. TMP appends the mark quads AFTER the glyphs in the same mesh, " +
             "so the band is painted ON TOP of the text — at full alpha it hides it completely. Keep it " +
             "low enough to read through. Ignored when a highlightImage is used, since that Image really " +
             "does sit behind the text and may stay opaque.")]
    public float markAlpha = 0.35f;

    [Tooltip("Let the band run under the icon too. Off = it fills the name only, so the label reads " +
             "'<icon> <banded name>' — how XP marks a tree item. Ignored when a highlightImage is used, " +
             "since that Image sits behind the whole row.")]
    public bool bandIncludesIcon = false;

    [Header("Sound")]
    [Tooltip("One of these is played at random through the SFXManager when the folder is clicked.")]
    public List<AudioClip> clickSFX = new List<AudioClip>();

    /// <summary>Position in the manager's lists — the content window this folder opens.</summary>
    public int Index { get; private set; } = -1;

    /// <summary>True while this is the opened folder: blue band, white text, opened icon.</summary>
    public bool IsSelected { get; private set; }

    /// <summary>Clicks are ignored while this is off (the folder still draws normally).</summary>
    public bool interactable = true;

    /// <summary>Raised on a click, with this folder. The manager listens to it to open the content.</summary>
    public event Action<Folder> OnClicked;

    // Set when the prefab carries a Button: the pointer handler then leaves the click to it.
    private Button button;
    private bool hovered;

    // CursorIconManager.SetButtonHover is reference counted, so every true must be paired with a false.
    private bool cursorHoverRegistered;

    private void Awake()
    {
        ResolveRefs();

        if (TryGetComponent(out button)) button.onClick.AddListener(Click);
    }

    private void OnDestroy()
    {
        if (button) button.onClick.RemoveListener(Click);

        // A folder destroyed while hovered would otherwise leave the cursor stuck on the button icon.
        SetCursorButtonHover(false);
    }

    private void OnEnable()
    {
        // A folder re-shown with the pointer already elsewhere must not keep a stale hover band.
        hovered = false;
        Refresh();
    }

    private void OnDisable()
    {
        // Closing the settings window never fires OnPointerExit, so release the cursor here.
        hovered = false;
        SetCursorButtonHover(false);
    }

    private void ResolveRefs()
    {
        if (!label) label = GetComponent<TMP_Text>();
        if (!label) label = GetComponentInChildren<TMP_Text>(true);

        if (!label) h.Out($"{name}: Folder has no TMP_Text to write into");
    }

    // ================================================================ Setup

    /// <summary>
    /// Configures a freshly spawned folder. <paramref name="onClicked"/> is added to
    /// <see cref="OnClicked"/> rather than replacing it, so a folder can have more than one listener.
    /// </summary>
    public void Setup(int index, string folderName, string iconTag, string openedIconTag,
                      Action<Folder> onClicked = null)
    {
        ResolveRefs();

        Index = index;
        this.folderName = folderName;
        this.iconTag = iconTag;
        this.openedIconTag = openedIconTag;

        if (onClicked != null) OnClicked += onClicked;

        name = $"Folder_{index}_{folderName}";

        SetSelected(false);
    }

    /// <summary>Opens/closes this folder's look. The manager calls it on every folder when one is opened.</summary>
    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        Refresh();
    }

    /// <summary>Renames the folder and redraws it.</summary>
    public void SetName(string folderName)
    {
        this.folderName = folderName;
        Refresh();
    }

    // ================================================================ Drawing

    /// <summary>
    /// The icon tag actually drawn: <see cref="openedIconTag"/> while this is the open folder, and
    /// <see cref="iconTag"/> otherwise — including for a folder that was given no opened variant, since
    /// an icon like the audio one has none to swap to.
    /// </summary>
    public string CurrentIconTag =>
        (IsSelected && !string.IsNullOrEmpty(openedIconTag)) ? openedIconTag : iconTag;

    /// <summary>Rebuilds the label text (icon + name) and applies the band and text colours.</summary>
    public void Refresh()
    {
        if (!label) return;

        // Empty tag = a folder drawn as a bare name, so the space after the icon goes too.
        string tag = CurrentIconTag;
        string body = string.IsNullOrEmpty(tag) ? folderName : $"{tag} {folderName}";

        bool banded = IsSelected || (hovered && highlightOnHover);
        Color band = IsSelected ? selectionColor : hoverColor;

        if (highlightImage)
        {
            highlightImage.enabled = banded;
            highlightImage.color = band;
            label.text = body;

            // A real Image sits behind the glyphs, so XP's white-on-blue works.
            // Only the opened folder turns white — a hovered one keeps black text, as XP does.
            label.color = IsSelected ? selectedTextColor : normalTextColor;
        }
        else
        {
            label.text = BandedText(body, tag, banded, band);

            // The <mark> band is a translucent wash OVER the glyphs, so white text would dissolve into
            // it. The text keeps its normal colour and the blue tint alone marks the open folder.
            label.color = normalTextColor;
        }
    }

    /// <summary>
    /// The label with TMP's own <c>&lt;mark&gt;</c> band applied. With <see cref="bandIncludesIcon"/>
    /// off the icon tag and the space after it are left OUTSIDE the mark, so the colour starts at the
    /// first letter of the name and the icon keeps the pane behind it — the shape XP draws.
    ///
    /// <para>The band is faded to <see cref="markAlpha"/> first: TMP draws the mark quads over the
    /// glyphs, so an opaque colour would swallow the name.</para>
    /// </summary>
    private string BandedText(string body, string iconTag, bool banded, Color band)
    {
        if (!banded) return body;

        band.a *= Mathf.Clamp01(markAlpha);

        // 8-digit hex, because <mark> wants the alpha spelled out.
        string hex = ColorUtility.ToHtmlStringRGBA(band);

        if (bandIncludesIcon || string.IsNullOrEmpty(iconTag)) return $"<mark=#{hex}>{body}</mark>";

        return $"{iconTag} <mark=#{hex}>{folderName}</mark>";
    }

    // ================================================================ Input

    public void OnPointerClick(PointerEventData eventData)
    {
        if (button) return; // the Button component already routes this click to Click()

        Click();
    }

    /// <summary>Clicks the folder from code (or from a Button wired in the inspector).</summary>
    public void Click()
    {
        if (!interactable) return;

        PlayClickSound();
        OnClicked?.Invoke(this);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
        SetCursorButtonHover(true);
        Refresh();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        SetCursorButtonHover(false);
        Refresh();
    }

    /// <summary>
    /// Tells <see cref="CursorIconManager"/> the pointer is over a button, so the folder gets the same
    /// cursor a real button does — the hover icon, and the pressed icon while the mouse is held down
    /// (the manager swaps those itself). A folder is only a TMP_Text, so the manager's automatic
    /// <see cref="Selectable"/> detection would otherwise miss it. The count is reference based, so a
    /// true is only ever sent once and always paired with a false.
    /// </summary>
    private void SetCursorButtonHover(bool hovering)
    {
        if (hovering && (!interactable || !isActiveAndEnabled)) return;
        if (cursorHoverRegistered == hovering) return;

        cursorHoverRegistered = hovering;

        if (CursorIconManager.Instance) CursorIconManager.Instance.SetButtonHover(hovering);
    }

    private void PlayClickSound()
    {
        if (clickSFX == null || clickSFX.Count == 0) return;
        if (!SFXManager.Instance) return;

        SFXManager.Instance.PlayRandomClip(clickSFX);
    }
}
