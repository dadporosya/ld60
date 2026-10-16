using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Windows XP style message alert popup.
/// Fills its TMP fields on Start and plays a random alert SFX (2D).
/// Pressing accept or close plays a random close sound and destroys the popup.
///
/// <para><b>Waiting for one.</b> <see cref="OnClosed"/> fires when the window goes away — whether the
/// player dismissed it, something else called <see cref="Close"/>, or the object was simply destroyed.
/// That is how a cutscene blocks on "the player pressed OK" without polling for a destroyed object.</para>
/// </summary>
public class WinXPMessageAlert : MonoBehaviour
{
    [Header("TMP targets")]
    public TMP_Text titleTMP;
    public TMP_Text alertTMP;

    [Header("Content")]
    [TextArea] public string titleText;
    [TextArea] public string alertText;

    [Header("Buttons")]
    public Button acceptBtn;
    public Button closeBtn;

    [Header("Sound")]
    public List<AudioClip> alertSFX = new List<AudioClip>();
    public List<AudioClip> closeSFX = new List<AudioClip>();

    /// <summary>True once <see cref="Close"/> has started — subclasses check it before reacting to input.</summary>
    protected bool closing;

    /// <summary>
    /// Raised exactly once, when the window goes away. Raised from <see cref="Close"/> before the object
    /// is destroyed (so listeners still get a live reference) and from <c>OnDestroy</c> otherwise, so a
    /// window torn down by a canvas going away or a manager's ClearPopups is not silently missed.
    /// </summary>
    public event System.Action<WinXPMessageAlert> OnClosed;

    // Guards the event against being raised twice: Close raises it, and OnDestroy runs afterwards.
    private bool closedRaised;

    protected virtual void Start()
    {
        ApplyTexts();

        if (acceptBtn) acceptBtn.onClick.AddListener(Close);
        if (closeBtn) closeBtn.onClick.AddListener(Close);

        PlayRandom(alertSFX);
    }

    /// <summary>Pushes <see cref="titleText"/> / <see cref="alertText"/> into the TMP fields.</summary>
    public void ApplyTexts()
    {
        if (titleTMP) titleTMP.text = titleText;
        if (alertTMP) alertTMP.text = alertText;
    }

    /// <summary>Sets the texts at runtime (call before/instead of relying on the inspector values).</summary>
    public void SetText(string title, string alert)
    {
        titleText = title;
        alertText = alert;
        ApplyTexts();
    }

    /// <summary>Plays the close sound and destroys the popup.</summary>
    public virtual void Close()
    {
        if (closing) return;
        closing = true;

        // Before the destroy, so listeners are handed a window that still exists.
        RaiseClosed();

        PlayRandom(closeSFX);

        if (acceptBtn) acceptBtn.onClick.RemoveListener(Close);
        if (closeBtn) closeBtn.onClick.RemoveListener(Close);

        Destroy(gameObject);
    }

    /// <summary>
    /// Closes the window without its close sound — how a window that is dismissed by something other
    /// than the player (a cutscene moving on, a timeout) goes away, since a click sound with no click
    /// behind it reads as a bug.
    /// </summary>
    public void CloseSilently()
    {
        if (closing) return;

        closeSFX = new List<AudioClip>();
        Close();
    }

    /// <summary>Raises <see cref="OnClosed"/> the first time it is called and does nothing after that.</summary>
    protected void RaiseClosed()
    {
        if (closedRaised) return;
        closedRaised = true;

        OnClosed?.Invoke(this);
    }

    /// <summary>
    /// Destroyed without going through <see cref="Close"/> (the canvas went away, ClearPopups, a scene
    /// change): listeners still have to hear about it.
    /// </summary>
    protected virtual void OnDestroy()
    {
        RaiseClosed();
    }

    // Played through SFXManager as an independent 2D sound so it survives this object being destroyed.
    protected void PlayRandom(List<AudioClip> clips)
    {
        if (clips == null || clips.Count == 0) return;

        if (!SFXManager.Instance)
        {
            h.Out("WinXPMessageAlert: no SFXManager instance, skipping sound");
            return;
        }

        SFXManager.Instance.PlayRandomClip(clips, is3D: false, playIndependently: true);
    }
}
