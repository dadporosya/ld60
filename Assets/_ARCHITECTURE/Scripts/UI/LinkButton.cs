using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to a GameObject with a Button component. On click, opens <see cref="url"/> in the default browser.
/// </summary>
[RequireComponent(typeof(Button))]
public class LinkButton : MonoBehaviour
{
    [Tooltip("URL to open in the browser when the button is clicked.")]
    public string url;

    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
    }

    private void OnEnable()
    {
        button.onClick.AddListener(OpenUrl);
    }

    private void OnDisable()
    {
        button.onClick.RemoveListener(OpenUrl);
    }

    public void OpenUrl()
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            h.Out($"{name}: LinkButton has no url set");
            return;
        }

        h.Out($"{name}: opening {url}");
        Application.OpenURL(url);
    }
}
