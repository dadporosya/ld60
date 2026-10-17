using System;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Shows one card: its title (the CardData's good or bad title, on the same text), the description
/// and the picture in the middle. Clickable when bound with a callback (the card choice); otherwise it only
/// displays. Has a CanvasGroup (added if missing) so the <see cref="CardManager"/> can fade it.
/// </summary>
public class CardView : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Tooltip("Shows the card's good title after a good call, its bad title after a bad one (both set in the CardData).")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [Tooltip("The picture in the middle of the card.")]
    [SerializeField] private Image art;

    [Header("Optional")]
    [Tooltip("Tinted Good Color / Bad Color.")]
    [SerializeField] private Image frame;
    [SerializeField] private Color goodColor = Color.white;
    [SerializeField] private Color badColor = new Color(0.75f, 0.55f, 0.55f);
    [Tooltip("Shown on permanent cards only.")]
    [SerializeField] private GameObject permanentBadge;
    [Tooltip("Shown on immediate cards only.")]
    [SerializeField] private GameObject immediateBadge;

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

    private CanvasGroup group;
    private Action<CardView> onClick;
    private Vector3 hoverBase = Vector3.one;
    private Tween hoverTween;
    private bool hovered;

    public void Bind(CardInstance card, Action<CardView> clicked = null)
    {
        Card = card;
        onClick = clicked;

        bool good = card == null || card.mood == CardMood.Hope;
        if (titleText) titleText.text = card?.Title ?? "";

        if (descriptionText) descriptionText.text = card?.Description ?? "";
        if (art)
        {
            art.sprite = card?.Art;
            art.enabled = art.sprite;
        }
        if (frame) frame.color = good ? goodColor : badColor;

        bool permanent = card != null && card.IsPermanent;
        if (permanentBadge) permanentBadge.SetActive(permanent);
        if (immediateBadge) immediateBadge.SetActive(card != null && !permanent);
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
        if (onClick == null || !Clickable) return;
        hovered = true;
        ScaleTo(hoverScale);
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
