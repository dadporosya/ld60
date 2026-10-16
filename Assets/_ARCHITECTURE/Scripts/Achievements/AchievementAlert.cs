using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI component representing an achievement alert popup or achievement list element.
/// Displays achievement title, name, description, icon, and optional progress info.
/// Supports dismissing/destroying itself when any of its close buttons are clicked.
/// </summary>
public class AchievementAlert : MonoBehaviour
{
    [Header("TMP Targets")]
    [Tooltip("Text field for the alert header/title.")]
    public TMP_Text titleTMP;

    [Tooltip("Text field for the achievement name.")]
    public TMP_Text achievementNameTMP;

    [Tooltip("Text field for the achievement description.")]
    public TMP_Text descriptionTMP;

    [Tooltip("Optional text field for showing numerical progress (e.g. '5 / 10').")]
    public TMP_Text progressTMP;

    [Header("Content")]
    [Tooltip("Header title text string.")]
    public string titleText = "Achievement Unlocked";

    [Tooltip("Achievement name text string.")]
    public string achievementText;

    [Tooltip("Achievement description text string.")]
    [TextArea(2, 4)]
    public string descriptionText;

    [Tooltip("Progress text string (e.g. '5 / 10').")]
    public string progressText;

    [Header("Visuals")]
    [Tooltip("Image component displaying the achievement icon.")]
    public Image icon;

    [Tooltip("Optional slider or progress fill bar component.")]
    public Slider progressBar;

    [Header("Close Buttons")]
    [Tooltip("List of buttons that close and destroy this alert when clicked.")]
    public List<Button> closeButtons = new List<Button>();

    [Header("Sound")]
    [Tooltip("Sound clips played when the alert is closed by the player.")]
    public List<AudioClip> closeSFX = new List<AudioClip>();

    /// <summary>
    /// Raised when the alert is closed or destroyed. Handled by managers/tweens to clean up sequences.
    /// </summary>
    public event Action<AchievementAlert> OnClosed;

    private bool isClosing;
    private bool closedRaised;

    protected virtual void Awake()
    {
        ResolveRefs();
    }

    protected virtual void Start()
    {
        ApplyTexts();
        BindCloseButtons();
    }

    protected virtual void OnDestroy()
    {
        UnbindCloseButtons();
        RaiseClosed();
    }

    /// <summary>
    /// Auto-resolves missing component references from children if not wired in the inspector.
    /// </summary>
    public void ResolveRefs()
    {
        if (!titleTMP) titleTMP = transform.Find("Title")?.GetComponent<TMP_Text>();
        if (!achievementNameTMP) achievementNameTMP = transform.Find("AchievementName")?.GetComponent<TMP_Text>();
        if (!descriptionTMP) descriptionTMP = transform.Find("Description")?.GetComponent<TMP_Text>();
        if (!progressTMP) progressTMP = transform.Find("Progress")?.GetComponent<TMP_Text>();
        if (!icon) icon = transform.Find("Icon")?.GetComponent<Image>();
        if (!progressBar) progressBar = GetComponentInChildren<Slider>(true);
    }

    /// <summary>
    /// Initializes this alert from an <see cref="AchievementData"/> ScriptableObject.
    /// Replaces all local parameters with matching data from the data object and applies them to UI fields.
    /// </summary>
    /// <param name="data">The achievement data to populate from.</param>
    public virtual void InitFromData(AchievementData data)
    {
        if (!data) return;

        if (!string.IsNullOrEmpty(data.title))
        {
            titleText = data.title;
        }

        achievementText = data.achievementName;
        descriptionText = data.description;

        if (icon && data.icon)
        {
            icon.sprite = data.icon;
            icon.enabled = true;
        }

        if (data.goalType == AchievementType.Value)
        {
            progressText = $"{data.currentValue:0.#} / {data.desiredValue:0.#}";
            if (progressBar)
            {
                progressBar.gameObject.SetActive(true);
                progressBar.value = data.ProgressNormalized;
            }
        }
        else
        {
            progressText = string.Empty;
            if (progressBar) progressBar.gameObject.SetActive(false);
        }

        ApplyTexts();
    }

    /// <summary>
    /// Pushes current string values and icon sprite into the respective TMP and Image components.
    /// </summary>
    public virtual void ApplyTexts()
    {
        if (titleTMP) titleTMP.text = titleText;
        if (achievementNameTMP) achievementNameTMP.text = achievementText;
        if (descriptionTMP) descriptionTMP.text = descriptionText;
        if (progressTMP)
        {
            progressTMP.text = progressText;
            progressTMP.gameObject.SetActive(!string.IsNullOrEmpty(progressText));
        }
    }

    /// <summary>
    /// Directly sets custom texts and updates UI.
    /// </summary>
    public void SetTexts(string title, string achievementName, string description)
    {
        titleText = title;
        achievementText = achievementName;
        descriptionText = description;
        ApplyTexts();
    }

    /// <summary>
    /// Hooks up onClick listeners to all assigned close buttons.
    /// </summary>
    protected void BindCloseButtons()
    {
        if (closeButtons == null) return;

        foreach (Button btn in closeButtons)
        {
            if (btn) btn.onClick.AddListener(Close);
        }
    }

    /// <summary>
    /// Unhooks onClick listeners from all assigned close buttons.
    /// </summary>
    protected void UnbindCloseButtons()
    {
        if (closeButtons == null) return;

        foreach (Button btn in closeButtons)
        {
            if (btn) btn.onClick.RemoveListener(Close);
        }
    }

    /// <summary>
    /// Enables or disables interaction with the close buttons (e.g. for list items in windows).
    /// </summary>
    public void SetCloseButtonsActive(bool active)
    {
        if (closeButtons == null) return;

        foreach (Button btn in closeButtons)
        {
            if (btn) btn.gameObject.SetActive(active);
        }
    }

    /// <summary>
    /// Dismisses and destroys the alert popup, playing an optional close sound.
    /// </summary>
    public virtual void Close()
    {
        if (isClosing) return;
        isClosing = true;

        RaiseClosed();
        PlayCloseSound();
        UnbindCloseButtons();

        Destroy(gameObject);
    }

    /// <summary>
    /// Invokes the <see cref="OnClosed"/> event safely once.
    /// </summary>
    protected void RaiseClosed()
    {
        if (closedRaised) return;
        closedRaised = true;

        OnClosed?.Invoke(this);
    }

    /// <summary>
    /// Plays a random close sound through <see cref="SFXManager"/> if available.
    /// </summary>
    protected void PlayCloseSound()
    {
        if (closeSFX == null || closeSFX.Count == 0) return;
        if (!SFXManager.Instance) return;

        SFXManager.Instance.PlayRandomClip(closeSFX, is3D: false, playIndependently: true);
    }
}
