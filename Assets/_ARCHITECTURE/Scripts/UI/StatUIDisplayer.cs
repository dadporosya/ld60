using System;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine.InputSystem;

public class StatUIDisplayer : MonoBehaviour
{
    public StatUIShownBase _obj;

    public StatUIShownBase obj
    {
        get { return _obj; }
        set
        {
            _obj = value;
            UpdateStats();
        }
    }

    [Header("TMP")]
    [SerializeField] private float fontSize = 22f;
    public TMP_Text nameTmp;
    private ScalingText scalingName;
    public TMP_Text statLabelsTmp;
    public TMP_Text statValuesTmp;
    public TMP_Text additionalInfoTmp;

    [Header("Avatar")]
    public Image avatarImg;

    [Header("Background Scaling")]
    public Image backGroundImage;
    [SerializeField] private float bgTopPadding    = 16f;
    [SerializeField] private float bgBottomPadding = 16f;
    [SerializeField] private float sectionSpacing  = 20f;
    // Vertical offset from BG bottom at which AddInfo is anchored.
    // Must match the anchored position you set on the AddInfo RectTransform.
    [SerializeField] private float addInfoBottomOffset = 12f;

    // ─────────────────────────────────────────────────────────────

    public void Awake()
    {
        if (statLabelsTmp)    statLabelsTmp.fontSize    = fontSize;
        if (statValuesTmp)    statValuesTmp.fontSize    = fontSize;
        if (additionalInfoTmp) additionalInfoTmp.fontSize = fontSize;
        if (nameTmp)
            scalingName = nameTmp.GetComponent<ScalingText>();

        UpdateAll(obj);
    }

    // ── Scaling ──────────────────────────────────────────────────

    /// <summary>
    /// Resizes the background's height so every section fits.
    /// Pivot must be Top-Center; height grows downward.
    /// AdditionalInfo is anchored to the BG's bottom edge.
    /// </summary>
    public void ScaleBG()
    {
        if (!backGroundImage) return;
        RectTransform bgRect = backGroundImage.rectTransform;

        // Force TMP to recalculate preferred sizes before we read them.
        if (statLabelsTmp)    LayoutRebuilder.ForceRebuildLayoutImmediate(statLabelsTmp.rectTransform);
        if (statValuesTmp)    LayoutRebuilder.ForceRebuildLayoutImmediate(statValuesTmp.rectTransform);
        if (additionalInfoTmp) LayoutRebuilder.ForceRebuildLayoutImmediate(additionalInfoTmp.rectTransform);
        if (nameTmp)          LayoutRebuilder.ForceRebuildLayoutImmediate(nameTmp.rectTransform);

        float height = bgTopPadding;

        // 1. Avatar (fixed — read its current rect height)
        if (avatarImg)
            height += avatarImg.rectTransform.rect.height + sectionSpacing;

        // 2. Name
        if (nameTmp)
            height += nameTmp.preferredHeight + sectionSpacing;

        // 3. Stats — take the taller of the two columns
        if (statLabelsTmp || statValuesTmp)
        {
            float labH = statLabelsTmp  ? statLabelsTmp.preferredHeight  : 0f;
            float valH = statValuesTmp  ? statValuesTmp.preferredHeight  : 0f;
            height += Mathf.Max(labH, valH) + sectionSpacing;
        }

        // 4. Additional info sits at the BG bottom, so we need enough
        //    room below the stats block to hold it plus its bottom offset.
        if (additionalInfoTmp)
            height += additionalInfoTmp.preferredHeight + addInfoBottomOffset;

        height += bgBottomPadding;

        // Apply — only touch Y; keep the designer-set width.
        Vector2 sd = bgRect.sizeDelta;
        sd.y = height;
        bgRect.sizeDelta = sd;
    }

    // ── Update methods ───────────────────────────────────────────

    public void UpdateStats(StatUIShownBase objIn = null)
    {
        if (objIn) { UpdateAll(objIn); return; }
        if (!obj) return;
        if (!statLabelsTmp || !statValuesTmp) return;

        statLabelsTmp.text = "";
        statValuesTmp.text = "";

        List<ObjectsCountContainer> allData = obj.GetStats();
        int m = allData.Count;

        void AddLineBreak()
        {
            statLabelsTmp.text += "\n";
            statValuesTmp.text += "\n";
        }

        foreach (var data in allData)
        {
            m--;
            int n = data.values.Count;
            foreach (var kv in data.values)
            {
                n--;
                if (obj.ignoreKeys.Contains(kv.Key)) continue;
                
                string label = HTML.GenerateSpriteWithString(kv.Key);
                string value = kv.Value == 0 ? "—" : kv.Value.ToString();

                statLabelsTmp.text += label;
                statValuesTmp.text += value;

                if (n > 0) AddLineBreak();
            }
            if (m > 0) AddLineBreak();
        }
    }

    public void UpdateAdditionalInfo(StatUIShownBase objIn = null)
    {
        if (objIn) { UpdateAll(objIn); return; }
        if (!obj) return;
        if (!additionalInfoTmp) return;

        additionalInfoTmp.text = "";
        List<string> data = obj.GetAdditionalInfo();
        int n = data.Count;
        for (int i = 0; i < n; i++)
            additionalInfoTmp.text += data[i] + (i == n - 1 ? "" : "\n");
    }

    public void UpdateAvatar(StatUIShownBase objIn = null)
    {
        if (objIn) { UpdateAll(objIn); return; }
        if (!obj) return;
        if (!avatarImg) return;

        avatarImg.sprite   = obj.GetSprite();
        avatarImg.material = obj.GetMaterial();
    }

    public void UpdateName(StatUIShownBase objIn = null)
    {
        if (objIn) { UpdateAll(objIn); return; }
        if (!obj) return;
        if (!nameTmp) return;

        string data = obj.GetName();
        if (scalingName) { scalingName.SetText(data); return; }
        nameTmp.text = data;
    }

    public void UpdateAll(
        StatUIShownBase objIn          = null,
        bool scaleBg = true,
        bool            updAvatar      = true,
        bool            updStats       = true,
        bool            updAdditionalInfo = true,
        bool            updName        = true)
    {
        if (objIn) obj = objIn;
        if (!obj) return;

        if (updAvatar)          UpdateAvatar();
        if (updStats)           UpdateStats();
        if (updAdditionalInfo)  UpdateAdditionalInfo();
        if (updName)            UpdateName();

        // Rescale BG whenever any text content changes.
        if (scaleBg) ScaleBG();
    }

    private void FixedUpdate()
    {
        UpdateAll(updAvatar: false, updName: false, scaleBg:false);
    }
}