using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The last frame before the game closes: <see cref="text"/> on black, all at once and shaking. <see cref="DeathCutscene"/> and
/// <see cref="GoodEndingCutscene"/> play it through <see cref="Play"/> and close the game once it's over
/// (<see cref="duration"/>). Everything runs on unscaled time.
///
/// <para>The frame goes on its own Screen Space - Camera canvas (<see cref="sortingOrder"/>), made under the
/// cutscene so it goes with it. Drawn through the camera, the post-processing volumes apply to it like the rest
/// of the UI.</para>
/// </summary>
[Serializable]
public class CreditsFrame
{
    [Tooltip("Off: the game closes straight away.")]
    public bool show = true;
    [TextArea] public string text = "Game by II-P";
    [Tooltip("Seconds to wait before the frame comes up; the screen stays as the cutscene left it. Not part of Duration.")]
    [Min(0f)] public float delay = 1.5f;
    [Tooltip("Seconds from the frame coming up to the game closing.")]
    [Min(0f)] public float duration = 6.7f;

    [Header("Text animation")]
    [Tooltip("Shake on the whole line (TextAnimation). 0: no shake.")]
    [Min(0f)] public float shakeStrength = 3f;
    [Min(0f)] public float shakeSpeed = 25f;

    [Header("Look")]
    [Tooltip("Empty: TextMeshPro's default font.")]
    public TMP_FontAsset font;
    [Min(1f)] public float fontSize = 72f;
    public Color color = Color.white;
    [Tooltip("Sorting order of the frame's runtime canvas. Keep it above everything else.")]
    public int sortingOrder = 2000;
    [Tooltip("The camera the frame's Screen Space - Camera canvas renders through, so post-processing applies to it. " +
             "Empty: the main camera. No camera at all: an overlay canvas, unprocessed.")]
    public Camera renderCamera;
    [Tooltip("How far in front of the camera the frame's canvas sits. Keep it closer than the other " +
             "Screen Space - Camera canvases.")]
    [Min(0.01f)] public float planeDistance = 1f;

    /// <summary>
    /// Waits <see cref="delay"/>, shows the frame under <paramref name="parent"/> and waits <see cref="duration"/>;
    /// nothing when <see cref="show"/> is off.
    /// </summary>
    public IEnumerator Play(Transform parent)
    {
        if (!show) yield break;
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

        Make(parent);
        if (duration > 0f) yield return new WaitForSecondsRealtime(duration);
    }

    private void Make(Transform parent)
    {
        GameObject go = new GameObject("Credits Canvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(parent, false);

        Canvas canvas = go.GetComponent<Canvas>();
        Camera cam = renderCamera ? renderCamera : Camera.main;
        if (cam)
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = Mathf.Max(planeDistance, cam.nearClipPlane + 0.01f);
        }
        else
        {
            h.Out("CreditsFrame: no camera, the frame is drawn as an overlay without post-processing.");
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Blocks clicks into whatever is behind it, too.
        GameObject black = new GameObject("Black", typeof(RectTransform), typeof(Image));
        black.transform.SetParent(go.transform, false);
        Stretch((RectTransform)black.transform, Vector2.zero);
        black.GetComponent<Image>().color = Color.black;

        GameObject textObject = new GameObject("Text", typeof(RectTransform));
        textObject.transform.SetParent(go.transform, false);
        Stretch((RectTransform)textObject.transform, new Vector2(-200f, -200f));
        TextMeshProUGUI label = textObject.AddComponent<TextMeshProUGUI>();
        if (font) label.font = font;
        label.fontSize = fontSize;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        label.text = text;

        TextAnimation animation = textObject.AddComponent<TextAnimation>();
        animation.animationTarget = TextAnimation.AnimationTarget.WholeText;
        animation.animations = new List<TextAnimation.AnimationType> { TextAnimation.AnimationType.Shake };
        animation.shakeStrength = shakeStrength;
        animation.shakeSpeed = shakeSpeed;
    }

    private static void Stretch(RectTransform rect, Vector2 sizeDelta)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = sizeDelta;
    }
}
