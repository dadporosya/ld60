using System;
using UnityEngine;

public class UnitStatBar : Bar
{
    [Header("Stat bar settings")]
    [SerializeField] private P.Stat rawMainStat = P.Stat.health;
    [SerializeField] private P.Stat rawMaxStat = P.Stat.maxHealth;
    [HideInInspector] public string mainStat;
    [HideInInspector] public string maxStat;
    private float oldMaxValue;
    public Unit unit;
    
    [SerializeField] private GameObject model;
    [SerializeField] private bool disableOnMaxValue;
    
    public override void Awake()
    {
        if (!unit && transform.parent) unit = transform.parent.GetComponent<Unit>();
        if (!unit)
        {
            h.Out("no unit for health bar");
            return;
        }
        
        mainStat = rawMainStat.ToString();
        maxStat = rawMaxStat.ToString();
        
        oldMaxValue =  unit.GetStat(maxStat);
        
        if (disableOnMaxValue) onFullFill.AddListener(() =>
        {
            if (model) model.SetActive(false); 
        });
        
        // targetValue = unit.stats.values["health"];
        // currentMaxValue = unit.stats.values["maxHealth"];
        // startFill = -1;
        // enableStartFill = false;
        
        // h.Out("INIT HP BAR", targetValue, currentMaxValue);
        // base.Awake();
        InitBar();
        
        // unit.onTakeDamage.AddListener(SetStat);
        // unit.onHeal.AddListener(SetStat);
        // unit.onUpdStats.AddListener(_ => SetStat());
        
        unit.onChangeStat.AddListener(CheckForInitBar);
        unit.onSetStat.AddListener(CheckForInitBar);
    }

    private void Start()
    {
        InitBar();
    }

    private void CheckForInitBar(string stat)
    {
        if (stat != mainStat && stat != maxStat) return;
        UpdateStat();
    }

    public void UpdateStat()
    {
        if (disableOnMaxValue &&
            Mathf.Abs(unit.GetStat(mainStat) - unit.GetStat(maxStat)) > 0.001f)
        {
            if (model) model.SetActive(true);    
        }
        
        if (Mathf.Abs(oldMaxValue - unit.GetStat(maxStat)) > 0.001f)
        {
            oldMaxValue =  unit.GetStat(maxStat);
            InitBar();
        }
        SetStat();
        
    }
    
    public void InitBar()
    {
        Init(
            unit.GetStat(mainStat),
            unit.GetStat(maxStat)
        );
    }

    public void SetStat()
    {
        // h.Out(unit.GetStat(mainStat));
        SetValue(unit.GetStat(mainStat));
    }
}