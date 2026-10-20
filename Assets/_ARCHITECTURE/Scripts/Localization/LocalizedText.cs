using TMPro;
using UnityEngine;

/// <summary>
/// Shows its English line, translated, in the TextMeshPro text beside it - now and on every language change.
///
/// <para>Labels that sit in a scene don't need it: <see cref="Loc"/> swaps any text that is a line of the
/// translation table by itself. Use it on prefabs spawned at runtime, or to keep the English line visible in the
/// inspector. The Translations window's scan picks its line up.</para>
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public class LocalizedText : MonoBehaviour
{
    [Tooltip("The English line. Empty: whatever the text component says.")]
    [SerializeField, TextArea, Localized] private string source;

    private TMP_Text label;

    /// <summary>The English line shown (translated); setting it updates the text.</summary>
    public string Source
    {
        get
        {
            if (!string.IsNullOrEmpty(source)) return source;
            TMP_Text text = label ? label : GetComponent<TMP_Text>();
            return text ? text.text : "";
        }
        set
        {
            source = value;
            Apply();
        }
    }

    private void Awake()
    {
        label = GetComponent<TMP_Text>();
        if (string.IsNullOrEmpty(source) && label) source = label.text;
    }

    private void OnEnable()
    {
        Loc.Changed += Apply;
        Apply();
    }

    private void OnDisable() => Loc.Changed -= Apply;

    public void Apply()
    {
        if (label) label.text = Loc.T(source);
    }
}
