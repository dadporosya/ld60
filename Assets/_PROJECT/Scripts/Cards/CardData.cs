using System;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>Which title a card shows: the good one after a call that went well, the bad one after one that didn't.</summary>
public enum CardMood
{
    Hope,
    Hate,
}

/// <summary>How a card's bonus works.</summary>
public enum CardKind
{
    [Tooltip("Kept: its bonus counts in every dice round, and it is shown with the player's cards.")]
    Permanent,
    [Tooltip("Applied once, the moment it is picked, then burned.")]
    Immediate,
}

/// <summary>A permanent card's bonus to the dice score.</summary>
public enum CardEffectType
{
    [Tooltip("+Amount points to every round's score.")]
    FlatPoints,
    [Tooltip("+Amount points when the dice make Combination.")]
    CombinationBonus,
    [Tooltip("The Combination's points are multiplied by Multiplier.")]
    CombinationMultiplier,
    [Tooltip("+Amount points for each die showing Face Value.")]
    FaceValueBonus,
    [Tooltip("+Amount rerolls every round.")]
    ExtraRerolls,
    [Tooltip("The whole score is multiplied by Multiplier.")]
    ScoreMultiplier,
}

/// <summary>An immediate card's one-off effect.</summary>
public enum ImmediateEffectType
{
    [Tooltip("Plays the song back by Seconds: more time left.")]
    RewindSong,
    [Tooltip("Nothing built in - react through CardManager.onImmediateCardApplied.")]
    Custom,
}

/// <summary>What a permanent card does to the dice.</summary>
[Serializable]
public class CardEffect
{
    public CardEffectType type = CardEffectType.FlatPoints;
    public int amount = 3;
    [Min(0f)] public float multiplier = 1.5f;
    public PokerDiceCombination combination = PokerDiceCombination.OnePair;
    [Range(1, 6)] public int faceValue = 6;

    public bool Matches(PokerDiceCombination hand) => hand == combination;

    /// <summary>Plain description of the bonus, for cards without their own text (or for {bonus}).</summary>
    public string Describe()
    {
        switch (type)
        {
            case CardEffectType.FlatPoints: return $"+{amount} points";
            case CardEffectType.CombinationBonus: return $"+{amount} points for {Nicify(combination)}";
            case CardEffectType.CombinationMultiplier: return $"{Nicify(combination)} scores x{multiplier:0.##}";
            case CardEffectType.FaceValueBonus: return $"+{amount} points for each {faceValue}";
            case CardEffectType.ExtraRerolls: return amount == 1 ? "+1 reroll" : $"+{amount} rerolls";
            case CardEffectType.ScoreMultiplier: return $"Score x{multiplier:0.##}";
            default: return "";
        }
    }

    public static string Nicify(PokerDiceCombination combination) =>
        Regex.Replace(combination.ToString(), "(\\B[A-Z])", " $1");
}

/// <summary>What an immediate card does once.</summary>
[Serializable]
public class ImmediateEffect
{
    public ImmediateEffectType type = ImmediateEffectType.RewindSong;
    [Tooltip("RewindSong: seconds the song is played back.")]
    [Min(0f)] public float seconds = 10f;

    public string Describe()
    {
        switch (type)
        {
            case ImmediateEffectType.RewindSong: return $"Play the song back {seconds:0.#} seconds";
            default: return "";
        }
    }
}

/// <summary>
/// A bonus card. <see cref="CardKind.Permanent"/> cards add to every dice round; <see cref="CardKind.Immediate"/>
/// ones act once when picked and burn. The card shows its good or bad title depending on how the call went;
/// the bonus is the same either way. The description may use {bonus} for the bonus's plain description; an
/// empty description shows just that.
/// </summary>
[CreateAssetMenu(fileName = "Card", menuName = "Funeral Song/Card")]
public class CardData : ScriptableObject
{
    [Header("Look")]
    public string goodTitle = "Hope";
    public string badTitle = "Hate";
    [TextArea] public string description;
    [Tooltip("The picture in the middle of the card.")]
    public Sprite art;
    [Tooltip("Optional: this card's own prefab instead of the CardManager's Card Prefab.")]
    public CardView viewPrefab;

    [Header("Bonus")]
    public CardKind kind = CardKind.Permanent;
    [Tooltip("Permanent cards: the bonus to every dice round.")]
    public CardEffect effect = new CardEffect();
    [Tooltip("Immediate cards: what happens the moment it is picked.")]
    public ImmediateEffect immediate = new ImmediateEffect();

    public bool IsPermanent => kind == CardKind.Permanent;

    public string Title(CardMood mood) => mood == CardMood.Hope ? goodTitle : badTitle;

    public string BonusDescription => IsPermanent ? effect.Describe() : immediate.Describe();

    public string Description()
    {
        string bonus = BonusDescription;
        return string.IsNullOrWhiteSpace(description) ? bonus : description.Replace("{bonus}", bonus);
    }
}

/// <summary>A card offered to or owned by the player: which card, and which of its titles it shows.</summary>
[Serializable]
public class CardInstance
{
    public CardData data;
    public CardMood mood;

    public CardInstance(CardData data, CardMood mood)
    {
        this.data = data;
        this.mood = mood;
    }

    public bool IsPermanent => data && data.IsPermanent;
    /// <summary>The dice bonus - only for permanent cards; immediate ones never touch the score.</summary>
    public CardEffect Effect => IsPermanent ? data.effect : null;
    public string Title => data ? data.Title(mood) : "";
    public string Description => data ? data.Description() : "";
    public Sprite Art => data ? data.art : null;
}
