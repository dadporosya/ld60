using PrimeTween;
using UnityEngine;

/// <summary>
/// A room lighting rig (<see cref="RoomAmbientLight"/> + <see cref="RoomLight"/>s) laid over the visual novel stage,
/// shown only while a VN dialogue plays: it fades in when the dialogue starts and out when it ends.
/// It also copies the stage picture's position and scale, so the lights stay on the walls during VN zoom effects.
///
/// <para>Put it on the rig's root, a child of the stage AFTER the picture images and BEFORE the Overlay, so fades to
/// black cover the lights too. Built by Tools > Room Lighting > Create VN Lighting (Match Hub).</para>
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class VisualNovelRoomLighting : MonoBehaviour
{
    [Tooltip("Dialogue whose playback turns the lights on. Defaults to the first VisualNovelTalkable in the scene.")]
    [SerializeField] private VisualNovelTalkable talkable;
    [Tooltip("Off: the lights stay on whenever the stage is active.")]
    [SerializeField] private bool onlyWhileDialogue = true;
    [Tooltip("Stage picture the lights sit on; its position and scale (VN zoom) are copied onto the rig. " +
             "Must cover the same rect as the rig. Empty: the rig stays put.")]
    [SerializeField] private RectTransform followTarget;
    [SerializeField, Min(0f)] private float fadeDuration = 0.4f;

    private CanvasGroup group;
    private bool shown;
    private Tween fade;

    private void Awake()
    {
        group = GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        if (!talkable) talkable = FindFirstObjectByType<VisualNovelTalkable>(FindObjectsInactive.Include);

        shown = ShouldShow();
        group.alpha = shown ? 1f : 0f;
    }

    private void OnDestroy()
    {
        fade.Stop();
    }

    private void LateUpdate()
    {
        bool show = ShouldShow();
        if (show != shown)
        {
            shown = show;
            fade.Stop();
            float target = show ? 1f : 0f;
            if (fadeDuration <= 0f) group.alpha = target;
            else fade = Tween.Alpha(group, target, fadeDuration, Ease.InOutSine);
        }

        if (followTarget)
        {
            transform.localPosition = followTarget.localPosition;
            transform.localScale = followTarget.localScale;
        }
    }

    private bool ShouldShow() => !onlyWhileDialogue || (talkable && talkable.IsPlaying);
}
