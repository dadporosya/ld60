using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Plays the shake animation(s) of an <see cref="AnimationControllerBase"/> while the attached
/// <see cref="Slider"/>'s handle is pressed or dragged. Put this on the same GameObject as the
/// Slider; the handle's press/drag is routed to the Slider, so the pointer/drag handlers fire here.
///
/// Only <see cref="ShakeAnimation"/> components are driven. This is deliberate: a co-located
/// FloatingAnimation (or any other animation) is left running, so pressing the handle no longer
/// stops it and snaps the whole object back to its initial position. Shake and float both apply
/// their offset additively to localPosition, so they stack cleanly when the float is left alone.
/// </summary>
[RequireComponent(typeof(Slider))]
public class SliderDragAnimation : MonoBehaviour,
    IPointerDownHandler,
    IPointerUpHandler,
    IBeginDragHandler,
    IEndDragHandler
{
    [Tooltip("Controller whose shake animation(s) play while the handle is pressed or dragged. " +
             "Only ShakeAnimation components are driven, so a floating animation on the same object keeps running.")]
    public AnimationControllerBase dragingAnimController;

    [Tooltip("Stop the shake when the handle is released. When false the shake runs to its natural end.")]
    [SerializeField] private bool stopOnRelease = true;

    // The shake animations under the controller. Collected directly (not via the controller's own
    // filtered list) so we only ever touch shakes and never the floating animation.
    private readonly List<ShakeAnimation> _shakes = new List<ShakeAnimation>();

    // True while a press/drag interaction is active, so pressing then dragging doesn't restart it.
    private bool _playing;

    private void Start()
    {
        if (dragingAnimController) dragingAnimController.GetComponentsInChildren(true, _shakes);
    }

    public void OnPointerDown(PointerEventData eventData) => Play();
    public void OnBeginDrag(PointerEventData eventData) => Play();

    public void OnPointerUp(PointerEventData eventData) => StopIfNeeded();
    public void OnEndDrag(PointerEventData eventData) => StopIfNeeded();

    private void Play()
    {
        if (_playing) return;
        _playing = true;
        foreach (ShakeAnimation shake in _shakes)
            if (shake) StartCoroutine(shake.Play());
    }

    private void StopIfNeeded()
    {
        if (!_playing) return;
        _playing = false;
        if (!stopOnRelease) return;
        foreach (ShakeAnimation shake in _shakes)
            if (shake) shake.Stop();
    }
}
