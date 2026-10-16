using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Animates a UI element while the pointer is over it, and puts it back when it leaves: scales it up in real time, and,
/// if a <see cref="TextAnimation"/> is assigned, adds <see cref="textAnimations"/> to its whole-text animations for the
/// duration of the hover. On a <see cref="Selectable"/> (e.g. a <see cref="Button"/>) that isn't interactable, the
/// hover is ignored, and one that stops being interactable while hovered lets go. The <see cref="EndingMenu"/> adds one
/// to its play again button, and each <see cref="EndingWindow"/> to its Hint? button, if the button doesn't have one.
/// </summary>
public class OnHoverAnimation : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Scale")]
    [SerializeField, Min(0f)] private float hoverScale = 1.08f;
    [SerializeField, Min(0f)] private float duration = 0.15f;
    [SerializeField] private Ease ease = Ease.OutQuad;

    [Header("Text Animation")]
    [Tooltip("Text played with Text Animations while hovered. Empty: no text animation.")]
    [SerializeField] private TextAnimation textAnimation;
    [Tooltip("Added to the Text Animation's own whole-text animations while hovered.")]
    [SerializeField] private List<TextAnimation.AnimationType> textAnimations = new();

    [Tooltip("Hover ignored while this isn't interactable. Empty: the Selectable on this object, if any.")]
    [SerializeField] private Selectable selectable;

    private Vector3 _restScale;
    private bool _hovered;
    private Tween _tween;

    // The Text Animation's own settings, restored when the hover ends.
    private List<TextAnimation.AnimationType> _restTextAnimations;
    private TextAnimation.AnimationTarget _restTextTarget;

    private void Awake()
    {
        _restScale = transform.localScale;
        if (!selectable) selectable = GetComponent<Selectable>();
    }

    private void OnDisable()
    {
        // Back to rest straight away, so the element isn't left animated when it comes back.
        _tween.Stop();
        if (_hovered) StopTextAnimations();
        _hovered = false;
        transform.localScale = _restScale;
    }

    private void Update()
    {
        // Let go if the selectable turns non-interactable under the pointer (e.g. a button disabled by its own click).
        if (_hovered && selectable && !selectable.IsInteractable()) SetHovered(false);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!selectable || selectable.IsInteractable()) SetHovered(true);
    }

    public void OnPointerExit(PointerEventData eventData) => SetHovered(false);

    private void SetHovered(bool hovered)
    {
        if (_hovered == hovered) return;
        _hovered = hovered;

        _tween.Stop();
        _tween = Tween.Scale(transform, hovered ? _restScale * hoverScale : _restScale, duration, ease,
            useUnscaledTime: true);

        if (hovered) StartTextAnimations();
        else StopTextAnimations();
    }

    private void StartTextAnimations()
    {
        if (!textAnimation || textAnimations.Count == 0) return;

        _restTextAnimations = new List<TextAnimation.AnimationType>(textAnimation.animations);
        _restTextTarget = textAnimation.animationTarget;

        foreach (var animationType in textAnimations)
            if (!textAnimation.animations.Contains(animationType)) textAnimation.animations.Add(animationType);

        // Whole-text animations only play under these targets; keep any tag animations the text already had.
        textAnimation.animationTarget = _restTextTarget switch
        {
            TextAnimation.AnimationTarget.None => TextAnimation.AnimationTarget.WholeText,
            TextAnimation.AnimationTarget.Tags => TextAnimation.AnimationTarget.Both,
            _ => _restTextTarget,
        };
    }

    private void StopTextAnimations()
    {
        if (!textAnimation || _restTextAnimations == null) return;

        // TextAnimation rebuilds the mesh from its clean base every frame, so the text settles back on its own.
        textAnimation.animations = _restTextAnimations;
        textAnimation.animationTarget = _restTextTarget;
        _restTextAnimations = null;
    }
}
