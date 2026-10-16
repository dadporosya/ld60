using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class ScreenManager : MonoBehaviour
{
    public static ScreenManager Instance;

    [SerializeField] private Image fadeImage;

    private void Awake()
    {
        h.CreateStaticInstance(this, ref Instance);
        SetupFadeImage();
    }

    private void SetupFadeImage()
    {
        if (fadeImage) return;
        
        // Create Canvas
        GameObject canvasGO = new GameObject("FadeCanvas");
        canvasGO.transform.SetParent(transform);

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999; // always on top

        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        // Create Image
        GameObject imageGO = new GameObject("FadeImage");
        imageGO.transform.SetParent(canvasGO.transform, false);

        fadeImage = imageGO.AddComponent<Image>();
        fadeImage.color = new Color(0, 0, 0, 0); // fully transparent

        // Stretch to fill screen
        RectTransform rect = fadeImage.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        
    }

    // Fade from transparent → black
    public void FadeOut(float duration, System.Action onComplete = null)
    {
        StartCoroutine(FadeRoutine(0f, 1f, duration, onComplete));
    }

    // Fade from black → transparent
    public void FadeIn(float duration, System.Action onComplete = null)
    {
        StartCoroutine(FadeRoutine(1f, 0f, duration, onComplete));
    }

    public IEnumerator FadeRoutine(float from, float to, float duration, System.Action onComplete=null)
    {
        fadeImage.raycastTarget = true; // block clicks during fade

        float elapsed = 0f;
        Color color = fadeImage.color;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            color.a = Mathf.Lerp(from, to, elapsed / duration);
            fadeImage.color = color;
            yield return null;
        }

        color.a = to;
        fadeImage.color = color;

        fadeImage.raycastTarget = to > 0f; // only block if visible
        onComplete?.Invoke();
    }
}