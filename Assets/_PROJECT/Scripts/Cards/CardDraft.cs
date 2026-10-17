using System;
using System.Collections.Generic;
using PrimeTween;
using TMPro;
using UnityEngine;

/// <summary>
/// The card pick after a call: <see cref="choices"/> different cards from the pool, all on the same
/// side - Hope after a good call, Hate after a bad one. Clicking one hands it back and closes the pick.
/// </summary>
public class CardDraft : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private Transform cardParent;
    [SerializeField] private CardView cardViewPrefab;
    [SerializeField] private TextMeshProUGUI headerText;
    [SerializeField] private string hopeHeader = "Hope";
    [SerializeField] private string hateHeader = "Hate";
    [Tooltip("Fades the pick in and out when set.")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField, Min(0f)] private float fadeDuration = 0.25f;

    [Header("Pool")]
    [SerializeField] private List<CardData> pool = new List<CardData>();
    [Min(1)] public int choices = 2;
    [Tooltip("Offer cards the player doesn't own yet first; owned ones only fill up a short pool.")]
    [SerializeField] private bool preferUnowned = true;

    private readonly List<CardView> spawned = new List<CardView>();
    private Action<CardInstance> onPicked;

    public bool IsOpen { get; private set; }

    private GameObject Root => root ? root : gameObject;

    private void Awake()
    {
        if (cardViewPrefab && cardViewPrefab.gameObject.scene.IsValid()) cardViewPrefab.gameObject.SetActive(false);
        // A panel that starts inactive only wakes when Show activates it - it must not hide itself then.
        if (!IsOpen) Root.SetActive(false);
    }

    /// <summary>
    /// Offers cards on the <paramref name="mood"/> side. <paramref name="picked"/> gets the chosen card,
    /// or null right away when the pool is empty.
    /// </summary>
    public void Show(CardMood mood, CardHand hand, Action<CardInstance> picked)
    {
        List<CardData> offer = PickOffer(hand);
        if (offer.Count == 0 || !cardViewPrefab)
        {
            picked?.Invoke(null);
            return;
        }

        onPicked = picked;
        Clear();

        foreach (CardData data in offer)
        {
            CardView view = Instantiate(cardViewPrefab, cardParent ? cardParent : cardViewPrefab.transform.parent);
            view.gameObject.SetActive(true);
            view.Bind(new CardInstance(data, mood), Pick);
            spawned.Add(view);
        }

        if (headerText) headerText.text = mood == CardMood.Hope ? hopeHeader : hateHeader;

        IsOpen = true;
        Root.SetActive(true);
        if (canvasGroup)
        {
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
            if (fadeDuration > 0f) Tween.Alpha(canvasGroup, 0f, 1f, fadeDuration);
            else canvasGroup.alpha = 1f;
        }
    }

    public void Hide()
    {
        if (!IsOpen) return;
        IsOpen = false;
        onPicked = null;

        if (canvasGroup && fadeDuration > 0f && Root.activeInHierarchy)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            Tween.Alpha(canvasGroup, 0f, fadeDuration)
                .OnComplete(this, draft => { if (!draft.IsOpen) draft.FinishHide(); });
        }
        else FinishHide();
    }

    private void FinishHide()
    {
        Clear();
        Root.SetActive(false);
    }

    private void Pick(CardView view)
    {
        Action<CardInstance> picked = onPicked;
        CardInstance card = view.Card;
        Hide();
        picked?.Invoke(card);
    }

    private List<CardData> PickOffer(CardHand hand)
    {
        List<CardData> fresh = new List<CardData>();
        List<CardData> owned = new List<CardData>();
        foreach (CardData data in pool)
        {
            if (!data || fresh.Contains(data) || owned.Contains(data)) continue;
            if (preferUnowned && hand && hand.Owns(data)) owned.Add(data);
            else fresh.Add(data);
        }

        Shuffle(fresh);
        Shuffle(owned);
        fresh.AddRange(owned);
        if (fresh.Count > choices) fresh.RemoveRange(choices, fresh.Count - choices);
        return fresh;
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private void Clear()
    {
        foreach (CardView view in spawned)
            if (view) Destroy(view.gameObject);
        spawned.Clear();
    }
}
