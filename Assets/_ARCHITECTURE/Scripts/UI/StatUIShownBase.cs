using System.Collections.Generic;
using UnityEngine;

public class StatUIShownBase : MonoBehaviour
{
    public bool showByDefault = true;
    [SerializeField] private Sprite avatarSprite;
    [SerializeField] public string uiDisplayName;
    public Sprite AvatarSprite
    {
        get => avatarSprite;
        set => avatarSprite = value;
    }
    
    [SerializeField] private Material _material;
    public Material material
    {
        get => _material;
        set => _material = value;
    }
    
    public List<string> additionalInfo = new List<string>();
    public List<string> ignoreKeys = new List<string>();
    
    public virtual List<ObjectsCountContainer> GetStats() // have to be overwritten
    {
        return new List<ObjectsCountContainer>();
    }

    public virtual List<string> GetAdditionalInfo()
    {
        return additionalInfo;
    }

    public virtual Sprite GetSprite()
    {
        return AvatarSprite;
    }

    public virtual Material GetMaterial()
    {
        return _material;
    }

    public virtual string GetName()
    {
        return uiDisplayName;
    }
    
    protected void InitStatUIShownBase()
    {
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (!sr) return;
        if (!material) material = sr.material;
        if (!AvatarSprite) AvatarSprite = sr.sprite;
    }
}