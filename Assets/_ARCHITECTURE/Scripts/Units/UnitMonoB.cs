using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
// using Runtime;
using UnityEngine;
using UnityEngine.Events;

public class Unit : StatUIShownBase, IDamageable
{
    [Header("Unit settings")]
    public string unitName;
    public string displayedName;
    public GameObject model;
    public enum State { active, dead, onSale }
    [HideInInspector] public State state = State.active;
    
    public ObjectsCountContainer baseStats;
    [SerializeField] private bool initCustomStats = false;
    public ObjectsCountContainer stats;
    
    // [SerializeField] public List<string> additionalNonLevelStats = new List<string>();
    // [SerializeField] public List<string> exclusiveNonLevelStats = new List<string>();
    

    public P.Team team;

    [HideInInspector] public TeamContainer teamContainer;
    public List<string> additionalEnemyTags =  new List<string>();
    public List<string> additionalEnemyParentTags =  new List<string>();
    
    // todo mb create countContainer for separate lvl mults

    public ObjectsCountContainer resources;


    Rigidbody2D rb;
    Collider2D collider2d;

    [SerializeField] private bool immortal = false;
    
    [SerializeField] private List<P.EquipmentType> equipmentSlotTypeRaw = new  List<P.EquipmentType>();
    [SerializeField] private List<int> equipmentSlotCountRaw = new List<int>();

    public Dictionary<P.EquipmentType, int> equipmentSlotsMaxCount = new Dictionary<P.EquipmentType, int>();
    public Dictionary<P.EquipmentType, int> equipmentSlotsCurrentCount = new Dictionary<P.EquipmentType, int>();

    [HideInInspector] public Dictionary<P.EquipmentType, List<EquipmentDNDSlot>> equipmentSlots = new Dictionary<P.EquipmentType, List<EquipmentDNDSlot>>();
    
    // public List<EquipmentBase> equipments = new List<EquipmentBase>();
    [HideInInspector] public Dictionary<P.EquipmentType, List<EquipmentControllerBase>> equipments =  new Dictionary<P.EquipmentType, List<EquipmentControllerBase>>();
    [SerializeField] private List<EquipmentBase> startEquipment = new List<EquipmentBase>();
    
    public UnityEvent onTakeDamage;
    public UnityEvent onHeal;
    public UnityEvent<string> onUpdStats;
    public UnityEvent<string> onChangeStat;
    public UnityEvent<string> onSetStat;

    // Key of the stat most recently touched by ChangeStat / SetStat.
    // Listeners read this to decide whether the change is relevant to them.
    [HideInInspector] public string lastChangedStatKey;

    private OutlineController outLineController;

    public UnityEvent onDeath;

    // Coroutine factories run (all at once) when the unit dies, before it is destroyed.
    // Register death animations here (e.g. PixelationDeathAnimation). Each returns a fresh
    // IEnumerator; Die() waits for every one of them to finish before finalizing death.
    [HideInInspector] public List<Func<IEnumerator>> coroutinesBeforeDeath = new List<Func<IEnumerator>>();
    private bool _dying = false;

    // Death animation controller (type == Death), located in Awake among all child controllers.
    // Its PlayAnimations coroutine runs alongside coroutinesBeforeDeath when the unit dies.
    private AnimationControllerBase deathAnimationController;

    [HideInInspector] public int stunStack = 0;
    public virtual void Init()
    {
        Awake();
        Start();
    }
    
    private void Awake()
    {
        if (unitName == "") unitName = name;
        if (displayedName == "") displayedName = unitName;
        if (uiDisplayName == "") uiDisplayName = displayedName;
        
        if (baseStats) baseStats = Instantiate(baseStats);
        if (stats || baseStats) stats = Instantiate(initCustomStats ? stats : baseStats);

        if (resources) resources = Instantiate(resources);

        h.AssignComponent(this, ref rb);
        h.AssignComponent(this, ref collider2d);
        
        if (!model)
            model = h.FindChildWithTag(transform, "Model"); //
        if (!model) model = gameObject;
        SpriteRenderer thisSr = model.GetComponent<SpriteRenderer>();
        if (!thisSr) thisSr = model.GetComponentInChildren<SpriteRenderer>();
        if (thisSr) AvatarSprite = thisSr.sprite;
        if (!AvatarSprite) AvatarSprite = GetComponentInChildren<SpriteRenderer>()?.sprite;

        if (displayedName=="") displayedName = gameObject.name;
        
        // h.Out(P.GetTeamContainerByTeam(team));
        teamContainer = P.GetTeamContainerByTeam(team).Copy();
        teamContainer.enemyTags.AddRange(additionalEnemyTags);
        teamContainer.enemyParentTags.AddRange(additionalEnemyParentTags);
        
        // create equipment slot count configuration
        if (equipmentSlotTypeRaw == null || equipmentSlotTypeRaw.Count == 0)
        {
            equipmentSlotTypeRaw = new List<P.EquipmentType>
            {
                P.EquipmentType.Turret, P.EquipmentType.Effect
            };
        }
        
        if (equipmentSlotCountRaw.Count != equipmentSlotTypeRaw.Count)
        {
            equipmentSlotCountRaw.Clear();
            equipmentSlotCountRaw = h.CreateList(equipmentSlotTypeRaw.Count, 0);
        }
        
        // h.Out(equipmentSlotTypeRaw, equipmentSlotCountRaw);
        
        
        for (int i = 0; i < equipmentSlotTypeRaw.Count; i++)
        {
            equipmentSlotsMaxCount.Add(equipmentSlotTypeRaw[i], equipmentSlotCountRaw[i]);
            equipmentSlotsCurrentCount.Add(equipmentSlotTypeRaw[i], 0);
        }
        
        // h.Out(equipmentSlotsMaxCount, equipmentSlotsCurrentCount);
        //
        // h.Out("START " + name);
        // h.Out(equipments, equipmentSlotsMaxCount);
        foreach (P.EquipmentType equipmentType in equipmentSlotsMaxCount.Keys)
        {
            // h.Out(equipmentType);
            equipments.Add(equipmentType, new List<EquipmentControllerBase>());
        }
        
        // h.Out("equipments", equipments);
        
        // INIT start equipment
        for (int i = 0; i < startEquipment.Count; i++)
        {
            EquipmentBase equipmentBase = startEquipment[i];
            if (!equipmentBase) continue;

            EquipmentControllerBase controller = ScriptableObject.CreateInstance<EquipmentControllerBase>();
            controller.prefabs.Add(equipmentBase);
            controller.type = equipmentBase.equipmentType;
            controller = ScriptableObject.Instantiate(controller);
            controller.Init(gameObject);
        }
        
        InitStatUIShownBase();
        
        // OutLineController
        OutlineController outlinectr = GetComponent<OutlineController>();
        if (outlinectr == null)
        {
            outlinectr = gameObject.AddComponent<OutlineController>();
        }
        outLineController = outlinectr;
        outLineController.SetEnabled(false);

        // Find the death animation controller among children (mirrors AttackingObject's lookup).
        foreach (AnimationControllerBase controller in GetComponentsInChildren<AnimationControllerBase>(true))
        {
            if (controller.type == AnimationPreferences.Type.Death)
            {
                deathAnimationController = controller;
                break;
            }
        }

        // h.Out(stats.values);
    }

    protected virtual void Start()
    {
        // Start, not Awake: OnHitAnimation adds a second MasterMaterialController to the model
        // child from its own Awake, and Awake order between components is undefined. By Start
        // every controller on the unit exists, so all of them get the spawn settings.
        SpawnMaterialDefaults.Apply(gameObject);
    }

    public void UpdateStats(int lvl=-1)
    {
        // Level-based stat scaling removed: it required the GlobalUnitManager /
        // GameProgressionManager scripts, which are not part of this project.
        var allEffects = GetComponentsInChildren<StatusEffectBase>();

        foreach (var effect in allEffects)
            effect.OnUpdateStats();

        onUpdStats?.Invoke(string.Empty);
    }

    
    /// <summary>
    /// Entry point for killing a unit. Marks it dead, runs every registered
    /// <see cref="coroutinesBeforeDeath"/> simultaneously, waits for all of them to finish,
    /// then finalizes death via <see cref="OnDeath"/>. Call this instead of <see cref="OnDeath"/>.
    /// </summary>
    public void Die()
    {
        if (_dying) return;
        _dying = true;
        state = State.dead;

        // Remove the stat bar immediately so it doesn't linger through the death animation.
        UnitStatBar statBar = GetComponentInChildren<UnitStatBar>(true);
        if (statBar) Destroy(statBar.gameObject);

        // Stop every animation controller except the death one, so nothing fights the death anim.
        foreach (AnimationControllerBase controller in GetComponentsInChildren<AnimationControllerBase>(true))
        {
            if (controller.type != AnimationPreferences.Type.Death)
                controller.StopAnimations();
        }

        // Immediate death bookkeeping (detach from tiles/teams/counts), so the unit leaves the
        // world state right away rather than waiting for the death animation to finish.
        OnDie();

        StartCoroutine(DieRoutine());
    }

    /// <summary>
    /// Immediate death bookkeeping, run the instant the unit dies — before the death animation.
    /// Subclasses override this to detach from tiles, teams and counts right away. Kept separate
    /// from <see cref="OnDeath"/> (which finalizes/destroys only after the animation completes).
    /// </summary>
    protected virtual void OnDie() { }

    private IEnumerator DieRoutine()
    {
        // Kick off all before-death coroutines at once...
        List<Coroutine> running = new List<Coroutine>();
        foreach (Func<IEnumerator> factory in coroutinesBeforeDeath)
        {
            IEnumerator routine = factory?.Invoke();
            if (routine != null) running.Add(StartCoroutine(routine));
        }

        // ...including the death animation, run as just another before-death coroutine.
        if (deathAnimationController != null)
            running.Add(StartCoroutine(deathAnimationController.PlayAnimations()));

        // ...then wait until every one of them has completed.
        foreach (Coroutine c in running)
            yield return c;

        OnDeath();
    }

    /// <summary>
    /// Finalizes death: tears down equipment, fires <see cref="onDeath"/> and destroys the unit.
    /// Runs after all <see cref="coroutinesBeforeDeath"/> have finished. Do not call directly;
    /// use <see cref="Die"/> instead.
    /// </summary>
    public virtual void OnDeath()
    {
        state = State.dead;

        DestroyAllEquipment();

        onDeath?.Invoke();

        Destroy(gameObject);
    }

    public override List<ObjectsCountContainer> GetStats()
    {
        return new List<ObjectsCountContainer>()
        {
            stats,
        };
    }

    /// <summary>
    /// Reads a stat from the source <see cref="baseStats"/> container. Unlike <see cref="GetStat"/>
    /// (which reads the runtime <c>stats</c> populated in Awake), this works on an un-instantiated
    /// prefab, so it can be used to inspect a unit's cost before deciding whether to spawn it.
    /// </summary>
    public float GetBaseStat(string key, float defaultValue = 0f)
    {
        if (baseStats != null && baseStats.values.TryGetValue(key, out float value)) return value;
        return defaultValue;
    }

    public float GetStat(string key, float defaultValue=0, bool pure=false)
    {
        if (stats == null || !stats.values.ContainsKey(key)) return defaultValue;
        if (pure) return stats.values[key];
        
        // 
        if (key == P.StatK[P.Stat.health])
        {
            return stats.values[nameof(P.Stat.health)] + GetStat(P.StatK[P.Stat.shields]); 
        };
        return stats.values[key];
    }
    
    public float GetStat(P.Stat key, float defaultValue=0, bool pure=false)
    {
        return GetStat(P.StatK[key], defaultValue, pure);
    }
    //
    public bool MayDamage(int damage)
    {   
        if (state != State.active) return false;
        if (GetStat(P.StatK[P.Stat.health]) >= damage) return true;
        return false;
    }

    private Color shieldBlockColor = new Color32(0x0A, 0xA2, 0xFF, 0xFF);
    private string shieldBlockLabel = HTML.StatEffectIconsTags[P.StatusEffects.Shields];
    
    public virtual bool    TakeDamage(
        int damage,
        bool invokeDamageAlert=true,
        Color? color = null,
        string label = null,
        Vector3 alertOffset = default)
    {
        // h.Out(name, "TAKE DAMAGE", damage);
        if (immortal) return false;
        
        int remaining = damage;
        
        
        
        // Absorb incoming damage with shields first; only the leftover hits health.
        // (Negative damage = heal, which must bypass shields and go straight to health.)
        if (remaining > 0)
        {
            float shields = GetStat(P.StatK[P.Stat.shields], pure: true);
            if (shields > 0)
            {
                int absorbed = (int)Mathf.Min(shields, remaining);
                ChangeStat(-absorbed, P.StatK[P.Stat.shields], min: 0);
                if (invokeDamageAlert) TextAlertManager.Instance.CreateDamageAlert(
                    absorbed,
                    transform,
                    shieldBlockColor,
                    shieldBlockLabel,
                    alertOffset);
                remaining -= absorbed;
            }
        }

        if (remaining > 0)
        {
            ChangeStat(-remaining, P.StatK[P.Stat.health]);
            if (invokeDamageAlert) TextAlertManager.Instance.CreateDamageAlert(remaining, transform, color, label, alertOffset);
        }
        // h.Out(GetStat("health"));
        if (GetStat(P.StatK[P.Stat.health]) <= 0)
        {
            Die();
            return true;
        }
        onTakeDamage?.Invoke();
        return false;
    }

    public void Heal(
        int amount,
        bool invokeDamageAlert=true,
        Color? color = null,
        string label = null,
        Vector3 alertOffset = default
        )
    {
        // h.Out("heal", amount);
        // TakeDamage(
        //     -1 * amount,
        //     invokeDamageAlert,
        //     color,
        //     label);

        ChangeStat(amount, P.StatK[P.Stat.health]);
        if (invokeDamageAlert) TextAlertManager.Instance.CreateDamageAlert(amount, transform, color, label, alertOffset);
        
        onHeal?.Invoke();
    }

    /// <summary>
    /// Adds <paramref name="value"/> to the given stat, records the key and fires onChangeStat.
    /// </summary>
    public void ChangeStat(
        float value,
        string stat,
        float max=float.MaxValue,
        float min=0
        )
    {
        if (stats == null || !stats.values.ContainsKey(stat)) return;
        stats.values[stat] = Mathf.Clamp(stats.values[stat] + value, min, max);
        lastChangedStatKey = stat;
        onChangeStat?.Invoke(stat);
    }
    
    public void ChangeStat(
        float value,
        P.Stat stat,
        float max=float.MaxValue,
        float min=0
    )
    {
        ChangeStat(value, P.StatK[stat], max, min);
    }

    /// <summary>
    /// Sets the given stat to <paramref name="value"/>, records the key and fires onSetStat.
    /// </summary>
    public void SetStat(
        float value,
        string stat,
        float max=float.MaxValue,
        float min=0
        )
    {
        if (stats == null || !stats.values.ContainsKey(stat)) return;
        // h.Out("set stat", value, stat);
        stats.values[stat] = Mathf.Clamp(value, min, max);
        lastChangedStatKey = stat;
        onSetStat?.Invoke(stat);
    }


    public void SetStat(
        float value,
        P.Stat stat,
        float max=float.MaxValue,
        float min=0
    )
    {
        SetStat(value, P.StatK[stat], max, min);
    }
    
    
    
    
    // ─────────────────────────────────────────────────────────────
    //  Equipment Management
    // ─────────────────────────────────────────────────────────────

    public void AppendEquipment(EquipmentControllerBase equipmentControllerIn, bool init=false, bool createInstance=false)
    {
        if (!equipmentControllerIn) return;
        // todo: create instance on start equip
        if (createInstance && false) equipmentControllerIn = ScriptableObject.Instantiate(equipmentControllerIn);
        if (init)
        {
            equipmentControllerIn.Init(gameObject);
            return;
        }
        equipments[equipmentControllerIn.type].Add(equipmentControllerIn);
        equipmentSlotsCurrentCount[equipmentControllerIn.type]++;
        
    }

    public void RemoveEquipment(EquipmentControllerBase equipmentControllerIn, bool remove=false)
    {
        h.Out("REMOVE EQUIP");
        if (remove)
        {
            equipmentControllerIn.Remove();
            return;
        }
        h.Out("dict", equipments[equipmentControllerIn.type], "\ninst", equipmentControllerIn);
        equipments[equipmentControllerIn.type].Remove(equipmentControllerIn);
        equipmentSlotsCurrentCount[equipmentControllerIn.type] = equipments[equipmentControllerIn.type].Count;
        h.Out(equipmentSlotsCurrentCount[equipmentControllerIn.type]);
    }

    public void HideEquipmentSlots()
    {
        SetActiveForEquipmentSlots(false);
    }

    public void ShowEquipmentSlots()
    {
        SetActiveForEquipmentSlots(true);
    }

    private void SetActiveForEquipmentSlots(bool value)
    {
        foreach (var slotList in equipmentSlots.Values)
        {
            foreach (var slot in slotList)
            {
                slot.containingObject?.gameObject.SetActive(value);
                slot.gameObject.SetActive(value);
            }
        }
    }
    
    public void DestroyAllEquipment()
    {
        // Remove all equipment
        foreach (var equipmentList in equipments.Values)
        {
            foreach (var equipment in equipmentList)
            {
                equipment.Remove();
            }
        }
        equipments.Clear();
        
        // Destroy all equipment slots
        foreach (var slotList in equipmentSlots.Values)
        {
            foreach (var slot in slotList)
            {
                Destroy(slot.gameObject);
            }
        }
        equipmentSlots.Clear();
        
        // Reset current counts
        foreach (var key in equipmentSlotsCurrentCount.Keys.ToList())
        {
            equipmentSlotsCurrentCount[key] = 0;
        }
    }

   
}