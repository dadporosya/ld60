using UnityEngine;
using TMPro;

public class ScalingText : MonoBehaviour
{
    [SerializeField] public TextMeshProUGUI _textComponent;
    [SerializeField] private RectTransform _textRectTransform;
    [SerializeField] private float _minFontSize = 1f;
    [SerializeField] private float _maxFontSize = 50f;

    private void Awake()
    {
        if (!_textComponent) _textComponent = GetComponent<TextMeshProUGUI>();
        if (!_textRectTransform) _textRectTransform = GetComponent<RectTransform>();
        _maxFontSize = _textComponent.fontSize;
    }

    public void SetText(string value)
    {
        _textComponent.text = value;
        ScaleTextToFit();
    }

    protected void OnRectTransformDimensionsChange()
    {
        ScaleTextToFit();
    }

    private void ScaleTextToFit()
    {
        if (!_textComponent || !_textRectTransform)
            return;
        
        float targetWidth = _textRectTransform.rect.width;
        float targetHeight = _textRectTransform.rect.height;
        float targetArea = targetWidth * targetHeight;

        float optimalSize = _textComponent.fontSize;;
        
        if (h.Area(_textComponent.GetPreferredValues(_textComponent.text)) > targetArea)
        {
            optimalSize = h.FindOptimalFontSize(
                _textComponent,
                _textRectTransform,
                _minFontSize,
                _maxFontSize
            );
        }
        
        // Set the final font size
        _textComponent.fontSize = optimalSize;
        _textComponent.ForceMeshUpdate();
    }
}