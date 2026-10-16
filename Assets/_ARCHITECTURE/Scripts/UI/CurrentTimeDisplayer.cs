using System;
using TMPro;
using UnityEngine;

/// <summary>
/// Writes the current system time into a TMP_Text field, refreshed every second.
/// </summary>
public class CurrentTimeDisplayer : MonoBehaviour
{
    [SerializeField] private TMP_Text text;
    [SerializeField] private bool AMPMFormat = false;
    [Tooltip("Show seconds as well as hours and minutes.")]
    [SerializeField] private bool showSeconds = false;
    [Tooltip("How often (in seconds) the label is refreshed.")]
    [SerializeField] private float updateInterval = 1f;

    private float timer;

    private void Reset()
    {
        text = GetComponent<TMP_Text>();
    }

    private void OnEnable()
    {
        if (text == null) text = GetComponent<TMP_Text>();
        timer = 0f;
        UpdateText();
    }

    private void Update()
    {
        timer -= Time.unscaledDeltaTime;
        if (timer > 0f) return;

        timer = Mathf.Max(0.02f, updateInterval);
        UpdateText();
    }

    private void UpdateText()
    {
        if (text == null) return;
        text.text = DateTime.Now.ToString(GetFormat(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private string GetFormat()
    {
        if (AMPMFormat) return showSeconds ? "hh:mm:ss tt" : "hh:mm tt";
        return showSeconds ? "HH:mm:ss" : "HH:mm";
    }

    /// <summary>Switch between 24h and AM/PM at runtime.</summary>
    public void SetAMPMFormat(bool value)
    {
        AMPMFormat = value;
        UpdateText();
    }

    /// <summary>Assign the target label at runtime.</summary>
    public void SetText(TMP_Text value)
    {
        text = value;
        UpdateText();
    }
}
