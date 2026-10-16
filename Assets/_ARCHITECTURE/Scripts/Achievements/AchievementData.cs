using UnityEngine;

/// <summary>
/// Defines the goal condition type for an achievement.
/// </summary>
public enum AchievementType
{
    [Tooltip("True/False condition achieved in a single trigger (e.g. beat the game, win a battle).")]
    Bool,

    [Tooltip("Progressive numeric goal achieved by reaching a desired target value (e.g. kill 100 enemies, collect 500 gold).")]
    Value
}

/// <summary>
/// ScriptableObject definition for an in-game achievement.
/// Stores display details (title, achievement name, description, icon), goal type, current/desired progress values, and unlock status.
/// </summary>
[CreateAssetMenu(fileName = "NewAchievementData", menuName = "Scriptable Objects/Achievements/AchievementData")]
public class AchievementData : ScriptableObject
{
    [Header("Display")]
    [Tooltip("Header title text shown on the alert popup (e.g. 'Achievement Unlocked').")]
    public string title = "Achievement Unlocked";

    [Tooltip("The unique name of this achievement (e.g. 'Master Builder').")]
    public string achievementName;

    [Tooltip("Detailed description of how the achievement was earned or its flavor text.")]
    [TextArea(2, 5)]
    public string description;

    [Tooltip("Icon representing this achievement.")]
    public Sprite icon;

    [Header("Goal Type & Progress")]
    [Tooltip("Goal type: Bool (simple condition) or Value (target numeric value required).")]
    public AchievementType goalType = AchievementType.Bool;

    [Tooltip("Current progress value towards the achievement (used when goalType is Value).")]
    public float currentValue = 0f;

    [Tooltip("Desired target value to unlock this achievement (used when goalType is Value).")]
    public float desiredValue = 1f;

    [Header("State")]
    [Tooltip("Whether this achievement has already been granted/unlocked.")]
    public bool unlocked = false;

    [Header("Persistence")]
    [Tooltip("If true, unlock status and progressive values persist in PlayerPrefs between runs.")]
    public bool persistProgress = true;

    /// <summary>
    /// PlayerPrefs key used for saving unlock status and progress value.
    /// </summary>
    public string PrefsKey => "Achievement_" + (string.IsNullOrEmpty(achievementName) ? name : achievementName);

    private void OnEnable()
    {
        LoadFromPrefs();
    }

    /// <summary>
    /// Loads the saved unlock state and progress from PlayerPrefs.
    /// </summary>
    public void LoadFromPrefs()
    {
        if (!persistProgress) return;

        string key = PrefsKey;
        if (PlayerPrefs.HasKey(key + "_Unlocked"))
        {
            unlocked = PlayerPrefs.GetInt(key + "_Unlocked") == 1;
        }

        if (PlayerPrefs.HasKey(key + "_Progress"))
        {
            currentValue = PlayerPrefs.GetFloat(key + "_Progress");
        }
    }

    /// <summary>
    /// Saves the current unlock state and progress to PlayerPrefs.
    /// </summary>
    public void SaveToPrefs()
    {
        if (!persistProgress) return;

        string key = PrefsKey;
        PlayerPrefs.SetInt(key + "_Unlocked", unlocked ? 1 : 0);
        PlayerPrefs.SetFloat(key + "_Progress", currentValue);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Property indicating whether this achievement is currently unlocked.
    /// </summary>
    public bool IsUnlocked => unlocked;

    /// <summary>
    /// Returns the progress ratio normalized between 0 and 1.
    /// </summary>
    public float ProgressNormalized
    {
        get
        {
            if (unlocked) return 1f;
            if (goalType == AchievementType.Bool) return 0f;
            return desiredValue > 0f ? Mathf.Clamp01(currentValue / desiredValue) : 0f;
        }
    }

    /// <summary>
    /// Marks the achievement as unlocked.
    /// </summary>
    public void Unlock()
    {
        unlocked = true;
        if (goalType == AchievementType.Value && currentValue < desiredValue)
        {
            currentValue = desiredValue;
        }
        SaveToPrefs();
    }

    /// <summary>
    /// Adds progress to a Value-based achievement. Returns true if this caused the achievement to unlock.
    /// </summary>
    /// <param name="amount">Amount to add to currentValue.</param>
    public bool AddProgress(float amount)
    {
        if (unlocked) return false;
        return SetProgress(currentValue + amount);
    }

    /// <summary>
    /// Sets current progress for a Value-based achievement. Returns true if this caused the achievement to unlock.
    /// </summary>
    /// <param name="value">New current value.</param>
    public bool SetProgress(float value)
    {
        if (unlocked) return false;

        currentValue = value;
        SaveToPrefs();

        if (goalType == AchievementType.Value && currentValue >= desiredValue)
        {
            Unlock();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resets the achievement back to locked state and resets currentValue to 0.
    /// </summary>
    public void ResetLock()
    {
        unlocked = false;
        currentValue = 0f;

        string key = PrefsKey;
        if (PlayerPrefs.HasKey(key + "_Unlocked")) PlayerPrefs.DeleteKey(key + "_Unlocked");
        if (PlayerPrefs.HasKey(key + "_Progress")) PlayerPrefs.DeleteKey(key + "_Progress");
        PlayerPrefs.Save();
    }

#if UNITY_EDITOR
    [ContextMenu("Unlock Achievement")]
    private void EditorUnlock()
    {
        Unlock();
    }

    [ContextMenu("Reset Unlock State")]
    private void EditorResetLock()
    {
        ResetLock();
    }
#endif
}
