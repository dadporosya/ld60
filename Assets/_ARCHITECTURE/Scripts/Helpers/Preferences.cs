using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Preferences
/// </summary>
public static class P
{
    public enum Team
    {
        None, Player, Enemy
    }
    
    public static TeamContainer PlayerTeam = new TeamContainer()
    {
        allyTags = new List<string> { "Player", "Ally" },
        allyParentTags = new List<string> { "PlayerParent" },
        
        enemyTags = new List<string> { "Enemy" },
        enemyParentTags = new List<string> { "EnemiesParent" }
    };
    
    public static TeamContainer EnemyTeam = new TeamContainer()
    {
        allyTags = new List<string> { "Enemy" },
        allyParentTags = new List<string> { "EnemiesParent" },
        
        enemyTags = new List<string> { "Player", "Ally" },
        enemyParentTags = new List<string> { "PlayerParent" }
    };
    
    public static TeamContainer EmptyTeam = new TeamContainer()
    {
    };

    public static TeamContainer GetTeamContainerByTeam(Team team)
    {
        switch (team)
        {
            case Team.Player: return PlayerTeam; break;
            case Team.Enemy: return EnemyTeam; break;
            case Team.None: return EmptyTeam; break;
        }

        return null;
    }

    public static Dictionary<string, string> raritiesNames = new Dictionary<string, string>
    {
        {"common", "dusted"},
        {"rare", "vanguard"},
        {"super rare", "x-grade"}
    };
    
    public static Dictionary<string, float> rarities = new Dictionary<string, float>
    {
        { raritiesNames["common"], 0.6f }, 
        { raritiesNames["rare"], 0.4f }, 
        { raritiesNames["super rare"], 0.1f }
    };
    public static float anomalyChange = 0.05f;

    public static List<string> equipmentTypes = new List<string>
    {
        "turret", "effect"
    };

    public enum EquipmentType
    {
        Any,
        Turret, 
        Effect,
        None
    }

    public enum DisplayType
    {
        Right,
        Left, Bottom,Top
    }

    public static Dictionary<EquipmentType, DisplayType> equipmentDisplayType =
        new Dictionary<EquipmentType, DisplayType>
        {
            {EquipmentType.Turret, DisplayType.Right},
            {EquipmentType.Effect, DisplayType.Left},
            {EquipmentType.Any, DisplayType.Top}
        };
    
    public enum PositionType
    {
        Head,
        Self,
        First,
        Last,
        Neighbours,
        Previous,
        Next,
        Distance,
        Position
    }

    public enum StatusEffects
    {
        Ignition,
        Stun,
        Shields
    }

    /// <summary>
    /// All possible Unit stats
    /// </summary>
    public enum Stat
    {
        none,
        health,
        maxHealth,
        speed,
        fireRate,
        damage,
        shields,
        maxShields,
        supply
    }

    /// <summary>
    /// Keys of all possible unit stats to get them from stat container
    /// </summary>
    public static Dictionary<Stat, string> StatK = new Dictionary<Stat, string>
    {
        { Stat.health, "health" },
        { Stat.maxHealth, "maxHealth" },
        { Stat.speed, "speed" },
        { Stat.fireRate, "fireRate" },
        { Stat.damage, "damage" },
        { Stat.shields, "shields" },
        { Stat.maxShields, "maxShields"},
        { Stat.supply, "supply" }
    };

    public enum MoodType
    {
        Despair, 
        Wrath,
        Hope,
    }

    public static Dictionary<string, Color> colors = new Dictionary<string, Color>()
    {
        {"move", Color.lawnGreen}, {"attack", Color.softRed}
    };

    
    // ---------------------PROJECT PREFERENCES----------------------
    // ---- Battle tuning -------------------------------------------------------------------------
    // These are the AUTHORED DEFAULTS. A scene that has an AttackBattleManager component overwrites
    // every one of them from its inspector values on Awake (and again on any inspector change while
    // playing), so the manager is the place to tune a fight; these are what a scene without one uses.

    /// <summary>
    /// How many units may attack one unit before it counts as "fully engaged". Units always prefer an
    /// enemy below this number, which is what stops a big stack from focusing its whole strength onto
    /// one small group in a single volley.
    ///
    /// <para>It is a preference, not a wall: when NO enemy on the tile is below it, units pile on
    /// anyway (see <see cref="overflowMaxAttackers"/>) rather than stand around next to a fight.</para>
    /// </summary>
    public static int maxAttackers = 3;

    /// <summary>
    /// Ceiling on attackers per unit in the overflow case — that is, when every enemy is already at
    /// <see cref="maxAttackers"/> and someone still has nobody to fight. <b>0 means no ceiling</b>
    /// (the default): everyone joins in, piling onto whichever enemy currently has the fewest
    /// attackers so the surplus spreads out instead of stacking on one unit.
    ///
    /// <para>Set it above <see cref="maxAttackers"/> to allow a limited overflow and hold the rest in
    /// reserve — e.g. maxAttackers 3 / overflow 5 means "spread out at 3 each, cram up to 5 onto a
    /// last survivor, and anyone beyond that waits their turn".</para>
    /// </summary>
    public static int overflowMaxAttackers = 0;

    /// <summary>
    /// Random opening delay (min, max) in seconds before a unit's FIRST swing at a new target. Purely
    /// cosmetic: it staggers units that reached their targets on the same frame so a squad doesn't
    /// attack in lockstep. The delay is paid once per engagement, so it does not change sustained DPS.
    /// </summary>
    public static Vector2 attackStartDelay = new Vector2(0f, 0.333f);

    /// <summary>
    /// When false, units swing the instant they arrive in range and <see cref="attackStartDelay"/> is
    /// ignored — the old lockstep look. Purely visual either way.
    /// </summary>
    public static bool staggerAttacks = true;

    /// <summary>
    /// How often (min, max seconds) a unit waiting in reserve re-checks whether an attack slot has
    /// opened up. Reserves are also re-checked immediately whenever the tile's roster changes
    /// (<see cref="Tile.TriggerTargetSearch"/>); this poll is the safety net for slots that free up
    /// without anyone joining or leaving the tile. Only reachable when
    /// <see cref="overflowMaxAttackers"/> is set, since an unlimited overflow never leaves anyone out.
    /// </summary>
    public static Vector2 reserveRecheckDelay = new Vector2(0.4f, 0.8f);

    /// <summary>
    /// Whether units waiting in reserve wander around behind the fight (true) or stand still where
    /// they stopped (false). Cosmetic; it does not change when they join the fight.
    /// </summary>
    public static bool reservesWander = true;

    /// <summary>
    /// Whether a unit that is attacked always fights back, even when its attacker is already at the
    /// attacker cap. True keeps a unit from standing there taking hits; false makes it look for a
    /// target with room instead.
    /// </summary>
    public static bool retaliateOverCap = true;

}

[Serializable]
public class TeamContainer
{
    public List<string> allyTags = new List<string>();
    public List<string> allyParentTags = new List<string>();

    public List<string> enemyTags = new List<string>();
    public List<string> enemyParentTags = new List<string>();

    public TeamContainer Copy() => new TeamContainer
    {
        allyTags = new List<string>(allyTags),
        allyParentTags = new List<string>(allyParentTags),
        enemyTags = new List<string>(enemyTags),
        enemyParentTags = new List<string>(enemyParentTags),
    };
}