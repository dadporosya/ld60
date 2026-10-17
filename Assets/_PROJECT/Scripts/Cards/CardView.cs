using System;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Shows one card: title, description and art of its Hope or Hate side, plus that side's frame color /
/// decoration. Clickable when bound with a callback (the card pick); otherwise only displays.
/// </summary>
public class CardView : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private Image art;
    [Tooltip("Tinted Hope Color / Hate Color.")]
    [SerializeField] private Image frame;
    [SerializeField] private Color hopeColor = new Color(0.95f, 0.85f, 0.55f);
    [SerializeField] private Color hateColor = new Color(0.55f, 0.15f, 0.15f);
    [Tooltip("Optional: shown only on the Hope side.")]
    [SerializeField] private GameObject hopeDecor;
    [Tooltip("Optional: shown only on the Hate side.")]
    [SerializeField] private GameObject hateDecor;

    [Header("Hover (clickable cards only)")]
    [SerializeField, Min(0f)] private float hoverScale = 1.08f;
    [SerializeField, Min(0f)] private float hoverDuration = 0.12f;

    public CardInstance Card { get; private set; }

    private Action<CardView> onClick;
    private Vector3 restScale;
    private bool restScaleKnown;
    private Tween hoverTween;

    public void Bind(CardInstance card, Action<CardView> clicked = null)
    {
        Card = card;
        onClick = clicked;

        if (!restScaleKnown)
        {
            restScale = transform.localScale;
            restScaleKnown = true;
        }

        bool hope = card == null || card.mood == CardMood.Hope;
        if (titleText) titleText.text = card?.Title ?? "";
        if (descriptionText) descriptionText.text = card?.Description ?? "";
        if (art)
        {
            art.sprite = card?.Art;
            art.enabled = art.sprite;
        }
        if (frame) frame.color = hope ? hopeColor : hateColor;
        if (hopeDecor) hopeDecor.SetActive(hope);
        if (hateDecor) hateDecor.SetActive(!hope);
    }

    private void OnDisable()
    {
        hoverTween.Stop();
        if (restScaleKnown) transform.localScale = restScale;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        onClick?.Invoke(this);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (onClick != null) ScaleTo(hoverScale);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (onClick != null) ScaleTo(1f);
    }

    private void ScaleTo(float scale)
    {
        if (!restScaleKnown) return;
        hoverTween.Stop();
        if (hoverDuration > 0f) hoverTween = Tween.Scale(transform, restScale * scale, hoverDuration, Ease.OutQuad);
        else transform.localScale = restScale * scale;
    }
}
