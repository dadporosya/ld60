using System;
using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// Owns the bonus cards: which ones can be offered, the choosing menu, and the cards the player keeps.
/// Access via <c>CardManager.Instance</c>.
///
/// <para><b>Choosing</b> (<see cref="ChooseCards(int, CardMood, Action{CardInstance})"/>): the fade image
/// darkens the screen a bit and the cards fly into the middle (<see cref="chooseArea"/>). Clicking one
/// picks it: a <see cref="CardKind.Immediate"/> card applies its effect and burns; a
/// <see cref="CardKind.Permanent"/> one is kept - shown in <see cref="permanentCardsSpot"/> and added to
/// every dice round (<see cref="PermanentCards"/>). Then the menu fades out and is disabled.</para>
///
/// <para><b>Upgrades</b>: with <see cref="UpgradeNextChoice"/> set (the player reached the risky goal), the next
/// choice offers upgraded cards - their points are worth <see cref="upgradeBonus"/> more and show tinted.</para>
///
/// <para><b>Dice</b> (<see cref="ChooseDice"/>, CardManager.DiceChoice.cs): the same menu offers new dice as cards,
/// plus a card to turn them down.</para>
///
/// <para>Keep this component OUTSIDE <see cref="root"/>: the root is disabled between choices, and the
/// debug key needs this object running.</para>
/// </summary>
public partial class CardManager : MonoBehaviour
{
    public static CardManager Instance;

    [Header("Cards")]
    [Tooltip("Every card that can be offered.")]
    [SerializeField] private List<CardData> availableCards = new List<CardData>();
    [Tooltip("The card's look: good title, bad title, description, picture. A card can bring its own (CardData.viewPrefab).")]
    [SerializeField] private CardView cardPrefab;
    [Tooltip("Cards offered per choice when no count is given.")]
    [SerializeField, Min(1)] private int defaultChoiceCount = 2;
    [Tooltip("Offer permanent cards the player already has, however many copies of them the deck holds (CardData.count). " +
             "Off: a card is offered until the player owns all its copies.")]
    [SerializeField] private bool allowOwnedPermanentCards = false;
    [Tooltip("A card offered once isn't offered again until every other card has been (a new game resets it).")]
    [SerializeField] private bool noRepeatsAcrossChoices = true;
    [Tooltip("Permanent cards the player starts with (optional).")]
    [SerializeField] private List<CardData> startingCards = new List<CardData>();

    [Header("Upgrades")]
    [Tooltip("Extra points an upgraded card grants, as a share of its own: 0.5 = 50% more, whatever its " +
             "condition (+10 becomes +15, x1.5 becomes x1.75). Cards are offered upgraded after the player " +
             "reaches a round's risky goal (Upgrade Next Choice).")]
    [SerializeField, Min(0f)] private float upgradeBonus = 0.5f;

    [Header("Choosing menu")]
    [Tooltip("The menu: enabled while choosing, disabled after.")]
    [SerializeField] private GameObject root;
    [Tooltip("Full-screen image that darkens the screen while choosing (and blocks clicks behind it).")]
    [SerializeField] private Image fadeImage;
    [SerializeField, Range(0f, 1f)] private float fadeAlpha = 0.6f;
    [SerializeField, Min(0f)] private float fadeDuration = 0.35f;
    [Tooltip("Where the cards to choose from spawn (the middle of the screen). With a layout group on it the " +
             "layout places them; without one they're lined up side by side around its centre.")]
    [SerializeField] private RectTransform chooseArea;
    [Tooltip("Gap between the cards to choose from (UI units, no layout group only).")]
    [SerializeField] private float cardSpacing = 40f;

    [Header("Choosing animation")]
    [SerializeField, Min(0f)] private float appearDuration = 0.35f;
    [Tooltip("Seconds between one card appearing and the next.")]
    [SerializeField, Min(0f)] private float appearStagger = 0.08f;
    [SerializeField] private Ease appearEase = Ease.OutBack;
    [Tooltip("Scale punch on the picked card.")]
    [SerializeField, Min(0f)] private float pickPunch = 0.15f;
    [Tooltip("Seconds the pick stays on screen before the menu closes.")]
    [SerializeField, Min(0f)] private float pickHold = 0.45f;
    [Tooltip("Seconds the cards take to fade (the others at once, the picked one / a burning one after the hold).")]
    [SerializeField, Min(0f)] private float cardFadeDuration = 0.25f;
    [Tooltip("Scale an immediate card grows to as it burns.")]
    [SerializeField, Min(0f)] private float burnScale = 1.3f;

    [Header("Permanent cards")]
    [Tooltip("Where kept permanent cards are shown - e.g. a RectTransform inside the dice gameplay window, so the " +
             "player sees the bonuses they've collected. With a layout group on it the layout places the cards; " +
             "without one they're lined up from its top-left corner, wrapping into new rows.")]
    [FormerlySerializedAs("permanentCardsParent")]
    [SerializeField] private RectTransform permanentCardsSpot;
    [Tooltip("Size of a kept card relative to the prefab.")]
    [SerializeField, Min(0.01f)] private float permanentCardScale = 0.6f;
    [Tooltip("Kept cards grow when hovered, so the player can read them.")]
    [SerializeField] private bool keptCardsHoverable = true;
    [Tooltip("How much a hovered kept card grows (1.67 with Permanent Card Scale 0.6 = full card size; 1.22 = a third of that growth).")]
    [SerializeField, Min(1f)] private float keptHoverScale = 1.22f;
    [Tooltip("Gap between kept cards (UI units, no layout group only).")]
    [SerializeField, Min(0f)] private float permanentCardSpacing = 12f;
    [Tooltip("Seconds the chosen card flies from the menu into its place in the spot, when the spot is on " +
             "screen. 0 (or the spot hidden): it pops up there instead.")]
    [SerializeField, Min(0f)] private float flyDuration = 0.5f;
    [SerializeField] private Ease flyEase = Ease.InOutQuad;
    [SerializeField, Min(0f)] private float permanentAppearDuration = 0.3f;

    [Header("Audio")]
    [SerializeField] private AudioClip appearSfx;
    [Tooltip("The mouse came onto a card that can be picked (or a kept card, when those are hoverable).")]
    [SerializeField] private AudioClip hoverSfx;
    [Tooltip("A card (or a die in the dice choice) was picked.")]
    [SerializeField] private AudioClip pickSfx;
    [SerializeField] private AudioClip burnSfx;

    [Header("Debug")]
    [Tooltip("Debug only: the key below opens a choice of Default Choice Count cards.")]
    [SerializeField] private bool debugKey = false;
    [SerializeField] private Key debugChooseKey = Key.Q;

    [Header("Events")]
    public UnityEvent onChoiceOpened = new UnityEvent();
    public UnityEvent<CardData> onCardChosen = new UnityEvent<CardData>();
    /// <summary>An immediate card was applied - hook custom effects (ImmediateEffectType.Custom) here.</summary>
    public UnityEvent<CardData> onImmediateCardApplied = new UnityEvent<CardData>();
    public UnityEvent<CardData> onPermanentCardAdded = new UnityEvent<CardData>();
    public UnityEvent onChoiceClosed = new UnityEvent();

    /// <summary>The permanent cards the player has; their bonuses go into every dice round.</summary>
    public IReadOnlyList<CardInstance> PermanentCards
    {
        get
        {
            Initialize();
            return permanentCards;
        }
    }

    public bool IsChoosing { get; private set; }

    public int DefaultChoiceCount => defaultChoiceCount;

    /// <summary>The card's look, for cards shown elsewhere (a special die's info card).</summary>
    public CardView CardPrefab => cardPrefab;

    /// <summary>Extra share of points an upgraded card grants (0.5 = 50% more).</summary>
    public float UpgradeBonus => upgradeBonus;

    /// <summary>
    /// The next choice offers upgraded cards (the player reached the risky goal). Used up when that choice
    /// opens; a picked upgraded card stays upgraded.
    /// </summary>
    public bool UpgradeNextChoice { get; set; }

    private readonly List<CardInstance> permanentCards = new List<CardInstance>();
    private readonly List<CardView> permanentViews = new List<CardView>();
    private readonly List<CardView> offered = new List<CardView>();
    // Cards offered since the deck last ran out (noRepeatsAcrossChoices): not offered again until it does.
    private readonly HashSet<CardData> shownCards = new HashSet<CardData>();
    // A permanent card just chosen: its view in the spot, hidden until the chosen card has flown there.
    private CardView pendingKept;
    private Action<CardInstance> onChosen;
    // A choice of plain card faces (OpenChoice): gets the picked one's index.
    private Action<int> onOptionChosen;
    private int chosenOption = -1;
    private bool picking;
    private bool initialized;
    private Tween fadeTween;
    private Tween stepTween;

    private void Awake() => Initialize();

    /// <summary>Runs from Awake, or on first use when this sits on an object that starts hidden.</summary>
    private void Initialize()
    {
        if (initialized) return;
        initialized = true;

        h.CreateStaticInstance(this, ref Instance, setDontDestroy: false);
        if (root == gameObject)
            h.Out("CardManager: it sits on its own menu root, which is disabled between choices - move it outside " +
                  "the root, or the debug key won't work.");

        if (fadeImage) SetFadeAlpha(0f);
        if (root && !IsChoosing) root.SetActive(false);

        foreach (CardData data in startingCards)
            if (data && data.IsPermanent) AddPermanent(new CardInstance(data, CardMood.Hope), animate: false);
    }

    private void Update()
    {
        if (!DebugHotkeys.Allowed(debugKey) || IsChoosing) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (debugChooseKey != Key.None && keyboard[debugChooseKey].wasPressedThisFrame) ChooseCards(defaultChoiceCount);
        else if (debugChooseDiceKey != Key.None && keyboard[debugChooseDiceKey].wasPressedThisFrame) ChooseDice();
    }

    // ---------------------------------------------------------------------
    // Choosing
    // ---------------------------------------------------------------------

    /// <summary>Offers <see cref="defaultChoiceCount"/> cards with their good titles. For UnityEvents / buttons.</summary>
    public void ChooseCards() => ChooseCards(defaultChoiceCount);

    /// <summary>
    /// Opens the choice: <paramref name="count"/> different cards showing their <paramref name="mood"/> title -
    /// or, with <paramref name="titles"/> (the level's list for how the round went), a title drawn from it at
    /// random, a different one per card while the list lasts. <paramref name="chosen"/> gets the picked card once
    /// the menu has closed - or null right away when there is nothing to offer. Returns false (and never calls
    /// back) when a choice is already open.
    /// </summary>
    public bool ChooseCards(int count, CardMood mood = CardMood.Hope, Action<CardInstance> chosen = null,
        IReadOnlyList<string> titles = null)
    {
        Initialize();

        if (IsChoosing)
        {
            h.Out("CardManager: a choice is already open.");
            return false;
        }

        List<CardData> offer = PickOffer(Mathf.Max(1, count));
        if (offer.Count == 0 || !chooseArea)
        {
            h.Out(!chooseArea ? "CardManager: no Choose Area to show the cards in." : "CardManager: no cards to offer.");
            chosen?.Invoke(null);
            return true;
        }

        BeginChoice();
        onChosen = chosen;

        float upgrade = UpgradeNextChoice ? upgradeBonus : 0f;
        UpgradeNextChoice = false;

        List<string> titlePool = new List<string>();
        for (int i = 0; i < offer.Count; i++)
        {
            CardView view = Spawn(offer[i].viewPrefab, chooseArea);
            if (!view) continue;
            view.Bind(new CardInstance(offer[i], mood, upgrade, DrawTitle(titles, titlePool)), Pick);
            ShowOffered(view, i, offer.Count);
        }

        FinishOpening();
        return true;
    }

    /// <summary>
    /// Opens the menu with plain card <paramref name="faces"/> - no bonus behind them, nothing kept.
    /// <paramref name="chosen"/> gets the picked one's index once the menu has closed (-1 when nothing could be
    /// shown, right away). Returns false (and never calls back) when a choice is already open.
    /// </summary>
    public bool OpenChoice(IReadOnlyList<CardFace> faces, Action<int> chosen)
    {
        Initialize();

        if (IsChoosing)
        {
            h.Out("CardManager: a choice is already open.");
            return false;
        }

        if (faces == null || faces.Count == 0 || !chooseArea)
        {
            if (!chooseArea) h.Out("CardManager: no Choose Area to show the cards in.");
            chosen?.Invoke(-1);
            return true;
        }

        BeginChoice();
        onOptionChosen = chosen;

        for (int i = 0; i < faces.Count; i++)
        {
            CardView view = Spawn(null, chooseArea);
            if (!view) continue;
            view.Bind(faces[i], Pick);
            ShowOffered(view, i, faces.Count);
        }

        FinishOpening();
        return true;
    }

    /// <summary>The menu comes up and darkens the screen, emptied of the last choice's cards.</summary>
    private void BeginChoice()
    {
        IsChoosing = true;
        picking = false;
        onChosen = null;
        onOptionChosen = null;
        chosenOption = -1;
        ClearOffered();

        if (root) root.SetActive(true);
        FadeTo(fadeAlpha, fadeDuration);
    }

    /// <summary>Card <paramref name="index"/> of <paramref name="count"/> flies in; clicks wait for <see cref="FinishOpening"/>.</summary>
    private void ShowOffered(CardView view, int index, int count)
    {
        view.Clickable = false;
        Vector3 scale = view.transform.localScale;
        view.SetHoverBase(scale);

        if (!chooseArea.GetComponent<LayoutGroup>()) PlaceInRow(view, index, count);

        view.transform.localScale = Vector3.zero;
        view.Group.alpha = 0f;
        float delay = index * appearStagger;
        Tween.Scale(view.transform, scale, appearDuration, appearEase, startDelay: delay, useUnscaledTime: true);
        Tween.Alpha(view.Group, 1f, appearDuration * 0.6f, startDelay: delay, useUnscaledTime: true);
        offered.Add(view);
    }

    private void FinishOpening()
    {
        PlaySfx(appearSfx);
        onChoiceOpened?.Invoke();

        // Clicks only once every card is in place.
        float total = appearDuration + appearStagger * Mathf.Max(0, offered.Count - 1);
        stepTween.Stop();
        stepTween = Tween.Delay(this, total, manager => manager.SetOfferedClickable(true), useUnscaledTime: true);
    }

    /// <summary>Closes an open choice without a pick (e.g. the game ended). Its callback is dropped.</summary>
    public void Cancel()
    {
        if (!IsChoosing) return;
        onChosen = null;
        onOptionChosen = null;
        stepTween.Stop();
        CloseMenu(animate: false);
    }

    private void Pick(CardView view)
    {
        if (picking || !IsChoosing || !view) return;
        picking = true;
        SetOfferedClickable(false);

        CardInstance card = view.Card;
        PlaySfx(pickSfx);

        // The others go at once; the pick stays up for a moment.
        foreach (CardView other in offered)
        {
            if (!other || other == view) continue;
            other.StopHover();
            Tween.Alpha(other.Group, 0f, cardFadeDuration, useUnscaledTime: true);
            Tween.Scale(other.transform, other.transform.localScale * 0.85f, cardFadeDuration, useUnscaledTime: true);
        }

        view.StopHover();
        if (pickPunch > 0f)
            Tween.PunchScale(view.transform, Vector3.one * pickPunch, 0.3f, useUnscaledTime: true);

        chosenOption = offered.IndexOf(view);
        // A plain face (OpenChoice) has no card: its caller applies the pick.
        if (card != null)
        {
            Apply(card);
            onCardChosen?.Invoke(card.data);
        }

        stepTween.Stop();
        stepTween = Tween.Delay(this, pickHold, manager => manager.FinishPick(view), useUnscaledTime: true);
    }

    private void FinishPick(CardView view)
    {
        CardView kept = pendingKept;
        if (view && kept && kept.gameObject.activeInHierarchy && flyDuration > 0f)
        {
            FlyToKept(view, kept);
            return;
        }

        // The spot is off screen (or there's no flight): the kept card just pops up in it.
        RevealPending(pop: true);

        if (view)
        {
            // Nothing kept: an immediate card - or a plain face (a die, the reject card) - burns.
            bool burn = view.Card == null || !view.Card.IsPermanent;
            if (burn)
            {
                PlaySfx(burnSfx);
                Tween.Scale(view.transform, view.transform.localScale * burnScale, cardFadeDuration, Ease.OutQuad, useUnscaledTime: true);
            }
            Tween.Alpha(view.Group, 0f, cardFadeDuration, useUnscaledTime: true);
        }

        stepTween = Tween.Delay(this, cardFadeDuration, manager =>
        {
            CardInstance card = view ? view.Card : null;
            manager.CloseMenu(animate: true, card);
        }, useUnscaledTime: true);
    }

    /// <summary>The chosen card flies from the menu into its kept card's place, which then takes over.</summary>
    private void FlyToKept(CardView view, CardView kept)
    {
        // The spot's layout must be final before its position is read.
        Canvas.ForceUpdateCanvases();

        Tween.StopAll(view.transform);
        float ratio = kept.transform.lossyScale.x / Mathf.Max(0.0001f, view.transform.lossyScale.x);
        Tween.Position(view.transform, kept.transform.position, flyDuration, flyEase, useUnscaledTime: true);
        Tween.Scale(view.transform, view.transform.localScale * ratio, flyDuration, flyEase, useUnscaledTime: true);

        stepTween = Tween.Delay(this, flyDuration, manager =>
        {
            if (view) view.Group.alpha = 0f;
            manager.RevealPending(pop: false);
            if (kept && manager.pickPunch > 0f)
                Tween.PunchScale(kept.transform, Vector3.one * manager.pickPunch, 0.25f, useUnscaledTime: true);
            manager.CloseMenu(animate: true, view ? view.Card : null);
        }, useUnscaledTime: true);
    }

    private void RevealPending(bool pop)
    {
        CardView kept = pendingKept;
        pendingKept = null;
        Reveal(kept, pop);
    }

    private void CloseMenu(bool animate, CardInstance chosen = null)
    {
        // Cut short mid-flight (Cancel): the kept card still shows.
        RevealPending(pop: false);

        Action<CardInstance> callback = onChosen;
        onChosen = null;
        Action<int> optionCallback = onOptionChosen;
        onOptionChosen = null;
        int option = chosenOption;
        chosenOption = -1;

        ClearOffered();
        IsChoosing = false;
        picking = false;

        if (animate && fadeImage && fadeDuration > 0f)
        {
            fadeTween.Stop();
            fadeTween = Tween.Alpha(fadeImage, 0f, fadeDuration, useUnscaledTime: true)
                .OnComplete(this, manager => { if (!manager.IsChoosing && manager.root) manager.root.SetActive(false); });
        }
        else
        {
            fadeTween.Stop();
            if (fadeImage) SetFadeAlpha(0f);
            if (root) root.SetActive(false);
        }

        onChoiceClosed?.Invoke();
        if (!animate) return;
        callback?.Invoke(chosen);
        optionCallback?.Invoke(option);
    }

    // ---------------------------------------------------------------------
    // Effects
    // ---------------------------------------------------------------------

    /// <summary>A picked card takes effect: an immediate one acts now, a permanent one is kept.</summary>
    private void Apply(CardInstance card)
    {
        if (card == null || !card.data) return;

        if (card.IsPermanent)
        {
            // Laid out but hidden: the chosen card flies into its place first (FinishPick).
            pendingKept = AddPermanent(card, animate: false, hidden: true);
            return;
        }

        ImmediateEffect effect = card.data.immediate;
        switch (effect.type)
        {
            case ImmediateEffectType.RewindSong:
                if (SongTimer.Instance) SongTimer.Instance.Seek(-effect.seconds);
                else h.Out("CardManager: no SongTimer to rewind.");
                break;
        }

        h.Out($"CardManager: '{card.Title}' applied and burned ({card.data.BonusDescription}).");
        onImmediateCardApplied?.Invoke(card.data);
    }

    /// <summary>Keeps <paramref name="card"/>: its bonus counts from now on, and it's shown with the player's cards.</summary>
    public void AddPermanent(CardInstance card, bool animate = true) => AddPermanent(card, animate, hidden: false);

    /// <summary>
    /// Keeps the card and puts its view in <see cref="permanentCardsSpot"/>. With <paramref name="hidden"/> the
    /// view is laid out but invisible - the chosen card flies into its place, then <see cref="Reveal"/> shows it.
    /// </summary>
    private CardView AddPermanent(CardInstance card, bool animate, bool hidden)
    {
        Initialize();
        if (card == null || !card.data) return null;

        permanentCards.Add(card);

        CardView view = null;
        if (permanentCardsSpot)
        {
            view = Spawn(card.data.viewPrefab, permanentCardsSpot);
            if (view)
            {
                view.Bind(card);
                view.transform.localScale *= permanentCardScale;
                view.SetHoverBase(view.transform.localScale);
                view.HoverOnly = keptCardsHoverable;
                view.HoverScaleOverride = keptHoverScale;
                view.BringToFrontOnHover = !permanentCardsSpot.GetComponent<LayoutGroup>();
                view.Group.blocksRaycasts = keptCardsHoverable;
                permanentViews.Add(view);
                ArrangeKept();

                if (hidden) view.Group.alpha = 0f;
                else Reveal(view, animate);
            }
        }
        else h.Out("CardManager: no Permanent Cards Spot - the card counts, but isn't shown.");

        onPermanentCardAdded?.Invoke(card.data);
        return view;
    }

    /// <summary>Shows a kept card's view, popping it in when <paramref name="pop"/>.</summary>
    private void Reveal(CardView view, bool pop)
    {
        if (!view) return;
        view.Group.alpha = 1f;
        if (!pop || permanentAppearDuration <= 0f) return;

        Vector3 scale = view.transform.localScale;
        view.transform.localScale = Vector3.zero;
        Tween.Scale(view.transform, scale, permanentAppearDuration, Ease.OutBack, useUnscaledTime: true);
    }

    /// <summary>
    /// Places the kept cards: a layout group on the spot does it itself; otherwise they go in rows from the
    /// spot's top-left corner, a new row whenever the next card wouldn't fit.
    /// </summary>
    private void ArrangeKept()
    {
        if (!permanentCardsSpot) return;

        if (permanentCardsSpot.GetComponent<LayoutGroup>())
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(permanentCardsSpot);
            return;
        }

        float width = permanentCardsSpot.rect.width;
        float x = 0f, y = 0f, rowHeight = 0f;
        foreach (CardView view in permanentViews)
        {
            if (!view) continue;
            RectTransform rect = view.Rect;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);

            float w = rect.rect.width * Mathf.Abs(rect.localScale.x);
            float hgt = rect.rect.height * Mathf.Abs(rect.localScale.y);
            if (x > 0f && x + w > width)
            {
                x = 0f;
                y -= rowHeight + permanentCardSpacing;
                rowHeight = 0f;
            }

            rect.anchoredPosition = new Vector2(x + w * rect.pivot.x, y - hgt * (1f - rect.pivot.y));
            x += w + permanentCardSpacing;
            rowHeight = Mathf.Max(rowHeight, hgt);
        }
    }

    public bool OwnsPermanent(CardData data)
    {
        foreach (CardInstance card in PermanentCards)
            if (card.data == data) return true;
        return false;
    }

    /// <summary>Drops every kept card (a new game).</summary>
    public void ClearPermanentCards()
    {
        UpgradeNextChoice = false;
        shownCards.Clear();
        permanentCards.Clear();
        pendingKept = null;
        foreach (CardView view in permanentViews)
            if (view) Destroy(view.gameObject);
        permanentViews.Clear();
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>How many of <paramref name="data"/> the player keeps.</summary>
    public int OwnedCount(CardData data)
    {
        int owned = 0;
        foreach (CardInstance card in PermanentCards)
            if (card.data == data) owned++;
        return owned;
    }

    /// <summary>
    /// <paramref name="count"/> different cards, each drawn with a chance by its copies left in the deck
    /// (<see cref="CardData.count"/>, less the ones the player keeps unless owned cards may come again). With
    /// <see cref="noRepeatsAcrossChoices"/> the cards of earlier choices sit out until the deck runs out.
    /// </summary>
    private List<CardData> PickOffer(int count)
    {
        List<CardData> pool = new List<CardData>();
        List<int> copies = new List<int>();
        foreach (CardData data in availableCards)
        {
            if (!data || pool.Contains(data)) continue;
            int left = Mathf.Max(1, data.count);
            if (data.IsPermanent && !allowOwnedPermanentCards) left -= OwnedCount(data);
            if (left <= 0) continue;
            pool.Add(data);
            copies.Add(left);
        }

        List<CardData> offer = new List<CardData>();
        while (offer.Count < count && pool.Count > 0)
        {
            int i = DrawIndex(pool, copies);
            if (i < 0)
            {
                // Every card left has been offered: the deck starts over - without this choice's cards, so the
                // next choice doesn't bring them straight back.
                shownCards.Clear();
                foreach (CardData data in offer) shownCards.Add(data);
                continue;
            }

            offer.Add(pool[i]);
            shownCards.Add(pool[i]);
            pool.RemoveAt(i);
            copies.RemoveAt(i);
        }
        return offer;
    }

    /// <summary>
    /// A card of <paramref name="pool"/> drawn with a chance by its <paramref name="copies"/>, skipping the ones
    /// already offered (<see cref="noRepeatsAcrossChoices"/>). -1 when every card left has been offered.
    /// </summary>
    private int DrawIndex(List<CardData> pool, List<int> copies)
    {
        int total = 0;
        for (int i = 0; i < pool.Count; i++)
            if (CanDraw(pool[i])) total += copies[i];
        if (total <= 0) return -1;

        int roll = UnityEngine.Random.Range(0, total);
        for (int i = 0; i < pool.Count; i++)
        {
            if (!CanDraw(pool[i])) continue;
            if (roll < copies[i]) return i;
            roll -= copies[i];
        }
        return -1;
    }

    private bool CanDraw(CardData data) => !noRepeatsAcrossChoices || !shownCards.Contains(data);

    /// <summary>
    /// A random title from <paramref name="titles"/> not drawn yet this choice (<paramref name="pool"/> holds the
    /// ones left; refilled once they're all used). Null without titles: the card shows its own.
    /// </summary>
    private static string DrawTitle(IReadOnlyList<string> titles, List<string> pool)
    {
        if (titles == null) return null;

        if (pool.Count == 0)
            foreach (string title in titles)
                if (!string.IsNullOrWhiteSpace(title)) pool.Add(title);
        if (pool.Count == 0) return null;

        int i = UnityEngine.Random.Range(0, pool.Count);
        string drawn = pool[i];
        pool.RemoveAt(i);
        return drawn;
    }

    /// <summary>A card from <paramref name="prefab"/> (a card's own), else <see cref="cardPrefab"/>.</summary>
    private CardView Spawn(CardView prefab, Transform parent)
    {
        if (!prefab) prefab = cardPrefab;
        if (!prefab)
        {
            h.Out("CardManager: no Card Prefab assigned.");
            return null;
        }

        CardView view = Instantiate(prefab, parent);
        view.gameObject.SetActive(true);
        view.Hovered = _ => PlaySfx(hoverSfx);
        return view;
    }

    /// <summary>Lines card <paramref name="index"/> of <paramref name="count"/> up side by side around the area's centre.</summary>
    private void PlaceInRow(CardView view, int index, int count)
    {
        RectTransform rect = view.Rect;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);

        float width = rect.rect.width * Mathf.Abs(rect.localScale.x);
        float step = width + cardSpacing;
        float x = (index - (count - 1) * 0.5f) * step;
        rect.anchoredPosition = new Vector2(x, 0f);
    }

    private void SetOfferedClickable(bool clickable)
    {
        foreach (CardView view in offered)
            if (view) view.Clickable = clickable;
    }

    private void ClearOffered()
    {
        foreach (CardView view in offered)
        {
            if (!view) continue;
            Tween.StopAll(view.transform);
            Tween.StopAll(view.Group);
            Destroy(view.gameObject);
        }
        offered.Clear();
    }

    private void FadeTo(float alpha, float duration)
    {
        if (!fadeImage) return;
        fadeImage.gameObject.SetActive(true);
        fadeImage.raycastTarget = true;
        fadeTween.Stop();
        if (duration > 0f) fadeTween = Tween.Alpha(fadeImage, alpha, duration, useUnscaledTime: true);
        else SetFadeAlpha(alpha);
    }

    private void SetFadeAlpha(float alpha)
    {
        Color c = fadeImage.color;
        c.a = alpha;
        fadeImage.color = c;
    }

    private static void PlaySfx(AudioClip clip)
    {
        if (clip && SFXManager.Instance) SFXManager.Instance.PlayClip(clip);
    }
}
