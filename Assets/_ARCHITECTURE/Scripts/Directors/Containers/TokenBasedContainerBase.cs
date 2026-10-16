using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class TokenBasedItemData<T>
{

    public T value;
    public float weight=1;
    public int cost=0;

    [Header("Auto Assigment")]
    public string id;
    public string name;
}

public abstract class TokenBasedContainerNonGenericBase : ScriptableObject
{
    public abstract void ClearNamesAndIds();
}

public class TokenBasedContainerBase<T>: TokenBasedContainerNonGenericBase
{
    public List<TokenBasedItemData<T>> items;

    public virtual void OnEnable()
    {
        // sort items by cost
        items.Sort((a, b) => a.cost.CompareTo(b.cost));
        foreach (var item in items)
        {
            if (item == null) continue;

            string itemFormalNameidk = item.value.ToString();
            bool isNameEmpty = string.IsNullOrEmpty(itemFormalNameidk);

            if (isNameEmpty) itemFormalNameidk = "";

            if (string.IsNullOrEmpty(item.name) && !isNameEmpty)
                item.name = itemFormalNameidk;

            if ((string.IsNullOrEmpty(item.id) || item.id == "null") && !isNameEmpty)
                item.id = itemFormalNameidk.ToLower().Replace(" ", "_");
        }
    }

    public override void ClearNamesAndIds()
    {
        if (items == null) return;
        foreach (var item in items)
        {
            if (item == null) continue;
            item.name = "";
            item.id = "";
        }
    }
}

public interface IConsumeAllTokens
{
    public float tokensCount { get; set; }
}




