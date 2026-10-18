using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class TextAlertManager : MonoBehaviour
{
    public static TextAlertManager Instance;

    [Header("Damage Alert Settings")]
    [SerializeField] private AlertDamageText damageTextPfb;
    [SerializeField] private float holdDuration    = 0.6f;
    [SerializeField] private float fadeOutDuration = 0.4f;
    [SerializeField] private float speed           = 1f;
    [SerializeField] private float spread = 1f;
    [SerializeField] private Vector3 alertOffset = Vector3.zero;

    [Tooltip("Size of every alert, as a multiplier of the prefab's own scale. 1 = as authored.")]
    [Min(0f)] [SerializeField] private float alertScale = 1f;

    [Header("Text Alert Settings (CreateTextAlert)")]
    [Tooltip("Prefab for text popups. Empty: the damage prefab. A UI prefab (RectTransform) spawns under UI Parent.")]
    [SerializeField] private AlertDamageText textAlertPfb;
    [Tooltip("Where UI popups spawn. Empty: the root canvas of the anchor they pop up at.")]
    [SerializeField] private RectTransform uiParent;
    [SerializeField] private float textHoldDuration    = 0.9f;
    [SerializeField] private float textFadeOutDuration = 0.5f;
    [Tooltip("Rise speed, in the parent's units per second (canvas units for UI popups).")]
    [SerializeField] private float textSpeed = 60f;
    [Tooltip("Random offset around the anchor, in the parent's units.")]
    [SerializeField] private float textSpread = 12f;

    [Header("Overlap")]
    [Tooltip("Text alerts that would overlap are slid apart: the ones above a new alert move up, the ones below " +
             "move down. They all rise at the same speed, so once apart they stay apart.")]
    [SerializeField] private bool avoidOverlap = true;
    [Tooltip("Space kept between text alerts, in the parent's units.")]
    [SerializeField, Min(0f)] private float overlapGap = 6f;

    // Text alerts still on screen, for making room.
    private readonly List<AlertDamageText> liveTextAlerts = new List<AlertDamageText>();

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance);
    }

    public void CreateDamageAlert(int damage, Vector3 position, Color? color = null, string label = null, Vector3 offset = default)
    {
        AlertDamageText alert = Instantiate(
            damageTextPfb,
            position + alertOffset + offset + h.RandomPositionInCircle(spread),
            Quaternion.identity);
        // The root, not damageText: AlertDamageText tweens the text's own scale down to zero on fade.
        alert.transform.localScale *= alertScale;
        alert.damageText.text  = label != null ? $"{label}{damage}" : damage.ToString();
        alert.holdDuration    = holdDuration;
        alert.fadeOutDuration = fadeOutDuration;
        alert.speed           = speed;
        if (color.HasValue) alert.damageText.color = color.Value;
    }

    public void CreateDamageAlert(int damage, Transform target, Color? color = null, string label = null, Vector3 offset = default)
    {
        if (target != null)
            CreateDamageAlert(damage, target.position, color, label, offset);
    }

    /// <summary>
    /// Pops <paramref name="text"/> (rich text allowed) up at <paramref name="anchor"/>, rising and fading. A UI
    /// prefab spawns under <see cref="uiParent"/> (else the anchor's root canvas); <paramref name="offset"/> and
    /// the spread are in that parent's units. <paramref name="scale"/> sizes this alert on top of the alert scale.
    /// Alerts already up are slid out of its way (<see cref="avoidOverlap"/>).
    /// </summary>
    public AlertDamageText CreateTextAlert(string text, Transform anchor, Color? color = null, Vector2 offset = default,
        float scale = 1f)
    {
        AlertDamageText prefab = textAlertPfb ? textAlertPfb : damageTextPfb;
        if (!prefab || !anchor) return null;

        bool ui = prefab.transform is RectTransform;
        Transform parent = ui ? UIParentFor(anchor) : null;

        AlertDamageText alert = Instantiate(prefab, parent, false);
        alert.transform.position = anchor.position;
        Vector3 local = alert.transform.localPosition + (Vector3)(offset + Random.insideUnitCircle * textSpread);
        if (ui) local.z = 0f;
        alert.transform.localPosition = local;
        alert.transform.localScale *= alertScale * scale;

        alert.damageText.text = text;
        alert.holdDuration    = textHoldDuration;
        alert.fadeOutDuration = textFadeOutDuration;
        alert.speed           = textSpeed;
        if (color.HasValue) alert.damageText.color = color.Value;

        MakeRoom(alert);
        return alert;
    }

    // ---------------------------------------------------------------------
    // Overlap
    // ---------------------------------------------------------------------

    /// <summary>
    /// Slides the live text alerts that overlap <paramref name="alert"/> out of its way, stacking: the ones above it
    /// go up (lowest first, each clearing the ones under it), the ones below go down. The new alert stays where it
    /// popped up.
    /// </summary>
    private void MakeRoom(AlertDamageText alert)
    {
        liveTextAlerts.RemoveAll(a => !a);
        if (avoidOverlap && alert.damageText)
        {
            Transform space = alert.transform.parent;
            alert.damageText.ForceMeshUpdate();
            Rect placed = BoundsIn(alert, space);

            List<(AlertDamageText alert, Rect rect)> above = new List<(AlertDamageText, Rect)>();
            List<(AlertDamageText alert, Rect rect)> below = new List<(AlertDamageText, Rect)>();
            foreach (AlertDamageText other in liveTextAlerts)
            {
                if (other.transform.parent != space || !other.damageText) continue;
                Rect rect = BoundsIn(other, space);
                (rect.center.y >= placed.center.y ? above : below).Add((other, rect));
            }

            above.Sort((a, b) => a.rect.yMin.CompareTo(b.rect.yMin));
            below.Sort((a, b) => b.rect.yMax.CompareTo(a.rect.yMax));
            Stack(above, placed, up: true);
            Stack(below, placed, up: false);
        }

        liveTextAlerts.Add(alert);
    }

    /// <summary>Pushes each alert (nearest to <paramref name="placed"/> first) clear of the ones already settled.</summary>
    private void Stack(List<(AlertDamageText alert, Rect rect)> alerts, Rect placed, bool up)
    {
        List<Rect> settled = new List<Rect> { placed };
        foreach ((AlertDamageText alert, Rect rect) in alerts)
        {
            Rect moved = rect;
            float push = 0f;
            foreach (Rect other in settled)
            {
                if (moved.xMin >= other.xMax || other.xMin >= moved.xMax) continue;
                push = up
                    ? Mathf.Max(push, other.yMax + overlapGap - moved.yMin)
                    : Mathf.Max(push, moved.yMax - (other.yMin - overlapGap));
            }

            if (push > 0f)
            {
                alert.Push(up ? push : -push);
                moved.y += up ? push : -push;
            }
            settled.Add(moved);
        }
    }

    /// <summary>
    /// The alert's text in <paramref name="space"/>'s local units (world units without one), where it will be once
    /// the pushes it is still sliding by are done.
    /// </summary>
    private static Rect BoundsIn(AlertDamageText alert, Transform space)
    {
        TMP_Text text = alert.damageText;
        Bounds local = text.textBounds;
        Vector3 min = local.min, max = local.max;

        Vector2 lo = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 hi = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < 4; i++)
        {
            Vector3 corner = new Vector3(i % 2 == 0 ? min.x : max.x, i < 2 ? min.y : max.y, 0f);
            Vector3 world = text.transform.TransformPoint(corner);
            Vector2 p = space ? (Vector2)space.InverseTransformPoint(world) : (Vector2)world;
            lo = Vector2.Min(lo, p);
            hi = Vector2.Max(hi, p);
        }

        return new Rect(lo.x, lo.y + alert.PendingPush, hi.x - lo.x, hi.y - lo.y);
    }

    private Transform UIParentFor(Transform anchor)
    {
        if (uiParent) return uiParent;
        Canvas canvas = anchor.GetComponentInParent<Canvas>();
        return canvas ? canvas.rootCanvas.transform : anchor.parent;
    }
}
