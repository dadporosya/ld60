using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class PricingListViewer : MonoBehaviour
{
    public ObjectsCountContainer data=null;
    public PricingListItem listItemPrefab;
    public List<PricingListItem> items;
    [SerializeField] private float gap=0;

    void Start()
    {
        // UpdatePriceList();
        MoveToParentsMidTop();
    }
    public void UpdatePriceList(ObjectsCountContainer newData=null)
    {
        if (newData)
        {
            // //h.Out("data: ", data, "nedata: ", newData);
            data = Instantiate(newData);
            data.Init(newData);
            //h.Out("data upd: ", data);
        }
        DestroyAllItems();

        if (!data) return;
        //////h.Out("UpdatePriceList. data: "); 
        // data.Show();
        foreach (KeyValuePair<string, float> pair in data.values)
        {
            //h.Out($"CreateList item: {pair.Key}, {pair.Value}");
            CreateLI(pair.Key, pair.Value); 
        }
        //////h.Out("Pricing Items");
        // ////h.Out(items.Select(i => i.gameObject.name).ToList());

        ArrangeItems();
        //h.Out("--------END--------");
    }

    public void CreateLI(string key, float value)
    {
        if (value <= 0) return;
        if (!transform.gameObject.scene.IsValid()) return; // mb problems
        //h.Out($"OK GOOOOOD: {key}, {value}");
        PricingListItem li;
        li = Instantiate(listItemPrefab.gameObject, transform).GetComponent<PricingListItem>();
        // //////h.Out(data.data.objects.ContainsKey(key));

        GameObject template = data.data.objects[key]; // get go from KindsContainer
        // //////h.Out(currentItem.name);

        li.sprite = template.GetComponent<SpriteRenderer>().sprite;
        li.text = value.ToString("0.##");
        li.Init();
        items.Add(li);
    }
    public void DestroyAllItems()
    {
        foreach (PricingListItem item in items)
        {
            if (item) Destroy(item.gameObject);
        }
        items.Clear();
    }

    public void ArrangeItems()
    {
        RectTransform rtOrig = listItemPrefab.GetComponent<RectTransform>();
        float itemHeight = rtOrig.rect.height * rtOrig.localScale.y + gap;

        for (int i = 0; i < items.Count; i++)
        {
            RectTransform rt = items[i].GetComponent<RectTransform>();

            rt.anchoredPosition = new Vector2(
                0,
                itemHeight * i
            );
        }
    }

    public void MoveToParentsMidTop()
    {
        SpriteRenderer parentSprite = transform.parent.GetComponent<SpriteRenderer>();
        float topY = parentSprite.sprite.bounds.extents.y;
        transform.localPosition = new Vector3(0, topY, 0);
        transform.rotation = Quaternion.identity;
    }
}
