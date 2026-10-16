using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PurchaseItemUIWindow : MonoBehaviour
{
    [SerializeField] private Image purchaseItemIcon;
    [SerializeField] private ScalingText titleText;
    [SerializeField] private ScalingText descriptionText;
    [SerializeField] private ScalingText rarityText;

    [SerializeField] private Button purchaseButton;

    private PurchaseManager purchaseManager;
    
    public PurchaseItemBase content;

    public void Init(PurchaseManager pm=null, PurchaseItemBase contentIn=null)
    {
        if (pm) purchaseManager = pm;
        if (!contentIn)
        {
            content = purchaseManager.GetRandomItem();
            // TODO rewrite somehow
        }
        else content = contentIn;

        // content = contentIn;
        
        if (!content) return;
        purchaseItemIcon.sprite = content.sprite;
        if (content.material) purchaseItemIcon.material = content.material;
        
        titleText.SetText(content.itemName);
        descriptionText.SetText(content.itemDescription);
        rarityText.SetText(content.rarity);
        
        
        
        // titleText._textComponent.text = content.itemName;
        // descriptionText._textComponent.text = content.itemDescription;
        // rarityText._textComponent.text = 
    }
    private void Start()
    {
        if (!purchaseManager) purchaseManager = FindFirstObjectByType<PurchaseManager>();
        
        Init(contentIn:content);
        
        if (!purchaseButton) purchaseButton = GetComponent<Button>();
        
        if (!content) return;
        // h.Out("assignbtn");
        if (purchaseButton) purchaseButton.onClick.AddListener(() =>
        {
            // purchaseManager.BuyItem(content);
        });
    }

    public void TestFunc()
    {
        h.Out("TestFunc");
    }
    
    
}
