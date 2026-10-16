using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

public class PurchaseManager : MonoBehaviour
{
    public StringDirector rarityDirector;
    public ScriptableObjectsKindsContainer data;
    // data: dict{"turrets": scrobjcontainer{dusted: scrobjcontainer, vanguard: scrobjcontainer, x-grade: scrobjcontainer}},
    // {"effects": scrobjcontainer{dusted: scrobjcontainer, vanguard: scrobjcontainer, x-grade: scrobjcontainer}}

    private List<PurchaseItemBase> currentChoices = new List<PurchaseItemBase>();

    [SerializeField]
    private int defaultItemsCount;

    public void GenerateChoices(float tokens)
    {
        if (!data || !rarityDirector)
        {
            ////h.Out("No data / rarity director for purchase items");;
            return;
        }

        currentChoices.Clear();

        // 1. Roll rarities against the available token budget.
        List<string> rarities = rarityDirector.ChooseSeveralPrefabs(3, tokens);
        if (rarities == null || rarities.Count == 0) return;

        // 2. Pick a type per rarity, guaranteeing each equipment type appears at least once.
        List<string> types = PickTypes(rarities.Count);
        
        //h.Out(rarities, types);
        // 3. For each (type, rarity) pair, pick a random concrete item from
        //    data.objects[type].objects[rarity].objects.
        for (int i = 0; i < rarities.Count; i++)
        {
            string rarity = rarities[i];
            string type = types[i];
            //h.Out(rarity, type);

            if (!data.objects.TryGetValue(type, out ScriptableObject typeObj)) continue;
            //h.Out("1", typeObj);
            if (typeObj is not ScriptableObjectsKindsContainer typeContainer) continue;
            //h.Out("2", typeContainer);
            
            if (!typeContainer.objects.TryGetValue(rarity, out ScriptableObject rarityObj)) continue;
            //h.Out("3", rarityObj);
            if (rarityObj is not ScriptableObjectsKindsContainer rarityContainer) continue;
            //h.Out("4", rarityContainer);
            if (rarityContainer.objects.Count == 0) continue;

            if (h.RandChoice(rarityContainer.objects) is not PurchaseItemBase item) continue;
            //h.Out("5", item);
            
            item.rarity = rarity;
            currentChoices.Add(item);
            //h.Out("add itemm");
        }
    }

    // Returns `count` equipment types where every type in P.equipmentTypes appears at
    // least once (when count allows), with the remaining slots filled randomly and shuffled.
    private List<string> PickTypes(int count)
    {
        List<string> types = new List<string>();

        foreach (string type in P.equipmentTypes)
        {
            if (types.Count >= count) break;
            types.Add(type);
        }

        while (types.Count < count) types.Add(h.RandChoice(P.equipmentTypes));

        for (int i = types.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (types[i], types[j]) = (types[j], types[i]);
        }

        return types;
    }
    public PurchaseItemBase GetRandomItem()
    {
        // if (currentChoices == null || currentChoices.Count == 0) GenerateChoices(shopTokens);
        if (currentChoices == null)
        {
            ////h.Out("No container init");
            return null;
        }
        
        var item = h.RandChoice(currentChoices);
        if (currentChoices.Count > defaultItemsCount)
        {
            currentChoices.Remove(item);
        }

        return item;
    }
}
