using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The "who do I call" choice: one button per person not called yet, plus a hang-up button that
/// goes back to the hub. The buttons are spawned from <see cref="buttonPrefab"/> (a Button with a
/// TextMeshProUGUI and, optionally, an Image named "Portrait" among its children).
/// </summary>
public class PhoneDialPanel : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private Transform buttonParent;
    [SerializeField] private Button buttonPrefab;
    [SerializeField] private Button cancelButton;
    [Tooltip("Fades the panel in and out when set.")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField, Min(0f)] private float fadeDuration = 0.2f;

    private readonly List<Button> spawned = new List<Button>();
    private Action<ContactData> onPicked;
    private Action onCancelled;

    public bool IsOpen { get; private set; }

    private GameObject Root => root ? root : gameObject;

    private void Awake()
    {
        if (cancelButton) cancelButton.onClick.AddListener(Cancel);
        // A template button in the scene stays hidden; a prefab asset is left alone.
        if (buttonPrefab && buttonPrefab.gameObject.scene.IsValid()) buttonPrefab.gameObject.SetActive(false);
        // A panel that starts inactive only wakes when Show activates it - it must not hide itself then.
        if (!IsOpen) Root.SetActive(false);
    }

    public void Show(IEnumerable<ContactData> contacts, Action<ContactData> picked, Action cancelled)
    {
        onPicked = picked;
        onCancelled = cancelled;
        Clear();

        foreach (ContactData contact in contacts)
        {
            if (!contact || !buttonPrefab) continue;

            Button button = Instantiate(buttonPrefab, buttonParent ? buttonParent : buttonPrefab.transform.parent);
            button.gameObject.SetActive(true);

            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label) label.text = contact.DisplayName;

            Transform portrait = button.transform.Find("Portrait");
            Image portraitImage = portrait ? portrait.GetComponent<Image>() : null;
            if (portraitImage)
            {
                portraitImage.sprite = contact.portrait;
                portraitImage.enabled = contact.portrait;
            }

            ContactData captured = contact;
            button.onClick.AddListener(() => Pick(captured));
            spawned.Add(button);
        }

        IsOpen = true;
        Root.SetActive(true);
        if (canvasGroup)
        {
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
            if (fadeDuration > 0f) PrimeTween.Tween.Alpha(canvasGroup, 0f, 1f, fadeDuration);
            else canvasGroup.alpha = 1f;
        }
    }

    public void Hide()
    {
        if (!IsOpen) return;
        IsOpen = false;
        onPicked = null;
        onCancelled = null;

        if (canvasGroup && fadeDuration > 0f && Root.activeInHierarchy)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            PrimeTween.Tween.Alpha(canvasGroup, 0f, fadeDuration)
                .OnComplete(this, panel => { if (!panel.IsOpen) panel.FinishHide(); });
        }
        else FinishHide();
    }

    private void FinishHide()
    {
        Clear();
        Root.SetActive(false);
    }

    private void Pick(ContactData contact)
    {
        Action<ContactData> picked = onPicked;
        Hide();
        picked?.Invoke(contact);
    }

    private void Cancel()
    {
        Action cancelled = onCancelled;
        Hide();
        cancelled?.Invoke();
    }

    private void Clear()
    {
        foreach (Button button in spawned)
            if (button) Destroy(button.gameObject);
        spawned.Clear();
    }
}
