using System;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Shows one card: its title (the CardData's good or bad title, on the same text), the description
/// (an upgraded card's points tinted <see cref="upgradedPointsColor"/>) and the icon in the middle - dice
/// sprite tags (<see cref="DiceIcons"/>) on a text. Clickable when bound with a callback (the card choice); otherwise it only
/// displays. Has a CanvasGroup (added if missing) so the <see cref="CardManager"/> can fade it.
///
/// <para>With <see cref="crop"/> on, the card's background is cut off just below where the description's
/// text ends (a top-down vertical fill), and so is its clickable area.</para>
/// </summary>
public class CardView : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Tooltip("Shows the card's good title after a good call, its bad title after a bad one (both set in the CardData).")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [Tooltip("The icon in the middle of the card: the dice the bonus is about, as sprite tags (or the CardData's own Icon text).")]
    [SerializeField] private TMP_Text icon;

    [Header("Crop")]
    [Tooltip("Cut the card's background off just below where the description's text ends.")]
    [SerializeField] private bool crop = true;
    [Tooltip("Card left below the description's last line when cropped (UI units).")]
    [SerializeField] private float cropPadding = 30f;
    [Tooltip("The card's background image that gets cropped. Empty: the Image on the card itself.")]
    [SerializeField] private Image background;

    [Header("Optional")]
    [Tooltip("Tinted Good Color / Bad Color.")]
    [SerializeField] private Image frame;
    [SerializeField] private Color goodColor = Color.white;
    [SerializeField] private Color badColor = new Color(0.75f, 0.55f, 0.55f);
    [Tooltip("Shown on permanent cards only.")]
    [SerializeField] private GameObject permanentBadge;
    [Tooltip("Shown on immediate cards only.")]
    [SerializeField] private GameObject immediateBadge;

    [Header("Upgrade")]
    [Tooltip("An upgraded card's points ({0} in the description) are tinted this.")]
    [SerializeField] private Color upgradedPointsColor = new Color(0.35f, 0.85f, 0.35f, 1f);
    [Tooltip("Optional: shown on upgraded cards only.")]
    [SerializeField] private GameObject upgradedBadge;

    [Header("Hover (clickable cards only)")]
    [SerializeField, Min(0f)] private float hoverScale = 1.08f;
    [SerializeField, Min(0f)] private float hoverDuration = 0.12f;

    public CardInstance Card { get; private set; }

    public CanvasGroup Group
    {
        get
        {
            if (!group) group = GetComponent<CanvasGroup>();
            if (!group) group = gameObject.AddComponent<CanvasGroup>();
            return group;
        }
    }

    public RectTransform Rect => (RectTransform)transform;

    /// <summary>Clicks are ignored while false (e.g. while the cards are still flying in).</summary>
    public bool Clickable { get; set; } = true;

    /// <summary>Grows on hover even without a click callback (kept cards, so the player can read them).</summary>
    public bool HoverOnly { get; set; }

    /// <summary>Hover scale for this card instead of the prefab's (0: the prefab's).</summary>
    public float HoverScaleOverride { get; set; }

    /// <summary>Moves to the end of its parent on hover so it's drawn on top. Off inside layout groups, where the order is the layout.</summary>
    public bool BringToFrontOnHover { get; set; }

    private CanvasGroup group;
    private Action<CardView> onClick;
    private Vector3 hoverBase = Vector3.one;
    private Tween hoverTween;
    private bool hovered;

    public void Bind(CardInstance card, Action<CardView> clicked = null)
    {
        Card = card;
        onClick = clicked;
        FindMissingReferences();

        bool good = card == null || card.mood == CardMood.Hope;
        if (titleText) titleText.text = card?.Title ?? "";

        if (descriptionText) descriptionText.text = card?.DescriptionWith(upgradedPointsColor) ?? "";
        if (icon)
        {
            icon.text = card?.Icon ?? "";
            icon.enabled = icon.text.Length > 0;
        }
        if (frame) frame.color = good ? goodColor : badColor;
        if (crop) Crop();

        bool permanent = card != null && card.IsPermanent;
        if (permanentBadge) permanentBadge.SetActive(permanent);
        if (immediateBadge) immediateBadge.SetActive(card != null && !permanent);
        if (upgradedBadge) upgradedBadge.SetActive(card != null && card.IsUpgraded);
    }

    /// <summary>
    /// Cuts the background off <see cref="cropPadding"/> below the description's last line: the image fills
    /// top-down only that far, and clicks below it miss the card.
    /// </summary>
    private void Crop()
    {
        if (!background || !descriptionText) return;

        RectTransform bgRect = background.rectTransform;
        Rect drawn = DrawnRect(background);

        // Where the text ends, in the background's space. No text: where the description's box starts.
        descriptionText.ForceMeshUpdate();
        RectTransform textRect = descriptionText.rectTransform;
        float textBottom = descriptionText.textInfo.characterCount > 0 ? descriptionText.textBounds.min.y : textRect.rect.yMax;
        float bottom = bgRect.InverseTransformPoint(textRect.TransformPoint(new Vector3(0f, textBottom, 0f))).y - cropPadding;

        background.type = Image.Type.Filled;
        background.fillMethod = Image.FillMethod.Vertical;
        background.fillOrigin = (int)Image.OriginVertical.Top;
        background.fillAmount = drawn.height > 0f ? Mathf.Clamp01((drawn.yMax - bottom) / drawn.height) : 1f;

        Vector4 padding = background.raycastPadding;
        padding.y = Mathf.Max(0f, Mathf.Max(bottom, drawn.yMin) - bgRect.rect.yMin);
        background.raycastPadding = padding;
    }

    /// <summary>The part of the image's rect its sprite is drawn in - smaller with Preserve Aspect, as Image does it.</summary>
    private static Rect DrawnRect(Image image)
    {
        RectTransform rect = image.rectTransform;
        Rect r = rect.rect;
        if (!image.preserveAspect || !image.sprite) return r;

        Vector2 size = image.sprite.rect.size;
        if (size.x <= 0f || size.y <= 0f) return r;

        float spriteRatio = size.x / size.y;
        if (spriteRatio > r.width / r.height)
        {
            float height = r.width / spriteRatio;
            r.y += (r.height - height) * rect.pivot.y;
            r.height = height;
        }
        return r;
    }

    private bool referencesChecked;

    /// <summary>
    /// Fills empty fields from the card's children by name: a text with "title" in its name, one with
    /// "desc", and one with "icon", "art", "image" or "picture"; the background is the card's own Image.
    /// Logs what it still can't find, once.
    /// </summary>
    private void FindMissingReferences()
    {
        if (referencesChecked) return;
        referencesChecked = true;

        TextMeshProUGUI[] texts = GetComponentsInChildren<TextMeshProUGUI>(true);
        if (!titleText) titleText = FindByName(texts, "title");
        if (!descriptionText) descriptionText = FindByName(texts, "desc");
        foreach (string part in new[] { "icon", "art", "image", "picture" })
        {
            if (icon) break;
            icon = FindByName(texts, part);
        }

        if (!background) background = GetComponent<Image>();

        string missing = (titleText ? "" : " Title Text") + (descriptionText ? "" : " Description Text") + (icon ? "" : " Icon") +
                         (background || !crop ? "" : " Background");
        if (missing.Length > 0)
            h.Out($"CardView '{name}': not assigned and not found by name:{missing}. Drag them into the CardView on the card prefab.");
    }

    private static T FindByName<T>(T[] candidates, string part) where T : Component
    {
        foreach (T candidate in candidates)
            if (candidate.name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) return candidate;
        return null;
    }

    /// <summary>The scale hover works from (the card's settled scale).</summary>
    public void SetHoverBase(Vector3 scale) => hoverBase = scale;

    /// <summary>Stops the hover and its tween (before something else animates the card).</summary>
    public void StopHover()
    {
        hovered = false;
        hoverTween.Stop();
    }

    private void OnDisable() => StopHover();

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (Clickable) onClick?.Invoke(this);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        bool canHover = HoverOnly || (onClick != null && Clickable);
        if (!canHover) return;

        hovered = true;
        // Drawn over its neighbours while it's bigger (order only - its position is set by whoever placed it).
        if (BringToFrontOnHover) transform.SetAsLastSibling();
        ScaleTo(HoverScaleOverride > 0f ? HoverScaleOverride : hoverScale);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!hovered) return;
        hovered = false;
        ScaleTo(1f);
    }

    private void ScaleTo(float scale)
    {
        hoverTween.Stop();
        if (hoverDuration > 0f)
            hoverTween = Tween.Scale(transform, hoverBase * scale, hoverDuration, Ease.OutQuad, useUnscaledTime: true);
        else transform.localScale = hoverBase * scale;
    }
}
