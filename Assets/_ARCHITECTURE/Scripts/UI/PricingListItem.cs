using UnityEngine;
using TMPro;

public class PricingListItem : MonoBehaviour
{
    public SpriteRenderer spriteRenderer;
    public TextMeshProUGUI textUI;
    public Sprite sprite;
    public string text;
    
    public void Init()
    {
        if (sprite) ChangeIcon(sprite);
        if (text != "") ChangeText(text);
    }

    void Start()
    {
        Init();
    }

    public void ChangeIcon(Sprite newIcon)
    {
        spriteRenderer.sprite = newIcon;
    }

    public void ChangeText(string newText)
    {
        textUI.text = newText;
    }
}
