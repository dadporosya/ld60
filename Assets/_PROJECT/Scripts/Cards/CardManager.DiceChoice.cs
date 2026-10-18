using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// The dice choice, after the card pick: <see cref="diceChoiceCount"/> different new dice, shown as cards (their
/// icon the die in its color), and a card to turn them down. A picked die replaces a random plain die of the table
/// (<see cref="DiceManager.ReplaceRandomDefaultDie"/>).
/// </summary>
public partial class CardManager
{
    [Header("Dice choice")]
    [Tooltip("Off: no dice choice after the card pick.")]
    [SerializeField] private bool offerDice = true;
    [Tooltip("Dice offered per choice (each a different kind), next to the reject card.")]
    [SerializeField, Min(1)] private int diceChoiceCount = 2;
    [Tooltip("The dice that can be offered.")]
    [SerializeField] private List<DiceKindOffer> diceOffers = DiceKindOffer.Defaults();
    [Tooltip("The card that turns the dice down: always offered last.")]
    [SerializeField] private CardFace rejectDiceCard = new CardFace("Keep My Dice", "Leave the dice as they are.", "");
    [Tooltip("Debug only (with Debug Key on): opens a dice choice.")]
    [SerializeField] private Key debugChooseDiceKey = Key.None;

    /// <summary>A die was picked (not the reject card) and is on the table.</summary>
    public UnityEvent<DiceKind> onDiceChosen = new UnityEvent<DiceKind>();

    /// <summary>
    /// Opens the dice choice; <paramref name="chosen"/> runs once the menu has closed - or right away when there
    /// is nothing to offer (off, no dice offers, no DiceManager). Returns false (and never calls back) when a
    /// choice is already open.
    /// </summary>
    public bool ChooseDice(Action chosen = null)
    {
        Initialize();

        DiceManager table = DiceManager.Instance ? DiceManager.Instance : FindFirstObjectByType<DiceManager>(FindObjectsInactive.Include);
        List<DiceKindOffer> offer = offerDice && table ? PickDiceOffer(diceChoiceCount) : new List<DiceKindOffer>();
        if (offer.Count == 0)
        {
            if (offerDice) h.Out(table ? "CardManager: no dice to offer." : "CardManager: no DiceManager for the dice choice.");
            if (IsChoosing) return false;
            chosen?.Invoke();
            return true;
        }

        List<CardFace> faces = new List<CardFace>();
        foreach (DiceKindOffer die in offer) faces.Add(die.Face);
        faces.Add(rejectDiceCard);

        return OpenChoice(faces, index =>
        {
            if (index >= 0 && index < offer.Count)
            {
                table.ReplaceRandomDefaultDie(offer[index].kind, offer[index].tint);
                onDiceChosen?.Invoke(offer[index].kind);
            }
            chosen?.Invoke();
        });
    }

    /// <summary><paramref name="count"/> different dice kinds, each drawn with a chance by its weight.</summary>
    private List<DiceKindOffer> PickDiceOffer(int count)
    {
        List<DiceKindOffer> pool = new List<DiceKindOffer>();
        foreach (DiceKindOffer die in diceOffers)
        {
            if (die == null || !die.enabled || die.kind == DiceKind.Default) continue;
            if (pool.Exists(other => other.kind == die.kind)) continue;
            pool.Add(die);
        }

        List<DiceKindOffer> offer = new List<DiceKindOffer>();
        while (offer.Count < count && pool.Count > 0)
        {
            int total = 0;
            foreach (DiceKindOffer die in pool) total += Mathf.Max(1, die.weight);

            int roll = UnityEngine.Random.Range(0, total);
            int i = 0;
            while (roll >= Mathf.Max(1, pool[i].weight)) roll -= Mathf.Max(1, pool[i++].weight);

            offer.Add(pool[i]);
            pool.RemoveAt(i);
        }
        return offer;
    }
}
