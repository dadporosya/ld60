using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The cards the player owns. Their bonuses go into every dice round
/// (<see cref="DiceManager.StartRound"/>), and each one is laid out on the table as a <see cref="CardView"/>.
/// </summary>
public class CardHand : MonoBehaviour
{
    public static CardHand Instance;

    [SerializeField] private Transform cardParent;
    [SerializeField] private CardView cardViewPrefab;

    [Tooltip("Cards the player starts with (optional).")]
    [SerializeField] private List<CardData> startingCards = new List<CardData>();
    [SerializeField] private CardMood startingMood = CardMood.Hope;

    public UnityEvent onChanged = new UnityEvent();

    private readonly List<CardInstance> cards = new List<CardInstance>();
    private readonly List<CardView> views = new List<CardView>();

    private bool initialized;

    public IReadOnlyList<CardInstance> Cards
    {
        get
        {
            Initialize();
            return cards;
        }
    }

    private void Awake() => Initialize();

    /// <summary>
    /// Runs from Awake, or on first use when the hand sits on a panel that starts hidden (an inactive
    /// object gets no Awake until it is shown).
    /// </summary>
    private void Initialize()
    {
        if (initialized) return;
        initialized = true;

        h.CreateStaticInstance(this, ref Instance, setDontDestroy: false);
        if (cardViewPrefab && cardViewPrefab.gameObject.scene.IsValid()) cardViewPrefab.gameObject.SetActive(false);

        foreach (CardData data in startingCards)
            if (data) Add(new CardInstance(data, startingMood));
    }

    public void Add(CardInstance card)
    {
        Initialize();
        if (card == null || !card.data) return;

        cards.Add(card);
        if (cardViewPrefab)
        {
            CardView view = Instantiate(cardViewPrefab, cardParent ? cardParent : transform);
            view.gameObject.SetActive(true);
            view.Bind(card);
            views.Add(view);
        }

        onChanged?.Invoke();
    }

    public bool Owns(CardData data)
    {
        foreach (CardInstance card in Cards)
            if (card.data == data) return true;
        return false;
    }

    public void Clear()
    {
        cards.Clear();
        foreach (CardView view in views)
            if (view) Destroy(view.gameObject);
        views.Clear();
        onChanged?.Invoke();
    }
}
